import { useEffect, useRef, useState } from "react";
import { useValue } from "cs2/api";
import {
  syncErgebnis$, syncErgebnisSchliessen, syncHaengt$, syncHaengtSchliessen, syncLaeuft$, syncMelden,
  syncPuls$, oeffneListe, icon,
} from "./bindings";
import { TooltipKnopf } from "./controls";
import { useTexte } from "./texte";
import styles from "./panel.module.scss";

/** Steht in jeder Meldung vorn: der Nutzer soll sehen, von welcher Mod sie kommt. */

/** So lange pulsiert die Meldung nach einem gesperrten Klick (zwei Schlaege). */
const PULS_MS = 1300;

/**
 * "ICH ARBEITE NOCH" OHNE WORTE (Nutzer 2026-10-06).
 *
 * Solange Sync oder Reparatur laufen, oeffnet C# das PLT-Werkzeug nicht
 * und zaehlt stattdessen `SyncPuls` hoch. Jede neue Zahl laesst die
 * Fortschrittsmeldung kurz gelb pulsieren - gelb heisst bei uns "es wartet
 * etwas". Der erste Wert nach dem Einhaengen pulsiert nicht.
 */
const usePuls = () => {
  const puls = useValue(syncPuls$);
  const zuletzt = useRef(puls);
  const [aktiv, setAktiv] = useState(false);
  useEffect(() => {
    if (puls === zuletzt.current) return;
    zuletzt.current = puls;
    setAktiv(false);
    // Ein Bild aus, dann wieder an: so startet die Animation auch bei
    // schnell aufeinanderfolgenden Klicks von vorn.
    const an = setTimeout(() => setAktiv(true), 16);
    const aus = setTimeout(() => setAktiv(false), PULS_MS);
    return () => { clearTimeout(an); clearTimeout(aus); };
  }, [puls]);
  return aktiv;
};

/**
 * Kleine Meldung unten mittig.
 *
 * Bleibt an `Game` montiert, auch wenn das Parkplatz-Panel geschlossen ist.
 * Waehrend einer automatischen Synchronisation Fortschritt und Balken,
 * klickdurchlaessig. Danach EINE Meldung mit allem, was von selbst passiert
 * ist (synchronisiert, Waisen repariert, Bauplaene wiederhergestellt), bis
 * C# sie nach 30 s wegnimmt oder ein Klick sie schliesst. Die Oberflaeche
 * merkt sich bewusst nichts - so haengt nichts davon ab, wann diese
 * Komponente eingehaengt wurde.
 */
export const SyncFortschritt = () => {
  const lauf = useValue(syncLaeuft$);
  const haengt = useValue(syncHaengt$);
  const ergebnis = useValue(syncErgebnis$);
  const pulsiert = usePuls();
  const t = useTexte();

  /*
   * HAENGT ER, WIRD DIE MELDUNG ZUM KNOPF (Nutzer 2026-10-13, Issue #16):
   * "klick auf mich und reporten auf github". Kein stiller Ausweg - der
   * Spieler soll es melden, und der Klick legt ihm den Bericht hin.
   */
  if (haengt) {
    const felder = lauf ? lauf.split("\t") : [];
    const erledigt = Number(felder[0]);
    const gesamt = Number(felder[1]);
    const mitBalken = felder.length === 3 && Number.isInteger(erledigt)
      && Number.isInteger(gesamt) && gesamt > 0;
    const anteil = mitBalken ? Math.min(1, Math.max(0, erledigt / gesamt)) : 0;
    // Zu per X oder Rechtsklick (Nutzer 2026-10-13); Linksklick meldet.
    return <div className={`${styles.syncFortschritt} ${styles.syncHaengt}`} role="alert"
      onMouseDown={(e: any) => { if (e.button === 2) { e.stopPropagation(); syncHaengtSchliessen(); } }}>
      <TooltipKnopf text={t.syncHaengtTooltip} className={styles.syncHaengtFlaeche}
        onClick={() => syncMelden()}>
        <div className={styles.syncMod}>{t.modnameDoppelpunkt}</div>
        <div className={styles.syncTitel}>{t.syncHaengt}</div>
        <div className={styles.syncTitel}>{t.syncHaengtMelden}</div>
        {mitBalken && <div className={styles.syncBalken}>
          <div className={styles.syncBalkenFuellung}
            style={{ width: `${Math.round(anteil * 100)}%` }} />
        </div>}
      </TooltipKnopf>
      <TooltipKnopf text={t.syncHaengtSchliessen} className={styles.syncHaengtZu}
        onClick={() => syncHaengtSchliessen()}>
        <img src={icon("XClose")} />
      </TooltipKnopf>
    </div>;
  }

  // Auch beim Sync von Hand: der Neubau laeuft im Hintergrund und dauert
  // (Nutzer 2026-10-03: "Sync all" ohne jede Rueckmeldung).
  if (lauf) {
    const felder = lauf.split("\t");
    const erledigt = Number(felder[0]);
    const gesamt = Number(felder[1]);
    if (felder.length !== 3 || !Number.isInteger(erledigt)
        || !Number.isInteger(gesamt) || gesamt <= 0) return null;
    const anteil = Math.min(1, Math.max(0, erledigt / gesamt));
    return <div className={`${styles.syncFortschritt} ${pulsiert ? styles.syncPuls : ""}`} role="status">
      <div className={styles.syncMod}>{t.modnameDoppelpunkt}</div>
      <div className={styles.syncTitel}>{t.syncFortschritt(erledigt, gesamt)}</div>
      {felder[2] && <div className={styles.syncTitel}>{felder[2]}</div>}
      <div className={styles.syncBalken}>
        <div className={styles.syncBalkenFuellung}
          style={{ width: `${Math.round(anteil * 100)}%` }} />
      </div>
    </div>;
  }

  if (!ergebnis) return null;
  const [sync, waisen, bauplaene, offen] = ergebnis.split("\t").map(Number);
  const zeilen: string[] = [];
  if (waisen > 0) zeilen.push(t.waisenRepariertMeldung(waisen));
  if (bauplaene > 0) zeilen.push(t.bauplaeneMeldung(bauplaene));
  if (sync > 0) zeilen.push(t.syncFertig(sync));
  if (offen > 0) zeilen.push(t.syncOffenMeldung(offen));
  if (zeilen.length === 0) return null;
  // Ein Klick fuehrt in die Parkplatzliste - dort stehen Sync- und
  // Reparaturknoepfe gelb hervorgehoben.
  return <TooltipKnopf text={t.syncSchliessen}
    className={`${styles.syncFortschritt} ${styles.syncFertig}`}
    role="status" onClick={() => { syncErgebnisSchliessen(); oeffneListe(); }}>
    <div className={styles.syncMod}>
      {t.modnameDoppelpunkt}
    </div>
    {zeilen.map((zeile, i) =>
      <div key={i} className={styles.syncTitel}>{zeile}</div>)}
  </TooltipKnopf>;
};
