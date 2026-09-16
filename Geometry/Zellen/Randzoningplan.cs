using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    // Ein Abschnitt fuer Material und Freihaltung. Im Bauzettel 21:51 lag
    // die Achse bei Laengs 4,3906..116,3406 m, das Hindernis aber bei
    // -4..130,7407 m. Die Stirnen muessen aus derselben Achse kommen.
    internal sealed class Randzoningabschnitt
    {
        internal Punkt A;
        internal Punkt B;
        internal Punkt Innen;
        internal double Tiefe;
        internal double Halb;
        internal Punkt[] Strasse => Band(-Halb, Halb);
        internal Punkt[] Bauland => Band(-Tiefe, -Halb);
        internal Punkt[] Freihaltung => Band(-Tiefe, Halb);
        private Punkt[] Band(double aussen, double innen) => new[] {
            A + Innen * aussen, B + Innen * aussen,
            B + Innen * innen, A + Innen * innen };

        /**
         * SCHNEIDET DAS BAULAND AUS EINER STRECKE HERAUS.
         *
         * Regel 3 des Nutzers, Skizze `fahrtgassenfussweg.png`: *"An der
         * Diagonalen muessten auch die Fahrtgassenfusswege verschwinden bzw.
         * nicht berechnet werden, sonst laufen sie in die Tiles hinein."*
         *
         * Wo Kacheln liegen, liegt kein Weg. Das Band reicht von der
         * Umrisskante bis zur AUSSENkante der Strasse - die Strasse selbst
         * bleibt frei, sie ist ja der Anschluss.
         *
         * Gerechnet wird im Rahmen des Abschnitts: `laengs` entlang der Achse,
         * `quer` in Richtung `Innen`. Das Band ist quer in [-Tiefe, -Halb] und
         * laengs in [0, Laenge].
         */
        internal void SchneideHeraus(Punkt a, Punkt b, List<(Punkt A, Punkt B)> ergebnis)
        {
            var d = B - A;
            var laenge = Geometrie.Laenge(d);
            if (laenge < 1e-6) { ergebnis.Add((a, b)); return; }
            var r = d * (1 / laenge);

            double Quer(Punkt p) => Geometrie.Skalar(p - A, Innen);
            double Laengs(Punkt p) => Geometrie.Skalar(p - A, r);

            var qa = Quer(a); var qb = Quer(b);
            var la = Laengs(a); var lb = Laengs(b);

            // Liegt die Strecke ganz ausserhalb, bleibt sie unangetastet.
            if (Math.Min(qa, qb) >= -Halb - 1e-9
                || Math.Max(qa, qb) <= -Tiefe + 1e-9
                || Math.Min(la, lb) >= laenge - 1e-9
                || Math.Max(la, lb) <= 1e-9)
            {
                ergebnis.Add((a, b));
                return;
            }

            /*
             * DIE PARAMETER, BEI DENEN DIE STRECKE DAS BAND BETRITT UND
             * VERLAESST - vier Halbebenen, nacheinander verschnitten.
             */
            var t0 = 0.0; var t1 = 1.0;
            bool Klemme(double wertA, double wertB, double unten, double oben)
            {
                var diff = wertB - wertA;
                if (Math.Abs(diff) < 1e-12)
                    return wertA >= unten - 1e-9 && wertA <= oben + 1e-9;
                var ta = (unten - wertA) / diff;
                var tb = (oben - wertA) / diff;
                var lo = Math.Min(ta, tb); var hi = Math.Max(ta, tb);
                t0 = Math.Max(t0, lo); t1 = Math.Min(t1, hi);
                return t1 > t0;
            }

            if (!Klemme(qa, qb, -Tiefe, -Halb) || !Klemme(la, lb, 0, laenge))
            {
                ergebnis.Add((a, b));
                return;
            }

            var ab = b - a;
            if (t0 > 1e-6) ergebnis.Add((a, a + ab * t0));
            if (t1 < 1 - 1e-6) ergebnis.Add((a + ab * t1, b));
        }

        internal static Randzoningabschnitt[] Plane(IReadOnlyList<Punkt> umriss,
            IReadOnlyList<(Punkt A, Punkt B)> gewaehlt, double tiefe,
            IReadOnlyList<Punkt> achse = null)
        {
            if (gewaehlt == null || gewaehlt.Count == 0) return Array.Empty<Randzoningabschnitt>();
            achse = achse ?? Layoutplanung.Innenrand(umriss, tiefe);
            var result = new List<Randzoningabschnitt>();
            if (achse.Count != umriss.Count) return result.ToArray();
            for (var i = 0; i < umriss.Count; i++) {
                var j = (i + 1) % umriss.Count;
                if (!ParkingGeometry.RandzoningIstKante(gewaehlt, umriss[i], umriss[j])) continue;
                var d = achse[j] - achse[i];
                var len = Geometrie.Laenge(d);
                if (len < 1e-6) continue;
                /*
                 * WO INNEN IST, WIRD GEPRUEFT - NICHT AUS DEM UMLAUFSINN
                 * ABGELEITET.
                 *
                 * Hier stand allein `(-d.Y, d.X)`, die linke Normale der
                 * Kantenrichtung. Die zeigt nach innen, SOLANGE der Ring
                 * gegen den Uhrzeigersinn laeuft. Der Umriss wird dafuer
                 * eigens gedreht (`Layout.cs`, Zeile 53) - aber gerechnet
                 * wird hier nicht auf dem Umriss, sondern auf dem nach innen
                 * VERSETZTEN Ring. Und der kann an einer engen oder konkaven
                 * Ecke eine Kante umdrehen: ist die Kante kuerzer als der
                 * Versatz, kreuzen sich ihre Nachbarn, und aus A->B wird
                 * B->A. Die linke Normale zeigt dann nach aussen, und das
                 * Randzoning kippt in den Parkplatz hinein.
                 *
                 * Das erklaert, warum es nur MANCHE Formen trifft - der
                 * Nutzer am 2026-09-16: *"Outside-Zoning funktioniert immer
                 * noch nicht richtig. Manche Formen gehen immer noch nach
                 * innen."*
                 *
                 * Die Gegenprobe kostet einen Punkt-im-Vieleck-Test und ist
                 * gegen beides immun: gegen den Umlaufsinn und gegen einen
                 * umgedrehten Versatzring.
                 */
                var innen = new Punkt(-d.Y, d.X) * (1 / len);
                var mitte = (achse[i] + achse[j]) * 0.5;
                var probe = mitte + innen * Math.Max(1.0, tiefe * 0.25);
                if (!Geometrie.Enthaelt(umriss, probe)) innen = innen * -1;
                result.Add(new Randzoningabschnitt { A = achse[i], B = achse[j],
                    Innen = innen, Tiefe = tiefe,
                    Halb = ParkingGeometry.ZoningStrassenbreite / 2 });
            }
            return result.ToArray();
        }

        /**
         * DIE FAHRGASSEN SIND DIE RANDZONING-STRASSE - NOTFALLS ALS TREPPE.
         *
         * Entwurf des Nutzers vom 2026-09-10, Skizze `treppe_diagonal.png`:
         *
         *   "Anstatt eine Strasse zu erzwingen, warum nehmen wir nicht die am
         *   naechsten liegende Fahrtgasse und nutzen die als RZ-Randstrasse."
         *
         * Vorher lag die erzwungene Strasse auf der Randstrassenachse, 10,40 m
         * innerhalb des Umrisses, und die erste echte Fahrgasse bei 24,18 m -
         * ZWEI parallele Strassen im Abstand von 13,78 m, wo eine reicht.
         *
         * EINE SCHRAEGE KANTE BRAUCHT MEHRERE. Sie laesst sich nicht von einer
         * waagerechten Gasse bedienen. Deshalb wird sie an den Gassenhoehen
         * zerschnitten: jede Gasse bedient das Stueck der Kante zwischen ihrer
         * eigenen Hoehe und der Hoehe der naechsten Gasse. In der Skizze sind
         * das die roten Rechtecke, die nach rechts unten treppen.
         *
         * KEINE BEDINGUNG AN DIE RICHTUNG. Auf Nachfrage stellte der Nutzer
         * klar: *"Die naechstliegende zur Polygonkante - ich habe nie gesagt,
         * die muessen parallel liegen."* Im Rahmen des Kerns laufen ohnehin
         * alle Fahrgassen waagerecht; schraeg ist hoechstens die Kante.
         */
        internal static Randzoningabschnitt[] PlaneAnGassen(
            IReadOnlyList<Punkt> umriss,
            IReadOnlyList<(Punkt A, Punkt B)> gewaehlt,
            IReadOnlyList<(double Y, double Von, double Bis)> gassen,
            double links, double rechts,
            IReadOnlyList<double> quermitten = null)
        {
            if (gewaehlt == null || gewaehlt.Count == 0
                || gassen == null || gassen.Count == 0)
                return Array.Empty<Randzoningabschnitt>();

            var halb = ParkingGeometry.ZoningStrassenbreite / 2;

            /*
             * KEIN LAENGENFILTER AUF DEN GASSEN - GEMESSEN UND VERWORFEN.
             *
             * Naheliegend waere, kurze Gassen gar nicht erst als Strasse
             * zuzulassen: an der Diagonalform liegt eine von 5,4 m Laenge in
             * der Ecke, waehrend die uebrigen 108 bis 194 m messen. Mit einem
             * Filter bei zwei Strassenbreiten fiel aber auch die waagerechte
             * Kante des ERSTEN Falls auseinander - 8 Buchten blieben im
             * Kachelband stehen, der Belag reichte bis 9,41 m statt 14,81 m.
             * Eine kurze Gasse bedient eben auch ein kurzes Stueck Kante.
             *
             * Der eigentliche Fehler war ein anderer: die Strasse spannte von
             * Rasterrand zu Rasterrand, statt dort zu enden, wo die Gasse
             * endet. Das steht unten.
             */
            var sortiert = gassen.OrderBy(g => g.Y).ToArray();
            var result = new List<Randzoningabschnitt>();

            for (var i = 0; i < umriss.Count; i++)
            {
                var j = (i + 1) % umriss.Count;
                if (!ParkingGeometry.RandzoningIstKante(gewaehlt, umriss[i], umriss[j]))
                    continue;
                var ka = umriss[i];
                var kb = umriss[j];

                /*
                 * AUF WELCHER SEITE LIEGT DER PARKPLATZ? Die Gassen liegen
                 * alle auf einer Seite der Kante; ihre Mehrheit entscheidet.
                 * `Innen` zeigt von der Kante weg, in den Parkplatz hinein.
                 */
                var kantenmitte = (ka + kb) * 0.5;
                var oberhalb = sortiert.Count(g => g.Y > kantenmitte.Y);
                var innenY = oberhalb * 2 >= sortiert.Length ? 1.0 : -1.0;
                var innen = new Punkt(0, innenY);

                /*
                 * DIE AEUSSERSTE GASSE GEHOERT DAZU.
                 *
                 * Hier wurde gegen die MITTE der Kante geprueft. Bei einer
                 * schraegen Kante wirft das die aeusserste Gasse hinaus,
                 * obwohl sie ein Stueck der Kante bedient: an der Form des
                 * Nutzers vom 2026-09-10 16:38 liegt die Kantenmitte bei
                 * -1150, die aeusserste Gasse bei -1159 - und die Kante
                 * reicht bis -1169. Die unterste Treppenstufe fiel damit weg,
                 * bevor sie geplant war.
                 *
                 * Massgeblich ist der NAECHSTE Punkt der Kante, nicht ihre
                 * Mitte: was jenseits davon liegt, liegt drinnen.
                 */
                var kantenNah = innenY > 0
                    ? Math.Min(ka.Y, kb.Y)
                    : Math.Max(ka.Y, kb.Y);

                /*
                 * ERST SAMMELN, DANN AUFEINANDER ABSTIMMEN.
                 *
                 * Jede Gasse bekommt zunaechst nur ihr Fenster. Welches
                 * Stueck sie am Ende wirklich traegt, entscheidet sich erst,
                 * wenn alle bekannt sind - siehe die Naht weiter unten.
                 */
                var kandidaten = new List<(double Y, double Von, double Bis,
                    double W0, double W1)>();

                foreach (var gasse in sortiert)
                {
                    var y = gasse.Y;
                    // Nur Gassen auf der Parkplatzseite der Kante.
                    if ((y - kantenNah) * innenY <= 0) continue;

                    /*
                     * WELCHES STUECK DER KANTE BEDIENT DIESE GASSE?
                     *
                     * Das Stueck, fuer das SIE die naechste Gasse ist. Also
                     * alles zwischen der Gasse davor (Richtung Kante) und ihr
                     * selbst.
                     *
                     * ERSTER ANLAUF WAR FALSCH: ich hatte das Fenster zwischen
                     * dieser Gasse und der NAECHSTEN gespannt. Eine
                     * waagerechte Kante liegt aber unterhalb ALLER Gassen und
                     * fiel damit durch jedes Fenster - der Planer gab nichts
                     * zurueck und der Rueckfall baute wieder die erzwungene
                     * Strasse bei 10,40 m.
                     *
                     * Nach aussen hin ist das Fenster offen: was jenseits der
                     * aeussersten Gasse liegt, gehoert ihr.
                     */
                    var davor = sortiert
                        .Where(o => (o.Y - y) * innenY < 0)
                        .OrderBy(o => Math.Abs(o.Y - y))
                        .Select(o => (double?)o.Y).FirstOrDefault();

                    var vonY = davor ?? y - innenY * 1e9;
                    var bisY = y;
                    var lo = Math.Min(vonY, bisY);
                    var hi = Math.Max(vonY, bisY);

                    // Die Kante auf dieses Hoehenfenster beschneiden.
                    var xs = new List<double>();
                    if (Math.Abs(kb.Y - ka.Y) < 1e-9)
                    {
                        // Waagerechte Kante: entweder ganz drin oder gar nicht.
                        if (ka.Y < lo - 1e-9 || ka.Y > hi + 1e-9) continue;
                        xs.Add(ka.X);
                        xs.Add(kb.X);
                    }
                    else
                    {
                        foreach (var grenze in new[] { lo, hi })
                        {
                            var t = (grenze - ka.Y) / (kb.Y - ka.Y);
                            if (t < 0 || t > 1) continue;
                            xs.Add(ka.X + (kb.X - ka.X) * t);
                        }
                        foreach (var ecke in new[] { ka, kb })
                            if (ecke.Y >= lo - 1e-9 && ecke.Y <= hi + 1e-9)
                                xs.Add(ecke.X);
                    }
                    if (xs.Count < 2) continue;

                    /*
                     * DIE STRASSE ENDET, WO DIE GASSE ENDET.
                     *
                     * Hier stand `links`/`rechts` - die Rasterbreite. Damit
                     * entstand auf dem 5,4-m-Stummel bei y = 123,3 ein
                     * Zoning-Kurs von 120 m: eine Strasse, die es nirgends
                     * gibt. Die Gasse IST die Strasse, also gilt ihre eigene
                     * Ausdehnung.
                     */
                    /*
                     * DIE STRASSE ENDET, WO DIE GASSE ENDET.
                     *
                     * Hier stand `links`/`rechts` - die Rasterbreite. Damit
                     * entstand auf einem 5,4 m langen Gassenstummel ein
                     * Zoning-Kurs von 120 m: eine Strasse, die es nirgends
                     * gibt. Die Gasse IST die Strasse, also gilt ihre eigene
                     * Ausdehnung.
                     */
                    kandidaten.Add((y,
                        Math.Max(links, gasse.Von),
                        Math.Min(rechts, gasse.Bis),
                        xs.Min(), xs.Max()));
                }

                /*
                 * DIE TREPPE DARF KEINE LUECKE HABEN.
                 *
                 * Das Fenster einer Gasse und ihre eigene Ausdehnung sind
                 * nicht dasselbe. An der Diagonalform des Nutzers (Zettel
                 * 16:06) endete jede Gasse 15,41 m frueher, als ihr Fenster
                 * reichte - die Gasse laeuft eben auch nur bis zur Kante, und
                 * ihr Band ist 7 m breit. Uebrig blieben Stuecke bei laengs
                 * 4,5..17,1 / 32,5..52,0 / 67,5..79,6 und dazwischen zweimal
                 * 15,4 m Kante GANZ OHNE Strasse. Ein Endfussweg lief dort
                 * prompt bis auf 7,0 m an die Kante heran.
                 *
                 * Was die aeussere Gasse nicht mehr schafft, uebernimmt die
                 * naechste nach innen - sie reicht weiter, weil die Kante von
                 * ihr wegfuehrt. Genau das ist eine Treppe: Stufe stoesst an
                 * Stufe, ohne Loch und ohne Ueberlappung.
                 *
                 * Gelaufen wird von aussen nach innen; `versorgtBis` merkt, bis
                 * wohin die Kante versorgt ist.
                 */
                var aussenEcke = innenY > 0
                    ? (ka.Y <= kb.Y ? ka : kb)
                    : (ka.Y >= kb.Y ? ka : kb);
                var innenEcke = aussenEcke.Equals(ka) ? kb : ka;
                var richtung = Math.Sign(innenEcke.X - aussenEcke.X);
                var versorgtBis = aussenEcke.X;

                var geordnet = kandidaten
                    .OrderBy(k => Math.Abs(k.Y - kantenNah)).ToArray();

                for (var n = 0; n < geordnet.Length; n++)
                {
                    var kandidat = geordnet[n];
                    var letzte = n == geordnet.Length - 1;

                    double x0, x1;
                    if (richtung >= 0)
                    {
                        x0 = Math.Max(kandidat.Von, versorgtBis);
                        x1 = Math.Min(kandidat.Bis,
                            letzte ? Math.Max(kandidat.W1, innenEcke.X) : kandidat.W1);
                    }
                    else
                    {
                        x1 = Math.Min(kandidat.Bis, versorgtBis);
                        x0 = Math.Max(kandidat.Von,
                            letzte ? Math.Min(kandidat.W0, innenEcke.X) : kandidat.W0);
                    }

                    var y = kandidat.Y;

                    /*
                     * DIE KACHELN ENDEN AN EINER QUERSTRASSE.
                     *
                     * Entwurf des Nutzers am 2026-09-10: *"Anstatt nur einen
                     * Teilabschnitt der Strasse zu nutzen, machen wir die
                     * ganze Fahrgasse zur RZ-Strasse. Dabei ist aber wichtig,
                     * dass wir selbst entscheiden koennen, wo ueberhaupt die
                     * Tiles liegen bzw. anfangen. Und nur dort, wo die Tiles
                     * hinkommen, fallen auch die Buchten weg."*
                     *
                     * Steuerbar ist das kantenweise: `ZonesDisabled` sitzt je
                     * Seite auf EINEM Strassenkurs, ein Kurs zont ganz oder
                     * gar nicht. Kurse enden an Knoten - bei uns an den
                     * Querstrassen. Die Kachelgrenze rastet deshalb nach
                     * aussen auf die naechste Querstrasse ein.
                     *
                     * Damit verschwindet auch der Grund fuer die frueheren
                     * frei erfundenen Treppenschnitte: die Grenzen liegen dort,
                     * wo das Netz ohnehin teilt, statt dort, wo die Diagonale
                     * zufaellig eine Gassenhoehe kreuzt. Genau daraus kamen die
                     * versetzten Rechteckecken.
                     */
                    /*
                     * DIE STUFEN STOSSEN ANEINANDER - SIE UEBERLAPPEN NICHT.
                     *
                     * Hier wurde jedes Stueck nach aussen auf die naechste
                     * Querstrasse gedehnt. Der Grund war ein anderer Mangel:
                     * eine Gasse auf derselben Linie endete 7,5 m vor der
                     * Strasse, also gab es nur an Querstrassen einen Knoten.
                     * Seit `ZoningLaengsGestossen` stossen beide exakt
                     * aneinander, und die Dehnung ist nicht mehr noetig.
                     *
                     * Sie war sogar schaedlich: an der Form des Nutzers vom
                     * 2026-09-10 16:38 dehnten sich ALLE DREI Stufen bis zur
                     * selben Querstrasse bei laengs -28,2. Damit lagen sie
                     * uebereinander, und die innerste - bei quer -1116 -
                     * legte ihr Kachelband ueber die beiden aeusseren.
                     * Gemessen blieben 3119 m2 von 13468 unbedeckt, 23 % des
                     * Areals: kein Belag, keine Buchten, keine Gasse.
                     *
                     * Jetzt gilt das Fenster, das die Gasse wirklich bedient.
                     */
                    if (x1 - x0 < ParkingGeometry.ZoningStrassenbreite) continue;

                    /*
                     * DIE TIEFE deckt die weiteste Stelle des bedienten
                     * Kantenstuecks ab - sonst bliebe bei schraeger Kante ein
                     * Zwickel ohne Bauland. Was ueber den Umriss hinausragt,
                     * schneidet der Umriss ohnehin ab.
                     */
                    var tiefe = 0.0;
                    foreach (var xx in new[] { x0, x1 })
                    {
                        var t = Math.Abs(kb.X - ka.X) < 1e-9 ? 0.0
                            : (xx - ka.X) / (kb.X - ka.X);
                        t = Math.Max(0.0, Math.Min(1.0, t));
                        var kantenY = ka.Y + (kb.Y - ka.Y) * t;
                        tiefe = Math.Max(tiefe, Math.Abs(y - kantenY));
                    }
                    if (tiefe <= halb) continue;

                    result.Add(new Randzoningabschnitt
                    {
                        A = new Punkt(x0, y),
                        B = new Punkt(x1, y),
                        Innen = innen,
                        Tiefe = tiefe,
                        Halb = halb,
                    });

                    // Bis hierhin ist die Kante versorgt. Eine uebersprungene
                    // Stufe schiebt die Marke NICHT weiter - dann bekommt die
                    // naechste nach innen das ganze Stueck.
                    versorgtBis = richtung >= 0 ? x1 : x0;
                }
            }
            return result.ToArray();
        }

        /**
         * WELCHE GASSE LIEGT DER GEWAEHLTEN KANTE AM NAECHSTEN?
         *
         * Gibt die Fahrgassenmitte zurueck, oder NaN, wenn es keine gibt -
         * dann bleibt es beim erzwungenen Weg. Ein Abbruch, der den Nutzer am
         * Bauen hindert, waere schlimmer als eine zusaetzliche Strasse.
         */
        internal static double NaechsteGasse(
            IReadOnlyList<Punkt> umriss,
            IReadOnlyList<(Punkt A, Punkt B)> gewaehlt,
            IEnumerable<double> gassenmitten)
        {
            var kanten = new List<(Punkt A, Punkt B)>();
            for (var i = 0; i < (umriss?.Count ?? 0); i++)
            {
                var j = (i + 1) % umriss.Count;
                if (ParkingGeometry.RandzoningIstKante(gewaehlt, umriss[i], umriss[j]))
                    kanten.Add((umriss[i], umriss[j]));
            }
            if (kanten.Count == 0) return double.NaN;

            var beste = double.NaN;
            var besterAbstand = double.PositiveInfinity;
            foreach (var y in gassenmitten ?? Array.Empty<double>())
            foreach (var kante in kanten)
            {
                var abstand = Math.Min(
                    Math.Abs(kante.A.Y - y), Math.Abs(kante.B.Y - y));
                if (abstand >= besterAbstand) continue;
                besterAbstand = abstand;
                beste = y;
            }
            return beste;
        }
    }
}
