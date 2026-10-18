using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Reiner Ringschnitt mit einem Rechteck; getrennte Inseln bleiben getrennte Ringe. */
    public static class OberflaechenSchnitt
    {
        public const int MaxEingabeknoten = 8192;
        private const double Epsilon = 1e-8;

        private sealed class Kante
        {
            public double2 A, B;
            public readonly List<(double T, double2 P)> Schnitte = new List<(double, double2)>();
            public Kante(double2 a, double2 b) { A = a; B = b; Schnitte.Add((0, a)); Schnitte.Add((1, b)); }
        }
        private readonly struct Pfeil
        {
            public readonly (long, long) A, B;
            public Pfeil((long, long) a, (long, long) b) { A = a; B = b; }
        }
        private static double Kreuz(double2 a, double2 b) => a.x * b.y - a.y * b.x;
        private static (long, long) Key(double2 p) => ((long)Math.Round(p.x * 1e7), (long)Math.Round(p.y * 1e7));
        public static double Flaeche(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return 0;
            var ursprung = new double2(ring[0].x, ring[0].y);
            double summe = 0;
            for (int i = 0; i < ring.Length; i++)
                summe += Kreuz(new double2(ring[i].x, ring[i].y) - ursprung,
                    new double2(ring[(i + 1) % ring.Length].x, ring[(i + 1) % ring.Length].y) - ursprung);
            return summe * 0.5;
        }
        /** Platzierungsringe sind einfache Ringe ohne Lochreferenzen, wie beim Bau. */
        public static float2[][] Schneide(float2[] umriss, OberflaechenMuster.Bereich bereich)
        {
            if (!bereich.Gueltig) throw new ArgumentException("Ungueltiger Ausschnitt.");
            var mitte = bereich.Mitte; float radius = bereich.Halbseite;
            var ringe = new List<double2[]>();
            int knoten = 0;
            void Ring(float2[] q)
            {
                if (q == null || q.Length < 3) throw new ArgumentException("Ring hat weniger als drei Knoten.");
                knoten += q.Length;
                if (knoten > MaxEingabeknoten) throw new ArgumentException("Zu viele Musterknoten.");
                if (q.Any(p => !math.all(math.isfinite(p)))) throw new ArgumentException("Nicht endliche Flaechenkoordinate.");
                var a = q.Select(p => new double2(p.x - (double)mitte.x, p.y - (double)mitte.y)).ToList();
                if (math.lengthsq(a[0] - a[a.Count - 1]) == 0) a.RemoveAt(a.Count - 1);
                for (int i = a.Count - 1; i > 0; i--) if (math.lengthsq(a[i] - a[i - 1]) == 0) a.RemoveAt(i);
                if (a.Count < 3) throw new ArgumentException("Entarteter Flaechenring.");
                double f = 0;
                for (int i = 0; i < a.Count; i++) f += Kreuz(a[i], a[(i + 1) % a.Count]);
                if (f < 0) a.Reverse();
                ringe.Add(a.ToArray());
            }
            Ring(umriss);

            var rechteck = new[] { new double2(-radius, -radius), new double2(radius, -radius),
                new double2(radius, radius), new double2(-radius, radius) };
            var quelle = Kanten(ringe);
            var rand = Kanten(new[] { rechteck });

            // Rechteckabweisung vor dem Produkt; entfernte Ringe brauchen keine Schnittpaare.
            double minx = ringe[0].Min(p => p.x), maxx = ringe[0].Max(p => p.x);
            double miny = ringe[0].Min(p => p.y), maxy = ringe[0].Max(p => p.y);
            if (minx > radius || maxx < -radius || miny > radius || maxy < -radius)
            {
                return Array.Empty<float2[]>();
            }
            // Vollstaendig enthaltene Baupolygone brauchen keinen Randgraphen.
            if (minx >= -radius && maxx <= radius && miny >= -radius && maxy <= radius)
                return Zurueck(ringe, mitte);
            foreach (var a in quelle)
            foreach (var b in rand)
            {
                Teile(a, b);
            }
            var innen = new List<Pfeil>();

            var punkte = new Dictionary<(long, long), double2>();
            void Add(List<Pfeil> ziel, double2 a, double2 b)
            {
                var ka = Key(a); var kb = Key(b);
                if (ka.Equals(kb)) return;
                punkte[ka] = a; punkte[kb] = b;
                ziel.Add(new Pfeil(ka, kb));
            }
            void Stuecke(Kante k, Action<double2, double2, double2> aktion)
            {
                var sortiert = k.Schnitte.OrderBy(s => s.T).ToArray();
                for (int i = 1; i < sortiert.Length; i++)
                {
                    var a = sortiert[i - 1].P; var b = sortiert[i].P;
                    if (math.lengthsq(b - a) < 1e-20) continue;
                    aktion(a, b, (a + b) * 0.5);
                }
            }
            foreach (var k in quelle) Stuecke(k, (a, b, m) =>
            {
                var lage = Lage(m, new[] { rechteck });
                if (lage == 0)
                {
                    var links = new double2(-(b - a).y, (b - a).x);
                    lage = Lage(m + links * (1e-6 / math.length(links)), new[] { rechteck });
                }
                if (lage > 0) Add(innen, a, b);
            });
            foreach (var k in rand) Stuecke(k, (a, b, m) =>
            {
                if (Lage(m, ringe) <= 0) return;
                Add(innen, a, b);
            });
            return Verbinde(innen, punkte, mitte);
        }
        private static List<Kante> Kanten(IEnumerable<double2[]> ringe)
        {
            var result = new List<Kante>();
            foreach (var ring in ringe)
                for (int i = 0; i < ring.Length; i++) result.Add(new Kante(ring[i], ring[(i + 1) % ring.Length]));
            return result;
        }
        private static void Teile(Kante a, Kante b)
        {
            var r = a.B - a.A; var s = b.B - b.A;
            double den = Kreuz(r, s);
            if (Math.Abs(den) < 1e-14)
            {
                // Deckungsgleiche Grenzen an ihren Endpunkten teilen.
                if (Math.Abs(Kreuz(b.A - a.A, r)) > Epsilon) return;
                void Punkt(double2 p, Kante k)
                {
                    var d = k.B - k.A; var t = math.dot(p - k.A, d) / math.lengthsq(d);
                    if (t > 0 && t < 1) k.Schnitte.Add((t, p));
                }
                Punkt(a.A, b); Punkt(a.B, b); Punkt(b.A, a); Punkt(b.B, a); return;
            }
            var t1 = Kreuz(b.A - a.A, s) / den;
            var t2 = Kreuz(b.A - a.A, r) / den;
            if (t1 < -1e-12 || t1 > 1 + 1e-12 || t2 < -1e-12 || t2 > 1 + 1e-12) return;
            var p = a.A + math.clamp(t1, 0, 1) * r;
            a.Schnitte.Add((math.clamp(t1, 0, 1), p)); b.Schnitte.Add((math.clamp(t2, 0, 1), p));
        }
        private static int Lage(double2 p, IEnumerable<double2[]> ringe)
        {
            bool drin = false;
            foreach (var ring in ringe)
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    var a = ring[j]; var b = ring[i]; var d = b - a;
                    if (Math.Abs(Kreuz(d, p - a)) <= Epsilon * math.length(d)
                        && math.dot(p - a, p - b) <= Epsilon) return 0;
                    if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (b.x - a.x) * (p.y - a.y) / (b.y - a.y)) drin = !drin;
                }
            return drin ? 1 : -1;
        }
        private static float2[][] Zurueck(IEnumerable<double2[]> ringe, float2 mitte)
            => ringe.Select(r => r.Select(p => new float2((float)(p.x + mitte.x), (float)(p.y + mitte.y))).ToArray()).ToArray();
        private static float2[][] Verbinde(List<Pfeil> kanten, Dictionary<(long, long), double2> punkte, float2 mitte)
        {
            var result = new List<double2[]>();
            var aus = new Dictionary<(long, long), List<int>>();
            var benutzt = new bool[kanten.Count];
            for (int i = 0; i < kanten.Count; i++)
            {
                if (!aus.TryGetValue(kanten[i].A, out var liste)) aus[kanten[i].A] = liste = new List<int>();
                liste.Add(i);
            }
            for (int i = 0; i < kanten.Count; i++)
            {
                if (benutzt[i]) continue;
                var ring = new List<double2>(); int aktuell = i;
                var start = kanten[i].A;
                for (int schritte = 0; schritte <= kanten.Count; schritte++)
                {
                    var k = kanten[aktuell]; benutzt[aktuell] = true; ring.Add(punkte[k.A]);
                    if (k.B.Equals(start)) break;
                    if (!aus.TryGetValue(k.B, out var optionen)) throw new InvalidOperationException("Offene Musternkante.");
                    var richtung = punkte[k.B] - punkte[k.A];
                    double besteDrehung = double.NegativeInfinity; int naechste = -1;
                    foreach (var o in optionen)
                    {
                        if (benutzt[o]) continue;
                        var d = punkte[kanten[o].B] - punkte[kanten[o].A];
                        var winkel = Math.Atan2(Kreuz(richtung, d), math.dot(richtung, d));
                        if (winkel > besteDrehung) { besteDrehung = winkel; naechste = o; }
                    }
                    if (naechste < 0) throw new InvalidOperationException("Musternkante ohne Fortsetzung.");
                    aktuell = naechste;
                }
                if (ring.Count >= 3) result.Add(ring.ToArray());
            }
            return Zurueck(result, mitte).Where(r => Math.Abs(Flaeche(r)) > 1e-10).ToArray();
        }
    }
}
