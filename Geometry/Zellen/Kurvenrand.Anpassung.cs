using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal static partial class Layoutbauer
    {
        private static List<Polygon> SchneideKurvenraster(List<Polygon> fragmente, Polygonteiler teiler,
            Kurvenrandplan kurve, Randreihenplan reihen,
            IEnumerable<(Linie Linie, Punkt Aussen, Punkt Innen)> stoesse, Linienregister linien)
        {
            var schnitte = reihen.Schnittlinien.Where(s => !s.EndenGerastert).ToList();
            foreach (var stoss in stoesse)
                schnitte.Add(new Randreihenschnitt { Linie = stoss.Linie, Aussen = stoss.Aussen, Innen = stoss.Innen });
            foreach (var stoss in kurve.Bodenstoesse)
                schnitte.Add(new Randreihenschnitt { Linie = kurve.Grenze(stoss.A, stoss.B, linien),
                    Aussen = stoss.A, Innen = stoss.B });
            foreach (var ring in kurve.Anschluesse.Concat(kurve.Graskeile))
                for (int i = 0; i < ring.Length; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    if (Geometrie.Laenge(b - a) < 1e-9) continue;
                    // Die Materialkontur ist ein ganzer Lauf; geschnitten
                    // wird nur am jeweiligen Randsegment, nicht im ganzen Band.
                    schnitte.Add(new Randreihenschnitt { Linie = kurve.Grenze(a, b, linien), Aussen = a, Innen = b });
                }
            var grenzen = new Dictionary<Polygon, (int Anzahl, double X0, double X1, double Y0, double Y1)>();
            // Dieselbe konstruktive Gerade genau einmal schneiden. Buchtseite
            // und ihre Fortsetzung zur Fahrbahn teilen Linie UND Schnittpunkte.
            foreach (var gruppe in schnitte.GroupBy(s => s.Linie.Id))
            {
                var erster = gruppe.First();
                var bereiche = gruppe.SelectMany(s => s.Bereich ?? new[] { s.Aussen, s.Innen }).ToArray();
                double x0 = bereiche.Min(p => p.X), x1 = bereiche.Max(p => p.X);
                double y0 = bereiche.Min(p => p.Y), y1 = bereiche.Max(p => p.Y);
                var neu = new List<Polygon>();
                foreach (var fragment in fragmente)
                {
                    if (!grenzen.TryGetValue(fragment, out var g) || g.Anzahl != fragment.Anzahl)
                    {
                        double fx0 = double.PositiveInfinity, fx1 = double.NegativeInfinity;
                        double fy0 = double.PositiveInfinity, fy1 = double.NegativeInfinity;
                        for (int i = 0; i < fragment.Anzahl; i++)
                        {
                            var p = fragment.Knoten(i).Punkt;
                            fx0 = Math.Min(fx0, p.X); fx1 = Math.Max(fx1, p.X);
                            fy0 = Math.Min(fy0, p.Y); fy1 = Math.Max(fy1, p.Y);
                        }
                        g = (fragment.Anzahl, fx0, fx1, fy0, fy1);
                        grenzen[fragment] = g;
                    }
                    if (g.X1 < x0 || g.X0 > x1 || g.Y1 < y0 || g.Y0 > y1) neu.Add(fragment);
                    else neu.AddRange(teiler.Teile(fragment, erster.Linie));
                }
                teiler.VervollstaendigeNachbarkanten(neu, erster.Linie);
                fragmente = neu;
            }
            return fragmente;
        }

        private static void ErgaenzeKurvenplan(Kurvenrandplan kurve, Randreihenplan plan,
            int ersteId, Linienregister linien)
        {
            var buchten = plan.Buchten.ToList();
            var schnitte = plan.Schnittlinien.ToList();
            foreach (var bucht in buchten)
                if (kurve.Zugaben[bucht.Randkante] > 0)
                {
                    var a = bucht.Ecken[0]; var b = bucht.Ecken[1];
                    schnitte.Add(new Randreihenschnitt
                    {
                        Linie = linien.FreieGerade(a, b, Linienart.Randbuchtgrenze, "Kurvenuebergang"),
                        Aussen = a, Innen = b, EndenGerastert = false, Bereich = bucht.Ecken,
                    });
                }
            foreach (var bucht in kurve.Buchten)
            {
                bucht.Id = ersteId + buchten.Count;
                buchten.Add(bucht);
                // Rechteckseiten begrenzen Bucht-IDs, keine Ausgabeflaechen.
                // Eine radiale Front faellt nicht auf eine Sehnenparallele.
                for (int i = 0; i < 4; i++)
                {
                    var a = bucht.Ecken[i]; var b = bucht.Ecken[(i + 1) % 4];
                    schnitte.Add(new Randreihenschnitt
                    {
                        Linie = kurve.Grenze(a, b, linien),
                        Aussen = a, Innen = b, EndenGerastert = false, Bereich = bucht.Ecken,
                    });
                }
            }
            plan.Buchten = buchten; plan.Schnittlinien = schnitte;
            kurve.PlaneMaterialsektoren(buchten);
        }
    }
}
