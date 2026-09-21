import { useValue } from "cs2/api";
import styles from "./panel.module.scss";
import {
  zoningModus$, setZoningModus, zoningWinkelmodus$, setZoningWinkelmodus,
  zoningSeitenModus$, setZoningSeitenModus, zoningSeitenMoeglich$,
  zoningWinkel$, setZoningWinkel, zoningFlaechen$, zoningParzellen$,
  zoningZug$, zoningLinienwahl$, setZoningLinienwahl,
  zoningAusrichtwinkel$, zoningAuswahl$,
  surfaceZoning$, setSurfaceZoning, surfaceList$,
  zoningAussentiefe$, setZoningAussentiefe,
} from "./bindings";
import { Auswahl, Flaeche, ModeChooser, Slider, Spalte, TooltipKnopf }
  from "./controls";
import { useTexte } from "./texte";

/**
 * Der Zoning-Reiter.
 *
 * Hier wird NICHTS gebaut. Der Reiter führt die gezogenen Parzellenfelder
 * als Entwurf und zeigt ihren Winkel; die Zoning-Straße und der Zonenblock
 * entstehen erst, wenn der Sondenlauf belegt hat, dass CS2 auf unseren
 * Zellen überhaupt Gebäude wachsen lässt. Andersherum stünde hier eine
 * Bedienung für etwas, das vielleicht gar nicht trägt.
 *
 * Der Winkel hat eigene Werte, aber dieselben vier Modi wie der
 * Reihenwinkel — Ansage des Nutzers: *"Ich wäre für die Modi, die wir bei
 * den Straßen schon haben."* Zwei Vokabeln für dieselbe Sache wären hier
 * die schlechtere Wahl.
 */
export const ZoningTab = () => {
  const t = useTexte();
  const modus = useValue(zoningModus$);
  const seitenModus = useValue(zoningSeitenModus$);
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
  /* Dieselbe Zerlegung wie im Layout-Reiter: eine Zeile je Flaeche, darin
     Name und Bildadresse durch einen Tabulator getrennt. */
  const flaechenliste: Flaeche[] = useValue(surfaceList$)
    .split("\n")
    .filter((zeile) => zeile !== "")
    .map((zeile) => {
      const teile = zeile.split("\t");
      return { name: teile[0], bild: teile[1] || "" };
    });
  /* Mit gewaehlter Linie heisst "Kante" nicht mehr "laengste Kante",
     sondern "entlang der Linie" - deshalb der andere Name. Genau wie beim
     Reihenwinkel; zwei Vokabeln fuer dieselbe Sache waeren hier falsch. */
  const hatBezug = !Number.isNaN(bezug);

  return (
    <div className={styles.spaltenGruppe}>
      <Spalte title={t.zoningReiter} ton="Zuschnitt" breit>
        <div className={styles.explain}>{t.zoningHinweis}</div>
        {/* `TooltipKnopf` statt `title`: in Cohtml zeigt das rohe
            HTML-Attribut nichts an. Und `meldeKnopfAn` faerbt den Knopf,
            solange der Modus laeuft - der Zustand steht damit in der Form
            und nicht nur im Text. */}
        <TooltipKnopf
          text={t.tooltipZoningSetzen}
          className={`${styles.meldeKnopf}
            ${modus ? styles.meldeKnopfAn : ""}`}
          onClick={() => setZoningModus(!modus)}
        >
          {modus ? t.zoningSetzenAus : t.zoningSetzen}
        </TooltipKnopf>
        {/* DER SEITENSCHALTER. Eigener Modus, weil er denselben Linksklick
            braucht wie das Ziehen - beides gleichzeitig ginge nicht. Die
            Seite waehlt man nicht im Panel, sondern durch den Klick auf die
            gemeinte Seite der Strasse; das Overlay zeigt vorher, welche
            gerade gemeint ist. */}
        {/* GESPERRT, SOLANGE NICHTS GEBAUT IST. Der Nutzer hat den Schalter
            in der Vorschau gesucht - ein naheliegender Irrtum, weil alles
            andere am Zoning dort arbeitet. Er kann es nicht: er aendert
            eine Eigenschaft an einer echten Strasse. Also sagt der Knopf
            es, statt es den Nutzer herausfinden zu lassen.
            Cohtml wertet `:disabled` nicht aus - Klasse, Klickschutz und
            aria-disabled sperren gemeinsam, wie bei den Reglerknoepfen. */}
        <TooltipKnopf
          text={seitenMoeglich
            ? t.tooltipZoningSeiten
            : t.tooltipZoningSeitenGesperrt}
          className={`${styles.meldeKnopf}
            ${seitenModus ? styles.meldeKnopfAn : ""}
            ${seitenMoeglich ? "" : styles.meldeKnopfAus}`}
          aria-disabled={!seitenMoeglich}
          onClick={() => {
            if (!seitenMoeglich) return;
            setZoningSeitenModus(!seitenModus);
          }}
        >
          {seitenModus ? t.zoningSeitenAus : t.zoningSeiten}
        </TooltipKnopf>
        {seitenMoeglich ? null : (
          <div className={styles.explain}>{t.zoningSeitenHinweis}</div>
        )}
        {/* WAEHREND DES ZIEHENS steht hier, was entsteht - und an der
            Grenze, warum es nicht weiter waechst. Sobald losgelassen wird,
            faellt die Zeile weg und der Bestand steht wieder da; zwei
            Zahlen gleichzeitig waeren zwei Wahrheiten. */}
        <div className={styles.explain}>
          {zug !== ""
            ? zug
            : `${flaechen} ${t.zoningStand} · ${parzellen} ${t.zoningParzellen}`
              + (auswahl >= 0 ? ` · ${t.zoningGewaehlt} ${auswahl + 1}` : "")}
        </div>
      </Spalte>

      <Spalte title={t.zoningSeite} ton="Zuschnitt" breit>
        <div className={styles.explain}>{t.zoningSeiteHinweis}</div>
        {/* SECHS KNOEPFE, KEIN WAEHLER UND KEIN ZAEHLER.
            Welche Seite gemeint ist, sagt der Klick auf die Aussenseite im
            Seitenmodus - dafuer braucht es keine Auswahl im Panel. Hier
            steht nur, wie tief das naechste angeklickte Band wird. Der
            Nutzer am 2026-09-21: *"einfach nur ein voreinstellen fuers
            klicken ... statt eine Auswahl mit + und - ... einfach 6 buttons
            hinbauen mit 1-6 die sozusagen die Tiefe darstellen."* */}
        <ModeChooser
          label={t.zoningAussentiefe}
          tooltip={t.tooltipZoningAussentiefe}
          value={String(aussentiefe)}
          ton="Zuschnitt"
          options={[1, 2, 3, 4, 5, 6].map((n) => ({
            id: String(n),
            text: String(n),
            tooltip: `${t.tooltipZoningTiefeKnopf} ${n}`,
          }))}
          onChange={(id) => setZoningAussentiefe(Number(id))}
        />
        <div className={styles.explain}>{t.zoningTiefeKlick}</div>
      </Spalte>

      <Spalte title={t.zoningWinkel} ton="Zuschnitt" breit>
        <ModeChooser
          label={t.zoningWinkel}
          tooltip={t.tooltipZoningWinkel}
          /* ENTWEDER ODER, auch sichtbar. Waehrend der Linienwahl ist KEIN
             Modus hervorgehoben - sonst leuchten zwei Knoepfe gleichzeitig
             und man sieht nicht, was gerade gilt. Der Nutzer dazu, schon
             beim Reihenwinkel: "enorm verwirrend, wenn mehrere Buttons
             highlighted sind." */
          value={linienwahl ? "" : winkelmodus}
          ton="Zuschnitt"
          raster
          options={[
            {
              id: "edge",
              text: hatBezug ? t.winkelNormal : t.winkelKante,
              tooltip: hatBezug
                ? t.tooltipWinkelNormal : t.tooltipWinkelKante,
            },
            { id: "quer", text: t.winkelQuer, tooltip: t.tooltipWinkelQuer },
            { id: "fixed", text: t.winkelFest, tooltip: t.tooltipWinkelFest },
          ]}
          onChange={setZoningWinkelmodus}
          /* Der vierte Knopf sitzt IN der Kachelreihe - aus drei Modi plus
             Linienwahl wird ein 2x2. Er ist kein Modus, sondern startet
             eine Auswahl; deshalb ein eigener Knopf mit demselben Stil. */
          extra={
            <TooltipKnopf
              text={linienwahl
                ? t.tooltipZoningLinieAbbrechen : t.tooltipZoningLinie}
              className={`${styles.modeKachel}
                ${linienwahl ? styles.modeAktivZuschnitt : ""}`}
              onClick={() => setZoningLinienwahl(!linienwahl)}
            >
              {linienwahl ? t.zoningLinieAbbrechen : t.zoningLinie}
            </TooltipKnopf>
          }
        />
        <Slider
          label={t.winkel}
          tooltip={t.tooltipWinkel}
          value={winkel}
          min={0}
          max={180}
          step={1}
          unit="°"
          ton="Zuschnitt"
          onChange={setZoningWinkel}
          disabled={winkelmodus !== "fixed"}
        />
      </Spalte>

      <Spalte title={t.zoningFlaeche} ton="Zuschnitt" breit>
        {/* NUR DER BODEN UNTER DEN PARZELLEN. Die Zoning-Strasse zaehlt
            weiterhin als Fahrfläche wie alle unsere Strassen - Ansage des
            Nutzers. */}
        <div className={styles.explain}>{t.zoningFlaecheHinweis}</div>
        <Auswahl
          label={t.zoningFlaeche}
          tooltip={t.tooltipZoningFlaeche}
          value={flaeche}
          options={flaechenliste}
          ton="Zuschnitt"
          onChange={setSurfaceZoning}
          ausLabel={t.zoningFlaecheAus}
        />
      </Spalte>
    </div>
  );
};
