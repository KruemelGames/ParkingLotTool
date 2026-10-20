"""Native Ring-/Knotenzahlen, gegen alten Plan und gleich lange gerade Reihe."""
import argparse
import csv
import json
from pathlib import Path
from shapely.geometry import Polygon, LineString
from shapely.ops import unary_union
from randmessung import welt_float


def lesen(datei):
    return {(d["Name"], d["Strasse"], d["Zufahrt"]): d
            for d in map(json.loads, Path(datei).read_text(encoding="utf-8-sig").splitlines())}


def zaehlen(roh):
    d = welt_float(roh)
    buchten = unary_union([Polygon(d["Buchten"][i]) for i in d["Kurven"]])
    band = unary_union([LineString(r["Punkte"]).buffer(
        d["Randabstand"] / 2 + d["Buchttiefe"] + d.get("Fahrgassenbreite", 7), single_sided=True)
        .union(LineString(r["Punkte"]).buffer(-d["Randabstand"] / 2, single_sided=True))
        for r in d["Standlinien"]])
    m = {}
    for art in ("Gras", "Asphalt"):
        ringe = d[art]
        im_band = [r for r in ringe if Polygon(r).intersection(band).area > 0.01]
        an_buchten = [r for r in ringe if Polygon(r).intersection(buchten).area > 0.01]
        m[art + "Ringe"] = len(ringe)
        m[art + "Knoten"] = sum(map(len, ringe))
        m[art + "BandRinge"] = len(im_band)
        m[art + "BandKnoten"] = sum(map(len, im_band))
        m[art + "BuchtenRinge"] = len(an_buchten)
        m[art + "BuchtenKnoten"] = sum(map(len, an_buchten))
    return m


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("neu")
    parser.add_argument("--vorher", required=True)
    parser.add_argument("--gerade", required=True)
    parser.add_argument("--ausgabe", required=True)
    parser.add_argument("--zeiten-vorher")
    parser.add_argument("--zeiten-nachher")
    args = parser.parse_args()
    alt, neu, gerade = lesen(args.vorher), lesen(args.neu), lesen(args.gerade)
    assert alt.keys() == neu.keys(), "Messmatrix vorher/nachher unvollstaendig"
    def zeiten(datei):
        if not datei:
            return {}
        with Path(datei).open(encoding="utf-8-sig", newline="") as log:
            return {(r["Fall"], True, r["Zufahrt"] == "True"): r
                    for r in csv.DictReader(log, delimiter=";")}
    z_alt, z_neu = zeiten(args.zeiten_vorher), zeiten(args.zeiten_nachher)
    zeilen = []
    for key, n in neu.items():
        a = alt[key]
        assert "Fehler" not in n, (key, n.get("Fehler"))
        for feld in ("Buchten", "Achsen", "Lampen"):
            assert a[feld] == n[feld], (key, feld + " wurde geaendert")
        if not n["Strasse"]:
            assert n["AltAbdruck"] == n["NeuAbdruck"], (key, "AUS nicht bitgleich")
        zeile = dict(Name=key[0], Strasse=key[1], Zufahrt=key[2], Buchten=n["Neu"],
                     Kurvenbuchten=len(n["Kurven"]), Cs2Abgelehnt=n["Abgelehnt"])
        if key in z_alt and key in z_neu:
            zeit_a = float(z_alt[key]["NeuMedianMs"])
            zeit_n = float(z_neu[key]["NeuMedianMs"])
            zeile.update(VorherMedianMs=zeit_a, NachherMedianMs=zeit_n,
                         LaufzeitFaktor=zeit_n / zeit_a,
                         AltpfadMedianMs=float(z_neu[key]["AltMedianMs"]))
        for name, daten in (("Vorher", a), ("Nachher", n), ("Gerade", gerade.get(key))):
            if daten:
                zeile.update({name + feld: wert for feld, wert in zaehlen(daten).items()})
        zeilen.append(zeile)
    felder = list(dict.fromkeys(f for z in zeilen for f in z))
    with Path(args.ausgabe).open("w", encoding="utf-8-sig", newline="") as datei:
        log = csv.DictWriter(datei, fieldnames=felder, delimiter=";")
        log.writeheader()
        log.writerows(zeilen)
    print(f"Materialvergleich: {len(zeilen)} Varianten, Buchten/Achsen/Laternen unveraendert.")
    print("Band: Standlinie bis innerer Fahrbahnrand; native Ringe/Knoten, keine Mess-Schnittpolygone.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as e:
        import sys
        print(e, file=sys.stderr)
        raise SystemExit(1)
