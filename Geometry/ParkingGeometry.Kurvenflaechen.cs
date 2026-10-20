using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        private static bool KurvenringEinfach(float2[] ring)
        {
            double Kreuz(float2 a, float2 b, float2 c) =>
                ((double)b.x - a.x) * ((double)c.y - a.y)
                - ((double)b.y - a.y) * ((double)c.x - a.x);
            bool Beruehrt(float2 p, float2 a, float2 b) => Kreuz(a, b, p) == 0
                && p.x >= System.Math.Min(a.x, b.x) && p.x <= System.Math.Max(a.x, b.x)
                && p.y >= System.Math.Min(a.y, b.y) && p.y <= System.Math.Max(a.y, b.y);
            for (int i = 0; i < ring.Length; i++)
            for (int j = i + 2; j < ring.Length; j++)
            {
                if (i == 0 && j == ring.Length - 1) continue;
                var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                var c = ring[j]; var d = ring[(j + 1) % ring.Length];
                if (Beruehrt(a, c, d) || Beruehrt(b, c, d)
                    || Beruehrt(c, a, b) || Beruehrt(d, a, b)) return false;
                if (Kreuz(a, b, c) * Kreuz(a, b, d) < 0
                    && Kreuz(c, d, a) * Kreuz(c, d, b) < 0) return false;
            }
            return true;
        }

        private static float2[][] Kurvenflaechen(float2[][] ringe,
            List<float2[]> unbaubar, List<float2[]> haarrisse)
        {
            var ausgabe = new List<float2[]>();
            foreach (var roh in ringe)
            {
                if (KurvenringEinfach(roh) && Cs2Triangulierung.Dreiecke(roh) != 0)
                {
                    ausgabe.Add(roh);
                    continue;
                }
                var schlaufen = ZerlegeSelbstberuehrung(MitTBeruehrungen(roh)).ToArray();
                double flaeche = RingflaecheFloat(roh);
                bool ohneLoecher = schlaufen.All(p => RingflaecheFloat(p) * flaeche > 0)
                    && System.Math.Abs(schlaufen.Sum(RingflaecheFloat) - flaeche) < 1e-6;
                foreach (var ring in ohneLoecher ? schlaufen : new[] { roh })
                {
                    if (KurvenringEinfach(ring) && Cs2Triangulierung.Dreiecke(ring) != 0)
                    {
                        ausgabe.Add(ring);
                        continue;
                    }
                    var doppel = ring.Select(p => new double2(p.x, p.y)).ToArray();
                    if (KurvenringEinfach(ring)
                        && PlaneZellenCs2Sehne(doppel, out var a, out var b)
                        && KurvenringEinfach(NachFloat(a)) && KurvenringEinfach(NachFloat(b)))
                    {
                        ausgabe.Add(NachFloat(a)); ausgabe.Add(NachFloat(b));
                        continue;
                    }
                    // Die Schlaufen einer schwach einfachen Kontur koennen
                    // Loecher um Buchten sein. Sie sind keine positiven Ringe.
                    // Der Ohrenschnitt erhaelt ihre Orientierung gemeinsam.
                    foreach (var teil in Dreiecksverbund(ring, unbaubar, einfacheRinge: true))
                        if (ZellenHaarriss(teil.Select(p => new double2(p.x, p.y)).ToArray()))
                            haarrisse.Add(teil);
                        else ausgabe.Add(teil);
                }
            }
            return ausgabe.ToArray();
        }
    }
}
