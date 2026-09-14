using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunRinglosflaechen()
    {
        var polygon = new[] { new float2(-1037.02f, 118.689774f),
            new float2(-1148.93567f, 122.5291f), new float2(-1152.90674f, 6.828209f),
            new float2(-1040.98938f, 2.98689938f) };
        var s = LayoutSettings.Cs2;
        s.Qk = true; s.Md = 2.5; s.AngleMode = "edge"; s.Angle = 0;
        s.Es = 1; s.Ai = 7; s.Cw = 3; s.Cr = 33; s.Sw = 3; s.Sl = 5.9;
        s.Entrances = Array.Empty<Entrance>();
        var fehler = 0;
        var mitRing = 0;
        foreach (var ring in new[] { true, false })
        {
            s.Randstrassen = ring;
            var l = ParkingGeometry.Build(polygon, s);
            var ringe = l.GrassSurface.Concat(l.AsphaltSurface).ToArray();
            var kurz = ringe.Count(r => r.Where((p, i) => math.distance(p, r[(i + 1) % r.Length]) < 0.375 - 1e-6).Any());
            var verworfen = ringe.Count(r => Cs2Triangulierung.Dreiecke(r) == 0);
            foreach (var r in ringe.Where(r => r.Where((p,i) => math.distance(p,r[(i+1)%r.Length]) < 0.375).Any())) Console.WriteLine("KURZ " + string.Join(";", r.Select(p => $"{p.x:R}/{p.y:R}")));
            Console.WriteLine($"Ring={ring}: {l.Stalls} Buchten, Gras={l.GrassSurface.Length}, Belag={l.AsphaltSurface.Length}, kurz={kurz}, verworfen={verworfen}, minKante={ringe.Min(r => r.Select((p,i) => math.distance(p,r[(i+1)%r.Length])).Min()):R}, Scherben={l.Warnings.Count(w => w.StartsWith("Halbebenenschnitt "))}");
            if (ring) mitRing = ringe.Length;
            else if (l.Stalls != 304 || kurz != 0 || verworfen != 0 || ringe.Length > 2 * mitRing) fehler++;
        }
        Console.WriteLine($"Ringlosflaechen: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
