from pathlib import Path
import os,subprocess,json,html
root=Path(__file__).resolve().parents[2]
ziel=root/'.codex-build/fahrrad/stufe4-protokoll';ziel.mkdir(exist_ok=True)
s=(root/'Tools/ParkingLotBauzettelWiederherstellung.cs').read_text(encoding='utf-8-sig')
start=s.index('internal static string ProtokollzeileZu(');a=s.index('{',start);i=a+1;tiefe=1
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
methode=s[start:i]
dll=Path(os.environ.get('CSII_MANAGEDPATH','C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed'))/'Newtonsoft.Json.dll'
assert dll.exists(),dll
core=root/'Geometry/Bauartvertrag.cs'
(ziel/'Protokoll.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="'+html.escape(str(core))+'" /><Reference Include="Newtonsoft.Json"><HintPath>'+html.escape(str(dll))+'</HintPath></Reference></ItemGroup></Project>')
prefix=r'''
using System;using System.IO;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using ParkingLotTool.Geometry;
namespace ParkingLotTool.Geometry { public enum Fahrzeugart {Auto,Fahrrad}public enum Fahrradlayout {A=0,C=2,D=3}public class LayoutSettings {public Fahrzeugart Fahrzeugart;public Fahrradlayout Fahrradlayout;} }
class Log {public void Warn(string text)=>Console.WriteLine(text);}
class Mod {public static Log log=new();}
class Probe {
static string Pfad=Path.Combine(AppContext.BaseDirectory,"journal.jsonl");
static string JournalPath()=>Pfad;
'''
suffix=r'''
static int Main(){try{return Test();}catch(Exception e){Console.WriteLine("FEHLER: "+e);return 1;}}
static int Test(){int n=0,f=0;void P(bool ok,string text){n++;if(!ok){f++;Console.WriteLine("FEHLER: "+text);}}
string R(string id,string art,string layout,int nummer)=>new JObject{["kennung"]=id,["layoutwerte"]=new JObject{["Fahrzeugart"]=art,["Fahrradlayout"]=layout},["n"]=nummer}.ToString(Formatting.None);
var alt=R("form",null,null,1);var a=R("form","Fahrrad","A",2);var c=R("form","Fahrrad","C",3);var d=R("form","Fahrrad","D",4);var cneu=R("form","Fahrrad","C",5);
var kaputt="{\"kennung\":\"form\",\"layoutwerte\":[]}";
var typfehler="{\"kennung\":\"form\",\"layoutwerte\":{\"Fahrzeugart\":[],\"Fahrradlayout\":\"D\"}}";
File.WriteAllLines(Pfad,new[]{alt,a,c,d,cneu,R("andere","Fahrrad","C",6),kaputt,typfehler});
P(ProtokollzeileZu("form",Fahrzeugart.Auto)==alt,"altes Auto-Protokoll trotz gleicher Fahrradform gefunden");
P(ProtokollzeileZu("form",Fahrzeugart.Fahrrad,Fahrradlayout.A)==a,"A korrekt gefunden");
P(ProtokollzeileZu("form",Fahrzeugart.Fahrrad,Fahrradlayout.C)==cneu,"neuester passender C-Eintrag statt erster/letzter fremder Eintrag");
P(ProtokollzeileZu("form",Fahrzeugart.Fahrrad,Fahrradlayout.D)==d,"D trotz spaeterer kaputter/fremder Eintraege gefunden");
P(ProtokollzeileZu("fehlt",Fahrzeugart.Fahrrad,Fahrradlayout.C)==null,"kein raeumliches Raten");
P(ProtokollzeileZu("form")==typfehler,"ungefilterte Diagnose bleibt unveraendert beim letzten Eintrag");
File.Delete(Pfad);P(ProtokollzeileZu("form",Fahrzeugart.Auto)==null,"fehlendes Journal ist harmlos");
Console.WriteLine($"FAHRRAD-PROTOKOLL: {n} Pruefungen, {f} Fehler");return f==0?0:1;}
}
'''
faelle=[('normal',None,None),('typbindung','if(!Bauartvertrag.ProtokollPasst','if(false && !Bauartvertrag.ProtokollPasst'),('neuester','treffer = zeile;','treffer = zeile; return treffer;'),('kaputter-eintrag','catch(Exception) { continue; }','catch(Exception) { return null; }'),('layoutbindung','art,layout)) continue;','art,null)) continue;')]
erg=[]
for name,alt,neu in faelle:
    code=methode
    if alt:
        assert code.count(alt)==1,(name,code.count(alt));code=code.replace(alt,neu)
    (ziel/'Program.cs').write_text(prefix+code+suffix,encoding='utf-8')
    with (ziel/(name+'.log')).open('w',encoding='utf-8') as out:
        p=subprocess.run(['dotnet','run','-c','Release','--project',str(ziel/'Protokoll.csproj')],cwd=root,stdout=out,stderr=subprocess.STDOUT)
    log=(ziel/(name+'.log')).read_text(encoding='utf-8');ok=p.returncode==(0 if name=='normal' else 1) and 'FAHRRAD-PROTOKOLL:' in log
    erg.append({'Lauf':name,'Exit':p.returncode,'Erwartet':ok});print(name,p.returncode,ok,flush=True)
(ziel/'Program.cs').write_text(prefix+methode+suffix,encoding='utf-8')
(ziel/'ergebnisse.json').write_text(json.dumps(erg,indent=2),encoding='utf-8')
raise SystemExit(0 if all(e['Erwartet'] for e in erg) else 1)
