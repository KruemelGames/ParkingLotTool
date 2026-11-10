"""Strikter Aenderungsvergleich fuer Auftrag 2; bestehende rote Laeufe bleiben rot.

Kein Toleranzkatalog: Ausgang und Ergebnis werden vollstaendig verglichen.
Positive Fahrradpruefungen verhindern, dass bloss unveraenderte Autos genuegen.
Aufruf aus dem Projektstamm: python Tests/Fahrrad/abschlussvergleich.py --mutationen
"""
from pathlib import Path
import copy
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
BELEGE = ROOT / '.codex-build/fahrrad'
AUSGANG = '2478c9a'


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)


def lesen(name):
    return (BELEGE / name).read_text(encoding='utf-8-sig')


def protokolle():
    return {
        'qualitaet': lesen('stufe6/qualitaet.log'),
        'qualitaet_alt': lesen('stufe6/qualitaet-ausgang.log'),
        'ausgaenge': lesen('stufe6/ergebnisse.txt'),
        'pflicht': lesen('stufe6-final/ergebnisse.txt'),
        'zettel': lesen('stufe6-final/zettelvollstaendig.log'),
        'zettel_alt': lesen('vorher/zettelvollstaendig.log'),
        'auto': json.loads(lesen('auto-stufe6.json')),
        'auto_alt': json.loads(lesen('auto-vorher.json')),
        'fahrrad': lesen('fahrrad-stufe6.log'),
        'spielbau': lesen('FahrradSpielbau-stufe6.log'),
        'ui': lesen('ui-stufe6.log'),
        'sprachen': lesen('ui-sprachen-stufe6.log'),
    }


def pruefen(d):
    fehler = []

    def muss(ok, text):
        if not ok:
            fehler.append(text)

    def faelle(s):
        return re.findall(r'^Qualitaet (\d+)/727: (.*) fertig, (\d+) Befunde$', s, re.M)

    def maengel(s):
        return [z for z in s.splitlines() if z.startswith('  FEHLER ')]

    aktuell, alt = faelle(d['qualitaet']), faelle(d['qualitaet_alt'])
    muss([int(f[0]) for f in aktuell] == list(range(1, 728)), 'Aktueller Lauf unvollstaendig')
    muss([int(f[0]) for f in alt] == list(range(1, 728)), 'Ausgangslauf unvollstaendig')
    muss(aktuell == alt, 'Fallbefunde veraendert')
    muss(bool(maengel(d['qualitaet_alt'])), 'Nachweis des roten Ausgangslaufs fehlt')
    muss(maengel(d['qualitaet']) == maengel(d['qualitaet_alt']), 'Fehlertexte veraendert')
    urteil = 'QUALITAETSTEST GESCHEITERT:'
    muss([z for z in d['qualitaet'].splitlines() if z.startswith(urteil)]
         == [z for z in d['qualitaet_alt'].splitlines() if z.startswith(urteil)]
         and urteil in d['qualitaet'], 'Rohurteil fehlt oder veraendert')
    exits = dict(re.findall(r'^(\S+) : (\d+)$', d['ausgaenge'], re.M))
    muss(exits.get('qualitaet') == exits.get('qualitaet-ausgang') == '1', 'Rote Roh-Exitcodes fehlen')
    muss(all(exits.get(k) == '0' for k in ['flaechen', 'triangulierung', 'lformen']),
         'Gruener README-Lauf fehlt oder ist rot')
    pflicht = dict(re.findall(r'^(\S+) : (\d+)$', d['pflicht'], re.M))
    muss(len(pflicht) == 31 and all(v == ('1' if k == 'zettelvollstaendig' else '0')
         for k, v in pflicht.items()), 'Pflichtlauf fehlt oder neu rot')
    # Nur reine Inventarzahlen aendern sich durch die zwei beauftragten Felder/Trigger.
    def zettel(s):
        return [z for z in s.splitlines() if z.startswith(('UI ', 'FEHLT:', 'ZU VIEL:',
                'DOPPELT:', 'PANEL '))]
    muss(zettel(d['zettel']) == zettel(d['zettel_alt']), 'Zettelbefunde veraendert')
    muss(re.findall(r', (\d+) Fehler\.', d['zettel'])
         == re.findall(r', (\d+) Fehler\.', d['zettel_alt']) != [], 'Zettelurteil veraendert')
    muss(len(d['auto']) == 12 and d['auto'] == d['auto_alt'], 'Auto-Bithashes veraendert')
    for name, objekte, kapazitaet in [('A', 84, 336), ('C', 24, 192), ('D', 84, 168)]:
        muss(f'{name}: {objekte} Objekte, {kapazitaet} Plaetze,' in d['fahrrad'],
             f'Positiver Fahrradnachweis {name} fehlt')
    muss('FAHRRAD: 5491 Pruefungen, 0 Fehler' in d['fahrrad'], 'Fahrradpruefung fehlt')
    muss('FAHRRAD-SPIELBAU: 6567 Pruefungen, 0 Fehler' in d['spielbau'], 'Spielbaupruefung fehlt')
    muss('UI-LAYOUT: 1628 Faelle, 0 mit Befund.' in d['ui'], 'Vollstaendige UI-Pruefung fehlt')
    muss('UI-LAYOUT: 130 Faelle, 0 mit Befund.' in d['sprachen'], 'Sprachstichproben fehlen')
    return fehler


def herkunft():
    # Git archiviert hier mit CRLF (z.B. 167 zusaetzliche Bytes im Auftragsregister).
    # Der normale Clean-Filter muss deshalb exakt das originale Git-Objekt liefern.
    basis = BELEGE / 'stufe6/ausgang-head'
    n = 0
    for zeile in git('ls-tree', '-r', AUSGANG, 'Geometry').decode().splitlines():
        meta, pfad = zeile.split('\t')
        blob = meta.split()[2]
        daten = (basis / pfad).read_bytes()
        ist = subprocess.check_output(['git', 'hash-object', '--path=' + pfad, '--stdin'],
                                      input=daten, cwd=ROOT).decode().strip()
        if ist != blob:
            raise AssertionError('Archivgeometrie veraendert: ' + pfad)
        n += 1
    if n == 0:
        raise AssertionError('Keine archivierte Geometrie gefunden')
    pfad = 'Tests/GeometryParity/CSharp/Program.Qualitaet.cs'
    vorher = git('show', AUSGANG + ':' + pfad).decode('utf-8-sig').splitlines()
    for stand in [ROOT, basis]:
        zeilen = (stand / pfad).read_text(encoding='utf-8-sig').splitlines()
        ohne_fortschritt = [z for z in zeilen if 'Console.WriteLine($"Qualitaet {i + 1}/' not in z]
        if ohne_fortschritt != vorher:
            raise AssertionError('Qualitaetskriterien veraendert: ' + str(stand))
    abzuege = list((BELEGE / 'vorher/auto').glob('*.json'))
    if len(abzuege) != 4:
        raise AssertionError('Vier Autoabzuege erforderlich')
    for p in abzuege:
        if p.read_bytes() != (BELEGE / 'stufe6-final/auto' / p.name).read_bytes():
            raise AssertionError('Autoabzug veraendert: ' + p.name)
    return n


def main():
    anzahl = herkunft()
    daten = protokolle()
    fehler = pruefen(daten)
    mutationen = []
    if '--mutationen' in sys.argv:
        for name in ['mehr-befunde', 'weniger-befunde', 'anderer-befund', 'unvollstaendig',
                     'auto-geaendert', 'fahrrad-nichts', 'pflicht-rot', 'readme-rot', 'zettel-neu']:
            d = copy.deepcopy(daten)
            if name == 'mehr-befunde':
                d['qualitaet'] += '\n  FEHLER neu: zusaetzlicher Mangel\n'
            elif name == 'weniger-befunde':
                d['qualitaet'] = re.sub(r'^  FEHLER .*\n', '', d['qualitaet'], count=1, flags=re.M)
            elif name == 'anderer-befund':
                d['qualitaet'] = d['qualitaet'].replace('Graskante 0,050', 'Graskante 0,051', 1)
            elif name == 'unvollstaendig':
                d['qualitaet'] = d['qualitaet'].replace('Qualitaet 727/727:', 'Qualitaet 726/727:')
            elif name == 'auto-geaendert':
                d['auto'][next(iter(d['auto']))] = 'FALSCH'
            elif name == 'fahrrad-nichts':
                d['fahrrad'] = d['fahrrad'].replace('A: 84 Objekte, 336 Plaetze,', 'A: 0 Objekte, 0 Plaetze,')
            elif name == 'pflicht-rot':
                d['pflicht'] = d['pflicht'].replace('zoningrasten : 0', 'zoningrasten : 1')
            elif name == 'readme-rot':
                d['ausgaenge'] = d['ausgaenge'].replace('flaechen : 0', 'flaechen : 1')
            else:
                d['zettel'] += '\nFEHLT: neuer Wert\n'
            erkannt = bool(pruefen(d))
            mutationen.append({'Mutation': name, 'Erkannt': erkannt})
            print(name, erkannt)
            if not erkannt:
                fehler.append('Mutation nicht erkannt: ' + name)
        # Auch die Quellenpruefung muss echte Aenderungen erkennen. Beide temporaeren
        # Mutationen liegen im Arbeitsverzeichnis und werden selbst bei Fehlern restauriert.
        for name, pfad in [('archiv-geaendert', BELEGE / 'stufe6/ausgang-head/Geometry/Auftragsregister.cs'),
                           ('kriterium-geaendert', ROOT / 'Tests/GeometryParity/CSharp/Program.Qualitaet.cs')]:
            original = pfad.read_bytes()
            erkannt = False
            try:
                pfad.write_bytes(original + b'\n// MUTATION: Quelle veraendert\n')
                try:
                    herkunft()
                except AssertionError:
                    erkannt = True
            finally:
                pfad.write_bytes(original)
            mutationen.append({'Mutation': name, 'Erkannt': erkannt})
            print(name, erkannt)
            if not erkannt:
                fehler.append('Mutation nicht erkannt: ' + name)
        herkunft()
    ergebnis = {'Ausgang': AUSGANG, 'Geometriedateien_identisch': anzahl,
                'Fehler': fehler, 'Mutationen': mutationen,
                'Rohlaeufe_weiterhin_rot': ['qualitaet', 'zettelvollstaendig'],
                'Native_Spielabnahme': 'offen'}
    ziel = BELEGE / 'stufe6-nachpruefung'
    ziel.mkdir(exist_ok=True)
    (ziel / 'abschlussvergleich.json').write_text(json.dumps(ergebnis, indent=2), encoding='utf-8')
    print(f'ABSCHLUSSVERGLEICH: {anzahl} Archivdateien, 727 Fallvergleiche, {len(fehler)} Fehler.')
    return 1 if fehler else 0


if __name__ == '__main__':
    raise SystemExit(main())
