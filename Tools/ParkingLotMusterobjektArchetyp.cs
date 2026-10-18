using System.Collections.Generic;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /** Nur die sichtbare Geometrie, keine Komponenten des Originals. */
    public sealed class ParkingLotMusterobjektPrefab : StaticObjectPrefab
    {
        public override void GetArchetypeComponents(HashSet<ComponentType> c)
        {
            base.GetArchetypeComponents(c);
            c.Remove(ComponentType.ReadWrite<Emissive>());
            c.Remove(ComponentType.ReadWrite<LightState>());
            c.Add(ComponentType.ReadWrite<ParkingLotMusterflaeche>());
        }
    }

}
