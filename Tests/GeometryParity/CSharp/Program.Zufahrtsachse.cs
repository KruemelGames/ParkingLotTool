using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /*
     * Gemessener Fall vom 2026-09-22: 10,4 m Lot-Tiefe werden bei
     * Projektion 0,8 zu 13,0 m Achse. Die Mutation ohne Kernweitergabe
     * ergab 0,632 Richtungsfehler und 2,600 m Laengenfehler.
     */
    private static int RunZufahrtsachse()
    {
        var form = new[]
        {
            new float2(0, 0), new float2(150, 0),
            new float2(150, 120), new float2(0, 120),
        };
        var fehler = 0;
        foreach (var gefangen in new[] { false, true })
        {
            var e = LayoutSettings.Cs2;
            e.Zellen = true;
            e.Auto = false;
            e.Randstrassen = true;
            e.AutomaticEntrances = false;
            var zugang = new Entrance { Edge = 0, Along = 75 };
            if (gefangen)
            {
                zugang.AxisDirection = new float2(0.6f, 0.8f);
                zugang.AxisLength = 13;
            }
            e.Entrances = new[] { zugang };
            var bau = ParkingGeometry.Build(form, e);
            if (bau.EntranceLine.Length != 1)
            {
                Console.WriteLine("FEHLER: erwartete genau eine gebaute Zufahrt");
                fehler++;
                continue;
            }
            var linie = bau.EntranceLine[0];
            var v = linie[1] - linie[0];
            var laenge = math.length(v);
            var richtung = v / laenge;
            var soll = gefangen ? new float2(0.6f, 0.8f) : new float2(0, 1);
            var richtungsfehler = math.distance(richtung, soll);
            var laengenfehler = gefangen ? math.abs(laenge - 13f) : 0f;
            Console.WriteLine($"Fang={gefangen}: "
                + $"Richtung ({richtung.x:F3}/{richtung.y:F3}), "
                + $"Laenge {laenge:F3} m, "
                + $"Richtungsfehler {richtungsfehler:F3}, "
                + $"Laengenfehler {laengenfehler:F3} m");
            if (richtungsfehler > 0.01f || laengenfehler > 0.01f)
                fehler++;
        }
        Console.WriteLine($"Zufahrtsachse: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
