# Sprachdateien und kompakte Beschriftungen

Stand 17.10.2026: Basis `ac9b609`, 970 Basisschluessel plus drei neue Sprachnamen.
RU/PL haben je 35 zusaetzliche .few/.many-Paare; KO hat identische Formen.
`Lang/en-US.json` ist die einzige Quelle fuer Schluessel und Platzhalter.

## Pruefen

```powershell
pwsh -NoProfile -File Tests/Language/pflichtlaeufe.ps1
# Einzeln:
dotnet build -c Release --project Tests/GeometryParity/CSharp/GeometryParity.csproj
dotnet run -c Release --no-build --project Tests/GeometryParity/CSharp/GeometryParity.csproj -- --sprache
python Tests/Language/check_cjk.py --report
```

Nur das Testprojekt bauen; der Mod-Build installiert ins Spiel.
`--sprache` prueft alle zehn Dateien; RU/PL muessen .few/.many fuer jede
Mehrzahlgruppe liefern. Andere Sprachen duerfen diese Formen nicht haben.
Platzhalternamen werden unabhaengig von der Satzreihenfolge verglichen.
Die stets gelieferte Zahl n darf in neuen .one-Vorlagen stehen, wo Englisch
woertlich 1 sagt: RU braucht sie fuer 21/101, KO fuer identische Vorlagen.
Alle anderen Namen bleiben unveraendert; RU darf diese Zahl nicht verlieren.

`check_cjk.py` prueft die 316 Basisschluessel aus `cjk-width-keys.json`:
ostasiatische breite/vollbreite Zeichen zaehlen 2, alle anderen 1.
Kompakte JA-/ZH-/RU-/PL-/KO-Texte duerfen die englische Breite nicht ueberschreiten.
Auch Pfeil-/Symbolbloecke und das Ersatzzeichen U+FFFD werden abgewiesen.
Gradzeichen, Quadratmeter, Waehrung und uebliche CJK-Interpunktion bleiben
erlaubt. Die vollstaendige Tabelle mit Texten und EN-/JA-/ZH-/RU-/PL-/KO-Breiten entsteht
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
sie werden nicht uebersetzt. Platzhalter bleiben als Text in der Messung,
auch das zusaetzliche n der RU-/KO-Singularvorlagen. Kurze eingesetzte
Infothemen/Sortierwerte werden ebenfalls geprueft.

**Bei neuen UI-Texten oder geaenderten Breiten das Manifest mitziehen.**
Dies ist eine nachvollziehbare Breitenrechnung, keine Messung der Spielschrift.
Die Ingame-Abnahme der neuen Sprachen in horizontalem und hochkantem Layout
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

## Russisch, Polnisch, Koreanisch (17.10.)

Die Kennungen ru-RU/pl-PL/ko-KR stehen sowohl im Dateinamen als auch im
Header ihrer .loc-Dateien in Cities2_Data/Content/Game/Locale.cok (ZIP).
Nur lesend ausgewertet. Beispiele der Schluessel: Assets.NAME[ParkingLot01],
Assets.NAME[Bus Stop], Assets.NAME[Bulldoze Tool], Services.NAME[Zones],
Services.NAME[Electricity], Services.NAME[Water & Sewage],
Assets.NAME[Alley], Assets.NAME[Gravel Road], SelectedInfoPanel.PARKING_FEE.

Begriffe: парковка / parking / 주차장; зоны / strefy / 구역;
автобусная остановка / przystanek autobusowy / 버스 정류장;
электричество / elektryczność / 전기; водоснабжение и канализация /
wodociągi i kanalizacja / 상하수도; бульдозер / buldożer / 불도저.
Die schmalen Labels kuerzen z. B. Переулок/Uliczka/골목길 auf Пер./Ulic./골목.
Eigene Begriffe fuer Bauplan, Traeger und Zoning-Strasse haben kein direktes
Vanilla-Gegenstueck. KO-Baumalter sind bewusst fachlich: 묘목/어린/성목/노목;
Vanilla schreibt 샘플링/청년/어른/노인. Die Kurzform 그루 meint den Baumstumpf.

Sprachtexte.Mehrzahlform ist die einzige Sprachregel. Fuer die UI kommt
zusaetzlich #mehrzahl als 100er-Tabelle in die bestehende JSON-Bindung:
genau +/-1 separat, sonst abs(n) modulo 100. Die Tabelle braucht keinen
neuen Sprachschluessel. Fehlendes .few/.many nutzt .other in derselben Sprache.
Bestehende Sprachen behalten genau 1/.other, auch fuer negative Werte.
Umsetzen waehlt nach der Zielsprache und ergaenzt n=1 bei woertlichem Singular.
Dimensionsvorlagen benutzen neutrale Mengenlabels statt numerusabhaengiger Nomen.

check_plural.cjs laedt die echten UI-Dateien mit minimalem API-Ersatz und
prueft die vom produktiven C#-Kern geschriebenen Faelle. gegenproben.py
verlangt 18 echte Fehler (keine Compilerfehler), restauriert JSON/TS bytegenau
und baut C#-Mutationen mit SprachtexteQuelle ausserhalb der Produktionsquellen.
Der Register-Pflichtlauf nimmt diese Tests mit. Main/Node/Python liefern bei
Fehlern Exit 1. Keine dieser Pruefungen baut oder installiert die Mod.
