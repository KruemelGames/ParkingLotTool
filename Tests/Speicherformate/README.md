# Speicherformate und Edit-Freigabe

`pwsh -NoProfile -File Tests/Speicherformate/pflichtlaeufe.ps1` startet die
bisherigen 33 Pflichtlaeufe, dieses Projekt, Baukontext, Sprach-/CJK-Pruefung,
UI-Typpruefung und die Mutationen. Keine Mod-Kompilierung und kein Deploy.
Protokolle liegen unter `.codex-build/speichersicherheit-3-4/`.

Einzellauf: `dotnet run -c Release --project Tests/Speicherformate/Speicherformate.csproj`.
Mutationen: `pwsh -NoProfile -File Tests/Speicherformate/mutationen.ps1`.
Main faengt jede Ausnahme ab und liefert Exit 1; auch Mutationen oeffnen
kein Windows-Absturzfenster. Die produktiven Dateien bleiben dabei unveraendert.

## Eingefrorene Vertraege

`Fixturen/historisch.json` enthaelt feste, typisierte historische Feldfolgen,
jeweils mit zwei unterschiedlichen Datensaetzen. Die Tests fuegen eine
Endmarke an und rufen die eingebundenen **produktiven Deserialize-Methoden**
auf. Sie pruefen Feldtyp, gelesene Anzahl, Werte, historische Vorgaben und
die Endmarke. Die aktuell geschriebenen Fassungen werden auch gegen diese
festen Folgen geprueft. Die Fixturen werden beim Bauen nicht erzeugt.

| Typ | Fassungen | Herkunft des Feldvertrags |
| --- | --- | --- |
| Receipt | 1 bis 6 | a823206: Leser 1-3 / Schreiber 3; e9ecc32: 4-5; 43d68fb: 6 |
| Rueckwegkurs | 1, 2 | 35044f2 / cbcbe78 |
| ErhaltenerKurs | 1, 2 | delegiert an denselben Rueckwegkurs |
| Rueckweganschluss | ohne Kennung, -2 | 35044f2 / cbcbe78 |
| StufeAPrefab | alt ohne Version | 8775880, genau ein Entity-Feld |
| StufeAPrefabID | neu, Version 1 | eigene Komponente, Version und ID-String |
| NeubauFaellig, Abrissabsicht, NebenarbeitOffen | 1 | 8775880 |

Der Rueckweg-Datenteil wurde ohne Feld-/Leseraenderung aus den netzbauenden
Dateien nach `Tools/ParkingLotRueckwegDaten.cs` verschoben. So laesst er sich
ohne nativen Netzbau gegenpruefen. Die alte Stufe-A-Komponente bleibt bestehen.

## Weitere Pruefungen und Grenzen

Die Stufe-A-Aufloesung wird mit alter Entity, neuer ID, widersprechendem Cache,
fehlendem Asset, Cache-Reset beim Lesen und Abschluss beider Marker geprueft.
Die produktive C#-Infosektion wird mit einer kontrollierten ECS-/UI-Umgebung
ausgefuehrt: Sichtbarkeit ohne Bauzettel, Begruendung, Reparaturausloeser,
Rueckwegsperre, laufende Arbeit, Wiederfreigabe, geloeschte/fremde Flaechen.
Die Baukontext-Pruefung dieses Containers bildet nur die Komponentenpraesenz
ab; der echte BaukontextLeser wird separat in Tests/Baukontext geprueft.

Dies sind **Feldvertragspruefungen**, keine binaeren .cok-Spielstaende.
Container ersetzen IReader/IWriter und native ECS-/Prefab-/UI-Zugriffe;
Unity.Mathematics kommt aus der lokalen Spiel-Toolchain. Native Entity-
Remaps, PrefabReferences-Phasen und die Darstellung in Cohtml bleiben
Ingame-Pruefungen. Die Tests beweisen keine pauschale Lesbarkeit beliebiger
beschaedigter Saves und keine Rueckwaertskompatibilitaet neuer Saves mit alten Mods.
