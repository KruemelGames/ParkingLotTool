using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class Strassenkurve
    {
        /**
         * WO TRIFFT EINE GERADE DEN BORDSTEIN? (19.10.)
         *
         * Wird eine gerade Kante neben einer Kurve verschoben, soll ihr Ende
         * nicht starr mitwandern, sondern auf dem Bordstein entlanggleiten
         * (Nutzer). Gesucht sind alle Schnitte der Geraden `q + s*d` mit den
         * Randstuecken der geladenen Kanten, naechster zu `erwartet` zuerst.
         * Welcher davon zum selben Bordstein gehoert, entscheidet der Aufrufer
         * mit `Verfolge` - die Gegenseite der Strasse schneidet die Gerade ja
         * auch.
         */
        public static List<Fang> Bordsteinschnitte(IEnumerable<Kante> kanten, float2 q, float2 d,
            float2 erwartet, double reichweite)
        {
            var fund = new List<(double Abstand, Fang Fang)>();
            if (kanten == null || math.lengthsq(d) < 1e-12f) return new List<Fang>();
            var r = math.normalize(d);
            const int proben = 64;
            foreach (var k in kanten)
            {
                if (k == null || k.Hoehe) continue;
                for (int seite = -1; seite <= 1; seite += 2)
                {
                    var teile = k.Seite(seite);
                    if (teile == null) continue;
                    for (int i = 0; i < teile.Length; i++)
                    {
                        var c = teile[i].Kurve;
                        if (!c.Endlich) continue;
                        double Lage(double t)
                        {
                            var p = c.Punkt(t).xz - q;
                            return (double)p.x * r.y - (double)p.y * r.x;
                        }
                        double t0 = 0, l0 = Lage(0);
                        for (int j = 1; j <= proben; j++)
                        {
                            double t1 = j / (double)proben, l1 = Lage(t1);
                            if (l0 == 0 || (l0 < 0) != (l1 < 0))
                            {
                                double a = t0, b = t1, la = l0;
                                for (int n = 0; n < 40 && la != 0; n++)
                                {
                                    double m = (a + b) / 2, lm = Lage(m);
                                    if ((la < 0) == (lm < 0)) { a = m; la = lm; } else b = m;
                                }
                                double t = la == 0 ? a : (a + b) / 2;
                                var punkt = c.Punkt(t);
                                double abstand = math.distance(punkt.xz, erwartet);
                                if (abstand <= reichweite)
                                {
                                    bool doppelt = false;
                                    foreach (var f in fund)
                                        if (f.Fang.Kante == k.Id && f.Fang.Seite == seite && math.distance(f.Fang.Punkt, punkt) < .001f) { doppelt = true; break; }
                                    if (!doppelt)
                                        fund.Add((abstand, new Fang { Kante = k.Id, Seite = seite, Teil = i, T = (float)t, Punkt = punkt }));
                                }
                            }
                            t0 = t1; l0 = l1;
                        }
                    }
                }
            }
            fund.Sort((x, y) => x.Abstand.CompareTo(y.Abstand));
            var liste = new List<Fang>(fund.Count);
            foreach (var f in fund) liste.Add(f.Fang);
            return liste;
        }
    }
}
