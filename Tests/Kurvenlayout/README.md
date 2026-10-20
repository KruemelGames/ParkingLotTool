# Rand-/Materialplan an Kurven

`pwsh -NoProfile -File Tests/Kurvenlayout/pflichtlaeufe.ps1`

Reine Geometrie, kein Spielstart/Mod-Build/Deploy. C#-Main und Python-Main
fangen Ausnahmen ab; Exit 1 statt Windows-Absturzfenster. Python benoetigt
das hier vorhandene Shapely 2.1.2; kein Einsatz in der Mod-DLL.

132 Varianten: R15/30/60/150 x 45/90/180 Grad innen/aussen, beide Seiten
einer symmetrischen S-Kurve und fuenf Originalbauzettel vom 19.10.15:56-15:59;
jeweils Randstrassen AN/AUS und 0/1 Zufahrt. Zusaetzlich die beiden alten
konkaven Planformen mit 24/96 Sehnen. Gerade Rasterrichtung 0 bei den
synthetischen Formen; Spielbauzettel mit ihren echten Einstellungen.

`Formen.cs` erzeugt kubische Bordsteine, friert sie ausschliesslich mit
`Strassenkurve.Einfrieren` ein und verwendet die ausgegebenen float-Punkte
bei (1300,4600). Radiusvorpruefung dabei neutral: so werden auch geometrische
R15-Grenzen tatsaechlich gemessen. Keine zweite Kurvenabtastung. Die normale
Aussenform hat radiale Endkappen; andernfalls erzeugte ein Halbkreis am
Sehnenende kuenstliche fast-180-Grad-Rueckspruenge. Die alten Planformen mit
genau diesem Problem bleiben als ausdrueckliche Grenzproben erhalten.

`Spiel20261019.json` enthaelt unverkuerzte Eingaben aus dem Bauprotokoll;
Randstrassen AN/1 muss die fuenf gespeicherten Buchtenzahlen reproduzieren.
Ausgabe unter `.codex-build/kurven-9/`: alle Formen, Materialringe, Gras,
Netzachsen, Buchten, Warnungen, Versatzgrund, Ausnahmen und Laufzeiten.
Ein Warmlauf, danach eine Zeit je Variante; keine Median-/Ingame-Messung.

Die unabhaengige Messung benutzt keine Polygonreparatur. Sie nennt rohe
Deckung/Overlap und zusaetzlich Breitenzeugen: beidseitiges Erodieren um
1 mm trennt float-Fugen von echter Ueberdeckung; Erodieren einer Luecke um
25 mm beweist einen enthaltenen Kreis mit Durchmesser >5 cm. Nullflaechen,
Selbstschnitte und von CS2 abgelehnte Ringe werden getrennt ausgewiesen.
Das sind pruefbare Fehlerzeugen, kein vollstaendiger Beweis, dass alle
Luecken ohne solchen Zeugen harmlos sind.

`pruefung.py` prueft das Messgeraet mit deckenden, doppelten, fehlenden,
sub-mm-getrennten und selbstschneidenden Flaechen. Vier Mutationen muessen
rot werden. `--abnahme` ist derzeit **rot**: Materialluecken >5 cm sowie
ein echter Spielumriss mit einer numerisch entarteten Zufahrts-Scherbe.
Unmoegliche Versaetze, fehlender Modulplatz und null Buchten werden als
Geometriegrenze ausgewiesen, keine Fehlerfall-Liste auf Namensebene.
Die bestehende Kurven-/Bestands-Pflichtpruefung wird dadurch nicht angepasst.

Nur messen (optional zweites Argument als Formnamenfilter):

`dotnet run -c Release --project Tests/Kurvenlayout/Kurvenlayout.csproj -- .codex-build/kurven-9/messung`

Danach `python Tests/Kurvenlayout/messung.py .codex-build/kurven-9/messung/faelle.jsonl`
und `python Tests/Kurvenlayout/pruefung.py --abnahme .codex-build/kurven-9/messung/faelle.jsonl`.

## Etappe 6: explizite Kurvenlaeufe (Teilstand, keine Abnahme)

`pwsh -NoProfile -File Tests/Kurvenlayout/rand-pflichtlaeufe.ps1`

96 Varianten: R15/20/30/50/100, 90/150 Grad, innen/aussen, Randstrassen
AN/AUS, 0/1 Zufahrt; dazu Gerade-Kurve-Gerade und die unveraenderten
Umrisse Lot201905/Lot136479. Die beiden Archivfixturen enthalten keine
Original-Bezierformel. Dort sind die Laufindizes ausdruecklich Testdaten;
eine interpolierende Hermite-Formel geht durch die Originalpunkte.
Produktiv wird niemals ein Kurvenlauf aus einem Polygon geraten.

Die Radienfaelle verwenden ausschliesslich `Strassenkurve.Einfrieren`.
Die 120-m-Tangentenkappen trennen die Radiusfrage von einem zu schmalen
Enddreieck; 50 m boten bei R15/150 nur 12,94 m Tiefe statt 13,9 m.

`--randlogik` prueft Clone, CW/CCW, ungueltige Herkunft und eine enge
Laternen-Standlinie, auf der 20 m Bogen weniger als 9 m Luftlinie sind.
Sechs Mutationen muessen an benannten Aussagen scheitern. Der neue
Zahlenleser ist zusaetzlich im echten Speicherformat-Pruefstand eingebunden.

`randmessung.py` misst unabhaengig ganze Rechtecke, Materialdeckung,
Selbstschnitte, Fahrbahnfreiheit, Keilmaterial und Bogenabstaende. Keine
Polygonreparatur, keine auf Namen bezogenen Ausnahmen. Layout-Abbrueche
werden je Variante protokolliert; der C#-Lauf prueft die restlichen Faelle
weiter und beendet sich bei einem einzigen Fehler mit Exit 1.

**Abschlussmessung 20.10.: Dieser neue Lauf ist noch rot.** 96 vollstaendige
Varianten, 0 C#-Abbrueche und 0 abgelehnte Ausgaberings; unabhaengig 59
Fehlerzeugen (51 Keilmaterial, 8 Deckung) in 10 Varianten. Die vier AUS-
Deckungsbefunde entsprechen dem unveraenderten Altpfad. Vollstaendige CSV:
`.codex-build/kurven-11/messwerte-20261020.csv`. Die Messung liest die
JSON-Weltkoordinaten ausdruecklich als float32; vier Gegenproben verhindern,
dass die kuerzeste Dezimaldarstellung als andere double-Geometrie gilt.
Bogenpaare werden nur an tatsaechlichen produktiven Zufahrtssperren ausgespart.
Bestehende Pflichtlaeufe werden weder angepasst noch auf diese neue Eingabe
umgestellt. Tools-Anbindung erst nach Behebung der offenen Abnahmefehler;
das leere Datenfeld nutzt den bitgleichen Altpfad.
