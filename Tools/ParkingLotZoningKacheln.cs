using System;
using System.Collections.Generic;
using System.Linq;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * DIE KACHELN KOMMEN AUS DEN STRASSEN, NICHT AUS DER FLAECHE.
     *
     * Bis zum 2026-09-03 zeichnete die Vorschau ihr Gitter ueber das
     * gezogene Rechteck und den aeusseren Ring - unabhaengig davon, ob dort
     * ueberhaupt eine Strasse Kacheln erzeugt. Sobald der Nutzer eine
     * Strassenseite abschaltete, log das Bild.
     *
     * Seine Bedingung nennt auch gleich die Falle: *"Wichtig ist aber, dass
     * Tiles nicht verschwinden duerfen, wenn eine andere Strasse welche
     * erzeugt, zum Beispiel gegenueberliegende oder an einer inneren
     * Ecke."* Eine Kachel gehoert also nicht EINER Seite, sondern bleibt,
     * solange IRGENDEINE eingeschaltete Seite sie erreicht.
     *
     * Deshalb wird je Zelle gefragt: erreicht mich eine eingeschaltete
     * Strassenseite? Das ist die Vereinigung, und sie kennt keine
     * Reihenfolge - eine gegenueberliegende Strasse haelt dieselbe Kachel am
     * Leben wie die naechste.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** CS2 laesst Haeuser hoechstens sechs Zellen tief wachsen. */
        private const int ZoningTiefeGrenze = 6;

        /**
         * Die geplanten Zoning-Strassen mit dem Zustand beider Seiten.
         *
         * Vor dem Bauen gibt es nur den Plan; dann gilt die Panelwahl. Steht
         * die Strasse schon, gilt ihr echter Zustand - nach einer
         * Handschaltung sind beide verschieden, und wahr ist die Strasse.
         */
        internal List<(float2 A, float2 B, bool LinksAn, bool RechtsAn)>
            ZoningStrassenMitSeiten()
        {
            /*
             * DER PLAN WIRD HIER GERECHNET, NICHT AUS DEM BAULAUF GELESEN.
             *
             * Zuerst stand hier `_areaPreviewLayout.NetLine`. Das ist das
             * Ergebnis des Hintergrundlaufs, der den ganzen Parkplatz
             * durchrechnet - und damit beim Ziehen IMMER einen Stand zu alt.
             * Die Kacheln blieben also an der alten Stelle stehen, waehrend
             * die Flaeche schon weiter war; beim Aufziehen einer neuen
             * Flaeche gab es ueberhaupt keinen Lauf und deshalb gar keine
             * Kacheln. Beides hat der Nutzer am 2026-09-03 gemeldet.
             *
             * Das Strassennetz allein kostet nichts - es sind vier Achsen je
             * Flaeche, geschnitten und verschmolzen. Also wird es jeden Frame
             * neu geplant, und die Kacheln haengen am Zeiger statt am
             * letzten fertigen Lauf.
             */
            /*
             * KEIN FRUEHAUSSTIEG MEHR, WENN ES KEINE FLAECHE GIBT.
             *
             * Randzoning kommt ohne gezogene Flaeche aus - und wer hier
             * aussteigt, liefert dann gar keine Strasse, also auch keine
             * Kachel und keinen Anhaltspunkt fuer die 1-Kachel-Regel.
             * Dieselbe Falle hatte am 2026-09-04 schon die Seitenwahl.
             */
            var flaechen = ZoningflaechenMitZug();
            var rzAchsen = RandzoningStrassenplan();
            var netz = flaechen.Count == 0
                ? null
                : ParkingGeometry.ZoningStrassenVorschau(
                    flaechen, _closed && _points.Count >= 3 ? _points : null,
                    Randgruentiefe,
                    // Die Zoningflaechen-Vorschau braucht nur die Achsen; die
                    // Innenrichtung ist Sache der Schleife weiter unten.
                    rzAchsen.Select(r => (r.A, r.B)).ToArray());

            var gebaute = GebauteZoningseiten();
            var alsFeld = flaechen.ToArray();
            List<(float2, float2, bool, bool)> ergebnis = null;

            for (var i = 0; netz != null && i < netz.Count; i++)
            {
                var a = netz[i].A;
                var b = netz[i].B;
                var richtung = b - a;
                if (math.lengthsq(richtung) < 1e-6f) continue;

                bool linksAn;
                bool rechtsAn;
                if (!SucheGebauteSeite(gebaute, a, b, out linksAn, out rechtsAn))
                {
                    // Kein gebautes Gegenstueck: es gilt die Panelwahl.
                    var mitte = (a + b) * 0.5f;
                    var innen = NaechsteZoningmitte(alsFeld, mitte);
                    var zurMitte = innen - mitte;
                    var innenIstLinks =
                        richtung.x * zurMitte.y - richtung.y * zurMitte.x > 0f;
                    var seite = ZoningSeite;
                    var innenAn = seite != ParkingGeometry.Zoningseite.Aussen;
                    var aussenAn = seite != ParkingGeometry.Zoningseite.Innen;
                    linksAn = innenIstLinks ? innenAn : aussenAn;
                    rechtsAn = innenIstLinks ? aussenAn : innenAn;
                }

                // ZULETZT die Handschaltungen - sie sind die Abweichung von
                // dem, was Panelwahl oder gebauter Zustand vorgeben, und
                // muessen deshalb obenauf liegen.
                WendeZoningSeitenplanAn(a, b, ref linksAn, ref rechtsAn);

                ergebnis ??= new List<(float2, float2, bool, bool)>();
                ergebnis.Add((a, b, linksAn, rechtsAn));
            }

            /*
             * DIE RZ-STRASSEN GEHOEREN IN DENSELBEN PLAN.
             *
             * Ohne sie fehlte in der Vorschau genau das, woran sich alles
             * andere ausrichtet - der Nutzer: *"Das ist, als willst du X + Y
             * rechnen, aber entfernst Y."* Sie sind auch die Strassen, aus
             * denen die Kacheln des Randzonings entstehen; ohne sie zeigte
             * die Vorschau dort gar keine.
             *
             * IHRE INNENSEITE IST IMMER AUS. Nach innen liegt der Parkplatz,
             * und dort will der Nutzer keine Kacheln.
             */
            foreach (var rz in rzAchsen)
            {
                var richtung = rz.B - rz.A;
                if (math.lengthsq(richtung) < 1e-6f) continue;

                /*
                 * WO INNEN IST, SAGT DER PLAN - NICHT DIE POLYGONMITTE.
                 *
                 * Hier wurde gefragt, ob der Schwerpunkt des Polygons links
                 * oder rechts der Fahrtrichtung liegt. Bei EINER Strasse
                 * entlang einer Kante geht das immer gut. Bei einer TREPPE
                 * nicht: eine Stufe kann so weit innen liegen, dass der
                 * Schwerpunkt auf ihrer anderen Seite steht - dann zont sie
                 * in den Parkplatz hinein. Der Nutzer: *"Nicht alle Tiles
                 * gehen nach aussen, da findet keine ordentliche Pruefung
                 * statt."*
                 *
                 * `Innen` kommt aus dem Abschnitt und steht senkrecht auf der
                 * Gasse, von der bedienten Kante weg. Die Polygonmitte bleibt
                 * als Rueckfall fuer den Weg mit Randstrassen, wo es keine
                 * Abschnitte gibt.
                 */
                float2 innenrichtung;
                if (math.lengthsq(rz.Innen) > 1e-6f)
                {
                    innenrichtung = rz.Innen;
                }
                else
                {
                    var mitte = (rz.A + rz.B) * 0.5f;
                    var lotmitte = float2.zero;
                    foreach (var p in _points) lotmitte += p;
                    if (_points.Count > 0) lotmitte /= _points.Count;
                    innenrichtung = lotmitte - mitte;
                }
                var innenLinks =
                    richtung.x * innenrichtung.y
                    - richtung.y * innenrichtung.x > 0f;

                /*
                 * KEINE HANDSCHALTUNG AN EINER RZ-STRASSE.
                 *
                 * Hier stand `WendeZoningSeitenplanAn` - damit liess sich die
                 * Innenseite doch noch einschalten, und die Vorschau zeigte
                 * dann Kacheln im Parkplatz. Der Nutzer: *"Wenn ich auf die
                 * Linie vom Polygon klicke, soll es kein Innen oder Aussen
                 * geben, sondern NUR aussen."*
                 *
                 * Es ist keine Wahl, sondern die Bauart: nach innen liegt der
                 * Parkplatz. Eine Einstellmoeglichkeit dafuer waere eine, die
                 * nur falsch benutzt werden kann.
                 */
                ergebnis ??= new List<(float2, float2, bool, bool)>();
                ergebnis.Add((rz.A, rz.B, !innenLinks, innenLinks));
            }
            return ergebnis;
        }

        /**
         * Die Randstrassenstuecke, die durch Randzoning zu Zoning-Strassen
         * geworden sind - live gerechnet wie der uebrige Plan.
         *
         * Die Tiefe kommt aus den Reglern: Randabstand plus Buchttiefe ergibt
         * die aeussere Fahrbahnkante, plus die halbe Fahrgasse die Achse.
         * Dieselbe Rechnung wie im Zellenkern
         * (`Randstrassenmittellinientiefe`); sie hier nachzubauen ist der
         * Preis dafuer, dass die Vorschau nicht auf den Baulauf wartet.
         */
        /**
         * Fuer das Overlay: die Achsen der RZ-Strassen.
         *
         * NICHT die Polygonlinien - die liegen 10,4 m weiter aussen. Wer die
         * Kacheln von dort rastert, setzt sie eine Buchtreihe daneben.
         */
        internal IReadOnlyList<(float2 A, float2 B, float2 Innen)>
            RandzoningStrassenAchsen => RandzoningStrassenplan();

        private IReadOnlyList<(float2 A, float2 B, float2 Innen)>
            RandzoningStrassenplan()
        {
            if (_randzoning.Count == 0 || !_closed || _points.Count < 3)
                return System.Array.Empty<(float2, float2, float2)>();

            /*
             * ZUERST DIE ACHSEN AUS DEM PLAN - GERATEN WIRD NUR NOTFALLS.
             *
             * Hier wurde die Achse jedes Mal neu gerechnet: 10,4 m parallel
             * nach innen, die Stelle der frueher ERZWUNGENEN Strasse. Seit
             * dem 2026-09-10 ist die naechstliegende Fahrgasse die
             * RZ-Strasse, und an einer schraegen Kante ist sie weder
             * parallel noch 10,4 m entfernt. Die Vorschau rasterte die
             * Kacheln also an einer Linie, an der keine Strasse liegt.
             *
             * Der Nutzer im Spiel: *"Nein tut sie nicht [richtig anzeigen].
             * Und auch auffaellig, dass die Tiles nach innen gehen statt
             * aussen."*
             *
             * `RandzoningRoad` steht im fertigen Layout und ist genau das,
             * was gebaut wird. Es kommt aus dem Hintergrundlauf und ist
             * deshalb einen Zug alt - fuer die RZ-Linien ist das harmlos,
             * die setzt der Nutzer mit einem Klick auf eine Kante und nicht
             * ziehend (siehe die Regel zur veralteten Vorschau).
             */
            var achsen = _areaPreviewLayout?.RandzoningRoad;
            if (achsen != null && achsen.Length > 0) return achsen;

            /*
             * NOTFALL: noch kein Layout. Dann die alte Rechnung, damit beim
             * Setzen der ersten Linie ueberhaupt etwas zu sehen ist. Ein
             * Nichts waere schlimmer als eine Naeherung.
             */
            var e = _uiSystem?.CurrentSettings();
            var tiefe = e == null ? 10.4 : e.Es + e.Sl + e.Ai / 2;
            // Ohne Plan auch ohne Richtung: der Nullvektor sagt der Schleife
            // oben, dass sie wie frueher ueber die Polygonmitte entscheidet.
            var genaehert = ParkingGeometry.RandzoningStrassen(
                _randzoning, _points, tiefe);
            if (genaehert == null || genaehert.Count == 0)
                return System.Array.Empty<(float2, float2, float2)>();
            var mitRichtung = new (float2 A, float2 B, float2 Innen)[genaehert.Count];
            for (var i = 0; i < genaehert.Count; i++)
                mitRichtung[i] = (genaehert[i].A, genaehert[i].B, float2.zero);
            return mitRichtung;
        }

        /**
         * Was am Zeiger stehen soll - Kachelzahl oder Seitenbefund.
         *
         * Ansage des Nutzers: *"Im User-Feedback sollte am besten auch
         * stehen X * Y tiles anstatt nur im UI oben."* Wer die Flaeche
         * aufzieht, schaut auf die Flaeche und nicht ins Panel.
         *
         * Die Reihenfolge ist die der Aufmerksamkeit: der Seitenschalter
         * geht vor, dann die Flaeche in der Hand, dann die unter dem Zeiger,
         * zuletzt die gewaehlte.
         */
        internal string ZoningZeigertext
        {
            get
            {
                if (ZoningSeitenModus) return ZoningSeitenBefund;
                if (!ZoningModus) return null;

                var f = ZoningVorschau;
                if (f == null && _zoningHover >= 0
                    && _zoningHover < _zoningflaechen.Count)
                    f = _zoningflaechen[_zoningHover];
                if (f == null && ZoningAuswahl >= 0
                    && ZoningAuswahl < _zoningflaechen.Count)
                    f = _zoningflaechen[ZoningAuswahl];
                if (f == null) return null;

                // Die Meter stehen dabei: Kacheln allein sind keine Groesse,
                // die man im Gelaende abschaetzen kann.
                var breite = f.Spalten * ParkingGeometry.Zoningparzelle;
                var tiefe = f.Reihen * ParkingGeometry.Zoningparzelle;
                return T(
                    $"{f.Spalten} × {f.Reihen} Kacheln · {f.Parzellen} Parzellen "
                        + $"· {breite:F0} × {tiefe:F0} m",
                    $"{f.Spalten} × {f.Reihen} tiles · {f.Parzellen} parcels "
                        + $"· {breite:F0} × {tiefe:F0} m");
            }
        }

        /**
         * Die gesetzten Flaechen UND die, die gerade aufgezogen wird.
         *
         * Beim Aufziehen liegt die neue Flaeche in `_zoningZug` und noch
         * nicht in der Liste - sie kommt erst beim Loslassen hinein. Ohne
         * sie plante die Vorschau ihre Strassen nicht, und der Nutzer sah
         * beim Erstellen gar keine Kacheln. Beim SCHIEBEN steht die Flaeche
         * dagegen schon in der Liste und wird dort laufend nachgefuehrt.
         */
        private List<ParkingGeometry.Zoningflaeche> ZoningflaechenMitZug()
        {
            var flaechen = new List<ParkingGeometry.Zoningflaeche>(
                _zoningflaechen.Count + 1);
            flaechen.AddRange(_zoningflaechen);
            if (ZoningVorschau != null) flaechen.Add(ZoningVorschau);
            return flaechen;
        }

        /**
         * Liegt diese Zelle auf einer Zoning-Strasse?
         *
         * Befund des Nutzers: *"Tiles werden auf Zoning-Strassen angezeigt,
         * obwohl sie da nicht sein sollten."*
         *
         * Vorher wurde das ueber die ZELLENNUMMER entschieden: der Ring
         * direkt um die eigenen Parzellen galt als Fahrbahn. Das stimmt nur,
         * solange eine Flaeche fuer sich steht. Sobald sich zwei eine
         * Strasse teilen oder eine Strasse an einer fremden Flaeche
         * entlanglaeuft, trifft es die falschen Zellen - und die richtigen
         * nicht.
         *
         * Die Strasse selbst zu fragen, ist unabhaengig davon, wem sie
         * gehoert: was innerhalb der halben Fahrbahnbreite liegt, ist
         * Fahrbahn. Punkt.
         */
        internal static bool ZoningZelleAufStrasse(
            IReadOnlyList<(float2 A, float2 B, bool LinksAn, bool RechtsAn)> strassen,
            float2 mitte)
        {
            if (strassen == null) return false;

            /*
             * NUR DIE STRECKE SELBST, KEIN UEBERSTAND AN DEN ENDEN.
             *
             * Hier stand zuerst eine Verlaengerung um die halbe Breite - mit
             * dem Gedanken, an einer Ecke biege die Strasse ja weiter. Das
             * war falsch, und es hat genau die Kacheln gefressen, die dem
             * Nutzer gefehlt haben:
             *
             *   "Bei Outside und Both werden die Tiles an der Ecke immer
             *   noch nicht richtig angezeigt. Beim Verschieben flackern
             *   manchmal die 2 fehlenden Tiles auf."
             *
             * Die Rechnung dazu: die Eckkachel der aeusseren Reihe liegt
             * 8,0 m hinter dem Ende der Querstrasse. Mit dem Ueberstand von
             * 4 m rueckte deren Fusspunkt auf 4 m heran, und der Abstand
             * fiel auf 3,99 m - eine Handbreit unter der Schwelle von
             * 4,01 m. Deshalb verschwand die Kachel, und deshalb flackerte
             * sie: 3,99 gegen 4,01 entscheidet die letzte Nachkommastelle
             * der Drehung.
             *
             * Die Ecke selbst braucht den Ueberstand gar nicht - dort liegt
             * die ANDERE Strasse, und die wird ohnehin mitgefragt.
             */
            var halb = (float)ParkingGeometry.ZoningStrassenbreite * 0.5f + 0.01f;

            for (var i = 0; i < strassen.Count; i++)
            {
                var s = strassen[i];
                var spanne = s.B - s.A;
                var laenge = math.length(spanne);
                if (laenge < 1e-6f) continue;
                var richtung = spanne / laenge;
                var laengs = math.clamp(
                    math.dot(mitte - s.A, richtung), 0f, laenge);
                if (math.distance(mitte, s.A + richtung * laengs) < halb)
                    return true;
            }
            return false;
        }

        /**
         * Die schon gebauten Kanten dieses Parkplatzes mit ihrem echten
         * Seitenzustand.
         *
         * NUR DIE DES EIGENEN TRAEGERS. Der naheliegende Weg waere eine
         * Abfrage ueber alle Kanten der Welt - der laeuft aber JEDEN FRAME
         * und wuerde in einer grossen Stadt jede Strasse anfassen, um am
         * Ende zwei Dutzend zu behalten. Der Traeger hat die eigenen ohnehin
         * in seinem `SubNet`-Puffer stehen, und mehr braucht die Vorschau
         * nicht: sie zeigt den Parkplatz, an dem gerade gearbeitet wird.
         *
         * Vor dem ersten Bau gibt es nichts - dann faellt die Panelwahl.
         */
        private List<(float2 A, float2 B, bool LinksAn, bool RechtsAn)>
            GebauteZoningseiten()
        {
            if (_lotOwner == Entity.Null
                || !EntityManager.Exists(_lotOwner)) return null;
            if (!EntityManager.HasComponent<ParkingLotCarrierReference>(_lotOwner))
                return null;
            var traeger = EntityManager
                .GetComponentData<ParkingLotCarrierReference>(_lotOwner).Carrier;
            if (traeger == Entity.Null || !EntityManager.Exists(traeger))
                return null;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return null;

            var prefabs = SammleZoningPrefabs();
            if (prefabs.Count == 0) return null;

            List<(float2, float2, bool, bool)> ergebnis = null;
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (!EntityManager.Exists(kante)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(kante)) continue;
                if (!prefabs.Contains(
                        EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab))
                    continue;
                if (!EntityManager.HasComponent<Curve>(kante)) continue;

                var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
                ergebnis ??= new List<(float2, float2, bool, bool)>();
                ergebnis.Add((
                    new float2(kurve.a.x, kurve.a.z),
                    new float2(kurve.d.x, kurve.d.z),
                    !LiestSeite(kante, true),
                    !LiestSeite(kante, false)));
            }
            return ergebnis;
        }

        private static bool SucheGebauteSeite(
            IReadOnlyList<(float2 A, float2 B, bool LinksAn, bool RechtsAn)> gebaute,
            float2 a, float2 b, out bool linksAn, out bool rechtsAn)
        {
            linksAn = true;
            rechtsAn = true;
            if (gebaute == null) return false;
            for (var i = 0; i < gebaute.Count; i++)
            {
                if (!ZoningSelbeKante(gebaute[i].A, gebaute[i].B, a, b)) continue;
                linksAn = gebaute[i].LinksAn;
                rechtsAn = gebaute[i].RechtsAn;
                return true;
            }
            return false;
        }

        /**
         * Erreicht eine eingeschaltete Strassenseite diese Zelle?
         *
         * Drei Bedingungen, und alle drei muessen gelten: die Zelle liegt auf
         * der richtigen SEITE, sie liegt innerhalb der LAENGE des Stuecks,
         * und sie ist hoechstens sechs Zellen TIEF entfernt - weiter laesst
         * CS2 nichts wachsen.
         *
         * Geprueft wird gegen ALLE Stuecke; das erste, das passt, genuegt.
         * Genau darin liegt die Bedingung des Nutzers: eine
         * gegenueberliegende Strasse haelt dieselbe Kachel am Leben.
         */
        internal static bool ZoningZelleErreicht(
            IReadOnlyList<(float2 A, float2 B, bool LinksAn, bool RechtsAn)> strassen,
            float2 mitte)
        {
            if (strassen == null) return false;

            /*
             * DIE TIEFE WIRD AB DER STRASSENMITTE GEMESSEN.
             *
             * Die erste Zellenmitte liegt 8 m von der Fahrbahnmitte
             * entfernt: 4 m halbe Fahrbahn, dann die halbe Zelle. Die
             * sechste liegt bei 8 + 5*8 = 48 m. Genau 48 ist also die
             * Grenze - nicht 52, wie ein aufgeschlagenes halbes
             * Strassenprofil nahelegen wuerde.
             */
            var tiefe = (float)(ZoningTiefeGrenze * ParkingGeometry.Zoningparzelle);

            /*
             * UND AN JEDES ENDE HAENGT CS2 4 m AN.
             *
             * Das ist dieselbe Rechnung, die schon die Kachelzahl erklaert
             * hat: `Kacheln = Laenge/8 + 1`. Ein 48-m-Stueck deckt 56 m ab,
             * also sieben Zellenmitten von 0 bis 48. Die Grenze zaehlt NICHT
             * mit, sonst waere es eine Zelle zu viel - genau der Spalt, den
             * der Nutzer am 2026-09-03 im Bild gezeigt hat.
             */
            var ueberstand =
                (float)ParkingGeometry.ZoningStrassenbreite * 0.5f - 0.01f;

            for (var i = 0; i < strassen.Count; i++)
            {
                var s = strassen[i];
                var spanne = s.B - s.A;
                var laenge = math.length(spanne);
                if (laenge < 1e-6f) continue;
                var richtung = spanne / laenge;

                var laengs = math.dot(mitte - s.A, richtung);
                if (laengs < -ueberstand || laengs > laenge + ueberstand) continue;

                var lot = s.A + richtung * laengs;
                var abstand = math.distance(mitte, lot);
                if (abstand > tiefe + 0.01f) continue;

                var zurZelle = mitte - lot;
                var links = richtung.x * zurZelle.y - richtung.y * zurZelle.x > 0f;
                if (!(links ? s.LinksAn : s.RechtsAn)) continue;

                // Und der Weg dorthin muss frei sein. Gezaehlt wird ab der
                // Fahrbahnkante, nicht ab der Mitte: an einer Ecke endet die
                // Querstrasse genau auf der Achse, und das ist kein
                // Hindernis, sondern der Anschluss.
                var start = lot + (mitte - lot) / abstand
                    * ((float)ParkingGeometry.ZoningStrassenbreite * 0.5f + 0.1f);
                if (VerdecktEineAndereStrasse(strassen, i, start, mitte)) continue;
                return true;
            }
            return false;
        }

        /**
         * LIEGT EINE ANDERE STRASSE ZWISCHEN STRASSE UND ZELLE?
         *
         * Das war die Regel, die gefehlt hat - und ohne sie war die Anzeige
         * unbrauchbar. Der Nutzer hat es an der einfachsten Form gemerkt:
         * *"Ziehe ich 3x3, dann sind eben innen 9 Tiles."* Es waren mehr.
         *
         * Der Grund: die Reichweite geht sechs Zellen weit, und eine Flaeche
         * von 3x3 ist nur drei Zellen breit. Die Strasse auf der einen Seite
         * erreichte damit rechnerisch auch noch Zellen JENSEITS der Strasse
         * auf der anderen Seite - vier Zellen weit ins Freie, und das sah
         * aus wie herausquellende Kacheln.
         *
         * CS2 macht es genauso: ein Zonenblock endet an der naechsten
         * Strasse. Was dahinter liegt, gehoert zu deren Block, nicht mehr zu
         * diesem.
         */
        private static bool VerdecktEineAndereStrasse(
            IReadOnlyList<(float2 A, float2 B, bool LinksAn, bool RechtsAn)> strassen,
            int ausser, float2 von, float2 bis)
        {
            for (var k = 0; k < strassen.Count; k++)
            {
                if (k == ausser) continue;
                if (StreckenKreuzen(von, bis, strassen[k].A, strassen[k].B))
                    return true;
            }
            return false;
        }
    }
}
