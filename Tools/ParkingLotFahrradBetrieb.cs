using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /** Nur Laufzeit-Metadaten am EIGENEN Prefab; kein neues Instanz-Speicherformat. */
    internal struct ParkingLotFahrradLayout : IComponentData
    {
        public Fahrradlayout Layout;
    }

    internal static partial class ParkingLotFahrradBetrieb
    {
        internal static bool TryLayout(EntityManager em, Entity lot, out Fahrradlayout layout)
        {
            layout = default;
            if (lot == Entity.Null || !em.Exists(lot) || !em.HasComponent<PrefabRef>(lot)) return false;
            var prefab = em.GetComponentData<PrefabRef>(lot).m_Prefab;
            if (prefab == Entity.Null || !em.Exists(prefab) || !em.HasComponent<ParkingLotFahrradLayout>(prefab)) return false;
            layout = em.GetComponentData<ParkingLotFahrradLayout>(prefab).Layout;
            return true;
        }

        internal static bool Ist(EntityManager em, Entity lot) => TryLayout(em, lot, out _);

        internal static int Unterhalt(EntityManager em, Entity lot, int kapazitaet)
            => TryLayout(em, lot, out var layout) ? FahrradBetrieb.Unterhalt(layout, kapazitaet)
                : ParkingLotEconomySystem.CalculateUpkeep(kapazitaet);
    }
}
