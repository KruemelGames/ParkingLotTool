/*
 * FARB-PRUEFSTAND (FRQ-34): das echte Panel mit dem echten Spiel-Stylesheet, in
 * allen drei Oberflaechenstilen von CS2 und beiden Einstellungen.
 *
 * Geprueft wird die berechnete Farbe jedes Elements in jedem Reiter:
 *   - Spielschema: keine Spaltenfarbe aus "Bunt" mehr, die Akzentfarbe kommt vor.
 *   - Bunt: die Spaltenfarben sind da, und zwar in jedem Stil dieselben.
 * Bilder nach out/farben. Aufruf nach dem webpack-Lauf: node farben.js
 */
const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright-core");

const SPIEL = process.env.CSII_GAMEUI || "C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Content/Game/UI";
const OUT = path.join(__dirname, "out");
const BILDER = path.join(OUT, "farben");
const url = (p) => "file:///" + p.split(String.fromCharCode(92)).join("/");

const STILE = [
  ["default", "", "75, 195, 241"],
  ["bright-blue", "style--bright-blue", "35, 145, 219"],
  ["dark-grey-orange", "style--dark-grey-orange", "219, 145, 35"],
];
const BUNT = ["240, 173, 62", "79, 195, 247", "124, 212, 85"];
const ARTEN = ["224, 90, 43", "242, 193, 78", "126, 217, 87", "187, 53, 82", "255, 95, 176"];
/** Text aus `--menuText*` in bright-blue: dunkelblau auf dunklem Panel, kaum lesbar. */
const DUNKEL = ["0, 41, 59", "8, 61, 92", "0, 18, 51"];

const texte = () => {
  const roh = JSON.parse(fs.readFileSync(path.join(__dirname, "../../Lang/en-US.json"), "utf8").replace(/^\uFEFF/, ""));
  const aus = {};
  for (const [k, v] of Object.entries(roh)) if (k.startsWith("ui.")) aus[k.slice(3)] = v;
  return aus;
};

const seite = (klasse, werte) => `<!doctype html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="${url(SPIEL)}/index.css">
<style>
  @font-face { font-family: Overpass; font-weight: 400; src: url("${url(SPIEL)}/Fonts/Overpass-Regular.ttf"); }
  @font-face { font-family: Overpass; font-weight: 700; src: url("${url(SPIEL)}/Fonts/Overpass-Bold.ttf"); }
  html { font-size: 1px !important; }
  :root { --fontScale: 1; --fontScaleChange: 0; }
  body { margin: 0; background: #5d6b52; overflow: hidden; }
  #game { position: absolute; left: 0; top: 0; width: 100vw; height: 100vh; }
  * { flex-shrink: 0; }
</style></head><body class="${klasse}"><div id="game"></div>
<script>window.__PLT_REM__ = 1; window.__PLT_WERTE__ = ${JSON.stringify(werte)};</script>
<script src="${url(OUT)}/harness.js"></script></body></html>`;

/** Alle Farben, die im Bild wirklich stehen (Text, Grund, Rand), ohne Alpha. */
const farbenImBild = () => {
  const aus = new Set();
  for (const e of document.querySelectorAll("#game *")) {
    const s = getComputedStyle(e);
    const r = e.getBoundingClientRect();
    if (r.width === 0 || r.height === 0 || s.visibility === "hidden") continue;
    const felder = [s.backgroundColor];
    if (e.textContent && e.children.length === 0) felder.push(s.color);
    if (parseFloat(s.borderLeftWidth) > 0) felder.push(s.borderLeftColor);
    if (parseFloat(s.borderBottomWidth) > 0) felder.push(s.borderBottomColor);
    for (const f of felder) {
      const m = /rgba?\((\d+), (\d+), (\d+)(?:, ([\d.]+))?\)/.exec(f);
      if (m && m[4] !== "0") aus.add(`${m[1]}, ${m[2]}, ${m[3]}`);
    }
  }
  return [...aus];
};

(async () => {
  fs.mkdirSync(BILDER, { recursive: true });
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  const fehlerJs = [];
  page.on("pageerror", (e) => fehlerJs.push(String(e)));
  const t = texte();
  const zeilen = [];
  let pruefungen = 0, fehler = 0;
  const pruefe = (ok, text) => { pruefungen++; if (!ok) { fehler++; zeilen.push("FEHLER " + text); } };

  for (const [name, klasse, akzent] of STILE)
    for (const farben of ["bunt", "spiel"])
      for (const stil of ["horizontal", "hochkant"]) {
        const html = path.join(OUT, "farben.html");
        fs.writeFileSync(html, seite(klasse, { PanelOpen: true, ToolActive: true, Sprachtexte: JSON.stringify(t),
          Fangarten: 0xFFFF, PanelStil: stil, PanelFarben: farben }));
        await page.goto(url(html));
        await page.waitForTimeout(350);
        const fall = `${name} ${farben} ${stil}`;
        const klassen = await page.evaluate(() => document.body.className);
        pruefe(klassen.includes("plt-farben-spiel") === (farben === "spiel"), `${fall}: Klasse am body: "${klassen}"`);
        const gesehen = new Set();
        const reiter = [null, "reiterListe", "reiterAnleitung", "reiterMelden"];
        for (const r of reiter) {
          if (r) {
            let knopf = page.getByText(t[r], { exact: true }).first();
            if (!(await knopf.count())) {
              const wahl = page.locator("[class*='reiterWahl_']").first();
              if (await wahl.count()) { await wahl.click(); await page.waitForTimeout(100); }
              knopf = page.getByText(t[r], { exact: true }).first();
            }
            if (await knopf.count()) { await knopf.click(); await page.waitForTimeout(200); }
          }
          for (const f of await page.evaluate(farbenImBild)) gesehen.add(f);
          await page.screenshot({ path: path.join(BILDER, `${name}_${farben}_${stil}_${r || "entwurf"}.png`) });
        }
        for (const d of DUNKEL) pruefe(!gesehen.has(d), `${fall}: dunkler Menuetext ${d}`);
        if (farben === "spiel") {
          for (const b of [...BUNT, ...ARTEN]) pruefe(!gesehen.has(b), `${fall}: Spaltenfarbe ${b} steht noch im Bild`);
          pruefe(gesehen.has(akzent), `${fall}: Akzentfarbe ${akzent} fehlt`);
        } else {
          for (const b of BUNT) pruefe(gesehen.has(b), `${fall}: Spaltenfarbe ${b} fehlt`);
        }
      }
  await browser.close();
  if (fehlerJs.length) { fehler++; zeilen.push("JS-Fehler: " + [...new Set(fehlerJs)].slice(0, 5).join(" | ")); }
  zeilen.push(`UI-FARBEN: ${pruefungen} Pruefungen, ${fehler} Fehler.`);
  console.log(zeilen.join("\n"));
  process.exit(fehler ? 1 : 0);
})();
