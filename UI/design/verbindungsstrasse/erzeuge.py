# -*- coding: utf-8 -*-
"""Erzeugt die vier Tafeln zur Verbindungsstrassen-Regel.

Die Geometrie wird EINMAL gerechnet und in alle Tafeln gesetzt - von Hand
viermal dieselben Koordinaten zu tippen waere die sichere Quelle fuer vier
leicht verschiedene Bilder.
"""
import io

# --- Geometrie in Metern -------------------------------------------------
NEIGUNG = -0.465          # dx/dy der schraegen Arealkante
Y_REF, X_REF = 49.1, 87.5  # die Kante geht durch diesen Punkt
FAHRBAHN_H = 7.72          # Randstrasse, waagerecht gemessen
RANDREIHE_H = 6.51         # Randbuchtreihe
KANTE_H = 1.09             # Randabstand es
QUER_A, QUER_E = 100.0, 103.0
BUCHT = 3.0
VB = (57.0, 40.0, 83.0, 38.0)   # x y w h
RECHTS = VB[0] + VB[2]
OBEN, UNTEN = VB[1], VB[1] + VB[3]

def innen(y):
    return X_REF + NEIGUNG * (y - Y_REF) * -1 if False else X_REF - NEIGUNG * (Y_REF - y) * -1

def innen(y):                      # noqa: F811  - klar und ohne Vorzeichentrick
    return X_REF + (Y_REF - y) * 0.465

BAENDER = [
    ("gasse",  40.0, 40.7),
    ("reihe",  40.7, 46.6),
    ("gruen",  46.6, 49.1),
    ("reihe",  49.1, 55.0),
    ("gasse",  55.0, 62.0),
    ("reihe",  62.0, 67.9),
    ("gruen",  67.9, 70.4),
    ("reihe",  70.4, 76.3),
    ("gasse",  76.3, 78.0),
]
OBERE, UNTERE = (49.1, 55.0), (62.0, 67.9)

# --- Farben --------------------------------------------------------------
GELAENDE = "#d8d3c6"
GRAS     = "#6d8b55"
BELAG    = "#3f4144"
FAHRBAHN = "#56585d"
MARKIER  = "#e9e4d8"
AMBER    = "#d99a2b"
WARN     = "#c65a3f"
TINTE    = "#23211d"

def zahl(w):
    return ("%.2f" % w).rstrip("0").rstrip(".")

def punkte(paare):
    return " ".join("%s,%s" % (zahl(x), zahl(y)) for x, y in paare)

def band_polygon(y0, y1):
    return punkte([(innen(y0), y0), (RECHTS, y0), (RECHTS, y1), (innen(y1), y1)])

def buchten_links(y0, kappe_ab=None):
    """Volle Buchten links der Querstrasse; die Kante schneidet oben am meisten."""
    grenze = kappe_ab if kappe_ab is not None else innen(y0)
    n = int((QUER_A - grenze) // BUCHT)
    return QUER_A - n * BUCHT, n

def trennlinien(x0, x1, y0, y1):
    raus = []
    x = x0
    while x <= x1 + 1e-6:
        raus.append('<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" '
                    'stroke-width="0.22" opacity="0.5"/>'
                    % (zahl(x), zahl(y0), zahl(x), zahl(y1), MARKIER))
        x += BUCHT
    return "".join(raus)

def plan(variante):
    s = []
    A = s.append
    A('<rect x="%s" y="%s" width="%s" height="%s" fill="%s"/>'
      % (zahl(VB[0]), zahl(VB[1]), zahl(VB[2]), zahl(VB[3]), GELAENDE))

    kante_o = innen(OBEN) - FAHRBAHN_H - RANDREIHE_H - KANTE_H
    kante_u = innen(UNTEN) - FAHRBAHN_H - RANDREIHE_H - KANTE_H
    A('<polygon points="%s" fill="%s"/>' % (punkte([
        (kante_o, OBEN), (RECHTS, OBEN), (RECHTS, UNTEN), (kante_u, UNTEN)]), GRAS))

    # Randbuchtreihe und Randstrasse als schraege Baender
    A('<polygon points="%s" fill="%s"/>' % (punkte([
        (innen(OBEN) - FAHRBAHN_H - RANDREIHE_H, OBEN),
        (innen(OBEN) - FAHRBAHN_H, OBEN),
        (innen(UNTEN) - FAHRBAHN_H, UNTEN),
        (innen(UNTEN) - FAHRBAHN_H - RANDREIHE_H, UNTEN)]), BELAG))
    schritt = BUCHT / 0.907
    y = OBEN
    striche = []
    while y <= UNTEN + 1e-6:
        x1 = innen(y) - FAHRBAHN_H
        x2 = innen(y) - FAHRBAHN_H - RANDREIHE_H
        striche.append('<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" '
                       'stroke-width="0.22" opacity="0.45"/>'
                       % (zahl(x1), zahl(y), zahl(x2), zahl(y), MARKIER))
        y += schritt
    A("".join(striche))
    A('<polygon points="%s" fill="%s"/>' % (punkte([
        (innen(OBEN) - FAHRBAHN_H, OBEN), (innen(OBEN), OBEN),
        (innen(UNTEN), UNTEN), (innen(UNTEN) - FAHRBAHN_H, UNTEN)]), FAHRBAHN))

    # Baender des Innenbereichs
    for art, y0, y1 in BAENDER:
        if art == "gruen":
            continue
        farbe = FAHRBAHN if art == "gasse" else BELAG
        A('<polygon points="%s" fill="%s"/>' % (band_polygon(y0, y1), farbe))

    # Buchten je Reihe
    for art, y0, y1 in BAENDER:
        if art != "reihe":
            continue
        ist_obere = (y0, y1) == OBERE
        ist_untere = (y0, y1) == UNTERE
        start, n = buchten_links(y0)

        if variante == "A" and (ist_obere or ist_untere):
            A('<rect x="%s" y="%s" width="%s" height="%s" fill="%s"/>'
              % (zahl(start), zahl(y0), zahl(RECHTS - start), zahl(y1 - y0), BELAG))
            A(trennlinien(start, RECHTS, y0, y1))
            continue
        if variante == "C" and ist_obere:
            A('<polygon points="%s" fill="%s"/>' % (punkte([
                (innen(y0), y0), (QUER_A, y0), (QUER_A, y1), (innen(y1), y1)]), GRAS))
        elif n > 0:
            A(trennlinien(start, QUER_A, y0, y1))
            if variante == "B" and ist_obere:
                A('<rect x="%s" y="%s" width="%s" height="%s" fill="%s" opacity="0.32"/>'
                  % (zahl(start), zahl(y0), zahl(QUER_A - start), zahl(y1 - y0), WARN))
        A(trennlinien(QUER_E, RECHTS, y0, y1))

    # Querstrasse
    if variante == "A":
        for y0, y1 in ((OBEN, 46.6), (70.4, UNTEN)):
            A('<rect x="%s" y="%s" width="%s" height="%s" fill="%s"/>'
              % (zahl(QUER_A), zahl(y0), zahl(QUER_E - QUER_A), zahl(y1 - y0), FAHRBAHN))
        A('<rect x="%s" y="46.6" width="%s" height="%s" fill="none" stroke="%s" '
          'stroke-width="0.4" stroke-dasharray="1.6 1.2"/>'
          % (zahl(QUER_A), zahl(QUER_E - QUER_A), zahl(70.4 - 46.6), AMBER))
    else:
        A('<rect x="%s" y="%s" width="%s" height="%s" fill="%s"/>'
          % (zahl(QUER_A), zahl(OBEN), zahl(QUER_E - QUER_A), zahl(VB[3]), FAHRBAHN))
    return "".join(s)

# --- Beschriftungen je Tafel --------------------------------------------
def text(x, y, inhalt, groesse=2.0, farbe="#f4f1ea", anker="start", fett=600):
    return ('<text x="%s" y="%s" font-size="%s" fill="%s" text-anchor="%s" '
            'font-family="IBM Plex Mono, Consolas, monospace" font-weight="%d" '
            'letter-spacing="0.02">%s</text>'
            % (zahl(x), zahl(y), zahl(groesse), farbe, anker, fett, inhalt))

def klammer(x0, x1, y, farbe, beschriftung, oben=True):
    tief = 1.3 if oben else -1.3
    d = ("M %s %s L %s %s L %s %s L %s %s"
         % (zahl(x0), zahl(y + tief), zahl(x0), zahl(y),
            zahl(x1), zahl(y), zahl(x1), zahl(y + tief)))
    ty = y - 1.1 if oben else y + 2.6
    return ('<path d="%s" fill="none" stroke="%s" stroke-width="0.35"/>%s'
            % (d, farbe, text((x0 + x1) / 2, ty, beschriftung, 2.0, farbe, "middle", 700)))

TAFELN = {
    "Ausgangslage": dict(
        variante="", kicker="Ausgangslage", titel="So sieht es heute aus",
        unter="Die schr&#228;ge Randstra&#223;e l&#228;sst in der oberen Reihe nur "
              "vier Buchten &#252;brig, in der unteren sechs.",
        fazit="Schwelle: eine Reihe braucht mindestens <b>5 Buchten</b>. "
              "Die obere rei&#223;t sie, die untere nicht.",
        marken=lambda: (
            klammer(88, 100, 48.4, WARN, "4 Buchten") +
            klammer(82, 100, 69.2, "#cfe0bd", "6 Buchten", oben=False) +
            text(104, 45.6, "Verbindungsstra&#223;e", 1.9, "#e8e3d6") +
            text(64, 44.5, "Randstra&#223;e", 1.9, "#e8e3d6") +
            text(120, 59.2, "Fahrgasse", 1.9, "#e8e3d6")),
    ),
    "Main": dict(
        variante="A", kicker="Variante A", titel="Die kleinere Reihe entscheidet",
        unter="Vier ist weniger als f&#252;nf, also f&#228;llt das St&#252;ck "
              "Verbindungsstra&#223;e hier weg. Beide Reihen wachsen durch.",
        fazit="<b>Kein einsames Gr&#252;ppchen, ein Konfliktpunkt weniger, mehr "
              "Buchten.</b> Preis: die untere Reihe verliert ihre Verbindung, "
              "obwohl sechs in Ordnung gewesen w&#228;ren.",
        marken=lambda: (
            text(104, 45.0, "St&#252;ck entfernt", 2.0, AMBER, "start", 700) +
            text(104, 47.6, "oben und unten l&#228;uft sie weiter", 1.6, "#e8e3d6") +
            klammer(88, 139, 48.4, "#cfe0bd", "17 Buchten am St&#252;ck") +
            klammer(82, 139, 69.2, "#cfe0bd", "19 Buchten am St&#252;ck", oben=False)),
    ),
    "VarianteB": dict(
        variante="B", kicker="Variante B", titel="Die gr&#246;&#223;ere Reihe entscheidet",
        unter="Sechs reicht, also bleibt die Verbindungsstra&#223;e stehen - "
              "und mit ihr die vier oben.",
        fazit="<b>Genau der Fehler, wegen dem wir das bauen.</b> Die vier Buchten "
              "liegen allein hinter der Stra&#223;e.",
        marken=lambda: (
            klammer(88, 100, 48.4, WARN, "4 bleiben einsam") +
            klammer(82, 100, 69.2, "#cfe0bd", "6 Buchten", oben=False)),
    ),
    "VarianteC": dict(
        variante="C", kicker="Variante C", titel="Stra&#223;e bleibt, die vier fallen weg",
        unter="Die Verbindungsstra&#223;e bleibt, die zu kurze Reihe wird gar "
              "nicht erst gebaut.",
        fazit="Kein einsames Gr&#252;ppchen - aber <b>vier Buchten sind verloren</b> "
              "und oben links bleibt eine L&#252;cke.",
        marken=lambda: (
            '<rect x="88" y="49.1" width="12" height="5.9" fill="none" stroke="%s" '
            'stroke-width="0.4" stroke-dasharray="1.6 1.2"/>' % WARN +
            text(86, 47.6, "4 Buchten verloren", 2.0, WARN, "start", 700) +
            klammer(82, 100, 69.2, "#cfe0bd", "6 Buchten", oben=False)),
    ),
}

RAHMEN = u'''<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@500;700&family=IBM+Plex+Mono:wght@400;600&display=swap">
  <style>
    body { margin: 0; }
    a { color: #b06b1f; } a:hover { color: #8a5316; }
  </style>
</helmet>
<div style="width: 900px; box-sizing: border-box; padding: 34px 38px 30px; background: #f4f2ed; font-family: 'Space Grotesk', 'Trebuchet MS', sans-serif; color: #23211d;">
  <div style="display: flex; align-items: baseline; gap: 14px;">
    <div style="font-family: 'IBM Plex Mono', Consolas, monospace; font-size: 13px; font-weight: 600; letter-spacing: 0.12em; text-transform: uppercase; color: __KICKERFARBE__;">__KICKER__</div>
    <div style="height: 1px; flex-grow: 1; background: #cdc7b8;"></div>
  </div>
  <h1 style="margin: 10px 0 6px; font-size: 30px; font-weight: 700; letter-spacing: -0.01em;">__TITEL__</h1>
  <p style="margin: 0 0 20px; font-size: 15px; line-height: 1.5; color: #55514a; max-width: 62ch;">__UNTER__</p>
  <svg viewBox="__VB__" style="display: block; width: 100%; height: auto; border-radius: 3px;">__PLAN____MARKEN__</svg>
  <div style="display: flex; gap: 18px; align-items: flex-start; margin-top: 18px;">
    <div style="display: flex; flex-direction: column; gap: 6px; flex-shrink: 0; font-family: 'IBM Plex Mono', Consolas, monospace; font-size: 11px; color: #55514a;">
      __LEGENDE__
    </div>
    <div style="width: 1px; align-self: stretch; background: #cdc7b8;"></div>
    <p style="margin: 0; font-size: 14px; line-height: 1.55; color: #3a3630;">__FAZIT__</p>
  </div>
</div>
</x-dc>
</body>
</html>
'''

def legende():
    eintraege = [(FAHRBAHN, u"Fahrbahn"), (BELAG, u"Buchten"),
                 (GRAS, u"Gr&#252;n"), (GELAENDE, u"au&#223;erhalb")]
    return "".join(
        '<div style="display: flex; align-items: center; gap: 7px;">'
        '<span style="width: 13px; height: 13px; border-radius: 2px; background: %s;'
        ' border: 1px solid rgba(0,0,0,.18);"></span>%s</div>' % (f, t)
        for f, t in eintraege)

for name, t in TAFELN.items():
    seite = (RAHMEN
             .replace("__KICKER__", t["kicker"])
             .replace("__KICKERFARBE__", AMBER if t["variante"] == "A" else "#8a8375")
             .replace("__TITEL__", t["titel"])
             .replace("__UNTER__", t["unter"])
             .replace("__VB__", "%s %s %s %s" % tuple(zahl(v) for v in VB))
             .replace("__PLAN__", plan(t["variante"]))
             .replace("__MARKEN__", t["marken"]())
             .replace("__LEGENDE__", legende())
             .replace("__FAZIT__", t["fazit"]))
    io.open(name + ".dc.html", "w", encoding="utf-8", newline="\n").write(seite)
    print("geschrieben:", name + ".dc.html")

canvas = u'''{
  "artboards": [
    { "file": "Ausgangslage.dc.html", "x": 0,    "y": 0,   "w": 900, "h": 640 },
    { "file": "Main.dc.html",         "x": 1000, "y": 0,   "w": 900, "h": 640 },
    { "file": "VarianteB.dc.html",    "x": 0,    "y": 760, "w": 900, "h": 640 },
    { "file": "VarianteC.dc.html",    "x": 1000, "y": 760, "w": 900, "h": 640 }
  ],
  "annotations": [
    { "id": "die-frage", "x": 0, "y": -190, "w": 640,
      "text": "Die Regel steht: passen weniger als 5 Buchten in eine Reihe, faellt das Stueck Verbindungsstrasse dort weg.\n\nOffen ist nur der Fall, in dem die beiden Reihen an EINER Fahrgasse verschieden lang sind. Darum geht es hier." }
  ],
  "launch": { "view": "canvas" }
}
'''
io.open("canvas.json", "w", encoding="utf-8", newline="\n").write(canvas)
print("geschrieben: canvas.json")
