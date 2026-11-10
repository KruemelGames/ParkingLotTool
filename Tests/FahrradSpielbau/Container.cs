using System;
using System.Collections.Generic;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

// Nur API-Datencontainer. Die echten Fabrik-/Bau-Methoden kommen aus Tools;
// kein Test behauptet, ECS-Generatoren oder LocalConnect im Spiel auszufuehren.
namespace Game.Common { public struct Owner { public Unity.Entities.Entity m_Owner; } }
namespace Game.Objects { public struct SubObject { public Unity.Entities.Entity m_SubObject; } }
namespace Game.Economy { public enum ResourceInEditor { Money } }
namespace Game.Simulation { public struct TerrainHeightData { } }
namespace Game.Net { [Flags] public enum RoadTypes { Car=1,Bicycle=16 } public struct SubNet { public Unity.Entities.Entity m_SubNet; } }
namespace Game.Areas { public struct SubArea { public Unity.Entities.Entity m_Area; } }
namespace Game.Buildings
{
    public struct CarParkingFacility { }
    public struct BicycleParkingFacility { }
    public struct ParkingFacility { }
}
namespace Game.Prefabs
{
    public class AreaPrefab : PrefabBase { public UnityEngine.Color m_Color,m_EdgeColor,m_SelectionColor,m_SelectionEdgeColor; }
    public class LotPrefab : AreaPrefab { public float m_MaxRadius; public bool m_AllowOverlap,m_AllowEditing; }
    public class SpacePrefab : AreaPrefab { }
    public class CityServiceBuilding : ComponentBase { public ServiceUpkeepItem[] m_Upkeeps; }
    public class ServiceUpkeepItem { public ResourceStackInEditor m_Resources; public bool m_ScaleWithUsage; }
    public struct ResourceStackInEditor { public Game.Economy.ResourceInEditor m_Resource; public int m_Amount; }
    public class ParkingFacility : ComponentBase
    { public Game.Net.RoadTypes m_RoadTypes; public int m_GarageMarkerCapacity; public float m_ComfortFactor; }
    public enum RouteConnectionType { None,Road,Pedestrian }
    public class NavigationArea : ComponentBase
    { public RouteConnectionType m_ConnectionType,m_SecondaryType; public Game.Net.RoadTypes m_RoadTypes; }
    public struct ObjectGeometryData { }
    public struct PlaceableObjectData { }
    public class StaticObjectPrefab : PrefabBase { }
    public readonly record struct PrefabID(string Type,string Name);
    public partial class PrefabSystem
    {
        internal readonly Dictionary<string,PrefabBase> Assets=new();
        internal readonly Dictionary<PrefabBase,Unity.Entities.Entity> Entities=new();
        public bool TryGetPrefab(PrefabID id,out PrefabBase p)=>Assets.TryGetValue(id.Name,out p);
        public bool TryGetEntity(PrefabBase p,out Unity.Entities.Entity e)=>Entities.TryGetValue(p,out e);
    }
}
namespace Unity.Entities
{
    public interface IComponentData { }
    public readonly record struct Entity(int Index) { public static Entity Null=>default; }
    public sealed class Buffer<T> : List<T> { public int Length=>Count; }
    public sealed class EntityManager
    {
        private readonly Dictionary<(Entity,Type),object> values=new();
        public bool HasComponent<T>(Entity e)=>values.ContainsKey((e,typeof(T)));
        public bool HasBuffer<T>(Entity e)=>values.ContainsKey((e,typeof(Buffer<T>)));
        public bool Exists(Entity e)=>e!=Entity.Null;
        public T GetComponentData<T>(Entity e)=>(T)values[(e,typeof(T))];
        public void AddComponent<T>(Entity e)=>values[(e,typeof(T))]=default(T);
        public void AddComponentData<T>(Entity e,T data)=>values[(e,typeof(T))]=data;
        public void RemoveComponent<T>(Entity e)=>values.Remove((e,typeof(T)));
        public Buffer<T> AddBuffer<T>(Entity e) {var b=new Buffer<T>();values[(e,typeof(Buffer<T>))]=b;return b;}
        public Buffer<T> GetBuffer<T>(Entity e)=>(Buffer<T>)values[(e,typeof(Buffer<T>))];
    }
    public sealed class World { public T GetOrCreateSystemManaged<T>() where T:new()=>new T(); }
}
namespace ParkingLotTool.Tools
{
    public static class Mod { public static readonly Logger log=new(); }
    public struct ParkingLotBuildReceipt { public Fahrzeugart Fahrzeugart; public Fahrradlayout Fahrradlayout; }
    public static class ParkingLotEconomySystem { public static int CalculateUpkeep(int n)=>48*n+438; }
    public class Logger { public readonly List<string> Zeilen=new(); public void Info(string s)=>Zeilen.Add(s);public void Warn(string s)=>Zeilen.Add(s); }
    public class ParkingLotFahrradPrefabSystem
    {
        public bool TryLot(out Unity.Entities.Entity e) {e=new(900);return true;}
        public bool TryLot(Fahrradlayout layout,out Unity.Entities.Entity e) {e=new(900);return true;}
        public bool TryWeg(bool zugang,double breite,out Unity.Entities.Entity e) {e=new(zugang?901:breite<2.75?903:902);return true;}
    }
    public sealed partial class ParkingLotToolSystem
    {
        internal readonly record struct Objekt(string Art,int Index,Unity.Entities.Entity Prefab,double2 Center,double2 Facing);
        internal readonly record struct Kurs(string Art,int Index,Unity.Entities.Entity Prefab,float2 A,float2 B,float? Starthoehe=null);
        internal readonly List<Objekt> Objekte=new(); internal readonly List<Kurs> Kurse=new();
        internal readonly Unity.Entities.EntityManager EntityManager=new();
        private readonly Unity.Entities.World World=new();
        private readonly PrefabSystem _prefabSystem=new();
        private ParkingLayout _areaPreviewLayout;
        private LayoutSettings _areaPreviewSettings;
        private Unity.Entities.Entity _lotOwner=new(100),_lotCarrier=new(101);
        private sealed class AreaTransferRecord {internal string Kind;internal Unity.Entities.Entity MaterializedEntity;}
        private readonly List<AreaTransferRecord> _areaTransferRecords=new();
        private float2[][] _vorflaechen,_vorflaechenSicht;
        private int[] _vorflaechenArt;
        private Kontext _baukontext;
        private UI _uiSystem;
        private class Kontext {public Zettel Zettel;}
        private class Zettel {public bool SurfaceApronOn=true,SurfaceRoadOn=true;}
        private class UI {public bool VorflaecheAn=true,FlaecheStrasseAn=true;}
        private bool SucheStadtstrasseFuerGasse(float2 a,float2 d,out float2 mitte,out float halb,out float strahl,
            out Unity.Entities.Entity e,out float t,out float hoehe)
        {mitte=a+d*8;halb=strahl=4;e=new(500);t=.5f;hoehe=.2f;return true;}
        private bool CreateBayDecalDefinition(string label,int i,Unity.Entities.Entity e,double2 p,double2 v,
            ref Game.Simulation.TerrainHeightData h,ref Unity.Mathematics.Random r)
        {Objekte.Add(new(label,i,e,p,v));return true;}
        private bool CreateCourseDefinition(string label,int i,float2 a,float2 b,Unity.Entities.Entity e,
            ref Game.Simulation.TerrainHeightData h,Dictionary<(long,long),float> heights,ref Unity.Mathematics.Random r)
        {if(math.distance(a,b)<1)throw new Exception("Unbaubarer Kurs");
            var key=((long)math.round(a.x*40),(long)math.round(a.y*40));
            Kurse.Add(new(label,i,e,a,b,heights.TryGetValue(key,out var h0)?h0:null));return true;}
        private static void MerkeHoehe(float2 p,float h,Dictionary<(long,long),float> heights)
            =>heights[((long)math.round(p.x*40),(long)math.round(p.y*40))]=h;
        internal void Baue(FahrradPlan p,LayoutSettings settings=null,float2[] polygon=null)
        {
            _areaPreviewLayout=new ParkingLayout{Fahrrad=p,Grundstueck=polygon??Array.Empty<float2>()};
            _areaPreviewSettings=settings;
            foreach(var o in p.Objekte)
            foreach(var n in new[]{o.Prefab,o.BodenPrefab})
            {
                if(n==null || _prefabSystem.Assets.ContainsKey(n))continue;
                var asset=new StaticObjectPrefab{name=n};var e=new Unity.Entities.Entity(_prefabSystem.Assets.Count+1);
                _prefabSystem.Assets[n]=asset;_prefabSystem.Entities[asset]=e;
                EntityManager.AddComponent<ObjectGeometryData>(e);EntityManager.AddComponent<PlaceableObjectData>(e);
            }
            foreach(var _ in FahrradObjekte(p,default)){}
            FahrradVorflaechen(p);
            foreach(var _ in FahrradNetze(p,default)){}
            _areaTransferRecords.Add(new(){Kind="FahrradGehflaeche",MaterializedEntity=new(102)});
            EntityManager.AddComponent<Game.Buildings.BicycleParkingFacility>(_lotOwner);
            FahrradTraeger();
        }
        internal bool TraegerRichtig=>EntityManager.HasComponent<Game.Buildings.ParkingFacility>(_lotCarrier)
            && EntityManager.HasComponent<Game.Buildings.BicycleParkingFacility>(_lotCarrier)
            && !EntityManager.HasComponent<Game.Buildings.CarParkingFacility>(_lotCarrier)
            && !EntityManager.HasComponent<Game.Buildings.BicycleParkingFacility>(_lotOwner);
        internal bool NavigationRichtig=>EntityManager.GetBuffer<Game.Areas.SubArea>(_lotCarrier).Count==1
            && EntityManager.GetBuffer<Game.Areas.SubArea>(_lotCarrier)[0].m_Area==new Unity.Entities.Entity(102);
        internal float2[][] Vorflaechen=>_vorflaechen;
    }
}
