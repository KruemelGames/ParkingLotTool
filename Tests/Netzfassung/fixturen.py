"""Alle Pflichtfixturen gegen den eingefrorenen Stand vor Runde 15 messen."""
import collections
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.codex-build/netzfassung'
MODI = ['', 'migrationen', 'statusmeldungen', 'sprache', 'laternen',
        'zoningflaeche', 'zoningnetz', 'zoningrasten', 'ueberlappung', 'versorgung',
        'infokarten', 'flaechenannahme', 'zufahrtsverlust', 'sonderplaetze',
        'zufahrtsquads', 'lformtoggle', 'fusswegzugang', 'gassenabstand',
        'endwegbreite', 'werkzeugzustand', 'diagonalwege', 'winkelmodus',
        'randzoningseite', 'querstrassengitter', 'geradepunkt', 'rzstufen',
        'ortsunabhaengig', 'rzanschluss', 'entarteteringe']


def schluessel(d):
    return hashlib.sha256((json.dumps(d['Polygon']) + d['Settings']).encode()).hexdigest()


def punkt(p):
    return tuple(struct.unpack('<f', struct.pack('<f', v))[0] for v in p)


def statistik(d):
    kurz = []
    graph = collections.defaultdict(set)
    for n in d['Net']:
        a, b = punkt(n['A']), punkt(n['B'])
        graph[a].add(b)
        graph[b].add(a)
        laenge = math.dist(a, b)
        if not math.isfinite(laenge) or laenge < 1:
            kurz.append(dict(Index=n['i'], Art=n['Kind'], Meter=laenge))
    offen = set(graph)
    komponenten = 0
    while offen:
        komponenten += 1
        stapel = [offen.pop()]
        while stapel:
            neu = graph[stapel.pop()] & offen
            offen.difference_update(neu)
            stapel.extend(neu)
    return kurz, komponenten


def projekt(art):
    quellen = dict(paritaet='Tests/Zufahrtsvorschau/Zufahrtsvorschau.csproj',
                   kurven='Tests/Kurvenlayout/Kurvenlayout.csproj',
                   echte='Tests/EchteVorschau/EchteVorschau.csproj')
    quelle = ROOT / quellen[art]
    ordner = OUT / art
    ordner.mkdir(parents=True, exist_ok=True)
    baum = ET.parse(quelle)
    gruppe = baum.getroot().find('ItemGroup')
    gruppe.clear()
    text = (ROOT / 'Geometry/ParkingGeometry.Types.cs').read_text(encoding='utf-8-sig')
    alt = '            return layout;'
    assert text.count(alt) == 1
    text = text.replace(alt, '            global::NetzFixturen.Merke(site, settings, layout);\n' + alt)
    (ordner / 'Types.cs').write_text(text, encoding='utf-8')
    for f in (ROOT / 'Geometry').rglob('*.cs'):
        ET.SubElement(gruppe, 'Compile', Include=str(ordner / 'Types.cs' if f.name == 'ParkingGeometry.Types.cs' else f))
    if art == 'paritaet':
        tests = list((ROOT / 'Tests/Zufahrtsvorschau').glob('*.cs')) + list((ROOT / 'Tests/GeometryParity/CSharp').glob('*.cs'))
    elif art == 'kurven':
        tests = list((ROOT / 'Tests/Kurvenlayout').glob('*.cs')) + [ROOT / 'Tests/Zufahrtsvorschau/Vergleich.cs']
    else:
        tests = list((ROOT / 'Tests/EchteVorschau').glob('*.cs')) + [ROOT / 'Tests/Zufahrtsvorschau/Vergleich.cs',
            ROOT / 'Tools/ParkingLotMusterobjektArchetyp.cs', ROOT / 'Tools/ParkingLotEchteVorschauEinstellung.cs']
        ET.SubElement(ET.SubElement(gruppe, 'Reference', Include='Newtonsoft.Json'), 'HintPath').text = \
            '$(SpielManaged)/Newtonsoft.Json.dll'
    for f in tests + [ROOT / 'Tests/Netzfassung/Fixturen.cs']:
        ET.SubElement(gruppe, 'Compile', Include=str(f))
    for ordnername in ['Tests/Zufahrtsvorschau', 'Tests/Kurvenlayout']:
        for f in (ROOT / ordnername).glob('*.json'):
            ET.SubElement(gruppe, 'None', Include=str(f), Link=f.name, CopyToOutputDirectory='PreserveNewest')
    ET.SubElement(ET.SubElement(gruppe, 'Reference', Include='Unity.Mathematics'), 'HintPath').text = \
        '$(CSII_TOOLPATH)/UnityModsProject/Library/ScriptAssemblies/Unity.Mathematics.dll'
    datei = ordner / 'Fixturen.csproj'
    baum.write(datei, encoding='unicode')
    return datei


def starten(projektdatei, mode, argumente):
    env = dict(os.environ, PLT_NETZ_AUDIT=str(OUT / 'aktuell.jsonl'), PLT_NETZ_MODUS=mode)
    with (OUT / (mode.replace('/', '-') + '.log')).open('w', encoding='utf-8') as log:
        subprocess.run(['dotnet', 'run', '-c', 'Release', '--no-build', '--project', str(projektdatei), '--'] + argumente,
                       cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    print(mode + ': Exit 0', flush=True)


def lauf():
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'aktuell.jsonl').write_text('', encoding='utf-8')
    for art in ['paritaet', 'kurven', 'echte']:
        p = projekt(art)
        with (OUT / (art + '-build.log')).open('w', encoding='utf-8') as log:
            subprocess.run(['dotnet', 'build', '-c', 'Release', str(p)], cwd=ROOT,
                           stdout=log, stderr=subprocess.STDOUT, check=True)
        if art == 'paritaet':
            for m in MODI:
                args = ['--paritaet'] + (['--' + m] if m else [])
                if m == 'sonderplaetze':
                    args += ['-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113',
                             '0,51.4;1,37.9;2,51.8;3,74.0']
                starten(p, m or 'standard', args)
            starten(p, 'zufahrtsvorschau', ['--referenz'])
        elif art == 'kurven':
            for m in ['randbuchten', 'randgeraden', 'kurvenzufahrten', 'knoten']:
                args = ['--kurvenzufahrten', '--knoten'] if m == 'knoten' else ['--' + m]
                starten(p, m, args + ['--ausgabe=' + str(OUT / (m + '.jsonl'))])
            starten(p, 'kurvenzufahrten-6m', ['--kurvenzufahrten', '--breite=6',
                    '--ausgabe=' + str(OUT / 'kurvenzufahrten-6m.jsonl')])
        else:
            starten(p, 'echte-vorschau', [])
    pruefen()


def pruefen():
    basis = {d['Key']: d for d in json.loads((ROOT / 'Tests/Netzfassung/VorRunde15.json').read_text())}
    zeilen = [json.loads(l) for l in (OUT / 'aktuell.jsonl').read_text().splitlines()]
    gesehen = set()
    geaendert = {}
    for d in zeilen:
        k = schluessel(d)
        assert k in basis, 'Neue Fixtur separat abnehmen: ' + d['Modus'] + '/' + k
        b = basis[k]
        kurz, komponenten = statistik(d)
        assert not kurz, 'Kurzer produktiver Kurs: ' + d['Modus'] + '/' + str(kurz)
        assert komponenten <= b['Komponenten'], 'Neue Netzluecke: ' + b['Name']
        assert d['Stalls'] == b['Buchten'], 'Buchtenzahl veraendert: ' + b['Name']
        if not b['Kurz']:
            assert d['Full'] == b['Full'], 'Unbetroffener Bau veraendert: ' + b['Name']
        elif d['Full'] != b['Full']:
            geaendert[k] = dict(Name=b['Name'], Key=k, Kurz=b['Kurz'], NetzAlt=b['Netzzahl'],
                               NetzNeu=len(d['Net']), NurNetz=d['Rest'] == b['Rest'])
        gesehen.add(k)
    assert gesehen == set(basis), 'Historische Fixturen fehlen: ' + str(len(set(basis) - gesehen))
    (OUT / 'aenderungen.json').write_text(json.dumps(list(geaendert.values()), indent=2), encoding='utf-8')
    print(f'Netz-Fixturen: {len(zeilen)} Bauten / {len(gesehen)} Eingaben, {len(geaendert)} betroffen; '
          f'{len(gesehen) - len(geaendert)} bitgleich, 0 Kurse unter 1 m.')


if __name__ == '__main__':
    try:
        if '--nur-vergleich' in sys.argv:
            pruefen()
        else:
            lauf()
    except Exception as exc:
        print('Netz-Fixturen FEHLER: ' + str(exc), file=sys.stderr)
        sys.exit(1)
