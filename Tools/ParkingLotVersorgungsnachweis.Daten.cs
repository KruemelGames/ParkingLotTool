using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Vor Apply: alle erwarteten eigenen Kanten eines geprueften Anschlusses.
     *  Temp-Verweise werden beim Speichern ohne Apply leer, dauerhafte bleiben. */
    [InternalBufferCapacity(0)]
    public struct ParkingLotVersorgungsnachweis : IBufferElementData, ISerializable
    {
        public Entity Kante;
        public float3 Start, Ziel;
        public bool Stadt;
        public void Serialize<W>(W w) where W : IWriter { w.Write(1); w.Write(Kante); w.Write(Start); w.Write(Ziel); w.Write(Stadt); }
        public void Deserialize<R>(R r) where R : IReader { r.Read(out int version); r.Read(out Kante); r.Read(out Start); r.Read(out Ziel); r.Read(out Stadt); }
    }

}
