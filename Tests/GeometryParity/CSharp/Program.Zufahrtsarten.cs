using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunZufahrtsarten()
    {
        var site = new[]
        {
            new float2(0, 0), new float2(120, 0),
            new float2(120, 90), new float2(0, 90),
        };
        var faelle = new[]
        {
            (Art: Zufahrtsart.Zufahrt, Breite: 7.0),
            (Art: Zufahrtsart.Einfahrt, Breite: 4.0),
            (Art: Zufahrtsart.Ausfahrt, Breite: 4.0),
            (Art: Zufahrtsart.Fussweg, Breite: 2.0),
        };
        var fehler = false;
        Console.WriteLine("Zugangsarten: konstruierte Flaechenbreite und NetLine-Art");
        foreach (var zellen in new[] { false, true })
        foreach (var fall in faelle)
        {
            var settings = LayoutSettings.Cs2;
            settings.Zellen = zellen;
            settings.AutomaticEntrances = false;
            settings.Entrances = new[]
            {
                new Entrance { Edge = 0, Along = 60, Art = fall.Art },
            };
            var layout = ParkingGeometry.Build(site, settings);
            var quad = layout.EntranceQuad.Single();
            // Die Testzufahrt liegt an der waagerechten Kante 0; deshalb ist
            // ihre konstruierte Breite die X-Ausdehnung. Die beiden Kerne
            // geben Polygonpunkte absichtlich in verschiedener Reihenfolge aus.
            var breite = quad.Max(punkt => punkt.x) - quad.Min(punkt => punkt.x);
            var netz = layout.NetLine.Single(stueck => stueck.Kind == "entrance");
            var stimmt = Math.Abs(breite - fall.Breite) <= 1e-4
                && layout.Entrances.Single().Art == fall.Art
                && netz.Art == fall.Art
                && netz.B.y > netz.A.y;
            Console.WriteLine($"  {(zellen ? "Zellen" : "Klassisch"),-9} "
                + $"{fall.Art,-8}: Flaeche {breite:F3} m | "
                + $"Art {netz.Art} | Kurs aussen->innen "
                + $"{netz.A.y:F3}->{netz.B.y:F3} | {(stimmt ? "OK" : "FEHLER")}");
            fehler |= !stimmt;
        }
        return fehler ? 1 : 0;
    }
}
