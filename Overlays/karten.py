# -*- coding: utf-8 -*-
"""
Erzeugt Einblend-Karten fuer die Videobearbeitung - eine je Tastenkuerzel.

Ergebnis sind PNG-Dateien MIT TRANSPARENZ, die sich in DaVinci Resolve direkt
auf eine obere Spur legen lassen. Kein Freistellen noetig.

Gestaltung nach dem Panel des Mods: dunkler, leicht durchscheinender Grund,
runde Ecken, oben der Titel, darunter die Tasten als Kappen.

Gerendert wird mit dem Edge, der auf dem Rechner ohnehin liegt - headless, mit
durchsichtigem Hintergrund und doppelter Aufloesung, damit die Kanten auch in
4K sauber bleiben.

    python Overlays/karten.py

Die Texte stehen unten in KUERZEL. Wer eine Karte aendern will, aendert dort
den Text und laesst das Skript neu laufen.
"""
import io
import os
import subprocess
import sys

HIER = os.path.dirname(os.path.abspath(__file__))
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

BREITE, HOEHE, SKALA = 1000, 340, 2

# ---------------------------------------------------------------------------
# DIE KARTE.
#
# Farben nach dem Panel: fast schwarzer Grund mit einem Stich ins Blaue, weisse
# Schrift, ein heller Saum oben, damit die Karte auf hellem Bild nicht
# verschwindet. Die Tastenkappen sind hell auf dunkel - so, wie man eine Taste
# im Bild erwartet, und gut lesbar auf jedem Untergrund.
# ---------------------------------------------------------------------------
VORLAGE = """<!doctype html>
<meta charset="utf-8">
<style>
  html,body{margin:0;padding:0;background:transparent;}
  body{
    width:%(breite)spx;height:%(hoehe)spx;
    display:flex;align-items:center;justify-content:center;
    font-family:"Segoe UI",system-ui,-apple-system,Arial,sans-serif;
  }
  .karte{
    box-sizing:border-box;
    width:%(kartenbreite)spx;
    padding:38px 48px 34px;
    background:rgba(14,18,24,.82);
    border:1px solid rgba(255,255,255,.14);
    border-top-color:rgba(255,255,255,.28);
    border-radius:22px;
    box-shadow:0 18px 48px rgba(0,0,0,.45);
    text-align:center;
  }
  .titel{
    color:#fff;font-size:40px;font-weight:600;line-height:1.15;
    letter-spacing:.2px;margin:0 0 26px;
  }
  .tasten{
    display:flex;align-items:center;justify-content:center;
    gap:14px;flex-wrap:wrap;
  }
  .kappe{
    display:inline-block;
    padding:12px 22px;min-width:34px;
    background:linear-gradient(180deg,#f4f7fa 0%%,#dfe5ec 100%%);
    color:#12171d;
    font-size:28px;font-weight:600;line-height:1;
    border-radius:10px;
    border:1px solid rgba(0,0,0,.35);
    box-shadow:0 4px 0 rgba(0,0,0,.45),0 6px 12px rgba(0,0,0,.35);
  }
  .plus{color:rgba(255,255,255,.65);font-size:30px;font-weight:400;}
  .notiz{
    color:rgba(255,255,255,.62);font-size:22px;line-height:1.35;
    margin:26px 0 0;
  }
</style>
<div class="karte">
  <p class="titel">%(titel)s</p>
  <div class="tasten">%(tasten)s</div>
  %(notiz)s
</div>
"""


def tastenhtml(tasten):
    teile = []
    for i, t in enumerate(tasten):
        if i:
            teile.append('<span class="plus">+</span>')
        teile.append('<span class="kappe">%s</span>' % t)
    return "".join(teile)


def baue(name, titel, tasten, notiz, ordner):
    html = VORLAGE % {
        "breite": BREITE, "hoehe": HOEHE,
        "kartenbreite": BREITE - 120,
        "titel": titel,
        "tasten": tastenhtml(tasten),
        "notiz": '<p class="notiz">%s</p>' % notiz if notiz else "",
    }
    htmlpfad = os.path.join(ordner, "html", name + ".html")
    pngpfad = os.path.join(ordner, name + ".png")
    os.makedirs(os.path.dirname(htmlpfad), exist_ok=True)
    io.open(htmlpfad, "w", encoding="utf-8").write(html)

    befehl = [
        EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars",
        "--default-background-color=00000000",
        "--force-device-scale-factor=%d" % SKALA,
        "--window-size=%d,%d" % (BREITE, HOEHE),
        "--screenshot=" + pngpfad,
        "file:///" + htmlpfad.replace("\\", "/"),
    ]
    subprocess.run(befehl, capture_output=True, timeout=120)
    return pngpfad, os.path.exists(pngpfad)


# ---------------------------------------------------------------------------
# DIE KUERZEL. Reihenfolge wie in TASTEN.md.
# (Dateiname, Titel deutsch, Titel englisch, Tasten, Notiz deutsch, Notiz engl.)
# ---------------------------------------------------------------------------
KUERZEL = [
    ("01-werkzeug", "Werkzeug ein- und ausschalten", "Toggle the tool",
     ["Strg", "Umschalt", "P"], ["Ctrl", "Shift", "P"],
     "In den Spieleinstellungen umstellbar",
     "Can be changed in the game settings"),
    ("02-punkt-setzen", "Punkt setzen", "Place a point",
     ["Linksklick"], ["Left click"], "", ""),
    ("03-punkt-zurueck", "Letzten Punkt zurücknehmen", "Undo the last point",
     ["Rechtsklick"], ["Right click"], "", ""),
    ("04-stufe-zurueck", "Eine Stufe zurück", "Step back one stage",
     ["Esc"], ["Esc"],
     "Wirft nicht alles weg", "Does not throw everything away"),
    ("05-kante-verschieben", "Kante verschieben", "Move an edge",
     ["Linksklick halten"], ["Hold left click"],
     "Kante anfassen und ziehen - sie geht senkrecht zu sich selbst",
     "Grab the edge and move it - it travels perpendicular to itself"),
    ("06-punkt-einfuegen", "Punkt in eine Kante einfügen",
     "Insert a point into an edge",
     ["Strg", "Linksklick"], ["Ctrl", "Left click"], "", ""),
    ("07-extrudieren", "Kante extrudieren", "Extrude an edge",
     ["Alt", "Linksklick halten"], ["Alt", "Hold left click"],
     "Kante anfassen und herausziehen",
     "Grab the edge and pull it out"),
    ("08-nachbarn-halten", "Nachbarkanten in ihrer Richtung halten",
     "Keep the neighbouring edges in line",
     ["Umschalt", "Linksklick halten"], ["Shift", "Hold left click"],
     "Beim Ziehen behalten die Nachbarn ihre Richtung",
     "While you move, the neighbours keep their direction"),
    ("09-zufahrt-setzen", "Zufahrt setzen", "Place an entrance",
     ["Umschalt", "Linksklick"], ["Shift", "Left click"], "", ""),
    ("10-zufahrt-loeschen", "Zufahrt löschen", "Delete an entrance",
     ["Rechtsklick"], ["Right click"],
     "Trifft die Zufahrt unter dem Zeiger",
     "Hits the entrance under the cursor"),
    ("11-bauen", "Bauen", "Build",
     ["Enter"], ["Enter"],
     "Ohne gedrückte Zusatztaste", "Without any modifier held"),
    ("12-rueckgaengig", "Rückgängig", "Undo",
     ["Strg", "Z"], ["Ctrl", "Z"], "", ""),
    ("13-wiederherstellen", "Wiederherstellen", "Redo",
     ["Strg", "Y"], ["Ctrl", "Y"],
     "Strg + Umschalt + Z geht auch", "Ctrl + Shift + Z works as well"),
    ("14-markiermodus", "Markiermodus ein- und ausschalten",
     "Toggle marking mode",
     ["Alt", "M"], ["Alt", "M"],
     "Linksklick markiert die Stelle, die falsch aussieht",
     "Left click marks the spot that looks wrong"),
    ("15-bericht", "Fehlerbericht schreiben", "Write a problem report",
     ["Alt", "P"], ["Alt", "P"], "", ""),
    ("16-objekt-beschreiben", "Angewähltes Objekt beschreiben",
     "Describe the selected object",
     ["Strg", "Alt", "P"], ["Ctrl", "Alt", "P"], "", ""),
    ("17-lotflaeche", "Lot-Besitzerfläche umschalten",
     "Toggle the lot owner surface",
     ["Umschalt", "P"], ["Shift", "P"],
     "Diagnoseschalter", "Diagnostic switch"),
]


def main():
    if not os.path.exists(EDGE):
        print("Edge nicht gefunden:", EDGE)
        return 1
    gesamt, fehlend = 0, []
    for name, titel_de, titel_en, tasten_de, tasten_en, notiz_de, notiz_en \
            in KUERZEL:
        for sprache, titel, tasten, notiz in (
                ("de", titel_de, tasten_de, notiz_de),
                ("en", titel_en, tasten_en, notiz_en)):
            ordner = os.path.join(HIER, sprache)
            pfad, ok = baue(name, titel, tasten, notiz, ordner)
            gesamt += 1
            if not ok:
                fehlend.append(pfad)
    print("%d Karten erzeugt, %d fehlgeschlagen" % (gesamt, len(fehlend)))
    for f in fehlend:
        print("  fehlt:", f)
    return 1 if fehlend else 0


if __name__ == "__main__":
    sys.exit(main())
