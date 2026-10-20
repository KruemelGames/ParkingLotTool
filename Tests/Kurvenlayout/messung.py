"""Unabhaengige Flaechenmessung der ausgegebenen Weltkoordinaten (Shapely 2)."""
import json
import math
import sys
from collections import Counter
from pathlib import Path

from shapely.geometry import LineString, Point, Polygon
from shapely.ops import unary_union


def messen(fall):
    # Derselbe deklarierte 1-mm-Eingangsstand; keine Polygonreparatur.
    umriss = Polygon([(round(x / .001) * .001, round(z / .001) * .001)
                      for x, z in fall["Polygon"]])
    ringe = [Polygon(p) for p in fall["Flaechen"]]
    invalid = sum(not p.is_valid for p in ringe)
    e = dict(Name=fall["Name"], Rand=fall["Rand"], Zufahrten=fall["Zufahrten"],
             UngueltigeRinge=invalid, UmrissGueltig=umriss.is_valid)
    if fall["Fehler"] or invalid or not umriss.is_valid:
        return e
    belegt = unary_union(ringe)
    fehlt = umriss.difference(belegt)
    # Gemeinsame float-Knoten koennen um unter 1 mm abweichen. Zusaetzlich
    # zur Rohflaeche deshalb einen Breitenzeugen messen, nicht Flaeche raten.
    innenringe = [p.buffer(-.001) for p in ringe]
    innenbelegt = unary_union(innenringe)
    # Umwandlung nach float an der Aussenkante ist von inneren Loechern
    # getrennt: das 1-mm-Randband verdeckt keine Luecke im Parkplatzinneren.
    e.update(Flaeche=umriss.area, Fehlend=fehlt.area,
             FehlendInnen=fehlt.difference(umriss.boundary.buffer(.001)).area,
             Ueberlappung=sum(p.area for p in ringe) - belegt.area,
             Ausserhalb=belegt.difference(umriss).area,
             LueckeBreiter2mm=fehlt.buffer(-.001).area,
             LueckeBreiter5cm=fehlt.buffer(-.025).area,
             UeberlappBreiter2mm=sum(p.area for p in innenringe) - innenbelegt.area,
             AusserhalbBreiter2mm=belegt.difference(umriss).buffer(-.001).area)
    enden = Counter(tuple(p) for linie in fall["Strassen"] for p in (linie[0], linie[-1]))
    e["RandnetzOffeneEnden"] = sum(n == 1 for n in enden.values())
    bogen = fall["Bogen"]
    if len(bogen) > 1:
        gruen = unary_union([Polygon(p) for p in fall["Gras"]])
        linien = [LineString(p) for p in fall["Strassen"]]
        netz = unary_union(linien)
        # Der Bogen steht am Anfang der Eingabe. Vorzeichen der ganzen Form
        # bestimmt die Innennormale; Erzeuger und Layout duerfen CW lesen.
        p = fall["Polygon"]
        sign = 1 if sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1]
                        for i in range(len(p))) > 0 else -1
        sonst = LineString(p[len(bogen) - 1:] + [p[0]])
        band = bandfehlt = strasse = strassefehlt = 0
        max_netzziel = 0.0
        for a, b in zip(bogen, bogen[1:]):
            dx, dz = b[0] - a[0], b[1] - a[1]
            laenge = math.hypot(dx, dz)
            nx, nz = -dz / laenge * sign, dx / laenge * sign
            for t in (.2, .5, .8):
                x, z = a[0] + t * dx, a[1] + t * dz
                q = Point(x + nx * .5, z + nz * .5)
                if umriss.contains(q) and sonst.distance(q) > 1:
                    band += 1
                    bandfehlt += not gruen.buffer(.001).covers(q)
                if fall["Rand"]:
                    q = Point(x + nx * 10.4, z + nz * 10.4)
                    if umriss.contains(q) and sonst.distance(q) > 13.91:
                        strasse += 1
                        # Materialpunkt kann zurueckgesetzt sein; Messung
                        # fordert daher Belag und Lage der geplanten Achse.
                        strassefehlt += not belegt.buffer(.001).covers(q)
                        if not netz.is_empty:
                            max_netzziel = max(max_netzziel, netz.distance(q))
        # Keine Aussage ueber gefaecherte Buchten: heute nur Sehnenrechtecke.
        kurvenrand = LineString(bogen)
        nahe_buchten = sum(Polygon(b).centroid.distance(kurvenrand) <= 7
                          for b in fall["BuchtenPolygone"])
        randbuchten = 0
        for b, art in zip(fall["BuchtenPolygone"], fall.get("Buchtarten", [])):
            if art != "Perimeter":
                continue
            # Nur lotrechte Projektion ins Innere des Bogens zaehlen;
            # die gerade Abschlusskante direkt neben dem Ende ist keine Kurve.
            mitte = Polygon(b).centroid
            entlang = kurvenrand.project(mitte)
            if .01 < entlang < kurvenrand.length - .01 and mitte.distance(kurvenrand) <= 7:
                randbuchten += 1
        e.update(RandbandProben=band, RandbandOhneGruen=bandfehlt,
                 StrassenProben=strasse, StrasseOhneFlaeche=strassefehlt,
                 AchsabstandMax=max_netzziel, KurvennaheBuchten=nahe_buchten,
                 Kurvenrandbuchten=randbuchten)
    return e


def main():
    datei = Path(sys.argv[1])
    daten = [json.loads(s) for s in datei.read_text(encoding="utf-8-sig").splitlines()]
    ergebnisse = [messen(d) for d in daten]
    ziel = datei.with_name("flaechenmessung.json")
    ziel.write_text(json.dumps(ergebnisse, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{len(ergebnisse)} unabhaengige Flaechenmessungen: {ziel}")
    for e in ergebnisse:
        if e.get("FehlendInnen", 0) > .01 or e.get("Ueberlappung", 0) > .01 or e["UngueltigeRinge"]:
            print(e)


if __name__ == "__main__":
    try:
        main()
    except Exception as fehler:
        print(fehler, file=sys.stderr)
        sys.exit(1)
