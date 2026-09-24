import { useValue } from "cs2/api";
import {
  syncAuto$, syncErgebnis$, syncErgebnisSchliessen, syncLaeuft$,
} from "./bindings";
import { TooltipKnopf } from "./controls";
import { useTexte } from "./texte";
import styles from "./panel.module.scss";

/** Steht in jeder Meldung vorn: der Nutzer soll sehen, von welcher Mod sie kommt. */
const MODNAME = "Parking Lot Tool";

/**
 * Kleine Meldung unten mittig.
 *
 * Bleibt an `Game` montiert, auch wenn das Parkplatz-Panel geschlossen ist.
 * Waehrend einer automatischen Synchronisation Fortschritt und Balken,
 * klickdurchlaessig. Danach EINE Meldung mit allem, was von selbst passiert
 * ist (synchronisiert, Waisen repariert, Bauplaene wiederhergestellt), bis
 * C# sie nach 10 s wegnimmt oder ein Klick sie schliesst. Die Oberflaeche
 * merkt sich bewusst nichts - so haengt nichts davon ab, wann diese
 * Komponente eingehaengt wurde.
 */
export const SyncFortschritt = () => {
  const auto = useValue(syncAuto$);
  const lauf = useValue(syncLaeuft$);
  const ergebnis = useValue(syncErgebnis$);
  const t = useTexte();

  if (auto && lauf) {
    const felder = lauf.split("\t");
    const erledigt = Number(felder[0]);
    const gesamt = Number(felder[1]);
    if (felder.length !== 2 || !Number.isInteger(erledigt)
        || !Number.isInteger(gesamt) || gesamt <= 0) return null;
    const anteil = Math.min(1, Math.max(0, erledigt / gesamt));
    return <div className={styles.syncFortschritt} role="status">
      <div className={styles.syncMod}>{`${MODNAME}:`}</div>
      <div className={styles.syncTitel}>{t.syncFortschritt(erledigt, gesamt)}</div>
      <div className={styles.syncBalken}>
        <div className={styles.syncBalkenFuellung}
          style={{ width: `${Math.round(anteil * 100)}%` }} />
      </div>
    </div>;
  }

  if (!ergebnis) return null;
  const [sync, waisen, bauplaene] = ergebnis.split("\t").map(Number);
  const zeilen: string[] = [];
  if (waisen > 0) zeilen.push(t.waisenRepariertMeldung(waisen));
  if (bauplaene > 0) zeilen.push(t.bauplaeneMeldung(bauplaene));
  if (sync > 0) zeilen.push(t.syncFertig(sync));
  if (zeilen.length === 0) return null;
  return <TooltipKnopf text={t.syncSchliessen}
    className={`${styles.syncFortschritt} ${styles.syncFertig}`}
    role="status" onClick={syncErgebnisSchliessen}>
    <div className={styles.syncMod}>
      {`${MODNAME}:`}
    </div>
    {zeilen.map((zeile, i) =>
      <div key={i} className={styles.syncTitel}>{zeile}</div>)}
  </TooltipKnopf>;
};
