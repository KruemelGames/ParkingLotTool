using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Ringlosplan
    {
        internal static bool Ueberlappt(Punkt[] a, Punkt[] b)
        {
            foreach (var poly in new[] { a, b })
                for (var i = 0; i < poly.Length; i++)
                {
                    var d = poly[(i + 1) % poly.Length] - poly[i];
                    var n = new Punkt(-d.Y, d.X);
                    var aa = a.Select(v => Geometrie.Skalar(v, n)).ToArray();
                    var bb = b.Select(v => Geometrie.Skalar(v, n)).ToArray();
                    if (aa.Max() <= bb.Min() + 1e-6 || bb.Max() <= aa.Min() + 1e-6) return false;
                }
            return true;
        }

        private static bool Frei(Weg w, IReadOnlyList<Punkt> innen, IReadOnlyList<Zoningvorgabe> zoning)
            => w.Ecken.All(v => Geometrie.EnthaeltOderRand(innen, v)
                || Enumerable.Range(0, innen.Count).Any(i => Geometrie.AbstandPunktStrecke(v, innen[i], innen[(i + 1) % innen.Count]) <= 1e-6))
               && !(zoning ?? Array.Empty<Zoningvorgabe>()).Any(z => Ueberlappt(w.Ecken, z.Ecken().ToArray()));

        private static IEnumerable<(double A, double B)> Waagerecht(IReadOnlyList<Punkt> poly, double y)
            => WaagerechtMitKante(poly, y).Select(s => (s.A, s.B));

        /**
         * Dasselbe wie `Waagerecht`, nur mit der Nummer der Konturkante, die
         * den jeweiligen Rand des Abschnitts erzeugt hat. Siehe `Weg.KanteA`.
         */
        private static IEnumerable<(double A, int KanteA, double B, int KanteB)>
            WaagerechtMitKante(IReadOnlyList<Punkt> poly, double y)
        {
            var treffer = new List<(double X, int Kante)>();
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                if ((a.Y > y) == (b.Y > y)) continue;
                treffer.Add((a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y), i));
            }
            treffer.Sort((l, r) => l.X.CompareTo(r.X));
            for (var i = 0; i + 1 < treffer.Count; i += 2)
                yield return (treffer[i].X, treffer[i].Kante,
                    treffer[i + 1].X, treffer[i + 1].Kante);
        }
    }
}
