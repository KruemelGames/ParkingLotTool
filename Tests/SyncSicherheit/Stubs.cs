using System;
using System.Collections.Generic;
using Unity.Entities;

// Containerschnittstellen fuer die unveraenderten Produktionsmethoden.
// Kein CS2-Job, Temp/Apply oder natives ECS wird hier nachgebildet.
namespace Unity.Entities
{
    public interface IComponentData { }
    public interface IBufferElementData { }
    public class InternalBufferCapacityAttribute : Attribute { public InternalBufferCapacityAttribute(int n) { } }
    public readonly record struct Entity(int Index) { public static Entity Null => default; }
    public class EntityManager
    {
        public readonly Dictionary<Entity, Dictionary<Type, object>> Data = new();
        public int Adds, Sets;
        public Action VorSchreiben;
        public Action<Entity> BeimZerstoeren;
        public readonly List<Entity> Zerstoerversuche = new();
        public void DestroyEntity(Entity e) { Zerstoerversuche.Add(e); BeimZerstoeren?.Invoke(e); Data.Remove(e); }
        public void Set<T>(Entity e, T c) { if (!Data.ContainsKey(e)) Data[e] = new(); Data[e][typeof(T)] = c; }
        public void SetComponentData<T>(Entity e, T c)
        {
            if (!HasComponent<T>(e)) throw new InvalidOperationException("Set auf fehlende Komponente: " + typeof(T).Name);
            VorSchreiben?.Invoke(); Sets++; Set(e, c);
        }
        public void AddComponentData<T>(Entity e, T c)
        {
            if (!Exists(e)) throw new InvalidOperationException("Add auf tote Entity");
            VorSchreiben?.Invoke(); Adds++; Set(e, c);
        }
        public void AddComponent<T>(Entity e) => AddComponentData(e, default(T));
        public void Remove(Entity e) => Data.Remove(e);
        public void RemoveComponent<T>(Entity e) { if (Exists(e)) { Data[e].Remove(typeof(T)); Data[e].Remove(typeof(List<T>)); } }
        public bool Exists(Entity e) => Data.ContainsKey(e);
        public bool HasComponent<T>(Entity e) => Exists(e) && Data[e].ContainsKey(typeof(T));
        public T GetComponentData<T>(Entity e) => (T)Data[e][typeof(T)];
        public Buffer<T> AddBuffer<T>(Entity e) { Set(e,new List<T>()); return GetBuffer<T>(e); }
        public bool HasBuffer<T>(Entity e) => HasComponent<List<T>>(e);
        public Buffer<T> GetBuffer<T>(Entity e, bool _ = false) => new(GetComponentData<List<T>>(e));
    }
    public class Buffer<T>
    {
        private readonly List<T> _werte;
        public Buffer(List<T> werte) => _werte = werte;
        public int Length => _werte.Count;
        public T this[int i] { get => _werte[i]; set => _werte[i] = value; }
        public void Add(T v) => _werte.Add(v);
        public void RemoveAt(int i) => _werte.RemoveAt(i);
    }
    public class World
    {
        public readonly EntityManager EntityManager = new();
        public World() { GetOrCreateSystemManaged<ParkingLotTool.Tools.ParkingLotAuftragsSystem>().PufferWiederhergestellt(); }
        public void DestroySystemManaged(Game.GameSystemBase system) { }
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
namespace Game.Common { public struct PseudoRandomSeed { public ushort m_Seed; } public struct Deleted { } public struct Owner { public Entity m_Owner; } }
namespace Game.Areas { public struct Area { } }
namespace Game.Net { public struct TestBezier { public Unity.Mathematics.float3 a, d; } public struct Curve { public TestBezier m_Bezier; } public struct SubNet { public Entity m_SubNet; } public struct Edge { } }
namespace Game.SceneFlow
{
    public class GameManager
    { public static GameManager instance = new(); public bool isGameLoading; public Game.GameMode gameMode = Game.GameMode.Game; }
}
namespace Game.Tools
{
    [Flags] public enum TempFlags { Create=1, Delete=2, Cancel=4 }
    public struct Temp { public Entity m_Original; public TempFlags m_Flags; }
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
namespace UnityEngine { public static class Time { public static int frameCount; public static float realtimeSinceStartup; } }
namespace UnityEngine.Scripting { public class PreserveAttribute : Attribute { } }
namespace Colossal.Serialization.Entities
{
    public struct Purpose { }
    public interface IWriter { void Write<T>(T value); }
    public interface IReader { void Read<T>(out T value); }
    public interface ISerializable { void Serialize<T>(T w) where T : IWriter; void Deserialize<T>(T r) where T : IReader; }
}
namespace ParkingLotTool.Tools
{
    public static class Mod { public static Logger log = new(); public static Optionen Optionen = new(); }
    public class Optionen { public bool AutomatischSynchronisieren; public bool AutomatischVersorgung = true; }
    public class Logger
    {
        public bool InfoFehler, WarnFehler;
        public void Info(string s) { if (InfoFehler) throw new InvalidOperationException("Logger"); }
        public void Warn(string s) { if (WarnFehler) throw new InvalidOperationException("Logger"); }
    }
    public struct ParkingLotCarrierReference { public Entity Carrier; }
    public struct ParkingLotBuildReceipt { }
    public struct ParkingLotDatenstand { public int Stand; }
    public sealed partial class ParkingLotToolSystem : Game.Tools.ToolBaseSystem
    {
        internal bool IsEditing; private Entity _pendingEditLot;
        private readonly List<int> _points = new(); private BuildStage _buildStage;
        private object _buildTask; private enum BuildStage { Idle }
    }
    public class ParkingLotFahrprefabSystem : Game.GameSystemBase
    {
        public readonly Dictionary<string, Entity> Ziele = new();
        public bool TryWeg(string n, out Entity e) => Ziele.TryGetValue(n, out e);
    }
    public sealed partial class ParkingLotFahrwegTauschWerkzeug : Game.Tools.ToolBaseSystem
    {
        public bool Beschaeftigt => _stufe != Stufe.Frei; public int Starts;
        public Action<Entity, bool, string> Fertig;
        public void Starte(Entity e, List<(Entity, Entity, Entity)> p, ParkingLotTool.Geometry.Auftragskennung id = default) { Starts++; _auftrag = id; _lot = e; _plan.Clear(); _plan.AddRange(p); _stufe = Stufe.Anlegen; }
        public void Zuruecksetzen() { _auftrag = default; _stufe = Stufe.Frei; _lot = Entity.Null; _plan.Clear(); _definitionen.Clear(); }
        public void PflegeOhneWerkzeug() { }
    }
    public sealed partial class ParkingLotBestandsTauschSystem : Game.GameSystemBase { public bool WerkzeugLaeuft; }
    public sealed partial class ParkingLotBestandsTauschWerkzeug : Game.Tools.ToolBaseSystem { }
    public sealed partial class ParkingLotFlaechenSyncSystem : Game.GameSystemBase { }
    public sealed partial class ParkingLotSyncSystem : Game.GameSystemBase
    {
        public readonly List<bool> Antworten = new();
        public bool AntwortFehler;
        public bool TestAntwort(ParkingLotTool.Geometry.Bauauftrag<Entity> a, Entity neu, bool ok)
        { Antworten.Add(ok); if (AntwortFehler) throw new InvalidOperationException("Empfaenger"); return ok; }
    }
}

namespace Unity.Collections
{
    public readonly struct FixedString512Bytes
    {
        private readonly string _text;
        public FixedString512Bytes(string s) => _text=s;
        public override string ToString() => _text ?? "";
    }
}
namespace Game.Objects
{
    public struct Transform { public Unity.Mathematics.float3 m_Position; }
    public struct Attached { public Unity.Entities.Entity m_Parent; }
}
namespace Game.Routes { public struct TransportStop { } }
namespace ParkingLotTool.Tools
{
    public struct ParkingLotPartRelation { public Unity.Entities.Entity Lot, Carrier; }
    public struct ParkingLotVersorgungsleitung { public Unity.Entities.Entity Lot, Carrier; public bool Strom; }
    internal static class ParkingLotPrefabname
    { internal static Unity.Collections.FixedString512Bytes Fest(string s) => new(s); }
    internal sealed partial class ParkingLotErzeugungsSystem : Game.GameSystemBase
    {
        public bool TestLeitung(Unity.Entities.Entity lot, Unity.Entities.Entity carrier, Unity.Entities.Entity prefab,
            ParkingLotErzeugungsauftrag q, Unity.Entities.Entity e) => BindeLeitung(lot,carrier,prefab,q,e);
        public bool TestBushalt(Unity.Entities.Entity lot, Unity.Entities.Entity carrier, Unity.Entities.Entity prefab,
            ref ParkingLotErzeugungsauftrag q, params Unity.Entities.Entity[] e) => BindeBushalt(lot,carrier,prefab,ref q,new(e));
    }
}

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class TestTrasse { internal Unity.Mathematics.float3 Start, Ziel; internal Unity.Entities.Entity Zielkante; }
        private sealed class AvKurs
        {
            internal readonly System.Collections.Generic.List<Unity.Entities.Entity> Kanten = new(), Anschlussstuecke = new();
            internal TestTrasse Trasse;
        }
        private sealed class AvVerbindung { internal Unity.Mathematics.float3 Start, Ziel; internal bool ZielIstStadt; }
        private readonly System.Collections.Generic.List<AvKurs> _avKurse = new();
        private readonly System.Collections.Generic.List<AvVerbindung> _avVerbindungen = new();
        private Unity.Entities.Entity _testLot;
        private Unity.Entities.Entity AvZugehoerigesLot() => _testLot;
        public void TestVorApply(Unity.Entities.Entity lot, Unity.Entities.Entity carrier, params Unity.Entities.Entity[] kanten)
        {
            _avKurse.Clear(); var k = new AvKurs(); k.Kanten.AddRange(kanten); _avKurse.Add(k);
            MarkiereVersorgungVorApply(lot, carrier);
        }
        public void TestSichereVerbindungen(Unity.Entities.Entity lot, params Unity.Entities.Entity[] kanten)
        {
            _testLot=lot; _avKurse.Clear();
            var k=new AvKurs { Trasse=new TestTrasse { Start=new(0,0,0), Ziel=new(10,0,0) } };
            k.Kanten.AddRange(kanten); _avKurse.Add(k); SichereVersorgungsverbindungen();
        }
        public void TestUebertrageVerbindungen(Unity.Entities.Entity alt, Unity.Entities.Entity neu, Unity.Entities.Entity carrier)
            => UebertrageVersorgungsnachweise(alt,neu,carrier);
        public int TestLadeVerbindungen(Unity.Entities.Entity lot)
        { _avVerbindungen.Clear(); LadeVersorgungsverbindungen(lot); return _avVerbindungen.Count; }
    }
}

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private enum AvPhase { Idle, LotWarten }
        private AvPhase _avPhase;
        private Unity.Entities.Entity _avTraeger;
        private int _avNochOffeneNetze;
        private readonly System.Collections.Generic.List<AvKurs> _avGebaut = new();
        private readonly System.Collections.Generic.List<int> _avTrassen = new(), _avAusstehend = new();
        private readonly System.Collections.Generic.Dictionary<int,int> _avNetzstand = new();
        public void TestAvLauf(Unity.Entities.Entity carrier)
        { _avGeneration=World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().Register.Generation; _avTraeger=carrier; _avPhase=AvPhase.LotWarten; _avKurse.Add(new()); }
        public bool TestAvGueltig() => AvGenerationGueltig();
        public bool TestAvLeer => _avKurse.Count==0 && _avTraeger==Unity.Entities.Entity.Null && _avPhase==AvPhase.Idle;
    }
}
