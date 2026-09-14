using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Ringlosplan
    {
        private static Weg EndwegAmRzAbschluss(Weg w, IReadOnlyList<Randzoningabschnitt> abschnitte)
        {
            foreach (var rz in abschnitte ?? Array.Empty<Randzoningabschnitt>())
            foreach (var endeB in new[] { false, true })
            {
                var ende = endeB ? rz.B : rz.A;
                var r = (rz.B - rz.A) * (1 / Geometrie.Laenge(rz.B - rz.A));
                var aussen = r * (endeB ? 1 : -1);
                {
                    var d = w.B - w.A;
                    var len = Geometrie.Laenge(d);
                    if (len < 1e-6 || Math.Abs(Geometrie.Skalar(d * (1 / len), r)) > 0.001) continue;
                    var abstand = Geometrie.Skalar((w.A + w.B) * 0.5 - ende, aussen);
                    if (Math.Abs(abstand - w.Breite / 2) > 0.01) continue;
                    var lotA = Geometrie.Skalar(w.A - ende, rz.Innen);
                    var lotB = Geometrie.Skalar(w.B - ende, rz.Innen);
                    if (Math.Max(lotA, lotB) < -rz.Halb || Math.Min(lotA, lotB) > rz.Halb) continue;
                    // Die RZ-Stirn und der fast senkrechte Endweg lagen im
                    // Nutzerfall um 0,00016 m auseinander. Das ergab eine
                    // Nadel in einem 360,53-m2-Grasring. Eine gemeinsame
                    // Stuetzlinie verhindert sie vor der Zellteilung.
                    if (lotA < lotB) lotA = Math.Min(lotA, -rz.Halb);
                    else lotB = Math.Min(lotB, -rz.Halb);
                    var neu = new Weg { A = ende + rz.Innen * lotA + aussen * (w.Breite / 2),
                        B = ende + rz.Innen * lotB + aussen * (w.Breite / 2),
                        Breite = w.Breite, Fuss = true, Art = Zufahrtsart.Fussweg };
                    return neu;
                }
            }
            return w;
        }
    }
}
