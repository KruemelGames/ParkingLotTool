import {
  editSelectedParkingLot, gewaehltenReparieren, meldeGewaehltenParkplatz,
} from "./bindings";
import { TooltipKnopf } from "./controls";
import styles from "./fee-section.module.scss";
import { useTexte } from "./texte";

/**
 * Zwei Einstiege; die Sichtbarkeit entscheidet die C#-Sektion.
 *
 * Neben "Bearbeiten" steht seit der Testveroeffentlichung "Bericht
 * schreiben". Ansage des Nutzers: *"Wenn denen was auffaellt, sollen die
 * einfach auf nen Parkplatz klicken koennen und nen Button druecken, um nen
 * Debug zu erstellen."* Genau hier ist der Ort dafuer - der Nutzer hat den
 * Parkplatz schon angeklickt, um sich zu wundern.
 */
export const ParkingEditSection = (props: { waise?: number; bauzettel?: boolean }) => {
  const t = useTexte();
  const waise = props.waise ?? 0;
  if (waise > 0) {
    return (
      <div className={styles.editSection}>
        <div className={styles.waiseTitel}>{t.waiseTitel}</div>
        <div className={styles.waiseText}>{t.waiseErklaerung}</div>
        {waise === 1
          ? <TooltipKnopf
              text={t.tooltipWaiseReparieren}
              className={styles.editButton}
              onMouseDown={(event: any) => event.stopPropagation()}
              onClick={gewaehltenReparieren}
            >
              {t.waiseReparieren}
            </TooltipKnopf>
          : <div className={styles.waiseText}>{t.waiseNichtReparierbar}</div>}
      </div>
    );
  }
  return (
    <div className={styles.editSection}>
      {/* Der reine HTML-`title` erzeugt in Cohtml KEINEN Tooltip. Die
          Projektregel in controls.tsx sagt es woertlich: "Kein
          title-Attribut als alleinige Quelle." `TooltipKnopf` setzt den
          Spiel-Tooltip und den Rueckfalltext aus derselben Quelle. */}
      <TooltipKnopf
        text={t.tooltipBearbeiten}
        className={styles.editButton}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={editSelectedParkingLot}
      >
        {t.bearbeiten}
      </TooltipKnopf>
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
