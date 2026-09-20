# Changelog

## Unreleased — September 20, 2026

- Preserve existing zoning road entities when an edit leaves the complete
  zoning road layout and prefab unchanged. Transfer their ownership to the
  replacement lot instead of rebuilding them, preserving the basis for zone
  painting and buildings. In-game retention validation is still pending.

- Add Alley In and Alley Out entrances using both driving lanes of a PLT
  clone of the regular Alley, with reversed course orientation for exits.
  No RoadBuilder dependency or Vanilla Alley Oneway parking strips are used.
- Refine entrance widths, surface coverage, arrows and terrain transitions;
  refresh surviving street junctions after removing old parking lot edges.
- Use a separate pedestrian entrance prefab that permits street connections
  while keeping automatic extensions disabled for internal pedestrian paths.
- Reduce utility connection planning work with bounded searches, exact
  projection fixed points and candidate-local start caches. Replace the fixed
  post-build delay with readiness checks and retain failed-target history.
- Validate utility connections through newly created vertical pipe sections
  and skip utility construction when no nearby street is available.
- Accelerate charging-station placement with conservative spatial filtering
  before the existing exact collision checks.
- Vary free-mode vegetation across equal-sized planting areas, sample across
  full search cells and add smoothly varying planting density. Persist the
  random seed in vegetation options; keep Line placement unchanged.
- Extend entrance, utility, vegetation and parking-facility diagnostics and
  regression tests, including mutation checks.
- Correct quoting in the existing stale temporary build-cache cleanup target.

Validation: Release build and all 27 required checks passed; vegetation tests
passed 23,165 checks and detected five injected faults. The Release build
still reports two existing CS0162 warnings.

Known limits: Alley clones still reference Vanilla section/piece assets;
full asset isolation has not been implemented. The intermittent Alley visual
issue was not conclusively traced to a particular shared resource. In/Out
behavior was confirmed in game, but this does not establish long-term visual
stability. The new vegetation distribution still needs visual acceptance.

Alle Aenderungen, die es in eine veroeffentlichte Version geschafft haben.
Dieselben Zeilen gehoeren vor jeder Veroeffentlichung in `<ChangeLog>` in
`Properties/PublishConfiguration.xml`.

## 1.0.0 - noch nicht veroeffentlicht

Erste Fassung.

- Parkplatz aus einer frei gezeichneten Flaeche: Buchten, Fahrgassen,
  Querstrassen, Randstrasse und die beiden Bodenflaechen.
- Die Buchten sind echte Parkspuren - Autos fahren hinein und parken.
- Zufahrten von Hand setzen, sie fangen sich an einer vorhandenen Strasse.
- Ecken und Kanten des Polygons lassen sich vor dem Bauen ziehen.
- Die Flaechenauswahl zeigt Vorschaubilder statt nur Namen, zwei
  Kacheln nebeneinander (braucht die Asset Icon Library).
- Einstellbar: Randabstand, Reihenwinkel, Fahrgassen- und
  Querstrassenbreite, Verbindung alle N Buchten, Mittelgruen und seine
  Tiefe, Kappen an Querstrassen, beide Flaechen (frei waehlbar oder aus),
  Buchtmarkierung.
- Jeder Wert laesst sich als eigener Standard speichern.
- Der ganze Parkplatz hat einen Besitzer: einmal anklicken, einmal
  abreissen.
- Panel auf Englisch, Deutsch in den Modeinstellungen.
- "Report a problem": Stelle in der Welt markieren, kurzen Bericht
  schreiben lassen.
