"""Alter Fangcode bleibt nach Entfernen der reinen Beobachteraufrufe identisch."""
from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parents[2]
original = json.loads(Path(__file__).with_name("FangOriginal.json").read_text())

def pruefe(datei, text):
    text = text.replace("\r\n", "\n")
    text = re.sub(r"^\s*(?:BeginKurvenfang|BestaetigeKurvenfang|MerkeKurvenquelle|MerkeKurvenstrasse|MerkeKurvenknotenquelle|MerkeKurvenlinienquelle|MerkeKurvenfanglinie|MerkeKurveneckenschnitt|BeginKurvenfanglinie|BeginKurveneckenschnitt)\([^\n]*\);\n", "", text, flags=re.M)
    return hashlib.sha256(text.encode()).hexdigest() == original[datei]

for name in original:
    text = (root / name).read_text(encoding="utf-8-sig")
    assert pruefe(name, text), f"Bestehender Fangcode geaendert: {name}"
    # Die Gegenprobe muss auch eine kleine Prioritaetsaenderung erkennen.
    if name.endswith("Snapping.cs"):
        assert "WeightCrossing = 2f" in text
        assert not pruefe(name, text.replace("WeightCrossing = 2f", "WeightCrossing = 3f"))
print("Fangvergleich: 2 Dateien identisch ohne Beobachter; Prioritaetsmutation erkannt.")
