# Surface Preview: Ausschnitt des geplanten Parkplatzes

```powershell
pwsh -NoProfile -File Tests/SurfacePreview/pflichtlaeufe.ps1 -GameDllQuellen '<gamedll-Ordner>'
```

- Produktive Geometry: etwa 25 % Lotflaeche nahe Punkt 0, Rechteckschnitt mit
  getrennten Inseln, Bereinigung kurzer Kanten und CS2-Annahme jedes Stuecks.
- Sichtbarkeitsmatrix fuer geschlossene Form, aktuellen Layoutstand, Ziehen,
  Bauphase/Arbeiter/Speichern; Generationswechsel und nur neueste Wahl.
- Harte Satzbudgets verkleinern den Ausschnitt statt den Satz halb auszugeben.
- Farbnetz-Differenz: Deckungsproben am Nullpunkt und am Kartenrand.
- Produktiver Marker-Leser/Schreiber gegen historische v1-Feldfolgen/Endmarke.
- 16 Gegenproben in Compile-Kopien, keine Mutation von Produktquellen.
- Native Quellcodebelege pruefen Permanent-Erzeugung, Marker und Materialklone.

Messungen/Logs: `.codex-build/surface-preview-4-20261018/`.
Fixtures: L-Form mit 54 Buchten, Rechteck x4 mit 3870 Buchten.
Die Messung umfasst Schnitt/Annahme und Farbmaskenschnitt, keine ECS-/GPU-Zeit.
Die Zustandsdiagnose im Spiel ergaenzt Definitionen, native Geometrie und
Renderbatch-Sichtbarkeit. Sichtbare Pixel, Flackern, Save/Load und Kosten
bleiben Spieltests; siehe Bericht. PLT startet keinen Spielstand-Save.
