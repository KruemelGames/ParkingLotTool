using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal static class Vereinigung
    {
        private sealed class Kantenfund
        {
            internal Kantenfund(int zelle, GerichteteKante kante)
            {
                Zelle = zelle;
                Kante = kante;
            }

            internal int Zelle { get; }
            internal GerichteteKante Kante { get; }
        }

        /**
         * Welche Zellarten duerfen zu EINER Flaeche verschmelzen?
         *
         * Die Fahrflaechen werden nach ihrer Rolle getrennt: Randstrasse,
         * Fahrgasse, Querstrasse und Buchtstreifen sind eigene Flaechen. Was
         * nur Fuellung ist, bleibt beisammen.
         */
        private static int Rollengruppe(Zellart art)
        {
            switch (art)
            {
                case Zellart.Randstrasse: return 1;
                case Zellart.Fahrgasse: return 2;
                case Zellart.Querstrasse: return 3;
                case Zellart.Bucht: return 4;
                case Zellart.Zufahrt: return 5;
                default: return 0;
            }
        }

        internal static (List<Flaeche> Flaechen, Topologiebericht Topologie) Vereinige(
            IReadOnlyList<Zelle> zellen,
            ISet<KantenSchluessel> gesperrteKanten = null)
        {
            var kantenindex = new Dictionary<KantenSchluessel, List<Kantenfund>>();
            foreach (var zelle in zellen)
            {
                if (Geometrie.Vorzeichenflaeche(zelle.Polygon.Punkte) <= 0)
                    throw new InvalidOperationException($"Cell {zelle.Id} is not counter-clockwise.");
                for (var i = 0; i < zelle.Polygon.Anzahl; i++)
                {
                    var kante = new GerichteteKante(
                        zelle.Polygon.Knoten(i),
                        zelle.Polygon.Knoten(i + 1),
                        zelle.Polygon.Linie(i));
                    List<Kantenfund> funde;
                    if (!kantenindex.TryGetValue(kante.Schluessel, out funde))
                    {
                        funde = new List<Kantenfund>();
                        kantenindex[kante.Schluessel] = funde;
                    }
                    funde.Add(new Kantenfund(zelle.Id, kante));
                }
            }

            var topologie = PruefeTopologie(kantenindex);
            var komponenten = new DisjunkteMenge(zellen.Count);
            foreach (var funde in kantenindex.Values)
            {
                if (funde.Count != 2) continue;
                if (gesperrteKanten != null
                    && gesperrteKanten.Contains(funde[0].Kante.Schluessel))
                    continue;
                var erste = zellen[funde[0].Zelle];
                var zweite = zellen[funde[1].Zelle];
                /**
                 * NACH ROLLE TRENNEN, NICHT NUR NACH MATERIAL.
                 *
                 * Vorher verschmolz alles, was Asphalt hiess und zusammenhing,
                 * zu EINEM Ring. Nutzerbefund am 2026-08-24 an "Gebaut 16":
                 * ein einziger Belagring mit 189 Punkten ueber 11.782 m2 - das
                 * sind 70 % des ganzen Areals, und Randstrasse, Fahrgassen,
                 * Querstrassen und Buchtstreifen stecken alle darin. Irgendwo
                 * ein 0,2-Grad-Zipfel, und CS2 verwirft das Ganze.
                 *
                 * Seine Worte dazu: *"Das schaut nach Puzzeln aus, nicht nach
                 * ordentlichem Verlegen von Flaechen."* Genau so war es - die
                 * Flaechen folgten nicht der Struktur des Parkplatzes, sondern
                 * dem Zufall, wo Zellen zusammenhaengen.
                 *
                 * Jetzt bekommt jede Rolle ihre eigene Flaeche: die Randstrasse
                 * eine, jede Fahrgasse eine, jede Querstrasse eine, und
                 * aneinanderliegende Buchten einer Reihe zusammen eine.
                 * Gemessen ueber alle Formen des Bauprotokolls:
                 *
                 *     verlorene Flaeche  62.044 m2  ->  513 m2
                 *     Stachel                    5  ->    0
                 *     verworfene Ringe          28  ->   21
                 *
                 * Der Preis sind mehr Flaechen (5.055 -> 10.932 Ringe). Das
                 * ist gewollt: sie sind kleiner, einfacher und folgen dem Bau.
                 */
                // Die zwei Umfangsboegen eines geplanten Randbands teilen
                // Material und Rolle, aber nicht dieselbe CS2-Flaeche. Ohne
                // diese vorab gesetzte Grenze wuerden sie wieder zum Ring.
                if (erste.Material == zweite.Material
                    && Rollengruppe(erste.Art) == Rollengruppe(zweite.Art)
                    && erste.Flaechenabschnitt == zweite.Flaechenabschnitt)
                    komponenten.Vereinige(erste.Id, zweite.Id);
            }

            var gruppen = zellen.GroupBy(zelle => komponenten.Wurzel(zelle.Id));
            var flaechen = new List<Flaeche>();
            foreach (var gruppe in gruppen)
            {
                var zellliste = gruppe.ToList();
                var randkanten = Randkanten(zellliste);
                var ringe = ZieheRinge(randkanten);
                var aussenringe = ringe
                    .Where(ring => ring.Vorzeichenflaeche > 0)
                    .ToList();
                var lochringe = ringe
                    .Where(ring => ring.Vorzeichenflaeche < 0)
                    .ToList();
                if (aussenringe.Count == 0
                    || aussenringe.Count + lochringe.Count != ringe.Count)
                    throw new InvalidOperationException(
                        "A material component has no valid outer ring.");

                var erzeugt = aussenringe.Select(aussenring => new Flaeche
                {
                    Id = flaechen.Count,
                    Material = zellliste[0].Material,
                    Aussenring = aussenring,
                    Loecher = new List<Ring>(),
                    ZellIds = zellliste.Select(zelle => zelle.Id).ToList(),
                }).ToList();

                foreach (var loch in lochringe)
                {
                    var pruefpunkt = loch.Kanten[0].Von.Punkt;
                    var traeger = erzeugt.FirstOrDefault(flaeche => Geometrie.Enthaelt(
                        flaeche.Aussenring.Knoten
                            .Select(knoten => knoten.Punkt)
                            .ToArray(),
                        pruefpunkt));
                    if (traeger == null)
                        throw new InvalidOperationException(
                            "A hole lies inside no outer ring.");
                    traeger.Loecher.Add(loch);
                }
                flaechen.AddRange(erzeugt);
            }

            // IDs werden erst nach allen Komponenten gesetzt, weil eine seltene
            // Punktberuehrung mehr als einen Aussenring liefern kann.
            for (var i = 0; i < flaechen.Count; i++)
            {
                var alt = flaechen[i];
                flaechen[i] = new Flaeche
                {
                    Id = i,
                    Material = alt.Material,
                    Aussenring = alt.Aussenring,
                    Loecher = alt.Loecher,
                    ZellIds = alt.ZellIds,
                };
            }
            return (flaechen, topologie);
        }

        private static Topologiebericht PruefeTopologie(
            IReadOnlyDictionary<KantenSchluessel, List<Kantenfund>> kantenindex)
        {
            var bericht = new Topologiebericht();
            foreach (var funde in kantenindex.Values)
            {
                if (funde.Count == 1)
                {
                    if (funde[0].Kante.Linie.Art == Linienart.Aussenkante)
                        bericht.Aussenkanten++;
                    else
                        bericht.UnerwarteteOffeneKanten++;
                    if (funde[0].Kante.Linie.Art == Linienart.Teilungsnaht)
                        bericht.OffeneTeilungsnaehte++;
                    continue;
                }
                if (funde.Count != 2)
                {
                    bericht.NichtMannigfaltigeKanten++;
                    continue;
                }
                bericht.Innenkanten++;
                var a = funde[0].Kante;
                var b = funde[1].Kante;
                if (a.Von.Id != b.Nach.Id || a.Nach.Id != b.Von.Id)
                    bericht.GleichgerichteteDoppelkanten++;
                if (a.Linie.Id != b.Linie.Id) bericht.AbweichendeLinienIds++;
            }
            return bericht;
        }

        private static List<GerichteteKante> Randkanten(IReadOnlyList<Zelle> zellen)
        {
            var offen = new Dictionary<KantenSchluessel, GerichteteKante>();
            foreach (var zelle in zellen)
                for (var i = 0; i < zelle.Polygon.Anzahl; i++)
                {
                    var kante = new GerichteteKante(
                        zelle.Polygon.Knoten(i),
                        zelle.Polygon.Knoten(i + 1),
                        zelle.Polygon.Linie(i));
                    if (!offen.Remove(kante.Schluessel))
                        offen.Add(kante.Schluessel, kante);
                }
            return offen.Values.ToList();
        }

        private static List<Ring> ZieheRinge(IReadOnlyList<GerichteteKante> kanten)
        {
            var ausgehend = new Dictionary<int, List<int>>();
            for (var i = 0; i < kanten.Count; i++)
            {
                List<int> liste;
                if (!ausgehend.TryGetValue(kanten[i].Von.Id, out liste))
                {
                    liste = new List<int>();
                    ausgehend[kanten[i].Von.Id] = liste;
                }
                liste.Add(i);
            }

            var unbenutzt = new HashSet<int>(Enumerable.Range(0, kanten.Count));
            var ringe = new List<Ring>();
            while (unbenutzt.Count != 0)
            {
                var startindex = unbenutzt.First();
                var startknoten = kanten[startindex].Von.Id;
                var ringkanten = new List<GerichteteKante>();
                var index = startindex;
                while (true)
                {
                    if (!unbenutzt.Remove(index))
                        throw new InvalidOperationException(
                            "A boundary edge was visited twice.");
                    var kante = kanten[index];
                    ringkanten.Add(kante);
                    if (kante.Nach.Id == startknoten) break;
                    List<int> kandidaten;
                    if (!ausgehend.TryGetValue(kante.Nach.Id, out kandidaten))
                        throw new InvalidOperationException("Ein Materialrand ist offen.");
                    var naechste = kandidaten.Where(unbenutzt.Contains).ToList();
                    /*
                     * MEHRERE FORTSETZUNGEN SIND EIN KNEIFPUNKT, KEIN FEHLER.
                     *
                     * GEMESSEN an der Referenzform "Referenz 08s": am Knoten
                     * (113,100/92,269) laufen ZWEI Randkanten weiter, beide
                     * auf derselben Senkrechten x = 113,1 - eine 0,085 m
                     * hinauf, eine 3,31 m hinunter. Der Materialrand beruehrt
                     * sich dort selbst; die Flaeche haengt an diesem einen
                     * Punkt zusammen wie zwei Raeume an einer Tuerschwelle.
                     *
                     * Beide Wege sind gueltig - man muss nur den RICHTIGEN
                     * nehmen. Die Regel dafuer ist die uebliche
                     * Flaechenverfolgung im ebenen Graphen: von der
                     * umgekehrten Ankunftsrichtung aus die naechste Kante im
                     * Uhrzeigersinn. Damit bleibt die Flaeche konsequent auf
                     * derselben Seite, und die beiden Ringe trennen sich
                     * sauber, statt sich zu verhaken.
                     *
                     * Bei genau EINER Fortsetzung liefert dieselbe Regel
                     * genau diese Kante - an allen bisher gebauten Formen
                     * aendert sich also nichts.
                     */
                    if (naechste.Count > 1)
                    {
                        var an = kante.Nach.Punkt - kante.Von.Punkt;
                        var zurueck = Math.Atan2(-an.Y, -an.X);
                        var bester = -1;
                        var bestesDelta = double.MaxValue;
                        foreach (var k in naechste)
                        {
                            var ab = kanten[k].Nach.Punkt - kanten[k].Von.Punkt;
                            // Von der Rueckrichtung im Uhrzeigersinn messen:
                            // die kleinste positive Drehung gewinnt.
                            var delta = zurueck - Math.Atan2(ab.Y, ab.X);
                            while (delta <= 0) delta += 2 * Math.PI;
                            while (delta > 2 * Math.PI) delta -= 2 * Math.PI;
                            if (delta >= bestesDelta) continue;
                            bestesDelta = delta;
                            bester = k;
                        }
                        if (bester >= 0)
                        {
                            index = bester;
                            continue;
                        }
                    }

                    if (naechste.Count != 1)
                    {
                        /*
                         * DIE MELDUNG MUSS DEN ORT NENNEN, NICHT NUR DIE ZAHL.
                         *
                         * "Materialrand an Knoten 228: 2 Fortsetzungen" sagt
                         * nicht, WO das ist und welche Kanten sich dort
                         * streiten - man kann damit nur raten. Ein T-Stoss
                         * entsteht, wenn eine Nachbarflaeche an dieser Stelle
                         * einen Knoten hat und die andere nicht; sichtbar wird
                         * das erst an den Koordinaten und den Richtungen.
                         */
                        var stelle = kante.Nach.Punkt;
                        var wege = string.Join(" | ", naechste.Select(k =>
                            $"-> ({kanten[k].Nach.Punkt.X:F3}/"
                            + $"{kanten[k].Nach.Punkt.Y:F3})"));
                        throw new InvalidOperationException(
                            $"Materialrand an Knoten {kante.Nach.Id}: "
                            + $"{naechste.Count} Fortsetzungen. Stelle "
                            + $"({stelle.X:F3}/{stelle.Y:F3}), ankommend von "
                            + $"({kante.Von.Punkt.X:F3}/{kante.Von.Punkt.Y:F3}), "
                            + $"weiter {wege}");
                    }
                    index = naechste[0];
                }
                ringe.Add(new Ring
                {
                    Kanten = EntferneLiniengleicheZwischenknoten(ringkanten),
                    Rohkanten = ringkanten,
                });
            }
            return ringe;
        }

        private static List<GerichteteKante> EntferneLiniengleicheZwischenknoten(
            IReadOnlyList<GerichteteKante> ring)
        {
            var knoten = ring.Select(kante => kante.Von).ToList();
            var linien = ring.Select(kante => kante.Linie).ToList();
            var geaendert = true;
            while (geaendert && knoten.Count >= 3)
            {
                geaendert = false;
                for (var i = 0; i < knoten.Count; i++)
                {
                    var vorher = Geometrie.Mod(i - 1, knoten.Count);
                    if (linien[vorher].Id != linien[i].Id) continue;
                    knoten.RemoveAt(i);
                    linien.RemoveAt(i);
                    geaendert = true;
                    break;
                }
            }
            if (knoten.Count < 3)
                throw new InvalidOperationException(
                    "The combinatorial simplification destroyed a ring.");
            var ausgabe = new List<GerichteteKante>();
            for (var i = 0; i < knoten.Count; i++)
                ausgabe.Add(new GerichteteKante(
                    knoten[i], knoten[(i + 1) % knoten.Count], linien[i]));
            return ausgabe;
        }

        private sealed class DisjunkteMenge
        {
            private readonly int[] _eltern;
            private readonly byte[] _rang;

            internal DisjunkteMenge(int anzahl)
            {
                _eltern = Enumerable.Range(0, anzahl).ToArray();
                _rang = new byte[anzahl];
            }

            internal int Wurzel(int wert)
            {
                if (_eltern[wert] != wert) _eltern[wert] = Wurzel(_eltern[wert]);
                return _eltern[wert];
            }

            internal void Vereinige(int a, int b)
            {
                var wurzelA = Wurzel(a);
                var wurzelB = Wurzel(b);
                if (wurzelA == wurzelB) return;
                if (_rang[wurzelA] < _rang[wurzelB])
                    _eltern[wurzelA] = wurzelB;
                else if (_rang[wurzelA] > _rang[wurzelB])
                    _eltern[wurzelB] = wurzelA;
                else
                {
                    _eltern[wurzelB] = wurzelA;
                    _rang[wurzelA]++;
                }
            }
        }
    }
}
