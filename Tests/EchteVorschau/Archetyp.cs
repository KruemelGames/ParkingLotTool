using System;
using System.Collections.Generic;
namespace Unity.Entities
{
    public readonly struct ComponentType : IEquatable<ComponentType>
    {
        private readonly Type _typ;
        private ComponentType(Type typ) {_typ=typ;}
        public static ComponentType ReadWrite<T>() => new ComponentType(typeof(T));
        public bool Equals(ComponentType b) => _typ==b._typ;
        public override bool Equals(object b) => b is ComponentType t && Equals(t);
        public override int GetHashCode()=>_typ.GetHashCode();
    }
}
namespace Game.Rendering {public struct Emissive {} public struct LightState {} public struct MeshBatch {}}
namespace Game.Prefabs
{
    public class StaticObjectPrefab
    {
        public virtual void GetArchetypeComponents(HashSet<Unity.Entities.ComponentType> c)
        {
            c.Add(Unity.Entities.ComponentType.ReadWrite<Game.Rendering.Emissive>());
            c.Add(Unity.Entities.ComponentType.ReadWrite<Game.Rendering.LightState>());
            c.Add(Unity.Entities.ComponentType.ReadWrite<Game.Rendering.MeshBatch>());
        }
    }
}
namespace ParkingLotTool.Tools { public struct ParkingLotMusterflaeche {} }
