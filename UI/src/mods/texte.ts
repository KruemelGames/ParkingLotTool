import { useValue } from "cs2/api";
import { sprachtexte$ } from "./bindings";

/**
 * Alle sichtbaren Texte des Panels - aus den Sprachdateien.
 *
 * Seit 2026-10-06 stehen die Texte nicht mehr hier, sondern in
 * `Lang/<sprache>.json` neben der DLL (Schluessel `ui.<name>`). C# schickt
 * die Texte der angezeigten Sprache ueber die Bindung "Sprachtexte"; dieser
 * Baukasten setzt daraus dasselbe Objekt zusammen, das die Komponenten schon
 * vorher als `t.<name>` benutzt haben.
 *
 * Platzhalter heissen `{name}`, ein woertliches `{` schreibt sich `{{` -
 * dieselbe Regel wie `Geometry/Sprachtexte.cs`. Mehrzahl: `<name>.one` fuer
 * genau 1, sonst `<name>.other`, die Zahl als `{n}`. Ein fehlender Schluessel
 * erscheint als `[name]` - sichtbar, nie still leer.
 *
 * Zusammengesetzte Zeilen bleiben EIN Stueck Text: Cohtml zieht
 * `white-space` nicht ueber getrennte Textknoten hinweg.
 */
/** Ein Satzstueck. `{ ort }` wird zu einem anklickbaren Ortsnamen. */
export type Ortsteil = { ort: string };
export type Satzteil = string | Ortsteil;
type Werte = Record<string, string | number>;

/** Setzt `{name}` ein; `{{`/`}}` werden zu `{`/`}`. Unbekannte Namen bleiben stehen. */
const format = (vorlage: string, werte?: Werte): string => {
  let aus = "";
  for (let i = 0; i < vorlage.length; i++) {
    const c = vorlage[i];
    if ((c === "{" || c === "}") && vorlage[i + 1] === c) { aus += c; i++; continue; }
    if (c === "{") {
      const ende = vorlage.indexOf("}", i + 1);
      if (ende > i) {
        const name = vorlage.substring(i + 1, ende);
        aus += werte && name in werte ? String(werte[name]) : "{" + name + "}";
        i = ende;
        continue;
      }
    }
    aus += c;
  }
  return aus;
};

const baue = (d: Record<string, string>) => {
  const tx = (k: string): string => {
    const t = d[k];
    return t === undefined ? "[" + k + "]" : format(t);
  };
  const tf = (k: string, werte: Werte): string => {
    const t = d[k];
    return t === undefined ? "[" + k + "]" : format(t, werte);
  };
  const tz = (k: string, n: number, werte?: Werte): string =>
    tf(k + (n === 1 ? ".one" : ".other"), { n, ...(werte ?? {}) });
  /** Satz mit Ortsnamen: `{ort}` wird zum anklickbaren Teil. */
  const satz = (k: string, werte: Werte, ort?: string): Satzteil[] => {
    const t = d[k];
    if (t === undefined) return ["[" + k + "]"];
    const stelle = ort === undefined ? -1 : t.indexOf("{ort}");
    if (stelle < 0) return [format(t, werte)];
    const teile: Satzteil[] = [];
    const vor = format(t.substring(0, stelle), werte);
    const nach = format(t.substring(stelle + 5), werte);
    if (vor !== "") teile.push(vor);
    teile.push({ ort: ort as string });
    if (nach !== "") teile.push(nach);
    return teile;
  };
  return {
    /** Freier Zugriff fuer Komponenten mit eigenen Schluesseln (laternen, vegetation). */
    text: (k: string, werte?: Werte): string => (werte ? tf(k, werte) : tx(k)),
    zahl: tz,
    /** Geldbetrag mit Waehrungszeichen in der Stellung der Sprache. */
    betrag: (betrag: number | string): string => tf("betrag", { betrag }),
    listeNotizKopf: (thema: string, nummer: number, anzahl: number): string =>
      tf("listeNotizKopf", { thema, nummer, anzahl }),
    listeAnzahl: (n: number): string => tz("listeAnzahl", n),
    listeSortiertNach: (name: string): string => tf("listeSortiertNach", { name }),
    listeTakt: tx("listeTakt"),
    teilen: tx("teilen"),
    tooltipTeilen: tx("tooltipTeilen"),
    teilenAbbrechen: tx("teilenAbbrechen"),
    tooltipTeilenAbbrechen: tx("tooltipTeilenAbbrechen"),
    teilungStand: (n: number) => tz("teilungStand", n),
    teilungBearbeiten: tx("teilungBearbeiten"),
    tooltipTeilungBearbeiten: tx("tooltipTeilungBearbeiten"),
    teilungEntfernen: tx("teilungEntfernen"),
    tooltipTeilungEntfernen: tx("tooltipTeilungEntfernen"),
    reiterAnleitung: tx("reiterAnleitung"),
    tooltipAnleitung: tx("tooltipAnleitung"),
    anleitungTitel: tx("anleitung.titel"),
    anleitungEinleitung: tx("anleitung.einleitung"),
    /** Titel der Folgen, in der Reihenfolge von FOLGEN in anleitung.tsx. */
    anleitungFolgen: [tx("anleitung.basics"), tx("anleitung.formUndLayout"),
      tx("anleitung.vegetationUndLaternen"), tx("anleitung.zoning"), tx("anleitung.probleme")],
    anleitungFolge: (nummer: number) => tf("anleitung.folge", { nummer }),
    anleitungBald: tx("anleitung.bald"),
    anleitungAnsehen: (titel: string) => tf("anleitung.ansehen", { titel }),
    anleitungBaldTooltip: (titel: string) => tf("anleitung.baldTooltip", { titel }),
    einheitMeter: tx("einheit.meter"),
    einheitGradZeichen: tx("einheit.grad"),
    /** Dezimalzahl mit dem Trennzeichen der Sprache ("6.5" / "6,5"). */
    dezimal: (wert: number, stellen: number): string =>
      wert.toFixed(stellen).replace(".", tx("dezimaltrenner")),
    /** Zahl und Einheit in der Reihenfolge der Sprache. */
    wertMitEinheit: (wert: string, einheit: string): string =>
      tf("wertMitEinheit", { wert, einheit }),
    bestimmungen: tx("bestimmungen"),
    artNameZufahrt: tx("artNameZufahrt"),
    bushaltestelle: tx("bushaltestelle"),
    tooltipBushaltestelle: tx("tooltipBushaltestelle"),
    fensterSchliessen: tx("fensterSchliessen"),
    leistungTitel: tx("leistungTitel"),
    leistungErklaerung: tx("leistungErklaerung"),
    leistungStart: tx("leistungStart"),
    leistungLaeuft: (s: number) => tf("leistungLaeuft", { s }),
    tooltipLeistung: tx("tooltipLeistung"),
    tooltipFensterHeim: tx("tooltipFensterHeim"),
    fangAlleAn: tx("fangAlleAn"),
    fangAlleAus: tx("fangAlleAus"),
    fangNamen: {
      ExistingGeometry: tx("fangNamen.ExistingGeometry"),
      StraightDirection: tx("fangNamen.StraightDirection"),
      NetSide: tx("fangNamen.NetSide"),
      ObjectSide: tx("fangNamen.ObjectSide"),
      GuideLines: tx("fangNamen.GuideLines"),
      ZoneGrid: tx("fangNamen.ZoneGrid"),
    } as Record<string, string>,
    stilHochkant: tx("stilHochkant"),
    stilHorizontal: tx("stilHorizontal"),
    tooltipStil: tx("tooltipStil"),
    artNameGasse: tx("artNameGasse"),
    artNameGasseEin: tx("artNameGasseEin"),
    artNameGasseAus: tx("artNameGasseAus"),
    artNameFussweg: tx("artNameFussweg"),
    artNameEinfahrt: tx("artNameEinfahrt"),
    artNameAusfahrt: tx("artNameAusfahrt"),
    fehltZugang: tx("fehltZugang"),
    fehltEinfahrt: tx("fehltEinfahrt"),
    fehltAusfahrt: tx("fehltAusfahrt"),
    angestellte: tx("angestellte"),
    beschaeftigte: tx("beschaeftigte"),
    parkgebuehr: tx("parkgebuehr"),
    bearbeiten: tx("bearbeiten"),
    tooltipBearbeiten: tx("tooltipBearbeiten"),
    waehrung: tx("waehrung"),
    gebuehrAus: tx("gebuehrAus"),
    flaecheStrasseAn: tx("flaecheStrasseAn"),
    flaecheDekoAn: tx("flaecheDekoAn"),
    geschriebenNach: (pfad: string) => tf("geschriebenNach", { pfad }),
    berichtOrt: tx("berichtOrt"),
    spalteFlaechen: tx("spalteFlaechen"),
    flaecheStrasse: tx("flaecheStrasse"),
    flaecheDeko: tx("flaecheDeko"),
    flaechenSuche: tx("flaechenSuche"),
    flaechenKeineTreffer: tx("flaechenKeineTreffer"),
    vorflaeche: tx("vorflaeche"),
    buchtsymbole: tx("buchtsymbole"),
    titel: tx("titel"),
    modnameDoppelpunkt: tx("modnameDoppelpunkt"),
    tasteEnter: tx("tasteEnter"),
    zuschnitt: tx("zuschnitt"),
    fahrwege: tx("fahrwege"),
    gruen: tx("gruen"),
    randabstand: tx("randabstand"),
    reihenwinkel: tx("reihenwinkel"),
    winkel: tx("winkel"),
    zoningReiter: tx("zoningReiter"),
    zoningSetzen: tx("zoningSetzen"),
    zoningSetzenAus: tx("zoningSetzenAus"),
    zoningWinkel: tx("zoningWinkel"),
    zoningLinie: tx("zoningLinie"),
    zoningSeite: tx("zoningSeite"),
    zoningAussentiefe: tx("zoningAussentiefe"),
    zoningSeiteHinweis: tx("zoningSeiteHinweis"),
    zoningTiefeKlick: tx("zoningTiefeKlick"),
    tooltipZoningAussentiefe: tx("tooltipZoningAussentiefe"),
    tooltipZoningTiefeKnopf: (n: number) => tz("tooltipZoningTiefeKnopf", n),
    zoningFlaeche: tx("zoningFlaeche"),
    zoningFlaecheHinweis: tx("zoningFlaecheHinweis"),
    tooltipZoningFlaeche: tx("tooltipZoningFlaeche"),
    zoningLinieAbbrechen: tx("zoningLinieAbbrechen"),
    tooltipZoningLinie: tx("tooltipZoningLinie"),
    tooltipZoningLinieAbbrechen: tx("tooltipZoningLinieAbbrechen"),
    /** "3 Flächen · 30 Parzellen" - ganzer Satz, Mehrzahl je Zahl. */
    zoningBestand: (flaechen: number, parzellen: number) => tf("zoningBestand", {
      flaechen: tz("zoningBestand.flaechen", flaechen),
      parzellen: tz("zoningBestand.parzellen", parzellen),
    }),
    zoningBestandGewaehlt: (flaechen: number, parzellen: number, nummer: number) =>
      tf("zoningBestandGewaehlt", {
        flaechen: tz("zoningBestand.flaechen", flaechen),
        parzellen: tz("zoningBestand.parzellen", parzellen),
        nummer,
      }),
    zoningHinweis: tx("zoningHinweis"),
    tooltipZoningReiter: tx("tooltipZoningReiter"),
    tooltipZoningGesperrt: tx("tooltipZoningGesperrt"),
    tooltipZoningSetzen: tx("tooltipZoningSetzen"),
    tooltipZoningWinkel: tx("tooltipZoningWinkel"),
    winkelKante: tx("winkelKante"),
    altbestandTitel: tx("altbestandTitel"),
    altbestandText1: tx("altbestandText1"),
    altbestandText2: tx("altbestandText2"),
    altbestandLoeschen: tx("altbestandLoeschen"),
    altbestandBehalten: tx("altbestandBehalten"),
    winkelFest: tx("winkelFest"),
    winkelQuer: tx("winkelQuer"),
    winkelNormal: tx("winkelNormal"),
    tooltipWinkelNormal: tx("tooltipWinkelNormal"),
    ausrichten: tx("ausrichten"),
    ausrichtenZurueck: tx("ausrichtenZurueck"),
    ausrichtenFertig: tx("ausrichtenFertig"),
    trennungFertig: tx("trennungFertig"),
    debugLiveLog: tx("debugLiveLog"),
    kurzDebugLiveLog: tx("kurzDebugLiveLog"),
    autoZufahrt: tx("autoZufahrt"),
    schalterAn: tx("schalterAn"),
    schalterAus: tx("schalterAus"),
    tooltipAutoZufahrt: tx("tooltipAutoZufahrt"),
    kurzDebugUeberlappung: tx("kurzDebugUeberlappung"),
    kurzDebugPrefabvergleich: tx("kurzDebugPrefabvergleich"),
    kurzDebugSonde: tx("kurzDebugSonde"),
    kurzDebugSondeErgebnis: tx("kurzDebugSondeErgebnis"),
    kurzDebugZoningsonde: tx("kurzDebugZoningsonde"),
    kurzDebugSezieren: tx("kurzDebugSezieren"),
    kurzDebugTraeger: tx("kurzDebugTraeger"),
    kurzSchritt1: tx("kurzSchritt1"),
    kurzSchritt2: tx("kurzSchritt2"),
    kurzSchritt3: tx("kurzSchritt3"),
    debugLiveLogAn: tx("debugLiveLogAn"),
    debugLiveLogAus: tx("debugLiveLogAus"),
    debugLiveLogHinweis: tx("debugLiveLogHinweis"),
    debugUeberlappung: tx("debugUeberlappung"),
    debugPrefabvergleich: tx("debugPrefabvergleich"),
    debugPrefabvergleichStart: tx("debugPrefabvergleichStart"),
    debugPrefabvergleichHinweis: tx("debugPrefabvergleichHinweis"),
    debugUeberlappungStart: tx("debugUeberlappungStart"),
    debugUeberlappungHinweis: tx("debugUeberlappungHinweis"),
    fahrgassenbreite: tx("fahrgassenbreite"),
    querstrassenbreite: tx("querstrassenbreite"),
    verbindungAlle: tx("verbindungAlle"),
    einheitBuchten: tx("einheitBuchten"),
    einheitGrad: tx("einheitGrad"),
    mittelgruen: tx("mittelgruen"),
    gruenstreifentiefe: tx("gruenstreifentiefe"),
    randstrassen: tx("randstrassen"),
    tooltipRandstrassen: tx("tooltipRandstrassen"),
    kappen: tx("kappen"),
    reiterEntwurf: tx("reiterEntwurf"),
    tooltipEntwurf: tx("tooltipEntwurf"),
    tooltipStellenMarkieren: tx("tooltipStellenMarkieren"),
    tooltipBerichtSchreiben: tx("tooltipBerichtSchreiben"),
    tooltipMeldungAbsturz: tx("tooltipMeldungAbsturz"),
    tooltipMeldungVorschau: tx("tooltipMeldungVorschau"),
    tooltipMeldeLot: tx("tooltipMeldeLot"),
    tooltipMeldungOrdner: tx("tooltipMeldungOrdner"),
    tooltipGebuehrHaken: tx("tooltipGebuehrHaken"),
    tooltipGebuehrRegler: tx("tooltipGebuehrRegler"),
    tooltipOrtSpringen: tx("tooltipOrtSpringen"),
    reiterMelden: tx("reiterMelden"),
    reiterDebug: tx("reiterDebug"),
    meldungTitel: tx("meldungTitel"),
    meldungAbsturzTitel: tx("meldungAbsturzTitel"),
    meldungAbsturzKnopf: tx("meldungAbsturzKnopf"),
    meldungVorschauKnopf: tx("meldungVorschauKnopf"),
    meldungOrdnerKnopf: tx("meldungOrdnerKnopf"),
    meldungErklaerung: tx("meldungErklaerung"),
    meldungVorschauErklaerung: tx("meldungVorschauErklaerung"),
    meldungLiegtBei: (pfad: string) => tf("meldungLiegtBei", { pfad }),
    befundTitel: tx("befundTitel"),
    befundNichts: tx("befundNichts"),
    kurzinfoTitel: tx("kurzinfoTitel"),
    lotBericht: tx("lotBericht"),
    meldeLotKnopf: tx("meldeLotKnopf"),
    meldeLotWahlLaeuft: tx("meldeLotWahlLaeuft"),
    meldeLotErklaerung: tx("meldeLotErklaerung"),
    tooltipLotBericht: tx("tooltipLotBericht"),
    debugSonde: tx("debugSonde"),
    debugSondeHinweis: tx("debugSondeHinweis"),
    debugSondeStart: tx("debugSondeStart"),
    debugSondeLaeuft: tx("debugSondeLaeuft"),
    debugSondeStop: tx("debugSondeStop"),
    debugSondeErgebnis: tx("debugSondeErgebnis"),
    debugZoningsonde: tx("debugZoningsonde"),
    debugZoningsondeHinweis: tx("debugZoningsondeHinweis"),
    debugZoningsondeGasse: tx("debugZoningsondeGasse"),
    debugZoningsondeSchotter: tx("debugZoningsondeSchotter"),
    debugZoningsondeUnsichtbar: tx("debugZoningsondeUnsichtbar"),
    zoningFlaecheAus: tx("zoningFlaecheAus"),
    zoningSeiten: tx("zoningSeiten"),
    zoningSeitenAus: tx("zoningSeitenAus"),
    tooltipZoningSeiten: tx("tooltipZoningSeiten"),
    tooltipZoningSeitenGesperrt: tx("tooltipZoningSeitenGesperrt"),
    zoningSeitenHinweis: tx("zoningSeitenHinweis"),
    debugZoningsondeBauen: tx("debugZoningsondeBauen"),
    debugZoningsondeMessen: tx("debugZoningsondeMessen"),
    debugZoningsondeLoeschen: tx("debugZoningsondeLoeschen"),
    debugSezieren: tx("debugSezieren"),
    debugSeziererHinweis: tx("debugSeziererHinweis"),
    debugSeziererStart: tx("debugSeziererStart"),
    debugTraeger: tx("debugTraeger"),
    debugTraegerHinweis: tx("debugTraegerHinweis"),
    debugTraegerStart: tx("debugTraegerStart"),
    debugTraegerAbschliessen: tx("debugTraegerAbschliessen"),
    debugNochNichts: tx("debugNochNichts"),
    bauen: tx("bauen"),
    stellplaetze: tx("stellplaetze"),
    titelZiehen: tx("titelZiehen"),
    schritt1: tx("schritt1"),
    schritt2: tx("schritt2"),
    schritt3: tx("schritt3"),
    stellenMarkieren: tx("stellenMarkieren"),
    markierenLaeuft: tx("markierenLaeuft"),
    gesetzt: tx("gesetzt"),
    letzteZurueck: tx("letzteZurueck"),
    alleLoeschen: tx("alleLoeschen"),
    berichtErstMarkieren: tx("berichtErstMarkieren"),
    amRandGassen: (rand: number, gassen: number) => tf("amRandGassen", { rand, gassen }),
    jeBucht: (wert: string) => wert === "" ? tx("jeBucht.leer") : tf("jeBucht", { wert }),
    winkelAreal: (winkel: string, flaeche: string) => winkel === "" ? tx("winkelAreal.leer") : tf("winkelAreal", { winkel, flaeche }),
    hinweis: (zeile: string) => tf("hinweis", { zeile }),
    berichtMitAnzahl: (anzahl: number) => tf("berichtMitAnzahl", { anzahl }),
    standardSpeichern: (name: string) => tf("standardSpeichern", { name }),
    standardZuruecksetzen: (name: string) => tf("standardZuruecksetzen", { name }),
    meldeEinleitung: tx("meldeEinleitung"),
    meldeKlickhinweis: tx("meldeKlickhinweis"),
    werkzeugTitel: tx("werkzeugTitel"),
    einstellungenOffen: tx("einstellungenOffen"),
    einstellungenZu: tx("einstellungenZu"),
    tooltipRandabstand: tx("tooltipRandabstand"),
    tooltipReihenwinkel: tx("tooltipReihenwinkel"),
    tooltipWinkelKante: tx("tooltipWinkelKante"),
    tooltipWinkelQuer: tx("tooltipWinkelQuer"),
    tooltipAusrichten: tx("tooltipAusrichten"),
    tooltipAusrichtenZurueck: tx("tooltipAusrichtenZurueck"),
    tooltipAusrichtenFertig: tx("tooltipAusrichtenFertig"),
    tooltipTrennungFertig: tx("tooltipTrennungFertig"),
    tooltipDebugLiveLog: tx("tooltipDebugLiveLog"),
    tooltipDebugUeberlappung: tx("tooltipDebugUeberlappung"),
    tooltipWinkelFest: tx("tooltipWinkelFest"),
    tooltipWinkel: tx("tooltipWinkel"),
    tooltipFahrgassenbreite: tx("tooltipFahrgassenbreite"),
    tooltipQuerstrassenbreite: tx("tooltipQuerstrassenbreite"),
    tooltipVerbindungAlle: tx("tooltipVerbindungAlle"),
    tooltipMittelgruen: tx("tooltipMittelgruen"),
    tooltipGruenstreifentiefe: tx("tooltipGruenstreifentiefe"),
    tooltipKappen: tx("tooltipKappen"),
    tooltipFlaecheStrasseAn: tx("tooltipFlaecheStrasseAn"),
    tooltipFlaecheStrasse: tx("tooltipFlaecheStrasse"),
    tooltipFlaecheDekoAn: tx("tooltipFlaecheDekoAn"),
    tooltipFlaecheDeko: tx("tooltipFlaecheDeko"),
    tooltipVorflaeche: tx("tooltipVorflaeche"),
    tooltipBuchtsymbole: tx("tooltipBuchtsymbole"),
    tooltipZufahrt: tx("tooltipZufahrt"),
    tooltipGasse: tx("tooltipGasse"),
    tooltipGasseEin: tx("tooltipGasseEin"),
    tooltipGasseAus: tx("tooltipGasseAus"),
    tooltipFussweg: tx("tooltipFussweg"),
    tooltipEinfahrt: tx("tooltipEinfahrt"),
    tooltipAusfahrt: tx("tooltipAusfahrt"),
    tooltipBauen: tx("tooltipBauen"),
    tooltipRueckgaengig: tx("tooltipRueckgaengig"),
    tooltipWiederherstellen: tx("tooltipWiederherstellen"),
    tooltipReset: tx("tooltipReset"),
    tooltipSchliessen: tx("tooltipSchliessen"),
    tooltipDebug: tx("tooltipDebug"),
    tooltipMelden: tx("tooltipMelden"),
    tooltipDebugSezieren: tx("tooltipDebugSezieren"),
    tooltipDebugSonde: tx("tooltipDebugSonde"),
    tooltipDebugZoningsonde: tx("tooltipDebugZoningsonde"),
    tooltipDebugTraeger: tx("tooltipDebugTraeger"),
    tooltipLetzteZurueck: tx("tooltipLetzteZurueck"),
    tooltipAlleLoeschen: tx("tooltipAlleLoeschen"),
    tooltipZuschnitt: tx("tooltipZuschnitt"),
    tooltipFahrwege: tx("tooltipFahrwege"),
    tooltipGruen: tx("tooltipGruen"),
    tooltipFlaechen: tx("tooltipFlaechen"),
    listeSuchen: tx("listeSuchen"),
    listeSummenzeile: (plaetze: number, frei: number, unterhalt: number, waehrung: string) => tf("listeSummenzeile", { plaetze, frei, unterhalt, waehrung }),
    listeSuche: tx("listeSuche"),
    listeFilter: [tx("listeFilter.0"), tx("listeFilter.1"), tx("listeFilter.2"), tx("listeFilter.3")],
    listeSortierungen: [tx("listeSortierungen.0"), tx("listeSortierungen.1"), tx("listeSortierungen.2"), tx("listeSortierungen.3")],
    listeSortieren: tx("listeSortieren"),
    listeThemen: [tx("listeThemen.0"), tx("listeThemen.1"), tx("listeThemen.2"), tx("listeThemen.3"), tx("listeThemen.4")],
    listePause: tx("listePause"),
    listeFortsetzen: tx("listeFortsetzen"),
    listePausiert: tx("listePausiert"),
    listeGebuehrHilfe: tx("listeGebuehrHilfe"),
    listeGebuehrFehler: tx("listeGebuehrFehler"),
    listeSchaetzung: tx("listeSchaetzung"),
    listeBearbeiten: tx("listeBearbeiten"),
    syncHinweis: (n: number) => tz("syncHinweis", n),
    syncFortschritt: (fertig: number, gesamt: number) => tf("syncFortschritt", { fertig, gesamt }),
    syncFertig: (n: number) => tz("syncFertig", n),
    waisenRepariertMeldung: (n: number) => tz("waisenRepariertMeldung", n),
    bauplaeneMeldung: (n: number) => tz("bauplaeneMeldung", n),
    syncOffenMeldung: (n: number) => tz("syncOffenMeldung", n),
    syncSchliessen: tx("syncSchliessen"),
    syncEinzeln: tx("syncEinzeln"),
    syncAlle: (n: number) => tf("syncAlle", { n }),
    syncAutomatisch: tx("syncAutomatisch"),
    tooltipSync: tx("tooltipSync"),
    tooltipSyncAlle: tx("tooltipSyncAlle"),
    tooltipSyncHinweis: tx("tooltipSyncHinweis"),
    tooltipSyncAuto: tx("tooltipSyncAuto"),
    waiseTitel: tx("waiseTitel"),
    waiseErklaerung: tx("waiseErklaerung"),
    waiseReparieren: tx("waiseReparieren"),
    tooltipWaiseReparieren: tx("tooltipWaiseReparieren"),
    waiseNichtReparierbar: tx("waiseNichtReparierbar"),
    waisenAlleReparieren: (n: number) => tf("waisenAlleReparieren", { n }),
    waisenAuto: tx("waisenAuto"),
    tooltipWaisenAuto: tx("tooltipWaisenAuto"),
    fehlendeAssets: (namen: string) => tf("fehlendeAssets", { namen }),
    tooltipFehlendReparieren: tx("tooltipFehlendReparieren"),
    wegeAusserhalb: tx("wegeAusserhalb"),
    wegeKnoten: tx("wegeKnoten"),
    tooltipWegeReparieren: tx("tooltipWegeReparieren"),
    ohneBauzettel: tx("ohneBauzettel"),
    bauplanTitel: tx("bauplanTitel"),
    bauplanText: tx("bauplanText"),
    bauplanWiederherstellen: tx("bauplanWiederherstellen"),
    tooltipBauplanWiederherstellen: tx("tooltipBauplanWiederherstellen"),
    listeNurKosten: tx("listeNurKosten"),
    listeKeineTreffer: tx("listeKeineTreffer"),
    listeFilterZurueck: tx("listeFilterZurueck"),
    listeSeite: (n: number, gesamt: number) => tf("listeSeite", { n, gesamt }),
    listeTreffer: (n: number, gesamt: number) => tf("listeTreffer", { n, gesamt }),
    listeVorigeSeite: tx("listeVorigeSeite"),
    listeNaechsteSeite: tx("listeNaechsteSeite"),
    reiterListe: tx("reiterListe"),
    tooltipListeArbeit: (n: number) => tz("tooltipListeArbeit", n),
    tooltipListe: tx("tooltipListe"),
    listeLeer: tx("listeLeer"),
    listeKopf: (lots: number, plaetze: number, unterhalt: number) => tf("listeKopf", { lots, plaetze, unterhalt }),
    spalteGebuehr: tx("spalteGebuehr"),
    tooltipGebuehr: tx("tooltipGebuehr"),
    tooltipHinspringen: tx("tooltipHinspringen"),
    tooltipUmbenennenListe: tx("tooltipUmbenennenListe"),
    tooltipBearbeitenListe: tx("tooltipBearbeitenListe"),
    tooltipVorigeInfo: tx("tooltipVorigeInfo"),
    tooltipNaechsteInfo: tx("tooltipNaechsteInfo"),
    infoNochKeineDaten: tx("infoNochKeineDaten"),
    listeGroesse: (breite: number, tiefe: number) => tf("listeGroesse", { breite, tiefe }),
    listeAlter: (tage: number) => tage < 60
      // `tage` zaehlt volle Spieltage: 1 heisst schon gestern gebaut.
      ? (tage < 1 ? tx("listeAlter.heute") : tz("listeAlter.tage", tage))
      : tf("listeAlter.monate", { monate: Math.round(tage / 30) }),
    listeBelegtVon: (belegt: number, gesamt: number) => tf("listeBelegtVon", { belegt, gesamt }),
    listeJeMonat: tx("listeJeMonat"),
    listeZuJung: tx("listeZuJung"),
    tooltipErgebnisKosten: tx("tooltipErgebnisKosten"),
    tooltipErgebnisGeschaetzt: tx("tooltipErgebnisGeschaetzt"),
    mengenwoerter: [tx("mengenwoerter.0"), tx("mengenwoerter.1"), tx("mengenwoerter.2"), tx("mengenwoerter.3")],
    zweckwoerter: [tx("zweckwoerter.0"), tx("zweckwoerter.1"), tx("zweckwoerter.2"), tx("zweckwoerter.3")],
    alterswoerter: [tx("alterswoerter.0"), tx("alterswoerter.1"), tx("alterswoerter.2"), tx("alterswoerter.3")],
    wegwoerter: [tx("wegwoerter.0"), tx("wegwoerter.1"), tx("wegwoerter.2")],
    infoZielRang: (menge: string, ort: string): Satzteil[] => satz("infoZielRang", { menge }, ort),
    infoZweck: (menge: string, zweck: string): Satzteil[] => satz("infoZweck", { menge, zweck }),
    infoWohnparkplatz: (menge: string): Satzteil[] => satz("infoWohnparkplatz", { menge }),
    infoTouristen: (menge: string): Satzteil[] => satz("infoTouristen", { menge }),
    infoAlter: (menge: string, gruppe: string): Satzteil[] => satz("infoAlter", { menge, gruppe }),
    infoBildung: (n: number): Satzteil[] => satz("infoBildung", { n }),
    infoZufriedenheit: (besser: boolean): Satzteil[] => [tx(besser ? "infoZufriedenheit.besser" : "infoZufriedenheit.schlechter")],
    infoFussweg: (meter: number, ort: string, urteil: string): Satzteil[] => satz("infoFussweg", { meter, urteil }, ort),
    infoLaufweite: (n: number): Satzteil[] => [tz("infoLaufweite", n)],
    infoErreichbar: (laeden: number, bueros: number, wohnungen: number): Satzteil[] => [tf("infoErreichbar", { liste: [
      laeden > 0 ? tz("infoErreichbar.laeden", laeden) : "",
      bueros > 0 ? tz("infoErreichbar.bueros", bueros) : "",
      wohnungen > 0 ? tz("infoErreichbar.wohnungen", wohnungen) : "",
    ].filter((x) => x !== "").join(tx("listentrenner")) })],
    infoTagesprofil: (voll: number, leer: number): Satzteil[] => satz("infoTagesprofil", { voll, leer }),
    infoSpitze: (stunde: number, belegt: number, gesamt: number): Satzteil[] => satz("infoSpitze", { stunde, belegt, gesamt }),
    infoDauerlast: (tage: number, prozent: number): Satzteil[] => satz("infoDauerlast", { tage, prozent }),
    infoLeerstand: (n: number): Satzteil[] => [tz("infoLeerstand", n)],
    infoDurchsatz: (autos: number, tage: number, jeTag: number): Satzteil[] => satz("infoDurchsatz", { autos, tage, jeTag }),
    infoStandzeit: (stunden: number, minuten: number): Satzteil[] => satz("infoStandzeit", { stunden, minuten }),
    infoRang: (rang: number, gesamt: number): Satzteil[] => [rang === 1 ? tx("infoRang.erster") : tf("infoRang.andere", { rang, gesamt })],
    infoKosten: (teuerster: boolean): Satzteil[] => [tx(teuerster ? "infoKosten.teuerster" : "infoKosten.guenstigster")],
    infoBilanz: (tage: number, autos: number): Satzteil[] => satz("infoBilanz", { tage, autos }),
    mangelTot: (): Satzteil[] => satz("mangelTot", {}),
    mangelKeinWeg: (menge: string): Satzteil[] => satz("mangelKeinWeg", { menge }),
    mangelZufahrt: (plaetze: number): Satzteil[] => satz("mangelZufahrt", { plaetze }),
    mangelUngleichgewicht: (ort: string, prozent: number): Satzteil[] => satz("mangelUngleichgewicht", { prozent }, ort),
    mangelGebuehr: (tage: number, prozent: number, richtung: number): Satzteil[] => [tf(
      richtung === 2 ? "mangelGebuehr.nichts" : richtung === 0 ? "mangelGebuehr.mehr" : "mangelGebuehr.weniger",
      { tage, prozent })],  };
};

export type Texte = ReturnType<typeof baue>;

let _json = "";
let _texte: Texte = baue({});

/**
 * Die Texte der angezeigten Sprache. Neu gebaut nur, wenn C# andere Texte
 * schickt - sonst kostet der Aufruf je Bild einen Zeichenkettenvergleich.
 */
export const useTexte = (): Texte => {
  const json = useValue(sprachtexte$);
  if (json !== _json) {
    _json = json;
    let d: Record<string, string> = {};
    try { d = JSON.parse(json); } catch { d = {}; }
    _texte = baue(d);
  }
  return _texte;
};
