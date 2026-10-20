using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using K = ParkingLotTool.Geometry.Strassenkurve;

internal static class RandkurvenTests
{
    internal static void Laufe(string[] args)
    {
        Directory.CreateDirectory(".codex-build/kurven-11");
        var datei = args.FirstOrDefault(a => a.StartsWith("--ausgabe="))?.Substring(10)
            ?? ".codex-build/kurven-11/randbuchten.jsonl";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datei)));
        using var log = new StreamWriter(datei);
        int faelle = 0, fehler = 0;
        foreach (var f in RandkurvenFaelle.Alle())
        foreach (bool strasse in new[] { true, false })
        foreach (bool zufahrt in new[] { false, true })
        {
            if (!strasse && args.Contains("--nur-an")) continue;
            double radius = f.Radius; bool innen = f.Innen;
            var filter = args.FirstOrDefault(a => a.StartsWith("--fall="));
            if (filter != null && f.Name != filter.Substring(7)) continue;
            if (args.Contains("--kurz") && (f.Name != "R30/90/True/False" && f.Name != "R30/90/False/False" || zufahrt || !strasse)) continue;
            var polygon = f.Polygon;
            var s = f.Settings.Clone(); s.Randstrassen = strasse;
            s.Entrances = Array.Empty<Entrance>();
            if (zufahrt)
            {
                int edge = (f.Lauf.Von + f.Lauf.Kanten / 2) % polygon.Length;
                var zugang = f.Settings.Entrances?.FirstOrDefault() ?? new Entrance
                { Edge = edge, Along = math.distance(polygon[edge], polygon[(edge + 1) % polygon.Length]) / 2 };
                s.Entrances = new[] { zugang };
            }
            var altUhr = Stopwatch.StartNew();
            var alt = ParkingGeometry.Build(polygon, s);
            altUhr.Stop();
            s.Kurvenlaeufe = new[] { f.Lauf.Clone() };
            ParkingGeometry.LiveSchreiber = args.Contains("--diagnose") ? Console.WriteLine : null;
            var uhr = Stopwatch.StartNew();
            ParkingLayout neu;
            try { neu = ParkingGeometry.Build(polygon, s); }
            catch (Exception e)
            {
                uhr.Stop(); fehler++;
                log.WriteLine(JsonSerializer.Serialize(new { f.Name, Radius = radius, Innen = innen,
                    Strasse = strasse, Zufahrt = zufahrt, Fehler = e.ToString(), Ms = uhr.Elapsed.TotalMilliseconds }));
                log.Flush();
                Console.WriteLine($"FEHLER {f.Name}/{strasse}/{zufahrt}: {e.Message}");
                continue;
            }
            uhr.Stop();
            var kurven = neu.KurvenRandreihen.SelectMany(r => r.Buchten).Distinct().ToArray();
            var lampen = new[] { 20f, 30f, 40f }.Select(abstand => new { Abstand = abstand,
                Positionen = ParkingLanterns.Plan(neu, abstand).Laternen.Select(l =>
                    new { P = new[] { l.Position.x, l.Position.y }, R = new[] { l.Richtung.x, l.Richtung.y }, Art = l.Art.ToString() }) }).ToArray();
            var ringe = neu.GrassSurface.Concat(neu.AsphaltSurface).ToArray();
            int abgelehnt = ringe.Count(r => Cs2Triangulierung.Dreiecke(r) == 0);
            Console.WriteLine($"R{radius}/{innen}/{strasse}/{zufahrt}: {alt.PerimeterStalls}->{neu.PerimeterStalls}, Kurve {kurven.Length}, {uhr.ElapsedMilliseconds} ms, Ringe abgelehnt {abgelehnt}");
            float[][] Punkte(float2[] p) => p.Select(v => new[] { v.x, v.y }).ToArray();
            log.WriteLine(JsonSerializer.Serialize(new { f.Name, Radius = radius, Innen = innen, Strasse = strasse, Zufahrt = zufahrt,
                AltGras = alt.GrassSurface.Select(Punkte), AltAsphalt = alt.AsphaltSurface.Select(Punkte),
                AltMs = altUhr.Elapsed.TotalMilliseconds,
                AltAbdruck = strasse ? null : Vergleich.Abdruck(alt),
                NeuAbdruck = strasse ? null : Vergleich.Abdruck(neu),
                Formel = f.Lauf.Kontrollpunkte.Select(p => new[] { p.x, p.z }),
                Randabstand = s.Es, Buchtbreite = s.Sw, Buchttiefe = s.Sl, Fahrgassenbreite = s.Ai,
                AltBuchten = alt.Bay.Select(Punkte), AltBuchtarten = alt.BayKind.Select(k => k.ToString()),
                LaufVon = f.Lauf.Von, LaufKanten = f.Lauf.Kanten,
                Alt = alt.Stalls, Neu = neu.Stalls, AltRand = alt.PerimeterStalls, NeuRand = neu.PerimeterStalls,
                Ms = uhr.Elapsed.TotalMilliseconds, Polygon = Punkte(polygon), Buchten = neu.Bay.Select(Punkte), Kurven = kurven,
                Gras = neu.GrassSurface.Select(Punkte), Asphalt = neu.AsphaltSurface.Select(Punkte), Abgelehnt = abgelehnt,
                Achsen = neu.NetLine.Select(n => new { A = new[] { n.A.x, n.A.y }, B = new[] { n.B.x, n.B.y }, n.Kind }),
                Lampen = lampen,
                Standlinien = neu.KurvenRandreihen.Select(r => new { Punkte = Punkte(r.Standlinie), r.Stationen,
                    Sperren = r.Sperren.Select(sperre => new[] { sperre.x, sperre.y }) }),
                Warnungen = neu.Warnings }));
            log.Flush();
            if (strasse && kurven.Length == 0) { fehler++; Console.WriteLine("FEHLER: Keine radiale Randbucht geplant."); }
            if (abgelehnt != 0) { fehler++; Console.WriteLine("FEHLER: CS2 lehnt einen ausgegebenen Ring ab."); }
            faelle++;
        }
        Console.WriteLine($"Randkurven: {faelle} ausgegebene Varianten, {fehler} Fehler.");
        if (fehler != 0) throw new Exception($"Randkurven-Matrix: {fehler} Fehler.");
    }
}
