import { useEffect, useMemo, useRef, useState } from "react";
import { useValue } from "cs2/api";
import styles from "./panel.module.scss";
import { Listengebuehr } from "./liste-gebuehr";
import { anteilBelegt, waehlePlaetze, Listenfilter, Listensortierung } from "./liste-logik";
import { MitTooltip, TooltipKnopf } from "./controls";
import { Satzteil, Texte, useTexte } from "./texte";
import {
  icon, parkplatzBearbeiten, parkplatzInfos$,
  parkplatzListe$, parkplatzRunde$, parkplatzRundeVor,
  parkplatzUmbenennen, parkplatzWaehlen,
  parkplatzReparieren, parkplaetzeReparieren, setWaisenAuto, waisenAuto$,
} from "./bindings";

/**
 * Wie lange eine Info steht, bevor die naechste kommt.
 *
 * Fuenfzehn Sekunden, so vom Nutzer festgelegt. Lang genug, um einen Satz
 * zu lesen und ihn zwischen mehreren Kacheln zu vergleichen.
 */
const INFO_MILLISEKUNDEN = 15000;

/**
 * Muss zu `Geometry/Infokarten.cs` passen.
 *
 * Die Zahlen kommen aus C# durch die Bindung; ein verschobener Wert waere
 * hier ein falscher Satz an der richtigen Stelle - also kein Absturz,
 * sondern eine stille Luege. Deshalb stehen sie ausgeschrieben und nicht
 * als fortlaufende Nummerierung.
 */
const Art = {
  Keine: 0,
  ZielRang: 10, Zweck: 11, Wohnparkplatz: 12, Touristen: 13,
  Alter: 14, Bildung: 15, Zufriedenheit: 16, Stammgaeste: 17,
  Fussweg: 30, Laufweite: 31, Erreichbar: 32,
  Tagesprofil: 50, Spitze: 51, Woche: 52, Dauerlast: 53,
  Leerstand: 54, Durchsatz: 55, Rekord: 56, Standzeit: 57,
  KeinWeg: 70, Tot: 71, Zufahrt: 72, Ungleichgewicht: 73,
  Gebuehrenwirkung: 74,
  Rang: 90, Kosten: 91, Bilanz: 92,
} as const;

/** Felder je Infoblock in der Bindung. Siehe `SchreibeBlock` in C#. */
const FELDER_JE_BLOCK = 7;

/** Wo der Mangelblock in der Grundzeile beginnt. */
const MANGEL_AB_FELD = 12;

/**
 * Nach dem Mangelblock: Waisenzustand (0 = in Ordnung, 1 = verwaist und
 * reparierbar, 2 = verwaist, nicht reparierbar) und ob ein Bauzettel da ist.
 */
const WAISE_FELD = MANGEL_AB_FELD + FELDER_JE_BLOCK;
const BAUZETTEL_FELD = WAISE_FELD + 1;

/** Ab so vielen Proben traut sich eine Kachel eine Aussage zu. */
const PROBEN_FUER_AUSSAGE = 5;

/** Ab hier ist ein Platz so voll, dass kaum noch jemand einen findet. */
const VOLL_AB = 0.9;



type Block = {
  art: number;
  zahl1: number;
  zahl2: number;
  zahl3: number;
  wort: number;
  zielId: string;
  zielName: string;
};

type Parkplatz = {
  id: string;
  name: string;
  kapazitaet: number;
  belegt: number;
  gebuehr: number;
  unterhalt: number;
  proben: number;
  breite: number;
  tiefe: number;
  alterTage: number;
  ergebnis: number;
  geschaetzt: boolean;
  mangel: Block;
  waise: number;
  bauzettel: boolean;
};

const zahl = (s: string | undefined) => {
  const n = Number(s);
  return Number.isFinite(n) ? n : 0;
};

const leererBlock = (): Block => ({
  art: 0, zahl1: 0, zahl2: 0, zahl3: 0, wort: 0, zielId: "", zielName: "",
});

const blockAus = (felder: string[], ab: number): Block => ({
  art: zahl(felder[ab]),
  zahl1: zahl(felder[ab + 1]),
  zahl2: zahl(felder[ab + 2]),
  zahl3: zahl(felder[ab + 3]),
  wort: zahl(felder[ab + 4]),
  zielId: felder[ab + 5] ?? "",
  zielName: felder[ab + 6] ?? "",
});

const zeilen = (roh: string) =>
  roh.split("\n").filter((z) => z !== "").map((z) => z.split("\t"));

/**
 * Aus einem Infoblock wird ein Satz.
 *
 * Der Satz kommt IMMER am Stueck aus `texte.ts`, nie hier zusammengesetzt.
 * Sonst liesse er sich nicht uebersetzen: im Englischen steht der Ortsname
 * an einer anderen Stelle, und aus Textstuecken gebaut kaeme dabei Unsinn
 * heraus. Was hier passiert, ist nur die Auswahl der richtigen Vorlage.
 */
const satzVon = (t: Texte, b: Block): Satzteil[] | null => {
  const menge = (i: number) => t.mengenwoerter[Math.min(Math.max(i, 0), 3)];
  switch (b.art) {
    case Art.ZielRang:
      return t.infoZielRang(menge(b.zahl1), b.zielName);
    case Art.Zweck:
      return t.infoZweck(menge(b.zahl1), t.zweckwoerter[b.wort] ?? "");
    case Art.Wohnparkplatz:
      return t.infoWohnparkplatz(menge(b.zahl1));
    case Art.Touristen:
      return t.infoTouristen(menge(b.zahl1));
    case Art.Alter:
      return t.infoAlter(menge(b.zahl1), t.alterswoerter[b.wort] ?? "");
    case Art.Bildung:
      return t.infoBildung(b.zahl1);
    case Art.Zufriedenheit:
      return t.infoZufriedenheit(b.wort === 0);
    case Art.Fussweg:
      return t.infoFussweg(b.zahl1, b.zielName, t.wegwoerter[b.wort] ?? "");
    case Art.Laufweite:
      return t.infoLaufweite(b.zahl1);
    case Art.Erreichbar:
      return t.infoErreichbar(b.zahl1, b.zahl2, b.zahl3);
    case Art.Tagesprofil:
      return t.infoTagesprofil(b.zahl1, b.zahl2);
    case Art.Spitze:
      return t.infoSpitze(b.zahl1, b.zahl2, b.zahl3);
    case Art.Dauerlast:
      return t.infoDauerlast(b.zahl1, b.zahl2);
    case Art.Leerstand:
      return t.infoLeerstand(b.zahl1);
    case Art.Durchsatz:
      return t.infoDurchsatz(b.zahl1, b.zahl2, b.zahl3);
    case Art.Standzeit:
      return t.infoStandzeit(b.zahl1, b.zahl2);
    case Art.Rang:
      return t.infoRang(b.zahl1, b.zahl2);
    case Art.Kosten:
      return t.infoKosten(b.wort === 0);
    case Art.Bilanz:
      return t.infoBilanz(b.zahl1, b.zahl2);
    case Art.Tot:
      return t.mangelTot();
    case Art.KeinWeg:
      return t.mangelKeinWeg(menge(b.zahl1));
    case Art.Zufahrt:
      return t.mangelZufahrt(b.zahl1);
    case Art.Ungleichgewicht:
      return t.mangelUngleichgewicht(b.zielName, b.zahl1);
    case Art.Gebuehrenwirkung:
      return t.mangelGebuehr(b.zahl1, b.zahl2, b.wort);
    default:
      return null;
  }
};

/**
 * Ein Satz mit anklickbaren Ortsnamen.
 *
 * Ein Ortsteil wird zu einem Knopf, der das Gebaeude auswaehlt und die
 * Kamera hinschickt - dasselbe, als haette man es in der Stadt angeklickt.
 */
const Satz = ({ teile, zielId }: { teile: Satzteil[]; zielId: string }) => {
  const t = useTexte();
  return (
  <>
    {teile.map((teil, i) =>
      typeof teil === "string"
        ? <span key={i}>{teil}</span>
        : zielId !== ""
          ? (
            <MitTooltip key={i} text={t.tooltipOrtSpringen}>
              <button
                className={styles.listeOrt}
                aria-label={t.tooltipOrtSpringen}
                onClick={() => parkplatzWaehlen(zielId)}
              >
                {teil.ort}
              </button>
            </MitTooltip>
          )
          : <span key={i}>{teil.ort}</span>)}
  </>
  );
};

/**
 * Der Name - ein Klick macht daraus ein Eingabefeld.
 *
 * Stift und Spiel-Tooltip unterscheiden Umbenennen von der beschrifteten
 * Bearbeiten-Aktion fuer den ganzen Parkplatz.
 */
const Name = ({ platz }: { platz: Parkplatz }) => {
  const t = useTexte();
  const [entwurf, setEntwurf] = useState<string | null>(null);

  if (entwurf === null) {
    return (
      <MitTooltip text={`${platz.name} · ${t.tooltipUmbenennenListe}`}>
        <button
          className={styles.listeName}
          onClick={() => setEntwurf(platz.name)}
        >
          <span className={styles.listeNameText}>{platz.name}</span>
          <img className={styles.listeNameStift} src={icon("Pencil")} />
        </button>
      </MitTooltip>
    );
  }

  const uebernehmen = () => {
    const sauber = entwurf.trim();
    if (sauber !== "" && sauber !== platz.name) {
      parkplatzUmbenennen(platz.id, sauber);
    }
    setEntwurf(null);
  };

  return (
    <input
      className={styles.listeNameFeld}
      value={entwurf}
      autoFocus
      onChange={(e: any) => setEntwurf(e.target.value)}
      onBlur={uebernehmen}
      onKeyDown={(e: any) => {
        e.stopPropagation();
        if (e.key === "Enter") { e.preventDefault(); uebernehmen(); }
        if (e.key === "Escape") setEntwurf(null);
      }}
    />
  );
};

/** Groesse und Alter unter dem Namen - nur, was wirklich bekannt ist. */
const Untertitel = ({ platz }: { platz: Parkplatz }) => {
  const t = useTexte();
  const teile: string[] = [];
  if (platz.breite > 0 && platz.tiefe > 0) {
    teile.push(t.listeGroesse(platz.breite, platz.tiefe));
  }
  if (platz.alterTage >= 0) teile.push(t.listeAlter(platz.alterTage));
  if (teile.length === 0) return null;
  return <div className={styles.listeUntertitel}>{teile.join(" · ")}</div>;
};

const Kachel = ({ platz, bloecke, slot, runde }: {
  platz: Parkplatz; bloecke: Block[]; slot: number; runde: string;
}) => {
  const t = useTexte();
  const [eigenerSlot, setEigenerSlot] = useState<number | null>(null);
  // Die gemeinsame 15-Sekunden-Uhr bleibt; manuelles Blaettern betrifft nur
  // diese Kachel und bleibt bis zur naechsten Runde stehen.
  useEffect(() => setEigenerSlot(null), [runde]);
  const anzahl = Math.max(1, bloecke.length);
  const index = Math.min(eigenerSlot ?? slot, anzahl - 1);
  const block = bloecke[index] ?? leererBlock();
  const thema = t.listeThemen[block.art >= 90 ? 4 : block.art >= 50 ? 3 : block.art >= 30 ? 2 : block.art >= 10 ? 1 : 0];
  const blaettern = (richtung: number) => setEigenerSlot(v => ((v ?? index) + richtung + anzahl) % anzahl);
  const jung = platz.proben < PROBEN_FUER_AUSSAGE;
  const satz = jung ? null : satzVon(t, block);
  const mangel = satzVon(t, platz.mangel);
  const anteil = anteilBelegt(platz.belegt, platz.kapazitaet);
  const voll = !jung && anteil >= VOLL_AB;

  /*
   * DER RAHMEN TRAEGT DEN ZUSTAND.
   *
   * Eine Kachel mit einem Mangel faellt so schon im Vorbeischauen auf, ohne
   * dass man den Satz unten lesen muss. Genau dafuer ist die Liste da.
   */
  const rahmen = mangel !== null ? styles.listeKachelMangel : "";

  const balken = jung
    ? styles.listeBalkenStill
    : voll ? styles.listeBalkenVoll : styles.listeBalkenGut;

  return (
    <div className={styles.listeZelle}>
      <div className={`${styles.listeKachel} ${rahmen}`}>
        <div className={styles.listeKopfzeile}>
          <div className={styles.listeTitelblock}>
            <Name platz={platz} />
            <Untertitel platz={platz} />
          </div>
          <MitTooltip text={t.tooltipHinspringen}>
            <button aria-label={t.tooltipHinspringen}
              className={`${styles.listeSymbol} ${styles.listeSymbolFokus}`}
              onClick={() => parkplatzWaehlen(platz.id)}
            >
              <img src={icon("MapMarker")} />
            </button>
          </MitTooltip>
          <MitTooltip text={platz.bauzettel ? t.tooltipBearbeitenListe : t.ohneBauzettel}>
            <button aria-label={t.tooltipBearbeitenListe}
              className={`${styles.listeSymbol} ${styles.listeBearbeiten}`}
              disabled={!platz.bauzettel}
              onClick={() => parkplatzBearbeiten(platz.id)}
            >
              {t.listeBearbeiten}
            </button>
          </MitTooltip>
        </div>

        <div className={styles.listeBalkenBett}>
          <div
            className={balken}
            style={{ width: `${Math.round(anteil * 100)}%` }}
          />
        </div>
        <div className={styles.listeBalkenZeile}>
          <div>{t.listeBelegtVon(platz.belegt, platz.kapazitaet)}</div>
          <div className={styles.listeLuecke} />
          <div className={voll ? styles.listeFastVoll : undefined}>
            {`${Math.round(anteil * 100)} %`}
          </div>
        </div>

        <div className={styles.listeGeldzeile}>
          <MitTooltip text={t.tooltipGebuehr}>
            <div className={styles.listeGeldName}>{t.spalteGebuehr}</div>
          </MitTooltip>
          <Listengebuehr id={platz.id} wert={platz.gebuehr} />
          <div className={styles.listeLuecke} />
          <MitTooltip
            text={platz.geschaetzt ? t.tooltipErgebnisGeschaetzt
              : t.tooltipErgebnisKosten}
          >
            <div className={styles.listeErgebnisBlock}>
              <div className={`${styles.listeErgebnis} ${
                platz.ergebnis >= 0 ? styles.listeGut : styles.listeSchlecht}`}
              >
                {`${platz.ergebnis >= 0 ? "+" : "−"}${Math.abs(platz.ergebnis)} ${t.waehrung}`}
              </div>
              <div className={styles.listeJeMonat}>{platz.geschaetzt ? t.listeSchaetzung : t.listeNurKosten}</div>
            </div>
          </MitTooltip>
        </div>

        <div className={styles.listeNotiz}>
          <div className={styles.listeNotizKopf}>
            <TooltipKnopf text={t.tooltipVorigeInfo} className={styles.listeNotizPfeil}
              disabled={anzahl < 2} onClick={() => blaettern(-1)}>‹</TooltipKnopf>
            <span>{`${thema} · ${index + 1}/${anzahl}`}</span>
            <TooltipKnopf text={t.tooltipNaechsteInfo} className={styles.listeNotizPfeil}
              disabled={anzahl < 2} onClick={() => blaettern(1)}>›</TooltipKnopf>
          </div>
          <div className={`${styles.listeHinweis} ${jung ? styles.listeHinweisJung : styles.listeHinweisGut}`}>
            <img src={icon(jung ? "Clock" : "CircleInfo")} />
            <div>{satz !== null ? <Satz teile={satz} zielId={block.zielId} />
              : jung ? t.listeZuJung : t.infoNochKeineDaten}</div>
          </div>
        </div>

        {mangel !== null && (
          <div className={`${styles.listeHinweis} ${styles.listeHinweisMangel}`}>
            <img src={icon("ExclamationMark")} />
            <div><Satz teile={mangel} zielId={platz.mangel.zielId} /></div>
          </div>
        )}
        {platz.waise > 0 && <Schleier platz={platz} />}
      </div>
    </div>
  );
};

/**
 * DER SCHLEIER UEBER EINER VERWAISTEN KACHEL.
 *
 * Ansage des Nutzers am 2026-09-24: die Kachel sieht aus wie jede andere,
 * aber ihr Text ist verschwommen - man soll sich fragen, warum, und direkt
 * darueber die Antwort und den Knopf finden. Deshalb liegt der Schleier
 * UEBER der vollen Kachel, statt eine eigene, andere Kachel zu bauen.
 */
const Schleier = ({ platz }: { platz: Parkplatz }) => {
  const t = useTexte();
  return (
    <div className={styles.listeSchleier}>
      <div className={styles.listeSchleierTitel}>{t.waiseTitel}</div>
      <div className={styles.listeSchleierText}>{t.waiseErklaerung}</div>
      {platz.waise === 1
        ? <TooltipKnopf text={t.tooltipWaiseReparieren}
            className={styles.listeSchleierKnopf}
            onClick={() => parkplatzReparieren(platz.id)}>
            {t.waiseReparieren}
          </TooltipKnopf>
        : <div className={styles.listeSchleierText}>{t.waiseNichtReparierbar}</div>}
    </div>
  );
};

/**
 * Der Reiter „Parkplaetze".
 *
 * DIE UHR LAEUFT HIER, EINMAL FUER ALLE KACHELN. Jede Kachel mit eigenem
 * Zeitgeber haette bald ihren eigenen Takt - dann stuende auf Kachel eins
 * die Belegung und auf Kachel drei das Alter der Gaeste, und Vergleichen
 * ginge nicht mehr. Genau dafuer gibt es die Liste aber.
 */
const FILTER: Listenfilter[] = ["alle", "probleme", "voll", "frei"];
const SORTIERUNG: Listensortierung[] = ["name", "belegung", "frei", "groesse"];
const SEITENGROESSE = 12;

export const ListeTab = () => {
  const t = useTexte();
  const rohListe = useValue(parkplatzListe$);
  const rohInfos = useValue(parkplatzInfos$);
  const rohRunde = useValue(parkplatzRunde$);
  const auto = useValue(waisenAuto$);
  const [slot, setSlot] = useState(0);
  const [pause, setPause] = useState(false);
  const [hover, setHover] = useState(false);
  const [fokus, setFokus] = useState(false);
  const [suche, setSuche] = useState("");
  const [filter, setFilter] = useState<Listenfilter>("alle");
  const [sortierung, setSortierung] = useState<Listensortierung>("name");
  const [sortOffen, setSortOffen] = useState(false);
  const [seite, setSeite] = useState(0);
  const zielslot = useRef<"anfang" | "ende">("anfang");
  const wechsel = useRef(false);
  const gitter = useRef<HTMLDivElement>(null);

  const plaetze = useMemo<Parkplatz[]>(() => zeilen(rohListe).map(f => ({
    id: f[0] ?? "", name: f[1] ?? "", kapazitaet: zahl(f[2]),
    belegt: zahl(f[3]), gebuehr: zahl(f[4]), unterhalt: zahl(f[5]),
    proben: zahl(f[6]), breite: zahl(f[7]), tiefe: zahl(f[8]),
    alterTage: f[9] === undefined ? -1 : zahl(f[9]), ergebnis: zahl(f[10]),
    geschaetzt: zahl(f[11]) === 1, mangel: blockAus(f, MANGEL_AB_FELD),
    waise: zahl(f[WAISE_FELD]), bauzettel: zahl(f[BAUZETTEL_FELD]) === 1,
  })), [rohListe]);
  const bloecke = useMemo(() => {
    const result = new Map<string, Block[]>();
    zeilen(rohInfos).forEach(f => {
      const liste: Block[] = [];
      for (let i = 1; i + FELDER_JE_BLOCK <= f.length; i += FELDER_JE_BLOCK)
        liste.push(blockAus(f, i));
      result.set(f[0] ?? "", liste);
    });
    return result;
  }, [rohInfos]);
  const rundenFelder = rohRunde.split("\t");
  const slots = Math.max(rundenFelder.length - 1, 1);
  useEffect(() => {
    setSlot(zielslot.current === "ende" ? slots - 1 : 0);
    zielslot.current = "anfang"; wechsel.current = false;
  }, [rohRunde]);
  const weiter = () => {
    if (wechsel.current) return;
    if (slot + 1 < slots) { setSlot(slot + 1); return; }
    wechsel.current = true; zielslot.current = "anfang"; parkplatzRundeVor();
  };
  const pausiert = pause || hover || fokus;
  useEffect(() => {
    if (pausiert || plaetze.length === 0) return;
    const timer = setTimeout(weiter, INFO_MILLISEKUNDEN);
    return () => clearTimeout(timer);
  }, [slot, pausiert, rohRunde, plaetze.length]);
  const gefiltert = useMemo(() => waehlePlaetze(plaetze, suche, filter, sortierung),
    [plaetze, suche, filter, sortierung]);
  const seiten = Math.max(1, Math.ceil(gefiltert.length / SEITENGROESSE));
  const aktuelleSeite = Math.min(seite, seiten - 1);
  useEffect(() => { setSeite(0); }, [suche, filter, sortierung]);
  useEffect(() => { setSeite(s => Math.min(s, seiten - 1)); }, [seiten]);
  useEffect(() => { if (gitter.current) gitter.current.scrollTop = 0; }, [aktuelleSeite, suche, filter, sortierung]);
  const waisen = plaetze.filter(p => p.waise > 0).length;
  const reparierbar = plaetze.filter(p => p.waise === 1).length;
  const summePlaetze = plaetze.reduce((summe, p) => summe + p.kapazitaet, 0);
  const summeFrei = plaetze.reduce((summe, p) => summe + Math.max(0, p.kapazitaet - p.belegt), 0);
  const unterhalt = plaetze.reduce((summe, p) => summe + p.unterhalt, 0);
  return (
    <div className={styles.listeRahmen}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      onFocus={e => setFokus(e.target.tagName === "INPUT" || e.target.getAttribute("role") === "slider")}
      onBlur={e => {
        if (!e.currentTarget.contains(e.relatedTarget as Node)) setFokus(false);
      }}>
      <div className={styles.listeKopf}>
        <div className={styles.listeUebersicht}>
          <strong>{`${plaetze.length} ${t.reiterListe}`}</strong>
        </div>
        <div className={styles.listeKopfAktionen}>
        {waisen > 0 && <div className={styles.listeFilter}>
          {reparierbar > 0 && <TooltipKnopf text={t.tooltipWaiseReparieren}
            className={styles.listeTextknopf}
            onClick={parkplaetzeReparieren}>{t.waisenAlleReparieren(reparierbar)}</TooltipKnopf>}
          <TooltipKnopf text={t.tooltipWaisenAuto}
            className={`${styles.listeTextknopf} ${auto ? styles.listeAktiv : ""}`}
            aria-pressed={auto} onClick={() => setWaisenAuto(!auto)}>{t.waisenAuto}</TooltipKnopf>
        </div>}
        <label className={styles.listeSuchfeld}>
          <span>{t.listeSuchen}</span>
          <input className={styles.listeSuche} aria-label={t.listeSuche}
            value={suche} onChange={e => setSuche(e.target.value)} onKeyDown={e => e.stopPropagation()} />
        </label>
        <div className={styles.listeFilter}>
          {FILTER.map((f, i) => <TooltipKnopf key={f} text={t.listeFilter[i]}
            className={`${styles.listeTextknopf} ${filter === f ? styles.listeAktiv : ""}`}
            aria-pressed={filter === f} onClick={() => setFilter(f)}>{t.listeFilter[i]}</TooltipKnopf>)}
        </div>
        <div className={styles.listeSortierung} onBlur={e => {
          if (!e.currentTarget.contains(e.relatedTarget as Node)) setSortOffen(false);
        }}>
          <TooltipKnopf text={t.listeSortieren} className={styles.listeTextknopf}
            aria-expanded={sortOffen} onClick={() => setSortOffen(!sortOffen)}>
            {`${t.listeSortieren}: ${t.listeSortierungen[SORTIERUNG.indexOf(sortierung)]}`}
          </TooltipKnopf>
          {sortOffen && <div className={styles.listeSortiermenue}>
            {SORTIERUNG.map((s, i) => <button key={s} className={styles.listeTextknopf}
              onClick={() => { setSortierung(s); setSortOffen(false); }}>{t.listeSortierungen[i]}</button>)}
          </div>}
        </div>
        </div>
      </div>
      <div ref={gitter} className={styles.listeScroll}>
        <div className={styles.listeGitter}>
          {gefiltert.slice(aktuelleSeite * SEITENGROESSE, (aktuelleSeite + 1) * SEITENGROESSE)
            .map(platz => <Kachel key={platz.id} platz={platz} bloecke={bloecke.get(platz.id) ?? []} slot={slot} runde={rundenFelder[0]} />)}
        </div>
        {gefiltert.length === 0 && <div className={styles.listeLeer}>
          {plaetze.length === 0 ? t.listeLeer : t.listeKeineTreffer}
          {plaetze.length > 0 && <TooltipKnopf text={t.listeFilterZurueck} className={styles.listeTextknopf}
            onClick={() => { setSuche(""); setFilter("alle"); }}>{t.listeFilterZurueck}</TooltipKnopf>}
        </div>}
      </div>
      <div className={styles.listeInfosteuerung}>
        <span className={styles.listeSummen}>{t.listeSummenzeile(summePlaetze, summeFrei, unterhalt, t.waehrung)}</span>
        <TooltipKnopf text={pause ? t.listeFortsetzen : t.listePause} className={styles.listeTextknopf}
          aria-pressed={pause} onClick={() => setPause(!pause)}>{pause ? "▶" : "Ⅱ"}</TooltipKnopf>
        <span>{pausiert ? t.listePausiert : "15 s"}</span>
        <div className={styles.listeLuecke} />
        <span>{t.listeTreffer(gefiltert.length, plaetze.length)}</span>
      </div>
      {seiten > 1 && <div className={styles.listeSeiten}>
        <TooltipKnopf text={t.listeVorigeSeite} className={styles.listeTextknopf}
          disabled={aktuelleSeite === 0} onClick={() => setSeite(aktuelleSeite - 1)}>‹</TooltipKnopf>
        <span>{t.listeSeite(aktuelleSeite + 1, seiten)}</span>
        <TooltipKnopf text={t.listeNaechsteSeite} className={styles.listeTextknopf}
          disabled={aktuelleSeite + 1 >= seiten} onClick={() => setSeite(aktuelleSeite + 1)}>›</TooltipKnopf>
      </div>}
    </div>
  );
};
