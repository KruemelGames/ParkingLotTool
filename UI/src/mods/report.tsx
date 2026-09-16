import styles from "./panel.module.scss";
import { MitTooltip, Spalte } from "./controls";
import {
  absturzBefund$, absturzErkannt$, baubefund$, baukurzinfo$,
  clearMarkers, icon, markerCount$, meldeLotWahl$,
  markerMode$, meldungAbsturz, meldungBau, meldungOrdner, meldungPfad$,
  meldungVorschau, removeLastMarker, reportPath$, schalteMeldeLotWahl,
  setMarkerMode, writeReport,
} from "./bindings";
import { useValue } from "cs2/api";
import { useTexte } from "./texte";

/**
 * Die drei Schritte des ausfuehrlichen Meldewegs: markieren, zaehlen,
 * Bericht schreiben.
 *
 * STEHEN SEIT DEM 2026-09-15 IM DEBUG-REITER. Ansage des Nutzers: *"1., 2.,
 * 3. und der Content dazu muessen auch noch in die Dev-Debug-Reiter. Das
 * heisst 'Send a Report' ist alleine drin."*
 *
 * Der Grund leuchtet ein: wer einen Fehler meldet, will einen Knopf
 * druecken. Ein dreistufiger Ablauf mit Markierungen davor sieht nach Arbeit
 * aus und schreckt genau die Leute ab, deren Meldung man braucht. Fuer die
 * Entwicklung bleibt er wertvoll - dort steht er jetzt.
 */
export const MarkierSpalten = () => {
  const t = useTexte();
  const markerMode = useValue(markerMode$);
  const markerCount = useValue(markerCount$);
  const reportPath = useValue(reportPath$);
  const hasMarkers = markerCount > 0;

  return (
    <>
      <Spalte title={t.schritt1} ton="Melden" breit>
        {/* Der Schalter traegt seinen Zustand im Text, nicht nur in der
            Farbe. */}
        <MitTooltip text={t.tooltipStellenMarkieren}>
        <button
          className={`${styles.meldeKnopf} ${markerMode ? styles.meldeKnopfAn : ""}`}
          onClick={() => setMarkerMode(!markerMode)}
        >
          <img src={icon(markerMode ? "Checkmark" : "Dot")} />
          <span>
            {markerMode
              ? t.markierenLaeuft
              : t.stellenMarkieren}
          </span>
        </button>
        </MitTooltip>
        <div className={styles.explain}>
          {markerMode ? t.meldeKlickhinweis : t.meldeEinleitung}
        </div>
      </Spalte>

      <Spalte title={t.schritt2} ton="Melden" breit>
        <div className={styles.controlHead}>
          <span className={styles.label}>{t.gesetzt}</span>
          <span className={`${styles.value} ${styles.titelMelden}`}>
            {markerCount}
          </span>
        </div>
        <div className={styles.rowButtons}>
          <MitTooltip text={t.tooltipLetzteZurueck}>
            <button
              className={styles.smallButton}
              aria-label={t.tooltipLetzteZurueck}
              title={t.tooltipLetzteZurueck}
              onClick={removeLastMarker}
            >
              {t.letzteZurueck}
            </button>
          </MitTooltip>
          <MitTooltip text={t.tooltipAlleLoeschen}>
            <button
              className={styles.smallButton}
              aria-label={t.tooltipAlleLoeschen}
              title={t.tooltipAlleLoeschen}
              onClick={clearMarkers}
            >
              {t.alleLoeschen}
            </button>
          </MitTooltip>
        </div>
      </Spalte>

      <Spalte title={t.schritt3} ton="Melden" breit>
        {/* Ohne Markierung waere der Bericht leer, deshalb sagt der Knopf
            das, statt still eine nutzlose Datei zu schreiben. */}
        <MitTooltip text={t.tooltipBerichtSchreiben}>
        <button
          className={`${styles.meldeKnopf} ${hasMarkers ? styles.meldeKnopfAn : ""}`}
          onClick={writeReport}
        >
          <img src={icon("DiskSave")} />
          <span>
            {hasMarkers
              ? t.berichtMitAnzahl(markerCount)
              : t.berichtErstMarkieren}
          </span>
        </button>
        </MitTooltip>
        {reportPath ? (
          <div className={styles.pathBox}>{t.geschriebenNach(reportPath)}</div>
        ) : (
          <div className={styles.explain}>{t.berichtOrt}</div>
        )}
      </Spalte>
    </>
  );
};

/**
 * Der Reiter "Fehler melden".
 *
 * WOFUER: Wer den Mod benutzt und nach dem Bauen etwas Krummes sieht, soll
 * das melden koennen. Vorher lag dieser Weg auf Alt+M und Alt+P - Tasten,
 * die niemand kennt, der den Mod aus dem Workshop laedt. Ein Meldeweg, den
 * man nicht findet, wird nicht benutzt.
 *
 * EIN KNOPF, EINE DATEI. Der ausfuehrliche Weg mit Markierungen steht seit
 * dem 2026-09-15 im Debug-Reiter; hier bleibt, was ein Tester wirklich
 * braucht.
 */
export const ReportTab = () => {
  const t = useTexte();
  const absturzErkannt = useValue(absturzErkannt$);
  const absturzBefund = useValue(absturzBefund$);
  const meldungPfad = useValue(meldungPfad$);
  const baubefund = useValue(baubefund$);
  const baukurzinfo = useValue(baukurzinfo$);
  const lotWahl = useValue(meldeLotWahl$);

  return (
    <div className={styles.spaltenGruppe}>
      <Spalte title={t.meldungTitel} ton="Melden" breit>
        {absturzErkannt ? (
          <>
            <div className={styles.explain}>
              {t.meldungAbsturzTitel}
              {absturzBefund ? " " + absturzBefund : ""}
            </div>
            <MitTooltip text={t.tooltipMeldungAbsturz}>
            <button
              className={`${styles.meldeKnopf} ${styles.meldeKnopfAn}`}
              onClick={meldungAbsturz}
            >
              <img src={icon("DiskSave")} />
              <span>{t.meldungAbsturzKnopf}</span>
            </button>
            </MitTooltip>
          </>
        ) : null}

        {/*
          VIER KNOEPFE, EINE GRUPPE.

          Reihenfolge nach dem, was man meldet: erst die beiden, die den
          aktuellen Zustand nehmen (Vorschau, letzter Bau), dann der, der
          einen gebauten Parkplatz im Gelaende anklickt. "Ordner oeffnen"
          steht unten rechts - es meldet nichts, es zeigt nur, wo die Dateien
          liegen. Ausdruecklich so gewuenscht.

          Der Parkplatz-Knopf ist der einzige mit einem Zustand; er zeigt ihn
          in derselben Form wie die uebrigen Schalter des Reiters, damit er
          nicht aus der Gruppe faellt.
        */}
        <div className={styles.meldeGitter}>
          <div className={styles.meldeGitterKnopf}>
            <MitTooltip text={t.tooltipMeldungVorschau}>
            <button className={styles.smallButton} onClick={meldungVorschau}>
              {t.meldungVorschauKnopf}
            </button>
            </MitTooltip>
          </div>
          <div className={styles.meldeGitterKnopf}>
            <MitTooltip text={t.tooltipMeldungBau}>
            <button className={styles.smallButton} onClick={meldungBau}>
              {t.meldungBauKnopf}
            </button>
            </MitTooltip>
          </div>
          <div className={styles.meldeGitterKnopf}>
            <MitTooltip text={t.tooltipMeldeLot}>
            <button
              className={`${styles.smallButton} ${lotWahl ? styles.smallButtonSelected : ""}`}
              onClick={() => schalteMeldeLotWahl(!lotWahl)}
            >
              {lotWahl ? t.meldeLotWahlLaeuft : t.meldeLotKnopf}
            </button>
            </MitTooltip>
          </div>
          <div className={styles.meldeGitterKnopf}>
            <MitTooltip text={t.tooltipMeldungOrdner}>
            <button className={styles.smallButton} onClick={meldungOrdner}>
              {t.meldungOrdnerKnopf}
            </button>
            </MitTooltip>
          </div>
        </div>
        <div className={styles.explain}>{t.meldeLotErklaerung}</div>

        <div className={styles.explain}>{t.meldungVorschauErklaerung}</div>

        {/*
          WAS AUFFIEL - VOR DEM MELDEN.
          Ein Tester soll selbst sehen, ob etwas faul war. Steht hier nichts,
          ist das auch eine Auskunft: dann hat die Mod beim letzten Lauf
          nichts bemaengelt, und der Befund liegt woanders.

          Ungefiltert, anders als die Statusleiste: die soll beim Arbeiten
          nicht zutexten, hier traegt genau so ein Satz den Befund.
        */}
        <div className={styles.explain}>
          <b>{t.befundTitel}</b>
        </div>
        {baubefund ? (
          <div className={styles.pathBox}>
            {baubefund.split("\n").map((zeile, i) => (
              <div key={i}>{zeile}</div>
            ))}
          </div>
        ) : (
          <div className={styles.explain}>{t.befundNichts}</div>
        )}

        {baukurzinfo ? (
          <div className={styles.explain}>
            <b>{t.kurzinfoTitel}</b> · {baukurzinfo}
          </div>
        ) : null}
        {meldungPfad ? (
          <div className={styles.pathBox}>{t.meldungLiegtBei(meldungPfad)}</div>
        ) : (
          <div className={styles.explain}>{t.meldungErklaerung}</div>
        )}
      </Spalte>
    </div>
  );
};
