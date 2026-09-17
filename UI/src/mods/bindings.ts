import { bindValue, trigger } from "cs2/api";

/**
 * Alles, was das Panel vom Mod weiss - und alles, was es ihm sagen kann.
 *
 * Eine einzige Stelle dafuer, damit Namen nicht ueber mehrere Dateien
 * verstreut sind: ein Tippfehler in einem Bindungsnamen faellt sonst erst
 * im Spiel auf, und zwar als stumm nicht reagierender Regler.
 */
const MOD = "ParkingLotTool";

export type SettingKey =
  | "EdgeSetback"
  | "AisleWidth"
  | "CrossWidth"
  | "MedianWidth"
  | "CrossBays"
  | "CarrierTestRunning"
  | "CarrierTestState"
  | "SurfaceRoad"
  | "SurfaceDecoration"
  | "SurfaceRoadOn"
  | "SurfaceDecorationOn"
  | "SurfaceApronOn"
  | "PanelStil"
  | "Fangarten"
  | "LeistungRest"
  | "BayIcons"
  | "RowAngle"
  | "GreenMedian"
  | "CrossCaps"
  | "Randstrassen"
  | "AngleMode";

export const panelOpen$ = bindValue<boolean>(MOD, "PanelOpen", false);
export const toolActive$ = bindValue<boolean>(MOD, "ToolActive", false);

/** Der Instanzwert des gerade ausgewaehlten PLT-Parkplatzes. */
export const parkingFeeVisible$ = bindValue<boolean>(
  MOD, "ParkingFeeVisible", false);
export const selectedParkingFee$ = bindValue<number>(
  MOD, "SelectedParkingFee", 0);
export const setSelectedParkingFee = (fee: number) =>
  trigger(MOD, "SetSelectedParkingFee", fee);

export const edgeSetback$ = bindValue<number>(MOD, "EdgeSetback", 1);
export const aisleWidth$ = bindValue<number>(MOD, "AisleWidth", 7);
export const crossWidth$ = bindValue<number>(MOD, "CrossWidth", 3);
export const greenMedian$ = bindValue<boolean>(MOD, "GreenMedian", true);
export const crossCaps$ = bindValue<boolean>(MOD, "CrossCaps", true);
export const randstrassen$ = bindValue<boolean>(MOD, "Randstrassen", true);
export const medianWidth$ = bindValue<number>(MOD, "MedianWidth", 2.5);
/**
 * Wie viele Buchten zwischen zwei Verbindungsstrassen liegen.
 *
 * Frueher ein Abstand in Metern. Gerechnet wird aber in Buchten, damit alle
 * Abschnitte gleich viele tragen - also steht das jetzt auch am Regler.
 */
export const crossBays$ = bindValue<number>(MOD, "CrossBays", 9);
export const angleMode$ = bindValue<string>(MOD, "AngleMode", "edge");
export const rowAngle$ = bindValue<number>(MOD, "RowAngle", 0);

/**
 * Welcher Rechenweg: "alt" oder "zellen".
 *
 * "alt" schneidet die Flaechen und repariert hinterher - gemessen bis zu
 * 12,5 Minuten auf einer schraegen L-Form. "zellen" baut sie als Raster,
 * das sich nicht ueberschneiden kann - dieselbe Form in 15,4 ms.
 */
export const engine$ = bindValue<string>(MOD, "Engine", "zellen");

/**
 * Hat der Nutzer die Nachfrage vor dem alten Rechenweg abgestellt?
 *
 * Der Wert liegt in den Optionen des Mods, nicht nur im Panel - deshalb
 * ueberlebt er das Spiel und laesst sich im ESC-Menue wieder einschalten.
 */
export const altEngineOhneWarnung$ =
  bindValue<boolean>(MOD, "AltEngineOhneWarnung", false);
export const edgeSetbackDefault$ = bindValue<number>(MOD, "EdgeSetbackDefault", 1);
export const aisleWidthDefault$ = bindValue<number>(MOD, "AisleWidthDefault", 7);
export const crossWidthDefault$ = bindValue<number>(MOD, "CrossWidthDefault", 3);
export const greenMedianDefault$ = bindValue<boolean>(MOD, "GreenMedianDefault", true);
export const crossCapsDefault$ = bindValue<boolean>(MOD, "CrossCapsDefault", true);
export const randstrassenDefault$ = bindValue<boolean>(MOD, "RandstrassenDefault", true);
export const medianWidthDefault$ = bindValue<number>(MOD, "MedianWidthDefault", 2.5);
export const crossBaysDefault$ = bindValue<number>(MOD, "CrossBaysDefault", 9);
export const angleModeDefault$ = bindValue<string>(MOD, "AngleModeDefault", "edge");
export const rowAngleDefault$ = bindValue<number>(MOD, "RowAngleDefault", 0);
export const polygonClosed$ = bindValue<boolean>(MOD, "PolygonClosed", false);
export const undoAvailable$ = bindValue<boolean>(MOD, "UndoAvailable", false);
export const entranceMode$ = bindValue<boolean>(MOD, "EntranceMode", false);
export const entranceCount$ = bindValue<number>(MOD, "EntranceCount", 0);
/*
 * Welche Art der naechste Klick setzt - 0 Zufahrt, 1 Einfahrt, 2 Ausfahrt,
 * 3 Fussweg. Die Reihenfolge ist die des C#-Aufzaehlungstyps `Zufahrtsart`;
 * sie darf nur gemeinsam geaendert werden.
 */
export const entranceKind$ = bindValue<number>(MOD, "EntranceKind", 0);
/*
 * Die Obergrenze kommt aus dem Werkzeug, nicht als Zahl aus einem Text.
 * Sonst stehen Grenze und Anzeige an zwei Orten und driften auseinander -
 * genau das war am 2026-08-27 mit der alten Zehn passiert.
 */
export const entranceMax$ = bindValue<number>(MOD, "EntranceMax", 15);
/*
 * Was zum Bauen fehlt: "" heisst fertig, sonst "zugang", "einfahrt" oder
 * "ausfahrt". Die Regel steht im Werkzeug - hier wird sie nur angezeigt.
 * Ein Fussweg allein zaehlt nie als Zugang.
 */
export const entranceMissing$ = bindValue<string>(MOD, "EntranceMissing", "zugang");

/**
 * Wo das Fenster steht - als Anteil des Bildschirms, 0 bis 1.
 *
 * Kein rem und keine Pixel: `rem` haengt in CS2 an der Fenstergroesse
 * (`html { font-size: .0925926vh }`), eine Pixelangabe liegt bei jeder
 * anderen Aufloesung woanders. Ein Anteil sitzt ueberall gleich, und das
 * Panel muss den rem-Faktor nirgends ausrechnen.
 */
export const panelX$ = bindValue<number>(MOD, "PanelX", 10 / 1920);
export const panelY$ = bindValue<number>(MOD, "PanelY", 60 / 1080);

export const stalls$ = bindValue<number>(MOD, "Stalls", 0);
export const perimeterStalls$ = bindValue<number>(MOD, "PerimeterStalls", 0);
export const areaPerStall$ = bindValue<string>(MOD, "AreaPerStall", "-");
export const aisles$ = bindValue<number>(MOD, "Aisles", 0);
export const rowAngleResult$ = bindValue<string>(MOD, "RowAngleResult", "-");
export const siteArea$ = bindValue<string>(MOD, "SiteArea", "-");
export const status$ = bindValue<string>(MOD, "Status", "");

/**
 * Sprache der Oberflaeche, "en" oder "de".
 *
 * Standard "en", auch bevor die Einstellungen geladen sind - sonst blitzt das
 * Panel beim Start kurz deutsch auf.
 */
export const sprache$ = bindValue<string>(MOD, "Sprache", "en");

/**
 * Zaehler, der bei "Fensterposition zuruecksetzen" steigt.
 *
 * Der Mod kennt weder die Breite der Leiste noch die Aufloesung - beides
 * weiss nur die Oberflaeche. Deshalb kommt von dort nur das Signal, und das
 * Panel rechnet seine Heimposition selbst aus.
 */
export const panelHome$ = bindValue<number>(MOD, "PanelHome", 0);

/** Meldet die gemessenen Zahlen ins Modlog - damit man nicht raten muss. */
export const panelDiagnose = (text: string) => trigger(MOD, "PanelDiagnose", text);

/** Worauf gefahren und geparkt wird, und alles dazwischen. */
export const surfaceRoad$ = bindValue<string>(MOD, "SurfaceRoad", "Pavement Surface 01");
export const surfaceDecoration$ = bindValue<string>(MOD, "SurfaceDecoration", "Grass Surface 01");

/** Die waehlbaren Flaechen - kommt aus dem SPIEL, nicht aus einer Liste im Code. */
export const surfaceList$ = bindValue<string>(MOD, "SurfaceList", "");
export const surfaceRoadDefault$ = bindValue<string>(MOD, "SurfaceRoadDefault", "Pavement Surface 01");
export const surfaceDecorationDefault$ = bindValue<string>(MOD, "SurfaceDecorationDefault", "Grass Surface 01");
export const bayIcons$ = bindValue<boolean>(MOD, "BayIcons", true);
export const bayIconsDefault$ = bindValue<boolean>(MOD, "BayIconsDefault", true);

/**
 * Ob eine Flaeche ueberhaupt gesetzt wird.
 *
 * Aendert NICHTS an der Rechnung - Buchten, Wege und Zufahrten bleiben
 * gleich. Fuer alle, die den vorhandenen Boden behalten und lieber mit dem
 * Terrain-Brush arbeiten wollen.
 */
export const surfaceRoadOn$ = bindValue<boolean>(MOD, "SurfaceRoadOn", true);
export const surfaceDecorationOn$ = bindValue<boolean>(MOD, "SurfaceDecorationOn", true);
export const surfaceRoadOnDefault$ = bindValue<boolean>(MOD, "SurfaceRoadOnDefault", true);
export const surfaceDecorationOnDefault$ = bindValue<boolean>(
  MOD, "SurfaceDecorationOnDefault", true);
export const setSurfaceRoadOn = (v: boolean) => trigger(MOD, "SetSurfaceRoadOn", v);
export const setSurfaceDecorationOn = (v: boolean) => trigger(MOD, "SetSurfaceDecorationOn", v);

/*
 * Der Belag laeuft ueber den Fussgaengerweg der Strasse bis an den Asphalt.
 * EIN Schalter fuer alle Zufahrten - so vom Nutzer entschieden.
 */
export const surfaceApronOn$ = bindValue<boolean>(MOD, "SurfaceApronOn", true);

/**
 * Der Stil des Fensters: "horizontal" (Leiste) oder "hochkant" (Spalte).
 *
 * Er entscheidet AUSSCHLIESSLICH ueber die Anordnung. Beide Stile zeigen
 * denselben Inhalt mit denselben Knoepfen; der Unterschied ist, ob die
 * Spalten nebeneinander liegen oder untereinander.
 */
export const panelStil$ = bindValue<string>(MOD, "PanelStil", "horizontal");

/**
 * DIE FANGOPTIONEN GEHOEREN DEM SPIEL, NICHT UNS.
 *
 * `ToolUISystem` haelt sie in der Bindungsgruppe "tool": `selectedSnapMask`
 * ist `activeTool.selectedSnap`, `availableSnapMask` kommt aus dem
 * `GetAvailableSnapMask` unseres eigenen Werkzeugs, und
 * `setSelectedSnapMask` schreibt zurueck.
 *
 * Wir lesen und schreiben damit denselben Zustand wie CS2s eigenes
 * Werkzeugfenster. Es wird nichts kopiert und nichts ausgeblendet; wer
 * einen Schalter hier umlegt, sieht ihn dort mitgehen.
 */
/*
 * Aus UNSERER Gruppe, nicht aus "tool": `tool.availableSnapMask` meldet
 * absichtlich 0, damit CS2 kein zweites Fangfenster baut.
 */
export const fangVerfuegbar$ = bindValue<number>(MOD, "Fangarten", 0);

/**
 * Sekunden bis zum Ende der Leistungsmessung; 0 heisst, es laeuft keine.
 */
export const leistungRest$ = bindValue<number>(MOD, "LeistungRest", 0);
export const starteLeistungstest = () =>
  trigger(MOD, "StarteLeistungstest");
export const fangGewaehlt$ = bindValue<number>("tool", "selectedSnapMask", 0);
export const setzeFang = (maske: number) =>
  trigger("tool", "setSelectedSnapMask", maske);

/**
 * Die Werte des `Snap`-Aufzaehlungstyps, die unser Werkzeug anbietet -
 * in der Reihenfolge des Typs, damit die Knoepfe nicht springen.
 * Das Symbol traegt denselben Namen wie der Wert.
 */
export const FANGOPTIONEN: { bit: number; name: string }[] = [
  { bit: 1, name: "ExistingGeometry" },
  { bit: 4, name: "StraightDirection" },
  { bit: 8, name: "NetSide" },
  { bit: 0x40, name: "ObjectSide" },
  { bit: 0x400, name: "GuideLines" },
  { bit: 0x800, name: "ZoneGrid" },
];
export const setPanelStil = (v: string) => trigger(MOD, "SetPanelStil", v);
export const surfaceApronOnDefault$ = bindValue<boolean>(
  MOD, "SurfaceApronOnDefault", true);
export const setSurfaceApronOn = (v: boolean) => trigger(MOD, "SetSurfaceApronOn", v);

export const setSurfaceRoad = (v: string) => trigger(MOD, "SetSurfaceRoad", v);
export const setSurfaceDecoration = (v: string) => trigger(MOD, "SetSurfaceDecoration", v);
/* Der Boden unter den Parzellen. Leer heisst: es gilt die Dekoflaeche. */
export const surfaceZoning$ = bindValue<string>(MOD, "SurfaceZoning", "");
export const setSurfaceZoning = (v: string) =>
  trigger(MOD, "SetSurfaceZoning", v);
export const setBayIcons = (v: boolean) => trigger(MOD, "SetBayIcons", v);

/**
 * Was der gewaehlte Rechenweg an dieser Eingabe nicht konnte.
 *
 * Mehrere Hinweise sind durch einen Zeilenumbruch getrennt. Leer heisst: alles verstanden.
 */
export const hinweis$ = bindValue<string>(MOD, "Hinweis", "");

/**
 * Der Reiter "Fehler melden".
 *
 * Der Meldeweg lag vorher nur auf Alt+M. Wer den Mod aus dem Workshop laedt,
 * kennt diese Taste nicht - und ein Meldeweg, den man nicht findet, wird
 * nicht benutzt.
 */
export const tab$ = bindValue<string>(MOD, "Tab", "layout");
export const markerMode$ = bindValue<boolean>(MOD, "MarkerMode", false);
export const markerCount$ = bindValue<number>(MOD, "MarkerCount", 0);
export const reportPath$ = bindValue<string>(MOD, "ReportPath", "");

export const togglePanel = () => trigger(MOD, "TogglePanel");
export const toggleTool = () => trigger(MOD, "ToggleTool");
export const setPanelOpen = (open: boolean) => trigger(MOD, "SetPanelOpen", open);
export const resetAll = () => trigger(MOD, "ResetAll");
export const discardDefaults = () => trigger(MOD, "DiscardDefaults");
/** Setzt das Fenster auf seinen Platz zurueck - wie der Knopf in den Optionen. */
export const fensterHeim = () => trigger(MOD, "FensterHeim");
export const resetOne = (key: SettingKey) => trigger(MOD, "ResetOne", key);
export const setAsDefault = (key: SettingKey) => trigger(MOD, "SetAsDefault", key);
export const placeEntrance = () => trigger(MOD, "PlaceEntrance");

/**
 * Der Sondenlauf im Debug-Reiter.
 *
 * `probeState$` ist leer, solange nichts laeuft - das ist zugleich das
 * Merkmal, an dem die Oberflaeche "laeuft gerade" erkennt.
 */
export const probeState$ = bindValue<string>(MOD, "ProbeState", "");
export const probeResults$ = bindValue<string>(MOD, "ProbeResults", "");
export const startProbe = () => trigger(MOD, "StartProbe");
export const cancelProbe = () => trigger(MOD, "CancelProbe");
export const buildZoningProbe = (road: string) =>
  trigger(MOD, "BuildZoningProbe", road);
export const measureZoningProbe = () => trigger(MOD, "MeasureZoningProbe");
export const cleanupZoningProbe = () => trigger(MOD, "CleanupZoningProbe");

/**
 * Der Prefab-Sezierer. Schreibt nur einen Abzug in den Logs-Ordner und
 * ruehrt nichts an - deshalb ohne Zustand und ohne Rueckmeldung ausser
 * der Statuszeile.
 */
export const dissectPrefabs = () => trigger(MOD, "DissectPrefabs");

/**
 * Der Traegertest. EIN Knopf, zwei Schritte - welcher dran ist, sagt
 * `carrierTestRunning$`. Der Zustand kommt aus einer Datei und ueberlebt
 * deshalb das Speichern und Laden, das mitten in den Test gehoert.
 */
export const toggleCarrierTest = () => trigger(MOD, "ToggleCarrierTest");
export const carrierTestRunning$ = bindValue<boolean>(
  MOD, "CarrierTestRunning", false);
export const carrierTestState$ = bindValue<string>(
  MOD, "CarrierTestState", "");

/** Nur beim LOSLASSEN rufen: jeder Ruf schreibt die Einstellungsdatei. */
export const setPanelPosition = (x: number, y: number) =>
  trigger(MOD, "SetPanelPosition", x, y);

/**
 * Der Bauknopf. Setzt im Werkzeug nur einen Bauwunsch - gebaut wird im
 * Werkzeug-Durchlauf, mit denselben Pruefungen wie bei Enter.
 */
export const buildNow = () => trigger(MOD, "BuildNow");
export const undo = () => trigger(MOD, "Undo");
export const redoAvailable$ = bindValue<boolean>(MOD, "RedoAvailable", false);
export const redo = () => trigger(MOD, "Redo");
export const altbestand$ = bindValue<number>(MOD, "Altbestand", 0);
export const altbestandLoeschen = () => trigger(MOD, "AltbestandLoeschen");
export const altbestandBehalten = () => trigger(MOD, "AltbestandBehalten");
export const ausrichtWahl$ = bindValue<boolean>(MOD, "AusrichtWahl", false);
export const ausrichtAktiv$ = bindValue<boolean>(MOD, "AusrichtAktiv", false);
export const ausrichtWaehlen = () => trigger(MOD, "AusrichtWaehlen");
export const ausrichtZuruecksetzen = () => trigger(MOD, "AusrichtZuruecksetzen");
export const ausrichtBestaetigen = () => trigger(MOD, "AusrichtBestaetigen");
export const trennmodus$ = bindValue<boolean>(MOD, "Trennmodus", false);

/* ZONING. Der Reiter fuehrt eigene Winkelwerte - die Parzellen muessen
   nicht parallel zu den Buchtreihen stehen. Die Modusnamen sind
   absichtlich dieselben ("edge"/"quer"/"fixed"), damit im Panel nicht
   zwei Vokabeln fuer dieselbe Sache stehen. */
export const zoningModus$ = bindValue<boolean>(MOD, "ZoningModus", false);
/* Der Seitenschalter: einzelne Zoning-Strassen links oder rechts an- und
   ausschalten. CS2s eigenes Zonenwerkzeug greift an unseren Strassen
   nicht, weil sie Unterelemente sind. */
export const zoningSeitenModus$ =
  bindValue<boolean>(MOD, "ZoningSeitenModus", false);
/* Der Schalter greift nur an GEBAUTEN Strassen - er aendert eine Flagge an
   einer echten Kante. In der Vorschau gibt es die noch nicht, und genau
   dort hat der Nutzer ihn zuerst gesucht. */
export const zoningSeitenMoeglich$ =
  bindValue<boolean>(MOD, "ZoningSeitenMoeglich", false);
export const setZoningSeitenModus = (an: boolean) =>
  trigger(MOD, "SetZoningSeitenModus", an);
export const setZoningModus = (an: boolean) =>
  trigger(MOD, "SetZoningModus", an);
export const zoningWinkelmodus$ =
  bindValue<string>(MOD, "ZoningWinkelmodus", "edge");
export const setZoningWinkelmodus = (modus: string) =>
  trigger(MOD, "SetZoningWinkelmodus", modus);
export const zoningWinkel$ = bindValue<number>(MOD, "ZoningWinkel", 0);
export const setZoningWinkel = (grad: number) =>
  trigger(MOD, "SetZoningWinkel", grad);
export const zoningZug$ = bindValue<string>(MOD, "ZoningZug", "");
/* Wo Bauland entsteht: "innen" (Vorgabe), "aussen" oder "beides". */
export const zoningSeite$ = bindValue<string>(MOD, "ZoningSeite", "innen");
export const setZoningSeite = (s: string) =>
  trigger(MOD, "SetZoningSeite", s);
export const zoningAussentiefe$ =
  bindValue<number>(MOD, "ZoningAussentiefe", 2);
export const setZoningAussentiefe = (n: number) =>
  trigger(MOD, "SetZoningAussentiefe", n);
export const zoningLinienwahl$ =
  bindValue<boolean>(MOD, "ZoningLinienwahl", false);
export const setZoningLinienwahl = (an: boolean) =>
  trigger(MOD, "SetZoningLinienwahl", an);
/* NaN heisst: keine Linie gewaehlt. Dieselbe Vereinbarung wie beim
   Reihenwinkel des Parkplatzes. */
export const zoningAusrichtwinkel$ =
  bindValue<number>(MOD, "ZoningAusrichtwinkel", Number.NaN);
export const zoningAuswahl$ = bindValue<number>(MOD, "ZoningAuswahl", -1);
export const zoningFlaechen$ = bindValue<number>(MOD, "ZoningFlaechen", 0);
export const zoningParzellen$ = bindValue<number>(MOD, "ZoningParzellen", 0);
export const trennungFertig = () => trigger(MOD, "TrennungFertig");
export const liveLog$ = bindValue<boolean>(MOD, "LiveLog", false);
export const liveLogPfad$ = bindValue<string>(MOD, "LiveLogPfad", "");
export const liveLogUmschalten = () => trigger(MOD, "LiveLogUmschalten");
export const ueberlappungsstand$ = bindValue<string>(
  MOD, "Ueberlappungsstand", "");
export const ueberlappungMessen = () => trigger(MOD, "UeberlappungMessen");
export const prefabsVergleichen = () => trigger(MOD, "PrefabsVergleichen");

/** Oeffnet den ausgewaehlten, mit Bauzettel versehenen Parkplatz im Werkzeug. */
export const editSelectedParkingLot = () =>
  trigger(MOD, "EditSelectedParkingLot");

/**
 * Zwischen Zufahrt-Setzen und Polygon-Bearbeiten umschalten.
 *
 * Der Knopf nennt immer das ZIEL, nicht den Zustand - deshalb schickt er
 * genau das Gegenteil des aktuellen Modus.
 */
export const setEntranceMode = (on: boolean) =>
  trigger(MOD, "SetEntranceMode", on);

/*
 * Waehlt die Art UND schaltet den Setzmodus ein - zwei Klicks fuer
 * "Einfahrt setzen" waeren einer zu viel. Nochmal auf dieselbe Art schaltet
 * wieder aus, jeder Knopf ist damit sein eigener Umschalter.
 */
export const setEntranceKind = (kind: number) =>
  trigger(MOD, "SetEntranceKind", kind);

export const setTab = (id: string) => trigger(MOD, "SetTab", id);
export const setMarkerMode = (on: boolean) => trigger(MOD, "SetMarkerMode", on);
export const removeLastMarker = () => trigger(MOD, "RemoveLastMarker");
export const clearMarkers = () => trigger(MOD, "ClearMarkers");
export const writeReport = () => trigger(MOD, "WriteReport");

/*
 * DER MELDEWEG FUER DIE TESTVEROEFFENTLICHUNG.
 *
 * Ein Tester soll EINE Datei verschicken koennen, ohne im Logs-Ordner zu
 * suchen - dort lagen auf der Entwicklungsmaschine 1535 Stueck. Die Mod
 * schnuert ein Archiv und nennt den Pfad; `meldungOrdner` macht den Ordner
 * auf, damit auch die Frage "wo liegt das?" wegfaellt.
 *
 * `absturzErkannt` steht schon beim Laden fest: die Mod merkt an einer Marke,
 * ob die vorige Sitzung sauber aufgehoert hat. Ohne Absturz bleibt der Knopf
 * weg - ein Knopf, der meistens nichts tut, erzieht dazu, ihn zu ignorieren.
 */
export const entwicklerDebug$ = bindValue<boolean>(MOD, "EntwicklerDebug", false);
export const absturzErkannt$ = bindValue<boolean>(MOD, "AbsturzErkannt", false);
export const absturzBefund$ = bindValue<string>(MOD, "AbsturzBefund", "");
export const meldungPfad$ = bindValue<string>(MOD, "MeldungPfad", "");
export const meldungAbsturz = () => trigger(MOD, "MeldungAbsturz");
export const meldungVorschau = () => trigger(MOD, "MeldungVorschau");
export const meldungBau = () => trigger(MOD, "MeldungBau");
export const meldungOrdner = () => trigger(MOD, "MeldungOrdner");
/** Was beim letzten Lauf auffiel - ungefiltert, eine Zeile je Hinweis. */
export const baubefund$ = bindValue<string>(MOD, "Baubefund", "");
/** Groesse, Zeit, Modfassung des letzten Laufs - fuer eine Meldung ohne Datei. */
export const baukurzinfo$ = bindValue<string>(MOD, "Baukurzinfo", "");
/** Bericht zum ANGEWAEHLTEN Parkplatz - Knopf im Info-Fenster. */
export const meldeGewaehltenParkplatz = () =>
  trigger(MOD, "MeldeGewaehltenParkplatz");

/** Laeuft die Parkplatzwahl im Melden-Reiter gerade? */
export const meldeLotWahl$ = bindValue<boolean>(MOD, "MeldeLotWahl", false);
export const schalteMeldeLotWahl = (an: boolean) =>
  trigger(MOD, "SchalteMeldeLotWahl", an);

export const setEdgeSetback = (v: number) => trigger(MOD, "SetEdgeSetback", v);
export const setAisleWidth = (v: number) => trigger(MOD, "SetAisleWidth", v);
export const setCrossWidth = (v: number) => trigger(MOD, "SetCrossWidth", v);
export const setMedianWidth = (v: number) => trigger(MOD, "SetMedianWidth", v);
export const setCrossBays = (v: number) => trigger(MOD, "SetCrossBays", v);
export const setRowAngle = (v: number) => trigger(MOD, "SetRowAngle", v);
export const setGreenMedian = (v: boolean) => trigger(MOD, "SetGreenMedian", v);
export const setCrossCaps = (v: boolean) => trigger(MOD, "SetCrossCaps", v);
export const setRandstrassen = (v: boolean) => trigger(MOD, "SetRandstrassen", v);
export const setAngleMode = (v: string) => trigger(MOD, "SetAngleMode", v);
export const setEngine = (v: string) => trigger(MOD, "SetEngine", v);
export const setAltEngineOhneWarnung = (v: boolean) =>
  trigger(MOD, "SetAltEngineOhneWarnung", v);

/** Unified Icon Library, Host `uil` wird von deren Mod registriert. */
export const icon = (name: string) => `coui://uil/Standard/${name}.svg`;

/* ------------------------------------------------------------------ *
 * Parkplatzliste
 *
 * Alles kommt als EIN String: Zeilen durch Zeilenumbruch, Felder durch
 * Tabulator. Der Grund steht oben bei der Flaechenliste und ist gemessen -
 * ein `bindValue<string[]>` reisst in CS2 beim Laden den ganzen Mod mit.
 * ------------------------------------------------------------------ */

/** Grunddaten je Parkplatz. Laeuft mit, etwa im Sekundentakt. */
export const parkplatzListe$ = bindValue<string>(MOD, "ParkplatzListe", "");

/** Die Infobloecke der laufenden Runde. Stehen eine ganze Runde still. */
export const parkplatzInfos$ = bindValue<string>(MOD, "ParkplatzInfos", "");

/** Rundennummer und die vier gezogenen Infoarten. */
export const parkplatzRunde$ = bindValue<string>(MOD, "ParkplatzRunde", "");
export const parkplatzHatVorrunde$ = bindValue<boolean>(MOD, "ParkplatzHatVorrunde", false);

/** Auswaehlen und hinspringen - fuer Parkplaetze wie fuer Orte in Infos. */
export const parkplatzWaehlen = (schluessel: string) =>
  trigger(MOD, "ParkplatzWaehlen", schluessel);

export const parkplatzGebuehr = (schluessel: string, gebuehr: number) =>
  trigger(MOD, "ParkplatzGebuehr", `${schluessel}\t${gebuehr}`);

export const parkplatzUmbenennen = (schluessel: string, name: string) =>
  trigger(MOD, "ParkplatzUmbenennen", `${schluessel}\t${name}`);

export const parkplatzRundeVor = () => trigger(MOD, "ParkplatzRundeVor");
export const parkplatzRundeZurueck = () =>
  trigger(MOD, "ParkplatzRundeZurueck");

/** Hinspringen UND oeffnen - in einem Zug, damit nichts dazwischenkommt. */
export const parkplatzBearbeiten = (schluessel: string) =>
  trigger(MOD, "ParkplatzBearbeiten", schluessel);
