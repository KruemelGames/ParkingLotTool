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
     * `Upgraded` wird gespeichert; `Composition` leitet CS2 nach dem Laden
     * daraus neu ab. Danach muss die Kante `Updated` bekommen, sonst baut
     * `BlockSystem` die Bloecke nicht neu.
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
         * Setzt die Seitenschalter an allen Zoning-Kanten des Traegers.
         * Rueckgabe: wieviele Kanten geaendert wurden.
         */
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

        private int SetzeZoningSeiten(Unity.Entities.Entity traeger)
        {
            if (traeger == Unity.Entities.Entity.Null
                || !EntityManager.Exists(traeger)) return 0;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return 0;

            /*
             * RANDZONING ALLEIN GENUEGT.
             *
             * Hier stieg die Seitenwahl aus, sobald es keine gezogene
             * Zoningflaeche gab - und beim reinen Randzoning gibt es keine.
             * Es wurde dann GAR KEINE Seite geschaltet, also zonte CS2 nach
             * beiden, und die Kacheln liefen in den Parkplatz hinein. Genau
             * das hat der Nutzer am 2026-09-04 gemeldet: *"Das Zoning der
             * Randstrasse geht gerade in den Parkplatz hinein statt
             * heraus."*
             *
             * Der Abbruch war richtig gemeint - eine Seitenwahl ohne Flaeche
             * WAR ein Fehler, solange es nur innere Flaechen gab. Seit es
             * Randzoning gibt, ist er es nicht mehr.
             */
            var flaechen = _zoningSeitenFlaechen
                ?? System.Array.Empty<ParkingGeometry.Zoningflaeche>();
            var randzoning = _zoningSeitenRandzoning
                ?? System.Array.Empty<ParkingGeometry.RandzoningLinie>();
            if (flaechen.Length == 0 && randzoning.Length == 0)
            {
                Mod.log.Warn("PLT-Zoningseiten: weder Zoning-Flaeche noch "
                    + "Randzoning gemerkt - die Seitenwahl greift nicht. "
                    + "Das ist ein Fehler, kein Normalfall.");
                return 0;
            }

            var zoningPrefabs = SammleZoningPrefabs();
            if (zoningPrefabs.Count == 0)
            {
                Mod.log.Warn("PLT-Zoningseiten: kein Zoning-Strassenprefab "
                    + "gefunden - die Seitenwahl greift nicht.");
                return 0;
            }

            var seite = _zoningSeitenWahl;
            var geaendert = 0;
            var randkanten = 0;
            var gesehen = 0;
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
                var mitte = (a + d) * 0.5f;
                var richtung = d - a;
                if (math.lengthsq(richtung) < 1e-6f) continue;

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
                    var innenrichtung = GeplanteInnenrichtung(a, d);
                    if (math.lengthsq(innenrichtung) < 1e-6f)
                        innenrichtung = _zoningSeitenLotmitte - mitte;
                    var innenLinks =
                        richtung.x * innenrichtung.y
                        - richtung.y * innenrichtung.x > 0f;
                    if (SetzeSeitenflaggen(kante, innenLinks, !innenLinks))
                    {
                        geaendert++;
                        randkanten++;
                    }
                    continue;
                }

                // Die naechstgelegene Zoning-Flaeche liefert den Innenpunkt.
                var innenpunkt = NaechsteZoningmitte(flaechen, mitte);
                var zurMitte = innenpunkt - mitte;

                /*
                 * Kreuzprodukt in der XZ-Ebene. Positiv heisst: der
                 * Innenpunkt liegt LINKS der gerichteten Kante.
                 */
                var kreuz = richtung.x * zurMitte.y - richtung.y * zurMitte.x;
                if (math.abs(kreuz) < 1e-4f) continue;
                var innenIstLinks = kreuz > 0f;

                var linksAus = seite == ParkingGeometry.Zoningseite.Innen
                    ? !innenIstLinks
                    : seite == ParkingGeometry.Zoningseite.Aussen
                        ? innenIstLinks
                        : false;
                var rechtsAus = seite == ParkingGeometry.Zoningseite.Innen
                    ? innenIstLinks
                    : seite == ParkingGeometry.Zoningseite.Aussen
                        ? !innenIstLinks
                        : false;

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

                if (SetzeSeitenflaggen(kante, linksAus, rechtsAus)) geaendert++;
            }

            // Auch der Nulllauf wird gemeldet. Eine Funktion, die stumm
            // nichts tut, kostet beim Suchen mehr als jede Logzeile.
            if (geaendert > 0)
            {
                /*
                 * DIE RANDZONING-KANTEN GETRENNT NENNEN.
                 *
                 * Die Zeile meldete alle Kanten unter der Panelwahl, also
                 * auch die des Randzonings - und die folgt ihr gar nicht,
                 * sie ist immer nur aussen. Im Bauzettel vom 2026-09-04 stand
                 * deshalb "5 von 5 Kante(n) auf 'Innen' gesetzt", obwohl eine
                 * davon die RZ-Strasse war. Wer danach sucht, sucht am
                 * falschen Ende.
                 */
                var hand = _zoningSeitenHandschaltungen?.Length ?? 0;
                Mod.log.Info($"PLT-Zoningseiten: {geaendert} von {gesehen} "
                    + $"Kante(n) gesetzt - {geaendert - randkanten} nach "
                    + $"Panelwahl '{seite}', {randkanten} im Randzoning "
                    + $"(immer nur aussen), {hand} Handschaltung(en) "
                    + "aufgetragen.");
            }
            else
            {
                Mod.log.Warn($"PLT-Zoningseiten: {gesehen} Zoning-Kante(n) "
                    + $"gesehen, aber keine geaendert (Wahl '{seite}').");
            }
            return geaendert;
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
         * Ein geschuetztes Leerzeichen, U+00A0.
         *
         * ALS ZEICHENCODE, NICHT ALS LITERAL: direkt in die Quelle
         * geschrieben waere es unsichtbar, und niemand koennte spaeter
         * erkennen, warum dort scheinbar ein leerer Text steht.
         */
        private static readonly string Unsichtbarername
            = ((char)0x00A0).ToString();

        /**
         * WELCHE SCHON BENANNT SIND, WIRD GEMERKT - nicht am Aggregat
         * abgelesen.
         *
         * Der Grund ist derselbe, aus dem es die Namenswache ueberhaupt
         * gibt: bildet CS2 den Strassenzug neu, entsteht eine NEUE Entity.
         * Die steht dann nicht in dieser Liste und gilt damit richtigerweise
         * wieder als unbenannt.
         */
        private readonly System.Collections.Generic.HashSet<Unity.Entities.Entity>
            _zoningBenannteAggregate =
                new System.Collections.Generic.HashSet<Unity.Entities.Entity>();

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

            var nameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            if (nameSystem == null)
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
                if (!_zoningBenannteAggregate.Add(aggregat)) continue;

                try
                {
                    nameSystem.SetCustomName(aggregat, Unsichtbarername);
                    if (!EntityManager.HasComponent<Game.UI.CustomName>(aggregat))
                        EntityManager.AddComponent<Game.UI.CustomName>(aggregat);
                    benannt++;
                }
                catch (System.Exception ausnahme)
                {
                    Mod.log.Warn("PLT-Zoningstrasse: Name nicht gesetzt - "
                        + ausnahme.Message);
                    _zoningBenannteAggregate.Remove(aggregat);
                    return benannt;
                }
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
                    + "Strassenzug/Strassenzuegen auf den unsichtbaren Namen "
                    + "gesetzt.");
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
                if (math.abs(richtung.x * w.y - richtung.y * w.x) <= 0.5f)
                    return i;
            }
            return -1;
        }

        /**
         * Setzt oder loescht `ZonesDisabled` an beiden Seiten und erhaelt
         * alle anderen Flaggen. Rueckgabe: wahr, wenn sich etwas geaendert
         * hat - nur dann wird `Updated` gesetzt.
         */
        private bool SetzeSeitenflaggen(Unity.Entities.Entity kante,
            bool linksAus, bool rechtsAus)
        {
            var hatte = EntityManager.HasComponent<Upgraded>(kante);
            var upgraded = hatte
                ? EntityManager.GetComponentData<Upgraded>(kante)
                : default;
            var vorherLinks = upgraded.m_Flags.m_Left;
            var vorherRechts = upgraded.m_Flags.m_Right;

            upgraded.m_Flags.m_Left = linksAus
                ? upgraded.m_Flags.m_Left | CompositionFlags.Side.ZonesDisabled
                : upgraded.m_Flags.m_Left & ~CompositionFlags.Side.ZonesDisabled;
            upgraded.m_Flags.m_Right = rechtsAus
                ? upgraded.m_Flags.m_Right | CompositionFlags.Side.ZonesDisabled
                : upgraded.m_Flags.m_Right & ~CompositionFlags.Side.ZonesDisabled;

            if (hatte && upgraded.m_Flags.m_Left == vorherLinks
                && upgraded.m_Flags.m_Right == vorherRechts) return false;

            if (hatte) EntityManager.SetComponentData(kante, upgraded);
            else EntityManager.AddComponentData(kante, upgraded);

            // Ohne Updated baut BlockSystem die Bloecke nicht neu; die
            // Flagge laege dann richtig da und wirkte trotzdem nicht.
            if (!EntityManager.HasComponent<Updated>(kante))
                EntityManager.AddComponent<Updated>(kante);
            return true;
        }
    }
}
