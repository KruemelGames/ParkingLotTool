from pathlib import Path
import subprocess,tempfile
repo=Path(__file__).resolve().parents[2]
out=Path(tempfile.gettempdir())/'plt-vegetation-catalog';out.mkdir(exist_ok=True)
source=(repo/'Tools/ParkingLotVegetationUI.cs').read_text(encoding='utf-8')
start=source.index('                    if (!prefabs.TryGetPrefab')
end=source.index('                    bool tree',start)
filters=source[start:end]
(out/'test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
stubs=r'''using System;using System.Linq;using System.Collections.Generic;
class PrefabBase { public string Name; }
class ObjectGeometryPrefab:PrefabBase { public object[] m_Meshes; }
class StaticObjectPrefab:ObjectGeometryPrefab {}
struct PlaceholderObjectData{} struct PlaceholderObjectElement{}
class Catalog {
 public Dictionary<int,PrefabBase> values=new();public HashSet<int> placeholders=new(),elements=new();
 public bool TryGetPrefab<T>(int e,out PrefabBase p)=>values.TryGetValue(e,out p);
 public bool HasComponent<T>(int e)=>placeholders.Contains(e);
 public bool HasBuffer<T>(int e)=>elements.Contains(e);
}
class Program {
 static void Main(){var prefabs=new Catalog();var EntityManager=prefabs;
 prefabs.values[1]=new StaticObjectPrefab{Name="OakTree01",m_Meshes=new object[6]};
 prefabs.values[2]=new StaticObjectPrefab{Name="GreenBushWild01",m_Meshes=new object[1]};
 prefabs.values[3]=new StaticObjectPrefab{Name="Road Tree Placeholder",m_Meshes=null};prefabs.placeholders.Add(3);
 prefabs.values[4]=new StaticObjectPrefab{Name="GardenBedNarrowRandom01",m_Meshes=new object[0]};prefabs.elements.Add(4);
 prefabs.values[5]=new StaticObjectPrefab{Name="PlaceholderWithMesh",m_Meshes=new object[1]};prefabs.placeholders.Add(5);
 prefabs.values[6]=new StaticObjectPrefab{Name="EmptyPlant",m_Meshes=null};
 prefabs.values[7]=new PrefabBase{Name="Other"};
 var accepted=new List<string>();foreach(var entity in Enumerable.Range(1,8)){
'''
for label,code in [('Original',filters),('Platzhalter',filters.replace('if (EntityManager.HasComponent<PlaceholderObjectData>(entity)','if (false').replace('|| EntityManager.HasBuffer<PlaceholderObjectElement>(entity)','|| false')),('LeeresModell',filters.replace('|| objectPrefab.m_Meshes == null || objectPrefab.m_Meshes.Length == 0',''))]:
 (out/'Program.cs').write_text(stubs+code+'accepted.Add(prefab.Name); } if(string.Join(",",accepted)!="OakTree01,GreenBushWild01")throw new Exception(string.Join(",",accepted));Console.WriteLine("2 echte Pflanzen aus 8 Kandidaten");}}',encoding='utf-8')
 result=subprocess.run(['dotnet','run','-c','Release','--project',str(out/'test.csproj')],capture_output=True,text=True)
 assert (result.returncode==0)==(label=='Original'),(label,result.stdout,result.stderr)
 print(label, 'bestanden' if result.returncode==0 else 'Mutation erkannt')
