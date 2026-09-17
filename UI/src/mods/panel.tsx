import { VegetationSchalter, VegetationFenster } from "./vegetation";
import { randstrassen$, randstrassenDefault$, setRandstrassen } from "./bindings";
import { useValue } from "cs2/api";
import { useEffect, useRef, useState } from "react";
import styles from "./panel.module.scss";
import {
  Auswahl, Fangschalter, Flaeche, ModeChooser, Slider, Spalte, Toggle,
  TooltipKnopf,
} from "./controls";
import { ReportTab } from "./report";
import { DebugTab } from "./debug";
import { entwicklerDebug$ } from "./bindings";
import { ZoningTab } from "./zoning";
import { ListeTab } from "./liste";
import {
  aisleWidth$, aisleWidthDefault$, aisles$, angleMode$, angleModeDefault$,
  areaPerStall$, buildNow, crossBays$, crossBaysDefault$, crossCaps$,
  crossCapsDefault$, crossWidth$, crossWidthDefault$, discardDefaults,
  edgeSetback$, edgeSetbackDefault$, entranceCount$, entranceMode$,
  entranceKind$, entranceMax$, entranceMissing$, setEntranceKind,
  greenMedian$, greenMedianDefault$, icon, medianWidth$, medianWidthDefault$,
  panelOpen$, panelX$, panelY$, perimeterStalls$, placeEntrance, polygonClosed$,
  resetAll, resetOne, fensterHeim, rowAngle$, rowAngleDefault$, rowAngleResult$,
  setAisleWidth, setAngleMode, setAsDefault, setCrossBays, setCrossCaps,
  setCrossWidth, setEdgeSetback, setGreenMedian, setMedianWidth,
  setEntranceMode, setPanelPosition, setRowAngle, setTab, siteArea$,
  stalls$,
  hinweis$, status$, tab$,
  panelDiagnose, panelHome$, surfaceRoad$, surfaceDecoration$, surfaceList$, bayIcons$,
  bayIconsDefault$, surfaceRoadOnDefault$, surfaceDecorationOnDefault$,
  surfaceRoadDefault$, surfaceDecorationDefault$,
  surfaceRoadOn$, surfaceDecorationOn$, setSurfaceRoadOn, setSurfaceDecorationOn,
  surfaceApronOn$, surfaceApronOnDefault$, setSurfaceApronOn,
  setSurfaceRoad, setSurfaceDecoration, setBayIcons,
  togglePanel, toolActive$,
  undo, undoAvailable$, redo, redoAvailable$,
  ausrichtWahl$, ausrichtAktiv$, ausrichtWaehlen, ausrichtZuruecksetzen,
  ausrichtBestaetigen, trennmodus$, trennungFertig,
  altbestand$, altbestandLoeschen, altbestandBehalten,
  panelStil$, setPanelStil,
} from "./bindings";
import { useTexte } from "./texte";

const numberDiffers = (value: number, standard: number) =>
  Math.abs(value - standard) > 0.000001;

/**
 * Wie weit die linke obere Ecke wandern darf, wenn das echte Panelmass
 * gerade nicht bekannt ist.
 *
 * Nicht bis 1: bei 1 waere die ganze Leiste aus dem Bild, und der Griff mit
 * ihr - man haette sich selbst ausgesperrt. Bleibt die Ecke im Bild, bleibt
 * auch die Schiene greifbar, an der gezogen wird. Der Knopf in den
 * Modeinstellungen ist trotzdem da, fuer eine von Hand verstellte Datei.
 */
const MAX_ANTEIL = 0.95;

/**
 * Was vom hochkanten Fenster mindestens stehenbleiben muss - Kopfleiste,
 * ein Stueck Einstellungen und der Abschluss mit dem Bauknopf.
 */
const MindesthoeheHochkant = 420;

/** Abstand zwischen Panel und Vegetationsfenster, in rem. */
const AbstandFenster = 12;

const klemme = (wert: number, maximum = MAX_ANTEIL) =>
  wert < 0 ? 0 : wert > maximum ? maximum : wert;

/**
 * Die Leiste.
 *
 * WAAGERECHT UND OBEN, nicht mehr als hohe Saeule links. Die alte Fassung
 * war 300 rem breit und ueber 570 rem hoch - neun Regler untereinander,
 * jeder zweizeilig, dazu sechs Kennzahlen. Sie verdeckte ein Drittel des
 * Bildes, und zwar genau die Flaeche, auf der man gerade zeichnet. Jetzt
 * sind es 1250 x 161 rem am oberen Rand, und die Karte darunter bleibt frei.
 *
 * Drei Spalten nebeneinander statt drei Gruppen untereinander, jede in
 * ihrer eigenen Farbe. Rechts das Ergebnis und daneben die beiden
 * Handlungen als Blockpaar im Verhaeltnis 1:2.
 *
 * ALLE HOOKS GANZ OBEN, ausnahmslos.
 *
 * Der erste Anlauf holte sie dort, wo sie gebraucht wurden - mitten im JSX
 * und hinter einem fruehen `return null`. Solange das Panel zu war, liefen
 * zwei Hooks; beim Oeffnen ploetzlich zwanzig. React quittiert das mit
 * Fehler #310 ("mehr Hooks als beim vorigen Durchlauf"), und im Spiel
 * bedeutete das: Strg+P liess die gesamte Oberflaeche abschmieren
 * (UI.log 2026-08-11 20:17:16). Hooks duerfen weder bedingt noch nach
 * einem Rueckgabepfad stehen - das gilt auch, wenn es beim Schreiben
 * kuerzer aussieht.
 */
/*
 * Die Zufahrtsarten. Reihenfolge und Nummern sind die des
 * C#-Aufzaehlungstyps `Zufahrtsart` - beide nur gemeinsam aendern.
 *
 * `ton` ist der Farbton, den auch der Ring in der Vorschau traegt.
 *
 * Die Gasse steht direkt neben der Zufahrt, weil sie dieselbe Sache ist -
 * gesetzt wird sie genauso, nur gebaut wird sie anders. Sie teilt sich
 * bewusst das Symbol mit ihr: die Standard-Symbole liegen in einem Bundle,
 * ein geratener Name waere im Spiel ein kaputtes Bild.
 */
const ARTEN = [
  { id: 0, ton: "artZufahrt",  icon: "Road",           tooltip: "tooltipZufahrt",  name: "artNameZufahrt"  },
  { id: 4, ton: "artGasse",    icon: "Road",           tooltip: "tooltipGasse",    name: "artNameGasse"    },
  { id: 3, ton: "artFussweg",  icon: "PedestrianPath", tooltip: "tooltipFussweg",  name: "artNameFussweg"  },
  { id: 1, ton: "artEinfahrt", icon: "ArrowUp",        tooltip: "tooltipEinfahrt", name: "artNameEinfahrt" },
  { id: 2, ton: "artAusfahrt", icon: "ArrowDown",      tooltip: "tooltipAusfahrt", name: "artNameAusfahrt" },
] as const;

export const ParkingLotPanel = () => {
  const t = useTexte();
  const surfaceRoad = useValue(surfaceRoad$);
  const surfaceDecoration = useValue(surfaceDecoration$);
  /**
   * DIE FLAECHENLISTE KOMMT ALS EIN STRING.
   *
   * Eine Zeile je Flaeche, darin Name und Bildadresse durch einen Tabulator
   * getrennt. Dass hier kein Array ankommt, hat einen gemessenen Grund: ein
   * `ValueBinding<string[]>` braucht in CS2 einen eigenen Schreiber, und ohne
   * ihn wirft schon der Konstruktor - mitten in `OnLoad`, womit der ganze Mod
   * wieder abgeraeumt wird. Das steht ausfuehrlich an der Bindung selbst in
   * `Tools/ParkingLotUISystem.cs`.
   *
   * Die Adresse darf leer sein - dann zeigt die Kachel nur den Namen.
   */
  const surfaceList: Flaeche[] = useValue(surfaceList$)
    .split("\n")
    .filter((zeile) => zeile !== "")
    .map((zeile) => {
      const teile = zeile.split("\t");
      return { name: teile[0], bild: teile[1] || "" };
    });
  const bayIcons = useValue(bayIcons$);
  const surfaceApronOn = useValue(surfaceApronOn$);
  const surfaceApronOnDefault = useValue(surfaceApronOnDefault$);
  const surfaceRoadDefault = useValue(surfaceRoadDefault$);
  const surfaceDecorationDefault = useValue(surfaceDecorationDefault$);
  const surfaceRoadOn = useValue(surfaceRoadOn$);
  const surfaceDecorationOn = useValue(surfaceDecorationOn$);
  const bayIconsDefault = useValue(bayIconsDefault$);
  const surfaceRoadOnDefault = useValue(surfaceRoadOnDefault$);
  const surfaceDecorationOnDefault = useValue(surfaceDecorationOnDefault$);
  const panelHome = useValue(panelHome$);
  /**
   * "vertikal" (Leiste) oder "hochkant" (Spalte). GANZ OBEN wie jeder Hook -
   * der Kommentar am Dateikopf erklaert, warum das hier keine Stilfrage ist,
   * sondern React-Fehler 310 und eine tote Spieloberflaeche.
   */
  const panelStil = useValue(panelStil$);
  const hochkant = panelStil === "hochkant";
  /**
   * DAS VEGETATIONSFENSTER LEBT NEBEN DEM PANEL, NICHT DARIN.
   *
   * Das ist keine Geschmacksfrage: ein Kind von `.panel` wuerde mit ihm
   * wandern, sobald man das Panel verschiebt - und genau das soll es
   * nicht. Der Nutzer will eine Startposition, keine Fessel.
   *
   * Die Position wird deshalb HIER gehalten und nur EINMAL ausgerechnet.
   * Im Fenster selbst koennte sie nicht liegen: beim Schliessen wird die
   * Komponente abgebaut, und beim naechsten Oeffnen faengt sie von vorn an.
   */
  const [vegOffen, setVegOffen] = useState(false);
  /*
   * ZWEI FAECHER, EINS JE STIL.
   *
   * Ueber der schmalen Spalte ist kein Platz - dort gehoert das Fenster
   * rechts daneben. Eine einzige Stelle fuer beide waere in einem der
   * beiden Stile immer falsch.
   */
  const [vegPos, setVegPos] = useState<{ x: number; y: number } | null>(null);
  const [vegPosHochkant, setVegPosHochkant] =
    useState<{ x: number; y: number } | null>(null);

  const leiste = useRef<HTMLDivElement>(null);

  /**
   * DER BEZUGSRAHMEN IST NICHT DAS FENSTER.
   *
   * Das Panel liegt `position: absolute`, also bezieht sich `left: x%` auf
   * den naechsten POSITIONIERTEN Vorfahren. Gerechnet wurde bisher mit
   * `window.innerWidth` - solange CS2 sein Modfenster nicht selbst versetzt,
   * faellt beides zusammen, sonst liegt alles um dessen Versatz daneben.
   * Genau das hat der Nutzer am 2026-08-22 gesehen: oben, aber nicht mittig.
   *
   * Deshalb wird der Rahmen gemessen statt angenommen. Faellt die Messung
   * aus, bleibt das Fenster der Rueckfall.
   */
  /**
   * WIE BREIT DIE LEISTE IST - AUSGERECHNET, NICHT GEMESSEN.
   *
   * Gemessen ging nicht: Cohtml liefert weder `getBoundingClientRect` noch
   * `offsetParent`. Im Modlog stand am 2026-08-22 dreimal
   * "Leiste 0, Elternelement keines" - und 0 heisst links, genau das war zu
   * sehen. `window.innerWidth` funktioniert dagegen.
   *
   * Also dieselbe Rechnung, die CS2s Stylesheet macht
   * (`Content/Game/UI/index.css`):
   *
   *     html { font-size: .0925926vh }
   *     @media (min-height: 56.25vw) { html { font-size: .0520833vw } }
   *
   * Also: ist die Hoehe mindestens 56,25 % der Breite (16:9 oder schmaler),
   * haengt 1rem an der BREITE, sonst an der HOEHE. Die Leiste ist hoechstens
   * 1680rem und zugleich 88 Prozent des Rahmens breit. `fontScale` vergroessert
   * nur die Schrift; die Flaeche zoomt nicht mit.
   *
   * `offsetWidth` wird trotzdem zuerst versucht: sollte Cohtml es eines Tages
   * koennen, ist die echte Breite immer besser als die nachgerechnete.
   */
  /**
   * Die Skala der Werkzeugleiste, wie der Spieler sie eingestellt hat.
   *
   * Sie steckt in `--toolbarScale` und geht in `--toolbarHeight` ein
   * (`calc(50rem * var(--toolbarScale))`). Cohtml ist nicht Chrome, also
   * wird der Zugriff abgesichert: faellt er aus, bleibt es bei 1 - dem
   * Wert, bei dem der Nutzer gemessen hat.
   */
  const toolbarSkala = () => {
    try {
      const wert = getComputedStyle(document.documentElement)
        .getPropertyValue("--toolbarScale");
      const zahl = parseFloat(wert);
      return zahl > 0 && zahl < 10 ? zahl : 1;
    } catch {
      return 1;
    }
  };

  /**
   * Wie hoch ueber der Bildschirmunterkante das Fenster enden soll.
   *
   * GEMESSEN, NICHT GERECHNET: der Nutzer hat bei 1920x1080 nachgesehen -
   * CS2s Hauptleiste beginnt oben bei 980 px, das Fenster darueber endet
   * bei 974. Macht 106 px Abstand zur Unterkante, und bei 1080p ist 1 rem
   * genau 1 px.
   *
   * Davon wachsen 50 rem mit `--toolbarScale` mit (das ist CS2s eigene
   * `--toolbarHeight`), die uebrigen 56 rem sind fest. Bei Skala 1 kommen
   * wieder die gemessenen 106 heraus.
   */
  const unterrandRem = () => 56 + 50 * toolbarSkala();

  /** 1 rem in Pixeln, nach derselben Rechnung wie CS2s Stylesheet. */
  const remGroesse = () => {
    const b = window.innerWidth || 1920;
    const h = window.innerHeight || 1080;
    return h >= b * 0.5625 ? b / 1920 : h / 1080;
  };

  /**
   * Die Breiten und Hoehen der beiden Stile in rem.
   *
   * GESCHAETZTE HOEHEN, und das steht hier, damit es niemand fuer gemessen
   * haelt: Cohtml liefert weder `getBoundingClientRect` noch `offsetHeight`.
   * Sie dienen nur der Heimposition - liegt das Fenster ein paar Pixel
   * daneben, schiebt man es. Laege es dagegen unter der Werkzeugleiste,
   * waere es unbedienbar, und genau davor schuetzt die Schaetzung.
   */
  /**
   * Breite je Stil. EINE HOEHE STEHT HIER NICHT MEHR.
   *
   * Sie war geschaetzt, und die Schaetzung ging daneben - der Nutzer sah
   * das Fenster "viel hoeher" als die gemessenen 6 px ueber der
   * Hauptleiste. Cohtml liefert keine Hoehe, also wird sie nicht mehr
   * gebraucht: die Leiste haengt am Unterrand, die Spalte am Oberrand.
   */
  const stilmass = () => (hochkant
    ? { breite: 364, anteil: 1 }
    : { breite: 1680, anteil: 0.88 });

  /**
   * DIESELBE RECHNUNG WIE IM STYLESHEET, sonst liegt die Mitte daneben.
   *
   * Der erste Anlauf deckelte hier auf 94 % und in der CSS auf 88 % - auf
   * einem breiten Bildschirm rechnete die Mitte dadurch mit einer zu
   * breiten Leiste, und die stand links versetzt. Der Anteil steht deshalb
   * beim Mass und nicht als Zahl in dieser Zeile.
   */
  const leistenbreite = () => {
    const gemessen = leiste.current?.offsetWidth ?? 0;
    if (gemessen > 0) return gemessen;
    const b = window.innerWidth || 1920;
    const mass = stilmass();
    return Math.min(mass.breite * remGroesse(), b * mass.anteil);
  };

  const rahmen = () => {
    const eltern = leiste.current?.offsetParent as HTMLElement | null;
    return {
      breite: eltern?.clientWidth || window.innerWidth || 1,
      hoehe: eltern?.clientHeight || window.innerHeight || 1,
    };
  };
  const open = useValue(panelOpen$);
  // GANZ OBEN, wie der Kommentar am Dateikopf verlangt. Der erste Anlauf
  // stand hinter `if (!open || !toolActive) return null;` - und damit lief
  // dieser Hook nur bei offenem Panel. React quittierte es mit Fehler #310,
  // und im Spiel starb die ganze Oberflaeche beim Oeffnen (UI.log
  // 2026-09-14 20:44:39). Derselbe Fehler wie am 2026-08-11.
  const entwicklerDebug = useValue(entwicklerDebug$);
  const toolActive = useValue(toolActive$);
  const edgeSetback = useValue(edgeSetback$);
  const edgeSetbackDefault = useValue(edgeSetbackDefault$);
  const angleMode = useValue(angleMode$);
  const angleModeDefault = useValue(angleModeDefault$);
  const rowAngle = useValue(rowAngle$);
  const rowAngleDefault = useValue(rowAngleDefault$);
  const aisleWidth = useValue(aisleWidth$);
  const aisleWidthDefault = useValue(aisleWidthDefault$);
  const crossWidth = useValue(crossWidth$);
  const crossWidthDefault = useValue(crossWidthDefault$);
  const crossBays = useValue(crossBays$);
  const crossBaysDefault = useValue(crossBaysDefault$);
  const greenMedian = useValue(greenMedian$);
  const greenMedianDefault = useValue(greenMedianDefault$);
  const randstrassen = useValue(randstrassen$);
  const randstrassenDefault = useValue(randstrassenDefault$);
  const crossCaps = useValue(crossCaps$);
  const crossCapsDefault = useValue(crossCapsDefault$);
  const medianWidth = useValue(medianWidth$);
  const medianWidthDefault = useValue(medianWidthDefault$);
  const stalls = useValue(stalls$);
  const perimeterStalls = useValue(perimeterStalls$);
  const areaPerStall = useValue(areaPerStall$);
  const aisles = useValue(aisles$);
  const rowAngleResult = useValue(rowAngleResult$);
  const siteArea = useValue(siteArea$);
  const status = useValue(status$);
  const hinweise = useValue(hinweis$).split("\n").filter((zeile) => zeile !== "");
  const tab = useValue(tab$);
  const polygonClosed = useValue(polygonClosed$);
  const undoAvailable = useValue(undoAvailable$);
  const redoAvailable = useValue(redoAvailable$);
  const ausrichtWahl = useValue(ausrichtWahl$);
  const ausrichtAktiv = useValue(ausrichtAktiv$);
  const trennmodus = useValue(trennmodus$);
  const altbestand = useValue(altbestand$);
  const entranceMode = useValue(entranceMode$);
  const entranceKind = useValue(entranceKind$);
  const entranceMax = useValue(entranceMax$);
  /*
   * Was zum Bauen fehlt, kommt fertig aus dem Werkzeug. Das Panel darf die
   * Regel NICHT nachbauen: dort liegen die Zufahrtsarten nicht, und zwei
   * Fassungen derselben Bedingung laufen frueher oder spaeter auseinander.
   */
  const entranceMissing = useValue(entranceMissing$);
  const darfBauen = entranceMissing === "";
  const fehltText = entranceMissing === "einfahrt" ? t.fehltEinfahrt
    : entranceMissing === "ausfahrt" ? t.fehltAusfahrt
    : t.fehltZugang;
  const entranceCount = useValue(entranceCount$);
  const panelX = useValue(panelX$);
  const panelY = useValue(panelY$);

  /**
   * NACHFRAGE VOR DEM ALTEN RECHENWEG.
   *
   * Am 2026-08-26 ist der Nutzer versehentlich auf "Alt" gelandet - die
   * beiden Knoepfe liegen in der Schiene, also genau dort, wo man das
   * Fenster anfasst - und hat danach lange einen Fehler gesucht, den es
   * ohne diesen Klick nicht gaebe.
   *
   * Der Haken "nicht mehr anzeigen" liegt in den Optionen, nicht hier: er
   * soll das Spiel ueberleben und im ESC-Menue umkehrbar sein.
   */

  /**
   * VERSCHIEBEN.
   *
   * Waehrend gezogen wird, steht die Position in `zug` - also hier oben in
   * der Oberflaeche. Erst beim Loslassen geht sie einmal an den Mod, der
   * sie in die Einstellungsdatei schreibt. Bei jedem Mausschritt zu
   * schreiben waeren 60 Dateizugriffe je Sekunde fuer ein Ergebnis, das
   * erst am Ende feststeht.
   *
   * Gezogen wird ueber eine durchsichtige Flaeche, die nur waehrend des
   * Ziehens existiert, nicht ueber `document.addEventListener`. Zwei
   * Gruende: die Maus verlaesst beim schnellen Ziehen den Griff, und
   * React-Ereignisse auf einem eigenen Element sind in Cohtml belegt -
   * genau so arbeitet der Schieberegler in controls.tsx seit dem ersten
   * Tag. Ein Ereignis am `document` waere geraten.
   */
  const [zug, setZug] = useState<{ x: number; y: number } | null>(null);
  const anker = useRef({ mausX: 0, mausY: 0, x: 0, y: 0 });

  /* ------------------------------------------------------- Abschnitte */
  /**
   * HOCHKANT IST IMMER NUR EIN ABSCHNITT OFFEN.
   *
   * Vier Abschnitte untereinander sind rund 700 rem hoch, der Platz reicht
   * fuer gut 450. Eine Bildlaufleiste war der erste Versuch; sie hat den
   * Platz aber nur verwaltet, statt ihn zu schaffen, und haette nebenbei die
   * nach oben aufklappende Flaechenliste abgeschnitten - Gameface kappt in
   * einem Scrollbereich auch absolut positionierte Kinder.
   *
   * Waagerecht bleibt alles offen: dort stehen sie nebeneinander.
   */
  const [offenerAbschnitt, setOffenerAbschnitt] = useState("Zuschnitt");
  const abschnitt = (name: string) => ({
    einklappbar: hochkant,
    offen: !hochkant || offenerAbschnitt === name,
    onKlick: () => setOffenerAbschnitt(
      offenerAbschnitt === name ? "" : name),
  });

  /* Der Spiel-Tooltip kennt Orange nur als Textfarbe. Die Klasse begrenzt
     den dunkelorangen Hintergrund auf die Zeit, in der unser dauerhafter
     Zufahrts-Hinweis existiert; Debug-Meldungen bleiben unverändert grün. */
  /**
   * HEIMPOSITION: OBEN, WAAGERECHT MITTIG - selbst ausgerechnet.
   *
   * Der Mod schickt nur einen Zaehler. Erst hier sind Leistenbreite und
   * Aufloesung bekannt, und nur aus beiden zusammen ergibt sich eine
   * Position, die nach jeder Aenderung an der Leiste wieder stimmt. Eine
   * feste Zahl im C#-Teil war genau deshalb kaputt, als die Leiste fuer die
   * vierte Spalte breiter wurde.
   *
   * `panelHome === 0` ist der Startwert - dann ist nichts angefordert.
   */
  /**
   * DIE HEIMPOSITION HAENGT AM STIL.
   *
   * Beide Stile weichen dem aus, was CS2 selbst belegt - gemessen in
   * `Content/Game/UI/index.css`: die Kopfleiste ist 55 rem hoch, die
   * Werkzeugleiste `calc(50rem * var(--toolbarScale))`, und CS2s eigenes
   * Werkzeugfenster sitzt auf `left: 12rem, bottom: 12rem`.
   *
   *   vertikal   mittig, knapp ueber der Werkzeugleiste - die obere
   *              Bildschirmhaelfte bleibt frei, und dort wird gezeichnet
   *   hochkant   links, im Slot von CS2s eigenen Werkzeugeinstellungen
   *
   * Laeuft auch beim STILWECHSEL, nicht nur auf Knopfdruck: eine Leiste von
   * 1400 rem, die zur 364er Spalte wird, behielte sonst ihre Ecke - und die
   * liegt fuer eine Spalte an der voellig falschen Stelle.
   */
  useEffect(() => {
    if (panelHome === 0) return;
    const { breite, hoehe } = rahmen();
    const rem = remGroesse();
    const mass = stilmass();
    const eigene = leistenbreite();
    panelDiagnose(
      `Fenster ${window.innerWidth}x${window.innerHeight}, Rahmen ${breite}, `
      + `Stil ${panelStil}, Leiste ${Math.round(eigene)}, gemessen `
      + `${leiste.current?.offsetWidth ?? "nicht moeglich"}`);
    // Hochkant an den linken Rand, mit demselben Abstand, den CS2 fuer
    // sein eigenes Werkzeugfenster haelt (`left: 12rem`).
    const links = hochkant
      ? klemme((12 * rem) / breite)
      : (eigene > 0 ? klemme((breite - eigene) / 2 / breite) : 0);
    /*
     * HOCHKANT GEHT NACH OBEN, NICHT NACH UNTEN.
     *
     * Unten links liegt CS2s eigenes Werkzeugfenster - `.tool-options_Cqd`
     * mit `left: 12rem, bottom: 12rem, width: 364rem`. Genau dort stand
     * unsere Spalte auch, und der Nutzer sah sein Fangfenster nur noch
     * hinter unserem. Dieselbe Breite an derselben Kante, nur von oben:
     * so bleibt der Slot des Spiels frei.
     *
     * Die Leiste bleibt unten - sie ist flach und laesst darunter nur die
     * 62 rem fuer Werkzeugleiste und Abstand.
     */
    /*
     * ZWEI BEDEUTUNGEN VON `y`, je nach Stil:
     *
     *   hochkant   Abstand der OBERKANTE vom oberen Bildrand
     *   waagerecht Abstand der UNTERKANTE vom unteren Bildrand
     *
     * Das klingt nach einer Falle, ist aber das Gegenteil: so braucht
     * keiner der beiden eine Hoehe, die wir nicht messen koennen.
     */
    const oben = hochkant
      ? klemme((55 + 12) * rem / hoehe)
      : klemme(unterrandRem() * rem / hoehe);
    setZug(null);
    setPanelPosition(links, Math.max(0, oben));
    // NICHT auf `panelStil` hoeren: jeder Stil merkt sich seine eigene Ecke,
    // und die kommt fertig aus dem Mod. Stuende der Stil hier mit drin,
    // ueberschriebe jeder Wechsel die gemerkte Position mit der Heimecke.
  }, [panelHome]);

  useEffect(() => {
    document.body.classList.toggle("pltEntranceHintActive", entranceMode);
    return () => document.body.classList.remove("pltEntranceHintActive");
  }, [entranceMode]);

  useEffect(() => {
    // Der Zellenweg besitzt noch keine Winkelsuche. War "Meiste" im alten
    // Alte Spielstaende koennen noch "auto" liefern - den Knopf "Meiste"
    // gibt es nicht mehr, also zurueck auf die Kante.
    if (angleMode === "auto") setAngleMode("edge");
  }, [angleMode]);

  /* Faellt der Umriss, waehrend der Zoning-Reiter offen ist, bliebe eine
     leere Seite stehen. Zurueck aufs Layout - dort ist ohnehin das
     Naechste, was er tun muss. */
  useEffect(() => {
    if (!polygonClosed && tab === "zoning") setTab("layout");
  }, [polygonClosed, tab]);

  /**
   * Wieviel Hoehe unter der Oberkante des Fensters noch frei ist.
   *
   * `y` ist bei Hochkant der Abstand der Oberkante vom oberen Bildrand;
   * darunter bleibt der Rest bis zu dem Streifen, den wir ueber CS2s
   * Hauptleiste frei lassen.
   */
  const platzHochkant = (obenAnteil: number, hoehe: number) =>
    Math.max(MindesthoeheHochkant,
      (1 - obenAnteil) * hoehe - unterrandRem() * remGroesse());

  /**
   * Wie weit das hochkante Fenster nach unten darf.
   *
   * Ohne diese Schranke schrumpft es beim Ziehen mit - und ab einem gewissen
   * Punkt bleibt nur noch die Kopfleiste stehen. Der Nutzer: *"Beim
   * Verschieben des Fensters verschwindet alles unterhalb des Headers."*
   * Das war kein Fehler in der Rechnung, sondern eine fehlende Untergrenze.
   */
  const maxYHochkant = (hoehe: number) => Math.max(0,
    (hoehe - unterrandRem() * remGroesse() - MindesthoeheHochkant) / hoehe);

  const { breite: rahmenBreite, hoehe: rahmenHoehe } = rahmen();
  const maxX = Math.max(0, (rahmenBreite - leistenbreite()) / rahmenBreite);
  const x = klemme(zug ? zug.x : panelX, maxX);
  // Auch ein aelterer gespeicherter Wert darf das Fenster nicht unter die
  // Mindesthoehe druecken.
  const y = klemme(zug ? zug.y : panelY,
    hochkant ? maxYHochkant(rahmenHoehe) : MAX_ANTEIL);

  const zugBeginnen = (event: any) => {
    anker.current = {
      mausX: event.clientX,
      mausY: event.clientY,
      x,
      y: panelY,
    };
    setZug({ x, y: panelY });
  };

  const zugBewegen = (event: any) => {
    if (!zug) return;
    const { breite, hoehe } = rahmen();
    const maxLinks = Math.max(0, (breite - leistenbreite()) / breite);
    setZug({
      x: klemme(
        anker.current.x + (event.clientX - anker.current.mausX) / breite,
        maxLinks,
      ),
      /*
       * Bei der waagerechten Leiste ist `y` der Abstand von UNTEN. Die Maus
       * nach unten zu ziehen muss den Wert also kleiner machen, nicht
       * groesser - sonst laeuft das Fenster der Maus davon.
       */
      y: klemme(anker.current.y + (hochkant ? 1 : -1)
        * (event.clientY - anker.current.mausY) / hoehe,
        hochkant ? maxYHochkant(hoehe) : MAX_ANTEIL),
    });
  };

  const zugBeenden = () => {
    if (!zug) return;
    setPanelPosition(zug.x, zug.y);
    setZug(null);
  };

  /**
   * Oeffnet oder schliesst das Vegetationsfenster.
   *
   * Beim ALLERERSTEN Oeffnen bekommt es seine Stelle: linksbuendig mit dem
   * Panel, darueber. Danach nie wieder - auch nicht, wenn das Panel
   * inzwischen woanders steht. Verschiebt der Nutzer das Fenster selbst,
   * gilt ab da seine Stelle.
   *
   * Die Panelhoehe wird dafuer geschaetzt, wenn Cohtml sie nicht liefert.
   * Das ist hier vertretbar - anders als beim Abstand zur Werkzeugleiste
   * geht es um einen Startpunkt, den man mit einem Zug korrigiert.
   */
  const vegStelle = hochkant ? vegPosHochkant : vegPos;
  const vegStelleSetzen = hochkant ? setVegPosHochkant : setVegPos;

  /**
   * Wo das Vegetationsfenster aufgeht.
   *
   *   waagerecht   ueber dem Panel, linksbuendig, 12 rem Abstand
   *   hochkant     rechts neben dem Panel, 12 rem Abstand, unten buendig
   *
   * GERECHNET WIRD IN BILDSCHIRMKOORDINATEN, nicht ab der Panelecke: das
   * Fenster ist ein Nachbar des Panels, kein Kind. `x: 0` heisst linker
   * Bildrand. Mein erster Versuch rechnete noch ab der Ecke und landete
   * halb ausserhalb des Bildes.
   *
   * `vonUnten` heisst: `y` ist der Abstand der UNTERKANTE vom unteren
   * Bildrand. So braucht das Fenster seine eigene Hoehe nicht zu kennen -
   * die liefert Cohtml ohnehin nicht.
   */
  const stelleVegetation = () => {
    const { breite, hoehe } = rahmen();
    const rem = remGroesse();
    const el = leiste.current;
    const gemessenH = el?.offsetHeight ?? 0;
    const gemessenB = el?.offsetWidth ?? 0;
    const panelhoehe = gemessenH > 0 ? gemessenH : (hochkant ? 700 : 420) * rem;
    const panelbreite = gemessenB > 0 ? gemessenB : leistenbreite();
    const panelLinks = x * breite;
    const panelOben = hochkant ? y * hoehe : hoehe - y * hoehe - panelhoehe;

    const links = hochkant
      ? (panelLinks + panelbreite) / rem + AbstandFenster
      : panelLinks / rem;
    const unten = hochkant
      ? (hoehe - panelOben - panelhoehe) / rem
      : (hoehe - panelOben) / rem + AbstandFenster;

    panelDiagnose(
      `Vegetationsfenster (${hochkant ? "hochkant" : "waagerecht"}): `
      + `Panel oben ${Math.round(panelOben)} px, `
      + `${Math.round(panelbreite)} x ${Math.round(panelhoehe)} px `
      + `(${gemessenH > 0 ? "gemessen" : "geschaetzt"}), `
      + `Fenster links ${Math.round(links)} rem, `
      + `unten ${Math.round(unten)} rem.`);

    vegStelleSetzen({
      // Nie ganz aus dem Bild: bleiben Unterkante und linke Kante drin,
      // bleibt auch der Griff greifbar.
      x: Math.min(Math.max(0, links), Math.max(0, breite / rem - 120)),
      y: Math.min(Math.max(0, unten), hoehe / rem - 40),
    });
  };

  useEffect(() => {
    if (!vegOffen || vegStelle !== null) return;
    /*
     * ZWEI BILDER WARTEN, DANN MESSEN.
     *
     * Gameface legt das Layout einmal je Bild an; was JS liest, ist ein Bild
     * alt. Direkt nach einem Stilwechsel steht in `offsetHeight` deshalb
     * noch die Hoehe des vorigen Stils, und das Fenster stuende schief.
     */
    let weg = false;
    const id = requestAnimationFrame(() => requestAnimationFrame(() => {
      if (!weg) stelleVegetation();
    }));
    return () => { weg = true; cancelAnimationFrame(id); };
  }, [vegOffen, hochkant, vegStelle]);

  const oeffneVegetation = (an: boolean) => { setVegOffen(an); };

  /** Verhindert, dass ein Knopf in der Schiene die Leiste mitzieht. */
  const haltAn = (event: any) => event.stopPropagation();

  if (!open || !toolActive) return null;

  const melden = tab === "report";
  const debug = tab === "debug";
  /* ZONING BRAUCHT EINEN GESCHLOSSENEN UMRISS. Die Parzellen liegen IM
     Parkplatz; ohne Umriss gaebe es kein Innen. Steht der Nutzer auf dem
     Reiter, wenn der Umriss faellt, holt ihn der Effekt weiter unten
     zurueck - sonst saehe er eine leere Seite ohne zu wissen warum. */
  const zoning = tab === "zoning" && polygonClosed;
  const liste = tab === "liste";

  /** Der Name des Mods - hochkant in einer Zeile mit den beiden Pfeilen. */
  const marke = (
    <div className={styles.marke}>
      <span className={styles.markeStrich} />
      <span className={styles.markeText}>{t.titel}</span>
    </div>
  );

  /**
   * Rueckgaengig und Wiederherstellen.
   *
   * Sie stehen an zwei verschiedenen Stellen, je nach Stil - waagerecht bei
   * den Fensterknoepfen, hochkant oben in der Reiterzeile -, aber es ist
   * derselbe Knopf. Deshalb steht er einmal hier und nicht zweimal im
   * Aufbau.
   */
  const undoRedo = (
    <>
      <TooltipKnopf text={t.tooltipRueckgaengig}
        className={`${styles.iconButton}
          ${undoAvailable ? "" : styles.iconButtonAus}`}
        aria-disabled={!undoAvailable}
        onMouseDown={haltAn}
        onClick={undo}
      >
        <span className={styles.undoSymbol}>↶</span>
      </TooltipKnopf>
      <TooltipKnopf text={t.tooltipWiederherstellen}
        className={`${styles.iconButton}
          ${redoAvailable ? "" : styles.iconButtonAus}`}
        aria-disabled={!redoAvailable}
        onMouseDown={haltAn}
        onClick={redo}
      >
        <span className={styles.undoSymbol}>↷</span>
      </TooltipKnopf>
    </>
  );

  return (
    <>
    <div
      ref={leiste}
      className={`${styles.panel} ${
        hochkant ? styles.stilHochkant : styles.stilHorizontal}`}
      style={hochkant
        ? { left: `${x * 100}%`, top: `${y * 100}%` }
        : { left: `${x * 100}%`, bottom: `${y * 100}%` }}
    >
      {/*
        DIE HOECHSTHOEHE SITZT HIER, NICHT AM PANEL.
        Am Panel hat sie den Inhalt zerlegt: das Panel waere dafuer ein
        Flex-Kasten geworden, und als dessen Kind rechnet `.reihe` nicht mehr
        nach Max-Content - `.inhalt` fiel auf seine Polsterung zusammen.

        An `.reihe` gilt beides: kurzer Inhalt laesst das Fenster
        schrumpfen, langer wird begrenzt, und erst dann geben `.inhalt` und
        die Spaltengruppe ihre Hoehe an die Bildlaufleiste weiter.

        AUSGERECHNET IN PIXELN, NICHT ALS `calc`: Cohtml mischt in `calc`
        keine Einheiten zuverlaessig (vh mit rem ist derselbe Fall wie
        Prozent mit rem, siehe cs2-ui-cohtml).
      */}
      <div className={styles.reihe}
           style={hochkant
             ? { maxHeight: `${platzHochkant(y, rahmenHoehe)}px` }
             : undefined}>

        {/* Die Schiene ist zugleich der Griff zum Verschieben. Reiter und
            Knoepfe darin halten das Mausereignis selbst auf, sonst wandert
            die Leiste bei jedem Klick ein Stueck mit. */}
        <div className={styles.rail} onMouseDown={zugBeginnen}
             title={t.titelZiehen}>
          {marke}
          {/*
            DER RECHENWEG GEHOERT NICHT IN DIE LAYOUT-SPALTE.
            Er stellt nicht EINE Eigenschaft des Parkplatzes ein, sondern
            entscheidet, WOMIT gerechnet wird - eine Ebene ueber allem
            anderen. Deshalb steht er jetzt in der Schiene unter dem Namen
            des Mods, nicht zwischen Randabstand und Winkel.
          */}
          {/* Der einzige Reiter ohne Tooltip - bis zum 2026-09-16. Die
              vier anderen hatten laengst einen; hier fehlte er, weil der
              Reiter als erster entstand und der `TooltipKnopf` spaeter
              dazukam. */}
          {/*
            EINE ZEILE FUER DIE REITER.
            Hochkant sitzen Rueckgaengig und Wiederherstellen rechts darin;
            waagerecht ist die Zeile nur eine Huelle, die nichts aendert.
          */}
          <div className={styles.reiterZeile}>
          <TooltipKnopf
            text={t.tooltipEntwurf}
            className={`${styles.tab} ${melden || debug || zoning || liste ? "" : styles.tabAktiv}`}
            onMouseDown={haltAn}
            onClick={() => setTab("layout")}
          >
            {t.reiterEntwurf}
          </TooltipKnopf>
          {/* ZONING steht VOR Debug: es ist eine Bau-Funktion und gehoert
              neben das Layout, nicht neben die Messwerkzeuge. */}
          {/* `haltAn` IST HIER PFLICHT, nicht Zierde: die Reiterschiene ist
              zugleich der Griff zum Verschieben des Panels. Ohne das
              Abfangen beginnt der Druck auf den Knopf einen Panelzug, und
              der verschluckt den Klick - der Reiter sah aktiv aus und tat
              nichts. Genau das hat der Nutzer gemeldet. Alle drei anderen
              Reiter hatten es, meiner nicht.

              Und `title` statt Tooltip war die zweite Haelfte desselben
              Fehlers: in Cohtml zeigt `title` nichts an, dafuer gibt es
              `MitTooltip`. Der Hinweis, WARUM der Reiter grau ist, waere
              also unsichtbar geblieben. */}
          <TooltipKnopf
            text={polygonClosed ? t.tooltipZoningReiter
              : t.tooltipZoningGesperrt}
            className={`${styles.tab} ${zoning ? styles.tabAktiv : ""}
              ${polygonClosed ? "" : styles.tabAus}`}
            aria-disabled={!polygonClosed}
            onMouseDown={haltAn}
            onClick={() => { if (polygonClosed) setTab("zoning"); }}
          >
            {t.zoningReiter}
          </TooltipKnopf>
          {/* Die Liste zeigt das FERTIGE. Sie steht deshalb hinter den
              Bau-Reitern und vor den Messwerkzeugen. */}
          <TooltipKnopf text={t.tooltipListe}
            className={`${styles.tab} ${liste ? styles.tabAktiv : ""}`}
            onMouseDown={haltAn}
            onClick={() => setTab("liste")}
          >
            {t.reiterListe}
          </TooltipKnopf>
          {/* MESSEN STATT BAUEN. Der Dev-Debug-Reiter fasst Werkzeuge
              zusammen, die CS2 etwas fragen, statt einen Parkplatz zu
              erzeugen.

              ER IST STANDARDMAESSIG AUS. Diese Werkzeuge sind ueber Monate an
              einzelnen Faellen gewachsen, erklaeren sich nicht von selbst und
              greifen teils in die Stadt ein. Vor der Testveroeffentlichung
              gehoert in die Hand eines Nutzers der Reiter "Fehler melden" -
              dort erzeugt er seinen Bericht. Einschalten laesst sich dieser
              hier in den Mod-Einstellungen. */}
          {entwicklerDebug ? <TooltipKnopf text={t.tooltipDebug}
            className={`${styles.tab} ${debug ? styles.tabAktiv : ""}`}
            onMouseDown={haltAn}
            onClick={() => setTab("debug")}
          >
            {t.reiterDebug}
          </TooltipKnopf> : null}
          <TooltipKnopf text={t.tooltipMelden}
            className={`${styles.tab} ${styles.tabMelden}
              ${melden ? styles.tabMeldenAktiv : ""}`}
            onMouseDown={haltAn}
            onClick={() => setTab("report")}
          >
            {t.reiterMelden}
          </TooltipKnopf>
          {/*
            HOCHKANT STEHEN DIE BEIDEN PFEILE HIER, rechts neben den
            Reitern. Beim ersten Anlauf passten sie nicht - die Reiter
            liessen 43 rem frei, zwei Knoepfe brauchten 52, und der zweite
            ist umgebrochen. Statt sie woanders hinzuschieben, ist jetzt
            Platz gemacht: engere Reiter und kleinere Knoepfe in dieser
            Zeile, zusammen rund 30 rem Luft.
          */}
          {hochkant ? <>
            <span className={styles.kopfFueller} />
            {undoRedo}
          </> : null}
          </div>
          {/* Die Fangschalter des Spiels, gleich links neben den
              Fensterknoepfen. Sie zeigen und setzen denselben Zustand wie
              CS2s eigenes Werkzeugfenster. */}
          {/*
            ALLES RECHTS IN EINER GRUPPE.
            Vorher schob jeder der beiden Bloecke sich einzeln nach rechts.
            Das haelt nur, solange die Kopfzeile nicht umbricht - danach
            faengt die zweite Zeile wieder links an. Eine gemeinsame Gruppe
            bleibt rechtsbuendig, egal wieviele Reiter davor stehen.
          */}
          {/*
            EIN FUELLER STATT `margin-left: auto`.
            Die Auto-Marge hat nicht geschoben - die Knoepfe klebten hinter
            den Reitern statt am rechten Rand. Nachgezaehlt in `index.css`:
            CS2 benutzt `margin-left:auto` 3-mal, `justify-content:flex-end`
            dagegen 77-mal und `space-between` 48-mal. Was das Spiel selbst
            meidet, kann Cohtml offenbar nicht verlaesslich - und ein
            wachsender Zwischenraum ist die Form, die ueberall im Mod schon
            traegt.
          */}
          <span className={styles.kopfFueller} />
          <div className={styles.kopfRechts}>
          <div className={styles.kopfKnoepfe}>
            {hochkant ? null : undoRedo}
            {/*
              DER STILUMSCHALTER STEHT BEI DEN FENSTERKNOEPFEN, nicht bei den
              Einstellungen: er aendert nichts am Parkplatz, sondern nur, wie
              dieses Fenster aussieht - dieselbe Ebene wie Schliessen.

              Beschriftet ist er mit dem Stil, in den er wechselt. Ein
              Symbol waere hier schlechter: fuer "schmale Spalte gegen breite
              Leiste" gibt es unter CS2s Standardsymbolen keines, und ein
              geratener Name waere im Spiel ein kaputtes Bild.
            */}
            {/* Der Stilknopf gehoert nicht zu Rueckgaengig/Wiederherstellen -
                er aendert nichts am Parkplatz, sondern am Fenster. */}
            {hochkant ? null : <span className={styles.kopfTrenner} />}
            <TooltipKnopf text={t.tooltipStil}
              className={`${styles.iconButton} ${styles.stilKnopf}`}
              onMouseDown={haltAn}
              onClick={() => setPanelStil(hochkant ? "horizontal" : "hochkant")}
            >
              {/* Das Symbol zeigt, WOHIN es geht: zwei Pfeile auseinander
                  in der Richtung, in der der andere Stil laeuft. */}
              <span className={styles.stilPfeile}>
                <img alt="" src={hochkant
                  ? "Media/Glyphs/ThickStrokeArrowLeft.svg"
                  : "Media/Glyphs/ThickStrokeArrowUp.svg"} />
                <img alt="" src={hochkant
                  ? "Media/Glyphs/ThickStrokeArrowRight.svg"
                  : "Media/Glyphs/ThickStrokeArrowDown.svg"} />
              </span>
            </TooltipKnopf>
            {[
              { text: t.tooltipReset, icon: "Reset", action: resetAll },
              /*
               * HIER STAND DER PAPIERKORB.
               *
               * Er warf die gemerkten Standardwerte weg - eine Funktion,
               * die der Nutzer nie bestellt hatte und die neben
               * "Zuruecksetzen" ohnehin schwer zu unterscheiden war.
               * Denselben Zweck erfuellt "Alle Einstellungen zuruecksetzen"
               * auf der Optionsseite.
               *
               * An seiner Stelle das, was man im Panel wirklich braucht:
               * das Fenster zurueck an seinen Platz. Bisher ging das nur
               * ueber ESC und die Modoptionen - also ausgerechnet dann
               * umstaendlich, wenn man das Fenster aus dem Bild geschoben
               * hat.
               */
              { text: t.tooltipFensterHeim,
                icon: "Media/Glyphs/ArrowCircular.svg", action: fensterHeim },
              { text: t.tooltipSchliessen, icon: "XClose", action: togglePanel },
            ].map((knopf) => (
              <TooltipKnopf key={knopf.icon} text={knopf.text}
                className={styles.iconButton}
                onMouseDown={haltAn}
                onClick={knopf.action}
              >
                {/*
                  Glyphen des Spiels als MASKE, nicht als Bild - sonst
                  kommt ihre eigene Farbe mit. Die Symbolbibliothek
                  (`coui://uil/...`) ist bereits weiss und bleibt ein Bild.
                */}
                {knopf.icon.indexOf("/") >= 0 ? (
                  <span className={styles.kopfGlyph}
                        style={{ maskImage: `url(${knopf.icon})` }} />
                ) : (
                  <img src={icon(knopf.icon)} />
                )}
              </TooltipKnopf>
            ))}
          </div>
          {/*
            DIE FANGSCHALTER GANZ NACH RECHTS.
            Zwischen Reitern und Fensterknoepfen sahen sie aus, als gehoerten
            sie zu beidem - "mitten zwischen den anderen Knoepfen", wie der
            Nutzer es genannt hat. Sie sind eine eigene Sache und stehen
            deshalb fuer sich am Rand.
          */}
          <Fangschalter />
          </div>
        </div>

        <div className={styles.trenner} />

        <div className={styles.inhalt}>
        {/* Aus heisst aus: wer den Schalter umlegt, waehrend der Reiter
            offen ist, soll nicht auf einem unsichtbaren Reiter sitzenbleiben. */}
        {melden ? <ReportTab /> : debug && entwicklerDebug ? <DebugTab />
          : liste ? <ListeTab />
          : zoning ? <ZoningTab /> : <>
        <div className={styles.spaltenGruppe}>
        <Spalte title={t.zuschnitt} ton="Zuschnitt" {...abschnitt("Zuschnitt")} titleTooltip={t.tooltipZuschnitt}>
          <Slider
            label={t.randabstand}
            tooltip={t.tooltipRandabstand}
            value={edgeSetback}
            min={0} max={6} step={0.5}
            ton="Zuschnitt"
            onChange={setEdgeSetback}
            differsFromDefault={numberDiffers(edgeSetback, edgeSetbackDefault)}
            onReset={() => resetOne("EdgeSetback")}
            onSetDefault={() => setAsDefault("EdgeSetback")}
          />
          <ModeChooser
            label={t.reihenwinkel}
            tooltip={t.tooltipReihenwinkel}
            /* Waehrend der Linienauswahl ist KEIN Modus hervorgehoben - sonst
               leuchten zwei Knoepfe gleichzeitig, und man sieht nicht, was
               gerade gilt. Der Nutzer: "enorm verwirrend, wenn mehrere Buttons
               highlighted sind". */
            value={ausrichtWahl ? "" : angleMode}
            ton="Zuschnitt"
            options={[
              /* Mit gewaehlter Bezugslinie heisst "Kante" nicht mehr
                 "laengste Kante", sondern "entlang der Linie" - deshalb der
                 andere Name. "Fest" faellt dann weg: die beiden Modi sind
                 entweder/oder. */
              {
                id: "edge",
                text: ausrichtAktiv ? t.winkelNormal : t.winkelKante,
                tooltip: ausrichtAktiv
                  ? t.tooltipWinkelNormal : t.tooltipWinkelKante,
              },
              /* "Meiste" probierte 36 Winkel durch. Ersetzt durch "Quer" -
                 senkrecht zur Bezugsrichtung, ohne den Regler zu bemuehen. */
              { id: "quer", text: t.winkelQuer, tooltip: t.tooltipWinkelQuer },
              /* "Fest" bleibt immer sichtbar. Es ist der einzige echte
                 Gegen-Modus zur Ausrichtung: wer ihn waehlt, verwirft die
                 Linie (siehe SetAngleMode in ParkingLotUISystem). "Normal"
                 und "Quer" sind dagegen nur die 0- und 90-Grad-Seite
                 desselben Bezugs und lassen die Linie stehen. */
              { id: "fixed", text: t.winkelFest, tooltip: t.tooltipWinkelFest },
            ]}
            onChange={setAngleMode}
            differsFromDefault={angleMode !== angleModeDefault}
            onReset={() => resetOne("AngleMode")}
            onSetDefault={() => setAsDefault("AngleMode")}
            raster
            /* Der vierte Knopf sitzt IN der Kachelreihe, damit aus drei
               Modi plus Ausrichten ein 2x2 wird. Er ist kein Modus, sondern
               startet die Auswahl - deshalb kein `id`, sondern ein eigener
               Knopf mit demselben Kachelstil. */
            /* IM TRENNMODUS IST ES EIN ANDERER KNOPF. Ansage des
               Nutzers: "align button ändert sich zu teilflächen
               bestätigen". Derselbe Platz, anderer Name, andere Wirkung -
               so bleibt die Kachelreihe ein 2x2 und man sucht nichts. */
            extra={
              <TooltipKnopf
                text={trennmodus ? t.tooltipTrennungFertig : t.tooltipAusrichten}
                className={`${styles.modeKachel}
                  ${ausrichtWahl ? styles.modeAktivZuschnitt : ""}`}
                onMouseDown={haltAn}
                onClick={trennmodus ? trennungFertig : ausrichtWaehlen}
              >
                {trennmodus ? t.trennungFertig : t.ausrichten}
              </TooltipKnopf>
            }
            /* Eine Zeile darunter, und nur wenn es etwas zu tun gibt. Der
               Nutzer wollte hier ausdruecklich keine kleinen Symbolknoepfe.

               ZWEI KNOEPFE, die zu verschiedenen Zeiten auftauchen:
               "Zuruecksetzen", sobald eine Linie gilt, und "Fertig", solange
               gewaehlt wird. Bis zum 2026-09-01 kam man aus dem Waehlen nur
               per Rechtsklick oder Esc heraus - das muss man erst einmal
               wissen. Steht nur einer da, nimmt er die ganze Breite; stehen
               beide, teilen sie sich die Zeile. */
            unten={ausrichtAktiv || ausrichtWahl ? (
              <div className={styles.ausrichtReihe}>
                {ausrichtAktiv ? (
                  <TooltipKnopf text={t.tooltipAusrichtenZurueck}
                    className={styles.ausrichtKnopf}
                    onMouseDown={haltAn}
                    onClick={ausrichtZuruecksetzen}
                  >
                    {t.ausrichtenZurueck}
                  </TooltipKnopf>
                ) : null}
                {ausrichtWahl ? (
                  <TooltipKnopf text={t.tooltipAusrichtenFertig}
                    className={`${styles.ausrichtKnopf} ${styles.ausrichtFertig}`}
                    onMouseDown={haltAn}
                    onClick={ausrichtBestaetigen}
                  >
                    {t.ausrichtenFertig}
                  </TooltipKnopf>
                ) : null}
              </div>
            ) : null}
          />
          <Slider
            label={t.winkel}
            tooltip={t.tooltipWinkel}
            value={rowAngle}
            min={0} max={175} step={5}
            ton="Zuschnitt"
            unit={t.einheitGrad} digits={0}
            disabled={angleMode !== "fixed"}
            onChange={setRowAngle}
            differsFromDefault={numberDiffers(rowAngle, rowAngleDefault)}
            onReset={() => resetOne("RowAngle")}
            onSetDefault={() => setAsDefault("RowAngle")}
          />
        </Spalte>

        <Spalte title={t.fahrwege} ton="Fahrwege" {...abschnitt("Fahrwege")} titleTooltip={t.tooltipFahrwege}>
          <Slider
            label={t.fahrgassenbreite}
            tooltip={t.tooltipFahrgassenbreite}
            value={aisleWidth}
            min={3} max={12} step={0.5}
            ton="Fahrwege"
            onChange={setAisleWidth}
            differsFromDefault={numberDiffers(aisleWidth, aisleWidthDefault)}
            onReset={() => resetOne("AisleWidth")}
            onSetDefault={() => setAsDefault("AisleWidth")}
          />
          <Slider
            label={t.querstrassenbreite}
            tooltip={t.tooltipQuerstrassenbreite}
            value={crossWidth}
            min={3} max={9} step={0.5}
            ton="Fahrwege"
            onChange={setCrossWidth}
            differsFromDefault={numberDiffers(crossWidth, crossWidthDefault)}
            onReset={() => resetOne("CrossWidth")}
            onSetDefault={() => setAsDefault("CrossWidth")}
          />
          {/*
            UNTERGRENZE 5, NICHT 3.
            Eine Buchtreihe braucht laut Regel mindestens 5 Buchten am
            Stueck; passen weniger in einen Abschnitt, faellt das
            Querstrassenstueck dort weg. Bei N=3 oder N=4 hat JEDER
            Abschnitt zu wenige - gemessen auf 300 x 105 m: null
            Querstrassen. Der Regler bot damit zwei Werte an, die nie
            eine Verbindung erzeugen koennen.
          */}
          <Slider
            label={t.verbindungAlle}
            tooltip={t.tooltipVerbindungAlle}
            value={crossBays}
            min={5} max={30} step={1}
            ton="Fahrwege"
            unit={t.einheitBuchten}
            digits={0}
            onChange={setCrossBays}
            differsFromDefault={numberDiffers(crossBays, crossBaysDefault)}
            onReset={() => resetOne("CrossBays")}
            onSetDefault={() => setAsDefault("CrossBays")}
          />
        </Spalte>

        <Spalte title={t.gruen} ton="Gruen" {...abschnitt("Gruen")} titleTooltip={t.tooltipGruen}>
          <Toggle
            label={t.mittelgruen}
            tooltip={t.tooltipMittelgruen}
            value={greenMedian}
            onChange={setGreenMedian}
            differsFromDefault={greenMedian !== greenMedianDefault}
            onReset={() => resetOne("GreenMedian")}
            onSetDefault={() => setAsDefault("GreenMedian")}
          />
          <Toggle
            label={t.kappen}
            tooltip={t.tooltipKappen}
            value={crossCaps}
            onChange={setCrossCaps}
            differsFromDefault={crossCaps !== crossCapsDefault}
            onReset={() => resetOne("CrossCaps")}
            onSetDefault={() => setAsDefault("CrossCaps")}
          />
          <Toggle
            label={t.randstrassen}
            tooltip={t.tooltipRandstrassen}
            value={randstrassen}
            onChange={setRandstrassen}
            differsFromDefault={randstrassen !== randstrassenDefault}
            onReset={() => resetOne("Randstrassen")}
            onSetDefault={() => setAsDefault("Randstrassen")}
          />
          <Slider
            label={t.gruenstreifentiefe}
            tooltip={t.tooltipGruenstreifentiefe}
            value={medianWidth}
            min={0} max={8} step={0.5}
            ton="Gruen"
            disabled={!greenMedian}
            onChange={setMedianWidth}
            differsFromDefault={numberDiffers(medianWidth, medianWidthDefault)}
            onReset={() => resetOne("MedianWidth")}
            onSetDefault={() => setAsDefault("MedianWidth")}
          />
          <VegetationSchalter onOeffnen={oeffneVegetation} />
        </Spalte>

        <Spalte title={t.spalteFlaechen} ton="Zufahrt" {...abschnitt("Flaechen")} titleTooltip={t.tooltipFlaechen}>
          <Auswahl
            label={t.flaecheStrasse}
            tooltip={t.tooltipFlaecheStrasse}
            value={surfaceRoad}
            options={surfaceList}
            ton="Zufahrt"
            onChange={setSurfaceRoad}
            differsFromDefault={surfaceRoad !== surfaceRoadDefault}
            onReset={() => resetOne("SurfaceRoad")}
            onSetDefault={() => setAsDefault("SurfaceRoad")}
            kopfSchalter={{
              label: t.flaecheStrasseAn,
              tooltip: t.tooltipFlaecheStrasseAn,
              value: surfaceRoadOn,
              onChange: setSurfaceRoadOn,
              differsFromDefault: surfaceRoadOn !== surfaceRoadOnDefault,
              onReset: () => resetOne("SurfaceRoadOn"),
              onSetDefault: () => setAsDefault("SurfaceRoadOn"),
            }}
          />
          <Auswahl
            label={t.flaecheDeko}
            tooltip={t.tooltipFlaecheDeko}
            value={surfaceDecoration}
            options={surfaceList}
            ton="Zufahrt"
            onChange={setSurfaceDecoration}
            differsFromDefault={surfaceDecoration !== surfaceDecorationDefault}
            onReset={() => resetOne("SurfaceDecoration")}
            onSetDefault={() => setAsDefault("SurfaceDecoration")}
            kopfSchalter={{
              label: t.flaecheDekoAn,
              tooltip: t.tooltipFlaecheDekoAn,
              value: surfaceDecorationOn,
              onChange: setSurfaceDecorationOn,
              differsFromDefault:
                surfaceDecorationOn !== surfaceDecorationOnDefault,
              onReset: () => resetOne("SurfaceDecorationOn"),
              onSetDefault: () => setAsDefault("SurfaceDecorationOn"),
            }}
          />
          {/*
            DIE DISKETTE MUSS AUCH ETWAS TUN.
            Bis zum 2026-08-24 rief dieser Schalter `setAsDefault` gar nicht
            auf, sondern setzte seinen eigenen Wert noch einmal - ein
            Knopf ohne Wirkung. Nach jedem Neustart standen sie wieder auf
            "an", obwohl man sie gespeichert zu haben glaubte.
          */}
          <Toggle
            label={t.vorflaeche}
            tooltip={t.tooltipVorflaeche}
            value={surfaceApronOn}
            onChange={setSurfaceApronOn}
            differsFromDefault={surfaceApronOn !== surfaceApronOnDefault}
            onReset={() => resetOne("SurfaceApronOn")}
            onSetDefault={() => setAsDefault("SurfaceApronOn")}
          />
          <Toggle
            label={t.buchtsymbole}
            tooltip={t.tooltipBuchtsymbole}
            value={bayIcons}
            onChange={setBayIcons}
            differsFromDefault={bayIcons !== bayIconsDefault}
            onReset={() => resetOne("BayIcons")}
            onSetDefault={() => setAsDefault("BayIcons")}
          />
        </Spalte>

        </div>

        <div className={styles.abschluss}>

        {/* Das Ergebnis zuerst, und die Stellplatzzahl gross: das ist die
            Zahl, wegen der man die Leiste ueberhaupt offen hat. Vorher stand
            sie als eine von sechs gleich grossen Kennzahlen ganz unten. */}
        <div className={styles.ergebnis}>
          <div className={styles.ergebnisKopf}>
            <span className={styles.ergebnisZahl}>{stalls}</span>
            <span className={styles.ergebnisName}>{t.stellplaetze}</span>
          </div>
          {/*
            JEDE ZEILE EIN EINZIGES TEXTSTUECK.
            Vorher stand hier `{perimeterStalls} am Rand · {aisles} Fahrgassen` -
            fuer React sind das VIER Textknoten, und Cohtml zieht
            `white-space: nowrap` nicht ueber getrennte Textknoten hinweg. Im
            Spiel wurde daraus "106 / am Rand · / 3 / Fahrgassen", also vier
            Zeilen. "Stellplaetze" und "Enter" daneben sind je ein einzelnes
            Stueck und brachen deshalb nie um - daran war es zu erkennen.
          */}
          <div className={styles.ergebnisZeile}>
            {t.amRandGassen(perimeterStalls, aisles)}
          </div>
          <div className={styles.ergebnisZeile}>
            {t.jeBucht(areaPerStall)}
          </div>
          <div className={styles.ergebnisZeile}>
            {t.winkelAreal(rowAngleResult, siteArea)}
          </div>
          <div className={styles.statusZeile}>
            <span className={styles.statusPunkt} />
            <span className={styles.statusText}>{status}</span>
          </div>
          {/*
            HINWEISE DES RECHENWEGS.

            Kann ein Rechenweg eine Einstellung nicht, rechnet er trotzdem -
            nur eben etwas anderes. Bis zum 2026-08-21 stand das nur im
            Debug-Abzug; im Spiel sah es aus wie ein normales Ergebnis. Eine
            stille Luecke ist schlimmer als eine Fehlermeldung.

            Jede Zeile ist EIN Textknoten - Cohtml zieht `white-space` nicht
            ueber getrennte Knoten hinweg, daraus wurden im Spiel schon
            einmal vier Zeilen aus einer.
          */}
          {hinweise.map((zeile, i) => (
            <div key={i} className={styles.hinweisZeile}>{t.hinweis(zeile)}</div>
          ))}
        </div>

        <div className={styles.aktionSpalte}>
        {/*
          EINE ART, EIN KNOPF - und die Farbe ist die Zuordnung.
          Jeder Knopf traegt denselben Farbton wie sein Ring in der Vorschau,
          damit man ohne Beschriftung sieht, was man gerade gesetzt hat.
          Ansage des Nutzers am 2026-08-27.

          Der aktive Knopf ist zugleich der Rueckweg: nochmal darauf schaltet
          den Setzmodus aus, man ist wieder am Polygon. Deshalb braucht es
          keinen getrennten "Polygon bearbeiten"-Knopf mehr.

          Die Obergrenze steht EINMAL daneben statt in jedem Knopf - sonst
          liest man sie viermal und aendert sie beim naechsten Mal dreimal
          nicht mit.
        */}
        <div className={styles.artZeile}>
          {ARTEN.map((art) => {
            const aktiv = entranceMode && entranceKind === art.id;
            return (
              <div key={art.id} className={styles.artZelle}>
               <TooltipKnopf text={t[art.tooltip]}
                  className={`${styles.artKnopf} ${styles[art.ton]}
                    ${aktiv ? styles.artAktiv : ""}
                    ${polygonClosed ? "" : styles.aktionAus}`}
                  aria-disabled={!polygonClosed}
                  onClick={() => { if (polygonClosed) setEntranceKind(art.id); }}
              >
                  <img src={icon(art.icon)} />
                  <span className={styles.artName}>{t[art.name]}</span>
              </TooltipKnopf>
              </div>
            );
          })}
          </div>
        <div className={styles.artZaehler}>
          {`${entranceCount} / ${entranceMax}`}
        </div>
        {/*
          DER GRUND STEHT DA, WO MAN IHN BRAUCHT.
          Ein ausgegrauter Bauen-Knopf sagt nur, DASS es nicht geht. Wer zwei
          Einfahrten gesetzt hat, sieht ohne diesen Satz nicht, dass ihm die
          Ausfahrt fehlt - und sucht den Fehler beim Polygon.
        */}
        {polygonClosed && !darfBauen ? (
          <div className={styles.zugangFehlt}>{fehltText}</div>
        ) : null}

        <TooltipKnopf text={t.tooltipBauen}
            className={`${styles.aktion} ${styles.aktionBauen}
              ${polygonClosed && darfBauen ? "" : styles.aktionAus}`}
            aria-disabled={!polygonClosed || entranceCount === 0}
            onClick={() => { if (polygonClosed && darfBauen) buildNow(); }}
        >
            <img src={icon("Checkmark")} />
            <div className={styles.aktionText}>
              <div className={styles.aktionName}>{t.bauen}</div>
              <div className={styles.aktionNebentext}>Enter</div>
            </div>
        </TooltipKnopf>
        </div>
        </div>
        </>}

      </div>
    </div>
    </div>

    {/* Nach dem Laden: Parkplaetze aus dem ausgebauten Rechenweg.
        Dieselbe Form wie die frueheren Nachfragen - liegt ueber allem und
        faengt Klicks daneben ab, damit man beim Wegklicken nicht
        versehentlich einen Regler darunter verstellt. Ohne Antwort wird
        NICHTS geloescht. */}
    {altbestand > 0 ? (
      <div className={styles.frageGrund} onMouseDown={haltAn}>
        <div className={styles.frage}>
          <div className={styles.frageTitel}>
            {t.altbestandTitel} ({altbestand})
          </div>
          <div className={styles.frageText}>{t.altbestandText1}</div>
          <div className={styles.frageText}>{t.altbestandText2}</div>
          <div className={styles.frageKnoepfe}>
            <button
              className={styles.frageKnopfNein}
              onClick={altbestandBehalten}
            >
              {t.altbestandBehalten}
            </button>
            <button
              className={styles.frageKnopfJa}
              onClick={altbestandLoeschen}
            >
              {t.altbestandLoeschen}
            </button>
          </div>
        </div>
      </div>
    ) : null}

    {/* Nur waehrend des Ziehens: faengt die Maus im ganzen Bild ein, damit
        sie den Griff nicht abhaengen kann. */}
    {/*
      NACHBAR DES PANELS, NICHT KIND: so bleibt es liegen, wenn das Panel
      verschoben wird. Beide haengen am selben positionierten Vorfahren.
    */}
    {vegOffen ? (
      <VegetationFenster
        pos={vegStelle ?? undefined}
        onPos={vegStelleSetzen}
        onClose={() => setVegOffen(false)}
      />
    ) : null}
    {zug ? (
      <div
        className={styles.dragLayer}
        onMouseMove={zugBewegen}
        onMouseUp={zugBeenden}
        onMouseLeave={zugBeenden}
      />
    ) : null}
    </>
  );
};
