import styles from "./panel.module.scss";
import { MitTooltip, Spalte } from "./controls";
import {
  absturzBefund$, absturzErkannt$, baubefund$, baukurzinfo$,
  clearMarkers, icon, markerCount$,
  markerMode$, meldungAbsturz, meldungBau, meldungOrdner, meldungPfad$,
  meldungVorschau, removeLastMarker, reportPath$, setMarkerMode, writeReport,
} from "./bindings";
import { useValue } from "cs2/api";
import { useTexte } from "./texte";

/**
 * Der Reiter "Fehler melden".
 *
 * WOFUER: Wer den Mod benutzt und nach dem Bauen etwas Krummes sieht, soll
 * das melden koennen. Vorher lag dieser Weg auf Alt+M und Alt+P - Tasten,
 * die niemand kennt, der den Mod aus dem Workshop laedt. Ein Meldeweg, den
 * man nicht findet, wird nicht benutzt.
 *
 * Der Ablauf steht als drei Spalten NEBENEINANDER, in der Reihenfolge, in
 * der man sie geht - dieselbe Leiste, dieselbe Hoehe wie der Entwurf. Als
 * die Ansicht noch ein hohes Fenster war, standen sie untereinander.
 */
export const ReportTab = () => {
  const t = useTexte();
  const markerMode = useValue(markerMode$);
  const markerCount = useValue(markerCount$);
  const reportPath = useValue(reportPath$);
  const absturzErkannt = useValue(absturzErkannt$);
  const absturzBefund = useValue(absturzBefund$);
  const meldungPfad = useValue(meldungPfad$);
  const baubefund = useValue(baubefund$);
  const baukurzinfo = useValue(baukurzinfo$);
  const hasMarkers = markerCount > 0;

  return (
    <div className={styles.spaltenGruppe}>
      <Spalte title={t.schritt1} ton="Melden" breit>
        {/* Der Schalter traegt seinen Zustand im Text, nicht nur in der
            Farbe. */}
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
        {reportPath ? (
          <div className={styles.pathBox}>{t.geschriebenNach(reportPath)}</div>
        ) : (
          <div className={styles.explain}>{t.berichtOrt}</div>
        )}
      </Spalte>

      {/*
        DER WEG FUER EINEN TESTER.
        Die drei Spalten darueber sind der ausfuehrliche Weg: markieren,
        beschreiben, Bericht schreiben. Der hier ist der kurze - ein Klick,
        eine Datei. Beides nebeneinander, weil beides seinen Fall hat: wer
        eine STELLE zeigen will, markiert; wer einen Absturz oder eine krumme
        Vorschau meldet, drueckt einen Knopf.
      */}
      <Spalte title={t.meldungTitel} ton="Melden" breit>
        {absturzErkannt ? (
          <>
            <div className={styles.explain}>
              {t.meldungAbsturzTitel}
              {absturzBefund ? " " + absturzBefund : ""}
            </div>
            <button
              className={`${styles.meldeKnopf} ${styles.meldeKnopfAn}`}
              onClick={meldungAbsturz}
            >
              <img src={icon("DiskSave")} />
              <span>{t.meldungAbsturzKnopf}</span>
            </button>
          </>
        ) : null}

        <div className={styles.rowButtons}>
          <button className={styles.smallButton} onClick={meldungVorschau}>
            {t.meldungVorschauKnopf}
          </button>
          <button className={styles.smallButton} onClick={meldungBau}>
            {t.meldungBauKnopf}
          </button>
          <button className={styles.smallButton} onClick={meldungOrdner}>
            {t.meldungOrdnerKnopf}
          </button>
        </div>

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
