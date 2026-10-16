using System;
using System.Collections.Generic;
using Unity.Mathematics;

// Nur Datencontainer fuer den echten Bauzettelleser. Kein ECS-, Generator-,
// Terrain-, Apply- oder Clear-Verhalten wird simuliert.
namespace Unity.Entities
{
    public interface IComponentData { }
    public interface IQueryTypeParameter { }
    public interface IBufferElementData { }
    public sealed class InternalBufferCapacityAttribute : Attribute
    {
        public InternalBufferCapacityAttribute(int capacity) { }
    }
    public struct Entity : IEquatable<Entity>
    {
        public int Index, Version;
        public static Entity Null => default;
        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object other) => other is Entity e && Equals(e);
        public override int GetHashCode() => HashCode.Combine(Index, Version);
        public static bool operator ==(Entity a, Entity b) => a.Equals(b);
        public static bool operator !=(Entity a, Entity b) => !a.Equals(b);
    }
    public struct DynamicBuffer<T>
    {
        private readonly List<T> _values;
        public DynamicBuffer(List<T> values) => _values = values;
        public int Length => _values.Count;
        public T this[int index] => _values[index];
    }
    public sealed class EntityManager
    {
        private readonly Dictionary<(Entity, Type), object> _values = new();
        public int Schreibzugriffe { get; private set; }
        public void Set<T>(Entity entity, T value)
        {
            _values[(entity, typeof(T))] = value;
            Schreibzugriffe++;
        }
        public void SetBuffer<T>(Entity entity, List<T> value)
        {
            _values[(entity, typeof(List<T>))] = value;
            Schreibzugriffe++;
        }
        public bool Exists(Entity entity) => HasComponent<ParkingLotTool.Tools.ParkingLotBuildReceipt>(entity);
        public bool HasComponent<T>(Entity entity) => _values.ContainsKey((entity, typeof(T)));
        public T GetComponentData<T>(Entity entity) => (T)_values[(entity, typeof(T))];
        public bool HasBuffer<T>(Entity entity) => _values.ContainsKey((entity, typeof(List<T>)));
        public DynamicBuffer<T> GetBuffer<T>(Entity entity, bool readOnly)
        {
            if (!readOnly) throw new InvalidOperationException("Leser fordert schreibbaren Puffer an.");
            return new DynamicBuffer<T>((List<T>)_values[(entity, typeof(List<T>))]);
        }
    }
}
namespace Colossal.Serialization.Entities
{
    public interface IWriter { void Write<T>(T value); }
    public interface IReader { void Read<T>(out T value); }
    public interface ISerializable
    {
        void Serialize<TWriter>(TWriter writer) where TWriter : IWriter;
        void Deserialize<TReader>(TReader reader) where TReader : IReader;
    }
}
namespace ParkingLotTool
{
    public static class ParkingLotTexte { public static string T(string key, params (string Name, object Wert)[] args) => key; }
    public static class Mod
    {
        public static readonly Log log = new();
        public sealed class Log { public void Info(string text) { } }
    }
}
namespace ParkingLotTool.Tools
{
    public struct ParkingLotCarrierReference { public Unity.Entities.Entity Carrier; }
    public sealed class ParkingLotToolSystem
    {
        internal sealed class Ausrichtzuweisung
        {
            internal float2 Anker, LinieA, LinieB;
            internal double Winkel;
        }
    }
}
