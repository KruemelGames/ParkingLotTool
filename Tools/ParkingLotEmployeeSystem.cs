using Game;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Legt echte, vom Vanilla-Arbeitsmarkt besetzbare Arbeitsplaetze an.
     *
     * WorkProvider und Employee stehen gemeinsam auf derselben Building-
     * Entity. Damit nimmt WorkProviderSystem den Begleiter in seine normale
     * Abfrage auf und erzeugt FreeWorkplaces beziehungsweise echte Worker.
     */
    public sealed partial class ParkingLotEmployeeSystem : GameSystemBase
    {
        private EntityQuery _companions;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _companions = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                ComponentType.ReadOnly<ParkingLotPartRelation>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Angestellte);
            if (!Mod.WirtschaftAn
                || _companions.IsEmptyIgnoreFilter) return;
            using var companions = _companions.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < companions.Length; i++)
                UpdateCompanion(companions[i]);
        }

        private void UpdateCompanion(Entity companion)
        {
            var relation = EntityManager
                .GetComponentData<ParkingLotPartRelation>(companion);
            if (relation.Lot == Entity.Null || !EntityManager.Exists(relation.Lot)
                || !EntityManager.HasComponent<ParkingLotEconomyData>(relation.Lot))
                return;
            var economy = EntityManager
                .GetComponentData<ParkingLotEconomyData>(relation.Lot);
            var workers = CalculateEmployees(economy.Capacity);

            if (workers == 0)
            {
                // Kleine Anlagen haben nach der gemessenen Stufe keinen
                // Abschnitt. Es gibt daher auch keine 0-von-N-Attrappe.
                if (EntityManager.HasComponent<WorkProvider>(companion))
                {
                    var provider = EntityManager
                        .GetComponentData<WorkProvider>(companion);
                    provider.m_MaxWorkers = 0;
                    EntityManager.SetComponentData(companion, provider);
                }
                return;
            }

            if (!EntityManager.HasBuffer<Employee>(companion))
                EntityManager.AddBuffer<Employee>(companion);
            if (!EntityManager.HasComponent<WorkProvider>(companion))
            {
                EntityManager.AddComponentData(companion, new WorkProvider
                {
                    m_MaxWorkers = workers,
                });
                Mod.log.Info("PLT-Begleiter " + companion.Index + " fuer Lot "
                    + relation.Lot.Index
                    + " mit " + workers + " echten Arbeitsplaetzen.");
            }
            else
            {
                var provider = EntityManager
                    .GetComponentData<WorkProvider>(companion);
                if (provider.m_MaxWorkers != workers)
                {
                    provider.m_MaxWorkers = workers;
                    EntityManager.SetComponentData(companion, provider);
                }
            }
        }

        internal static int CalculateEmployees(int capacity)
            => capacity <= 40 ? 0 : capacity < 300 ? 2 : 4;
    }
}
