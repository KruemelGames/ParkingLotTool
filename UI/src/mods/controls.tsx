import { useCallback, useEffect, useRef, useState } from "react";
import styles from "./panel.module.scss";
import {
  icon, FANGOPTIONEN, fangGewaehlt$, fangVerfuegbar$, setzeFang,
} from "./bindings";
import { useValue } from "cs2/api";
import { useTexte } from "./texte";
import { brauchbar, Tooltip } from "./cs2-bausteine";

/**
 * Huelle mit dem Tooltip des Spiels - oder ohne, wenn es ihn nicht gibt.
 *
 * `title` allein zeigt in Cohtml nichts; der sichtbare Tooltip kommt aus
 * `cs2/ui`. Fehlt die Komponente einmal, soll der Knopf trotzdem bedienbar
 * bleiben und nur der Tooltip ausfallen - ein fehlender Hinweis ist ein
 * Mangel, eine tote Oberflaeche waere ein Ausfall.
 */
/**
 * Ein kleines Fenster, das man am Kopf verschieben kann.
 *
 * Bewusst OHNE Merken der Position: es ist ein Werkzeug fuer einen Moment -
 * aufmachen, etwas waehlen, zumachen. Eine gemerkte Position waere eine
 * weitere Einstellung, die der Nutzer pflegen muss, und beim naechsten
 * Oeffnen an der falschen Stelle, sobald das Panel woanders steht.
 *
 * Die Maus wird am FENSTER selbst verfolgt, nicht am Dokument: Cohtml
 * liefert `onMouseMove` an das Element unter dem Zeiger, und ein Zug, der
 * das Fenster verlaesst, endet damit von selbst. Genau so macht es die
 * Leiste seit dem 2026-08-22.
 */
export const Fenster = ({ titel, breite = 320, links = 0, oben = 0,
                          pos: posAussen, onPos, vonUnten,
                          onClose, children }: {
  titel: string;
  breite?: number;
  links?: number;
  oben?: number;
  /**
   * Von aussen gehaltene Position. Wird sie mitgegeben, ueberlebt sie das
   * Schliessen des Fensters - `useState` hier drin taete das nicht, denn
   * beim Schliessen wird die Komponente abgebaut und beim naechsten
   * Oeffnen mit dem Startwert neu aufgebaut.
   */
  pos?: { x: number; y: number };
  onPos?: (p: { x: number; y: number }) => void;
  /**
   * `y` als Abstand von UNTEN statt von oben.
   *
   * Dann muss niemand die Hoehe des Fensters kennen, um es ueber etwas
   * anderes zu setzen - dieselbe Ueberlegung wie bei der waagerechten
   * Leiste des Panels, und aus demselben Grund: Cohtml liefert keine
   * Hoehen.
   */
  vonUnten?: boolean;
  onClose: () => void;
  children: any;
}) => {
  const t = useTexte();
  const [posInnen, setPosInnen] = useState({ x: links, y: oben });
  const pos = posAussen ?? posInnen;
  const setPos = (p: { x: number; y: number }) => {
    if (onPos) onPos(p); else setPosInnen(p);
  };
  const anker = useRef({ mausX: 0, mausY: 0, x: 0, y: 0 });
  const [zug, setZug] = useState(false);

  const beginnen = (event: any) => {
    anker.current = {
      mausX: event.clientX, mausY: event.clientY, x: pos.x, y: pos.y,
    };
    setZug(true);
  };
  const bewegen = (event: any) => {
    if (!zug) return;
    setPos({
      x: anker.current.x + (event.clientX - anker.current.mausX),
      // Bei unten verankertem Fenster laeuft y andersherum, sonst liefe es
      // der Maus davon.
      y: anker.current.y + (vonUnten ? -1 : 1)
        * (event.clientY - anker.current.mausY),
    });
  };
  const beenden = () => setZug(false);

  return (
    <div
      className={styles.fenster}
      style={vonUnten
        ? { left: `${pos.x}rem`, bottom: `${pos.y}rem`, width: `${breite}rem` }
        : { left: `${pos.x}rem`, top: `${pos.y}rem`, width: `${breite}rem` }}
      onMouseMove={bewegen}
      onMouseUp={beenden}
      onMouseLeave={beenden}
    >
      <div className={styles.fensterKopf} onMouseDown={beginnen}>
        <span className={styles.fensterTitel}>{titel}</span>
        <button
          className={styles.fensterSchliessen}
          aria-label={t.fensterSchliessen}
          onMouseDown={(event: any) => event.stopPropagation()}
          onClick={onClose}
        >
          &times;
        </button>
      </div>
      <div className={styles.fensterInhalt}>{children}</div>
    </div>
  );
};

/**
 * Die Fangschalter, wie CS2 sie in seinem Werkzeugfenster zeigt - nur in
 * unserer Kopfleiste.
 *
 * Gezeigt wird nur, was unser Werkzeug auch anbietet: `availableSnapMask`
 * kommt aus unserem eigenen `GetAvailableSnapMask`. Faellt die Bindung aus
 * (kein aktives Werkzeug), bleibt die Zeile leer statt sechs tote Knoepfe
 * zu zeigen.
 */
export const Fangschalter = () => {
  const t = useTexte();
  const verfuegbar = useValue(fangVerfuegbar$);
  const gewaehlt = useValue(fangGewaehlt$);
  const offen = FANGOPTIONEN.filter((o) => (verfuegbar & o.bit) !== 0);
  if (offen.length === 0) return null;
  /*
   * "Alle" ist an, sobald JEDE angebotene Art an ist. Der Klick macht
   * daraus alles-aus, sonst alles-an - dieselbe Logik wie in CS2s
   * eigenem Fenster, und das Symbol ist auch dasselbe.
   */
  const alleBits = offen.reduce((summe, o) => summe | o.bit, 0);
  const alleAn = (gewaehlt & alleBits) === alleBits;
  return (
    <div className={styles.fangZeile}>
      <TooltipKnopf
        text={alleAn ? t.fangAlleAus : t.fangAlleAn}
        className={`${styles.fangKnopf} ${alleAn ? styles.fangAn : ""}`}
        aria-pressed={alleAn}
        aria-label={alleAn ? t.fangAlleAus : t.fangAlleAn}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={() => setzeFang(alleAn ? gewaehlt & ~alleBits
                                        : gewaehlt | alleBits)}
      >
        <img alt="" src="Media/Tools/Snap Options/All.svg" />
      </TooltipKnopf>
      {offen.map((o) => {
        const an = (gewaehlt & o.bit) !== 0;
        const text = t.fangNamen[o.name] ?? o.name;
        return (
          <TooltipKnopf
            key={o.name}
            text={text}
            className={`${styles.fangKnopf} ${an ? styles.fangAn : ""}`}
            aria-pressed={an}
            aria-label={text}
            onMouseDown={(event: any) => event.stopPropagation()}
            onClick={() => setzeFang(an ? gewaehlt & ~o.bit : gewaehlt | o.bit)}
          >
            <img alt="" src={`Media/Tools/Snap Options/${o.name}.svg`} />
          </TooltipKnopf>
        );
      })}
    </div>
  );
};

export const MitTooltip = ({ text, children }: {
  text: string;
  children: JSX.Element;
}) => brauchbar(Tooltip)
  ? <Tooltip tooltip={text}>{children}</Tooltip>
  : children;

/** Ein Knopf, dessen sichtbarer Spiel-Tooltip und Rueckfalltexte immer aus
    derselben Quelle kommen. Beliebige Knopfattribute werden durchgereicht. */
export const TooltipKnopf = ({ text, children, ...props }: {
  text: string;
  children: any;
  [name: string]: any;
}) => (
  <MitTooltip text={text}>
    <button {...props} title={text} aria-label={text}>{children}</button>
  </MitTooltip>
);

/*
 * JEDE NEUE FUNKTION BRINGT IHREN TOOLTIP GLEICH MIT.
 *
 * Was passiert, nicht nur wie es heisst.
 * Verbzuerst, unter zehn Woertern, eine Zeile.
 * Die Folge nennen, wo sie nicht offensichtlich ist.
 * Redundanz ist erlaubt - es muss nur MEHR dastehen als der Name allein.
 * Tastenkuerzel ans Ende, mit Mittelpunkt abgetrennt.
 * Kein title-Attribut als alleinige Quelle.
 */

/**
 * Die Bausteine der Leiste.
 *
 * CS2 liefert Panel, Button und Dropdown mit, aber weder Schieberegler noch
 * Schalter. Der erste Anlauf nahm `<input type="range">` - das war doppelt
 * falsch: kein einziger der 117 installierten Mods benutzt es, und Cohtml
 * kennt `::-webkit-slider-thumb` nicht, der Griff liesse sich also gar nicht
 * gestalten. Der Regler ist deshalb aus Divs gebaut.
 *
 * JEDER Baustein kennt seine FARBE. Sie kommt als `ton` von aussen, weil
 * die Farbe zur Spalte gehoert und nicht zum Bauteil: derselbe Regler ist
 * unter "Zuschnitt" bernsteinfarben und unter "Fahrwege" cyan. Vorher trug
 * jedes Bedienelement dieselbe Akzentfarbe des Spiels.
 */
/**
  * "Zufahrt" ist seit dem 2026-08-21 dabei - die Flaechenspalte benutzt sie.
  * Die Farbe und die zugehoerigen Klassen gab es in der SCSS-Datei laengst,
  * nur der Typ kannte sie nicht.
  */
export type Ton = "Zuschnitt" | "Fahrwege" | "Gruen" | "Zufahrt";

interface SliderProps {
  label: string;
  tooltip: string;
  value: number;
  min: number;
  max: number;
  step: number;
  ton: Ton;
  unit?: string;
  digits?: number;
  disabled?: boolean;
  onChange: (value: number) => void;
  /* FREIWILLIG, seit es Regler ohne gespeicherten Standard gibt (Zoning).
     Ein Knopf, der nichts tut, ist schlechter als kein Knopf - deshalb
     entfaellt die ganze Knopfgruppe, wenn kein `onReset` da ist. */
  differsFromDefault?: boolean;
  onReset?: () => void;
  onSetDefault?: () => void;
}

export const Slider = ({ label, tooltip, value, min, max, step, ton, unit = "m",
                         digits = 1, disabled, onChange, differsFromDefault,
                         onReset, onSetDefault }: SliderProps) => {
  const track = useRef<HTMLDivElement>(null);
  const dragging = useRef(false);
  /**
   * Der Wert waehrend des Ziehens, unabhaengig von der Bindung.
   *
   * Ohne ihn zeigt der Regler das, was aus dem Spiel zurueckkommt - und das
   * kommt bei einem grossen Parkplatz erst nach der neuen Berechnung. Bis
   * dahin springt der Knopf auf den alten Wert zurueck und der Zug wirkt
   * zaeh. `null` heisst: kein Zug, die Bindung gilt.
   */
  const [zugwert, setZugwert] = useState<number | null>(null);
  const letzterZugwert = useRef(value);
  const wartetAufBinding = useRef(false);

  useEffect(() => {
    if (!wartetAufBinding.current || value !== letzterZugwert.current) return;
    wartetAufBinding.current = false;
    setZugwert(null);
  }, [value]);

  const valueAt = useCallback((clientX: number) => {
    const box = track.current?.getBoundingClientRect();
    if (!box || box.width <= 0) return value;
    const share = Math.min(1, Math.max(0, (clientX - box.left) / box.width));
    const raw = min + share * (max - min);
    // Auf die Schrittweite rasten, sonst entstehen Werte wie 6,83 m, die
    // niemand einstellen wollte.
    const snapped = Math.round(raw / step) * step;
    return Math.min(max, Math.max(min, snapped));
  }, [min, max, step, value]);

  const apply = (clientX: number) => {
    const next = valueAt(clientX);
    letzterZugwert.current = next;
    setZugwert(next);
  };

  /**
   * DER ZUG GEHOERT ANS FENSTER, NICHT AN DIE SCHIENE.
   *
   * Vorher hingen `mousemove` und `mouseup` an der Schiene selbst, dazu ein
   * `mouseleave`, das den Zug beendete. Die Schiene ist ein paar Pixel hoch -
   * wer schnell zieht, verlaesst sie senkrecht fast immer. Dann feuerte
   * `mouseleave`, der Zug war tot, und der Regler blieb auf halbem Weg
   * stehen. Nutzerbefund am 2026-08-24: "ich will 10 m, ziehe von 2 nach 10,
   * das UI stoppt bei 6".
   *
   * Am Fenster ueberlebt der Zug jede senkrechte Abweichung und endet genau
   * dann, wenn die Taste losgelassen wird - auch ausserhalb des Panels.
   */
  const zugBeenden = useCallback(() => {
    dragging.current = false;
    window.removeEventListener("mousemove", zugBewegen);
    window.removeEventListener("mouseup", zugBeenden);
    const next = letzterZugwert.current;
    if (next !== value)
    {
      wartetAufBinding.current = true;
      onChange(next);
    }
    else setZugwert(null);
  }, [value, onChange]);

  const zugBewegen = useCallback((event: MouseEvent) => {
    if (!dragging.current) return;
    apply(event.clientX);
  }, [min, max, step, value]);

  const anzeige = zugwert ?? value;
  const share = max > min ? (anzeige - min) / (max - min) : 0;
  const percent = `${Math.round(share * 100)}%`;

  return (
    <div className={`${styles.control} ${disabled ? styles.controlOff : ""}`}>
      <div className={styles.funktionsZeile}>
        {/* Die Huelle ist selbst der normale Flex-Inhalt der Zeile. Ihre
            Grenzen kommen damit immer aus Beschriftung plus Regler, nie aus
            festen oder absoluten Tooltip-Massen. */}
        <MitTooltip text={tooltip}>
          <div className={styles.funktionsBereich} title={tooltip} aria-label={tooltip}>
            <div className={styles.controlHead}>
              <span className={styles.label}>{label}</span>
              <span className={`${styles.value} ${disabled ? "" : styles[`wert${ton}`]}`}>
                {anzeige.toFixed(digits).replace(".", ",")} {unit}
              </span>
            </div>
            <div
              ref={track}
              className={`${styles.track} ${disabled ? styles.trackOff : ""}`}
              onMouseDown={(event: any) => {
                if (disabled) return;
                dragging.current = true;
                apply(event.clientX);
                window.addEventListener("mousemove", zugBewegen);
                window.addEventListener("mouseup", zugBeenden);
              }}
            >
              {disabled ? null : (
                <div
                  className={`${styles.fill} ${styles[`fill${ton}`]}`}
                  style={{ width: percent }}
                />
              )}
              <div
                className={`${styles.knob} ${disabled ? styles.knobOff : ""}`}
                style={{ left: disabled ? "0%" : percent }}
              />
            </div>
          </div>
        </MitTooltip>
        {onReset && onSetDefault ? (
          <SettingActions
            label={label}
            active={differsFromDefault === true}
            onReset={onReset}
            onSetDefault={onSetDefault}
          />
        ) : null}
      </div>
    </div>
  );
};

interface SettingActionsProps {
  label: string;
  active: boolean;
  onReset: () => void;
  onSetDefault: () => void;
}

/**
 * Zuruecksetzen und als Standard speichern - an jedem Regler und jedem
 * Schalter, so wie bisher.
 *
 * Beide Aktionen haben nur dann eine Wirkung, wenn aktueller Wert und
 * Benutzerstandard verschieden sind. Cohtml wertet `:disabled` nicht aus;
 * deshalb sperren Klasse, Klickschutz und `aria-disabled` gemeinsam. In der
 * Leiste heisst das: fast alle achtzehn Knoepfe sind blass, und genau der
 * eine Regler, den man verstellt hat, faellt auf.
 */
export const SettingActions = ({ label, active, onReset,
                                 onSetDefault }: SettingActionsProps) => {
  const t = useTexte();
  return (
  <span className={styles.settingActions}>
    <MitTooltip text={t.standardZuruecksetzen(label)}>
      <button
        className={`${styles.settingButton} ${active ? "" : styles.settingButtonOff}`}
        aria-disabled={!active}
        aria-label={t.standardZuruecksetzen(label)}
        title={t.standardZuruecksetzen(label)}
        onClick={() => { if (active) onReset(); }}
      >
        <img src={icon("Reset")} />
      </button>
    </MitTooltip>
    <MitTooltip text={t.standardSpeichern(label)}>
      <button
        className={`${styles.settingButton} ${active ? "" : styles.settingButtonOff}`}
        aria-disabled={!active}
        aria-label={t.standardSpeichern(label)}
        title={t.standardSpeichern(label)}
        onClick={() => { if (active) onSetDefault(); }}
      >
        <img src={icon("DiskSave")} />
      </button>
    </MitTooltip>
  </span>
  );
};

interface ToggleProps {
  label: string;
  tooltip: string;
  value: boolean;
  onChange: (value: boolean) => void;
  differsFromDefault: boolean;
  onReset: () => void;
  onSetDefault: () => void;
}

/**
 * Kippschalter statt Knopf ueber die volle Breite: halbe Hoehe, und der
 * Zustand steht in der Form selbst - man muss den Text nicht lesen, um ihn
 * zu sehen. Die Farbe traegt die Aussage nicht allein, die Lage des Griffs
 * tut es auch.
 */
export const Toggle = ({ label, tooltip, value, onChange, differsFromDefault,
                         onReset, onSetDefault }: ToggleProps) => (
  <div className={styles.schalterReihe}>
    <MitTooltip text={tooltip}>
      <div className={styles.schalterBereich} title={tooltip} aria-label={tooltip}>
        <span className={styles.label}>{label}</span>
        <button
          className={`${styles.schalter} ${value ? styles.schalterAn : ""}`}
          title={tooltip}
          aria-label={tooltip}
          onClick={() => onChange(!value)}
        >
          <span
            className={`${styles.schalterGriff} ${value ? styles.schalterGriffAn : ""}`}
          />
        </button>
      </div>
    </MitTooltip>
    <SettingActions
      label={label}
      active={differsFromDefault}
      onReset={onReset}
      onSetDefault={onSetDefault}
    />
  </div>
);

interface ModeProps {
  label: string;
  tooltip: string;
  value: string;
  options: { id: string; text: string; tooltip: string; disabled?: boolean }[];
  ton: Ton;
  onChange: (value: string) => void;
  /* FREIWILLIG, seit es Regler ohne gespeicherten Standard gibt (Zoning).
     Ein Knopf, der nichts tut, ist schlechter als kein Knopf - deshalb
     entfaellt die ganze Knopfgruppe, wenn kein `onReset` da ist. */
  differsFromDefault?: boolean;
  onReset?: () => void;
  onSetDefault?: () => void;
  /** Zweispaltig statt nebeneinander. */
  raster?: boolean;
  /** Ein weiterer Knopf in derselben Kachelreihe. */
  extra?: any;
  /** Eine volle Zeile unter den Kacheln. */
  unten?: any;
}

/** Drei kurze Moeglichkeiten nebeneinander lesen sich schneller als ein
    aufklappendes Menue, das seinen Inhalt verbirgt. */
/**
 * `raster` stellt die Knoepfe zweispaltig statt nebeneinander, `extra`
 * haengt einen weiteren Knopf in DIESELBE Kachelreihe, und `unten` setzt
 * eine volle Zeile darunter.
 *
 * Alle drei sind freiwillig: ohne sie verhaelt sich der Waehler wie bisher,
 * und die uebrigen Aufrufer im Panel bleiben unberuehrt.
 */
export const ModeChooser = ({ label, tooltip, value, options, ton, onChange,
                              differsFromDefault, onReset, onSetDefault,
                              raster, extra, unten }: ModeProps) => (
  <div className={styles.control}>
    <div className={styles.controlHead}>
      <MitTooltip text={tooltip}>
        <span className={styles.label} title={tooltip} aria-label={tooltip}>{label}</span>
      </MitTooltip>
      {onReset && onSetDefault ? (
        <SettingActions
          label={label}
          active={differsFromDefault === true}
          onReset={onReset}
          onSetDefault={onSetDefault}
        />
      ) : null}
    </div>
    <div className={raster ? styles.modesRaster : styles.modes}>
      {options.map((option) => (
        <MitTooltip key={option.id} text={option.tooltip}>
          <button
            className={`${raster ? styles.modeKachel : styles.mode}
              ${option.id === value ? styles[`modeAktiv${ton}`] : ""}
              ${option.disabled ? styles.modeOff : ""}`}
            aria-disabled={option.disabled}
            aria-label={option.tooltip}
            title={option.tooltip}
            onClick={() => { if (!option.disabled) onChange(option.id); }}
          >
            {option.text}
          </button>
        </MitTooltip>
      ))}
      {extra}
    </div>
    {unten}
  </div>
);

interface SpalteProps {
  title: string;
  ton: "Zuschnitt" | "Fahrwege" | "Gruen" | "Zufahrt" | "Melden";
  breit?: boolean;
  titleTooltip?: string;
  bereichTooltip?: string;
  /**
   * Nur im hochkanten Stil: die Ueberschrift wird zum Schalter, und
   * zugeklappt bleibt von der Spalte nur diese eine Zeile stehen.
   *
   * Waagerecht stehen die Spalten nebeneinander und sind alle offen; dort
   * kommen diese drei gar nicht erst mit.
   */
  einklappbar?: boolean;
  offen?: boolean;
  onKlick?: () => void;
  /**
   * GANZ WEG, nicht nur zugeklappt.
   *
   * Eine zugeklappte Spalte zeigt weiter ihren Titel - und im waagerechten
   * Panel steht jede Spalte NEBEN der naechsten. Elf Titel nebeneinander
   * passen nicht in 1680 rem; sie ueberlappen sich und brechen in eine
   * zweite Zeile unter den Panelrand. Der Debug-Reiter waehlt deshalb ein
   * Werkzeug aus und versteckt die anderen vollstaendig.
   */
  versteckt?: boolean;
  children: any;
}

/**
 * Eine Spalte der Leiste, mit ihrer Ueberschrift.
 *
 * Die Ueberschrift ist gross, in Grossbuchstaben und in der Farbe der
 * Spalte; die Haarlinie fuehrt sie bis zum Spaltenende. Vorher stand sie
 * als weisser Text auf einem Balken in der Akzentfarbe des Spiels - bei
 * allen drei Gruppen derselbe Balken, und mit 14 rem kleiner als die
 * Beschriftungen darunter.
 */
export const Spalte = ({ title, ton, breit, titleTooltip, bereichTooltip,
                         einklappbar, offen = true, onKlick, versteckt,
                         children }: SpalteProps) => {
  if (versteckt) return null;
  const titel = (
    <div
      className={`${styles.spaltenTitel} ${styles[`titel${ton}`]}
        ${einklappbar ? styles.spaltenTitelKlapp : ""}`}
      title={titleTooltip}
      aria-label={titleTooltip}
      role={einklappbar ? "button" : undefined}
      aria-expanded={einklappbar ? offen : undefined}
      onClick={einklappbar ? onKlick : undefined}
    >
      <span>{title.toUpperCase()}</span>
      <span className={`${styles.spaltenLinie} ${styles[`linie${ton}`]}`} />
      {/* Der Pfeil zeigt, WOHIN es geht: nach unten aufklappen, nach
          rechts steht fuer die zugeklappte Zeile. */}
      {einklappbar ? (
        <span className={styles.klappPfeil}
              style={{ maskImage: `url(Media/Glyphs/ThickStrokeArrow${
                offen ? "Down" : "Right"}.svg)` }} />
      ) : null}
    </div>
  );
  const inhalt = (
    <div
      className={styles.spaltenInhalt}
      title={bereichTooltip}
      aria-label={bereichTooltip}
    >
      {titleTooltip ? <MitTooltip text={titleTooltip}>{titel}</MitTooltip> : titel}
      {offen ? children : null}
    </div>
  );
  return (
    <div className={breit ? styles.meldeSpalte : styles.spalte}>
      {bereichTooltip
        ? <MitTooltip text={bereichTooltip}>{inhalt}</MitTooltip>
        : inhalt}
    </div>
  );
};

/**
 * Eine waehlbare Flaeche: der Prefab-Name und die Adresse ihres Bildes.
 *
 * `bild` darf leer sein. Es kommt aus `UIObject.m_Icon` am Prefab - dort
 * hinein schreibt die Asset Icon Library ihre Vorschaubilder. Fehlt die
 * Bibliothek, ist das Feld leer, und die Kachel zeigt nur den Namen.
 */
export interface Flaeche {
  name: string;
  bild: string;
}

interface AuswahlProps {
  label: string;
  tooltip: string;
  value: string;
  options: Flaeche[];
  ton: Ton;
  onChange: (value: string) => void;
  /* Freiwillig, seit es Auswahlen ohne gespeicherten Standard gibt (die
     Baulandflaeche). Fehlen sie, entfaellt die Knopfgruppe ganz - ein
     Knopf, der nichts tut, ist schlechter als kein Knopf. */
  differsFromDefault?: boolean;
  onReset?: () => void;
  onSetDefault?: () => void;
  kopfSchalter?: ToggleProps;
  /* Beschriftung fuer "nichts gewaehlt". Ist sie gesetzt, bekommt die
     Liste eine erste Kachel dafuer, und der zugeklappte Knopf zeigt sie
     statt eines leeren Feldes. Der Nutzer hat das fuer den
     Parzellenboden verlangt: leer heisst dort AUS, nicht "wie
     Dekoration", und das muss man sehen koennen. */
  ausLabel?: string;
}

/**
 * Die Flaechenauswahl - zwei Kacheln nebeneinander, mit Vorschaubild.
 *
 * WARUM BILDER. Bis zum 2026-08-25 stand hier eine Liste blosser Namen:
 * "Pavement Surface 01", "Tiles Surface 03". Wer die nicht auswendig kennt,
 * waehlt blind und baut den Parkplatz noch einmal. Bei siebzehn Flaechen im
 * Grundspiel - und beliebig vielen aus Mods - ist das der schwaechste Punkt
 * der Spalte gewesen.
 *
 * WARUM ZWEI SPALTEN. Untereinander waeren siebzehn Kacheln mit Bild eine
 * Rolle von ueber 500 rem; die Leiste ist selbst nur rund 180 rem hoch. Zu
 * zweit nebeneinander halbiert sich das, und ein Bild von 34 rem ist im
 * Spiel noch klar zu erkennen. Der Kasten scrollt weiterhin, damit ein
 * Flaechen-Mod mit fuenfzig Eintraegen das Panel nicht sprengt.
 *
 * KEIN `display: grid`, KEIN `gap` - Cohtml wertet beides nicht aus (siehe
 * den Kopf von `panel.module.scss`). Die zwei Spalten entstehen aus
 * `flex-wrap` und einer Breite von 50 % je Kachel, die Abstaende aus
 * Innenrand statt Zwischenraum.
 *
 * Bewusst aus einfachen divs gebaut: ein echtes <select> ist in Cohtml
 * nicht verlaesslich.
 */
export const Auswahl = ({ label, tooltip, value, options, ton, onChange, differsFromDefault,
                         onReset, onSetDefault,
                         kopfSchalter, ausLabel }: AuswahlProps) => {
  const [offen, setOffen] = useState(false);
  const gewaehlt = options.find((f) => f.name === value);
  return (
    <div className={`${styles.control} ${styles.auswahl}`}>
      <div className={styles.flaechenFunktionsZeile}>
        {kopfSchalter ? (
          <MitTooltip text={kopfSchalter.tooltip}>
            <button
              className={`${styles.schalter} ${styles.schalterImKopf}
                ${kopfSchalter.value ? styles.schalterAn : ""}`}
              title={kopfSchalter.tooltip}
              aria-label={kopfSchalter.tooltip}
              onClick={() => kopfSchalter.onChange(!kopfSchalter.value)}
            >
              <span
                className={`${styles.schalterGriff}
                  ${kopfSchalter.value ? styles.schalterGriffAn : ""}`}
              />
            </button>
          </MitTooltip>
        ) : null}
        <MitTooltip text={tooltip}>
          <div className={styles.funktionsBereich} title={tooltip} aria-label={tooltip}>
            <div className={styles.controlHead}>
              <span className={styles.label}>{label}</span>
            </div>
            <button
              className={`${styles.flaecheKnopf} ${styles[`modeAktiv${ton}`]}`}
              title={tooltip}
              aria-label={tooltip}
              onClick={() => setOffen(!offen)}
            >
              {gewaehlt && gewaehlt.bild
                ? <img className={styles.flaecheKnopfBild} src={gewaehlt.bild} />
                : null}
              <span className={styles.flaecheKnopfName}>
                {value ? value : (ausLabel ?? value)}
              </span>
            </button>
          </div>
        </MitTooltip>
        <div className={styles.flaechenAktionen}>
          {onReset && onSetDefault ? (
            <SettingActions
              label={label}
              active={differsFromDefault === true}
              onReset={onReset}
              onSetDefault={onSetDefault}
            />
          ) : null}
          {kopfSchalter ? (
            <SettingActions
              label={kopfSchalter.label}
              active={kopfSchalter.differsFromDefault}
              onReset={kopfSchalter.onReset}
              onSetDefault={kopfSchalter.onSetDefault}
            />
          ) : null}
        </div>
      </div>
      {offen ? (
        <div className={styles.flaechenGitter}>
          {ausLabel ? (
            <button
              key="__aus"
              className={`${styles.flaecheKachel} ${
                value ? "" : styles[`flaecheAktiv${ton}`]}`}
              title={ausLabel}
              onClick={() => { onChange(""); setOffen(false); }}
            >
              <span className={styles.flaecheOhneBild} />
              <span className={styles.flaecheName}>{ausLabel}</span>
            </button>
          ) : null}
          {options.map((flaeche) => (
            <button
              key={flaeche.name}
              className={`${styles.flaecheKachel} ${
                flaeche.name === value ? styles[`flaecheAktiv${ton}`] : ""}`}
              title={flaeche.name}
              onClick={() => { onChange(flaeche.name); setOffen(false); }}
            >
              {flaeche.bild
                ? <img className={styles.flaecheBild} src={flaeche.bild} />
                : <span className={styles.flaecheOhneBild} />}
              <span className={styles.flaecheName}>{flaeche.name}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
};
