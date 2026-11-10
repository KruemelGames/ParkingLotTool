from pathlib import Path
import subprocess,json
root=Path(__file__).resolve().parents[2]
ziel=root/'.codex-build/fahrrad/stufe3-wirtschaft'
ziel.mkdir(exist_ok=True)
source=(root/'Tools/ParkingLotEconomySystem.cs').read_text(encoding='utf-8-sig')
def methode(signatur):
    start=source.index(signatur)
    arrow=source.find('=>',start)
    brace=source.find('{',start)
    if arrow>=0 and arrow<brace:
        return source[start:source.index(';',arrow)+1]
    tiefe=1;ende=brace+1
    while tiefe:
        tiefe+=(source[ende]=='{')-(source[ende]=='}');ende+=1
    return source[start:ende]
methoden='\n'.join(methode(s) for s in ['internal static int CalculateUpkeep(', 'internal static int WirksamerUnterhalt(', 'internal int MonatsUnterhalt(', 'internal int MitBudget(', 'private int MesseKapazitaet('])
prefix=r'''
using System;
using System.Collections.Generic;
using ParkingLotTool.Tools;
using Game.Prefabs;
using Unity.Entities;
namespace Unity.Entities { public readonly record struct Entity(int Index) { public static Entity Null=>default; } }
namespace Game.Prefabs { public struct PrefabRef { public Entity m_Prefab; } }
namespace Game.Simulation { public class CityServiceBudgetSystem { public int Budget=100; public int GetServiceBudget(Entity e)=>Budget; } }
namespace Game.Vehicles { public static class VehicleUtils {
 public static readonly Dictionary<Entity,int> Kapazitaet=new(); public static readonly List<Entity> Aufrufe=new();
 public static void GetParkingData(object s,Entity e,ref int l,ref int c,ref int p,ref int f) {Aufrufe.Add(e);c=Kapazitaet.GetValueOrDefault(e);l=c>0?1:0;}
} }
namespace ParkingLotTool.Tools {
 public static class Mod { public static float UnterhaltFaktor=1; public static bool WirtschaftAn=true; }
 public struct ParkingLotCarrierReference { public Entity Carrier; }
 public class Manager { public readonly Dictionary<Entity,Entity> Carrier=new(); public bool Exists(Entity e)=>e!=Entity.Null;
  public bool HasComponent<T>(Entity e)=>Carrier.ContainsKey(e);
  public T GetComponentData<T>(Entity e)=>(T)(object)new ParkingLotCarrierReference{Carrier=Carrier[e]}; }
 public static class ParkingLotFahrradBetrieb { public static readonly HashSet<Entity> Fahrrad=new();public static bool Ist(Manager m,Entity e)=>Fahrrad.Contains(e); }
 public class Welt { public readonly Game.Simulation.CityServiceBudgetSystem Budget=new(); public T GetExistingSystemManaged<T>()=>(T)(object)Budget; }
 public class ParkingLotEconomySystem {
  private readonly Manager EntityManager=new();private readonly Welt World=new(); private Entity _roadsService=new(999);
  private int Loehne(Entity e)=>ParkingLotFahrradBetrieb.Ist(EntityManager,e)?0:42;
'''
suffix=r'''
 public static int Main() {
  var s=new ParkingLotEconomySystem(); var bike=new Entity(1);var auto=new Entity(2);var traeger=new Entity(3);
  ParkingLotFahrradBetrieb.Fahrrad.Add(bike);int n=0,fehler=0;
  void P(bool ok,string name) {n++;if(!ok){fehler++;Console.WriteLine("FEHLER: "+name);}}
  foreach(var faktor in new[]{0f,.5f,1f,1.5f,2f}) foreach(var budget in new[]{0,50,100,125,150}) {
   Mod.UnterhaltFaktor=faktor;s.World.Budget.Budget=budget;
   foreach(var kosten in new[]{2016,2400,3864,0,13}) {
    Mod.WirtschaftAn=true;
    P(s.MonatsUnterhalt(bike,kosten)==(int)Math.Round(Math.Round(kosten*(double)faktor)*budget/100.0),"Fahrrad volle Budgetkette");
    P(s.MonatsUnterhalt(auto,kosten)==(int)Math.Round((Math.Round(kosten*(double)faktor)+42)*budget/100.0),"Auto mit unveraendertem Lohnanteil");
    Mod.WirtschaftAn=false;
    P(s.MonatsUnterhalt(bike,kosten)==0,"Fahrrad Wirtschaft AUS kostet 0");
   }
  }
  for(int kap=0;kap<=2000;kap++) P(CalculateUpkeep(kap)==48*kap+438,"Autoformel");
  s.EntityManager.Carrier[bike]=traeger;
  foreach(var kap in new[]{168,192,336}) {
   Game.Vehicles.VehicleUtils.Aufrufe.Clear();Game.Vehicles.VehicleUtils.Kapazitaet[bike]=0;
   Game.Vehicles.VehicleUtils.Kapazitaet[traeger]=kap;
   var spuren=s.MesseKapazitaet(bike,out var gemessen);
   P(gemessen==kap && spuren==1,"wirkliche Spuren am Traeger gemessen");
   P(Game.Vehicles.VehicleUtils.Aufrufe.Count==2,"beide Wurzeln bei leerem Lot geprueft");
   Game.Vehicles.VehicleUtils.Aufrufe.Clear();Game.Vehicles.VehicleUtils.Kapazitaet[bike]=kap;
   s.MesseKapazitaet(bike,out gemessen);
   P(gemessen==kap && Game.Vehicles.VehicleUtils.Aufrufe.Count==1,"keine Doppelzaehlung");
  }
  Console.WriteLine($"FAHRRAD-WIRTSCHAFT: {n} Pruefungen, {fehler} Fehler");return fehler==0?0:1;
 }
}}
'''
(ziel/'Wirtschaft.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
faelle=[('normal',None,None),('faktor','upkeep * (double)Mod.UnterhaltFaktor','upkeep * 1.0'),('budget','betrag * budget / 100.0','betrag * 1.0'),('hauptschalter','&& !Mod.WirtschaftAn ? 0','&& false ? 0'),('kapazitaet','capacity = capacity2;','capacity = 0;'),('doppelzaehlung','capacity = capacity2;','capacity = capacity2*2;')]
ergebnisse=[]
for name,alt,neu in faelle:
    text=methoden
    if alt:
        assert text.count(alt)==1,(name,text.count(alt));text=text.replace(alt,neu)
    (ziel/'Program.cs').write_text(prefix+text+suffix,encoding='utf-8')
    with (ziel/(name+'.log')).open('w',encoding='utf-8') as out:
        p=subprocess.run(['dotnet','run','-c','Release','--project',str(ziel/'Wirtschaft.csproj')],cwd=root,stdout=out,stderr=subprocess.STDOUT)
    log=(ziel/(name+'.log')).read_text(encoding='utf-8')
    ok=p.returncode==(0 if name=='normal' else 1) and 'FAHRRAD-WIRTSCHAFT:' in log
    if name!='normal':ok=ok and 'FEHLER:' in log
    ergebnisse.append({'Lauf':name,'Exit':p.returncode,'Erwartet':ok});print(name,p.returncode,ok,flush=True)
(ziel/'Program.cs').write_text(prefix+methoden+suffix,encoding='utf-8')
(ziel/'ergebnisse.json').write_text(json.dumps(ergebnisse,indent=2),encoding='utf-8')
raise SystemExit(0 if all(r['Erwartet'] for r in ergebnisse) else 1)
