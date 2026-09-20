from pathlib import Path
import subprocess,tempfile,os
r=Path(__file__).resolve().parents[2];out=Path(tempfile.gettempdir())/'plt-charger-candidates-test';out.mkdir(exist_ok=True)
managed=os.environ.get('CSII_MANAGEDPATH') or r"C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
refs=['Game','Colossal.Mathematics','Unity.Mathematics','Unity.Entities','Unity.Collections','UnityEngine.CoreModule']
(out/'check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>'+''.join(f'<Reference Include="{n}"><HintPath>{managed}/{n}.dll</HintPath></Reference>' for n in refs)+'</ItemGroup></Project>')
def extract(file,name):
 s=(r/'Tools'/file).read_text(encoding='utf-8');start=s.index('        private static '+name);end=s.index('{',start)+1;level=1
 while level:
  level+=(s[end]=='{')-(s[end]=='}');end+=1
 return s[start:end]
helpers=extract('ParkingLotChargerPlacement.cs','bool ShrunkBoxesIntersect')+extract('ParkingLotChargerDiagnostics.cs','Bounds3 CollisionBounds')
program=(r/'Tests/ChargerCandidates/Program.txt').read_text(encoding='utf-8')
(out/'Program.cs').write_text(program.replace('// HELPERS',helpers),encoding='utf-8')
source=(r/'Geometry/ChargerCandidates.cs').read_text(encoding='utf-8')
for name,code in [('Original',source),('Mutation',source.replace('radius+objectRadius+0.05f','0f'))]:
 (out/'ChargerCandidates.cs').write_text(code,encoding='utf-8')
 p=subprocess.run(['dotnet','run','-c','Release','--project',str(out/'check.csproj')],capture_output=True,text=True,encoding='utf-8',errors='replace')
 print(name,p.stdout,p.stderr[:1200]);assert (p.returncode==0)==(name=='Original')
