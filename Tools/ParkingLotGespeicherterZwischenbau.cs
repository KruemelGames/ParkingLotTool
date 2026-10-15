using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Issue #17: Rueckweg wurde verworfen, der Neubau aus dem Bauplan ist
     *  noch nicht gesichert. Ueberlebt das Speichern; beim Laden wird der
     *  Neubau erneut eingereiht. Faellt weg, sobald der neue Rueckweg steht. */
    public struct ParkingLotNeubauFaellig : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(1);
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); }
    }
    public struct ParkingLotNebenarbeitOffen : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(1);
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); }
    }
    // Vor dem Generator ist dessen Ergebnis-ID noch unbekannt. Die gesendete
    // Lot-Kontur identifiziert nach Laden auch die Luecke A -> Besitzerfund.
    public struct ParkingLotStufeAPrefab : IComponentData, ISerializable
    {
        public Entity Prefab;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(Prefab);
        public void Deserialize<TReader>(TReader r) where TReader : IReader => r.Read(out Prefab);
    }
    [InternalBufferCapacity(0)]
    public struct ParkingLotStufeAKnoten : IBufferElementData, ISerializable
    {
        public float3 Position;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(Position);
        public void Deserialize<TReader>(TReader r) where TReader : IReader => r.Read(out Position);
    }
    [InternalBufferCapacity(0)]
    public struct ParkingLotRueckweganschluss : IBufferElementData, ISerializable
    {
        public int Kursindex;
        public Entity Knoten;
        public Entity Prefab, Besitzer;
        public float3 Position;
        public float Lage;
        /** PrefabID des Prefabs (Issue #17), siehe ParkingLotPrefabname. */
        public FixedString512Bytes PrefabName;
        // Das alte Format hatte keine Version und begann mit Kursindex >= 0.
        // Eine negative Kennung davor erkennt das neue Format eindeutig.
        private const int Format2 = -2;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter
        {
            w.Write(Format2);
            w.Write(Kursindex); w.Write(Knoten); w.Write(Lage); w.Write(Prefab); w.Write(Besitzer); w.Write(Position);
            w.Write(PrefabName.ToString());
        }
        public void Deserialize<TReader>(TReader r) where TReader : IReader
        {
            r.Read(out int erstes);
            bool neu = erstes == Format2;
            if (neu) r.Read(out Kursindex); else Kursindex = erstes;
            r.Read(out Knoten); r.Read(out Lage); r.Read(out Prefab); r.Read(out Besitzer); r.Read(out Position);
            PrefabName = default;
            if (neu) { r.Read(out string name); PrefabName = ParkingLotPrefabname.Fest(name); }
        }
    }
}
