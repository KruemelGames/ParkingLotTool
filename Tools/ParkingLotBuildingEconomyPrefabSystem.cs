using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Registriert das Begleiter-Prefab in CS2s eigener Prefabphase. */
    public sealed partial class ParkingLotBuildingEconomyPrefabSystem
        : GameSystemBase
    {
        private EntityQuery _sources;
        private PrefabSystem _prefabSystem;
        private int _attempts;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _sources = GetEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<PollutionData>(),
                ComponentType.ReadOnly<DestructibleObjectData>(),
                ComponentType.ReadOnly<ObjectGeometryData>(),
                ComponentType.ReadOnly<ConsumptionData>(),
                ComponentType.ReadOnly<WorkplaceData>(),
                ComponentType.ReadOnly<DefaultPolicyData>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            _attempts++;
            using var sources = _sources.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (!_prefabSystem.TryGetPrefab<BuildingPrefab>(source,
                        out var value)
                    || value == null
                    || value.name != ParkingLotBuildingEconomySystem
                        .CompanionSourcePrefabName)
                    continue;

                var companion = (BuildingPrefab)value.Clone(
                    ParkingLotBuildingEconomySystem.CompanionPrefabName);
                companion.m_LotWidth = 1;
                companion.m_LotDepth = 1;
                /*
                 * Nur der Klon bekommt die beidseitige Terraform-Sperre.
                 * BuildingInitializeSystem uebertraegt diese beiden Flags in
                 * BuildingTerraformData. Weitere Override-Felder bleiben
                 * unangetastet; hier wird insbesondere kein Mass erfunden.
                 */
                var terraform = companion
                    .AddOrGetComponent<BuildingTerraformOverride>();
                terraform.m_DontRaise = true;
                terraform.m_DontLower = true;
                /*
                 * Der volle Building-Archetyp enthaelt immer `Object`. Deshalb
                 * muessen neben den Unterteilen auch die Hauptmeshes vor
                 * AddPrefab weg; nur dann erzeugt CS2 einen leeren SubMesh-
                 * Puffer statt eines sichtbaren ParkingLot04-Koerpers.
                 */
                companion.m_Meshes = new ObjectMeshInfo[0];

                // Nur Strom, Arbeitsplatz und Laerm gehoeren zum Begleiter.
                // CityServiceBuilding wuerde den bestaetigten PLT-Unterhalt um
                // die gemessenen 16.000 des Quellprefabs erhoehen.
                if (companion.TryGet<ServiceConsumption>(out var consumption))
                {
                    consumption.m_Upkeep = 0;
                    consumption.m_WaterConsumption = 0;
                    consumption.m_GarbageAccumulation = 0;
                    consumption.m_TelecomNeed = 0f;
                }
                companion.Remove<CityServiceBuilding>();
                companion.Remove<Game.Prefabs.ParkingFacility>();
                companion.Remove<DefaultPolicies>();
                companion.Remove<EffectSource>();
                companion.Remove<UIObject>();
                companion.Remove<ObjectSubObjects>();
                companion.Remove<ObjectSubNets>();
                companion.Remove<ObjectSubLanes>();
                companion.Remove<ObjectSubAreas>();
                if (!_prefabSystem.AddPrefab(companion))
                {
                    UnityEngine.Object.Destroy(companion);
                    Mod.log.Error("PLT konnte das BuildingPrefab des Begleiters "
                        + "nicht registrieren.");
                    Enabled = false;
                    return;
                }

                Mod.log.Info("PLT-Begleiter-Prefab registriert: '"
                    + ParkingLotBuildingEconomySystem.CompanionPrefabName
                    + "' aus typkorrektem Rezept '"
                    + ParkingLotBuildingEconomySystem.CompanionSourcePrefabName
                    + "' in PrefabUpdate vor BuildingInitializeSystem; "
                    + "Hauptmeshes 0, Unterteile 0, eigener Unterhalt 0, Versuch "
                    + _attempts + ", Terraform DontRaise/DontLower gesetzt.");
                Enabled = false;
                return;
            }

            if (_attempts == 8 || _attempts == 32)
                Mod.log.Warn("PLT-Begleiter-Prefab wartet in PrefabUpdate auf '"
                    + ParkingLotBuildingEconomySystem.CompanionSourcePrefabName
                    + "' (Versuch " + _attempts + ").");
        }
    }

    /** Kennzeichnet ausschliesslich den unsichtbaren Wirtschafts-Begleiter. */
    public struct ParkingLotBuildingEconomyEnabled : IComponentData,
        IQueryTypeParameter, IEmptySerializable
    {
    }
}
