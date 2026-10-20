using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Eingefrorener Zahlenstand. Die ID kommt spaeter aus der Shape, nie aus einer Entity.
    public sealed class Kurvengruppe
    {
        public readonly struct Abschnitt
        {
            public readonly double3 A, B, C, D;

            public Abschnitt(double3 a, double3 b, double3 c, double3 d)
            {
                A = a; B = b; C = c; D = d;
            }

            internal static Abschnitt Aus(Strassenkurve.Bezier c)
            {
                if (!c.HatQuelle) return new Abschnitt(c.A, c.B, c.C, c.D);
                var basis = new Abschnitt(c.QuelleA, c.QuelleB, c.QuelleC, c.QuelleD);
                return basis.Teil(c.Von, c.Bis);
            }

            public double3 Punkt(double t)
            {
                double u = 1 - t;
                return u * u * u * A + 3 * u * u * t * B + 3 * u * t * t * C + t * t * t * D;
            }

            public Abschnitt Umkehren() => new Abschnitt(D, C, B, A);

            public Abschnitt Teil(double von, double bis)
            {
                if (von > bis) return Teil(bis, von).Umkehren();
                Teile(bis, out var links, out _);
                if (von > 0 && bis > 0) links.Teile(von / bis, out _, out links);
                return links;
            }

            private void Teile(double t, out Abschnitt links, out Abschnitt rechts)
            {
                var ab = math.lerp(A, B, t);
                var bc = math.lerp(B, C, t);
                var cd = math.lerp(C, D, t);
                var abc = math.lerp(ab, bc, t);
                var bcd = math.lerp(bc, cd, t);
                var mitte = math.lerp(abc, bcd, t);
                links = new Abschnitt(A, ab, abc, mitte);
                rechts = new Abschnitt(mitte, bcd, cd, D);
            }

            internal double Kontrolllaenge => math.distance(A, B) + math.distance(B, C) + math.distance(C, D);

            internal double Huelle(double3 a, double3 b)
            {
                return Math.Max(Abstand(A, a, b), Math.Max(Abstand(B, a, b),
                    Math.Max(Abstand(C, a, b), Abstand(D, a, b))));
            }

            private static double Abstand(double3 p, double3 a, double3 b)
            {
                var v = b.xz - a.xz;
                double nenner = math.lengthsq(v);
                double t = nenner > 0 ? math.clamp(math.dot(p.xz - a.xz, v) / nenner, 0, 1) : 0;
                return math.distance(p.xz, a.xz + t * v);
            }

            // Adaptive Simpson-Integration; absolute Laengentoleranz in Metern.
            public double Bogenlaenge(double bis = 1)
            {
                if (!math.isfinite(bis)) throw new ArgumentOutOfRangeException(nameof(bis));
                if (bis <= 0) return 0;
                bis = Math.Min(1, bis);
                var v0 = B - A;
                var v1 = C - B;
                var v2 = D - C;
                double Tempo(double t)
                {
                    var v = 3 * (1 - t) * (1 - t) * v0
                        + 6 * (1 - t) * t * v1 + 3 * t * t * v2;
                    return math.length(v);
                }
                double Integral(double a, double b, double fa, double fm, double fb, double alt, double eps, int tiefe)
                {
                    double m = (a + b) / 2;
                    double fl = Tempo((a + m) / 2), fr = Tempo((m + b) / 2);
                    double l = (m - a) * (fa + 4 * fl + fm) / 6;
                    double r = (b - m) * (fm + 4 * fr + fb) / 6;
                    if (tiefe == 0 || Math.Abs(l + r - alt) <= 15 * eps)
                        return l + r + (l + r - alt) / 15;
                    return Integral(a, m, fa, fl, fm, l, eps / 2, tiefe - 1)
                        + Integral(m, b, fm, fr, fb, r, eps / 2, tiefe - 1);
                }
                double f0 = Tempo(0), fh = Tempo(bis / 2), f1 = Tempo(bis);
                return Integral(0, bis, f0, fh, f1, bis * (f0 + 4 * fh + f1) / 6, Bogenfehler, 18);
            }
        }

        public readonly struct Polygonlage
        {
            public readonly int Segment;
            // Wie Entrance.Along: XZ-Meter ab Polygonsegmentanfang, kein Anteil.
            public readonly double Along;
            public Polygonlage(int segment, double along) { Segment = segment; Along = along; }
        }

        public const double Bogenfehler = .00001;
        public long Id { get; }
        public ReadOnlyCollection<Abschnitt> Abschnitte { get; }
        public ReadOnlyCollection<double3> Punkte { get; }
        public ReadOnlyCollection<double> Bogenlaengen { get; }
        public double Laenge => Bogenlaengen[Bogenlaengen.Count - 1];
        public double3 Anfang => Punkte[0];
        public double3 Ende => Punkte[Punkte.Count - 1];
        public int Stuecke => Punkte.Count - 1;

        internal Kurvengruppe(long id, IReadOnlyList<Strassenkurve.Bezier> kurven,
            float3[] punkte, (int Index, float T)[] stellen)
        {
            Id = id;
            var abschnitte = new Abschnitt[kurven.Count];
            var grenzen = new double[kurven.Count + 1];
            for (int i = 0; i < kurven.Count; i++)
            {
                abschnitte[i] = Abschnitt.Aus(kurven[i]);
                grenzen[i + 1] = grenzen[i] + abschnitte[i].Bogenlaenge();
            }
            var p = new double3[punkte.Length];
            var s = new double[punkte.Length];
            for (int i = 0; i < p.Length; i++)
            {
                p[i] = punkte[i];
                var ort = stellen[i];
                s[i] = grenzen[ort.Index] + abschnitte[ort.Index].Bogenlaenge(ort.T);
            }
            s[0] = 0;
            s[s.Length - 1] = grenzen[grenzen.Length - 1];
            Abschnitte = Array.AsReadOnly(abschnitte);
            Punkte = Array.AsReadOnly(p);
            Bogenlaengen = Array.AsReadOnly(s);
        }

        private Kurvengruppe(long id, Abschnitt[] abschnitte, double3[] punkte, double[] laengen)
        {
            Id = id;
            Abschnitte = Array.AsReadOnly(abschnitte);
            Punkte = Array.AsReadOnly(punkte);
            Bogenlaengen = Array.AsReadOnly(laengen);
        }

        public Kurvengruppe MitId(long id)
            => new Kurvengruppe(id, Kopie(Abschnitte), Kopie(Punkte), Kopie(Bogenlaengen));

        private static T[] Kopie<T>(IReadOnlyList<T> quelle)
        {
            var ziel = new T[quelle.Count];
            for (int i = 0; i < ziel.Length; i++) ziel[i] = quelle[i];
            return ziel;
        }

        public Kurvengruppe Verschoben(double3 delta)
        {
            var a = new Abschnitt[Abschnitte.Count];
            var p = new double3[Punkte.Count];
            for (int i = 0; i < a.Length; i++)
            {
                var c = Abschnitte[i];
                a[i] = new Abschnitt(c.A + delta, c.B + delta, c.C + delta, c.D + delta);
            }
            for (int i = 0; i < p.Length; i++) p[i] = Punkte[i] + delta;
            return new Kurvengruppe(Id, a, p, Kopie(Bogenlaengen));
        }

        // Ohne passenden Fang bleibt die Kontur, nur ihr bewegtes Ende folgt.
        public Kurvengruppe MitEndpunkten(double3 anfang, double3 ende)
        {
            var a = Kopie(Abschnitte);
            var p = Kopie(Punkte);
            var c = a[0]; var d = anfang - Anfang;
            a[0] = new Abschnitt(anfang, c.B + d, c.C, c.D);
            c = a[a.Length - 1]; d = ende - Ende;
            a[a.Length - 1] = new Abschnitt(c.A, c.B, c.C + d, ende);
            p[0] = anfang; p[p.Length - 1] = ende;
            // Die alte Abtastung bleibt. Bogenpositionen folgen ihren neuen
            // Sehnenlaengen, damit geloeste Enden keine Zufahrt versetzen.
            var s = Kopie(Bogenlaengen);
            double prefix = 0;
            for (int i = 1; i < s.Length; i++)
            {
                prefix += math.distance(p[i - 1], p[i]);
                s[i] = prefix;
            }
            return new Kurvengruppe(Id, a, p, s);
        }

        public void Teilen(double bogen, out Kurvengruppe links, out Kurvengruppe rechts)
        {
            if (!math.isfinite(bogen) || bogen <= .001 || bogen >= Laenge - .001)
                throw new ArgumentOutOfRangeException(nameof(bogen));
            AufPolygon(bogen, 0, Stuecke, out var lage);
            int k = lage.Segment;
            double t = (bogen - Bogenlaengen[k]) / (Bogenlaengen[k + 1] - Bogenlaengen[k]);
            var schnitt = math.lerp(Punkte[k], Punkte[k + 1], t);
            // Die Bezier-Abschnitte werden am selben Bogenparameter geteilt.
            double rest = bogen; int j = 0;
            while (j + 1 < Abschnitte.Count && rest > Abschnitte[j].Bogenlaenge())
                rest -= Abschnitte[j++].Bogenlaenge();
            double lo = 0, hi = 1;
            for (int i = 0; i < 40; i++)
            {
                double m = (lo + hi) / 2;
                if (Abschnitte[j].Bogenlaenge(m) < rest) lo = m; else hi = m;
            }
            double u = (lo + hi) / 2;
            var al = new List<Abschnitt>(); var ar = new List<Abschnitt>();
            for (int i = 0; i < j; i++) al.Add(Abschnitte[i]);
            al.Add(Abschnitte[j].Teil(0, u)); ar.Add(Abschnitte[j].Teil(u, 1));
            for (int i = j + 1; i < Abschnitte.Count; i++) ar.Add(Abschnitte[i]);
            var pl = new List<double3>(); var sl = new List<double>();
            var pr = new List<double3>(); var sr = new List<double>();
            for (int i = 0; i <= k; i++) { pl.Add(Punkte[i]); sl.Add(Bogenlaengen[i]); }
            if (math.distance(pl[pl.Count - 1], schnitt) > .00001) { pl.Add(schnitt); sl.Add(bogen); }
            pr.Add(schnitt); sr.Add(0);
            for (int i = k + 1; i < Punkte.Count; i++)
                if (math.distance(pr[pr.Count - 1], Punkte[i]) > .00001)
                { pr.Add(Punkte[i]); sr.Add(Bogenlaengen[i] - bogen); }
            links = new Kurvengruppe(Id, al.ToArray(), pl.ToArray(), sl.ToArray());
            rechts = new Kurvengruppe(Id, ar.ToArray(), pr.ToArray(), sr.ToArray());
        }

        public Kurvengruppe Umkehren()
        {
            var a = new Abschnitt[Abschnitte.Count];
            var p = new double3[Punkte.Count];
            var s = new double[Punkte.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Abschnitte[a.Length - 1 - i].Umkehren();
            for (int i = 0; i < p.Length; i++)
            {
                p[i] = Punkte[p.Length - 1 - i];
                s[i] = Laenge - Bogenlaengen[p.Length - 1 - i];
            }
            return new Kurvengruppe(Id, a, p, s);
        }

        public bool AufPolygon(double bogen, int ersteKante, int polygonkanten, out Polygonlage lage)
        {
            lage = default;
            if (!GueltigeZuordnung(ersteKante, polygonkanten) || !math.isfinite(bogen) || bogen < 0 || bogen > Laenge)
                return false;
            int i = 0;
            while (i + 1 < Stuecke && bogen >= Bogenlaengen[i + 1]) i++;
            double anteil = (bogen - Bogenlaengen[i]) / (Bogenlaengen[i + 1] - Bogenlaengen[i]);
            lage = new Polygonlage((ersteKante + i) % polygonkanten,
                anteil * math.distance(Punkte[i].xz, Punkte[i + 1].xz));
            return true;
        }

        public bool VomPolygon(Polygonlage lage, int ersteKante, int polygonkanten, out double bogen)
        {
            bogen = 0;
            if (!GueltigeZuordnung(ersteKante, polygonkanten) || lage.Segment < 0 || lage.Segment >= polygonkanten
                || !math.isfinite(lage.Along)) return false;
            int i = (lage.Segment - ersteKante + polygonkanten) % polygonkanten;
            if (i >= Stuecke) return false;
            double sehne = math.distance(Punkte[i].xz, Punkte[i + 1].xz);
            if (lage.Along < 0 || lage.Along > sehne) return false;
            bogen = Bogenlaengen[i] + lage.Along / sehne * (Bogenlaengen[i + 1] - Bogenlaengen[i]);
            return true;
        }

        private bool GueltigeZuordnung(int ersteKante, int anzahl)
            => anzahl >= Stuecke && ersteKante >= 0 && ersteKante < anzahl;
    }
}
