# Strassenbelag an sichtbaren Zufahrtsgassen

Vom Repository-Wurzelverzeichnis:

    pwsh -NoProfile -File Tests/Strassenbelag/pflichtlaeufe.ps1

34 Bauten: gerade, schraege und gedrehte Weltform; Randstrassen an/aus;
Zufahrt/Fussweg als unveraenderte Kontrolle; Gasse/GasseEin/GasseAus;
zwei Gassen sowie gemeinsamer Belag. Die Referenzen stammen aus dem
Geometriestand 245e4f9 vor dieser Aenderung. `Referenz.json` prueft alle
alten Layoutfelder in 12 Faellen ohne Gasse bitweise, ausserdem Buchten,
Rollen und Wegenetz in allen 34 Faellen. Die neue Ringliste ist beim
Altvergleich bewusst ausgenommen. Die Materialreferenz enthaelt die
tatsaechlichen alten Weltkoordinaten, keine Sollwerte aus dem neuen Pfad.

C# prueft 8-m-Deckung anhand der ausgegebenen Gassenachsen und die
CS2-Triangulierung aller exportierten Materialringe. `messung.py` prueft
unabhaengig mit GEOS Deckung, Ueberlappung und die Begrenzung der
Knotenabdeckung auf vorhandenen Asphalt. Es liest die echten float32-Werte,
nicht die als double interpretierten JSON-Dezimalzahlen. Grenzwerte:
0,01 m2, 2 mm geometrische Randtoleranz. Rohflaechensummen werden zusaetzlich
protokolliert; submillimetergrosse Rundungsstreifen gibt es schon im Altpfad.

Fuenf Gegenproben verwenden Kopien unter `.codex-build/issue22`, niemals
geaenderte Produktivdateien: 5,5 statt 8 m, fehlende Materialtrennung und
ignorierte Gassen sowie die alte XOR-Randkette und ein faelschlich als
Materialring behandelter exakter Nullzyklus. Lot201905 wird zusaetzlich
als vollstaendiger Archivbau gerechnet. Jeder Test-Main faengt Ausnahmen ab und liefert Exit 1.

Offen am 27.10.: die schräge ringlose Gasse trennt einen 0,285-m2-Graskeil
ab, den der bestehende 5-cm-Exportfilter weglässt. Die unabhängige Messung
meldet deshalb in drei Varianten Fehler; der Pflichtlauf bleibt Exit 1.
Referenzen und Schranken wurden dafür nicht angepasst.
