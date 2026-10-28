"""Unabhaengige Deckung an echten Welt-float-Grenzen (GEOS, kein buffer(0))."""
import json
import struct
import sys
from shapely.geometry import Polygon
from shapely.ops import unary_union


def koordinate(p):
    return [struct.unpack("<f", struct.pack("<f", v))[0] for v in p]


def polygon(ring):
    return Polygon([koordinate(p) for p in ring])


def messen(faelle):
    fehler = 0
    for f in faelle:
        if "/gasse/" not in f["Name"]:
            continue
        ringe = [polygon(r) for art in ("Asphalt", "Gras") for r in f["Neu"][art]]
        ringe += [polygon(r) for r in f["Road"]]
        if any(not p.is_valid for p in ringe):
            print("FEHLER", f["Name"], "ungueltiger Ring")
            fehler += 1
            continue
        ziel = polygon(f["Polygon"])
        belag = unary_union(ringe)
        fehlt = ziel.difference(belag.buffer(.002)).area
        aussen = belag.difference(ziel.buffer(.002)).area
        ueberlappt = sum(p.buffer(-.002).intersection(q.buffer(-.002)).area
                        for i, p in enumerate(ringe) for q in ringe[:i])
        # Ausserhalb der 8-m-Achse ist nur Asphalt zulaessig. Zusaetzliche
        # Gruenreste duerfen nur unter der bestehenden 5-cm-Mindestbreite
        # bleiben und muessen die Gasse beruehren (keine breite Gruenrodung).
        strasse = unary_union([polygon(r) for r in f["Road"]])
        alt_asphalt = unary_union([polygon(r) for r in f["Alt"]["Asphalt"]])
        # Achsen kommen als numerische Koordinaten direkt aus NetLine.
        from shapely.geometry import LineString
        from math import hypot
        korridor_liste = []
        for a, b in f["Gassen"]:
            a, b = koordinate(a), koordinate(b)
            laenge = hypot(b[0] - a[0], b[1] - a[1])
            vorn = [a[k] - (b[k] - a[k]) * 4 / laenge for k in (0, 1)]
            korridor_liste.append(LineString([vorn, b]).buffer(4, cap_style=2))
        korridore = unary_union(korridor_liste)
        alt_gras = unary_union([polygon(r) for r in f["Alt"]["Gras"]])
        neu_gras = unary_union([polygon(r) for r in f["Neu"]["Gras"]])
        extra = alt_gras.difference(neu_gras).difference(korridore)
        teile = list(extra.geoms) if hasattr(extra, 'geoms') else [extra]
        erlaubt = []
        falsches_gruen = 0
        for teil in teile:
            if teil.area <= .01:
                continue
            if 2 * teil.area / teil.length <= .05 and teil.distance(korridore) <= .002:
                erlaubt.append(teil)
            else:
                falsches_gruen += teil.area
        erlaubt = unary_union(erlaubt)
        gras_ueberdeckt = strasse.difference(alt_asphalt.union(korridore).union(erlaubt).buffer(.002)).area
        ok = (fehlt <= .01 and aussen <= .01 and ueberlappt <= .01
              and gras_ueberdeckt <= .01 and falsches_gruen <= .01)
        fehler += not ok
        print(f'{f["Name"]}: fehlt {fehlt:.6f}, aussen {aussen:.6f}, '
              f'ueberlappt {ueberlappt:.6f}, Gras ueberdeckt {gras_ueberdeckt:.6f}, unzulaessiger Gruenverlust {falsches_gruen:.6f} m2; {"OK" if ok else "FEHLER"}')
    return fehler


if __name__ == "__main__":
    try:
        fehler = messen(json.load(open(sys.argv[1], encoding="utf-8")))
        print(f"Materialmessung: {fehler} Fehler.")
        sys.exit(1 if fehler else 0)
    except Exception as e:
        print(e, file=sys.stderr)
        sys.exit(1)
