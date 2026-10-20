using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class VersatzTests
{
    internal static float2[] Kontur(int n, float2 o)
    {
        var p = new List<float2> { new float2(-150, -100) + o, new float2(150, -100) + o };
        for (int i = 0; i <= n; i++)
            p.Add(new float2((float)(150 * Math.Cos(Math.PI * i / n)), (float)(180 - 150 * Math.Sin(Math.PI * i / n))) + o);
        return p.ToArray();
    }
    internal static void Pruefe(Action<bool, string> test)
    {
        foreach (var o in new[] { default(float2), new float2(-1500, 2000) })
        foreach (int n in new[] { 8, 24, 96 })
        {
            var ring = Kontur(n, o);
            test(Cs2Triangulierung.Dreiecke(ring) == ring.Length - 2, "Versatz-Eingang ist CS2-baubar");
            var uhr = Stopwatch.StartNew();
            var e = Kurvenversatz.Pruefe(ring, new[] { 1d, 6.9, 10.4, 13.9 });
            uhr.Stop();
            test(n == 8 ? e.Passt : e.Grund == Kurvenversatz.Grund.VersatzBrichtAb, "Echter heutiger Versatzabbruch wird erkannt");
            test(n == 8 || Math.Abs(e.Abstand - (n == 24 ? 10.4 : 6.9)) < 1e-9, "Erste abbrechende Versatztiefe");
            test(n == 8 || e.Kernmeldung.Contains("not counter-clockwise"), "Versatz nennt echte Kernursache");
            var um = Kurvenversatz.Pruefe(ring.Reverse().ToArray(), new[] { 1d, 6.9, 10.4, 13.9 });
            test(um.Grund == e.Grund && um.Abstand == e.Abstand, "Versatzpruefung bei Gegenrichtung gleich");
            Console.WriteLine($"Versatz n={n}, Ort={o}, {e.Grund}, d={e.Abstand:F1}, {uhr.Elapsed.TotalMilliseconds:F3} ms");
        }
        test(Kurvenversatz.Pruefe(new[] { new float2(float.NaN), new float2(1), new float2(2) }, new[] { 1d }).Grund
            == Kurvenversatz.Grund.Ungueltig, "Versatz NaN-sicher");
    }
}
