from pathlib import Path
import tempfile,subprocess
r=Path(__file__).resolve().parents[2];out=Path(tempfile.gettempdir())/'plt-vegetation-receipt';out.mkdir(exist_ok=True)
def method(file,name):
 s=(r/'Tools'/file).read_text(encoding='utf-8');start=s.index('        private '+name);i=s.index('{',start);end=i+1;level=1
 while level:
  level+=(s[end]=='{')-(s[end]=='}');end+=1
 return s[start:end]
methods=method('ParkingLotVegetationBuild.cs','VegetationReceipt ReadVegetation')+method('ParkingLotVegetationBuild.cs','void WriteVegetation')+method('ParkingLotEditMode.cs','static void AddBuildText')+method('ParkingLotEditMode.cs','static bool TryReadBuildText')
(out/'test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>F:/Games/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed/Newtonsoft.Json.dll</HintPath></Reference></ItemGroup></Project>')
stubs=r'''using System;using System.Text;using System.Collections.Generic;using Newtonsoft.Json;
struct Entity { public int Index;public static Entity Null=>default;public static implicit operator Entity(int i)=>new Entity{Index=i};public static bool operator==(Entity a,Entity b)=>a.Index==b.Index;public static bool operator!=(Entity a,Entity b)=>!(a==b);public override bool Equals(object o)=>o is Entity e&&this==e;public override int GetHashCode()=>Index;}
class DynamicBuffer<T>:List<T>{public int Length=>Count;}
struct ParkingLotBuildText{public const int CurrentVersion=1;public int Version,Kind,Index;public byte Value;}
struct ParkingLotVegetationReceipt{public byte Value;}
class VegetationReceipt{public int Version=1;public string Options,Signature;}
class Manager {public Dictionary<(int,Type),object> data=new();public bool Exists(Entity e)=>e.Index!=0;public bool HasBuffer<T>(Entity e)=>data.ContainsKey((e.Index,typeof(T)));public DynamicBuffer<T> GetBuffer<T>(Entity e,bool ro=false)=>(DynamicBuffer<T>)data[(e.Index,typeof(T))];public DynamicBuffer<T> AddBuffer<T>(Entity e){var b=new DynamicBuffer<T>();data[(e.Index,typeof(T))]=b;return b;}}
static class Mod{public static Log log=new();}class Log{public void Info(string s){}public void Warn(string s){}}
class Program{Manager EntityManager=new();VegetationReceipt _vegetationReceipt;
static void Main()=>new Program().Test();
void Test(){Entity lot=1;EntityManager.AddBuffer<ParkingLotBuildText>(lot);
foreach(var json in new[]{"{\"Enabled\":true,\"Line\":true,\"Density\":85,\"Ages\":32,\"Species\":[\"StaticObjectPrefab:OakTree01\",\"Grün\"]}","{\"Enabled\":true,\"Line\":false,\"Density\":20,\"Ages\":2,\"Species\":[]}"}) {
EntityManager.GetBuffer<ParkingLotBuildText>(lot).Clear();AddBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot),1,"Belag");
_vegetationReceipt=new VegetationReceipt{Options=json,Signature=json};WriteVegetation(lot);
if(ReadVegetation(lot)?.Options!=json)throw new Exception("Rueckladen falsch");
EntityManager.data.Remove((lot.Index,typeof(ParkingLotVegetationReceipt)));
if(ReadVegetation(lot)?.Options!=json)throw new Exception("Hauptbauzettel unvollstaendig");
WriteVegetation(lot);EntityManager.data.Remove((lot.Index,typeof(ParkingLotBuildText)));
if(ReadVegetation(lot)?.Options!=json)throw new Exception("Altformat nicht lesbar");EntityManager.AddBuffer<ParkingLotBuildText>(lot);
}Console.WriteLine("Zwei unterschiedliche Rezepte: neue/alte Speicherung und Rueckladen bestanden");}
'''
for label,code in [('Original',methods),('FehlenderHauptzettel',methods.replace('AddBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot),4,saved);',''))]:
 (out/'Program.cs').write_text(stubs+code+'}',encoding='utf-8');x=subprocess.run(['dotnet','run','-c','Release','--project',str(out/'test.csproj')],capture_output=True,text=True)
 assert (x.returncode==0)==(label=='Original'),(label,x.stdout,x.stderr)
 print(label,'bestanden' if x.returncode==0 else 'Mutation erkannt')
