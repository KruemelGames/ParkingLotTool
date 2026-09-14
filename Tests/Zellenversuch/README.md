# Zellenversuch, Teil 3: die Zufahrt

Stand des vollständigen Messlaufs: 2026-08-20.

## Urteil

**Die topologische Vermutung trägt, die uneingeschränkte Abnahmevermutung
nicht.** Eine Zufahrt lässt sich vor der Materialvereinigung als Umwidmung von
Zellen plus genau zwei Seitenlinien bauen. In allen 27 Läufen entstanden

- 0 gemeinsame Punktabweichungen,
- 0 offene oder nicht-mannigfaltige Kanten,
- 0 neue Null- oder Splitterzellen,
- 0,00 m² ungedeckte und 0,00 m² überlappte Fläche,
- 0 fertige Löcher und 0 Randbandinseln,
- 0 kombinatorische Flächenbilanzfehler und
- 0 numerische Toleranzen.

**Als vollständiger Ersatz für den heutigen nachträglichen Schnitt trägt die
bloße Umwidmung plus zwei Linien trotzdem nicht für beliebiges `along`.** Drei
der 27 Pflichtläufe unterschreiten in einer fertigen Materialfläche die
belegte CS2-Grenze von 0,375 m:

| Form und Fall | engste fertige Stelle | kürzeste fertige Kante | Urteil |
|---|---:|---:|---|
| Rechteck, 1,5 m von Ecke | **0,250 m** | 1,000 m | trägt nicht |
| L-Form, 1,5 m von Ecke | **0,250 m** | 1,000 m | trägt nicht |
| L groß, zehn Zufahrten | **0,100 m** | **0,100 m** | trägt nicht |

Beim Rechteck liegt der neue Zufahrtsrand bei `x=5,000` und eine vorhandene
Zell-/Materialgrenze bei `x=5,250`: Der verbleibende 0,250-m-Hals ist exakt,
nicht gerundet. Beim Zehnerfall wurden zusätzlich 0,200 und 0,300 m gemessen.
Eine Toleranz würde diese Geometrie nur verschweigen. Für einen tragfähigen
Produktionsweg braucht es deshalb noch eine konstruktive Regel, welche solche
Nachbargrenzen vor der Vereinigung mitplant oder die betroffenen Zellen anders
umwidmet. Ein nachträglicher Polygonschnitt ist dafür nicht bewiesen nötig;
die getesteten **zwei Linien allein** reichen aber nicht.

Der vollständige Lauf endet deshalb absichtlich mit Exitcode 1 und
`GESAMTURTEIL: ... trägt ... nicht`.

## Konstruktion

Die Angabe entspricht dem Mod: `Kante` ist der Polygonkantenindex, `Along` der
stufenlose Abstand vom Anfang dieser Kante. Mehrere Vorgaben bleiben eine
Liste; Fall 6 benutzt zehn Einträge.

Die Zufahrt ist 7,0 m breit. Ihre Mittellinie läuft von der Polygonkante
6,9 m nach innen:

```text
6,9 m = es 1,0 m + Buchttiefe 5,9 m
```

Dort trifft sie die bereits im Grundnetz vorhandene Außenkante der 7,0-m-
Randstraße. Außenkante und Randstraßenkante sind also schon Zellgrenzen. Pro
Zufahrt kommen ausschließlich die beiden 6,9-m-Seitensegmente hinzu.

Der Ablauf ist:

1. Grundzellen einschließlich Randstraßenkante aufbauen.
2. Jede der zwei Zufahrtsseiten durch die von ihr getroffenen Zellen führen.
3. Einen Endpunkt auf einer gemeinsamen Kante als **denselben Knoten** in die
   Nachbarzelle einsetzen.
4. Fragmente im 7,0 × 6,9-m-Korridor zu Asphalt/Zufahrt umwidmen.
5. Eine angeschnittene Bucht nicht als vollständige 3,0 × 5,9-m-Bucht
   weiterzählen; übrige Asphaltfragmente bleiben Restbelag.
6. Erst danach Materialzellen vereinigen und vorhandene Materialringe wie in
   Teil 2 an gemeinsamen Zellkanten öffnen.

Es wird keine fertige Grün- oder Asphaltfläche geschnitten. Berührende oder
deckungsgleiche Seitenlinien werden nicht künstlich verdoppelt: Im
Zellgrenzenfall ist nur eine von zwei Linien wirksam; bei zwei sich
berührenden Zufahrten sind weniger als vier Linien wirksam. Dabei entstanden
keine Nullflächen.

## Gebaute Fälle

Die Fälle 1, 2, 4, 5a und 5b laufen auf **jeder der fünf Formen**. Fall 3
gehört definitionsgemäß zu `L schräg`; Fall 6 läuft auf `L groß`.

| Nr. | Lage | konkrete Vorgaben |
|---|---|---|
| 1 | Mitte einer Kante | Kante 0, `along = Kantenlänge / 2` |
| 2 | 1,5 m von einer Ecke | Kante 0, `along = 1,5`; trifft gemessen zwei Randseiten |
| 3 | nicht achsparallel | `L schräg`, Kante 3, `along = 20,471` |
| 4 | vorhandene Zellgrenze | Kante 0, `along = 8,750`; nur eine von zwei neuen Linien wird wirksam |
| 5a | zwei Zufahrten berühren | Mittelpunkte 7,0 m auseinander; mehrfach belegte Fläche 0,000 m² |
| 5b | zwei Zufahrten überlappen | Mittelpunkte 5,0 m auseinander; Überlappung **13,800 m²** |
| 6 | zehn Zufahrten | `L groß`, zehn Vorgaben auf allen sechs Polygonkanten |

Die zehn Vorgaben sind:

```text
k0@30  k0@90  k1@20  k1@55  k2@20
k3@20  k4@30  k4@90  k5@30  k5@90
```

Aufkleber bleiben bewusst außerhalb des Versuchs.

## Baseline aus Teil 2

Die zusätzliche 6,9-m-Randstraßenkante verfeinert das Grundnetz, ändert aber
weder Buchtenzahl noch Materialergebnis. Vor der Ringtrennung stehen weiterhin
dieselben 7/3/14/13/9 Löcher, danach jeweils null. Auch die fünf Buchtenzahlen
aus Teil 2 bleiben unverändert.

| Form | Grundzellen | Buchten | Löcher roh → fertig | ungedeckt | Median ohne Zufahrt |
|---|---:|---:|---:|---:|---:|
| Rechteck | 861 | 208 | 7 → 0 | 0,00 % | 6,570 ms |
| L-Form | 812 | 148 | 3 → 0 | 0,00 % | 6,850 ms |
| L schräg | 1.704 | **385** | 14 → 0 | 0,00 % | 21,090 ms |
| L groß | 1.610 | **368** | 13 → 0 | 0,00 % | 21,219 ms |
| U-Form | 1.460 | 310 | 9 → 0 | 0,00 % | 18,024 ms |

## Vollständige Fallmatrix

Die Bauzeit ist jeweils der Median aus 200 vollständigen Neubauten nach 100
gemeinsamen Aufwärmrunden. Tiered Compilation war abgeschaltet. Die unabhängige
0,1-m-Rasterprüfung ist darin nicht enthalten.

| Form | Fall | Zufahrten | neue Punkte | Punktabweichungen | neue Splitter | engste fertige Stelle | Median | Urteil |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Rechteck | 1 Mitte | 1 | 8 | 0 | 0 | 0,500 m | 7,156 ms | trägt |
| Rechteck | 2 Ecke | 1 | 4 | 0 | 0 | **0,250 m** | 7,070 ms | **trägt nicht** |
| Rechteck | 4 Zellgrenze | 1 | 4 | 0 | 0 | 1,000 m | 7,123 ms | trägt |
| Rechteck | 5a Berührung | 2 | 12 | 0 | 0 | 0,500 m | 8,127 ms | trägt |
| Rechteck | 5b Überlappung | 2 | 12 | 0 | 0 | 1,000 m | 8,031 ms | trägt |
| L-Form | 1 Mitte | 1 | 8 | 0 | 0 | 0,500 m | 7,325 ms | trägt |
| L-Form | 2 Ecke | 1 | 4 | 0 | 0 | **0,250 m** | 7,533 ms | **trägt nicht** |
| L-Form | 4 Zellgrenze | 1 | 4 | 0 | 0 | 1,000 m | 7,755 ms | trägt |
| L-Form | 5a Berührung | 2 | 12 | 0 | 0 | 0,500 m | 8,480 ms | trägt |
| L-Form | 5b Überlappung | 2 | 8 | 0 | 0 | 1,000 m | 8,341 ms | trägt |
| L schräg | 1 Mitte | 1 | 6 | 0 | 0 | 0,514 m | 23,225 ms | trägt |
| L schräg | 2 Ecke | 1 | 3 | 0 | 0 | 0,514 m | 24,150 ms | trägt |
| L schräg | 3 schräge Kante | 1 | **10** | **0** | **0** | **0,514 m** | **23,330 ms** | **trägt** |
| L schräg | 4 Zellgrenze | 1 | 3 | 0 | 0 | 0,514 m | 24,065 ms | trägt |
| L schräg | 5a Berührung | 2 | 9 | 0 | 0 | 0,514 m | 25,416 ms | trägt |
| L schräg | 5b Überlappung | 2 | 12 | 0 | 0 | 0,514 m | 25,981 ms | trägt |
| L groß | 1 Mitte | 1 | 6 | 0 | 0 | 1,000 m | 22,000 ms | trägt |
| L groß | 2 Ecke | 1 | 3 | 0 | 0 | 1,000 m | 22,118 ms | trägt |
| L groß | 4 Zellgrenze | 1 | 3 | 0 | 0 | 1,000 m | 21,764 ms | trägt |
| L groß | 5a Berührung | 2 | 9 | 0 | 0 | 1,000 m | 23,734 ms | trägt |
| L groß | 5b Überlappung | 2 | 12 | 0 | 0 | 1,000 m | 26,685 ms | trägt |
| L groß | 6 zehn | **10** | **73** | **0** | **0** | **0,100 m** | **37,646 ms** | **trägt nicht** |
| U-Form | 1 Mitte | 1 | 6 | 0 | 0 | 0,500 m | 20,937 ms | trägt |
| U-Form | 2 Ecke | 1 | 3 | 0 | 0 | 0,500 m | 19,688 ms | trägt |
| U-Form | 4 Zellgrenze | 1 | 3 | 0 | 0 | 0,500 m | 20,587 ms | trägt |
| U-Form | 5a Berührung | 2 | 6 | 0 | 0 | 0,500 m | 21,677 ms | trägt |
| U-Form | 5b Überlappung | 2 | 6 | 0 | 0 | 0,500 m | 20,517 ms | trägt |

Damit tragen 24 von 27 Läufen. Der Höchstmedian von 37,646 ms bleibt unter
dem 50-ms-Maßstab.

## Punkte, Bilanz und Topologie

Je nach Lage entstehen 3 bis 12 neue Geometriepunkte; bei zehn Zufahrten sind
es 73. Am schrägen Pflichtfall entstehen 10. Die Schnittpunktmessung gruppiert
jeden neuen Punkt nach vorhandener Quelllinie und Zufahrtslinie. In allen 27
Läufen benutzen die Nachbarzellen dieselbe Knoten-ID:

| Prüfung | Maximum über 27 Läufe |
|---|---:|
| gemeinsame Punktabweichungen | **0** |
| neue Punkte mit zu wenigen Nutzern | **0** |
| offene Teilungs-/Innenkanten | **0** |
| nicht-mannigfaltige Kanten | **0** |
| gleichgerichtete Doppelkanten | **0** |
| abweichende Linien-IDs | **0** |

Die Flächenbilanz ist in allen Läufen kombinatorisch exakt: Jede Zelle vor der
Zufahrt hat mindestens einen eindeutigen Nachfolger, jeder Nachfolger genau
eine Quellzelle; Quellzellfehler **0**. Die erneut als `double` summierte Fläche
ist nicht immer bitgleich. Die größte gemessene Vorher-/Nachher-Abweichung ist
`7,276E-012 m²` bei `L schräg`. Sie wird weder ausgeglichen noch toleriert.

## Löcher und Randband

Die Rohzahl der Materiallöcher vor der Ringtrennung ändert sich nur dort, wo
die Asphaltzufahrt zwei bisher getrennte Asphaltbereiche verbindet:

| Form | ohne Zufahrt | typische Zufahrt | Sonderfall |
|---|---:|---:|---:|
| Rechteck | 7 | 6 | Ecke 7 |
| L-Form | 3 | 2 | Ecke 3 |
| L schräg | 14 | 14 | schräge Kante 13 |
| L groß | 13 | 13 | zehn Zufahrten 13 |
| U-Form | 9 | 9 | — |

Nach der unveränderten Ringtrennung stehen **in jedem Lauf 0 Löcher**. Eine
einzelne Zufahrt lässt den offenen Randbandweg als eine Komponente bestehen.
Die zehn Zufahrten erzeugen zehn Randbandkomponenten, aber **0 Inseln**: Jede
Komponente berührt weiterhin eine echte Polygonaußenkante.

## Null- und Splitterzellen

Nullflächenzellen: in allen Läufen `0 → 0`.

Neue Splitterzellen unter 0,01 m²: in allen Läufen `0`.

`L schräg` besitzt bereits **vor** jeder Zufahrt fünf exakte Splitterzellen;
alle Zufahrtsfälle bleiben bei `5 → 5`. Ihre Flächen liegen zwischen
`0,000009697` und `0,004654725 m²`. Vier entstehen an beinahe durch einen
Rasterknoten laufenden schrägen Außen-, Innen- oder Zerlegungslinien; eine an
der vorab vorhandenen Randstraßenkante. Ohne Mindestflächenfilter müssen diese
Zellen erhalten bleiben: Wegwerfen würde Fläche entfernen. Sie werden mit
gleichartigem Material vereinigt und erscheinen nicht als CS2-Einzelflächen;
die engste fertige Stelle bleibt 0,514 m.

Das ist die verlangte Erklärung, warum der Rohzellen-Zielwert dort nicht null
ist. Für die Zufahrt selbst ist die Differenz null: Sie erzeugt keinen der fünf
Splitter neu.

## Toleranzen

Anzahl numerischer Toleranzen: **0**.

Es gibt keinen Fangwert, kein Epsilon, keine Rundung, keine
Mindestflächenlöschung und keinen nachträglichen Reparaturschnitt. Diese Zahlen
sind Mess- oder Abnahmeschwellen, keine Toleranzen:

- `0,01 m²`: nur die Meldegrenze für Splitterzellen; nichts wird verworfen;
- `0,1 m`: Auflösung des unabhängigen Messrasters;
- `0,375 m`: belegte CS2-Abnahmeschwelle;
- `1,0 / 3,0 / 5,9 / 7,0 / 34,0 m`: Layoutmaße.

## Reproduzieren

```powershell
cd Tests/Zellenversuch
dotnet run -c Release
```

Der vollständige Lauf dauerte gemessen 506 s einschließlich aller
Rasterprüfungen, 100 Aufwärmrunden und 200 Zeitwiederholungen je Lage. Eine
einzelne Form kann angehängt werden, zum Beispiel:

```powershell
dotnet run -c Release -- "L schraeg"
```

Das Projekt ist eigenständig, zielt auf .NET 9 und referenziert weder Mod- noch
Unity-Dateien. Geändert wurde ausschließlich `Tests/Zellenversuch/`; es wurde
nichts committet.

## Abdeckung der sechs verlangten Fälle

- **Gebaut:** 1 Mitte, auf allen fünf Formen.
- **Gebaut:** 2 Ecke bei `along=1,5`, auf allen fünf Formen und mit zwei
  tatsächlich getroffenen Randseiten.
- **Gebaut:** 3 schräge Kante 3 von `L schräg`, nicht achsparallel zum Raster.
- **Gebaut:** 4 exakt vorhandene Zellgrenze, auf allen fünf Formen; keine
  Nullfläche.
- **Gebaut:** 5 zwei Zufahrten sowohl berührend als auch 2,0 m überlappend, auf
  allen fünf Formen.
- **Gebaut:** 6 zehn Zufahrten auf `L groß`, verteilt über alle sechs Kanten.
- **Nicht gebaut:** Aufkleber. Kein verlangter Zufahrtsfall wurde ausgelassen.
