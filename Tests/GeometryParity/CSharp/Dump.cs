using System;
using System.Globalization;
using System.IO;
using System.Text;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/// <summary>
/// Gibt das VOLLSTAENDIGE Ergebnis als JSON aus, damit es Punkt fuer Punkt
/// gegen das JS-Modell gehalten werden kann.
///
/// Warum eigens dafuer: gleiche Buchtenzahl heisst nicht gleiche Geometrie.
/// Zwei Layouts koennen 226 Buchten haben und trotzdem anderswo liegen. Die
/// Abnahme prueft Zahlen, dieser Abzug prueft Koordinaten.
/// </summary>
internal static class Dump
{
    internal static void Write(string ordner)
    {
        Directory.CreateDirectory(ordner);
        foreach (var item in Program.Cases)
        {
            var layout = ParkingGeometry.Build(item.Site, LayoutSettings.Cs2);
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"stalls\": ").Append(layout.Stalls).Append(",\n");
            sb.Append("  \"angle\": ").Append(N(layout.Angle)).Append(",\n");
            Quads(sb, "bay", layout.Bay, true);
            Quads(sb, "cap", layout.Cap, true);
            Quads(sb, "median", layout.Median, true);
            Quads(sb, "green", layout.Green, true);
            Quads(sb, "perimeterQuad", layout.PerimeterQuad, true);
            Quads(sb, "entranceQuad", layout.EntranceQuad, true);
            Lines(sb, "aisleLine", layout.AisleLine, true);
            Quads(sb, "aisleQuad", layout.AisleQuad, true);
            Lines(sb, "crossLine", layout.CrossLine, true);
            Quads(sb, "crossQuad", layout.CrossQuad, true);
            Lines(sb, "perimeterLine", layout.PerimeterLine, true);
            Lines(sb, "entranceLine", layout.EntranceLine, true);
            Quads(sb, "fill", layout.Fill, true);
            Quads(sb, "fillHole", layout.FillHole, false);
            sb.Append("\n}\n");
            var datei = Path.Combine(ordner, Dateiname(item.Name) + ".json");
            File.WriteAllText(datei, sb.ToString());
            Console.WriteLine("Abzug: " + datei);
        }
    }

    private static string Dateiname(string name) =>
        name.Replace(" ", "-").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue");

    private static string N(double v) =>
        v.ToString("G17", CultureInfo.InvariantCulture);

    private static void Quads(StringBuilder sb, string name, float2[][] quads, bool komma)
    {
        sb.Append("  \"").Append(name).Append("\": [");
        for (var i = 0; i < quads.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[');
            for (var k = 0; k < quads[i].Length; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append('[').Append(N(quads[i][k].x)).Append(',')
                  .Append(N(quads[i][k].y)).Append(']');
            }
            sb.Append(']');
        }
        sb.Append(']').Append(komma ? ",\n" : "\n");
    }

    private static void Lines(StringBuilder sb, string name, float2[][] lines, bool komma)
        => Quads(sb, name, lines, komma);
}
