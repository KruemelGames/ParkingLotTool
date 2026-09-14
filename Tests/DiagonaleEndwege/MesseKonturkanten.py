"""Abzugsmessung, kein Ersatz fuer einen Lauf der aktuellen C#-Geometrie.

Aufruf: python Tests/DiagonaleEndwege/MesseKonturkanten.py diagonal.json stufe.json
Exitcode 1 bedeutet: Das geforderte Verbindungsziel fehlt im Abzug.
Die Auswahl gilt fuer diese zwei Abzuege: automatische Endwege haben mindestens
6,9 m Querausdehnung; die einzige gesetzte Zufahrt hat weniger als 0,01 m.
Fuer andere Zugaenge ist eine explizite Herkunft im Export erforderlich.
"""
import argparse
import json
import math
from pathlib import Path

EPS = 0.001  # Weltkoordinaten aus float; 1 mm statt bitgenauem Vergleich.


def streifen(a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    laenge = math.hypot(dx, dy)
    nx, ny = -dy / laenge, dx / laenge  # Halbe Endwegbreite: 1 m.
    return [(a[0]-nx, a[1]-ny), (b[0]-nx, b[1]-ny),
            (b[0]+nx, b[1]+ny), (a[0]+nx, a[1]+ny)]


def beruehrt(a, b):
    # Trennachsenpruefung der konstruierten Rechtecke, inklusive Randkontakt.
    for ring in (a, b):
        for p, q in zip(ring, ring[1:] + ring[:1]):
            nx, ny = p[1]-q[1], q[0]-p[0]
            laenge = math.hypot(nx, ny)
            aa = [(x*nx+y*ny)/laenge for x, y in a]
            bb = [(x*nx+y*ny)/laenge for x, y in b]
            if max(aa) < min(bb)-EPS or max(bb) < min(aa)-EPS:
                return False
    return True


def komponenten(ringe):
    offen = set(range(len(ringe)))
    anzahl = 0
    while offen:
        stapel = [offen.pop()]
        anzahl += 1
        while stapel:
            i = stapel.pop()
            nachbarn = {j for j in offen if beruehrt(ringe[i], ringe[j])}
            offen -= nachbarn
            stapel.extend(nachbarn)
    return anzahl


def selbsttest():
    # Mutation: eine korrekte Verbindung auftrennen; der Messwert MUSS kippen.
    a = streifen((0, 0), (0, 10))
    b = streifen((0, 10), (0, 20))
    assert komponenten([a, b]) == 1
    b = streifen((0, 10.1), (0, 20))
    assert komponenten([a, b]) == 2
    assert komponenten([]) == 0  # Nichts-Tun darf nicht als 1 Verbindung gelten.
    c = streifen((0, 0), (10, 10))
    d = streifen((10, 10), (20, 20))
    assert komponenten([c, d]) == 1
    d = streifen((10.1, 10.1), (20, 20))
    assert komponenten([c, d]) == 2
    print('Messgeraet: 5 Gegenproben bestanden, darunter 2 Trennmutationen.')


def messe(datei, soll):
    daten = json.loads(Path(datei).read_text(encoding='utf-8-sig'))
    layout = daten['Preview']['Layout']
    linien = layout['AisleLine']
    a, b = linien[0][0], linien[0][-1]
    dx, dy = b['X']-a['X'], b['Z']-a['Z']
    laenge = math.hypot(dx, dy)
    dx, dy = dx/laenge, dy/laenge

    def lokal(p):
        return p['X']*dx+p['Z']*dy, -p['X']*dy+p['Z']*dx

    gassen = sorted([(lokal(g[0]), lokal(g[-1])) for g in linien],
                    key=lambda g: g[0][1])
    kontur = [lokal(p) for p in daten['Input']['PolygonXZ']]
    print(f'\n{Path(datei).name}: {len(kontur)} Ecken, '
          f'{layout["Stalls"]} Buchten, {len(gassen)} Gassen')
    print('Gassenenden laengs: ' + '  '.join(f'{b[0]:.1f}' for a, b in gassen))
    print('Konturkanten (nullbasiert, entlang des positiven Gassenstrahls):')
    benutzte_kanten = set()
    for nummer, (a, b) in enumerate(gassen):
        treffer = []
        for i, (p, q) in enumerate(zip(kontur, kontur[1:] + kontur[:1])):
            if abs(q[1]-p[1]) < 1e-9:
                continue
            t = (b[1]-p[1])/(q[1]-p[1])
            x = p[0]+t*(q[0]-p[0])
            if -1e-9 <= t <= 1+1e-9 and x >= b[0]-EPS:
                treffer.append((x, i))
        if not treffer:
            raise ValueError(f'Gasse {nummer}: kein Konturtreffer')
        x, kante = min(treffer)
        benutzte_kanten.add(kante)
        print(f'  Gasse {nummer}: Kante {kante}, Kontur laengs {x:.6f}, '
              f'Reserve {x-b[0]:.6f} m')
    for i, (p, q) in enumerate(zip(kontur, kontur[1:] + kontur[:1])):
        if i not in benutzte_kanten or abs(q[1]-p[1]) < 1e-9:
            continue
        steigung = (q[0]-p[0])/(q[1]-p[1])
        print(f'  Kante {i}: Neigung gegen quer '
              f'{math.degrees(math.atan(abs(steigung))):.6f} Grad, '
              f'Rechteckluecke bei 7 m {7*abs(steigung):.6f} m')

    # Gegenueberliegende Endwege und die gesetzte Zufahrt ausschliessen.
    mitte = sum((a[0]+b[0])/2 for a, b in gassen)/len(gassen)
    wege = []
    for linie in layout['EntranceLine']:
        a, b = lokal(linie[0]), lokal(linie[-1])
        if abs(b[1]-a[1]) >= 6.9 and (a[0]+b[0])/2 > mitte:
            wege.append((a, b))
    wege.sort(key=lambda w: min(w[0][1], w[1][1]))
    for a, b in wege:
        print(f'Endfussweg: quer {min(a[1], b[1]):.1f}..{max(a[1], b[1]):.1f}'
              f' bei laengs {a[0]:.1f}..{b[0]:.1f}')
    anzahl = komponenten([streifen(a, b) for a, b in wege])
    print(f'Endweg-Komponenten: {anzahl}, Ziel {soll}; '
          + ('OK' if anzahl == soll else 'FEHLER'))
    return anzahl == soll


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('diagonal')
    parser.add_argument('stufe')
    args = parser.parse_args()
    selbsttest()
    ergebnisse = [messe(args.diagonal, 1), messe(args.stufe, 2)]
    raise SystemExit(0 if all(ergebnisse) else 1)
