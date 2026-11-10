from pathlib import Path
import subprocess,json,os
root=Path(__file__).resolve().parents[2]
ziel=root/'.codex-build/fahrrad/stufe3-mutationen';ziel.mkdir(exist_ok=True)
projekt='Tests/FahrradSpielbau/FahrradSpielbau.csproj'
core=[
('satz-a','BetriebQuelle','Geometry/FahrradBetrieb.cs','Fahrradlayout.A => 6.0','Fahrradlayout.A => 0.0'),
('satz-c','BetriebQuelle','Geometry/FahrradBetrieb.cs','Fahrradlayout.C => 12.5','Fahrradlayout.C => 6.0'),
('satz-d','BetriebQuelle','Geometry/FahrradBetrieb.cs','Fahrradlayout.D => 23.0','Fahrradlayout.D => 12.5'),
('komfort-a','BetriebQuelle','Geometry/FahrradBetrieb.cs','Fahrradlayout.A => .2f','Fahrradlayout.A => .5f'),
('komfort-c','BetriebQuelle','Geometry/FahrradBetrieb.cs','Fahrradlayout.C => .3f','Fahrradlayout.C => .5f'),
('komfort-d','BetriebQuelle','Geometry/FahrradBetrieb.cs','Fahrradlayout.D => .5f','Fahrradlayout.D => .2f'),
('metadaten','BetriebsbrueckeQuelle','Tools/ParkingLotFahrradBetrieb.cs','layout = em.GetComponentData<ParkingLotFahrradLayout>(prefab).Layout;','layout = default;'),
('autoformel','BetriebsbrueckeQuelle','Tools/ParkingLotFahrradBetrieb.cs',': ParkingLotEconomySystem.CalculateUpkeep(kapazitaet);',': 0;'),
('doppelkosten','FabrikQuelle','Tools/ParkingLotFahrradPrefabBau.cs','m_Amount = 0','m_Amount = 65536'),
]
ergebnisse=[]
def lauf(name,args,env=None):
    with (ziel/(name+'.log')).open('w',encoding='utf-8') as out:
        p=subprocess.run(['dotnet','run','-c','Release',*args,'--project',projekt],cwd=root,env=env,stdout=out,stderr=subprocess.STDOUT)
    log=(ziel/(name+'.log')).read_text(encoding='utf-8');ok=p.returncode==1 and 'FEHLER:' in log and 'FAHRRAD-SPIELBAU:' in log
    ergebnisse.append({'Mutation':name,'Exit':p.returncode,'Erkannt':ok});print(name,ok,flush=True)
for name,prop,pfad,alt,neu in core:
    s=(root/pfad).read_text(encoding='utf-8-sig');assert s.count(alt)==1,(name,s.count(alt))
    p=ziel/(name+'.cs');p.write_text(s.replace(alt,neu),encoding='utf-8');lauf(name,[f'-p:{prop}={p}'])
# Wieder die echten Quellen kompilieren, bevor reine Verdrahtungs-Mutationen laufen.
p=subprocess.run(['dotnet','run','-c','Release','--project',projekt],cwd=root,stdout=subprocess.DEVNULL)
assert p.returncode==0
vertraege=[
('anmeldung','Tools/ParkingLotFahrradPrefabSystem.cs','new ParkingLotFahrradLayout { Layout = layout }','new ParkingLotFahrradLayout { Layout = Fahrradlayout.A }'),
('baulayout','Tools/ParkingLotLotArea.cs','TryLot(_areaPreviewLayout.Fahrrad.Layout, out prefab)','TryLot(out prefab)'),
('unterhalt','Tools/ParkingLotEconomySystem.cs','Upkeep = ParkingLotFahrradBetrieb.Unterhalt(EntityManager, lot, capacity)','Upkeep = CalculateUpkeep(capacity)'),
('gebuehr','Tools/ParkingLotEconomySystem.cs','ParkingLotFahrradBetrieb.Ist(EntityManager, lot) ? 0 : UnityEngine.Mathf.Clamp','false ? 0 : UnityEngine.Mathf.Clamp'),
('aus-abrechnung','Tools/ParkingLotEconomySystem.cs','if (Mod.WirtschaftAn) RestoreRuntimeComponents(lot, b, economy);','RestoreRuntimeComponents(lot, b, economy);'),
('aus-messung','Tools/ParkingLotEconomySystem.cs','MesseFahrradOhneWirtschaft();',';'),
('messung','Tools/ParkingLotEconomy.Fahrrad.cs','UpdateLot(lot, leer);',';'),
('begleiter','Tools/ParkingLotBuildingEconomySystem.cs','|| ParkingLotFahrradPrefabBau.IstLotname(value.name)','|| false'),
('personal','Tools/ParkingLotOhnePersonal.cs','ParkingLotFahrradBetrieb.Ist(em, lot)','false'),
('personalabschnitt','Tools/ParkingLotEmployeeSection.cs','&& !ParkingLotFahrradBetrieb.Ist(EntityManager, selectedEntity)','&& true'),
('gebuehrabschnitt','Tools/ParkingLotFeeSection.cs','&& !ParkingLotFahrradBetrieb.Ist(EntityManager, lot)','&& true'),
('gebuehrtrigger','Tools/ParkingLotFeeUISystem.cs','&& !ParkingLotFahrradBetrieb.Ist(EntityManager, entity)','&& true'),
('spurgebuehr','Tools/ParkingLotComfortSystem.cs','var zielGebuehrInt = !wirtschaftAn || fahrrad','var zielGebuehrInt = !wirtschaftAn'),
('spurkomfort','Tools/ParkingLotComfortSystem.cs','FahrradBetrieb.Komfort(fahrradlayout)','FahrradBetrieb.Komfort(Fahrradlayout.A)'),
('sofortgebuehr','Tools/ParkingLotComfortSystem.cs','if (ParkingLotFahrradBetrieb.Ist(EntityManager, lot)) fee = 0;',';'),
('infoart','Tools/ParkingLotInfoansicht.cs','var aktiv = fahrrad ? fahrradIndex : index;','var aktiv = index;'),
('infosymbol','Tools/ParkingLotInfoansicht.cs','var symbol = fahrrad ? fahrradMarker : marker;','var symbol = marker;'),
('listentrigger','Tools/ParkingLotListeUISystem.cs','if (ParkingLotFahrradBetrieb.Ist(EntityManager, lot)) return;',';'),
('listenausgabe','Tools/ParkingLotListeUISystem.cs','ParkingLotFahrradBetrieb.Ist(EntityManager, lot) ? 1 : 0','0'),
('infoausgabe','Tools/ParkingLotEditSection.cs','GetComponentData<ParkingLotEconomyData>(selectedEntity).Capacity : 0) : -1','GetComponentData<ParkingLotEconomyData>(selectedEntity).Capacity : 0) : 0'),
('listeneingabe','UI/src/mods/liste.tsx','fahrrad: zahl(f[WEGE_FELD + 1]) === 1','fahrrad: false'),
('listengebuehr','UI/src/mods/liste.tsx','platz.fahrrad ? <span>{t.fahrradKostenlos}','false ? <span>{t.fahrradKostenlos}'),
('listenart','UI/src/mods/liste.tsx','platz.fahrrad && <div','false && <div'),
('infotext','UI/src/mods/edit-section.tsx','t.fahrradPlaetze(props.fahrradPlaetze','t.fahrradPlaetze(0'),
('abriss-c','Tools/ParkingLotStadtreinigung.cs','|| wert.name == ParkingLotFahrradPrefabBau.LotC','|| false'),
('abriss-d','Tools/ParkingLotStadtreinigung.cs','|| wert.name == ParkingLotFahrradPrefabBau.LotD','|| false'),
('restzaehlung','Tools/ParkingLotStadtreinigung.cs','ParkingLotFahrradPrefabBau.IstLotname(p.name)','p.name == ParkingLotFahrradPrefabBau.Lotname'),
]
# Alle vom neuen Vertrag gelesenen Quellen unveraendert als Referenz kopieren.
referenz=(root/'Tests/FahrradSpielbau/Program.Betriebsvertraege.cs').read_text()
import re
dateien=set(re.findall(r'(?:Lies|Hat)\("([^"]+)"',referenz))
for name,datei,alt,neu in vertraege:
    ordner=ziel/name
    for f in dateien:
        p=ordner/f;p.parent.mkdir(parents=True,exist_ok=True);s=(root/f).read_text(encoding='utf-8-sig')
        if f==datei:
            assert alt in s,(name,alt);s=s.replace(alt,neu)
        p.write_text(s,encoding='utf-8')
    env=os.environ.copy();env['PLT_FAHRRAD_BETRIEBSWURZEL']=str(ordner);lauf(name,['--no-build'],env)
(ziel/'ergebnisse.json').write_text(json.dumps(ergebnisse,indent=2),encoding='utf-8')
raise SystemExit(0 if all(r['Erkannt'] for r in ergebnisse) else 1)
