import { useValue } from "cs2/api";
import { sprache$ } from "./bindings";

/**
 * Alle sichtbaren Texte des Panels, in beiden Sprachen.
 *
 * Standard ist ENGLISCH - Wunsch des Nutzers am 2026-08-21: der Mod geht in
 * den Workshop, und dort ist Englisch die Sprache, die jeder lesen kann.
 * Umgestellt wird ueber die Modeinstellungen.
 *
 * Das englische Woerterbuch gibt den TYP vor, das deutsche wird dagegen
 * geprueft: ein vergessener Schluessel faellt beim Uebersetzen auf und nicht
 * erst im Spiel als "undefined". Zusammengesetzte Zeilen sind Funktionen -
 * Cohtml zieht `white-space` nicht ueber getrennte Textknoten hinweg, ein
 * zusammengesetzter Satz muss also EIN Stueck bleiben.
 */
/** Ein Satzstueck. `{ ort }` wird zu einem anklickbaren Ortsnamen. */
export type Ortsteil = { ort: string };
export type Satzteil = string | Ortsteil;

const en = {
  parkgebuehrJeVorgang: "Parking fee per stay",
  bestimmungen: "Policies",
  artNameZufahrt: "Entrance",
  bushaltestelle: "Bus stop",
  tooltipBushaltestelle:
    "Place a working bus stop on either side of a zoning road. "
    + "The cursor chooses the side; right click removes a stop.",
  fensterSchliessen: "Close window",
  leistungTitel: "Slow game?",
  leistungErklaerung:
    "Measures for one minute where the frame time goes, then writes a "
    + "report you can send. Just keep playing while it runs - the ordinary "
    + "case is what we want to see.",
  leistungStart: "Measure for one minute",
  leistungLaeuft: (s: number) => `Measuring … ${s} s`,
  tooltipLeistung:
    "Records one line per second and one for every frame over 50 ms, "
    + "broken down by what the mod was doing. Frames that are not ours "
    + "show up as such.",
  tooltipFensterHeim:
    "Puts the window back where it started - the way out if you pushed it "
    + "off the screen.",
  fangAlleAn: "Turn on every snap",
  fangAlleAus: "Turn off every snap",
  fangNamen: {
    ExistingGeometry: "Snap to existing lots",
    StraightDirection: "Snap to straight angles",
    NetSide: "Snap to road edges",
    ObjectSide: "Snap to building edges",
    GuideLines: "Snap to guide lines",
    ZoneGrid: "Snap to the zone grid",
  } as Record<string, string>,
  stilHochkant: "Upright",
  stilHorizontal: "Bar",
  tooltipStil:
    "Switches the window between a narrow upright column at the left and a "
    + "wide bar above the toolbar. Same settings either way.",
  artNameGasse: "Alley",
  artNameGasseEin: "Alley in",
  artNameGasseAus: "Alley out",
  artNameFussweg: "Footpath",
  artNameEinfahrt: "Entry",
  artNameAusfahrt: "Exit",
  fehltZugang: "Needs a two-way entrance, or one entry and one exit",
  fehltEinfahrt: "An exit without an entry - place an entry",
  fehltAusfahrt: "An entry without an exit - cars could not leave",
  titelZufahrt: "Two-way entrance - place on the outline",
  titelEinfahrt: "Entry only, one lane - place on the outline",
  titelAusfahrt: "Exit only, one lane - place on the outline",
  titelFussweg: "Pedestrian access - place on the outline",
  angestellte: "Employees",
  beschaeftigte: "Staff",
  parkgebuehr: "Parking Fee",
  bearbeiten: "Edit",
  tooltipBearbeiten:
    "Reopens this lot to change its outline, entrances and settings.",
  waehrung: "¢",
  gebuehrAus: "Off",
  flaecheStrasseAn: "Place road surface",
  flaecheDekoAn: "Place decoration surface",
  geschriebenNach: (pfad: string) => `Written to: ${pfad}`,
  berichtOrt:
    "The file goes to the game's Logs folder and is called "
    + "'ParkingLotTool-report-…txt'. It is short enough to send.",
  spalteFlaechen: "Surfaces",
  flaecheStrasse: "Road surface",
  flaecheDeko: "Decoration surface",
  vorflaeche: "Surface to road",
  buchtsymbole: "Bay markings",
  titel: "PARKING LOT TOOL",
  zuschnitt: "Layout",
  fahrwege: "Roads",
  gruen: "Green",
  randabstand: "Edge setback",
  reihenwinkel: "Row angle",
  winkel: "Angle",
  zoningReiter: "Zoning",
  zoningSetzen: "Place patches",
  zoningSetzenAus: "Stop placing",
  zoningWinkel: "Parcel angle",
  zoningLinie: "Line",
  zoningSeite: "Outer band",
  zoningAussentiefe: "Depth in tiles",
  zoningSeiteHinweis:
    "The zoning road runs around what you drew. Every side of it can carry "
    + "extra parcels outside, each with its own depth. The parking lot keeps "
    + "that room free — no aisle, no cross road, no bays.",
  zoningTiefeKlick:
    "Pick a depth, then turn on “Toggle road side” and click the OUTER "
    + "side of a zoning road. Clicking the same side again removes the band.",
  tooltipZoningAussentiefe:
    "How deep the next band you click becomes, in 8 m tiles.",
  tooltipZoningTiefeKnopf: "Tiles deep:",
  zoningFlaeche: "Parcel ground",
  zoningFlaecheHinweis:
    "The ground under the parcels only — the zoning road stays a road "
    + "surface like all the others. Leave it unset to use the decoration "
    + "surface.",
  tooltipZoningFlaeche:
    "Picks the surface painted under the building parcels.",
  zoningLinieAbbrechen: "Cancel",
  zoningGewaehlt: "selected:",
  tooltipZoningLinie:
    "Pick one line of your outline; the parcels follow it instead of the "
    + "longest edge.",
  tooltipZoningLinieAbbrechen: "Stops picking. The outline stays as it is.",
  zoningStand: "patches",
  zoningParzellen: "parcels",
  zoningHinweis:
    "Drag a rectangle inside the closed outline. It snaps to whole 8 m "
    + "parcels, at most 25 by 25. Drag a patch to move it, right-click to "
    + "delete it. What you drag are the parcels only — the zoning road "
    + "is added around them.",
  tooltipZoningReiter: "Places building parcels inside the parking lot.",
  tooltipZoningGesperrt:
    "Draw and close an outline first — the parcels sit inside the "
    + "parking lot.",
  tooltipZoningSetzen:
    "Turns the left mouse button over to the parcels. The outline stays "
    + "as it is.",
  tooltipZoningWinkel:
    "Same four modes as the parking rows, with their own values — the "
    + "parcels do not have to line up with the bays.",
  winkelKante: "Edge",
  altbestandTitel: "Parking lots from the removed engine",
  altbestandText1:
    "This save contains parking lots that were built with the old engine. It has been removed.",
  altbestandText2:
    "They keep working as they are. But they can never be rebuilt the same way, and editing one gives you today's result from the first Apply on.",
  altbestandLoeschen: "Delete them",
  altbestandBehalten: "Keep them",
  winkelFest: "Fixed",
  winkelQuer: "Across",
  winkelNormal: "Normal",
  ausrichten: "Align to polygon line",
  ausrichtenZurueck: "Reset alignment",
  ausrichtenFertig: "Done",
  trennungFertig: "Done splitting",
  debugLiveLog: "Live log",
  kurzDebugLiveLog: "Live log",
  autoZufahrt: "Auto switch to Entry",
  schalterAn: "On",
  schalterAus: "Off",
  tooltipAutoZufahrt:
    "After the outline is closed, switch to entrance placement right away. Same switch as in the mod settings.",
  kurzDebugUeberlappung: "Overlaps",
  kurzDebugPrefabvergleich: "Road prefabs",
  kurzDebugSonde: "Shape probe",
  kurzDebugSondeErgebnis: "Limits",
  kurzDebugZoningsonde: "Zoning probe",
  kurzDebugSezieren: "Prefab dump",
  kurzDebugTraeger: "Carrier",
  kurzSchritt1: "Mark",
  kurzSchritt2: "Marks",
  kurzSchritt3: "Report",
  debugLiveLogAn: "Start live log",
  debugLiveLogAus: "Stop live log",
  debugLiveLogHinweis:
    "Writes one short line per preview run while you work — what was calculated, and where the time went. Starts a fresh file every time you switch it on.",
  debugUeberlappung: "Object overlap diagnosis",
  debugPrefabvergleich: "Road prefab comparison",
  debugPrefabvergleichStart: "Compare road prefabs",
  debugPrefabvergleichHinweis:
    "Writes the junction-relevant prefab data of the zoning road clone, its Alley original and the invisible paths to ParkingLotTool.Mod.log. No selection needed.",
  debugUeberlappungStart: "Scan selected parking lot",
  debugUeberlappungHinweis:
    "Select a PLT parking lot or a building grown on its zoning road, then scan. The result always goes to ParkingLotTool.Mod.log; an enabled live log receives the detailed trace as well.",
  fahrgassenbreite: "Aisle width",
  querstrassenbreite: "Cross road width",
  verbindungAlle: "Cross road every",
  einheitBuchten: "bays",
  einheitGrad: "deg",
  mittelgruen: "Median",
  gruenstreifentiefe: "Median depth",
  randstrassen: "Perimeter roads",
  tooltipRandstrassen: "Off uses the full site for rows. Roads serving edge zoning remain.",
  kappen: "Caps at cross roads",
  reiterEntwurf: "Draft",
  tooltipEntwurf:
    "Outline, rows, roads and surfaces of the lot you are drawing.",
  tooltipStellenMarkieren:
    "Turns the left mouse button into a marker pen. Click every spot that "
    + "looks wrong.",
  tooltipBerichtSchreiben:
    "Writes your marks and what the mod last did into one file you can send.",
  tooltipMeldungAbsturz:
    "Collects the log of the run that crashed, before this one overwrites it.",
  tooltipMeldungVorschau:
    "Reports what the preview shows right now - nothing has to be built yet.",
  tooltipMeldungBau:
    "Reports the lot you built last, with the receipt of how it was made.",
  tooltipMeldeLot:
    "Then click a lot out in the world; right-click cancels.",
  tooltipMeldungOrdner:
    "Opens the folder holding the files, ready to attach.",
  tooltipGebuehrHaken:
    "Turns the fee off and back on. The amount you set is kept.",
  tooltipGebuehrRegler:
    "Drag to set what drivers pay here. It steers them between lots rather "
    + "than earning you money.",
  tooltipOrtSpringen: "Select this place and move the camera to it.",
  reiterMelden: "Report a problem",
  reiterDebug: "Dev-Debug",

  /*
   * Der Meldeweg fuer die Testveroeffentlichung. Bewusst in der Sprache des
   * Spielers: nicht "Dump", nicht "Abzug", sondern "report" und "file".
   */
  meldungTitel: "Send a report",
  meldungAbsturzTitel: "The game crashed last time",
  meldungAbsturzKnopf: "Create crash report",
  meldungVorschauKnopf: "Report the preview",
  meldungBauKnopf: "Report the last build",
  meldungOrdnerKnopf: "Open folder",
  meldungErklaerung:
    "Creates one file holding everything needed to look into it: what you "
    + "built, what the mod did last, and the game's own log. Send that file "
    + "with your description.",
  meldungVorschauErklaerung:
    "Use this while something looks wrong in the preview - before building.",
  meldungLiegtBei: (pfad: string) => `Saved to ${pfad}`,
  befundTitel: "What the mod noticed",
  befundNichts: "Nothing was flagged during the last run.",
  kurzinfoTitel: "Last run",
  lotBericht: "Report this lot",
  meldeLotKnopf: "Report a parking lot",
  meldeLotWahlLaeuft: "Click a parking lot …",
  meldeLotErklaerung:
    "Pick one of your parking lots in the world and it goes into the report, "
    + "the same as \"Report this lot\" in its info window. Right-click cancels.",
  tooltipLotBericht:
    "Writes one file about THIS parking lot - what it was built from and "
    + "what the mod did. Send it along if something here looks wrong.",
  debugSonde: "Probe run: what does CS2 accept?",
  debugSondeHinweis:
    "Places single test surfaces on the terrain, reads from the triangle "
    + "buffer whether CS2 accepted them, deletes them again and halves its "
    + "way to the real limit. Only ever one surface at a time. Point at a "
    + "spot on the ground first, then start.",
  debugSondeStart: "Start probe run",
  debugSondeLaeuft: "Probe run in progress",
  debugSondeStop: "Cancel",
  debugSondeErgebnis: "Measured limits",
  debugZoningsonde: "Zoning probe",
  debugZoningsondeHinweis:
    "Point at a free place on the ground, choose the narrow real road and "
    + "build the probe. Its complete result is written to the game log with "
    + "the prefix PLT-Zoningsonde:. Use a throwaway save.",
  debugZoningsondeGasse: "Alley",
  debugZoningsondeSchotter: "Gravel road",
  debugZoningsondeUnsichtbar: "Invisible",
  zoningFlaecheAus: "Off",
  zoningSeiten: "Toggle road sides",
  zoningSeitenAus: "Done toggling sides",
  tooltipZoningSeiten:
    "Two jobs, one tool - whatever is closer to the cursor wins. On a "
    + "zoning road: click the side you mean to turn its zoning on or "
    + "off. On a line of the outline: click to turn that stretch into "
    + "edge zoning - the perimeter road there becomes a zoning road and "
    + "houses grow outward. Right click or Esc leaves the mode.",
  tooltipZoningSeitenGesperrt:
    "Only works on built roads. Build the parking lot first - the "
    + "switch changes a flag on a real road, and in the preview there "
    + "is none yet.",
  zoningSeitenHinweis: "Available after building",
  debugZoningsondeBauen: "Build road and zone",
  debugZoningsondeMessen: "Measure growth / load",
  debugZoningsondeLoeschen: "Remove probe",
  debugSezieren: "Prefab dump: how is a real parking lot built?",
  debugSeziererHinweis:
    "Writes a text file listing what the game’s own parking lot prefabs "
    + "are made of — their parts, their ECS components, and what our own "
    + "surface is missing compared to them. Changes nothing; you can press "
    + "it any time. Place a vanilla parking lot first if you want the "
    + "section about a finished one to be filled in.",
  debugSeziererStart: "Write prefab dump",
  debugTraeger: "Carrier test: can a bare owner hold the markings?",
  debugTraegerHinweis:
    "An experiment, not a build step. It re-parents five markings of an "
    + "existing lot to a bare carrier entity and watches whether they stay "
    + "put. USE A THROWAWAY SAVE. Press 'create', then hover the lot, "
    + "build a road next to it, save, reload - and press 'check'.",
  debugTraegerStart: "Start carrier test",
  debugTraegerAbschliessen: "Finish carrier test",
  debugNochNichts: "Nothing measured yet.",
  debugFertig: "Finished.",
  bauen: "Build",
  rueckgaengig: "Undo",
  stellplaetze: "spaces",
  titelZurueckPolygon: "Back to editing the polygon",
  titelErstZufahrt: "Place an entrance first",
  titelPolygonSchliessen: "Close the polygon first",
  titelBauen: "Build parking lot (Enter)",
  titelZiehen: "Drag to move",
  titelSchliessen: "Close",
  titelReset: "Reset all values to your defaults",
  titelVerwerfen: "Discard saved defaults and load factory values",
  schritt1: "1 · Mark",
  schritt2: "2 · Marks",
  schritt3: "3 · Report",
  stellenMarkieren: "Mark spots",
  markierenLaeuft: "Marking active · click in the world",
  gesetzt: "Placed",
  letzteZurueck: "Undo last",
  alleLoeschen: "Clear all",
  berichtErstMarkieren: "Write report · mark something first",
  berichtSchreiben: "Write report",
  amRandGassen: (rand: number, gassen: number) =>
    `${rand} perimeter · ${gassen} aisles`,
  jeBucht: (wert: string) => `${wert} per space`,
  winkelAreal: (winkel: string, flaeche: string) => `${winkel} · ${flaeche} site`,
  hinweis: (zeile: string) => `Note: ${zeile}`,
  berichtMitAnzahl: (anzahl: number) => `Write report (${anzahl})`,
  standardSpeichern: (name: string) =>
    `Remembers this value as your default for ${name}.`,
  standardZuruecksetzen: (name: string) =>
    `Back to your default for ${name}.`,
  meldeEinleitung: "Something looks wrong? Mark the spot and write a report. "
    + "It says in plain words what is off there.",
  meldeKlickhinweis: "Left click places a magenta mark, right click takes the last one back.",
  werkzeugTitel: "Parking lot tool",
  einstellungenOffen: "Close parking lot settings",
  einstellungenZu: "Parking lot settings",
  tooltipRandabstand:
    "Increases the setback from the outline. Tidier, costs spaces.",
  tooltipReihenwinkel: "Sets the row angle for the whole lot.",
  tooltipWinkelKante: "Follows the edge you drew.",
  tooltipWinkelQuer: "Rows at a right angle to the reference line.",
  tooltipWinkelNormal: "Rows along the line you picked.",
  tooltipAusrichten:
    "Pick one line of your outline; the rows follow it instead of the longest edge.",
  tooltipAusrichtenZurueck:
    "Drops the picked line — the longest edge counts again.",
  tooltipAusrichtenFertig:
    "Finishes picking and brings the preview back. Every line you picked stays.",
  tooltipTrennungFertig:
    "Takes the cuts you drew as the sub-areas and starts picking reference lines. Without a cut the whole outline stays one area.",
  tooltipDebugLiveLog:
    "For reporting a problem that only happens on your shape: switch it on, do the one thing, switch it off, send the file.",
  tooltipDebugUeberlappung:
    "Finds Overridden objects and exact collision pairs only around the selected lot.",
  tooltipWinkelFest: "Takes the angle from the slider below, measured from the edge.",
  tooltipWinkel: "Turns the rows away from the edge. Only listened to with Fixed.",
  tooltipFahrgassenbreite:
    "Widens the aisles — more room to manoeuvre.",
  tooltipQuerstrassenbreite: "Widens the cross roads between the rows.",
  tooltipVerbindungAlle:
    "Adds a cross road after this many bays. Shorter walks.",
  tooltipMittelgruen:
    "Slips a strip of grass between two back-to-back rows.",
  tooltipGruenstreifentiefe: "Widens the median. Costs a row of bays.",
  tooltipKappen:
    "Caps each row with grass. Off packs in more cars.",
  tooltipFlaecheStrasseAn:
    "Lays the road surface. Off leaves the ground untouched.",
  tooltipFlaecheStrasse: "Chooses which road surface is laid.",
  tooltipFlaecheDekoAn:
    "Lays the decoration surface. Off leaves the ground untouched.",
  tooltipFlaecheDeko: "Chooses which decoration surface is laid.",
  tooltipVorflaeche:
    "Carries the road surface across the pavement to the asphalt.",
  tooltipBuchtsymbole:
    "Paints the lines and the disabled and electric symbols.",
  tooltipZufahrt: "Cars come in and go out here.",
  tooltipGasse:
    "Same placement as an entrance, but built as an invisible alley. "
    + "The street outside gets a real junction, so its kerb opens up. "
    + "It costs a few zoning cells there.",
  tooltipGasseEin:
    "An alley that only leads IN: from the street into the lot. "
    + "Narrower than the two-way alley, and the kerb opens up just the same.",
  tooltipGasseAus:
    "An alley that only leads OUT: from the lot onto the street. "
    + "Narrower than the two-way alley, and the kerb opens up just the same.",
  tooltipFussweg:
    "The way in on foot. Disabled and electric bays move here.",
  tooltipEinfahrt: "One way in. Needs an exit somewhere else.",
  tooltipAusfahrt: "One way out. Needs an entry somewhere else.",
  tooltipBauen: "Builds what the preview shows · Enter",
  tooltipRueckgaengig: "Takes back the last completed step · Ctrl+Z",
  wiederherstellen: "Redo",
  tooltipWiederherstellen:
    "Puts back the step you just undid · Ctrl+Y",
  tooltipReset: "Brings every value back to your defaults.",
  tooltipVerwerfen:
    "Throws your defaults away and takes the factory ones.",
  tooltipSchliessen: "Closes the window · Ctrl+Shift+P",
  tooltipDebug: "Measuring runs for when something looks wrong.",
  tooltipMelden: "Marks the odd spots and writes them up for me.",
  tooltipDebugSezieren:
    "Writes down what one of the game's own lots is made of.",
  tooltipDebugSonde:
    "Tries hundreds of shapes and notes which ones CS2 accepts.",
  tooltipDebugZoningsonde:
    "Builds one real road and logs its zoning and growth.",
  tooltipDebugTraeger:
    "Checks whether the markings stay put without a building.",
  tooltipLetzteZurueck: "Takes back the mark you just set.",
  tooltipAlleLoeschen: "Removes every mark on the lot.",
  tooltipZuschnitt: "Shape and direction of the bay rows.",
  tooltipFahrwege: "The lanes inside the lot and how they connect.",
  tooltipGruen: "Where grass goes instead of paving.",
  tooltipFlaechen:
    "Which ground is laid, and the markings on it.",

  /* --- Parkplatzliste ---------------------------------------------- */
  listeSuchen: "Search",
  listeSummenzeile: (plaetze: number, frei: number, unterhalt: number, waehrung: string) =>
    `${plaetze} spaces · ${frei} free · ${unterhalt} ${waehrung} upkeep / month`,
  listeSuche: "Find a parking lot…",
  listeFilter: ["All", "Problems", "Almost full", "Free spaces"],
  listeSortierungen: ["Name", "Occupancy", "Free spaces", "Size"],
  listeSortieren: "Sort by",
  listeThemen: ["Insights", "Visitors", "Walking routes", "Usage", "City comparison"],
  listePause: "Pause insights",
  listeFortsetzen: "Resume insights",
  listePausiert: "Paused",
  listeGebuehrHilfe: "Drag or enter 0–50. 0 = free. Enter saves; Escape cancels.",
  listeGebuehrFehler: "Fee not saved. Enter a whole number from 0 to 50 and try again.",
  listeSchaetzung: "Estimate / month",
  listeBearbeiten: "Edit",
  waiseTitel: "Orphaned parking lot",
  waiseErklaerung:
    "This save was stored while Parking Lot Tool was off or missing, so the "
    + "lot lost its link to the mod.",
  waiseReparieren: "Repair",
  tooltipWaiseReparieren:
    "Reconnects the lot to the mod. Shape, markings and roads stay exactly "
    + "as they are.",
  waiseNichtReparierbar:
    "Its markings cannot be matched to it without doubt, so it is not "
    + "repaired.",
  waisenAlleReparieren: (n: number) => `Repair all (${n})`,
  waisenAuto: "Repair automatically",
  tooltipWaisenAuto:
    "Same switch as in the mod settings. When on, orphaned lots are "
    + "reconnected after loading, one at a time.",
  ohneBauzettel: "Editing unavailable: the build plan was lost with the save.",
  listeNurKosten: "Upkeep / month",
  listeKeineTreffer: "No parking lots match these filters.",
  listeFilterZurueck: "Clear search and filters",
  listeFreiePlaetze: (n: number) => `${n} free`,
  listeUnterhaltSumme: (n: number, waehrung: string) => `${n} ${waehrung} upkeep / month`,
  listeSeite: (n: number, gesamt: number) => `Page ${n} of ${gesamt}`,
  listeTreffer: (n: number, gesamt: number) => `${n} of ${gesamt} parking lots`,
  listeVorigeSeite: "Previous parking lots",
  listeNaechsteSeite: "Next parking lots",
  reiterListe: "Parking lots",
  tooltipListe: "Every lot you built: figures, fee and a jump to it.",
  listeLeer: "No parking lots built yet.",
  listeKopf: (lots: number, plaetze: number, unterhalt: number) =>
    `${lots} lots · ${plaetze} spaces · ${unterhalt} upkeep`,
  spaltePlaetze: "Spaces",
  spalteBelegt: "Occupied",
  spalteGebuehr: "Fee",
  tooltipGebuehr:
    "What drivers pay here. In CS2 a fee does not earn you money per lot - "
    + "it steers drivers to other lots.",
  spalteUnterhalt: "Upkeep",
  tooltipHinspringen: "Select this lot and move the camera to it.",
  tooltipUmbenennenListe: "Click the name to rename this lot.",
  tooltipBearbeitenListe: "Jump to the lot and reopen it for editing.",
  tooltipVorigeInfo: "Previous fact",
  tooltipNaechsteInfo: "Next fact",
  infoNochKeineDaten: "Nothing measured here yet.",
  listeGroesse: (breite: number, tiefe: number) =>
    `${breite} \u00d7 ${tiefe} m`,
  listeAlter: (tage: number) => tage < 60
    ? (tage <= 1 ? "built today" : `built ${tage} days ago`)
    : `${Math.round(tage / 30)} months old`,
  listeBelegtVon: (belegt: number, gesamt: number) =>
    `${belegt} of ${gesamt} taken`,
  listeJeMonat: "per month",
  listeZuJung:
    "Too fresh to judge. It takes a few weeks before word gets around that "
    + "this lot exists - red figures at the start are normal.",
  tooltipErgebnisKosten:
    "Upkeep per month, the same figure the info window shows.",
  tooltipErgebnisGeschaetzt:
    "Upkeep minus estimated fees. CS2 books no parking income per lot - "
    + "this is the mod counting arrivals, and it counts low: a car that "
    + "comes and goes between two samples is never seen.",
  mengenwoerter: ["A few", "Some", "Many", "Most"],
  zweckwoerter: ["shopping", "work", "relaxing", "sightseeing"],
  alterswoerter: ["children", "teenagers", "adults", "seniors"],
  wegwoerter: ["short", "usual", "far"],

  infoZielRang: (menge: string, ort: string): Satzteil[] =>
    [`${menge} walk from here to `, { ort }, "."],
  infoZweck: (menge: string, zweck: string): Satzteil[] =>
    [`${menge} are here for ${zweck}.`],
  infoWohnparkplatz: (menge: string): Satzteil[] =>
    [`${menge} walk home from here.`],
  infoTouristen: (menge: string): Satzteil[] =>
    [`${menge} cars here belong to tourists.`],
  infoAlter: (menge: string, gruppe: string): Satzteil[] =>
    [`${menge} guests are ${gruppe}.`],
  infoBildung: (n: number): Satzteil[] =>
    [`${n} in 10 guests hold a university degree.`],
  infoZufriedenheit: (besser: boolean): Satzteil[] =>
    [besser
      ? "People parking here are happier than the rest of the city."
      : "People parking here are unhappier than the rest of the city."],
  infoFussweg: (meter: number, ort: string, urteil: string): Satzteil[] =>
    [`It is another ${meter} m on foot to `, { ort }, `. That is ${urteil}.`],
  infoLaufweite: (n: number): Satzteil[] =>
    [n === 1
      ? "This lot serves one building within walking distance."
      : `This lot serves ${n} buildings within walking distance.`],
  infoErreichbar: (laeden: number, bueros: number, wohnungen: number)
    : Satzteil[] => ["Within walking distance: " + [
      laeden > 0 ? `${laeden} ${laeden === 1 ? "shop" : "shops"}` : "",
      bueros > 0 ? `${bueros} ${bueros === 1 ? "office" : "offices"}` : "",
      wohnungen > 0
        ? `${wohnungen} ${wohnungen === 1 ? "home" : "homes"}` : "",
    ].filter((s) => s !== "").join(", ") + "."],
  infoTagesprofil: (voll: number, leer: number): Satzteil[] =>
    [`Busy from ${voll}:00, empty from ${leer}:00.`],
  infoSpitze: (stunde: number, belegt: number, gesamt: number): Satzteil[] =>
    [`Peak at ${stunde}:00 - ${belegt} of ${gesamt} taken.`],
  infoDauerlast: (tage: number, prozent: number): Satzteil[] =>
    [`Around ${prozent} % full across the last ${tage} days.`],
  infoLeerstand: (n: number): Satzteil[] =>
    [n === 1 ? "One space usually stays empty."
      : `${n} spaces usually stay empty.`],
  infoDurchsatz: (autos: number, tage: number, jeTag: number): Satzteil[] =>
    [`${autos} cars in ${tage} days - ${jeTag} per space and day.`],
  infoStandzeit: (stunden: number, minuten: number): Satzteil[] =>
    [`Cars stay ${stunden} h ${minuten} min on average.`],
  infoRang: (rang: number, gesamt: number): Satzteil[] =>
    [rang === 1
      ? "The largest parking lot in your city."
      : `Number ${rang} of your ${gesamt} lots by size.`],
  infoKosten: (teuerster: boolean): Satzteil[] =>
    [teuerster
      ? "Your most expensive lot per space."
      : "Your cheapest lot per space."],
  infoBilanz: (tage: number, autos: number): Satzteil[] =>
    [`${tage} days old, ${autos} cars since it opened.`],

  mangelTot: (): Satzteil[] =>
    ["Not a single car all week. Check the entrance."],
  mangelKeinWeg: (menge: string): Satzteil[] =>
    [`${menge} guests cannot get any further from here.`],
  mangelZufahrt: (plaetze: number): Satzteil[] =>
    [`One entrance for ${plaetze} spaces. The exit will jam.`],
  mangelUngleichgewicht: (ort: string, prozent: number): Satzteil[] =>
    ["This lot is full while ", { ort },
      ` sits at ${prozent} %. Raise the fee here, lower it there.`],
  mangelGebuehr: (tage: number, prozent: number, richtung: number)
    : Satzteil[] => [richtung === 2
      ? `You changed the fee ${tage} days ago. Nothing moved - there is no `
        + "alternative nearby."
      : `Since the fee changed ${tage} days ago, ${prozent} % `
        + `${richtung === 0 ? "more" : "fewer"} cars come here.`],
};

export type Texte = typeof en;

const de: Texte = {
  parkgebuehrJeVorgang: "Parkgebuehr je Vorgang",
  bestimmungen: "Bestimmungen",
  artNameZufahrt: "Zufahrt",
  bushaltestelle: "Bushaltestelle",
  tooltipBushaltestelle:
    "Eine echte Bushaltestelle auf eine Zoning-Straße setzen. "
    + "Der Cursor wählt die Seite; Rechtsklick entfernt einen Halt.",
  fensterSchliessen: "Fenster schließen",
  leistungTitel: "Spiel ruckelt?",
  leistungErklaerung:
    "Misst eine Minute lang, wohin die Bildzeit geht, und schreibt danach "
    + "einen Bericht zum Verschicken. Spiel dabei einfach weiter - gerade "
    + "das Gewöhnliche soll gemessen werden.",
  leistungStart: "Eine Minute messen",
  leistungLaeuft: (s: number) => `Messung läuft … noch ${s} s`,
  tooltipLeistung:
    "Schreibt eine Zeile je Sekunde und eine für jedes Bild über 50 ms, "
    + "aufgeschlüsselt danach, was der Mod gerade getan hat. Bilder, die "
    + "nicht von uns kommen, sind als solche erkennbar.",
  tooltipFensterHeim:
    "Setzt das Fenster auf seinen Platz zurück - der Ausweg, wenn man es "
    + "aus dem Bild geschoben hat.",
  fangAlleAn: "Einrasten für alle einschalten",
  fangAlleAus: "Einrasten für alle ausschalten",
  fangNamen: {
    ExistingGeometry: "An bestehenden Grundstücken einrasten",
    StraightDirection: "An geraden Winkeln einrasten",
    NetSide: "An Fahrbahnkanten einrasten",
    ObjectSide: "An Gebäudekanten einrasten",
    GuideLines: "An Hilfslinien einrasten",
    ZoneGrid: "Am Zonenraster einrasten",
  } as Record<string, string>,
  stilHochkant: "Hochkant",
  stilHorizontal: "Horizontal",
  tooltipStil:
    "Wechselt zwischen schmaler Spalte links und breiter Leiste über der "
    + "Werkzeugleiste. Die Einstellungen sind in beiden dieselben.",
  artNameGasse: "Gasse",
  artNameGasseEin: "Gasse rein",
  artNameGasseAus: "Gasse raus",
  artNameFussweg: "Fußweg",
  artNameEinfahrt: "Einfahrt",
  artNameAusfahrt: "Ausfahrt",
  fehltZugang: "Es fehlt eine Zufahrt, oder je eine Ein- und Ausfahrt",
  fehltEinfahrt: "Eine Ausfahrt ohne Einfahrt - bitte eine Einfahrt setzen",
  fehltAusfahrt: "Eine Einfahrt ohne Ausfahrt - die Autos kämen nicht heraus",
  titelZufahrt: "Zufahrt, beide Richtungen - auf den Umriss setzen",
  titelEinfahrt: "Nur Einfahrt, eine Spur - auf den Umriss setzen",
  titelAusfahrt: "Nur Ausfahrt, eine Spur - auf den Umriss setzen",
  titelFussweg: "Fußgängerzugang - auf den Umriss setzen",
  angestellte: "Angestellte",
  beschaeftigte: "Beschäftigte",
  parkgebuehr: "Parkgebühr",
  bearbeiten: "Bearbeiten",
  tooltipBearbeiten:
    "Öffnet den Parkplatz wieder zum Ändern von Umriss, Zufahrten und Einstellungen.",
  waehrung: "¢",
  gebuehrAus: "Aus",
  flaecheStrasseAn: "Fahrfläche setzen",
  flaecheDekoAn: "Zwischenfläche setzen",
  geschriebenNach: (pfad: string) => `Geschrieben nach: ${pfad}`,
  berichtOrt:
    "Die Datei landet im Logs-Ordner des Spiels und heißt "
    + "«ParkingLotTool-report-…txt». Sie ist kurz genug zum Verschicken.",
  spalteFlaechen: "Flächen",
  flaecheStrasse: "Fahrfläche",
  flaecheDeko: "Zwischenfläche",
  vorflaeche: "Belag bis zur Straße",
  buchtsymbole: "Buchtmarkierung",
  titel: "PARKING LOT TOOL",
  zuschnitt: "Zuschnitt",
  fahrwege: "Fahrwege",
  gruen: "Grün",
  randabstand: "Randabstand",
  reihenwinkel: "Reihenwinkel",
  winkel: "Winkel",
  zoningReiter: "Zoning",
  zoningSetzen: "Flächen setzen",
  zoningSetzenAus: "Setzen beenden",
  zoningWinkel: "Parzellenwinkel",
  zoningLinie: "Linie",
  zoningSeite: "Außenband",
  zoningAussentiefe: "Tiefe in Kacheln",
  zoningSeiteHinweis:
    "Die Zoning-Straße läuft außen um das gezogene Rechteck. Jede ihrer "
    + "Seiten kann außen zusätzliche Parzellen tragen, jede mit eigener "
    + "Tiefe. Der Parkplatz hält den Platz frei — keine Fahrgasse, keine "
    + "Querstraße, keine Buchten.",
  zoningTiefeKlick:
    "Tiefe wählen, dann „Straßenseite schalten“ einschalten und auf die "
    + "AUSSENSEITE einer Zoning-Straße klicken. Noch einmal auf dieselbe "
    + "Seite klicken nimmt das Band wieder weg.",
  tooltipZoningAussentiefe:
    "Wie tief das nächste angeklickte Band wird, in 8-m-Kacheln.",
  tooltipZoningTiefeKnopf: "Kacheln tief:",
  zoningFlaeche: "Parzellenboden",
  zoningFlaecheHinweis:
    "Nur der Boden unter den Parzellen — die Zoning-Straße bleibt eine "
    + "Fahrfläche wie alle anderen. Ohne Wahl gilt die Dekofläche.",
  tooltipZoningFlaeche:
    "Wählt die Fläche, die unter den Baugrundstücken liegt.",
  zoningLinieAbbrechen: "Abbrechen",
  zoningGewaehlt: "gewählt:",
  tooltipZoningLinie:
    "Eine Linie des Umrisses anklicken; die Parzellen folgen ihr statt der "
    + "längsten Kante.",
  tooltipZoningLinieAbbrechen:
    "Beendet die Wahl. Der Umriss bleibt, wie er ist.",
  zoningStand: "Flächen",
  zoningParzellen: "Parzellen",
  zoningHinweis:
    "Im geschlossenen Umriss ein Rechteck ziehen. Es rastet auf ganze "
    + "Parzellen zu 8 m, höchstens 25 mal 25. Ziehen verschiebt eine Fläche, "
    + "Rechtsklick löscht sie. Was du ziehst, sind nur die Parzellen — "
    + "die Zoning-Straße kommt außen dazu.",
  tooltipZoningReiter: "Setzt Baugrundstücke in den Parkplatz.",
  tooltipZoningGesperrt:
    "Erst einen Umriss zeichnen und schließen — die Parzellen liegen "
    + "im Parkplatz.",
  tooltipZoningSetzen:
    "Übergibt die linke Maustaste an die Parzellen. Der Umriss bleibt, "
    + "wie er ist.",
  tooltipZoningWinkel:
    "Dieselben vier Modi wie beim Reihenwinkel, mit eigenen Werten — "
    + "die Parzellen müssen nicht zu den Buchten passen.",
  winkelKante: "Kante",
  altbestandTitel: "Parkplätze aus dem ausgebauten Rechenweg",
  altbestandText1:
    "In diesem Spielstand liegen Parkplätze, die mit dem alten Rechenweg gebaut wurden. Den gibt es nicht mehr.",
  altbestandText2:
    "Sie funktionieren weiter wie sie sind. Gleich neu bauen lassen sie sich aber nie wieder, und wer einen bearbeitet, bekommt ab dem ersten Übernehmen das heutige Ergebnis.",
  altbestandLoeschen: "Löschen",
  altbestandBehalten: "Behalten",
  winkelFest: "Fest",
  winkelQuer: "Quer",
  winkelNormal: "Normal",
  ausrichten: "An Polygonlinie ausrichten",
  ausrichtenZurueck: "Ausrichtung zurücksetzen",
  ausrichtenFertig: "Fertig",
  trennungFertig: "Trennung fertig",
  debugLiveLog: "Live-Log",
  kurzDebugLiveLog: "Live-Log",
  autoZufahrt: "Automatisch zur Zufahrt",
  schalterAn: "An",
  schalterAus: "Aus",
  tooltipAutoZufahrt:
    "Nach dem Schließen des Umrisses sofort in den Zufahrt-Modus wechseln. Derselbe Schalter wie in den Mod-Einstellungen.",
  kurzDebugUeberlappung: "Überlappung",
  kurzDebugPrefabvergleich: "Straßenprefabs",
  kurzDebugSonde: "Flächensonde",
  kurzDebugSondeErgebnis: "Grenzen",
  kurzDebugZoningsonde: "Zoning-Sonde",
  kurzDebugSezieren: "Prefababzug",
  kurzDebugTraeger: "Träger",
  kurzSchritt1: "Markieren",
  kurzSchritt2: "Markierungen",
  kurzSchritt3: "Bericht",
  debugLiveLogAn: "Live-Log starten",
  debugLiveLogAus: "Live-Log stoppen",
  debugLiveLogHinweis:
    "Schreibt beim Arbeiten eine kurze Zeile je Vorschaulauf — was gerechnet wurde und woran die Zeit hing. Beginnt bei jedem Einschalten eine neue Datei.",
  debugUeberlappung: "Objektüberlappungen diagnostizieren",
  debugPrefabvergleich: "Straßenprefabs vergleichen",
  debugPrefabvergleichStart: "Straßenprefabs vergleichen",
  debugPrefabvergleichHinweis:
    "Schreibt die kreuzungsrelevanten Prefabdaten des Zoning-Straßenklons, seines Alley-Vorbilds und der unsichtbaren Wege in ParkingLotTool.Mod.log. Keine Auswahl nötig.",
  debugUeberlappungStart: "Gewählten Parkplatz absuchen",
  debugUeberlappungHinweis:
    "Einen PLT-Parkplatz oder ein an seiner Zoning-Straße gewachsenes Gebäude auswählen, dann suchen. Der Befund landet immer in ParkingLotTool.Mod.log; ein eingeschaltetes Live-Log erhält zusätzlich die ausführliche Spur.",
  fahrgassenbreite: "Fahrgassenbreite",
  querstrassenbreite: "Querstraßenbreite",
  verbindungAlle: "Verbindung alle",
  einheitBuchten: "Buchten",
  einheitGrad: "Grad",
  mittelgruen: "Mittelgrün",
  gruenstreifentiefe: "Grünstreifentiefe",
  randstrassen: "Randstraßen",
  tooltipRandstrassen: "Aus nutzt die Fläche für durchgehende Reihen. Straßen am Randzoning bleiben erhalten.",
  kappen: "Kappen an Querstraßen",
  reiterEntwurf: "Entwurf",
  tooltipEntwurf:
    "Umriss, Reihen, Fahrwege und Flächen des Parkplatzes, den du gerade "
    + "zeichnest.",
  tooltipStellenMarkieren:
    "Macht aus der linken Maustaste einen Stift. Jede Stelle anklicken, die "
    + "falsch aussieht.",
  tooltipBerichtSchreiben:
    "Schreibt deine Markierungen und was die Mod zuletzt tat in EINE Datei "
    + "zum Verschicken.",
  tooltipMeldungAbsturz:
    "Sichert das Log des abgestürzten Laufs, bevor dieser es überschreibt.",
  tooltipMeldungVorschau:
    "Meldet, was die Vorschau gerade zeigt - es muss noch nichts gebaut sein.",
  tooltipMeldungBau:
    "Meldet den zuletzt gebauten Parkplatz samt Bauzettel.",
  tooltipMeldeLot:
    "Danach einen Parkplatz im Gelände anklicken; Rechtsklick bricht ab.",
  tooltipMeldungOrdner:
    "Öffnet den Ordner mit den Dateien, fertig zum Anhängen.",
  tooltipGebuehrHaken:
    "Schaltet die Gebühr aus und wieder an. Der eingestellte Betrag bleibt.",
  tooltipGebuehrRegler:
    "Ziehen stellt ein, was Fahrer hier zahlen. Das lenkt sie zwischen "
    + "Parkplätzen, statt dir Geld zu bringen.",
  tooltipOrtSpringen: "Diesen Ort auswählen und die Kamera hinschicken.",
  reiterMelden: "Fehler melden",
  reiterDebug: "Dev-Debug",
  meldungTitel: "Bericht schicken",
  meldungAbsturzTitel: "Das Spiel ist letztes Mal abgestürzt",
  meldungAbsturzKnopf: "Absturzbericht erstellen",
  meldungVorschauKnopf: "Vorschau melden",
  meldungBauKnopf: "Letzten Bau melden",
  meldungOrdnerKnopf: "Ordner öffnen",
  meldungErklaerung:
    "Erstellt EINE Datei mit allem, was zum Nachsehen nötig ist: was du "
    + "gebaut hast, was die Mod zuletzt getan hat, und das Log des Spiels. "
    + "Schick die Datei zusammen mit deiner Beschreibung.",
  meldungVorschauErklaerung:
    "Dafür, wenn in der Vorschau etwas krumm aussieht - noch vor dem Bauen.",
  meldungLiegtBei: (pfad: string) => `Gespeichert unter ${pfad}`,
  befundTitel: "Was der Mod aufgefallen ist",
  befundNichts: "Beim letzten Lauf wurde nichts bemängelt.",
  kurzinfoTitel: "Letzter Lauf",
  lotBericht: "Bericht schreiben",
  meldeLotKnopf: "Einen Parkplatz melden",
  meldeLotWahlLaeuft: "Parkplatz anklicken …",
  meldeLotErklaerung:
    "Einen deiner Parkplätze im Gelände anklicken; er kommt in den Bericht, "
    + "genau wie über „Bericht schreiben“ in seinem Fenster. Rechtsklick "
    + "bricht ab.",
  tooltipLotBericht:
    "Schreibt EINE Datei zu DIESEM Parkplatz - woraus er gebaut wurde und "
    + "was die Mod zuletzt getan hat. Schick sie mit, wenn hier etwas krumm "
    + "aussieht.",
  debugSonde: "Sondenlauf: was nimmt CS2 an?",
  debugSondeHinweis:
    "Setzt einzelne Testflächen ins Gelände, liest am Dreieckspuffer ab, ob "
    + "CS2 sie angenommen hat, löscht sie wieder und halbiert sich so an die "
    + "wahre Grenze heran. Immer nur eine Fläche gleichzeitig. Zeige zuerst "
    + "auf eine Stelle im Gelände, dann starten.",
  debugSondeStart: "Sondenlauf starten",
  debugSondeLaeuft: "Sondenlauf läuft",
  debugSondeStop: "Abbrechen",
  debugSondeErgebnis: "Gemessene Grenzen",
  debugZoningsonde: "Zoning-Sonde",
  debugZoningsondeHinweis:
    "Auf eine freie Stelle im Gelände zeigen, die schmale echte Straße "
    + "wählen und die Sonde bauen. Das vollständige Ergebnis steht mit dem "
    + "Präfix PLT-Zoningsonde: im Spiellog. Wegwerf-Spielstand benutzen.",
  debugZoningsondeGasse: "Gasse",
  debugZoningsondeSchotter: "Schotterstraße",
  debugZoningsondeUnsichtbar: "Unsichtbar",
  zoningFlaecheAus: "Aus",
  zoningSeiten: "Straßenseiten schalten",
  zoningSeitenAus: "Seiten fertig",
  tooltipZoningSeiten:
    "Zwei Aufgaben, ein Werkzeug — was näher am Zeiger liegt, gewinnt. "
    + "Auf einer Zoning-Straße: auf die gemeinte Seite klicken, das "
    + "schaltet dort das Zoning an oder aus. Auf einer Linie des "
    + "Umrisses: klicken macht daraus Randzoning — die Randstraße wird "
    + "dort zur Zoning-Straße, und die Häuser wachsen nach außen. "
    + "Rechtsklick oder Esc beendet den Modus.",
  tooltipZoningSeitenGesperrt:
    "Geht nur an gebauten Straßen. Erst den Parkplatz bauen - der "
    + "Schalter ändert eine Eigenschaft an einer echten Straße, und "
    + "in der Vorschau gibt es die noch nicht.",
  zoningSeitenHinweis: "Erst nach dem Bauen",
  debugZoningsondeBauen: "Straße bauen und zonieren",
  debugZoningsondeMessen: "Wachstum / Laden messen",
  debugZoningsondeLoeschen: "Sonde entfernen",
  debugSezieren: "Prefababzug: woraus besteht ein echter Parkplatz?",
  debugSeziererHinweis:
    "Schreibt eine Textdatei, die auflöst, woraus die Parkplatz-Prefabs "
    + "des Spiels bestehen — ihre Bauteile, ihre ECS-Komponenten, und was "
    + "unserer eigenen Fläche im Vergleich fehlt. Ändert nichts, kann "
    + "jederzeit gedrückt werden. Setz vorher einen Vanilla-Parkplatz, "
    + "wenn auch der Abschnitt über ein fertiges Ding gefüllt sein soll.",
  debugSeziererStart: "Prefababzug schreiben",
  debugTraeger: "Trägertest: hält ein nackter Besitzer die Markierungen?",
  debugTraegerHinweis:
    "Ein Versuch, kein Bauschritt. Hängt fünf Markierungen eines "
    + "vorhandenen Parkplatzes an eine nackte Träger-Entity um und schaut, "
    + "ob sie liegenbleiben. NIMM EINEN WEGWERF-SPIELSTAND. Erst "
    + "„anlegen“, dann hovern, eine Straße daneben bauen, speichern, "
    + "neu laden - und dann „prüfen“.",
  debugTraegerStart: "Trägertest starten",
  debugTraegerAbschliessen: "Trägertest abschließen",
  debugNochNichts: "Noch nichts gemessen.",
  debugFertig: "Fertig.",
  bauen: "Bauen",
  rueckgaengig: "Rückgängig",
  stellplaetze: "Stellplätze",
  titelZurueckPolygon: "Zurück zum Bearbeiten des Polygons",
  titelErstZufahrt: "Erst eine Zufahrt setzen",
  titelPolygonSchliessen: "Polygon zuerst schließen",
  titelBauen: "Parkplatz bauen (Enter)",
  titelZiehen: "Zum Verschieben ziehen",
  titelSchliessen: "Schließen",
  titelReset: "Alle Werte auf Benutzerstandard zurücksetzen",
  titelVerwerfen: "Gespeicherte Standards verwerfen und Werkswerte laden",
  schritt1: "1 · Markieren",
  schritt2: "2 · Markierungen",
  schritt3: "3 · Bericht",
  stellenMarkieren: "Stellen markieren",
  markierenLaeuft: "Markieren läuft · in die Welt klicken",
  gesetzt: "Gesetzt",
  letzteZurueck: "Letzte zurück",
  alleLoeschen: "Alle löschen",
  berichtErstMarkieren: "Bericht schreiben · erst markieren",
  berichtSchreiben: "Bericht schreiben",
  amRandGassen: (rand: number, gassen: number) =>
    `${rand} am Rand · ${gassen} Gassen`,
  jeBucht: (wert: string) => `${wert} je Bucht`,
  winkelAreal: (winkel: string, flaeche: string) => `${winkel} · ${flaeche} Areal`,
  hinweis: (zeile: string) => `Hinweis: ${zeile}`,
  berichtMitAnzahl: (anzahl: number) => `Bericht schreiben (${anzahl})`,
  standardSpeichern: (Name: string) =>
    `Merkt sich diesen Wert als deinen Standard für ${Name}.`,
  standardZuruecksetzen: (Name: string) =>
    `Zurück auf deinen Standard für ${Name}.`,
  meldeEinleitung: "Etwas sieht falsch aus? Markiere die Stelle und schreibe einen "
    + "Bericht. Er nennt in Klartext, was dort nicht stimmt.",
  meldeKlickhinweis: "Linksklick setzt eine magenta Markierung, Rechtsklick nimmt die "
    + "letzte zurück.",
  werkzeugTitel: "Parkplatz-Werkzeug",
  einstellungenOffen: "Parkplatz-Einstellungen schließen",
  einstellungenZu: "Parkplatz-Einstellungen",
  tooltipRandabstand:
    "Vergrößert den Randabstand. Wirkt aufgeräumter, kostet Plätze.",
  tooltipReihenwinkel:
    "Legt den Reihenwinkel für den ganzen Parkplatz fest.",
  tooltipWinkelKante: "Folgt der Kante, die du gezogen hast.",
  tooltipWinkelQuer: "Reihen im rechten Winkel zur Bezugslinie.",
  tooltipWinkelNormal: "Reihen entlang der gewählten Linie.",
  tooltipAusrichten:
    "Eine Linie des Umrisses wählen; die Reihen folgen ihr statt der längsten Kante.",
  tooltipAusrichtenZurueck:
    "Verwirft die gewählte Linie — es zählt wieder die längste Kante.",
  tooltipAusrichtenFertig:
    "Beendet das Wählen und zeigt wieder die Vorschau. Alle gewählten Linien bleiben.",
  tooltipTrennungFertig:
    "Übernimmt die gezogenen Schnitte als Teilflächen und beginnt die Linienwahl. Ohne Schnitt bleibt der ganze Umriss eine Fläche.",
  tooltipDebugLiveLog:
    "Für Probleme, die nur an deiner Form auftreten: einschalten, die eine Sache tun, ausschalten, Datei schicken.",
  tooltipDebugUeberlappung:
    "Findet Overridden-Objekte und exakte Kollisionspaare nur um den gewählten Parkplatz.",
  tooltipWinkelFest: "Nimmt den Winkel aus dem Regler darunter, gemessen ab der Kante.",
  tooltipWinkel: "Dreht die Reihen weg von der Kante. Zählt nur bei Fest.",
  tooltipFahrgassenbreite:
    "Verbreitert die Fahrgassen — mehr Raum zum Rangieren.",
  tooltipQuerstrassenbreite:
    "Verbreitert die Querstraßen zwischen den Reihen.",
  tooltipVerbindungAlle:
    "Setzt nach so vielen Buchten eine Querstraße. Kürzere Wege.",
  tooltipMittelgruen:
    "Schiebt Gras zwischen zwei Reihen, die Rücken an Rücken stehen.",
  tooltipGruenstreifentiefe:
    "Verbreitert den Grünstreifen. Kostet eine Buchtreihe.",
  tooltipKappen:
    "Setzt Graskappen an die Reihenenden. Aus bringt mehr Autos.",
  tooltipFlaecheStrasseAn:
    "Legt die Fahrfläche an. Aus lässt den Boden unberührt.",
  tooltipFlaecheStrasse: "Wählt den Belag für die Fahrfläche.",
  tooltipFlaecheDekoAn:
    "Legt die Zwischenfläche an. Aus lässt den Boden unberührt.",
  tooltipFlaecheDeko: "Wählt den Belag für die Zwischenfläche.",
  tooltipVorflaeche:
    "Führt die Fahrfläche über den Gehweg bis an den Asphalt.",
  tooltipBuchtsymbole:
    "Malt die Linien und die Zeichen für Behinderten- und E-Plätze.",
  tooltipZufahrt: "Hier fahren die Autos rein und wieder raus.",
  tooltipGasse:
    "Wird gesetzt wie eine Zufahrt, aber als unsichtbare Gasse gebaut. "
    + "Die Straße draußen bekommt dadurch eine echte Einmündung, und ihr "
    + "Bordstein geht auf. Ein paar Zonenkacheln kostet das dort.",
  tooltipGasseEin:
    "Eine Gasse, die nur HINEIN führt: von der Straße in den Parkplatz. "
    + "Schmaler als die zweispurige, und der Bordstein geht genauso auf.",
  tooltipGasseAus:
    "Eine Gasse, die nur HINAUS führt: vom Parkplatz auf die Straße. "
    + "Schmaler als die zweispurige, und der Bordstein geht genauso auf.",
  tooltipFussweg:
    "Der Weg zu Fuß. Behinderten- und E-Plätze rücken hierher.",
  tooltipEinfahrt: "Nur rein. Braucht anderswo eine Ausfahrt.",
  tooltipAusfahrt: "Nur raus. Braucht anderswo eine Einfahrt.",
  tooltipBauen: "Baut, was die Vorschau zeigt · Enter",
  tooltipRueckgaengig: "Nimmt den letzten abgeschlossenen Schritt zurück · Strg+Z",
  wiederherstellen: "Wiederherstellen",
  tooltipWiederherstellen:
    "Setzt den zurückgenommenen Schritt wieder ein · Strg+Y",
  tooltipReset: "Holt alle Werte auf deine Standards zurück.",
  tooltipVerwerfen:
    "Wirft deine Standards weg und nimmt die Werkswerte.",
  tooltipSchliessen: "Schließt das Fenster · Strg+Umschalt+P",
  tooltipDebug:
    "Messläufe für den Fall, dass etwas komisch aussieht.",
  tooltipMelden:
    "Markiert die komischen Stellen und schreibt sie auf.",
  tooltipDebugSezieren:
    "Schreibt auf, woraus ein Parkplatz des Spiels gebaut ist.",
  tooltipDebugSonde:
    "Probiert hunderte Formen durch und notiert, was CS2 annimmt.",
  tooltipDebugZoningsonde:
    "Baut eine echte Straße und protokolliert Zoning und Wachstum.",
  tooltipDebugTraeger:
    "Prüft, ob die Markierungen ohne Gebäude liegen bleiben.",
  tooltipLetzteZurueck:
    "Nimmt die eben gesetzte Markierung zurück.",
  tooltipAlleLoeschen: "Entfernt alle Markierungen.",
  tooltipZuschnitt: "Form und Ausrichtung der Buchtreihen.",
  tooltipFahrwege:
    "Die Fahrwege im Parkplatz und ihre Verbindungen.",
  tooltipGruen: "Wo Gras liegt statt Belag.",
  tooltipFlaechen:
    "Welcher Boden gelegt wird und was darauf steht.",

  /* --- Parkplatzliste ---------------------------------------------- */
  listeSuchen: "Suche",
  listeSummenzeile: (plaetze: number, frei: number, unterhalt: number, waehrung: string) =>
    `${plaetze} Plätze · ${frei} frei · ${unterhalt} ${waehrung} Unterhalt / Monat`,
  listeSuche: "Parkplatz suchen…",
  listeFilter: ["Alle", "Probleme", "Fast voll", "Freie Plätze"],
  listeSortierungen: ["Name", "Belegung", "Freie Plätze", "Größe"],
  listeSortieren: "Sortieren nach",
  listeThemen: ["Einblicke", "Besucher", "Fußwege", "Nutzung", "Stadtvergleich"],
  listePause: "Infowechsel pausieren",
  listeFortsetzen: "Infowechsel fortsetzen",
  listePausiert: "Pausiert",
  listeGebuehrHilfe: "Ziehen oder 0–50 eingeben. 0 = kostenlos. Enter speichert; Escape verwirft.",
  listeGebuehrFehler: "Gebühr nicht gespeichert. Ganze Zahl von 0 bis 50 eingeben und erneut versuchen.",
  listeSchaetzung: "Schätzung / Monat",
  listeBearbeiten: "Bearbeiten",
  waiseTitel: "Verwaister Parkplatz",
  waiseErklaerung:
    "Der Spielstand wurde gespeichert, während Parking Lot Tool aus war oder "
    + "fehlte. Der Parkplatz hat dabei seine Verbindung zur Mod verloren.",
  waiseReparieren: "Reparieren",
  tooltipWaiseReparieren:
    "Verbindet den Parkplatz wieder mit der Mod. Form, Markierungen und "
    + "Straßen bleiben genau, wie sie sind.",
  waiseNichtReparierbar:
    "Seine Markierungen lassen sich ihm nicht zweifelsfrei zuordnen, "
    + "deshalb wird er nicht repariert.",
  waisenAlleReparieren: (n: number) => `Alle reparieren (${n})`,
  waisenAuto: "Automatisch reparieren",
  tooltipWaisenAuto:
    "Derselbe Schalter wie in den Mod-Einstellungen. Ist er an, werden "
    + "verwaiste Parkplätze nach dem Laden wieder verbunden, einer nach dem "
    + "anderen.",
  ohneBauzettel: "Bearbeiten nicht möglich: der Bauplan ging mit dem Spielstand verloren.",
  listeNurKosten: "Unterhalt / Monat",
  listeKeineTreffer: "Keine Parkplätze passen zu diesen Filtern.",
  listeFilterZurueck: "Suche und Filter zurücksetzen",
  listeFreiePlaetze: (n: number) => `${n} frei`,
  listeUnterhaltSumme: (n: number, waehrung: string) => `${n} ${waehrung} Unterhalt / Monat`,
  listeSeite: (n: number, gesamt: number) => `Seite ${n} von ${gesamt}`,
  listeTreffer: (n: number, gesamt: number) => `${n} von ${gesamt} Parkplätzen`,
  listeVorigeSeite: "Vorige Parkplätze",
  listeNaechsteSeite: "Nächste Parkplätze",
  reiterListe: "Parkplätze",
  tooltipListe:
    "Alle gebauten Parkplätze: Zahlen, Gebühr und ein Sprung hin.",
  listeLeer: "Noch keinen Parkplatz gebaut.",
  listeKopf: (lots: number, plaetze: number, unterhalt: number) =>
    `${lots} Parkplätze · ${plaetze} Plätze · ${unterhalt} Unterhalt`,
  spaltePlaetze: "Plätze",
  spalteBelegt: "Belegt",
  spalteGebuehr: "Gebühr",
  tooltipGebuehr:
    "Was Fahrer hier zahlen. In CS2 bringt die Gebühr je Parkplatz kein "
    + "Geld - sie lenkt Fahrer auf andere Plätze.",
  spalteUnterhalt: "Unterhalt",
  tooltipHinspringen: "Diesen Parkplatz auswählen und hinspringen.",
  tooltipUmbenennenListe: "Auf den Namen klicken, um ihn zu ändern.",
  tooltipBearbeitenListe:
    "Hinspringen und den Parkplatz zum Bearbeiten öffnen.",
  tooltipVorigeInfo: "Vorige Info",
  tooltipNaechsteInfo: "Nächste Info",
  infoNochKeineDaten: "Hier ist noch nichts gemessen.",
  listeGroesse: (breite: number, tiefe: number) =>
    `${breite} \u00d7 ${tiefe} m`,
  listeAlter: (tage: number) => tage < 60
    ? (tage <= 1 ? "heute gebaut" : `vor ${tage} Tagen gebaut`)
    : `seit ${Math.round(tage / 30)} Monaten`,
  listeBelegtVon: (belegt: number, gesamt: number) =>
    `${belegt} von ${gesamt} belegt`,
  listeJeMonat: "je Monat",
  listeZuJung:
    "Noch zu frisch f\u00fcr ein Urteil. Bis sich herumspricht, dass es den "
    + "Platz gibt, vergehen ein paar Wochen - die roten Zahlen am Anfang "
    + "sind normal.",
  tooltipErgebnisKosten:
    "Unterhalt je Monat, dieselbe Zahl wie im Infofenster.",
  tooltipErgebnisGeschaetzt:
    "Unterhalt abz\u00fcglich gesch\u00e4tzter Geb\u00fchren. CS2 bucht "
    + "keine Parkeinnahmen je Parkplatz - das hier z\u00e4hlt der Mod selbst, "
    + "und er z\u00e4hlt zu wenig: wer zwischen zwei Proben kommt und wieder "
    + "f\u00e4hrt, wird nie gesehen.",
  mengenwoerter: ["Wenige", "Einige", "Viele", "Die meisten"],
  zweckwoerter: ["Einkaufen", "Arbeiten", "Entspannen", "Besichtigen"],
  alterswoerter: ["Kinder", "Jugendliche", "Erwachsene", "Senioren"],
  wegwoerter: ["kurz", "üblich", "weit"],

  infoZielRang: (menge: string, ort: string): Satzteil[] =>
    [`${menge} gehen von hier zu `, { ort }, "."],
  infoZweck: (menge: string, zweck: string): Satzteil[] =>
    [`${menge} sind zum ${zweck} hier.`],
  infoWohnparkplatz: (menge: string): Satzteil[] =>
    [`${menge} laufen von hier nach Hause.`],
  infoTouristen: (menge: string): Satzteil[] =>
    [`${menge} Autos hier gehören Touristen.`],
  infoAlter: (menge: string, gruppe: string): Satzteil[] =>
    [`${menge} Gäste sind ${gruppe}.`],
  infoBildung: (n: number): Satzteil[] =>
    [`${n} von 10 Gästen haben einen Hochschulabschluss.`],
  infoZufriedenheit: (besser: boolean): Satzteil[] =>
    [besser
      ? "Wer hier parkt, ist zufriedener als der Rest der Stadt."
      : "Wer hier parkt, ist unzufriedener als der Rest der Stadt."],
  infoFussweg: (meter: number, ort: string, urteil: string): Satzteil[] =>
    [`Von hier sind es noch ${meter} m zu Fuß bis `, { ort },
      `. Das ist ${urteil}.`],
  infoLaufweite: (n: number): Satzteil[] =>
    [n === 1
      ? "Dieser Platz bedient ein Gebäude in Laufweite."
      : `Dieser Platz bedient ${n} Gebäude in Laufweite.`],
  infoErreichbar: (laeden: number, bueros: number, wohnungen: number)
    : Satzteil[] => ["Zu Fuß erreichbar: " + [
      laeden > 0 ? `${laeden} ${laeden === 1 ? "Laden" : "Läden"}` : "",
      bueros > 0 ? `${bueros} ${bueros === 1 ? "Büro" : "Büros"}` : "",
      wohnungen > 0
        ? `${wohnungen} ${wohnungen === 1 ? "Wohnhaus" : "Wohnhäuser"}`
        : "",
    ].filter((s) => s !== "").join(", ") + "."],
  infoTagesprofil: (voll: number, leer: number): Satzteil[] =>
    [`Voll ab ${voll} Uhr, leer ab ${leer} Uhr.`],
  infoSpitze: (stunde: number, belegt: number, gesamt: number): Satzteil[] =>
    [`Spitze um ${stunde} Uhr: ${belegt} von ${gesamt} belegt.`],
  infoDauerlast: (tage: number, prozent: number): Satzteil[] =>
    [`In den letzten ${tage} Tagen etwa ${prozent} % belegt.`],
  infoLeerstand: (n: number): Satzteil[] =>
    [n === 1 ? "Ein Platz bleibt meistens leer."
      : `${n} Plätze bleiben meistens leer.`],
  infoDurchsatz: (autos: number, tage: number, jeTag: number): Satzteil[] =>
    [`${autos} Autos in ${tage} Tagen - ${jeTag} je Platz und Tag.`],
  infoStandzeit: (stunden: number, minuten: number): Satzteil[] =>
    [`Autos stehen hier im Schnitt ${stunden} h ${minuten} min.`],
  infoRang: (rang: number, gesamt: number): Satzteil[] =>
    [rang === 1
      ? "Der größte Parkplatz deiner Stadt."
      : `Nummer ${rang} deiner ${gesamt} Parkplätze nach Größe.`],
  infoKosten: (teuerster: boolean): Satzteil[] =>
    [teuerster
      ? "Dein teuerster Parkplatz je Stellplatz."
      : "Dein günstigster Parkplatz je Stellplatz."],
  infoBilanz: (tage: number, autos: number): Satzteil[] =>
    [`${tage} Tage alt, ${autos} Autos seit der Eröffnung.`],

  mangelTot: (): Satzteil[] =>
    ["Die ganze Woche kein einziges Auto. Prüfe die Zufahrt."],
  mangelKeinWeg: (menge: string): Satzteil[] =>
    [`${menge} Gäste kommen von hier nicht weiter.`],
  mangelZufahrt: (plaetze: number): Satzteil[] =>
    [`Eine Zufahrt für ${plaetze} Plätze. An der Ausfahrt wird es eng.`],
  mangelUngleichgewicht: (ort: string, prozent: number): Satzteil[] =>
    ["Der Platz ist voll, ", { ort },
      ` steht bei ${prozent} %. Hier die Gebühr rauf, dort runter.`],
  mangelGebuehr: (tage: number, prozent: number, richtung: number)
    : Satzteil[] => [richtung === 2
      ? `Du hast die Gebühr vor ${tage} Tagen geändert. Es hat nichts `
        + "geändert - hier gibt es keine Alternative."
      : `Seit der Gebührenänderung vor ${tage} Tagen kommen ${prozent} % `
        + `${richtung === 0 ? "mehr" : "weniger"} Autos.`],
};

export const texte = { en, de };

/** Die Texte der eingestellten Sprache; alles ausser "de" ist Englisch. */
export const useTexte = (): Texte =>
  useValue(sprache$) === "de" ? de : en;
