/*
 * LAYOUT-PRUEFSTAND (28.10.2026).
 *
 * Spieler sehen das Panel bei ganz anderen Bildschirmen als wir (Marcaliseth:
 * 2560x1440, Leiste abgeschnitten). Der Nutzer hat nur 1920x1080. Deshalb
 * rendert dieser Lauf das ECHTE Panel (UI/src, gebaut ueber webpack.config.js)
 * mit dem ECHTEN Spiel-Stylesheet (index.css samt Schrift) in Edge - bei jeder
 * Aufloesung, mit "Interface scaling" an/aus, Textgroesse 100/150 %, in beiden
 * Stilen - und meldet alles, was aus dem Panel oder aus dem Bild ragt.
 *
 * Gameface-Eigenheit nachgestellt: flex-shrink ist dort 0, solange eine Regel
 * nichts anderes sagt.
 *
 *   node run.js            alle Faelle, Exit 1 bei Befund
 *   node run.js --bild     zusaetzlich Bildschirmfotos nach out/bilder
 */
const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright-core");

const SPIEL = process.env.CSII_GAMEUI || "C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Content/Game/UI";
const OUT = path.join(__dirname, "out");
const SPIEL_URL = SPIEL.split(String.fromCharCode(92)).join("/");
const bilder = process.argv.includes("--bild");
const nurFall = (process.argv.find((a) => a.startsWith("--fall=")) || "").slice(7);

const AUFLOESUNGEN = [
  [1280, 720], [1366, 768], [1600, 900], [1920, 1080], [1920, 1200], [2560, 1080],
  [2560, 1440], [3440, 1440], [3840, 1080], [3840, 2160], [1280, 1024],
];
const STILE = ["horizontal", "hochkant"];
const SKALIERUNG = [true, false];
const TEXT = [1, 1.5];
/** Englisch ist der Standard, Deutsch und Russisch haben die laengsten Woerter. */
const SPRACHEN = (process.argv.find((a) => a.startsWith("--sprachen=")) || "--sprachen=en-US").slice(11).split(",");

/** Wie ParkingLotSprachdateien.OberflaechenJson: ui.*-Schluessel ohne Praefix, Englisch fuellt Luecken. */
const texte = (sprache) => {
  const lies = (s) => JSON.parse(fs.readFileSync(path.join(__dirname, "../../Lang", s + ".json"), "utf8").replace(/^﻿/, ""));
  const aus = {};
  for (const quelle of ["en-US", sprache])
    for (const [k, v] of Object.entries(lies(quelle))) if (k.startsWith("ui.")) aus[k.slice(3)] = v;
  return JSON.stringify(aus);
};

/**
 * EINE ECHT AUSSEHENDE PARKPLATZLISTE (Bindung "ParkplatzListe", Format aus
 * ParkingLotListeUISystem / liste.tsx): ohne sie misst der Reiter nur die
 * leere Liste - die Kacheln, die bei 150 % abgeschnitten waren (Nutzer 28.10.),
 * kaemen nie vor. Felder: id, Name, Plaetze, belegt, Gebuehr, Unterhalt,
 * Proben, Breite, Tiefe, Alter, Ergebnis, geschaetzt, Mangelblock (7),
 * Waise, Bauzettel, Sync, fehlende Assets, Wegebefund.
 */
const LISTE = [
  ["67394:1", "Parkplatz (2143 Stellplätze)", 2143, 1702, 12, 4650, 40, 180, 140, 31, 2400, 0, 72, 3, 0, 0, 0, "", "", 0, 1, 1, "", 0],
  ["69367:1", "Parking Lot (323 spaces)", 323, 12, 8, 1200, 40, 90, 70, 24, -300, 0, 70, 0, 0, 0, 0, "", "", 0, 1, 0, "", 3],
  ["82600:1", "Parking Lot (146 spaces)", 146, 140, 10, 700, 40, 60, 40, 18, 900, 0, 54, 2, 0, 0, 0, "", "", 0, 1, 0, "", 0],
  ["82601:1", "Parking Lot (287 spaces)", 287, 200, 10, 1100, 40, 80, 60, 18, 1500, 1, 0, 0, 0, 0, 0, "", "", 0, 1, 1, "", 0],
  ["85417:1", "Parking Lot (166 spaces)", 166, 5, 0, 640, 2, 60, 50, 12, -640, 0, 0, 0, 0, 0, 0, "", "", 0, 1, 0, "Herringbone Pavers", 0],
  ["85418:1", "Parking Lot (372 spaces)", 372, 371, 15, 1500, 40, 100, 70, 9, 3100, 0, 51, 4, 0, 0, 0, "", "", 0, 1, 0, "", 1],
  ["85419:1", "Parking Lot (440 spaces)", 440, 300, 12, 1700, 40, 110, 80, 40, 2000, 0, 31, 220, 0, 0, 0, "", "", 0, 1, 0, "", 0],
  ["496578:1", "Parking Lot (192 spaces)", 192, 0, 10, 820, 40, 70, 60, 31, -820, 0, 70, 0, 0, 0, 0, "", "", 0, 1, 0, "", 3],
].map((z) => z.join("\t")).join("\n");

/** CS2s eigene Rechnung (index.css): 16:9 oder schmaler -> Breite, sonst Hoehe. Aus = 1 px. */
const rem = (b, h, skaliert) => (!skaliert ? 1 : h >= b * 0.5625 ? b / 1920 : h / 1080);

const seite = (fall) => `<!doctype html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="file:///${SPIEL.replace(/\\/g, "/")}/index.css">
<style>
  @font-face { font-family: Overpass; font-weight: 400; src: url("file:///${SPIEL_URL}/Fonts/Overpass-Regular.ttf"); }
  @font-face { font-family: Overpass; font-weight: 700; src: url("file:///${SPIEL_URL}/Fonts/Overpass-Bold.ttf"); }
  html { font-size: ${fall.rem}px !important; }
  :root { --fontScale: ${fall.text}; --fontScaleChange: ${fall.text - 1}; }
  body { margin: 0; background: #3a4a5a; overflow: hidden; }
  #game { position: absolute; left: 0; top: 0; width: ${fall.rahmen === "eng" ? "560px" : "100vw"}; height: 100vh; }
  * { flex-shrink: 0; }
  ${process.env.PLT_OHNE_BALKEN ? '::-webkit-scrollbar { width: 0; height: 0; }' : ''}
</style></head><body><div id="game"></div>
<script>/* Wie CS2: Textgroesse als Inline-Stil an <html> - rahmen.tsx liest genau dort. */
document.documentElement.style.setProperty("--fontScale", "${fall.text}");
document.documentElement.style.setProperty("--fontScaleChange", "${fall.text - 1}");</script>
<script src="file:///${OUT.replace(/\\/g, "/")}/harness.js"></script></body></html>`;

/** Im Browser: alles, was aus dem Panel oder dem Bild ragt. */
function messen() {
  const W = innerWidth, H = innerHeight;
  const panel = [...document.querySelectorAll("div")].find((d) => /(^| )(stilHorizontal|stilHochkant)_/.test(String(d.className)));
  if (!panel) return { fehler: "kein Panel gerendert" };
  const pr = panel.getBoundingClientRect();
  const name = (el) => {
    const k = String(el.className || "").split(/\s+/).filter(Boolean).map((c) => c.replace(/_[A-Za-z0-9+/=-]{3}$/, ""));
    const t = (el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 24);
    return (k.length ? "." + k.join(".") : el.tagName.toLowerCase()) + (t ? ` "${t}"` : "");
  };
  const funde = new Map();
  const hinweise = new Map();
  const melde = (art, el, px) => {
    if (px <= 1) return;
    const k = art + " " + name(el);
    if (!funde.has(k) || funde.get(k) < px) funde.set(k, Math.round(px));
  };
  if (pr.left < -1) melde("Panel links aus dem Bild", panel, -pr.left);
  if (pr.top < -1) melde("Panel oben aus dem Bild", panel, -pr.top);
  if (pr.right > W + 1) melde("Panel rechts aus dem Bild", panel, pr.right - W);
  if (pr.bottom > H + 1) melde("Panel unten aus dem Bild", panel, pr.bottom - H);
  const klemmt = (el) => {
    for (let p = el.parentElement; p && p !== panel; p = p.parentElement) {
      const s = getComputedStyle(p);
      if (s.overflowX !== "visible" || s.overflowY !== "visible") return true;
    }
    return false;
  };
  for (const el of panel.querySelectorAll("*")) {
    const s = getComputedStyle(el);
    if (s.display === "none" || s.visibility === "hidden") continue;
    const r = el.getBoundingClientRect();
    if (r.width === 0 && r.height === 0) continue;
    if ((s.overflowX === "auto" || s.overflowX === "scroll" || s.overflowX === "hidden") && el.scrollWidth > el.clientWidth + 1) {
      // Gekuerzter Text (...) ist nach der Grundregel erlaubt - "nur Text gibt nach" -, aber zu zeigen.
      // Reiner Text, den das Panel bewusst abschneidet (textPlatz einzeilig), zaehlt wie "...".
      const nurText = el.children.length === 0 && s.whiteSpace === "nowrap";
      if (s.textOverflow === "ellipsis" || nurText) hinweise.set(name(el), Math.round(el.scrollWidth - el.clientWidth));
      else {
        // Wer ragt hinaus? Der breiteste Nachfahre ueber den rechten Innenrand.
        const kante = r.left + el.clientLeft + el.clientWidth;
        let schuld = null, weit = 0;
        for (const k of el.querySelectorAll("*")) {
          const kr = k.getBoundingClientRect();
          if (kr.width > 0 && kr.right - kante > weit) { weit = kr.right - kante; schuld = k; }
        }
        melde("waagerecht abgeschnitten/gescrollt" + (schuld ? " durch " + name(schuld).slice(0, 60) : ""), el, el.scrollWidth - el.clientWidth);
      }
    }
    if (klemmt(el)) continue;
    melde("ragt rechts aus dem Panel", el, r.right - pr.right);
    melde("ragt links aus dem Panel", el, pr.left - r.left);
    melde("ragt unten aus dem Panel", el, r.bottom - pr.bottom);
    melde("ragt rechts aus dem Bild", el, r.right - W);
    melde("ragt unten aus dem Bild", el, r.bottom - H);
  }
  return {
    panel: { links: Math.round(pr.left), oben: Math.round(pr.top), breite: Math.round(pr.width), hoehe: Math.round(pr.height) },
    funde: [...funde.entries()].map(([k, px]) => `${k}: ${px} px`),
    gekuerzt: [...hinweise.entries()].map(([k, px]) => `${k} (-${px} px)`),
  };
}

(async () => {
  fs.mkdirSync(path.join(OUT, "bilder"), { recursive: true });
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const page = await browser.newPage();
  const fehlerJs = [];
  page.on("pageerror", (e) => fehlerJs.push(String(e)));
  let befunde = 0, faelle = 0;
  const zeilen = [];
  const pruefe = async (kennung) => {
    faelle++;
    const m = await page.evaluate(messen);
    if (bilder) await page.screenshot({ path: path.join(OUT, "bilder", kennung.replace(/[ /]/g, "_") + ".png") });
    const n = m.funde ? m.funde.length : 1;
    befunde += n > 0 ? 1 : 0;
    zeilen.push(`${n ? "FEHLER" : "ok    "} ${kennung}  Panel ${m.panel ? `${m.panel.breite}x${m.panel.hoehe} @${m.panel.links},${m.panel.oben}` : m.fehler}`);
    for (const f of (m.funde || []).slice(0, 12)) zeilen.push("         " + f);
    if (m.funde && m.funde.length > 12) zeilen.push(`         ... ${m.funde.length - 12} weitere`);
    if (m.gekuerzt && m.gekuerzt.length) zeilen.push(`         gekuerzt (erlaubt): ${m.gekuerzt.join(", ")}`);
  };
  /** Einen Reiter oeffnen - hochkant steckt er bei langem Text in der Auswahlliste. */
  const reiter = async (name) => {
    const knopf = page.getByText(name, { exact: true }).first();
    if (!(await knopf.count())) {
      const wahl = page.locator("[class*='reiterWahl_']").first();
      if (!(await wahl.count())) return false;
      await wahl.click();
      await page.waitForTimeout(100);
      if (!(await page.getByText(name, { exact: true }).first().count())) return false;
    }
    await page.getByText(name, { exact: true }).first().click();
    await page.waitForTimeout(250);
    return true;
  };
  for (const sprache of SPRACHEN)
  for (const [b, h] of AUFLOESUNGEN)
    for (const skaliert of SKALIERUNG)
      for (const text of TEXT)
        for (const stil of STILE)
          // "eng": der umgebende Rahmen ist schmal (Spielerbild Marcaliseth) -
          // die Breite muss trotzdem aus dem Bildschirm kommen.
          for (const rahmen of skaliert && text === 1 ? ["voll", "eng"] : ["voll"]) {
          const fall = { b, h, skaliert, text, stil, rahmen, rem: rem(b, h, skaliert), texte: texte(sprache), liste: LISTE };
          const kennung = `${sprache} ${b}x${h} ${skaliert ? "skaliert" : "1px"} text${text * 100} ${stil}${rahmen === "eng" ? " engerRahmen" : ""}`;
          if (nurFall && !kennung.includes(nurFall)) continue;
          await page.setViewportSize({ width: b, height: h });
          await page.addInitScript((f) => {
            window.__PLT_REM__ = f.rem;
            window.__PLT_WERTE__ = { PanelOpen: true, ToolActive: true, PanelStil: f.stil, Sprachtexte: f.texte, Fangarten: 0xFFFF, ParkplatzListe: f.liste };
          }, fall);
          const html = path.join(OUT, "seite.html");
          fs.writeFileSync(html, seite(fall));
          await page.goto("file:///" + html.replace(/\\/g, "/"));
          await page.waitForTimeout(350);
          await pruefe(kennung + " / Entwurf");
          if (rahmen === "eng") continue;
          const t = JSON.parse(fall.texte);
          for (const schluessel of ["reiterListe", "reiterAnleitung", "reiterMelden"])
            if (await reiter(t[schluessel])) await pruefe(kennung + " / " + t[schluessel]);
            else { zeilen.push(`FEHLER ${kennung} / Reiter "${t[schluessel]}" nicht gefunden`); befunde++; faelle++; }
        }
  await browser.close();
  if (fehlerJs.length) zeilen.push("JS-Fehler: " + [...new Set(fehlerJs)].slice(0, 5).join(" | "));
  zeilen.push(`UI-LAYOUT: ${faelle} Faelle, ${befunde} mit Befund.`);
  fs.writeFileSync(path.join(OUT, "bericht.txt"), zeilen.join("\n"));
  console.log(zeilen.join("\n"));
  process.exit(befunde > 0 || fehlerJs.length ? 1 : 0);
})();
