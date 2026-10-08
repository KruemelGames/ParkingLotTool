import { useValue } from "cs2/api";
import styles from "./anleitung.module.scss";
import { TooltipKnopf, MitTooltip } from "./controls";
import { oeffneVideo, panelStil$ } from "./bindings";
import { useTexte } from "./texte";
import basicsBild from "../assets/video-basics.jpg";
import abspielenSymbol from "../assets/abspielen.svg";

/**
 * REITER "HOW IT WORKS" - DIE ANLEITUNGSVIDEOS (Nutzer 2026-10-08).
 *
 * Eine Kachel je Folge, in der Reihenfolge der Reihe. Fertige Folgen zeigen
 * ihr Vorschaubild und oeffnen das Video im Browser; kommende stehen als
 * Platzhalter in DERSELBEN Groesse daneben, damit die Reihe schon sichtbar
 * ist und beim Erscheinen nichts springt.
 *
 * Jede Folge traegt die Farbe der Aufgabe, um die es geht - dieselben Farben
 * wie die Spalten im Entwurf (Fahrwege cyan, Zuschnitt orange, Gruen gruen,
 * Zufahrt magenta) und Korallrot vom Melde-Reiter fuer "Probleme".
 *
 * Die Adresse steht NICHT hier, sondern in C# (`ParkingLotUISystem.Videos`);
 * die Oberflaeche schickt nur die Kennung.
 *
 * Neue Folge fertig: `bild` und `kennung` eintragen, Adresse in C# ergaenzen.
 */
type Folge = {
  /** Kennung fuer React; der Titel steht in `t.anleitungFolgen` an derselben Stelle. */
  titel: string;
  ton: "Fahrwege" | "Zuschnitt" | "Gruen" | "Zufahrt" | "Melden";
  /** Gesetzt = erschienen; fehlt = Platzhalter. */
  kennung?: string;
  bild?: string;
};

const FOLGEN: Folge[] = [
  { titel: "basics", ton: "Fahrwege", kennung: "basics", bild: basicsBild },
  { titel: "formUndLayout", ton: "Zuschnitt" },
  { titel: "vegetationUndLaternen", ton: "Gruen" },
  { titel: "zoning", ton: "Zufahrt" },
  { titel: "probleme", ton: "Melden" },
];

export const AnleitungTab = () => {
  const t = useTexte();
  const hochkant = useValue(panelStil$) === "hochkant";
  return (
    <div className={`${styles.anleitung} ${hochkant ? styles.hochkant : ""}`}>
      <div className={styles.kopf}>
        <div className={styles.titel}>{t.anleitungTitel}</div>
        <div className={styles.einleitung}>{t.anleitungEinleitung}</div>
      </div>
      <div className={styles.kacheln}>
        {FOLGEN.map((folge, i) => {
          const nummer = t.anleitungFolge(i + 1);
          const name = t.anleitungFolgen[i];
          const inhalt = (
            <>
              <div className={`${styles.bildRahmen} ${styles["rahmen" + folge.ton]}`}>
                {folge.bild
                  ? <>
                      <img className={styles.bild} src={folge.bild} />
                      <span className={styles.abspielen}>
                        <img className={styles.abspielenSymbol} src={abspielenSymbol} />
                      </span>
                    </>
                  : <div className={`${styles.platzhalter} ${styles["grund" + folge.ton]}`}>
                      <span className={styles.platzhalterNummer}>{i + 1}</span>
                      <span className={styles.bald}>{t.anleitungBald}</span>
                    </div>}
              </div>
              <div className={styles.unterzeile}>
                <span className={`${styles.nummer} ${styles["text" + folge.ton]}`}>{nummer}</span>
                <span className={styles.name}>{name}</span>
              </div>
            </>
          );
          return folge.kennung
            ? <TooltipKnopf key={folge.titel} text={t.anleitungAnsehen(name)}
                className={`${styles.kachel} ${styles.kachelFertig}`}
                onClick={() => oeffneVideo(folge.kennung as string)}>
                {inhalt}
              </TooltipKnopf>
            : <MitTooltip key={folge.titel} text={t.anleitungBaldTooltip(name)}>
                <div className={`${styles.kachel} ${styles.kachelBald}`}>{inhalt}</div>
              </MitTooltip>;
        })}
      </div>
    </div>
  );
};
