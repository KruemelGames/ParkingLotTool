using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Nur Container des Game-API. Der zu pruefende Algorithmus wird unveraendert
// aus Tools/ eingebunden. Kein Unity-/ECS-/Asset-/Spielverhalten simuliert.
namespace UnityEngine
{
    public class Object { public string name; }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
        public static ScriptableObject CreateInstance(Type t) => (ScriptableObject)Activator.CreateInstance(t);
    }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a) {this.r=r;this.g=g;this.b=b;this.a=a;} }
}
namespace Game.Prefabs
{
    public class ComponentBase : UnityEngine.ScriptableObject
    {
        public bool active = true;
        public PrefabBase prefab;
    }
    public class PrefabBase : ComponentBase
    {
        public T AddComponent<T>() where T : ComponentBase,new() => AddOrGetComponent<T>();
        public List<ComponentBase> components = new();
        public object asset;
        public PrefabBase() { prefab = this; }
        public T GetComponent<T>() where T : ComponentBase => components.OfType<T>().FirstOrDefault();
        public T AddOrGetComponent<T>() where T : ComponentBase, new()
        {
            var c = GetComponent<T>();
            if (c != null) return c;
            c = new T { prefab = this }; components.Add(c); return c;
        }
        public ComponentBase AddComponentFrom(ComponentBase from)
        {
            var c = (ComponentBase)Activator.CreateInstance(from.GetType());
            foreach (var f in from.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (f.Name != "prefab") f.SetValue(c, f.GetValue(from));
            c.prefab = this; components.Add(c); return c;
        }
    }
    public class NetGeometryPrefab : PrefabBase { public NetSectionInfo[] m_Sections; }
    public class PathwayPrefab : NetGeometryPrefab { public float m_SpeedLimit; }
    public class RoadPrefab : NetGeometryPrefab { public float m_SpeedLimit; public PrefabBase m_ZoneBlock; }
    public class NetSectionInfo { public NetSectionPrefab m_Section; public bool m_Flip; }
    public class NetSectionPrefab : PrefabBase { public NetPieceInfo[] m_Pieces; public NetSubSectionInfo[] m_SubSections; }
    public class NetSubSectionInfo { public NetSectionPrefab m_Section; }
    public class NetPieceInfo { public NetPiecePrefab m_Piece; }
    public class RenderPrefab : PrefabBase { public UnityEngine.Object geometryAsset; }
    public class NetPiecePrefab : RenderPrefab { public float m_Width,m_WidthOffset; }
    public class LodProperties : ComponentBase { public RenderPrefab[] m_LodMeshes; }
    public class NetLanePrefab : PrefabBase { public PathfindPrefab m_PathfindPrefab; }
    public class NetLaneGeometryPrefab : NetLanePrefab { public RenderPrefab[] m_Meshes; }
    public class NetLaneInfo { public NetLanePrefab m_Lane; public Unity.Mathematics.float3 m_Position; }
    public class NetPieceLanes : ComponentBase { public NetLaneInfo[] m_Lanes; }
    public class SecondaryLane : ComponentBase { public NetLaneInfo[] m_LeftLanes; }
    public class PathfindPrefab : PrefabBase { public bool m_TrackTrafficFlow; }
    public class CarLane : ComponentBase { public int m_RoadTypes; public Game.Net.RoadTypes m_RoadType; public float m_Width; }
    public class PedestrianLane : ComponentBase { public float m_Width; }
    public struct PathfindCostInfo
    {
        public float m_Time, m_Behaviour, m_Money, m_Comfort;
        public PathfindCostInfo(float t, float b, float m, float c)
            { m_Time = t; m_Behaviour = b; m_Money = m; m_Comfort = c; }
        public PathfindCosts ToPathfindCosts() => new() { m_Value = new(m_Time, m_Behaviour, m_Money, m_Comfort) };
    }
    public class CarPathfind : ComponentBase
    {
        public PathfindCostInfo m_DrivingCost;
        public PathfindCostInfo m_ParkingCost;
    }
    public class PedestrianPathfind : ComponentBase { public float m_Cost; }
    public class EditorAssetCategoryOverride : ComponentBase { public string[] m_IncludeCategories, m_ExcludeCategories; }
    public class UIObject : ComponentBase { }
    public class ObsoleteIdentifiers : ComponentBase { }
    public class SpawnableArea : ComponentBase { }
    public class SpawnableObject : ComponentBase { }
    public class SpawnableLane : ComponentBase { }
    public class SpawnableBuilding : ComponentBase { }
    public class PlaceholderArea : ComponentBase { }
    public class PlaceholderObject : ComponentBase { }
    public class PlaceholderLane : ComponentBase { }
    public class PlaceholderBuilding : ComponentBase { }
    public class AssetPackItem : ComponentBase { }
    public struct PathfindCosts { public Unity.Mathematics.float4 m_Value; }
    public struct PrefabRef { public Unity.Entities.Entity m_Prefab; }
    public struct NetLaneData { public Unity.Entities.Entity m_PathfindPrefab; }
    public struct PathfindCarData { public PathfindCosts m_DrivingCost; }
    public partial class PrefabSystem
    {
        public readonly Dictionary<Unity.Entities.Entity, string> Names = new();
        public string GetPrefabName(Unity.Entities.Entity e) => Names[e];
    }
}
