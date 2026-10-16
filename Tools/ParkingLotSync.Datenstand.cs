using Game.Common;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotSyncSystem
    {
        private int StandVon(Entity lot)
            => EntityManager.Exists(lot) && EntityManager.HasComponent<ParkingLotDatenstand>(lot)
                ? System.Math.Max(0, EntityManager.GetComponentData<ParkingLotDatenstand>(lot).Stand)
                : 0;

        private void SetzeStand(Entity lot, int stand)
        {
            if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)
                || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return;
            var wert = new ParkingLotDatenstand { Stand = stand };
            if (EntityManager.HasComponent<ParkingLotDatenstand>(lot))
                EntityManager.SetComponentData(lot, wert);
            else EntityManager.AddComponentData(lot, wert);
        }

    }
}
