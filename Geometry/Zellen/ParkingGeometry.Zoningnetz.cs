using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * PLANT DIE ZONING-STRASSEN FUER ALLE FLAECHEN ZUSAMMEN.
     *
     * Bis zum 2026-09-03 bekam jede Flaeche ihren eigenen Ring, ohne von den
     * Nachbarn zu wissen. Zwei Flaechen mit einer Kachel Abstand ergaben dort
     * zwangslaeufig ZWEI Strassen nebeneinander - nicht aus Absicht, sondern
     * weil keine Flaeche die andere sah.
     *
     * Der Nutzer setzt Flaechen aber als Bausteine nebeneinander, um daraus
     * groessere Formen zu bauen: *"Ich will dem User Snapping erlauben, damit
     * er verschieden grosse und unterschiedliche Formen zeichnen kann, die
     * nebeneinander liegen. Denn ein L waere in unserer derzeitigen
     * Konfiguration nicht moeglich."* Und dazu: *"Wenn ein Tile dazwischen
     * Platz ist, kann EINE Strasse hin, nicht zwei."*
     *
     * Deshalb entscheidet nicht mehr die einzelne Flaeche, wo eine Strasse
     * hinkommt, sondern die Gruppe.
     */
    public static partial class ParkingGeometry
    {
        /**
         * Vier Schritte, und ihre Reihenfolge ist der Sinn der Sache.
         *
         *  1. Je Flaeche die vier Achsen ihres Rings sammeln.
         *  2. Wegnehmen, was im Parzellenrechteck einer ANDEREN Flaeche
         *     liegt - dort ist kein Platz fuer eine Strasse. Das ist
         *     dieselbe Regel wie am Parkplatzrand: kein Platz, keine
         *     Strasse.
         *  3. Deckungsgleiche Stuecke verschmelzen. Bei genau einer Kachel
         *     Abstand fallen die Achsen zweier Nachbarn VON SELBST
         *     aufeinander - beide liegen 4 m vor ihrer Parzellenkante, und
         *     4 + 4 ist die Kachel. Aus zwei Strassen wird so eine.
         *  4. Am Randgruen kappen, ganz oder gar nicht, und ein Ende um eine
         *     Kachel einziehen, wenn dort keine andere Strasse weitergeht.
         */
        private static void ZoningStrassennetz(List<ZellenStrasse> ausgabe,
            IReadOnlyList<Zoningvorgabe> bauland,
            IReadOnlyList<Punkt> randgruen,
            IReadOnlyList<(Punkt A, Punkt B)> randzoningAchsen = null)
        {
            var kandidaten = new List<(Punkt A, Punkt B)>();
            foreach (var flaeche in bauland)
            {
                var ecken = ZoningRingMitRandachse(flaeche, randzoningAchsen);
                for (var k = 0; k < 4; k++)
                {
                    var a = ecken[k];
                    var b = ecken[(k + 1) % 4];
                    if (Geometrie.Laenge(b - a) < 1e-6) continue;

                    var stuecke = new List<(Punkt A, Punkt B)> { (a, b) };
                    foreach (var andere in bauland)
                    {
                        if (ReferenceEquals(andere, flaeche)) continue;
                        /*
                         * WEGGENOMMEN WIRD BIS ZUR ACHSE DER NACHBARIN,
                         * NICHT BIS ZU IHREN PARZELLEN.
                         *
                         * Hier stand `EckenMitAufschlag(0.0)`, also das
                         * gezogene Rechteck. Damit endete die Strasse vier
                         * Meter zu frueh - naemlich an der Parzellenkante der
                         * Nachbarin statt an deren Strassenachse, die vier
                         * Meter davor liegt.
                         *
                         * An einer AUSSENECKE faellt das nicht auf. An einer
                         * INNENECKE - wenn also eine Flaeche buendig an der
                         * Seite einer anderen sitzt - laufen die beiden
                         * Strassen dadurch vier Meter aneinander VORBEI,
                         * statt sich zu treffen. Und CS2 verbindet nur, was
                         * denselben Punkt hat.
                         *
                         * Gemessen im Fall des Nutzers (Protokoll
                         * PLT-E24B7149, 6x6 mit buendiger 7x2): vier Stuecke,
                         * zwei getrennte Strassenzuege, freie Enden bei
                         * (-1149,5/20,2) und (-1096,9/34,4). Im Spiel riss
                         * dort die Wasser- und Abwasserleitung ab.
                         *
                         * Mit der halben Strassenbreite treffen sich die
                         * beiden Achsen auf den Millimeter genau, und aus
                         * zwei Zuegen wird einer.
                         */
                        stuecke = ZoningOhneRechteck(stuecke,
                            andere.EckenMitAufschlag(
                                Zoningvorgabe.Strassenhalbbreite));
                        if (stuecke.Count == 0) break;
                    }
                    kandidaten.AddRange(stuecke);
                }
            }

            /*
             * WO SCHON EINE RZ-STRASSE LIEGT, BRAUCHT DIE FLAECHE KEINE.
             *
             * Ansage des Nutzers: *"Da beim Bauen die Strasse der ZF, die an
             * der RZ-Strasse liegt, nicht gebaut wird, sollte sie auch in der
             * Preview verschwinden."* Genau das - und zwar an beiden Stellen
             * mit derselben Zeile, weil Vorschau und Bau denselben Planer
             * benutzen.
             *
             * Die beiden liegen ohnehin auf derselben Achse: eine Flaeche
             * darf bis an die Innenkante der RZ-Strasse, ihr Ring laege dann
             * exakt auf deren Mittellinie. Zwei Strassen an einer Stelle
             * waeren nicht nur doppelt, sie kaempfen im Spiel auch um
             * dieselben Knoten.
             */
            if (randzoningAchsen != null && randzoningAchsen.Count > 0)
            {
                var ohneRz = new List<(Punkt A, Punkt B)>();
                foreach (var stueck in kandidaten)
                {
                    var bleibt = true;
                    foreach (var achse in randzoningAchsen)
                    {
                        if (!ZoningDeckungsgleich(stueck, achse)) continue;
                        bleibt = false;
                        break;
                    }
                    if (bleibt) ohneRz.Add(stueck);
                }
                kandidaten = ohneRz;
            }

            var verschmolzen = ZoningVerschmelzeKollineare(kandidaten);

            // Ganz oder gar nicht am Randgruen - eine halb gekuerzte Kante
            // verschoebe das Kachelraster um eine halbe Kachel.
            //
            // OHNE RANDGRUEN WIRD NICHT GEKAPPT. Die Vorschau ruft diesen
            // Plan waehrend des Ziehens auf, und dort ist der Gruenrand noch
            // gar nicht gerechnet - er entsteht erst im Hintergrundlauf.
            // Nichts zu kappen ist dann richtiger als alles zu verwerfen.
            var drin = new List<(Punkt A, Punkt B)>();
            foreach (var stueck in verschmolzen)
            {
                var laenge = Geometrie.Laenge(stueck.B - stueck.A);
                if (laenge < 1e-6) continue;
                if (randgruen == null || randgruen.Count < 3)
                {
                    drin.Add(stueck);
                    continue;
                }
                var innen = 0.0;
                foreach (var teil in ZoningInnenstuecke(
                             randgruen, stueck.A, stueck.B))
                    innen += Geometrie.Laenge(teil.B - teil.A);
                if (innen * 2 >= laenge) drin.Add(stueck);
            }

            /*
             * Ein Ende, an dem keine andere Strasse weitergeht, wird um eine
             * Kachel eingezogen.
             *
             * CS2 haengt an jedes Kantenende 4 m an und rechnet daraus die
             * Kachelzahl. Trifft dort eine Querstrasse, nimmt sie eine Kachel
             * weg - das ist eingeplant. Fehlt sie, bliebe eine Kachel zu viel,
             * und das ganze Raster saesse eine halbe Kachel daneben. Genau
             * diesen Spalt hat der Nutzer am 2026-09-03 im Bild gezeigt.
             */
            var kachel = 2 * Zoningvorgabe.Strassenhalbbreite;
            for (var i = 0; i < drin.Count; i++)
            {
                var a = drin[i].A;
                var b = drin[i].B;
                var laenge = Geometrie.Laenge(b - a);
                if (laenge < 1e-6) continue;
                var richtung = (b - a) * (1.0 / laenge);

                /*
                 * EIN STUECK, DAS DABEI AUFGEBRAUCHT WIRD, FAELLT WEG.
                 *
                 * Hier wurden vorher einfach beide Enden verschoben. Bei
                 * einem Stueck, das kuerzer ist als das, was abgeht, drehte
                 * es dabei um: aus 4 m Rest wurde ein 4-m-Stummel, der in
                 * die Gegenrichtung ueber die Ecke hinausragte. Die
                 * Laengenpruefung danach fing das nicht, weil die Laenge ja
                 * wieder positiv war.
                 *
                 * Zu sehen war das genau dort, wo zwei Zoningflaechen sich
                 * beruehren: von der Strasse dazwischen bleiben an beiden
                 * Ecken 4-m-Reste stehen, und aus denen wurden die
                 * Stummel. Der Nutzer: *"Wenn ich 2 ZF aneinander lege,
                 * dann wird die Zoning-Strasse noch angezeigt in der
                 * Preview. Das darf aber nicht, weil beim Bau keine
                 * hinkommt."*
                 *
                 * Jetzt wird in Laengen gerechnet und geprueft, ob ueberhaupt
                 * etwas uebrig bleibt.
                 */
                /*
                 * DIE RZ-STRASSE ZAEHLT ALS GEGENUEBER - auch wenn sie hier
                 * nicht mehr in der Liste steht.
                 *
                 * Ein freies Ende wird um eine Kachel eingekuerzt, damit kein
                 * Stummel ins Leere ragt. `drin` enthaelt aber nur die
                 * ZF-Stuecke: die RZ-Achsen sind weiter oben absichtlich
                 * herausgenommen worden, weil die RZ-Strasse getrennt gebaut
                 * wird. Ein Stueck, das GENAU auf ihr endet, galt damit als
                 * frei - und wurde 8 m vor ihr abgeschnitten.
                 *
                 * GEMESSEN am 2026-09-04 im Bau des Nutzers um 17:46: ein
                 * freies Ende bei (-1095,6/-42,5) lag 7,95 m neben der
                 * RZ-Achse, seine Laengsposition mitten auf deren 140-m-Lauf.
                 * Also keine T-Kreuzung, sondern eine Luecke von genau einer
                 * Kachel. Der Mod hat es selbst gemeldet: *"5 Stueck(e)
                 * zerfallen in 2 Strassenzuege - dort reissen Wasser,
                 * Abwasser und Strom ab."* Der Nutzer sah es als fehlende
                 * Verbindung an der T-Kreuzung.
                 */
                var von = ZoningTrifftAnderes(drin, i, a, randzoningAchsen)
                    ? 0.0 : kachel;
                var bis = ZoningTrifftAnderes(drin, i, b, randzoningAchsen)
                    ? laenge : laenge - kachel;
                if (bis - von < 1e-6) continue;

                var anfang = a + richtung * von;
                var ende = a + richtung * bis;
                ZoningZurRandzoningstrasse(
                    randzoningAchsen, ref anfang, ref ende);
                ausgabe.Add(new ZellenStrasse
                {
                    Kind = "zoning",
                    A = anfang,
                    B = ende,
                });
            }
        }

        /**
         * ZIEHT EIN ENDE BIS AUF DIE ACHSE DER RANDZONING-STRASSE.
         *
         * Der Nutzer am 2026-09-04: *"An der T-Kreuzung, also ZF-Strasse
         * trifft auf RZ-Strasse, wird Strom sowie Wasser/Abwasser nicht
         * verbunden."*
         *
         * GEMESSEN in seinem Bau um 17:46: das freie Ende lag 7,95 m neben
         * der RZ-Achse, seine Laengsposition mitten auf deren 140-m-Lauf.
         * Der Mod hat es selbst gemeldet - *"5 Stueck(e) zerfallen in 2
         * Strassenzuege - dort reissen Wasser, Abwasser und Strom ab."*
         *
         * Die 8 m sind KEIN Rechenfehler, sondern Bauart: die 1-Kachel-Regel
         * haelt die Zoningflaeche eine Kachel von der Fahrbahnkante der
         * RZ-Strasse weg, damit sich ihre Parzellen nicht mit deren Bauland
         * schlagen. Ihr Korridorring endet damit zwangslaeufig eine Kachel
         * vor der RZ-Achse.
         *
         * Fuer die PARZELLEN ist dieser Abstand richtig. Fuer die STRASSE ist
         * er es nicht: eine Strasse, die acht Meter vor der naechsten
         * aufhoert, ist keine Kreuzung, sondern eine Sackgasse - und CS2
         * fuehrt Strom, Wasser und Abwasser nur durch verbundene Strassen.
         *
         * Deshalb wird genau das Ende, das auf die RZ-Strasse ZEIGT, bis auf
         * deren Mittellinie verlaengert. Nicht das gegenueberliegende, nicht
         * ein parallel danebenlaufendes Stueck: verlangt werden ein Fusspunkt
         * INNERHALB der Achse und ein Winkel, der wirklich hinfuehrt.
         */
        private static void ZoningZurRandzoningstrasse(
            IReadOnlyList<(Punkt A, Punkt B)> randzoningAchsen,
            ref Punkt anfang, ref Punkt ende)
        {
            if (randzoningAchsen == null || randzoningAchsen.Count == 0) return;
            // Anderthalb Kacheln: die Bauart gibt genau eine vor, der Rest ist
            // Luft fuer gerundete Ecken. Weiter darf nichts gezogen werden,
            // sonst wandert ein Stueck quer durch den Parkplatz.
            const double reichweite = 12.0;

            Punkt Gezogen(Punkt punkt, Punkt gegenueber)
            {
                var laengsrichtung = punkt - gegenueber;
                var laenge = Geometrie.Laenge(laengsrichtung);
                if (laenge < 1e-6) return punkt;
                laengsrichtung = laengsrichtung * (1.0 / laenge);
                foreach (var achse in randzoningAchsen)
                {
                    var d = achse.B - achse.A;
                    var achsenlaenge = Geometrie.Laenge(d);
                    if (achsenlaenge < 1e-6) continue;
                    var r = d * (1.0 / achsenlaenge);
                    var n = new Punkt(-r.Y, r.X);
                    var abstand = Geometrie.Skalar(punkt - achse.A, n);
                    if (Math.Abs(abstand) < 1e-3
                        || Math.Abs(abstand) > reichweite) continue;
                    // Fusspunkt muss auf dem Stueck liegen, nicht daneben.
                    var laengs = Geometrie.Skalar(punkt - achse.A, r);
                    if (laengs < 0 || laengs > achsenlaenge) continue;
                    // Und das Stueck muss wirklich hinfuehren: laeuft es
                    // parallel zur Achse, gibt es keinen Schnittpunkt.
                    var nenner = Geometrie.Skalar(laengsrichtung, n);
                    if (Math.Abs(nenner) < 1e-6) continue;
                    var schritt = -abstand / nenner;
                    if (schritt <= 0 || schritt > reichweite) continue;
                    return punkt + laengsrichtung * schritt;
                }
                return punkt;
            }

            var neuAnfang = Gezogen(anfang, ende);
            var neuEnde = Gezogen(ende, anfang);
            anfang = neuAnfang;
            ende = neuEnde;
        }

        /**
         * DERSELBE PLAN, ABER SOFORT - fuer die Vorschau.
         *
         * Der gebaute Plan entsteht im Hintergrundlauf, der den ganzen
         * Parkplatz durchrechnet. Waehrend der Nutzer eine Flaeche zieht
         * oder schiebt, ist dessen Ergebnis vom LETZTEN Stand: die Strassen
         * stehen noch dort, wo die Flaeche vorher war. Genau das hat der
         * Nutzer am 2026-09-03 gesehen - *"die inneren Tiles verschwinden
         * nach aussen"* - und beim Aufziehen einer neuen Flaeche gibt es
         * ueberhaupt noch keinen Lauf, also auch keine Kacheln.
         *
         * Diese Fassung rechnet nur das Strassennetz, in Weltkoordinaten,
         * ohne Buchten und ohne Flaechen. Das kostet nichts und ist immer
         * auf dem Stand des Zeigers.
         *
         * `umriss` darf leer sein - dann wird nicht am Gruenrand gekappt.
         */
        public static List<(float2 A, float2 B)> ZoningStrassenVorschau(
            IReadOnlyList<Zoningflaeche> flaechen,
            IReadOnlyList<float2> umriss, double randgruentiefe,
            IReadOnlyList<(float2 A, float2 B)> randzoningAchsen = null)
        {
            if (flaechen == null || flaechen.Count == 0) return null;

            var vorgaben = new List<Zoningvorgabe>(flaechen.Count);
            foreach (var f in flaechen)
            {
                if (f == null || f.Spalten <= 0 || f.Reihen <= 0) continue;
                vorgaben.Add(new Zoningvorgabe
                {
                    Ecke = new Punkt(f.Ecke.x, f.Ecke.y),
                    Spalten = f.Spalten,
                    Reihen = f.Reihen,
                    Winkel = f.Winkel,
                    Rand = f.Rand,
                });
            }
            if (vorgaben.Count == 0) return null;

            IReadOnlyList<Punkt> gruenrand = null;
            if (umriss != null && umriss.Count >= 3)
            {
                var aussen = new List<Punkt>(umriss.Count);
                foreach (var p in umriss) aussen.Add(new Punkt(p.x, p.y));

                /*
                 * DER UMLAUFSINN MUSS STIMMEN, SONST VERSETZT `Innenrand`
                 * NACH AUSSEN.
                 *
                 * Die Funktion schiebt jede Kante entlang ihrer LINKEN
                 * Normalen. Bei umgekehrtem Umlaufsinn zeigt die nach
                 * draussen - der "Innenrand" wird dann groesser als das
                 * Polygon, und gekappt wird gar nichts mehr.
                 *
                 * Der Baulauf dreht deshalb zuerst um (`Layout.cs`, direkt
                 * vor dem Rahmen). Diese Zeile hat der Vorschau gefehlt, und
                 * damit erklaert sich der Befund des Nutzers: *"Es werden
                 * weiterhin alle Zoning-Strassen in der Preview angezeigt,
                 * auch wenn sie durch Ecke oder Rand gar nicht gebaut
                 * werden."* Ob es auffiel, hing allein daran, in welche
                 * Richtung er das Polygon gezogen hatte.
                 */
                if (Geometrie.Vorzeichenflaeche(aussen) < 0) aussen.Reverse();

                try
                {
                    gruenrand = randgruentiefe > 0
                        ? Layoutplanung.Innenrand(aussen, randgruentiefe)
                        : aussen;
                }
                catch (Exception)
                {
                    // Ein entarteter Umriss (Kante der Laenge null) darf die
                    // Vorschau nicht abschiessen - dann eben ungekappt.
                    gruenrand = null;
                }
            }

            // Dieselben Achsen wie beim Bau - sonst zeigte die Vorschau eine
            // Strasse, die spaeter nicht gebaut wird.
            var achsen = new List<(Punkt A, Punkt B)>();
            if (randzoningAchsen != null)
                foreach (var achse in randzoningAchsen)
                    achsen.Add((new Punkt(achse.A.x, achse.A.y),
                        new Punkt(achse.B.x, achse.B.y)));

            var roh = new List<ZellenStrasse>();
            ZoningStrassennetz(roh, vorgaben, gruenrand, achsen);
            if (roh.Count == 0) return null;

            var ergebnis = new List<(float2, float2)>(roh.Count);
            foreach (var strasse in roh)
                ergebnis.Add((
                    new float2((float)strasse.A.X, (float)strasse.A.Y),
                    new float2((float)strasse.B.X, (float)strasse.B.Y)));
            return ergebnis;
        }

        /**
         * Trifft dieser Punkt ein anderes Stueck - irgendwo?
         *
         * FRUEHER WURDEN NUR DIE ENDPUNKTE GEFRAGT. Das reicht fuer eine
         * einzelne Flaeche, deren vier Ringseiten sich an den Ecken treffen.
         * Sobald zwei verschieden grosse Flaechen nebeneinander liegen,
         * endet aber eine Strasse MITTEN auf einer anderen - eine
         * T-Einmuendung. Deren Endpunkt lag auf keinem fremden Endpunkt,
         * galt damit als freies Ende und wurde um eine Kachel eingezogen.
         *
         * Die Folge war im Spiel zu sehen: der Nutzer hat am 2026-09-03 die
         * Wasser- und Abwasserleitungen aufgerufen und mitten im Parkplatz
         * eine Luecke gefunden. Rohre laufen in CS2 in den Strassen - wo die
         * Strasse fehlt, fehlt das Rohr, und der Parkplatz ist geteilt.
         *
         * Fuer den Zweck der Einkuerzung ist der Unterschied ohnehin
         * keiner: eingezogen wird, weil an einem freien Ende sonst eine
         * Kachel zu viel entstuende. Eine Querstrasse nimmt diese Kachel -
         * und ob sie dort ENDET oder DURCHLAEUFT, aendert daran nichts.
         */
        /**
         * DIE RANDSTRASSE IM RANDZONING-ABSCHNITT - fuer die Vorschau.
         *
         * Der Nutzer hat den Fehler in einem Satz erklaert: *"Durch das
         * Userfeedback beim Erstellen von RZ entfaellt die Randstrasse
         * rechnerisch, und dadurch ist die 1-Tile-Regel nicht moeglich. Das
         * ist, als willst du X + Y rechnen, aber entfernst Y."*
         *
         * Genau so war es: sobald Randzoning gesetzt ist, ist die Randstrasse
         * dort keine Randstrasse mehr - und als Zoning-Strasse zeichnete die
         * Vorschau sie nicht, weil dort nur die Ringe der GEZOGENEN Flaechen
         * geplant werden. In der Vorschau klaffte eine Luecke, an der sich
         * nichts mehr ausrichten liess.
         *
         * Sie kommt hier zurueck. Ihre LAGE aendert sich beim Umwidmen
         * ueberhaupt nicht - sie liegt weiter auf der Randstrassenachse, also
         * `randstrassentiefe` innerhalb des Umrisses. Nur ihre Sorte ist eine
         * andere.
         */
        public static List<(float2 A, float2 B)> RandzoningStrassen(
            IReadOnlyList<RandzoningLinie> randzoning,
            IReadOnlyList<float2> umriss, double randstrassentiefe)
        {
            if (randzoning == null || randzoning.Count == 0) return null;
            if (umriss == null || umriss.Count < 3) return null;

            var aussen = new List<Punkt>(umriss.Count);
            foreach (var p in umriss) aussen.Add(new Punkt(p.x, p.y));
            // Derselbe Umlaufsinn wie beim Bau - sonst versetzt `Innenrand`
            // nach aussen, siehe `ZoningStrassenVorschau`.
            if (Geometrie.Vorzeichenflaeche(aussen) < 0) aussen.Reverse();

            IReadOnlyList<Punkt> achse;
            try { achse = Layoutplanung.Innenrand(aussen, randstrassentiefe); }
            catch (Exception) { return null; }
            if (achse == null || achse.Count < 3) return null;

            /*
             * DIE GEMERKTEN LINIEN IM RAHMEN DES RINGS.
             *
             * Verglichen wird gleich Kante gegen Kante, und dafuer muessen
             * beide dieselbe Sorte Punkt sein.
             */
            var gewaehlt = new List<(Punkt A, Punkt B)>(randzoning.Count);
            foreach (var linie in randzoning)
                if (linie != null)
                    gewaehlt.Add((new Punkt(linie.A.x, linie.A.y),
                        new Punkt(linie.B.x, linie.B.y)));

            var ergebnis = new List<(float2, float2)>();
            for (var i = 0; i < achse.Count; i++)
            {
                var a = new float2((float)achse[i].X, (float)achse[i].Y);
                var b = new float2(
                    (float)achse[(i + 1) % achse.Count].X,
                    (float)achse[(i + 1) % achse.Count].Y);
                if (math.distance(a, b) < 0.01f) continue;
                /*
                 * NUR DIE STUECKE IM GEWAEHLTEN ABSCHNITT - und zwar ueber
                 * die NUMMER, nicht ueber den Abstand.
                 *
                 * `Innenrand` behaelt die Nummerierung: Kante i der Achse ist
                 * der Versatz von Kante i des Umrisses. Vorher stand hier ein
                 * Abstandstest, der bei einer L-Form mit schmalem Arm auch die
                 * gegenueberliegende Armseite mitnahm - siehe
                 * `RandzoningIstKante`.
                 */
                if (achse.Count != aussen.Count) continue;
                if (!RandzoningIstKante(gewaehlt, aussen[i],
                        aussen[(i + 1) % aussen.Count])) continue;
                ergebnis.Add((a, b));
            }
            return ergebnis.Count == 0 ? null : ergebnis;
        }

        private static bool ZoningTrifftAnderes(
            IReadOnlyList<(Punkt A, Punkt B)> stuecke, int ausser, Punkt punkt,
            IReadOnlyList<(Punkt A, Punkt B)> randzoningAchsen = null)
        {
            for (var i = 0; i < stuecke.Count; i++)
            {
                if (i == ausser) continue;
                if (ZoningNahBei(stuecke[i].A, punkt)) return true;
                if (ZoningNahBei(stuecke[i].B, punkt)) return true;
                if (ZoningPunktAufStueck(stuecke[i].A, stuecke[i].B, punkt))
                    return true;
            }
            if (randzoningAchsen == null) return false;
            foreach (var achse in randzoningAchsen)
            {
                if (ZoningNahBei(achse.A, punkt)) return true;
                if (ZoningNahBei(achse.B, punkt)) return true;
                if (ZoningPunktAufStueck(achse.A, achse.B, punkt)) return true;
            }
            return false;
        }

        /** Liegt der Punkt auf der Strecke - Enden eingeschlossen? */
        private static bool ZoningPunktAufStueck(Punkt a, Punkt b, Punkt punkt)
        {
            // Ein Millimeter, dieselbe Schwelle wie `ZoningNahBei`. Die
            // Achsen kommen aus gerasterten Rechtecken; wer hier grosszuegig
            // waere, verbaende Strassen, die sich nur fast beruehren.
            const double toleranz = 1e-3;
            var d = b - a;
            var laenge = Geometrie.Laenge(d);
            if (laenge < toleranz) return false;
            var richtung = d * (1.0 / laenge);
            var w = punkt - a;
            var laengs = w.X * richtung.X + w.Y * richtung.Y;
            if (laengs < -toleranz || laengs > laenge + toleranz) return false;
            var quer = Math.Abs(w.X * richtung.Y - w.Y * richtung.X);
            return quer < toleranz;
        }

        /**
         * Die Teile der Strecken, die AUSSERHALB des Rechtecks liegen.
         *
         * Gerechnet im Rahmen des Rechtecks: zwei Skalarprodukte, damit der
         * Umlaufsinn keine Rolle spielt und gedrehte Flaechen genauso
         * funktionieren wie achsenparallele.
         */
        private static List<(Punkt A, Punkt B)> ZoningOhneRechteck(
            IReadOnlyList<(Punkt A, Punkt B)> stuecke, Punkt[] rechteck)
        {
            var ergebnis = new List<(Punkt A, Punkt B)>();
            foreach (var stueck in stuecke)
            {
                var d = stueck.B - stueck.A;
                var innen = new Bereich(0, 1);
                for (var k = 0; k < 2; k++)
                {
                    var achse = rechteck[k + 1] - rechteck[k];
                    var seitenlaenge = Geometrie.Laenge(achse);
                    if (seitenlaenge < 1e-9) { innen = Bereich.Leer; break; }
                    achse = achse * (1.0 / seitenlaenge);
                    var start = stueck.A - rechteck[k];
                    var richtung = d.X * achse.X + d.Y * achse.Y;
                    var versatz = start.X * achse.X + start.Y * achse.Y;
                    if (Math.Abs(richtung) < 1e-9)
                    {
                        // Parallel zum Streifen: entweder ganz drin oder ganz
                        // draussen. Beruehren zaehlt als draussen - sonst
                        // faellt eine Strasse weg, die genau an der Kante
                        // liegt, und das ist der Normalfall.
                        if (versatz < 1e-3 || versatz > seitenlaenge - 1e-3)
                        {
                            innen = Bereich.Leer;
                            break;
                        }
                        continue;
                    }
                    var t1 = (0 - versatz) / richtung;
                    var t2 = (seitenlaenge - versatz) / richtung;
                    innen = innen.Geschnitten(
                        new Bereich(Math.Min(t1, t2), Math.Max(t1, t2)));
                    if (innen.IstLeer) break;
                }

                if (innen.IstLeer || innen.Bis - innen.Von < 1e-6)
                {
                    ergebnis.Add(stueck);
                    continue;
                }
                if (innen.Von > 1e-6)
                    ergebnis.Add((stueck.A, stueck.A + d * innen.Von));
                if (innen.Bis < 1 - 1e-6)
                    ergebnis.Add((stueck.A + d * innen.Bis, stueck.B));
            }
            return ergebnis;
        }

        /**
         * Fasst Stuecke zusammen, die auf derselben Geraden liegen und sich
         * ueberdecken oder beruehren.
         *
         * GENAU HIER wird aus zwei Nachbarstrassen eine einzige. Es braucht
         * dafuer keinen Sonderfall: liegt zwischen zwei Flaechen eine Kachel,
         * fallen ihre beiden Achsen auf dieselbe Linie, und dieser Schritt
         * findet sie als deckungsgleich.
         */
        private static List<(Punkt A, Punkt B)> ZoningVerschmelzeKollineare(
            IReadOnlyList<(Punkt A, Punkt B)> stuecke)
        {
            var offen = new List<(Punkt A, Punkt B)>(stuecke);
            var ergebnis = new List<(Punkt A, Punkt B)>();
            while (offen.Count > 0)
            {
                var aktuell = offen[offen.Count - 1];
                offen.RemoveAt(offen.Count - 1);
                var laenge = Geometrie.Laenge(aktuell.B - aktuell.A);
                if (laenge < 1e-6) continue;
                var richtung = (aktuell.B - aktuell.A) * (1.0 / laenge);
                var von = 0.0;
                var bis = laenge;

                var nochmal = true;
                while (nochmal)
                {
                    nochmal = false;
                    for (var i = offen.Count - 1; i >= 0; i--)
                    {
                        var kandidat = offen[i];
                        if (!ZoningAufDerselbenGeraden(
                                aktuell.A, richtung, kandidat)) continue;
                        var ta = Geometrie.Skalar(
                            kandidat.A - aktuell.A, richtung);
                        var tb = Geometrie.Skalar(
                            kandidat.B - aktuell.A, richtung);
                        var kVon = Math.Min(ta, tb);
                        var kBis = Math.Max(ta, tb);
                        // Beruehren genuegt: zwei Stuecke Kante an Kante sind
                        // eine Strasse, kein Paar.
                        if (kBis < von - 1e-3 || kVon > bis + 1e-3) continue;
                        von = Math.Min(von, kVon);
                        bis = Math.Max(bis, kBis);
                        offen.RemoveAt(i);
                        nochmal = true;
                    }
                }

                ergebnis.Add((aktuell.A + richtung * von,
                    aktuell.A + richtung * bis));
            }
            return ergebnis;
        }

        /**
         * Liegen die beiden auf derselben Geraden UND ueberdecken einander?
         *
         * Nur die Ueberdeckung zaehlt, nicht die Beruehrung: zwei Stuecke,
         * die sich an einem Punkt treffen, sind ein Anschluss und kein
         * Doppel.
         */
        private static bool ZoningDeckungsgleich(
            (Punkt A, Punkt B) x, (Punkt A, Punkt B) y)
        {
            var laenge = Geometrie.Laenge(x.B - x.A);
            if (laenge < 1e-6) return false;
            var richtung = (x.B - x.A) * (1.0 / laenge);
            if (!ZoningAufDerselbenGeraden(x.A, richtung, y)) return false;
            var ta = Geometrie.Skalar(y.A - x.A, richtung);
            var tb = Geometrie.Skalar(y.B - x.A, richtung);
            var von = Math.Min(ta, tb);
            var bis = Math.Max(ta, tb);
            // Ein echter Ueberlappungsbereich, kein blosser Beruehrpunkt.
            return Math.Min(bis, laenge) - Math.Max(von, 0.0) > 1e-3;
        }

        /**
         * ZEHN MILLIMETER WAREN ZU WENIG.
         *
         * Hier stand 1e-2, also 10 mm. GEMESSEN am 2026-09-04 im Protokoll
         * PLT-A950B8E0: die Achse einer Zoningflaechen-Strasse und die der
         * Randzoning-Strasse an derselben Polygonkante lagen 18,7 mm
         * auseinander - knapp daneben. Folge: 42,60 m Strasse blieben doppelt
         * stehen, zwei Fahrbahnen kaum zwei Zentimeter nebeneinander.
         *
         * Der Abstand entsteht aus dem Einrasten des gezogenen Rechtecks; er
         * ist Rest, nicht Absicht. ZWEI ECHTE Zoning-Strassen koennen nie so
         * dicht liegen: dazwischen liegt immer mindestens eine 8 m breite
         * Parzellenreihe. 0,25 m trennt beides sicher und faengt das
         * Einrasten mit reichlich Luft.
         */
        /*
         * NACHGEBESSERT AM 2026-09-05: 0,25 m waren immer noch zu wenig.
         *
         * Im Bau des Nutzers um 00:30 liefen zwei Zoning-Strassen 17,9 m weit
         * mit einem Achsabstand von 0,28 m nebeneinander her - knapp ueber der
         * damaligen Grenze, also ueberlebten beide. Folge im Spiel: eine
         * doppelte Fahrbahn, die alles darauf blockiert. *"Durch das ist die
         * gesamte RZ-Strasse gesperrt."*
         *
         * Und es blieb nicht dabei: weil dort keine T-Kreuzung erkannt wurde,
         * sondern zwei fast parallele Strassen, lief der Knotenbau ueberhaupt
         * nicht an - im ganzen Bauzettel steht keine einzige
         * `Zoningknotenbau`-Zeile. Die fehlende Stromverbindung haengt also
         * an derselben Zahl.
         *
         * EIN METER ist immer noch weit von einer echten Trennung entfernt:
         * zwischen zwei wirklichen Zoning-Strassen liegt mindestens eine
         * 8 m breite Parzellenreihe. Was darunter liegt, ist Rest aus dem
         * Einrasten, nicht Absicht.
         */
        private const double ZoningGeradenToleranz = 1.0;

        private static bool ZoningAufDerselbenGeraden(
            Punkt ursprung, Punkt richtung, (Punkt A, Punkt B) stueck)
        {
            var normale = new Punkt(-richtung.Y, richtung.X);
            var a = Math.Abs(Geometrie.Skalar(stueck.A - ursprung, normale));
            var b = Math.Abs(Geometrie.Skalar(stueck.B - ursprung, normale));
            return a < ZoningGeradenToleranz && b < ZoningGeradenToleranz;
        }

        /**
         * TRENNT EINEN WEG, DER AUF DERSELBEN GERADEN LIEGT WIE DIE ACHSE.
         *
         * `ZoningOhneStrassenband` legt um die Achse eine Kapsel: Streifen
         * plus Halbkreis an jedem Ende. Fuer eine FREMDE Strasse ist das
         * richtig - zwei Fahrbahnen beruehren sich bei der halben Summe ihrer
         * Breiten, und naeher duerfen sie nicht.
         *
         * Fuer eine Gasse auf DERSELBEN Linie ist es verkehrt. Sie ist keine
         * fremde Strasse, sie ist die Fortsetzung: das eine Stueck der Linie
         * ist Zoning-Strasse, das andere Fahrgasse. Die Kapsel liess dazwischen
         * 7,5 m Luecke - und CS2 verbindet innen NUR ueber einen identischen
         * Endpunkt. Also hing die RZ-Strasse frei, ausser wo zufaellig eine
         * Querstrasse traf. Genau deswegen musste die Kachelgrenze bisher auf
         * Querstrassen einrasten, und genau daraus entstanden die
         * uebereinanderliegenden Treppenstufen.
         *
         * Hier endet der Weg deshalb EXAKT auf dem Endpunkt der Achse - nicht
         * auf einem gerechneten Punkt daneben, sondern auf demselben Wert.
         * Das ist der Knoten.
         */
        private static List<(Punkt A, Punkt B)> ZoningLaengsGestossen(
            IReadOnlyList<(Punkt A, Punkt B)> stuecke, (Punkt A, Punkt B) achse)
        {
            var ergebnis = new List<(Punkt A, Punkt B)>();
            var delta = achse.B - achse.A;
            var laenge = Geometrie.Laenge(delta);
            if (laenge < 1e-9)
            {
                ergebnis.AddRange(stuecke);
                return ergebnis;
            }
            var r = delta * (1.0 / laenge);

            foreach (var stueck in stuecke)
            {
                var ta = Geometrie.Skalar(stueck.A - achse.A, r);
                var tb = Geometrie.Skalar(stueck.B - achse.A, r);
                var lo = Math.Min(ta, tb);
                var hi = Math.Max(ta, tb);
                if (hi <= 1e-6 || lo >= laenge - 1e-6)
                {
                    // Beruehrt die Achse laengs gar nicht.
                    ergebnis.Add(stueck);
                    continue;
                }
                var unten = ta <= tb ? stueck.A : stueck.B;
                var oben = ta <= tb ? stueck.B : stueck.A;
                if (lo < -1e-6) ergebnis.Add((unten, achse.A));
                if (hi > laenge + 1e-6) ergebnis.Add((achse.B, oben));
            }
            return ergebnis;
        }

        /**
         * Schneidet ein Fahrwegsegment am vorab bekannten Fahrbahnband aus.
         *
         * Das Band ist die Minkowski-Summe aus Achssegment und Kreis mit dem
         * halben Summenmass beider Strassenbreiten. Der Schnitt wird analytisch
         * aus Laengsstreifen und beiden Endkreisen gerechnet; Abtasten wuerde
         * wieder die bekannte schrittweitegrosse Fuge erzeugen.
         */
        private static List<(Punkt A, Punkt B)> ZoningOhneStrassenband(
            IReadOnlyList<(Punkt A, Punkt B)> stuecke,
            (Punkt A, Punkt B) achse, double abstand)
        {
            var ergebnis = new List<(Punkt A, Punkt B)>();
            var achsendelta = achse.B - achse.A;
            var achsenlaenge = Geometrie.Laenge(achsendelta);
            if (achsenlaenge < 1e-9 || abstand <= 0)
            {
                ergebnis.AddRange(stuecke);
                return ergebnis;
            }
            var r = achsendelta * (1.0 / achsenlaenge);
            var n = new Punkt(-r.Y, r.X);

            foreach (var stueck in stuecke)
            {
                var d = stueck.B - stueck.A;
                var quadrat = Geometrie.Skalar(d, d);
                if (quadrat < 1e-18) continue;
                var sperren = new List<Bereich>();

                // Unendlicher Mittelstreifen, auf die Laenge der Achse
                // begrenzt. Die Endkreise darunter schliessen beide Kappen.
                var w = stueck.A - achse.A;
                var laengs = Parameterbereich(
                    Geometrie.Skalar(w, r), Geometrie.Skalar(d, r),
                    0.0, achsenlaenge);
                var quer = Parameterbereich(
                    Geometrie.Skalar(w, n), Geometrie.Skalar(d, n),
                    -abstand, abstand);
                var streifen = laengs.Geschnitten(quer)
                    .Geschnitten(new Bereich(0, 1));
                if (!streifen.IstLeer) sperren.Add(streifen);

                FuegeKreisbereich(achse.A);
                FuegeKreisbereich(achse.B);
                void FuegeKreisbereich(Punkt mitte)
                {
                    var v = stueck.A - mitte;
                    var b = 2.0 * Geometrie.Skalar(v, d);
                    var c = Geometrie.Skalar(v, v) - abstand * abstand;
                    var diskriminante = b * b - 4.0 * quadrat * c;
                    if (diskriminante <= 0) return;
                    var wurzel = Math.Sqrt(diskriminante);
                    var kreis = new Bereich(
                            (-b - wurzel) / (2.0 * quadrat),
                            (-b + wurzel) / (2.0 * quadrat))
                        .Geschnitten(new Bereich(0, 1));
                    if (!kreis.IstLeer) sperren.Add(kreis);
                }

                if (sperren.Count == 0)
                {
                    ergebnis.Add(stueck);
                    continue;
                }
                sperren.Sort((x, y) => x.Von.CompareTo(y.Von));
                var freiAb = 0.0;
                foreach (var sperre in sperren)
                {
                    if (sperre.Bis <= freiAb) continue;
                    if (sperre.Von - freiAb > 1e-6)
                        ergebnis.Add((stueck.A + d * freiAb,
                            stueck.A + d * Math.Min(sperre.Von, 1.0)));
                    freiAb = Math.Max(freiAb, sperre.Bis);
                    if (freiAb >= 1.0) break;
                }
                if (1.0 - freiAb > 1e-6)
                    ergebnis.Add((stueck.A + d * freiAb, stueck.B));
            }
            return ergebnis;
        }

        /** Parameter, fuer die start + delta*t im geschlossenen Bereich liegt. */
        private static Bereich Parameterbereich(
            double start, double delta, double min, double max)
        {
            if (Math.Abs(delta) < 1e-12)
                return start >= min && start <= max
                    ? new Bereich(double.NegativeInfinity,
                        double.PositiveInfinity)
                    : Bereich.Leer;
            var a = (min - start) / delta;
            var b = (max - start) / delta;
            return new Bereich(Math.Min(a, b), Math.Max(a, b));
        }
    }
}
