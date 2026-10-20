using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class Zufahrtspruefung
{
    private static int _tests;
    private static readonly JsonSerializerOptions Optionen = new JsonSerializerOptions
    { IncludeFields = true, Converters = { new VektorLeser(), new JsonStringEnumConverter() } };
    private static void Pruefe(bool ok, string name)
    { _tests++; if (!ok) throw new Exception(name); }
    private static int Main(string[] args)
    {
        try { Laufe(args); Console.WriteLine($"Zufahrtsvorschau: {_tests} Pruefungen, 0 Fehler."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Laufe(string[] args)
    {
        if (args.Length > 0 && args[0] == "--paritaet")
        {
            var run = typeof(Program).GetMethod("Run", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            int code = (int)run.Invoke(null, new object[] { args.Skip(1).ToArray() });
            Pruefe(code == 0, "GeometryParity mit doppeltem Bau");
            var zaehler = typeof(ParkingGeometry).GetField("VerglicheneVorschauen");
            Console.WriteLine("Bitgleich gepruefte Layouts: " + (zaehler?.GetValue(null) ?? 0));
            return;
        }
        VorbereitungTests.Laufe(Pruefe);
        SchnittTests.Laufe(Pruefe, !args.Contains("--referenz"));
        if (args.Contains("--cache")) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Bericht-20261019.json")));
        var daten = doc.RootElement;
        PruefeRinge(daten);
        if (args.Contains("--ringe")) return;
        var polygon = Punkte(daten.GetProperty("Polygon"));
        var settings = Einstellungen(daten.GetProperty("Settings"));
        if (args.Contains("--messen")) { Messe(polygon, settings, args.Contains("--ohne-vorbereitung")); return; }
        var faelle = new List<(string Name, float2[] Polygon, LayoutSettings Settings)> { ("Bericht71", polygon, settings) };
        using (var lot = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Lot136479.json"))))
        {
            var punktfolge = Punkte(lot.RootElement.GetProperty("Polygon"));
            var s = Einstellungen(lot.RootElement.GetProperty("Settings"));
            // Zwei echte gesetzte Lagen, fuer den kontrollierten Vergleich
            // kombiniert; der Bauzettel von 15:34:12 selbst enthaelt nur eine.
            s.Entrances = s.Entrances.Concat(new[] {
                lot.RootElement.GetProperty("ZweiteZufahrt").Deserialize<Entrance>(Optionen) }).ToArray();
            if (args.Contains("--messen-lot"))
            {
                foreach (bool rand in new[] { false, true })
                {
                    s.Randstrassen = rand;
                    Console.WriteLine("LOT136479 Randstrassen=" + rand);
                    Messe(punktfolge, s, args.Contains("--ohne-vorbereitung"));
                }
                return;
            }
            foreach (bool rand in new[] { false, true })
            {
                var variante = s.Clone(); variante.Randstrassen = rand;
                faelle.Add(("Lot136479/" + rand, punktfolge, variante));
            }
        }
        // Zufahrt ueber das Ende einer Kurvensehne (Vorschau 19.10. 20:19):
        // die Stirnecke lag 3,3 cm ausserhalb und galt als Hindernis.
        using (var lot = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Lot201905.json"))))
        {
            var punktfolge = Punkte(lot.RootElement.GetProperty("Polygon"));
            var s = Einstellungen(lot.RootElement.GetProperty("Settings"));
            foreach (var auswahl in new[] { new[] { 0 }, new[] { 1 }, new[] { 0, 1 } })
            {
                var variante = s.Clone(); variante.Randstrassen = false;
                variante.Entrances = auswahl.Select(i => s.Entrances[i]).ToArray();
                var layout = ParkingGeometry.Build(punktfolge, variante);
                Pruefe(!layout.Warnings.Any(w => w.Contains("Hindernis")),
                    $"Lot201905 Zufahrten [{string.Join(",", auswahl)}]: Zufahrt an der Kurvensehne ist kein Hindernis");
            }
            faelle.Add(("Lot201905/False", punktfolge, s));
        }
        foreach (var pfad in Directory.GetFiles("Tests/GeometryParity/Berichtsfaelle", "*.json").OrderBy(p => p))
        {
            using var d = JsonDocument.Parse(File.ReadAllText(pfad));
            var input = d.RootElement.GetProperty("Input");
            var roh = input.GetProperty("LayoutSettings");
            if (roh.TryGetProperty("AlleWerte", out var alle)) roh = alle;
            faelle.Add((Path.GetFileName(pfad), Punkte(input.GetProperty("PolygonXZ")), Einstellungen(roh)));
        }
        using (var d = JsonDocument.Parse(File.ReadAllText("Tests/DreiAbzuege/original-eingaben.json")))
            foreach (var eintrag in d.RootElement.EnumerateArray())
            {
                var input = eintrag.GetProperty("Input");
                faelle.Add(("DreiAbzuege" + faelle.Count, Punkte(input.GetProperty("PolygonXZ")),
                    Einstellungen(input.GetProperty("LayoutSettings"))));
            }
        // Zusammen mit den historischen Berichten: konvex/konkav, verschiedene
        // Raster, Ringlos, Teilflaechen und Weltkoordinaten.
        foreach (var rand in new[] { false, true })
        foreach (var winkel in new[] { 0.0, 23.0 })
        {
            var s = LayoutSettings.Cs2; s.Randstrassen = rand; s.AngleMode = "fixed"; s.Angle = winkel;
            faelle.Add(($"Rechteck/{rand}/{winkel}", new[] { new float2(0, 0), new float2(120, 0), new float2(120, 90), new float2(0, 90) }, s));
            faelle.Add(($"L/{rand}/{winkel}", new[] { new float2(0, 0), new float2(120, 0), new float2(120, 45), new float2(60, 45), new float2(60, 90), new float2(0, 90) }, s));
        }
        var speicher = new VorschauVorbereitung();
        foreach (var fall in faelle)
        {
            var liste = new List<Entrance>(fall.Settings.Entrances ?? Array.Empty<Entrance>());
            var kanten = Enumerable.Range(0, fall.Polygon.Length)
                .OrderByDescending(k => math.distance(fall.Polygon[k], fall.Polygon[(k + 1) % fall.Polygon.Length]));
            foreach (int k in kanten)
            {
                if (liste.Count >= 2) break;
                if (liste.Any(e => e.Edge == k)) continue;
                liste.Add(new Entrance { Edge = k, Along = math.distance(fall.Polygon[k],
                    fall.Polygon[(k + 1) % fall.Polygon.Length]) / 2 });
            }
            var zugaenge = liste.ToArray();
            for (int n = 0; n <= 2; n++)
            {
                var s = fall.Settings.Clone(); s.AutomaticEntrances = false;
                s.Entrances = zugaenge.Take(n).ToArray();
                var uhr = Stopwatch.StartNew();
                var voll = Baue(fall.Polygon, s, null);
                double vorher = uhr.Elapsed.TotalMilliseconds; uhr.Restart();
                var schnell = Baue(fall.Polygon, s, speicher);
                double nachher = uhr.Elapsed.TotalMilliseconds;
                if (fall.Name == "Bericht71" || fall.Name.StartsWith("Lot136479/")) Pruefe(!voll.StartsWith("Abgelehnt"), "Bericht71 muss ein Layout liefern");
                Pruefe(voll == schnell, $"Bitgleich {fall.Name}, {n} Zufahrten");
                Console.WriteLine($"{fall.Name}/{n}: voll {vorher:F1} ms, vorbereitet {nachher:F1} ms, {voll}");
            }
        }
        // Regler, Ausrichtung und Punktzug am selben Speicher duerfen keinen
        // alten Ring zurueckgeben. Dieselben Koordinaten in neuer Arrayinstanz
        // muessen dagegen wiederverwendbar bleiben.
        foreach (var d in new[] { 0.5, 2.0, 1.0 })
        {
            var s = settings.Clone(); s.Es = d;
            Pruefe(Baue(polygon.ToArray(), s, null) == Baue(polygon.ToArray(), s, speicher), "Versatzwechsel bitgleich");
        }
        Pruefe(speicher.WiederverwendeteZerlegungen > 2 && speicher.WiederverwendeteVersaetze > 6,
            "Vorbereitung wird tatsaechlich wiederverwendet");
        Console.WriteLine($"Wiederverwendet: {speicher.WiederverwendeteZerlegungen} Zerlegungen, {speicher.WiederverwendeteVersaetze} Versaetze.");
        var bewegt = polygon.Select(p => p + new float2(1, -2)).ToArray();
        Pruefe(Baue(bewegt, settings, null) == Baue(bewegt, settings, speicher), "Punktwechsel bitgleich");
    }

    private static LayoutSettings Einstellungen(JsonElement e)
    {
        var s = e.Deserialize<LayoutSettings>(Optionen);
        bool B(string name, bool standard) => e.TryGetProperty(name, out var v) ? v.GetBoolean() : standard;
        s.Zellen = B("Zellen", true); s.EineFlaeche = B("EineFlaeche", false);
        s.NoNotch = B("NoNotch", false); s.Single = B("Single", false); s.NoHalf = B("NoHalf", false);
        if (e.TryGetProperty("KantenVersatz", out var d)) s.KantenVersatz = d.GetDouble();
        return s;
    }

    private static void Messe(float2[] polygon, LayoutSettings settings, bool ohne)
    {
        var speicher = ohne ? null : new VorschauVorbereitung();
        var zugaenge = settings.Entrances;
        ParkingGeometry.LiveSchreiber = zeile => { if (zeile.Contains("zellenbau")) Console.WriteLine(zeile); };
        // Warmstart je Variante; Geometrie/Einstellungen bleiben gleich,
        // anschliessend aendern sich ausschliesslich die Zufahrten.
        foreach (int n in new[] { 0, 1, 2 })
        {
            var s = settings.Clone(); s.AutomaticEntrances = false; s.Entrances = zugaenge.Take(n).ToArray();
            var zeiten = new List<double>();
            for (int i = 0; i < 4; i++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var uhr = Stopwatch.StartNew();
                var layout = ParkingGeometry.Build(polygon, s, speicher);
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                uhr.Stop();
                if (i > 0) zeiten.Add(uhr.Elapsed.TotalMilliseconds);
                Console.WriteLine($"Lauf {n}/{i}: {uhr.Elapsed.TotalMilliseconds:F1} ms, {layout.Stalls} Buchten, "
                    + $"{layout.GrassSurface.Length}/{layout.AsphaltSurface.Length} Gras-/Belagringe, {bytes / 1048576.0:F1} MiB allokiert.");
            }
            zeiten.Sort();
            Console.WriteLine($"MEDIAN {n} Zufahrten ({(ohne ? "ohne Vorbereitung" : "mit Vorbereitung")}): {zeiten[1]:F1} ms");
        }
        ParkingGeometry.LiveSchreiber = null;
    }

    private static string Baue(float2[] p, LayoutSettings s, VorschauVorbereitung speicher)
    {
        ParkingLayout layout;
        try { layout = ParkingGeometry.Build(p, s, speicher); }
        catch (InvalidOperationException e) { return "Abgelehnt: " + e.Message; }
        catch (ArgumentException e) { return "Abgelehnt: " + e.Message; }
        return Vergleich.Abdruck(layout);
    }
    private static float2[] Punkte(JsonElement e) => e.EnumerateArray()
        .Select(p => new float2(p.GetProperty("X").GetSingle(), p.GetProperty("Z").GetSingle())).ToArray();

    private static void PruefeRinge(JsonElement daten)
    {
        int angenommen = 0, abgelehnt = 0;
        foreach (var s in daten.GetProperty("Surfaces").EnumerateArray())
        {
            var p = s.GetProperty("Nodes").EnumerateArray().Select(v => new float2(v[0].GetSingle(), v[1].GetSingle())).ToArray();
            bool erwartet = s.GetProperty("TriangleCount").GetInt32() != 0;
            Pruefe((Cs2Triangulierung.Dreiecke(p) != 0) == erwartet,
                $"Echte CS2-Annahme {s.GetProperty("Kind")}/{s.GetProperty("Index")}");
            if (erwartet) { angenommen++; continue; }
            abgelehnt++;
            var verloren = new List<float2[]>(); var haarrisse = new List<float2[]>();
            var stuecke = ParkingGeometry.BaubareStuecke(p, verloren, haarrisse);
            Pruefe(stuecke.Count > 0 && stuecke.All(q => Cs2Triangulierung.Dreiecke(q) > 0), "Abgelehnter Ring wird baubar gerettet");
            double flaeche = Math.Abs(Flaeche(p)), gerettet = stuecke.Sum(q => Math.Abs(Flaeche(q))), rest = verloren.Concat(haarrisse).Sum(q => Math.Abs(Flaeche(q)));
            Pruefe(Math.Abs(flaeche - gerettet - rest) < .01, "Rettung bilanziert die Flaeche");
            Console.WriteLine($"Rettung {s.GetProperty("Kind")}/{s.GetProperty("Index")}: {p.Length} Knoten, {flaeche:F6} m2, {stuecke.Count} Stuecke/{gerettet:F6} m2, Rest {rest:F6} m2");
        }
        Pruefe(angenommen > 100 && abgelehnt == 2, "Positive und negative Spielgegenproben vorhanden");
        Console.WriteLine($"CS2-Materialringe: {angenommen} angenommen, {abgelehnt} abgelehnt, alle getroffen.");
    }
    private static double Flaeche(float2[] p)
    {
        double sum = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var a = (double2)p[i] - (double2)p[0]; var b = (double2)p[(i + 1) % p.Length] - (double2)p[0];
            sum += a.x * b.y - a.y * b.x;
        }
        return sum / 2;
    }
    private sealed class VektorLeser : JsonConverter<float2>
    {
        public override float2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var d = JsonDocument.ParseValue(ref reader);
            var p = d.RootElement;
            return p.ValueKind == JsonValueKind.Array ? new float2(p[0].GetSingle(), p[1].GetSingle())
                : new float2(p.GetProperty("x").GetSingle(), p.GetProperty("y").GetSingle());
        }
        public override void Write(Utf8JsonWriter w, float2 p, JsonSerializerOptions options) => throw new NotSupportedException();
    }
}
