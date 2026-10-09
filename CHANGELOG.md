# Changelog

## 1.0.7 — October 9, 2026

- Addition: "How it works" tab with the tutorial videos; the first one, "Basics", is out.
- Addition: "Upkeep costs" in the mod settings scales the upkeep of all parking lots (100 %, 75 %, 50 %, 25 % or off).
- Change: "Align to polygon line" aligns the whole lot; splitting is now its own "Split area" button, with "Remove split".
- Improvement: improved language system for further additions; all texts now live in language files in the mod folder.
- Improvement: "Edge" or "Fixed" turns alignment off but keeps the lines and the split; "Align" brings them back.
- Improvement: the split follows the shape when points are moved or deleted.
- Fix: some texts in the status bar, tooltips and panel stayed in the other language.
- Fix: the parking lot list showed half the real upkeep when the road services budget was at 100 %.
- Fix: some asphalt and grass surfaces were not built; "Synchronize" adds them to existing lots.
- Fix: in a narrow arm of a lot, paths could run across the lot and far outside it; affected lots show "Repair" in the list (thanks kunred).
- Fix: where a narrow arm is too tight for the road, parking spaces no longer line the end of the arm without a path; it stays green.
- Fix: some parking decals pointed towards the median instead of the road; editing a lot applies it.
- Fix: possible crash when splitting an area.
- Fix: crash when resetting the alignment while a split had no line.

## 1.0.6 — October 6, 2026

- Improvement: while parking lots are being updated or repaired, the panel and Edit stay closed and the progress message pulses instead; the panel opens again when the work is done.
- Improvement: "Repair" and "Synchronize" can be used in any order: lots that need a repair are updated after it.
- Possible fix: updating parking lots could stop at "1/3" for good, for example after switching tools during the update or when lots needed the 25 km/h paths and lanterns at the same time (thanks MakaPakaUK and Tylerps2).
- Fix: with an unfinished drawing open, updating took long and added no lanterns.
- Fix: lots whose build plan was restored got no lanterns; "Synchronize" adds them now.
- Fix: lanterns could be placed twice when "Synchronize all" was clicked again during an update.
- Fix: the progress counter counted a lot as done while it was still being updated.
- Fix: "Parking lots can be updated" showed up again right after the update.
- Fix: possible crash when editing a lot that has a road connected to one of its paths.

## 1.0.5 — October 5, 2026

- Addition: street lanterns along the rows, lit at night; street, commercial and industrial sets or your own, spacing 20–40 m; "Synchronize" adds them to existing lots.
- Addition: "Show light range" in the lantern window draws each lantern's reach in the preview (off by default).
- Addition: lots that use a surface or plant from a mod that is no longer loaded say what is missing; "Repair" rebuilds them with your saved default surface and plants, or automatically with "Repair automatically".
- Improvement: "Synchronize" takes a lot through all pending steps in one click; the result message stays 30 seconds.
- Improvement: "Repair" and "Synchronize" close the panel, for one lot or all of them.
- Improvement: the "Parking lots" tab turns yellow and shows how many lots need a sync or repair.
- Improvement: bushes with growth stages are planted like bushes, denser and right up to lanterns; "Synchronize" replants existing lots.
- Improvement: the monthly income in the lot list appears after one game hour instead of three days and keeps updating.
- Fix: notifications such as missing workers now show above the parking lot instead of on the road at its entrance.
- Fix: every parking lot added about 65,000 a month to road upkeep regardless of size; it now costs what its info panel shows (thanks SeanDFC02).
- Fix: grass left on the road where perimeter roads overlap; editing an existing lot applies it (thanks bikester1).
- Fix: editing a lot continues after an autosave instead of losing the selection (thanks bikester1).
- Fix: "Record a crash trace" turns itself off the next time the game starts; left on, it slowed the game down.
- Fix: the game no longer reports the mod's report ZIPs as broken assets when it starts; they now live in `Logs\.ParkingLotTool-Logs`.

## 1.0.4 — October 4, 2026

- Change: traffic inside parking lots drives at 25 km/h and routes through them cost more, so through traffic avoids them.
- Addition: "Update" brings existing lots to the new speed in seconds.
- Addition: option "trees don't age"; save and reset for the vegetation window.
- Addition: pending updates are shown after loading; a click opens the lot list.
- Improvement: crash reports now show where the game crashed; the button is now "Report the last crash".
- Fix: crash when editing a lot with bus stops while utilities were connected.
- Fix: bus stops disappeared after editing a lot.
- Fix: cancelling an edit could remove the lot's paths.
- Fix: crash after loading a save whose lots use surfaces from other mods.

## 1.0.3 — October 1, 2026

- Improvement: crash reports show how far the mod got while loading a save.
- Fix: the crash notice pointed to a tab players cannot see.

## 1.0.2 — September 29, 2026

- Improvement: clearer status messages; a missing preview now says why and what to do, all messages fully translated.
- Addition: status messages for the next step (preview ready, lot built).
- Fix: the preview could stop appearing until the game was restarted.

## 1.0.1 — September 28, 2026

- Fix: EV charging stations could be scattered across the lot.
- Change: Find It added to the dependencies (required by Asset Icon Library).

## 1.0.0 — September 28, 2026

- Addition: first release on Paradox Mods.

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
