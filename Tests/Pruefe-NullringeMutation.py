from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
source = root / 'Geometry/Zellen/Zerlegung.cs'
original = source.read_bytes()
text = original.decode('utf-8-sig')
fixed = 'var a = punkte[i] - ursprung; var b = punkte[(i + 1) % punkte.Length] - ursprung;'
mutant = 'var a = punkte[i]; var b = punkte[(i + 1) % punkte.Length];'
assert text.count(fixed) == 1, 'Mutationsstelle nicht eindeutig'
try:
    source.write_text(text.replace(fixed, mutant), encoding='utf-8')
    with (root / 'artifacts/nullringe/mutation.txt').open('w', encoding='utf-8') as log:
        result = subprocess.run(['dotnet', 'run', '-c', 'Release', '--project',
            'Tests/GeometryParity/CSharp/GeometryParity.csproj', '--', '--entarteteringe'],
            cwd=root, stdout=log, stderr=subprocess.STDOUT)
    assert result.returncode == 1, f'Mutation muss fachlich rot werden: {result.returncode}'
    output = (root / 'artifacts/nullringe/mutation.txt').read_text(encoding='utf-8')
    assert 'Entarteteringe: 1 Fehler' in output, 'Erwarteter Nullring nicht erkannt'
    with (root / 'artifacts/nullringe/mutation-konstruktion.txt').open('w', encoding='utf-8') as log:
        construction = subprocess.run(['dotnet', 'run', '-c', 'Release', '--project',
            'Tests/NullringeKonstruktion/NullringeKonstruktion.csproj'],
            cwd=root, stdout=log, stderr=subprocess.STDOUT)
    output = (root / 'artifacts/nullringe/mutation-konstruktion.txt').read_text(encoding='utf-8')
    assert construction.returncode == 1 and '8 Faelle, 6 Fehler' in output, \
        'Konstruktionstest muss die 6 ortsabhaengigen Verluste erkennen'
    print('Mutation erkannt: Nullringe 1 Fehler, Konstruktion 6 Fehler; Quelle wird wiederhergestellt.')
finally:
    source.write_bytes(original)
