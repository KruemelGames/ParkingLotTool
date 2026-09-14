using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * DIE AUTOMATISCH GEBAUTEN LEITUNGEN VERSCHWINDEN MIT DEM PARKPLATZ.
     *
     * Ansage des Nutzers am 2026-09-06: *"dann muessen wir noch beim loeschen
     * des Parkplatzes auch die erstellten verbindungen loeschen."*
     *
     * Betroffen sind ausschliesslich Leitungen, die DER MOD gelegt hat -
     * erkennbar an `ParkingLotVersorgungsleitung`. Was der Nutzer selbst
     * gezogen hat, traegt diese Markierung nicht und wird nie angefasst.
     * Geloeschte Vanilla-Objekte kann er nicht wiederherstellen; hier falsch
     * zu liegen waere teurer als jeder stehengebliebene Rest.
     *
     * WAS BEIM LOESCHEN VON SELBST GESCHIEHT - im Dekompilat nachgelesen:
     *
     *   - `Game.Simulation.ElectricityGraphDeleteSystem` (Abfrage bei 137)
     *     greift bei JEDER Entity mit `Deleted` ohne `Temp`, die eine
     *     `ElectricityNodeConnection` traegt, und ruft
     *     `ElectricityGraphUtils.DeleteFlowNode` (119): der Flussknoten und
     *     ALLE an ihm haengenden Flusskanten bekommen ebenfalls `Deleted`.
     *     Fuer Wasser und Abwasser gilt dasselbe ueber
     *     `WaterPipeGraphDeleteSystem`.
     *
     *     Unsere Leitungskante traegt diese Komponente - `ElectricityEdge
     *     GraphSystem` setzt sie beim Bau auf die Kante (Mittelknoten). Der
     *     Versorgungsgraph raeumt sich also selbst ab; nichts davon muss der
     *     Mod von Hand anfassen.
     *
     *   - `Game.Net.ReferencesSystem.UpdateNodeReferencesJob` (41) behandelt
     *     einen geloeschten KNOTEN eigens: er entfernt ihn aus dem
     *     `ConnectedNode`-Puffer jeder Kante, an der er haengt. Der seitliche
     *     Eintrag an der fremden Stadtstrasse verschwindet damit von selbst -
     *     aber nur, wenn der KNOTEN geloescht wird, nicht schon bei der Kante.
     *
     * DARAUS FOLGT: die Kante allein reicht nicht. Ihre beiden Endknoten
     * muessen mit - sonst bleibt ein Knoten ohne Kante stehen, und die
     * Stadtstrasse behaelt einen Verweis auf ihn.
     *
     * Ein Knoten wird nur dann mitgeloescht, wenn AUSSCHLIESSLICH unsere
     * eigenen markierten Leitungskanten an ihm haengen. Haengt dort noch
     * irgendetwas anderes, bleibt er stehen: dann gehoert er nicht uns.
     */
    public sealed partial class ParkingLotCleanupSystem
    {
        private EntityQuery _versorgungsleitungQuery;

        private void InitialisiereVersorgungsaufraeumung()
        {
            _versorgungsleitungQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotVersorgungsleitung>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        /**
         * Merkt die Leitungen eines abgerissenen Parkplatzes zum Loeschen vor.
         *
         * Rueckgabe ist die Zahl der vorgemerkten KANTEN. Sie geht in dieselbe
         * Portionsrechnung wie die uebrigen Teile ein, damit ein grosser
         * Abriss nicht in einem einzigen Frame stattfindet.
         */
        private int MarkiereVersorgungsleitungen(
            EntityCommandBuffer buffer, Entity lot, Entity carrier)
        {
            if (_versorgungsleitungQuery.IsEmptyIgnoreFilter) return 0;

            using var leitungen = _versorgungsleitungQuery
                .ToEntityArray(Allocator.TempJob);

            // Erst alle unsere Kanten sammeln, dann loeschen: die Pruefung
            // "haengt an diesem Knoten noch etwas Fremdes?" braucht die
            // vollstaendige Liste, sonst haelt sie die Nachbarkante derselben
            // Leitung faelschlich fuer fremd.
            var unsere = new NativeHashSet<Entity>(leitungen.Length,
                Allocator.Temp);
            for (var i = 0; i < leitungen.Length; i++)
            {
                var zuordnung = EntityManager
                    .GetComponentData<ParkingLotVersorgungsleitung>(
                        leitungen[i]);
                if (zuordnung.Lot != lot
                    && (carrier == Entity.Null
                        || zuordnung.Carrier != carrier)) continue;
                unsere.Add(leitungen[i]);
            }
            if (unsere.Count == 0) { unsere.Dispose(); return 0; }

            var kanten = 0;
            for (var i = 0; i < leitungen.Length; i++)
            {
                var kante = leitungen[i];
                if (!unsere.Contains(kante)) continue;
                // Temp niemals anfassen - eine Bulldozer-Vorschau traegt
                // Deleted + Temp, und die darf nichts wirklich loeschen.
                if (EntityManager.HasComponent<Temp>(kante)) continue;

                /*
                 * DIE KNOTEN GEHOEREN CS2 - WIR RUEHREN SIE NICHT AN.
                 *
                 * Hier wurde jeder Knoten, an dem nur unsere eigenen Leitungen
                 * hingen, selbst als `Deleted` markiert. GENAU DAS liess das
                 * Spiel abstuerzen.
                 *
                 * NACHGEWIESEN am 2026-09-10 durch Weglassen: mit dieser
                 * Abraeumung stuerzte CS2 beim Weggbaggern viermal ab, immer
                 * drei Durchgaenge nach dem Bagger und unabhaengig davon, ob
                 * dabei 98 Teile geloescht wurden oder 3 - die Teilezahl war
                 * also nie die Ursache. Ohne sie lief derselbe Abriss glatt
                 * durch: 399 Teile, Traeger entfernt, "relationsbasierter
                 * Abriss vollstaendig". Der Nutzer: *"Bingo!"*
                 *
                 * Dazu passt sein Befund vom 2026-08-26 - *"Stufe 1 liess sich
                 * sauber loeschen, Stufe 2 (Strom) stuerzte beim Loeschen
                 * ab."* Es war immer diese Ecke.
                 *
                 * WARUM ES FALSCH WAR: einen Knoten loescht man in CS2 nicht
                 * selbst. Der Bulldozer markiert Kanten; verwaiste Knoten
                 * raeumt das Spiel danach ab, zusammen mit dem Fluss-Graphen
                 * von Strom und Wasser. Wer beides im selben Zug markiert,
                 * nimmt dem Spiel die Grundlage fuer genau diesen Aufraeumzug
                 * weg. Fuer die Fluss-Anteile stand die Regel hier schon in
                 * der Meldung darunter - sie galt nur fuer die Knoten nicht.
                 *
                 * Ein Knoten, den CS2 wider Erwarten stehen laesst, ist ein
                 * Schoenheitsfehler. Ein Absturz ist keiner.
                 */
                buffer.AddComponent<Deleted>(kante);
                kanten++;
            }
            unsere.Dispose();

            if (kanten > 0)
                Mod.log.Info($"PLT-Aufraeumer: {kanten} eigene Leitungskante(n) "
                    + $"von Parkplatz {lot.Index} zum Loeschen vorgemerkt. "
                    + "Knoten, Flussknoten und Flusskanten raeumt CS2 selbst "
                    + "ab - sie selbst zu markieren war die Absturzursache "
                    + "vom 2026-09-10.");
            return kanten;
        }

        /*
         * HIER STAND `KnotenGehoertNurUns`.
         *
         * Sie prüfte, ob an einem Knoten ausser unseren eigenen Leitungen
         * noch etwas haengt - die Schranke dagegen, einen Knoten der
         * Stadtstrasse mitzureissen. Die Frage stellt sich nicht mehr: wir
         * markieren ueberhaupt keine Knoten mehr, weil genau das der Absturz
         * war (2026-09-10, oben belegt). Eine Schranke ohne Tor ist toter
         * Code, und toter Code liest sich beim naechsten Mal wie eine
         * geltende Regel.
         */
    }
}
