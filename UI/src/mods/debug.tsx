import { useValue } from "cs2/api";
import { useState } from "react";
import styles from "./panel.module.scss";
import {
  probeState$, probeResults$, startProbe, cancelProbe, dissectPrefabs,
  toggleCarrierTest, carrierTestRunning$, carrierTestState$, icon,
  liveLog$, liveLogPfad$, liveLogUmschalten,
  ueberlappungsstand$, ueberlappungMessen, prefabsVergleichen,
  buildZoningProbe, measureZoningProbe, cleanupZoningProbe,
} from "./bindings";
import { Spalte } from "./controls";
import { useTexte } from "./texte";

/**
 * Der Debug-Reiter.
 *
 * Hier stehen Werkzeuge, die MESSEN statt bauen. Das erste ist der
 * Sondenlauf: er setzt einzelne Testflächen ins Spiel, liest an CS2s
 * Dreieckspuffer ab, ob sie angenommen wurden, löscht sie wieder und
 * halbiert sich so an die wahre Grenze heran.
 *
 * Warum das nötig ist: der Mod rechnet bis heute gegen GESCHÄTZTE Grenzen.
 * Die 0,375 m stammen aus dem Dekompilat und sind belastbar — ob CS2 bei
 * einer Einschnürung dieselbe Zahl anlegt, ob es einen spitzesten Winkel
 * gibt und ob die Umlaufrichtung zählt, hat nie jemand gemessen. Bevor der
 * Flächenkern deswegen umgebaut wird, sollen diese Zahlen dastehen.
 */
export const DebugTab = () => {
  const t = useTexte();
  const stand = useValue(probeState$);
  const ergebnisse = useValue(probeResults$)
    .split("\n")
    .filter((zeile) => zeile !== "");
  const laeuft = stand !== "" && stand !== t.debugFertig;
  const traegerLaeuft = useValue(carrierTestRunning$);
  const traegerstand = useValue(carrierTestState$);
  const liveAn = useValue(liveLog$);
  const livePfad = useValue(liveLogPfad$);
  const ueberlappungsstand = useValue(ueberlappungsstand$);
  const [zoningRoad, setZoningRoad] = useState("Alley");
  // Der unsichtbare Klon wird als Namenszusatz durchgereicht, damit
  // die Bindung zur Sonde eine einzige Zeichenkette bleibt.
  const [zoningUnsichtbar, setZoningUnsichtbar] = useState(false);

  return (
    <div className={styles.spaltenGruppe}>
      <Spalte
        title={t.debugSonde}
        ton="Melden"
        breit
        bereichTooltip={t.tooltipDebugSonde}
      >
        <div className={styles.explain}>{t.debugSondeHinweis}</div>
        <button className={styles.meldeKnopf} onClick={startProbe}>
          <img src={icon(laeuft ? "Dot" : "Checkmark")} />
          <span>{laeuft ? t.debugSondeLaeuft : t.debugSondeStart}</span>
        </button>
        <div className={styles.rowButtons}>
          <button className={styles.smallButton} onClick={cancelProbe}>
            {t.debugSondeStop}
          </button>
        </div>
        {stand !== "" ? (
          <div className={styles.statusZeile}>
            <span className={styles.statusPunkt} />
            <span className={styles.statusText}>{stand}</span>
          </div>
        ) : null}
      </Spalte>

      <Spalte
        title={t.debugZoningsonde}
        ton="Melden"
        breit
        bereichTooltip={t.tooltipDebugZoningsonde}
      >
        <div className={styles.explain}>{t.debugZoningsondeHinweis}</div>
        <div className={styles.rowButtons}>
          <button
            className={`${styles.smallButton} ${
              zoningRoad === "Alley" ? styles.smallButtonSelected : ""
            }`}
            onClick={() => setZoningRoad("Alley")}
            aria-pressed={zoningRoad === "Alley"}
          >
            {t.debugZoningsondeGasse}
          </button>
          <button
            className={`${styles.smallButton} ${
              zoningRoad === "Gravel Road" ? styles.smallButtonSelected : ""
            }`}
            onClick={() => setZoningRoad("Gravel Road")}
            aria-pressed={zoningRoad === "Gravel Road"}
          >
            {t.debugZoningsondeSchotter}
          </button>
          <button
            className={`${styles.smallButton} ${
              zoningUnsichtbar ? styles.smallButtonSelected : ""
            }`}
            onClick={() => setZoningUnsichtbar(!zoningUnsichtbar)}
            aria-pressed={zoningUnsichtbar}
          >
            {t.debugZoningsondeUnsichtbar}
          </button>
        </div>
        <button
          className={styles.meldeKnopf}
          onClick={() =>
            buildZoningProbe(
              zoningUnsichtbar ? zoningRoad + " (unsichtbar)" : zoningRoad
            )
          }
        >
          <img src={icon("Lanes")} />
          <span>{t.debugZoningsondeBauen}</span>
        </button>
        <div className={styles.rowButtons}>
          <button className={styles.smallButton} onClick={measureZoningProbe}>
            {t.debugZoningsondeMessen}
          </button>
          <button className={styles.smallButton} onClick={cleanupZoningProbe}>
            {t.debugZoningsondeLoeschen}
          </button>
        </div>
      </Spalte>

      {/*
        DER PREFAB-SEZIERER.

        Er misst nicht die Geometrie, sondern das SPIEL: woraus eine echte
        Parkanlage besteht. Auftrag des Nutzers am 2026-08-25, damit der
        Parkplatz endlich mit der Simulation zusammenarbeitet - Kapazitaet,
        Auslastung, Infoansicht "Parken".

        Bewusst hier und nicht im Melde-Reiter: er baut nichts, er schreibt
        nur einen Abzug. Deshalb auch kein Zustand und kein Abbrechen.
      */}
      <Spalte
        title={t.debugSezieren}
        ton="Melden"
        breit
        bereichTooltip={t.tooltipDebugSezieren}
      >
        <div className={styles.explain}>{t.debugSeziererHinweis}</div>
        <button className={styles.meldeKnopf} onClick={dissectPrefabs}>
          <img src={icon("DiskSave")} />
          <span>{t.debugSeziererStart}</span>
        </button>
      </Spalte>

      {/*
        DER TRAEGERTEST - ein Versuch, kein Bauschritt.

        Er beantwortet die eine Frage, an der der naechste Umbau haengt:
        bleibt ein Aufkleber liegen, wenn er an einer nackten Traeger-Entity
        haengt? Zwei andere Wege sind am Code gescheitert; dieser ist aus
        AttachPositionSystem abgeleitet und noch Hypothese.

        Zwei Knoepfe, weil zwischen ihnen etwas passieren muss: hovern,
        bauen, speichern, laden.
      */}
      <Spalte
        title={t.debugTraeger}
        ton="Melden"
        breit
        bereichTooltip={t.tooltipDebugTraeger}
      >
        <div className={styles.explain}>{t.debugTraegerHinweis}</div>
        <button className={styles.meldeKnopf} onClick={toggleCarrierTest}>
          <img src={icon(traegerLaeuft ? "Checkmark" : "Dot")} />
          <span>
            {traegerLaeuft ? t.debugTraegerAbschliessen : t.debugTraegerStart}
          </span>
        </button>
        {traegerstand !== "" ? (
          <div className={styles.statusZeile}>
            <span className={styles.statusPunkt} />
            <span className={styles.statusText}>{traegerstand}</span>
          </div>
        ) : null}
      </Spalte>

      {/* DER LIVE-LOG.

        Er steht hier und nicht bei den Bau-Einstellungen, weil er nichts
        baut: er misst. Aus, bis jemand ihn einschaltet - ein Dauerlog waere
        nach einer Stunde unlesbar. Der Pfad steht daneben, sobald er laeuft;
        ohne ihn muesste man raten, welche Datei gemeint ist.
      */}
      <Spalte
        title={t.debugUeberlappung}
        ton="Melden"
        breit
        bereichTooltip={t.tooltipDebugUeberlappung}
      >
        <div className={styles.explain}>{t.debugUeberlappungHinweis}</div>
        <button className={styles.meldeKnopf} onClick={ueberlappungMessen}>
          <img src={icon("Lanes")} />
          <span>{t.debugUeberlappungStart}</span>
        </button>
        {ueberlappungsstand !== "" ? (
          <div className={styles.statusZeile}>
            <span className={styles.statusPunkt} />
            <span className={styles.statusText}>{ueberlappungsstand}</span>
          </div>
        ) : null}
      </Spalte>

      <Spalte
        title={t.debugPrefabvergleich}
        ton="Melden"
        breit
      >
        <div className={styles.explain}>{t.debugPrefabvergleichHinweis}</div>
        <button className={styles.meldeKnopf} onClick={prefabsVergleichen}>
          <img src={icon("Lanes")} />
          <span>{t.debugPrefabvergleichStart}</span>
        </button>
      </Spalte>

      <Spalte
        title={t.debugLiveLog}
        ton="Melden"
        breit
        bereichTooltip={t.tooltipDebugLiveLog}
      >
        <div className={styles.explain}>{t.debugLiveLogHinweis}</div>
        <button className={styles.meldeKnopf} onClick={liveLogUmschalten}>
          <img src={icon(liveAn ? "Checkmark" : "Dot")} />
          <span>{liveAn ? t.debugLiveLogAus : t.debugLiveLogAn}</span>
        </button>
        {livePfad !== "" ? (
          <div className={styles.pathBox}>{livePfad}</div>
        ) : null}
      </Spalte>

      <Spalte title={t.debugSondeErgebnis} ton="Melden" breit>
        {ergebnisse.length === 0 ? (
          <div className={styles.explain}>{t.debugNochNichts}</div>
        ) : (
          ergebnisse.map((zeile, i) => (
            <div key={i} className={styles.pathBox}>{zeile}</div>
          ))
        )}
      </Spalte>
    </div>
  );
};
