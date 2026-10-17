# Surface Preview: dauerhaftes Muster mit Ladebereinigung

```powershell
pwsh -NoProfile -File Tests/SurfacePreview/pflichtlaeufe.ps1 -GameDllQuellen '<gamedll-Ordner>'
```

- Produktive Geometry/OberflaechenMuster: 40-m-Quadrat, Sichtbarkeitsmatrix,
  Marker-/Ausgabenentscheidung, Dreiecksdifferenz fuer das Farbnetz.
- Quadratannahme mit bestehendem CS2-Nachbau: beide Umlaufrichtungen,
  expliziter Ringschluss, fuenf Weltlagen, kleiner/grosser Parkplatz.
- Produktiver Marker-Leser und -Schreiber in Tests/Speicherformate:
  zwei eingefrorene v1-Feldfolgen hintereinander plus Endmarke.
- Neun Compile-Kopien fuer Mutationen; keine Aenderung der Produktquellen.
- Permanent-Belege pruefen den neuen nativen Erzeugungsweg am lokalen
  Dekompilat und die Archetyp-Markierung. Keine behauptete Ingame-Pruefung.

Logs/Messungen unter `.codex-build/surface-preview-3-20261017/`.
Native Darstellung, Wechsel-Latenz, Bildzeit und Save/Load gehoeren zur
Spielabnahme im Bericht. PLT fuehrt niemals selbst einen Spielstand-Save aus.
Das alte Renderer-Stopp-Skript und die Linsenversuche sind entfernt.
