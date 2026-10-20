using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class Kurvenzufahrten
{
    internal static void Laufe(string[] args)
    {
        if (args.Contains("--knoten")) { Knotenfaelle(args); return; }
        using var daten = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Lot485961.json")));
        var d = daten.RootElement;
        var polygon = d.GetProperty("Polygon").EnumerateArray()
            .Select(v => new float2(v[0].GetSingle(), v[1].GetSingle())).ToArray();
        var opt = new JsonSerializerOptions { IncludeFields = true,
            Converters = { new VektorLeser(), new JsonStringEnumConverter() } };
        var settings = d.GetProperty("Settings").Deserialize<LayoutSettings>(opt);
        var breite = args.FirstOrDefault(a => a.StartsWith("--breite="));
        if (breite != null) settings.Gassenbreite = double.Parse(breite.Substring(9), System.Globalization.CultureInfo.InvariantCulture);
        var lauf = Archivkurve(polygon, 0, 27);
        var pfad = args.FirstOrDefault(a => a.StartsWith("--ausgabe="))?.Substring(10)
            ?? ".codex-build/kurven-14/zufahrten.jsonl";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfad)));
        using var log = new StreamWriter(pfad);
        int fehler = 0;
        var vorbereitung = new VorschauVorbereitung();
        foreach (bool strasse in new[] { true, false })
        foreach (int maske in new[] { 0, 1, 2, 3 })
        {
            var s = settings.Clone(); s.Randstrassen = strasse;
            s.Entrances = settings.Entrances.Where((e, i) => (maske & (1 << i)) != 0).ToArray();
            s.Kurvenlaeufe = new[] { lauf.Clone() };
            var layout = ParkingGeometry.Build(polygon, s);
            var vorbereitet = ParkingGeometry.Build(polygon, s, vorbereitung);
            if (Vergleich.Abdruck(layout) != Vergleich.Abdruck(vorbereitet))
                throw new Exception("Spielzufahrt: Vorschauvorbereitung und Vollbau weichen ab.");
            int abgelehnt = layout.GrassSurface.Concat(layout.AsphaltSurface).Count(r => Cs2Triangulierung.Dreiecke(r) == 0);
            var loch = layout.Warnings.Where(w => w.Contains("hole separation")).ToArray();
            Console.WriteLine($"Spiel/{strasse}/{maske}: Buchten {layout.Stalls}, G/A {layout.GrassSurface.Length}/{layout.AsphaltSurface.Length}, Netz {layout.NetLine.Length}, Lochwarnungen {loch.Length}, CS2 {abgelehnt}");
            Schreibe(log, "Lot485961", strasse, maske, polygon, s, layout, abgelehnt);
            int materialfehler = strasse ? PruefeKorridore(polygon, layout, s.Gassenbreite) : 0;
            if (abgelehnt != 0 || loch.Length != 0 || materialfehler != 0) fehler++;
        }
        if (!args.Contains("--befund") && fehler != 0)
            throw new Exception("Kurvenzufahrt hat einen gebrochenen Materialplan.");
    }

    private static bool Enthaelt(float2[] ring, double x, double y)
    {
        bool innen = false;
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Length];
            if ((a.y > y) != (b.y > y)
                && x < a.x + (y - a.y) * ((double)b.x - a.x) / ((double)b.y - a.y)) innen = !innen;
        }
        return innen;
    }

    private static int PruefeKorridore(float2[] polygon, ParkingLayout layout, double breite)
    {
        int fehler = 0;
        foreach (var n in layout.NetLine.Where(n => n.Kind == "entrance"))
        {
            var a = new double2(n.A.x, n.A.y); var b = new double2(n.B.x, n.B.y);
            double laenge = math.distance(a, b); var u = (b - a) / laenge;
            var q = new double2(-u.y, u.x);
            // Physischer Gassenquerschnitt, unabhaengig von der geplanten Kontur.
            // Auch hinter der Sehnentangente: dort liegt bei konkaven Seiten
            // bereits Lotflaeche. 2 mm entsprechen der bestehenden Ausgabegrenze.
            for (double t = -breite; t < laenge; t += 0.25)
            for (int k = -4; k <= 4; k++)
            {
                var p = a + u * t + q * ((breite / 2 - 0.002) * k / 4);
                if (!Enthaelt(polygon, p.x, p.y)) continue;
                // An der Ausgabegrenze gilt dieselbe 2-mm-Schranke wie im
                // unabhaengigen Ringpruefer (1-mm-Eingangsraster + float).
                bool amRand = false;
                for (int i = 0; i < polygon.Length; i++)
                {
                    var pa = new double2(polygon[i].x, polygon[i].y);
                    var pb = new double2(polygon[(i + 1) % polygon.Length].x, polygon[(i + 1) % polygon.Length].y);
                    var v = pb - pa;
                    double tRand = math.clamp(math.dot(p - pa, v) / math.lengthsq(v), 0, 1);
                    if (math.distance(p, pa + v * tRand) <= 0.002) { amRand = true; break; }
                }
                if (amRand) continue;
                if (!layout.AsphaltSurface.Any(r => Enthaelt(r, p.x, p.y))
                    || layout.GrassSurface.Any(r => Enthaelt(r, p.x, p.y)))
                {
                    if (fehler < 3) Console.WriteLine($"Korridorluecke {p.x:R}/{p.y:R}, Station {t:R}, Quer {k}");
                    fehler++;
                }
            }
        }
        return fehler;
    }

    private static void Knotenfaelle(string[] args)
    {
        PruefeParametergrenze();
        int faelle = 0;
        var datei = args.FirstOrDefault(a => a.StartsWith("--ausgabe="))?.Substring(10)
            ?? ".codex-build/kurven-14/knoten.jsonl";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datei)));
        using var log = new StreamWriter(datei);
        foreach (double radius in new[] { 15d, 30, 57 })
        foreach (bool innen in new[] { true, false })
        foreach (double schraeg in new[] { -30d, 0, 30 })
        foreach (int lage in new[] { -1, 0, 1, 2, 3 })
        foreach (bool strasse in new[] { true, false })
        {
            var f = RandkurvenFaelle.Kreis(radius, 90, innen, false);
            var s = f.Settings.Clone(); s.Randstrassen = strasse;
            var p = f.Polygon; int knot = Math.Max(2, f.Lauf.Kanten / 2);
            var vorher = p[knot] - p[knot - 1]; var nachher = p[knot + 1] - p[knot];
            var tangente = math.normalizesafe(math.normalizesafe(vorher) + math.normalizesafe(nachher));
            double flaeche = 0;
            for (int i = 0; i < p.Length; i++) flaeche += (double)p[i].x * p[(i + 1) % p.Length].y - (double)p[i].y * p[(i + 1) % p.Length].x;
            var normale = new float2(-tangente.y, tangente.x) * (flaeche > 0 ? 1 : -1);
            float winkel = (float)(schraeg * Math.PI / 180);
            var achse = normale * math.cos(winkel) + tangente * math.sin(winkel);
            // Beide Welt-Kantenenden (double-Kern / float-Werkzeug) muessen
            // auch nach dem Eingangsgrid gueltige Enden bleiben.
            double laenge = math.length(new double2((double)p[knot].x - p[knot - 1].x,
                (double)p[knot].y - p[knot - 1].y));
            int edge = lage < 0 || lage >= 2 ? knot - 1 : knot;
            double along = lage < 0 ? laenge - 0.01 : lage == 2 ? laenge : lage == 3
                ? math.distance(p[knot - 1], p[knot]) : lage == 0 ? 0 : 0.01;
            s.Entrances = new[] { new Entrance { Edge = edge, Along = along,
                AxisDirection = achse, Art = lage % 2 == 0 ? Zufahrtsart.GasseAus : Zufahrtsart.GasseEin } };
            s.Kurvenlaeufe = new[] { f.Lauf.Clone() };
            var layout = ParkingGeometry.Build(p, s);
            int fehler = strasse ? PruefeKorridore(p, layout, s.Gassenbreite) : 0;
            int abgelehnt = layout.GrassSurface.Concat(layout.AsphaltSurface).Count(r => Cs2Triangulierung.Dreiecke(r) == 0);
            Console.WriteLine($"Knoten R{radius}/{innen}/{schraeg}/{lage}/{strasse}: Material {fehler}, CS2 {abgelehnt}, Warnungen {layout.Warnings.Length}");
            if (fehler != 0 || abgelehnt != 0 || (strasse && layout.Warnings.Any(w => w.Contains("hole separation"))))
            {
                float[][] V(float2[] r) => r.Select(v => new[] { v.x, v.y }).ToArray();
                Directory.CreateDirectory(".codex-build/kurven-14");
                File.WriteAllText(".codex-build/kurven-14/knoten-fehler.json", JsonSerializer.Serialize(new {
                    Polygon = V(p), Gras = layout.GrassSurface.Select(V), Asphalt = layout.AsphaltSurface.Select(V),
                    Achsen = layout.NetLine.Select(n => new { n.Kind, A = new[] { n.A.x, n.A.y }, B = new[] { n.B.x, n.B.y } }) }));
                throw new Exception("Kurvenzufahrt hat einen gebrochenen Materialplan.");
            }
            Schreibe(log, $"R{radius}/{innen}/{schraeg}/{lage}", strasse, 1, p, s, layout, abgelehnt);
            faelle++;
        }
        Console.WriteLine($"Kurvenknoten: {faelle} Varianten, 0 Fehler.");
    }

    private static void PruefeParametergrenze()
    {
        var f = RandkurvenFaelle.Kreis(15, 90, true, false);
        int k = Math.Max(2, f.Lauf.Kanten / 2) - 1;
        var a = f.Polygon[k]; var b = f.Polygon[k + 1];
        double G(float v) => Math.Round(v / ParkingGeometry.ZellenEingangsgitter) * ParkingGeometry.ZellenEingangsgitter;
        double laenge = math.length(new double2(G(b.x) - G(a.x), G(b.y) - G(a.y)));
        var s = f.Settings.Clone(); s.Randstrassen = false;
        s.Entrances = new[] { new Entrance { Edge = k, Along = laenge + 0.0000001 } };
        bool abgelehnt = false;
        try { ParkingGeometry.Build(f.Polygon, s); }
        catch (InvalidOperationException e) when (e.Message.Contains(" is not on edge ")) { abgelehnt = true; }
        if (!abgelehnt) throw new Exception("Nicht-Endpunkt ausserhalb der Kante wurde akzeptiert.");
        Console.WriteLine("Parametergrenze: 0,1 Mikrometer ausserhalb bleibt ungueltig; keine neue Toleranz.");
    }

    private static void Schreibe(StreamWriter log, string name, bool strasse, int maske,
        float2[] polygon, LayoutSettings s, ParkingLayout layout, int abgelehnt)
    {
        float[][] Punkte(float2[] p) => p.Select(v => new[] { v.x, v.y }).ToArray();
        string abdruck = Vergleich.Abdruck(layout), ohneKurven = null;
        if (!strasse)
        {
            var alt = s.Clone(); alt.Kurvenlaeufe = Array.Empty<KurvenRandlauf>();
            ohneKurven = Vergleich.Abdruck(ParkingGeometry.Build(polygon, alt));
            if (ohneKurven != abdruck) throw new Exception("AUS-Layout weicht vom Altpfad ab.");
        }
        log.WriteLine(JsonSerializer.Serialize(new { Name = name, Strasse = strasse, Maske = maske,
            Polygon = Punkte(polygon), Buchten = layout.Bay.Select(Punkte),
            Gras = layout.GrassSurface.Select(Punkte), Asphalt = layout.AsphaltSurface.Select(Punkte),
            Achsen = layout.NetLine.Select(n => new { n.Kind, A = new[] { n.A.x, n.A.y }, B = new[] { n.B.x, n.B.y } }),
            Zufahrten = s.Entrances.Select(e => new { e.Edge, e.Along, Art = e.Art.ToString() }),
            Kurven = layout.KurvenRandreihen.SelectMany(r => r.Buchten),
            Randabstand = s.Es, Buchttiefe = s.Sl, Fahrgassenbreite = s.Ai, Gassenbreite = s.Gassenbreite,
            Warnungen = layout.Warnings, Abgelehnt = abgelehnt, layout.Stalls,
            Vollabdruck = abdruck, OhneKurvenAbdruck = ohneKurven }));
    }

    internal static KurvenRandlauf Archivkurve(float2[] p, int von, int kanten)
    {
        var kontrollen = new List<float3>();
        var zug = Enumerable.Range(0, kanten + 1).Select(i => p[(von + i) % p.Length]).ToArray();
        for (int i = 0; i < kanten; i++)
        {
            var a = zug[i]; var b = zug[i + 1];
            var da = i == 0 ? b - a : zug[i + 1] - zug[i - 1];
            var db = i + 1 == kanten ? b - a : zug[i + 2] - zug[i];
            float l = math.distance(a, b) / 3;
            float3 V(float2 v) => new float3(v.x, 0, v.y);
            kontrollen.Add(V(a)); kontrollen.Add(V(a + math.normalizesafe(da) * l));
            kontrollen.Add(V(b - math.normalizesafe(db) * l)); kontrollen.Add(V(b));
        }
        return new KurvenRandlauf { Von = von, Kanten = kanten, Kontrollpunkte = kontrollen.ToArray() };
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
