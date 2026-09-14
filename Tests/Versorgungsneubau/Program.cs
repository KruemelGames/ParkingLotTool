using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Game.Net;
using Game.Common;
using Game.Tools;

// ECS-Testdouble: testet die originale Tools-Quelldatei, keine nachgebaute
// Ablaufentscheidung. Native Barrieren und CS2-Simulation bleiben Ingame-Test.
namespace Unity.Collections { public enum Allocator { Temp } }
namespace Unity.Entities {
 public readonly record struct Entity(int Index, int Version=1) { public static Entity Null => default; }
 public readonly record struct ComponentType(Type Type,bool Excluded) {
  public static ComponentType ReadOnly<T>()=>new(typeof(T),false);
  public static ComponentType Exclude<T>()=>new(typeof(T),true);
 }
 public sealed class FakeArray : List<Entity>, IDisposable { public void Dispose(){} }
 public sealed class EntityQuery {
  public FakeManager Manager; public ComponentType[] Types;
  public FakeArray ToEntityArray(Unity.Collections.Allocator _) {
   var result=new FakeArray();
   result.AddRange(Manager.Data.Where(x=>Types.All(t=>x.Value.ContainsKey(t.Type)!=t.Excluded)).Select(x=>x.Key));
   return result;
  }
 }
 public sealed class FakeManager {
  public readonly Dictionary<Entity,Dictionary<Type,object>> Data=new();
  public void Add<T>(Entity e,T value) { if(!Data.ContainsKey(e))Data[e]=new();Data[e][typeof(T)]=value; }
  public bool Exists(Entity e)=>Data.ContainsKey(e);
  public bool HasComponent<T>(Entity e)=>Exists(e)&&Data[e].ContainsKey(typeof(T));
  public T GetComponentData<T>(Entity e)=>(T)Data[e][typeof(T)];
  public bool HasBuffer<T>(Entity e)=>HasComponent<List<T>>(e);
  public List<T> GetBuffer<T>(Entity e,bool _)=>GetComponentData<List<T>>(e);
 }
}
namespace Game.Common { public struct Deleted {} }
namespace Game.Tools { public struct Temp {} }
namespace Game.Net {
 public struct Edge { public Entity m_Start,m_End; }
 public struct ConnectedEdge { public Entity m_Edge; }
}
namespace ParkingLotTool.Tools {
 public struct ParkingLotVersorgungsleitung { public Entity Lot,Carrier; }
 public static class Mod { public static FakeLog log=new(); }
 public sealed class FakeLog { public void Info(string text){} }
 public sealed partial class ParkingLotToolSystem {
  private FakeManager EntityManager=new();
  private EntityQuery GetEntityQuery(params ComponentType[] types)=>new(){Manager=EntityManager,Types=types};
  private bool VersorgungsentityLebt(Entity e)=>e!=Entity.Null&&EntityManager.Exists(e)
    &&!EntityManager.HasComponent<Deleted>(e)&&!EntityManager.HasComponent<Temp>(e);
  public static void Main() { new ParkingLotToolSystem().Run(); }
  private void Run() {
   var count=0;
   void Check(bool value,string name) {count++;if(!value)throw new Exception("FEHLER: "+name);}
   var lot=new Entity(1);var carrier=new Entity(2);var node=new Entity(3);
   var own=new Entity(4);var manual=new Entity(5);var other=new Entity(6);
   var street=new Entity(7);
   EntityManager.Add(carrier,0);
   EntityManager.Add(own,new Edge{m_Start=node,m_End=new Entity(8)});
   EntityManager.Add(own,new ParkingLotVersorgungsleitung{Lot=lot,Carrier=carrier});
   var buffer=new List<ConnectedEdge>{new(){m_Edge=own}};EntityManager.Add(node,buffer);
   Check(IstReinerAutomatischerVersorgungsknoten(node,lot),"eigener automatischer Anschluss wird ersetzt");
   Check(!IstReinerAutomatischerVersorgungsknoten(node,other),"anderes Lot erhalten");
   EntityManager.Add(manual,new Edge{m_Start=node,m_End=new Entity(9)});
   buffer.Add(new(){m_Edge=manual});
   Check(!IstReinerAutomatischerVersorgungsknoten(node,lot),"gemischter manueller Anschluss bleibt");
   buffer.RemoveAt(0);
   Check(!IstReinerAutomatischerVersorgungsknoten(node,lot),"rein manueller Anschluss bleibt");
   buffer.Clear();buffer.Add(new(){m_Edge=own});
   EntityManager.Add(street,new Edge{m_Start=new Entity(20),m_End=new Entity(21)});
   buffer.Add(new(){m_Edge=street});
   Check(IstReinerAutomatischerVersorgungsknoten(node,lot),"seitliche Strassenreferenz ist kein fremder Leitungsendpunkt");
   buffer.Add(new(){m_Edge=new Entity(999)});
   Check(IstReinerAutomatischerVersorgungsknoten(node,lot),"veraltete Referenz stoert nicht");
   Check(!IstReinerAutomatischerVersorgungsknoten(new Entity(999),lot),"fehlender Knoten bleibt unangetastet");
   EntityManager.Add(other,new Edge{m_Start=node,m_End=new Entity(30)});
   EntityManager.Add(other,new ParkingLotVersorgungsleitung{Lot=new Entity(99),Carrier=new Entity(98)});
   var temp=new Entity(40);EntityManager.Add(temp,new Edge());EntityManager.Add(temp,new Temp());
   EntityManager.Add(temp,new ParkingLotVersorgungsleitung{Lot=lot,Carrier=carrier});
   AvMerkeAbriss(lot,carrier);
   Check(_avAbrissKanten.SequenceEqual(new[]{own}),"nur dauerhafte eigene Kanten im Abrissauftrag");
   Check(!AvAbrissFertig(),"kein Neubau vor Traegerabriss");
   EntityManager.Data.Remove(carrier);
   Check(!AvAbrissFertig(),"kein Neubau vor Leitungsabriss");
   EntityManager.Add(own,new Deleted());
   Check(!AvAbrissFertig(),"Deleted allein reicht nicht");
   EntityManager.Data.Remove(own);
   EntityManager.Add(new Entity(own.Index,own.Version+1),new Edge());
   Check(AvAbrissFertig(),"vollstaendig entfernter Abriss gibt Neubau frei trotz wiederverwendetem Index");
   Check(EntityManager.Exists(manual)&&EntityManager.Exists(other),"fremde Leitungen nicht angefasst");
   Check(_avAbrissKanten.Count==0&&_avAbrissTraeger==Entity.Null,"fertiger Auftrag bereinigt");
   AvMerkeAbriss(lot,carrier);AvMerkeAbriss(Entity.Null,Entity.Null);
   Check(AvAbrissFertig(),"normaler Neubau erbt keinen alten Abrissauftrag");

   // KNOTENWARTEN - die Absturzursache vom 2026-09-14.
   //
   // Der Aufraeumer markiert mit Absicht nur KANTEN; Knoten selbst zu
   // markieren war der Absturz vom 2026-09-10. CS2 raeumt die verwaisten
   // Knoten danach selbst ab, aber ein paar Frames spaeter. Faengt der
   // Neubau in diesem Fenster an, sucht GenerateEdgesSystem den Knoten des
   // Kursendes ueber die POSITION - und findet genau den sterbenden, denn
   // die neue Leitung beginnt dort, wo die alte begann.
   //
   // Geprueft werden beide Gestalten des Fensters: der schon als geloescht
   // markierte Knoten und der noch unauffaellige ohne jede Kante.
   var altKante=new Entity(50);var unser=new Entity(51);var strasse=new Entity(52);
   EntityManager.Add(altKante,new Edge{m_Start=unser,m_End=strasse});
   EntityManager.Add(altKante,new ParkingLotVersorgungsleitung{Lot=lot,Carrier=carrier});
   var unserPuffer=new List<ConnectedEdge>{new(){m_Edge=altKante}};
   EntityManager.Add(unser,unserPuffer);
   EntityManager.Add(strasse,new List<ConnectedEdge>{new(){m_Edge=altKante},new(){m_Edge=street}});
   AvMerkeAbriss(lot,carrier);
   Check(_avAbrissKnoten.Contains(unser)&&_avAbrissKnoten.Contains(strasse),
     "beide Endknoten der alten Leitung werden gemerkt");
   EntityManager.Data.Remove(altKante);
   EntityManager.Add(unser,new Deleted());
   Check(!AvAbrissFertig(),"als geloescht markierter Endknoten haelt den Neubau auf");
   EntityManager.Data[unser].Remove(typeof(Deleted));unserPuffer.Clear();
   Check(!AvAbrissFertig(),"Endknoten ohne Kante haelt den Neubau auf, auch ohne Deleted");
   EntityManager.Data.Remove(unser);
   Check(AvAbrissFertig(),"lebender Strassenknoten mit Kanten gibt den Neubau frei");
   Check(_avAbrissKnoten.Count==0,"fertiger Auftrag bereinigt auch die Knoten");
   Console.WriteLine($"Versorgungsneubau: {count} Pruefungen, 0 Fehler.");
  }
 }
}
