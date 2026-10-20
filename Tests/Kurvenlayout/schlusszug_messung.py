"""Schlusszug: unabhaengige Materialpruefung mit unveraenderten Randschranken."""
import json
import sys
from pathlib import Path
from randmessung import pruefe


def main():
    datei = Path(sys.argv[1])
    fehler = 0
    faelle = 0
    maxima = {}
    with datei.with_name("schlusszug-messung.jsonl").open("w", encoding="utf-8") as ausgabe:
        for zeile in datei.read_text(encoding="utf-8-sig").splitlines():
            d = json.loads(zeile)
            # AUS muss im C#-Lauf vollstaendig bitgleich ohne Kurvenlauf sein.
            # Hier wird die korrigierte Rand-/Kappenkonstruktion mit AN gemessen.
            if not d["Strasse"]:
                continue
            # Der gemeinsame Pruefer berechnet auch einen optionalen
            # Altvergleich. Hier gibt es keinen Alt-Buchtenplan; weder
            # Platzhalter noch Altzahl werden als Messwert ausgegeben.
            d["AltBuchten"] = []
            m = pruefe(d)
            m.pop("AltNichtBeparkt", None)
            ausgabe.write(json.dumps(dict(Name=d["Name"], Umgekehrt=d["Umgekehrt"], **m)) + "\n")
            for k, v in m.get("Maxima", {}).items():
                maxima[k] = max(maxima.get(k, 0), v)
            if m["Fehler"]:
                print(d["Name"], d["Umgekehrt"], *m["Fehler"][:5], sep="\n  ")
                fehler += len(m["Fehler"])
            faelle += 1
    print(f"Schlusszug-Material: {faelle} AN-Bauten, {fehler} Fehler; Maxima {maxima}.")
    return 1 if fehler else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as ex:
        print("Schlusszug-Messung fehlgeschlagen:", ex, file=sys.stderr)
        sys.exit(1)
