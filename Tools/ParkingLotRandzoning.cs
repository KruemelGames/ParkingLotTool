using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * RANDZONING: BAULAND AN EINER LINIE DES UMRISSES.
     *
     * Bedient wird es mit DEMSELBEN Werkzeug wie der Seitenschalter.
     * Ansage des Nutzers am 2026-09-03: *"Zum Erstellen von RZ soll bitte das
     * gleiche Tool genommen werden, womit wir derzeit die Road Side
     * einstellen."* Beides sind Klicks auf etwas Vorhandenes - eine Strasse
     * oder eine Umrisslinie -, und beides braucht denselben Linksklick. Zwei
     * Werkzeuge dafuer waeren ein Schalter, den der Nutzer bedienen muesste,
     * ohne dass ihm die Trennung etwas brächte.
     *
     * WAS NAEHER LIEGT, GEWINNT. Der Zeiger entscheidet, nicht ein Modus.
     *
     * DIE 1-KACHEL-REGEL. Seine Vorgabe: liegt eine Zoningflaeche am Rand und
     * kommt dort Randzoning dazu, rutscht die Flaeche eine Kachel nach innen;
     * geht das nicht, wird abgelehnt und am Zeiger gemeldet. Der Grund ist
     * handfest: das Randzoning-Bauland liegt AUSSERHALB der Randstrasse,
     * genau dort, wo eine randnahe Zoningflaeche noch hinreicht.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** Wie weit vom Zeiger eine Umrisslinie noch angefasst werden kann. */
        private const float RandzoningReichweite = 20f;

        /**
         * Wieviel Platz eine Zoningflaeche zur Umrisskante halten muss, damit
         * dort Randzoning moeglich ist.
         *
         * DER BEZUG IST DIE INNENKANTE DER RZ-STRASSE. Der Nutzer hat das in
         * zwei Schritten berichtigt: erst *"es sollte die innere Seite der
         * Randstrasse als Rand gelten, nicht Randgruen, Polygonlinie oder
         * etwas anderes"*, dann - nachdem klar war, dass die Randstrasse dort
         * gar keine mehr ist - *"dann ist der Rand fuer die 1-Tile-Regel die
         * RZ-Strasse, nicht die Randstrasse."*
         *
         * GEMESSEN WIRD DAS GEZOGENE RECHTECK, ALSO DIE PARZELLEN - der Ring
         * der Flaeche zaehlt NICHT mit. Berichtigung des Nutzers: *"ZF sind
         * die inneren Tiles, nicht die Strasse mit. Das gleiche eben wie beim
         * Polygonrand/Ecke."*
         *
         * Ich hatte hier eine Kachel zu viel aufgeschlagen. Am Polygonrand
         * duerfen die Parzellen bis ans Randgruen; was dort nicht mehr
         * hinpasst, ist die Strasse, und die WEICHT - nicht die Flaeche.
         * Genauso hier: die Parzellen duerfen bis an die Innenkante der
         * RZ-Strasse, und deren Platz nimmt der Ring dann gar nicht erst in
         * Anspruch. Beide teilen sich diese eine Strasse.
         *
         * Von der Umrisskante nach innen: 10,4 m bis zur Achse der
         * RZ-Strasse, plus 4 m halbe Strassenbreite ergibt ihre Innenkante
         * bei 14,4 m.
         */
        private static readonly double Randzoningabstand = 10.4 + 4.0;

        private readonly List<ParkingGeometry.RandzoningLinie> _randzoning
            = new List<ParkingGeometry.RandzoningLinie>();

        /** Welche Umrisslinie gerade unter dem Zeiger liegt. -1 wenn keine. */
        private int _randzoningZiel = -1;

        internal IReadOnlyList<ParkingGeometry.RandzoningLinie> Randzoninglinien
            => _randzoning;

        /** Vom Bauzettel oder beim Zuruecksetzen. */
        internal void SetzeRandzoning(
            IReadOnlyList<ParkingGeometry.RandzoningLinie> linien)
        {
            _randzoning.Clear();
            if (linien == null) return;
            foreach (var l in linien)
                if (l != null) _randzoning.Add(l.Clone());
        }

        internal void VergissRandzoning() => _randzoning.Clear();

        /**
         * DIE RANDZONING-LINIEN FOLGEN IHRER POLYGONKANTE.
         *
         * Eine Linie wird mit den Endpunkten der Kante gemerkt, auf die der
         * Nutzer geklickt hat - als absolute Weltpunkte. Verschiebt er danach
         * einen Polygonpunkt, wandert die KANTE, die gemerkte Linie aber
         * nicht. Sie deckt die Kante dann nur noch teilweise ab oder liegt
         * ganz daneben.
         *
         * Der Nutzer am 2026-09-04: *"Ich habe nur die Polygonlinie
         * verschoben, die 90 Grad von der RZ liegt, und ploetzlich wird an der
         * RZ eine Strasse nicht gesetzt sowie ploetzlich eine Gruenflaeche
         * erstellt, die da nicht sein duerfte."*
         *
         * Beides folgt daraus: wo kein Randzoning mehr gilt, entsteht keine
         * Zoning-Strasse, und der Abschnitt faellt auf die normale Randstrasse
         * samt Randgruen zurueck. In seinen zwei Bauzetteln stand es
         * schwarz auf weiss - Polygon bis x = -1189, RZ-Linie weiter bei
         * x = -1163.
         *
         * Die Linie gehoert also an die KANTE gebunden, nicht an Koordinaten.
         * Ein fester Kantenindex reicht nicht, weil Punkte eingefuegt und
         * geloescht werden koennen; deshalb wird die naechstgelegene Kante
         * gesucht. Das ist verlaesslich, weil hier bei JEDER Umrissaenderung
         * nachgezogen wird - der Versatz je Schritt ist winzig.
         *
         * Findet sich keine passende Kante mehr, faellt die Linie weg. Eine
         * Auswahl, die auf nichts mehr zeigt, still stehenzulassen waere
         * schlimmer: sie wuerde irgendwo im Parkplatz eine Strasse ersetzen.
         */
        private void RandzoningFolgeDemUmriss()
        {
            if (_randzoning.Count == 0 || !_closed || _points.Count < 3) return;

            var verloren = 0;
            for (var i = _randzoning.Count - 1; i >= 0; i--)
            {
                var linie = _randzoning[i];
                var mitte = (linie.A + linie.B) * 0.5f;
                var richtung = linie.B - linie.A;
                var laenge = math.length(richtung);
                if (laenge < 1e-3f) { _randzoning.RemoveAt(i); verloren++; continue; }
                richtung /= laenge;

                /*
                 * EINE GETEILTE KANTE BLEIBT EINE AUSWAHL - IN ZWEI STUECKEN.
                 *
                 * Hier wurde nur die EINE naechste Kante gesucht und die
                 * gemerkte Linie darauf geschnappt. Setzt der Nutzer einen
                 * Punkt mitten auf eine Randzoning-Kante, schrumpft sie damit
                 * auf eine Haelfte - Astra hat am 2026-09-09 gemessen:
                 * 178,912277 m gebaute Zoning-Strasse werden 121,804596 m,
                 * also 57,1 m weg, ohne dass der Nutzer die Auswahl angefasst
                 * haette.
                 *
                 * Deshalb zuerst: welche Umrisskanten liegen NOCH auf der
                 * gemerkten Linie? Sind es mehrere, treten sie an ihre Stelle
                 * - eine Auswahl je Kante, zusammen wieder die volle Laenge.
                 * Erst wenn keine mehr darauf liegt, greift die Suche nach der
                 * naechsten unten: dann hat der Nutzer die Kante VERSCHOBEN,
                 * und die Auswahl soll ihr folgen.
                 */
                var aufLinie = new List<int>();
                for (var k = 0; k < _points.Count; k++)
                    if (ParkingGeometry.RandzoningStueckAufLinie(
                            linie.A, linie.B, _points[k],
                            _points[(k + 1) % _points.Count]))
                        aufLinie.Add(k);
                if (aufLinie.Count > 1)
                {
                    _randzoning.RemoveAt(i);
                    foreach (var k in aufLinie)
                        _randzoning.Insert(i, new ParkingGeometry.RandzoningLinie
                        {
                            A = _points[k],
                            B = _points[(k + 1) % _points.Count],
                        });
                    continue;
                }

                var beste = -1;
                var besterAbstand = float.MaxValue;
                for (var k = 0; k < _points.Count; k++)
                {
                    var a = _points[k];
                    var b = _points[(k + 1) % _points.Count];
                    var d = b - a;
                    var l = math.length(d);
                    if (l < 1e-3f) continue;
                    // Nur Kanten, die noch ungefaehr in dieselbe Richtung
                    // zeigen - sonst rutscht die Auswahl bei einer
                    // Punkteinfuegung auf die Nachbarkante.
                    if (math.abs(math.dot(d / l, richtung)) < 0.7f) continue;
                    var abstand = ParkingGeometry.ZoningPunktStrecke(mitte, a, b);
                    if (abstand >= besterAbstand) continue;
                    besterAbstand = abstand;
                    beste = k;
                }

                // Eine halbe Buchtreihe Toleranz. Wer die Kante weiter zieht,
                // hat sie in einem Bild ohnehin schrittweise mitgenommen.
                if (beste < 0 || besterAbstand > 12f)
                {
                    _randzoning.RemoveAt(i);
                    verloren++;
                    continue;
                }

                var neuA = _points[beste];
                var neuB = _points[(beste + 1) % _points.Count];
                if (math.distance(linie.A, neuA) < 1e-3f
                    && math.distance(linie.B, neuB) < 1e-3f) continue;
                linie.A = neuA;
                linie.B = neuB;
            }

            if (verloren > 0)
                _uiSystem?.SetStatus(T(
                    verloren + " Randzoning-Linie(n) entfallen: die Kante gibt "
                        + "es nicht mehr.",
                    verloren + " edge zoning line(s) dropped: their edge is "
                        + "gone."));
        }

        /** Ist an dieser Umrisslinie schon Randzoning? */
        /** Liegt an dieser Umrisslinie schon eine Randzoninglinie? */
        private bool RandzoningLiegtAn(int kante)
        {
            if (kante < 0 || kante >= _points.Count) return false;
            return RandzoningIndex(_points[kante],
                _points[(kante + 1) % _points.Count]) >= 0;
        }

        private int RandzoningIndex(float2 a, float2 b)
        {
            for (var i = 0; i < _randzoning.Count; i++)
                if (ParkingGeometry.RandzoningSelbeLinie(
                        _randzoning[i].A, _randzoning[i].B, a, b))
                    return i;
            return -1;
        }

        /**
         * Sucht die naechste Umrisslinie unter dem Zeiger.
         *
         * Rueckgabe ist ihr Index im Umriss, oder -1. Der Abstand kommt
         * mit heraus, damit der Aufrufer ihn gegen den Abstand zur naechsten
         * Zoning-Strasse halten kann - naeher gewinnt.
         */
        private int SucheUmrisslinie(out float abstand)
        {
            abstand = float.PositiveInfinity;
            if (!_closed || _points.Count < 3) return -1;
            if (!_letzteWeltpositionGueltig) return -1;

            var welt = _letzteWeltposition;
            var zeiger = new float2(welt.x, welt.z);
            var treffer = -1;
            for (var i = 0; i < _points.Count; i++)
            {
                var a = _points[i];
                var b = _points[(i + 1) % _points.Count];
                var d = b - a;
                var laenge = math.lengthsq(d);
                if (laenge < 1e-6f) continue;
                var t = math.clamp(math.dot(zeiger - a, d) / laenge, 0f, 1f);
                var nah = math.distance(zeiger, a + d * t);
                if (nah >= abstand) continue;
                abstand = nah;
                treffer = i;
            }
            return abstand <= RandzoningReichweite ? treffer : -1;
        }

        /**
         * Schaltet das Randzoning an dieser Umrisslinie um.
         *
         * Beim EINSCHALTEN wird zuerst versucht, im Weg stehende
         * Zoningflaechen eine Kachel nach innen zu ruecken. Klappt das nicht,
         * passiert gar nichts - und der Zeigertext sagt warum.
         */
        private void SchalteRandzoning(int kante)
        {
            if (kante < 0 || kante >= _points.Count) return;
            var a = _points[kante];
            var b = _points[(kante + 1) % _points.Count];

            var vorhanden = RandzoningIndex(a, b);
            var vorher = CaptureUndoState();
            if (vorhanden >= 0)
            {
                _randzoning.RemoveAt(vorhanden);
                NachZoningaenderung(vorher, "Randzoning entfernt");
                return;
            }

            // Eine Zufahrt quert das ganze Aussenband - dort kann kein
            // Bauland entstehen, und verschieben laesst sie sich nicht.
            if (ZufahrtAufLinie(kante))
            {
                _uiSystem?.SetStatus(T(
                    "Hier liegt eine Zufahrt oder ein Fußweg — erst "
                        + "verschieben oder entfernen.",
                    "An entrance or footpath sits here — move or remove it "
                        + "first."));
                return;
            }

            if (!RaeumeFuerRandzoning(a, b))
            {
                _uiSystem?.SetStatus(T(
                    "Hier ist kein Platz: eine Zoning-Fläche steht an der "
                        + "Kante und kann nicht ausweichen.",
                    "No room here: a zoning patch sits on this edge and "
                        + "cannot move aside."));
                return;
            }

            _randzoning.Add(new ParkingGeometry.RandzoningLinie { A = a, B = b });
            if (ParkingGeometry.LiveAn)
                ParkingGeometry.Live("  randzoning gesetzt | kante " + kante
                    + " | jetzt " + _randzoning.Count + " Linie(n) | flaechen "
                    + _zoningflaechen.Count);
            NachZoningaenderung(vorher, "Randzoning gesetzt");
        }

        /**
         * Schiebt Zoningflaechen aus dem Randzoning-Streifen - eine Kachel je
         * betroffener Achse.
         *
         * Rueckgabe: falsch, wenn eine Flaeche nicht ausweichen kann. Dann
         * bleibt ALLES unveraendert; ein halb ausgefuehrtes Ausweichen waere
         * schlimmer als gar keins.
         *
         * Die Regel stammt vom Nutzer: *"Wenn ZF an Rand/Ecke liegt und eine
         * RF erstellt wird, dann soll die ZF um 1 Tile in die Richtung
         * verschoben werden - bei Ecke nach innen in 2 Achsen um 1 Tile."*
         * Die zweite Achse ergibt sich hier von selbst: an einer Ecke ist die
         * Flaeche ZWEI Linien zu nah, und jede schiebt auf ihrer eigenen
         * Achse.
         */
        private bool RaeumeFuerRandzoning(float2 a, float2 b)
        {
            if (!PruefeRandzoning(a, b, out var entwurf)) return false;
            SetzeZoningflaechen(entwurf);
            return true;
        }

        /**
         * DIESELBE RECHNUNG, ABER OHNE SIE AUSZUFUEHREN.
         *
         * Der Nutzer will die Warnung am Zeiger, nicht nach dem Klick:
         * *"Dann kann RF oder ZF nicht erstellt werden, und das wird dem
         * Nutzer gemeldet per Cursor-Hinweis (farbig)."* Eine Meldung, die
         * erst nach dem Klick kommt, erklaert etwas, das schon passiert ist -
         * oder eben nicht passiert ist, und dann steht der Nutzer da und
         * klickt nochmal.
         *
         * Deshalb ist das Pruefen vom Ausfuehren getrennt. Die Vorschau ruft
         * es jeden Frame, der Klick genau einmal.
         */
        private bool PruefeRandzoning(float2 a, float2 b,
            out List<ParkingGeometry.Zoningflaeche> entwurfAus)
        {
            entwurfAus = null;
            var kachel = (float)ParkingGeometry.Zoningparzelle;
            if (math.distance(a, b) < 1e-3f) return false;

            /*
             * JEDE LINIE SCHIEBT AUF IHRER EIGENEN ACHSE - wie ein Kolben.
             *
             * Das Bild stammt vom Nutzer: *"Stell dir einfach einen Kolben
             * aus Minecraft vor, der nen Block verschiebt."* Genau so ist es
             * gemeint, und genau daran fehlte es: geschoben wurde nur entlang
             * der NEUEN Linie. Steht die Flaeche in der ECKE zweier Linien,
             * muss sie auf BEIDEN Achsen weichen - der Nutzer: *"Beim
             * Verschieben aus der Ecke heraus muss das in beide Richtungen
             * funktionieren, nicht nur in x oder y."*
             *
             * Die alte Fassung brach in diesem Fall mit "kein Platz" ab,
             * obwohl eine Kachel gereicht haette: die zweite Linie wurde
             * geprueft, aber nie zum Schieben benutzt.
             *
             * Deshalb liegen jetzt ALLE Linien in einem Topf - die schon
             * gesetzten und die neue -, und in jedem Durchgang schiebt die
             * erste, die noch verletzt ist. Das laeuft, bis keine mehr
             * verletzt ist.
             */
            var linien = new List<(float2 A, float2 B)>();
            foreach (var l in _randzoning) linien.Add((l.A, l.B));
            linien.Add((a, b));

            var lotmitte = float2.zero;
            foreach (var p in _points) lotmitte += p;
            if (_points.Count > 0) lotmitte /= _points.Count;

            /** Die Richtung, in die diese Linie schiebt: nach innen. */
            (float2 Richtung, float2 Normale) Achse((float2 A, float2 B) l)
            {
                var d = l.B - l.A;
                var richtung = d / math.length(d);
                var normale = new float2(-richtung.y, richtung.x);
                if (math.dot(normale, lotmitte - l.A) < 0) normale = -normale;
                return (richtung, normale);
            }

            var entwurf = new List<ParkingGeometry.Zoningflaeche>();
            foreach (var f in _zoningflaechen) entwurf.Add(f.Clone());

            /*
             * DER KOLBEN SCHIEBT AUCH DIE DAHINTER.
             *
             * Frage des Nutzers: *"Wenn mehrere ZF an einer Polygonlinie
             * liegen und aneinander eingerastet sind - werden dann alle
             * verschoben und bleiben zueinander eingerastet?"*
             *
             * Fuer eine REIHE nebeneinander galt das schon: sie stehen alle
             * gleich weit von der Linie, brauchen also gleich viele Kacheln,
             * und ihr Abstand zueinander bleibt. Steht eine Flaeche aber
             * HINTER einer anderen, lief die vordere in sie hinein - und die
             * alte Fassung brach dann mit "kein Platz" ab, statt die hintere
             * mitzunehmen.
             *
             * Ein Kolben tut genau das. Also tut es dieser jetzt auch: wer
             * beim Ausweichen jemanden anstoesst, schiebt ihn in derselben
             * Richtung weiter. Das Raster bleibt dabei erhalten, weil alle in
             * ganzen Kacheln ruecken.
             */
            /*
             * JEDE FLAECHE RUECKT JE STOSS HOECHSTENS EINMAL.
             *
             * Ohne diese Sperre schoeben sich zwei Flaechen, die einander
             * ueberlappen, gegenseitig immer weiter - A stoesst B, B stoesst
             * A, und beide wandern bis an den Umriss. Ein Kolben schiebt die
             * Kette auch nur um einen Block.
             */
            bool Schiebe(int index, float2 normale, HashSet<int> schonBewegt)
            {
                if (!schonBewegt.Add(index)) return true;
                entwurf[index] = ParkingGeometry.ZoningVerschoben(
                    entwurf[index], normale * kachel);
                if (!ZoningLiegtImUmriss(entwurf[index], _points.ToArray(),
                        Randgruentiefe)) return false;

                // Wen ich dabei angestossen habe, nehme ich mit.
                for (var k = 0; k < entwurf.Count; k++)
                {
                    if (k == index) continue;
                    if (!ParkingGeometry.ZoningRechteckeUeberlappen(
                            ParkingGeometry.ZoningEcken(entwurf[index]),
                            ParkingGeometry.ZoningEcken(entwurf[k]))) continue;
                    if (!Schiebe(k, normale, schonBewegt)) return false;
                }
                return true;
            }

            // 64 Schritte reichen fuer eine lange Kette ueber zwei Achsen;
            // mehr waere kein Ausweichen mehr, sondern Verschieben.
            for (var schritt = 0; schritt <= 64; schritt++)
            {
                var geschoben = false;
                foreach (var l in linien)
                {
                    if (math.distance(l.A, l.B) < 1e-3f) continue;
                    var (richtung, normale) = Achse(l);
                    for (var i = 0; i < entwurf.Count; i++)
                    {
                        if (!RandzoningZuNah(entwurf[i], l.A, l.B,
                                richtung, normale)) continue;
                        if (!Schiebe(i, normale, new HashSet<int>()))
                            return false;
                        geschoben = true;
                    }
                }
                if (!geschoben) break;
                if (schritt == 64)
                {
                    if (ParkingGeometry.LiveAn)
                        ParkingGeometry.Live("  randzoning kolben | nach 64 "
                            + "Schritten immer noch im Weg - abgelehnt");
                    return false;
                }
            }

            /*
             * WIEVIEL DER KOLBEN GESCHOBEN HAT.
             *
             * Ohne diese Zeile ist "kein Platz" nicht von "hat geklappt, aber
             * anders als erwartet" zu unterscheiden - und genau darum ging es
             * bei den letzten drei Befunden.
             */
            if (ParkingGeometry.LiveAn)
                for (var i = 0; i < entwurf.Count; i++)
                {
                    var weg = math.distance(
                        entwurf[i].Ecke, _zoningflaechen[i].Ecke);
                    if (weg < 0.01f) continue;
                    ParkingGeometry.Live("  randzoning kolben | flaeche " + i
                        + " um " + ParkingLotLiveLog.Zahl(weg) + " m geschoben");
                }

            // Erst wenn ALLE frei sind, wird uebernommen - und dann muessen
            // sie sich auch untereinander noch vertragen. Eine Flaeche, die
            // ausweicht, koennte sonst in ihre Nachbarin laufen.
            for (var i = 0; i < entwurf.Count; i++)
                for (var k = i + 1; k < entwurf.Count; k++)
                    if (ParkingGeometry.ZoningRechteckeUeberlappen(
                            ParkingGeometry.ZoningEcken(entwurf[i]),
                            ParkingGeometry.ZoningEcken(entwurf[k])))
                        return false;

            entwurfAus = entwurf;
            return true;
        }

        /**
         * Steht diese Flaeche dem Randzoning an dieser Linie im Weg?
         *
         * DER LAENGSBEREICH IST NACH BEIDEN SEITEN BEGRENZT. Vorher war er
         * es nur nach vorne - eine Flaeche weit hinter dem Ende der Linie
         * galt damit noch als betroffen. An einer Ecke, an der eine Linie
         * Randzoning hat und die andere nicht, blockierte die eine also
         * Flaechen, die gar nicht an ihr liegen. Genau danach hat der Nutzer
         * gefragt: *"Wenn ich eine Ecke habe, die eine Linie RZ hat und eine
         * Linie nicht, funktioniert dann beides?"*
         *
         * Eine Kachel Zugabe an jedem Ende deckt die Ecke selbst mit ab -
         * dort gehoert die Flaeche zu BEIDEN Linien, und beide Regeln gelten
         * nebeneinander.
         */
        private static bool RandzoningZuNah(ParkingGeometry.Zoningflaeche f,
            float2 a, float2 b, float2 richtung, float2 normale)
        {
            var laenge = math.distance(a, b);
            var kachel = (float)ParkingGeometry.Zoningparzelle;
            foreach (var ecke in ParkingGeometry.ZoningEcken(f))
            {
                var w = ecke - a;
                // Nur was AUF DER HOEHE der Linie liegt, ist betroffen.
                var laengs = math.dot(w, richtung);
                if (laengs < -kachel || laengs > laenge + kachel) continue;
                if (math.dot(w, normale) < Randzoningabstand) return true;
            }
            return false;
        }

        /**
         * Haelt diese Flaeche zu ALLEN gesetzten Randzoning-Linien Abstand?
         *
         * Das ist die zweite Haelfte der Regel des Nutzers. Die erste -
         * *"wenn ZF an Rand liegt und eine RZ erstellt wird, rutscht die ZF
         * eine Kachel nach innen"* - stand schon. Die zweite fehlte:
         * *"Wenn RZ besteht und ZF erstellt wird, darf sie nur ran bis auf
         * 1 Tile Abstand."*
         *
         * Ohne sie war die Regel nur in EINER Richtung wirksam: wer zuerst
         * das Randzoning setzte und danach die Flaeche zog, kam ungehindert
         * bis ans Randgruen.
         */
        internal bool HaeltRandzoningAbstand(ParkingGeometry.Zoningflaeche f)
        {
            if (_randzoning.Count == 0) return true;
            var lotmitte = float2.zero;
            foreach (var p in _points) lotmitte += p;
            if (_points.Count > 0) lotmitte /= _points.Count;

            foreach (var linie in _randzoning)
            {
                var d = linie.B - linie.A;
                var laenge = math.length(d);
                if (laenge < 1e-3f) continue;
                var richtung = d / laenge;
                var normale = new float2(-richtung.y, richtung.x);
                if (math.dot(normale, lotmitte - linie.A) < 0) normale = -normale;
                if (RandzoningZuNah(f, linie.A, linie.B, richtung, normale))
                    return false;
            }
            return true;
        }

        /**
         * WO EINE ZUFAHRT LIEGT, DARF KEIN BAULAND SEIN - UND UMGEKEHRT.
         *
         * Ansage des Nutzers: *"Zufahrten sowie Fusswege an ZF und RZ
         * verhindern bzw. andersherum."*
         *
         * Der Grund ist derselbe wie bei den Buchten: eine Zufahrt fuehrt
         * vom Rand ins Innere, und Bauland an derselben Stelle hiesse ein
         * Haus auf der Einfahrt. Beim Randzoning trifft es zwangslaeufig
         * zu - sein Bauland belegt das GANZE Aussenband der Linie, und genau
         * dort quert jede Zufahrt.
         *
         * DER KORRIDOR wird aus der Kante gerechnet, an der die Zufahrt
         * haengt: Punkt auf der Kante, Richtung nach innen, Breite nach Art.
         * Ein Meter Zuschlag je Seite, damit ein Haus nicht auf Tuchfuehlung
         * an der Fahrbahn steht.
         */
        private const float Zufahrtsluft = 1f;

        private bool ZufahrtsRechteck(Entrance zufahrt, out float2[] ecken)
        {
            ecken = null;
            if (zufahrt == null || !_closed || _points.Count < 3) return false;
            if (zufahrt.Edge < 0 || zufahrt.Edge >= _points.Count) return false;

            var a = _points[zufahrt.Edge];
            var b = _points[(zufahrt.Edge + 1) % _points.Count];
            var d = b - a;
            var laenge = math.length(d);
            if (laenge < 1e-3f) return false;
            var richtung = d / laenge;
            var normale = new float2(-richtung.y, richtung.x);
            var mitte = float2.zero;
            foreach (var p in _points) mitte += p;
            mitte /= _points.Count;
            if (math.dot(normale, mitte - a) < 0) normale = -normale;

            var punkt = a + richtung
                * (float)math.clamp(zufahrt.Along, 0.0, laenge);
            var e = _uiSystem?.CurrentSettings();
            var breite = (float)zufahrt.Breite(e?.Ai ?? 7.0) * 0.5f
                + Zufahrtsluft;
            // Bis zur Randstrassenachse - weiter reicht keine Zufahrt.
            var tiefe = (float)(e == null ? 10.4 : e.Es + e.Sl + e.Ai / 2);

            ecken = new[]
            {
                punkt - richtung * breite,
                punkt + richtung * breite,
                punkt + richtung * breite + normale * tiefe,
                punkt - richtung * breite + normale * tiefe,
            };
            return true;
        }

        /** Steht eine Zufahrt dieser Zoningflaeche im Weg? */
        internal bool ZufahrtTrifftFlaeche(ParkingGeometry.Zoningflaeche f)
        {
            if (_entrances.Count == 0) return false;
            var ecken = ParkingGeometry.ZoningEcken(f);
            foreach (var zufahrt in _entrances)
                if (ZufahrtsRechteck(zufahrt, out var korridor)
                    && ParkingGeometry.ZoningRechteckeUeberlappen(
                        ecken, korridor)) return true;
            return false;
        }

        /** Liegt eine Zufahrt an dieser Umrisslinie? */
        private bool ZufahrtAufLinie(int kante)
        {
            foreach (var zufahrt in _entrances)
                if (zufahrt != null && zufahrt.Edge == kante) return true;
            return false;
        }

        /**
         * Die Gegenprobe beim SETZEN einer Zufahrt: liegt dort Bauland?
         *
         * Zwei Faelle. Am Randzoning ist es einfach - die ganze Linie ist
         * belegt. Bei einer Zoningflaeche entscheidet, ob der Korridor sie
         * trifft.
         */
        internal bool ZufahrtTrifftBauland(Entrance zufahrt)
        {
            if (zufahrt == null) return false;
            if (zufahrt.Edge >= 0 && zufahrt.Edge < _points.Count
                && _randzoning.Count > 0)
            {
                var a = _points[zufahrt.Edge];
                var b = _points[(zufahrt.Edge + 1) % _points.Count];
                if (RandzoningIndex(a, b) >= 0) return true;
            }
            if (_zoningflaechen.Count == 0) return false;
            if (!ZufahrtsRechteck(zufahrt, out var korridor)) return false;
            foreach (var f in _zoningflaechen)
                if (ParkingGeometry.ZoningRechteckeUeberlappen(
                        ParkingGeometry.ZoningEcken(f), korridor)) return true;
            return false;
        }

        /**
         * Liegt diese gebaute Kante in einem Randzoning-Abschnitt?
         *
         * Gebraucht bei der Seitenwahl: dort zont nur die Aussenseite.
         */
        private bool LiegtImRandzoning(float2 a, float2 b)
            => _randzoning.Count > 0
                && ParkingGeometry.RandzoningEnthaelt(_randzoning, a, b);

        /** Fuer das Overlay: welche Umrisslinie gerade gemeint ist. */
        internal bool RandzoningVorschau(out float2 a, out float2 b,
            out int zustand)
        {
            a = default;
            b = default;
            // 0 = frei, 1 = schon gesetzt, 2 = blockiert
            zustand = 0;
            if (!ZoningSeitenModus || _randzoningZiel < 0) return false;
            if (_randzoningZiel >= _points.Count) return false;
            a = _points[_randzoningZiel];
            b = _points[(_randzoningZiel + 1) % _points.Count];
            if (RandzoningIndex(a, b) >= 0) zustand = 1;
            else if (!PruefeRandzoning(a, b, out _)) zustand = 2;
            return true;
        }
    }
}
