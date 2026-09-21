using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    // Alle Korridore werden vor der Zellteilung geplant. Die 2-m-Endstreifen
    // liegen ausserhalb der Gassen; bei einer Gasse gibt es 0 Verbindungen.
    internal sealed partial class Ringlosplan
    {
        internal sealed class Weg
        {
            internal Punkt A;
            internal Punkt B;
            internal double Breite;
            internal bool Fuss;
            internal int Band = -1;
            internal Zufahrtsart Art;
            internal bool Zufahrt;
            /**
             * SCHRAEGE STIRN: LAENGSVERSATZ DER VIER ECKEN.
             *
             * Bis zum 2026-09-09 war ein Weg immer ein Rechteck - seine
             * Stirn stand rechtwinklig auf der Achse. An einer schraegen
             * Grundstueckskante geht das nicht auf: die Kante der L-Form des
             * Nutzers steht 54,6 Grad zur Querrichtung, und ueber 7 m
             * Gassenbreite verschieben sich ihre Schnittpunkte um 9,85 m in
             * Laengsrichtung. Eine rechtwinklige Stirn laesst dort also fast
             * zehn Meter klaffen.
             *
             * Ansage des Nutzers: *"Der Asphalt der Fahrtgasse muss sich an
             * die Fahrtgassenfusswege anpassen."* Dafuer darf jede der vier
             * Ecken einzeln entlang der ACHSE verschoben werden; die Stirn
             * wird damit zu einer beliebigen Geraden zwischen ihren beiden
             * Ecken.
             *
             * Alle vier Werte null ergeben exakt das alte Rechteck - das ist
             * der Normalfall und bleibt zeichengenau gleich.
             */
            /**
             * AN WELCHER KONTURKANTE ENDET DIESE GASSE?
             *
             * Nullbasiert wie `Input.PolygonXZ`, -1 heisst "nicht von der
             * Kontur begrenzt" (Hindernis, Zoning, globale Klammer).
             *
             * Astras Messung am 2026-09-09: bei der schraegen Form des
             * Nutzers enden die Gassen 0-3 an Kante 0 und die Gassen 4-5 an
             * Kante 1; bei der Stufenform enden 4-5 dagegen an Kante 2, mit
             * Kante 1 als Ruecksprung dazwischen. Genau daran haengt, ob zwei
             * versetzte Enden verbunden gehoeren oder nicht - der ABSTAND
             * taugt dafuer nicht: hier sind es Schritte von 16,0 und 29,9 m,
             * dort ein Sprung von 88 m.
             *
             * Die Herkunft muss VOR Reserve und Angleichen festgehalten
             * werden. Aus dem fertigen Endpunkt laesst sie sich nicht
             * zurueckrechnen: die kurzen Gassen enden 8,65 m vor ihrem
             * Konturtreffer, die vier anderen nur 3,01 bis 3,21 m.
             */
            internal int KanteA = -1;
            internal int KanteB = -1;

            /** Diese Seite wird schon von einem schraegen Endstreifen bedient. */
            internal bool EndwegA;
            internal bool EndwegB;

            internal double SchraegARechts;
            internal double SchraegALinks;
            internal double SchraegBRechts;
            internal double SchraegBLinks;

            internal bool Schraeg => SchraegARechts != 0 || SchraegALinks != 0
                || SchraegBRechts != 0 || SchraegBLinks != 0;

            internal Punkt[] Ecken
            {
                get
                {
                    var d = B - A;
                    var laenge = Geometrie.Laenge(d);
                    var n = new Punkt(-d.Y, d.X) * (Breite / (2 * laenge));
                    if (!Schraeg) return new[] { A - n, B - n, B + n, A + n };
                    var u = d * (1 / laenge);
                    return new[]
                    {
                        A - n + u * SchraegARechts,
                        B - n + u * SchraegBRechts,
                        B + n + u * SchraegBLinks,
                        A + n + u * SchraegALinks,
                    };
                }
            }
        }

        internal readonly List<Weg> Gassen = new List<Weg>();
        internal readonly List<Weg> Fusswege = new List<Weg>();
        internal readonly List<Weg> Zufahrten = new List<Weg>();
        internal readonly List<Querstrassenstueck> Querwege = new List<Querstrassenstueck>();
        internal readonly List<string> Warnungen = new List<string>();
        internal double Links;
        internal double Rechts;

        internal static Ringlosplan Plane(Bandplan baender, IReadOnlyList<Punkt> innen,
            IReadOnlyList<Punkt> areal, IReadOnlyList<Zufahrtsvorgabe> zufahrten,
            Zelleneinstellungen e, Linienregister register,
            out IReadOnlyList<Querstrassenplan> quer,
            IReadOnlyList<Zoningvorgabe> zoning = null,
            IReadOnlyList<(Punkt A, Punkt B)> randzoning = null)
        {
            var p = new Ringlosplan();
            p.Links = innen.Min(v => v.X);
            p.Rechts = innen.Max(v => v.X);
            var reserve = baender.Module.Count > 1 ? 2.0 : 0.0;
            p.Links += reserve;
            p.Rechts -= reserve;
            var hindernisse = (zoning ?? Array.Empty<Zoningvorgabe>()).Select(z => z.Ecken().ToArray()).ToList();
            /*
             * DIE ALTE ERZWUNGENE STRASSE FORMT DIE GASSEN NICHT MEHR.
             *
             * Hier wurde sie geplant und gleich als gesperrter Streifen in
             * `hindernisse` gelegt. Damit kappte sie jede Gasse, die an eine
             * Randzoning-Kante heranlief - und ZWAR BEVOR entschieden war,
             * welche Gasse die RZ-Strasse wird.
             *
             * Das war dieselbe Kreisschluessigkeit wie beim `Freihaltung`-Band
             * eine Ebene weiter unten, nur frueher: erst formte die alte
             * Strasse die Gassen, dann wurde aus diesen Gassen die neue
             * Strasse geplant. Am Bauzettel des Nutzers vom 2026-09-10 16:38
             * stand das Ergebnis in drei Zahlen - das Polygon reichte bei den
             * Gassen -1159 / -1138 / -1116 bis laengs 88 / 113 / 122, die
             * Gassen endeten aber bei 73 / 67 / 59. Die beiden, die an der
             * Diagonalen die Treppenstufen tragen mussten, kamen dort gar
             * nicht mehr hin. Uebrig blieb die INNERSTE - sie bekam die ganze
             * Kante, und alles zwischen ihr und der Diagonalen wurde
             * Kachelband: keine Buchten, kein Belag, keine Gasse. Sein Urteil:
             * *"Da kommt der groesste Mist ueberhaupt raus."*
             *
             * Jetzt laufen die Gassen erst einmal frei bis an die Kante - sie
             * SIND ja die Strasse -, und die Abschnitte entstehen daraus.
             */
            var rzAbschnitte = Array.Empty<Randzoningabschnitt>();
            /*
             * DER ABSCHNITT RAEUMT DAS BAULAND - NICHT SICH SELBST.
             *
             * Hier stand `Freihaltung`: das Band von der Umrisskante bis zur
             * INNENkante der Zoningstrasse. Solange die Strasse eigens erzeugt
             * wurde, war das richtig - sie musste freigehalten werden.
             *
             * Seit die Strasse eine FAHRGASSE ist, ist es verkehrt: das Band
             * schliesst die Gasse mit ein, und sie wird von ihrem eigenen
             * Abschnitt weggeschnitten. Erst wird die Strasse aus der Gasse
             * geplant, dann loescht die Strasse die Gasse.
             *
             * Im Bauzettel des Nutzers vom 2026-09-10 16:18 stand das
             * Ergebnis: der Zoning-Kurs lag bei quer -1,29 ueber laengs
             * -1150,7..-1103,8, die zugehoerige Gasse bei demselben quer, aber
             * ueber -1096,3..-1043,5. Gleiche Linie, kein gemeinsamer Meter -
             * eine Strasse dort, wo die Gasse nicht mehr ist. Sein Urteil
             * dazu: *"Es kommt kompletter Mist heraus."*
             *
             * `Bauland` reicht von der Kante nur bis zur AUSSENkante der
             * Strasse. Die Strasse selbst und alles dahinter bleibt stehen -
             * genau die Regel des Nutzers: *"Nur die eine Seite der Buchten
             * muss weg, nicht beide."*
             */
            void PlaneGassen()
            {
            p.Gassen.Clear();
            foreach (var m in baender.Module)
            {
                var y = m.Fahrgassenmitte;
                // Die Kantenherkunft reist ab hier mit: wer den Rand
                // enger macht, gibt seine Kante weiter; wer nicht von der
                // Kontur kommt, setzt sie auf -1.
                var spans = WaagerechtMitKante(innen, y).ToList();
                foreach (var yy in new[] { y - e.Fahrgassenbreite / 2, y + e.Fahrgassenbreite / 2 })
                    spans = spans.SelectMany(s => WaagerechtMitKante(innen, yy)
                        .Select(t => (
                            A: Math.Max(s.A, t.A),
                            KanteA: s.A >= t.A ? s.KanteA : t.KanteA,
                            B: Math.Min(s.B, t.B),
                            KanteB: s.B <= t.B ? s.KanteB : t.KanteB))
                        .Where(t => t.B > t.A + 1e-6)).ToList();
                foreach (var box in hindernisse)
                {
                    if (box.Max(v => v.Y) < y - e.Fahrgassenbreite / 2
                        || box.Min(v => v.Y) > y + e.Fahrgassenbreite / 2) continue;
                    var lo = box.Min(v => v.X); var hi = box.Max(v => v.X);
                    spans = spans.SelectMany(s => new[]
                        {
                            (s.A, s.KanteA, Math.Min(s.B, lo), s.B <= lo ? s.KanteB : -1),
                            (Math.Max(s.A, hi), s.A >= hi ? s.KanteA : -1, s.B, s.KanteB),
                        })
                        .Where(s => s.Item3 > s.Item1 + 1e-6).ToList();
                }
                foreach (var span in spans)
                {
                    /*
                     * AN EINER ZONINGKANTE KEINE RESERVE.
                     *
                     * Die Reserve haelt die Gasse vom Rand des Parkplatzes
                     * fort. Wo aber die Zoningflaeche geschnitten hat, soll
                     * die Gasse bis an sie heranlaufen - sonst bleibt ein
                     * toter Streifen, in den der Code spaeter eine
                     * geklemmte Querstrasse setzt.
                     *
                     * Erkannt wird die Kante daran, dass sie auf dem Rand
                     * eines Hindernisses liegt; der Name der Kante taugt
                     * nicht, weil `-1` auch die globale Klammer bedeutet.
                     */
                    bool AnZoning(double x) => hindernisse.Any(box =>
                        box.Max(v => v.Y) >= y - e.Fahrgassenbreite / 2
                        && box.Min(v => v.Y) <= y + e.Fahrgassenbreite / 2
                        && (Math.Abs(box.Min(v => v.X) - x) < 1e-6
                            || Math.Abs(box.Max(v => v.X) - x) < 1e-6));

                    var reserveA = AnZoning(span.A) ? 0.0 : reserve;
                    var reserveB = AnZoning(span.B) ? 0.0 : reserve;
                    var a = Math.Max(p.Links, span.A + reserveA);
                    var b = Math.Min(p.Rechts, span.B - reserveB);
                    if (b - a < e.Buchtbreite - 1e-6) continue;
                    p.Gassen.Add(new Weg { A = new Punkt(a, y), B = new Punkt(b, y),
                        Breite = e.Fahrgassenbreite, Band = m.Fahrgasse.Id,
                        // Die globale Klammer ist keine Konturkante.
                        KanteA = p.Links > span.A + reserveA ? -1 : span.KanteA,
                        KanteB = p.Rechts < span.B - reserveB ? -1 : span.KanteB });
                }
            }
            }

            PlaneGassen();

            /*
             * JETZT ERST: WELCHE GASSE WIRD DIE RZ-STRASSE?
             *
             * `quermitten` bleibt hier leer - die Querstrassen entstehen erst
             * weiter unten. Fuer die beiden Zwecke an dieser Stelle reicht
             * das: die Endwege sollen wissen, wo Kacheln liegen, und die
             * Querstrassen brauchen die Achse. Wo die Kachelgrenze am Ende
             * genau einrastet, rechnet `Layout` mit den echten Querstrassen
             * noch einmal - und `Fuege` schneidet danach jeden Weg daran.
             */
            var rueckfall = false;
            if (randzoning != null && randzoning.Count > 0)
            {
                rzAbschnitte = Randzoningabschnitt.PlaneAnGassen(
                    areal, randzoning,
                    p.Gassen.Select(g => (Y: g.A.Y,
                        Von: Math.Min(g.A.X, g.B.X),
                        Bis: Math.Max(g.A.X, g.B.X))).ToArray(),
                    p.Links, p.Rechts);

                /*
                 * FINDET SICH KEINE GASSE, BLEIBT ES BEIM ERZWUNGENEN WEG -
                 * und dann gilt auch die alte Sperre wieder, sonst laege die
                 * erzwungene Strasse auf einer Gasse. Ueberlappende Objekte
                 * sind in Vanilla-CS2 weder loeschbar noch bearbeitbar.
                 */
                if (rzAbschnitte.Length == 0)
                {
                    rueckfall = true;
                    rzAbschnitte = Randzoningabschnitt.Plane(
                        areal, randzoning, e.Randstrassenmittellinientiefe);
                    hindernisse.AddRange(rzAbschnitte.Select(rz => rz.Bauland));
                    PlaneGassen();
                }
            }
            // Erst gemeinsame Enden, dann die Fussstreifen konstruieren.
            // Am Nutzerpolygon 08.09. entstanden sonst aus den leicht schiefen
            // Enden 55 Scherben; gemeinsame Enden reduzieren diese auf 1
            // (die letzte kam vom unabhaengigen Randbandstoss).
            p.PlaneGemeinsameEnden(e.Buchtbreite);
            // Zuerst die schraegen Kanten - sie schneiden die Stirnen und
            // melden ihre Seite ab, damit der gewoehnliche Weg sie auslaesst.
            p.PlaneSchraegeEndwege(innen, zoning, e.Buchtbreite);
            p.PlaneEndwege(innen, zoning, e.Buchtbreite, rzAbschnitte);
            /*
             * ERST VERSCHMELZEN, DANN GEHRUNG, DANN NOCHMAL VERSCHMELZEN.
             *
             * `PlaneEndwege` legt je Gassenpaar ein Stueck an. Ohne das
             * Verschmelzen davor zog die Gehrung DREI dieser Stuecke zum
             * selben Eck - drei Streifen uebereinander, und ueberlappende
             * Objekte sind in Vanilla-CS2 nicht mehr loeschbar.
             */
            p.VereinigeFussstreifen();
            p.VerbindeAnEcken(innen, zoning);
            p.VereinigeFussstreifen();
            var eingang = Zufahrtsbauer.Plane(areal, zufahrten, 0, register);
            var erster = eingang.FirstOrDefault(z => z.Vorgabe.Art != Zufahrtsart.Fussweg);
            var anker = erster == null ? (p.Links + p.Rechts) / 2 : erster.Start.X;
            if (erster != null && Math.Abs(erster.Innennormale.Y) > 1e-6
                && p.Gassen.Count > 0)
            {
                var abstaende = p.Gassen.Select(g => (g.A.Y - erster.Start.Y) / erster.Innennormale.Y)
                    .Where(t => t > 0).ToArray();
                if (abstaende.Length > 0)
                {
                    /*
                     * EINE ZUFAHRT LAENGS DER GASSEN ERREICHT KEINE.
                     *
                     * Die Zeile darunter schiebt den Anker dorthin, wo die
                     * Zufahrt auf die erste Gasse trifft: sie faehrt `weg`
                     * Meter ihre Innennormale entlang. Steht die Zufahrt aber
                     * fast PARALLEL zu den Gassen, wird `Innennormale.Y`
                     * winzig und der Weg riesig - sie trifft sie nie durch
                     * Queren, sondern laeuft daneben her.
                     *
                     * Der Anschlag `> 1e-6` reichte dafuer nicht. Astra hat am
                     * 2026-09-09 einen Fall gemessen: `Innennormale.Y =
                     * 0,000012428040`, Anker 162 km vom Rahmenursprung. Eine
                     * Konturaenderung von weniger als einem MILLIMETER
                     * verschob damit die Gitterphase um 14,2 cm - die
                     * Querstrassen wandern, ohne dass jemand etwas geaendert
                     * hat, was man sehen koennte.
                     *
                     * Die Grenze ist deshalb kein weiterer Epsilonwert,
                     * sondern der Parkplatz selbst: laenger als seine
                     * Diagonale kann kein Weg IM Parkplatz sein. Wer weiter
                     * muesste, faehrt laengs - dann bleibt der Anker dort, wo
                     * die Zufahrt ansetzt.
                     */
                    var weg = abstaende.Min();
                    var laengsweite = p.Rechts - p.Links;
                    var querweite = p.Gassen.Max(g => g.A.Y)
                        - p.Gassen.Min(g => g.A.Y);
                    var diagonale = Math.Sqrt(
                        laengsweite * laengsweite + querweite * querweite);
                    if (weg <= diagonale)
                        anker += erster.Innennormale.X * weg;
                }
            }
            var strassen = new List<Querstrassenplan>();
            Querstrassenplan Strasse(double x, bool notwendig)
            {
                var alt = strassen.FirstOrDefault(q => Math.Abs(q.Mitte - x) < 1e-6);
                if (alt != null) { alt.Notwendig |= notwendig; return alt; }
                var id = strassen.Count;
                var a = x - e.Querstrassenbreite / 2;
                var b = x + e.Querstrassenbreite / 2;
                var q = new Querstrassenplan(id, x, a, b,
                    register.Querstrassenkante(a, id, "links"),
                    register.Querstrassenkante(b, id, "rechts")) { Notwendig = notwendig };
                strassen.Add(q);
                return q;
            }
            /*
             * DIE RANDZONING-STRASSE IST EIN PARTNER, KEIN HINDERNIS.
             *
             * Befund des Nutzers am 2026-09-09: *"Sehr auffaellig war, dass
             * die RZ niemals ueber Querstrassen verbunden wurde. Das ist ein
             * grober Fehler."* Gemessen am Zettel 22:22:21: ein Zoning-Kurs,
             * 89,0 m lang, an beiden Enden beruehrt ihn NICHTS; der naechste
             * Punkt des Parkplatznetzes lag 9,73 m entfernt.
             *
             * Die Ursache stand ein paar Zeilen weiter oben: die RZ-Abschnitte
             * gingen als `Freihaltung` in die HINDERNISSE. Damit war die
             * Strasse etwas, um das herumgeplant wird - und nie etwas, das
             * angeschlossen wird. Mit eingeschalteter Randstrasse faellt das
             * nicht auf, denn dort verbindet die Randstrasse alles; ohne sie
             * gibt es gar keine, und die RZ-Strasse bleibt allein.
             *
             * SIE IST ABER GENAU DAS, WAS DIE RANDSTRASSE WAR: eine Achse
             * parallel zu den Gassen, einen Abstand vom Rand entfernt. In
             * beiden Faellen des Nutzers - 21:51 und 22:22 - liegt sie 0,0
             * Grad zur Gassenrichtung. Also laeuft sie hier als Partner in
             * derselben Paarschleife mit, und die Querstrassen finden sie wie
             * jede Nachbargasse: auf demselben Gitter, unter derselben
             * N-Regel.
             *
             * Sie kommt NICHT in `p.Gassen` - dort wuerde aus ihr eine
             * Fahrgasse mit Buchten. Sie ist nur Ziel, nicht Reihe.
             */
            var partner = new List<Weg>(p.Gassen);
            /*
             * NUR IM RUECKFALL. Liegt die RZ-Strasse auf einer Fahrgasse, ist
             * sie als Partner laengst dabei - `p.Gassen` enthaelt sie. Sie
             * ein zweites Mal einzutragen gaebe demselben Gitterpunkt zwei
             * Ziele, und die Querstrasse haette die Wahl zwischen zwei
             * identischen Achsen.
             */
            foreach (var rz in rueckfall ? rzAbschnitte : Array.Empty<Randzoningabschnitt>())
            {
                var richtung = rz.B - rz.A;
                var laenge = Geometrie.Laenge(richtung);
                if (laenge < 1e-6) continue;
                // Nur, wenn sie wirklich laengs laeuft. Eine quer liegende
                // RZ-Strasse braucht einen anderen Anschluss - der Fall ist
                // nicht gemessen und wird deshalb nicht geraten.
                if (Math.Abs(richtung.Y) > Math.Abs(richtung.X)) continue;
                var y = (rz.A.Y + rz.B.Y) / 2;
                partner.Add(new Weg
                {
                    A = new Punkt(Math.Min(rz.A.X, rz.B.X), y),
                    B = new Punkt(Math.Max(rz.A.X, rz.B.X), y),
                    Breite = ParkingGeometry.ZoningStrassenbreite,
                    Band = -1,
                    KanteA = -1,
                    KanteB = -1,
                });
            }
            partner.Sort((x, y) => x.A.Y.CompareTo(y.A.Y));

            // Jede benachbarte, geometrisch erreichbare Gasse bekommt ihren
            // Anschluss unabhaengig von N. Zusaetzliche Wege folgen demselben N-Raster.
            for (var i = 0; i < partner.Count; i++)
            for (var j = i + 1; j < partner.Count; j++)
            {
                var a = partner[i]; var b = partner[j];
                if (b.A.Y <= a.A.Y + 1e-6) continue;
                if (partner.Any(g => g.A.Y > a.A.Y + 1e-6 && g.A.Y < b.A.Y - 1e-6
                    && g.B.X > Math.Max(a.A.X, b.A.X) && g.A.X < Math.Min(a.B.X, b.B.X))) continue;
                var l = Math.Max(a.A.X, b.A.X) + e.Querstrassenbreite / 2;
                var r = Math.Min(a.B.X, b.B.X) - e.Querstrassenbreite / 2;
                if (r < l - 1e-6) continue;
                var n = Math.Max(1, (int)Math.Round(e.Querstrassenabstand / e.Buchtbreite
                    - (e.Querstrassenkappen ? 2 : 0), MidpointRounding.AwayFromZero));
                var schritt = (n + (e.Querstrassenkappen ? 2 : 0)) * e.Buchtbreite + e.Querstrassenbreite;
                var rest = (Math.Min(5, n) + (e.Querstrassenkappen ? 2 : 0)) * e.Buchtbreite;
                /**
                 * DIE N-REGEL GILT AUCH FUER DIE ERSTE QUERSTRASSE.
                 *
                 * Befund des Nutzers am 2026-09-08: *"Dort liegt eine
                 * Querstrasse direkt neben einem Randstrasse-aus-Fussweg.
                 * Anscheinend funktioniert die Regel nur auf einer Seite."*
                 *
                 * Gemessen am Bauzettel 23:51 (N = 9, also 21 m gefordert):
                 * je Korridor lag genau eine Querstrasse 1,5 m vom Gassenende
                 * - exakt die halbe Querstrassenbreite, also am Anschlag. Die
                 * uebrigen drei hielten 37,5 / 73,5 / 109,5 m ein.
                 *
                 * Ursache: die Rasterstrassen unten pruefen `rest` in beide
                 * Richtungen, der ANKER wurde aber nur auf den Korridor
                 * beschnitten. Lag er darunter, drueckte ihn `Math.Max` ans
                 * Ende. Am anderen Ende gibt es keinen Anschlag - daher der
                 * einseitige Eindruck.
                 *
                 * Ist der Korridor zu kurz fuer die Regel, gilt weiter die
                 * Verbindung: zwei unverbundene Fahrgassen sind schlimmer als
                 * eine Querstrasse zu nah am Rand.
                 */
                var lRegel = l + rest;
                var rRegel = r - rest;
                if (rRegel < lRegel) { lRegel = l; rRegel = r; }

                /*
                 * DIE LAGE KOMMT AUS DEM GITTER, NICHT AUS DEM KORRIDOR.
                 *
                 * Befund des Nutzers am 2026-09-09: *"Bei Randstrassen aus
                 * verhaelt sich die Position der Querstrassen sehr komisch,
                 * die bewegen sich oft - bzw. einige und manche nicht. Es
                 * bleibt kein richtiges Grid mehr."*
                 *
                 * Hier stand `Math.Max(lRegel, Math.Min(rRegel, anker))`: der
                 * Anker wurde in den Korridor GEKLEMMT. Passt er hinein,
                 * fluchten alle; passt er nicht, rutscht jeder Korridor an
                 * SEINEN Rand - und jeder Rand liegt woanders. Am Dreieck
                 * 200 x 140 gemessen, Schritt 36 m: Phasen 31,61 / 26,04 /
                 * 20,47 / 29,90, drei von vier Korridoren aus der Flucht.
                 *
                 * DAS IST AELTER ALS DIE N-REGEL VOM 2026-09-08. Mit dem
                 * Stand davor - ohne `rest` im Anschlag - waren es dieselben
                 * drei von vier, nur an anderen Stellen (26,21 / 5,04 /
                 * 35,47 / 29,90). Die Regel hat den Fehler verschoben, nicht
                 * verursacht.
                 *
                 * Anker und Schritt sind beide GLOBAL. Also wird auch global
                 * gewaehlt: aus `anker + k * schritt` der Punkt, der im
                 * erlaubten Band liegt und dem Anker am naechsten ist. Jeder
                 * Korridor trifft damit dieselben Linien, und die
                 * Zusatzstrassen unten schreiten ohnehin in `schritt` weiter.
                 *
                 * Findet sich keiner, gilt weiter die Klemme - dieselbe
                 * Rangfolge wie beim leeren Regelband darueber: zwei
                 * unverbundene Fahrgassen sind schlimmer als eine
                 * Querstrasse ausserhalb der Flucht.
                 */
                double Gitterpunkt(double von, double bis)
                {
                    if (anker >= von - 1e-9 && anker <= bis + 1e-9) return anker;
                    var k = anker < von
                        ? Math.Ceiling((von - anker) / schritt)
                        : Math.Floor((bis - anker) / schritt);
                    var kandidat = anker + k * schritt;
                    return kandidat >= von - 1e-9 && kandidat <= bis + 1e-9
                        ? kandidat : double.NaN;
                }
                var x = Gitterpunkt(lRegel, rRegel);

                /*
                 * DER GANZE KORRIDOR ALS RUECKFALL WAERE ZU TEUER - GEMESSEN.
                 *
                 * Naheliegend waere, bei leerem Regelband den GANZEN Korridor
                 * nach einer Gitterlinie abzusuchen. Die Treppe 180 x 120
                 * fluchtete damit vollstaendig - alle drei Korridore Phase
                 * 18,00 -, aber DREI Querstrassen ruecken dabei auf 3,0 m an
                 * eine Gassenstirn. Das ist genau der Befund vom 2026-09-08:
                 * *"Dort liegt eine Querstrasse direkt neben einem
                 * Randstrasse-aus-Fussweg."* Die Flucht ist das nicht wert.
                 *
                 * Es bleibt die Klemme. Sie trifft nur noch Korridore, deren
                 * Regelband schmaler als ein Schritt ist - dort gibt es gar
                 * keine Gitterlinie zu holen. An der Treppe faellt das Band
                 * auf einen einzigen Punkt zusammen (lRegel = rRegel = 34,5).
                 */
                // Der Streifen, in den keine Buchtreihe mehr passt. Was
                // ganz darin liegt, steht zwischen Fahrgasse und
                // Zoningstrasse - und genau das soll dort nicht stehen.
                var zoningluft = ParkingGeometry.ZoningStrassenbreite / 2
                                 + e.Buchttiefe;
                var geklemmt = double.IsNaN(x);
                if (geklemmt)
                    x = Math.Max(lRegel, Math.Min(rRegel, anker));
                var gitterwunsch = x;
                var ausgewichen = false;
                var verbindung = new Weg { A = new Punkt(x, a.A.Y), B = new Punkt(x, b.A.Y), Breite = e.Querstrassenbreite };
                if (!Frei(verbindung, innen, zoning))
                {
                    ausgewichen = true;
                    // Der erste Anker ist bevorzugt. Bei einem Hindernis wird
                    // vor dem Raster eine andere geometrisch freie Verbindung gesucht.
                    /*
                     * AUCH DIE AUSWEICHSTELLE BLEIBT ZUERST IM GITTER.
                     *
                     * Die Gitterlinien des Bandes stehen vorn und werden
                     * bevorzugt, danach erst die Korridorraender und die
                     * Kanten der Baulandflaechen. Sonst raeumte ein einziges
                     * Hindernis die Flucht wieder ab.
                     */
                    var gitter = new List<double>();
                    for (var k = Math.Ceiling((lRegel - anker) / schritt);
                        anker + k * schritt <= rRegel + 1e-9; k++)
                        gitter.Add(anker + k * schritt);
                    var kandidaten = gitter.Concat(
                        new[] { lRegel, rRegel, (lRegel + rRegel) / 2 }).Concat(
                        (zoning ?? Array.Empty<Zoningvorgabe>()).SelectMany(z => z.Ecken())
                        .SelectMany(v => new[] { v.X - e.Querstrassenbreite / 2 - 1e-5,
                            v.X + e.Querstrassenbreite / 2 + 1e-5 }));
                    var frei = kandidaten.Where(xx => xx >= lRegel && xx <= rRegel)
                        .OrderBy(xx => gitter.Any(g => Math.Abs(g - xx) < 1e-6) ? 0 : 1)
                        .ThenBy(xx => Math.Abs(xx - anker)).Where(xx => Frei(new Weg {
                            A = new Punkt(xx, a.A.Y), B = new Punkt(xx, b.A.Y), Breite = e.Querstrassenbreite }, innen, zoning)).ToArray();
                    if (frei.Length == 0) continue;
                    x = frei[0];
                }
                void Fuege(double mitte, bool notwendig)
                {
                    var q = Strasse(mitte, notwendig);
                    p.Querwege.Add(new Querstrassenstueck { Querstrasse = q,
                        Anfang = new Punkt(mitte, a.A.Y), Ende = new Punkt(mitte, b.A.Y) });
                }
                /*
                 * DIE GEKLEMMTE STRASSE FAELLT AN EINER ZONINGFLAECHE WEG.
                 *
                 * Nur die geklemmte: wo eine Gitterlinie ins Band passt,
                 * steht die Strasse im Raster und stoert niemanden. Und nur
                 * an einer Zoningflaeche: ein von Natur aus schmaler
                 * Korridor bekommt seine Verbindung weiterhin, sonst haengen
                 * dort zwei Fahrgassen in der Luft.
                 */
                if (geklemmt && ZoningZerschneidetKorridor(
                        zoning, l, r, a.A.Y, b.A.Y, zoningluft))
                {
                    if (ParkingGeometry.LiveAn)
                        ParkingGeometry.Live("  querstrassen korridor"
                            + " | band " + l.ToString("F2") + ".."
                            + r.ToString("F2")
                            + " | geklemmt bei " + x.ToString("F2")
                            + " an einer Zoningflaeche - WEGGELASSEN");
                    continue;
                }

                Fuege(x, true);
                /*
                 * Eine zusaetzliche Querstrasse darf an die Zoningstrasse
                 * STOSSEN, aber nicht neben ihr herlaufen. Liegt sie auf
                 * ganzer Laenge in dem Streifen, in den keine Buchtreihe
                 * mehr passt, bleibt dort totes Band - dann faellt sie weg
                 * und die Reihe reicht bis an die Zoningstrasse.
                 */
                var gefuellt = new List<double>();
                var gewuenscht = double.IsNaN(gitterwunsch) ? anker : gitterwunsch;
                var vonK = (int)Math.Ceiling((l + rest - anker) / schritt);
                var bisK = (int)Math.Floor((r - rest - anker) / schritt);
                for (var k = vonK; k <= bisK; k++)
                {
                    var xx = anker + k * schritt;
                    if (!(xx > l + rest) || !(xx < r - rest)) continue;
                    // Die notwendige Verbindung steht schon; eine zweite
                    // Strasse, die sie ueberlappt, waere keine.
                    if (Math.Abs(xx - x) < e.Querstrassenbreite) continue;
                    var w = new Weg { A = new Punkt(xx, a.A.Y), B = new Punkt(xx, b.A.Y), Breite = e.Querstrassenbreite };
                    if (!Frei(w, innen, zoning)) continue;
                    if (LiegtZwischenZoningUndGasse(w, zoning, zoningluft))
                        continue;
                    Fuege(xx, false);
                    gefuellt.Add(xx);
                }

                if (ParkingGeometry.LiveAn)
                    ParkingGeometry.Live("  querstrassen korridor"
                        + " | anker " + anker.ToString("F2")
                        + " | schritt " + schritt.ToString("F2")
                        + " | band " + l.ToString("F2") + ".." + r.ToString("F2")
                        + " | gewuenscht " + gewuenscht.ToString("F2")
                        + (ausgewichen ? " AUSGEWICHEN" : " frei")
                        + " | notwendig bei " + x.ToString("F2")
                        + " | Rasterabstand " + (schritt <= 0 ? "-"
                            : (Math.Abs(x - anker) / schritt).ToString("F3"))
                        + " | zusaetzlich " + gefuellt.Count
                        + (gefuellt.Count == 0 ? string.Empty
                            : " bei " + string.Join(", ",
                                gefuellt.Select(v => v.ToString("F2")))));
            }
            p.VereinigeFussstreifen();
            p.PlaneZufahrten(eingang, areal, zoning);
            if (erster == null) p.Warnungen.Add("Randstraßen aus: 0 Autozufahrten; eine Autozufahrt setzen.");
            quer = strassen.OrderBy(q => q.Mitte).ToArray();
            return p;
        }

        internal void PlaneZufahrten(IReadOnlyList<Zufahrtsgeometrie> eingang,
            IReadOnlyList<Punkt> areal, IReadOnlyList<Zoningvorgabe> zoning)
        {
            var p = this;
            foreach (var z in eingang)
            {
                var ziele = p.Gassen.Concat(p.Querwege.Select(q => new Weg { A = q.Anfang, B = q.Ende })).ToList();
                if (z.Vorgabe.Art == Zufahrtsart.Fussweg) ziele.AddRange(p.Fusswege);
                var treffer = new List<double>();
                foreach (var ziel in ziele)
                {
                    /*
                     * ZWEI ARTEN, EINEN WEG ZU ERREICHEN.
                     *
                     * Bisher zaehlte nur die Kreuzung. Das reicht nicht:
                     * laeuft die Zufahrt neben einer Fahrgasse her statt
                     * quer durch sie, kreuzt sie nichts - und lief bis zur
                     * naechsten Querstrasse weiter, wobei sie die Fahrgasse
                     * auf der ganzen Strecke verdraengte.
                     *
                     * Befund des Nutzers am 2026-09-08: *"Die Zufahrt geht
                     * bis an die Querstrasse, dabei soll sie aber nur an das
                     * Ende der Fahrtgasse. Derzeit verdraengt die Zufahrt die
                     * Fahrtgasse komplett."*
                     *
                     * GEMESSEN, und der Winkel entscheidet: bei exakt 90 Grad
                     * lief die Zufahrt 3,0 m weit, bei 88 Grad ganze 60,0 m -
                     * 76 Buchten Unterschied. Zwei Grad Schraeglage genuegen,
                     * damit der Strahl die fast parallele Gasse doch irgendwo
                     * weit oben kreuzt. Eine Sonderbehandlung nur fuer die
                     * exakte Parallele haette das nicht erwischt.
                     *
                     * Also beides sammeln und das NAECHSTE nehmen: die
                     * Kreuzung und das nahe ENDE eines Weges, neben dem die
                     * Zufahrt herlaeuft.
                     */
                    var d = ziel.B - ziel.A;

                    // (1) Erreichen: das nahe Ende eines Weges, dessen
                    // Korridor sich mit dem der Zufahrt seitlich ueberlappt.
                    var seitlich = z.Vorgabe.Breite / 2 + 1e-6;
                    foreach (var kante in new[] { ziel.A, ziel.B })
                    {
                        var nach = kante - z.Start;
                        var laengs = Geometrie.Skalar(nach, z.Innennormale);
                        if (laengs <= 1e-6) continue;
                        if (Math.Abs(Geometrie.Kreuz(z.Innennormale, nach))
                            > seitlich) continue;
                        treffer.Add(laengs);
                    }

                    // (2) Kreuzen: der klassische Schnittpunkt.
                    var det = Geometrie.Kreuz(z.Innennormale, d);
                    if (Math.Abs(det) < 1e-8) continue;
                    var t = Geometrie.Kreuz(ziel.A - z.Start, d) / det;
                    var u = Geometrie.Kreuz(ziel.A - z.Start, z.Innennormale) / det;
                    if (t > 1e-6 && u >= -1e-6 && u <= 1 + 1e-6) treffer.Add(t);
                }
                if (treffer.Count == 0)
                {
                    p.Warnungen.Add("Randstraßen aus: Zufahrt " + z.Nummer + " hat keinen geraden Anschluss; Lage ändern.");
                    continue;
                }
                var anschluss = new Weg { A = z.Start, B = z.Start + z.Innennormale * treffer.Min(),
                    Breite = z.Vorgabe.Breite, Zufahrt = true, Art = z.Vorgabe.Art, Fuss = z.Vorgabe.Art == Zufahrtsart.Fussweg };
                // Die aeussere Stirn liegt auf der Grundstückskante. Das
                // Rechteck muss dennoch einschliesslich Breite frei bleiben.
                if (Frei(anschluss, areal, zoning)) p.Zufahrten.Add(anschluss);
                else p.Warnungen.Add("Randstraßen aus: Zufahrt " + z.Nummer + " trifft ein Hindernis.");
            }
        }

        private void VereinigeFussstreifen()
        {
            var fertig = new List<Weg>();
            foreach (var w in Fusswege.OrderBy(w => w.A.X).ThenBy(w => w.A.Y))
            {
                var vorher = fertig.LastOrDefault();
                if (vorher != null && Geometrie.Laenge(vorher.B - w.A) < 1e-6
                    && Math.Abs(Geometrie.Kreuz(vorher.B - vorher.A, w.B - w.A)) < 1e-6)
                    vorher.B = w.B;
                else fertig.Add(w);
            }
            Fusswege.Clear(); Fusswege.AddRange(fertig);
        }

        /**
         * GLEICHE ENDEN ANGLEICHEN - NICHT ALLE AUF DAS KUERZESTE STUTZEN.
         *
         * Zweck: bei 88 Grad enden benachbarte Gassen um Zentimeter versetzt,
         * und daraus entstanden am Nutzerpolygon vom 2026-09-08 fuenfundfuenfzig
         * Scherben. Auf einem Rechteck ist das Angleichen deshalb richtig -
         * gemessen an genau diesem Fall betraegt der Schnitt 0,01 m.
         *
         * Bis zum 2026-09-09 stutzte die Funktion aber JEDE Gruppe auf die
         * SCHNITTMENGE aller Enden. Auf einem Rechteck faellt das nicht auf.
         * Auf einer L-Form ist es eine Amputation - gemessen am Bauzettel
         * 2026-09-09 00:14:
         *
         *     vorher   Gasse 1  A -73,00  B  80,07   Laenge 153,1
         *              Gasse 2  A -73,00  B  80,07   Laenge 153,1
         *              Gasse 3  A -73,00  B -13,12   Laenge  59,9
         *              Gasse 4  A -73,00  B -13,12   Laenge  59,9
         *     nachher  alle vier          B -13,12   Laenge  59,9
         *
         * Die zwei kurzen Gassen zogen die zwei langen um 93 m mit herunter.
         * Der obere Arm der L hatte danach keine Fahrgasse mehr, also auch
         * keine Strassen, keinen Belag und keine Buchten - 133 von 248
         * Buchten und 4.524 m2 Flaeche. Befund des Nutzers: *"dass einfach
         * eine Teilform vom Parkplatz frei bleibt ohne Strassen."*
         *
         * Jetzt werden nur Enden zusammengelegt, die ohnehin fast gleich
         * sind. Die Schranke ist nicht gegriffen, sondern die Buchtbreite,
         * die die Funktion schon kennt: **angeglichen wird nur, was keine
         * Bucht kostet.** Gemessen liegen die echten Faelle bei 0,01 m
         * (Rechteck) und 0,43 m (oberer Arm allein), der Fehlerfall bei 93 m.
         */
        private void PlaneGemeinsameEnden(double mindestlaenge)
        {
            var offen = new HashSet<Weg>(Gassen);
            while (offen.Count > 0)
            {
                var gruppe = new List<Weg> { offen.First() };
                offen.Remove(gruppe[0]);
                for (var i = 0; i < gruppe.Count; i++)
                {
                    var g = gruppe[i];
                    var nachbarn = offen.Where(h => Math.Abs(h.A.Y - g.A.Y) > 1e-6
                        && Math.Min(h.B.X, g.B.X) - Math.Max(h.A.X, g.A.X) >= mindestlaenge).ToArray();
                    foreach (var h in nachbarn) { offen.Remove(h); gruppe.Add(h); }
                }
                GleicheEndenAn(gruppe, true, mindestlaenge);
                GleicheEndenAn(gruppe, false, mindestlaenge);
            }
        }

        /**
         * Sammelt die Enden einer Gruppe zu Haeufchen und gibt jedem Haeufchen
         * einen gemeinsamen Wert - links das groesste, rechts das kleinste,
         * damit keine Gasse ueber die Kontur hinauswaechst.
         *
         * ENTSCHEIDEND IST DIE LUECKE ZWISCHEN ZWEI NACHBARENDEN, nicht der
         * Abstand zum Haeufchen insgesamt.
         *
         * Der erste Versuch am 2026-09-09 hat jedes Ende einzeln am engsten
         * gemessen, ohne Kette. Das war zu grob und `--zufahrtsverlust` fing
         * es sofort: bei 88 Grad wandert das Ende von Gasse zu Gasse um rund
         * 0,6 m, ueber sieben Gassen also 3,7 m - mehr als eine Buchtbreite.
         * Es entstanden drei verschiedene Laengen (113,1 / 112,5 / 109,4) und
         * daraus eine Treppe statt eines geraden Endfusswegs, sechs Stuecke
         * statt zwei.
         *
         * Mit der Kette stimmt beides: aufeinanderfolgende Enden auf DERSELBEN
         * Konturkante liegen nur den Schraeglaufversatz auseinander und
         * wachsen zu einem Haeufchen zusammen; der Sprung auf eine ANDERE
         * Kante - bei der L-Form 93 m - reisst die Kette.
         *
         *     Rechteck 88 Grad, 7 Gassen   Schritte 0,6 m   -> ein Haeufchen
         *     L-Form                       Sprung  93,2 m   -> zwei Haeufchen
         */
        private static void GleicheEndenAn(
            List<Weg> gruppe, bool linkesEnde, double mindestlaenge)
        {
            double Wert(Weg g) => linkesEnde ? g.A.X : g.B.X;
            var sortiert = gruppe.OrderBy(Wert).ToList();
            var i = 0;
            while (i < sortiert.Count)
            {
                var j = i + 1;
                while (j < sortiert.Count
                    && Wert(sortiert[j]) - Wert(sortiert[j - 1]) < mindestlaenge)
                    j++;
                var haeufchen = sortiert.GetRange(i, j - i);
                var bezug = linkesEnde
                    ? haeufchen.Max(Wert)
                    : haeufchen.Min(Wert);
                foreach (var g in haeufchen)
                {
                    // Eine Gasse, die dabei zu kurz wuerde, bleibt wie sie ist.
                    var links = linkesEnde ? bezug : g.A.X;
                    var rechts = linkesEnde ? g.B.X : bezug;
                    if (rechts - links < mindestlaenge) continue;
                    if (linkesEnde) g.A = new Punkt(bezug, g.A.Y);
                    else g.B = new Punkt(bezug, g.B.Y);
                }
                i = j;
            }
        }

        internal IEnumerable<Weg> Korridore => Gassen.Concat(Fusswege).Concat(Zufahrten)
            .Concat(Querwege.Select(q => new Weg { A = q.Anfang, B = q.Ende,
                Breite = q.Querstrasse.Ende - q.Querstrasse.Anfang }));

        internal bool BuchtFrei(Punkt[] ecken, double gassenY)
        {
            var min = ecken.Min(v => v.X); var max = ecken.Max(v => v.X);
            return Gassen.Any(g => Math.Abs(g.A.Y - gassenY) < 1e-6 && min >= g.A.X - 1e-6 && max <= g.B.X + 1e-6)
                && !Fusswege.Concat(Zufahrten).Any(w => Ueberlappt(ecken, w.Ecken));
        }

        internal static bool Ueberlappt(Punkt[] a, Punkt[] b)
        {
            foreach (var poly in new[] { a, b })
                for (var i = 0; i < poly.Length; i++)
                {
                    var d = poly[(i + 1) % poly.Length] - poly[i];
                    var n = new Punkt(-d.Y, d.X);
                    var aa = a.Select(v => Geometrie.Skalar(v, n)).ToArray();
                    var bb = b.Select(v => Geometrie.Skalar(v, n)).ToArray();
                    if (aa.Max() <= bb.Min() + 1e-6 || bb.Max() <= aa.Min() + 1e-6) return false;
                }
            return true;
        }

        /**
         * Reicht eine Zoningflaeche in diesen Korridor hinein?
         *
         * Der Korridor spannt sich von `l` bis `r` in Reihenrichtung und
         * von `y1` bis `y2` quer dazu. Beruehrt ihn eine Zoningflaeche -
         * mit `luft` Zuschlag, weil ihre Strasse aussen herumlaeuft -, dann
         * ist sein Band von ihr zerschnitten und nicht von der Form des
         * Parkplatzes.
         *
         * Genau dann darf die Klemme nicht greifen: sie wuerde die
         * Querstrasse an eine Kante heften, die der Nutzer beim naechsten
         * Verschieben mitnimmt.
         */
        private static bool ZoningZerschneidetKorridor(
            IReadOnlyList<Zoningvorgabe> zoning,
            double l, double r, double y1, double y2, double luft)
        {
            if (zoning == null || zoning.Count == 0) return false;
            var yMin = Math.Min(y1, y2) - luft;
            var yMax = Math.Max(y1, y2) + luft;
            foreach (var z in zoning)
            {
                var ecken = z.Ecken().ToArray();
                if (ecken.Length < 3) continue;
                var zxMin = ecken.Min(p2 => p2.X) - luft;
                var zxMax = ecken.Max(p2 => p2.X) + luft;
                var zyMin = ecken.Min(p2 => p2.Y) - luft;
                var zyMax = ecken.Max(p2 => p2.Y) + luft;
                if (zxMin <= r && zxMax >= l && zyMin <= yMax && zyMax >= yMin)
                    return true;
            }
            return false;
        }

        /**
         * Liegt dieser Weg auf GANZER LAENGE im Streifen neben einer
         * Zoningflaeche?
         *
         * `Frei` fragt nur nach Ueberlappung. Das genuegt fuer ein
         * Hindernis, aber nicht fuer die Zoningstrasse: die laeuft AUSSEN
         * um die Flaeche herum, und was dicht daneben liegt, ueberlappt
         * nichts und ist trotzdem im Weg.
         *
         * ENTSCHEIDEND IST DER GROESSTE ABSTAND, nicht der kleinste.
         * Ansage des Nutzers am 2026-09-21: *"querstrassen duerfen an die
         * Zoningstrasse laufen aber nicht dazwischen liegen, zwischen
         * Fahrtgasse und Zoningstrasse."* Eine Querstrasse, die auf die
         * Zoningstrasse ZULAEUFT, hat ihr Ende dort - der kleinste Abstand
         * ist also nahezu null, und nach ihm gemessen fiele sie faelschlich
         * weg. Ihr fernes Ende liegt aber weit draussen. Was wirklich
         * dazwischenliegt, ist mit ALLEN Ecken im Streifen.
         */
        private static bool LiegtZwischenZoningUndGasse(Weg w,
            IReadOnlyList<Zoningvorgabe> zoning, double abstand)
        {
            if (zoning == null || zoning.Count == 0 || abstand <= 0) return false;
            var wegEcken = w.Ecken;
            if (wegEcken == null || wegEcken.Length == 0) return false;
            foreach (var z in zoning)
            {
                var zEcken = z.Ecken().ToArray();
                if (zEcken.Length < 3) continue;

                var alleDrin = true;
                foreach (var ecke in wegEcken)
                {
                    var naechster = double.MaxValue;
                    for (var j = 0; j < zEcken.Length; j++)
                        naechster = Math.Min(naechster,
                            Geometrie.AbstandPunktStrecke(ecke,
                                zEcken[j], zEcken[(j + 1) % zEcken.Length]));
                    if (naechster < abstand) continue;
                    alleDrin = false;
                    break;
                }
                if (alleDrin) return true;
            }
            return false;
        }

        private static bool Frei(Weg w, IReadOnlyList<Punkt> innen, IReadOnlyList<Zoningvorgabe> zoning)
            => w.Ecken.All(v => Geometrie.EnthaeltOderRand(innen, v)
                || Enumerable.Range(0, innen.Count).Any(i => Geometrie.AbstandPunktStrecke(v, innen[i], innen[(i + 1) % innen.Count]) <= 1e-6))
               && !(zoning ?? Array.Empty<Zoningvorgabe>()).Any(z => Ueberlappt(w.Ecken, z.Ecken().ToArray()));

        private static IEnumerable<(double A, double B)> Waagerecht(IReadOnlyList<Punkt> poly, double y)
            => WaagerechtMitKante(poly, y).Select(s => (s.A, s.B));

        /**
         * Dasselbe wie `Waagerecht`, nur mit der Nummer der Konturkante, die
         * den jeweiligen Rand des Abschnitts erzeugt hat. Siehe `Weg.KanteA`.
         */
        private static IEnumerable<(double A, int KanteA, double B, int KanteB)>
            WaagerechtMitKante(IReadOnlyList<Punkt> poly, double y)
        {
            var treffer = new List<(double X, int Kante)>();
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                if ((a.Y > y) == (b.Y > y)) continue;
                treffer.Add((a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y), i));
            }
            treffer.Sort((l, r) => l.X.CompareTo(r.X));
            for (var i = 0; i + 1 < treffer.Count; i += 2)
                yield return (treffer[i].X, treffer[i].Kante,
                    treffer[i + 1].X, treffer[i + 1].Kante);
        }
    }
}
