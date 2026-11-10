using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotEconomySystem
    {
        /** Auch bei Wirtschaft AUS muss die Liste echte Fahrradplaetze kennen.
         *  Keine Abrechnung/kein Begleiter: nur einmalig vorhandene Spuren messen. */
        private void MesseFahrradOhneWirtschaft()
        {
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            var leer = new System.Collections.Generic.Dictionary<Entity, Entity>();
            foreach (var lot in lots)
                if (ParkingLotFahrradBetrieb.Ist(EntityManager, lot)
                    && !EntityManager.HasComponent<ParkingLotEconomyData>(lot)) UpdateLot(lot, leer);
        }
    }
}
