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
        PruefeSuchgrenze(pruefe);
        PruefeProjektionsFixpunkt(pruefe);
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
    private static void PruefeSuchgrenze(Action<bool, string> pruefe)
    {
        long altSicht = 0, neuSicht = 0, altKnoten = 0, neuKnoten = 0;
        for (var fall = 0; fall < 200; fall++)
        {
            var winkel = fall * 0.137f;
            float2 P(float x, float y) => new float2(x * math.cos(winkel) - y * math.sin(winkel),
                x * math.sin(winkel) + y * math.cos(winkel)) + new float2(8000, -7000);
            var h = new List<Versorgungsweg.Hindernis>();
            for (var i = 0; i < 20; i++)
            {
                var x = 4 + i * 4f;
                h.Add(new Versorgungsweg.Hindernis { Ring = new[] {
                    P(x,-3), P(x+2,-3), P(x+2,3), P(x,3) } });
            }
            var starts = new List<float2> { P(0, 0), P(-2, -1) };
            IEnumerable<Versorgungsweg.Ziel> Ziele(float2 p) => new[] {
                new Versorgungsweg.Ziel { Index = 0, Punkt = P(10, 0) },
                new Versorgungsweg.Ziel { Index = 1, Punkt = P(85, 0) } };
            Versorgungsweg.Ergebnis Suche(float grenze) => Versorgungsweg.Suche(starts, h,
                _ => new HashSet<int>(), Ziele, (_, __) => true, maxLaenge: grenze);
            var alt = Suche(float.MaxValue);
            var neu = Suche(alt.Laenge + 0.002f);
            pruefe(alt.Punkte != null && neu.Punkte != null && alt.Ziel == neu.Ziel
                && math.abs(alt.Laenge - neu.Laenge) < 0.002f,
                $"Suchgrenze {fall}: gueltiger Umweg und Ziel bleiben erhalten");
            var eng = Suche(1.35f);
            pruefe(eng.Punkte == null && eng.Sichtpruefungen == 0 && eng.Knoten == starts.Count,
                $"Suchgrenze {fall}: fernes Netz verursacht keine Graphsuche");
            altSicht += alt.Sichtpruefungen; neuSicht += neu.Sichtpruefungen;
            altKnoten += alt.Knoten; neuKnoten += neu.Knoten;
            var aufrufe = 0;
            var gerade = Versorgungsnetz.Gerade(starts, Ziele, (_, __) => { aufrufe++; return true; }, 1.35f);
            pruefe(gerade.Punkte == null && aufrufe == 0,
                $"Suchgrenze {fall}: keine teure Spurpruefung ausserhalb der Grenze");
            var gueltig = Versorgungsnetz.Gerade(starts,
                p => new[] { new Versorgungsweg.Ziel { Punkt = p + new float2(1.2f, 0) } },
                (_, __) => true, 1.35f);
            pruefe(gueltig.Punkte != null, $"Suchgrenze {fall}: kuerzere Gerade wird gefunden");
        }
        pruefe(neuKnoten < altKnoten && neuSicht < altSicht, "Suchgrenze: messbar weniger Grapharbeit");
        Console.WriteLine($"Suchgrenze 200 Faelle: Knoten {altKnoten} -> {neuKnoten}, Sichtpruefungen {altSicht} -> {neuSicht}");
    }

    private static void PruefeProjektionsFixpunkt(Action<bool, string> pruefe)
    {
        int vorher = 0, nachher = 0;
        for (int fall = 0; fall < 200; fall++)
        {
            float w = fall * 0.071f;
            float2 P(float x, float y) => new float2(x * math.cos(w) - y * math.sin(w),
                x * math.sin(w) + y * math.cos(w)) + new float2(8000, -7000);
            var a = P(0, 0); var b = P(100, 0); var c = P(10, 20); var d = P(90, 20);
            float2 Start(float t) => math.lerp(a, b, t);
            float2 Ziel(float t) => math.lerp(c, d, t);
            float2 SP(float2 p) => Versorgungsnetz.Projektion(p, a, b);
            float2 ZP(float2 p) => Versorgungsnetz.Projektion(p, c, d);
            var alt = new List<float2>();
            foreach (var p in Versorgungsnetz.Kantenstarts(a, b, c, d)) alt.Add(SP(p));
            for (int i = 0; i <= 16; i++)
            {
                var p = Start(i / 16f);
                for (int j = 0; j < 12; j++) { p = SP(ZP(p)); vorher++; }
                alt.Add(p);
            }
            var neu = new List<float2>(Versorgungsnetz.Kantenpunkte(Start, SP, Ziel, p => { nachher++; return ZP(p); }));
            pruefe(neu.Count == alt.Count, $"Fixpunkt {fall}: alle Kandidaten erhalten");
            for (int i = 0; i < alt.Count; i++)
                pruefe(math.all(neu[i] == alt[i]), $"Fixpunkt {fall}/{i}: bitgleicher Kandidat");
        }
        pruefe(nachher < vorher / 2, "Fixpunkt: mehr als die Haelfte der Projektionen eingespart");
        Console.WriteLine($"Fixpunkt: {vorher} -> {nachher} Projektionspaare, Kandidaten bitgleich");
    }

}
