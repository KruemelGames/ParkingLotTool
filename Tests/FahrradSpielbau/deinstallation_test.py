from pathlib import Path
import subprocess,json
root=Path(__file__).resolve().parents[2]
ziel=root/'.codex-build/fahrrad/stufe4-deinstallation';ziel.mkdir(exist_ok=True)
s=(root/'Tools/ParkingLotStadtreinigung.cs').read_text(encoding='utf-8-sig')
def methode(signatur):
 start=s.index(signatur);i=s.index('{',start)+1;tiefe=1
 while tiefe:
  c=s[i]
  if s.startswith('//',i):i=s.index('\n',i);continue
  if s.startswith('/*',i):i=s.index('*/',i)+2;continue
  if c in ('"',"'"):
   q=c;i+=1
   while i<len(s):
    if s[i]=='\\':i+=2;continue
    if s[i]==q:break
    i+=1
  else:tiefe+=(c=='{')-(c=='}')
  i+=1
 return s[start:i]
code='\n'.join(methode(x) for x in ['internal bool Blockiert(', 'internal int EntferneAlle(', 'internal void ReisseAb(', 'private bool IstUnserLot('])
prefix=r'''
using System;using System.Collections.Generic;using System.Linq;
using Unity.Entities;using Unity.Collections;using Game.Tools;using Game.Prefabs;using ParkingLotTool.Tools;
namespace Unity.Entities { public readonly record struct Entity(int Index) { public static Entity Null=>default; } }
namespace Unity.Collections {
public enum Allocator {Temp}
public class Feld<T> : IDisposable {public T[] Werte;public int Length=>Werte.Length;public T this[int i]=>Werte[i];public void Dispose(){} }
public class NativeList<T> : List<T>,IDisposable {public NativeList(int n,Allocator a){}public int Length=>Count;public void Dispose(){} }
}
namespace Game.Prefabs { public class PrefabBase {public string name;}public struct PrefabRef {public Entity m_Prefab;} }
namespace Game.Tools {public class ToolSystem {public object activeTool;} }
namespace ParkingLotTool.Tools {
class Log {public void Info(string s){}public void Warn(string s){} }class Mod {public static Log log=new();}
class ParkingLotToolSystem {public const string LotOwnerPrefabName="PLT Parkplatzflaeche";public bool ArbeitetGerade;}
class ParkingLotSyncSystem {public bool ArbeitLaeuft;}
class ParkingLotFahrradPrefabBau {public const string Lotname="PLT Fahrradparkplatzflaeche",LotC="PLT Fahrradparkplatzflaeche C",LotD="PLT Fahrradparkplatzflaeche D";}
struct ParkingLotCarrierReference {}
class Manager {
 public readonly Dictionary<Entity,Entity> Prefabs=new();public readonly HashSet<Entity> Referenzen=new();
 public bool Exists(Entity e)=>Prefabs.ContainsKey(e);
 public bool HasComponent<T>(Entity e)=>typeof(T)==typeof(PrefabRef)?Prefabs.ContainsKey(e):Referenzen.Contains(e);
 public T GetComponentData<T>(Entity e)=>(T)(object)new PrefabRef {m_Prefab=Prefabs[e]};
}
class Prefabs { public readonly Dictionary<Entity,PrefabBase> Daten=new();public bool TryGetPrefab<T>(Entity e,out T p) where T:PrefabBase {p=Daten.GetValueOrDefault(e) as T;return p!=null;} }
class ParkingLotFahrradBetrieb { public static bool IstBestand(Manager em,Entity e)=>e.Index>=2&&e.Index<=4; }
class ParkingLotWaisenSystem {public Manager Em;public int Aufrufe;public bool VerbindeFahrradFuerAbriss(Entity e){Aufrufe++;Em.Referenzen.Add(e);return true;}}
class Welt {public readonly Dictionary<Type,object> Systeme=new();public T GetExistingSystemManaged<T>() where T:class=>Systeme.GetValueOrDefault(typeof(T)) as T;public T GetOrCreateSystemManaged<T>() where T:class=>GetExistingSystemManaged<T>();}
class Query {public int Gelesen;public Entity[] Werte;public void CompleteDependency(){Gelesen++;}public Feld<Entity> ToEntityArray(Allocator a)=>new(){Werte=Werte};}
class Abriss {public readonly List<Entity> Auftraege=new();public List<Entity> Listen;public bool Reihenfolge=true;public void Einreihen(List<Entity> q){Reihenfolge=q.All(Listen.Contains);Auftraege.AddRange(q);}}
class Probe {
readonly Manager EntityManager=new();readonly Welt World=new();readonly Prefabs _prefabSystem=new();readonly Query _flaechen=new();readonly List<Entity> Listen=new();readonly Abriss _abriss=new();
int _uebergangen,_erwartet,_rest,_ohneFortschritt,_durchgaenge,Warten;bool _gewarnt;
enum Knopfzustand {Nichts,Laeuft}void SetzeStand(Knopfzustand k,int a=0,int b=0,int c=0){}void Warte(){Warten++;}void FuelleBesitzlisten(Entity e){Listen.Add(e);}
Probe(){_abriss.Listen=Listen;_flaechen.Werte=new[]{new Entity(1),new Entity(2),new Entity(3),new Entity(4),new Entity(5)};
var namen=new[]{ParkingLotToolSystem.LotOwnerPrefabName,ParkingLotFahrradPrefabBau.Lotname,ParkingLotFahrradPrefabBau.LotC,ParkingLotFahrradPrefabBau.LotD,"BicycleParkingArea01"};
for(int i=0;i<namen.Length;i++){var e=new Entity(i+1);var p=new Entity(100+i);EntityManager.Prefabs[e]=p;_prefabSystem.Daten[p]=new PrefabBase{name=namen[i]};if(i!=2)EntityManager.Referenzen.Add(e);}
World.Systeme[typeof(ParkingLotWaisenSystem)]=new ParkingLotWaisenSystem{Em=EntityManager};}
'''
suffix=r'''
static int Main(){try{return Test();}catch(Exception e){Console.WriteLine("FEHLER: "+e);return 1;}}
static int Test(){int n=0,f=0;void P(bool ok,string text){n++;if(!ok){f++;Console.WriteLine("FEHLER: "+text);}}
foreach(var sync in new[]{-1,0,1})foreach(var aktiv in new[]{-1,0,1})foreach(var arbeit in new[]{false,true}){
var p=new Probe();if(sync>=0)p.World.Systeme[typeof(ParkingLotSyncSystem)]=new ParkingLotSyncSystem{ArbeitLaeuft=sync==1};
if(aktiv>=0){var w=new ParkingLotToolSystem{ArbeitetGerade=arbeit};p.World.Systeme[typeof(ParkingLotToolSystem)]=w;p.World.Systeme[typeof(ToolSystem)]=new ToolSystem{activeTool=aktiv==1?w:new object()};}
var sperre=sync==1 || aktiv==1&&arbeit;P(p.Blockiert()==sperre,"Sperrmatrix Sync/Werkzeug/Weltarbeit");
var anzahl=p.EntferneAlle();
if(sperre){P(anzahl==0&&p._flaechen.Gelesen==0&&p.Listen.Count==0&&p._abriss.Auftraege.Count==0&&p.Warten==1,"Annahmesperre verhindert 100% der Besitzer-/Abrissarbeit");}
else {P(anzahl==4,"Auto und A/C/D gehen an den Bulldozer");P(p._abriss.Auftraege.Count==4&&!p._abriss.Auftraege.Contains(new Entity(5)),"Vanilla wird nicht angefasst");P(p.Listen.Count==4&&p._abriss.Reihenfolge,"Besitzlisten stehen vor der Bulldozer-Uebergabe");P(p.World.GetExistingSystemManaged<ParkingLotWaisenSystem>().Aufrufe==1,"Fahrrad C ohne Mod-Relation wird exakt einmal angebunden");}
}
Console.WriteLine($"FAHRRAD-DEINSTALLATION: {n} Pruefungen, {f} Fehler");return f==0?0:1;}
}}
'''
(ziel/'Deinstallation.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>CS0169;CS0414</NoWarn></PropertyGroup></Project>')
faelle=[('normal',None,None),('annahme','if(Blockiert()) { Warte(); return 0; }',';'),('sync','if (sync != null && sync.ArbeitLaeuft) return true;',';'),('edit','&& werkzeug.ArbeitetGerade;','&& false;'),('bulldozer','_abriss.Einreihen(auftrag);',';'),('besitzlisten','FuelleBesitzlisten(lot);',';'),('c-name','|| wert.name == ParkingLotFahrradPrefabBau.LotC','|| false'),('waise','World.GetOrCreateSystemManaged<ParkingLotWaisenSystem>().VerbindeFahrradFuerAbriss(flaechen[i]);',';')]
erg=[]
for name,alt,neu in faelle:
 text=code
 if alt:
  assert text.count(alt)==1,(name,text.count(alt));text=text.replace(alt,neu)
 (ziel/'Program.cs').write_text(prefix+text+suffix,encoding='utf-8')
 with (ziel/(name+'.log')).open('w',encoding='utf-8') as out:
  p=subprocess.run(['dotnet','run','-c','Release','--project',str(ziel/'Deinstallation.csproj')],cwd=root,stdout=out,stderr=subprocess.STDOUT)
 log=(ziel/(name+'.log')).read_text(encoding='utf-8');ok=p.returncode==(0 if name=='normal' else 1) and 'FAHRRAD-DEINSTALLATION:' in log
 erg.append({'Lauf':name,'Exit':p.returncode,'Erwartet':ok});print(name,p.returncode,ok,flush=True)
(ziel/'Program.cs').write_text(prefix+code+suffix,encoding='utf-8')
(ziel/'ergebnisse.json').write_text(json.dumps(erg,indent=2),encoding='utf-8')
raise SystemExit(0 if all(e['Erwartet'] for e in erg) else 1)
