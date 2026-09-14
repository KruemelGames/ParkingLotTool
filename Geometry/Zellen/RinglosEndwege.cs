using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Ringlosplan
    {
        /**
         * EIN DURCHGEHENDER WEG ENTLANG EINER SCHRAEGEN KANTE.
         *
         * Ansage des Nutzers am 2026-09-09, zu zwei Formen mit denselben
         * Reglern: *"Es wurden halt einfach Fusswege an die Strassen gepappt
         * ohne dass die sich untereinander verbinden."* Und zum Weg dorthin:
         * *"Der Asphalt der Fahrtgasse muss sich an die Fahrtgassenfusswege
         * anpassen."*
         *
         * Gemessen an seinen Bauzetteln 14:08 und 14:10:
         *
         *     Diagonale   Gassenenden 83,4 83,4 83,4 83,4  67,4  37,5
         *                 Gassen 0-3 an Kante 0, Gassen 4-5 an Kante 1
         *     Stufe       Gassenenden 83,4 83,4 83,4 83,4  -4,4  -4,4
         *                 Gassen 0-3 an Kante 0, Gassen 4-5 an Kante 2
         *
         * Der Unterschied ist die KANTE, nicht der Abstand: bei der
         * Diagonalen folgen die versetzten Enden EINER schraegen Kante, bei
         * der Stufe springen sie auf eine andere. Schrittweiten waeren als
         * Massstab untauglich - hier 16,0 und 29,9 m, dort 88 m.
         *
         * Eine rechtwinklige Stirn taugt an einer schraegen Kante nicht:
         * Astras Messung ergab bei 2 Grad schon 0,244 m Spalt, und Kante 1
         * dieser Form steht 54,6 Grad zur Querrichtung - ueber 7 m
         * Gassenbreite waeren das 9,85 m. Deshalb bekommen die Gassen einer
         * solchen Gruppe eine SCHRAEGE Stirn auf einer gemeinsamen
         * Stuetzlinie, und der Fussweg laeuft als gerader Streifen an
         * derselben Linie entlang.
         *
         * Die Stuetzlinie schneidet nur zurueck, nie nach aussen.
         */
        private void PlaneSchraegeEndwege(IReadOnlyList<Punkt> innen,
            IReadOnlyList<Zoningvorgabe> zoning, double mindestlaenge = 3)
        {
            foreach (var rechtsSeite in new[] { false, true })
            {
                int Kante(Weg g) => rechtsSeite ? g.KanteB : g.KanteA;
                double Ende(Weg g) => rechtsSeite ? g.B.X : g.A.X;
                foreach (var gruppe in Gassen
                    .Where(g => Kante(g) >= 0)
                    .GroupBy(Kante)
                    .Where(gr => gr.Count() >= 2)
                    .ToArray())
                {
                    var glieder = gruppe.OrderBy(g => g.A.Y).ToArray();
                    // Buendige Enden macht der gewoehnliche Weg schon richtig.
                    if (glieder.Max(Ende) - glieder.Min(Ende) < 1e-6) continue;

                    var erste = glieder[0];
                    var letzte = glieder[glieder.Length - 1];
                    var dy = letzte.A.Y - erste.A.Y;
                    if (Math.Abs(dy) < 1e-6) continue;

                    /*
                     * DIE STUETZLINIE GEHT DURCH DIE VORHANDENEN STIRNECKEN.
                     *
                     * Sie schneidet damit nur zurueck, nie nach aussen.
                     *
                     * VERSUCHT UND VERWORFEN: die Linie stattdessen aus der
                     * KONTURKANTE abzuleiten. Das waere sauberer - an der
                     * 54,6-Grad-Kante des Nutzers enden die Gassen 8,65 m vor
                     * der Kontur, weil `Waagerecht` bei Achse plus/minus
                     * halber Breite schneidet und das Minimum nimmt; aus der
                     * Kante abgeleitet holt die schraege Stirn diese Meter
                     * zurueck und BRINGT Buchten (gemessen 506 -> 511).
                     *
                     * Es zerlegt aber den 25-Grad-Kontrollfall: `--randstrassen`
                     * meldete "schraeg: isolierte Autowege oder keine aeussere
                     * Zufahrt" und beim Teilwinkel zusaetzlich eine
                     * Bucht/Weg-Ueberlappung. Auf einem Rechteck mit gedrehten
                     * Reihen ist die rechtwinklige Stirn eben KEIN Verlust,
                     * und das Verlaengern reisst dort Anschluesse ab. Die
                     * fuenf Buchten sind das nicht wert; wer es nochmal
                     * versucht, braucht zuerst eine Erreichbarkeitspruefung
                     * INNERHALB der Planung.
                     */
                    var m = (Ende(letzte) - Ende(erste)) / dy;
                    var c = glieder.SelectMany(g => new[]
                        {
                            Ende(g) - m * (g.A.Y - g.Breite / 2),
                            Ende(g) - m * (g.A.Y + g.Breite / 2),
                        })
                        .Aggregate(
                            rechtsSeite ? double.PositiveInfinity : double.NegativeInfinity,
                            (best, wert) => rechtsSeite
                                ? Math.Min(best, wert) : Math.Max(best, wert));
                    double Linie(double y) => c + m * y;

                    var laenge = Geometrie.Laenge(new Punkt(m, 1));
                    var aussen = new Punkt(1, -m) * ((rechtsSeite ? 1.0 : -1.0) / laenge);
                    var yVon = erste.A.Y - erste.Breite / 2;
                    var yBis = letzte.A.Y + letzte.Breite / 2;

                    /*
                     * PASST ER NICHT, RUECKT ER NACH INNEN.
                     *
                     * Der Streifen liegt einen Meter vor der Stuetzlinie.
                     * An manchen Formen ragt er dabei aus der Kontur -
                     * Astras Messstand am Abzug 15:44:15:
                     *
                     *     ECK 82,32/1096,19 innen=False Abstand=0,25
                     *
                     * Fuenfundzwanzig Zentimeter, und der ganze Streifen fiel
                     * aus; es blieb bei zwei unverbundenen Stummeln. Statt
                     * aufzugeben wird die Linie nach innen geschoben, in
                     * Vierteln bis vier Meter - dasselbe Mittel, das
                     * `PlaneEndwege` an gleicher Stelle laengst benutzt. Das
                     * kostet Gassenlaenge, aber ein fehlender Weg kostet
                     * mehr.
                     */
                    Weg streifen = null;
                    var versatz = 0.0;
                    for (var d = 0.0; d <= 4.0; d += 0.25)
                    {
                        // Vor der Zusage alle Stirnecken pruefen: 21:51
                        // verkuerzte die Linie 3,3195 m auf -0,08685 m.
                        // Ein freier Fussstreifen allein beweist keine Gasse.
                        var verschiebung = (rechtsSeite ? -1 : 1) * d * laenge;
                        if (glieder.Any(g => new[] { -g.Breite / 2, g.Breite / 2 }.Any(dyEcke =>
                            (rechtsSeite ? Linie(g.A.Y + dyEcke) + verschiebung - g.A.X
                                : g.B.X - Linie(g.A.Y + dyEcke) - verschiebung) < mindestlaenge - 1e-6))) continue;
                        var probe = new Weg
                        {
                            A = new Punkt(Linie(yVon), yVon) + aussen * (1 - d),
                            B = new Punkt(Linie(yBis), yBis) + aussen * (1 - d),
                            Breite = 2,
                            Fuss = true,
                            Art = Zufahrtsart.Fussweg,
                        };
                        SchneideEnde(probe, false, new Punkt(0, yVon), new Punkt(1, 0));
                        SchneideEnde(probe, true, new Punkt(0, yBis), new Punkt(1, 0));
                        if (!Frei(probe, innen, zoning)) continue;
                        streifen = probe;
                        versatz = d;
                        break;
                    }
                    if (streifen == null)
                    {
                        Warnungen.Add("Randstrassen aus: schraeger Endfussweg an Kante "
                            + gruppe.Key + " passt nicht in die Kontur.");
                        continue;
                    }
                    // Die Stirnen der Gassen folgen der verschobenen Linie.
                    double LinieVersetzt(double y) => Linie(y)
                        + (rechtsSeite ? -1 : 1) * versatz * laenge;
                    /*
                     * DIE ENDEN BUENDIG ZUR FAHRGASSE.
                     *
                     * Befund des Nutzers am 2026-09-09 mit Bild: der schraege
                     * Streifen stand ueber die aeusserste Fahrgasse hinaus
                     * (gemessen 0,8 m), weil seine Stirn rechtwinklig zur
                     * eigenen Achse steht statt buendig zur Gasse. Er hat die
                     * gewuenschte Kante blau eingezeichnet: der Fussweg soll
                     * mit der Fahrgasse abschliessen.
                     *
                     * Geschnitten wird auf die Aussenkante der jeweils
                     * aeussersten Gasse, also auf eine Linie in
                     * Gassenrichtung.
                     */
                    // Erst jetzt schneiden. Passt der Streifen nicht, bleibt
                    // die Gruppe unveraendert - ein halber Eingriff waere
                    // schlimmer als keiner.
                    foreach (var g in glieder)
                    {
                        var rechtsEcke = LinieVersetzt(g.A.Y - g.Breite / 2);
                        var linksEcke = LinieVersetzt(g.A.Y + g.Breite / 2);
                        var achse = LinieVersetzt(g.A.Y);
                        if (rechtsSeite)
                        {
                            g.B = new Punkt(achse, g.A.Y);
                            g.SchraegBRechts = rechtsEcke - achse;
                            g.SchraegBLinks = linksEcke - achse;
                            g.EndwegB = true;
                        }
                        else
                        {
                            g.A = new Punkt(achse, g.A.Y);
                            g.SchraegARechts = rechtsEcke - achse;
                            g.SchraegALinks = linksEcke - achse;
                            g.EndwegA = true;
                        }
                    }
                    Fusswege.Add(streifen);
                }
            }
        }

        /**
         * SCHNEIDET EIN STREIFENENDE AUF EINE GERADE.
         *
         * Ein Rechteck hat rechtwinklige Stirnen. Laeuft der Streifen
         * schraeg, steht seine Stirn damit schief zu allem, woran sie
         * anschliessen soll - beim Nutzer sichtbar als Ueberstand ueber die
         * Fahrgasse hinaus und als Luecke zwischen geradem und schraegem
         * Streifen.
         *
         * Beide Stirnecken werden entlang der EIGENEN Achse so verschoben,
         * dass sie auf der uebergebenen Geraden liegen. Steht die Gerade
         * fast parallel zur Achse, gibt es keinen sinnvollen Schnitt - dann
         * bleibt die Stirn wie sie ist.
         */
        private static void SchneideEnde(Weg w, bool endeB, Punkt punkt, Punkt richtung)
        {
            var d = w.B - w.A;
            var laenge = Geometrie.Laenge(d);
            if (laenge < 1e-9) return;
            var u = d * (1 / laenge);
            var n = new Punkt(-u.Y, u.X) * (w.Breite / 2);
            var nenner = Geometrie.Kreuz(richtung, u);
            if (Math.Abs(nenner) < 1e-6) return;
            var basis = endeB ? w.B : w.A;
            double Versatz(Punkt ecke)
                => Geometrie.Kreuz(richtung, punkt - ecke) / nenner;
            var rechts = Versatz(basis - n);
            var links = Versatz(basis + n);
            if (endeB)
            {
                w.SchraegBRechts = rechts;
                w.SchraegBLinks = links;
            }
            else
            {
                w.SchraegARechts = rechts;
                w.SchraegALinks = links;
            }
        }

        /**
         * GEHRUNG AM GEMEINSAMEN ECK.
         *
         * Zwei Endstreifen, die an benachbarten Konturkanten liegen, hoeren
         * heute vor dem Eck auf: an der Form des Nutzers der eine bei
         * quer 1126,9, der andere erst bei 1142,0 - dazwischen 15,1 m Luecke.
         * Sein Bild zeigt aber EINEN durchgehenden Weg um das Eck herum.
         *
         * Verlaengert wird bis zum Schnittpunkt der beiden Achsen. Das
         * unterscheidet die Faelle von selbst:
         *
         *   - Zwei buendige Streifen (die Stufe) laufen PARALLEL. Sie
         *     schneiden sich nie, also passiert nichts - und genau das ist
         *     dort richtig, weil zwischen ihren Kanten ein Ruecksprung liegt.
         *   - Ein schraeger und ein buendiger Streifen schneiden sich am
         *     Eck. Dort gehoeren sie zusammen.
         *
         * Die Schranke ist keine Zahl, sondern die Geometrie: die
         * Verlaengerung muss in der Kontur bleiben und darf KEINE Fahrgasse
         * ueberlaufen. `Frei` allein genuegt dafuer nicht - es prueft nur
         * Kontur und Zoning. Eine Verbindung quer ueber die Buchtreihen
         * scheitert damit an der Gasse, die dazwischen liegt.
         */
        private void VerbindeAnEcken(IReadOnlyList<Punkt> innen,
            IReadOnlyList<Zoningvorgabe> zoning)
        {
            for (var i = 0; i < Fusswege.Count; i++)
            for (var j = i + 1; j < Fusswege.Count; j++)
            {
                var u = Fusswege[i];
                var v = Fusswege[j];
                if (Ueberlappt(u.Ecken, v.Ecken)) continue;      // haengen schon

                var du = u.B - u.A;
                var dv = v.B - v.A;
                var nenner = Geometrie.Kreuz(du, dv);
                if (Math.Abs(nenner) < 1e-9) continue;           // parallel
                var t = Geometrie.Kreuz(v.A - u.A, dv) / nenner;
                var schnitt = u.A + du * t;

                // Beide Streifen bis zum Schnittpunkt fuehren - jeweils das
                // naehere Ende.
                /*
                 * DAS FERNE ENDE BEHAELT SEINE SCHRAEGE STIRN.
                 *
                 * Der erste Anlauf legte hier frische Wege an und verlor
                 * dabei die Stirnwerte des urspruenglichen Streifens. Das
                 * ferne Ende fiel damit auf rechtwinklig zurueck - und der
                 * Ueberstand ueber die aeusserste Fahrgasse war wieder da,
                 * am Plan gemessen 1,63 m. Nur das Ende, das zur Gehrung
                 * zeigt, wird neu geschnitten.
                 */
                Weg Verlaengert(Weg w)
                {
                    var nachA = Geometrie.Laenge(schnitt - w.A)
                        < Geometrie.Laenge(schnitt - w.B);
                    return new Weg
                    {
                        A = nachA ? schnitt : w.A,
                        B = nachA ? w.B : schnitt,
                        Breite = w.Breite, Fuss = true, Art = Zufahrtsart.Fussweg,
                        SchraegARechts = nachA ? 0 : w.SchraegARechts,
                        SchraegALinks = nachA ? 0 : w.SchraegALinks,
                        SchraegBRechts = nachA ? w.SchraegBRechts : 0,
                        SchraegBLinks = nachA ? w.SchraegBLinks : 0,
                    };
                }
                var uNeu = Verlaengert(u);
                var vNeu = Verlaengert(v);
                if (Geometrie.Laenge(uNeu.B - uNeu.A) < 1e-6) continue;
                if (Geometrie.Laenge(vNeu.B - vNeu.A) < 1e-6) continue;
                if (!Frei(uNeu, innen, zoning) || !Frei(vNeu, innen, zoning)) continue;

                /*
                 * KEINE GASSENSCHRANKE - DIE GEOMETRIE BEGRENZT SCHON.
                 *
                 * Der erste Anlauf verbot die Gehrung, wenn die
                 * verlaengerten Streifen eine Fahrgasse beruehren. Das ist
                 * genau falsch herum: ein Endfussweg LIEGT an den
                 * Gassenstirnen an, das ist sein Zweck. Astras Messstand
                 * zeigt den Schaden am Abzug 2026-09-09 15:43:59:
                 *
                 *     GEHRUNG 1/2 Schnitt=81,07/1096,08 frei=True/True Gassen=2
                 *
                 * Beide Verlaengerungen waren frei, und trotzdem blockierte
                 * die Schranke - die 15-m-Luecke, die der Nutzer sah. Auch
                 * die feinere Fassung, die nur das ANGESETZTE Stueck prueft,
                 * scheiterte daran: auch das laeuft an der Stirn entlang.
                 *
                 * Gemeint war "keine Verbindung quer ueber die Buchtreihen".
                 * Das verhindert die Geometrie von selbst: beide Streifen
                 * muessen `Frei` bleiben, und der Schnittpunkt zweier
                 * Endstreifen-Achsen liegt bauartbedingt an ihrem
                 * gemeinsamen Eck. Zwei buendige Streifen - die Stufe - sind
                 * parallel und schneiden sich nie.
                 *
                 * Belegt an fuenf Formen in `--diagonalwege`: alle drei
                 * Abzuege des Nutzers und beide Formen vom Vormittag geben
                 * jetzt die geforderte Zahl.
                 */

                /*
                 * DIE STIRNEN AUF DIE WINKELHALBIERENDE.
                 *
                 * Bis zum Schnittpunkt zu verlaengern reicht nicht: zwei
                 * Rechtecke, die sich unter einem Winkel treffen, lassen
                 * aussen einen Keil offen. Der Nutzer sieht ihn als Luecke
                 * zwischen gerader und schraeger Linie. Auf der
                 * Winkelhalbierenden geschnitten stossen sie ohne Luecke und
                 * ohne Ueberlappung aneinander.
                 */
                var ru = uNeu.B - uNeu.A;
                var rv = vNeu.B - vNeu.A;
                var eu = ru * (1 / Geometrie.Laenge(ru));
                var ev = rv * (1 / Geometrie.Laenge(rv));
                var zuP = Geometrie.Laenge(schnitt - uNeu.A) < 1e-6;
                var zvP = Geometrie.Laenge(schnitt - vNeu.A) < 1e-6;
                // Beide Richtungen VOM Schnittpunkt weg zeigen lassen.
                var au = zuP ? eu : new Punkt(-eu.X, -eu.Y);
                var av = zvP ? ev : new Punkt(-ev.X, -ev.Y);
                var halb = au + av;
                if (Geometrie.Laenge(halb) > 1e-6)
                {
                    SchneideEnde(uNeu, !zuP, schnitt, halb);
                    SchneideEnde(vNeu, !zvP, schnitt, halb);
                }

                Fusswege[i] = uNeu;
                Fusswege[j] = vNeu;
            }
        }

        private void PlaneEndwege(IReadOnlyList<Punkt> innen,
            IReadOnlyList<Zoningvorgabe> zoning, double buchtbreite,
            IReadOnlyList<Randzoningabschnitt> rzAbschnitte = null)
        {
            var enden = Gassen.Select(g => (Gasse: g, A: g.A, B: g.B)).ToArray();
            /**
             * AUCH EINE GASSE, DIE ALLEIN ENDET, BRAUCHT IHREN STREIFEN.
             *
             * Die Schleife baut den Endfussweg ZWISCHEN zwei benachbarten
             * Gassen. Fuer eine Gasse, die an ihrem Ende allein steht, gibt
             * es kein Paar - und damit gar keinen Weg. Solange
             * `PlaneGemeinsameEnden` alle Gassen auf ein gemeinsames Ende
             * stutzte, kam das nie vor; seit die Enden je Konturkante
             * getrennt bleiben duerfen, schon.
             *
             * Astras Messstand `Tests/DiagonaleEndwege` weist das nach: in
             * seinen Faellen "L-Sprung" und "Einzelende" bleiben je ZWEI
             * Gassenstirnen ungedeckt, im Fall "88-Grad-Kette" keine. Seine
             * Empfehlung lautet "ein vollstaendiger Streifen je Endgruppe,
             * einschliesslich Einzelenden" - genau das ist der Durchlauf mit
             * `j == i`.
             *
             * Er bekommt dieselbe Freiraumsuche und dieselbe Gassenkuerzung
             * wie ein Paar; nur seine Ausdehnung ist die eigene Stirn statt
             * der Weg zum Nachbarn.
             */
            for (var i = 0; i < enden.Length; i++)
            for (var j = i; j < enden.Length; j++)
            {
                var a = enden[i]; var b = enden[j];
                var alleine = i == j;
                if (!alleine && (b.A.Y <= a.A.Y + 1e-6
                    || Math.Min(a.B.X, b.B.X) <= Math.Max(a.A.X, b.A.X))) continue;
                if (!alleine && enden.Any(g => g.A.Y > a.A.Y + 1e-6 && g.A.Y < b.A.Y - 1e-6
                    && g.B.X > Math.Max(a.A.X, b.A.X) && g.A.X < Math.Min(a.B.X, b.B.X))) continue;
                /*
                 * BIS AN DIE AUSSENKANTE, NICHT BIS ZUR ACHSE.
                 *
                 * Der Streifen lief von `a.A.Y` bis `b.A.Y` - beides
                 * Gassen-ACHSEN. Zwischen zwei inneren Gassen stimmt das:
                 * dort stoesst der Streifen des naechsten Paares genau an.
                 * An den beiden aeussersten Enden steht aber nichts mehr
                 * dahinter, und der Weg hoerte mitten in der Randgasse auf -
                 * bei 7 m Gassenbreite fehlten je 3,5 m.
                 *
                 * Befund des Nutzers am 2026-09-08: *"der Fussweg geht leider
                 * nur bis zur Mitte der am Rand liegenden Fahrtgasse"*. Das
                 * erklaerte zugleich die Naht an dieser Stelle und den
                 * unsauberen Anschluss an die Gasse.
                 *
                 * Verlaengert wird nur nach aussen, und nur dort, wo keine
                 * weitere Gasse mehr folgt - sonst ueberlappten sich zwei
                 * Streifen auf der gemeinsamen Gasse.
                 */
                foreach (var links in new[] { true, false })
                {
                    /**
                     * NUR EIN NACHBAR, DER HIER AUCH AUFHOERT, TRAEGT WEITER.
                     *
                     * Die Verlaengerung ueber die Gassenachse hinaus
                     * unterbleibt, wenn eine weitere Gasse folgt - dort
                     * stoesst deren eigener Streifen an. Bis zum 2026-09-09
                     * genuegte dafuer, dass die Nachbargasse EXISTIERT und
                     * der Laenge nach ueberlappt. Seit ein Endfussweg nur
                     * noch zwischen Gassen mit demselben Ende entsteht,
                     * stimmt das nicht mehr: eine Nachbargasse, die hier
                     * weiterlaeuft statt zu enden, bekommt gar keinen
                     * Streifen - der Weg stiess also gegen nichts und hoerte
                     * mitten in der Fahrbahn auf.
                     *
                     * Befund des Nutzers am 2026-09-09: *"Dort wo sie sich
                     * teilen gehen sie nur bis zur Haelfte der Strasse."*
                     * Gemessen am Bauzettel 12:19 (Stufenform, sieben
                     * Gassen, quer gemessen):
                     *
                     *     Aussenkanten des Platzes   1048,9  und  1183,7
                     *     Endweg 0   1048,9 .. 1183,7   volle Breite
                     *     Endweg 1   1137,6 .. 1183,7   faengt auf der ACHSE
                     *                                   von Gasse 4 an
                     *     Endweg 2   1048,9 .. 1116,3   hoert auf der ACHSE
                     *                                   von Gasse 3 auf
                     *
                     * Je 3,5 m fehlten - genau die halbe Fahrgasse. Die
                     * langen Gassen (bis 80 m) und die kurzen (bis -13 m)
                     * enden an verschiedenen Stellen; sie sind Nachbarn,
                     * aber keine Partner.
                     */
                    double Ende(Ringlosplan.Weg g) => links ? g.A.X : g.B.X;
                    bool EndetHier(Ringlosplan.Weg g)
                        => Math.Abs(Ende(g) - Ende(a.Gasse)) < 1e-6;
                    var weiterUnten = enden.Any(g => g.A.Y < a.A.Y - 1e-6
                        && g.B.X > Math.Max(a.A.X, b.A.X)
                        && g.A.X < Math.Min(a.B.X, b.B.X)
                        && EndetHier(g.Gasse));
                    var weiterOben = enden.Any(g => g.A.Y > b.A.Y + 1e-6
                        && g.B.X > Math.Max(a.A.X, b.A.X)
                        && g.A.X < Math.Min(a.B.X, b.B.X)
                        && EndetHier(g.Gasse));
                    var aY = weiterUnten ? a.A.Y : a.A.Y - a.Gasse.Breite / 2;
                    var bY = weiterOben ? b.A.Y : b.A.Y + b.Gasse.Breite / 2;

                    /*
                     * NUR ZWISCHEN GASSEN MIT DEMSELBEN ENDE.
                     *
                     * Der Streifen laeuft von `a`s Ende zu `b`s Ende. Das
                     * ergibt nur dann eine gerade Linie, wenn beide Gassen
                     * dort auch wirklich aufhoeren. Bis zum 2026-09-09 war
                     * das immer so, weil `PlaneGemeinsameEnden` ALLE Gassen
                     * auf ein gemeinsames Ende stutzte - um den Preis, dass
                     * bei einer L-Form ein ganzer Arm wegfiel.
                     *
                     * Seit die Enden je Konturkante getrennt bleiben duerfen,
                     * gilt die Annahme nicht mehr. Befund des Nutzers am
                     * 2026-09-09: *"Dieser fuehrt quer entlang, also er
                     * verbindet die Fahrtgassen obwohl die weit auseinander
                     * liegen bzw in einem anderen Teilgebiet sind."*
                     * Gemessen an seiner L-Form: ein Fussweg von
                     * (-1076,3/118,0) nach (-1100,8/25,6) - 95,6 m lang,
                     * davon 24,5 m quer. Genau die Diagonale im Bild.
                     *
                     * Enden einer Kante sind nach dem Angleichen auf den
                     * Millimeter gleich; ein Sprung auf die naechste Kante
                     * ist Meter weit. Der Vergleich braucht deshalb keine
                     * Toleranz. Verglichen wird der Stand VOR dem Kuerzen -
                     * `enden` ist genau diese Momentaufnahme -, sonst
                     * verschoebe der erste gebaute Streifen die Vergleiche
                     * fuer alle folgenden.
                     */
                    if (Math.Abs((links ? a.A.X : a.B.X)
                        - (links ? b.A.X : b.B.X)) > 1e-6) continue;
                    /*
                     * Der Einzeldurchlauf gilt nur, wenn die Gasse an DIESER
                     * Seite wirklich allein endet. Hat sie dort einen
                     * Partner, baut ihn das Paar - ein zweiter Streifen waere
                     * eine Ueberlappung, und ueberlappende Objekte sind in
                     * Vanilla-CS2 nicht mehr loeschbar.
                     */
                    if (alleine && enden.Any(g => !ReferenceEquals(g.Gasse, a.Gasse)
                        && EndetHier(g.Gasse))) continue;
                    /*
                     * UND NUR, WENN ES UEBERHAUPT EINE GASSENSCHAR GIBT.
                     *
                     * Der Endfussweg bedient die Stirnseiten einer Reihe von
                     * Gassen. Besteht der ganze Plan aus EINER Gasse, gibt es
                     * keine Reihe - dort war und bleibt kein Weg richtig.
                     * `--randstrassen` haelt das seit jeher fest ("Eine
                     * Gasse: Buchten fehlen oder Endweg vorhanden"), und der
                     * erste Anlauf dieses Einzeldurchlaufs hat genau daran
                     * angeschlagen.
                     *
                     * Der Fall des Nutzers ist ein anderer: dort endet eine
                     * Gasse allein INNERHALB mehrerer, und ihre Stirn bliebe
                     * sonst als einzige unbedeckt.
                     */
                    if (alleine && Gassen.Count < 2) continue;
                    // Diese Seite hat schon einen schraegen Streifen.
                    if (links ? a.Gasse.EndwegA || b.Gasse.EndwegA
                              : a.Gasse.EndwegB || b.Gasse.EndwegB) continue;
                    var ax = links ? a.A.X - 1 : a.B.X + 1;
                    var bx = links ? b.A.X - 1 : b.B.X + 1;
                    Weg gewaehlt = null;
                    List<(Weg Gasse, double Ende)> grenzen = null;
                    // Bei einer schraegen Grundstueckskante kann die volle
                    // Streifenbreite mehr Reserve verlangen als seine Achse.
                    // Gesucht wird ausschliesslich vor Raster und Buchtenwahl.
                    for (var reserve = 0.0; reserve <= 8.0; reserve += 0.5)
                    {
                        var versatz = links ? reserve : -reserve;
                        var w = new Weg { A = new Punkt(ax + versatz, aY),
                            B = new Punkt(bx + versatz, bY), Breite = 2,
                            Fuss = true, Art = Zufahrtsart.Fussweg };
                        w = EndwegAmRzAbschluss(w, rzAbschnitte);
                        if (!Frei(w, innen, zoning)) continue;
                        var kandidaten = new List<(Weg Gasse, double Ende)>();
                        var passt = true;
                        foreach (var g in Gassen.Where(g => g.A.Y >= a.A.Y - 1e-6 && g.A.Y <= b.A.Y + 1e-6))
                        {
                            var punkte = w.Ecken;
                            var xs = new List<double>();
                            var unten = g.A.Y - g.Breite / 2;
                            var oben = g.A.Y + g.Breite / 2;
                            foreach (var p in punkte)
                                if (p.Y >= unten && p.Y <= oben) xs.Add(p.X);
                            foreach (var y in new[] { unten, oben })
                                foreach (var span in Waagerecht(punkte, y)) { xs.Add(span.A); xs.Add(span.B); }
                            if (xs.Count == 0) continue;
                            if (g != a.Gasse && g != b.Gasse && (xs.Max() <= g.A.X || xs.Min() >= g.B.X)) continue;
                            var ende = links ? Math.Max(g.A.X, xs.Max()) : Math.Min(g.B.X, xs.Min());
                            if ((links ? g.B.X - ende : ende - g.A.X) < buchtbreite - 1e-6) { passt = false; break; }
                            kandidaten.Add((g, ende));
                        }
                        if (!passt) continue;
                        gewaehlt = w; grenzen = kandidaten; break;
                    }
                    if (gewaehlt == null)
                    {
                        Warnungen.Add("Randstraßen aus: Endfußweg zwischen Gassen bei "
                            + a.A.Y.ToString("F1") + "/" + b.A.Y.ToString("F1") + " m passt nicht als gerader Streifen.");
                        continue;
                    }
                    foreach (var grenze in grenzen)
                    {
                        if (links) grenze.Gasse.A = new Punkt(grenze.Ende, grenze.Gasse.A.Y);
                        else grenze.Gasse.B = new Punkt(grenze.Ende, grenze.Gasse.B.Y);
                        // Auch die Gassenstirn benutzt die gemeinsame RZ-
                        // Stuetzlinie. Sonst bleibt am 1,966-Grad-Fall ein
                        // weniger als 0,001 m2 grosser Keil zwischen beiden.
                        if (Math.Abs(gewaehlt.B.X - gewaehlt.A.X) > 1e-9)
                        {
                            var ecken = gewaehlt.Ecken;
                            var ersteRechts = ecken[0].X + ecken[1].X > ecken[2].X + ecken[3].X;
                            var k = ersteRechts == links ? 0 : 2;
                            SchneideEnde(grenze.Gasse, !links, ecken[k], ecken[k + 1] - ecken[k]);
                        }
                    }
                    Fusswege.Add(gewaehlt);
                }
            }
        }
    }
}
