using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /** Der native Generator erzeugt den gespeicherten Marker im selben ECB wie die Flaeche. */
    public sealed class ParkingLotMusterkennung : ComponentBase
    {
        public override void GetPrefabComponents(HashSet<ComponentType> components) { }
        public override void GetArchetypeComponents(HashSet<ComponentType> components)
            => components.Add(ComponentType.ReadWrite<ParkingLotMusterflaeche>());
    }
}
