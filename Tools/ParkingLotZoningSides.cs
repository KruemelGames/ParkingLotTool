using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * SCHALTET DAS ZONING JE STRASSENSEITE AN ODER AUS.
     *
     * Die Zoning-Strasse laeuft als Ring um die gezogenen Parzellen. CS2
     * legt an JEDER Seite jeder Kante einen Zonenblock an - auch nach
     * aussen, in den Parkplatz hinein. Der Nutzer hat das gemessen:
     *
     *     4 Kanten -> 8 Bloecke, 178 gueltige Zellen
     *     8x6 gueltig 8x3 | 7x6 gueltig 7x4    <- die Aussenseiten
     *
     * Bei einer gezogenen 6x6-Flaeche sind das 36 gewollte Zellen und der
     * Rest ungewollt. Genau dafuer hat er im Panel innen / aussen / beides
     * verlangt.
     *
     * DER MECHANISMUS ist `CompositionFlags.Side.ZonesDisabled` in
     * `Game.Net.Upgraded`, wie das Vanilla-Zonenwerkzeug es benutzt.
     * Seit 2026-09-25 haengt es an der BAUDEFINITION des Zoningkurses
     * (`EntscheideZoningseiten`), nicht mehr nachtraeglich an der fertigen
     * Kante - siehe dort, warum.
     *
     * WELCHE SEITE INNEN IST, wird nicht aus einer angenommenen
     * Ringkonvention abgeleitet, sondern gerechnet: das Kreuzprodukt aus
     * der Kantenrichtung und dem Vektor zur Mitte der Zoning-Flaeche. Eine
     * von CS2 umgedrehte Kante vertauscht sonst still links und rechts, und
     * der Fehler faellt erst im Spiel auf.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Traegt die gemerkten Handschaltungen auf eine gebaute Kante auf.
         *
         * Gespiegelt zu `WendeZoningSeitenplanAn` in der Vorschau - dort
         * heissen die Flaggen "an", hier "aus". Die gemerkte Kante kann
         * andersherum gespeichert sein als die gebaute; dann ist ihr "links"
         * unser "rechts".
         */
        private void WendeHandschaltungAn(
            float2 a, float2 b, ref bool linksAus, ref bool rechtsAus)
        {
            var plan = _zoningSeitenHandschaltungen;
            if (plan == null) return;
            for (var i = 0; i < plan.Length; i++)
            {
                var eintrag = plan[i];
                if (!ZoningSelbeKante(eintrag.A, eintrag.B, a, b)) continue;
                var gedreht = math.distance(eintrag.A, b) < 0.5f
                    && math.distance(eintrag.B, a) < 0.5f;
                var links = gedreht ? !eintrag.Links : eintrag.Links;
                if (links) linksAus = eintrag.Aus;
                else rechtsAus = eintrag.Aus;
            }
        }

        /**
         * WELCHE SEITEN EINER ZONINGKANTE BLEIBEN OHNE ZONING?
         *
         * Fuer die gerichtete Strecke `a -> d`. Seit 2026-09-25 wird das VOR
         * dem Bau entschieden und als `Upgraded` an die Baudefinition des
         * Zoningkurses gehaengt (`ParkingLotNetBuilder`), so wie CS2s eigenes
         * Werkzeug es tut - `GenerateEdgesSystem` legt die Kante damit an, und
         * `CourseSplitSystem` gibt es an jedes Teilstueck weiter.
         *
         * WARUM NICHT MEHR NACHTRAEGLICH: hier stand `SetzeSeitenflaggen`, das
         * `Upgraded` direkt in die fertige Kante schrieb und sie `Updated`
         * markierte - am CS2-Upgradeweg (Temp + Apply, Knoten und Verweise
         * mitgefuehrt) vorbei. Beim Bearbeiten eines Parkplatzes mit zwei
         * Zoningstrassenzuegen stuerzte CS2 danach sechsmal nativ ab, jedes
         * Mal im selben Bild; mit und ohne TownRoadLane, mit und ohne neue
         * Leitungen.
         *
         * `false`: keine Entscheidung moeglich, die Kante zont beidseitig.
         */
        private bool EntscheideZoningseiten(float2 a, float2 d, bool melden,
            out bool linksAus, out bool rechtsAus, out bool rand)
        {
            linksAus = false;
            rechtsAus = false;
            rand = false;
            var flaechen = _zoningSeitenFlaechen
                ?? System.Array.Empty<ParkingGeometry.Zoningflaeche>();
            var randzoning = _zoningSeitenRandzoning
                ?? System.Array.Empty<ParkingGeometry.RandzoningLinie>();
            if (flaechen.Length == 0 && randzoning.Length == 0) return false;
            var seite = _zoningSeitenWahl;
            var mitte = (a + d) * 0.5f;
            var richtung = d - a;
            if (math.lengthsq(richtung) < 1e-6f) return false;

            /*
             * RANDZONING ZONT NUR NACH AUSSEN.
             *
             * Ansage des Nutzers: *"Nur nach aussen, denn der User kann
             * innen ZF nutzen."* Eine Strasse im Randzoning-Abschnitt
             * bekommt deshalb IMMER die Innenseite aus - unabhaengig von
             * der Panelwahl, die nur fuer die inneren Flaechen gilt.
             *
             * "Innen" heisst hier: zur Mitte des Parkplatzes. Das ist
             * dieselbe Rechnung wie unten, nur mit einem anderen
             * Bezugspunkt - die Polygonmitte statt der Flaechenmitte.
             */
            /*
             * ERKANNT WIRD AM PLAN, NICHT AN DER LAGE.
             *
             * `RandzoningEnthaelt` verlangt, dass der Kurs PARALLEL zur
             * gewaehlten Kante liegt (auf ein Grad genau) und hoechstens
             * 15 m von ihr entfernt. Solange die RZ-Strasse eigens
             * erzeugt wurde, traf das immer zu - sie lief 10,4 m parallel
             * zur Kante.
             *
             * Seit die naechstliegende Fahrgasse die RZ-Strasse ist,
             * trifft es nicht mehr zu: an einer schraegen Kante steht sie
             * rund 34 Grad zu ihr. Der Kurs fiel durch, galt nicht als
             * Randzoning, und statt "Innenseite immer aus" entschied der
             * Zweig fuer die inneren Zoningflaechen. Ohne eine solche
             * Flaeche kam die Seite willkuerlich heraus. Der Nutzer:
             * *"Auffaellig, dass die Tiles nach innen gehen statt
             * aussen."*
             *
             * `_zoningSeitenRandachsen` sind die Achsen aus dem Bauplan
             * (`ParkingLayout.RandzoningRoad`), gemerkt wie die Lotmitte.
             * Der Lagetest bleibt als Rueckfall stehen: er ist fuer den
             * Fall mit Randstrassen weiterhin richtig, und ein Kurs ohne
             * Zuordnung waere schlimmer als eine Naeherung.
             */
            if (randzoning.Length > 0
                && (IstGeplanteRandzoningachse(a, d)
                    || ParkingGeometry.RandzoningEnthaelt(randzoning, a, d)))
            {
                /*
                 * WO INNEN IST, SAGT DER PLAN.
                 *
                 * Hier entschied die Polygonmitte: *"liegt der Schwerpunkt
                 * links oder rechts der Fahrtrichtung?"* Fuer EINE Strasse
                 * entlang einer Kante ist das richtig. Fuer eine TREPPE
                 * nicht - eine weit innen liegende Stufe hat den
                 * Schwerpunkt auf ihrer anderen Seite und zonte dann in
                 * den Parkplatz hinein. Der Nutzer: *"Nicht alle Tiles
                 * gehen nach aussen, da findet keine ordentliche Pruefung
                 * statt."*
                 *
                 * Die gemerkte Mitte bleibt als Rueckfall - fuer den Weg
                 * mit Randstrassen, wo es keine Abschnitte gibt, und fuer
                 * Parkplaetze aus aelteren Spielstaenden ohne gemerkte
                 * Richtung.
                 */
                /*
                 * EINE REGEL: WAS IM UMRISS LIEGT, IST INNEN.
                 *
                 * Hier standen drei Regeln uebereinander - die geplante
                 * Achse, der Umriss, der Schwerpunkt -, und bei jeder Form
                 * griff eine andere. Genau darin hat sich der Fehler
                 * versteckt: eine Form nahm den Plan und lag richtig, die
                 * naechste fiel auf den Schwerpunkt zurueck und lag falsch,
                 * und von aussen sah beides gleich aus.
                 *
                 * Der Nutzer am 2026-09-16: *"Ich glaube du gehst das
                 * ganze zu schwierig an. Es ist doch offensichtlich was
                 * aussen und was innen ist."* Er hat recht. Der Nutzer
                 * zeichnet ein Polygon; das IST der Parkplatz. Was darin
                 * liegt, ist innen. Mehr braucht die Frage nicht, und jede
                 * zusaetzliche Regel ist nur eine weitere Stelle, an der
                 * es schiefgehen kann.
                 */
                var innenrichtung = InnenAusUmriss(mitte, richtung);
                if (math.lengthsq(innenrichtung) < 1e-6f)
                {
                    /*
                     * Beide Seiten im Umriss oder beide draussen - dann
                     * ist es keine Randkante, sondern eine, die quer
                     * hindurchlaeuft. Lieber melden als raten.
                     */
                    if (melden) Mod.log.Warn("PLT-Zoningseiten: Randzoning-Kante bei "
                        + Ort(mitte) + " - der Umriss gibt keine Innenseite "
                        + "her (beide Seiten gleich). Sie bleibt, wie CS2 "
                        + "sie angelegt hat.");
                    return false;
                }
                var innenLinks =
                    richtung.x * innenrichtung.y
                    - richtung.y * innenrichtung.x > 0f;
                if (melden)
                    MeldeKante("Randzoning", mitte, richtung, innenrichtung,
                        innenLinks, innenLinks, !innenLinks);
                linksAus = innenLinks;
                rechtsAus = !innenLinks;
                rand = true;
                return true;
            }

            // Die naechstgelegene Zoning-Flaeche liefert den Innenpunkt.
            var innenpunkt = NaechsteZoningmitte(flaechen, mitte);
            var zurMitte = innenpunkt - mitte;

            /*
             * Kreuzprodukt in der XZ-Ebene. Positiv heisst: der
             * Innenpunkt liegt LINKS der gerichteten Kante.
             */
            var kreuz = richtung.x * zurMitte.y - richtung.y * zurMitte.x;
            if (math.abs(kreuz) < 1e-4f)
            {
                /*
                 * KEIN STILLER AUSSTIEG MEHR.
                 *
                 * Liegt der Bezugspunkt fast auf der Geraden der Kante,
                 * sagt das Kreuzprodukt nichts - und bisher blieb die
                 * Kante dann unangetastet. Sie behaelt damit CS2s
                 * Vorgabe: Zoning auf BEIDEN Seiten. Von aussen sieht
                 * das aus, als zone sie nach innen.
                 */
                if (melden) Mod.log.Warn("PLT-Zoningseiten: Kante bei "
                    + Ort(mitte) + " UNENTSCHIEDEN - der Bezugspunkt "
                    + Ort(innenpunkt) + " liegt auf ihrer Geraden "
                    + "(Kreuzprodukt " + kreuz.ToString("F6")
                    + "). Sie bleibt, wie CS2 sie angelegt hat, also auf "
                    + "beiden Seiten zonend.");
                return false;
            }
            var innenIstLinks = kreuz > 0f;

            /*
             * JE KANTE, NICHT JE PARKPLATZ.
             *
             * Hier entschied bis zum 2026-09-21 die Panelwahl
             * innen/aussen/beides fuer ALLE Kanten gleich. Seit der
             * Nutzer die Tiefe je Seite einstellt, taugt das nicht mehr:
             * haette EINE Seite ein Band, zonten alle vier nach aussen -
             * und dort, wo kein Band ist, haelt der Parkplatz keinen
             * Platz frei. Die Kacheln laegen auf Buchten. Genau das
             * wollte er nicht: *"damit nicht die Tiles auf Strassen von
             * uns liegen."*
             *
             * Also fragt jede Kante ihre eigene Seite: hat sie ein Band,
             * zont sie nach beiden Seiten, sonst nur nach innen.
             */
            var aussenkacheln =
                ParkingGeometry.ZoningAussenkachelnBei(flaechen, mitte);
            linksAus = aussenkacheln > 0 ? false : !innenIstLinks;
            rechtsAus = aussenkacheln > 0 ? false : innenIstLinks;

            /*
             * ZULETZT DIE HANDSCHALTUNG - sie ist die Abweichung von der
             * Panelwahl und muss deshalb obenauf liegen.
             *
             * Dieselbe Reihenfolge wie in der Vorschau
             * (`ZoningStrassenMitSeiten`). Randzoning-Kanten sind oben
             * schon mit `continue` heraus: dort gibt es keine Wahl, nur
             * aussen.
             */
            WendeHandschaltungAn(a, d, ref linksAus, ref rechtsAus);

            if (melden)
                MeldeKante("Panelwahl " + seite, mitte, richtung,
                    zurMitte, innenIstLinks, linksAus, rechtsAus);
            return true;
        }

        /**
         * NACH DEM BAU NUR NOCH PRUEFEN, NIE SCHREIBEN.
         *
         * Liest an jeder gebauten Zoningkante, ob `Upgraded` so gekommen ist,
         * wie `EntscheideZoningseiten` es vor dem Bau festgelegt hat. Eine
         * Abweichung wird gemeldet, nicht repariert - eine Reparatur waere
         * wieder das Direktschreiben, das den Absturz ausgeloest hat.
         */
        private int PruefeZoningSeiten(Unity.Entities.Entity traeger)
        {
            if (traeger == Unity.Entities.Entity.Null
                || !EntityManager.Exists(traeger)) return 0;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return 0;
            var zoningPrefabs = SammleZoningPrefabs();
            if (zoningPrefabs.Count == 0) return 0;
            var gesehen = 0;
            var stimmt = 0;
            var abweichungen = new List<string>();
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (!EntityManager.Exists(kante)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(kante)) continue;
                if (!zoningPrefabs.Contains(
                        EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab))
                    continue;
                if (!EntityManager.HasComponent<Curve>(kante)) continue;
                gesehen++;
                var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
                var a = new float2(kurve.a.x, kurve.a.z);
                var d = new float2(kurve.d.x, kurve.d.z);
                if (!EntscheideZoningseiten(a, d, false, out var linksAus,
                        out var rechtsAus, out _))
                {
                    stimmt++;
                    continue;
                }
                var istLinks = LiestSeite(kante, true);
                var istRechts = LiestSeite(kante, false);
                if (istLinks == linksAus && istRechts == rechtsAus)
                {
                    stimmt++;
                    continue;
                }
                if (abweichungen.Count < 6)
                    abweichungen.Add(Ort((a + d) * 0.5f) + " soll aus "
                        + (linksAus ? "links" : "-") + "/" + (rechtsAus ? "rechts" : "-")
                        + ", ist aus " + (istLinks ? "links" : "-") + "/"
                        + (istRechts ? "rechts" : "-"));
            }
            if (abweichungen.Count == 0)
                Mod.log.Info($"PLT-Zoningseiten: {stimmt} von {gesehen} Kante(n) "
                    + "wie geplant - beim Bau gesetzt, nachtraeglich nichts geschrieben.");
            else
                Mod.log.Warn($"PLT-Zoningseiten: {gesehen - stimmt} von {gesehen} "
                    + "Kante(n) weichen vom Plan ab (NICHT nachgeschrieben): "
                    + string.Join("; ", abweichungen));
            return gesehen - stimmt;
        }

        /**
         * NIMMT DER ZONING-STRASSE IHREN NAMEN.
         *
         * CS2 vergibt fuer Strassen Namen wie "Gasse 12" und blendet sie im
         * Spiel ein. Unsere Zoning-Strasse ist unsichtbar; ein Schild
         * mitten im Parkplatz waere Unsinn.
         *
         * Der Trick stammt vom Nutzer: *"Wenn die Strassen '<nbsp>' heissen,
         * wird dieser Name ausgeschaltet."* Ein geschuetztes Leerzeichen
         * (U+00A0) ist ein gueltiger Name, aber unsichtbar.
         *
         * DER NAME HAENGT AM AGGREGAT, nicht an der Kante: CS2 fasst
         * zusammenhaengende Kanten zu einem Strassenzug zusammen und
         * beschriftet diesen. Deshalb wird jedes Aggregat nur einmal
         * angefasst.
         */
        /**
         * Ein Zeichen ohne Breite, U+200B - NICHT das geschuetzte
         * Leerzeichen.
         *
         * HIER STAND U+00A0, UND DAMIT HAT ES NIE FUNKTIONIERT.
         *
         * `Game.UI.NameSystem.SetCustomName` (Zeile 207) faengt den Fall ab:
         *
         *     if (string.IsNullOrWhiteSpace(name))
         *     {   // Namen LOESCHEN und Eintrag entfernen
         *         entityCommandBuffer.RemoveComponent<CustomName>(entity);
         *         m_Names.Remove(entity);
         *     }
         *     else { m_Names[entity] = name; ... }
         *
         * Ein geschuetztes Leerzeichen IST fuer .NET ein Leerzeichen -
         * `char.IsWhiteSpace('\u00A0')` ist wahr. CS2 nahm also jedes Mal
         * den ersten Zweig und hat den Namen GELOESCHT statt ihn zu setzen.
         * Danach vergibt das Spiel wieder seinen eigenen, "Gasse 12".
         *
         * U+200B ist dagegen ein Formatzeichen, kein Leerzeichen:
         * `IsNullOrWhiteSpace` ist damit falsch, der Name wird gespeichert -
         * und er hat keine Breite, ist also genauso unsichtbar.
         *
         * ALS ZEICHENCODE, NICHT ALS LITERAL: direkt in die Quelle
         * geschrieben waere es unsichtbar, und niemand koennte spaeter
         * erkennen, warum dort scheinbar ein leerer Text steht.
         */
        internal static readonly string Unsichtbarername
            = ((char)0x200B).ToString();

        /*
         * HIER STAND `_zoningBenannteAggregate`.
         *
         * Eine Liste "die habe ich schon angefasst". Sie war die Grundlage
         * fuer die Erfolgsmeldung der Wache - und damit eine Selbstauskunft:
         * angefasst heisst nicht angenommen. Gefragt wird jetzt CS2 selbst,
         * ueber `CustomName`. Siehe BenenneZoningstrassen.
         */

        /**
         * Rueckgabe: wie viele Strassenzuege in DIESEM Durchgang benannt
         * werden mussten. Null heisst, dass alle schon unseren Namen tragen
         * - erst dann ist die Sache stabil.
         */
        private int BenenneZoningstrassen(Unity.Entities.Entity traeger)
        {
            if (traeger == Unity.Entities.Entity.Null
                || !EntityManager.Exists(traeger))
            {
                Mod.log.Warn("PLT-Zoningstrasse: kein Traeger zum Benennen.");
                return 0;
            }
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger))
            {
                Mod.log.Warn("PLT-Zoningstrasse: Traeger ohne SubNet-Puffer.");
                return 0;
            }

            var zoningPrefabs = SammleZoningPrefabs();
            if (zoningPrefabs.Count == 0)
            {
                Mod.log.Warn("PLT-Zoningstrasse: kein Zoning-Prefab gefunden, "
                    + "es wird nichts benannt.");
                return 0;
            }

            // Gesetzt wird anderswo (siehe unten), aber wenn es das System
            // gar nicht gibt, ist auch dort nichts zu holen - dann lieber
            // hier melden als spaeter still scheitern.
            if (World.GetOrCreateSystemManaged<Game.UI.NameSystem>() == null)
            {
                Mod.log.Warn("PLT-Zoningstrasse: kein NameSystem.");
                return 0;
            }

            var erledigt = new System.Collections.Generic.HashSet<
                Unity.Entities.Entity>();
            var benannt = 0;
            var gesehen = 0;
            var ohneAggregat = 0;
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (!EntityManager.Exists(kante)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(kante)) continue;
                if (!zoningPrefabs.Contains(
                        EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab))
                    continue;
                if (!EntityManager.HasComponent<Aggregated>(kante))
                {
                    ohneAggregat++;
                    continue;
                }

                var aggregat = EntityManager
                    .GetComponentData<Aggregated>(kante).m_Aggregate;
                if (aggregat == Unity.Entities.Entity.Null
                    || !EntityManager.Exists(aggregat)) continue;
                if (!erledigt.Add(aggregat)) continue;
                gesehen++;

                /*
                 * DIE RUECKFRAGE STATT DER EIGENEN LISTE.
                 *
                 * Vorher wurde hier `_zoningBenannteAggregate` gefragt: "habe
                 * ich den schon mal angefasst?" - und wenn ja, uebersprungen.
                 * Damit meldete die Wache Erfolg, sobald sie jeden
                 * Strassenzug EINMAL angefasst hatte, voellig unabhaengig
                 * davon, ob der Name auch ankam. Er kam nie an.
                 *
                 * `CustomName` ist die ehrliche Antwort: CS2 heftet die
                 * Komponente in `SetCustomName` NUR im else-Zweig an, also
                 * nur, wenn der Name angenommen wurde. Wir heften sie
                 * deshalb auch nicht mehr selbst an - genau das hat den
                 * Fehlschlag verdeckt.
                 *
                 * Sie erscheint einen Frame spaeter (EndFrameBarrier); die
                 * Wache laeuft alle 30 Frames, das reicht reichlich.
                 */
                if (EntityManager.HasComponent<Game.UI.CustomName>(aggregat))
                    continue;

                /*
                 * NUR ABLEGEN, NICHT SETZEN.
                 *
                 * `SetCustomName` braucht die `EndFrameBarrier`, und deren
                 * Fenster ist aus dem Werkzeug heraus immer zu - `ToolSystem`
                 * laeuft vor `AllowBarrier<EndFrameBarrier>`. Gesetzt wird in
                 * `ParkingLotStrassennameSystem` (Phase `UIUpdate`), im
                 * selben Frame ein paar Systeme spaeter. Dort steht auch die
                 * vollstaendige Begruendung.
                 */
                ParkingLotStrassennameSystem.Merke(aggregat);
                benannt++;
            }

            /*
             * KEINE KANTE HAT EIN AGGREGAT - genau diesen Fall verschluckte
             * die alte Fassung stumm. Der Name haengt am Strassenzug; gibt
             * es keinen, gibt es auch nichts zu benennen, und daran aendert
             * kein zweiter Versuch etwas. Das muss im Log stehen, sonst
             * sucht man an der falschen Stelle.
             */
            if (gesehen == 0 && ohneAggregat > 0)
                Mod.log.Warn($"PLT-Zoningstrasse: {ohneAggregat} Kante(n) ohne "
                    + "Aggregat - CS2 hat den Strassenzug noch nicht gebildet.");
            if (benannt > 0)
                Mod.log.Info($"PLT-Zoningstrasse: {benannt} von {gesehen} "
                    + "Strassenzug/Strassenzuegen zum Umbenennen abgelegt; "
                    + "gesetzt wird in UIUpdate.");
            return benannt;
        }

        /** Mitte der Zoning-Flaeche, die diesem Punkt am naechsten liegt. */
        private static float2 NaechsteZoningmitte(
            ParkingGeometry.Zoningflaeche[] flaechen, float2 punkt)
        {
            var beste = float2.zero;
            var bester = float.PositiveInfinity;
            for (var i = 0; i < flaechen.Length; i++)
            {
                if (flaechen[i] == null) continue;
                var mitte = ParkingGeometry.ZoningMitte(flaechen[i]);
                var abstand = math.distancesq(mitte, punkt);
                if (abstand >= bester) continue;
                bester = abstand;
                beste = mitte;
            }
            return beste;
        }

        /**
         * LIEGT DIESER KURS AUF EINER GEPLANTEN RANDZONING-ACHSE?
         *
         * Verglichen wird gegen die Achsen aus dem Bauplan. CS2 kann eine
         * Kante beim Bauen weiter teilen, deshalb genuegt es NICHT, die
         * Endpunkte zu vergleichen: gefragt ist, ob das Stueck AUF der Achse
         * liegt. Ein halber Meter quer faengt die float-Rechnung des Bauwegs
         * ab, ohne eine danebenliegende Gasse mitzunehmen - die naechste
         * liegt einen Modulabstand weit weg.
         */
        private bool IstGeplanteRandzoningachse(float2 a, float2 b)
            => math.lengthsq(GeplanteInnenrichtung(a, b)) > 1e-6f
            || GeplanteAchse(a, b) >= 0;

        /**
         * EINE ZEILE JE ZONING-KANTE - Ort, Regel, Bezug, Ergebnis.
         *
         * Die Summenzeile am Ende sagt, wieviele Kanten gesetzt wurden. Sie
         * sagt nicht, WELCHE falsch liegt. Genau das fehlte, als der Nutzer
         * am 2026-09-16 meldete, manche Formen zonten weiter nach innen.
         *
         * Aufgeschrieben wird deshalb alles, was in die Entscheidung
         * eingeht: wo die Kante liegt, wohin sie zeigt, welche Regel gegriffen
         * hat, worauf sie sich bezogen hat, und was am Ende abgeschaltet
         * wurde. Mit diesen Zeilen laesst sich eine falsche Kante im Bild
         * wiederfinden, ohne raten zu muessen.
         */
        private static void MeldeKante(string regel, float2 mitte,
            float2 richtung, float2 bezug, bool innenLinks,
            bool linksAus, bool rechtsAus)
        {
            Mod.log.Info("PLT-Zoningseite " + Ort(mitte)
                + " Richtung " + Ort(math.normalizesafe(richtung))
                + " | Regel " + regel
                + " | innen ist " + (innenLinks ? "LINKS" : "RECHTS")
                + " (Bezug " + Ort(bezug) + ")"
                + " | aus: " + (linksAus ? "links" : "-")
                + "/" + (rechtsAus ? "rechts" : "-"));
        }

        private static string Ort(float2 p)
            => "(" + p.x.ToString("F1") + "/" + p.y.ToString("F1") + ")";

        /**
         * Welche Seite dieser Kante liegt IM Umriss des Parkplatzes?
         *
         * Nullvektor, wenn kein Umriss gemerkt ist oder beide Seiten dieselbe
         * Antwort geben - dann ist die Frage hier nicht zu beantworten und
         * der Aufrufer nimmt seinen naechsten Weg.
         */
        /**
         * WELCHE SEITE DIESER KANTE ZEIGT INS INNERE DES PARKPLATZES?
         *
         * ERSTER ANLAUF WAR ZU KURZ GEDACHT. Ich habe links und rechts der
         * Kante je einen Punkt gesetzt und gefragt, welcher im Umriss liegt.
         * Das beantwortet die Frage nur, wenn die Strasse AUF dem Rand
         * liegt. Seit die naechstliegende Fahrgasse die RZ-Strasse ist, liegt
         * sie aber tief im Parkplatz - dann liegen BEIDE Punkte drinnen, und
         * im Log stand folgerichtig *"der Umriss gibt keine Innenseite her
         * (beide Seiten gleich)"*.
         *
         * Die Frage muss also andersherum gestellt werden, und dann ist sie
         * tatsaechlich einfach: Randzoning waechst zur KANTE hin, fuer die es
         * gewaehlt wurde. Aussen ist die Richtung zum naechsten Punkt des
         * Umrisses, innen die Gegenrichtung. Das gilt gleichermassen fuer eine
         * Strasse auf dem Rand und fuer eine dreissig Meter weiter innen.
         *
         * Liegt die Kante praktisch auf dem Rand, ist diese Richtung nicht
         * bestimmbar - dann entscheidet doch die Seitenprobe, und dort ist sie
         * auch richtig.
         */
        private float2 InnenAusUmriss(float2 mitte, float2 richtung)
            => InnenAusUmriss(mitte, richtung, _zoningSeitenUmriss);

        /**
         * Dieselbe Regel mit einem uebergebenen Umriss.
         *
         * Zwei Aufrufer: das Bauen nimmt den gemerkten Umriss, die Vorschau
         * den aktuellen Punktzug. Die REGEL darf es deshalb nur einmal geben -
         * zwei Fassungen derselben Frage waren genau der Fehler, der am
         * 2026-09-16 dazu fuehrte, dass die berichtigte Regel beim Bauen
         * griff und die Vorschau auf der alten stehenblieb.
         */
        internal static float2 InnenAusUmriss(float2 mitte, float2 richtung,
            IReadOnlyList<float2> punkte)
        {
            var umriss = punkte as float2[] ?? punkte?.ToArray();
            if (umriss == null || umriss.Length < 3) return float2.zero;
            var laenge = math.length(richtung);
            if (laenge < 1e-6f) return float2.zero;
            var normale = new float2(-richtung.y, richtung.x) / laenge;

            var rand = NaechsterRandpunkt(umriss, mitte);
            var nachAussen = rand - mitte;
            var abstand = math.length(nachAussen);
            if (abstand > 0.5f)
            {
                // Nur der Anteil QUER zur Kante entscheidet ueber die Seite.
                var quer = math.dot(nachAussen / abstand, normale);
                if (math.abs(quer) > 0.2f)
                    return quer > 0f ? -normale : normale;
            }

            // Rueckfall: die Kante liegt auf dem Rand oder laeuft auf ihn zu.
            var schritt = (float)ParkingGeometry.ZoningStrassenbreite * 0.75f;
            var links = ImUmriss(umriss, mitte + normale * schritt);
            var rechts = ImUmriss(umriss, mitte - normale * schritt);
            if (links == rechts) return float2.zero;
            return links ? normale : -normale;
        }

        /** Der naechstgelegene Punkt auf dem Umriss - Ecken eingeschlossen. */
        private static float2 NaechsterRandpunkt(float2[] ring, float2 punkt)
        {
            var beste = ring[0];
            var bester = float.MaxValue;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                var d = b - a;
                var quadrat = math.lengthsq(d);
                var t = quadrat < 1e-9f ? 0f
                    : math.saturate(math.dot(punkt - a, d) / quadrat);
                var nah = a + d * t;
                var abstand = math.distancesq(punkt, nah);
                if (abstand >= bester) continue;
                bester = abstand;
                beste = nah;
            }
            return beste;
        }

        /** Strahlverfahren - ungerade Zahl von Kantenschnitten heisst innen. */
        private static bool ImUmriss(float2[] ring, float2 punkt)
        {
            var innen = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if (a.y > punkt.y != b.y > punkt.y
                    && punkt.x < (b.x - a.x) * (punkt.y - a.y) / (b.y - a.y) + a.x)
                    innen = !innen;
            }
            return innen;
        }

        /**
         * Die Innenrichtung der geplanten Achse, auf der dieser Kurs liegt -
         * oder der Nullvektor, wenn er auf keiner liegt.
         */
        private float2 GeplanteInnenrichtung(float2 a, float2 b)
        {
            var treffer = GeplanteAchse(a, b);
            return treffer < 0 ? float2.zero
                : _zoningSeitenRandachsen[treffer].Innen;
        }

        private int GeplanteAchse(float2 a, float2 b)
        {
            if (_zoningSeitenRandachsen == null) return -1;
            var mitte = (a + b) * 0.5f;
            for (var i = 0; i < _zoningSeitenRandachsen.Length; i++)
            {
                var achse = _zoningSeitenRandachsen[i];
                var spanne = achse.B - achse.A;
                var laenge = math.length(spanne);
                if (laenge < 1e-3f) continue;
                var richtung = spanne / laenge;
                var w = mitte - achse.A;
                var laengs = math.dot(w, richtung);
                if (laengs < -0.5f || laengs > laenge + 0.5f) continue;
                /*
                 * TOLERANZ QUER ZUR ACHSE.
                 *
                 * 0,5 m stammten aus der Zeit, als die RZ-Strasse eigens
                 * erzeugt wurde und exakt auf der geplanten Achse lag. Seit
                 * die naechstliegende Fahrgasse die RZ-Strasse ist, liegt sie
                 * daneben - die Achse wurde nicht mehr gefunden, und es griff
                 * der Rueckfall. Die halbe Zoningstrassenbreite ist der
                 * Abstand, in dem eine Kante noch zu derselben Achse gehoert.
                 */
                var quertoleranz =
                    (float)ParkingGeometry.ZoningStrassenbreite * 0.5f;
                if (math.abs(richtung.x * w.y - richtung.y * w.x)
                    <= quertoleranz)
                    return i;
            }
            return -1;
        }

    }
}
