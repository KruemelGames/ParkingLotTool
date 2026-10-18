# Echte Vorschau: Flaechen, Laternen, Vegetation

Aufruf vom Projektstamm (nur Tests, kein Spielstart oder Deploy):

```powershell
pwsh -NoProfile -File Tests/EchteVorschau/pflichtlaeufe.ps1 -GameDllQuellen '<gamedll-Ordner>'
```

`-NurGezielt` laesst die bereits vorhandenen Pflichtlaeufe aus. Ohne diesen
Schalter folgen die SurfacePreview- und Register-Pflichtlaeufe automatisch.
Die Spielquellen werden nur gelesen. Laufdaten liegen in `.codex-build/`.

Geprueft werden die produktive Ausschluss-/Auswahllogik aus Geometry,
der echte Json.NET-Leser der neuen Einstellung und der produktive
Archetyp-Zusatz fuer reine Renderklone. Historische Leserfolgen des
unveraenderten gemeinsamen Markers prueft weiterhin Tests/Speicherformate.
Die Typumgebung im kleinen Projekt ist vereinfacht; die sichere native
Kompilierpruefung bleibt zusaetzlich erforderlich.

Die vier Messfaelle benutzen echte Layoutplaene: F-l-form mit 54 Buchten
und ein berechnetes Rechteck mit 3.870 Buchten. Gemessen wird nur die reine
Ausschnittwahl (31 Laeufe nach Aufwaermen), nicht der Pflanzenplaner oder
die native Ausgabe. Die kleine Laternenfixtur wird gegen ihre eingefrorene
Erwartung geprueft, damit ein falsch eingelesener leerer Plan nicht besteht.

17 Quellenmutationen plus 15 Griff-Gegenproben muessen Exit 1 mit echtem Prueffehler liefern.
Der gemeinsame Pflichtlauf ruft `griff-pflichtlaeufe.ps1` automatisch auf;
dieser prueft auch die produktive Eingabereihenfolge und Rohmaus-/Masken-Anbindung.
Die Griffmessung erfasst 100.000 warme Zug-/Hover-Paare mit Allokationszaehler,
keine behauptete native Zeichen- oder Bildzeit.
Die Mutationen aendern niemals Produktivdateien. Main faengt Ausnahmen ab.
`spielcode_befund.py` prueft 25 konkrete Quellcodevoraussetzungen;
das ersetzt keinen Ingame-Test fuer Sichtbarkeit, Farbe, Alter und Aufraeumen.
