using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class RandmaterialGeraden
{
    internal static void Laufe(string[] args)
    {
        var datei = args.FirstOrDefault(a => a.StartsWith("--ausgabe="))?.Substring(10)
            ?? ".codex-build/kurven-13/geraden.jsonl";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datei)));
        using var log = new StreamWriter(datei);
        foreach (var f in RandkurvenFaelle.Alle())
        foreach (bool zufahrt in new[] { false, true })
        {
            double laenge = 0;
            var k = f.Lauf.Kontrollpunkte;
            // Nur die Vergleichslaenge; weder Bau noch Produktionsabtastung.
            for (int i = 0; i < k.Length; i += 4)
            {
                var vorher = new double2(k[i].x, k[i].z);
                for (int j = 1; j <= 256; j++)
                {
                    double t = j / 256d, u = 1 - t;
                    double2 Kontrolle(int n) => new double2(k[i + n].x, k[i + n].z);
                    var p = Kontrolle(0) * (u * u * u) + Kontrolle(1) * (3 * u * u * t)
                        + Kontrolle(2) * (3 * u * t * t) + Kontrolle(3) * (t * t * t);
                    laenge += math.distance(vorher, p); vorher = p;
                }
            }
            // Dieselben 120-m-Endkappen wie in der Bogenmatrix. Gemessen
            // wird nur das mittlere Stueck der gleichen Bordsteinlaenge.
            var s = f.Settings.Clone(); s.Randstrassen = true;
            s.Kurvenlaeufe = Array.Empty<KurvenRandlauf>(); s.Angle = 0;
            s.Entrances = zufahrt ? new[] { new Entrance { Edge = 0, Along = 120 + laenge / 2 } }
                : Array.Empty<Entrance>();
            float2 P(float x, float y) => new float2(1300 + x, 4600 + y);
            var layout = ParkingGeometry.Build(new[] { P(0, 0), P((float)laenge + 240, 0),
                P((float)laenge + 240, 80), P(0, 80) }, s);
            var reihe = Enumerable.Range(0, layout.Bay.Length).Where(i =>
            {
                var p = layout.Bay[i].Aggregate(float2.zero, (a, b) => a + b) / 4;
                return layout.BayKind[i] == BayKind.Perimeter
                    && Math.Abs(p.y - (4600 + s.Es + s.Sl / 2)) < 0.002
                    && p.x >= 1420 && p.x <= 1420 + laenge;
            }).ToArray();
            float[][] Punkte(float2[] ring) => ring.Select(p => new[] { p.x, p.y }).ToArray();
            int abgelehnt = layout.GrassSurface.Concat(layout.AsphaltSurface)
                .Count(r => Cs2Triangulierung.Dreiecke(r) == 0);
            if (reihe.Length == 0 || abgelehnt != 0)
                throw new Exception("Gerade Vergleichsreihe fehlt oder hat unbaubare Ringe: " + f.Name);
            log.WriteLine(JsonSerializer.Serialize(new { f.Name, Strasse = true, Zufahrt = zufahrt,
                Laenge = laenge, Randabstand = s.Es, Buchttiefe = s.Sl, Fahrgassenbreite = s.Ai,
                Gras = layout.GrassSurface.Select(Punkte), Asphalt = layout.AsphaltSurface.Select(Punkte),
                Buchten = layout.Bay.Select(Punkte), Kurven = reihe, Abgelehnt = abgelehnt,
                Standlinien = new[] { new { Punkte = Punkte(new[] { P(120, (float)s.Es / 2),
                    P((float)laenge + 120, (float)s.Es / 2) }) } } }));
        }
        Console.WriteLine("Gerade Vergleichsreihen: 48 Varianten, 0 fehlende Reihen, 0 abgelehnte Ringe.");
    }
}
