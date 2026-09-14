using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * LAEUFT DIE FAHRGASSE BIS ZUR STRASSE?
     *
     * Der Nutzer zeigt auf Fahrgassen, die kurz vor der Randstrasse und kurz
     * vor der Verbindungsstrasse aufhoeren und ins Gruen laufen. Zwei Dinge
     * koennen das erzeugen, und sie sind verschieden:
     *
     *   1. DER BELAG endet zu frueh - die Gasse liegt dort auf Gras.
     *   2. DER NETZKURS endet zu frueh - die unsichtbare Strasse haengt in
     *      der Luft und CS2 verbindet sie nicht.
     *
     * Beides wird hier getrennt gemessen, damit nicht wieder eine Vermutung
     * fuer einen Befund gehalten wird.
     */
    private static double GrasUnterGassen(ParkingLayout layout)
    {
        var wege = layout.AisleQuad.Concat(layout.CrossQuad).ToArray();
        var flaeche = layout.GrassSurface
            .SelectMany(TriangulateMaterialRegion)
            .Sum(dreieck => dreieck.Weight * wege.Sum(weg =>
                ConvexOverlapArea(dreieck.Points, weg)));
        return flaeche < 1e-8 ? 0 : flaeche;
    }

    /**
     * Ein Gassenende ohne Anschluss.
     *
     * CS2 verbindet innen NUR ueber einen identischen Endpunkt. Trifft das
     * Ende einer Fahrgasse keinen anderen Kurs, ist die Gasse eine Sackgasse
     * - egal wie gut der Belag aussieht.
     */
    private static (int Offen, int Gesamt, double Schlimmster)
        OffeneGassenenden(ParkingLayout layout)
    {
        var segmente = layout.NetLine;
        double AbstandZuSegment(float2 p, float2 a, float2 b)
        {
            var ab = new double2(b.x - a.x, b.y - a.y);
            var ap = new double2(p.x - a.x, p.y - a.y);
            var quadrat = ab.x * ab.x + ab.y * ab.y;
            var t = quadrat <= 1e-12 ? 0 : Math.Max(0, Math.Min(1,
                (ap.x * ab.x + ap.y * ab.y) / quadrat));
            var lot = new double2(a.x + ab.x * t, a.y + ab.y * t);
            return Math.Sqrt((p.x - lot.x) * (p.x - lot.x)
                + (p.y - lot.y) * (p.y - lot.y));
        }

        var offen = 0;
        var gesamt = 0;
        var schlimmster = 0.0;
        for (var i = 0; i < segmente.Length; i++)
        {
            if (segmente[i].Kind != "aisle") continue;
            foreach (var ende in new[] { segmente[i].A, segmente[i].B })
            {
                gesamt++;
                var naechster = double.MaxValue;
                for (var k = 0; k < segmente.Length; k++)
                {
                    if (k == i) continue;
                    var d = AbstandZuSegment(
                        ende, segmente[k].A, segmente[k].B);
                    if (d < naechster) naechster = d;
                }
                if (naechster <= 0.01) continue;
                offen++;
                if (naechster > schlimmster) schlimmster = naechster;
            }
        }
        return (offen, gesamt, schlimmster);
    }

    /**
     * DIE PRUEFUNG, DIE DER MOD IM SPIEL FAEHRT - hier nachgebaut.
     *
     * CS2 verwirft eine Flaeche, deren kuerzeste Kante unter 0,375 m liegt;
     * an ihrer Stelle bleibt nackter Boden. Genau das ist dem Nutzer am
     * 2026-09-01 im Ausrichtbau passiert (Logzeile: "CS2 wird 0 Gras- und 1
     * Belagflaeche(n) VERWERFEN", kuerzeste Kante 0,038 m). Der Vergleichsbau
     * ohne Ausrichtung war sauber.
     */
    private static (int Verdaechtig, double KuerzesteKante)
        Haarkanten(ParkingLayout layout)
    {
        var verdaechtig = 0;
        var kuerzesteGesamt = double.PositiveInfinity;
        foreach (var ring in layout.GrassSurface.Concat(layout.AsphaltSurface))
        {
            if (ring == null || ring.Length < 3) continue;
            var kuerzeste = double.PositiveInfinity;
            for (var i = 0; i < ring.Length; i++)
            {
                var b = ring[(i + 1) % ring.Length];
                var d = math.length(b - ring[i]);
                if (d < kuerzeste) kuerzeste = d;
            }
            /*
             * NICHT die 0,375-m-Regel: die ist seit dem 2026-08-24
             * widerlegt. Massgeblich ist das nachgebaute Ear-Clipping des
             * Spiels - dieselbe Pruefung, die der Mod im Spiel faehrt.
             */
            if (Cs2Triangulierung.Dreiecke(ring) == 0) verdaechtig++;
            if (kuerzeste < kuerzesteGesamt) kuerzesteGesamt = kuerzeste;
        }
        return (verdaechtig, double.IsPositiveInfinity(kuerzesteGesamt)
            ? 0 : kuerzesteGesamt);
    }

    /** Beschreibt jeden Ring, den CS2 verwerfen wird - Ort, Groesse, Form. */
    private static void ZeigeVerworfeneRinge(
        ParkingLayout layout, float2 schnittA, float2 schnittB)
    {
        void Pruefe(string art, float2[][] ringe)
        {
            for (var n = 0; n < ringe.Length; n++)
            {
                var ring = ringe[n];
                if (ring == null || ring.Length < 3) continue;
                if (Cs2Triangulierung.Dreiecke(ring) != 0) continue;
                var kuerzeste = double.PositiveInfinity;
                var kurze = 0;
                for (var i = 0; i < ring.Length; i++)
                {
                    var d = math.length(ring[(i + 1) % ring.Length] - ring[i]);
                    if (d < kuerzeste) kuerzeste = d;
                    if (d < 0.05) kurze++;
                }
                var mitte = float2.zero;
                foreach (var pt in ring) mitte += pt;
                mitte /= ring.Length;
                var flaeche = Math.Abs(Flaeche(ring
                    .Select(pt => new double2(pt.x, pt.y)).ToArray()));
                var hals = MinimumMaterialBottleneck(new[] { ring });
                var kreuz = SelfIntersectionCount(ring);
                var doppelt = HasNearDuplicatePoints(ring);
                Console.WriteLine($"      VERWORFEN {art} #{n}: "
                    + $"{ring.Length} Punkte, {flaeche:F1} m2, kuerzeste Kante "
                    + $"{kuerzeste:F4} m, davon unter 5 cm: {kurze} | "
                    + $"Einschnuerung {hals:F4} m | Selbstschnitte {kreuz} | "
                    + $"Doppelpunkte {(doppelt ? "ja" : "nein")} | "
                    + $"Abstand zum Schnitt "
                    + $"{AbstandZurStrecke(mitte, schnittA, schnittB):F1} m");
                var versetzt = Cs2Triangulierung.VersetzterRing(ring);
                Console.WriteLine($"        nach CS2s 0,1-m-Versatz: "
                    + $"Selbstschnitte {SelfIntersectionCount(versetzt)} | "
                    + $"Flaeche {Math.Abs(Flaeche(versetzt.Select(pt =>
                        new double2(pt.x, pt.y)).ToArray())):F1} m2 | "
                    + $"Umlauf {(Flaeche(versetzt.Select(pt =>
                        new double2(pt.x, pt.y)).ToArray()) >= 0
                        ? "gleich" : "GEKIPPT")}");
                for (var i = 0; i < ring.Length; i++)
                {
                    var kante = math.length(
                        ring[(i + 1) % ring.Length] - ring[i]);
                    var kanteV = math.length(
                        versetzt[(i + 1) % versetzt.Length] - versetzt[i]);
                    if (kante >= 0.5 && kanteV >= 0.5) continue;
                    Console.WriteLine($"        Kante {i}: vorher "
                        + $"{kante:F4} m -> nachher {kanteV:F4} m");
                }
            }
        }
        Pruefe("Gras", layout.GrassSurface);
        Pruefe("Belag", layout.AsphaltSurface);
    }
}
