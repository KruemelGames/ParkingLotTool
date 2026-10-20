using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * NAEHTE ZWISCHEN GLEICHEM MATERIAL GERADE ZIEHEN (20.10.2026).
         *
         * Fahrgasse und Randstrasse sind getrennte Asphaltflaechen (Vereinigung
         * trennt nach Rolle). Ihre Naht folgt dem Innenrand der Randstrasse, und
         * an einer Kurve hat der Sehnenpunkte. Traf die Gasse 11,6 cm hinter
         * einem solchen Punkt auf den Rand, bekam ihr Ring eine 11,6-cm-Kante
         * neben einer fast rechtwinkligen Ecke. CS2 versetzt jeden Ring vor dem
         * Dreiecken 0,1 m nach innen; diese Kante kehrte sich dabei um, CS2
         * lehnte den ganzen Ring ab, und beim Zerlegen ging ein 6 cm schmaler,
         * 70 m langer Streifen als Haarriss verloren (KURVEN-15, Raster 0,2).
         *
         * Wo die Naht zwischen zwei Flaechen DESSELBEN Materials verlaeuft,
         * sieht niemand. Liegt so ein Punkt nur auf der Naht (kein dritter Ring,
         * auch keiner aus dem anderen Material), wird er aus beiden Ringen
         * genommen: die Naht laeuft gerade, das Dreieck wechselt die Seite,
         * keine Flaeche geht verloren. Nur an Ringen, die CS2 sonst ablehnt -
         * alles andere bleibt bitgleich.
         */
        private const float NahtKurzeKante = 0.375f;
        private const double NahtHoechsterKnick = 15.0;

        private static float2[][] GlaetteNaehte(float2[][] ringe, float2[][] andere)
        {
            bool Abgelehnt(float2[] ring) =>
                !KurvenringEinfach(ring) || Cs2Triangulierung.Dreiecke(ring) == 0;
            if (ringe == null || !ringe.Any(Abgelehnt)) return ringe;
            var liste = ringe.Select(r => (float2[])r.Clone()).ToList();
            for (int r = 0; r < liste.Count; r++)
                for (int schritt = 0; schritt < 8 && Abgelehnt(liste[r]); schritt++)
                    if (!GlaetteEineNaht(liste, r, andere, Abgelehnt)) break;
            return liste.ToArray();
        }

        private static bool GlaetteEineNaht(List<float2[]> liste, int r, float2[][] andere,
            System.Func<float2[], bool> abgelehnt)
        {
            var ring = liste[r];
            int n = ring.Length;
            if (n < 4) return false;
            double Knick(int i)
            {
                var a = ring[(i - 1 + n) % n]; var p = ring[i]; var b = ring[(i + 1) % n];
                double2 u = p - a, v = b - p;
                return math.degrees(System.Math.Abs(System.Math.Atan2(
                    u.x * v.y - u.y * v.x, u.x * v.x + u.y * v.y)));
            }
            float KuerzereKante(int i) => math.min(
                math.distance(ring[(i - 1 + n) % n], ring[i]),
                math.distance(ring[i], ring[(i + 1) % n]));
            var kandidaten = Enumerable.Range(0, n)
                .Where(i => KuerzereKante(i) < NahtKurzeKante && Knick(i) < NahtHoechsterKnick)
                .OrderBy(KuerzereKante);
            foreach (int i in kandidaten)
            {
                var p = ring[i];
                var o = ring[(i - 1 + n) % n];
                var q = ring[(i + 1) % n];
                // Gehoert der Punkt einer Flaeche aus anderem Material, ist es
                // eine echte Grenze, keine Naht.
                if (andere != null && andere.Any(a => a.Any(x => x.Equals(p)))) continue;
                int nachbar = -1, stelle = -1, treffer = 0;
                for (int s = 0; s < liste.Count; s++)
                {
                    if (s == r) continue;
                    for (int j = 0; j < liste[s].Length; j++)
                        if (liste[s][j].Equals(p)) { nachbar = s; stelle = j; treffer++; }
                }
                if (treffer != 1) continue;
                var gegen = liste[nachbar];
                int m = gegen.Length;
                var vor = gegen[(stelle - 1 + m) % m];
                var nach = gegen[(stelle + 1) % m];
                // Die Nachbarflaeche laeuft die Naht gegenlaeufig: q liegt auf
                // ihrer Kante vor p, o auf der danach.
                if (!(q.Equals(vor) || AufNaht(q, vor, p)) || !(o.Equals(nach) || AufNaht(o, p, nach))) continue;
                var einsatz = new List<float2>();
                if (!q.Equals(vor)) einsatz.Add(q);
                if (!o.Equals(nach)) einsatz.Add(o);
                var neuGegen = gegen.Take(stelle).Concat(einsatz).Concat(gegen.Skip(stelle + 1)).ToArray();
                var neuRing = ring.Where((_, k) => k != i).ToArray();
                if (!KurvenringEinfach(neuRing) || !KurvenringEinfach(neuGegen)) continue;
                if (!abgelehnt(gegen) && abgelehnt(neuGegen)) continue;
                double vorher = RingflaecheFloat(ring) + RingflaecheFloat(gegen);
                double nachher = RingflaecheFloat(neuRing) + RingflaecheFloat(neuGegen);
                // 0,01 m2 wie die Materialpruefung: float-Rundung der Nahtpunkte.
                if (System.Math.Abs(vorher - nachher) > 0.01) continue;
                liste[r] = neuRing;
                liste[nachbar] = neuGegen;
                return true;
            }
            return false;
        }

        // x liegt (bis 1 mm) im Inneren der Strecke a-b.
        private static bool AufNaht(float2 x, float2 a, float2 b)
        {
            double2 ab = (double2)b - a, ax = (double2)x - a;
            double laenge = math.lengthsq(ab);
            if (laenge < 1e-12) return false;
            double t = math.dot(ax, ab) / laenge;
            if (t <= 0 || t >= 1) return false;
            return math.distance(a + t * ab, (double2)x) < 1e-3;
        }
    }
}
