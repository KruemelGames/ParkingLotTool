# Netzfassung: Vertrag mit dem CS2-Kurserzeuger

Start aus der Repo-Wurzel:

    pwsh -NoProfile -File Tests/Netzfassung/pflichtlaeufe.ps1

- `Program.cs` ruft die produktive Knotenbindung auf: gemeinsamer Anschluss,
  feste Kurvenecken, getrennte Nachbarwege, transitive Bindung, Welt-float-Rundung
  und der echte Layout-Aufruf. Main faengt alle Ausnahmen ab (Exit 1).
- Acht Mutationen pruefen insbesondere Weglassen statt Verbinden, alte 0,375-m-
  Zulassung, verlorene Anschlussenden und fehlende Weltkoordinatenpruefung.
- `fixturen.py` instrumentiert nur eine Quellkopie unter `.codex-build/` und
  startet 29 Pflichtmodi, den Zufahrtsvergleich, Rand-/Knotenmatrix, 6-m-Zusatzfall
  und die echte Vorschau. Es gibt keinen Mod-Build und keinen Deploy.
- `VorRunde15.json` enthaelt die eingefrorenen Hashes vor dem Fix (680 Eingaben,
  821 Bauten). Ohne alte Kurzstrecke muss das gesamte Layout bitgleich bleiben;
  fuer alle Faelle gelten mindestens 1 m nach Welt-float-Rundung, keine neuen
  Netzkomponenten und unveraenderte Buchtenzahlen. Materialdeckung pruefen die
  unabhaengigen bestehenden Rand-/Zufahrtspflichtlaeufe zusaetzlich.
- Keine automatische Neuerzeugung der Referenzdatei. Neue Eingaben separat
  abnehmen. Die historische Quellenkopie liegt lokal unter
  `.codex-build/kurven-15/vorher/Geometry/`.

SHA256 des historischen `Zellen/ParkingGeometry.Zellen.cs`:
`4a0d729c6bffad04cc3911e66a116ce698a6673108326b1f3618506a3def3422`.
SHA256 des historischen `ParkingGeometry.Kurvenzufahrten.cs`:
`4f17daf1c1939c571996439a7b16c52dc33c16ab859738ef6412b73ee7e6b902`.
