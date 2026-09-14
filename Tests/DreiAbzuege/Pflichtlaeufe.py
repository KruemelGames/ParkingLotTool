import subprocess,pathlib,concurrent.futures,json,os
p=pathlib.Path('Tests/DreiAbzuege')
names=['zoningflaeche','zoningnetz','zoningrasten','ueberlappung','versorgung','infokarten','flaechenannahme','zufahrtsverlust','sonderplaetze','zufahrtsquads','lformtoggle','fusswegzugang','gassenabstand','endwegbreite','diagonalwege','winkelmodus','randstrassen']
def run(name):
 cmd=['dotnet','run','-c','Release','--no-build','--project','Tests/GeometryParity/CSharp/GeometryParity.csproj','--','--'+name]
 if name=='sonderplaetze':cmd+=['-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113','0,51.4;1,37.9;2,51.8;3,74.0']
 with (p/(name+'.txt')).open('w',encoding='utf-8') as f:r=subprocess.run(cmd,stdout=f,stderr=subprocess.STDOUT)
 print(name,r.returncode,flush=True);return name,r.returncode
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool: results=list(pool.map(run,names))
for name,proj in [('versorgungsneubau','Tests/Versorgungsneubau/Versorgungsneubau.csproj'),('diagonaleendwege','Tests/DiagonaleEndwege/DiagonaleEndwege.csproj')]:
 with (p/(name+'.txt')).open('w',encoding='utf-8') as f:r=subprocess.run(['dotnet','run','-c','Release','--project',proj],stdout=f,stderr=subprocess.STDOUT)
 print(name,r.returncode,flush=True);results.append((name,r.returncode))
(p/'pflicht-ergebnisse.json').write_text(json.dumps(results,indent=2))
