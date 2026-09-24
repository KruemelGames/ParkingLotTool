import { useEffect, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { syncAuto$, syncErgebnis$, syncLaeuft$ } from "./bindings";
import { useTexte } from "./texte";
import styles from "./panel.module.scss";

/** So lange bleibt die Abschlussmeldung stehen (Nutzer, 2026-09-25). */
const FERTIG_MILLISEKUNDEN = 10000;

/**
 * Kleine Meldung unten mittig, nur bei eingeschalteter Automatik.
 *
 * Bleibt an `Game` montiert, auch wenn das Parkplatz-Panel geschlossen ist.
 * Waehrend des Laufs Fortschritt und Balken, klickdurchlaessig. Danach
 * 10 s "N Parkplaetze aktualisiert"; ein Klick schliesst sie. Die
 * Abschlussmeldung ist noetig, weil ein kleiner Durchgang im selben Bild
 * fertig ist, in dem er beginnt - der Fortschritt waere nie zu sehen.
 */
export const SyncFortschritt = () => {
  const auto = useValue(syncAuto$);
  const lauf = useValue(syncLaeuft$);
  const ergebnis = useValue(syncErgebnis$);
  const t = useTexte();
  const [fertig, setFertig] = useState<number | null>(null);
  const gesehen = useRef<string | null>(null);

  useEffect(() => {
    // Der Stand beim Einhaengen ist ein alter Durchgang - nicht zeigen.
    if (gesehen.current === null) { gesehen.current = ergebnis; return; }
    if (ergebnis === gesehen.current || ergebnis === "") return;
    gesehen.current = ergebnis;
    const anzahl = Number(ergebnis.split("\t")[1]);
    if (!auto || !Number.isInteger(anzahl) || anzahl <= 0) return;
    setFertig(anzahl);
    const uhr = setTimeout(() => setFertig(null), FERTIG_MILLISEKUNDEN);
    return () => clearTimeout(uhr);
  }, [ergebnis]);

  if (!auto) return null;

  if (lauf) {
    const felder = lauf.split("\t");
    const erledigt = Number(felder[0]);
    const gesamt = Number(felder[1]);
    if (felder.length !== 2 || !Number.isInteger(erledigt)
        || !Number.isInteger(gesamt) || gesamt <= 0) return null;
    const anteil = Math.min(1, Math.max(0, erledigt / gesamt));
    return <div className={styles.syncFortschritt} role="status">
      <div>{t.syncFortschritt(erledigt, gesamt)}</div>
      <div className={styles.syncBalken}>
        <div className={styles.syncBalkenFuellung}
          style={{ width: `${Math.round(anteil * 100)}%` }} />
      </div>
    </div>;
  }

  if (fertig === null) return null;
  return <button className={`${styles.syncFortschritt} ${styles.syncFertig}`}
    role="status" aria-label={t.syncSchliessen}
    onClick={() => setFertig(null)}>
    <div>{t.syncFertig(fertig)}</div>
    <div className={styles.syncFertigHinweis}>{t.syncSchliessen}</div>
  </button>;
};
