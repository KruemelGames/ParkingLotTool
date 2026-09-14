using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Gespeicherter Sollwert einer gebauten Parkplatzinstanz.
     *
     * Die Kapazitaet stammt einmalig aus den wirklichen Parkspuren. Danach
     * kann der Unterhalt auch in einem Ladeframe wiederhergestellt werden, in
     * dem diese Spurhierarchie noch nicht vollstaendig aufgebaut ist.
     */
    public struct ParkingLotEconomyData : IComponentData,
                                          IQueryTypeParameter,
                                          ISerializable
    {
        // In alten Spielstaenden fehlt dieser Wert. -1 bedeutet deshalb:
        // beim ersten Wirtschaftslauf aus dem aktuellen Baustandard fuellen.
        public const int UninitializedParkingFee = -1;

        private const int PackedMarker = unchecked((int)0x80000000u);
        private const int PackedCapacityMask = 0x00ffffff;
        private const int PackedFeeShift = 24;

        public int Capacity;
        public int Upkeep;
        public int ParkingFee;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            /*
             * KOMPATIBEL ZU BEREITS GEBAUTEN PARKPLAETZEN.
             *
             * Der Serializer legt alle Instanzen dieser Komponente ohne
             * Einzelblock hintereinander. Ein drittes geschriebenes int
             * wuerde alte Spielstaende deshalb nicht erweitern, sondern den
             * folgenden Datensatz verschieben. Kapazitaet braucht real nur
             * einen Bruchteil von 24 Bit (PLT plant hoechstens 750 Buchten;
             * im Spiel wurden auch 1.216 gemessen). Die oberen sieben Bit
             * tragen daher die Gebuehr, das Vorzeichenbit kennzeichnet das
             * neue Format. Der Unterhalt bleibt unveraendert das zweite int.
             */
            var fee = Unity.Mathematics.math.clamp(ParkingFee, 0, 127);
            var packedCapacity = Capacity >= 0 && Capacity <= PackedCapacityMask
                ? PackedMarker | (fee << PackedFeeShift) | Capacity
                : Capacity;
            writer.Write(packedCapacity);
            writer.Write(Upkeep);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int packedCapacity);
            reader.Read(out Upkeep);
            if (packedCapacity < 0)
            {
                Capacity = packedCapacity & PackedCapacityMask;
                ParkingFee = (packedCapacity >> PackedFeeShift) & 0x7f;
            }
            else
            {
                Capacity = packedCapacity;
                ParkingFee = UninitializedParkingFee;
            }
        }
    }

    /**
     * Skaliert den einen Prefab-Unterhalt je gebauter PLT-Instanz.
     *
     * `RequiredComponentSystem` ergaenzt beim Laden fehlendes ServiceUsage
     * mit 1. Deshalb kontrolliert dieses System den gespeicherten Sollwert bei
     * jedem Lauf und setzt eine fehlende oder abweichende Komponente zurueck.
     * `UpdateFrame` ist ebenfalls Pflicht: ohne diese Shared Component nimmt
     * `CityServiceUpkeepSystem` die Instanz gar nicht in seine Abfrage auf.
     */
    public sealed partial class ParkingLotEconomySystem : GameSystemBase
    {
        // 65536 ist eine Zweierpotenz. Fuer alle maximal 750 Buchten ist
        // Soll/Basis in float exakt rueckmultiplizierbar: max. 36.438 < 2^24.
        // Zugleich bleibt ServiceUsage im ueblichen Bereich von 0 bis 1.
        internal const int UpkeepBasis = 65536;

        private EntityQuery _lots;
        private EntityQuery _parkingPrefabSources;
        private PrefabSystem _prefabSystem;
        private Entity _roadsService = Entity.Null;

        // Je Parkplatz hoechstens eine Meldung, siehe RestoreRuntimeComponents.
        private bool? _zuletztAn;

        private readonly System.Collections.Generic.Dictionary<Entity, int>
            _stilleDurchgaenge =
                new System.Collections.Generic.Dictionary<Entity, int>();

        private readonly System.Collections.Generic.HashSet<Entity>
            _archetypBemaengelt = new System.Collections.Generic.HashSet<Entity>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            _parkingPrefabSources = GetEntityQuery(
                ComponentType.ReadOnly<ParkingFacilityData>(),
                ComponentType.ReadOnly<ServiceObjectData>());
            RequireForUpdate(_lots);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            /*
             * Den Wechsel MELDEN, nicht nur befolgen. Am 2026-08-27 liess sich
             * aus dem Log nicht ablesen, ob dieses System den Schalter
             * ueberhaupt gesehen hat - der Begleiter meldete seinen Stand, der
             * Unterhalt nicht. Zwei Systeme am selben Schalter, und nur eines
             * sagt etwas: dann sucht man beim naechsten Mal wieder im Dunkeln.
             */
            var an = Mod.WirtschaftAn;
            if (_zuletztAn != an)
            {
                _zuletztAn = an;
                Mod.log.Info("PLT-Wirtschaft (Unterhalt und Gebuehr): "
                    + (an ? "EIN" : "AUS") + ".");
            }
            if (!an)
            {
                Abschalten();
                return;
            }
            if (!TryResolveRoadsService()) return;
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
                UpdateLot(lots[i]);
        }

        /**
         * WIRTSCHAFT AUS: WIRKUNG WEG, WERTE BLEIBEN.
         *
         * `ParkingLotEconomyData` wird bewusst NICHT entfernt. Dort steht die
         * am einzelnen Parkplatz eingestellte Gebuehr, und die soll beim
         * Wiedereinschalten zurueckkommen - ausdruecklicher Wunsch des
         * Nutzers am 2026-08-26. Abgeschaltet wird nur die Wirkung: ohne
         * Nutzungsanteil rechnet `CityServiceUpkeepSystem` keinen Unterhalt.
         *
         * Ebenso bewusst wird nichts an- oder abgehaengt. Ein Schalter, den
         * man im laufenden Spiel umlegt, darf keine Strukturaenderung
         * ausloesen - daran ist heute schon genug abgestuerzt. Eine Zahl auf
         * null zu setzen ist die harmloseste Fassung derselben Wirkung.
         *
         * Parkplaetze, die im Aus-Zustand gebaut werden, bekommen hier gar
         * keine Wirtschaftsdaten. Genau deshalb erhalten sie beim Einschalten
         * den aktuellen Baustandard - "weil ja keine vorhanden waren".
         */
        private void Abschalten()
        {
            if (_lots.IsEmptyIgnoreFilter) return;
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            var gestoppt = 0;
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                if (!EntityManager.HasComponent<ServiceUsage>(lot)) continue;
                var usage = EntityManager.GetComponentData<ServiceUsage>(lot);
                if (usage.m_Usage == 0f) continue;
                usage.m_Usage = 0f;
                EntityManager.SetComponentData(lot, usage);
                gestoppt++;
            }
            if (gestoppt > 0)
                Mod.log.Info("PLT-Wirtschaft aus: Unterhalt an " + gestoppt
                    + " Parkplaetzen auf null gesetzt. Die eingestellten "
                    + "Gebuehren bleiben gespeichert.");
        }

        /**
         * Geldunterhalt wird im Budgetsystem einem Dienst zugeordnet. Der
         * skalierte ServiceUpkeepData-Eintrag allein reicht fuer die Anzeige,
         * aber ohne ServiceObjectData nicht fuer die echte Stadtausgabe.
         */
        private bool TryResolveRoadsService()
        {
            if (_roadsService != Entity.Null) return true;
            using var sources = _parkingPrefabSources.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(source, out var prefab)
                    || prefab == null || prefab.name != "ParkingLot01")
                    continue;
                _roadsService = EntityManager
                    .GetComponentData<ServiceObjectData>(source).m_Service;
                if (_roadsService != Entity.Null)
                {
                    Mod.log.Info("PLT-Wirtschaft: Roads-Dienst aus "
                        + "ParkingLot01 aufgeloest.");
                    return true;
                }
            }
            return false;
        }

        private void UpdateLot(Entity lot)
        {
            var prefab = EntityManager.GetComponentData<PrefabRef>(lot).m_Prefab;
            if (!EnsureServiceObject(prefab))
            {
                Melde(lot, "das Lot-Prefab taugt nicht als Dienstobjekt");
                return;
            }

            ParkingLotEconomyData economy;
            if (EntityManager.HasComponent<ParkingLotEconomyData>(lot))
            {
                economy = EntityManager.GetComponentData<ParkingLotEconomyData>(lot);
            }
            else
            {
                var lanes = MesseKapazitaet(lot, out var capacity);
                // Direkt nach dem Bau und beim fruehen Laden koennen die
                // referenzierten Spuren noch fehlen. Dann spaeter erneut
                // messen, statt einen falschen Null-Parkplatz zu speichern.
                if (capacity <= 0)
                {
                    Melde(lot, "es sind keine Parkspuren zu finden");
                    return;
                }
                _stilleDurchgaenge.Remove(lot);

                economy = new ParkingLotEconomyData
                {
                    Capacity = capacity,
                    Upkeep = CalculateUpkeep(capacity),
                    ParkingFee = UnityEngine.Mathf.Clamp(
                        Mod.Optionen?.Parkgebuehr ?? 10, 0, 50),
                };
                EntityManager.AddComponentData(lot, economy);
                Mod.log.Info("PLT-Wirtschaft: Lot " + lot.Index + " aus "
                    + lanes + " echten Parkspuren gemessen: " + capacity
                    + " Plaetze, Prefab-Unterhalt " + economy.Upkeep
                    + ", Auswahlfenster monatlich " + (economy.Upkeep / 2)
                    + ".");
            }

            // Parkplaetze aus der vorigen Fassung tragen im alten
            // Acht-Byte-Format noch keine Gebuehr. Sie erhalten einmalig den
            // aktuellen Baustandard; danach ist der Wert Teil der Instanz.
            if (economy.ParkingFee == ParkingLotEconomyData.UninitializedParkingFee)
            {
                economy.ParkingFee = UnityEngine.Mathf.Clamp(
                    Mod.Optionen?.Parkgebuehr ?? 10, 0, 50);
                EntityManager.SetComponentData(lot, economy);
                Mod.log.Info("PLT-Wirtschaft: Lot " + lot.Index
                    + " aus altem Speicherformat mit Parkgebuehr "
                    + economy.ParkingFee + " vorbelegt.");
            }

            RestoreRuntimeComponents(lot, economy);
        }

        /**
         * Eicht ein gerade dauerhaft gewordenes Ersatz-Lot aus seinen echten
         * Parkspuren und setzt danach ausschliesslich die alte Parkgebuehr.
         * Kapazitaet und Unterhalt werden absichtlich nicht vom alten Lot
         * kopiert: genau diese beiden Werte sollen dem neuen Zuschnitt folgen.
         *
         * Der Weg laeuft auch bei ausgeschaltetem Hauptschalter. So bleibt die
         * Gebuehr im gespeicherten Datensatz erhalten und ist beim spaeteren
         * Einschalten wieder da, ohne bereits Unterhalt zu aktivieren.
         */
        internal bool TryInitializeReplacement(Entity lot, int parkingFee)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || EntityManager.HasComponent<Deleted>(lot)
                || EntityManager.HasComponent<Temp>(lot)) return false;

            var lanes = MesseKapazitaet(lot, out var capacity);
            if (capacity <= 0) return false;
            var economy = new ParkingLotEconomyData
            {
                Capacity = capacity,
                Upkeep = CalculateUpkeep(capacity),
                ParkingFee = UnityEngine.Mathf.Clamp(parkingFee, 0, 50),
            };
            if (EntityManager.HasComponent<ParkingLotEconomyData>(lot))
                EntityManager.SetComponentData(lot, economy);
            else EntityManager.AddComponentData(lot, economy);

            if (Mod.WirtschaftAn && TryResolveRoadsService())
                RestoreRuntimeComponents(lot, economy);
            Mod.log.Info("PLT-Wirtschaft: Ersatz-Lot " + lot.Index + " aus "
                + lanes + " echten Parkspuren neu geeicht: " + capacity
                + " Plätze, Unterhalt " + economy.Upkeep + ", Parkgebühr "
                + economy.ParkingFee + ".");
            return true;
        }

        /**
         * MESSEN GEHT UEBER ZWEI WEGE, NICHT NUR UEBER DIE FLAECHE.
         *
         * `VehicleUtils.GetParkingData` folgt `SubLane`, `SubNet` und
         * `SubObject` der uebergebenen Entity. Unsere Parkspuren haengen aber
         * am nackten Traeger - "Traeger-SubNet: 32 Netzkanten" steht so im
         * Bauprotokoll. Beim frisch gebauten Parkplatz findet die Flaeche sie
         * trotzdem; steht der Bau eine Weile zurueck, nicht mehr zuverlaessig.
         *
         * Am 2026-08-27 kostete genau das einen Parkplatz seine Wirtschaft: mit
         * ausgeschalteter Wirtschaft gebaut, danach eingeschaltet - und die
         * Messung fand 80 Sekunden lang nichts, weil nur die Flaeche befragt
         * wurde. Deshalb wird jetzt auch der Traeger gefragt und der groessere
         * Wert genommen; doppelt gezaehlt wird dabei nichts.
         */
        private int MesseKapazitaet(Entity lot, out int capacity)
        {
            var lanes = 0;
            capacity = 0;
            var parked = 0;
            var fee = 0;
            Game.Vehicles.VehicleUtils.GetParkingData(
                this, lot, ref lanes, ref capacity, ref parked, ref fee);
            if (capacity > 0) return lanes;

            if (!EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
                return lanes;
            var traeger = EntityManager
                .GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (traeger == Entity.Null || !EntityManager.Exists(traeger))
                return lanes;

            var lanes2 = 0;
            var capacity2 = 0;
            Game.Vehicles.VehicleUtils.GetParkingData(
                this, traeger, ref lanes2, ref capacity2, ref parked, ref fee);
            if (capacity2 <= capacity) return lanes;
            capacity = capacity2;
            return lanes2;
        }

        /**
         * ZAEHLER AN JEDER STILLEN ABBRUCHSTELLE.
         *
         * Ohne sie hat am 2026-08-27 ein Parkplatz einfach keine Wirtschaft
         * bekommen, und im Log stand dazu nichts - weder ein Grund noch ein
         * Hinweis, dass ueberhaupt etwas uebersprungen wurde. Erst der
         * Vergleich zweier Parkplaetze im Protokoll hat es sichtbar gemacht.
         *
         * Gemeldet wird spaet und einmal: die ersten Durchgaenge nach dem Bau
         * sind planmaessig ergebnislos, weil die Spuren noch entstehen.
         */
        private const int StilleDurchgaengeBisMeldung = 600;

        private void Melde(Entity lot, string grund)
        {
            _stilleDurchgaenge.TryGetValue(lot, out var zahl);
            zahl++;
            _stilleDurchgaenge[lot] = zahl;
            if (zahl != StilleDurchgaengeBisMeldung) return;
            Mod.log.Warn("PLT-Wirtschaft: Lot " + lot.Index + " bekommt seit "
                + zahl + " Durchgaengen keine Wirtschaftsdaten - " + grund
                + ".");
        }

        private bool EnsureServiceObject(Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.Exists(prefab))
                return false;
            // Shift+P kann den Lot-Besitzer fuer einen Diagnosebau abschalten.
            // Dann verweist die fachliche Wurzel auf ein fremdes Surface-
            // Prefab. Daran darf die Wirtschaft unter keinen Umstaenden
            // ServiceObjectData anbringen und damit alle Surface-Instanzen
            // des Spiels veraendern.
            if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var lotPrefab)
                || lotPrefab == null
                || lotPrefab.name != ParkingLotToolSystem.LotOwnerPrefabName)
                return false;
            if (!EntityManager.HasComponent<CollectedServiceBuildingBudgetData>(
                    prefab))
            {
                Mod.log.Error("PLT-Wirtschaft: dem Lot-Prefab fehlt "
                    + "CollectedServiceBuildingBudgetData aus "
                    + "CityServiceBuilding.");
                return false;
            }
            if (!EntityManager.HasComponent<ServiceObjectData>(prefab))
            {
                EntityManager.AddComponentData(prefab,
                    new ServiceObjectData { m_Service = _roadsService });
                Mod.log.Info("PLT-Wirtschaft: einzelnes Lot-Prefab dem "
                    + "Roads-Dienst zugeordnet.");
            }
            return true;
        }

        private void RestoreRuntimeComponents(Entity lot,
                                               ParkingLotEconomyData economy)
        {
            if (!EntityManager.HasComponent<CityServiceUpkeep>(lot)
                || !EntityManager.HasBuffer<Game.Economy.Resources>(lot))
            {
                /*
                 * Diese beiden Typen muessen aus CityServiceBuilding im
                 * Prefab-Archetyp kommen. Nachtraegliches Ankleben wuerde den
                 * eigentlichen Initialisierungsfehler nur verdecken.
                 *
                 * DER REGELFALL IST HARMLOS: Parkplaetze aus einem Spielstand,
                 * der VOR dieser Fassung gebaut wurde, tragen den alten
                 * Archetyp und koennen ihn nicht mehr bekommen - der Archetyp
                 * entsteht einmalig beim Anmelden des Prefabs. Sie bleiben
                 * ohne Unterhalt, und daran ist nichts zu reparieren.
                 *
                 * Deshalb nur EINMAL je Parkplatz melden. Dieses System laeuft
                 * in jedem Simulationsframe; ohne die Sperre schriebe ein
                 * einziger alter Parkplatz das Log zu.
                 */
                if (_archetypBemaengelt.Add(lot))
                {
                    Mod.log.Warn("PLT-Wirtschaft: Lot " + lot.Index
                        + " hat den alten Archetyp ohne CityServiceUpkeep "
                        + "und bleibt ohne Unterhalt. Vor dieser Fassung "
                        + "gebaut - neu bauen, dann rechnet er mit.");
                }
                return;
            }

            var usage = (float)economy.Upkeep / UpkeepBasis;
            if (!EntityManager.HasComponent<ServiceUsage>(lot))
            {
                EntityManager.AddComponentData(lot,
                    new ServiceUsage { m_Usage = usage });
                Mod.log.Info("PLT-Wirtschaft: ServiceUsage an Lot "
                    + lot.Index + " gesetzt (" + economy.Upkeep + ").");
            }
            else
            {
                var current = EntityManager.GetComponentData<ServiceUsage>(lot);
                if (current.m_Usage != usage)
                {
                    current.m_Usage = usage;
                    EntityManager.SetComponentData(lot, current);
                    Mod.log.Info("PLT-Wirtschaft: ServiceUsage an Lot "
                        + lot.Index + " nach dem Laden korrigiert ("
                        + economy.Upkeep + ").");
                }
            }

            if (!EntityManager.HasComponent<UpdateFrame>(lot))
            {
                EntityManager.AddSharedComponent(lot,
                    new UpdateFrame((uint)lot.Index & 0xFu));
                Mod.log.Info("PLT-Wirtschaft: UpdateFrame an Lot "
                    + lot.Index + " gesetzt.");
            }
        }

        internal static int CalculateUpkeep(int capacity)
            => 48 * capacity + 438;
    }
}
