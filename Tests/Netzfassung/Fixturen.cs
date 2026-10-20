using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class NetzFixturen
{
    internal static void Merke(float2[] polygon, LayoutSettings settings, ParkingLayout layout)
    {
        string datei = Environment.GetEnvironmentVariable("PLT_NETZ_AUDIT");
        if (string.IsNullOrEmpty(datei)) return;
        var netz = layout.NetLine;
        string voll = Vergleich.Abdruck(layout);
        string ohneNetz;
        try
        {
            layout.NetLine = Array.Empty<NetSegment>();
            ohneNetz = Vergleich.Abdruck(layout);
        }
        finally { layout.NetLine = netz; }
        var zeile = new
        {
            Full = voll, Rest = ohneNetz,
            Polygon = polygon.Select(p => new[] { p.x, p.y }),
            Settings = Vergleich.Abdruck(settings),
            Modus = Environment.GetEnvironmentVariable("PLT_NETZ_MODUS"),
            Rand = settings.Randstrassen, Zufahrten = settings.Entrances?.Length,
            Net = netz.Select((n, i) => new { i, n.Kind,
                A = new[] { n.A.x, n.A.y }, B = new[] { n.B.x, n.B.y } }),
            layout.Stalls,
        };
        File.AppendAllText(datei, JsonSerializer.Serialize(zeile) + Environment.NewLine);
    }
}
