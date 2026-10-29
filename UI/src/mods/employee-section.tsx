import { useEffect, useState } from "react";
import { icon, setOhnePersonal } from "./bindings";
import { brauchbar, Foldout, Zeile } from "./cs2-bausteine";
import { MitTooltip } from "./controls";
import styles from "./fee-section.module.scss";
import { useTexte } from "./texte";

/*
 * DIE ZAHLEN KOMMEN OHNE BINDUNG HERUEBER.
 *
 * `InfoSectionBase.Write` oeffnet ein Objekt mit dem vollen Klassennamen und
 * ruft danach `OnWriteProperties`. Was dort geschrieben wird, landet als
 * Eigenschaft an dieser Komponente - siehe `Tools/ParkingLotEmployeeSection.cs`.
 * Fuer zwei Zahlen ist das der kuerzere Weg als bei der Parkgebuehr, die einen
 * Regler bedient und deshalb echte Bindungen braucht.
 *
 * Das Symbol ist das Vanilla-Symbol des Spiels, kein Bibliothekssymbol: die
 * Zeile soll neben den anderen Zeilen des Fensters nicht auffallen. Der Pfad
 * ist bewusst relativ, genau wie im Spielbundle - er loest gegen das Dokument
 * auf, nicht gegen unser Modul.
 */
const SYMBOL = "Media/Game/Icons/Workers.svg";

interface Eigenschaften {
  employees?: number;
  maxEmployees?: number;
  /** Issue #23: Parkplatz ohne Personal (halber Unterhalt, keine Stellen). */
  ohnePersonal?: boolean;
  /** Stellen, die der Parkplatz MIT Personal haette - 0 heisst: zu klein, kein Schalter. */
  mitPersonal?: number;
  /** Monatsloehne nach Budget-Prozent; stecken im Unterhalt, hier einzeln sichtbar. */
  loehne?: number;
}

export const ParkingEmployeeSection = ({
  employees = 0,
  maxEmployees = 0,
  ohnePersonal = false,
  mitPersonal = 0,
  loehne = 0,
}: Eigenschaften) => {
  const t = useTexte();

  /*
   * DAS HAEKCHEN SPRINGT SOFORT UM (Nutzer 29.10.: "so lange, dass es mir
   * kurz vorkam, als haette ich es gar nicht gesetzt"). Der Wert kommt sonst
   * erst mit der naechsten Auffrischung des Fensters zurueck. Die Wahl gilt,
   * bis das Spiel sie bestaetigt - oder hoechstens zwei Sekunden, falls der
   * Klick dort nicht ankam (dann zeigt das Haekchen wieder die Wahrheit).
   */
  const [gewuenscht, setGewuenscht] = useState<boolean | null>(null);
  useEffect(() => {
    if (gewuenscht === null) return;
    if (gewuenscht === !ohnePersonal) { setGewuenscht(null); return; }
    const uhr = setTimeout(() => setGewuenscht(null), 2000);
    return () => clearTimeout(uhr);
  }, [gewuenscht, ohnePersonal]);

  /*
   * DER SCHALTER WIE DAS GEBUEHRENHAEKCHEN (Issue #23).
   *
   * Haken an = mit Personal, der Standard. Aus = ohne Angestellte und mit
   * halbem Unterhalt. Gleiches Bild wie die Parkgebuehr darueber, damit der
   * Spieler nichts Neues lernen muss. Kleine Parkplaetze (bis 40 Buchten)
   * haben ohnehin kein Personal; dort gibt es nichts umzuschalten.
   */
  const mit = gewuenscht ?? !ohnePersonal;
  const haken = mitPersonal > 0 ? (
    <MitTooltip text={t.tooltipMitPersonal}>
      <button
        className={`${styles.haken} ${mit ? styles.hakenAn : ""}`}
        title={t.tooltipMitPersonal}
        aria-label={t.tooltipMitPersonal}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={() => { setGewuenscht(!mit); setOhnePersonal(mit); }}
      >
        {mit ? <img src={icon("Checkmark")} /> : null}
      </button>
    </MitTooltip>
  ) : null;
  /*
   * DIE LOEHNE ALS EIGENE ZEILE (Nutzer 29.10.: "ein Vermerk, dass man sich
   * das angucken kann"). Sie stecken im Unterhalt darueber - so rechnet die
   * Stadtkasse -, hier sieht man den Anteil einzeln. Ohne Personal 0.
   */
  const loehneWert = (
    <MitTooltip text={t.tooltipLoehne}>
      <span className={styles.value}>{t.betrag(loehne)}</span>
    </MitTooltip>
  );
  const loehneZeile = mitPersonal > 0 && (brauchbar(Zeile)
    ? (
      <Zeile
        icon={SYMBOL}
        className={styles.infoZeile}
        left={<span className={styles.name}>{t.loehneJeMonat}</span>}
        right={loehneWert}
        uppercase={false}
        disableFocus
      />
    )
    : (
      <div className={styles.zeile}>
        <img className={styles.symbol} src={SYMBOL} />
        <span className={styles.name}>{t.loehneJeMonat}</span>
        {loehneWert}
      </div>
    ));

  const schalter = haken && (brauchbar(Zeile)
    ? (
      <Zeile
        icon={SYMBOL}
        className={`${styles.infoZeile} ${styles.infoZeileSchalter}`}
        left={<span className={styles.name}>{t.mitPersonal}</span>}
        right={haken}
        uppercase={false}
        disableFocus
      />
    )
    : (
      <div className={styles.zeile}>
        <img className={styles.symbol} src={SYMBOL} />
        <span className={styles.name}>{t.mitPersonal}</span>
        {haken}
      </div>
    ));

  const zeile = brauchbar(Zeile)
    ? (
      <Zeile
        icon={SYMBOL}
        className={styles.infoZeile}
        left={<span className={styles.name}>{t.beschaeftigte}</span>}
        right={<span className={styles.value}>{`${employees} / ${maxEmployees}`}</span>}
        uppercase={false}
        disableFocus
      />
    )
    : (
      <div className={styles.zeile}>
        <img className={styles.symbol} src={SYMBOL} />
        <span className={styles.name}>{t.beschaeftigte}</span>
        <span className={styles.value}>{`${employees} / ${maxEmployees}`}</span>
      </div>
    );

  if (!brauchbar(Foldout)) {
    return (
      <div className={styles.section}>
        <div className={styles.header}>{t.angestellte}</div>
        {zeile}
        {loehneZeile}
        {schalter}
      </div>
    );
  }

  return (
    <Foldout
      header={<div className={styles.header}>{t.angestellte}</div>}
      initialExpanded
      disableFocus
      className={styles.section}
    >
      {zeile}
      {loehneZeile}
      {schalter}
    </Foldout>
  );
};
