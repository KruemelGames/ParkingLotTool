using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WIE BREIT IST DER FUSSGAENGERWEG AN DIESER STRASSE?
     *
     * Begriffe zuerst, weil sie im Projekt sonst durcheinandergehen:
     *
     *   FUSSWEG        unsere eigene Zugangsart, ein gesetzter unsichtbarer
     *                  Pfad vom Polygonrand zur Strasse.
     *   FUSSGAENGERWEG der Gehweg, der zur Stadtstrasse GEHOERT und Teil
     *                  ihres Querschnitts ist. Den messen wir hier.
     *
     * Die Vorflaeche vor einer Zufahrt soll ueber diesen Fussgaengerweg
     * hinweg bis an den Asphalt reichen. Eine feste Zahl reicht dafuer
     * nicht: CS2 kennt Gehwege in 1 / 1,5 / 2 / 2,5 / 3 / 3,5 / 4 / 4,5 /
     * 5 und 7 m (belegt an RoadBuilder, `SidewalkGroupPrefab`), manche
     * Strassen haben ueberhaupt keinen, und mit RoadBuilder gebaute
     * Strassen koennen links und rechts verschieden breit sein. Wir muessen
     * also an DIESER Kante, auf UNSERER Seite nachmessen.
     *
     * WAS CS2 UNS GIBT (alles am Dekompilat belegt):
     *
     *   Game.Net.EdgeGeometry   `m_Start`/`m_End`, je ein Segment mit den
     *                           Kurven `m_Left` und `m_Right`. Das sind die
     *                           AUSSENKANTEN des ganzen Querschnitts in
     *                           WELTKOORDINATEN - `GeometrySystem` erzeugt
     *                           sie als Mittelkurve, versetzt um
     *                           `+m_Width/2` bzw. `-m_Width/2` (Zeile 526).
     *   NetCompositionData      `m_Width`, und `m_Flags.m_Left`/`m_Right`
     *                           mit `CompositionFlags.Side.Sidewalk` bzw.
     *                           `WideSidewalk` - GETRENNT je Seite. Damit
     *                           ist "hat diese Seite ueberhaupt einen
     *                           Fussgaengerweg" eine Abfrage, keine Schaetzung.
     *   NetCompositionPiece     die Querschnittsteile mit `m_Offset.x`
     *                           (Mitte quer) und `m_Size.x` (Breite). Teile
     *                           mit `NetPieceFlags.HasRoadLanes` tragen die
     *                           Fahrspuren - das ist der Asphalt.
     *
     * EINEN GEHWEG ALS SOLCHEN GIBT ES IN DER ZERLEGUNG NICHT. Es gibt kein
     * Teil, das "Sidewalk" heisst; es gibt nur Fahrbahnteile und den Rest.
     * Genau so messen wir: der Abstand von der Aussenkante bis zum
     * aeussersten Fahrbahnteil ist alles, was zwischen uns und dem Asphalt
     * liegt - Gehweg, Bordstein, Gruenstreifen, Parkstreifen. Fuer die
     * Vorflaeche ist das die richtige Zahl, denn sie soll bis an den
     * Asphalt reichen, nicht bis an eine bestimmte Bauteilsorte.
     *
     * KEINE VORZEICHEN-ANNAHME. Es waere naheliegend, die Aussenkante als
     * `m_Width/2 + m_MiddleOffset` zu rechnen. Das haette eine Herleitung
     * ueber `NetCompositionHelpers` gebraucht, deren Vorzeichen ich nicht
     * gemessen habe. Stattdessen kommt die Aussenkante aus DENSELBEN Teilen:
     * der aeusserste Teilrand IST die Aussenkante. Damit kuerzt sich jede
     * Konvention heraus.
     *
     * WELCHE SEITE UNSERE IST, entscheidet die Geometrie, nicht ein Flag:
     * wir vergleichen den Abstand unseres Punktes zu beiden Aussenkanten in
     * der Welt. Die naehere ist unsere. Die Zuordnung dieser Kurve zur
     * Seite in der Zerlegung steht in `GeometrySystem`: `m_Left` ist die um
     * das POSITIVE Mass versetzte Kurve, gehoert also zum groesseren
     * Querwert.
     */
    internal sealed class Strassenaufmass
    {
        /** Wurde ueberhaupt eine Strasse gefunden? */
        internal bool Gefunden;
        internal Entity Kante;
        internal string Prefab = string.Empty;
        /** Traegt UNSERE Seite laut Flag einen Fussgaengerweg? */
        internal bool HatFussgaengerweg;
        /** Aussenkante -> Asphaltkante auf unserer Seite, in Metern. */
        internal float Randbreite;
        /** Naechster Punkt auf der Aussenkante, Welt (x/z). */
        internal float2 Randpunkt;
        /** Richtung der gemessenen Fahrbahnkante dort, normiert. */
        internal float2 Randrichtung;
        /** Von der Aussenkante zum Asphalt zeigende Normale, normiert. */
        internal float2 ZumAsphalt;
        /** Der in Weltkoordinaten gemessene Punkt am Fahrbahnrand. */
        internal float2 Asphaltpunkt;
        internal float Gesamtbreite;
        internal float Abstand;
        internal string Notiz = string.Empty;
    }

    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Wie weit wir eine Strasse suchen. Vier Meter mehr als die halbe
         * groesste Vanilla-Strasse; die Vorflaeche haengt am Polygonrand,
         * und der liegt bauartbedingt dicht an der Strasse.
         */
        private const float StrassensucheRadius = 32f;

        /**
         * NOTNAGEL fuer die halbe Spurbreite - nur, wenn das Spur-Prefab
         * keine nennt. Normalerweise kommt sie aus `NetLaneData.m_Width`.
         * 1,50 m ist die halbe Vanilla-Fahrspur (Medium Road: 4 Spuren auf
         * 12 m, gemessen am Querschnitt -6..6).
         */
        private const float HalbeSpur = 1.5f;

        /**
         * Zwei verschiedene Weltmasse derselben real erzeugten Spuren:
         * die Mitte fuer die Seitenzuordnung und der Rand fuer das Ergebnis.
         */
        private struct Weltspuraufmass
        {
            internal bool HatMitte;
            internal float Mittenabstand;
            internal bool HatRand;
            internal float Randabstand;
            internal float RandspurMittenabstand;
            internal float Breite;
            internal float2 Randpunkt;
            internal float2 Randrichtung;
            internal Entity Spur;
            internal bool IstParkspur;
            internal int Anzahl;
            internal int FehlendeBreiten;
        }

        /**
         * Misst die Strasse, die dem Punkt am naechsten liegt.
         *
         * `richtung` zeigt vom Parkplatz nach draussen (Fahrtrichtung der
         * Zufahrt). Sie entscheidet nichts ueber die Seitenwahl - die macht
         * der Abstand - aber sie sortiert Kandidaten aus, die HINTER uns
         * liegen: ohne das kann bei einer schmalen Strasse die Kante der
         * gegenueberliegenden Seite naeher sein als die eigene.
         */
        internal Strassenaufmass MisseStrasse(float2 punkt, float2 richtung)
        {
            var ergebnis = new Strassenaufmass();
            if (_netSearchSystem == null)
            {
                ergebnis.Notiz = "Kein Netz-Suchbaum.";
                return ergebnis;
            }

            var suchfeld = new Bounds2(
                new float2(punkt.x - StrassensucheRadius,
                    punkt.y - StrassensucheRadius),
                new float2(punkt.x + StrassensucheRadius,
                    punkt.y + StrassensucheRadius));
            var baum = _netSearchSystem.GetNetSearchTree(readOnly: true, out var deps);
            deps.Complete();
            using var treffer = new NativeList<Entity>(32, Allocator.Temp);
            var iterator = new EntityIterator { Bounds = suchfeld, Results = treffer };
            baum.Iterate(ref iterator);

            var gesehen = new HashSet<Entity>();
            // Zaehler statt Vermutung: bleibt die Suche leer, sagt die
            // Meldung, WORAN die Kandidaten gescheitert sind.
            var gesichtet = 0;
            var uebersprungenEigen = 0;
            var uebersprungenGeloescht = 0;
            var uebersprungenUnvollstaendig = 0;
            var uebersprungenVorschau = 0;
            var uebersprungenGeometrie = 0;
            var uebersprungenFalscheSeite = 0;
            var naechsterAbstand = float.PositiveInfinity;
            var beste = float.MaxValue;
            var diagnoseComposition = Entity.Null;
            var diagnoseOben = false;
            var diagnoseWeltspur = default(Weltspuraufmass);
            var diagnoseQuerschnitt = 0f;
            for (var i = 0; i < treffer.Length; i++)
            {
                var kante = treffer[i];
                if (kante == Entity.Null || !gesehen.Add(kante)
                    || !EntityManager.Exists(kante)) continue;
                gesichtet++;

                /*
                 * BERICHTIGT 2026-08-31: der Suchbaum fuehrt ueberhaupt
                 * keine Temp-Kanten. `Game.Net.SearchSystem` schliesst sie in
                 * JEDER seiner Abfragen aus (`None = Temp`). Die
                 * Unterscheidung unten kostet nichts und bleibt als Schranke
                 * stehen, aber sie war NICHT die Ursache der fehlenden
                 * Vorflaechen - die Zaehler weiter unten suchen sie dort, wo
                 * wirklich entschieden wird.
                 *
                 * TEMP IST NICHT GLEICH TEMP.
                 *
                 * Bis zum 2026-08-31 flogen hier ALLE Kanten mit `Temp` oder
                 * `Deleted` raus. Das ist genau ein Fall zu viel.
                 *
                 * Sobald sich eine unserer Zufahrten an eine Stadtstrasse
                 * haengt, legt CS2 eine Vorschaufassung dieser Strasse an:
                 * eine neue Kante mit `Temp`, deren `m_Original` auf die
                 * echte zeigt - und die echte bekommt `Deleted`. Beide Haelften
                 * fielen durch unseren Filter. Die Strasse verschwand damit
                 * mitten in der Vorschau aus der Suche, und die naechste
                 * Zufahrt meldete "Keine Strasse innerhalb 32 m", obwohl sie
                 * direkt daneben lag. Genau das hat der Nutzer beobachtet:
                 * Zufahrt 1 geht, Zufahrt 2 nicht - und allein gebaut geht
                 * dieselbe Zufahrt wieder.
                 *
                 * Unterschieden wird jetzt nach `m_Original`:
                 *   Temp MIT Original  = Vorschaufassung einer echten Strasse
                 *                        -> messen, sie traegt die gueltige
                 *                           Geometrie.
                 *   Temp OHNE Original = frisch erfundenes Netz, also unseres
                 *                        -> ueberspringen.
                 *   Deleted ohne Temp  = das Original, das die Vorschaufassung
                 *                        ersetzt (oder ein Abriss)
                 *                        -> ueberspringen, die Temp-Kante wird
                 *                           eigenstaendig gefunden.
                 */
                var istTemp = EntityManager.HasComponent<Temp>(kante);
                if (istTemp)
                {
                    var original = EntityManager
                        .GetComponentData<Temp>(kante).m_Original;
                    if (original == Entity.Null
                        || !EntityManager.Exists(original))
                    {
                        uebersprungenEigen++;
                        continue;
                    }
                    uebersprungenVorschau--;
                }
                else if (EntityManager.HasComponent<Deleted>(kante))
                {
                    uebersprungenGeloescht++;
                    continue;
                }

                if (!EntityManager.HasComponent<Edge>(kante)
                    || !EntityManager.HasComponent<EdgeGeometry>(kante)
                    || !EntityManager.HasComponent<Composition>(kante))
                {
                    uebersprungenUnvollstaendig++;
                    continue;
                }

                var geometrie = EntityManager.GetComponentData<EdgeGeometry>(kante);
                if (!NaechsteAussenkante(geometrie, punkt, richtung,
                        out var abstand, out var linksseite,
                        out var randpunkt, out var randrichtung,
                        out var gegenpunkt))
                {
                    uebersprungenGeometrie++;
                    continue;
                }
                naechsterAbstand = math.min(naechsterAbstand, abstand);
                if (abstand >= beste) continue;

                var zumAsphalt = ZumAsphaltHin(randpunkt, randrichtung, gegenpunkt);
                /*
                 * SICHERUNG GEGEN DIE FALSCHE STRASSENSEITE.
                 *
                 * Zeichnet der Nutzer den Polygonrand ein Stueck ueber die
                 * Aussenkante hinaus, liegt die eigene Kante HINTER uns und
                 * faellt aus der Suche. Dann waere die naechste Kandidatin
                 * die Aussenkante der GEGENUEBERLIEGENDEN Seite - und die
                 * Vorflaeche liefe quer ueber die ganze Fahrbahn.
                 *
                 * Auf der richtigen Seite liegt der Asphalt IN Fahrtrichtung
                 * vor uns. Zeigt er zurueck, ist es die falsche Seite.
                 */
                if (math.dot(zumAsphalt, richtung) <= 0f)
                {
                    uebersprungenFalscheSeite++;
                    continue;
                }

                /*
                 * DIE SEITE WIRD GEMESSEN, NICHT HERGELEITET.
                 *
                 * `linksseite` kommt aus einer Zuordnung, die ich nur aus dem
                 * Vorzeichen des Kurvenversatzes abgeleitet hatte. Am
                 * 2026-08-27 kam bei drei voellig verschiedenen Strassen jedes
                 * Mal exakt 6,00 m Randbreite heraus, waehrend der Nutzer in
                 * RoadBuilder 9 m und 7 m eingestellt hatte - diese Werte
                 * standen jeweils auf der ANDEREN Seite. Bei symmetrischen
                 * Vanilla-Strassen faellt das nie auf.
                 *
                 * Deshalb kommt jetzt ein WELTMASS dazu: der Abstand unserer
                 * Aussenkante zur naechsten befahrbaren Spur. Die Seite, deren
                 * rechnerischer Abstand dazu passt, ist unsere. Das braucht
                 * keine Konvention mehr - es vergleicht zwei Zahlen, die
                 * dasselbe messen.
                 */
                var weltspur = WeltabstandZurSpur(kante, randpunkt, zumAsphalt);
                var spurabstand = weltspur.HatMitte
                    ? weltspur.Mittenabstand : float.NaN;
                var composition = EntityManager.GetComponentData<Composition>(kante);
                if (!MisseQuerschnitt(composition.m_Edge, linksseite, spurabstand,
                        out var randbreite, out var gesamtbreite,
                        out var hatWeg, out var obenIstUnsere,
                        out var notiz)) continue;

                /*
                 * EINE BUCHT KANN NICHT AUS IHREM BAUTEIL HERAUSRAGEN.
                 *
                 * Gemessen am 2026-08-28 mit dem Kantenmesswerkzeug, an zwei
                 * Strassen mit Parkspur:
                 *
                 *   Vanilla:  Mitte 4,00, Breite 2,00 -> Bucht 3,00..5,00
                 *             Bauteil 'Sidewalk With Parking Piece 5' 0..5,00
                 *             passt genau, Ergebnis 3,00 - vom Nutzer als
                 *             perfekt bestaetigt.
                 *   RoadBuilder: Mitte 6,50, Breite 5,50 -> Bucht 3,75..9,25
                 *             Bauteil 'ParkingLane9 Mesh' 0..9,00
                 *             die Bucht ragt 0,25 m HERAUS - und genau um
                 *             diese 0,25 m lag der Belag zu weit.
                 *
                 * Die Spurkurve ist die Bezugslinie des Stellplatzrasters,
                 * nicht die Buchtmitte; sie darf danebenliegen. Das Bauteil
                 * ist die physische Grenze. Wird die Bucht so weit
                 * hineingeschoben, dass sie hineinpasst, kommt im zweiten Fall
                 * 3,50 m heraus und im ersten unveraendert 3,00 m.
                 *
                 * Das ist eine Regel, kein Beispielabgleich: sie trifft beide
                 * gemessenen Faelle exakt und aendert am bestaetigten nichts.
                 */
                if (weltspur.HatRand && weltspur.IstParkspur
                    && ParkbuchtRand(composition.m_Edge, obenIstUnsere,
                        weltspur.RandspurMittenabstand, weltspur.Breite,
                        out var geklammert, out var klammernotiz))
                {
                    notiz += $" | Bucht im Bauteil geklammert: {klammernotiz}";
                    weltspur.Randpunkt = randpunkt + zumAsphalt * geklammert;
                    weltspur.Randrichtung = randrichtung;
                }

                /*
                 * DIE BEBAUBARE FLAECHE SCHLAEGT ALLES ANDERE.
                 *
                 * Sie ist die einzige Quelle, die das Spiel SELBST als Grenze
                 * zwischen Strasse und Nicht-Strasse fuehrt. Spurmitte plus
                 * Breite, Bauteilrand und Buchtklammer sind Schluesse darauf -
                 * gute, aber eben Schluesse. Sie bleiben als Vergleichswerte
                 * im Log und als Rueckfall, wenn ein Querschnitt keine
                 * bebaubare Flaeche fuehrt (Bruecken, Tunnel).
                 */
                var bebaubarGefunden = BebaubareTiefe(composition.m_Edge,
                    obenIstUnsere, out var bebaubareTiefe, out var bebaubarNotiz);

                var querschnittRandbreite = randbreite;
                var asphaltpunkt = randpunkt + zumAsphalt * randbreite;
                var asphaltRichtung = randrichtung;
                if (weltspur.HatRand)
                {
                    /*
                     * DIE WELTSPUR IST DAS ERGEBNIS, NICHT NUR EINE SONDE.
                     *
                     * `LaneSystem` hat diese Kurve bereits aus der aktuellen
                     * Composition gebaut. Ihr tatsaechlicher `PrefabRef`
                     * liefert die Breite. Damit messen wir den Rand, den das
                     * Spiel wirklich benutzt, ohne lokale Seite oder Offset
                     * ein zweites Mal zu deuten. Wie schon zwischen Bauteilen
                     * und Querschnittsspuren gewinnt aber der weiter AUSSEN
                     * liegende Rand: kleinerer Abstand bedeutet im Zweifel
                     * kuerzere Vorflaeche statt Asphalt auf der Fahrbahn.
                     */
                    var weltRandbreite = math.distance(
                        randpunkt, weltspur.Randpunkt);
                    /*
                     * DIE MESSUNG SCHLAEGT DIE HERLEITUNG.
                     *
                     * Hier stand `weltRandbreite <= querschnittRandbreite` -
                     * also "im Zweifel der weiter aussen liegende Rand". Das
                     * war meine Vorgabe, und sie hat am 2026-08-28 genau die
                     * beiden Faelle verdorben, die der Nutzer als zu kurz
                     * gemeldet hat: dort hatte die Weltkante recht (5,00 und
                     * 7,00 m), und die Regel hat sie zugunsten des falschen
                     * Querschnittswerts weggeworfen.
                     *
                     * Die Weltkante misst die Spur, die das Spiel wirklich
                     * gebaut hat, mit ihrer eigenen Breite. Der Querschnitt
                     * ist eine Rechnung darueber. Wenn beide sich
                     * widersprechen, gilt die Messung; der Querschnitt bleibt
                     * Rueckfall, wenn keine Spur mit echter Breite da ist -
                     * und steht weiter zum Vergleich im Log.
                     */
                    var weltGenommen = true;
                    if (weltGenommen)
                    {
                        asphaltpunkt = weltspur.Randpunkt;
                        randbreite = weltRandbreite;
                        var weltHin = asphaltpunkt - randpunkt;
                        if (math.lengthsq(weltHin) > 1e-6f)
                            zumAsphalt = math.normalizesafe(weltHin);
                        if (math.lengthsq(weltspur.Randrichtung) > 0.5f)
                            asphaltRichtung = weltspur.Randrichtung;
                    }

                    notiz += $" | Weltkante {weltRandbreite:F2} m "
                        + $"({(weltspur.IstParkspur ? "Parkspur" : "Fahrspur")} "
                        + $"'{StrassenPrefabname(weltspur.Spur)}', Mitte "
                        + $"{weltspur.RandspurMittenabstand:F2} m, Breite "
                        + $"{weltspur.Breite:F2} m; Querschnitt "
                        + $"{querschnittRandbreite:F2} m) -> "
                        + (weltGenommen ? "Welt" : "Querschnitt")
                        + " genommen"
                        // Die Abweichung ausschreiben: sie ist der Hinweis
                        // darauf, dass eine der beiden Quellen etwas anderes
                        // sieht als die andere.
                        + $", Abweichung {math.abs(weltRandbreite - querschnittRandbreite):F2} m";
                }
                else
                {
                    notiz += " | Keine Weltkante mit echter Spurbreite"
                        + (weltspur.Anzahl > 0
                            ? $" ({weltspur.Anzahl} Spur(en), "
                                + $"{weltspur.FehlendeBreiten} Breite(n) fehlen)"
                            : " (keine reale Fahr-/Parkspur)")
                        + $"; Querschnitt {querschnittRandbreite:F2} m genommen";
                }
                if (bebaubarGefunden)
                {
                    notiz += $" | {bebaubarNotiz} -> BEBAUBAR GENOMMEN "
                        + $"(vorher {randbreite:F2} m, Abweichung "
                        + $"{math.abs(bebaubareTiefe - randbreite):F2} m)";
                    randbreite = bebaubareTiefe;
                    asphaltpunkt = randpunkt + zumAsphalt * bebaubareTiefe;
                    asphaltRichtung = randrichtung;
                }
                else
                {
                    notiz += " | keine bebaubare Flaeche im Querschnitt";
                }
                notiz += " | " + GesetzteAufwertungen(kante);

                beste = abstand;
                ergebnis.Gefunden = true;
                ergebnis.Kante = kante;
                ergebnis.Prefab = StrassenPrefabname(kante);
                ergebnis.HatFussgaengerweg = hatWeg;
                ergebnis.Randbreite = randbreite;
                ergebnis.Gesamtbreite = gesamtbreite;
                ergebnis.Randpunkt = randpunkt;
                ergebnis.Randrichtung = asphaltRichtung;
                ergebnis.ZumAsphalt = zumAsphalt;
                ergebnis.Asphaltpunkt = asphaltpunkt;
                ergebnis.Abstand = abstand;
                ergebnis.Notiz = notiz;
                diagnoseComposition = composition.m_Edge;
                diagnoseOben = obenIstUnsere;
                diagnoseWeltspur = weltspur;
                diagnoseQuerschnitt = querschnittRandbreite;
            }

            if (!ergebnis.Gefunden && string.IsNullOrEmpty(ergebnis.Notiz))
            {
                var naechste = float.IsInfinity(naechsterAbstand)
                    ? "keine Aussenkante berechenbar"
                    : $"naechste Aussenkante {naechsterAbstand:F1} m";
                ergebnis.Notiz = $"Keine Strasse innerhalb {StrassensucheRadius:F0} m "
                    + $"({treffer.Length} Kandidaten im Suchfeld, {gesichtet} geprueft, "
                    + $"{naechste}; verworfen: {uebersprungenEigen} eigenes Netz, "
                    + $"{uebersprungenGeloescht} geloescht/ersetzt, "
                    + $"{uebersprungenUnvollstaendig} ohne Kanten- oder "
                    + $"Querschnittsdaten, {uebersprungenGeometrie} ohne "
                    + $"passende Aussenkante, {uebersprungenFalscheSeite} "
                    + $"falsche Strassenseite; {-uebersprungenVorschau} "
                    + $"Vorschaufassung(en) mitgemessen).";
            }

            /*
             * DIE AUSFUEHRLICHE KANTENMESSUNG IST ZUSCHALTBAR.
             *
             * Sie schrieb bei jedem gefundenen Strassenstueck 15 bis 25
             * Zeilen - gemessen 101 Zeilen in einem einzigen Bau. Gebraucht
             * wird sie, wenn die Vorflaeche nicht sauber an der Fahrbahn
             * endet; das ist seit Tagen nicht mehr der Fall.
             *
             * Sie bleibt vollstaendig erhalten und laeuft wieder, sobald
             * `kantenmessung` in `<Spielordner>/Logs/PLT-AUS.txt` steht.
             */
            if (ergebnis.Gefunden && Mod.An("kantenmessung"))
            {
                MeldeFahrbahnkantenDetails(ergebnis.Kante, diagnoseComposition,
                    diagnoseOben, ergebnis.Randpunkt, ergebnis.ZumAsphalt,
                    diagnoseWeltspur, diagnoseQuerschnitt,
                    ergebnis.Randbreite);
            }
            return ergebnis;
        }

        /**
         * Der naechste Punkt auf einer der vier Aussenkanten dieser Kante.
         *
         * Vier, weil `EdgeGeometry` aus ZWEI Segmenten besteht (Anfang und
         * Ende) und jedes eine linke und eine rechte Randkurve hat.
         */
        private static bool NaechsteAussenkante(EdgeGeometry geometrie, float2 punkt,
            float2 richtung, out float abstand, out bool linksseite,
            out float2 randpunkt, out float2 randrichtung, out float2 gegenpunkt)
        {
            var besterAbstand = float.MaxValue;
            var besteSeite = false;
            var besterPunkt = default(float2);
            var besteRichtung = default(float2);
            var besterGegenpunkt = default(float2);
            var gefunden = false;
            var sonde = new float3(punkt.x, 0f, punkt.y);

            void Pruefe(Bezier4x3 kurve, Bezier4x3 gegenkurve, bool istLinks)
            {
                var flach = kurve;
                flach.a.y = 0f; flach.b.y = 0f; flach.c.y = 0f; flach.d.y = 0f;
                var d = MathUtils.Distance(flach, sonde, out var t);
                if (d >= besterAbstand) return;
                var lage = MathUtils.Position(flach, t);
                var tangente = math.normalizesafe(MathUtils.Tangent(flach, t).xz);
                if (math.lengthsq(tangente) < 0.5f) return;
                /*
                 * HIER STAND EINE ZU SCHARFE SCHRANKE.
                 *
                 * Sie verwarf jeden Kurvenpunkt, der nicht VOR uns lag
                 * (`dot(hin, richtung) < 0`). Gedacht war sie gegen eine
                 * Strasse im Ruecken. Sie trifft aber auch den Fall, in dem
                 * die Strasse SEITLICH liegt - dann ist `hin` fast senkrecht
                 * zu `richtung`, und ein winziges negatives Skalarprodukt
                 * reicht zum Verwerfen. Genau dazu kommt es, wenn ein
                 * Polygonpunkt neben der Zufahrt nicht an der Strasse liegt:
                 * die Zufahrtsachse zeigt dann schraeg, die Strasse liegt
                 * seitlich, und BEIDE Richtungen melden "keine Strasse
                 * innerhalb 32 m" - der Befund des Nutzers.
                 *
                 * Die Schranke wird nicht mehr gebraucht. Seit die Zufahrt in
                 * beide Richtungen misst und die naehere Strasse nimmt, wird
                 * eine Strasse im Ruecken ohnehin von der anderen Sonde
                 * gefunden und ueber den ABSTAND verglichen. Und die falsche
                 * STRASSENSEITE faengt weiterhin die eigene Pruefung im
                 * Aufrufer ab (`zumAsphalt` gegen `richtung`), die genau dafuer
                 * da ist.
                 *
                 * Entschieden wird damit nach der Lage der Zufahrt, nicht nach
                 * einer Annahme ueber ihre Richtung.
                 */
                besterAbstand = d;
                besteSeite = istLinks;
                besterPunkt = new float2(lage.x, lage.z);
                besteRichtung = tangente;
                // Die Gegenkante am GLEICHEN Kurvenparameter - das ist der
                // Querschnitt an dieser Stelle. Der Mittelwert ueber die
                // ganze Kante waere bei einer Kurve die falsche Richtung.
                var gegen = MathUtils.Position(gegenkurve, t);
                besterGegenpunkt = new float2(gegen.x, gegen.z);
                gefunden = true;
            }

            Pruefe(geometrie.m_Start.m_Left, geometrie.m_Start.m_Right, true);
            Pruefe(geometrie.m_Start.m_Right, geometrie.m_Start.m_Left, false);
            Pruefe(geometrie.m_End.m_Left, geometrie.m_End.m_Right, true);
            Pruefe(geometrie.m_End.m_Right, geometrie.m_End.m_Left, false);

            abstand = besterAbstand;
            linksseite = besteSeite;
            randpunkt = besterPunkt;
            randrichtung = besteRichtung;
            gegenpunkt = besterGegenpunkt;
            return gefunden;
        }

        /**
         * Von unserer Aussenkante zum Asphalt hin.
         *
         * Die Normale der Randkurve hat zwei Richtungen; genommen wird die,
         * die zur Gegenkante zeigt - also quer durch die Strasse.
         */
        private static float2 ZumAsphaltHin(float2 randpunkt, float2 randrichtung,
            float2 gegenpunkt)
        {
            var normale = new float2(-randrichtung.y, randrichtung.x);
            var hin = gegenpunkt - randpunkt;
            if (math.lengthsq(hin) < 1e-6f) return math.normalizesafe(normale);
            return math.normalizesafe(
                math.dot(normale, hin) >= 0f ? normale : -normale);
        }

        /**
         * Der kuerzeste Abstand von einem Punkt zu einer befahrbaren Spur
         * dieser Kante, in der WELT gemessen - zur Mitte UND zum Rand.
         *
         * Genommen werden Fahr- UND Parkspuren: eine Parkspur ist Strasse,
         * ueber sie darf die Vorflaeche nicht laufen. Fusswege bleiben
         * draussen, sie liegen ja gerade in dem Streifen, den wir belegen.
         *
         * Der Rand ist die kuerzeste Entfernung zur Kurve minus der halben
         * `NetLaneData.m_Width` des tatsaechlichen Spur-Prefabs. Das ist die
         * reale Weltkante der Spur, keine aus dem Querschnitt vorhergesagte.
         */
        private Weltspuraufmass WeltabstandZurSpur(Entity kante, float2 punkt,
            float2 innenrichtung)
        {
            var ergebnis = new Weltspuraufmass
            {
                Mittenabstand = float.MaxValue,
                Randabstand = float.MaxValue,
            };
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(kante))
                return ergebnis;
            var spuren = EntityManager.GetBuffer<Game.Net.SubLane>(kante, true);
            var sonde = new float3(punkt.x, 0f, punkt.y);
            for (var i = 0; i < spuren.Length; i++)
            {
                var spur = spuren[i].m_SubLane;
                if (spur == Entity.Null || !EntityManager.Exists(spur)) continue;
                if (!EntityManager.HasComponent<Game.Net.CarLane>(spur)
                    && !EntityManager.HasComponent<Game.Net.ParkingLane>(spur))
                    continue;
                if (!EntityManager.HasComponent<Game.Net.Curve>(spur)) continue;
                var kurve = EntityManager
                    .GetComponentData<Game.Net.Curve>(spur).m_Bezier;
                kurve.a.y = 0f; kurve.b.y = 0f; kurve.c.y = 0f; kurve.d.y = 0f;
                var d = MathUtils.Distance(kurve, sonde, out var t);
                var lage3 = MathUtils.Position(kurve, t);
                var lage = new float2(lage3.x, lage3.z);
                var zurMitte = lage - punkt;
                // Nur Spuren quer durch die gefundene Strasse, nie eine am
                // Knoten zufaellig hinter der gewaehlten Aussenkante.
                if (math.lengthsq(zurMitte) > 1e-6f
                    && math.dot(zurMitte, innenrichtung) <= 0f) continue;

                ergebnis.Anzahl++;
                if (d < ergebnis.Mittenabstand)
                {
                    ergebnis.HatMitte = true;
                    ergebnis.Mittenabstand = d;
                }

                var breite = 0f;
                if (EntityManager.HasComponent<PrefabRef>(spur))
                {
                    var prefab = EntityManager
                        .GetComponentData<PrefabRef>(spur).m_Prefab;
                    if (prefab != Entity.Null
                        && EntityManager.HasComponent<NetLaneData>(prefab))
                    {
                        breite = EntityManager
                            .GetComponentData<NetLaneData>(prefab).m_Width;
                    }
                }
                if (breite <= 0f)
                {
                    ergebnis.FehlendeBreiten++;
                    continue;
                }

                var randabstand = math.max(0f, d - breite * 0.5f);
                if (ergebnis.HatRand && randabstand >= ergebnis.Randabstand)
                    continue;

                var zumRand = math.normalizesafe(zurMitte);
                var tangente = math.normalizesafe(MathUtils.Tangent(kurve, t).xz);
                ergebnis.HatRand = true;
                ergebnis.Randabstand = randabstand;
                ergebnis.RandspurMittenabstand = d;
                ergebnis.Breite = breite;
                ergebnis.Randpunkt = math.lengthsq(zumRand) > 0.5f
                    ? punkt + zumRand * randabstand : punkt;
                ergebnis.Randrichtung = tangente;
                ergebnis.Spur = spur;
                ergebnis.IstParkspur = EntityManager
                    .HasComponent<Game.Net.ParkingLane>(spur);
            }
            return ergebnis;
        }

        /**
         * Der Querschnitt: wie viel liegt auf unserer Seite zwischen
         * Aussenkante und Asphalt?
         *
         * Beide Raender kommen aus derselben Teileliste, damit sich jede
         * Vorzeichenkonvention herauskuerzt. `m_Left` gehoert laut
         * `GeometrySystem` zum groesseren Querwert - also zu `alleMax`.
         */
        private bool MisseQuerschnitt(Entity compositionPrefab, bool linksseite,
            float spurabstand,
            out float randbreite, out float gesamtbreite, out bool hatWeg,
            out bool obenIstUnsere, out string notiz)
        {
            randbreite = 0f;
            gesamtbreite = 0f;
            hatWeg = false;
            obenIstUnsere = false;
            notiz = string.Empty;
            if (compositionPrefab == Entity.Null
                || !EntityManager.HasComponent<NetCompositionData>(compositionPrefab)
                || !EntityManager.HasBuffer<NetCompositionPiece>(compositionPrefab))
            {
                notiz = "Kante ohne Querschnittsdaten.";
                return false;
            }

            var daten = EntityManager
                .GetComponentData<NetCompositionData>(compositionPrefab);
            gesamtbreite = daten.m_Width;
            var teile = EntityManager.GetBuffer<NetCompositionPiece>(
                compositionPrefab, isReadOnly: true);
            var alleMin = float.MaxValue;
            var alleMax = float.MinValue;
            var fahrbahnMin = float.MaxValue;
            var fahrbahnMax = float.MinValue;
            for (var i = 0; i < teile.Length; i++)
            {
                var teil = teile[i];
                if (teil.m_Size.x <= 0f) continue;
                var links = teil.m_Offset.x - teil.m_Size.x * 0.5f;
                var rechts = teil.m_Offset.x + teil.m_Size.x * 0.5f;
                alleMin = math.min(alleMin, links);
                alleMax = math.max(alleMax, rechts);
                if ((teil.m_PieceFlags & NetPieceFlags.HasRoadLanes) == 0) continue;
                fahrbahnMin = math.min(fahrbahnMin, links);
                fahrbahnMax = math.max(fahrbahnMax, rechts);
            }

            if (alleMax <= alleMin)
            {
                notiz = "Querschnitt ohne messbare Teile.";
                return false;
            }
            /*
             * SPUREN ZAEHLEN MIT, NICHT NUR BAUTEILE.
             *
             * `HasRoadLanes` sitzt am Bauteil. Der Nutzer hat am 2026-08-27
             * eine RoadBuilder-Strasse mit Parkbuchten gebaut, und dort trug
             * das Parkstreifen-Teil das Flag nicht - unsere Messung hielt den
             * Streifen fuer Gehweg und pflasterte darueber. Eine Parkspur ist
             * aber Strasse.
             *
             * Die Spurliste des Querschnitts kennt beide Sorten. Genommen wird
             * am Ende der weiter AUSSEN liegende der beiden Raender: lieber
             * eine Vorflaeche, die einen halben Meter zu kurz ist, als eine,
             * die auf der Fahrbahn liegt.
             */
            var spurMin = float.MaxValue;
            var spurMax = float.MinValue;
            var spurRandMin = float.MaxValue;
            var spurRandMax = float.MinValue;
            var fehlendeBreiten = 0;
            if (EntityManager.HasBuffer<NetCompositionLane>(compositionPrefab))
            {
                var spuren = EntityManager.GetBuffer<NetCompositionLane>(
                    compositionPrefab, isReadOnly: true);
                for (var i = 0; i < spuren.Length; i++)
                {
                    var flags = spuren[i].m_Flags;
                    if ((flags & (LaneFlags.Road | LaneFlags.Parking)) == 0) continue;
                    if ((flags & LaneFlags.Pedestrian) != 0) continue;
                    var mitte = spuren[i].m_Position.x;
                    spurMin = math.min(spurMin, mitte);
                    spurMax = math.max(spurMax, mitte);
                    /*
                     * DIE ECHTE SPURBREITE, NICHT MEINE SCHAETZUNG.
                     *
                     * Der erste Anlauf rechnete mit pauschal 1,50 m halber
                     * Spurbreite. Bei einer RoadBuilder-Strasse trugen die
                     * BAUTEILE gar kein `HasRoadLanes`, also stammte der
                     * Fahrbahnrand allein aus dieser Schaetzung - und lag
                     * 1,50 m zu weit aussen. Im Bild des Nutzers blieb genau
                     * ein heller Streifen Gehweg uebrig.
                     *
                     * `NetLaneData.m_Width` am Spur-Prefab nennt die Breite.
                     */
                    /*
                     * EINE GESCHAETZTE BREITE DARF KEINE KANTE FESTLEGEN.
                     *
                     * Gemessen am 2026-08-28 an vier Bauten des Nutzers: in
                     * genau den beiden Faellen, in denen eine Spurbreite
                     * geschaetzt werden musste, lag der Querschnittswert
                     * daneben (3,50 statt 5,00 bei Rasen; 5,50 statt 7,00 bei
                     * einer breiten RoadBuilder-Strasse). In den beiden
                     * Faellen ohne Schaetzung stimmte er auf den Zentimeter.
                     *
                     * Rechnet man die geschaetzte Spur heraus, kommt exakt die
                     * unabhaengig gemessene Weltkante heraus. Solche Spuren
                     * gehen deshalb nur noch in die MITTEN ein - die tragen
                     * die Seitenwahl und brauchen keine Breite - aber nicht
                     * mehr in den Fahrbahnrand.
                     */
                    var lane = spuren[i].m_Lane;
                    var breiteDerSpur = 0f;
                    if (lane != Entity.Null
                        && EntityManager.HasComponent<NetLaneData>(lane))
                        breiteDerSpur = EntityManager
                            .GetComponentData<NetLaneData>(lane).m_Width;
                    if (breiteDerSpur <= 0f)
                    {
                        fehlendeBreiten++;
                        continue;
                    }
                    var halbe = breiteDerSpur * 0.5f;
                    spurRandMin = math.min(spurRandMin, mitte - halbe);
                    spurRandMax = math.max(spurRandMax, mitte + halbe);
                }
            }
            var hatSpuren = spurMax >= spurMin;

            if (fahrbahnMax <= fahrbahnMin && !hatSpuren)
            {
                // Fussgaengerbruecken, Bahnstrecken, Zaeune: kein Asphalt,
                // also auch keine Vorflaeche. Kein Fehler, nur nichts zu tun.
                notiz = "Kante ohne Fahrspuren.";
                return false;
            }

            /*
             * Der aeusserste Fahrbahnrand je Seite. `HalbeSpur` deckt ab, dass
             * `NetCompositionLane.m_Position` die MITTE einer Spur nennt.
             */
            var bahnMax = float.MinValue;
            var bahnMin = float.MaxValue;
            if (fahrbahnMax > fahrbahnMin)
            {
                bahnMax = fahrbahnMax;
                bahnMin = fahrbahnMin;
            }
            if (hatSpuren)
            {
                bahnMax = math.max(bahnMax, spurRandMax);
                bahnMin = math.min(bahnMin, spurRandMin);
            }
            bahnMax = math.min(bahnMax, alleMax);
            bahnMin = math.max(bahnMin, alleMin);

            var randOben = math.max(0f, alleMax - bahnMax);
            var randUnten = math.max(0f, bahnMin - alleMin);

            /*
             * WELCHE SEITE IST UNSERE? Die Antwort kommt aus dem Vergleich
             * zweier Zahlen, die dasselbe messen: dem gemessenen Weltabstand
             * unserer Aussenkante zur naechsten Spur und dem rechnerischen
             * Abstand je Seite. Passt keiner (kein Spurmass verfuegbar),
             * bleibt es bei der hergeleiteten Seite - und die Meldung sagt es.
             */
            obenIstUnsere = linksseite;
            var grund = "hergeleitet";
            if (hatSpuren && !float.IsNaN(spurabstand))
            {
                var erwartetOben = math.abs(alleMax - spurMax);
                var erwartetUnten = math.abs(spurMin - alleMin);
                var abweichungOben = math.abs(erwartetOben - spurabstand);
                var abweichungUnten = math.abs(erwartetUnten - spurabstand);
                if (math.abs(abweichungOben - abweichungUnten) > 0.10f)
                {
                    obenIstUnsere = abweichungOben < abweichungUnten;
                    grund = $"gemessen (Spur {spurabstand:F2} m, erwartet oben "
                        + $"{erwartetOben:F2} / unten {erwartetUnten:F2})";
                }
                else
                {
                    grund = $"symmetrisch (Spur {spurabstand:F2} m)";
                }
            }
            else if (float.IsNaN(spurabstand))
            {
                grund = "hergeleitet (keine Spur in der Welt gefunden)";
            }

            // Das Gehweg-Flag gehoert zu DER Seite, die wir wirklich nehmen.
            var seiteEcht = obenIstUnsere ? daten.m_Flags.m_Left : daten.m_Flags.m_Right;
            hatWeg = (seiteEcht & (CompositionFlags.Side.Sidewalk
                | CompositionFlags.Side.WideSidewalk)) != 0;

            randbreite = obenIstUnsere ? randOben : randUnten;
            /*
             * BEIDE QUELLEN GETRENNT NENNEN. Der erste Anlauf loggte nur den
             * kombinierten Wert - dadurch war nicht zu sehen, dass die
             * Bauteile nichts beigetragen hatten und alles an der Spurbreite
             * hing. Genau das war die Ursache.
             */
            var ausTeilen = fahrbahnMax > fahrbahnMin
                ? $"{fahrbahnMin:F2}..{fahrbahnMax:F2}" : "keine";
            var ausSpuren = hatSpuren
                ? $"{spurRandMin:F2}..{spurRandMax:F2} (Mitten {spurMin:F2}..{spurMax:F2}"
                    + (fehlendeBreiten > 0
                        ? $", {fehlendeBreiten} Breite(n) fehlen)" : ")")
                : "keine";
            notiz = $"Querschnitt {alleMin:F2}..{alleMax:F2} m | Fahrbahn aus "
                + $"Bauteilen {ausTeilen}, aus Spuren {ausSpuren} -> "
                + $"{bahnMin:F2}..{bahnMax:F2} | Seiten {randUnten:F2}/{randOben:F2} m, "
                + "genommen " + (obenIstUnsere ? "oben" : "unten") + " - " + grund
                + $" | Composition-Flags L[{Flagtext(daten.m_Flags.m_Left)}] "
                + $"R[{Flagtext(daten.m_Flags.m_Right)}], unsere "
                + $"[{Flagtext(seiteEcht)}]";
            return true;
        }

        /** Die am Strassensegment gesetzten Aufwertungen, nicht nur die
         * daraus bereits ausgewaehlte Composition. So ist "mit Rasen" in
         * derselben Diagnosezeile beweisbar. */
        private string GesetzteAufwertungen(Entity kante)
        {
            if (!EntityManager.HasComponent<Upgraded>(kante))
                return "Upgraded-Flags keine";
            var flags = EntityManager.GetComponentData<Upgraded>(kante).m_Flags;
            return $"Upgraded-Flags M[{Flagtext(flags.m_General)}] "
                + $"L[{Flagtext(flags.m_Left)}] R[{Flagtext(flags.m_Right)}]";
        }

        private static string Flagtext(CompositionFlags.General flags)
        {
            return flags == 0 ? "keine" : flags.ToString();
        }

        private static string Flagtext(CompositionFlags.Side flags)
        {
            return flags == 0 ? "keine" : flags.ToString();
        }

        /**
         * DIE BEBAUBARE FLAECHE DES QUERSCHNITTS - die gesuchte Grenze.
         *
         * CS2 fuehrt sie selbst: `NetCompositionArea` mit `NetAreaFlags.Buildable`
         * beschreibt je Seite den Streifen, der NICHT Fahrbahn ist. Genau dort
         * soll unsere Vorflaeche enden.
         *
         * Am 2026-08-28 an vier gebauten Strassen des Nutzers geprueft, jede
         * mit seinem Sichturteil (Abstand von unserer Aussenkante):
         *
         *   Vanilla Small Road      Buildable bis 3,00   bisher 3,00   PERFEKT
         *   Vanilla mit Rasen       Buildable bis 4,50   bisher 5,00   0,50 zu weit
         *   RoadBuilder breit       Buildable bis 6,50   bisher 7,00   0,50 zu weit
         *   RoadBuilder mit Buchten Buildable bis 3,50   bisher 3,50   PERFEKT
         *
         * Beide bestaetigten Faelle bleiben unveraendert, beide beanstandeten
         * werden auf den Zentimeter erklaert. Vier von vier - und zwar mit
         * einer Zahl, die das Spiel selbst fuehrt, statt aus Spurmitten,
         * Spurbreiten oder Bauteilraendern GESCHLOSSEN zu werden. Alle
         * bisherigen Versuche waren solche Schluesse; deshalb trafen sie mal
         * und mal nicht.
         *
         * Genommen wird die bebaubare Flaeche, die an UNSERER Aussenkante
         * beginnt. Loecher und Mittelstreifen zaehlen nicht.
         *
         * UND ES BLEIBT DABEI - ein Versuch, von hier aus noch ueber den
         * Bordstein bis zum Bauteilrand zu gehen, ist am 2026-08-28
         * GESCHEITERT und wurde zurueckgenommen. Er hatte einen Mechanismus
         * ("eine Spur ist benutzte Strasse, der Bordstein ist nur Kante") und
         * liess die beiden bestaetigten Faelle rechnerisch unangetastet -
         * trotzdem meldete der Nutzer danach Fall 4 als "drueber", der vorher
         * perfekt war. Die Regel war also falsch, und die Rechnung hat es
         * nicht gezeigt.
         *
         * Die bebaubare Flaeche allein ist der Stand, in dem sich alle Faelle
         * GLEICH verhalten: knapp vor dem Bordstein, nie auf der Fahrbahn.
         * Das ist die Wahl des Nutzers ("wir nehmen das wo es bei 2-5 am
         * besten geklappt hat"), und sie ist die sichere Seite.
         */
        private bool BebaubareTiefe(Entity compositionPrefab, bool obenIstUnsere,
            out float tiefe, out string notiz)
        {
            tiefe = 0f;
            notiz = string.Empty;
            if (compositionPrefab == Entity.Null
                || !EntityManager.HasBuffer<NetCompositionArea>(compositionPrefab)
                || !EntityManager.HasBuffer<NetCompositionPiece>(compositionPrefab))
                return false;

            var teile = EntityManager.GetBuffer<NetCompositionPiece>(
                compositionPrefab, isReadOnly: true);
            var alleMin = float.MaxValue;
            var alleMax = float.MinValue;
            for (var i = 0; i < teile.Length; i++)
            {
                if (teile[i].m_Size.x <= 0f) continue;
                alleMin = math.min(alleMin,
                    teile[i].m_Offset.x - teile[i].m_Size.x * 0.5f);
                alleMax = math.max(alleMax,
                    teile[i].m_Offset.x + teile[i].m_Size.x * 0.5f);
            }
            if (alleMax <= alleMin) return false;

            var flaechen = EntityManager.GetBuffer<NetCompositionArea>(
                compositionPrefab, isReadOnly: true);
            var gefunden = false;
            for (var i = 0; i < flaechen.Length; i++)
            {
                var flaeche = flaechen[i];
                if ((flaeche.m_Flags & NetAreaFlags.Buildable) == 0) continue;
                if ((flaeche.m_Flags
                    & (NetAreaFlags.Hole | NetAreaFlags.Median)) != 0) continue;
                if (flaeche.m_Width <= 0f) continue;
                var x0 = flaeche.m_Position.x - flaeche.m_Width * 0.5f;
                var x1 = flaeche.m_Position.x + flaeche.m_Width * 0.5f;
                var d0 = obenIstUnsere ? alleMax - x1 : x0 - alleMin;
                var d1 = obenIstUnsere ? alleMax - x0 : x1 - alleMin;
                // Nur die Flaeche an UNSERER Aussenkante; die Gegenseite
                // beginnt weit drinnen.
                if (d0 > 0.10f) continue;
                if (d1 <= tiefe) continue;
                tiefe = d1;
                notiz = $"bebaubar {d0:F2}..{d1:F2} m [{flaeche.m_Flags}]";
                gefunden = true;
            }
            return gefunden && tiefe > 0f;
        }

        /**
         * Der aeussere Rand einer Parkbucht, geklammert auf ihr Bauteil.
         *
         * Alles in ABSTAND VON UNSERER AUSSENKANTE, damit die
         * Seitenkonvention keine Rolle spielt. Das Bauteil ist dasjenige,
         * dessen Spanne die Spurmitte enthaelt - geometrisch, ohne Abgleich
         * von Prefabnamen. Bei mehreren Treffern gewinnt das schmalste.
         */
        private bool ParkbuchtRand(Entity compositionPrefab, bool obenIstUnsere,
            float mitte, float breite, out float rand, out string notiz)
        {
            rand = 0f;
            notiz = string.Empty;
            if (breite <= 0f || compositionPrefab == Entity.Null
                || !EntityManager.HasBuffer<NetCompositionPiece>(compositionPrefab))
                return false;

            var teile = EntityManager.GetBuffer<NetCompositionPiece>(
                compositionPrefab, isReadOnly: true);
            var alleMin = float.MaxValue;
            var alleMax = float.MinValue;
            for (var i = 0; i < teile.Length; i++)
            {
                if (teile[i].m_Size.x <= 0f) continue;
                alleMin = math.min(alleMin, teile[i].m_Offset.x - teile[i].m_Size.x * 0.5f);
                alleMax = math.max(alleMax, teile[i].m_Offset.x + teile[i].m_Size.x * 0.5f);
            }
            if (alleMax <= alleMin) return false;

            var gefunden = false;
            var teilAussen = 0f;
            var teilInnen = 0f;
            var schmalste = float.MaxValue;
            for (var i = 0; i < teile.Length; i++)
            {
                var teil = teile[i];
                if (teil.m_Size.x <= 0f) continue;
                var x0 = teil.m_Offset.x - teil.m_Size.x * 0.5f;
                var x1 = teil.m_Offset.x + teil.m_Size.x * 0.5f;
                var d0 = obenIstUnsere ? alleMax - x1 : x0 - alleMin;
                var d1 = obenIstUnsere ? alleMax - x0 : x1 - alleMin;
                if (mitte < d0 - 0.01f || mitte > d1 + 0.01f) continue;
                if (teil.m_Size.x >= schmalste) continue;
                schmalste = teil.m_Size.x;
                teilAussen = d0;
                teilInnen = d1;
                gefunden = true;
            }
            if (!gefunden) return false;

            var innen = math.min(mitte + breite * 0.5f, teilInnen);
            rand = math.max(innen - breite, teilAussen);
            notiz = $"Bauteil {teilAussen:F2}..{teilInnen:F2} m, Bucht "
                + $"{mitte - breite * 0.5f:F2}..{mitte + breite * 0.5f:F2} -> "
                + $"Rand {rand:F2} m";
            return true;
        }

        private string StrassenPrefabname(Entity entity)
        {
            if (!EntityManager.HasComponent<PrefabRef>(entity)) return "unbekannt";
            var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null) return "unbekannt";
            try
            {
                var system = World.GetExistingSystemManaged<PrefabSystem>();
                return system != null ? system.GetPrefabName(prefab) : "unbekannt";
            }
            catch (Exception)
            {
                return "unbekannt";
            }
        }
    }
}
