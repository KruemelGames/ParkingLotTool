using System;
using System.Collections.Generic;
using Unity.Entities;

// Containerschnittstellen fuer die unveraenderten Produktionsmethoden.
// Kein CS2-Job, Temp/Apply oder natives ECS wird hier nachgebildet.
namespace Unity.Entities
{
    public readonly record struct Entity(int Index) { public static Entity Null => default; }
    public class EntityManager
    {
        public readonly Dictionary<Entity, Dictionary<Type, object>> Data = new();
        public int Adds, Sets;
        public void Set<T>(Entity e, T c) { if (!Data.ContainsKey(e)) Data[e] = new(); Data[e][typeof(T)] = c; }
        public void SetComponentData<T>(Entity e, T c)
        {
            if (!HasComponent<T>(e)) throw new InvalidOperationException("Set auf fehlende Komponente: " + typeof(T).Name);
            Sets++; Set(e, c);
        }
        public void AddComponentData<T>(Entity e, T c)
        {
            if (!Exists(e)) throw new InvalidOperationException("Add auf tote Entity");
            Adds++; Set(e, c);
        }
        public void AddComponent<T>(Entity e) => AddComponentData(e, default(T));
        public void Remove(Entity e) => Data.Remove(e);
        public bool Exists(Entity e) => Data.ContainsKey(e);
        public bool HasComponent<T>(Entity e) => Exists(e) && Data[e].ContainsKey(typeof(T));
        public T GetComponentData<T>(Entity e) => (T)Data[e][typeof(T)];
        public bool HasBuffer<T>(Entity e) => HasComponent<List<T>>(e);
        public Buffer<T> GetBuffer<T>(Entity e, bool _) => new(GetComponentData<List<T>>(e));
    }
    public class Buffer<T>
    {
        private readonly List<T> _werte;
        public Buffer(List<T> werte) => _werte = werte;
        public int Length => _werte.Count;
        public T this[int i] => _werte[i];
    }
    public class World
    {
        public readonly EntityManager EntityManager = new();
        private readonly Dictionary<Type, Game.GameSystemBase> _systems = new();
        public T GetOrCreateSystemManaged<T>() where T : Game.GameSystemBase, new()
        {
            if (!_systems.TryGetValue(typeof(T), out var s))
            { s = new T { World = this }; _systems[typeof(T)] = s; s.Create(); }
            return (T)s;
        }
    }
}
namespace Game
{
    public enum GameMode { Game, Menu }
    public static class Modes { public static bool IsGame(this GameMode m) => m == GameMode.Game; }
    public class GameSystemBase
    {
        public World World;
        public EntityManager EntityManager => World.EntityManager;
        protected virtual void OnCreate() { }
        protected virtual void OnUpdate() { }
        protected virtual void OnGamePreload(Colossal.Serialization.Entities.Purpose p, GameMode m) { }
        public void Create() => OnCreate(); public void Tick() => OnUpdate();
        public void Preload() => OnGamePreload(default, GameMode.Game);
    }
}
namespace Game.Common { public struct Deleted { } public struct Owner { public Entity m_Owner; } }
namespace Game.Net { public struct SubNet { public Entity m_SubNet; } public struct Edge { } }
namespace Game.SceneFlow
{
    public class GameManager
    { public static GameManager instance = new(); public bool isGameLoading; public Game.GameMode gameMode = Game.GameMode.Game; }
}
namespace Game.Tools
{
    public struct Temp { }
    public class ToolBaseSystem : Game.GameSystemBase { }
    public class ToolSystem : Game.GameSystemBase { public ToolBaseSystem activeTool; }
    public class DefaultToolSystem : ToolBaseSystem { }
}
namespace Game.Prefabs
{
    public class PrefabBase { public string name; }
    public struct PrefabRef { public Entity m_Prefab; }
    public readonly record struct PathfindCostInfo(float Time, float Behaviour, float Money, float Comfort);
    public class PrefabSystem : Game.GameSystemBase
    {
        public readonly Dictionary<Entity, PrefabBase> Prefabs = new();
        public bool TryGetPrefab<T>(Entity e, out T p) where T : PrefabBase
        { p = Prefabs.TryGetValue(e, out var f) ? f as T : null; return p != null; }
    }
}
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace UnityEngine.Scripting { public class PreserveAttribute : Attribute { } }
namespace Colossal.Serialization.Entities { public struct Purpose { } }
namespace ParkingLotTool.Tools
{
    public static class Mod { public static Logger log = new(); }
    public class Logger { public void Info(string s) { } }
    public struct ParkingLotCarrierReference { public Entity Carrier; }
    public struct ParkingLotBuildReceipt { }
    public struct ParkingLotDatenstand { public int Stand; }
    public struct ParkingLotNebenarbeitOffen { }
    public sealed partial class ParkingLotToolSystem : Game.Tools.ToolBaseSystem
    {
        private bool IsEditing; private Entity _pendingEditLot;
        private readonly List<int> _points = new(); private BuildStage _buildStage;
        private object _buildTask; private enum BuildStage { Idle }
    }
    public class ParkingLotFahrprefabSystem : Game.GameSystemBase
    {
        public readonly Dictionary<string, Entity> Ziele = new();
        public bool TryWeg(string n, out Entity e) => Ziele.TryGetValue(n, out e);
    }
    public class ParkingLotFahrwegTauschWerkzeug : Game.Tools.ToolBaseSystem
    {
        public bool Beschaeftigt; public int Starts;
        public Action<Entity, bool, string> Fertig;
        public void Starte(Entity e, List<(Entity, Entity, Entity)> p) { Starts++; Beschaeftigt = true; }
        public void Zuruecksetzen() => Beschaeftigt = false;
        public void PflegeOhneWerkzeug() { }
    }
    public class ParkingLotBestandsTauschSystem : Game.GameSystemBase { public bool WerkzeugLaeuft; }
    public class ParkingLotBestandsTauschWerkzeug : Game.Tools.ToolBaseSystem { }
    public class ParkingLotFlaechenSyncSystem : Game.GameSystemBase { public bool Laeuft; }
    public class ParkingLotSyncSystem : Game.GameSystemBase
    {
        public readonly List<bool> Antworten = new();
        public void MeldeHintergrundEnde(Entity e, Entity n, bool ok) => Antworten.Add(ok);
    }
}
