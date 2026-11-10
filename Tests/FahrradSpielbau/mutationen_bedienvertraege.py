from pathlib import Path
import shutil,subprocess,os,json
root=Path(__file__).resolve().parents[2];ziel=root/'.codex-build/fahrrad/stufe5-verdrahtung';ziel.mkdir(exist_ok=True)
faelle=[
('start','Tools/ParkingLotUISystem.cs','BindeFahrrad();',';'),
('weltarbeit','Tools/ParkingLotWerkzeugzustand.cs','=> FahrradBedienung.Arbeit(Fahrradmodus ? Fahrzeugart.Fahrrad : Fahrzeugart.Auto, arbeit)\n                && Werkzeugzustand.Erlaubt','=> Werkzeugzustand.Erlaubt'),
('modi','Tools/ParkingLotWerkzeugzustand.cs','_zufahrtsart = Zufahrtsart.Zufahrt;',';'),
('geometrie','Tools/ParkingLotToolSystem.cs','FahrradBedienung.FuerBau(_buildSettings);',';'),
('art','Tools/ParkingLotEntrances.cs','if (!FahrradBedienung.Zugang(Fahrradmodus ? Fahrzeugart.Fahrrad : Fahrzeugart.Auto, art)) return;',';'),
('automodus','Tools/ParkingLotEntrances.cs','if (Fahrradmodus) _zufahrtsart = Zufahrtsart.Zufahrt;',';'),
('hinweis','Tools/ParkingLotEntrances.cs','SetStatus(Fahrradmodus ? T("ui.fahrradZugang") : !_entranceMissingPrompt','SetStatus(!_entranceMissingPrompt'),
('zugang','Tools/ParkingLotEntrances.cs','if (Fahrradmodus) return _entrances.Exists(e => e != null) ? null : "zugang";',';'),
('bushalt','Tools/ParkingLotBusStops.cs','if (on && (Fahrradmodus || !_closed || _reiter != Werkzeugreiter.Entwurf)) return;','if (on && (!_closed || _reiter != Werkzeugreiter.Entwurf)) return;'),
('halteauftrag','Tools/ParkingLotBusStops.cs','Fahrradbau ? Array.Empty<BusStopPlacement>() : _busStops.ToArray()','_busStops.ToArray()'),
('zoning','Tools/ParkingLotZoning.cs','if (an && Fahrradmodus)','if (false)'),
('zoningseiten','Tools/ParkingLotZoningRoadEdit.cs','if (an && Fahrradmodus)','if (false)'),
('bauland','Tools/ParkingLotRandzoning.cs','if (Fahrradmodus || zufahrt == null)','if (zufahrt == null)'),
('sperrquads','Tools/ParkingLotZoningBand.cs','if (Fahrradmodus || layout == null || !_closed)','if (layout == null || !_closed)'),
('zettel','Tools/ParkingLotBauzettelSchreiber.cs','q.Zoningflaechen = Array.Empty<ParkingGeometry.Zoningflaeche>();',';'),
]
with (ziel/'normal.log').open('w',encoding='utf-8') as out:
 normal=subprocess.run(['dotnet','run','-c','Release','--project','Tests/FahrradSpielbau/FahrradSpielbau.csproj'],cwd=root,stdout=out,stderr=subprocess.STDOUT)
assert normal.returncode==0
for p in {f[1] for f in faelle}:
 dest=ziel/p;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(root/p,dest)
env=dict(os.environ,PLT_FAHRRAD_UIWURZEL=str(ziel));ergebnis=[]
for name,p,alt,neu in faelle:
 s=(root/p).read_text(encoding='utf-8-sig');assert s.count(alt)==1,(name,s.count(alt));dest=ziel/p;dest.write_text(s.replace(alt,neu),encoding='utf-8')
 with (ziel/(name+'.log')).open('w',encoding='utf-8') as out:r=subprocess.run(['dotnet','run','-c','Release','--no-build','--project','Tests/FahrradSpielbau/FahrradSpielbau.csproj'],cwd=root,env=env,stdout=out,stderr=subprocess.STDOUT)
 log=(ziel/(name+'.log')).read_text(encoding='utf-8');ok=r.returncode==1 and 'FEHLER:' in log and 'FAHRRAD-SPIELBAU:' in log;ergebnis.append({'Mutation':name,'Erkannt':ok});print(name,ok,flush=True);shutil.copyfile(root/p,dest)
(ziel/'ergebnisse.json').write_text(json.dumps(ergebnis,indent=2),encoding='utf-8');assert all(e['Erkannt'] for e in ergebnis)
