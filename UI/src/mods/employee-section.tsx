import { brauchbar, Foldout, Zeile } from "./cs2-bausteine";
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
}

export const ParkingEmployeeSection = ({
  employees = 0,
  maxEmployees = 0,
}: Eigenschaften) => {
  const t = useTexte();

  const zeile = brauchbar(Zeile)
    ? (
      <Zeile
        icon={SYMBOL}
        left={t.beschaeftigte}
        right={`${employees} / ${maxEmployees}`}
        uppercase={false}
        disableFocus
      />
    )
    : (
      <div className={styles.zeile}>
        <img className={styles.symbol} src={SYMBOL} />
        <span className={styles.name}>{t.beschaeftigte}</span>
        <span className={styles.value}>{employees} / {maxEmployees}</span>
      </div>
    );

  if (!brauchbar(Foldout)) {
    return (
      <div className={styles.section}>
        <div className={styles.header}>{t.angestellte}</div>
        {zeile}
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
    </Foldout>
  );
};
