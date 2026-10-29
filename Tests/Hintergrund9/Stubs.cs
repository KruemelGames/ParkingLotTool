using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

// Test-API fuer die echten Produktionsmethoden, keine nativen Unity-Jobs.
namespace UnityEngine.Scripting { public sealed class PreserveAttribute : Attribute {} }
namespace Unity.Collections
{
    public enum Allocator { Temp }
    public sealed class NativeArray<T> : IEnumerable<T>, IDisposable
    {
        private readonly T[] _werte;
        public NativeArray(IEnumerable<T> werte) { _werte = werte.ToArray(); }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_werte).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() {}
    }
}
namespace Unity.Entities
{
    public struct Entity : IEquatable<Entity>
    {
        public int Index, Version;
        public Entity(int index) { Index=index; Version=1; }
        public static Entity Null => default;
        public bool Equals(Entity e) => Index==e.Index && Version==e.Version;
        public override bool Equals(object o) => o is Entity e && Equals(e);
        public override int GetHashCode() => HashCode.Combine(Index,Version);
        public static bool operator ==(Entity a,Entity b) => a.Equals(b);
        public static bool operator !=(Entity a,Entity b) => !a.Equals(b);
        public override string ToString() => $"{Index}.{Version}";
    }
    public interface IComponentData {}
    public sealed class DisableAutoCreationAttribute : Attribute {}
    public struct ComponentType
    {
        public Type Type; public bool Ausschluss;
        public static ComponentType ReadOnly<T>() => new() { Type=typeof(T) };
        public static ComponentType Exclude<T>() => new() { Type=typeof(T),Ausschluss=true };
    }
    public sealed class DynamicBuffer<T> : IEnumerable<T>
    {
        public readonly List<T> Werte = new();
        public void Add(T v) => Werte.Add(v);
        public int Length => Werte.Count;
        public List<T>.Enumerator GetEnumerator() => Werte.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw new NotImplementedException("CS2 DynamicBuffer LINQ verboten");
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }
    public sealed class EntityQuery : IDisposable
    {
        private readonly EntityManager _em; private readonly ComponentType[] _typen;
        public EntityQuery(EntityManager em,ComponentType[] typen) { _em=em; _typen=typen; }
        public Unity.Collections.NativeArray<Entity> ToEntityArray(Unity.Collections.Allocator a)
            => new(_em.Daten.Keys.Where(e=>_typen.All(t=>_em.Daten[e].ContainsKey(t.Type)!=t.Ausschluss)).ToArray());
        public void Dispose() {}
    }
    public sealed class EntityManager
    {
        public readonly Dictionary<Entity,Dictionary<Type,object>> Daten = new();
        public void Neu(Entity e) => Daten[e]=new();
        public bool Exists(Entity e) => Daten.ContainsKey(e);
        public bool HasComponent<T>(Entity e) => Exists(e)&&Daten[e].ContainsKey(typeof(T));
        public T GetComponentData<T>(Entity e) => (T)Daten[e][typeof(T)];
        public void AddComponentData<T>(Entity e,T v) => Daten[e][typeof(T)]=v;
        public void AddComponent<T>(Entity e) where T:new() => AddComponentData(e,new T());
        public void RemoveComponent<T>(Entity e) => Daten[e].Remove(typeof(T));
        public bool HasBuffer<T>(Entity e) => HasComponent<DynamicBuffer<T>>(e);
        public DynamicBuffer<T> GetBuffer<T>(Entity e,bool read=false) => GetComponentData<DynamicBuffer<T>>(e);
        public DynamicBuffer<T> AddBuffer<T>(Entity e) { var b=new DynamicBuffer<T>(); AddComponentData(e,b); return b; }
        public EntityQuery CreateEntityQuery(params ComponentType[] typen) => new(this,typen);
        public void Bildende()
        {
            foreach(var e in Daten.Keys.ToArray()) if(HasComponent<Game.Common.Deleted>(e)) Daten.Remove(e);
        }
    }
}
namespace Game
{
    public abstract class GameSystemBase
    {
        public Unity.Entities.EntityManager EntityManager = new();
        protected virtual void OnCreate() {}
        protected virtual void OnUpdate() {}
        protected Unity.Entities.EntityQuery GetEntityQuery(params Unity.Entities.ComponentType[] typen) => EntityManager.CreateEntityQuery(typen);
        public void Init() => OnCreate(); public void Tick() => OnUpdate();
    }
}
namespace Game.Common
{
    public struct Deleted {}
    // Seit 16.10. setzt der Ersatzabriss `Updated` an behaltenen Knoten.
    public struct Updated {}
    public struct Owner { public Unity.Entities.Entity m_Owner; }
}
namespace Game.Prefabs
{
    public struct PrefabRef { public Unity.Entities.Entity m_Prefab; }
    public struct PathwayData {}
    public struct AuxiliaryNet {}
    public struct NetData { public Game.Net.Layer m_RequiredLayers,m_ConnectLayers; }
}
namespace Game.Net
{
    public struct Node { public float3 m_Position; }
    [Flags] public enum Layer { Road=1,MarkerPathway=2 }
    public struct Edge { public Unity.Entities.Entity m_Start,m_End; }
    public struct ConnectedEdge { public Unity.Entities.Entity m_Edge; }
    public static class NetUtils
    { public static bool CanConnect(Game.Prefabs.NetData a,Game.Prefabs.NetData b) => (a.m_RequiredLayers&b.m_ConnectLayers)==a.m_RequiredLayers||(b.m_RequiredLayers&a.m_ConnectLayers)==b.m_RequiredLayers; }
}
namespace Game.Tools
{
    [Flags] public enum CreationFlags { Permanent=1 }
    public struct CreationDefinition { public CreationFlags m_Flags; public Unity.Entities.Entity m_Owner,m_Prefab; }
    public struct CoursePos { public Unity.Entities.Entity m_Entity; public float3 m_Position; }
    public struct NetCourse { public CoursePos m_StartPosition,m_EndPosition; }
    public struct Temp {}
}
namespace ParkingLotTool.Tools
{
    public struct ParkingLotAuftragsdefinition {}
    public struct Bestand { public Unity.Entities.Entity Kante,Start,Ende; }
    public struct ParkingLotErhaltenerKurs { public Bestand Bestand; }
    public static class ParkingLotNetzerhalt
    { public static void EntferneGelieheneVerweise(Unity.Entities.EntityManager em,Unity.Entities.Entity alt,Unity.Entities.Entity lot,Unity.Entities.Entity traeger) {} }
    public static class ParkingLotNetzRueckweg
    {
        public static void Melde(string text) => Console.WriteLine(text);
        public static bool Lebt(Unity.Entities.EntityManager em,Unity.Entities.Entity e) => em.Exists(e)&&!em.HasComponent<Game.Common.Deleted>(e);
    }
}
