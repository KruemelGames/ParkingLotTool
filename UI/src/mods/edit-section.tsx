import {
  editSelectedParkingLot, meldeGewaehltenParkplatz,
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
export const ParkingEditSection = () => {
  const t = useTexte();
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
