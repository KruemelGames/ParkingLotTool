using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * `--bericht <prebuild.json>`: rechnet die Vorschau eines Vorschau-Berichts
 * exakt nach (Umriss + alle Einstellungen aus `Input`) und prueft, ob jede
 * Fahrgasse von einer Zufahrt aus erreichbar ist.
 *
 * Seit 2026-09-25 schreibt "Vorschau melden" den Vorab-Abzug des aktuellen
 * Stands; damit wird jeder gemeldete Fall ein wiederholbarer Testfall.
 */
internal static partial class Program
{
    private static int RechneBerichtNach(string pfad)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(pfad));
        var input = doc.RootElement.GetProperty("Input");
        var optionen = new JsonSerializerOptions
        {
            Converters = { new Float2Json(), new JsonStringEnumConverter() },
        };
        var alle = input.GetProperty("LayoutSettings").GetProperty("AlleWerte");
        var settings = alle.Deserialize<LayoutSettings>(optionen);
        bool B(string n, bool v) => alle.TryGetProperty(n, out var e) ? e.GetBoolean() : v;
        settings.Zellen = B("Zellen", true);
        settings.EineFlaeche = B("EineFlaeche", false);
        settings.NoNotch = B("NoNotch", false);
        settings.Single = B("Single", false);
        settings.NoHalf = B("NoHalf", false);
        if (alle.TryGetProperty("KantenVersatz", out var kv)) settings.KantenVersatz = kv.GetDouble();
        var site = input.GetProperty("PolygonXZ").EnumerateArray()
            .Select(p => new float2(p.GetProperty("X").GetSingle(),
                p.GetProperty("Z").GetSingle())).ToArray();

        var layout = ParkingGeometry.Build(site, settings);
        Console.WriteLine(Path.GetFileName(pfad) + ": " + layout.Stalls + " Buchten, "
            + layout.NetLine.Length + " Netzlinien, Randstrassen "
            + (settings.Randstrassen ? "an" : "aus"));
        foreach (var n in layout.NetLine)
            Console.WriteLine($"  {n.Kind,-9} {n.Art,-9} ({n.A.x:F2}/{n.A.y:F2}) -> ({n.B.x:F2}/{n.B.y:F2})");
        var befund = RandstrassenErreichbarkeit.Pruefe(layout.NetLine, site,
            settings.Ai, settings.Cw);
        Console.WriteLine($"Erreichbarkeit: {befund.Erreicht} von {befund.Autowege} Autowegen, "
            + $"{befund.Quellen} Quelle(n), vollstaendig {befund.Vollstaendig}");
        foreach (var w in layout.Warnings) Console.WriteLine("  Warnung: " + w);
        return befund.Vollstaendig ? 0 : 1;
    }
}
