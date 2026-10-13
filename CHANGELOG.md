# Changelog

## Unreleased

- Fix: with the automatic entrance mode switched off, the entrance buttons stayed locked after closing an outline that was too small.
- Fix: after splitting or moving points, picking a new line for a sub-area could have no effect.
- Fix: "Updating parking lots" could stay forever after loading a save made while lots were being updated (thanks PlatinMartin).
- Change: "How it works" shows the episodes in their new order, episode 2 is now "Advanced Building and Mod Settings"; shorter tooltip for "Reset window position".
## 1.0.10 — October 12, 2026

- Addition: Brazilian Portuguese (thanks CrNet0), Spanish and French, automatic or chosen in the mod settings.
- Addition: parking lots show in the "Roads" info view, colored like the base-game lots and with the parking icon, and count towards its parking capacity and availability (thanks TheEsotericEric).
- Addition: "Post probe" in the Dev-Debug tab, to help track down a reported post office problem.
- Fix: possible crash when bulldozing a large parking lot while the bulldozer was still active.
- Fix: some parking lots found no staff because their workplace had lost its road connection; "Synchronize" restores it.
- Fix: updating parking lots could stay stuck for good, for example after "Synchronize all" while a preview was still being calculated, or when replacing plants or lamps ran into an error.

## 1.0.9 — October 11, 2026

- Addition: Japanese and Simplified Chinese, automatic or chosen in the mod settings.
- Addition: "Cross roads" switch under "Cross road every" turns off the cross roads between the rows.
- Change: "Median" is now called "Median green".
- Fix: a freshly built lot asked to be synchronized right away.
- Fix: the undo and redo buttons were empty when the game runs in Japanese (and possibly other languages).

## 1.0.8 — October 11, 2026

- Improvement: the surface and plant lists open where you left them.
- Improvement: "Report a problem" has a button that opens a new GitHub problem report.
- Fix: possible crash when a repair or update finished in the background.
- Fix: "Parking lots can be updated" kept coming back when a lot also offered "Repair"; it now shows its "Synchronize" button too.
- Fix: "Repair" was offered for lots whose alley connects to a wide road, and repairing changed nothing.
- Fix: some lots could not hire workers because their building had no road connection; "Synchronize" connects it.
- Fix: an update could be skipped silently when a needed part was not loaded yet.
- Fix: no preview when a point on a straight edge was snapped a few millimetres off the line.

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
