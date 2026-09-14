using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * RANDZONING: BAULAND AN DER RANDSTRASSE, AUSSERHALB DES PARKPLATZES.
     *
     * Die innere Zoningflaeche legt ein Rechteck INS Areal und zieht sich
     * eine eigene Strasse drumherum. Das Randzoning geht den anderen Weg: es
     * nimmt eine Linie des gezeichneten Umrisses, macht die Randstrasse in
     * diesem Abschnitt zur Zoning-Strasse und laesst die Haeuser AUSSERHALB
     * des Polygons wachsen.
     *
     * WARUM NUR NACH AUSSEN. Ansage des Nutzers am 2026-09-03: *"Nur nach
     * aussen, denn der User kann innen ZF nutzen."* Das erspart die ganze
     * Frage, wie sich Randzoning und Zoningflaeche im Inneren vertragen -
     * ihre Parzellen koennen sich schlicht nicht begegnen.
     *
     * WARUM DIE TIEFE NICHT EINSTELLBAR IST. In CS2s `BlockSystem` steht an
     * beiden Stellen, an denen ein Zonenblock entsteht, hartcodiert
     * `block.m_Size.y = 6;`. Die Tiefe kommt also nicht aus dem Prefab und
     * laesst sich nicht setzen. Der Nutzer hatte es vermutet; nachgesehen am
     * 2026-09-03, seine Vermutung stimmte.
     */
    public static partial class ParkingGeometry
    {
        /**
         * Eine Umrisslinie, an der Randzoning entsteht.
         *
         * Zwei Punkte, keine Nummer: die Nummerierung des Umrisses ueberlebt
         * keine Bearbeitung.
         */
        public sealed class RandzoningLinie
        {
            public float2 A { get; set; }
            public float2 B { get; set; }

            public RandzoningLinie Clone() => new RandzoningLinie
            {
                A = A,
                B = B,
            };
        }

        /**
         * Wie nah eine Linie an der gemerkten liegen darf, um noch als
         * dieselbe zu gelten.
         *
         * Der Umriss verschiebt sich beim Bearbeiten um Zentimeter; ein
         * halber Meter faengt das ab, ohne die Nachbarlinie zu treffen -
         * die liegt immer mindestens eine Buchttiefe weiter.
         */
        private const float RandzoningToleranz = 0.5f;

        /** Ist das dieselbe Umrisslinie, auch andersherum gespeichert? */
        public static bool RandzoningSelbeLinie(
            float2 a1, float2 b1, float2 a2, float2 b2)
        {
            var gleich = math.distance(a1, a2) < RandzoningToleranz
                && math.distance(b1, b2) < RandzoningToleranz;
            var gedreht = math.distance(a1, b2) < RandzoningToleranz
                && math.distance(b1, a2) < RandzoningToleranz;
            return gleich || gedreht;
        }

        /**
         * Liegt dieser Punkt im Abschnitt einer Randzoning-Linie?
         *
         * Gemessen wird ENTLANG der Linie, nicht als Abstand zu ihr: die
         * Randstrasse laeuft ein Stueck nach innen versetzt, ihre Punkte
         * liegen also nie auf der Umrisslinie selbst. Wissen wollen wir nur,
         * ob sie in demselben Abschnitt liegen - deshalb der Laengsanteil.
         *
         * `aufschlag` verlaengert den Abschnitt an beiden Enden. Damit laesst
         * sich fragen "gehoert das gerade noch dazu", etwa fuer die
         * Randstrassenstuecke, die einen Tick ueber die Ecke laufen.
         */
        /**
         * Wie weit die Randstrasse von der Umrisslinie entfernt sein darf.
         *
         * Sie laeuft rund zehn Meter nach innen versetzt. Fuenfzehn faengt
         * das samt Reglerspiel ab und schliesst die Fahrgassen aus, die eine
         * Buchtreihe weiter liegen.
         */
        private const float RandzoningQuerabstand = 15f;

        /**
         * Gehoert dieses STRECKENSTUECK zum Abschnitt einer Randzoning-Linie?
         *
         * Gefragt wird nach einem Stueck, nicht nach einem Punkt - und das ist
         * der Kern der Sache: die Randstrasse laeuft um eine Ecke, und die
         * anschliessende Kante kommt der gewaehlten Linie an der Ecke sehr
         * nahe. Ein reiner Punkttest nimmt sie mit; erst die RICHTUNG trennt
         * beide sauber.
         *
         * Drei Bedingungen: parallel zur Linie, laengs innerhalb ihres
         * Abschnitts, und quer nah genug.
         */
        public static bool RandzoningEnthaelt(
            IReadOnlyList<RandzoningLinie> linien, float2 a, float2 b,
            float aufschlag = 0f)
        {
            if (linien == null) return false;
            var eigen = b - a;
            var eigenlaenge = math.length(eigen);
            if (eigenlaenge < 1e-4f) return false;
            var eigenrichtung = eigen / eigenlaenge;
            var mitte = (a + b) * 0.5f;

            for (var i = 0; i < linien.Count; i++)
            {
                if (linien[i] == null) continue;
                var spanne = linien[i].B - linien[i].A;
                var laenge = math.length(spanne);
                if (laenge < 1e-3f) continue;
                var richtung = spanne / laenge;

                // Parallel? Das Kreuzprodukt zweier Einheitsvektoren ist der
                // Sinus des Zwischenwinkels; 0,02 sind gut ein Grad.
                if (math.abs(richtung.x * eigenrichtung.y
                        - richtung.y * eigenrichtung.x) > 0.02f) continue;

                var w = mitte - linien[i].A;
                var laengs = math.dot(w, richtung);
                if (laengs < -aufschlag || laengs > laenge + aufschlag) continue;
                if (math.abs(richtung.x * w.y - richtung.y * w.x)
                    <= RandzoningQuerabstand) return true;
            }
            return false;
        }

        /**
         * GEHOERT DIESE UMRISSKANTE ZU EINER GEWAEHLTEN LINIE?
         *
         * Die genaueste Frage von allen - und sie braucht keinen Abstand.
         * Eine Randzoning-Linie IST eine ganze Umrisskante: `SchalteRandzoning`
         * merkt sich `_points[kante]` und `_points[kante+1]`, nie ein Stueck
         * davon. Wer die Kante selbst in der Hand hat, muss also nicht
         * schaetzen, ob etwas "nah genug" liegt.
         *
         * WARUM DAS HIER STEHT. `RandzoningEnthaelt` fragt nach einem
         * beliebigen Streckenstueck und muss dafuer einen Quergurtel von 15 m
         * ziehen. Bei einer L-Form mit schmalem Arm liegt die
         * GEGENUEBERLIEGENDE Armseite in diesem Guertel - 13,6 m bei 24 m
         * Armbreite und 10,4 m Randstrassentiefe. Sie laeuft zwar
         * entgegengesetzt, aber geprueft wurde der BETRAG des Kreuzprodukts,
         * und der kennt keine Richtung.
         *
         * Befund des Nutzers am 2026-09-09: *"Bei einer L-Form ging eine
         * Seite beim Rand-Toggle nach innen."* Genau diese zweite Kante war
         * es: sie wurde mitgewaehlt und zonte, von der Parkplatzmitte weg
         * gerechnet, in den Parkplatz hinein.
         *
         * Ein Ring aus `Layoutplanung.Innenrand` behaelt die Nummerierung
         * seines Umrisses - Kante i des Rings ist der Versatz von Kante i des
         * Umrisses. Wo diese Nummer zur Hand ist, wird deshalb hier gefragt
         * und nicht dort. Dasselbe Muster steht seit dem 2026-09-04 schon in
         * `Layout.cs` fuer die beiden Materialringe.
         */
        /**
         * LIEGT DIESES STUECK AUF DER GEMERKTEN LINIE?
         *
         * Nicht "ist es dieselbe Kante", sondern "liegt es auf ihr". Der
         * Unterschied kostet eine Zeile und rettet die ganze Auswahl:
         *
         * Der Nutzer setzt einen Punkt MITTEN auf eine Kante, an der
         * Randzoning haengt. Der Umriss hat danach zwei Kanten, wo vorher eine
         * war - die gemerkte Linie ist mit keiner davon mehr identisch. Astra
         * hat am 2026-09-09 gemessen, was dann passiert: von 178,912277 m
         * gebauter Zoning-Strasse bleiben **0 m**. Die Auswahl ist weg, und
         * der Nutzer hat nichts getan ausser einen Punkt zu setzen, der
         * optisch genau auf der Linie liegt.
         *
         * Beide Haelften liegen aber weiterhin auf der gemerkten Linie. Wer
         * danach fragt statt nach Gleichheit, behaelt die vollen 178,9 m.
         *
         * Geprueft wird beides: quer der Lotabstand, laengs die Lage im
         * Abschnitt. Ohne die Laengspruefung gaelte auch die Verlaengerung
         * derselben Geraden weit hinter dem Ende noch als "drauf".
         */
        internal static bool RandzoningStueckAufLinie(
            Zellen.Punkt linieA, Zellen.Punkt linieB,
            Zellen.Punkt a, Zellen.Punkt b)
        {
            var spanne = linieB - linieA;
            var laenge = Zellen.Geometrie.Laenge(spanne);
            if (laenge < 1e-9) return false;

            bool Drauf(Zellen.Punkt p)
            {
                var w = p - linieA;
                var laengs = Zellen.Geometrie.Skalar(w, spanne) / laenge;
                if (laengs < -RandzoningToleranz
                    || laengs > laenge + RandzoningToleranz) return false;
                return Math.Abs(Zellen.Geometrie.Kreuz(spanne, w)) / laenge
                    <= RandzoningToleranz;
            }

            return Drauf(a) && Drauf(b);
        }

        /** Dieselbe Frage fuer Werkzeug und Vorschau, in `float2`. */
        public static bool RandzoningStueckAufLinie(
            float2 linieA, float2 linieB, float2 a, float2 b)
            => RandzoningStueckAufLinie(
                new Zellen.Punkt(linieA.x, linieA.y),
                new Zellen.Punkt(linieB.x, linieB.y),
                new Zellen.Punkt(a.x, a.y),
                new Zellen.Punkt(b.x, b.y));

        internal static bool RandzoningIstKante(
            IReadOnlyList<(Zellen.Punkt A, Zellen.Punkt B)> linien,
            Zellen.Punkt a, Zellen.Punkt b)
        {
            if (linien == null) return false;
            foreach (var linie in linien)
                if (RandzoningStueckAufLinie(linie.A, linie.B, a, b))
                    return true;
            return false;
        }

        /**
         * Dieselbe Frage im Rahmen des Zellenkerns.
         *
         * Der Kern rechnet in `Punkt` und in seinem eigenen, gedrehten
         * Rahmen; die Linien werden dafuer einmal umgerechnet. Die zehn
         * Zeilen stehen deshalb zweimal da - einmal hier fuer den Kern,
         * einmal oben in `float2` fuer Werkzeug und Vorschau. Wer eine
         * aendert, aendert beide.
         */
        internal static bool RandzoningEnthaeltLokal(
            IReadOnlyList<(Zellen.Punkt A, Zellen.Punkt B)> linien,
            Zellen.Punkt a, Zellen.Punkt b, double aufschlag = 0.0)
        {
            if (linien == null) return false;
            var eigen = b - a;
            var eigenlaenge = Zellen.Geometrie.Laenge(eigen);
            if (eigenlaenge < 1e-4) return false;
            var eigenrichtung = eigen * (1.0 / eigenlaenge);
            var mitte = (a + b) * 0.5;

            for (var i = 0; i < linien.Count; i++)
            {
                var spanne = linien[i].B - linien[i].A;
                var laenge = Zellen.Geometrie.Laenge(spanne);
                if (laenge < 1e-3) continue;
                var richtung = spanne * (1.0 / laenge);
                if (Math.Abs(richtung.X * eigenrichtung.Y
                        - richtung.Y * eigenrichtung.X) > 0.02) continue;

                var w = mitte - linien[i].A;
                var laengs = w.X * richtung.X + w.Y * richtung.Y;
                if (laengs < -aufschlag || laengs > laenge + aufschlag) continue;
                if (Math.Abs(w.X * richtung.Y - w.Y * richtung.X)
                    <= RandzoningQuerabstand) return true;
            }
            return false;
        }

        /**
         * Gehoert diese ZELLE zum Randzoning?
         *
         * Eine Zelle hat keine Richtung, der Streckentest hilft hier also
         * nicht. Entschieden wird ueber die NAECHSTGELEGENE Umrisskante: Was
         * im Aussenband liegt, gehoert zu der Kante, an der es liegt - und
         * nur wenn das eine gewaehlte Linie ist, wird daraus Bauland.
         *
         * Das trennt auch an der Ecke sauber. Dort ist eine Zelle zu ZWEI
         * Kanten nah; die naechste ist aber immer nur eine, und mehr braucht
         * die Entscheidung nicht.
         */
        internal static bool RandzoningZelle(
            IReadOnlyList<Zellen.Punkt> umriss,
            IReadOnlyList<(Zellen.Punkt A, Zellen.Punkt B)> linien,
            Zellen.Punkt mitte)
        {
            if (umriss == null || umriss.Count < 3) return false;
            if (linien == null || linien.Count == 0) return false;

            var besterAbstand = double.PositiveInfinity;
            var besteA = default(Zellen.Punkt);
            var besteB = default(Zellen.Punkt);
            for (var i = 0; i < umriss.Count; i++)
            {
                var a = umriss[i];
                var b = umriss[(i + 1) % umriss.Count];
                var abstand = RandzoningPunktStrecke(a, b, mitte);
                if (abstand >= besterAbstand) continue;
                besterAbstand = abstand;
                besteA = a;
                besteB = b;
            }

            foreach (var linie in linien)
            {
                var gleich = Zellen.Geometrie.Laenge(linie.A - besteA) < 0.5
                    && Zellen.Geometrie.Laenge(linie.B - besteB) < 0.5;
                var gedreht = Zellen.Geometrie.Laenge(linie.A - besteB) < 0.5
                    && Zellen.Geometrie.Laenge(linie.B - besteA) < 0.5;
                if (gleich || gedreht) return true;
            }
            return false;
        }

        private static double RandzoningPunktStrecke(
            Zellen.Punkt a, Zellen.Punkt b, Zellen.Punkt p)
        {
            var d = b - a;
            var laenge = Zellen.Geometrie.Laenge(d);
            if (laenge < 1e-9) return Zellen.Geometrie.Laenge(p - a);
            var w = p - a;
            var t = (w.X * d.X + w.Y * d.Y) / (laenge * laenge);
            t = Math.Max(0.0, Math.Min(1.0, t));
            return Zellen.Geometrie.Laenge(p - (a + d * t));
        }

        /**
         * Teilt eine Randstrassenstrecke in die Stuecke IM Randzoning und die
         * ausserhalb.
         *
         * DIE GRENZEN WERDEN GESAMMELT, DANN WIRD ABSCHNITTWEISE
         * ENTSCHIEDEN. Ein Randstrassenstueck kann mehrere Abschnitte
         * streifen; wer nur den Mittelpunkt fragt, verliert den Rest.
         * Dieselbe Rasterlogik wie im Zellenkern: erst teilen, dann
         * einordnen - nie umgekehrt (siehe
         * [[cs2-schwerpunkt-entscheidet-alles]]).
         */
        internal static void RandzoningTeileLokal(
            IReadOnlyList<(Zellen.Punkt A, Zellen.Punkt B)> linien,
            Zellen.Punkt a, Zellen.Punkt b,
            List<(Zellen.Punkt A, Zellen.Punkt B)> drin,
            List<(Zellen.Punkt A, Zellen.Punkt B)> draussen)
        {
            var spanne = b - a;
            var laenge = Zellen.Geometrie.Laenge(spanne);
            if (laenge < 1e-4) return;
            if (linien == null || linien.Count == 0)
            {
                draussen?.Add((a, b));
                return;
            }
            var richtung = spanne * (1.0 / laenge);

            var marken = new List<double> { 0.0, laenge };
            foreach (var linie in linien)
            {
                foreach (var ende in new[] { linie.A, linie.B })
                {
                    var w = ende - a;
                    var t = w.X * richtung.X + w.Y * richtung.Y;
                    if (t > 1e-4 && t < laenge - 1e-4) marken.Add(t);
                }
            }
            marken.Sort();

            for (var i = 0; i + 1 < marken.Count; i++)
            {
                var von = marken[i];
                var bis = marken[i + 1];
                if (bis - von < 1e-4) continue;
                var p1 = a + richtung * von;
                var p2 = a + richtung * bis;
                if (RandzoningEnthaeltLokal(linien, p1, p2)) drin?.Add((p1, p2));
                else draussen?.Add((p1, p2));
            }
        }
    }
}
