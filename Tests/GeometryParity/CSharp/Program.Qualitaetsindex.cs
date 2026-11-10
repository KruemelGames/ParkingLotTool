using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

internal static partial class Program
{
    /** Nur eine Vorauswahl: PointIn und Randabstand bleiben die bisherigen Messungen. */
    private sealed class Qualitaetsindex
    {
        private const double Schritt = 8;
        private const double Rand = 1e-6;
        private readonly Dictionary<(int X, int Y), List<float2[]>> _zellen = new();
        private static int Zelle(double p) => (int)Math.Floor(p / Schritt);

        internal Qualitaetsindex(IEnumerable<float2[]> ringe)
        {
            foreach (var ring in ringe)
            {
                if (ring.Length == 0) continue;
                var x0 = Zelle((double)ring.Min(p => p.x) - Rand);
                var x1 = Zelle((double)ring.Max(p => p.x) + Rand);
                var y0 = Zelle((double)ring.Min(p => p.y) - Rand);
                var y1 = Zelle((double)ring.Max(p => p.y) + Rand);
                for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                {
                    if (!_zellen.TryGetValue((x, y), out var liste))
                        _zellen[(x, y)] = liste = new List<float2[]>();
                    liste.Add(ring);
                }
            }
        }

        internal bool Enthaelt(float2 p)
        {
            if (!_zellen.TryGetValue((Zelle(p.x), Zelle(p.y)), out var ringe)) return false;
            foreach (var ring in ringe) if (PointIn(p, ring)) return true;
            foreach (var ring in ringe) if (DistanceToBoundary(p, ring) <= Rand) return true;
            return false;
        }
    }

    private static int PruefeQualitaetsindex()
    {
        var n = 0; var fehler = 0;
        foreach (var versatz in new[] { new float2(0), new float2(-1250, 842), new float2(8000, -8000) })
        {
            var ringe = new List<float2[]>();
            for (var i = 0; i < 40; i++)
            {
                var x = (i % 8) * 8f; var y = (i / 8) * 8f;
                var r = new[] { new float2(x,y), new float2(x+7,y), new float2(x+7,y+3),
                    new float2(x+3,y+3), new float2(x+3,y+7), new float2(x,y+7) };
                ringe.Add(r.Select(p => p + versatz).ToArray());
            }
            var index = new Qualitaetsindex(ringe);
            var zufall = new System.Random(2361);
            var proben = new List<float2>();
            for (var i = 0; i < 20000; i++)
                proben.Add(versatz + new float2((float)(zufall.NextDouble()*80-8), (float)(zufall.NextDouble()*56-8)));
            foreach (var r in ringe)
            foreach (var p in r)
            foreach (var d in new[] { 0f, .0000005f, -.0000005f, .000002f, -.000002f })
                proben.Add(p + new float2(d, 0));
            foreach (var p in proben)
            {
                // Unveraenderte alte Vollsuche ist das unabhaengige Vergleichsgeraet.
                var vorher = ringe.Any(r => PointIn(p,r)) || ringe.Any(r => DistanceToBoundary(p,r) <= 1e-6);
                n++;
                if (index.Enthaelt(p) != vorher) fehler++;
            }
        }
        Console.WriteLine($"QUALITAETSINDEX: {n} Vergleiche mit Vollsuche, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}
