# Schlusskante einer Kurvenform verschieben

Vom Repo-Wurzelverzeichnis:

```powershell
pwsh -NoProfile -File Tests/Kurvenlayout/schlusszug-pflichtlaeufe.ps1
```

Der Lauf prueft die produktive Bandkonstruktion, setzt sie per Mutation auf
seine alte Grenze zurueck, baut die Endlagenmatrix und misst das Ergebnis
unabhaengig mit GEOS. Ein Materialfehler liefert weiterhin Exit 1. Es gibt
keine Fallausnahmen und keine neuen Toleranzen; `randmessung.pruefe` behaelt
0,01 m2 / 2 mm / 5 cm Breite.

`Schlusszug2033.json` enthaelt 28 protokollierte Startpolygone und die
LayoutSettings/Kontrollpunkte des letzten gelungenen Stands 46. Stand 54
und der letzte Stand 46 stammen exakt aus dem ZIP-Abzug. Die anderen
Enden sind Logwerte; unveraenderte Innenpunkte wurden aus dem Abzug
uebernommen. Die zweimal vorkommenden Standnummern bleiben eigene Faelle.
`LogBuchten` ist keine Sollzahl: die asynchronen Logantworten lassen sich
nicht durchgaengig einem Start zuordnen. Wenn die Formel 46 die Enden
nicht erreicht, liefert die bestehende Archiv-Hermite-Methode die Formel.

Hinzu kommen ein dichtes Endlagenraster ueber die ersten beiden kubischen
Abschnitte, Proben vor/auf/hinter Knoten und echte Sehnenmitten. Alle
Faelle laufen mit Randstrassen AN/AUS und in beiden Richtungen. AUS muss
vollstaendig bitgleich zum selben Bau ohne Kurvenlaeufe bleiben.
Eine zusaetzliche 1-mm-Sehnenendlage erzeugt in allen vier Betriebs-/
Richtungsvarianten nach Rundung exakt identische benachbarte
Eingangsgitterpunkte. Sie sind Negativfaelle: der
Kern MUSS die Nullkante ablehnen; ein angenommener Bau waere ein Testfehler.
Ein Modulabbruch wird nur zugelassen, wenn die separat gemessene
Innenhoehe kleiner als `2 * Sl + Ai` ist; Bandfehler bleiben Fehler.

Einzelbefunde:

```powershell
dotnet run -c Release --project Tests/Kurvenlayout/Kurvenlayout.csproj -- --schlusszug --stand=54
dotnet run -c Release --project Tests/Kurvenlayout/Kurvenlayout.csproj -- --schlusszug --raster --name=Raster/0.2
```

Ausgaben liegen unter `.codex-build/kurven-16`. Der strenge Materiallauf
zeigt zusaetzliche schmale Exportverluste im Inneren; siehe
`Berichte/BERICHT-KURVEN-15-20261020.md`. Diese sind nicht freigestellt.
