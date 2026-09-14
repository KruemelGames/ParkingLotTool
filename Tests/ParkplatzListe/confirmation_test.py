from pathlib import Path
import tempfile, subprocess
repo=Path(__file__).resolve().parents[2]
out=Path(tempfile.gettempdir())/'plt-confirm-test';out.mkdir(exist_ok=True)
(out/'test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>true</EnableDefaultCompileItems></PropertyGroup></Project>')
source=(repo/'Tools/ParkingLotBulldozeConfirmationPatch.cs').read_text(encoding='utf-8')
(out/'Program.cs').write_text(r'''
using System;
using System.Reflection;
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)] public class HarmonyPatch:Attribute {public HarmonyPatch(){} public HarmonyPatch(Type t,string s){} }
 public class HarmonyPrefix:Attribute{}
 public static class AccessTools { public static FieldInfo Field(Type t,string s)=>t.GetField(s,BindingFlags.Instance|BindingFlags.NonPublic); public static MethodInfo PropertySetter(Type t,string s)=>t.GetProperty(s).GetSetMethod(true); }
}
namespace Unity.Collections {public enum Allocator{Temp}}
namespace Unity.Jobs {public struct JobHandle {public void Complete(){} }}
namespace Game.Areas {public struct Area{}}
namespace Unity.Entities {
 public struct Entity {public int Id;public static Entity Null=>default;public static bool operator==(Entity a,Entity b)=>a.Id==b.Id;public static bool operator!=(Entity a,Entity b)=>a.Id!=b.Id;public override bool Equals(object o)=>o is Entity e&&e==this;public override int GetHashCode()=>Id;}
 public struct ComponentType {public static ComponentType ReadOnly<T>()=>default;}
 public class EntityManager {public Game.Tools.Temp Temp;public bool IsLot;public EntityQuery CreateEntityQuery(params ComponentType[] c)=>new EntityQuery(); public T GetComponentData<T>()=>default; public T GetComponentData<T>(Entity e)=>(T)(object)Temp;public bool Exists(Entity e)=>e.Id==2;public bool HasComponent<T>(Entity e)=>IsLot;}
 public class EntityQuery:IDisposable {public Entities ToEntityArray(Unity.Collections.Allocator a)=>new Entities();public void Dispose(){} }
 public class Entities:IDisposable {public System.Collections.Generic.IEnumerator<Entity> GetEnumerator(){yield return new Entity{Id=1};}public void Dispose(){} }
}
namespace Game.Tools {
 public enum ApplyMode{None,Apply} public enum TempFlags{None,Delete} public struct Temp {public TempFlags m_Flags;public Unity.Entities.Entity m_Original;}
 public class ToolBaseSystem {public ApplyMode applyMode{get;protected set;}=ApplyMode.Apply;}
 public class BulldozeToolSystem:ToolBaseSystem {private enum State{Default,Applying,Waiting,Confirmed,Cancelled} private State m_State;public string Status {get=>m_State.ToString();set=>m_State=Enum.Parse<State>(value);}public Unity.Entities.EntityManager EntityManager=new Unity.Entities.EntityManager();public Action EventConfirmationRequested;}
}
namespace ParkingLotTool {public static class Mod {public static Log log=new Log();public static Setting Optionen=new Setting();} public class Setting {public bool ParkplatzLoeschenBestaetigen=true;} public class Log {public void Warn(string s){} }}
namespace ParkingLotTool.Tools {public struct ParkingLotCarrierReference{}}
class Program {
 static Type P=typeof(ParkingLotTool.Tools.ParkingLotBulldozeConfirmationPatch);
 static void Update(Game.Tools.BulldozeToolSystem b)=>P.GetMethod("VorUpdate",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{b});
 static bool Apply(Game.Tools.BulldozeToolSystem b)=>(bool)P.GetMethod("VorAnwenden",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{b,new Unity.Jobs.JobHandle(),new Unity.Jobs.JobHandle()});
 static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine(s);}
 static void Main(){
 var b=new Game.Tools.BulldozeToolSystem();int dialogs=0;b.EventConfirmationRequested=()=>dialogs++;
 b.EntityManager.Temp=new Game.Tools.Temp{m_Flags=Game.Tools.TempFlags.Delete,m_Original=new Unity.Entities.Entity{Id=2}};
 Update(b);Check(Apply(b)&&dialogs==0,"Vanilla unveraendert");
 b.EntityManager.IsLot=true;Update(b);Check(!Apply(b)&&dialogs==1&&b.Status=="Waiting"&&b.applyMode==Game.Tools.ApplyMode.None,"PLT wartet vor Apply");
 ParkingLotTool.Mod.Optionen.ParkplatzLoeschenBestaetigen=false;Update(b);Check(Apply(b)&&dialogs==1,"Option aus: keine PLT-Abfrage");
 ParkingLotTool.Mod.Optionen.ParkplatzLoeschenBestaetigen=true;Update(b);Check(!Apply(b)&&dialogs==2,"Option an wirkt sofort");dialogs=1;
 b.Status="Confirmed";Update(b);b.Status="Default";Check(Apply(b)&&dialogs==1,"Ja gibt genau diesen Apply frei");
 Update(b);Check(!Apply(b)&&dialogs==2,"Naechster Parkplatz fragt erneut");
 b.Status="Cancelled";Update(b);Check(!Apply(b)&&dialogs==3,"Nein hinterlaesst keine Freigabe");
 b.Status="Confirmed";Update(b);b.Status="Default";Update(b);Check(!Apply(b),"Werkzeugwechsel verliert alte Freigabe");
 b.EntityManager.Temp=new Game.Tools.Temp{m_Original=new Unity.Entities.Entity{Id=2}};Update(b);Check(Apply(b),"Keine Loeschvorschau: keine Abfrage");
 b.EntityManager.Temp=new Game.Tools.Temp{m_Flags=Game.Tools.TempFlags.Delete,m_Original=new Unity.Entities.Entity{Id=2}};b.EventConfirmationRequested=null;Update(b);Check(!Apply(b)&&b.Status=="Cancelled","Fehlender Dialog loescht nicht");
 }
}
''',encoding='utf-8')
for name,code in [('Original',source),('OptionMutation',source.replace('if (Mod.Optionen?.ParkplatzLoeschenBestaetigen == false) return true;', '')),('Mutation',source.replace('if (!parkplatz) return true;','return true;'))]:
 (out/'Patch.cs').write_text(code,encoding='utf-8')
 result=subprocess.run(['dotnet','run','--project',str(out/'test.csproj'),'-c','Release'],capture_output=True,text=True,encoding='utf-8',errors='replace')
 print(name,result.returncode,result.stdout,result.stderr)
 assert (result.returncode==0)==(name=='Original')
