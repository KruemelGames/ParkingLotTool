using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class StrassenbelagTests
{
    private static void MesseLaufzeit()
    {
        var faelle = Faelle().Where(f => f.Name.EndsWith("/gasse/Gasse", StringComparison.Ordinal)).ToList();
        var archiv = Archivdaten();
        faelle.Add(("Lot201905", archiv.Polygon, archiv.Settings));
        foreach (var f in faelle)
        {
            for (int warm = 0; warm < 3; warm++) ParkingGeometry.Build(f.Polygon, f.Settings);
            var zeiten = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var uhr = Stopwatch.StartNew();
                var layout = ParkingGeometry.Build(f.Polygon, f.Settings);
                uhr.Stop();
                zeiten.Add(uhr.Elapsed.TotalMilliseconds);
                GC.KeepAlive(layout);
            }
            zeiten.Sort();
            Console.WriteLine($"{f.Name}: Median {zeiten[2]:F1} ms, min {zeiten[0]:F1}, max {zeiten[4]:F1}");
        }
    }
}
