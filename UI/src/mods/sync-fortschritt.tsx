import { useValue } from "cs2/api";
import {
  icon, syncAuto$, syncErgebnis$, syncErgebnisSchliessen, syncLaeuft$,
} from "./bindings";
import { useTexte } from "./texte";
import styles from "./panel.module.scss";

/**
 * Kleine Meldung unten mittig, nur bei eingeschalteter Automatik.
 *
 * Bleibt an `Game` montiert, auch wenn das Parkplatz-Panel geschlossen ist.
 * Waehrend des Laufs Fortschritt und Balken, klickdurchlaessig. Danach
 * "N Parkplaetze aktualisiert", bis C# sie nach 10 s wegnimmt oder ein Klick
 * sie schliesst. Die Oberflaeche merkt sich hier bewusst NICHTS: ob die
 * Meldung steht, sagt allein die Bindung - so haengt es nicht davon ab, wann
 * diese Komponente eingehaengt wurde.
 */
export const SyncFortschritt = () => {
  const auto = useValue(syncAuto$);
  const lauf = useValue(syncLaeuft$);
  const ergebnis = useValue(syncErgebnis$);
  const t = useTexte();
  if (!auto) return null;

  if (lauf) {
    const felder = lauf.split("\t");
    const erledigt = Number(felder[0]);
    const gesamt = Number(felder[1]);
    if (felder.length !== 2 || !Number.isInteger(erledigt)
        || !Number.isInteger(gesamt) || gesamt <= 0) return null;
    const anteil = Math.min(1, Math.max(0, erledigt / gesamt));
    return <div className={styles.syncFortschritt} role="status">
      <div className={styles.syncTitel}>{t.syncFortschritt(erledigt, gesamt)}</div>
      <div className={styles.syncBalken}>
        <div className={styles.syncBalkenFuellung}
          style={{ width: `${Math.round(anteil * 100)}%` }} />
      </div>
    </div>;
  }

  const anzahl = Number(ergebnis);
  if (!ergebnis || !Number.isInteger(anzahl) || anzahl <= 0) return null;
  return <button className={`${styles.syncFortschritt} ${styles.syncFertig}`}
    role="status" aria-label={t.syncSchliessen}
    onClick={syncErgebnisSchliessen}>
    <div className={styles.syncTitel}>
      <img className={styles.syncHaken} src={icon("Checkmark")} />
      {t.syncFertig(anzahl)}
    </div>
    <div className={styles.syncFertigHinweis}>{t.syncSchliessen}</div>
  </button>;
};
