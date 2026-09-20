using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

class Program
{
    static int checks;
    static void Check(bool valid, string message)
    {
        checks++;
        if (!valid) throw new Exception(message);
    }
    static float2[] Road(float x) => new[] { new float2(x, 0), new float2(x, 40) };
    static void Main()
    {
        var before = new[] { Road(0), Road(40) };
        Check(ZoningErhalt.Gleich(before, new[] { Road(40), Road(0) }, "Alley", "Alley"), "Unveraendertes Netz trotz anderer Reihenfolge erhalten");
        Check(!ZoningErhalt.Gleich(before, new[] { Road(0), Road(41) }, "Alley", "Alley"), "Verschobenes Netz nicht erhalten");
        Check(!ZoningErhalt.Gleich(before, new[] { Road(0), Road(0) }, "Alley", "Alley"), "Doppelte Kurse ersetzen keine fehlende Strasse");
        Check(!ZoningErhalt.Gleich(before, new[] { Road(0) }, "Alley", "Alley"), "Entfernte Strasse erkennen");
        Check(!ZoningErhalt.Gleich(before, before, "Alley", "Gravel Road"), "Prefabwechsel erkennen");
        var reversed = Road(40); Array.Reverse(reversed);
        Check(!ZoningErhalt.Gleich(before, new[] { Road(0), reversed }, "Alley", "Alley"), "Seitenwechsel erkennen");
        Check(!ZoningErhalt.Gleich(null, before, "Alley", "Alley"), "Keine Baseline ist kein Erhalt");
        Check(!ZoningErhalt.Gleich(Array.Empty<float2[]>(), Array.Empty<float2[]>(), "Alley", "Alley"), "Leere Netze nicht erhalten");
        Console.WriteLine($"Zoningerhalt: {checks} Pruefungen, 0 Fehler");
    }
}
