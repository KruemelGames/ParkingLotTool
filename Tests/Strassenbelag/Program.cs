using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class StrassenbelagTests
{
    private static int _tests;
    private static int _fehler;
    private static void Pruefe(bool ok, string name)
    {
        _tests++;
        if (!ok) throw new InvalidOperationException(name);
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--messen"))
            {
                MesseLaufzeit();
                return 0;
            }
            if (!args.Contains("--aufnahme"))
            {
                KettenTest();
                SaumkontaktTest();
                ArchivTest();
                try { EckalleyTest(); } catch (InvalidOperationException e) { _fehler++; Console.Error.WriteLine(e.Message); }
                try { BrueckenTest(); } catch (InvalidOperationException e) { _fehler++; Console.Error.WriteLine(e.Message); }
            }
            var ausgabe = new Dictionary<string, string>();
            var messungen = new List<object>();
            var datei = args.LastOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                ?? Path.Combine(AppContext.BaseDirectory, "Referenz.json");
            var materialdatei = datei + ".material.json";
            var material = args.Contains("--aufnahme") ? new Dictionary<string, Materialstand>()
                : JsonSerializer.Deserialize<Dictionary<string, Materialstand>>(File.ReadAllText(materialdatei));
            foreach (var fall in Faelle())
            {
                var layout = ParkingGeometry.Build(fall.Polygon, fall.Settings);
                ausgabe[fall.Name] = Abdruck(layout, false);
                ausgabe[fall.Name + "/bauplan"] = Abdruck(layout, true);
                if (args.Contains("--aufnahme")) material[fall.Name] = Merke(layout);
                else
                {
                    messungen.Add(new
                    {
                        Name = fall.Name,
                        Polygon = fall.Polygon.Select(p => new[] { p.x, p.y }).ToArray(),
                        Alt = material[fall.Name], Neu = Merke(layout),
                        Gassen = layout.NetLine.Where(n => n.Kind == "entrance" && Zufahrtsarten.IstGasse(n.Art))
                            .Select(n => new[] { new[] { n.A.x, n.A.y }, new[] { n.B.x, n.B.y } }).ToArray(),
                        Road = ((float2[][])typeof(ParkingLayout).GetProperty("StrassenbelagSurface").GetValue(layout))
                            .Select(r => r.Select(p => new[] { p.x, p.y }).ToArray()).ToArray(),
                    });
                    try { Messe(fall.Name, fall.Polygon, layout, material[fall.Name]); }
                    catch (InvalidOperationException e) { _fehler++; Console.Error.WriteLine(e.Message); }
                }
            }
            if (args.Contains("--aufnahme"))
            {
                File.WriteAllText(datei, JsonSerializer.Serialize(ausgabe, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(materialdatei, JsonSerializer.Serialize(material));
            }
            else
            {
                var alt = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(datei));
                foreach (var p in ausgabe)
                    if (p.Key.Contains("/ohne/") || p.Key.EndsWith("/bauplan"))
                        Pruefe(alt[p.Key] == p.Value, "Bitvergleich: " + p.Key);
            }
            if (!args.Contains("--aufnahme")) File.WriteAllText(".codex-build/issue22/messungen.json", JsonSerializer.Serialize(messungen));
            Console.WriteLine($"Strassenbelag: {_tests} Pruefungen, {_fehler} Fehler.");
            return _fehler == 0 ? 0 : 1;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static string Abdruck(ParkingLayout layout, bool bauplan)
    {
        var geometrie = new HashSet<string> { "Bay", "BayKind", "BayRole", "ElectricPair", "NetLine",
            "PerimeterLine", "AisleLine", "CrossLine", "EntranceLine", "KurvenRandreihen", "SpecialStalls" };
        return Vergleich.Abdruck(typeof(ParkingLayout).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.Name != "StrassenbelagSurface" && p.Name != "FahrbahnSurface" && (!bauplan || geometrie.Contains(p.Name)))
            .OrderBy(p => p.Name).Select(p => new object[] { p.Name, p.GetValue(layout) }).ToArray());
    }

    private static IEnumerable<(string Name, float2[] Polygon, LayoutSettings Settings)> Faelle()
    {
        for (int form = 0; form < 3; form++)
        foreach (bool rand in new[] { true, false })
        foreach (var art in new[] { Zufahrtsart.Zufahrt, Zufahrtsart.Fussweg, Zufahrtsart.Gasse, Zufahrtsart.GasseEin, Zufahrtsart.GasseAus })
        {
            var polygon = new[] { new float2(0, 0), new float2(120, 0), new float2(120, 90), new float2(0, 90) };
            if (form == 1) polygon[1] = new float2(120, 30);
            if (form == 2) polygon = polygon.Select(p => new float2(p.x * .8f - p.y * .6f + 1300,
                p.x * .6f + p.y * .8f + 4600)).ToArray();
            var s = LayoutSettings.Cs2;
            s.Zellen = true; s.Randstrassen = rand; s.AutomaticEntrances = false;
            s.Gassenbreite = 5.5;
            s.Entrances = new[] { new Entrance { Edge = 0, Along = 60, Art = art } };
            var name = $"{form}/{rand}/" + (Zufahrtsarten.IstGasse(art) ? "gasse/" : "ohne/") + art;
            yield return (name, polygon, s);
            if (form == 0 && art == Zufahrtsart.Gasse)
            {
                var zusammen = s.Clone();
                zusammen.Entrances = zusammen.Entrances.Concat(new[]
                    { new Entrance { Edge = 2, Along = 60, Art = Zufahrtsart.GasseEin } }).ToArray();
                yield return (name + "/zwei", polygon, zusammen);
                var einheitlich = s.Clone(); einheitlich.EineFlaeche = true;
                yield return (name + "/einheitlich", polygon, einheitlich);
            }
        }
    }

}
