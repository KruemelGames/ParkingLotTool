using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class RandkurvenMessung
{
    internal static void Laufe(string[] args)
    {
        int runden = int.Parse(args.FirstOrDefault(a => a.StartsWith("--runden="))?.Substring(9) ?? "5");
        var datei = args.FirstOrDefault(a => a.StartsWith("--ausgabe="))?.Substring(10)
            ?? ".codex-build/kurven-12/laufzeit.csv";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datei)));
        using var log = new StreamWriter(datei);
        log.WriteLine("Fall;Zufahrt;AltMedianMs;NeuMedianMs;Faktor;Abdruck");
        foreach (var f in RandkurvenFaelle.Alle().Where(f => args.Contains("--alle") || (f.Name == "R100/150/True/False" || f.Name == "R100/150/False/False")
            || f.Name == "Lot136479" || f.Name == "Lot201905"))
        foreach (bool zufahrt in new[] { false, true })
        {
            var s = f.Settings.Clone(); s.Randstrassen = true;
            int k = (f.Lauf.Von + f.Lauf.Kanten / 2) % f.Polygon.Length;
            s.Entrances = zufahrt ? new[] { f.Settings.Entrances?.FirstOrDefault() ?? new Entrance
                { Edge = k, Along = math.distance(f.Polygon[k], f.Polygon[(k + 1) % f.Polygon.Length]) / 2 } }
                : Array.Empty<Entrance>();
            var zeiten = new[] { new List<double>(), new List<double>() };
            var abdruck = new string[2];
            for (int i = -2; i < runden; i++)
            for (int j = 0; j < 2; j++)
            {
                int art = (i & 1) == 0 ? j : 1 - j;
                s.Kurvenlaeufe = art == 0 ? Array.Empty<KurvenRandlauf>() : new[] { f.Lauf.Clone() };
                GC.Collect(); GC.WaitForPendingFinalizers();
                var uhr = Stopwatch.StartNew();
                var layout = ParkingGeometry.Build(f.Polygon, s);
                uhr.Stop();
                string hash = Vergleich.Abdruck(layout);
                if (abdruck[art] != null && abdruck[art] != hash)
                    throw new Exception("Warmlauf liefert anderes Layout: " + f.Name);
                abdruck[art] = hash;
                if (i >= 0) zeiten[art].Add(uhr.Elapsed.TotalMilliseconds);
            }
            double Median(List<double> werte)
            {
                werte.Sort();
                return (werte[(werte.Count - 1) / 2] + werte[werte.Count / 2]) / 2;
            }
            double alt = Median(zeiten[0]), neu = Median(zeiten[1]);
            string zeile = $"{f.Name};{zufahrt};{alt:F1};{neu:F1};{neu / alt:F3};{abdruck[1]}";
            log.WriteLine(zeile); log.Flush(); Console.WriteLine(zeile);
            if (args.Contains("--diagnose"))
            {
                s.Kurvenlaeufe = new[] { f.Lauf.Clone() };
                ParkingGeometry.LiveSchreiber = Console.WriteLine;
                try { ParkingGeometry.Build(f.Polygon, s); }
                finally { ParkingGeometry.LiveSchreiber = null; }
            }
        }
    }
}
