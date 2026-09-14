using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static void PruefeVersorgungsRinge(Action<bool, string> pruefe)
    {
        for (var grad = 0; grad < 360; grad += 5)
        {
            var w = grad * math.PI / 180;
            float2 P(float x, float y) => new float2(x * math.cos(w) - y * math.sin(w),
                x * math.sin(w) + y * math.cos(w)) + new float2(-1052.6f, -32.9f);
            var a = P(-40, 0); var b = P(40, 0); var c = P(-10, -12); var d = P(10, -12);
            var starts = new List<float2>(Versorgungsnetz.Kantenpunkte(t => math.lerp(a, b, t),
                p => Versorgungsnetz.Projektion(p, a, b), t => math.lerp(c, d, t),
                p => Versorgungsnetz.Projektion(p, c, d)));
            var suche = Versorgungsnetz.Gerade(starts, p => new[] {
                new Versorgungsweg.Ziel { Index = 0, Punkt = P(-60, 0) },
                new Versorgungsweg.Ziel { Index = 1, Punkt = Versorgungsnetz.Projektion(p, c, d) } }, (_, z) => true);
            pruefe(suche.Punkte != null && suche.Ziel == 1 && math.abs(suche.Laenge - 12) < 0.002f,
                $"Ring {grad}: unten 12 m gewinnt gegen seitlich 20 m");
            pruefe(math.distance(suche.Punkte[0], P(0, 0)) < 0.002f,
                $"Ring {grad}: gleichlange Loesung an Kantenmitte");
            var verworfen = Versorgungsnetz.Gerade(starts, p => new[] {
                new Versorgungsweg.Ziel { Index = 0, Punkt = p + P(0, 1) - P(0, 0) },
                new Versorgungsweg.Ziel { Index = 1, Punkt = Versorgungsnetz.Projektion(p, c, d) } }, (_, z) => z.Index == 1);
            pruefe(verworfen.Punkte != null && verworfen.Ziel == 1, $"Ring {grad}: gueltige Alternative trotz kuerzerem unzulaessigem Ziel");
            var h = new List<Versorgungsweg.Hindernis>();
            Versorgungsweg.Bogen(h, 1, a, a, b, b, 4);
            Versorgungsweg.Bogen(h, 2, c, c, d, d, 4);
            var weg = new List<float2> { P(0, 0), P(0, -12) };
            pruefe(Versorgungsweg.Spuren(weg, 1.5f, 3.5f, h, new HashSet<int> { 1 }, 8,
                out _, out _, (_, __) => true, new HashSet<int> { 2 }), $"Ring {grad}: seitlicher eigener Zielanschluss erlaubt");
            pruefe(!Versorgungsweg.Spuren(weg, 1.5f, 3.5f, h, new HashSet<int> { 1 }, 8,
                out _, out _, (_, __) => true), $"Ring {grad}: ohne Zielausnahme gesperrt");
            pruefe(!Versorgungsweg.Spuren(weg, 1.5f, 3.5f, h, new HashSet<int> { 1 }, 8,
                out _, out _, (_, __) => true, new HashSet<int> { 2 }, (_, __) => false), $"Ring {grad}: Starttor zwingend");
            foreach (var fehltStrom in new[] { true, false })
                pruefe(!Versorgungsweg.Spuren(weg, 1.5f, 3.5f, h, new HashSet<int> { 1 }, 8,
                    out _, out _, (_, __) => true, new HashSet<int> { 2 }, (_, strom) => strom != fehltStrom),
                    $"Ring {grad}: einzelnes Starttor fehlt, Strom={fehltStrom}");
            pruefe(!Versorgungsweg.Frei(P(-30, 0), P(30, 0), h, new HashSet<int> { 1 }, 8, new HashSet<int> { 1 }),
                $"Ring {grad}: 60 m unter eigener Strasse verboten, auch mit zwei Anschlussbereichen");
            pruefe(!Versorgungsweg.Frei(P(0, 12), P(0, -24), h, null, 8, new HashSet<int> { 2 }),
                $"Ring {grad}: fremde eigene Startstrasse bleibt Hindernis");
            var fahrgassen = new List<Versorgungsweg.Hindernis>();
            var ring = new[] { P(-20, -20), P(20, -20), P(20, 20), P(-20, 20) };
            for (var i = 0; i < 4; i++)
                Versorgungsweg.Bogen(fahrgassen, 10 + i, ring[i], ring[i], ring[(i + 1) % 4],
                    ring[(i + 1) % 4], 3, querbar: true);
            // Innerer ZF-Ring: Start an der rechten Kante, Stadtziel ausserhalb.
            var innenring = new[] { P(-5, -5), P(5, -5), P(5, 5), P(-5, 5) };
            for (var i = 0; i < 4; i++)
                Versorgungsweg.Bogen(fahrgassen, 20 + i, innenring[i], innenring[i],
                    innenring[(i + 1) % 4], innenring[(i + 1) % 4], 2);
            var startIds = new HashSet<int> { 21 };
            var querweg = Versorgungsweg.Suche(new List<float2> { P(5, 0) }, fahrgassen,
                _ => startIds, _ => new[] { new Versorgungsweg.Ziel { Punkt = P(40, 0) } },
                (punkte, zielIndex) => Versorgungsweg.Spuren(punkte, 1.5f, 3.5f, fahrgassen,
                    startIds, 8, out _, out _));
            pruefe(querweg.Punkte != null && math.abs(querweg.Laenge - 35) < 0.002f,
                $"Fahrgassen {grad}: umschlossener ZF-Ring MUSS 35-m-Trasse mit beiden Spuren bekommen");
            pruefe(!Versorgungsweg.Frei(P(20, -15), P(20, 15), fahrgassen),
                $"Fahrgassen {grad}: 30 m Laengsfahrt bleiben gesperrt");
            pruefe(!Versorgungsweg.Frei(P(0, 0), P(40, 0), fahrgassen),
                $"Fahrgassen {grad}: ZF ohne Startausnahme bleibt gesperrt");
        }
        var graph = new List<int2> { new int2(1, 2), new int2(2, 3), new int2(3, 1),
            new int2(4, 5), new int2(5, 6), new int2(6, 4), new int2(3, 4) };
        pruefe(Versorgungsnetz.Erreichbar(new[] { 0 }, graph).Count == 1, "Stadtpfad: zwei verbundene Ringe ohne Quelle bleiben unversorgt");
        graph.Add(new int2(0, 1));
        pruefe(Versorgungsnetz.Erreichbar(new[] { 0 }, graph).Count == 7, "Stadtpfad: Stadt-ZF-ZF/RZ erreicht beide Ringe");
        var wasser = new List<int2>(graph); wasser.RemoveAt(wasser.Count - 1);
        pruefe(!Versorgungsnetz.Erreichbar(new[] { 0 }, wasser).Contains(4), "Stadtpfad: Strompfad ersetzt keinen Wasserpfad");
        graph.RemoveAt(graph.Count - 1);
        pruefe(!Versorgungsnetz.Erreichbar(new[] { 0 }, graph).Contains(4), "Stadtpfad: fehlgeschlagener Apply gibt Folgeziele nicht frei");
        graph.Add(new int2(0, 7));
        pruefe(Versorgungsnetz.Erreichbar(new[] { 0 }, graph).Contains(7), "Stadtpfad: unabhaengiges Netz bleibt baubar");
    }
}
