from pathlib import Path
import tempfile,subprocess
repo=Path(__file__).resolve().parents[2];out=Path(tempfile.gettempdir())/'plt-vegetation-mutation';out.mkdir(exist_ok=True)
csproj=(repo/'Tests/Vegetation/Vegetation.csproj').read_text().replace('<Compile Include="../../Geometry/ParkingVegetation.cs" Link="ParkingVegetation.cs"/>','')
(out/'test.csproj').write_text(csproj)
(out/'Program.cs').write_text((repo/'Tests/Vegetation/Program.cs').read_text(encoding='utf-8'),encoding='utf-8')
source=(repo/'Geometry/ParkingVegetation.cs').read_text(encoding='utf-8')
variants=[('Original',source),('Altersstufen',source.replace('mask &= 63;', 'mask &= 15;')),('Abstand',source.replace('if (!clear) { result.AbstandVerworfen++; continue; }','')),('Dichte',source.replace('plan.Plants.RemoveAll(p=>Unit(Hash(p.Seed+97)) >= math.clamp(density,0,100)/100f);','')),('Median',source.replace('var world = origin + axis * p.x + across * p.y;','var world = origin + axis * p.x + across * (p.y + (options.Line ? .4f : 0));')),('Seed ignoriert',source.replace('var result = new VegetationPlan();','options.Seed = 0; var result = new VegetationPlan();'))]
for name,code in variants:
 (out/'ParkingVegetation.cs').write_text(code,encoding='utf-8')
 r=subprocess.run(['dotnet','run','-c','Release','--project',str(out/'test.csproj')],capture_output=True,text=True,encoding='utf-8',errors='replace')
 assert (r.returncode==0)==(name=='Original'),(name,r.stdout,r.stderr)
 print(name, 'bestanden' if r.returncode==0 else 'Mutation erkannt',r.stdout.strip(),r.stderr.splitlines()[:1])
