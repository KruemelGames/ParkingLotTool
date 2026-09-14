from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[1]
p=root/'Geometry/Zellen/ParkingGeometry.Zellen.cs'
original=p.read_bytes()
old=b'if (rzQueranschluss\n                        && randzoningAchsen.Any'
# Die Quelle kann CRLF haben; der Austausch erhaelt ihre Zeilenenden.
if old not in original: old=old.replace(b'\n',b'\r\n')
assert original.count(old)==1, 'Mutationsstelle nicht eindeutig'
new=old.replace(b'if (rzQueranschluss',b'if (false && rzQueranschluss')
try:
 p.write_bytes(original.replace(old,new))
 run=subprocess.run(['dotnet','run','-c','Release','--project','Tests/GeometryParity/CSharp/GeometryParity.csproj','--','--rzanschluss'],cwd=root,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 (root/'artifacts/rzanschluss/mutation-kappung.txt').write_bytes(run.stdout)
 assert run.returncode==1, f'Mutation muss rot sein, Exit={run.returncode}'
 assert b'5,50 m' in run.stdout or b'5.50 m' in run.stdout, 'Die bekannte Kappung wurde nicht gemessen'
 print('Mutation Kappung: Exit 1, bekannte Luecke 5,50 m erkannt')
finally:
 p.write_bytes(original)
assert p.read_bytes()==original
print('Produktionsdatei bytegleich wiederhergestellt')
