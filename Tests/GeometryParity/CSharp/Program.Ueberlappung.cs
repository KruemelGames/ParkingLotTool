using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /** Nachgestellter Objektfall fuer das Messgeraet der Ingame-Diagnose. */
    private static int RunUeberlappung()
    {
        var bucht = new[]
        {
            new double2(0, 0), new double2(6, 0),
            new double2(6, 4), new double2(0, 4),
        };
        var gebaeude = new[]
        {
            new double2(4, 1), new double2(8, 1),
            new double2(8, 3), new double2(4, 3),
        };
        var nurBeruehrt = new[]
        {
            new double2(6, 0), new double2(8, 0),
            new double2(8, 4), new double2(6, 4),
        };

        var trifft = ParkingOverlapGeometry.Measure(
            bucht, gebaeude, out var messung);
        var beruehrt = ParkingOverlapGeometry.Measure(
            bucht, nurBeruehrt, out _);
        var sauber = trifft
            && Math.Abs(messung.Area - 4.0) <= 1e-9
            && Math.Abs(messung.Penetration - 2.0) <= 1e-9
            && math.distance(messung.Center, new double2(5, 2)) <= 1e-9
            && !beruehrt;

        Console.WriteLine("UEBERLAPPUNG-NACHSTELLUNG: "
            + (sauber ? "alles sauber" : "FEHLER")
            + $"; Flaeche={messung.Area:F2} m2, Eindringtiefe="
            + $"{messung.Penetration:F2} m, Mittelpunkt="
            + $"{messung.Center.x:F2}/{messung.Center.y:F2}, "
            + $"reine Beruehrung={(beruehrt ? "faelschlich Treffer" : "kein Treffer")}");
        return sauber ? 0 : 1;
    }
}
