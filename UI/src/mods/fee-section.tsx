import { useValue } from "cs2/api";
import { useCallback, useEffect, useRef, useState } from "react";
import {
  selectedParkingFee$,
  setSelectedParkingFee,
  icon,
} from "./bindings";
import { brauchbar, Foldout, Zeile } from "./cs2-bausteine";
import { MitTooltip } from "./controls";
import styles from "./fee-section.module.scss";
import { useTexte } from "./texte";

/*
 * Bausteine und die Begruendung fuer die Pruefung: cs2-bausteine.tsx.
 */

/*
 * DER REGLER GEHT VON 1 BIS 50, NICHT VON 0.
 *
 * CS2 fuehrt die Parkgebuehr als Richtlinie mit HAKEN plus Regler. Der Haken
 * schaltet ein und aus, der Regler kennt nur Betraege - eine "0" gibt es dort
 * nicht. Intern bleibt 0 der Aus-Zustand, so liest es
 * `ParkingLotComfortSystem`; am Regler taucht sie nie auf.
 */
const MIN_FEE = 1;
const MAX_FEE = 50;
const STANDARD_FEE = 10;

export const ParkingFeeSection = () => {
  const storedFee = useValue(selectedParkingFee$);
  const t = useTexte();
  const track = useRef<HTMLDivElement>(null);
  const [dragging, setDragging] = useState(false);
  const [dragFee, setDragFee] = useState<number | null>(null);

  /*
   * Der zuletzt bewusst gewaehlte Betrag, damit der Haken ihn zurueckholt.
   * Sonst finge jedes Wiedereinschalten beim Standard an, und ein
   * versehentliches Ausschalten kostete die Einstellung.
   */
  const letzterBetrag = useRef(STANDARD_FEE);
  useEffect(() => {
    if (storedFee > 0) letzterBetrag.current = storedFee;
  }, [storedFee]);

  useEffect(() => {
    if (!dragging) setDragFee(null);
  }, [storedFee, dragging]);

  const feeAt = useCallback((clientX: number) => {
    const box = track.current?.getBoundingClientRect();
    if (!box || box.width <= 0) return Math.max(MIN_FEE, storedFee);
    const share = Math.min(1, Math.max(0, (clientX - box.left) / box.width));
    return Math.round(MIN_FEE + share * (MAX_FEE - MIN_FEE));
  }, [storedFee]);

  const apply = useCallback((clientX: number) => {
    const next = feeAt(clientX);
    setDragFee(next);
    if (next !== storedFee) setSelectedParkingFee(next);
  }, [feeAt, storedFee]);

  useEffect(() => {
    if (!dragging) return;
    const move = (event: MouseEvent) => apply(event.clientX);
    const end = () => setDragging(false);
    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup", end);
    return () => {
      window.removeEventListener("mousemove", move);
      window.removeEventListener("mouseup", end);
    };
  }, [dragging, apply]);

  const an = storedFee > 0;
  const shownFee = dragFee ?? (an ? storedFee : letzterBetrag.current);
  const percent = `${Math.round(
    100 * (shownFee - MIN_FEE) / (MAX_FEE - MIN_FEE),
  )}%`;

  const umschalten = () =>
    setSelectedParkingFee(an ? 0 : Math.max(MIN_FEE, letzterBetrag.current));

  /*
   * `title` allein zeigt in Cohtml NICHTS - hier stand es als einzige
   * Quelle, und der Haken war damit unbeschriftet. Dieselbe Falle wie
   * seinerzeit am Zoning-Reiter; die Regel steht in controls.tsx.
   */
  const haken = (
    <MitTooltip text={t.tooltipGebuehrHaken}>
      <button
        className={`${styles.haken} ${an ? styles.hakenAn : ""}`}
        title={t.tooltipGebuehrHaken}
        aria-label={t.tooltipGebuehrHaken}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={umschalten}
      >
        {an ? <img src={icon("Checkmark")} /> : null}
      </button>
    </MitTooltip>
  );

  const regler = (
    /*
     * Aus heisst blass, nicht weg: der eingestellte Betrag bleibt sichtbar,
     * damit man weiss, was der Haken zurueckholt.
     */
    <MitTooltip text={t.tooltipGebuehrRegler}>
    <div className={`${styles.sliderArea} ${an ? "" : styles.ausgegraut}`}>
      <div
        ref={track}
        aria-label={t.tooltipGebuehrRegler}
        className={styles.track}
        onMouseDown={(event: any) => {
          if (!an) return;
          event.stopPropagation();
          setDragging(true);
          apply(event.clientX);
        }}
      >
        <div className={styles.fill} style={{ width: percent }} />
        <div className={styles.knob} style={{ left: percent }} />
      </div>
      <div className={styles.betrag}>{t.waehrung}{shownFee}</div>
    </div>
    </MitTooltip>
  );

  const inhalt = brauchbar(Zeile)
    ? (
      <>
        <Zeile
          icon={icon("Lanes")}
          left={t.parkgebuehr}
          right={haken}
          uppercase={false}
          disableFocus
        />
        {regler}
      </>
    )
    : (
      <>
        <div className={styles.zeile}>
          <img className={styles.symbol} src={icon("Lanes")} />
          <span className={styles.name}>{t.parkgebuehr}</span>
          {haken}
        </div>
        {regler}
      </>
    );

  if (!brauchbar(Foldout)) {
    return (
      <div className={styles.section}>
        <div className={styles.header}>{t.bestimmungen}</div>
        {inhalt}
      </div>
    );
  }

  return (
    <Foldout
      header={<div className={styles.header}>{t.bestimmungen}</div>}
      initialExpanded
      disableFocus
      className={styles.section}
    >
      {inhalt}
    </Foldout>
  );
};
