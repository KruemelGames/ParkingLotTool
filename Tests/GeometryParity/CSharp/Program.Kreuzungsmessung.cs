using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * `--kreuzungen <kennung>`: baut einen Parkplatz aus dem Bauprotokoll nach
 * und misst, wie die anderen Netzlinien an seine Zoning-Linien stossen.
 *
 * Anlass (2026-09-25): der Bushalt sprang an einer Einmuendung Fahrgasse ->
 * Zoning-Strasse NICHT weiter; `BusStopSnap.Sperrbereiche` hat die Stelle
 * also nicht als Kreuzung erkannt. Statt zu raten, woran es liegt, zeigt
 * diese Messung die echten Abstaende.
 */
internal static partial class Program
{
    private sealed class Float2Json : JsonConverter<float2>
    {
        public override float2 Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        {
            var feld = JsonSerializer.Deserialize<float[]>(ref reader, o);
            return new float2(feld[0], feld[1]);
        }
        public override void Write(Utf8JsonWriter w, float2 v, JsonSerializerOptions o)
            => throw new NotSupportedException();
    }

    private static int MesseKreuzungen(string kennung)
    {
        var pfad = ProtokollPfad();
        var zeile = File.ReadAllLines(pfad).LastOrDefault(z =>
            z.StartsWith("{\"kennung\":\"" + kennung + "\"", StringComparison.Ordinal));
        if (zeile == null) { Console.WriteLine("Keine Kennung " + kennung); return 1; }
        using var doc = JsonDocument.Parse(zeile);
        var wurzel = doc.RootElement;
        var optionen = new JsonSerializerOptions
        {
            Converters = { new Float2Json(), new JsonStringEnumConverter() },
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        var lw = wurzel.GetProperty("layoutwerte");
        var settings = lw.Deserialize<LayoutSettings>(optionen);
        bool B(string n, bool v) => lw.TryGetProperty(n, out var e) ? e.GetBoolean() : v;
        settings.Zellen = B("Zellen", true);
        settings.EineFlaeche = B("EineFlaeche", false);
        settings.NoNotch = B("NoNotch", false);
        settings.Single = B("Single", false);
        settings.NoHalf = B("NoHalf", false);
        if (lw.TryGetProperty("KantenVersatz", out var kv)) settings.KantenVersatz = kv.GetDouble();
        var site = wurzel.GetProperty("polygon").EnumerateArray()
            .Select(p => new float2(p[0].GetSingle(), p[1].GetSingle())).ToArray();

        var layout = ParkingGeometry.Build(site, settings);
        var linien = layout.NetLine;
        Console.WriteLine(kennung + ": " + linien.Length + " Netzlinien, Arten "
            + string.Join(", ", linien.GroupBy(l => l.Kind).Select(g => g.Key + "=" + g.Count())));
        foreach (var z in linien.Where(l => l.Kind == "zoning"))
        {
            var d = z.B - z.A;
            var laenge = math.length(d);
            if (laenge < 12f) continue;
            Console.WriteLine();
            Console.WriteLine($"ZONING ({z.A.x:F1}/{z.A.y:F1}) -> ({z.B.x:F1}/{z.B.y:F1}), {laenge:F1} m");
            var sperren = BusStopSnap.Sperrbereiche(layout, z, laenge,
                settings.Ai, settings.Cw);
            Console.WriteLine("  Sperren (m entlang): " + string.Join("  ", sperren
                .Select(s => $"[{Meter(s.x, laenge)}..{Meter(s.y, laenge)}]")));
            foreach (var n in linien)
            {
                if (ReferenceEquals(n, z) || (math.all(n.A == z.A) && math.all(n.B == z.B))) continue;
                foreach (var (ende, name) in new[] { (n.A, "A"), (n.B, "B") })
                {
                    var t = math.dot(ende - z.A, d) / (laenge * laenge);
                    if (t < -0.05f || t > 1.05f) continue;
                    var lot = z.A + math.clamp(t, 0f, 1f) * d;
                    var abstand = math.distance(ende, lot);
                    if (abstand > 12f) continue;
                    Console.WriteLine($"  {n.Kind,-10} Ende {name} bei {t * laenge,6:F1} m entlang, {abstand,9:F5} m quer");
                }
            }
            // Parallele Nachbarn (Randstrasse neben der Zoning-Strasse?)
            foreach (var n in linien)
            {
                if (n.Kind == "zoning") continue;
                var e = n.B - n.A;
                var el = math.length(e);
                if (el < 1f) continue;
                var sinus = math.abs(d.x * e.y - d.y * e.x) / (laenge * el);
                if (sinus > 0.05f) continue;
                var qa = math.abs((d.x * (n.A.y - z.A.y) - d.y * (n.A.x - z.A.x)) / laenge);
                if (qa > 12f) continue;
                var ta = math.dot(n.A - z.A, d) / laenge;
                var tb = math.dot(n.B - z.A, d) / laenge;
                Console.WriteLine($"  PARALLEL {n.Kind,-10} {qa,5:F2} m daneben, von {math.min(ta, tb),6:F1} bis {math.max(ta, tb),6:F1} m");
            }
        }
        return 0;
    }

    private static string Meter(float t, float laenge)
        => float.IsInfinity(t) ? (t < 0 ? "-inf" : "inf") : (t * laenge).ToString("F1");
}
