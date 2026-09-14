"""Originale Werkzeugmatrix samt Sichtbarkeit; Mutationen nur in Tempkopien."""
from pathlib import Path
import subprocess, tempfile
r = Path(__file__).resolve().parents[2]
tmp = Path(tempfile.mkdtemp(prefix='plt-werkzeug-mutation-'))
source = (r/'Geometry/Werkzeugzustand.cs').read_text(encoding='utf-8-sig')
(tmp/'Test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
(tmp/'Tests.cs').write_text((r/'Tests/GeometryParity/CSharp/Program.Werkzeugzustand.cs').read_text(encoding='utf-8-sig'),encoding='utf-8')
(tmp/'Main.cs').write_text('internal static partial class Program { static int Main()=>RunWerkzeugzustand(); }')
cases = [('Original', source, None),
 ('Liste zeigt Vorschau',source.replace('=> reiter != Werkzeugreiter.Liste;', '=> true;'),'Vorschau im Reiter Liste'),
 ('Nur Zoning gesperrt',source.replace('=> Erlaubt(reiter, modus, Weltarbeit.Umriss);',
    '=> modus != Werkzeugmodus.Zoningflaeche && modus != Werkzeugmodus.Zoningseite;'), 'Zeichenhilfe Liste/Grund')]
for name,code,expected in cases:
    (tmp/'Werkzeugzustand.cs').write_text(code,encoding='utf-8')
    run=subprocess.run(['dotnet','run','-c','Release','--project',str(tmp/'Test.csproj')],capture_output=True,text=True)
    if expected is None:
        assert run.returncode==0,run.stdout+run.stderr
    else:
        assert run.returncode!=0 and expected in run.stdout,run.stdout+run.stderr
    print(name+': '+('bestanden' if expected is None else 'Mutation erkannt'))
