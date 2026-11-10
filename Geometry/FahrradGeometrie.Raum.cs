using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class FahrradGeometrie
    {
        internal const double Epsilon = 0.00001;

        private sealed class Raum
        {
            internal double2[] Ring;
            internal double2 Ursprung, X, Y;
            internal double MinX, MaxX, MinY, MaxY, Winkel;

            internal Raum(float2[] polygon, LayoutSettings settings)
                : this(polygon, Startwinkel(polygon, settings)) { }

            private static double Startwinkel(float2[] polygon, LayoutSettings settings)
            {
                var laengste = new double2(1, 0);
                foreach (var i in Enumerable.Range(0, polygon.Length))
                {
                    var d = (double2)polygon[(i + 1) % polygon.Length] - (double2)polygon[i];
                    if (math.lengthsq(d) > math.lengthsq(laengste) + Epsilon) laengste = d;
                }
                return ((settings.Ausrichtwinkel ?? (Math.Atan2(laengste.y, laengste.x) * 180 / Math.PI))
                    + settings.Angle + (settings.AngleMode == "quer" ? 90 : 0)) * Math.PI / 180;
            }

            /** Lokalrahmen mit fest vorgegebener +X-Richtung (Radiant). */
            internal Raum(float2[] polygon, double winkel)
            {
                Ursprung = (double2)polygon[0];
                Winkel = winkel;
                X = new double2(Math.Cos(Winkel), Math.Sin(Winkel));
                Y = new double2(-X.y, X.x);
                Ring = polygon.Select(Lokal).ToArray();
                MinX = Ring.Min(p => p.x); MaxX = Ring.Max(p => p.x);
                MinY = Ring.Min(p => p.y); MaxY = Ring.Max(p => p.y);
            }

            internal double2 Lokal(float2 p)
            {
                var d = (double2)p - Ursprung;
                return new double2(math.dot(d, X), math.dot(d, Y));
            }

            internal float2 Welt(double2 p) => (float2)(Ursprung + X * p.x + Y * p.y);
            internal float2[] Welt(double2[] ring) => ring.Select(Welt).ToArray();
            internal bool Innen(double2 p) => ParkingGeometry.PointIn(p, Ring)
                || ParkingGeometry.DistToBoundary(p, Ring) < Epsilon;

            internal bool Passt(double2[] rechteck, double rand = 0)
            {
                foreach (var p in rechteck)
                    if (!Innen(p) || ParkingGeometry.DistToBoundary(p, Ring) + Epsilon < rand)
                        return false;
                for (var i = 0; i < rechteck.Length; i++)
                {
                    var a = rechteck[i]; var b = rechteck[(i + 1) % rechteck.Length];
                    if (!Innen((a + b) / 2)) return false;
                    for (var j = 0; j < Ring.Length; j++)
                        if (Kreuzt(a, b, Ring[j], Ring[(j + 1) % Ring.Length])) return false;
                }
                // Eine konkave Kerbe kann ganz innerhalb des Rechtecks enden.
                foreach (var p in Ring)
                    if (StrengInnen(p, rechteck)) return false;
                return true;
            }

            internal bool Strecke(double2 a, double2 b, double breite)
                => Passt(Streifen(a, b, breite, true));

            /** Exakte Ereignisse des Bandes, bevor die Intervalle klassifiziert werden. */
            internal List<(double Von, double Bis)> Baender(double mitte, double breite, bool senkrecht)
            {
                var werte = new List<double>();
                double Quer(double2 p) => senkrecht ? p.x : p.y;
                double Laengs(double2 p) => senkrecht ? p.y : p.x;
                for (var i = 0; i < Ring.Length; i++)
                {
                    var a = Ring[i]; var b = Ring[(i + 1) % Ring.Length];
                    werte.Add(Laengs(a));
                    foreach (var q in new[] { mitte - breite / 2, mitte + breite / 2 })
                    {
                        var d = Quer(b) - Quer(a);
                        if (Math.Abs(d) < Epsilon) continue;
                        var t = (q - Quer(a)) / d;
                        if (t >= 0 && t <= 1) werte.Add(Laengs(a + (b - a) * t));
                    }
                }
                werte.Sort();
                var result = new List<(double Von, double Bis)>();
                for (var i = 1; i < werte.Count; i++)
                {
                    var lo = werte[i - 1]; var hi = werte[i];
                    if (hi - lo < Epsilon) continue;
                    var rect = senkrecht ? Rechteck(mitte - breite / 2, lo, breite, hi - lo)
                        : Rechteck(lo, mitte - breite / 2, hi - lo, breite);
                    if (!Passt(rect)) continue;
                    if (result.Count > 0 && Math.Abs(result[result.Count - 1].Bis - lo) < Epsilon)
                        result[result.Count - 1] = (result[result.Count - 1].Von, hi);
                    else result.Add((lo, hi));
                }
                return result;
            }
        }

        internal static double Kreuz(double2 a, double2 b) => a.x * b.y - a.y * b.x;
        private static bool Kreuzt(double2 a, double2 b, double2 c, double2 d)
        {
            var ab = b - a; var cd = d - c;
            var k = Kreuz(ab, cd);
            if (Math.Abs(k) < Epsilon) return false;
            var t = Kreuz(c - a, cd) / k; var u = Kreuz(c - a, ab) / k;
            return t > Epsilon && t < 1 - Epsilon && u > Epsilon && u < 1 - Epsilon;
        }

        internal static bool StrengInnen(double2 p, double2[] ring)
            => ParkingGeometry.PointIn(p, ring) && ParkingGeometry.DistToBoundary(p, ring) > Epsilon;

        internal static double2[] Rechteck(double x, double y, double w, double h)
            => new[] { new double2(x, y), new double2(x + w, y),
                new double2(x + w, y + h), new double2(x, y + h) };

        internal static double2[] Streifen(double2 a, double2 b, double breite, bool kappen = false)
        {
            var d = b - a;
            if (math.lengthsq(d) < Epsilon * Epsilon)
                return Rechteck(a.x - breite / 2, a.y - breite / 2, breite, breite);
            d = math.normalize(d);
            var n = new double2(-d.y, d.x) * (breite / 2);
            if (kappen) { a -= d * (breite / 2); b += d * (breite / 2); }
            return new[] { a - n, b - n, b + n, a + n };
        }

        internal static bool Ueberlappt(double2[] a, double2[] b)
        {
            // Trennachsensatz: Beruehrung ist erlaubt, positive Flaeche nicht.
            foreach (var ring in new[] { a, b })
                for (var i = 0; i < ring.Length; i++)
                {
                    var d = ring[(i + 1) % ring.Length] - ring[i];
                    var n = math.normalize(new double2(-d.y, d.x));
                    var av = a.Select(p => math.dot(p, n));
                    var bv = b.Select(p => math.dot(p, n));
                    if (av.Max() <= bv.Min() + Epsilon || bv.Max() <= av.Min() + Epsilon) return false;
                }
            return true;
        }
    }
}
