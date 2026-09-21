using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal static class Zoningkanten
    {
        // Im Reihenrahmen ist X die gemessene Gassenrichtung, nicht Welt-X.
        // Gleichstand bei 45 Grad (+/- 0,001 Grad): direkter Gassenanschluss.
        internal static bool Direkt(Punkt a, Punkt b)
        {
            var d = b - a;
            return Math.Atan2(Math.Abs(d.Y), Math.Abs(d.X)) >= (45 - 0.001) * Math.PI / 180;
        }

        // Exakte Projektion des mit dem Gassenstreifen geschnittenen Polygons.
        // Ein Huelleck ausserhalb dieses Streifens darf die Gasse nicht kuerzen.
        internal static (double A, double B)? Streifenschnitt(Punkt[] polygon, double unten, double oben)
        {
            var xs = new List<double>();
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                if (a.Y >= unten - 1e-9 && a.Y <= oben + 1e-9) xs.Add(a.X);
                if (Math.Abs(b.Y - a.Y) < 1e-12) continue;
                foreach (var y in new[] { unten, oben })
                {
                    var t = (y - a.Y) / (b.Y - a.Y);
                    if (t >= 0 && t <= 1) xs.Add(a.X + t * (b.X - a.X));
                }
            }
            return xs.Count == 0 ? ((double, double)?)null : (xs.Min(), xs.Max());
        }

        internal static bool Schnitt(Punkt a, Punkt b, Punkt c, Punkt d, out Punkt punkt)
        {
            punkt = default;
            var ab = b - a; var cd = d - c;
            var det = Geometrie.Kreuz(ab, cd);
            if (Math.Abs(det) < 1e-10) return false;
            var t = Geometrie.Kreuz(c - a, cd) / det;
            var u = Geometrie.Kreuz(c - a, ab) / det;
            if (t < -1e-8 || t > 1 + 1e-8 || u < -1e-8 || u > 1 + 1e-8) return false;
            punkt = c + cd * Math.Max(0, Math.Min(1, u));
            return true;
        }

        internal static Punkt Anschluss(Punkt ende, Punkt richtung, double breite,
            IReadOnlyList<Zoningvorgabe> zoning, IReadOnlyList<Punkt> innen, bool direkt)
        {
            var ziel = ende;
            var best = double.PositiveInfinity;
            foreach (var z in zoning ?? Array.Empty<Zoningvorgabe>())
            {
                var ring = z.Strassenring();
                // Ab 45 Grad ist der Projektionsfaktor hoechstens sqrt(2).
                var reichweite = (Math.Max(0, z.Rand - Zoningvorgabe.Strassenhalbbreite)
                    + breite / 2) * Math.Sqrt(2) + 1e-6;
                for (var i = 0; i < ring.Length; i++)
                {
                    // Aussenzoning ist Bauland, kein befahrbarer
                    // Anschlussstreifen - aber nur an SEINER Seite. Bis zum
                    // 2026-09-21 fiel hier die ganze Flaeche weg, sobald
                    // irgendwo aussen Bauland stand.
                    if (z.AussenSeite(i) > 1e-6) continue;
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    if (Direkt(a, b) != direkt) continue;
                    if (!Schnitt(ende, ende + richtung * reichweite, a, b, out var p)) continue;
                    var dist = Geometrie.Laenge(p - ende);
                    if (dist >= best) continue;
                    // Nur aus dem aeusseren Halbraum an die zugewandte Kante.
                    if (Geometrie.Kreuz(b - a, ende - a) > 1e-6) continue;
                    if (dist > 1e-6)
                    {
                        var anschluss = new Ringlosplan.Weg { A = ende, B = p, Breite = breite };
                        if (!anschluss.Ecken.All(v => Geometrie.EnthaeltOderRand(innen, v))) continue;
                        if (zoning.Any(h => Ringlosplan.Ueberlappt(anschluss.Ecken,
                            ReferenceEquals(h, z) ? h.EckenMitAufschlag(0) : h.Umriss()))) continue;
                    }
                    best = dist; ziel = p;
                }
            }
            return ziel;
        }

        // Der verbotene Streifen folgt der echten Kante. Schon eine teilweise
        // Ueberdeckung ist verboten; die Normalkanten sind hier ausgenommen.
        internal static bool ImDirektstreifen(Ringlosplan.Weg weg,
            IReadOnlyList<Zoningvorgabe> zoning, double tiefe)
        {
            foreach (var z in zoning ?? Array.Empty<Zoningvorgabe>())
            {
                var ring = z.Strassenring();
                for (var i = 0; i < ring.Length; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    if (!Direkt(a, b)) continue;
                    var d = b - a;
                    var n = new Punkt(d.Y, -d.X) * ((Zoningvorgabe.Strassenhalbbreite + tiefe) / Geometrie.Laenge(d));
                    if (Ringlosplan.Ueberlappt(weg.Ecken, new[] { a, b, b + n, a + n })) return true;
                }
            }
            return false;
        }
    }
}
