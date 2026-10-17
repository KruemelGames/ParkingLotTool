"""Kompakte PLT-Texte pruefen: CJK = 2, sonst = 1 (Nutzerauftrag 2026-10-11).

Manifest: TSX/SCSS-Durchsicht, Begruendung in README.md. Platzhalter bleiben
als Text enthalten; auch das neue {n} statt woertlicher 1 wird mitgemessen.
Das ist ein Modell, keine Font-Pixelmessung.
Aufruf vom Repository aus: python Tests/Language/check_cjk.py
Mit --report entsteht die vollstaendige CSV im artifacts-Ordner.
"""
import argparse
import csv
import json
import pathlib
import sys
import unicodedata

ROOT = pathlib.Path(__file__).resolve().parents[2]


def width(text):
    return sum(2 if unicodedata.east_asian_width(c) in ("W", "F") else 1 for c in text)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", action="store_true")
    args = parser.parse_args()
    keys = json.loads((ROOT / "Tests/Language/cjk-width-keys.json").read_text(encoding="utf-8"))
    languages = {locale: json.loads((ROOT / f"Lang/{locale}.json").read_text(encoding="utf-8-sig"))
                 for locale in ("en-US", "ja-JP", "zh-HANS", "ru-RU", "pl-PL", "ko-KR")}
    errors = []
    rows = []
    comparisons = 0
    # Die leeren Undo-Knoepfe waren der Anlass: keine Pfeil-/Symbolbloecke
    # oder kaputten Ersatzzeichen in den neuen Dateien, auch in Tooltips.
    for locale in ("ja-JP", "zh-HANS", "ru-RU", "pl-PL", "ko-KR"):
        for key, value in languages[locale].items():
            if any(0x2190 <= ord(c) <= 0x2BFF or c == "\uFFFD" for c in value):
                errors.append(f"{locale} {key}: Sonderpfeil, Symbol oder Ersatzzeichen")
    if len(keys) != len(set(keys)):
        errors.append("Doppelte Schluessel im Breitenmanifest")
    for key in keys:
        if any(key not in data for data in languages.values()):
            errors.append(f"{key}: fehlt in einer Sprache")
            continue
        values = [data[key] for data in languages.values()]
        widths = list(map(width, values))
        for locale, value, measured in zip(languages, values, widths):
            if locale != "en-US":
                comparisons += 1
                if measured > widths[0]:
                    errors.append(f"{locale} {key}: EN {widths[0]}, Text {measured}: {value}")
        rows.append([key, *values, *widths])
        # Zusaetzliche CLDR-Formen benutzen denselben kompakten Platz.
        if key.endswith(".other"):
            for form in ("few", "many"):
                extra_key = key[:-5] + form
                extra_values = [languages["en-US"][key], "", ""]
                for locale in ("ru-RU", "pl-PL"):
                    value = languages[locale].get(extra_key)
                    if value is None:
                        errors.append(f"{locale} {extra_key}: Mehrzahlform fehlt")
                        value = ""
                    comparisons += 1
                    if width(value) > widths[0]:
                        errors.append(f"{locale} {extra_key}: EN {widths[0]}, Text {width(value)}: {value}")
                    extra_values.append(value)
                extra_values.append("")
                rows.append([extra_key, *extra_values, *map(width, extra_values)])
    if args.report:
        destination = ROOT / "artifacts/cjk-localization-widths.csv"
        destination.parent.mkdir(parents=True, exist_ok=True)
        with destination.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.writer(stream)
            writer.writerow(["Schluessel", "EN", "JA", "ZH", "RU", "PL", "KO", "Breite EN", "Breite JA", "Breite ZH", "Breite RU", "Breite PL", "Breite KO"])
            writer.writerows(rows)
        print(f"Breitenliste: {destination.relative_to(ROOT)}")
    for error in errors:
        print("FEHLER Breite: " + error)
    print(f"CJK-Breiten: {len(rows)} Schluessel, {comparisons} Vergleiche, {len(errors)} Fehler.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
