"""Messgeraet pruefen; --abnahme unterscheidet explizite Grenzen und offene Fehler."""
import copy
import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True
from messung import messen


def test(ok, name):
    if not ok:
        raise AssertionError(name)


def selbsttest():
    rechteck = [[0, 0], [10, 0], [10, 10], [0, 10]]
    basis = dict(Name="Selbsttest", Polygon=rechteck, Flaechen=[rechteck], Fehler=None,
                 Rand=False, Zufahrten=0, Bogen=[], Abgelehnt=0, Strassen=[])
    e = messen(basis)
    test(e["FehlendInnen"] == 0 and e["Ueberlappung"] == 0, "Deckung wird gefunden")
    loch = copy.deepcopy(basis)
    loch["Flaechen"] = [[[0, 0], [4, 0], [4, 10], [0, 10]],
                        [[6, 0], [10, 0], [10, 10], [6, 10]]]
    e = messen(loch)
    test(abs(e["Fehlend"] - 20) < 1e-9 and e["LueckeBreiter5cm"] > 0, "Loch wird gemessen")
    doppelt = copy.deepcopy(basis)
    doppelt["Flaechen"].append(rechteck)
    e = messen(doppelt)
    test(abs(e["Ueberlappung"] - 100) < 1e-9 and e["UeberlappBreiter2mm"] > 0,
         "Doppelflaeche wird gemessen")
    nadel = copy.deepcopy(basis)
    nadel["Flaechen"] = [[[0, 0], [4.9999, 0], [4.9999, 10], [0, 10]],
                        [[5.0001, 0], [10, 0], [10, 10], [5.0001, 10]]]
    e = messen(nadel)
    test(e["Fehlend"] > 0 and e["LueckeBreiter2mm"] == 0, "Sub-mm-Spalt bleibt separat sichtbar")
    krumm = copy.deepcopy(basis)
    krumm["Flaechen"] = [[[0, 0], [10, 10], [0, 10], [10, 0]]]
    test(messen(krumm)["UngueltigeRinge"] == 1, "Selbstschnitt wird nicht repariert")
    print("Kurvenlayout-Messgeraet: 5 Gegenproben gruen.")


def abnahme(datei):
    daten = [json.loads(s) for s in datei.read_text(encoding="utf-8-sig").splitlines()]
    offen = []
    grenzen = 0
    for d in daten:
        name = f"{d['Name']}/{d['Rand']}/{d['Zufahrten']}"
        if d["Fehler"]:
            # Das ist keine Liste erlaubter Fehlerfaelle: ein nachweislich
            # unmoeglicher Versatz oder fehlender Rasterplatz ist eine Grenze.
            if d["Versatz"] != "Keiner" or "No parking module fits" in d["Fehler"]:
                grenzen += 1
            else:
                offen.append(name + ": Kernfehler trotz gueltigem Versatz")
            continue
        if d["Buchten"] == 0:
            grenzen += 1
        e = messen(d)
        if d["Abgelehnt"] or e["UngueltigeRinge"]:
            offen.append(name + ": Ring nicht baubar")
        if e["LueckeBreiter5cm"] > 1e-6:
            offen.append(name + ": Materialluecke breiter als 5 cm")
        if e["UeberlappBreiter2mm"] > 1e-6:
            offen.append(name + ": Materialueberlappung breiter als 2 mm")
    for s in offen:
        print("OFFEN " + s)
    print(f"Kurvenlayout-Abnahme: {len(daten)} Varianten, {grenzen} begruendete Grenzen, {len(offen)} offene Befunde.")
    return 1 if offen else 0


def main():
    selbsttest()
    if len(sys.argv) > 1 and sys.argv[1] == "--abnahme":
        return abnahme(Path(sys.argv[2]))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as fehler:
        print(fehler, file=sys.stderr)
        sys.exit(1)
