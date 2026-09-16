using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Dauerhafte, exakte Zuordnung eines von PLT gesetzten Objekts.
     *
     * Beide Verweise werden gespeichert: die Lot-Flaeche bleibt der fachliche
     * Parkplatz, der nackte Traeger ist der technische Parent fuer die
     * Parkspuranbindung. Damit braucht weder die spaetere Auswahl noch das
     * spaetere Loeschen einen raeumlichen Rueckschluss ueber Vanilla-Prefab
     * und Polygon.
     */
    public struct ParkingLotPartRelation : IComponentData, IQueryTypeParameter,
                                           ISerializable
    {
        public Entity Lot;
        public Entity Carrier;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(Lot);
            writer.Write(Carrier);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Lot);
            reader.Read(out Carrier);
        }
    }

    /**
     * Exakter Rueckweg von der fachlichen Lot-Flaeche zum technischen Traeger.
     *
     * `VehicleUtils.GetParkingData` beginnt an der ausgewaehlten Entity und
     * folgt nur deren `SubLane`, `SubNet` und `SubObject`. Seit die Aufkleber
     * am Traeger haengen, kann die Flaeche sie deshalb nicht mehr sehen. Der
     * Verweis laesst ausschliesslich die Statistik am Traeger beginnen; Klick,
     * Darstellung und Besitzerverhaeltnisse bleiben bei der Flaeche.
     */
    public struct ParkingLotCarrierReference : IComponentData,
                                               IQueryTypeParameter,
                                               ISerializable
    {
        public Entity Carrier;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
            => writer.Write(Carrier);

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
            => reader.Read(out Carrier);
    }

    /**
     * Bindet Wege an die PLT-Flaeche und Objekte an ihren nackten Traeger und
     * gibt dem fachlichen Parkplatz einen Namen.
     *
     * ABSTURZGEFAHR, GEPRUEFT IM DEKOMPILAT: `SubAreaReferencesSystem` fragt
     * vor jedem Zugriff `m_SubAreas.HasBuffer(owner)` - deshalb liefen die
     * Flaechen bisher ohne eigenen Puffer durch. `SubNetReferencesSystem`
     * und `SubObjectReferencesSystem` tun das NICHT: in ihrem Zweig fuer
     * dauerhaft gewordene Kinder steht blank
     * `CollectionUtils.TryAddUniqueValue(m_SubNets[owner.m_Owner], ...)`.
     * Ein Besitzer ohne `SubNet`- bzw. `SubObject`-Puffer laesst dieses
     * Burst-Job also auflaufen, sobald unsere Wege dauerhaft werden.
     * Die Puffer werden hier deshalb IMMER angelegt, auch leer.
     *
     * Der Besitzer bleibt die groesste eigene Flaeche. Eine selbstgebaute
     * Entity waere sauberer, hat aber kein `PrefabRef` - und
     * `SelectedInfoUISystem` oeffnet nur mit einem
     * (`TryGetSelection(...) && TryGetComponent<PrefabRef>(entity, ...)`).
     */
    public sealed partial class ParkingLotToolSystem
    {
        internal sealed class PartTransferRecord
        {
            internal string Kind;
            internal int Index;
            internal Entity Prefab;
            internal Entity Definition;
            internal float3 From;
            internal float3 To;
        }

        private readonly List<PartTransferRecord> _netRecords =
            new List<PartTransferRecord>();
        private readonly List<PartTransferRecord> _objectRecords =
            new List<PartTransferRecord>();

        private Entity _lotCarrier = Entity.Null;

        private EntityQuery _tempNetQuery;
        private EntityQuery _tempObjectQuery;

        private void InitializeLotOwner()
        {
            _tempNetQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Game.Net.Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Owner>(),
                },
            });
            // Unsere Aufkleber und Ladesaeulen. Sie bekommen bewusst KEINEN
            // Besitzer (siehe oben), aber sie brauchen ein `Attached`.
            _tempObjectQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Owner>(),
                },
            });
        }

        private void ResetPartRecords()
        {
            _netRecords.Clear();
            _objectRecords.Clear();
            // Ein Traeger gehoert genau zu dem Bau, aus dessen fertigem Plan
            // er entsteht. Der vorige Parkplatz darf nie wiederverwendet
            // werden, auch wenn dieselben Vanilla-Prefabs vorkommen.
            _lotCarrier = Entity.Null;
        }

        private void RecordNetDefinition(string kind, int index, Entity prefab,
                                         Entity definition, float3 from, float3 to)
        {
            _netRecords.Add(new PartTransferRecord
            {
                Kind = kind,
                Index = index,
                Prefab = prefab,
                Definition = definition,
                From = from,
                To = to,
            });
        }

        private void RecordObjectDefinition(string kind, int index, Entity prefab,
                                            Entity definition, float3 position)
        {
            _objectRecords.Add(new PartTransferRecord
            {
                Kind = kind,
                Index = index,
                Prefab = prefab,
                Definition = definition,
                From = position,
                To = position,
            });
        }

        /**
         * Legt die drei Kind-Puffer am Besitzer an.
         *
         * Muss laufen, BEVOR ein Kind dauerhaft wird - siehe die
         * Absturzbegruendung oben am Typ.
         */
        private void EnsureOwnerBuffers(Entity owner)
        {
            if (owner == Entity.Null || !EntityManager.Exists(owner)) return;
            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(owner))
                EntityManager.AddBuffer<Game.Areas.SubArea>(owner);
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(owner))
                EntityManager.AddBuffer<Game.Net.SubNet>(owner);
            if (!EntityManager.HasBuffer<Game.Objects.SubObject>(owner))
                EntityManager.AddBuffer<Game.Objects.SubObject>(owner);
        }

        /**
         * Erzeugt den technischen Traeger aus dem bereits materialisierten
         * fertigen Bauplan.
         *
         * Der Ingame-Versuch vom 2026-08-25 hat genau diese nackte Gestalt
         * gemessen: 5 Aufkleber blieben bis auf 0,0005 m liegen und die Entity
         * ueberlebte Speichern/Laden. Transform, Node, Edge und Area bleiben
         * deshalb nicht nur unbenutzt, sondern werden gar nicht erst angelegt.
         */
        private bool CreateLotCarrier()
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner)
                || !EntityManager.HasComponent<PrefabRef>(_lotOwner))
            {
                Mod.log.Warn("PLT-Traeger nicht angelegt: die fertige "
                    + "Parkplatzflaeche oder ihr PrefabRef fehlt.");
                return false;
            }

            ProtokolliereBauschritt("Träger CreateEntity");
            var carrier = EntityManager.CreateEntity();
            ProtokolliereBauschritt("Träger PrefabRef");
            EntityManager.AddComponentData(carrier,
                EntityManager.GetComponentData<PrefabRef>(_lotOwner));
            ProtokolliereBauschritt("Träger SubObject");
            EntityManager.AddBuffer<Game.Objects.SubObject>(carrier);
            ProtokolliereBauschritt("Träger SubNet");
            EntityManager.AddBuffer<Game.Net.SubNet>(carrier);
            ProtokolliereBauschritt("Besitzer ParkingLotCarrierReference");
            EntityManager.AddComponentData(_lotOwner,
                new ParkingLotCarrierReference { Carrier = carrier });
            _lotCarrier = carrier;
            // Nachzaehlen, was CS2 aus unserem Zoning-Ring macht. Laeuft
            // verzoegert, weil BlockSystem und ValidAreaSystem erst nach uns
            // an der Reihe sind.
            ProtokolliereBauschritt("PlaneZoningBlockmessung");
            PlaneZoningBlockmessung(carrier);
            // Zaehlt unsere Flaechen jetzt und noch einmal, wenn Haeuser
            // gewachsen sind - beantwortet "ueberlagert oder verdraengt".
            ProtokolliereBauschritt("ParkingLotSurfaceWatchSystem.Beobachte");
            World.GetOrCreateSystemManaged<ParkingLotSurfaceWatchSystem>()
                .Beobachte(_lotOwner);
            Mod.log.Info($"PLT-Traeger {carrier.Index} aus fertigem Plan "
                + $"angelegt fuer Flaeche {_lotOwner.Index}: PrefabRef, "
                + "SubObject und SubNet; kein Transform/Node/Edge/Area.");
            return true;
        }

        /**
         * Setzt den Besitzer auf alle eigenen Temp-Wege.
         *
         * Erkannt werden sie am Prefab: nur unsere unsichtbaren Wege. Temp-
         * Entities gehoeren immer dem gerade aktiven Werkzeug, und das sind in
         * diesem Moment wir - Kopien fremder Netze, die CS2 fuers Anschliessen
         * anlegt, tragen ein Strassen-Prefab und fallen damit heraus.
         *
         * Dass ein Weg einen Besitzer bekommt, schadet der Strassenanbindung
         * nicht: `ReferencesSystem.AllowConnection` steigt bei einem Besitzer
         * ohne `Transform` sofort mit `true` aus, und unsere Flaeche hat
         * keinen. `GenerateEdgesSystem` sucht mit Besitzer sogar 8 m weiter.
         *
         * DECALS BEKOMMEN AUSDRUECKLICH KEINEN BESITZER MEHR.
         * Vegetation seit 14.09.2026: Owner zum NACKTEN Traeger, niemals
         * direkt zur Flaeche; siehe VEGETATION-OWNER-20260914.md. Den Traeger
         * nicht Updated/SubObjectsUpdated markieren (sonst native Neuerzeugung).
         *
         * Sie hatten einen, und das war der Fehler vom 2026-08-12: baute der
         * Nutzer eine Strasse NEBEN den Parkplatz, lagen anschliessend alle
         * Decals und Ladesaeulen zufaellig verstreut und verdreht auf der
         * Flaeche. Gelesen in `Game.Objects.SubObjectSystem`:
         *
         *   - Die Verzweigung entscheidet nach dem Besitzer: hat er ein
         *     `Transform`, laeuft der Objektpfad; ist er eine Flaeche
         *     (`m_AreaData`), laeuft `RelocateSubObjects`.
         *   - Dort bekommt JEDES Kind, dessen Pruefkreis einen Nachbarn
         *     schneidet, `AreaUtils.GetRandomPosition` und
         *     `GetRandomRotation`. CS2 behandelt Kinder einer Flaeche wie
         *     Streugut in einem Park.
         *   - `AreaUtils.IntersectObjects` ist ein Kreistest ueber die halbe
         *     Bounds-Diagonale. Ein Stellplatzdecal misst 3,0 x 5,9 m, sein
         *     Radius ist damit ~3,3 m, der Abstand zum Nachbarn aber nur
         *     3,0 m. Die Ueberschneidung ist unvermeidbar, also trifft es
         *     praktisch jedes Decal.
         *   - Der Strassenbau nebenan muss die Flaeche nur als `Updated`
         *     markieren; das genuegt als Ausloeser.
         *
         * Zwei naheliegende Auswege sind GEPRUEFT UND FALSCH:
         *
         *   - `Secondary` an die Objekte: `RelocateSubObjects` ueberspringt
         *     sie zwar, aber `SecondaryObjectSystem.FillOldSubObjectsBuffer`
         *     sammelt jedes `Secondary`-Kind als "alt" ein und
         *     `RemoveUnusedOldSubObjects` loescht es danach. Aus verstreut
         *     wuerde geloescht.
         *   - `Transform` an die Besitzerflaeche: dann liefe der Objektpfad,
         *     der die Kinder aus den im PREFAB deklarierten SubObjects
         *     erzeugt. Unser Lot-Prefab deklariert keine, also verbraucht
         *     nichts die Altliste - und `RemoveUnusedOldSubObjects` loescht
         *     ebenfalls alles.
         *
         * Ohne Besitzer fasst CS2 die Objekte gar nicht erst an. Die Flaechen
         * behalten ihren Besitzer: `SubAreaReferencesSystem` ist der einzige
         * der drei Referenzpfade ohne diese Umverteilung.
         */
        private (int Nets, int Objects) AttachPartsToLotOwner()
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner))
                return (0, 0);

            EnsureOwnerBuffers(_lotOwner);
            var nets = AttachByPrefab(_tempNetQuery, IsOwnPathPrefab);
            var objects = HefteObjekteAnTraeger();

            /**
             * NACHZAEHLEN - aber mit der richtigen Einheit.
             *
             * Ein `NetCourse` wird zu einer Kante UND ihren Knoten, und die
             * Abfrage sammelt beides ein. Gemessen am Bau vom 2026-08-12: 34
             * Kurse ergaben 98 Entities. Mein erster Zaehler verglich Kurse mit
             * Entities und meldete prompt "98 von 34" als Fehler. Ein falscher
             * Alarm ist schlimmer als keiner - er gewoehnt einen daran,
             * Warnungen zu ueberlesen. Deshalb hier nur die Richtung: WENIGER
             * Entities als Kurse waere ein Problem.
             *
             * Objekte bekommen weiterhin KEINEN Besitzer, aber seit dem
             * 2026-08-17 ein `Attached` - fuer den nackten Traeger hat der
             * Ingame-Versuch am 2026-08-25 eine groesste Lageabweichung von
             * 0,0005 m gemessen. Die Zahl steht in der Bau-Meldung.
             */
            /*
             * DIE ZAHL STEHT JETZT IMMER DA, NICHT NUR IM FEHLERFALL.
             *
             * Am 2026-08-31 war "nur 54 Wegteile aus 72 Kursen" der einzige
             * Hinweis auf einen Umbau, bei dem Wege am alten Traeger haengen
             * blieben - und er erschien nur bei EINEM von fuenf Umbauten.
             * Ohne Zeile im Normalfall laesst sich nicht sagen, ob ein Bau
             * gesund war oder die Schwelle nur knapp verfehlt hat. Ein
             * Rueckfall soll an einer Zahl erkennbar sein, nicht am Gefuehl.
             */
            Mod.log.Info($"PLT-Besitzer: {nets} Wegteile aus "
                + $"{_netRecords.Count} Kursen. Erwartet wird MEHR als Kurse "
                + "(ein Kurs ergibt eine Kante und ihre Knoten).");
            if (nets < _netRecords.Count)
                Mod.log.Warn($"PLT-Besitzer: nur {nets} Wegteile aus "
                    + $"{_netRecords.Count} Kursen haben einen Besitzer "
                    + "bekommen. Der Rest bliebe herrenlos - jeder neue Weg "
                    + "muss über RecordNetDefinition laufen.");
            if (objects != _objectRecords.Count)
                Mod.log.Warn($"PLT-Relation: {objects} von "
                    + $"{_objectRecords.Count} gesetzten Aufklebern und "
                    + "Ladesäulen tragen Fläche und Träger. Ein Teil ohne "
                    + "Relation darf nicht dauerhaft werden.");
            return (nets, objects);
        }

        /**
         * WELCHE TEILE UNS GEHOEREN - und damit an den Besitzer muessen.
         *
         * Hier stand eine HANDGEPFLEGTE LISTE der drei Aufkleber-Prefabs. Beim
         * Einbau der Ladesaeule habe ich sie prompt vergessen: die Saeulen
         * waeren als herrenlose Einzelobjekte im Gelaende stehengeblieben -
         * nicht mit dem Parkplatz loeschbar, nicht Teil seines Umrisses, beim
         * naechsten Bulldozer uebrig.
         *
         * Deshalb keine Liste mehr, sondern die Prefabs, die WIR IN DIESEM
         * DURCHGANG SELBST GESETZT HABEN. `RecordObjectDefinition` und
         * `RecordNetDefinition` laufen bei jedem erzeugten Teil mit, also ist
         * jedes kuenftige Asset automatisch dabei, ohne dass jemand daran
         * denken muss.
         *
         * Der Filter bleibt trotzdem noetig: die Abfrage findet ALLE Temp-
         * Objekte ohne Besitzer, auch die eines fremden Werkzeugs.
         */
        private bool IsOwnPathPrefab(Entity prefab)
            => IsRecordedPrefab(_netRecords, prefab);

        private bool IsOwnObjectPrefab(Entity prefab)
            => IsRecordedPrefab(_objectRecords, prefab);

        /**
         * Notaus. Steht das hier auf false, verhaelt sich der Mod wie vor dem
         * 2026-08-17. Die Aufkleber verlieren dann wieder ihre
         * Strassenanbindung, aber nichts anderes aendert sich.
         */
        private static readonly bool AufkleberAnTraegerHeften = true;

        /**
         * WARUM DIE AUFKLEBER EIN `Attached` BRAUCHEN - und weiterhin KEINEN
         * `Owner` bekommen duerfen.
         *
         * Der Nutzer meldete am 2026-08-17: Autos fahren auf unseren Parkplatz,
         * wenden dort und fahren wieder weg. Der Vanilla-Parkplatz daneben war
         * mit 135 Fahrzeugen randvoll (freier Platz 0,0 m), bei uns standen
         * 270 m frei und genau ein Motorrad. Das ist keine Vorliebe mehr.
         *
         * Ursache, gelesen in `Game.Net.LaneConnectionSystem`
         * (`FindParkingConnection`, Zeile 471-483):
         *
         *     Owner owner = m_OwnerData[entity];          // = der Aufkleber
         *     while (m_OwnerData.TryGetComponent(owner.m_Owner, out cd)
         *            && !m_BuildingData.HasComponent(owner.m_Owner))
         *         owner = cd;                             // hoch bis GEBAEUDE
         *     if (!m_BuildingData.HasComponent(owner.m_Owner)
         *         && m_AttachedData.TryGetComponent(owner.m_Owner, out cd2)
         *         && m_PrefabRefData.HasComponent(cd2.m_Parent))
         *         owner.m_Owner = cd2.m_Parent;           // ODER Attached
         *
         * Danach wird die Strassenanbindung AUSSCHLIESSLICH in `SubLane`,
         * `SubNet` und `SubArea` dieses einen Besitzers gesucht - nicht in der
         * Welt. Beim Vanilla-Parkplatz steigt die Kette bis zum Gebaeude, dessen
         * `SubNet` die unsichtbaren Fahrwege enthaelt. Unser Aufkleber hat
         * keinen Besitzer, die Schleife laeuft also nie, und gesucht wird in den
         * Puffern des Aufklebers selbst - der hat weder `SubNet` noch `SubArea`.
         * Keine Anbindung, keine erreichbare Bucht.
         *
         * `Attached` ist der zweite Weg, den CS2 selbst anbietet, und er kostet
         * uns den Besitzer nicht: die Streu-Pfade in `SubObjectSystem`
         * (`RelocateSubObjects`) haengen am `SubObject`-Puffer des Besitzers und
         * verlangen zusaetzlich `m_OwnerData.HasComponent(subObject)`. Ohne
         * `Owner` ist ein Objekt in keinem solchen Puffer. Der Grund, aus dem
         * die Aufkleber keinen Besitzer haben, bleibt damit unberuehrt.
         *
         * Das Ziel ist der nackte Traeger. Er fuehrt `SubNet` und `SubObject`,
         * aber keine der vier Komponenten, nach denen AttachPositionSystem und
         * SubObjectSystem ihre Verschiebepfade waehlen. Seine `SubNet`-Kanten
         * werden beim Bau direkt aus denselben bereits materialisierten
         * Netzen gesetzt, die der fertige Plan erzeugt hat.
         */
        private int HefteObjekteAnTraeger()
        {
            if (!AufkleberAnTraegerHeften) return 0;
            if (_lotCarrier == Entity.Null || !EntityManager.Exists(_lotCarrier))
                return 0;
            var geheftet = 0;
            var vegetation = 0;
            using var entities = _tempObjectQuery.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!EntityManager.Exists(entity)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (!IsOwnObjectPrefab(prefab)) continue;
                if (ApplyVegetationAge(entity, prefab))
                {
                    SetVegetationOwner(entity,_lotCarrier,_lotOwner);
                    vegetation++;
                }
                EntityManager.AddComponentData(entity,
                    new Game.Objects.Attached(_lotCarrier, Entity.Null, 0f));
                EntityManager.AddComponentData(entity, new ParkingLotPartRelation
                {
                    Lot = _lotOwner,
                    Carrier = _lotCarrier,
                });
                geheftet++;
            }
            if (_vegetationCount > 0)
            {
                Mod.log.Info("PLT-Vorbauzettel Vegetation: native Pflanzen="
                    + vegetation + "/" + _vegetationCount
                    + "; Baumalter-Ziele=" + _vegetationTreeStates.Count
                    + "; Zustand GESETZT=" + _vegZustandGesetzt
                    + "; ohne Tree-Komponente=" + _vegOhneTreeKomponente
                    + "; ohne Ziel=" + _vegOhneZiel);
                if (vegetation != _vegetationCount) Mod.log.Warn("PLT-Vegetation: Abweichung zwischen Definitionen und erzeugten Pflanzen; siehe Vorbauzettel.");
            }
            return geheftet;
        }

        private static bool IsRecordedPrefab(List<PartTransferRecord> records,
                                             Entity prefab)
        {
            if (prefab == Entity.Null) return false;
            for (var i = 0; i < records.Count; i++)
                if (records[i].Prefab == prefab) return true;
            return false;
        }

        private int AttachByPrefab(EntityQuery query, Func<Entity, bool> matches)
        {
            var attached = 0;
            using var entities = query.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!EntityManager.Exists(entity)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (!matches(prefab)) continue;
                EntityManager.AddComponentData(entity, new Owner(_lotOwner));
                // Nur Kanten gehoeren in SubNet. Die Abfrage enthaelt auch die
                // Knoten eines Kurses; am Bau vom 2026-08-12 waren es deshalb
                // 98 Entities aus 34 Kursen. Die native SubNet-Liste fuehrt
                // dagegen die wirklichen Netzkanten.
                if (_lotCarrier != Entity.Null
                    && EntityManager.Exists(_lotCarrier)
                    && EntityManager.HasComponent<Game.Net.Edge>(entity))
                    AddCarrierSubNet(entity);
                attached++;
            }
            return attached;
        }

        private void AddCarrierSubNet(Entity edge)
        {
            var buffer = EntityManager.GetBuffer<Game.Net.SubNet>(_lotCarrier);
            for (var i = 0; i < buffer.Length; i++)
                if (buffer[i].m_SubNet == edge) return;
            buffer.Add(new Game.Net.SubNet(edge));
        }

        /**
         * Baut den beim Speichern verlorenen nativen SubNet-Puffer vor dem
         * ersten Spielframe wieder auf.
         *
         * Quelle ist bewusst der SubNet-Puffer der in der eigenen Relation
         * genannten PLT-Flaeche. CS2 pflegt ihn ueber den bestehenden Owner
         * der Netzkanten selbst. Ein zweites eigenes Kantenregister koennte
         * dagegen veralten, falls CS2 spaeter eine Kante ersetzt. Der
         * Traegertest hat 109 -> 0 Eintraege am Traeger gemessen; SubObject
         * wurde aus Attached von CS2 selbst wieder aufgebaut.
         */
        private void RestoreCarrierSubNetsAfterLoad()
        {
            var query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            using var parts = query.ToEntityArray(Allocator.TempJob);
            if (parts.Length == 0)
            {
                // Auch Null wird gemeldet: sonst waere nach einem Save/Load
                // nicht unterscheidbar, ob wirklich keine Relation geladen
                // wurde oder dieser fruehe Callback gar nicht lief.
                Mod.log.Info("PLT-Laden: 0 gespeicherte Teilrelationen; "
                    + "kein Traeger-SubNet nachzufuellen.");
                return;
            }

            var lotsByCarrier = new Dictionary<Entity, Entity>();
            var invalidRelations = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                var relation = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(parts[i]);
                if (relation.Lot == Entity.Null || relation.Carrier == Entity.Null
                    || !EntityManager.Exists(relation.Lot)
                    || !EntityManager.Exists(relation.Carrier))
                {
                    invalidRelations++;
                    continue;
                }
                // Einmalige Save-Migration: native Neubewertung statt Overridden laufend zu entfernen.
                if (EntityManager.HasComponent<PrefabRef>(parts[i]) &&
                    EntityManager.HasComponent<PlantData>(EntityManager.GetComponentData<PrefabRef>(parts[i]).m_Prefab) &&
                    (!EntityManager.HasComponent<Owner>(parts[i]) || !EntityManager.HasComponent<Owner>(relation.Carrier)))
                {
                    SetVegetationOwner(parts[i],relation.Carrier,relation.Lot);
                    if (!EntityManager.HasComponent<Updated>(parts[i])) EntityManager.AddComponent<Updated>(parts[i]);
                }
                if (lotsByCarrier.TryGetValue(relation.Carrier, out var knownLot))
                {
                    if (knownLot != relation.Lot) invalidRelations++;
                    continue;
                }
                lotsByCarrier.Add(relation.Carrier, relation.Lot);
            }

            var restoredCarriers = 0;
            var restoredEdges = 0;
            var missingSources = 0;
            foreach (var pair in lotsByCarrier)
            {
                var carrier = pair.Key;
                var lot = pair.Value;
                var reference = new ParkingLotCarrierReference { Carrier = carrier };
                if (EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
                    EntityManager.SetComponentData(lot, reference);
                else
                    EntityManager.AddComponentData(lot, reference);
                if (!EntityManager.HasBuffer<Game.Net.SubNet>(lot))
                {
                    missingSources++;
                    continue;
                }
                if (!EntityManager.HasBuffer<Game.Net.SubNet>(carrier))
                    EntityManager.AddBuffer<Game.Net.SubNet>(carrier);
                if (!EntityManager.HasBuffer<Game.Objects.SubObject>(carrier))
                    EntityManager.AddBuffer<Game.Objects.SubObject>(carrier);

                var source = EntityManager.GetBuffer<Game.Net.SubNet>(lot, true);
                var target = EntityManager.GetBuffer<Game.Net.SubNet>(carrier);
                target.Clear();
                for (var i = 0; i < source.Length; i++) target.Add(source[i]);
                restoredCarriers++;
                restoredEdges += source.Length;
            }

            Mod.log.Info($"PLT-Laden: SubNet fuer {restoredCarriers} Traeger "
                + $"mit {restoredEdges} Netzkanten aus {parts.Length} "
                + "gespeicherten Teilrelationen gefuellt; "
                + $"{invalidRelations} ungueltige Relation(en), "
                + $"{missingSources} Flaeche(n) ohne SubNet-Quelle.");
        }

        /**
         * Gibt dem Parkplatz einen Namen im Infofenster.
         *
         * Ohne das stuende dort der Prefabname der Besitzerflaeche, also
         * "Pavement Surface 01". Die Stellplatzzahl steht bewusst IM Namen:
         * ein eigener Panel-Abschnitt (`AddMiddleSection`) braucht eine
         * React-Komponente in einem UI-Bundle, und PLT hat kein UI-Projekt.
         * Der Name kostet nichts und traegt dieselbe Information.
         */
        private void NameLotOwner(int stalls)
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner)) return;
            // Einmal beim Bauen gesetzt und im Spielstand gespeichert -
            // vorhandene Parkplaetze behalten also ihren Namen, auch wenn
            // spaeter die Sprache wechselt.
            var name = stalls == 1
                ? ParkingLotTexte.T("Parkplatz (1 Stellplatz)",
                    "Parking Lot (1 space)")
                : ParkingLotTexte.T($"Parkplatz ({stalls} Stellplätze)",
                    $"Parking Lot ({stalls} spaces)");
            try
            {
                /**
                 * `SetCustomName` macht ZWEI Dinge, und nur das erste geht aus
                 * einem Werkzeug heraus:
                 *
                 *   m_Names[entity] = name;                      <- klappt
                 *   m_EndFrameBarrier.CreateCommandBuffer();     <- wirft hier
                 *       "Trying to create EntityCommandBuffer when it's not
                 *       allowed!" - im Spiel gemessen am 2026-08-11.
                 *
                 * Weil das Woerterbuch VOR dem Wurf gefuellt wird und
                 * `GetName` genau dort zuerst nachsieht, stand der Name
                 * trotzdem schon im Fenster. Verloren ging nur die
                 * `CustomName`-Komponente - und damit haette kein Spielstand
                 * den Namen behalten. Die setzen wir deshalb selbst; als
                 * Werkzeugsystem duerfen wir strukturell aendern, wir tun es
                 * beim Besitzer ohnehin.
                 */
                var nameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
                // Bewusst JEDE Ausnahme: der erste Versuch fing nur
                // InvalidOperationException, und im Spiel kam eine andere Art
                // durch - die Methode brach dadurch ab, bevor sie die
                // CustomName-Komponente setzen konnte. Was wirklich gilt,
                // sagt gleich darunter TryGetCustomName; deshalb ist
                // Schlucken hier sicher und nicht blind.
                try { nameSystem.SetCustomName(_lotOwner, name); }
                catch (Exception) { }

                if (!EntityManager.HasComponent<Game.UI.CustomName>(_lotOwner))
                    EntityManager.AddComponent<Game.UI.CustomName>(_lotOwner);
                if (!EntityManager.HasComponent<BatchesUpdated>(_lotOwner))
                    EntityManager.AddComponent<BatchesUpdated>(_lotOwner);

                var applied = nameSystem.TryGetCustomName(_lotOwner, out var stored)
                    && stored == name;
                if (applied) Mod.log.Info($"PLT-Name gesetzt: \"{name}\".");
                else Mod.log.Warn("PLT-Name wurde nicht übernommen; im Infofenster "
                    + "steht weiter der Prefabname.");
            }
            catch (Exception exception)
            {
                // Ein fehlender Name ist ein Schoenheitsfehler, kein Grund,
                // den fertigen Parkplatz nicht zu bauen.
                Mod.log.Warn($"PLT konnte den Parkplatz nicht benennen: "
                    + exception.Message);
            }
        }
    }
}
