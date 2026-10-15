import * as CS2UI from "cs2/ui";
import * as CS2Utils from "cs2/utils";
import { createContext, useContext, useEffect, useState } from "react";
import { brauchbar } from "./cs2-bausteine";

/*
 * DIE BAUSTEINE FUER JEDES PLT-FENSTER (2026-10-15).
 *
 * Grundregel des Nutzers: "Das Panel selbst darf nicht groesser werden, sonst
 * ragt es irgendwann aus dem Fenster. Der Inhalt wird kleiner - aber nur
 * Text." Jedes Fenster von PLT (Panel in beiden Stilen, Vegetation, Laternen,
 * Sync-Meldung) holt seine Hoechstmasse von hier, statt eigene Zahlen zu
 * rechnen. Was nicht hineinpasst, laeuft in einem Laufbereich; was aufklappt,
 * wird ueber `Aufklapper` ausserhalb jedes Laufbereichs gezeichnet.
 */

/* ------------------------------------------------------------------ rem */

const cs2UseRem: (() => number) | null =
  typeof (CS2Utils as any).useRem === "function" ? (CS2Utils as any).useRem : null;

/** Nachrechnung von CS2s Stylesheet, nur als Rueckfall ohne `useRem`. */
const remAusFenster = () => {
  const b = window.innerWidth || 1920;
  const h = window.innerHeight || 1080;
  return h >= b * 0.5625 ? b / 1920 : h / 1080;
};

/**
 * 1 rem in Pixeln - CS2s EIGENE Rechnung (`useRem` aus `cs2/utils`).
 *
 * Bis zum 2026-10-15 rechnete PLT die Formel aus `index.css` nach. Die kennt
 * den Schalter "Interface scaling" nicht: ist er aus, setzt CS2
 * `html { font-size: 1px }`, und bei 1280x720 rechnete PLT mit 0,67 statt 1
 * Pixel je rem. `useRem` liest denselben Schalter wie das Spiel.
 *
 * Ein Hook: nur oben in einer Komponente aufrufen. Ob CS2 ihn anbietet,
 * steht beim Laden fest - die Bedingung wechselt also nie zwischen zwei
 * Durchlaeufen.
 */
export const useRemPx: () => number = cs2UseRem
  ? () => { const r = cs2UseRem!(); return r > 0 ? r : remAusFenster(); }
  : () => remAusFenster();

/* ----------------------------------------------------------- Bildschirm */

/** Die Bildschirmgroesse in Pixeln; rendert bei einer Groessenaenderung neu. */
export const useBildschirm = () => {
  const lesen = () => ({
    breite: window.innerWidth || 1920,
    hoehe: window.innerHeight || 1080,
  });
  const [mass, setMass] = useState(lesen);
  useEffect(() => {
    const neu = () => setMass(lesen());
    window.addEventListener("resize", neu);
    return () => window.removeEventListener("resize", neu);
  }, []);
  return mass;
};

/* ------------------------------------------------------------ Textgroesse */

/**
 * WIE STARK DIE SCHRIFT GEGENUEBER 100 % WAECHST (2026-10-15).
 *
 * CS2 setzt fuer den Regler "Text scale" `--fontScale` am Wurzelelement
 * (index.js, `FR`), und das Spielthema rechnet daraus
 *
 *   --fontSizeXS: calc(12rem * var(--fontScale)
 *                      + var(--fontScaleChange) * 1.15 * 12rem)
 *
 * Bei 150 % ist die Schrift also nicht 1,5-, sondern gut 2-mal so gross.
 * Breiten, die Text tragen muessen (Abschnitte, Abschluss, Zufahrtknoepfe),
 * wachsen um diesen Faktor; das Fenster selbst nicht - es bleibt in seinen
 * Hoechstmassen, und was nicht passt, laeuft.
 *
 * Unter 100 % bleibt es bei 1: kleinere Schrift braucht keinen Platz mehr.
 */
const textFaktorLesen = () => {
  try {
    const s = parseFloat((document.documentElement.style
      .getPropertyValue("--fontScale") || "1").trim());
    if (!(s > 0 && s < 5)) return 1;
    return Math.max(1, s + 1.15 * (s - 1));
  } catch {
    return 1;
  }
};

/**
 * Der Textfaktor als Hook. CS2 meldet den Reglerwechsel nicht; einmal je
 * Sekunde nachsehen kostet nichts und rendert nur bei einer Aenderung neu.
 */
export const useTextFaktor = () => {
  const [faktor, setFaktor] = useState(textFaktorLesen);
  useEffect(() => {
    const id = setInterval(() => {
      const neu = textFaktorLesen();
      setFaktor((alt) => (Math.abs(alt - neu) > 0.001 ? neu : alt));
    }, 1000);
    return () => clearInterval(id);
  }, []);
  return faktor;
};

/** Abstand jedes Fensters zum Bildrand, wie CS2s eigene Werkzeugfenster. */
export const RAND_REM = 12;

/** Oben frei: CS2s obere Leiste (55 rem) plus Rand. */
export const OBEN_FREI_REM = 55 + RAND_REM;

/** Kleinste sinnvolle Hoehe eines Fensters, bevor es unbedienbar wird. */
export const MINDESTHOEHE_REM = 160;

/**
 * Die Skala der Werkzeugleiste (`--toolbarScale`, 0,8 bis 1,3).
 * Faellt der Zugriff aus, bleibt es bei 1.
 */
export const toolbarSkala = () => {
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
 * Unten frei: CS2s Werkzeugleiste. Gemessen bei 1920x1080: die Leiste
 * beginnt bei 980 px, das Fenster darueber endet bei 974 - 106 rem, davon
 * wachsen 50 mit `--toolbarScale` (CS2s `--toolbarHeight`).
 */
export const unterFreiRem = () => 56 + 50 * toolbarSkala();

/**
 * Wie hoch ein Fenster hoechstens sein darf, in Pixeln.
 *
 *   oben    Oberkante in px vom oberen Bildrand, oder
 *   unten   Unterkante in px vom unteren Bildrand
 *
 * Genau eines von beiden ist bekannt - die Fenster haengen je nach Stil oben
 * oder unten. Die Grenze ist eine OBERGRENZE fuer das Fenster und keine
 * Untergrenze: ist der Bildschirm kleiner als `MINDESTHOEHE_REM`, wird das
 * Fenster trotzdem nicht hoeher als der Bildschirm.
 */
export const hoechsthoehePx = (rem: number, hoehe: number,
                               anker: { oben?: number; unten?: number }) => {
  const frei = anker.oben !== undefined
    ? hoehe - anker.oben - unterFreiRem() * rem
    : hoehe - (anker.unten ?? 0) - OBEN_FREI_REM * rem;
  // Nie ueber den freien Platz hinaus - sonst ragte die Oberkante aus dem
  // Bild. Dass genug frei bleibt, sichert `hoechsterAnker` beim Verschieben.
  return Math.max(0, Math.floor(frei));
};

/**
 * Wie weit der Anker (Ober- bzw. Unterkante, px vom jeweiligen Rand) hoechstens
 * wandern darf, damit noch `MINDESTHOEHE_REM` fuer das Fenster frei bleiben.
 */
export const hoechsterAnker = (rem: number, hoehe: number, obenVerankert: boolean) =>
  Math.max(0, hoehe - (MINDESTHOEHE_REM
    + (obenVerankert ? unterFreiRem() : OBEN_FREI_REM)) * rem);

/* --------------------------------------------------------------- Spalten */

/**
 * EINE SPALTENBREITE FUER JEDEN ABSCHNITT (2026-10-15).
 *
 * Platz je Abschnitt in rem, Innenrand eingerechnet (Basis 210-230 rem plus
 * 16 rem Rand), und Platz fuer den Abschluss (Ergebnis + Bauknopf). Wie
 * viele Abschnitte nebeneinander stehen, rechnet das Panel aus seiner
 * Breite aus und gibt es ueber `SpaltenProReihe` weiter - die
 * `SpaltenGruppe` (controls.tsx) baut daraus echte Reihen, statt sich auf
 * den Umbruch von Gameface zu verlassen. Der rechnete die Hoehe
 * umgebrochener Zeilen falsch, daraus wurde Bild 2 (alles uebereinander).
 *
 * Beide Werte gelten bei 100 % Text; das Panel multipliziert sie mit
 * `useTextFaktor()`.
 */
export const SPALTE_REM = 230;
/**
 * Breite des Abschlusses: die Handlungsspalte (196 rem) waechst mit dem
 * Text, das Ergebnis daneben bekommt feste 200 rem plus 2 x 16 Rand und
 * bricht seinen Text um. Muss zu `.stilHorizontal .inhaltEntwurf >
 * .abschluss` in panel.module.scss passen.
 */
export const abschlussRem = (textFaktor: number) => 196 * textFaktor + 232;

/**
 * DIE WAAGERECHTE LEISTE WIRD MIT DEM TEXT NICHT HOEHER (Nutzer 2026-10-15).
 *
 * "150 % war zu hoch, deswegen sollte es max so hoch sein wie 100 %." Bei
 * 100 % Text steht die Leiste so hoch wie ihr Inhalt, hoechstens
 * `HOEHE_WAAGERECHT_REM` (40 % eines 16:9-Bildes). Diese Hoehe misst das
 * Panel im Entwurf und merkt sie sich; bei groesserem Text ist sie die
 * Obergrenze, und was nicht passt, laeuft. Noch nie bei 100 % gemessen:
 * `HOEHE_100_RUECKFALL_REM`, geschaetzt aus Kopfleiste und hoechstem
 * Abschnitt.
 */
export const HOEHE_WAAGERECHT_REM = 432;
export const HOEHE_100_RUECKFALL_REM = 340;

/** 0 = keine Reihen bauen (hochkant: eine Spalte, das macht das Stylesheet). */
export const SpaltenProReihe = createContext<number>(0);

/* ------------------------------------------------------------ Aufklapper */

const cs2Portal: any = brauchbar((CS2UI as any).Portal) ? (CS2UI as any).Portal : null;

/**
 * Die Ebene, in die Aufklapper gezeichnet werden: ein Nachbar des Panels,
 * kein Kind. So schneidet kein Laufbereich sie ab.
 */
const AufklappEbeneKontext = createContext<HTMLElement | null>(null);

/**
 * Stellt die Ebene bereit. Gehoert als Nachbar neben das Panel - in
 * denselben positionierten Vorfahren, damit die Rechnung in Pixeln stimmt.
 */
export const AufklappEbene = ({ children }: { children: any }) => {
  const [ebene, setEbene] = useState<HTMLElement | null>(null);
  return (
    <AufklappEbeneKontext.Provider value={ebene}>
      {children}
      <div ref={setEbene} style={{
        position: "absolute", left: 0, top: 0, width: "100%", height: "100%",
        pointerEvents: "none",
      }} />
    </AufklappEbeneKontext.Provider>
  );
};

type Platz = { links: number; breite: number; oben?: number; unten?: number; hoehe: number };

/**
 * ETWAS, DAS AN EINEM KNOPF AUFKLAPPT - Flaechenliste, Sortiermenue.
 *
 * Gezeichnet wird es mit CS2s `Portal` in die `AufklappEbene`, also
 * AUSSERHALB jedes Laufbereichs: Gameface schneidet in einem Laufbereich auch
 * absolut positionierte Kinder ab. Die Stelle kommt aus der Messung des
 * Ankers (`getBoundingClientRect`, das CS2 selbst 35-mal benutzt), zwei
 * Bilder nach dem Oeffnen - Gameface legt das Layout einmal je Bild an.
 *
 * Richtung: dorthin, wo mehr Platz ist; `richtung` legt die bevorzugte fest.
 * Die Hoehe ist auf den Platz bis zum Bildrand begrenzt, der Inhalt laeuft.
 *
 * RUECKFALL: fehlt `Portal` oder die Ebene, oder misst Gameface 0, wird
 * der Inhalt wie bisher an Ort und Stelle gezeichnet (`klasseVorOrt`).
 */
export const Aufklapper = ({ anker, richtung = "oben", breiteRem, klasse,
                             klasseVorOrt, innenRef, children }: {
  anker: { current: HTMLElement | null };
  richtung?: "oben" | "unten";
  /** Feste Breite in rem; ohne Angabe so breit wie der Anker. */
  breiteRem?: number;
  /** Aussehen (Grund, Rand) - gilt in beiden Faellen. */
  klasse: string;
  /** Stelle im Rueckfall (absolut am Anker). */
  klasseVorOrt: string;
  /** Damit der Aufrufer einen Klick IN der Liste als "innen" erkennt. */
  innenRef?: (el: HTMLElement | null) => void;
  children: any;
}) => {
  const ebene = useContext(AufklappEbeneKontext);
  const rem = useRemPx();
  const [platz, setPlatz] = useState<Platz | null>(null);
  const [vorOrt, setVorOrt] = useState(!cs2Portal);

  const messen = () => {
    const a = anker.current;
    if (!a || !ebene) { setVorOrt(true); return; }
    let ra: DOMRect, re: DOMRect;
    try {
      ra = a.getBoundingClientRect();
      re = ebene.getBoundingClientRect();
    } catch {
      setVorOrt(true); return;
    }
    if (!(ra.width > 0) || !(re.height > 0)) { setVorOrt(true); return; }
    const abstand = 4 * rem;
    const rand = RAND_REM * rem;
    const ueber = ra.top - re.top - abstand - rand;
    const unter = re.bottom - ra.bottom - abstand - rand;
    const nachOben = richtung === "oben" ? ueber >= Math.min(unter, 200 * rem) : unter < Math.min(ueber, 200 * rem);
    const breite = breiteRem ? breiteRem * rem : ra.width;
    const links = Math.max(rand, Math.min(ra.left - re.left, re.width - breite - rand));
    setPlatz(nachOben
      ? { links, breite, unten: re.bottom - ra.top + abstand, hoehe: Math.max(0, ueber) }
      : { links, breite, oben: ra.bottom - re.top + abstand, hoehe: Math.max(0, unter) });
    setVorOrt(false);
  };

  useEffect(() => {
    if (!cs2Portal) return;
    let weg = false;
    const id = requestAnimationFrame(() => requestAnimationFrame(() => {
      if (!weg) messen();
    }));
    /* Laeuft der Bereich darunter weiter, wandert der Anker mit - nach dem
       naechsten Bild neu messen, sonst schwebt die Liste an der alten Stelle. */
    let nach = 0;
    const spaeter = () => {
      cancelAnimationFrame(nach);
      nach = requestAnimationFrame(() => requestAnimationFrame(() => {
        if (!weg) messen();
      }));
    };
    window.addEventListener("resize", spaeter);
    window.addEventListener("wheel", spaeter);
    return () => {
      weg = true;
      cancelAnimationFrame(id);
      cancelAnimationFrame(nach);
      window.removeEventListener("resize", spaeter);
      window.removeEventListener("wheel", spaeter);
    };
  }, [ebene, rem]);

  if (vorOrt || !ebene) {
    return <div ref={innenRef} className={`${klasse} ${klasseVorOrt}`}>{children}</div>;
  }
  if (!platz) return null;
  const Portal = cs2Portal;
  return (
    <Portal container={ebene}>
      <div ref={innenRef} className={klasse} style={{
        position: "absolute",
        left: `${platz.links}px`,
        width: `${platz.breite}px`,
        maxHeight: `${platz.hoehe}px`,
        ...(platz.unten !== undefined
          ? { bottom: `${platz.unten}px` }
          : { top: `${platz.oben}px` }),
        display: "flex",
        flexDirection: "column",
        pointerEvents: "auto",
        boxSizing: "border-box",
      }}>
        {children}
      </div>
    </Portal>
  );
};
