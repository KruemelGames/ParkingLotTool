using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class Program
{
    private static readonly JsonSerializerOptions Optionen = new JsonSerializerOptions
    { IncludeFields = true, Converters = { new VektorLeser(), new JsonStringEnumConverter() } };

    private static int Main(string[] args)
    {
        try { Laufe(args); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Laufe(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Contains("--schlusszug")) { Schlusszug.Laufe(args); return; }
        if (args.Contains("--kurvenzufahrten")) { Kurvenzufahrten.Laufe(args); return; }
        if (args.Contains("--randgeraden")) { RandmaterialGeraden.Laufe(args); return; }
        if (args.Contains("--randzeit")) { RandkurvenMessung.Laufe(args); return; }
        if (args.Contains("--randlogik")) { RandkurvenLogik.Laufe(); return; }
        if (args.Contains("--randbuchten")) { RandkurvenTests.Laufe(args); return; }
        string ablage = args.Length > 0 ? args[0] : ".codex-build/kurven-9";
        Directory.CreateDirectory(ablage);
        var faelle = Formen.Alle().ToList();
        using (var daten = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Spiel20261019.json"))))
            foreach (var d in daten.RootElement.EnumerateArray())
            {
                var s = d.GetProperty("Settings").Deserialize<LayoutSettings>(Optionen);
                var roh = d.GetProperty("Settings");
                bool B(string n) => roh.TryGetProperty(n, out var v) && v.GetBoolean();
                s.Zellen = true; s.EineFlaeche = B("EineFlaeche"); s.NoNotch = B("NoNotch"); s.Single = B("Single"); s.NoHalf = B("NoHalf");
                faelle.Add(new Formfall { Name = "Spiel/" + d.GetProperty("Zeit").GetString(),
                    Polygon = d.GetProperty("Polygon").EnumerateArray().Select(p => new float2(p[0].GetSingle(), p[1].GetSingle())).ToArray(),
                    Bogen = Array.Empty<float2>(), Settings = s,
                    ErwarteteBuchten = d.GetProperty("ErwarteteBuchten").GetInt32() });
            }
        if (args.Length > 1) faelle = faelle.Where(f => f.Name.Contains(args[1])).ToList();
        // JIT des normalen Layoutpfads vor der Messmatrix anwaermen.
        var warm = LayoutSettings.Cs2; warm.Auto = false; warm.AngleMode = "fixed";
        ParkingGeometry.Build(new[] { new float2(0, 0), new float2(120, 0), new float2(120, 90), new float2(0, 90) }, warm);
        using var ausgabe = new StreamWriter(Path.Combine(ablage, "faelle.jsonl"), false);
        int bauten = 0, grenzen = 0;
        foreach (var f in faelle)
        foreach (bool rand in new[] { false, true })
        foreach (int anzahl in new[] { 0, 1 })
        {
            var s = f.Settings.Clone(); s.Randstrassen = rand; s.AutomaticEntrances = false;
            var zugang = (s.Entrances ?? Array.Empty<Entrance>()).FirstOrDefault();
            if (zugang == null)
            {
                int k = f.Polygon.Length - 2;
                zugang = new Entrance { Edge = k, Along = math.distance(f.Polygon[k], f.Polygon[(k + 1) % f.Polygon.Length]) / 2 };
            }
            s.Entrances = anzahl == 0 ? Array.Empty<Entrance>() : new[] { zugang };
            var abstaende = rand ? new[] { s.Es, s.Es + s.Sl, s.Es + s.Sl + s.Ai / 2, s.Es + s.Sl + s.Ai } : new[] { s.Es };
            var versatz = Kurvenversatz.Pruefe(f.Polygon, abstaende);
            ParkingLayout layout = null;
            string fehler = null;
            var uhr = Stopwatch.StartNew();
            try { layout = ParkingGeometry.Build(f.Polygon, s); }
            catch (InvalidOperationException e) { fehler = e.ToString(); }
            catch (ArgumentException e) { fehler = e.ToString(); }
            uhr.Stop();
            if (layout != null) bauten++; else grenzen++;
            if (layout != null && f.ErwarteteBuchten.HasValue && rand && anzahl == 1
                && layout.Stalls != f.ErwarteteBuchten.Value)
                throw new Exception($"Spielbau {f.Name}: {layout.Stalls} statt {f.ErwarteteBuchten} Buchten.");
            var ringe = layout == null ? Array.Empty<float2[]>() : layout.GrassSurface.Concat(layout.AsphaltSurface)
                .Concat(layout.ZoningSurface).Concat(layout.ZoningRoadSurface).ToArray();
            int abgelehnt = ringe.Count(p => Cs2Triangulierung.Dreiecke(p) == 0);
            var zeile = new {
                f.Name, Rand = rand, Zufahrten = anzahl, f.Stuecke, Punkte = f.Polygon.Length,
                Versatz = versatz.Grund.ToString(), VersatzAbstand = versatz.Abstand, VersatzMeldung = versatz.Kernmeldung,
                Fehler = fehler, Ms = uhr.Elapsed.TotalMilliseconds, Buchten = layout?.Stalls,
                Randbuchten = layout?.PerimeterStalls, Innenbuchten = layout?.InnerStalls,
                Winkel = layout?.Angle, Randabstand = s.Es, Buchttiefe = s.Sl, Fahrgassenbreite = s.Ai,
                Ringe = ringe.Length, Abgelehnt = abgelehnt, Warnungen = layout?.Warnings,
                Polygon = Koordinaten(f.Polygon), Bogen = Koordinaten(f.Bogen),
                Flaechen = ringe.Select(Koordinaten), Gras = layout?.GrassSurface.Select(Koordinaten),
                Strassen = layout?.PerimeterLine.Select(Koordinaten), BuchtenPolygone = layout?.Bay.Select(Koordinaten),
                Buchtarten = layout?.BayKind.Select(k => k.ToString())
            };
            ausgabe.WriteLine(JsonSerializer.Serialize(zeile)); ausgabe.Flush();
            Console.WriteLine($"{f.Name}/{rand}/{anzahl}: {uhr.Elapsed.TotalMilliseconds:F1} ms, {layout?.Stalls} Buchten, "
                + $"Versatz {versatz.Grund}/{versatz.Abstand:F1}, {abgelehnt}/{ringe.Length} abgelehnt; {fehler?.Split('\n')[0]}");
        }
        Console.WriteLine($"Messmatrix: {bauten} Layouts, {grenzen} abgebrochene Varianten. Abnahme separat mit pruefung.py --abnahme.");
    }

    private static float[][] Koordinaten(float2[] p) => p.Select(v => new[] { v.x, v.y }).ToArray();

    private sealed class VektorLeser : JsonConverter<float2>
    {
        public override float2 Read(ref Utf8JsonReader r, Type type, JsonSerializerOptions o)
        {
            using var d = JsonDocument.ParseValue(ref r);
            var p = d.RootElement;
            return p.ValueKind == JsonValueKind.Array ? new float2(p[0].GetSingle(), p[1].GetSingle())
                : new float2(p.GetProperty("x").GetSingle(), p.GetProperty("y").GetSingle());
        }
        public override void Write(Utf8JsonWriter w, float2 p, JsonSerializerOptions o) => throw new NotSupportedException();
    }
}
