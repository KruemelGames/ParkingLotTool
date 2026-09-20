using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private int _fusswegBefundAb;

        private void PruefeFusswegBefund()
        {
            if (_fusswegBefundAb == 0 || UnityEngine.Time.frameCount < _fusswegBefundAb)
                return;
            _fusswegBefundAb = 0;
            var prefab = World.GetOrCreateSystemManaged<ParkingLotFusswegPrefabSystem>().ZugangBereit;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)) return;
            Mod.log.Info("PLT-Fusswegzugang: gesetzter Zugangs-Prefab ist vorhanden; "
                + "Befund bleibt read-only.");
        }
    }
}
