"""Fuehrt echte UI-Trigger und CurrentSettings in einem isolierten C#-Container aus.
Keine Game-UI/Native-Laufzeit: Bindungen, Tool-Zustand und Prefabbreite sind Attrappen.
"""
from pathlib import Path
import subprocess,sys,json
root=Path(__file__).resolve().parents[2]
ziel=root/'.codex-build/fahrrad/stufe5-bedienung';ziel.mkdir(exist_ok=True)

def methode(s,name):
 at=s.index(name);start=s.rfind('\n',0,at)+1;offen=s.index('{',at);tiefe=1;i=offen+1
 while tiefe:
  tiefe+=(s[i]=='{')-(s[i]=='}');i+=1
 return s[start:i]

harness=r'''
using System;
using System.Collections.Generic;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Colossal.UI.Binding;
using static ParkingLotTool.Tools.ParkingLotTexte;
namespace Colossal.UI.Binding {
 public class ValueBinding<T> { public T value; public ValueBinding(T v){value=v;} public void Update(T v){value=v;} }
 public class GetterValueBinding<T> { public string Name; public Func<T> Get; public GetterValueBinding(string g,string n,Func<T> f){Name=n;Get=f;} }
 public class TriggerBinding<T> { public string Name; public Action<T> Fire; public TriggerBinding(string g,string n,Action<T> f){Name=n;Fire=f;} }
}
namespace ParkingLotTool.Tools {
 static class Mod { public static Logger log=new(); public class Logger{public void Info(string s){}} }
 static class ParkingLotTexte {public static string T(string s)=>s;}
 sealed class ParkingLotToolSystem {
  public bool IsEditing,BauartBeschaeftigt; public int Wechsel,Reiterwechsel;
  public Werkzeugreiter Reiter; public float Gassenbreite()=>8;
  public void BauartGewechselt(){Wechsel++;}
  public void SetzeReiter(Werkzeugreiter r){Reiter=r;Reiterwechsel++;}
  public static Werkzeugreiter ReiterAus(string s)=>s=="zoning"?Werkzeugreiter.Zoning:Werkzeugreiter.Entwurf;
 }
 public sealed partial class ParkingLotUISystem {
  const string Group="ParkingLotTool",DraftTab="layout",ZoningTab="zoning",ReportTab="report";
  const float GassenbelagLuft=2.5f; const double Buchtbreite=3;
  internal Fahrzeugart FahrzeugartFuerNeubau {get;set;}
  internal Fahrradlayout FahrradlayoutFuerNeubau {get;set;}
  private Fahrzeugart? _editFahrzeugart; private Fahrradlayout _editFahrradlayout;
  private LayoutSettings _loadedBuildReceiptTemplate; double? _ausrichtwinkel;
  private ValueBinding<float> _edgeSetback=new(1.5f),_aisleWidth=new(7.5f),_crossWidth=new(4),_medianWidth=new(2.5f),_crossBays=new(11),_rowAngle=new(17);
  private ValueBinding<bool> _greenMedian=new(true),_crossCaps=new(true),_randstrassen=new(true),_crossRoads=new(true);
  private ValueBinding<string> _angleMode=new("fixed"),_engine=new("zellen"),_tab=new("layout");
  internal string FlaecheStrasse=>"Pavement"; internal string FlaecheDekoration=>"Grass";
  internal int Revision; internal string Status; internal ParkingLotToolSystem tool=new();
  internal Dictionary<string,Func<object>> getters=new(); Dictionary<string,Action<int>> triggers=new();
  private void AddUpdateBinding<T>(GetterValueBinding<T> b){getters[b.Name]=()=>b.Get();}
  private void AddBinding(TriggerBinding<int> b){triggers[b.Name]=b.Fire;}
  private ParkingLotToolSystem Tool()=>tool;
  private void VerlasseMeldemodus(){} private void SetStatus(string s){Status=s;}
  public ParkingLotUISystem(){BindeFahrrad();}
  internal void Fire(string n,int v)=>triggers[n](v);
  internal void Tab(string s)=>SetTab(s);
  internal string Tabwert=>_tab.value;
  internal void Edit(Fahrzeugart art,Fahrradlayout layout,bool vorlage){_editFahrzeugart=art;_editFahrradlayout=layout;tool.IsEditing=true;_loadedBuildReceiptTemplate=vorlage?(art==Fahrzeugart.Fahrrad?LayoutSettings.Fahrrad(layout):LayoutSettings.Cs2):null;}
  internal void Ende(){tool.IsEditing=false;ClearBuildReceiptTemplate();}
  internal static string Hash(LayoutSettings s)=>JsonSerializer.Serialize(ParkingSettingsInventory.Erfasse(s,ParkingSettingsInventory.Layout));
'''
main=r'''
 }
 static class Program {
  static int n,fehler; static void P(bool ok,string s){n++;if(!ok){fehler++;Console.WriteLine("FEHLER: "+s);}}
  static int Main(){
   try {
    var u=new ParkingLotUISystem(); var vorher=ParkingLotUISystem.Hash(u.CurrentSettings());
    P((int)u.getters["Fahrzeugart"]()==0 && !(bool)u.getters["BauartEdit"](),"Auto als Anfangswert");
    u.Fire("SetFahrradlayout",2); P((int)u.getters["Fahrradlayout"]()==0,"Auto nimmt keine Fahrradlayoutwahl an");
    u.Fire("SetFahrzeugart",99); P(u.Revision==0,"ungueltige Art bleibt ohne Wirkung");
    u.tool.BauartBeschaeftigt=true; u.Fire("SetFahrzeugart",1); P(u.Revision==0,"Bau sperrt Artwechsel");
    u.tool.BauartBeschaeftigt=false;
    foreach(var layout in new[]{0,2,3}){
     var vorherRev=u.Revision;u.Fire("SetFahrzeugart",1);P(u.Revision==vorherRev+1,"Artwechsel invalidiert Vorschau");u.Fire("SetFahrradlayout",layout);
     var s=u.CurrentSettings();P(s.Fahrzeugart==Fahrzeugart.Fahrrad && (int)s.Fahrradlayout==layout,"Trigger erreicht echte Settings "+layout);
     P(s.Ai==3 && s.Gassenbreite==3 && !s.Randstrassen && s.Md==0,"Fahrradmasse "+layout);
     P(s.Cw==(layout==2?2.5:3) && s.Sl==(layout==2?2.5:2),"Layoutmasse "+layout);
     P((int)u.getters["Fahrradlayout"]()==layout,"Getter folgt Layout "+layout);
     var rev=u.Revision;u.Fire("SetFahrradlayout",1);u.Fire("SetFahrradlayout",-1);P(u.Revision==rev,"B und negative Layouts abgewiesen");
     u.Tab("zoning");P(u.Tabwert=="layout" && u.Status=="ui.fahrradZoningAus","Server sperrt Zoning");
     u.tool.BauartBeschaeftigt=true;u.Fire("SetFahrradlayout",layout==2?0:2);P((int)u.CurrentSettings().Fahrradlayout==layout,"Bau sperrt Layout");u.tool.BauartBeschaeftigt=false;
     u.Fire("SetFahrzeugart",0);P(ParkingLotUISystem.Hash(u.CurrentSettings())==vorher,"Autoeinstellungen bitgleich nach Rueckkehr "+layout);
     u.Tab("zoning");P(u.Tabwert=="zoning","Auto Zoning weiterhin erlaubt");u.Tab("layout");
    }
    P(u.tool.Wechsel==6,"Jeder erfolgreiche Wechsel raeumt Weltmodi ab");
    foreach(var art in new[]{Fahrzeugart.Auto,Fahrzeugart.Fahrrad}) foreach(var layout in new[]{Fahrradlayout.A,Fahrradlayout.C,Fahrradlayout.D})foreach(var vorlage in new[]{false,true}){
     u.Edit(art,layout,vorlage);P((bool)u.getters["BauartEdit"](),"Editgetter");
     var rev=u.Revision;u.Fire("SetFahrzeugart",art==Fahrzeugart.Auto?1:0);P(u.Revision==rev && u.CurrentSettings().Fahrzeugart==art,"Edit-Art fest auch ohne Vorlage");
     if(art==Fahrzeugart.Fahrrad){u.Fire("SetFahrradlayout",2);P(u.CurrentSettings().Fahrradlayout==Fahrradlayout.C,"Fahrradlayout im Edit wechselbar");}
     u.Ende();P(!(bool)u.getters["BauartEdit"]() && u.CurrentSettings().Fahrzeugart==Fahrzeugart.Auto,"Edit gibt Neubauwahl frei");
    }
    // Tool-Edit sperrt auch im Zeitfenster vor der Zetteluebernahme.
    u.tool.IsEditing=true;u.Fire("SetFahrzeugart",1);P(u.CurrentSettings().Fahrzeugart==Fahrzeugart.Auto,"Edit vor Vorlage gesperrt");u.tool.IsEditing=false;
    foreach(var art in new[]{Fahrzeugart.Auto,Fahrzeugart.Fahrrad}){
     foreach(Weltarbeit a in Enum.GetValues(typeof(Weltarbeit))) P(FahrradBedienung.Arbeit(art,a)==(art==Fahrzeugart.Auto||!(a==Weltarbeit.Zoningflaeche||a==Weltarbeit.Zoningseite||a==Weltarbeit.Bushaltestelle)),"Arbeitsmatrix "+art+a);
     foreach(Zufahrtsart z in Enum.GetValues(typeof(Zufahrtsart))) P(FahrradBedienung.Zugang(art,(int)z)==(art==Fahrzeugart.Auto||z==Zufahrtsart.Zufahrt),"Zugangsmatrix "+art+z);
     var s=LayoutSettings.Cs2;s.Fahrzeugart=art;
     s.BusStops=new[]{new BusStopPlacement()};s.Zoningflaechen=new[]{new ParkingGeometry.Zoningflaeche()};s.Randzoning=new[]{new ParkingGeometry.RandzoningLinie()};s.Entrances=new[]{new Entrance{Art=Zufahrtsart.Einfahrt}};
     var hash=ParkingLotUISystem.Hash(s);FahrradBedienung.FuerBau(s);
     P(art==Fahrzeugart.Auto?ParkingLotUISystem.Hash(s)==hash:s.BusStops.Length==0 && s.Zoningflaechen.Length==0 && s.Randzoning.Length==0 && s.Entrances[0].Art==Zufahrtsart.Zufahrt,"Baueingabe filtert nur Fahrrad");
    }
   }catch(Exception e){fehler++;Console.WriteLine("FEHLER: "+e);}
   Console.WriteLine($"FAHRRAD-BEDIENUNG: {n} Pruefungen, {fehler} Fehler");return fehler==0?0:1;
  }
 }
}
'''
ui=(root/'Tools/ParkingLotUISystem.cs').read_text(encoding='utf-8-sig')
(ziel/'Container.cs').write_text(harness+'\n'.join(methode(ui,n) for n in ['internal LayoutSettings CurrentSettings()','internal void ClearBuildReceiptTemplate()','private void SetTab(string value)'])+main,encoding='utf-8')
projekt='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>$(NoWarn);CS0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="{root}/Geometry/**/*.cs" Exclude="{root}/Geometry/FahrradBedienung.cs"/><Compile Include="{bedienung}"/><Compile Include="{ui}"/><Reference Include="Unity.Mathematics"><HintPath>$(CSII_TOOLPATH)/UnityModsProject/Library/ScriptAssemblies/Unity.Mathematics.dll</HintPath></Reference></ItemGroup></Project>'''
def lauf(name,regeln=None,trigger=None):
 p=ziel/(name+'.csproj');p.write_text(projekt.format(root=root.as_posix(),bedienung=(regeln or root/'Geometry/FahrradBedienung.cs').as_posix(),ui=(trigger or root/'Tools/ParkingLotUI.Fahrrad.cs').as_posix()),encoding='utf-8')
 # Alle Container sind gleich; Mutationsquellen liegen ausserhalb dieses Projektordners.
 with (ziel/(name+'.log')).open('w',encoding='utf-8') as out:r=subprocess.run(['dotnet','run','-c','Release','--project',str(p)],cwd=root,stdout=out,stderr=subprocess.STDOUT)
 log=(ziel/(name+'.log')).read_text(encoding='utf-8');print(name,r.returncode,log.splitlines()[-1],flush=True);return r.returncode,log
code,log=lauf('normal');assert code==0,log
if '--mutationen' in sys.argv:
 mut=ziel.parent/'stufe5-bedienung-mutationen';mut.mkdir(exist_ok=True)
 faelle=[
 ('edit','ui','_editFahrzeugart.HasValue || (tool?.IsEditing ?? false),','false,'),
 ('tool-edit','ui','_editFahrzeugart.HasValue || (tool?.IsEditing ?? false),','_editFahrzeugart.HasValue,'),
 ('busy','regel','=> !edit && !beschaeftigt;','=> !edit;'),
 ('layout-b','regel','Enum.IsDefined(typeof(Fahrradlayout), layout)','true'),
 ('layout-auto','regel','art == Fahrzeugart.Fahrrad && !beschaeftigt','!beschaeftigt'),
 ('layout-busy','regel','art == Fahrzeugart.Fahrrad && !beschaeftigt','art == Fahrzeugart.Fahrrad'),
 ('layout-noop','ui','else FahrradlayoutFuerNeubau = (Fahrradlayout)layout;','else FahrradlayoutFuerNeubau = Fahrradlayout.A;'),
 ('art-noop','ui','FahrzeugartFuerNeubau = (Fahrzeugart)art;','FahrzeugartFuerNeubau = Fahrzeugart.Auto;'),
 ('revision','ui','Revision++;\n            Mod.log','/* Revision entfernt */\n            Mod.log'),
 ('weltmodi','ui','tool?.BauartGewechselt();',';'),
 ('bus','regel','settings.BusStops = Array.Empty<BusStopPlacement>();',';'),
 ('zoning','regel','settings.Zoningflaechen = Array.Empty<ParkingGeometry.Zoningflaeche>();',';'),
 ('randzoning','regel','settings.Randzoning = Array.Empty<ParkingGeometry.RandzoningLinie>();',';'),
 ('eingang','regel','if (e != null) e.Art = Zufahrtsart.Zufahrt;',';'),
 ('auto','regel','if (settings.Fahrzeugart != Fahrzeugart.Fahrrad) return;',';'),
 ('arbeit','regel','arbeit != Weltarbeit.Zoningflaeche','true'),
 ('zugang','regel','zugang == (int)Zufahrtsart.Zufahrt','true')]
 ergebnis=[]
 for name,typ,alt,neu in faelle:
  src=root/('Tools/ParkingLotUI.Fahrrad.cs' if typ=='ui' else 'Geometry/FahrradBedienung.cs');s=src.read_text(encoding='utf-8-sig');assert s.count(alt)==1,(name,s.count(alt))
  pf=mut/(name+'.cs');pf.write_text(s.replace(alt,neu),encoding='utf-8');code,log=lauf(name,trigger=pf if typ=='ui' else None,regeln=pf if typ=='regel' else None)
  ok=code==1 and 'FAHRRAD-BEDIENUNG:' in log and 'FEHLER:' in log;ergebnis.append({'Mutation':name,'Erkannt':ok})
 (ziel/'mutationen.json').write_text(json.dumps(ergebnis,indent=2),encoding='utf-8');assert all(e['Erkannt'] for e in ergebnis)
 lauf('normal')
