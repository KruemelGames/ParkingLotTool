using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        private static bool GeplanterZoningknoten(string kind, Punkt a, Punkt b, ZellenStrasse z)
        {
            if (kind != "aisle" && kind != "cross") return false;
            if (Zoningkanten.Direkt(z.A, z.B) != (kind == "aisle")) return false;
            return Geometrie.AbstandPunktStrecke(a, z.A, z.B) < 1e-6
                || Geometrie.AbstandPunktStrecke(b, z.A, z.B) < 1e-6;
        }

        // Die Teilung ist Bestandteil der Netzkonstruktion: ein T-Stoss allein
        // hat 0 gemeinsame Endpunkte, die beiden Teilkurse haben je einen.
        private static IEnumerable<ZellenStrasse> ZoningAnAnschluessenGeteilt(
            IEnumerable<ZellenStrasse> zoning, List<ZellenStrasse> wege)
        {
            foreach (var z in zoning)
            {
                var d = z.B - z.A;
                var len2 = Geometrie.Skalar(d, d);
                var punkte = new List<Punkt> { z.A, z.B };
                foreach (var w in wege.Where(w => GeplanterZoningknoten(w.Kind, w.A, w.B, z)))
                    foreach (var p in new[] { w.A, w.B })
                        if (Geometrie.AbstandPunktStrecke(p, z.A, z.B) < 1e-6
                            && !punkte.Any(q => Geometrie.Laenge(p - q) < 1e-6)) punkte.Add(p);
                punkte = punkte.OrderBy(p => Geometrie.Skalar(p - z.A, d) / len2).ToList();
                for (var i = 1; i < punkte.Count; i++)
                    yield return new ZellenStrasse { Kind = z.Kind, A = punkte[i - 1], B = punkte[i],
                        Randzoning = z.Randzoning, RandzoningInnen = z.RandzoningInnen };
            }
        }
    }
}
