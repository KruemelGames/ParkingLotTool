using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class FahrradGeometrie
    {
        /** Konstruiert gemeinsame Endpunkte; geometrische T-Stoesse allein
         * erzeugen in CS2 keinen Knoten (AGENTS: 89 m frei haengende RZ-Strasse). */
        private static void Netz(Raum raum, List<Weg> wege, FahrradPlan plan)
        {
            var gesehen = new HashSet<string>();
            foreach (var w in wege)
            {
                var d = w.B - w.A; var l2 = math.lengthsq(d);
                if (l2 < Epsilon * Epsilon) continue;
                var ts = new List<double> { 0, 1 };
                foreach (var v in wege)
                {
                    foreach (var p in new[] { v.A, v.B })
                    {
                        var t = math.dot(p - w.A, d) / l2;
                        if (t > Epsilon && t < 1 - Epsilon && math.length(w.A + d * t - p) < Epsilon) ts.Add(t);
                    }
                    var dv = v.B - v.A; var k = Kreuz(d, dv);
                    if (Math.Abs(k) < Epsilon) continue;
                    var t1 = Kreuz(v.A - w.A, dv) / k;
                    var t2 = Kreuz(v.A - w.A, d) / k;
                    if (t1 > Epsilon && t1 < 1 - Epsilon && t2 >= -Epsilon && t2 <= 1 + Epsilon) ts.Add(t1);
                }
                ts.Sort();
                for (var i = 1; i < ts.Count; i++)
                {
                    if ((ts[i] - ts[i - 1]) * Math.Sqrt(l2) < Epsilon) continue;
                    // 1-mm-Knotenraster VOR dem Kantenbau: nahezu gleiche
                    // Schnittparameter ergaben bei 8 km identische float2-
                    // Enden, obwohl ihre double-Laenge noch > 10 um war.
                    var a = math.round((w.A + d * ts[i - 1]) * 1000) / 1000;
                    var b = math.round((w.A + d * ts[i]) * 1000) / 1000;
                    var ka = Schluessel(a); var kb = Schluessel(b);
                    if (ka == kb) continue;
                    var key = string.CompareOrdinal(ka, kb) < 0 ? ka + "/" + kb : kb + "/" + ka;
                    if (!gesehen.Add(key)) continue;
                    plan.Wege.Add(new Fahrradweg { A = raum.Welt(a), B = raum.Welt(b),
                        Art = w.Art, Breite = w.Breite, Zufahrt = w.Zufahrt });
                }
            }
        }

        private static string Schluessel(double2 p) => Math.Round(p.x * 100000) + ":" + Math.Round(p.y * 100000);
    }
}
