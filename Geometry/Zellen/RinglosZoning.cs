using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Ringlosplan
    {
        private void PlaneNormaleZoningkanten(IReadOnlyList<Zoningvorgabe> zoning,
            IReadOnlyList<Punkt> innen, double anker, double schritt, double breite, double buchttiefe,
            Func<double, bool, Querstrassenplan> strasse)
        {
            foreach (var z in zoning ?? Array.Empty<Zoningvorgabe>())
            {
                var ring = z.Strassenring();
                for (var i = 0; i < ring.Length; i++)
                {
                    // Seitenweise, nicht mehr fuer die ganze Flaeche.
                    // Hier stand `z.Rand > Strassenbreite`, und weil der Rand
                    // bis zum 2026-09-21 die Aussentiefe mittrug, fiel damit
                    // die GESAMTE Flaeche fuer Querstrassen weg, sobald
                    // irgendwo aussen Bauland stand. Der Nutzer will Baender
                    // nur an einzelnen Seiten; die uebrigen behalten ihren
                    // Anschluss.
                    if (z.AussenSeite(i) > 1e-6) continue;
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    if (Zoningkanten.Direkt(a, b)) continue;
                    var lo = Math.Min(a.X, b.X) + breite / 2;
                    var hi = Math.Max(a.X, b.X) - breite / 2;
                    var xs = new List<double>();
                    for (var k = Math.Ceiling((lo - anker) / schritt); anker + k * schritt <= hi + 1e-8; k++)
                        xs.Add(anker + k * schritt);
                    if (xs.Count == 0 && hi >= lo) xs.Add(Math.Max(lo, Math.Min(hi, anker)));
                    foreach (var x in xs)
                    {
                        var ziel = new Punkt(x, a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X));
                        foreach (var g in Gassen.Where(g => x >= g.A.X && x <= g.B.X)
                            .OrderBy(g => Math.Abs(g.A.Y - ziel.Y)))
                        {
                            var start = new Punkt(x, g.A.Y);
                            if (Geometrie.Kreuz(b - a, start - a) >= -1e-6) continue;
                            var w = new Weg { A = start.Y < ziel.Y ? start : ziel,
                                B = start.Y < ziel.Y ? ziel : start, Breite = breite };
                            if (!w.Ecken.All(p => Geometrie.EnthaeltOderRand(innen, p))) continue;
                            if (zoning.Any(h => Ueberlappt(w.Ecken,
                                ReferenceEquals(h, z) ? h.EckenMitAufschlag(0) : h.Umriss()))) continue;
                            if (Zoningkanten.ImDirektstreifen(w, zoning, buchttiefe)) continue;
                            if (Querwege.Any(q => Ueberlappt(w.Ecken, new Weg {
                                A = q.Anfang, B = q.Ende, Breite = breite }.Ecken))) continue;
                            Querwege.Add(new Querstrassenstueck { Querstrasse = strasse(x, true),
                                Anfang = w.A, Ende = w.B });
                            break;
                        }
                    }
                }
            }
        }
    }
}
