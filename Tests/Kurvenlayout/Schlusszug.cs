using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Schlusszug
{
    private static int _bauten, _module, _bandfehler, _nullkanten;

    internal static void Laufe(string[] args)
    {
        using var datei = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schlusszug2033.json")));
        var d = datei.RootElement;
        var opt = new JsonSerializerOptions { IncludeFields = true,
            Converters = { new VektorLeser(), new JsonStringEnumConverter() } };
        var vorlage = d.GetProperty("Settings").Deserialize<LayoutSettings>(opt);
        // Interne Schalter werden wie im Produktionsaufruf uebernommen.
        var roh = d.GetProperty("Settings");
        bool B(string n) => roh.TryGetProperty(n, out var v) && v.GetBoolean();
        vorlage.Zellen = B("Zellen"); vorlage.EineFlaeche = B("EineFlaeche");
        vorlage.NoNotch = B("NoNotch"); vorlage.Single = B("Single"); vorlage.NoHalf = B("NoHalf");
        var kontrollen = d.GetProperty("Kontrollpunkte46").EnumerateArray()
            .Select(v => new float3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle())).ToArray();
        var pfad = args.FirstOrDefault(a => a.StartsWith("--ausgabe="))?.Substring(10)
            ?? ".codex-build/kurven-16/schlusszug.jsonl";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfad)));
        using var log = new StreamWriter(pfad);
        int nummer = 0;
        foreach (var f in d.GetProperty("Faelle").EnumerateArray())
        {
            int stand = f.GetProperty("Stand").GetInt32();
            nummer++;
            if (args.Any(a => a.StartsWith("--stand=")) && !args.Contains("--stand=" + stand)) continue;
            var polygon = f.GetProperty("Polygon").EnumerateArray()
                .Select(v => new float2(v[0].GetSingle(), v[1].GetSingle())).ToArray();
            var k = Kurvenzettel.Zuschneiden(kontrollen, polygon[0], polygon[polygon.Length - 1], Strassenkurve.Pfeilhoehe + .05);
            var lauf = k == null ? Kurvenzufahrten.Archivkurve(polygon, 0, polygon.Length - 1)
                : new KurvenRandlauf { Von = 0, Kanten = polygon.Length - 1, Kontrollpunkte = k };
            foreach (bool strasse in args.Contains("--befund") ? new[] { true } : new[] { true, false })
            foreach (bool umgekehrt in args.Contains("--befund") ? new[] { false } : new[] { false, true })
                Baue($"Log/{nummer}/{stand}", polygon, lauf, vorlage, strasse, umgekehrt, log, args);
        }
        if (args.Contains("--raster"))
        {
            var polygon = d.GetProperty("Faelle")[d.GetProperty("Faelle").GetArrayLength() - 1]
                .GetProperty("Polygon").EnumerateArray()
                .Select(v => new float2(v[0].GetSingle(), v[1].GetSingle())).ToArray();
            foreach (var f in Endlagen(kontrollen, polygon).Concat(Sehnenenden(kontrollen, polygon)))
            foreach (bool strasse in new[] { true, false })
            foreach (bool umgekehrt in new[] { false, true })
                Baue(f.Name, f.Polygon, f.Lauf, vorlage, strasse, umgekehrt, log, args);
        }
        if (!args.Contains("--befund")) PruefeZuschnitt(kontrollen);
        Console.WriteLine($"Schlusszug: {_bauten} Bauten, {_module} zu kleine Innenkonturen, {_nullkanten} ungueltige Gitter-Nullkanten, {_bandfehler} Bandfehler.");
    }

    private static void Baue(string name, float2[] polygon, KurvenRandlauf lauf, LayoutSettings vorlage,
        bool strasse, bool umgekehrt, StreamWriter log, string[] args)
    {
        var filter = args.FirstOrDefault(a => a.StartsWith("--name="))?.Substring(7);
        if (filter != null && name != filter) return;
        polygon = polygon.ToArray(); lauf = lauf.Clone();
        if (umgekehrt) { Array.Reverse(polygon); Array.Reverse(lauf.Kontrollpunkte); }
        var s = vorlage.Clone(); s.Randstrassen = strasse;
        s.Kurvenlaeufe = args.Contains("--ohnekurven") ? Array.Empty<KurvenRandlauf>() : new[] { lauf };
        // Die zusaetzlichen 1-mm-Sehnenproben koennen am Welt-float exakt
        // auf dasselbe Eingabegitterfeld wie ein Innenpunkt fallen. Das ist
        // ein eigener Negativfall, kein gueltiges Polygon mit Bandfehler.
        double G(float v) => Math.Round(v / ParkingGeometry.ZellenEingangsgitter);
        bool nullkante = Enumerable.Range(0, polygon.Length).Any(i =>
            G(polygon[i].x) == G(polygon[(i + 1) % polygon.Length].x)
            && G(polygon[i].y) == G(polygon[(i + 1) % polygon.Length].y));
        if (nullkante)
        {
            bool nullAbgelehnt = false;
            try { ParkingGeometry.Build(polygon, s); }
            catch (InvalidOperationException e) when (e.Message == "The outer ring contains a zero-length edge.")
            { nullAbgelehnt = true; }
            if (!nullAbgelehnt) throw new Exception(name + ": Nullkante wurde nicht abgelehnt.");
            _nullkanten++;
            Console.WriteLine(name + ": identische Eingangsgitterpunkte, korrekt als ungueltiger Umriss abgelehnt.");
            return;
        }
        var uhr = Stopwatch.StartNew();
        ParkingLayout layout;
        try { layout = ParkingGeometry.Build(polygon, s); }
        catch (InvalidOperationException ex)
        {
            if (args.Contains("--befund"))
            {
                if (ex.Message.Contains("Kurvenkeil liegt ausserhalb")) _bandfehler++;
                Console.WriteLine($"{name}/{strasse}/{umgekehrt}: {ex.Message}"); return;
            }
            if (strasse && ex.Message == "No parking module fits inside the inner contour.")
            {
                // Die Modulhoehe wird separat gegen den geplanten Innenrand
                // gemessen; keine Freigabe anderer Ausnahmen oder Bandfehler.
                PruefeModulgrenze(polygon, s);
                _module++; return;
            }
            throw;
        }
        _bauten++;
        int abgelehnt = layout.GrassSurface.Concat(layout.AsphaltSurface).Count(r => Cs2Triangulierung.Dreiecke(r) == 0);
        if (abgelehnt != 0) throw new Exception(name + ": CS2 lehnt einen Materialring ab.");
        if (layout.Warnings.Any(w => w.Contains("hole separation")))
            throw new Exception(name + ": Materialring braucht Lochtrennung.");
        if (!strasse && !args.Contains("--befund"))
        {
            var alt = s.Clone(); alt.Kurvenlaeufe = Array.Empty<KurvenRandlauf>();
            if (Vergleich.Abdruck(layout) != Vergleich.Abdruck(ParkingGeometry.Build(polygon, alt)))
                throw new Exception(name + ": AUS weicht ohne Kurvenlaeufe ab.");
        }
        float[][] Punkte(float2[] p) => p.Select(v => new[] { v.x, v.y }).ToArray();
        log.WriteLine(JsonSerializer.Serialize(new { Name = name, Strasse = strasse, Umgekehrt = umgekehrt,
            Polygon = Punkte(polygon), Buchten = layout.Bay.Select(Punkte),
            Gras = layout.GrassSurface.Select(Punkte), Asphalt = layout.AsphaltSurface.Select(Punkte),
            Standlinien = Array.Empty<int>(), Lampen = Array.Empty<int>(),
            Achsen = layout.NetLine.Select(n => new { n.Kind, A = new[] { n.A.x, n.A.y }, B = new[] { n.B.x, n.B.y } }),
            Kurven = layout.KurvenRandreihen.SelectMany(r => r.Buchten), Abgelehnt = abgelehnt,
            Vollabdruck = Vergleich.Abdruck(layout), layout.Stalls, Warnungen = layout.Warnings, Ms = uhr.Elapsed.TotalMilliseconds }));
        Console.WriteLine($"{name}/{strasse}/{umgekehrt}: {layout.Stalls} Buchten, G/A {layout.GrassSurface.Length}/{layout.AsphaltSurface.Length}, {uhr.Elapsed.TotalMilliseconds:F0} ms");
    }

    private sealed class VektorLeser : JsonConverter<float2>
    {
        public override float2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var d = JsonDocument.ParseValue(ref reader);
            return new float2(d.RootElement[0].GetSingle(), d.RootElement[1].GetSingle());
        }
        public override void Write(Utf8JsonWriter writer, float2 value, JsonSerializerOptions options)
            => throw new NotSupportedException();
    }
}
