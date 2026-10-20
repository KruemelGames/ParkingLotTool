using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingLanterns
    {
        private static void Kurvenlaternen(LaternenPlan plan, KurvenRandreihe[] reihen,
            float abstand, Func<float2, bool> istGruen)
        {
            foreach (var reihe in reihen ?? Array.Empty<KurvenRandreihe>())
            {
                if (reihe.Buchten.Length == 0 || reihe.Stationen.Length < 2) continue;
                double laenge = reihe.Stationen[reihe.Stationen.Length - 1];
                var abschnitte = new List<double2>();
                double start = 0;
                foreach (var sperre in reihe.Sperren.OrderBy(s => s.x))
                {
                    double ende = Math.Min(laenge, sperre.x);
                    if (ende > start) abschnitte.Add(new double2(start, ende));
                    start = Math.Max(start, sperre.y);
                }
                if (start < laenge) abschnitte.Add(new double2(start, laenge));
                var stellen = new List<double>();
                foreach (var abschnitt in abschnitte)
                {
                    double l = abschnitt.y - abschnitt.x;
                    int anzahl = Math.Max(1, 1 + (int)Math.Floor(Math.Max(0, l - 3) / abstand));
                    double rest = l - (anzahl - 1) * abstand;
                    for (int i = 0; i < anzahl; i++)
                        stellen.Add(abschnitt.x + rest / 2 + i * abstand);
                }
                plan.Randlaeufe += abschnitte.Count;
                double vorher = double.NegativeInfinity;
                foreach (double stelle in stellen)
                {
                    // Ausduennen auf derselben Standlinie, nicht durch die Luft
                    // quer durch einen Bogen oder an einem Wendepunkt vorbei.
                    if (stelle - vorher < 0.45 * abstand)
                    {
                        plan.Ausgeduennt++;
                        continue;
                    }
                    int k = Array.BinarySearch(reihe.Stationen, stelle);
                    if (k < 0) k = ~k;
                    k = Math.Max(1, Math.Min(reihe.Stationen.Length - 1, k));
                    double ds = reihe.Stationen[k] - reihe.Stationen[k - 1];
                    float t = ds == 0 ? 0 : (float)((stelle - reihe.Stationen[k - 1]) / ds);
                    var punkt = math.lerp(reihe.Standlinie[k - 1], reihe.Standlinie[k], t);
                    if (!istGruen(punkt)) continue;
                    var richtung = math.normalizesafe(math.lerp(reihe.Innenrichtungen[k - 1], reihe.Innenrichtungen[k], t));
                    plan.Laternen.Add(new LaternenPlatz { Position = punkt, Art = LaternenArt.Rand, Richtung = richtung });
                    vorher = stelle;
                }
            }
        }
    }
}
