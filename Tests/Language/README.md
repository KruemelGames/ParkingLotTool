# Japanisch und vereinfachtes Chinesisch

Ausgangsstand: `972e8e7` plus aktuelle englische Sprachdatei vom 11.10.2026.
`Lang/en-US.json` ist die einzige Quelle fuer Schluessel und Platzhalter.

## Pruefen

```powershell
dotnet build -c Release --project Tests/GeometryParity/CSharp/GeometryParity.csproj
dotnet run -c Release --no-build --project Tests/GeometryParity/CSharp/GeometryParity.csproj -- --sprache
python Tests/Language/check_cjk.py --report
```

Nur das Testprojekt bauen; der Mod-Build installiert ins Spiel.
`--sprache` verlangt vollstaendiges Deutsch sowie vollstaendige japanische
und chinesische Dateien, sofern diese vorhanden sind. Platzhalternamen
werden unabhaengig von ihrer Satzreihenfolge verglichen.

`check_cjk.py` prueft die 315 Schluessel aus `cjk-width-keys.json`:
ostasiatische breite/vollbreite Zeichen zaehlen 2, alle anderen 1.
Kompakte JA-/ZH-Texte duerfen die englische Breite nicht ueberschreiten.
Auch Pfeil-/Symbolbloecke und das Ersatzzeichen U+FFFD werden abgewiesen.
Gradzeichen, Quadratmeter, Waehrung und uebliche CJK-Interpunktion bleiben
erlaubt. Die vollstaendige Tabelle mit Texten und EN-/JA-/ZH-Breiten entsteht
in `artifacts/cjk-localization-widths.csv` (UTF-8 mit BOM fuer Tabellenprogramme).

## Warum diese Texte kompakt bleiben muessen

Durchgesehen wurden alle TSX-Komponenten und alle `*.module.scss` unter
`UI/src/mods`, einschliesslich der englischen Zuordnung in `texte.ts`.
Die aktuelle CSS-Obergrenze ist **1680rem** (`panel.module.scss:283`), nicht
1650rem; zudem greift `width:88%`. Die Breitenregel bleibt trotzdem strikt.

| Bereich | Aufrufer / Breitenbeleg | Manifest |
| --- | --- | --- |
| Kopf, Reiter, Zugangsarten | `panel.tsx`, `.markeText` / `.tab` mit nowrap, `.artName` mit hidden/nowrap | `ui.titel`, `ui.reiter*`, `ui.artName*`, `ui.bushaltestelle` |
| Draft: Spalten, Regler, Schalter | `panel.tsx`, `controls.tsx`; `.spaltenGruppe` verteilt die Breite; `.label` mit ellipsis/nowrap | Spaltentitel, Regler-/Schalterlabels, Winkelwerte, Einheiten |
| Draft: Aktionen und Zahlen | `panel.tsx`; `.abschluss`, `.aktionSpalte`, `.teilungAbzeichen`, `.smallButton`, Ergebniszeilen | Bauen, Ausrichten, Teilen, Zugangsschalter, Ergebnisvorlagen |
| Zoning | `zoning.tsx`; `Spalte`, `Slider`, `ModeChooser`, `.smallButton` und `.statusText` | Titel, Labels, Moduswerte, Aktionstexte, Bestandszahlen |
| Parkplatzliste | `liste.tsx`, `liste-gebuehr.tsx`; `.listeZelle`, `.listeNameText`, `.listeKopfAktionen .listeTextknopf`, `.listeBalkenZeile`, `.listeNotizKopf span` | Anzahl, Filter, Sortierung, Suche, Edit/Repair/Sync, Alter, Belegung, Geld, Infothemen und Seitenwerte |
| Vegetation | `vegetation.tsx`; Fenster 300/640rem, `.teilLinks` 284rem, `.teilRechts` 308rem | sichtbare Labels und Titel; kurze Icon-Tooltips vorsorglich mitgeprueft |
| Laternen | `laternen.tsx`, `laternen.module.scss`; feste Fensterhaelften, `.setName` nowrap, `.setKnopf` min-width 96rem | ein-/doppelte Lampe, Abstand, Sets und Auswahl; Setnamen inklusive |
| Anleitung | `anleitung.tsx`, `anleitung.module.scss`; `.kachel` 240rem bzw. 150rem hochkant | Episodentitel, Nummern, Kurzstatus |
| Melden und Debug | `report.tsx`, `debug.tsx`; `.werkzeugKnopf`, `.smallButton`, `.meldeGitterKnopf` 50% | kurze Werkzeugnamen, Titel, Aktionsbeschriftungen |
| Infofenster | `fee-section.tsx`, `employee-section.tsx`, `edit-section.tsx`; `fee-section.module.scss`, spielinterne Panelzeilen | Gebuehr, Angestellte, Edit/Repair, Bauplan, Name des Parkplatzes |
| ESC-Einstellungen | `Setting.cs`, spielinterne Optionszeilen | Labels, Gruppen, Tab, Enum-Werte, Knopfzustaende |

Die explizite Liste prueft vorsorglich auch einige kurze Texte, deren
Bausteine umbrechen koennen. Erklaerungen, lange Befunde, Infokarten-Saetze
und echte Tooltips sind ausgenommen: `.explain`, `.statusText`, `.frageText`,
`.listeHinweis` und `.waiseText` erlauben Umbruch. Die Hinweise der
Vegetationsfenster haben `min-height`, keine abschneidende Maximalhoehe.
Eigene Set-/Parkplatznamen, Prefab-Namen und Zahlen sind dynamische Werte;
sie werden nicht uebersetzt. Platzhalter tragen in allen Vorlagen denselben
Beitrag; kurze eingesetzte Infothemen/Sortierwerte werden ebenfalls geprueft.

**Bei neuen UI-Texten oder geaenderten Breiten das Manifest mitziehen.**
Dies ist eine nachvollziehbare Breitenrechnung, keine Messung der Spielschrift.
Die Ingame-Abnahme beider Sprachen in horizontalem und hochkantem Layout
bleibt erforderlich; dieser Auftrag erlaubt keinen Spiel-Build/Deploy.

## Terminologie

Einheitlich: Parkplatz = 駐車場 / 停车场; Strasse = 道路 / 道路;
Footpath = 歩道 / 步道; Zoning = ゾーン / 分区; Bulldozing = 撤去 / 拆除.
Technische Namen, Mod-/Prefab-Namen und Tastennamen bleiben erhalten.
Vanilla-Prefab-Auswahl `Alley` und `Gravel road` ist unveraendert.

Ohne Abgleich mit den nativen CS2-Woerterbuechern sind die exakten Spieltexte
fuer Wild-Sets, die sechs Baumalter, Zoning/Parzellen und Footpath nicht
verifiziert. Verwendet werden gebraeuchliche Begriffe: 野生の低木 / 野生灌木,
野生の広葉樹 / 野生阔叶树, 野生の針葉樹 / 野生针叶树;
苗木・若木・成木・老木・枯死・切株 bzw. 树苗・幼树・成树・老树・枯死・树桩.
Kurze Werte bei sehr kleinen englischen Labels: Fee = 料 / 费,
On/Off = 入/切 / 开/关; Erklaerungen stehen unverkuerzt im Tooltip.
