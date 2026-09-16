using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotOverlay
    {
        internal void Draw(OverlayRenderSystem.Buffer nativeBuffer,
            IReadOnlyList<float3> polygon, bool closed, bool hasCursor,
            float3 cursor, bool canClose, int hoverPoint, int dragPoint,
            ParkingLotToolSystem.SnapKind snap
                = ParkingLotToolSystem.SnapKind.None,
            bool hasSnapGuide = false, Line3.Segment snapGuide = default,
            IReadOnlyList<float3> markers = null, bool markerMode = false,
            ParkingLotEntranceOverlayState entrances = null,
            int hoverEdge = -1, int dragEdge = -1,
            bool insertReady = false, float3 insertPosition = default,
            bool edgeSnapped = false, float3 edgeSnapPoint = default,
            float3 edgeGuide = default,
            bool extrudeReady = false, bool shiftReady = false,
            bool ausrichtWahl = false,
            IReadOnlyList<ParkingLotToolSystem.Teilflaeche> teilflaechen = null,
            bool teilflaechenWahl = false, int gewaehlteTeilflaeche = -1,
            IReadOnlyList<bool> teilflaecheZugewiesen = null,
            int hervorgehobeneTeilflaeche = -1,
            int zeigerTeilflaeche = -1,
            int blinkKante = -1,
            bool trennmodus = false,
            IReadOnlyList<(float3 A, float3 B)> trennlinien = null,
            int trennAnfang = -1,
            IReadOnlyList<ParkingGeometry.Zoningflaeche> zoningflaechen = null,
            ParkingGeometry.Zoningflaeche zoningVorschau = null,
            int zoningHover = -1,
            int zoningAuswahl = -1,
            ParkingGeometry.Zoningseite zoningSeite =
                ParkingGeometry.Zoningseite.Innen,
            IReadOnlyList<(float2 A, float2 B, bool LinksAn, bool RechtsAn)>
                zoningstrassen = null,
            (float2 A, float2 B, bool Links)? zoningSeitenziel = null,
            bool zoningSeitenModus = false,
            IReadOnlyList<(float2 A, float2 B)> randzoning = null,
            IReadOnlyList<(float2 A, float2 B)> randzoningachsen = null,
            (float2 A, float2 B, int Zustand)? randzoningZiel = null)
        {
            var buffer = new ParkingLotPreviewBuffer(nativeBuffer);
            /*
             * DIE VORSCHAU BLEIBT AUCH BEIM AUSRICHTEN AN.
             *
             * Zuerst war es andersherum: die Kacheln STATT der Vorschau, so
             * ausdruecklich gewuenscht. In der Hand hat sich das gedreht -
             * die Preview soll wieder an, "damit man sieht, was sich
             * aendert". Und das ist der Punkt: die Bezugslinie dreht die
             * Buchtreihen, und genau diese Wirkung will man beim Waehlen
             * sehen. Die Kacheln liegen jetzt als durchscheinende Flaechen
             * DARUEBER - sie verdecken die Vorschau nicht mehr, sie faerben
             * sie ein.
             */
            foreach(var plant in _vegetation)
                buffer.DrawCircle(VegetationColor,plant.Position,plant.Tree ? VegetationTreeDiameter : VegetationShrubDiameter);
            /*
             * DIE STRASSENBAENDER WERDEN GAR NICHT MEHR GEZEICHNET.
             *
             * Erst waren sie gefuellt und lagen uebereinander, dann als
             * Umriss - und lagen als Linien immer noch uebereinander. Der
             * Nutzer am 2026-09-15, direkt nach der Umstellung: *"Die Umrisse
             * der Rechtecke sind noch da."*
             *
             * Sie zeigen auch nichts mehr, was fehlen wuerde: der Belag
             * steht als verschmolzene Flaeche da, und WELCHE Strasse wo
             * liegt, sieht man an ihrer Form. Zufahrten haben ihre eigenen
             * Marken, die bleiben.
             *
             * Die Buchten bleiben als Umriss. Sie ueberlappen sich nicht -
             * `ParkingBayRuns.Merge` legt sie als saubere Reihen an -, und
             * ihre Farbe ist die einzige Stelle, an der man Behinderten- und
             * E-Plaetze ueberhaupt erkennt.
             */
            var netzFuellt = Flaechennetz != null;
            DrawBands(buffer, _green, false, netzFuellt);
            if (!netzFuellt) DrawBands(buffer, _roads, true);
            DrawBands(buffer, _bays, true, netzFuellt);
            for (var i = 0; i < _chargers.Count; i++)
                buffer.DrawCircle(ChargerColor, _chargers[i], ChargerDiameter);

            /*
             * DIE TEILFLAECHEN ALS KLICKZIELE.
             *
             * Jede Teilflaeche zeigt ihre echte Kontur und ihr Klickrechteck -
             * "einfach grob Rechtecke anzeigen, die anklickbar sind".
             *
             * ZWEI STUFEN DURCHSICHT, so vom Nutzer vorgegeben: leicht
             * durchscheinend, solange die Flaeche noch keine Bezugslinie hat,
             * deutlich blasser, sobald sie eine hat. Damit sieht man im
             * Vorbeigehen, was noch offen ist - ohne eine Liste zu lesen.
             *
             * Gezeichnet NACH der Vorschau, damit die Faerbung ueber den
             * Buchten liegt und nicht unter ihnen. Gefuellt wird wie jedes
             * andere Band: ein Strich in voller Rechteckbreite.
             *
             * Sichtbar in BEIDEN Schritten, nicht nur bei der Flaechenwahl.
             * Waehrend man die Linie sucht, muss man sehen koennen, fuer
             * welche Flaeche man sie gerade waehlt.
             *
             * BEI EINER EINZIGEN TEILFLAECHE GAR NICHT. Sie waere die ganze
             * Form, und ein Rahmen um alles unterscheidet nichts von nichts -
             * er verdeckt nur die Vorschau, die man gerade beurteilen will.
             * Ansage des Nutzers: "wenn nur eine Teilflaeche vorhanden ist
             * brauchen wir keine Umrandung".
             */
            Flaechennetz?.BeginneTeilflaechen();
            if ((teilflaechenWahl || ausrichtWahl || trennmodus)
                && teilflaechen != null && teilflaechen.Count > 1)
                for (var i = 0; i < teilflaechen.Count; i++)
                {
                    var teil = teilflaechen[i];
                    var farbe = TeilflaechenFarbe(i);
                    var hoehe = cursor.y;
                    var benutzt = teilflaecheZugewiesen != null
                        && i < teilflaecheZugewiesen.Count
                        && teilflaecheZugewiesen[i];
                    // Umschalt ueber einer abgehakten Flaeche holt sie
                    // kurz auf den offenen Wert zurueck - nur zum Ansehen.
                    /*
                     * IN DER LINIENWAHL ZAEHLT NUR DIE GEWAEHLTE FLAECHE.
                     *
                     * Sobald eine Flaeche steht, kann man keine andere mehr
                     * anklicken - also darf auch keine andere so aussehen,
                     * als koennte man. Die uebrigen fallen auf den blassen
                     * Wert zurueck, die gewaehlte steht deutlich da. Der
                     * Nutzer am 2026-09-15: *"Ich sehe gerade nicht wirklich
                     * ob ich markiert habe oder nicht."*
                     */
                    var linienwahl = gewaehlteTeilflaeche >= 0;
                    var fuellung = farbe;
                    fuellung.a = i == gewaehlteTeilflaeche
                        ? TeilflaecheFuellungGewaehlt
                        : linienwahl ? TeilflaecheFuellungBenutzt
                        : i == zeigerTeilflaeche ? TeilflaecheFuellungZeiger
                        : benutzt && i != hervorgehobeneTeilflaeche
                            ? TeilflaecheFuellungBenutzt : TeilflaecheFuellung;
                    /*
                     * DIE ECHTE FORM EINFAERBEN, NICHT DAS KLICKRECHTECK.
                     *
                     * Hier lag ein Strich in voller Rechteckbreite von
                     * `teil.Min` bis `teil.Max` - also ein Rechteck um die
                     * Form herum. Bei einem schraegen oder L-foermigen
                     * Teilstueck faerbt das die Nachbarn mit ein. Genau das
                     * hat der Nutzer am 2026-09-15 als Erstes gemeldet:
                     * *"koennen wir die Auswahl der Teilflaechen besser
                     * angezeigt bekommen als nur ein Rechteck."*
                     *
                     * Das Klickziel BLEIBT das Rechteck - es wird weiter
                     * unten gestrichelt gezeigt. Gefuellt wird der Umriss.
                     */
                    if (Flaechennetz != null && teil.Umriss != null
                        && teil.Umriss.Length >= 3)
                    {
                        Flaechennetz.ZeichneTeilflaeche(teil.Umriss, fuellung);
                    }
                    else
                    {
                        var mitte = (teil.Min.y + teil.Max.y) * 0.5f;
                        var breite = teil.Max.y - teil.Min.y;
                        if (breite > 0.01f && teil.Max.x - teil.Min.x > 0.01f)
                            buffer.DrawLine(fuellung, fuellung, 0f,
                                OverlayRenderSystem.StyleFlags.Projected,
                                new Line3.Segment(
                                    new float3(teil.Min.x, hoehe, mitte),
                                    new float3(teil.Max.x, hoehe, mitte)),
                                breite, default);
                    }

                    /*
                     * HIER LAG DAS GESTRICHELTE HUELLRECHTECK.
                     *
                     * Es zeigte das Klickziel an, solange die Trefferpruefung
                     * gegen Rechtecke lief. Seit sie gegen die echte Form
                     * laeuft, zeigt es nichts mehr an, was es gibt - und der
                     * Nutzer hat es prompt als stoerend gemeldet: *"die
                     * Rechtecke sind immer noch da aber jetzt gestrichelt."*
                     */
                    var dick = i == gewaehlteTeilflaeche
                        ? SelectedLineWidth : PolygonLineWidth;
                    if (teil.Umriss != null)
                        for (var k = 0; k < teil.Umriss.Length; k++)
                        {
                            var a = teil.Umriss[k];
                            var b = teil.Umriss[(k + 1) % teil.Umriss.Length];
                            buffer.DrawLine(farbe, new Line3.Segment(
                                new float3(a.x, hoehe, a.y),
                                new float3(b.x, hoehe, b.y)), dick);
                        }
                    buffer.DrawCircle(farbe,
                        new float3(teil.Anker.x, hoehe, teil.Anker.y),
                        SnapDiameter);
                }

            Flaechennetz?.SchliesseTeilflaechen();

            ZeichneZoning(buffer, cursor.y, zoningflaechen, zoningVorschau,
                zoningHover, zoningAuswahl, zoningSeite, zoningstrassen,
                closed ? polygon : null, randzoningachsen);

            /*
             * WELCHE SEITE ZONING HAT - direkt an der Strasse.
             *
             * Ansage des Nutzers: *"Es gibt kein visuelles Feedback, welche
             * Seite der Strasse Zoning hat oder nicht."*
             *
             * Ein schmaler Streifen an jeder Seite, in der Farbe des
             * Zustands: Gruen heisst an, Graublau heisst aus. Schmal und
             * dicht an der Fahrbahn, damit er nicht mit den Kacheln
             * verwechselt wird - er sagt etwas ueber die STRASSE, nicht
             * ueber das Bauland.
             */
            if (zoningSeitenModus && zoningstrassen != null)
            {
                var streifen = SelectedLineWidth;
                foreach (var kante in zoningstrassen)
                {
                    /*
                     * AN EINER RZ-STRASSE KEINE SEITENSTREIFEN.
                     *
                     * Die Streifen sagen "hier kannst du schalten" - an einer
                     * RZ-Strasse kann man das nicht, sie zont immer nach
                     * aussen. Der Nutzer: *"Die RZ-Strasse wird in der
                     * Preview immer noch so angezeigt wie die ZF-Strassen,
                     * als waere sie road-side-toggable. Aber da sie es nicht
                     * ist, muss das aus."*
                     */
                    var istRz = false;
                    if (randzoningachsen != null)
                        foreach (var achse in randzoningachsen)
                            if (ParkingGeometry.RandzoningSelbeLinie(
                                    achse.A, achse.B, kante.A, kante.B))
                            { istRz = true; break; }
                    if (istRz) continue;
                    var richtung = kante.B - kante.A;
                    var laenge = math.length(richtung);
                    if (laenge < 0.01f) continue;
                    richtung /= laenge;
                    var halb = (float)ParkingGeometry.ZoningStrassenbreite * 0.5f;

                    for (var seite = 0; seite < 2; seite++)
                    {
                        var links = seite == 0;
                        var an = links ? kante.LinksAn : kante.RechtsAn;
                        var normale = links
                            ? new float2(-richtung.y, richtung.x)
                            : new float2(richtung.y, -richtung.x);
                        var versatz = normale * (halb + streifen * 0.5f);
                        var farbe = an
                            ? Alpha(Positive, 0.65f)
                            : Muted;
                        var linie = new Line3.Segment(
                            new float3((kante.A + versatz).x, cursor.y,
                                (kante.A + versatz).y),
                            new float3((kante.B + versatz).x, cursor.y,
                                (kante.B + versatz).y));
                        if (an) buffer.DrawLine(farbe, linie, streifen);
                        else buffer.DrawDashedLine(farbe, linie, streifen,
                            DashLength, DashGap);
                    }
                }
            }

            /*
             * DAS RANDZONING - gesetzte Linien und die unter dem Zeiger.
             *
             * Ansage des Nutzers: *"Das ganze muss natuerlich auch in der
             * Preview ordentlich geschnitten werden."* Und zum Feedback:
             * *"Ein neues Userfeedback aehnlich wie bei Roadside waere
             * passend."*
             *
             * Also derselbe Aufbau wie dort: ein Band auf der Linie in der
             * Farbe des Zustands. Gruen heisst gesetzt, blass heisst
             * anklickbar - und das Band liegt auf der Umrisslinie selbst,
             * nicht daneben, weil genau sie gemeint ist.
             */
            if (randzoning != null)
            {
                var gesetzt = Alpha(Positive, 0.55f);
                foreach (var linie in randzoning)
                {
                    if (math.distance(linie.A, linie.B) < 0.01f) continue;
                    buffer.DrawLine(gesetzt, gesetzt, 0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new Line3.Segment(
                            new float3(linie.A.x, cursor.y, linie.A.y),
                            new float3(linie.B.x, cursor.y, linie.B.y)),
                        SelectedLineWidth, default);
                }
            }
            if (randzoningZiel.HasValue)
            {
                var ziel = randzoningZiel.Value;
                if (math.distance(ziel.A, ziel.B) > 0.01f)
                {
                    /*
                     * DREI ZUSTAENDE, DREI FARBEN.
                     *
                     *   hell   - frei, Klick macht Randzoning
                     *   rot    - schon gesetzt, Klick nimmt es weg
                     *   orange - blockiert, hier geht es nicht
                     *
                     * Die Absage ist ORANGE und nicht rot: rot heisst hier
                     * schon "wegnehmen", und zwei Bedeutungen auf einer Farbe
                     * waeren genau die Verwechslung, die der Nutzer beim
                     * Seitenschalter zu Recht bemaengelt hat.
                     */
                    var farbe = ziel.Zustand == 1
                        ? Negative
                        : ziel.Zustand == 2
                            ? Warning
                            : Accent;
                    buffer.DrawLine(farbe, farbe, 0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new Line3.Segment(
                            new float3(ziel.A.x, cursor.y, ziel.A.y),
                            new float3(ziel.B.x, cursor.y, ziel.B.y)),
                        SelectedLineWidth, default);
                }
            }

            /*
             * DIE GEMEINTE STRASSENSEITE.
             *
             * Der Nutzer waehlt sie nicht im Panel, sondern durch den Klick
             * auf die Seite, die er meint. Damit das kein Ratespiel ist,
             * liegt vorher ein Balken auf genau dieser Seite - und zwar in
             * der Tiefe, in der dort Kacheln entstehen wuerden.
             */
            if (zoningSeitenziel.HasValue)
            {
                var ziel = zoningSeitenziel.Value;
                var richtung = ziel.B - ziel.A;
                var laenge = math.length(richtung);
                if (laenge > 0.01f)
                {
                    richtung /= laenge;
                    var normale = ziel.Links
                        ? new float2(-richtung.y, richtung.x)
                        : new float2(richtung.y, -richtung.x);
                    var halb = (float)ParkingGeometry.ZoningStrassenbreite * 0.5f;
                    var tiefe = (float)(6 * ParkingGeometry.Zoningparzelle);
                    var mitteA = ziel.A + normale * (halb + tiefe * 0.5f);
                    var mitteB = ziel.B + normale * (halb + tiefe * 0.5f);
                    var seitenfarbe = ZoningColor;
                    seitenfarbe.a = FillSelected;
                    buffer.DrawLine(seitenfarbe, seitenfarbe, 0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new Line3.Segment(
                            new float3(mitteA.x, cursor.y, mitteA.y),
                            new float3(mitteB.x, cursor.y, mitteB.y)),
                        tiefe, default);
                }
            }

            /*
             * DIE GEPLANTEN ZONING-STRASSEN.
             *
             * Sie sind im Spiel unsichtbar - genau deshalb gehoeren sie in
             * die Vorschau. Ohne sie sieht der Nutzer erst nach dem Bauen,
             * wo eine Strasse entstanden ist und wo nicht, und gerade das
             * entscheidet ueber die Kacheln.
             *
             * Gezeichnet als breites Band in der Fahrbahnbreite, damit man
             * den belegten Platz sieht und nicht nur eine Linie.
             */
            if (zoningstrassen != null)
            {
                var strassenfarbe = ZoningColor;
                strassenfarbe.a = FillQuiet;
                var breite = (float)ParkingGeometry.ZoningStrassenbreite;
                foreach (var strasse in zoningstrassen)
                {
                    if (math.distance(strasse.A, strasse.B) < 0.01f) continue;
                    buffer.DrawLine(strassenfarbe, strassenfarbe, 0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new Line3.Segment(
                            new float3(strasse.A.x, cursor.y, strasse.A.y),
                            new float3(strasse.B.x, cursor.y, strasse.B.y)),
                        breite, default);
                }
            }

            /*
             * DIE GEZOGENEN TRENNSCHNITTE.
             *
             * Sie liegen ueber den Kacheln: sie sind der Grund, warum die
             * Kacheln so aussehen, wie sie aussehen. Kraeftig und dick, damit
             * man sie vom Umriss unterscheidet - und damit man sie zum
             * Loeschen trifft.
             *
             * Der halb gesetzte Schnitt bekommt einen Ring um seinen ersten
             * Punkt. Ohne ihn wuesste man nach einem Klick nicht, ob er
             * angekommen ist.
             */
            if (trennlinien != null)
                for (var i = 0; i < trennlinien.Count; i++)
                    buffer.DrawLine(TrennschnittColor,
                        new Line3.Segment(trennlinien[i].A, trennlinien[i].B),
                        SelectedLineWidth, false);
            if (trennmodus && trennAnfang >= 0 && trennAnfang < polygon.Count)
                buffer.DrawCircle(TrennschnittColor, polygon[trennAnfang],
                    SnapDiameter);

            /*
             * DER KLICK-BLITZ.
             *
             * Steht bewusst NICHT im Hover-Zweig: er soll auch dann noch zu
             * sehen sein, wenn der Zeiger die Kante im selben Moment
             * verlaesst. Weiss, weil jede andere Farbe im Bild schon eine
             * Bedeutung hat - dieser Blitz bedeutet nur "angekommen".
             */
            if (blinkKante >= 0 && blinkKante < polygon.Count
                && polygon.Count >= 2)
                buffer.DrawLine(Color.white, new Line3.Segment(
                        polygon[blinkKante],
                        polygon[(blinkKante + 1) % polygon.Count]),
                    SelectedLineWidth * 1.6f);

            /**
             * Die Hilfslinie des Achsenfangs. Bei den anderen drei Fangarten
             * sieht man das Ziel ohnehin - Bordstein, Hauswand, Nachbarflaeche
             * liegen ja sichtbar da. Die Achse dagegen ist gedacht, und ohne
             * Linie raet man, an welcher der beiden man gerade haengt.
             */
            if (hasCursor && hasSnapGuide)
                buffer.DrawDashedLine(AngleSnapColor, snapGuide,
                    SnapGuideWidth, DashLength, DashGap);

            if (hasCursor && snap != ParkingLotToolSystem.SnapKind.None)
                buffer.DrawCircle(SnapColor(snap),
                    cursor, SnapDiameter);

            if (polygon != null)
            {
                for (var i = 1; i < polygon.Count; i++)
                    buffer.DrawLine(PolygonColor,
                        new Line3.Segment(polygon[i - 1], polygon[i]),
                        PolygonLineWidth, false);

                if (closed && polygon.Count > 2)
                    buffer.DrawLine(PolygonColor,
                        new Line3.Segment(polygon[polygon.Count - 1], polygon[0]),
                        PolygonLineWidth, false);
                else if (hasCursor && polygon.Count > 0)
                    buffer.DrawDashedLine(canClose ? ClosePointColor : PreviewColor,
                        new Line3.Segment(polygon[polygon.Count - 1],
                            canClose ? polygon[0] : cursor),
                        PreviewLineWidth, DashLength, DashGap);

                /**
                 * DIE GEGRIFFENE KANTE.
                 *
                 * Sie ersetzt den Zeigerwechsel des Browser-Prototyps: dort
                 * wird der Mauszeiger je nach Kantenrichtung zu einem
                 * Doppelpfeil (`ew-resize` und Verwandte). Diese vier Zeiger
                 * kommen in CS2s index.css NULL-mal vor - Cohtml haette sie
                 * stillschweigend verworfen. Dieselbe Aussage steht deshalb
                 * in der Welt: die Kante leuchtet auf, und zwei Pfeile auf
                 * ihrer Normalen zeigen, wohin sie laeuft.
                 */
                var griff = dragEdge >= 0 ? dragEdge : hoverEdge;
                if (closed && griff >= 0 && griff < polygon.Count
                    && polygon.Count > 2)
                {
                    var a = polygon[griff];
                    var b = polygon[(griff + 1) % polygon.Count];
                    // Mit Strg heisst dieselbe Kante etwas anderes: nicht
                    // verschieben, sondern einfuegen. Sie traegt deshalb die
                    // Farbe des Schliessens und statt der Pfeile einen Ring
                    // genau dort, wo der neue Punkt entstehen wuerde.
                    /*
                     * LINIENAUSWAHL: die Kante ist hier kein Griff, sondern
                     * die Antwort auf eine Frage. Sie leuchtet deshalb voll
                     * durchgezogen und deutlich dicker als beim Ziehen - und
                     * OHNE die Pfeile, die "verschiebbar" bedeuten wuerden.
                     * Zwei Ringe an den Enden zeigen, welche Strecke gemeint
                     * ist. Der erste Anlauf zeigte nur die gewoehnliche
                     * Hover-Hervorhebung; der Nutzer las das zu Recht nicht
                     * als Auswahl.
                     */
                    if (ausrichtWahl)
                    {
                        /*
                         * IN DER FARBE DER GEWAEHLTEN FLAECHE.
                         *
                         * Die Kante beantwortet eine Frage, die zu genau
                         * einer Flaeche gehoert. Vorher leuchtete sie im
                         * allgemeinen Gruen und stand damit beziehungslos im
                         * Bild - der Nutzer: *"die Linie die ich hovere bzw
                         * klicke ist auch naja."* Gleiche Farbe wie die
                         * Fuellung heisst: die beiden gehoeren zusammen.
                         */
                        var kantenfarbe = gewaehlteTeilflaeche >= 0
                            && teilflaechen != null
                            && gewaehlteTeilflaeche < teilflaechen.Count
                            ? TeilflaechenFarbe(gewaehlteTeilflaeche)
                            : ClosePointColor;
                        buffer.DrawLine(kantenfarbe,
                            new Line3.Segment(a, b), SelectedLineWidth);
                        buffer.DrawCircle(kantenfarbe, a,
                            ActivePointDiameter);
                        buffer.DrawCircle(kantenfarbe, b,
                            ActivePointDiameter);
                    }
                    else if (insertReady)
                    {
                        buffer.DrawDashedLine(ClosePointColor,
                            new Line3.Segment(a, b),
                            SelectedLineWidth, DashLength, DashGap);
                        buffer.DrawCircle(ClosePointColor, insertPosition,
                            ActivePointDiameter);
                    }
                    /**
                     * ALT: EXTRUDIEREN. Orange, gestrichelt, und an beiden
                     * Enden ein Ring - dort entstehen die zwei neuen Punkte.
                     * Zwei Ringe statt einem trennen es eindeutig vom
                     * Einfuegen mit Strg, das genau einen zeigt.
                     */
                    else if (extrudeReady)
                    {
                        buffer.DrawDashedLine(ExtrudeColor,
                            new Line3.Segment(a, b),
                            SelectedLineWidth, DashLength, DashGap);
                        buffer.DrawCircle(ExtrudeColor, a, ActivePointDiameter);
                        buffer.DrawCircle(ExtrudeColor, b, ActivePointDiameter);
                    }
                    /**
                     * SHIFT: die Nachbarkanten behalten ihre Richtung. Also
                     * leuchten GENAU SIE mit auf - man sieht damit vorher,
                     * welche zwei Kanten sich nur in der Laenge aendern.
                     */
                    else if (shiftReady)
                    {
                        buffer.DrawLine(ShiftColor, new Line3.Segment(a, b),
                            SelectedLineWidth, false);
                        var davor = polygon[(griff - 1 + polygon.Count) % polygon.Count];
                        var danach = polygon[(griff + 2) % polygon.Count];
                        buffer.DrawDashedLine(ShiftColor, new Line3.Segment(davor, a),
                            PreviewLineWidth, DashLength, DashGap);
                        buffer.DrawDashedLine(ShiftColor, new Line3.Segment(b, danach),
                            PreviewLineWidth, DashLength, DashGap);
                    }
                    else buffer.DrawLine(ActivePointColor, new Line3.Segment(a, b),
                        SelectedLineWidth, false);

                    // Auf welchem Punkt die Kante gerade einrastet. Ohne die
                    // Anzeige merkt man den Fang erst am Ergebnis.
                    if (edgeSnapped)
                        buffer.DrawCircle(AngleSnapColor, edgeSnapPoint,
                            SnapDiameter);

                    /**
                     * DER ANFASSPUNKT ALS BEZUG.
                     *
                     * Der Strich laeuft QUER zur Kante durch genau die Stelle,
                     * an der man sie haelt, und die beiden Punkte an seinen
                     * Enden schliessen ihn ab. Vorher sass er in der
                     * Kantenmitte - dann zeigt er zwar die Richtung, aber
                     * nicht, woran man zieht.
                     */
                    var richtung = b - a;
                    var laenge = math.length(new float2(richtung.x, richtung.z));
                    /*
                     * BEI DER LINIENAUSWAHL KEINE ZIEHMARKEN.
                     *
                     * Der Querstrich samt seinen zwei Enden bedeutet
                     * "verschiebbar" - beim Waehlen eines Lineals ist das die
                     * falsche Aussage. Der Nutzer hat sie zu Recht bemaengelt:
                     * meine Auswahl-Hervorhebung ersetzte nur die Linienfarbe,
                     * dieser Block hier lief unabhaengig davon weiter.
                     */
                    if (!insertReady && !ausrichtWahl && laenge > 0.01f)
                    {
                        var normale = new float3(-richtung.z, 0f, richtung.x) / laenge;
                        /**
                         * OFFEN beim Zeigen, GESCHLOSSEN beim Halten.
                         *
                         * Solange man nur darueberfaehrt, stehen zwei
                         * getrennte Striche da - ein Angebot. Sobald
                         * gegriffen wird, schliessen sie sich zu einer
                         * durchgehenden Linie durch den Anfasspunkt: jetzt
                         * haelt etwas fest. Der Unterschied ist die ganze
                         * Aussage, deshalb kein Punkt in der Mitte, der sie
                         * verwischt.
                         */
                        if (dragEdge >= 0)
                            buffer.DrawLine(ActivePointColor,
                                new Line3.Segment(edgeGuide - normale * EdgeArrowReach,
                                    edgeGuide + normale * EdgeArrowReach),
                                HoverLineWidth, false);
                        else
                        {
                            buffer.DrawLine(ActivePointColor,
                                new Line3.Segment(edgeGuide + normale * EdgeArrowGap,
                                    edgeGuide + normale * EdgeArrowReach),
                                HoverLineWidth, false);
                            buffer.DrawLine(ActivePointColor,
                                new Line3.Segment(edgeGuide - normale * EdgeArrowGap,
                                    edgeGuide - normale * EdgeArrowReach),
                                HoverLineWidth, false);
                        }
                        buffer.DrawCircle(ActivePointColor,
                            edgeGuide + normale * EdgeArrowReach, PointDiameter);
                        buffer.DrawCircle(ActivePointColor,
                            edgeGuide - normale * EdgeArrowReach, PointDiameter);
                    }
                }

                for (var i = 0; i < polygon.Count; i++)
                {
                    var active = i == dragPoint || i == hoverPoint;
                    var closeTarget = !closed && canClose && i == 0;
                    buffer.DrawCircle(closeTarget ? ClosePointColor
                            : active ? ActivePointColor : PointColor,
                        polygon[i], active || closeTarget
                            ? ActivePointDiameter : PointDiameter);
                }
            }

            DrawEntranceEditing(buffer, entrances);

            if (hasCursor && (polygon == null || polygon.Count == 0))
                buffer.DrawCircle(PreviewColor, cursor, PointDiameter);

            /**
             * Markierte Fehlerstellen. Ganz zuletzt gezeichnet, damit sie
             * ueber allem liegen - man setzt sie ja gerade, weil dort etwas
             * nicht stimmt.
             */
            if (markers != null)
                for (var i = 0; i < markers.Count; i++)
                    buffer.DrawCircle(MarkerColor, markers[i], MarkerDiameter);
            if (markerMode && hasCursor)
                buffer.DrawCircle(MarkerColor, cursor, MarkerDiameter * 0.6f);
        }

        /**
         * Die 13,0-m-Abstandsspanne bei CS2-Standardmass wird als projizierte
         * Linie sichtbar. Treffen zwei Enden aufeinander, beruehren sich die
         * beiden Kapseln exakt; Rot zeigt den blockierten Bereich schon vor
         * dem Klick.
         */
    }
}
