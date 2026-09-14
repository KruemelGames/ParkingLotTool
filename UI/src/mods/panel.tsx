import { Vegetation } from "./vegetation";
import { randstrassen$, randstrassenDefault$, setRandstrassen } from "./bindings";
import { useValue } from "cs2/api";
import { useEffect, useRef, useState } from "react";
import styles from "./panel.module.scss";
import {
  Auswahl, Flaeche, ModeChooser, Slider, Spalte, Toggle, TooltipKnopf,
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
  resetAll, resetOne, rowAngle$, rowAngleDefault$, rowAngleResult$,
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
 * Die vier Zufahrtsarten. Reihenfolge und Nummern sind die des
 * C#-Aufzaehlungstyps `Zufahrtsart` - beide nur gemeinsam aendern.
 *
 * `ton` ist der Farbton, den auch der Ring in der Vorschau traegt.
 */
const ARTEN = [
  { id: 0, ton: "artZufahrt",  icon: "Road",           tooltip: "tooltipZufahrt",  name: "artNameZufahrt"  },
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
  const leistenbreite = () => {
    const gemessen = leiste.current?.offsetWidth ?? 0;
    if (gemessen > 0) return gemessen;
    const b = window.innerWidth || 1920;
    const h = window.innerHeight || 1080;
    const rem = h >= b * 0.5625 ? b / 1920 : h / 1080;
    return Math.min(1680 * rem, b * 0.88);
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
  useEffect(() => {
    if (panelHome === 0) return;
    const { breite } = rahmen();
    const eigene = leistenbreite();
    panelDiagnose(
      `Fenster ${window.innerWidth}x${window.innerHeight}, Rahmen ${breite}, `
      + `Leiste ${Math.round(eigene)}, gemessen `
      + `${leiste.current?.offsetWidth ?? "nicht moeglich"}`);
    const links = eigene > 0 ? klemme((breite - eigene) / 2 / breite) : 0;
    setZug(null);
    setPanelPosition(links, 0);
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

  const { breite: rahmenBreite } = rahmen();
  const maxX = Math.max(0, (rahmenBreite - leistenbreite()) / rahmenBreite);
  const x = klemme(zug ? zug.x : panelX, maxX);
  const y = zug ? zug.y : panelY;

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
      y: klemme(anker.current.y + (event.clientY - anker.current.mausY) / hoehe),
    });
  };

  const zugBeenden = () => {
    if (!zug) return;
    setPanelPosition(zug.x, zug.y);
    setZug(null);
  };

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

  return (
    <>
    <div
      ref={leiste}
      className={styles.panel}
      style={{ left: `${x * 100}%`, top: `${y * 100}%` }}
    >
      <div className={styles.reihe}>

        {/* Die Schiene ist zugleich der Griff zum Verschieben. Reiter und
            Knoepfe darin halten das Mausereignis selbst auf, sonst wandert
            die Leiste bei jedem Klick ein Stueck mit. */}
        <div className={styles.rail} onMouseDown={zugBeginnen}
             title={t.titelZiehen}>
          <div className={styles.marke}>
            <span className={styles.markeStrich} />
            <span className={styles.markeText}>{t.titel}</span>
          </div>
          {/*
            DER RECHENWEG GEHOERT NICHT IN DIE LAYOUT-SPALTE.
            Er stellt nicht EINE Eigenschaft des Parkplatzes ein, sondern
            entscheidet, WOMIT gerechnet wird - eine Ebene ueber allem
            anderen. Deshalb steht er jetzt in der Schiene unter dem Namen
            des Mods, nicht zwischen Randabstand und Winkel.
          */}
          <button
            className={`${styles.tab} ${melden || debug || zoning || liste ? "" : styles.tabAktiv}`}
            onMouseDown={haltAn}
            onClick={() => setTab("layout")}
          >
            {t.reiterEntwurf}
          </button>
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
          <div className={styles.kopfKnoepfe}>
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
            {[
              { text: t.tooltipReset, icon: "Reset", action: resetAll },
              { text: t.tooltipVerwerfen, icon: "Trash", action: discardDefaults },
              { text: t.tooltipSchliessen, icon: "XClose", action: togglePanel },
            ].map((knopf) => (
              <TooltipKnopf key={knopf.icon} text={knopf.text}
                className={styles.iconButton}
                onMouseDown={haltAn}
                onClick={knopf.action}
              >
                <img src={icon(knopf.icon)} />
              </TooltipKnopf>
            ))}
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
        <Spalte title={t.zuschnitt} ton="Zuschnitt" titleTooltip={t.tooltipZuschnitt}>
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

        <Spalte title={t.fahrwege} ton="Fahrwege" titleTooltip={t.tooltipFahrwege}>
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

        <Spalte title={t.gruen} ton="Gruen" titleTooltip={t.tooltipGruen}>
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
          <Vegetation />
        </Spalte>

        <Spalte title={t.spalteFlaechen} ton="Zufahrt" titleTooltip={t.tooltipFlaechen}>
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
          VIER ARTEN, VIER KNOEPFE - und die Farbe ist die Zuordnung.
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
