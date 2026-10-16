using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public enum ParkingLotErzeugungsart { Bushalt, Strom, Wasser }

    /** Vor der Definition gespeichert; keine rohe Prefab-Entity. */
    [InternalBufferCapacity(0)]
    public struct ParkingLotErzeugungsauftrag : IBufferElementData, ISerializable
    {
        public ParkingLotErzeugungsart Art;
        public FixedString512Bytes Prefabname;
        public int Seed;
        public Entity Bezug, Ergebnis;
        public float3 A, D;
        public bool Erledigt;
        public void Serialize<W>(W w) where W : IWriter
        {
            w.Write(1); w.Write((int)Art); w.Write(Prefabname.ToString()); w.Write(Seed);
            w.Write(Bezug); w.Write(Ergebnis); w.Write(A); w.Write(D); w.Write(Erledigt);
        }
        public void Deserialize<R>(R r) where R : IReader
        {
            r.Read(out int version); r.Read(out int art); Art = (ParkingLotErzeugungsart)art;
            r.Read(out string name); Prefabname = ParkingLotPrefabname.Fest(name);
            r.Read(out Seed); r.Read(out Bezug); r.Read(out Ergebnis); r.Read(out A); r.Read(out D); r.Read(out Erledigt);
        }
    }
}
