using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    // Eigenes Materialzellnetz: keine nachtraeglichen Ueberlagerungspolygone.
    // Der Buchten-/Netzplan behaelt seine unveraenderten Zellen.
    internal static class Strassenbelagplan
    {
        private sealed class Abschnitt
        {
            internal Punkt[] Ring;
        }

        /*
         * DIE UNSICHTBARE STRASSE ALS EIGENE FAHRBAHN (Nutzer 27.10.).
         *
         * Wo die Alley an der Randstrasse (oder einer Fahrgasse) endet,
         * zeichnet CS2 ihren Knoten als Halbkreis in diese Strasse. Frueher
         * lag der Asphalt mit der Ebene `Roads` darueber; seit Issue #22
         * zeichnet er nur aufs Gelaende, weil `Roads` an grossen Flaechen auf
         * die Fahrbahn einer Bruecke darueber durchschlug. Die Randstrasse
         * steckt aber in denselben grossen Asphaltringen wie die Buchten.
         *
         * Ursache, nicht Symptom: die Strasse, an der eine Alley endet, wird
         * eine EIGENE, durchgaengige Flaeche mit `Roads` (Nutzer: "die
         * Randstrasse soll durchgaengig sein aber jede alley abdecken"). Sie
         * liegt ueber dem Asphalt, ohne ihn zu zerschneiden - je
         * Asphaltring ein Stueck, damit Tools ihr dessen Texturlage geben
         * kann (RichteFahrbahnAus). Zwei Irrwege davor: Alley-Flaeche quer
         * ueber die Strasse; ein aufgeklebtes Kreuzungsstueck, das an einer
         * Ecke der Randstrasse auch noch wegfiel.
         */
        private static bool Strassenzelle(Zelle z) => !IstBucht(z)
            && z.Material == Material.Asphalt
            && (z.Art == Zellart.Fahrgasse || z.Art == Zellart.Randstrasse
                || z.Art == Zellart.Querstrasse);

        /*
         * WAS UNTER DER ALLEY STRASSENBELAG WIRD (Nutzer 27.10.).
         *
         * Asphalt und Gruen, soweit die Alley-Fahrbahn darueber liegt - das
         * Gruen dort sieht ohnehin niemand, die Strasse deckt es. NICHT:
         * Buchten und die Zellen unter einer unsichtbaren Strasse
         * (Fahrgasse, Randstrasse, Querstrasse). Der Querbalken des T gehoert
         * damit zur Fahrgasse und teilt ihren Asphalt nicht mehr; die
         * Alley-Flaeche endet an deren Rand. Nutzer: "nur den Teil der Alley
         * wo keine Invisible road langlaeuft auch als alley flaeche".
         */
        private static bool Belegbar(Zelle z) => !IstBucht(z)
            && (z.Material == Material.Asphalt || z.Material == Material.Gruen)
            && z.Art != Zellart.Fahrgasse && z.Art != Zellart.Randstrasse
            && z.Art != Zellart.Querstrasse;

        private static bool IstBucht(Zelle z) => z.BuchtId.HasValue || z.Art == Zellart.Bucht;

        internal static Bauergebnis Baue(Bauergebnis bau,
            IReadOnlyList<(Punkt A, Punkt B)> gassen,
            IReadOnlyList<Punkt[]> bruecken = null)
        {
            bruecken = bruecken ?? Array.Empty<Punkt[]>();
            var abschnitte = new List<Abschnitt>();
            var halb = ParkingGeometry.Zufahrtsgassenbreite / 2;
            foreach (var gasse in gassen)
            {
                var d = gasse.B - gasse.A;
                var laenge = Geometrie.Laenge(d);
                if (laenge <= 1e-9) continue;
                var u = d * (1 / laenge);
                var q = new Punkt(-u.Y, u.X);
                /*
                 * Genau die gemessene Alley: 8 m breit, von der Stadtstrasse
                 * bis zu ihrem inneren Knoten. Ein frueheres Endstueck ueber
                 * die Fahrgasse (fuer die Knotenrundung) zerschnitt deren
                 * Asphalt sichtbar und ist raus; die Zellen der unsichtbaren
                 * Strasse schliesst Belegbar aus, daher endet der Belag an
                 * ihrem Rand. Die Stirn beginnt vor A: an einer hohlen Kontur
                 * liegt Lotboden auch kurz vor der Sehnentangente.
                 */
                var a0 = gasse.A - u * halb;
                abschnitte.Add(new Abschnitt { Ring = new[]
                    { a0 - q * halb, gasse.B - q * halb, gasse.B + q * halb, a0 + q * halb } });
            }
            if (abschnitte.Count == 0) return bau;

            var knoten = new Knotenfabrik();
            knoten.Fortsetzen(bau.Zellen.SelectMany(z => z.Polygon.Ecken)
                .Max(e => e.Knoten.Id) + 1);
            var linien = new Linienregister();
            linien.Fortsetzen(bau.Zellen.SelectMany(z => z.Polygon.Ecken)
                .Max(e => e.LinieBisNaechste.Id) + 1);
            var teiler = new Polygonteiler(knoten);
            var zellen = bau.Zellen.Select(z => Kopie(z, new Polygon(z.Polygon.Ecken))).ToList();
            // Geschnitten wird nur, was Strassenbelag werden kann; Buchten und
            // Strassenzellen behalten ihre Zellen (nur Nachbarknoten kommen dazu).
            zellen = Teile(zellen, abschnitte.Select(a => a.Ring), linien, teiler, Belegbar);
            // Brueckenraender schneiden nur Strassenzellen: die Fahrbahn
            // spart den Grundriss einer Bruecke darueber aus.
            if (bruecken.Count > 0)
                zellen = Teile(zellen, bruecken, linien, teiler, Strassenzelle);
            // Native Baubarkeit liefert Randbedingungen fuer den Materialplan,
            // bevor dessen endgueltige Rasterzellen klassifiziert werden.
            var vorplan = Klassifiziere(zellen, abschnitte, Array.Empty<Punkt[]>());
            var roh = Vereinigung.Vereinige(vorplan);
            var offen = Lochtrenner.Trenne(vorplan, roh.Flaechen, bau.Rahmen);
            var rastersaeume = ParkingGeometry.GruenRastervorgaben(bau.Rahmen, offen.Flaechen, vorplan);
            var materialzellen = vorplan;
            var trennung = offen;
            if (rastersaeume.Length > 0)
            {
                zellen = Teile(zellen, rastersaeume, linien, teiler);
                materialzellen = Klassifiziere(zellen, abschnitte, rastersaeume);
                var vereinigung = Vereinigung.Vereinige(materialzellen);
                trennung = Lochtrenner.Trenne(materialzellen, vereinigung.Flaechen, bau.Rahmen);
            }
            return new Bauergebnis
            {
                Rahmen = bau.Rahmen,
                Kurvenrand = bau.Kurvenrand,
                Fahrbahnringe = Fahrbahnen(gassen, materialzellen, trennung.Flaechen, bau.Rahmen, bruecken),
                Zellen = materialzellen,
                Flaechen = trennung.Flaechen,
                Lochtrennung = trennung.Bericht,
                Schnittwarnungen = teiler.Warnungen.ToArray(),
            };
        }

        /*
         * Welche Strassen? Die Arten der Strassenzellen am inneren Knoten
         * jeder Alley - in der Regel die Randstrasse. Von diesen Arten die
         * ganze Strasse, je Asphaltring vereinigt (Loecher getrennt: eine
         * umlaufende Randstrasse in einem einzigen Ring waere sonst ein
         * Kreisring, den CS2 nicht kennt).
         *
         * UNTER EINER BRUECKE KEINE FAHRBAHN (Issue #22, Nutzer 27.10.).
         * Der Flaechenshader prueft keine Hoehe: `Roads` malte auch auf die
         * Brueckenfahrbahn darueber. Dort bleibt nur der Asphalt darunter;
         * nur ein Alley-Knoten genau unter einer Bruecke bliebe sichtbar.
         */
        private static IReadOnlyList<Ring> Fahrbahnen(IReadOnlyList<(Punkt A, Punkt B)> gassen,
            IReadOnlyList<Zelle> zellen, IReadOnlyList<Flaeche> flaechen, Rahmen rahmen,
            IReadOnlyList<Punkt[]> bruecken)
        {
            var arten = new HashSet<Zellart>();
            foreach (var gasse in gassen)
            foreach (var z in zellen)
            {
                if (!Strassenzelle(z)) continue;
                var punkte = z.Polygon.Punkte.ToArray();
                var amKnoten = Geometrie.EnthaeltOderRand(punkte, gasse.B)
                    || Enumerable.Range(0, punkte.Length).Any(i => Geometrie.AbstandPunktStrecke(
                        gasse.B, punkte[i], punkte[(i + 1) % punkte.Length]) <= 0.05);
                if (amKnoten) arten.Add(z.Art);
            }
            var ringe = new List<Ring>();
            if (arten.Count == 0) return ringe;
            var nachId = new Dictionary<int, Zelle>();
            foreach (var z in zellen) nachId[z.Id] = z;
            foreach (var f in flaechen.Where(f => f.Material == Material.Asphalt))
            {
                var auswahl = (f.ZellIds ?? new List<int>())
                    .Where(nachId.ContainsKey).Select(id => nachId[id])
                    .Where(z => Strassenzelle(z) && arten.Contains(z.Art))
                    .Where(z =>
                    {
                        var mitte = Geometrie.Mittelwert(z.Polygon);
                        return !bruecken.Any(b => Geometrie.Enthaelt(b, mitte));
                    })
                    .Select((z, i) =>
                    {
                        var kopie = Kopie(z, z.Polygon);
                        kopie.Id = i;
                        return kopie;
                    }).ToList();
                if (auswahl.Count == 0) continue;
                var roh = Vereinigung.Vereinige(auswahl);
                ringe.AddRange(Lochtrenner.Trenne(auswahl, roh.Flaechen, rahmen).Flaechen
                    .Select(g => g.Aussenring));
            }
            return ringe;
        }

        private static List<Zelle> Teile(List<Zelle> zellen, IEnumerable<Punkt[]> ringe,
            Linienregister linien, Polygonteiler teiler, Func<Zelle, bool> schneiden = null)
            => Teile(zellen, ringe.SelectMany(ring => Enumerable.Range(0, ring.Length)
                .Select(k => (ring[k], ring[(k + 1) % ring.Length]))), linien, teiler, schneiden);

        private static List<Zelle> Teile(List<Zelle> zellen, IEnumerable<(Punkt A, Punkt B)> kanten,
            Linienregister linien, Polygonteiler teiler, Func<Zelle, bool> schneiden)
        {
            foreach (var (a, b) in kanten)
            {
                if (Geometrie.Laenge(b - a) <= 1e-9) continue;
                var linie = linien.FreieGerade(a, b, Linienart.Zufahrtskante, "Strassenbelag");
                var d = b - a;
                var laenge2 = Geometrie.Skalar(d, d);
                var geteilt = new List<Zelle>();
                foreach (var zelle in zellen)
                {
                    var stationen = zelle.Polygon.Punkte.Select(p => Geometrie.Skalar(p - a, d)).ToArray();
                    var polygone = stationen.Max() < 0 || stationen.Min() > laenge2
                        || schneiden != null && !schneiden(zelle)
                        ? zelle.Polygon.Einzeln() : teiler.Teile(zelle.Polygon, linie);
                    foreach (var polygon in polygone) geteilt.Add(Kopie(zelle, polygon));
                }
                teiler.VervollstaendigeNachbarkanten(geteilt.Select(z => z.Polygon).ToArray(), linie);
                zellen = geteilt;
            }
            return zellen;
        }

        private static List<Zelle> Klassifiziere(IReadOnlyList<Zelle> quelle,
            IReadOnlyList<Abschnitt> strassen, IReadOnlyList<Punkt[]> saume)
        {
            var mitten = quelle.Select(z => Geometrie.Mittelwert(z.Polygon)).ToArray();
            var road = quelle.Select((z, i) => Belegbar(z)
                && strassen.Any(a => Geometrie.Enthaelt(a.Ring, mitten[i]))).ToArray();
            var asphaltkanten = new Dictionary<KantenSchluessel, Zelle>();
            for (int i = 0; saume.Count > 0 && i < quelle.Count; i++)
                if (!road[i] && quelle[i].Material == Material.Asphalt)
                    foreach (var kante in Kanten(quelle[i]))
                        if (!asphaltkanten.ContainsKey(kante.Schluessel))
                            asphaltkanten.Add(kante.Schluessel, quelle[i]);
            var boden = new Dictionary<int, Zelle>();
            foreach (var saum in saume)
            {
                var ids = Enumerable.Range(0, quelle.Count).Where(i => !road[i]
                    && quelle[i].Material == Material.Gruen && Geometrie.Enthaelt(saum, mitten[i])).ToArray();
                Zelle nachbar = null;
                double laengste = -1;
                foreach (var id in ids)
                foreach (var kante in Kanten(quelle[id]))
                {
                    if (!asphaltkanten.TryGetValue(kante.Schluessel, out var kandidat)) continue;
                    var laenge = Geometrie.Laenge(kante.Nach.Punkt - kante.Von.Punkt);
                    if (laenge <= laengste) continue;
                    nachbar = kandidat;
                    laengste = laenge;
                }
                if (nachbar != null) foreach (var id in ids) boden[id] = nachbar;
            }
            var ausgabe = new List<Zelle>();
            var angeschnitteneBuchten = new HashSet<int>();
            for (int i = 0; i < quelle.Count; i++)
            {
                var z = Kopie(quelle[i], quelle[i].Polygon);
                z.Id = i;
                if (road[i])
                {
                    if (z.BuchtId.HasValue) angeschnitteneBuchten.Add(z.BuchtId.Value);
                    z.Material = Material.Strassenbelag;
                    z.Art = Zellart.Zufahrt;
                    z.BuchtId = null;
                    z.Flaechenabschnitt = null;
                    z.GemeinsamerBoden = true;
                }
                else if (boden.TryGetValue(i, out var nachbar))
                {
                    // Normaler Boden, nicht Roads: die kurze Gruenspitze
                    // gehoert konstruktiv zum angrenzenden Asphaltabschnitt.
                    z.Material = Material.Asphalt;
                    z.Art = nachbar.Art;
                    z.Flaechenabschnitt = nachbar.Flaechenabschnitt;
                    z.GemeinsamerBoden = nachbar.GemeinsamerBoden;
                    z.BuchtId = null;
                }
                ausgabe.Add(z);
            }
            foreach (var z in ausgabe)
                if (z.BuchtId.HasValue && angeschnitteneBuchten.Contains(z.BuchtId.Value)) z.BuchtId = null;
            return ausgabe;
        }

        private static IEnumerable<GerichteteKante> Kanten(Zelle z)
        {
            for (int i = 0; i < z.Polygon.Anzahl; i++)
                yield return new GerichteteKante(z.Polygon.Knoten(i), z.Polygon.Knoten(i + 1), z.Polygon.Linie(i));
        }

        internal static List<Flaeche> Einheitsbelag(Bauergebnis bau)
        {
            var zellen = bau.Zellen.Select(z => Kopie(z, z.Polygon)).ToList();
            foreach (var zelle in zellen)
            {
                if (zelle.Material != Material.Asphalt && zelle.Material != Material.Gruen) continue;
                zelle.Material = Material.Asphalt;
                zelle.Art = Zellart.Restbelag;
                zelle.BuchtId = null;
                zelle.Flaechenabschnitt = null;
                zelle.GemeinsamerBoden = true;
            }
            var vereinigt = Vereinigung.Vereinige(zellen);
            return Lochtrenner.Trenne(zellen, vereinigt.Flaechen, bau.Rahmen).Flaechen;
        }

        private static Zelle Kopie(Zelle z, Polygon polygon) => new Zelle
        {
            Id = z.Id,
            Polygon = polygon,
            Material = z.Material,
            Art = z.Art,
            QuellzelleId = z.QuellzelleId,
            Ursprungsmaterial = z.Ursprungsmaterial,
            Ursprungsart = z.Ursprungsart,
            BuchtId = z.BuchtId,
            QuerstrassenId = z.QuerstrassenId,
            Flaechenabschnitt = z.Flaechenabschnitt,
            GemeinsamerBoden = z.GemeinsamerBoden,
            Zufahrtsdeckungen = z.Zufahrtsdeckungen,
            Zugangsart = z.Zugangsart,
        };
    }
}
