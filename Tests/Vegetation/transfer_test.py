from pathlib import Path
import tempfile,subprocess
repo=Path(__file__).resolve().parents[2];out=Path(tempfile.gettempdir())/'plt-vegetation-transfer';out.mkdir(exist_ok=True)
source=(repo/'Tools/ParkingLotVegetationBuild.cs').read_text(encoding='utf-8');start=source.index('        private void TransferVegetation(');opening=source.index('{',start);level=1;end=opening+1
while level:
 level+=(source[end]=='{')-(source[end]=='}');end+=1
method=source[start:end]
start=source.index('        private void SetVegetationOwner(');opening=source.index('{',start);level=1;end=opening+1
while level:
 level+=(source[end]=='{')-(source[end]=='}');end+=1
helper=source[start:end]
(out/'test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
stubs=r'''
using System;
using System.Linq;
using System.Collections.Generic;
namespace Game.Prefabs {public struct PlantData{} public struct PrefabRef{public Unity.Entities.Entity m_Prefab;}}
namespace Game.Common{public struct Hidden{} public struct Owner{public Unity.Entities.Entity m_Owner;}}
namespace Game.Rendering{public struct BatchesUpdated{}}
namespace Game.Objects{public struct SubObject{public Unity.Entities.Entity m_SubObject;}public struct Attached{public Unity.Entities.Entity Carrier;public Attached(Unity.Entities.Entity c,Unity.Entities.Entity unused,float n){Carrier=c;}}}
namespace Unity.Collections {public enum Allocator{Temp}}
namespace Unity.Entities {
 public struct Entity{public int Id;public static Entity Null=>default;public static bool operator==(Entity a,Entity b)=>a.Id==b.Id;public static bool operator!=(Entity a,Entity b)=>a.Id!=b.Id;public override bool Equals(object o)=>o is Entity e&&e==this;public override int GetHashCode()=>Id;public static implicit operator Entity(int i)=>new Entity{Id=i};}
 public class Buffer<T>:List<T>{public int Length=>Count;}
 public class EntityManager{public Dictionary<(int,Type),object> data=new();public bool Exists(Entity e)=>e.Id!=0;public bool HasComponent<T>(Entity e)=>data.ContainsKey((e.Id,typeof(T)));public T GetComponentData<T>(Entity e)=>(T)data[(e.Id,typeof(T))];public void SetComponentData<T>(Entity e,T v)=>data[(e.Id,typeof(T))]=v;public void AddComponentData<T>(Entity e,T v)=>SetComponentData(e,v);public void AddComponent<T>(Entity e)=>SetComponentData(e,default(T));public void RemoveComponent<T>(Entity e)=>data.Remove((e.Id,typeof(T)));public bool HasBuffer<T>(Entity e)=>data.ContainsKey((e.Id,typeof(List<T>)));public Buffer<T> GetBuffer<T>(Entity e)=>(Buffer<T>)data[(e.Id,typeof(List<T>))];public void AddBuffer<T>(Entity e)=>data[(e.Id,typeof(List<T>))]=new Buffer<T>();}
 public class Query{public Entity[] parts;public Entries ToEntityArray(Unity.Collections.Allocator a)=>new Entries{parts=parts};}
 public class Entries:IDisposable{public Entity[] parts;public IEnumerator<Entity> GetEnumerator()=>((IEnumerable<Entity>)parts).GetEnumerator();public void Dispose(){}}
}
namespace ParkingLotTool.Tools {
 using Unity.Entities;using Unity.Collections;using Game.Prefabs;using Game.Common;using Game.Rendering;
 public struct ParkingLotPartRelation{public Entity Lot,Carrier;}
 public static class Mod{public static Log log=new Log();}public class Log{public void Info(string s){}}
 public partial class ParkingLotToolSystem {
 public EntityManager EntityManager=new EntityManager();public bool _vegetationPreserve;public Query _editRelatedParts=new Query();public HashSet<Entity> _hiddenByEdit=new();
 public void Test(){
 var em=EntityManager;em.SetComponentData((Entity)10,new ParkingLotPartRelation{Lot=1,Carrier=2});em.SetComponentData((Entity)10,new PrefabRef{m_Prefab=100});em.AddComponent<PlantData>(100);em.AddComponent<Hidden>(10);em.SetComponentData((Entity)10,new Game.Objects.Attached(2,Entity.Null,0));
 em.SetComponentData((Entity)11,new ParkingLotPartRelation{Lot=1,Carrier=2});em.SetComponentData((Entity)11,new PrefabRef{m_Prefab=101});
 em.SetComponentData((Entity)12,new ParkingLotPartRelation{Lot=8,Carrier=9});
 em.AddBuffer<Game.Objects.SubObject>(2);em.GetBuffer<Game.Objects.SubObject>(2).Add(new Game.Objects.SubObject{m_SubObject=10});em.AddBuffer<Game.Objects.SubObject>(4);
 _editRelatedParts.parts=new Entity[]{10,11,12};_hiddenByEdit.Add(10);
 TransferVegetation(1,3,4);Check(em.GetComponentData<ParkingLotPartRelation>(10).Lot==1,"Aenderung: keine Uebernahme");
 _vegetationPreserve=true;TransferVegetation(1,3,4);
 Check(em.GetComponentData<Owner>(10).m_Owner==4,"Pflanze Owner neuer Traeger");
 Check(em.GetComponentData<Owner>(4).m_Owner==3,"Traeger Owner neues Lot");
 // Native OverrideSystem.CheckObject/AreaIterator: Owner bis zum obersten Entity verfolgen.
 Entity top=10;while(em.HasComponent<Owner>(top))top=em.GetComponentData<Owner>(top).m_Owner;
 Check(top==3,"Eigene Flaeche wird vom Override-Test ausgenommen");
 Check(top!=8,"Fremde Flaeche bleibt Kollisionspartner");
 Check(em.GetComponentData<ParkingLotPartRelation>(10).Lot==3,"Pflanze gehoert neuem Lot");
 Check(em.GetComponentData<ParkingLotPartRelation>(10).Carrier==4,"Relation gehoert neuem Traeger");
 Check(em.GetComponentData<Game.Objects.Attached>(10).Carrier==4,"Attached gehoert neuem Traeger");
 Check(em.GetBuffer<Game.Objects.SubObject>(2).Count==0,"Alter Abriss hat keinen Pflanzenverweis");
 Check(em.GetBuffer<Game.Objects.SubObject>(4).Single().m_SubObject==10,"Neuer Traeger referenziert dieselbe Entity");
 Check(!em.HasComponent<Hidden>(10)&&!_hiddenByEdit.Contains(10),"Pflanze wieder sichtbar");
 Check(em.GetComponentData<ParkingLotPartRelation>(11).Lot==1,"Decal bleibt beim alten Lot");
 Check(em.GetComponentData<ParkingLotPartRelation>(12).Lot==8,"Nachbarlot bleibt unveraendert");
 }
 static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine(s);}
'''
for label,code in [('Original',method),('Mutation',method.replace('previous.RemoveAt(i);','{}')),('OwnerMutation',method.replace('SetVegetationOwner(part,carrier,next);',''))]:
 (out/'Program.cs').write_text(stubs+helper+code+'\n}}\nclass Program{static void Main()=>new ParkingLotTool.Tools.ParkingLotToolSystem().Test();}',encoding='utf-8')
 r=subprocess.run(['dotnet','run','--project',str(out/'test.csproj'),'-c','Release'],capture_output=True,text=True,encoding='utf-8',errors='replace')
 assert (r.returncode==0)==(label=='Original'),(label,r.stdout,r.stderr)
 print(label,r.stdout,r.stderr.splitlines()[:1])
