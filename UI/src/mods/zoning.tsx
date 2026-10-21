import { useLayoutEffect, useRef, useState } from "react";
import { useValue } from "cs2/api";
import styles from "./panel.module.scss";
import {
  zoningModus$, setZoningModus, zoningWinkelmodus$, setZoningWinkelmodus,
  zoningSeitenModus$, setZoningSeitenModus, zoningSeitenMoeglich$,
  zoningRandModus$, setZoningRandModus,
  zoningWinkel$, setZoningWinkel, zoningFlaechen$, zoningParzellen$,
  zoningZug$, zoningLinienwahl$, setZoningLinienwahl,
  zoningAusrichtwinkel$, zoningAuswahl$,
  surfaceZoning$, setSurfaceZoning, surfaceList$,
  zoningAussentiefe$, setZoningAussentiefe, zoningVerloren$,
  zoningPlatzierart$, setZoningPlatzierart,
  zoningFestBreite$, zoningFestTiefe$, setZoningFestGroesse,
  zoningVorlagen$, speichereZoningVorlage, entferneZoningVorlage,
  zoningGrenzeBreite$, zoningGrenzeTiefe$,
} from "./bindings";
import { Auswahl, Flaeche, MitTooltip, ModeChooser, Slider, Spalte, SpaltenGruppe, TooltipKnopf }
  from "./controls";
import { useTexte } from "./texte";
import ziehenSymbol from "../assets/zoning-ziehen.svg";
import festSymbol from "../assets/zoning-fest.svg";
import anleitungBild from "../assets/zoning-anleitung.svg";
import randBild from "../assets/zoning-rand.svg";
import seitenBild from "../assets/zoning-seiten.svg";

/**
 * Der Zoning-Reiter (Aufbau vom 21.10.2026, Entwurf mit dem Nutzer).
 *
 * Drei Bereiche: "Zoning" mit Setzen und den beiden Strassenwerkzeugen,
 * die Groesse fuer den Werkzeugmodus "Fixed size" (bei "Drag size" steht
 * dort eine kurze Bildanleitung) und alles, was die Parzellen selbst
 * betrifft - Winkel, Aussenband, Boden - untereinander in einem Bereich.
 * Welcher Werkzeugmodus gilt, waehlt das kleine Fenster rechts ueber dem
 * Panel, solange gesetzt wird (`ZoningWerkzeugfenster`).
 *
 * Der Winkel hat eigene Werte, aber dieselben Modi wie der Reihenwinkel -
 * Ansage des Nutzers: *"Ich wäre für die Modi, die wir bei den Straßen
 * schon haben."*
 */
export const ZoningTab = () => {
  const t = useTexte();
  const modus = useValue(zoningModus$);
  const seitenModus = useValue(zoningSeitenModus$);
  const randModus = useValue(zoningRandModus$);
  const seitenMoeglich = useValue(zoningSeitenMoeglich$);
  const winkelmodus = useValue(zoningWinkelmodus$);
  const winkel = useValue(zoningWinkel$);
  const flaechen = useValue(zoningFlaechen$);
  const parzellen = useValue(zoningParzellen$);
  const zug = useValue(zoningZug$);
  const linienwahl = useValue(zoningLinienwahl$);
  const bezug = useValue(zoningAusrichtwinkel$);
  const auswahl = useValue(zoningAuswahl$);
  const flaeche = useValue(surfaceZoning$);
  const aussentiefe = useValue(zoningAussentiefe$);
  const verloren = useValue(zoningVerloren$);
  const fest = useValue(zoningPlatzierart$) === "fest";
  const flaechenliste: Flaeche[] = useValue(surfaceList$)
    .split("\n")
    .filter((zeile) => zeile !== "")
    .map((zeile) => {
      const teile = zeile.split("\t");
      return { name: teile[0], bild: teile[1] || "", titel: teile[2] || teile[0] };
    });
  const hatBezug = !Number.isNaN(bezug);
  return (
    <SpaltenGruppe>
      <Spalte title={t.zoningReiter} ton="Zuschnitt" breit>
        <div className={styles.explain}>{t.zoningHinweis}</div>
        {/* `meldeKnopfAn` faerbt den Knopf, solange der Modus laeuft - der
            Zustand steht damit in der Form und nicht nur im Text. */}
        <TooltipKnopf
          text={t.tooltipZoningSetzen}
          className={`${styles.meldeKnopf} ${modus ? styles.meldeKnopfAn : ""}`}
          onClick={() => setZoningModus(!modus)}
        >
          {modus ? t.zoningSetzenAus : t.zoningSetzen}
        </TooltipKnopf>
        {/* ZWEI KNOEPFE STATT EINES WERKZEUGS MIT ZWEI AUFGABEN (21.10.).
            Vorher entschied der Abstand zum Zeiger, ob eine Umrisslinie
            oder eine Zoningstrasse gemeint war. Cohtml wertet `:disabled`
            nicht aus - Klasse, Klickschutz und aria-disabled sperren
            gemeinsam. */}
        <div className={styles.zoningKnopfPaar}>
          <TooltipKnopf
            text={seitenMoeglich ? t.tooltipZoningRandzoning : t.tooltipZoningSeitenGesperrt}
            className={`${styles.meldeKnopf} ${styles.zoningKnopfHaelfte}
              ${randModus ? styles.meldeKnopfAn : ""}
              ${seitenMoeglich ? "" : styles.meldeKnopfAus}`}
            aria-disabled={!seitenMoeglich}
            onClick={() => { if (seitenMoeglich) setZoningRandModus(!randModus); }}
          >
            {t.zoningRandzoning}
          </TooltipKnopf>
          <TooltipKnopf
            text={seitenMoeglich ? t.tooltipZoningSeiten : t.tooltipZoningSeitenGesperrt}
            className={`${styles.meldeKnopf} ${styles.zoningKnopfHaelfte}
              ${seitenModus ? styles.meldeKnopfAn : ""}
              ${seitenMoeglich ? "" : styles.meldeKnopfAus}`}
            aria-disabled={!seitenMoeglich}
            onClick={() => { if (seitenMoeglich) setZoningSeitenModus(!seitenModus); }}
          >
            {t.zoningSeiten}
          </TooltipKnopf>
        </div>
        {seitenMoeglich ? null : (
          <div className={styles.explain}>{t.zoningSeitenHinweis}</div>
        )}
        {/* AUSSENBAND unter "Zoning" (Nutzer 21.10.): es gehoert zum Werkzeug
            "Inner zoning" daneben. SECHS KNOEPFE fuer die Tiefe des
            naechsten angeklickten Bands (Nutzer 2026-09-21). Ab 2: eine
            einzelne Reihe loescht CS2 immer mit (21.10.). AUSGEGRAUT, solange
            weder Edge zoning noch Inner zoning laeuft (Nutzer 21.10.) - nur
            dort setzt ein Klick ein Band. */}
        <ModeChooser
          label={t.zoningSeite}
          tooltip={randModus || seitenModus ? t.tooltipZoningAussentiefe : t.tooltipZoningAussentiefeGesperrt}
          disabled={!(randModus || seitenModus)}
          value={String(aussentiefe)}
          ton="Zuschnitt"
          options={[2, 3, 4, 5, 6].map((n) => ({
            id: String(n),
            text: String(n),
            tooltip: t.tooltipZoningTiefeKnopf(n),
          }))}
          onChange={(id) => setZoningAussentiefe(Number(id))}
        />
        <div className={styles.explain}>{t.zoningTiefeKlick}</div>
        {/* Nur wenn es sie gibt: gelbe Kacheln mit X setzt CS2 nicht. */}
        {verloren > 0 ? (
          <div className={styles.zoningHinweisWarn}>{t.zoningVerloren}</div>
        ) : null}
        {/* WAEHREND DES ZIEHENS steht hier, was entsteht; danach wieder der
            Bestand. Zwei Zahlen gleichzeitig waeren zwei Wahrheiten. */}
        <div className={styles.explain}>
          {zug !== ""
            ? zug
            : auswahl >= 0
              ? t.zoningBestandGewaehlt(flaechen, parzellen, auswahl + 1)
              : t.zoningBestand(flaechen, parzellen)}
        </div>
      </Spalte>

      {/* DER MITTLERE BEREICH ERKLAERT DAS AKTIVE WERKZEUG (Nutzer 21.10.):
          Edge zoning und Zoning road sides je mit eigener Zeichnung, sonst
          das Setzen - "Drag size" als Zeichnung, "Fixed size" mit der Groesse. */}
      {randModus ? (
        <Spalte title={t.zoningWieRand} ton="Zuschnitt" breit>
          <Anleitung bild={randBild} schritte={[t.zoningRandSchritt1, t.zoningRandSchritt2, t.zoningRandSchritt3]} />
        </Spalte>
      ) : seitenModus ? (
        <Spalte title={t.zoningWieSeiten} ton="Zuschnitt" breit>
          <Anleitung bild={seitenBild} schritte={[t.zoningSeitenSchritt1, t.zoningSeitenSchritt2, t.zoningSeitenSchritt3]} />
        </Spalte>
      ) : (
        <Spalte title={fest ? t.zoningGroesse : t.zoningWieZiehen} ton="Zuschnitt" breit>
          {fest ? <Groessenwahl /> : (
            <Anleitung bild={anleitungBild}
              schritte={[t.zoningZiehenSchritt1, t.zoningZiehenSchritt2, t.zoningZiehenSchritt3]} />
          )}
        </Spalte>
      )}

      <Spalte title={t.zoningParzellen} ton="Zuschnitt" breit>
        <ModeChooser
          label={t.zoningWinkel}
          tooltip={t.tooltipZoningWinkel}
          /* ENTWEDER ODER, auch sichtbar: waehrend der Linienwahl ist KEIN
             Modus hervorgehoben (Nutzer: "enorm verwirrend, wenn mehrere
             Buttons highlighted sind"). */
          value={linienwahl ? "" : winkelmodus}
          ton="Zuschnitt"
          options={[
            {
              id: "edge",
              text: hatBezug ? t.winkelNormal : t.winkelKante,
              tooltip: hatBezug ? t.tooltipWinkelNormal : t.tooltipWinkelKante,
            },
            { id: "quer", text: t.winkelQuer, tooltip: t.tooltipWinkelQuer },
            { id: "fixed", text: t.winkelFest, tooltip: t.tooltipWinkelFest },
          ]}
          onChange={setZoningWinkelmodus}
          /* Die Linie ist kein Modus, sondern startet eine Auswahl -
             deshalb ein eigener Knopf im selben Stil. */
          extra={
            <TooltipKnopf
              text={linienwahl ? t.tooltipZoningLinieAbbrechen : t.tooltipZoningLinie}
              className={`${styles.mode} ${linienwahl ? styles.modeAktivZuschnitt : ""}`}
              onClick={() => setZoningLinienwahl(!linienwahl)}
            >
              {linienwahl ? t.zoningLinieAbbrechen : t.zoningLinie}
            </TooltipKnopf>
          }
        />
        {/* Der Regler UNTER den Knoepfen (Nutzer 21.10.): spart die Breite. */}
        <Slider
          label={t.winkel}
          tooltip={t.tooltipWinkel}
          value={winkel}
          min={0}
          max={180}
          step={1}
          unit={t.einheitGradZeichen}
          ton="Zuschnitt"
          onChange={setZoningWinkel}
          disabled={winkelmodus !== "fixed"}
        />
        {/* NUR DER BODEN UNTER DEN PARZELLEN; die Zoning-Strasse bleibt
            Fahrflaeche wie alle unsere Strassen. */}
        <Auswahl
          scrollSchluessel="flaeche.bauland" surfaceSlot="zoning"
          label={t.zoningFlaeche}
          tooltip={t.tooltipZoningFlaeche}
          value={flaeche}
          options={flaechenliste}
          ton="Zuschnitt"
          onChange={setSurfaceZoning}
          ausLabel={t.zoningFlaecheAus}
        />
        <div className={styles.explain}>{t.zoningFlaecheHinweis}</div>
      </Spalte>
    </SpaltenGruppe>
  );
};

/**
 * DAS KLEINE WERKZEUGFENSTER RECHTS UEBER DEM PANEL (Nutzer 21.10.).
 *
 * Nur die beiden Werkzeugmodi und ein Satz, was in diesem Modus zu tun
 * ist - so klein, dass es nichts vom Spiel verdeckt. Es steht nur da,
 * solange gesetzt wird; die Wahl selbst bleibt gespeichert.
 */
export const ZoningWerkzeugfenster = ({ hochkant, haltAn }: {
  hochkant: boolean;
  haltAn: (e: any) => void;
}) => {
  const t = useTexte();
  const fest = useValue(zoningPlatzierart$) === "fest";
  /* WO PLATZ IST - gemessen NACH dem Layout am Panel (Elternelement) und am
     eigenen Fenster, abgesichert wie in rahmen.tsx: fehlt die Messung in
     einem Cohtml-Zustand, bleibt die Grundlage stehen (Codex 21.10.). */
  const ref = useRef<HTMLDivElement>(null);
  const [lage, setLage] = useState<"oben" | "unten" | "rechts" | "links">(hochkant ? "rechts" : "oben");
  useLayoutEffect(() => {
    try {
      const fenster = ref.current;
      const panel = fenster?.parentElement;
      if (!fenster || !panel) return;
      const p = panel.getBoundingClientRect();
      const f = fenster.getBoundingClientRect();
      const neu = hochkant
        ? (p.right + f.width + 16 > window.innerWidth ? "links" : "rechts")
        : (p.top - f.height - 16 < 0 ? "unten" : "oben");
      if (neu !== lage) setLage(neu);
    } catch { /* ohne Messung gilt die Grundlage */ }
  });
  return (
    <div
      ref={ref}
      className={`${styles.werkzeugfenster} ${
        lage === "unten" ? styles.werkzeugfensterUnten
          : lage === "rechts" ? styles.werkzeugfensterRechts
          : lage === "links" ? styles.werkzeugfensterLinks : ""}`}
      onMouseDown={haltAn}
    >
      <div className={styles.werkzeugfensterKnoepfe}>
        <TooltipKnopf
          text={t.tooltipWerkzeugZiehen}
          className={`${styles.werkzeugKnopf} ${fest ? "" : styles.werkzeugKnopfAn}`}
          aria-pressed={!fest}
          onClick={() => setZoningPlatzierart("ziehen")}
        >
          <img className={styles.werkzeugSymbol} src={ziehenSymbol} alt="" />
          {t.werkzeugZiehen}
        </TooltipKnopf>
        <TooltipKnopf
          text={t.tooltipWerkzeugFest}
          className={`${styles.werkzeugKnopf} ${fest ? styles.werkzeugKnopfAn : ""}`}
          aria-pressed={fest}
          onClick={() => setZoningPlatzierart("fest")}
        >
          <img className={styles.werkzeugSymbol} src={festSymbol} alt="" />
          {t.werkzeugFest}
        </TooltipKnopf>
      </div>
      <div className={styles.werkzeugfensterText}>
        {fest ? t.werkzeugErklaerungFest : t.werkzeugErklaerungZiehen}
      </div>
    </div>
  );
};

/** Kurze Bildanleitung zum aktiven Werkzeug: Zeichnung und drei Schritte. */
const Anleitung = ({ bild, schritte }: { bild: string; schritte: string[] }) => (
  <div className={styles.zoningAnleitung}>
    <img className={styles.zoningGrafik} src={bild} alt="" />
    <div className={styles.zoningSchritte}>
      {schritte.map((text, i) => (
        <div key={i} className={styles.zoningSchritt}>
          <span className={styles.zoningSchrittNr}>{i + 1}</span>
          <span className={styles.zoningSchrittText}>{text}</span>
        </div>
      ))}
    </div>
  </div>
);

/**
 * BREITE x TIEFE FUER "FIXED SIZE" (Nutzer 21.10.).
 *
 * Eintippen oder Pfeile neben der Zahl. Nie ueber der Grenze aus den
 * Mod-Einstellungen und nie unter 1 - geht ein Wert darueber hinaus, wird
 * er begrenzt und der Hinweis sagt, wo die Grenze steht. Gespeichert wird
 * im Werkzeug (Optionen), also auch ueber einen Neustart.
 */
const Groessenwahl = () => {
  const t = useTexte();
  const breite = useValue(zoningFestBreite$);
  const tiefe = useValue(zoningFestTiefe$);
  const grenzeBreite = useValue(zoningGrenzeBreite$);
  const grenzeTiefe = useValue(zoningGrenzeTiefe$);
  const vorlagen = useValue(zoningVorlagen$)
    .split(";")
    .map((v) => v.split("x").map(Number))
    .filter((v) => v.length === 2 && v[0] >= 1 && v[1] >= 1);
  const [hinweis, setHinweis] = useState("");
  return (
    <>
      <div className={styles.explain}>{t.zoningGroesseHinweis}</div>
      <div className={styles.zoningGroesseZeile}>
        <Zahlenfeld
          label={t.zoningBreite} tooltip={t.tooltipZoningBreite}
          wert={breite} grenze={grenzeBreite}
          zuGross={t.zoningZuBreit(grenzeBreite)} mindestens={t.zoningMindestens}
          plus={t.zoningBreitePlus} minus={t.zoningBreiteMinus}
          onHinweis={setHinweis}
          onSetze={(n) => setZoningFestGroesse(n, tiefe)}
        />
        <span className={styles.zoningMal}>×</span>
        <Zahlenfeld
          label={t.zoningTiefe} tooltip={t.tooltipZoningTiefe}
          wert={tiefe} grenze={grenzeTiefe}
          zuGross={t.zoningZuTief(grenzeTiefe)} mindestens={t.zoningMindestens}
          plus={t.zoningTiefePlus} minus={t.zoningTiefeMinus}
          onHinweis={setHinweis}
          onSetze={(n) => setZoningFestGroesse(breite, n)}
        />
      </div>
      <div className={styles.zoningMeter}>{t.zoningGroesseMeter(breite, tiefe)}</div>
      <div className={hinweis ? styles.zoningHinweisWarn : styles.explain}>
        {hinweis || t.zoningGrenze(grenzeBreite, grenzeTiefe)}
      </div>
      <div className={styles.zoningVorlagen}>
        <span className={styles.zoningVorlagenText}>{t.zoningGespeichert}</span>
        {vorlagen.map((v) => (
          <TooltipKnopf key={v[0] + "x" + v[1]} text={t.tooltipZoningVorlage}
            className={styles.zoningVorlage}
            onClick={() => {
              /* Ueber einer inzwischen kleineren Grenze: klemmen, aber sagen. */
              setHinweis(v[0] > grenzeBreite ? t.zoningZuBreit(grenzeBreite)
                : v[1] > grenzeTiefe ? t.zoningZuTief(grenzeTiefe) : "");
              setZoningFestGroesse(Math.min(v[0], grenzeBreite), Math.min(v[1], grenzeTiefe));
            }}
            onMouseDown={(e: any) => {
              if (e.button !== 2) return;
              e.preventDefault(); e.stopPropagation();
              entferneZoningVorlage(v[0] + "x" + v[1]);
            }}
          >
            {v[0]} × {v[1]}
          </TooltipKnopf>
        ))}
        <TooltipKnopf text={t.tooltipZoningSpeichern}
          className={`${styles.zoningVorlage} ${styles.zoningVorlageNeu}`}
          onClick={speichereZoningVorlage}
        >
          {t.zoningSpeichern}
        </TooltipKnopf>
      </div>
    </>
  );
};

/**
 * EIN ZAHLENFELD MIT PFEILEN.
 *
 * Wie das Gebuehrenfeld der Liste: waehrend des Tippens gilt ein Entwurf,
 * uebernommen wird bei Enter oder beim Verlassen. Tastendruecke bleiben im
 * Feld, sonst loesen Ziffern Spielkuerzel aus; Pfeil hoch/runter zaehlen.
 */
const Zahlenfeld = ({ label, tooltip, wert, grenze, zuGross, mindestens,
                      plus, minus, onHinweis, onSetze }: {
  label: string; tooltip: string; wert: number; grenze: number;
  zuGross: string; mindestens: string; plus: string; minus: string;
  onHinweis: (text: string) => void; onSetze: (n: number) => void;
}) => {
  /* DER ENTWURF STEHT IN EINER REF, nicht nur im Zustand: Escape loescht
     ihn, bevor `blur()` den Abschluss ausloest - sonst uebernaehme der
     noch alte Zustand den verworfenen Text (Codex 21.10., gleiches Muster
     wie das Gebuehrenfeld der Liste). */
  const entwurf = useRef<string | null>(null);
  const [text, setText] = useState<string | null>(null);
  const setzeEntwurf = (wertNeu: string | null) => { entwurf.current = wertNeu; setText(wertNeu); };
  const uebernehmen = (roh: string) => {
    const n = parseInt(roh, 10);
    setzeEntwurf(null);
    if (Number.isNaN(n) || n < 1) { onHinweis(mindestens); onSetze(1); return; }
    if (n > grenze) { onHinweis(zuGross); onSetze(grenze); return; }
    onHinweis(""); onSetze(n);
  };
  /* Pfeile zaehlen vom gerade Eingetippten aus, wenn es eine Zahl ist. */
  const schritt = (d: number) => {
    const getippt = entwurf.current === null ? NaN : parseInt(entwurf.current, 10);
    const basis = Number.isNaN(getippt) ? wert : getippt;
    setzeEntwurf(null);
    const n = basis + d;
    if (n > grenze) { onHinweis(zuGross); onSetze(grenze); return; }
    if (n < 1) { onHinweis(mindestens); onSetze(1); return; }
    onHinweis(""); onSetze(n);
  };
  return (
    <div className={styles.zahlenfeld}>
      <MitTooltip text={tooltip}>
        <span className={styles.zahlenfeldLabel}>{label}</span>
      </MitTooltip>
      <div className={styles.zahlenfeldRahmen}>
        <input type="text" className={styles.zahlenfeldEingabe} aria-label={label}
          value={text ?? String(wert)}
          onFocus={(e) => { setzeEntwurf(String(wert)); e.target.select(); }}
          onChange={(e) => setzeEntwurf(e.target.value.replace(/[^0-9]/g, "").slice(0, 3))}
          onBlur={() => { if (entwurf.current !== null) uebernehmen(entwurf.current); }}
          onKeyDown={(e) => {
            e.stopPropagation();
            if (e.key === "Enter") {
              e.preventDefault(); uebernehmen(entwurf.current ?? String(wert)); e.currentTarget.blur();
            }
            if (e.key === "Escape") { e.preventDefault(); setzeEntwurf(null); e.currentTarget.blur(); }
            if (e.key === "ArrowUp") { e.preventDefault(); schritt(1); }
            if (e.key === "ArrowDown") { e.preventDefault(); schritt(-1); }
          }} />
        {/* Die Pfeile nehmen dem Feld den Fokus nicht: sonst uebernaehme der
            Blur den Entwurf, bevor der Klick ihn zaehlen kann (Codex). */}
        <div className={styles.zahlenfeldPfeile}>
          <TooltipKnopf text={plus} className={styles.zahlenfeldPfeil}
            onMouseDown={(e: any) => e.preventDefault()} onClick={() => schritt(1)}>
            <span className={styles.zahlenfeldGlyph}
                  style={{ maskImage: "url(Media/Glyphs/ThickStrokeArrowUp.svg)" }} />
          </TooltipKnopf>
          <TooltipKnopf text={minus} className={styles.zahlenfeldPfeil}
            onMouseDown={(e: any) => e.preventDefault()} onClick={() => schritt(-1)}>
            <span className={styles.zahlenfeldGlyph}
                  style={{ maskImage: "url(Media/Glyphs/ThickStrokeArrowDown.svg)" }} />
          </TooltipKnopf>
        </div>
      </div>
    </div>
  );
};
