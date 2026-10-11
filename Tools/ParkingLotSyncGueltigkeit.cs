using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotSyncGueltigkeit
    {
        internal static bool Bestand(EntityManager em, Entity lot, Entity traeger)
            => lot != Entity.Null && traeger != Entity.Null && em.Exists(lot) && em.Exists(traeger)
               && !em.HasComponent<Deleted>(lot) && !em.HasComponent<Temp>(lot)
               && !em.HasComponent<Deleted>(traeger) && !em.HasComponent<Temp>(traeger)
               && em.HasComponent<ParkingLotCarrierReference>(lot)
               && em.GetComponentData<ParkingLotCarrierReference>(lot).Carrier == traeger;
    }
}
