// Diese Datei ist PLT-Projektcode unter GPL-3.0 und verwendet Harmony 2.2.2
// als Bibliotheksabhaengigkeit. Es wurde kein Spiel- oder Harmony-Quellcode
// uebernommen. Herkunft und MIT-Lizenz der Bibliothek stehen vollstaendig in
// `ParkingLotRaycastPatch.cs` sowie unter `Library/Harmony/`.

using System;
using System.Reflection;
using Game;
using Game.Vehicles;
using HarmonyLib;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Laesst Vanilla die Parkplatzbelegung vom technischen Traeger lesen.
     *
     * Gemessen im Spiel am 2026-08-25: Die Simulation fand auf einem PLT-Lot
     * 611 echte Parkspuren und 354 Fahrzeuge, das Auswahlfenster zeigte 0.
     * Der lokale Abzug von `VehicleUtils.GetParkingData` belegt den Grund: Die
     * Methode folgt nur `SubLane`, `SubNet` und `SubObject` der uebergebenen
     * Entity. Diese Puffer liegen fuer die Aufkleber seit Umbau 1 am Traeger.
     *
     * Der Prefix tauscht nur fuer eine PLT-Flaeche mit gueltigem eigenen
     * Verweis das Start-Entity aus. Vanilla rechnet Kapazitaet und Belegung
     * danach selbst aus den echten Spuren; es werden keine Zahlen erfunden.
     */
    [HarmonyPatch]
    internal static class ParkingLotParkingDataPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            var byRefInt = typeof(int).MakeByRefType();
            return AccessTools.Method(typeof(VehicleUtils),
                nameof(VehicleUtils.GetParkingData),
                new[]
                {
                    typeof(SystemBase), typeof(Entity), byRefInt, byRefInt,
                    byRefInt, byRefInt,
                });
        }

        [HarmonyPrefix]
        private static void UseCarrier(SystemBase __0, ref Entity __1)
        {
            if (__0 == null || __1 == Entity.Null) return;
            var entityManager = __0.EntityManager;
            if (!entityManager.Exists(__1)
                || !entityManager.HasComponent<ParkingLotCarrierReference>(__1))
                return;

            var carrier = entityManager
                .GetComponentData<ParkingLotCarrierReference>(__1).Carrier;
            if (carrier == Entity.Null || !entityManager.Exists(carrier)
                || !entityManager.HasBuffer<Game.Net.SubNet>(carrier)
                || !entityManager.HasBuffer<Game.Objects.SubObject>(carrier))
                return;

            __1 = carrier;
        }
    }
}
