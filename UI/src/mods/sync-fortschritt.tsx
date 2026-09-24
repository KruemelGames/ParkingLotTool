import { useValue } from "cs2/api";
import { syncAuto$, syncLaeuft$ } from "./bindings";
import { useTexte } from "./texte";
import styles from "./panel.module.scss";

/** Bleibt an Game montiert, auch wenn das Parkplatz-Panel geschlossen ist. */
export const SyncFortschritt = () => {
  const auto = useValue(syncAuto$);
  const lauf = useValue(syncLaeuft$);
  const t = useTexte();
  if (!auto || !lauf) return null;

  const felder = lauf.split("\t");
  const fertig = Number(felder[0]);
  const gesamt = Number(felder[1]);
  if (felder.length !== 2 || !Number.isInteger(fertig)
      || !Number.isInteger(gesamt) || gesamt <= 0) return null;

  const fortschritt = Math.min(1, Math.max(0, fertig / gesamt));
  return <div className={styles.syncFortschritt} role="status">
    <div>{t.syncFortschritt(fertig, gesamt)}</div>
    <div className={styles.syncBalken}>
      <div className={styles.syncBalkenFuellung}
        style={{ width: `${Math.round(fortschritt * 100)}%` }} />
    </div>
  </div>;
};
