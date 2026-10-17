"""Sprachfehler gezielt einsetzen; Dateien im finally bytegenau restaurieren."""
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / ".codex-build/sprachpruefung/mutationen"
PROJECT = "Tests/GeometryParity/CSharp/GeometryParity.csproj"
RUN = ["dotnet", "run", "-c", "Release", "--project", PROJECT]


def run(args):
    result = subprocess.run(args, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    return result.returncode, result.stdout.decode("utf-8-sig", errors="replace")


def expected(name, result):
    code, text = result
    (OUT / (name + ".log")).write_text(text, encoding="utf-8")
    if code != 1 or re.search(r"error (CS|MSB)[0-9]+", text) or "FEHLER" not in text:
        raise RuntimeError(f"Gegenprobe {name} nicht erkannt, Exit {code}")
    print(name + ": erwarteter Testfehler, Exit 1", flush=True)


def control():
    code, text = run(RUN + ["--", "--sprache"])
    (OUT / "kontrolle.log").write_text(text, encoding="utf-8")
    if code:
        raise RuntimeError("Produktiver Sprachlauf fehlgeschlagen: " + text[-2500:])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    control()
    data_cases = [
        ("RU-few-fehlt", "ru-RU", "ui.listeAnzahl.few", None, "sprache"),
        ("PL-many-fehlt", "pl-PL", "ui.listeAnzahl.many", None, "sprache"),
        ("DE-fremde-Form", "de-DE", "ui.listeAnzahl.few", "{n} Parkplaetze", "sprache"),
        ("RU-falscher-Platzhalter", "ru-RU", "ui.listeAnzahl.few", "{falsch}", "sprache"),
        ("RU-Singular-ohne-n", "ru-RU", "ui.syncFertig.one", "1 Parkplatz", "sprache"),
        ("KO-Schluessel-fehlt", "ko-KR", "ui.githubKnopf", None, "sprache"),
        ("KO-Formen-verschieden", "ko-KR", "ui.listeAnzahl.one", "{n} X", "sprache"),
        ("RU-Symbolpfeil", "ru-RU", "ui.tooltipBauen", "\u2192", "breite"),
        ("PL-Knopf-zu-breit", "pl-PL", "ui.bauen", "X" * 80, "breite"),
        ("PL-few-zu-breit", "pl-PL", "ui.listeAnzahl.few", "{n}" + "X" * 80, "breite"),
    ]
    for name, locale, key, value, check in data_cases:
        file = ROOT / "Lang" / (locale + ".json")
        old = file.read_bytes()
        try:
            data = json.loads(old.decode("utf-8-sig"))
            if value is None:
                del data[key]
            else:
                data[key] = value
            file.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
            args = RUN + ["--no-build", "--", "--sprache"] if check == "sprache" else [sys.executable, "Tests/Language/check_cjk.py"]
            expected(name, run(args))
        finally:
            file.write_bytes(old)
    source = (ROOT / "Geometry/Sprachtexte.cs").read_text(encoding="utf-8-sig")
    cases = [
        ("PL-nicht-RU", 'if (ru ? ende == 1 && rest != 11 : n == 1 || n == -1)', 'if (ende == 1 && rest != 11)'),
        ("Teen-Ausnahme", '(rest < 12 || rest > 14)', 'true'),
        ("Umsetzen-Regel", '? MehrzahlSchluessel(nach, basis, zahl)', '? basis + (zahl == 1 ? ".one" : ".other")'),
        ("Singularzahl-fehlt", 'var eins = schluessel.EndsWith(".one") && Array.IndexOf(namen, "n") < 0;', 'var eins = false;'),
        ("Rueckfall-fehlt", '&& !Hat(sprache, schluessel + form)', '&& false'),
        ("UI-Tabelle-falsch", 'Mehrzahlform(sprache, 100 + i)', 'Mehrzahlform("en-US", 100 + i)'),
    ]
    try:
        for name, old, new in cases:
            if source.count(old) != 1:
                raise RuntimeError("Mutationsstelle fehlt: " + name)
            mutated = OUT / (name + ".cs")
            mutated.write_text(source.replace(old, new), encoding="utf-8")
            result = run(RUN + ["-p:SprachtexteQuelle=" + str(mutated), "--", "--sprache"])
            if result[0] == 0:
                code, ui = run(["node", "Tests/Language/check_plural.cjs"])
                result = code, result[1] + ui
            expected(name, result)
    finally:
        control()
    ui_cases = [
        ("UI-alte-Regel", 'let form = mehrzahlEndung(mehrzahl, n);', 'let form = n === 1 ? ".one" : ".other";'),
        ("UI-Rueckfall-fehlt", 'if ((form === ".few" || form === ".many") && d[k + form] === undefined)', 'if (false)'),
    ]
    file = ROOT / "UI/src/mods/texte.ts"
    original = file.read_bytes()
    try:
        source = original.decode("utf-8-sig")
        for name, old, new in ui_cases:
            if source.count(old) != 1:
                raise RuntimeError("Mutationsstelle fehlt: " + name)
            file.write_bytes(source.replace(old, new).encode("utf-8"))
            expected(name, run(["node", "Tests/Language/check_plural.cjs"]))
    finally:
        file.write_bytes(original)
    code, text = run(["node", "Tests/Language/check_plural.cjs"])
    if code:
        raise RuntimeError("UI-Kontrolllauf fehlgeschlagen: " + text)
    print("Sprachgegenproben: 18/18 erkannt; Quellen und Sprachdateien restauriert.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    try:
        raise SystemExit(main())
    except Exception as error:
        print("FEHLER Gegenprobe: " + str(error), file=sys.stderr)
        raise SystemExit(1)
