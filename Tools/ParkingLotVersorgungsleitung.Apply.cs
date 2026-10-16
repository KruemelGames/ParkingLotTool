using System.Collections.Generic;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private static IEnumerable<Entity> Beide(AvKurs kurs)
        {
            foreach (var e in kurs.Kanten) yield return e;
            foreach (var e in kurs.Anschlussstuecke) yield return e;
        }

        private void MarkiereVersorgungVorApply(Entity lot, Entity carrier)
        {
            foreach (var kurs in _avKurse)
                foreach (var e in Beide(kurs))
                    if (!EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e)
                        || EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot != lot
                        || EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e).Carrier != carrier)
                        throw new System.InvalidOperationException("Leitung vor Apply ohne belegte PLT-Herkunft: " + e);
        }

    }
}
