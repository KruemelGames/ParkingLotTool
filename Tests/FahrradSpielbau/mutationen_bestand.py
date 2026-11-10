from pathlib import Path
import subprocess,json,re,os
root=Path(__file__).resolve().parents[2]
ziel=root/'.codex-build/fahrrad/stufe4-mutationen';ziel.mkdir(exist_ok=True)
speicher='Tests/Speicherformate/Speicherformate.csproj';bau='Tests/FahrradSpielbau/FahrradSpielbau.csproj'
faelle=[
('version','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','public const int CurrentVersion = 7;','public const int CurrentVersion = 6;',speicher),
('schreiben-art','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','writer.Write((int)Fahrzeugart);','writer.Write(0);',speicher),
('schreiben-layout','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','writer.Write((int)Fahrradlayout);','writer.Write(0);',speicher),
('lesen-v7','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','if (Version >= 7)','if (Version >= 8)',speicher),
('alte-art','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','Fahrzeugart = Fahrzeugart.Auto;','Fahrzeugart = Fahrzeugart.Fahrrad;',speicher),
('altes-layout','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','Fahrradlayout = Fahrradlayout.A;','Fahrradlayout = Fahrradlayout.D;',speicher),
('settings-art','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','Fahrzeugart = Fahrzeugart,','Fahrzeugart = Fahrzeugart.Auto,',speicher),
('settings-layout','ReceiptQuelle','Tools/ParkingLotBuildReceipt.cs','Fahrradlayout = Fahrradlayout,','Fahrradlayout = Fahrradlayout.A,',speicher),
('edit-art','BauartQuelle','Geometry/Bauartvertrag.cs','settings.Fahrzeugart = gespeichert.Value;','settings.Fahrzeugart = Fahrzeugart.Auto;',speicher),
('edit-layout','BauartQuelle','Geometry/Bauartvertrag.cs','settings.Fahrradlayout = layout;','settings.Fahrradlayout = Fahrradlayout.A;',speicher),
('layout-b','BauartQuelle','Geometry/Bauartvertrag.cs','Enum.IsDefined(typeof(Fahrradlayout), layout)','true',speicher),
('ladefenster','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','=> Ist(em, lot) || (lot != Entity.Null','=> Ist(em, lot) || (false && lot != Entity.Null',speicher),
('navigation','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','ziel.Add(new Game.Areas.SubArea { m_Area=flaeche });',';',speicher),
('abrissflaeche','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','if(!schon) root.Add(new Game.Areas.SubArea { m_Area=flaeche });',';',speicher),
('duplikate','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','if(schon) continue;',';',speicher),
('zyklus','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','if (flaeche == lot || flaeche == traeger || flaeche == Entity.Null','if (false || flaeche == Entity.Null',speicher),
('netzverweise','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','for(var i=0;i<quelle.Length;i++) ziel.Add(quelle[i]);',';',speicher),
('traeger-prefab','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','em.GetComponentData<PrefabRef>(kandidat).m_Prefab==em.GetComponentData<PrefabRef>(lot).m_Prefab','true',speicher),
('traeger-owner','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','em.GetComponentData<Owner>(kandidat).m_Owner==lot','true',speicher),
('mehrdeutig','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','return anzahl==1?gefunden:Entity.Null;','return anzahl>0?gefunden:Entity.Null;',speicher),
('edit-prefabtyp','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','zettel.Fahrzeugart==(fahrrad?Fahrzeugart.Fahrrad:Fahrzeugart.Auto)','true',speicher),
('edit-prefablayout','BestandQuelle','Tools/ParkingLotFahrradBestand.cs','(!fahrrad || zettel.Fahrradlayout==layout)','true',speicher),
('baumlagen','DekorationQuelle','Geometry/FahrradDekoration.cs','foreach(var p in plan.Baeume)','foreach(var p in plan.Baeume.Take(0))',bau),
]
erg=[]
def lauf(name,projekt,args,env=None):
 with (ziel/(name+'.log')).open('w',encoding='utf-8') as out:
  p=subprocess.run(['dotnet','run','-c','Release','--project',projekt,*args],cwd=root,env=env,stdout=out,stderr=subprocess.STDOUT)
 log=(ziel/(name+'.log')).read_text(encoding='utf-8');ok=p.returncode==1 and 'FEHLER:' in log and ('SPEICHERFORMATE:' in log or 'FAHRRAD-SPIELBAU:' in log or 'FEHLER: unbehandelte Ausnahme:' in log)
 erg.append({'Mutation':name,'Exit':p.returncode,'Erkannt':ok});print(name,ok,flush=True)
for name,prop,pfad,alt,neu,projekt in faelle:
 s=(root/pfad).read_text(encoding='utf-8-sig');assert s.count(alt)==1,(name,s.count(alt))
 p=ziel/(name+'.cs');p.write_text(s.replace(alt,neu),encoding='utf-8');lauf(name,projekt,[f'-p:{prop}={p}'])
# Produktionsquellen wiederherstellen, bevor die quelltextlichen Verdrahtungsproben laufen.
for projekt in [speicher,bau]:
 p=subprocess.run(['dotnet','run','-c','Release','--project',projekt],cwd=root,stdout=subprocess.DEVNULL);assert p.returncode==0
vertraege=[
('zettelschreiber','Tools/ParkingLotBauzettelSchreiber.cs','Fahrzeugart = settings.Fahrzeugart,','Fahrzeugart = Fahrzeugart.Auto,'),
('edit-laden','Tools/ParkingLotUISystem.cs','_editFahrzeugart = receipt.Fahrzeugart;','_editFahrzeugart = null;'),
('edit-einstieg','Tools/ParkingLotEditMode.cs','if(gelesen && !ParkingLotFahrradBetrieb.PasstZettel(EntityManager,lot,receipt))','if(false)'),
('edit-klemmen','Tools/ParkingLotUISystem.cs','Bauartvertrag.Edit(settings, _editFahrzeugart, _editFahrradlayout);',';'),
('edit-freigeben','Tools/ParkingLotUISystem.cs','_editFahrzeugart = null;',';'),
('leser-validierung','Tools/ParkingLotBaukontextLeser.cs','Bauartvertrag.Gueltig(r.Fahrzeugart, r.Fahrradlayout)','true'),
('sync-aufnahme','Tools/ParkingLotSync.cs','if (ParkingLotFahrradBetrieb.IstBestand(EntityManager, lot)) continue;',';'),
('sync-einreihen','Tools/ParkingLotSync.cs','if (ParkingLotFahrradBetrieb.IstBestand(EntityManager, lot)) return;',';'),
('sync-anwenden','Tools/ParkingLotSync.cs','if (ParkingLotFahrradBetrieb.IstBestand(EntityManager, lot)) return false;',';'),
('hintergrund','Tools/ParkingLotHintergrundSystem.cs','if (ParkingLotFahrradBetrieb.IstBestand(EntityManager, lot))','if (false)'),
('rueckweg','Tools/ParkingLotHintergrund.Auftrag.cs','if (!rueckweg && !KannNeubauen(lot, out var grund))','if (!KannNeubauen(lot, out var grund))'),
('wegvorbau','Tools/ParkingLotWegpruefung.cs','if (ParkingLotFahrradBetrieb.IstBestand(EntityManager, lot))','if (false)'),
('assetneubau','Tools/ParkingLotFehlendeAssets.cs','if (ParkingLotFahrradBetrieb.IstBestand(EntityManager, lot))','if (false)'),
('waisen','Tools/ParkingLotWaisen.cs','|| ParkingLotFahrradPrefabBau.IstLotname(name)','|| false'),
('waisen-typ','Tools/ParkingLotWaisen.Reparatur.cs','!ParkingLotFahrradBetrieb.TraegerPasst(EntityManager,lot,traeger)','false'),
('protokollfilter','Tools/ParkingLotBauzettelWiederherstellung.cs','if(!Bauartvertrag.ProtokollPasst','if(false && !Bauartvertrag.ProtokollPasst'),
('protokoll-aufruf','Tools/ParkingLotBauzettelWiederherstellung.cs','ProtokollzeileZu(kennung,fahrrad?Fahrzeugart.Fahrrad:Fahrzeugart.Auto,','ProtokollzeileZu(kennung,null,'),
('ladephase','Tools/ParkingLotBayObjects.cs','RestoreFahrradGehflaechenAfterLoad();',';'),
('ladeprefab','Tools/ParkingLotFahrradLaden.cs','p.name != ParkingLotFahrradPrefabBau.Gehname','false'),
('ladeneKanten','Tools/ParkingLotFahrradLaden.cs','ParkingLotFahrradBetrieb.LadeNetze(EntityManager,lot,traeger)','0'),
('pflanzenuebernahme','Tools/ParkingLotVegetationBuild.cs',"if (Fahrradbau) s.Append('|').Append(FahrradDekoration.Speicherkennung(_areaPreviewLayout.Fahrrad));",';'),
('deinstallation-waise','Tools/ParkingLotStadtreinigung.cs','VerbindeFahrradFuerAbriss(flaechen[i]);','Reparieren(flaechen[i]);'),
('abriss-zuordnung','Tools/ParkingLotWaisen.Fahrrad.cs','return Verbinden(lot);','return false;'),
('restzaehlung','Tools/ParkingLotStadtreinigung.cs','+ lots + fahrradtraeger;','+ lots;'),
('sperre','Tools/ParkingLotStadtreinigung.cs','sync.ArbeitLaeuft','false'),
('annahmesperre','Tools/ParkingLotStadtreinigung.cs','if(Blockiert()) { Warte(); return 0; }',';'),
]
text=(root/'Tests/FahrradSpielbau/Program.Bestandsvertraege.cs').read_text(encoding='utf-8-sig')
dateien=set(re.findall(r'(?:Lies|Hat)\("([^"]+)"',text))
for name,datei,alt,neu in vertraege:
 ordner=ziel/name
 for f in dateien:
  p=ordner/f;p.parent.mkdir(parents=True,exist_ok=True);s=(root/f).read_text(encoding='utf-8-sig')
  if f==datei:
   assert alt in s,(name,alt);s=s.replace(alt,neu)
  p.write_text(s,encoding='utf-8')
 env=os.environ.copy();env['PLT_FAHRRAD_BESTANDSWURZEL']=str(ordner);lauf(name,bau,['--no-build'],env)
(ziel/'ergebnisse.json').write_text(json.dumps(erg,indent=2,ensure_ascii=False),encoding='utf-8')
raise SystemExit(0 if all(e['Erkannt'] for e in erg) else 1)
