"""Prueft originale C#-Ablaufmethoden mit UI-/ECS-Testdoubles, inkl. Mutation."""
from pathlib import Path
import subprocess, tempfile

root = Path(__file__).resolve().parents[2]
source = (root / 'Tools/ParkingLotListeUISystem.cs').read_text(encoding='utf-8-sig')
def method(signature):
    start = source.index(signature)
    first = source.index('{', start)
    depth = 1
    end = first + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

methods = '\n'.join(method(s) for s in [
    'protected override void OnUpdate()', 'private void RundeVor()',
    'private void RundeZurueck()', 'private static void Setze('])
prefix = '''using System;
using System.Collections.Generic;
class Base { protected virtual void OnUpdate() {} }
class ValueBinding<T> { public T value; public ValueBinding(T v){value=v;} public void Update(T v){value=v;} }
class Query { public int Count=2; public int CalculateEntityCount()=>Count; }
class Test : Base {
 const int AktualisierungFrames=60;
 int _frames,_zuletztGezaehlt=-1,number,infoWrites,listWrites;
 bool _rundeSteht,_zeigtVorrunde;
 string _infosJetzt="",_infosVorher="",_rundeTextJetzt="",_rundeTextVorher="";
 ValueBinding<string> _infos=new(""),_liste=new(""),_runde=new("");
 ValueBinding<bool> _hatVorrunde=new(false);
 Query _lotQuery=new(); List<int> _lots=new();
 void SammleLots(){_lots.Clear();for(int i=0;i<_lotQuery.Count;i++)_lots.Add(i);}
 void SammleWerte(){}
 void ZieheNeueRunde(){_rundeTextVorher=_rundeTextJetzt;_rundeTextJetzt=(++number).ToString();_rundeSteht=true;}
 void SchreibeListe(){listWrites++;}
 void SchreibeInfos(){infoWrites++;_infosJetzt="Runde"+number;Setze(_infos,_infosJetzt);Setze(_runde,_rundeTextJetzt);_hatVorrunde.Update(_infosVorher!="");}
 void Tick(){_frames=60;OnUpdate();}
 int checks;
 void Check(bool ok,string text){checks++;if(!ok)throw new Exception("FEHLER: "+text);}
 public static void Main(){new Test().Run();}
 void Run(){
  Tick();Check(infoWrites==1,"erste Runde wird geschrieben");
  Tick();Check(infoWrites==1 && listWrites==2,"Grunddaten live, Infos bleiben stehen");
  RundeZurueck();Check(!_zeigtVorrunde,"ohne Historie kein Ruecksprung");
  RundeVor();RundeVor();Tick();Check(number==2,"Doppelklick zieht nur einmal");
  Check(_infosVorher=="Runde1","Vorrunde gespeichert");
  RundeZurueck();Check(_infos.value=="Runde1"&&_runde.value=="1","historischer Inhalt und Rundennummer zusammen");
  Check(!_hatVorrunde.value,"kein Ruecksprung hinter gespeicherte Historie");
  Tick();Check(_infos.value=="Runde1"&&infoWrites==2,"historische Ansicht ueberlebt Live-Update");
  RundeZurueck();Check(_infos.value=="Runde1","wiederholtes Zurueck toggelt nicht");
  RundeVor();Check(_infos.value=="Runde2"&&number==2,"Vorwaerts kehrt zur aktuellen Runde zurueck");
  Check(_hatVorrunde.value,"Historie wieder erreichbar");
  Tick();Check(infoWrites==2,"Rueckkehr zieht nicht neu");
  RundeVor();Tick();Check(_infosVorher=="Runde2"&&_infos.value=="Runde3","naechste Runde uebernimmt korrekte Historie");
  RundeZurueck();_lotQuery.Count=0;Tick();
  Check(_infos.value==""&&_runde.value==""&&!_zeigtVorrunde&&!_hatVorrunde.value,"leerer Spielstand verwirft Historie");
  Console.WriteLine($"Rundenablauf: {checks} Pruefungen, 0 Fehler.");
 }
'''
folder=Path(tempfile.mkdtemp(prefix='plt-rundentest-'))
(folder/'Test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
for mutation in [False,True]:
    # Alten Ablauf wiederherstellen: Infos einmal JEDEM Live-Update schreiben,
    # statt nur beim Ziehen. Erste Runde bleibt korrekt, erst das Update faellt auf.
    code=methods.replace('SchreibeInfos();','').replace('SchreibeListe();','SchreibeListe(); SchreibeInfos();') if mutation else methods
    (folder/'Program.cs').write_text(prefix+code+'\n}',encoding='utf-8')
    result=subprocess.run(['dotnet','run','-c','Release','--project',str(folder/'Test.csproj')],capture_output=True,text=True)
    if not mutation:
        assert result.returncode==0,result.stdout+result.stderr
        print(result.stdout.strip())
    else:
        assert result.returncode!=0 and 'Grunddaten live, Infos bleiben stehen' in result.stderr,result.stdout+result.stderr
        print('Mutation erkannt: erneutes Schreiben der Infos beim Live-Update wird rot.')
print('Native CS2-Bindings und ECS sind Testdoubles; der Ingame-Test bleibt separat.')
