"""GEOS-Gegenmessung: ganzer Gassenquerschnitt, Bucht- und Materialgrenzen."""
import json
import sys
from pathlib import Path
from shapely.geometry import Polygon, LineString
from shapely.ops import unary_union
from randmessung import welt_float, pruefe


def messe(eingabe):
    d = welt_float(eingabe)
    p = Polygon(d["Polygon"])
    gras = unary_union([Polygon(r) for r in d["Gras"]])
    asphalt = unary_union([Polygon(r) for r in d["Asphalt"]])
    # Der gemeinsame Pruefer verlangt AltBuchten nur fuer seine Vergleichszahl.
    # Hier wird gegen unabhaengige Schranken gemessen, nicht gegen einen Altbau.
    m = pruefe(dict(d, AltBuchten=[], Standlinien=[], Lampen=[]))
    korridore = []
    for a in d["Achsen"]:
        if a["Kind"] != "entrance":
            continue
        querschnitt = LineString([a["A"], a["B"]]).buffer(
            d.get("Gassenbreite", 5.5) / 2 - 0.002, cap_style=2).intersection(p.buffer(-0.002))
        gruen = querschnitt.intersection(gras).area
        fehlend = querschnitt.difference(asphalt).area
        korridore.append(dict(Gras=gruen, Fehlend=fehlend))
        if gruen > 0.01 or fehlend > 0.01:
            m["Fehler"].append(f"Gassenquerschnitt: Gras {gruen:.6f}, fehlender Belag {fehlend:.6f} m2")
    return dict(Fehler=m["Fehler"], Korridore=korridore, Maxima=m["Maxima"])


def main():
    anzahl = fehler = 0
    ziel = Path(".codex-build/kurven-14/zufahrten-messung.jsonl")
    with ziel.open("w", encoding="utf-8") as ausgabe:
        for datei in sys.argv[1:]:
            for zeile in Path(datei).read_text(encoding="utf-8-sig").splitlines():
                d = json.loads(zeile)
                # AUS hat keinen radialen Pfad; dessen vollstaendiger Bitvergleich
                # gegen dieselbe Eingabe ohne Kurvenlaeufe erfolgt im C#-Lauf.
                if not d["Strasse"]:
                    if d.get("OhneKurvenAbdruck") != d.get("Vollabdruck"):
                        raise ValueError("AUS-Layout ist nicht bitgleich zum Altpfad")
                    continue
                m = messe(d)
                ausgabe.write(json.dumps(dict(Name=d["Name"], Maske=d["Maske"], **m)) + "\n")
                if m["Fehler"]:
                    print(d["Name"], d["Maske"], *m["Fehler"][:8], sep="\n  ")
                    fehler += len(m["Fehler"])
                anzahl += 1
    print(f"Kurvenzufahrten GEOS: {anzahl} AN-Faelle, {fehler} Fehler (0,01 m2 / 2 mm / 5 cm).")
    return 1 if fehler else 0


if __name__ == "__main__":
    sys.exit(main())
