using Colossal.Serialization.Entities;
using Game;
using Game.Economy;
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
                // Der geerbte ServiceConsumption-Unterhalt des Quellprefabs
                // (gemessene 16.000) wird auf null gesetzt; der PLT-Unterhalt
                // kommt allein aus dem ServiceUpkeepItem weiter unten.
                if (companion.TryGet<ServiceConsumption>(out var consumption))
                {
                    consumption.m_Upkeep = 0;
                    consumption.m_WaterConsumption = 0;
                    consumption.m_GarbageAccumulation = 0;
                    consumption.m_TelecomNeed = 0f;
                }

                /*
                 * DER UNTERHALT SITZT AM BEGLEITER, NICHT AN DER FLAECHE.
                 *
                 * Bis zum 2026-09-14 trug die Parkplatzflaeche selbst den
                 * Unterhalt. Das hat CS2 zuverlaessig zum Absturz gebracht,
                 * und zwar aus einem Grund, der nichts mit Geld zu tun hat:
                 *
                 * `CityServiceUpkeepSystem` nimmt nur Entities in seine
                 * Abfrage auf, die eine `UpdateFrame` tragen. Also haben wir
                 * der Flaeche eine angehaengt. `UpdateGroupSystem` sammelt
                 * aber JEDE Entity mit `UpdateFrame` ein, sobald sie `Created`
                 * oder `Deleted` ist, und fragt sie nach ihrer Art. Es kennt
                 * Fahrzeuge, Baeume, Gebaeude, Netzknoten, Kanten, Spuren,
                 * Firmen, Haushalte, Buerger und Haustiere - und sonst nichts.
                 * Eine Flaeche ist nichts davon.
                 *
                 * Was dann passiert, steht in `UpdateGroupSystem`:
                 *
                 *     NativeArray<int> result = m_UpdateGroupSizes.Get(...);
                 *     if (!result.IsCreated)
                 *     {
                 *         ... GetComponentTypes();
                 *         UnityEngine.Debug.Log("UpdateFrame added to "
                 *             + "unsupported type");
                 *         for (...) UnityEngine.Debug.Log($"Component: ...");
                 *     }
                 *
                 * Das laeuft in einem Burst-Job auf einem Arbeitsthread. 38
                 * Log-Zeilen aus einem Job heraus bringen Mono um.
                 *
                 * GEMESSEN AM 2026-09-14: Player.log endet mit genau dieser
                 * Meldung und 37 Komponentenzeilen, dann nativer Absturz ohne
                 * managed Stacktrace. Die abgerissene Flaeche des Nutzers hatte
                 * 36 Komponenten; mit `Deleted` sind es 37. Dieselbe Entity.
                 *
                 * Deshalb zahlt jetzt der Begleiter. Er ist ein echtes
                 * `Building`, steht in der Liste oben drin, traegt seine
                 * `UpdateFrame` seit jeher unauffaellig - und es gibt ihn
                 * ohnehin fuer jeden Parkplatz.
                 *
                 * An der Flaeche bleibt alles andere, wie es war: `Efficiency`
                 * haengt in `Game.Prefabs.ParkingFacility` daran, dass hier
                 * ein `CityServiceBuilding` steht, und `TripNeeded`,
                 * `GuestVehicle` und `OwnedVehicle` kommen aus demselben
                 * Bauteil. Die Flaeche behaelt also ihren Archetyp samt
                 * ungenutztem `CityServiceUpkeep` - das ist billiger als vier
                 * Komponenten anzufassen, deren Nutzer man erst suchen muss.
                 *
                 * Der Basisbetrag ist derselbe wie vorher an der Flaeche; die
                 * Instanz setzt den Faktor ueber `ServiceUsage`.
                 */
                companion.Remove<CityServiceBuilding>();
                var begleiterWirtschaft =
                    companion.AddComponent<CityServiceBuilding>();
                begleiterWirtschaft.m_Upkeeps = new[]
                {
                    new ServiceUpkeepItem
                    {
                        m_Resources = new ResourceStackInEditor
                        {
                            m_Resource = ResourceInEditor.Money,
                            m_Amount = ParkingLotEconomySystem.UpkeepBasis,
                        },
                        m_ScaleWithUsage = true,
                    },
                };
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
