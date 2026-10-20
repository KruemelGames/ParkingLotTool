"""Unabhaengige Rechteck-, Deckungs- und Bogenmessung mit GEOS (kein buffer(0))."""
import json
import math
import sys
import struct
from pathlib import Path
from shapely.geometry import Polygon, LineString, Point
from shapely.ops import unary_union, nearest_points
from shapely.validation import explain_validity


def welt_float(d):
    # System.Text.Json schreibt die kuerzeste dezimale float-Rundreise.
    # GEOS rechnet double; die reale CS2-Koordinate ist aber dieser float32,
    # nicht der als double gelesene Dezimalbruch (bis 0,05 mm Unterschied).
    def punkt(p):
        return [struct.unpack("<f", struct.pack("<f", x))[0] for x in p]
    d = dict(d)
    for name in ("Polygon", "Formel"):
        if name in d:
            d[name] = [punkt(p) for p in d[name]]
    for name in ("Buchten", "Gras", "Asphalt", "AltBuchten", "AltGras", "AltAsphalt"):
        if name in d:
            d[name] = [[punkt(p) for p in r] for r in d[name]]
    d["Achsen"] = [dict(a, A=punkt(a["A"]), B=punkt(a["B"])) for a in d.get("Achsen", [])]
    d["Standlinien"] = [dict(l, Punkte=[punkt(p) for p in l["Punkte"]]) for l in d.get("Standlinien", [])]
    d["Lampen"] = [dict(l, Positionen=[dict(p, P=punkt(p["P"]), R=punkt(p["R"]))
                    for p in l["Positionen"]]) for l in d.get("Lampen", [])]
    return d


def graskeil(drehung, breite):
    # Unabhaengige Materialvorgabe des Nutzers: nur breite Keile zum Bordstein.
    return drehung > 0 and breite >= 1.0


def pruefe(d):
    d = welt_float(d)
    if d.get("Fehler"):
        return dict(Fehler=["Layout abgebrochen: " + d["Fehler"].splitlines()[0]])
    ungueltig = [f"{art}/{i}: {explain_validity(Polygon(r))}"
                 for art in ("Gras", "Asphalt") for i, r in enumerate(d[art])
                 if not Polygon(r).is_valid]
    if ungueltig:
        return dict(Fehler=ungueltig)
    p = Polygon(d["Polygon"])
    buchten = [Polygon(b) for b in d["Buchten"]]
    kurven = d["Kurven"]
    buchtverbund = unary_union(buchten)
    gras = unary_union([Polygon(r) for r in d["Gras"]])
    asphalt = unary_union([Polygon(r) for r in d["Asphalt"]])
    fehler = []
    maximum = dict(BuchtBucht=0.0, BuchtGras=0.0, BuchtOhneBelag=0.0, BuchtAussen=0.0, Fahrbahneingriff=0.0)
    # 2 mm entsprechen hier ausschliesslich der float-Ausgabe in Weltkoordinaten.
    for i in kurven:
        b = buchten[i]
        e = d["Buchten"][i]
        masse = sorted(math.dist(e[j], e[(j + 1) % 4]) for j in range(4))
        if any(abs(a - soll) > 0.002 for a, soll in zip(masse, [3, 3, 5.9, 5.9])):
            fehler.append(f"Bucht {i}: falsche Masse {masse}")
        maximum["BuchtAussen"] = max(maximum["BuchtAussen"], b.difference(p).area)
        maximum["BuchtOhneBelag"] = max(maximum["BuchtOhneBelag"], b.difference(asphalt).area)
        maximum["BuchtGras"] = max(maximum["BuchtGras"], b.intersection(gras).area)
        if b.difference(p).area > 0.002:
            fehler.append(f"Bucht {i}: ausserhalb {b.difference(p).area}")
        if b.difference(asphalt).area > 0.01:
            fehler.append(f"Bucht {i}: ohne Asphalt {b.difference(asphalt).area}")
        if b.intersection(gras).area > 0.01:
            fehler.append(f"Bucht {i}: Gras {b.intersection(gras).area}")
        for j, andere in enumerate(buchten):
            if j == i:
                continue
            maximum["BuchtBucht"] = max(maximum["BuchtBucht"], b.intersection(andere).area)
            if b.intersection(andere).area > 0.003:
                fehler.append(f"Buchten {i}/{j}: Ueberlappung {b.intersection(andere).area}")
        for a in d["Achsen"]:
            if a["Kind"] in ("perimeter", "aisle", "cross"):
                abstand = LineString([a["A"], a["B"]]).distance(b)
                breite = 3.5 if a["Kind"] != "cross" else 1.5
                maximum["Fahrbahneingriff"] = max(maximum["Fahrbahneingriff"], breite - abstand)
                if abstand < breite - 0.002:
                    fehler.append(f"Bucht {i}: {a['Kind']} {abstand}")
    if d["Abgelehnt"]:
        fehler.append("CS2-Ring abgelehnt")
    fehlend = p.difference(unary_union([gras, asphalt]))
    if fehlend.buffer(-0.025).area > 0.001:
        fehler.append(f"Materialluecke breiter 5 cm: {fehlend.area}")
    if gras.intersection(asphalt).buffer(-0.001).area > 0.001:
        fehler.append("Materialueberlappung breiter 2 mm")
    for i, j in zip(kurven, kurven[1:]):
        if buchten[i].distance(buchten[j]) > 0.01:
            continue
        a, b = d["Buchten"][i], d["Buchten"][j]
        n0 = [a[3][k] - a[0][k] for k in (0, 1)]
        n1 = [b[3][k] - b[0][k] for k in (0, 1)]
        drehung = n0[0] * n1[1] - n0[1] * n1[0]
        if abs(drehung) < 1e-3:
            continue
        eng0, eng1 = nearest_points(buchten[i], buchten[j])
        spitze = [(eng0.x + eng1.x) / 2, (eng0.y + eng1.y) / 2]
        weit0, weit1 = (a[1], b[0]) if drehung > 0 else (a[2], b[3])
        probe = Point([(weit0[k] + weit1[k] + spitze[k]) / 3 for k in (0, 1)])
        if not p.contains(probe) or any(q.covers(probe) for q in buchten):
            continue
        gruen = graskeil(drehung, math.dist(weit0, weit1))
        soll, anderes = (gras, asphalt) if gruen else (asphalt, gras)
        if not soll.covers(probe):
            fehler.append(f"Keil {i}/{j}: falsches Material")
        # Der ganze Keil, nicht nur seine Mitte: keine gruene Spitze
        # zwischen den Buchten und der Fahrbahn. Gleiche 0,01-m2-Grenze.
        keil = Polygon([weit0, weit1, spitze]).intersection(p).difference(buchtverbund)
        if keil.difference(soll).area > 0.01 or keil.intersection(anderes).area > 0.01:
            fehler.append(f"Keil {i}/{j}: Materialdeckung unvollstaendig")
    lampenabstaende = []
    for lauf in d["Standlinien"]:
        linie = LineString(lauf["Punkte"])
        for plan in d["Lampen"]:
            stationen = []
            for l in plan["Positionen"]:
                punkt = Point(l["P"])
                if linie.distance(punkt) > 0.002:
                    continue
                if not gras.covers(punkt) or asphalt.covers(punkt):
                    fehler.append("Kurvenlaterne steht nicht auf freiem Gras")
                stationen.append(linie.project(punkt))
            stationen.sort()
            for a, b in zip(stationen, stationen[1:]):
                abstand = b - a
                # Eine Zufahrt unterbricht die Reihe; der Abstand ueber diese
                # Sperre wird separat durch den produktiven Sperrplan geprueft.
                unterbrochen = any(a < y and b > x for x, y in lauf.get("Sperren", []))
                if "Sperren" not in lauf and d["Zufahrt"]:
                    raise ValueError("Bogenmessung braucht den produktiven Zufahrtssperrplan")
                if not unterbrochen and abs(abstand - plan["Abstand"]) > plan["Abstand"] * 0.1:
                    fehler.append(f"Laternenbogenabstand {abstand}/{plan['Abstand']}")
                if not unterbrochen:
                    lampenabstaende.append(abstand)
    return dict(Fehler=fehler, Buchtflaeche=sum(buchten[i].area for i in kurven),
                NichtBeparkt=p.area - sum(b.area for b in buchten),
                Lampenabstaende=lampenabstaende, Fehlend=fehlend.area, Maxima=maximum,
                AltNichtBeparkt=p.area - sum(Polygon(r).area for r in d["AltBuchten"]))


def pruefe_abnahme(d):
    m = pruefe(d)
    if not d["Strasse"]:
        # Auftrag Runde 12: AUS bleibt ausserhalb der Materialkorrektur.
        # Keine Fallausnahme: das GESAMTE Layout muss zum Altpfad bitgleich
        # sein. Die alten Deckungsbefunde bleiben im Messprotokoll sichtbar.
        if not d.get("AltAbdruck") or d["AltAbdruck"] != d.get("NeuAbdruck"):
            m["Fehler"].append("AUS-Layout weicht vom Altpfad ab")
        else:
            m["AltpfadBefunde"] = m["Fehler"]
            m["Fehler"] = []
    return m


def main():
    datei = Path(sys.argv[1] if len(sys.argv) > 1 else ".codex-build/kurven-11/randbuchten.jsonl")
    fehler = 0
    with datei.with_name("randmessung.jsonl").open("w", encoding="utf-8") as ausgabe:
        for l in datei.read_text(encoding="utf-8-sig").splitlines():
            d = json.loads(l)
            m = pruefe_abnahme(d)
            name = f"{d.get('Name', d['Radius'])}/{d['Strasse']}/{d['Zufahrt']}"
            ausgabe.write(json.dumps(dict(Name=name, **m)) + "\n")
            if m["Fehler"]:
                print(name, *m["Fehler"][:8], sep="\n  ")
                fehler += len(m["Fehler"])
    print(f"Unabhaengige Randmessung: {fehler} Fehler.")
    return 1 if fehler else 0


if __name__ == "__main__":
    sys.exit(main())
