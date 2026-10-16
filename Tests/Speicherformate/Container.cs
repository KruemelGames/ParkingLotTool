using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;

// Nur Datentypen und API-Zugriffe. Kein natives ECS, Generator oder Spielserializer.
namespace Unity.Entities
{
    public interface IComponentData { }
    public interface IBufferElementData { }
    public interface IQueryTypeParameter { }
    public class InternalBufferCapacityAttribute : Attribute { public InternalBufferCapacityAttribute(int n) { } }
    public readonly record struct Entity(int Index, int Version = 1)
    {
        public static Entity Null => default;
    }
    public sealed class World
    {
        public readonly Game.Prefabs.PrefabSystem Prefabs = new();
        public readonly EntityManager EntityManager;
        public World() { EntityManager = new EntityManager(this); }
        private readonly Dictionary<Type, object> _systeme = new();
        public T GetExistingSystemManaged<T>() where T : class => Prefabs as T;
        public T GetOrCreateSystemManaged<T>() where T : class
        {
            if (Prefabs is T prefab) return prefab;
            if (!_systeme.TryGetValue(typeof(T), out var system))
                _systeme[typeof(T)] = system = Activator.CreateInstance<T>();
            return (T)system;
        }
    }
    public sealed class EntityManager
    {
        public World World { get; }
        private readonly Dictionary<(Entity, Type), object> _werte = new();
        public EntityManager(World world) { World = world; }
        public void Set<T>(Entity e, T value) => _werte[(e, typeof(T))] = value;
        public bool Exists(Entity e) { foreach (var key in _werte.Keys) if (key.Item1 == e) return true; return false; }
        public void SetBuffer<T>(Entity e) => Set(e, new List<T>());
        public bool HasBuffer<T>(Entity e) => HasComponent<List<T>>(e);
        public bool HasComponent<T>(Entity e) => _werte.ContainsKey((e, typeof(T)));
        public T GetComponentData<T>(Entity e) => (T)_werte[(e, typeof(T))];
        public void AddComponent<T>(Entity e) => Set(e, default(T));
        public void RemoveComponent<T>(Entity e) => _werte.Remove((e, typeof(T)));
    }
    public struct EntityCommandBuffer
    {
        public void AddComponent<T>(Entity e) => throw new NotSupportedException("Kein Barrier-Test.");
        public void RemoveComponent<T>(Entity e) => throw new NotSupportedException("Kein Barrier-Test.");
    }
}
namespace Unity.Collections
{
    public struct FixedString512Bytes
    {
        public const int UTF8MaxLengthInBytes = 509;
        private string _text;
        public FixedString512Bytes(string text)
        {
            if (Encoding.UTF8.GetByteCount(text) > UTF8MaxLengthInBytes) throw new ArgumentException("FixedString zu lang.");
            _text = text;
        }
        public bool IsEmpty => string.IsNullOrEmpty(_text);
        public override string ToString() => _text ?? "";
    }
}
namespace Colossal.Serialization.Entities
{
    public interface IWriter { void Write<T>(T value); }
    public interface IReader { void Read<T>(out T value); }
    public interface ISerializable
    {
        void Serialize<T>(T w) where T : IWriter;
        void Deserialize<T>(T r) where T : IReader;
    }
}
namespace Colossal
{
    public readonly record struct Hash128(string Text)
    {
        public override string ToString() => Text ?? "";
        public static bool TryParse(string text, out Hash128 hash)
        {
            var ok = System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9a-fA-F]{32}$");
            hash = ok ? new Hash128(text.ToLowerInvariant()) : default;
            return ok;
        }
    }
}
namespace Colossal.Mathematics
{
    public struct Bezier4x3
    {
        public float3 a, b, c, d;
        public Bezier4x3(float3 aa, float3 bb, float3 cc, float3 dd) { a = aa; b = bb; c = cc; d = dd; }
    }
}
namespace Game.Net
{
    public struct CompositionFlags
    {
        [Flags] public enum General : uint { }
        [Flags] public enum Side : uint { }
        public General m_General; public Side m_Left, m_Right;
        public CompositionFlags(General g, Side l, Side r) { m_General = g; m_Left = l; m_Right = r; }
    }
    public struct Upgraded { public CompositionFlags m_Flags; }
}
namespace Game.Tools
{
    [Flags] public enum CoursePosFlags : uint { }
    public struct Temp { }
    public struct CoursePos
    {
        public Entity m_Entity; public float3 m_Position; public quaternion m_Rotation;
        public float2 m_Elevation; public CoursePosFlags m_Flags; public int m_ParentMesh; public float m_CourseDelta;
    }
    public struct NetCourse
    {
        public Colossal.Mathematics.Bezier4x3 m_Curve; public float m_Length;
        public float2 m_Elevation; public int m_FixedIndex; public CoursePos m_StartPosition, m_EndPosition;
    }
}
namespace Game.Common { public struct Deleted { } public struct Owner { public Entity m_Owner; } }
namespace Game.Areas { public struct Area { } }
namespace Game.Prefabs
{
    public struct PrefabData { }
    public struct PrefabRef { public Entity m_Prefab; }
    public readonly record struct PrefabID(string Type, string Name, Colossal.Hash128 Hash = default)
    {
        public string ToUrlSegment() => Uri.EscapeDataString(Type) + "/" + Uri.EscapeDataString(Name)
            + (Hash == default ? "" : "/" + Hash.ToString());
    }
    public class PrefabBase
    {
        public PrefabID ID; public string name;
        public PrefabID GetPrefabID() => ID;
    }
    public sealed class PrefabSystem
    {
        public readonly Dictionary<PrefabID, (Entity Entity, PrefabBase Prefab)> Werte = new();
        public bool TryGetPrefab(PrefabID id, out PrefabBase prefab)
        { var ok = Werte.TryGetValue(id, out var w); prefab = w.Prefab; return ok; }
        public bool TryGetPrefab<T>(Entity e, out T prefab) where T : PrefabBase
        { foreach (var w in Werte.Values) if (w.Entity == e) { prefab = w.Prefab as T; return prefab != null; } prefab = null; return false; }
        public bool TryGetEntity(PrefabBase prefab, out Entity e)
        { foreach (var w in Werte.Values) if (w.Prefab == prefab) { e = w.Entity; return true; } e = Entity.Null; return false; }
    }
}
namespace ParkingLotTool.Tools
{
    public struct ParkingLotCarrierReference { public Entity Carrier; }
    public struct ParkingLotAbrisswurzel { }
    public static class Mod { public static readonly Logger log = new(); }
    public sealed class Logger { public void Info(string text) { } public void Warn(string text) { } }
}
