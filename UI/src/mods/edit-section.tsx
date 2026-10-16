import {
  editSelectedParkingLot, gewaehltenReparieren, meldeGewaehltenParkplatz,
  gewaehltenBearbeiten,
} from "./bindings";
import { TooltipKnopf } from "./controls";
import styles from "./fee-section.module.scss";
import { useTexte } from "./texte";

/** C# erkennt die PLT-Flaeche und liefert Freigabe samt lokalisiertem Grund. */
export const ParkingEditSection = (props: {
  waise?: number; bauzettel?: boolean; schluessel?: string;
  bearbeitbar?: boolean; reparierbar?: boolean; grund?: string;
}) => {
  const t = useTexte();
  const waise = props.waise ?? 0;
  const bearbeitbar = props.bearbeitbar ?? false;
  const reparierbar = props.reparierbar ?? false;
  const grund = props.grund ?? "";
  // Der Schluessel bleibt auch beim Werkzeugwechsel auf derselben Flaeche.
  const bearbeiten = () => {
    if (!bearbeitbar) return;
    const schluessel = props.schluessel ?? "";
    if (schluessel !== "") gewaehltenBearbeiten(schluessel);
    else editSelectedParkingLot();
  };
  const knopfklasse = (aktiv: boolean) =>
    `${styles.editButton} ${aktiv ? "" : styles.editButtonGesperrt}`;
  return (
    <div className={styles.editSection}>
      {waise > 0 && <div className={styles.waiseTitel}>
        {waise === 3 ? t.bauplanTitel : t.waiseTitel}
      </div>}
      {waise === 3 && <div className={styles.waiseText}>{t.bauplanText}</div>}
      {grund !== "" && <div className={styles.waiseText} role="status">{grund}</div>}
      {waise === 2 && <div className={styles.waiseText}>{t.waiseNichtReparierbar}</div>}
      <TooltipKnopf
        text={grund || t.tooltipBearbeiten}
        className={knopfklasse(bearbeitbar)}
        disabled={!bearbeitbar}
        aria-disabled={!bearbeitbar}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={bearbeiten}
      >
        {t.bearbeiten}
      </TooltipKnopf>
      {(waise === 1 || waise === 3) && <TooltipKnopf
        text={reparierbar ? (waise === 3 ? t.tooltipBauplanWiederherstellen : t.tooltipWaiseReparieren) : grund}
        className={knopfklasse(reparierbar)}
        disabled={!reparierbar}
        aria-disabled={!reparierbar}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={() => { if (reparierbar) gewaehltenReparieren(); }}
      >
        {waise === 3 ? t.bauplanWiederherstellen : t.waiseReparieren}
      </TooltipKnopf>}
      <TooltipKnopf
        text={t.tooltipLotBericht}
        className={styles.editButton}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={meldeGewaehltenParkplatz}
      >
        {t.lotBericht}
      </TooltipKnopf>
    </div>
  );
};
