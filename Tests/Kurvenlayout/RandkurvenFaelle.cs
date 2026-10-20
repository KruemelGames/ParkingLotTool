using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using K = ParkingLotTool.Geometry.Strassenkurve;

internal sealed class Randkurvenfall
{
    internal string Name;
    internal double Radius;
    internal bool Innen;
    internal float2[] Polygon;
    internal KurvenRandlauf Lauf;
    internal LayoutSettings Settings;
}

internal static class RandkurvenFaelle
{
    internal static IEnumerable<Randkurvenfall> Alle()
    {
        foreach (double r in new[] { 15d, 20, 30, 50, 100 })
        foreach (int grad in new[] { 90, 150 })
        foreach (bool innen in new[] { true, false })
            yield return Kreis(r, grad, innen, false);
        foreach (bool innen in new[] { true, false })
            yield return Kreis(30, 90, innen, true);
        // Die expliziten Prueflaeufe sind Fixture-Daten. Die Archivdateien
        // enthalten noch keine Formel; diese Hermite-Abschnitte interpolieren
        // deren unveraenderte Punkte. Produktion erkennt hier nichts aus der Form.
        foreach (var (name, von, kanten) in new[] { ("Lot201905", 0, 26), ("Lot136479", 4, 45) })
        {
            using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name + ".json")));
            var p = d.RootElement.GetProperty("Polygon").EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Array ? new float2(v[0].GetSingle(), v[1].GetSingle())
                    : new float2(v.GetProperty("X").GetSingle(), v.GetProperty("Z").GetSingle())).ToArray();
            var opt = new JsonSerializerOptions { IncludeFields = true, Converters = { new VektorLeser(), new JsonStringEnumConverter() } };
            var s = d.RootElement.GetProperty("Settings").Deserialize<LayoutSettings>(opt);
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
            s.Auto = false;
            yield return new Randkurvenfall { Name = name, Radius = name == "Lot201905" ? 103 : 0,
                Innen = false, Polygon = p, Settings = s,
                Lauf = new KurvenRandlauf { Von = von, Kanten = kanten, Kontrollpunkte = kontrollen.ToArray() } };
        }
    }

    internal static Randkurvenfall Kreis(double radius, int grad, bool innen, bool gerade)
    {
        float3 P(double x, double y) => new float3((float)(1300 + x), 0, (float)(4600 + y));
        var kurven = new List<K.Bezier>();
        int segmente = (int)Math.Ceiling(grad / 90d);
        for (int i = 0; i < segmente; i++)
        {
            double a = (-grad / 2d + grad * i / (double)segmente) * Math.PI / 180;
            double b = (-grad / 2d + grad * (i + 1) / (double)segmente) * Math.PI / 180;
            double h = 4d / 3 * Math.Tan((b - a) / 4);
            kurven.Add(new K.Bezier(P(radius * Math.Cos(a), radius * Math.Sin(a)),
                P(radius * (Math.Cos(a) - h * Math.Sin(a)), radius * (Math.Sin(a) + h * Math.Cos(a))),
                P(radius * (Math.Cos(b) + h * Math.Sin(b)), radius * (Math.Sin(b) - h * Math.Cos(b))),
                P(radius * Math.Cos(b), radius * Math.Sin(b))));
        }
        if (gerade)
        {
            var a = kurven[0].A; var b = kurven[^1].D;
            var u = math.normalizesafe(kurven[0].B - a); var v = math.normalizesafe(b - kurven[^1].C);
            kurven.Insert(0, new K.Bezier(a - u * 50, a - u * (100f / 3), a - u * (50f / 3), a));
            kurven.Add(new K.Bezier(b, b + v * (50f / 3), b + v * (100f / 3), b + v * 50));
        }
        var stand = K.Einfrieren(100, kurven);
        if (!stand.Folgt) throw new Exception("Produktionsabtastung: " + stand.Grund);
        var rand = stand.Punkte.Select(p => p.xz).ToArray();
        // Beim 150-Grad-Bogen reichen 50 m Tangente an der Endkappe
        // nur 12,94 m in Y: weniger als Reihe plus ganze Fahrbahn (13,9 m).
        // Die Radiusmatrix isoliert den Bogen mit ausreichend tiefen Endkappen.
        const float tangentenkappe = 120;
        float x = innen ? rand.Min(p => p.x) - 140 : rand.Max(p => p.x) + 140;
        var oben = rand[^1] + math.normalizesafe((kurven[^1].D - kurven[^1].C).xz) * tangentenkappe;
        var unten = rand[0] - math.normalizesafe((kurven[0].B - kurven[0].A).xz) * tangentenkappe;
        var polygon = rand.Concat(new[] { oben, new float2(x, oben.y), new float2(x, unten.y), unten }).ToArray();
        var s = LayoutSettings.Cs2;
        s.Sl = 5.9; s.Auto = false; s.AngleMode = "fixed"; s.Angle = 0; s.AutomaticEntrances = false;
        return new Randkurvenfall { Name = $"R{radius}/{grad}/{innen}/{gerade}", Radius = radius,
            Innen = innen, Polygon = polygon, Settings = s,
            Lauf = new KurvenRandlauf { Von = 0, Kanten = rand.Length - 1,
                Kontrollpunkte = kurven.SelectMany(c => new[] { c.A, c.B, c.C, c.D }).ToArray() } };
    }

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
