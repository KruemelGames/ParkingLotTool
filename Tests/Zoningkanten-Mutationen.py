from pathlib import Path
import subprocess, sys
root=Path.cwd()
ziel=root/'artifacts/zoningkanten-20260921'
mutationen=[
 ('huellkasten','Geometry/Zellen/Zoningkanten.cs','(xs.Min(), xs.Max())','(polygon.Min(p => p.X), polygon.Max(p => p.X))'),
 ('kein-direktanschluss','Geometry/Zellen/Zoningkanten.cs','var ziel = ende;','if (zoning != null) return ende;\n            var ziel = ende;'),
 ('kein-streifenschutz','Geometry/Zellen/Zoningkanten.cs','var ring = z.Strassenring();\n                for (var i = 0; i < ring.Length; i++)','if (tiefe >= 0) return false;\n                var ring = z.Strassenring();\n                for (var i = 0; i < ring.Length; i++)'),
 ('keine-knotenteilung','Geometry/Zellen/ParkingGeometry.Zellen.cs','ZoningAnAnschluessenGeteilt(zoningstrassen, ausgabe).ToList()','zoningstrassen')]
fehler = 0
for name,datei,alt,neu in mutationen:
 if len(sys.argv) > 1 and name not in sys.argv[1:]: continue
 p=root/datei; original=p.read_bytes(); text=original.decode('utf-8').replace("\r\n", "\n"); assert text.count(alt)==1,(name,text.count(alt))
 try:
  p.write_text(text.replace(alt,neu),encoding='utf-8')
  with (ziel/('mutation-'+name+'.txt')).open('w',encoding='utf-8') as log:
   r=subprocess.run(['dotnet','run','-c','Release','--project','Tests/GeometryParity/CSharp/GeometryParity.csproj','--','--zoningkanten'],stdout=log,stderr=subprocess.STDOUT)
  erkannt = r.returncode != 0 and 'FEHLER:' in (ziel/('mutation-'+name+'.txt')).read_text(encoding='utf-8')
  if not erkannt: fehler += 1
  print(name,'Exitcode',r.returncode,'erkannt',erkannt,flush=True)
 finally: p.write_bytes(original)
with (ziel/'zoningkanten-final.txt').open('w',encoding='utf-8') as log:
 r=subprocess.run(['dotnet','run','-c','Release','--project','Tests/GeometryParity/CSharp/GeometryParity.csproj','--','--zoningkanten'],stdout=log,stderr=subprocess.STDOUT)
print('wiederhergestellt Exitcode',r.returncode,flush=True)

sys.exit(1 if fehler or r.returncode else 0)
