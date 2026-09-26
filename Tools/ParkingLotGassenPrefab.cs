using Game.Prefabs;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /** Ist das eine unserer Zufahrtsgassen? Die eine Stelle fuer diese Frage. */
    internal static class GassenPrefab
    {
        internal static bool Ist(PrefabSystem prefabs, Entity prefab)
            => prefabs != null && prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null
               && p.name.StartsWith("PLT Zufahrtsgasse", System.StringComparison.Ordinal);
    }
}
