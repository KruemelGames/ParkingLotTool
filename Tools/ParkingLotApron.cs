using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * DIE VORFLAECHE: BELAG VON DER ZUFAHRT BIS AN DIE STRASSE.
     *
     * Bis hierher endet der Parkplatz hart am gezeichneten Polygon. Dahinter
     * liegt der Fussgaengerweg der Stadtstrasse, und die Zufahrt fuehrt
     * sichtbar ueber fremden Boden - der Parkplatz sieht aus, als hoere er an
     * einer unsichtbaren Grundstuecksgrenze auf. Die Vorflaeche schliesst
     * diese Luecke: derselbe Belag laeuft ueber den Fussgaengerweg weiter bis
     * an den Asphalt.
     *
     * VIER REGELN, ALLE VOM NUTZER:
     *
     *   1. Der Winkel bleibt der der Zufahrt. Die Flaeche dreht sich NICHT,
     *      um rechtwinklig auf die Strasse zu treffen.
     *   2. Der Abschluss ist buendig mit der Strasse. Steht die Zufahrt
     *      schief, wird eine Seite kuerzer und die andere laenger - das
     *      ergibt sich hier von selbst, weil beide Seitenstrahlen dieselbe
     *      Strassenlinie treffen, nur an verschiedenen Stellen.
     *   3. Flaeche 1, also derselbe Belag wie die Zufahrt. Deshalb haengen
     *      die Ringe an `AsphaltSurface` und nicht an einer eigenen Liste.
     *   4. NUR Zufahrt, Einfahrt und Ausfahrt. Der Fussweg bekommt keine.
     *
     * SIE DARF AUSSERHALB DES POLYGONS LIEGEN - ausdruecklich erlaubt. Genau
     * deshalb entsteht sie hier und nicht in `ParkingGeometry`: der
     * Flaechenkern arbeitet als Zellenraster INNERHALB des Polygons, und die
     * Strassendaten gibt es ohnehin nur zur Laufzeit. Angehaengt wird erst,
     * wenn das Layout fertig ist; Vorschau, Bau, Signatur und Zaehlung
     * nehmen die Ringe dann ohne eigene Sonderbehandlung mit.
     *
     * WOHER DIE ZUFAHRTEN KOMMEN: aus `layout.NetLine`. Dort tragen die
     * Segmente Kind UND Art an EINEM Eintrag. Ueber Indexfenster in
     * `EntranceQuad` zu gehen waere falsch - im Zellenmodell sind das
     * Rasterstuecke, keine Zufahrtsrechtecke.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Laenger als das ist keine Vorflaeche mehr plausibel. Beim Vanilla-
         * Querschnitt liegen zwischen Grundstueck und Asphalt hoechstens
         * Gehweg (bis 7 m) und Gruenstreifen.
         */
        private const float VorflaecheMaxLaenge = 25f;

        /**
         * Wie weit die Vorflaeche INS Polygon hineinreicht - NULL.
         *
         * Erst waren es 1,5 m, um die Naht auf der Polygonkante zu
         * ueberdecken. Der Nutzer meldet am 2026-08-28 aber genau diese
         * Ueberlappung als sichtbar: "es sieht weiterhin aus als waeren 2
         * Flaechen vorhanden, denn es gibt eine Ueberlappung am Rande vom
         * Polygon nach dem Bauen".
         *
         * Das ist zwangslaeufig so. Die Vorflaeche haengt an einem EIGENEN
         * Prefab (sie braucht die Decal-Ebene `Roads`), und ein eigenes
         * Prefab heisst ein eigenes Material. Zwei Materialien
         * uebereinander werden doppelt gezeichnet - der ueberlappte
         * Streifen wird dunkler, und genau das sieht man.
         *
         * Ueberdecken allein half nicht - Aneinanderstossen aber auch
         * nicht, und der Nutzer hat gesagt warum: die ECKENRUNDUNG zeichnet
         * die Trennlinie nach, egal ob die Flaechen sich beruehren oder
         * ueberlappen.
         *
         * Die Rundung ist am Klon jetzt abgeschaltet (ParkingLotApronPrefab).
         * Damit wird die Ueberlappung wieder sinnvoll: die Vorflaeche legt
         * sich mit GERADER Kante ueber den Parkplatzbelag und verdeckt dessen
         * abgerundete Ecken an der Polygonkante.
         *
         * DIE UEBERLAPPUNG IST TROTZDEM WIEDER 0 - sie hat einen neuen
         * Schaden gemacht. Der Nutzer meldet am 2026-08-28 ein Flattern an
         * der Kante zwischen dem Gras der Zufahrt und dem Asphalt der ersten
         * Bucht, mal beidseitig, mal einseitig, dazu eine Luecke im Terrain.
         * Ort und Zeitpunkt passen genau: seit der 1,0-m-Ueberlappung liegen
         * dort zwei Flaechen praktisch auf gleicher Hoehe uebereinander.
         *
         * Mit abgeschalteter Rundung braucht es sie auch nicht mehr: die
         * Vorflaeche hat gerade Kanten und stoesst sauber an. Genau diese
         * Kombination - keine Rundung UND keine Ueberlappung - gab es noch
         * nicht; vorher war immer eines von beiden im Spiel.
         */
        private const float VorflaecheInnenreserve = 0f;

        /** Kuerzer als das lohnt keine eigene Flaeche. */
        private const float VorflaecheMinLaenge = 0.20f;

        /**
         * Kuerzeste erlaubte Einzelseite. Bei 0 fielen zwei Ecken des Rings
         * aufeinander; ein Ring mit Doppelpunkt ist kein gueltiges Polygon.
         */
        private const float VorflaecheMinSeite = 0.05f;

        private string _letzteVorflaechenmeldung = string.Empty;

        /**
         * Die Vorflaechen des zuletzt gerechneten Layouts.
         *
         * BEWUSST NICHT in `layout.AsphaltSurface` gehaengt. Der erste Anlauf
         * tat genau das - bequem, weil dann Vorschau, Bau und Zaehlung ohne
         * Zutun mitliefen. Nur braucht die Vorflaeche ein ANDERES Prefab als
         * der Parkplatzbelag (siehe ParkingLotApronPrefab), und das geht in
         * einer gemeinsamen Liste nicht.
         */
        private float2[][] _vorflaechen = Array.Empty<float2[]>();

        /**
         * Die Zugangsart je Vorflaeche, index-treu zu `_vorflaechen`.
         *
         * Nur fuer die Vorschau: dort bekommt jede Vorflaeche die Farbe ihrer
         * Zufahrt, damit man sieht, WELCHE Einfahrt sich da fortsetzt.
         */
        private int[] _vorflaechenArt = Array.Empty<int>();

        /**
         * Je Vorflaeche vier Punkte: innen links, innen rechts, aussen
         * links, aussen rechts. Grundlage der Verschmelzung mit dem
         * Asphaltring, siehe `ParkingLotApronMerge`.
         */
        private float2[][] _vorflaechenKante = Array.Empty<float2[]>();

        /**
         * Nur der Teil AUSSERHALB des Polygons, allein fuer die Vorschau.
         *
         * Gebaut wird die Vorflaeche in einem Stueck von der Fahrgasse bis
         * zur Strasse. Im Overlay steht der innere Teil aber schon als
         * Zufahrtsrechteck; beides uebereinander wuerde die Farbe verdoppeln
         * und wie ein Fehler aussehen.
         */
        private float2[][] _vorflaechenSicht = Array.Empty<float2[]>();

        /**
         * Haengt die Vorflaechen an `layout.AsphaltSurface` an.
         *
         * Laeuft auf dem Hauptfaden - `MisseStrasse` liest den
         * EntityManager, der Geometrielauf selbst steckt in einem
         * Hintergrundtask und darf das nicht.
         */
        private void ErgaenzeVorflaechen(ParkingLayout layout,
            LayoutSettings settings, float2[] site)
        {
            _vorflaechen = Array.Empty<float2[]>();
            _vorflaechenSicht = Array.Empty<float2[]>();
            _vorflaechenArt = Array.Empty<int>();
            _vorflaechenKante = Array.Empty<float2[]>();
            if (layout?.NetLine == null || site == null || site.Length < 3) return;
            if (!(_uiSystem?.VorflaecheAn ?? true)) return;
            // Die Vorflaeche IST Flaeche 1. Wer den Belag ganz abschaltet,
            // will auch vor der Zufahrt keinen.
            if (!(_uiSystem?.FlaecheStrasseAn ?? true)) return;

            /*
             * CS2 ZEICHNET FLAECHEN GROESSER, ALS SIE SIND.
             *
             * `AreaBatchSystem` setzt je Flaechen-Prefab
             * `RenderedAreaData.m_ExpandAmount` und weitet die Flaeche beim
             * Zeichnen um diesen Betrag auf. Die gezeichnete Kante liegt also
             * um genau diesen Wert weiter draussen als die gerechnete.
             *
             * Gemessen am 2026-08-28: in den beiden Faellen, in denen unsere
             * Flaeche an die FAHRBAHN grenzt (Rasen, breite RoadBuilder-
             * Strasse), sass die Rechnung exakt auf der Bauteilgrenze - die
             * Fahrspur fuellt dort ihr Bauteil vollstaendig aus - und der
             * Nutzer sah trotzdem einen Ueberstand. In den beiden Faellen mit
             * PARKSPUR daneben faellt derselbe Ueberstand nicht auf, weil er
             * auf gleichfarbigem Belag landet.
             *
             * Deshalb wird der Aufweitungsbetrag abgezogen. Er wird GELESEN,
             * nicht geschaetzt, und steht in der Meldezeile - traegt die
             * Erklaerung nicht, sieht man es dort sofort.
             */
            var aufweitung = Flaechenaufweitung();

            var ringe = new List<float2[]>();
            var sicht = new List<float2[]>();
            var arten = new List<int>();
            var kanten = new List<float2[]>();
            var meldungen = new List<string>();
            foreach (var segment in layout.NetLine)
            {
                if (!string.Equals(segment.Kind, "entrance", StringComparison.Ordinal))
                    continue;
                // Regel 4: der Fussweg bekommt keine Vorflaeche.
                if (segment.Art == Zufahrtsart.Fussweg) continue;

                var breite = (float)new Entrance { Art = segment.Art }
                    .Breite(settings?.Ai ?? 6.0);
                if (breite <= 0f) continue;

                var ring = VorflaecheFuer(segment, breite, aufweitung,
                    layout.EntranceQuad,
                    out var aussenteil, out var innenkante, out var meldung);
                if (ring != null)
                {
                    ringe.Add(ring);
                    sicht.Add(aussenteil ?? ring);
                    arten.Add((int)segment.Art);
                    kanten.Add(innenkante);
                }
                if (!string.IsNullOrEmpty(meldung)) meldungen.Add(meldung);
            }

            MeldeVorflaechen(ringe.Count, meldungen);
            _vorflaechen = ringe.ToArray();
            _vorflaechenSicht = sicht.ToArray();
            _vorflaechenArt = arten.ToArray();
            _vorflaechenKante = kanten.ToArray();
        }

        /**
         * Eine Vorflaeche. Gibt `null` zurueck, wenn es nichts zu belegen
         * gibt; die Meldung sagt in dem Fall warum.
         */
        /**
         * Wie weit CS2 unsere Flaeche beim Zeichnen aufweitet.
         *
         * Bevorzugt vom Vorflaechen-Prefab; solange das noch nicht steht, vom
         * Belag-Prefab - beide teilen denselben Rundungswert, aus dem der
         * Betrag entsteht. Ist keins auflesbar, wird nichts abgezogen.
         */
        private float Flaechenaufweitung()
        {
            foreach (var prefab in new[] { _vorflaechenPrefab, _pavementSurfacePrefab })
            {
                if (prefab == Entity.Null || !EntityManager.Exists(prefab)) continue;
                if (!EntityManager.HasComponent<Game.Prefabs.RenderedAreaData>(prefab))
                    continue;
                var wert = EntityManager
                    .GetComponentData<Game.Prefabs.RenderedAreaData>(prefab)
                    .m_ExpandAmount;
                if (wert > 0f) return wert;
            }
            return 0f;
        }

        private float2[] VorflaecheFuer(NetSegment segment, float breite,
            float aufweitung, float2[][] zufahrtsrechtecke,
            out float2[] aussenteil, out float2[] innenkante,
            out string meldung)
        {
            meldung = null;
            aussenteil = null;
            innenkante = null;
            /*
             * Welches Ende zeigt nach draussen? Das Zufahrtssegment beginnt
             * auf der Polygonkante und laeuft nach innen; das aeussere Ende
             * liegt also praktisch auf dem Rand, das innere mehrere Meter
             * davon entfernt. Ueber den Randabstand ist das eindeutig, ohne
             * dass wir uns auf eine Punktreihenfolge verlassen muessten.
             */
            /*
             * DIE ZUFAHRT ENTSCHEIDET SELBST, WO AUSSEN IST.
             *
             * Bis zum 2026-08-31 kam diese Entscheidung aus dem POLYGON: das
             * Ende mit dem kleineren Randabstand galt als aussen. Der
             * Parkplatzumriss weiss aber nicht, wo die Strasse liegt. Wo die
             * beiden Enden aehnlich weit vom Rand entfernt waren, stieg die
             * Funktion ganz aus - und wo die Reihenfolge kippte, zeigte
             * `richtung` nach INNEN. Dann sucht `MisseStrasse` in den
             * Parkplatz hinein und meldet "Keine Strasse innerhalb 32 m",
             * obwohl die Strasse zwei Meter hinter dem anderen Ende liegt.
             * Genau diese Meldung steht im Log des Nutzers, an einer Zufahrt,
             * die sichtbar an einer Strasse haengt.
             *
             * Jetzt wird in BEIDE Richtungen gemessen und die genommen, die
             * eine Strasse findet - bei zweien die naehere. Das Polygon ist
             * an der Entscheidung nicht mehr beteiligt.
             */
            var achse = math.normalizesafe(segment.B - segment.A);
            if (math.lengthsq(achse) < 0.5f)
            {
                meldung = $"{Art(segment.Art)}: Zufahrt ohne Richtung.";
                return null;
            }

            var nachB = MisseStrasse(segment.B, achse);
            var nachA = MisseStrasse(segment.A, -achse);
            var wegB = nachB.Gefunden
                ? math.distance(segment.B, nachB.Asphaltpunkt)
                : float.PositiveInfinity;
            var wegA = nachA.Gefunden
                ? math.distance(segment.A, nachA.Asphaltpunkt)
                : float.PositiveInfinity;

            if (!nachA.Gefunden && !nachB.Gefunden)
            {
                meldung = $"{Art(segment.Art)}: keine Strasse gefunden - "
                    + $"ab A ({nachA.Notiz}), ab B ({nachB.Notiz}).";
                return null;
            }

            var nachAussenB = wegB <= wegA;
            var aussen = nachAussenB ? segment.B : segment.A;
            var innen = nachAussenB ? segment.A : segment.B;
            var richtung = nachAussenB ? achse : -achse;
            var aufmass = nachAussenB ? nachB : nachA;
            var seitenwahl = "aussen ist "
                + (nachAussenB ? "B" : "A") + " (Weg zur Strasse "
                + Weg(wegA) + " ab A, " + Weg(wegB) + " ab B)";

            var quer = new float2(-richtung.y, richtung.x);
            /*
             * EINE FLAECHE, NICHT ZWEI.
             *
             * Der erste Anlauf begann 0,30 m innerhalb der Polygonkante. Damit
             * lag die Naht zwischen Zufahrtsbelag und Vorflaeche GENAU auf der
             * Kante - der Nutzer hat sie dreimal gemeldet: "die beiden
             * Flaechen sind weiterhin 2 und nicht eine".
             *
             * Die Naht laesst sich nicht schoenrechnen, aber abschaffen: die
             * Vorflaeche beginnt jetzt am INNEREN Ende der Zufahrt, also an
             * der Fahrgasse, und laeuft in EINEM Stueck bis an die Strasse.
             * Zwischen Fahrgasse und Strasse gibt es damit keine Kante mehr,
             * an der zwei Flaechen aneinanderstossen.
             *
             * Der Belag des Parkplatzes liegt darunter weiter - das stoert
             * nicht, es ist derselbe Werkstoff. Ihn herauszuschneiden hiesse,
             * in das Zellenraster zu schneiden, und dort haben Schnitte in
             * diesem Projekt noch nie etwas Gutes bewirkt.
             */
            /*
             * NUR SO WEIT NACH INNEN WIE NOETIG.
             *
             * Der erste Anlauf lief bis ans INNERE Ende der Zufahrt, also bis
             * an die Randstrasse - und lappte sichtbar ueber deren Flaeche.
             * Der Nutzer hat das zu Recht beanstandet: das war Bequemlichkeit,
             * das innere Ende lag halt schon vor. Gebraucht wird es nicht.
             *
             * Zu ueberbruecken ist genau EINE Stelle: die Polygonkante. Dort
             * wechselt der Untergrund von Parkplatz auf Gehweg, und dort war
             * die Naht sichtbar. Wie weit man dafuer greifen muss, sagt das
             * Prefab selbst: sein `Hoehenversatz` steht im Log auf 1,50 m und
             * ist definiert als das Doppelte des Mindestknotenabstands (also
             * 0,75 m). Der weiche Rand einer Flaeche misst davon hoechstens
             * die Haelfte, rund 0,37 m. 1,5 m sind das Vierfache davon - die
             * Naht ist sicher ueberdeckt, und die Randstrasse bleibt frei.
             */
            var innenreserve = math.min(VorflaecheInnenreserve,
                math.distance(aussen, innen));
            var start = aussen - richtung * innenreserve;
            /*
             * DIE BREITE WIRD GENOMMEN, NICHT GERECHNET.
             *
             * Bis zum 2026-08-31 entstanden die beiden Startpunkte aus
             * `Entrance.Breite()`. Der Belag der Zufahrt entsteht aber im
             * Zellenmodell aus `EntranceQuad`. Zwei getrennt gerechnete
             * Breiten stimmen nie auf den Millimeter ueberein - und der
             * Unterschied stand als ABSATZ im Bild, genau dort, wo die
             * Vorflaeche beginnt. Der Nutzer hat ihn als "Einschneidung"
             * gemeldet.
             *
             * Also werden die zwei aeusseren Ecken des vorhandenen
             * Zufahrtsrechtecks uebernommen. Damit ist die Vorflaeche per
             * Konstruktion genau so breit wie der Belag darueber, und beim
             * Verschmelzen fallen ihre Innenpunkte mit vorhandenen Ringecken
             * zusammen statt neue danebenzusetzen.
             */
            var uebernommen = TryAeussereEcken(zufahrtsrechtecke, start,
                richtung, quer, breite, out var links, out var rechts);
            if (!uebernommen)
            {
                links = start + quer * (breite * 0.5f);
                rechts = start - quer * (breite * 0.5f);
            }

            // Regel 2: BEIDE Seiten treffen dieselbe Strassenlinie. Der
            // Laengenunterschied ist das Ergebnis, nicht der Zweck.
            var ziel = aufmass.Asphaltpunkt;
            if (!TrifftLinie(links, richtung, ziel, aufmass.Randrichtung, out var tLinks)
                || !TrifftLinie(rechts, richtung, ziel, aufmass.Randrichtung,
                    out var tRechts))
            {
                meldung = $"{Art(segment.Art)}: Zufahrt laeuft parallel zur Strasse.";
                return null;
            }

            // Der Teil INNERHALB des Polygons zaehlt nicht als Laenge - sonst
            // waere die Mindestlaenge allein durch den Vorlauf erfuellt, und
            // die Plausibilitaetsgrenze schlueg bei langen Zufahrten an.
            var echteLaenge = math.max(tLinks, tRechts) - innenreserve;
            if (echteLaenge < VorflaecheMinLaenge)
            {
                /*
                 * Bis zum 2026-08-31 kam hier ein stilles `return null`. Wenn
                 * der Nutzer meldet "manchmal findet keine Erweiterung
                 * statt", war das der einzige Ausgang, der dazu nichts sagte.
                 */
                meldung = $"{Art(segment.Art)}: Zufahrt endet schon am "
                    + "Asphalt - nichts zu belegen ("
                    + echteLaenge.ToString("F2", CultureInfo.InvariantCulture)
                    + " m, " + seitenwahl + ").";
                return null;
            }
            if (echteLaenge > VorflaecheMaxLaenge)
            {
                meldung = $"{Art(segment.Art)}: Strasse waere "
                    + $"{echteLaenge.ToString("F1", CultureInfo.InvariantCulture)} m "
                    + "entfernt - zu weit, keine Vorflaeche.";
                return null;
            }

            /*
             * Keine Seite darf auf null zusammenfallen.
             *
             * Zwei Gruende. Erstens faltet sich der Ring, wenn eine Seite
             * nach hinten laeuft - das kommt vor, wenn die Zufahrt sehr spitz
             * auf die Strasse trifft. Zweitens entstuende bei genau 0 ein
             * DOPPELPUNKT, und ein Ring mit zwei gleichen Ecken ist fuer den
             * Flaechenbau von CS2 kein gueltiges Polygon mehr.
             */
            // Die gezeichnete Kante liegt um die Aufweitung weiter draussen
            // als die gerechnete. Also hier zuruecknehmen.
            tLinks -= aufweitung;
            tRechts -= aufweitung;
            tLinks = math.max(tLinks, VorflaecheMinSeite);
            tRechts = math.max(tRechts, VorflaecheMinSeite);

            meldung = $"{Art(segment.Art)}: {seitenwahl}, Breite "
                + (uebernommen ? "aus dem Zufahrtsrechteck" : "GERECHNET (kein Rechteck gefunden)")
                + $" {math.distance(links, rechts).ToString("F2", CultureInfo.InvariantCulture)} m, "
                + $"Aufweitung {aufweitung.ToString("F2", CultureInfo.InvariantCulture)} m abgezogen, "
                + $"{aufmass.Prefab}, "
                + $"Fussgaengerweg {(aufmass.HatFussgaengerweg ? "ja" : "nein")}, "
                + $"Randbreite {aufmass.Randbreite.ToString("F2", CultureInfo.InvariantCulture)} m, "
                + $"Gesamtbreite {aufmass.Gesamtbreite.ToString("F2", CultureInfo.InvariantCulture)} m, "
                + $"Vorflaeche {tLinks.ToString("F2", CultureInfo.InvariantCulture)}/"
                + $"{tRechts.ToString("F2", CultureInfo.InvariantCulture)} m "
                + $"({aufmass.Notiz})";

            // Dieselbe Ausrichtung wie alle anderen Materialflaechen:
            // `MaterialSurfaces` normiert dort auf positiven Umlaufsinn.
            var ring = new[]
            {
                links,
                links + richtung * tLinks,
                rechts + richtung * tRechts,
                rechts,
            };

            // Fuer die Vorschau derselbe Streifen, aber erst ab der
            // Polygonkante - innen zeichnet das Overlay schon die Zufahrt.
            var sichtLinks = links + richtung * innenreserve;
            var sichtRechts = rechts + richtung * innenreserve;
            var sicht = new[]
            {
                sichtLinks,
                links + richtung * tLinks,
                rechts + richtung * tRechts,
                sichtRechts,
            };
            aussenteil = Flaeche(sicht) >= 0 ? sicht : sicht.Reverse().ToArray();
            /*
             * Die vier Punkte einzeln herausgeben, nicht aus dem Ring raten.
             * Nach der Umlaufkorrektur unten stimmt keine feste Indexlage
             * mehr - und die Verschmelzung braucht genau diese Zuordnung:
             * innen liegt auf der Polygonkante, aussen an der Strasse.
             */
            innenkante = new[]
            {
                links,
                rechts,
                links + richtung * tLinks,
                rechts + richtung * tRechts,
            };
            return Flaeche(ring) >= 0 ? ring : ring.Reverse().ToArray();
        }

        /**
         * Die zwei aeusseren Ecken des Zufahrtsrechtecks, das zu dieser
         * Zufahrt gehoert.
         *
         * Gesucht wird ueber die Lage, nicht ueber einen Index: die Listen
         * `NetLine` und `EntranceQuad` entstehen an verschiedenen Stellen,
         * eine gemeinsame Reihenfolge ist nirgends zugesichert. Genommen wird
         * das Rechteck, dessen Mittelpunkt der Zufahrtsachse am naechsten
         * liegt; "aussen" sind darin die beiden Ecken mit der groessten
         * Projektion auf die Fahrtrichtung.
         *
         * Die Breitenprobe ist die Schranke gegen einen Fehlgriff: weicht die
         * gefundene Breite stark von der erwarteten ab, war es das falsche
         * Rechteck, und der Aufrufer rechnet wie bisher.
         */
        private static bool TryAeussereEcken(float2[][] rechtecke,
            float2 start, float2 richtung, float2 quer, float breite,
            out float2 links, out float2 rechts)
        {
            links = default;
            rechts = default;
            if (rechtecke == null || rechtecke.Length == 0) return false;

            var bestes = -1;
            var besterAbstand = float.PositiveInfinity;
            for (var i = 0; i < rechtecke.Length; i++)
            {
                var quad = rechtecke[i];
                if (quad == null || quad.Length < 4) continue;
                var mitte = float2.zero;
                for (var k = 0; k < quad.Length; k++) mitte += quad[k];
                mitte /= quad.Length;
                // Abstand des Rechteckmittelpunkts zur Zufahrtsachse.
                var abstand = math.abs(math.dot(mitte - start, quer))
                    + math.abs(math.min(0f, math.dot(mitte - start, richtung)));
                if (abstand >= besterAbstand) continue;
                besterAbstand = abstand;
                bestes = i;
            }

            if (bestes < 0) return false;
            var gewaehlt = rechtecke[bestes];

            // Die zwei Ecken, die am weitesten in Fahrtrichtung liegen.
            var ersteEcke = -1;
            var zweiteEcke = -1;
            var ersteProjektion = float.NegativeInfinity;
            var zweiteProjektion = float.NegativeInfinity;
            for (var k = 0; k < gewaehlt.Length; k++)
            {
                var projektion = math.dot(gewaehlt[k] - start, richtung);
                if (projektion > ersteProjektion)
                {
                    zweiteProjektion = ersteProjektion;
                    zweiteEcke = ersteEcke;
                    ersteProjektion = projektion;
                    ersteEcke = k;
                }
                else if (projektion > zweiteProjektion)
                {
                    zweiteProjektion = projektion;
                    zweiteEcke = k;
                }
            }

            if (ersteEcke < 0 || zweiteEcke < 0) return false;
            var a = gewaehlt[ersteEcke];
            var b = gewaehlt[zweiteEcke];
            var gefunden = math.distance(a, b);
            if (gefunden < breite * 0.5f || gefunden > breite * 2f)
                return false;

            // `links` liegt auf der +quer-Seite, wie beim gerechneten Weg.
            if (math.dot(a - b, quer) >= 0f) { links = a; rechts = b; }
            else { links = b; rechts = a; }
            return true;
        }

        private static string Weg(float wert)
            => float.IsInfinity(wert)
                ? "keine"
                : wert.ToString("F1", CultureInfo.InvariantCulture) + " m";

        private static float Flaeche(float2[] ring)
        {
            var summe = 0f;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                summe += a.x * b.y - b.x * a.y;
            }
            return summe * 0.5f;
        }

        private static string Art(Zufahrtsart art)
        {
            switch (art)
            {
                case Zufahrtsart.Einfahrt: return "Einfahrt";
                case Zufahrtsart.Ausfahrt: return "Ausfahrt";
                case Zufahrtsart.Fussweg: return "Fussweg";
                default: return "Zufahrt";
            }
        }

        /**
         * Schnittpunkt des Strahls `von + t*richtung` mit der Geraden durch
         * `linienpunkt` in `linienrichtung`.
         */
        private static bool TrifftLinie(float2 von, float2 richtung,
            float2 linienpunkt, float2 linienrichtung, out float t)
        {
            t = 0f;
            var nenner = richtung.x * linienrichtung.y - richtung.y * linienrichtung.x;
            if (math.abs(nenner) < 1e-4f) return false;
            var hin = linienpunkt - von;
            t = (hin.x * linienrichtung.y - hin.y * linienrichtung.x) / nenner;
            return true;
        }


        /**
         * Eine Zeile je Bauzustand, nicht je Vorschaulauf.
         *
         * Die Vorschau rechnet bei jeder Mausbewegung neu. Ohne diese
         * Sperre stuende dieselbe Messung hundertfach im Log und die
         * wirklich interessante Aenderung ginge darin unter.
         */
        private void MeldeVorflaechen(int anzahl, List<string> meldungen)
        {
            var text = anzahl + " | " + string.Join(" | ", meldungen);
            if (text == _letzteVorflaechenmeldung) return;
            _letzteVorflaechenmeldung = text;
            if (anzahl == 0 && meldungen.Count == 0) return;
            Mod.log.Info($"PLT-Vorflaeche: {anzahl} angelegt."
                + (meldungen.Count > 0 ? " " + string.Join(" | ", meldungen) : ""));
        }
    }
}
