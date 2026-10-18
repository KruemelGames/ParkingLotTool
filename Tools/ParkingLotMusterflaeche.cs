using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /** Gemeinsamer Vorschaumarker (historischer Typname); niemals Bestandteil eines Parkplatzes. */
    public struct ParkingLotMusterflaeche : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(1);
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); }
    }
}
