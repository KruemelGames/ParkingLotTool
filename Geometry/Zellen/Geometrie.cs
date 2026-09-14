using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal static class Geometrie
    {
        internal static int Mod(int wert, int modul)
        {
            var rest = wert % modul;
            return rest < 0 ? rest + modul : rest;
        }

        internal static double Kreuz(Punkt a, Punkt b) => a.X * b.Y - a.Y * b.X;
        internal static double Skalar(Punkt a, Punkt b) => a.X * b.X + a.Y * b.Y;
        internal static double Laenge(Punkt vektor) => Math.Sqrt(Skalar(vektor, vektor));

        internal static double Vorzeichenflaeche(IEnumerable<Punkt> punkte)
        {
            var ring = punkte.ToArray();
            var summe = 0.0;
            // Ortsnah rechnen: beim Zufahrtsschnitt Kante 0 wurde ein Loch
            // von ca. 1,72e-13 m2 bei X=-1113 sonst exakt 0. Die globale
            // Schuhbaendelsumme verliert das Vorzeichen durch Ausloeschung.
            for (var i = 1; i + 1 < ring.Length; i++)
                summe += Kreuz(ring[i] - ring[0], ring[i + 1] - ring[0]);
            return summe / 2;
        }

        internal static double Flaeche(Polygon polygon) =>
            Math.Abs(Vorzeichenflaeche(polygon.Punkte));

        /**
         * WIE WEIT EIN PUNKT VON DER GERADEN ABLIEGEN DARF, UM AUF IHR ZU
         * LIEGEN.
         *
         * Fuenf Zentimeter Lotabstand zur Sehne. Das ist eine BEDIENGRENZE,
         * keine Rechengroesse: der Nutzer zieht einen Punkt mit der Maus auf
         * eine Linie, und was er dort als "genau drauf" sieht, soll auch so
         * rechnen. Der Fall, der die Frage aufwarf, lag bei 0,47 mm.
         *
         * GEMESSEN WIRD DER LOTABSTAND, NICHT DER WINKEL. Ein Winkelvergleich
         * liesse bei langen Kanten viel groessere Abweichungen durchgehen als
         * bei kurzen, und paarweise verglichen summierten sich viele kleine
         * Knicke zu einem grossen auf. Der Abstand zur GEMEINSAMEN Sehne hat
         * beides nicht.
         */
        internal const double GeradeToleranz = 0.05;

        /**
         * DIE LAENGSTE GERADE DES UMRISSES - nicht die laengste KANTE.
         *
         * Befund des Nutzers am 2026-09-09: *"Ein Polygonpunkt liegt direkt
         * auf einer direktionalen Linie. Der Punkt liegt eigentlich genau so
         * da, dass er nichts aendern sollte - theoretisch. Aber praktisch
         * aendert er ganz viel."* Zwei Vorschau-Berichte, drei Sekunden
         * auseinander, dieselbe Flaeche (29633,2 gegen 29633,3 m2):
         *
         *     6 Punkte   703 Buchten   7 Fahrgassen   Winkel 178,03
         *     7 Punkte   699 Buchten   8 Fahrgassen   Winkel  88,04
         *
         * Der siebte Punkt liegt 0,47 mm neben der Geraden und zerlegt die
         * 199,7-m-Kante in 67,5 + 132,2 m. Damit wird die QUER laufende
         * Schlusskante mit 177,3 m die laengste - und hier stand bis dahin
         * "nimm die laengste gespeicherte Kante". Der ganze Parkplatz drehte
         * sich um 90 Grad.
         *
         * Eine Kantenliste ist aber nicht die Form. Zwei aufeinander folgende
         * Kanten in derselben Richtung sind EINE Linie, und die zaehlt hier.
         * Gesucht wird deshalb der laengste zusammenhaengende Lauf, dessen
         * Zwischenpunkte alle innerhalb von `GeradeToleranz` neben der Sehne
         * liegen und auf ihr VORWAERTS laufen - sonst gaelte auch eine
         * Zickzacklinie zwischen zwei weit entfernten Punkten als gerade.
         *
         * Der Lauf darf ueber den Listenanfang hinweggehen; sonst haenge das
         * Ergebnis daran, wo der Nutzer zu zeichnen begonnen hat. Bei
         * Laengengleichstand gewinnt der kleinere Winkel - damit ist die Wahl
         * unabhaengig von Umlaufsinn und Startpunkt. Astra hat das mit 70
         * Umlaufproben je Lage nachgemessen: 0 Abweichungen, und bei 5,1 cm
         * Lotabstand endet die Zusammenfassung wie gewollt.
         */
        internal static (double Grad, Punkt Richtung, double Laenge)
            LaengsteGerade(IReadOnlyList<Punkt> punkte)
        {
            var besteLaenge = -1.0;
            var besterWinkel = 0.0;
            var richtung = default(Punkt);
            for (var i = 0; i < punkte.Count; i++)
            for (var schritte = 1; schritte < punkte.Count; schritte++)
            {
                var d = punkte[(i + schritte) % punkte.Count] - punkte[i];
                var laenge = Laenge(d);
                if (laenge < 1e-9) continue;

                var gerade = true;
                var vorher = 0.0;
                for (var s = 1; s < schritte; s++)
                {
                    var v = punkte[(i + s) % punkte.Count] - punkte[i];
                    var laengs = Skalar(v, d) / laenge;
                    if (laengs < vorher - 1e-9 || laengs > laenge + 1e-9
                        || Math.Abs(Kreuz(d, v)) / laenge > GeradeToleranz + 1e-9)
                    {
                        gerade = false;
                        break;
                    }
                    vorher = laengs;
                }
                if (!gerade) continue;

                var winkel = (Math.Atan2(d.Y, d.X) * 180 / Math.PI + 180) % 180;
                if (laenge <= besteLaenge + 1e-9
                    && !(Math.Abs(laenge - besteLaenge) <= 1e-9
                        && winkel < besterWinkel)) continue;
                besteLaenge = laenge;
                besterWinkel = winkel;
                richtung = d * (1 / laenge);
            }
            return (besterWinkel, richtung, besteLaenge);
        }

        internal static Rahmen Reihenrahmen(
            IReadOnlyList<Punkt> punkte,
            double? reihenwinkel = null)
        {
            if (reihenwinkel.HasValue)
            {
                var rad = reihenwinkel.Value * Math.PI / 180;
                var x = new Punkt(Math.Cos(rad), Math.Sin(rad));
                return new Rahmen(x, new Punkt(-x.Y, x.X));
            }

            var lauf = LaengsteGerade(punkte);
            if (lauf.Laenge <= 0)
                throw new InvalidOperationException("The site has no edge with any length.");
            /*
             * DIE GEFUNDENE RICHTUNG, NICHT DER WINKEL ZURUECKGERECHNET.
             *
             * Aus dem Winkel eine Achse zu bauen waere naheliegend, dreht aber
             * jede Form, deren laengste Gerade "nach rechts" zeigt, um 180
             * Grad - der Rahmen spiegelt dann, und Baender, Anker und
             * Links/Rechts kippen mit. Wo der Lauf eine einzelne Kante ist -
             * also bei fast jeder Form - kommt so genau dieselbe Achse heraus
             * wie vorher.
             */
            var achse = lauf.Richtung;
            return new Rahmen(achse, new Punkt(-achse.Y, achse.X));
        }

        internal static bool IstReflex(Polygon polygon, int index)
        {
            if (Vorzeichenflaeche(polygon.Punkte) <= 0)
                throw new InvalidOperationException("The reflex check expects a counter-clockwise ring.");
            // Die Zerlegungsnaht ist die verlaengerte ankommende Kante. Am
            // gemeinsamen Endpunkt ist der Winkel deshalb konstruktiv gerade.
            // Bei 15 frei gedrehten L-Formen lag der erneut berechnete Treffer
            // nur 2,98e-15 bis 9,66e-14 m auf der negativen Seite und erfand
            // ohne diese Topologieinformation eine zweite Reflexecke.
            if (polygon.Linie(index - 1).TragendeGeradenId
                == polygon.Linie(index).TragendeGeradenId)
                return false;
            var a = polygon.Knoten(index - 1).Punkt;
            var b = polygon.Knoten(index).Punkt;
            var c = polygon.Knoten(index + 1).Punkt;
            return Kreuz(b - a, c - b) < 0;
        }

        internal static bool IstKonvex(Polygon polygon)
        {
            if (Vorzeichenflaeche(polygon.Punkte) <= 0) return false;
            for (var i = 0; i < polygon.Anzahl; i++)
                if (IstReflex(polygon, i)) return false;
            return true;
        }

        /// <summary>
        /// Gerade-Ungerade-Test ohne Rand-Epsilon. Die Messpunkte liegen in der
        /// Mitte der 0,1-m-Rasterzellen; die fuenf Versuchsformen benoetigen daher
        /// keine Sonderbehandlung fuer beinahe getroffene Kanten.
        /// </summary>
        internal static bool Enthaelt(IReadOnlyList<Punkt> ring, Punkt punkt)
        {
            var innen = false;
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                if ((a.Y > punkt.Y) == (b.Y > punkt.Y)) continue;
                var schnittX = a.X + (punkt.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (punkt.X < schnittX) innen = !innen;
            }
            return innen;
        }

        internal static bool EnthaeltOderRand(IReadOnlyList<Punkt> ring, Punkt punkt)
        {
            for (var i = 0; i < ring.Count; i++)
                if (LiegtAufStrecke(punkt, ring[i], ring[(i + 1) % ring.Count]))
                    return true;
            return Enthaelt(ring, punkt);
        }

        internal static bool LiegtAufStrecke(Punkt punkt, Punkt a, Punkt b)
        {
            if (Kreuz(b - a, punkt - a) != 0) return false;
            return Skalar(punkt - a, punkt - b) <= 0;
        }

        internal static bool Enthaelt(Flaeche flaeche, Punkt punkt)
        {
            if (!Enthaelt(
                    flaeche.Aussenring.Knoten.Select(knoten => knoten.Punkt).ToArray(),
                    punkt))
                return false;
            return flaeche.Loecher.All(loch =>
                !Enthaelt(loch.Knoten.Select(knoten => knoten.Punkt).ToArray(), punkt));
        }

        internal static Punkt Mittelwert(Polygon polygon)
        {
            var x = 0.0;
            var y = 0.0;
            foreach (var punkt in polygon.Punkte)
            {
                x += punkt.X;
                y += punkt.Y;
            }
            return new Punkt(x / polygon.Anzahl, y / polygon.Anzahl);
        }

        internal static Punkt Schwerpunkt(Ring ring)
        {
            var doppelteFlaeche = 0.0;
            var x = 0.0;
            var y = 0.0;
            var punkte = ring.Knoten.Select(knoten => knoten.Punkt).ToArray();
            // Derselbe lokale Ursprung wie bei der Flaeche; sonst scheitert
            // das erkannte 1,72e-13-m2-Loch anschliessend am Schwerpunkt.
            for (var i = 0; i < punkte.Length; i++)
            {
                var a = punkte[i] - punkte[0];
                var b = punkte[(i + 1) % punkte.Length] - punkte[0];
                var kreuz = Kreuz(a, b);
                doppelteFlaeche += kreuz;
                x += (a.X + b.X) * kreuz;
                y += (a.Y + b.Y) * kreuz;
            }
            if (doppelteFlaeche == 0)
                throw new InvalidOperationException("A ring without area has no centroid.");
            return punkte[0] + new Punkt(x / (3 * doppelteFlaeche), y / (3 * doppelteFlaeche));
        }

        internal static double AbstandPunktStrecke(Punkt punkt, Punkt a, Punkt b)
        {
            var kante = b - a;
            var laengenquadrat = Skalar(kante, kante);
            if (laengenquadrat == 0) return Laenge(punkt - a);
            var t = Skalar(punkt - a, kante) / laengenquadrat;
            t = Math.Max(0, Math.Min(1, t));
            return Laenge(punkt - (a + kante * t));
        }
    }
}
