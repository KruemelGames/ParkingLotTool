using System.Collections.Generic;
using Game;
using Game.Areas;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Raeumt die Teile eines weggebaggerten PLT-Parkplatzes ueber ihre eigene
     * Relation ab.
     *
     * Der fruehere Weg suchte Vanilla-Prefabs ohne `Owner` im Polygon. Damit
     * blieben Ladesaeulen stehen: Sie tragen absichtlich einen Schutz-Owner,
     * damit die Lot-Flaeche ihr `Overridable`-Prefab nicht ausblendet. Seit
     * Umbau 1 nennt dagegen jedes Teil sein Lot und seinen Traeger exakt.
     *
     * Zwei gemessene Absturzsicherungen bleiben unveraendert:
     *
     * - Eine Bulldozer-Vorschau traegt `Deleted + Temp` und darf den Aufraeumer
     *   nicht starten. Deshalb schliesst die Lot-Abfrage `Temp` aus.
     * - Hoechstens 32 Teile werden je Durchgang vorgemerkt. Diese Grenze stammt
     *   aus der Absturzserie im Juli; ohne neuen Ingame-Stresstest gibt es
     *   keinen Messwert, der eine Erhoehung rechtfertigt.
     *
     * Das System bleibt in Modification3 vor `LaneSystem` in Phase 4. So sieht
     * Vanilla jedes geloeschte Decal noch rechtzeitig und raeumt dessen eigene
     * Parkspur mit auf. Strukturaenderungen laufen ausschliesslich ueber die
     * Barrier derselben Phase.
     */
    public sealed partial class ParkingLotCleanupSystem : GameSystemBase
    {
        private sealed class CleanupWork
        {
            internal Entity Lot;
            internal Entity Carrier;
            internal bool Ready;
        }

        private ModificationBarrier3 _barrier;
        private EntityQuery _deletedLotQuery;
        private EntityQuery _partQuery;
        private readonly List<CleanupWork> _pending = new List<CleanupWork>();
        private readonly HashSet<Entity> _knownLots = new HashSet<Entity>();

        /*
         * PROBELAUF 2026-09-10 18:53: EINS JE DURCHGANG.
         *
         * Der Absturz beim Weggbaggern kommt dreimal an derselben Stelle -
         * nach drei Portionen zu 32, also rund 98 Teilen. Die Fahndungsliste
         * zeigt dort nur gewoehnliche Buchten-Aufkleber, keinen Ausreisser.
         * Damit bleiben genau zwei Moeglichkeiten, und eine Zahl trennt sie:
         *
         *   - Es haengt an EINEM bestimmten Teil. Dann stuerzt es auch bei
         *     einem Teil je Durchgang ab, und die letzte TEILE-Zeile nennt
         *     genau dieses eine.
         *   - Es haengt an der MENGE je Durchgang. Dann laeuft der Abriss
         *     jetzt durch, und die Portionsgroesse IST der Fehler.
         *
         * Die 32 stammen aus der Absturzserie im Juli und waren nie
         * nachgemessen - sie waren die Zahl, bei der es damals aufhoerte.
         */
        private const int PartsPerPass = 32;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _barrier = World.GetOrCreateSystemManaged<ModificationBarrier3>();

            _deletedLotQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });

            // Temp bleibt hier sichtbar, damit ein temporaeres Teil das
            // Entfernen des Traegers aufschiebt. Geloescht wird es nie.
            _partQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Aufraeumen);
            CollectDeletedLots();
            ProcessOnePass();
        }

        private void CollectDeletedLots()
        {
            if (_deletedLotQuery.IsEmptyIgnoreFilter) return;

            using var lots = _deletedLotQuery.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                if (!_knownLots.Add(lot)) continue;

                var carrier = EntityManager
                    .GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
                _pending.Add(new CleanupWork { Lot = lot, Carrier = carrier });
                Mod.log.Info($"PLT-Aufraeumer: Parkplatz {lot.Index} wirklich "
                    + "weggebaggert; relationsbasiertes Aufraeumen beginnt "
                    + "im naechsten Durchgang.");
            }
        }

        private void ProcessOnePass()
        {
            if (_pending.Count == 0) return;

            var work = _pending[0];
            if (!work.Ready)
            {
                // Das Lot wurde in diesem Systemdurchlauf erst entdeckt. Die
                // erste Teilportion beginnt bewusst einen Durchgang spaeter,
                // getrennt von der Vanilla-Loeschkaskade des Lots.
                work.Ready = true;
                return;
            }
            var buffer = _barrier.CreateCommandBuffer();
            var (marked, remaining, pflanzen, pflanzenUebrig) =
                MarkRelatedParts(buffer, work.Lot);
            /*
             * DIE LEITUNGEN WERDEN HIER NICHT MEHR ANGEFASST.
             *
             * Sie fielen frueher in derselben Portion wie die uebrigen Teile.
             * Das war die Absturzursache: `Modification3` ist zu spaet, um
             * eine NETZKANTE zu loeschen - `Game.Net.ReferencesSystem` laeuft
             * in `Modification2B` und bekommt sie nie zu sehen. Seit dem
             * 2026-09-14 erledigt das `ParkingLotLeitungsabrissSystem` in
             * `Modification2`; die vollstaendige Herleitung steht in dessen
             * Dateikopf.
             *
             * Fuer die Portionsrechnung aendert das nichts: die Leitungen
             * tragen keine `ParkingLotPartRelation` und zaehlten nie in
             * `remaining` mit. Der Traeger faellt weiterhin erst, wenn kein
             * relationiertes Teil mehr uebrig ist.
             */

            if (marked > 0)
            {
                // Auch wenn dies die letzte Portion war, bleibt der Traeger
                // bis zum naechsten Durchgang stehen. So laufen letzte
                // Teil-Loeschung und Traeger-Loeschung nie in derselben Kaskade.
                /*
                 * PFLANZEN GETRENNT GEZAEHLT.
                 *
                 * Der Nutzer am 2026-09-15: *"ich habe Baeume auf Sapling
                 * gesetzt und es blieben noch die alten stehen."* Die alten
                 * Pflanzen muessen ueber genau diesen Weg verschwinden - sie
                 * tragen `ParkingLotPartRelation` auf das alte Lot. Ob sie
                 * hier ueberhaupt ankommen, stand nirgends. Eine Zahl, die
                 * stumm 0 ist, sieht aus wie "nichts zu tun".
                 */
                Mod.log.Info($"PLT-Aufraeumer: {marked} relationierte Teile "
                    + $"vorgemerkt ({pflanzen} davon Pflanzen), {remaining} "
                    + $"bleiben uebrig ({pflanzenUebrig} davon Pflanzen); "
                    + $"Grenze {PartsPerPass} je Durchgang.");
                ParkingLotSchrittmarke.Setze(
                    $"Abriss: {marked} Teile vorgemerkt, {remaining} uebrig");
/*
                 * HIER STAND DIE FAHNDUNGSLISTE.
                 *
                 * Sie nannte jedes vorgemerkte Teil beim Namen - bis zu 400
                 * je Abriss. Sie hat am 2026-09-10 zwei Absturztheorien
                 * erledigt (ein bestimmtes Teil, die Portionsgroesse) und
                 * damit den Weg zur echten Ursache frei gemacht: wir hatten
                 * die Leitungsknoten selbst markiert. Der Befund steht, die
                 * Liste hat ihre Arbeit getan.
                 */
                return;
            }

            if (remaining > 0)
            {
                // Das sind ausschliesslich temporaere Teile. Sie werden weder
                // geloescht noch vom Traeger abgeschnitten.
                Mod.log.Info($"PLT-Aufraeumer: 0 dauerhafte Teile vorgemerkt, "
                    + $"{remaining} temporaere Teile blockieren den Abschluss.");
                return;
            }

            var carrierRemoved = work.Carrier != Entity.Null
                && EntityManager.Exists(work.Carrier);
            if (carrierRemoved)
            {
                // Der technische Traeger hat keine eigene Simulation. Nach
                // einem vollstaendig leeren Teilregister wird er direkt an der
                // Barrier zerstoert, damit seine inzwischen alten SubNet- und
                // SubObject-Verweise keine zweite Vanilla-Loeschkaskade starten.
                buffer.DestroyEntity(work.Carrier);
            }

            _pending.RemoveAt(0);
            _knownLots.Remove(work.Lot);
            Mod.log.Info("PLT-Aufraeumer: relationsbasierter Abriss vollstaendig; "
                + (carrierRemoved ? "technischer Traeger entfernt."
                                  : "technischer Traeger war bereits fort."));
            // "0 uebrig" heisst nur, dass nichts mehr VORGEMERKT wird. Der
            // Traeger faellt einen Durchgang spaeter - und genau dieser
            // letzte Schritt war in der Spur bisher unsichtbar.
            ParkingLotSchrittmarke.Setze("Abriss: fertig, Traeger "
                + (carrierRemoved ? "entfernt" : "war schon fort"));
        }

        /** Traegt dieses Teil eine Pflanze? Ueber das Prefab, nicht ueber
         *  eine gepflegte Liste - eine Liste vergisst man. */
        private bool IstPflanze(Entity part)
            => EntityManager.HasComponent<Game.Prefabs.PrefabRef>(part)
               && EntityManager.HasComponent<Game.Prefabs.PlantData>(
                   EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(part)
                       .m_Prefab);

        private (int Marked, int Remaining, int Pflanzen, int PflanzenUebrig)
            MarkRelatedParts(EntityCommandBuffer buffer, Entity lot)
        {
            using var parts = _partQuery.ToEntityArray(Allocator.TempJob);
            var marked = 0;
            var remaining = 0;
            var pflanzen = 0;
            var pflanzenUebrig = 0;

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var relation = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(part);
                if (relation.Lot != lot) continue;

                if (EntityManager.HasComponent<Temp>(part)
                    || marked >= PartsPerPass)
                {
                    remaining++;
                    if (IstPflanze(part)) pflanzenUebrig++;
                    continue;
                }
                if (IstPflanze(part)) pflanzen++;

                // Ein Decal bringt eigene Parkspuren mit. Sie werden zusammen
                // mit dem Teil in Modification3 markiert, damit LaneSystem in
                // Phase 4 keine Waisen vorfindet.
                if (EntityManager.HasBuffer<Game.Net.SubLane>(part))
                {
                    var lanes = EntityManager.GetBuffer<Game.Net.SubLane>(part);
                    for (var laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
                    {
                        var lane = lanes[laneIndex].m_SubLane;
                        if (lane != Entity.Null && EntityManager.Exists(lane)
                            && !EntityManager.HasComponent<Deleted>(lane))
                            buffer.AddComponent<Deleted>(lane);
                    }
                }

                /*
                 * DIE GEGENBUCHUNG ZUM STROMANSCHLUSS DES BEGLEITERS.
                 *
                 * Nutzerbefund am 2026-08-26: Stufe 1 liess sich sauber
                 * loeschen, Stufe 2 (Strom) stuerzte beim Loeschen ab. Seine
                 * Vermutung - "er versucht die Stromanbindung zu loeschen, die
                 * nie erstellt wurde" - trifft die richtige Ecke.
                 *
                 * Gemessen im Dekompilat: `ConnectedBuilding`-Eintraege werden
                 * im GANZEN Spiel an genau einer Stelle wieder entfernt,
                 * `Game.Buildings/RoadConnectionSystem.cs:628`. Und genau
                 * dieses System steigt bei unserem Begleiter sofort aus, weil
                 * sein Prefab `NoRoadConnection` traegt (Zeile 73).
                 *
                 * Wir schreiben uns also in eine Liste ein, die wir selbst nie
                 * wieder aufraeumen. Hier ist der Moment dafuer: das Teil ist
                 * dem Untergang geweiht, aber Strasse und Puffer stehen noch.
                 */
                if (EntityManager.HasComponent<Game.Buildings.Building>(part))
                {
                    var strasse = EntityManager
                        .GetComponentData<Game.Buildings.Building>(part)
                        .m_RoadEdge;
                    if (strasse != Entity.Null
                        && EntityManager.Exists(strasse)
                        && EntityManager.HasBuffer<
                            Game.Buildings.ConnectedBuilding>(strasse))
                    {
                        var angeschlossen = EntityManager.GetBuffer<
                            Game.Buildings.ConnectedBuilding>(strasse);
                        for (var eintrag = angeschlossen.Length - 1;
                             eintrag >= 0; eintrag--)
                            if (angeschlossen[eintrag].m_Building == part)
                                angeschlossen.RemoveAt(eintrag);
                    }
                }

                buffer.AddComponent<Deleted>(part);
                marked++;
            }

            return (marked, remaining, pflanzen, pflanzenUebrig);
        }
    }
}
