# Lokale Mutationen: Originalbytes werden auch bei einem Laufabbruch restauriert.
from pathlib import Path
import os
import re
import subprocess

root = Path(__file__).resolve().parent.parent
source = root / 'Geometry/Zellen/Ringlos.cs'
original = source.read_bytes()
needle = b'            PlaneEndwegeNebenZufahrtsgassen();'
assert original.count(needle) == 1
out = root / 'artifacts/gassenreste-20260921'
out.mkdir(exist_ok=True)
env = dict(os.environ, PLT_PERIMETER_SNAPSHOT=str(root / 'artifacts/gassenreste-perimeter-formen.txt'))
command = ['dotnet', 'run', '-c', 'Release', '--project', 'Tests/GeometryParity/CSharp/GeometryParity.csproj', '--', '--gassenreste']
try:
    for name, replacement in [('alte-kreuzung', b'            // Mutation: alter durchgehender Endweg.'),
                              ('alle-fusswege-weg', b'            Fusswege.Clear();')]:
        source.write_bytes(original.replace(needle, replacement))
        run = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (out / ('mutation-' + name + '.txt')).write_bytes(run.stdout)
        print(name, 'Exitcode=' + str(run.returncode), flush=True)
        summary = re.search(rb'Gassenreste: .*?, (\d+) Fehler', run.stdout)
        if run.returncode != 1 or summary is None or int(summary.group(1)) == 0:
            raise RuntimeError('Mutation wurde nicht durch eine rote Regression erkannt')
finally:
    source.write_bytes(original)
    run = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (out / 'gassenreste.txt').write_bytes(run.stdout)
    print('Original restauriert: Exitcode=' + str(run.returncode), flush=True)
    if run.returncode != 0:
        raise RuntimeError('Restaurierter Stand ist nicht gruen')

