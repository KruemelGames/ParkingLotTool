# Kurvendiagnose und eingefrorene Geometrie, Etappen 1 und 2

`pwsh -NoProfile -File Tests/Kurven/pflichtlaeufe.ps1`

Der Rechner verknuepft die produktive `Geometry/Strassenkurve.cs`, ohne ECS
oder Spielstart. Gerade, Kreisbogennaeherungen, symmetrische S-Kurve,
gedrehte Edge-Orientierung, Seitentreue, Knotenflags, Hoehe, Stetigkeit,
Innenversatz, Budget und Weltverschiebung werden geprueft. Die Zeitmessung
umfasst zwei Kanten samt Sampling und Radiusberechnung, keinen ECS-Fang.
Main faengt Ausnahmen ab und liefert Exit 1.

Etappe 2 verknuepft auch `Kurvengruppe`, `Kurvenversatz` und den echten
Zellenkern. Der unveraenderliche Gruppenstand hat eine vom Aufrufer vergebene
ID, orientierte double-Kontrollpunkte, Abtastpunkte und kumulierte 3D-Laengen.
Die Diagnose verwendet vorerst ID 1; eindeutige Shape-IDs kommen in Etappe 3.
`Along` misst XZ-Meter auf dem Polygonsegment. An einer Naht waehlt die
Vorwaertsabbildung das folgende Segment, am Gesamtende das letzte Segment.
Innerhalb eines Segments wird die gespeicherte Bogenlaengenspanne linear
auf dessen Sehnenlaenge abgebildet; die Rundreise erfindet keine neue Kurve.
`Umkehren` erhaelt ID und Punktstand, kehrt Abschnitte und Laengenrichtung um.
Bogenintegration: adaptive Simpson-Quadratur, Ziel 0,00001 m je Abschnitt;
Gegenpruefung mit 32768 unabhaengigen Sehnen je Bezier.

Kontrollhuelle in double (nicht Mittelpunkt), totale Tangentendrehung samt
nativen Nahtknicken <=15 Grad, Sehnen 1..8 m (1 mm Zahlentoleranz), maximal
64 Stuecke. Fehler geben weder Gruppe noch Teilpunktkette aus. Dichte Proben
mit 4096 Schritten je Bezier messen Abstaende und Winkel unabhaengig.
R15/30/60/150 x 90/180, S, Weltverschiebung, Gegenrichtung, native Knoten,
Budget exakt 64/63, beide Bogen-/Polygonrundreisen und wechselnde Startecke
sind positive Pflichtfaelle; die gebildeten Kreisringe gehen durch CS2s
Triangulierungsnachbau. Vier neue Mutationen pruefen Gruppenhuelle,
Laengenreihenfolge, Gegenrichtung und Rueckabbildung.

Die Versatzpruefung ruft den heutigen `Layoutplanung.Innenrand` numerisch
auf und nimmt keine Aenderung an ihm vor. Sie prueft jeden erfolgreichen
Innenring in Weltkoordinaten gegen `Cs2Triangulierung`; zusammengefallene
Doppelpunkte werden nur fuer diese Annahmepruefung ausgelassen. Sie gibt
keine Kontur aus und ist noch an keinen Werkzeug-/Baupfad angeschlossen.
Die Fehlformen aus dem Plan bleiben bei 24 Stuecken/10,4 m sowie
96 Stuecken/6,9 m als `VersatzBrichtAb` reproduzierbar; der 8er-Fall MUSS
gelingen. Shape, Bauzettel, Bedienung und Layoutkern werden nicht umgebaut.

14 mutierte Quellkopien bleiben unter `.codex-build/kurven-4`.
Neu: Netzaufnahme und Logfilter sind ebenfalls produktive Geometry-Klassen.
Die Mutationen nehmen die zweite Knotenhaelfte heraus, geben die Verbindung
vor dem Lesen der Zielgeometrie frei, vereinen die Logquellen, melden einen
Kurzabschnitt als ZuEng oder entfernen die Lueckenmessung.

`NativeKnoten.json` friert die Ausgabe der **echten Game.dll** ein (SHA-256
im JSON). Am 18.10.2026 ohne Spielstart erzeugt: Reflection-Aufruf von
`GeometrySystem.CalculateNodeGeometryJob.CalculateMiddleSegment` sowie
`Invert(Segment)`, einschliesslich der nativen Mittelkurvenbildung, mit den
ebenfalls nativen `NetUtils.FitCurve`-Randkurven (Radien 148/142/136 m).
Quelle: dekompiliertes `Game.Net/GeometrySystem.cs:1917-1928,2038,2125,2760-2786`.
Gleiche Komposition, 12 m Breite, MiddleOffset 0: die vier angebotenen
Kurven einer Edge sind Aussen/Mitte, Mitte/Innen; die andere Edge liefert
die umgekehrte zweite Haelfte. Die doppelte Mitte wird zusammengefasst.
Damit sind es sechs Angebote, nicht ein erfundener durchgehender Verbinder.
Das ist eine reproduzierbare synthetische native Fixtur, kein ECS-Abzug aus
dem Spielerstand. Ihr Gegenlauf entfernt gezielt eine native Haelfte.
Acht Faelle: beide Laufrichtungen, zweite Edge gleich-/gegenlaeufig und
Fang weit vom bzw. knapp vor/hinter dem Knoten. Die Rohwerte bleiben fest.

`fangvergleich.py` vergleicht den unveraenderten Fangpfad mit seinem vor
Etappe 1 eingefrorenen Hash, nachdem die reinen Beobachteraufrufe
entfernt wurden. Eine Prioritaetsmutation muss erkannt werden. Das belegt
die unveraenderten Fangoperationen; die sichtbare Darstellung und native
Geometrieuebergaenge muessen weiterhin im Spiel abgenommen werden.
