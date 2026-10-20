# Zufahrtsvorschau und CS2-Flaechenannahme

`pwsh -NoProfile -File Tests/Zufahrtsvorschau/pflichtlaeufe.ps1`

Reine Produktivgeometrie, kein ECS, kein Mod-Build/Deploy und kein Spielstart.
Main faengt Ausnahmen und liefert Exit 1; der Datenvergleich begrenzt seine
Rekursion, damit auch ein fehlerhaftes Messgeraet keinen Absturzdialog erzeugt.

- `Bericht-20261019.json` friert das 71-Punkte-Polygon, alle Einstellungen
  und die **materialisierten** Knoten von 141 Materialflaechen aus dem echten
  Spielbericht ein. 139 sind angenommen, 2 mit `NoTriangles` abgelehnt.
  Der LotOwner ist keine Materialflaeche und gehoert nicht zu dieser Probe.
  Hoehen sind fuer `Snip`/`GetExpandedNode` in XZ nicht beteiligt.
- Beide abgelehnten Ringe muessen gerettet werden, ihre Flaechenbilanz muss
  bis 0,01 m2 stimmen; kein ausgegebenes Stueck darf abgelehnt werden.
- 18 Form-/Einstellungskombinationen mit 0/1/2 Zufahrten vergleichen ALLE
  Layoutdaten bitweise, einschliesslich interner Rollenflaechen und Warnungen.
  Vorhandene Zufahrten bleiben; fehlende Kontrollzufahrten werden mittig auf
  langen, verschiedenen Seiten gesetzt. Die Eingaben stammen aus dem neuen
  Bericht, allen vier `GeometryParity/Berichtsfaelle`, den drei Originalabzuegen
  sowie geraden/schraegen Rechteck-/L-Kontrollen mit und ohne Randstrasse.
- `Lot136479.json` stammt unverkuerzt aus Bauzettel PLT-A9D9B47A
  (15:34:12): 49 Punkte, Randstrassen AUS, eine gesetzte Zufahrt. Der zweite
  Kontrollzugang stammt aus dem Bauzettel 15:35:55 und wird hier mit dem
  ersten kombiniert. Der Vergleich prueft beide Randstrassenstellungen.
  Der Spielvergleich 477 gegen 509 Buchten wechselte auch diese Stellung:
  AUS/1 liefert 477, AN/0 liefert 509; AUS/0 liefert dagegen 494.
- Dieselben 54 Ergebnisse werden mit den bisherigen Teilerschleifen und der
  bisher je Knoten berechneten Normalenlaenge verglichen. Referenzkopien und
  Mutationen entstehen ausschliesslich unter `.codex-build/kurven-8`.
  Die Referenz nimmt auch Einzelpolygon-Wiederverwendung und die direkte
  Mittelpunktiteration zurueck; sie laesst nur den Allokationsgrenzwert aus.
- Koordinaten-/Versatzwechsel, Identitaeten und veraenderbare Polygonlisten
  werden separat geprueft. Counter belegen echte Cachetreffer.
- Sieben Mutationen: Einzelarrays je Anfrage, Mittelpunktiterator, alte Knoten ignorieren, Koordinaten ignorieren, IDs nicht
  fortsetzen, Versatzabstand ignorieren, Innenrand ohne Kopie herausgeben.

`alle-fixturen.ps1` wird ebenfalls aufgerufen. Es verknuepft die vorhandenen
GeometryParity-Pruefungen und laesst alle 29 Pflichtmodi laufen. Eine **nur im
Test erzeugte** Kopie von `ParkingGeometry.Types.cs` vergleicht jeden dort
erfolgreichen Bau mit zwei vorbereiteten Bauten (einschliesslich warmem
Treffer). Am Ende wird wieder aus den echten Quellen kompiliert. Der Lauf
vom 19.10. verglich 145 Layouts in 290 vorbereiteten Rechnungen bitgleich.
Produktivcode bekommt dadurch weder Pruefhaken noch Vergleichskosten.

200000 warme Schnitt-Nichttreffer muessen ohne Array-/Iteratorallokationen
ablaufen. Echte Schnitte, Randberuehrung und nachtraegliche Nachbarknoten
pruefen zusaetzlich, dass die Wiederverwendung keine alte Geometrie liefert.

Messung am Berichtspolygon:

`dotnet run -c Release --project Tests/Zufahrtsvorschau/Zufahrtsvorschau.csproj -- --messen`

`--messen-lot` misst stattdessen Lot 136479 in beiden Randstrassenstellungen
mit den beiden dokumentierten Zugangslagen. Ausgabe: Gesamtzeit, Buchten,
Materialringe und allokierte Bytes. Timinglaeufe nicht parallel ausfuehren.

Je 0/1/2 Zufahrten ein Warmlauf und drei Messungen, Median; vorhandene
Live-Phasen zeigen Vorplanung, Ring-/Stoss-/Bandschnitte, Zellklassifikation,
Zufahrt und Vereinigung. `--ohne-vorbereitung` misst denselben Kern ohne Cache.
Der Speicher teilt ausschliesslich Zerlegung und Innenversaetze: Randbuchten,
Raster-/Materialplaene haengen bereits von den Zufahrten ab und werden neu
gerechnet. Kein globaler Cache und keine neue gespeicherte Bauabsicht.
