// Echten UI-Baukasten mit der produktiven C#-Regeltabelle pruefen; kein UI-Build.
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const assert = require("node:assert/strict");
const root = path.resolve(__dirname, "../..");
const ts = require(path.join(root, "UI/node_modules/typescript"));
let json = "{}";
let assertions = 0;
function load(file) {
  const exports = {};
  const module = { exports };
  const source = fs.readFileSync(path.join(root, file), "utf8");
  const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2018 } }).outputText;
  const requireStub = id => {
    if (id === "cs2/api") return { useValue: () => json };
    if (id === "./bindings") return { sprachtexte$: {} };
    if (id === "./mehrzahl") return load("UI/src/mods/mehrzahl.ts");
    throw new Error("Unerwarteter Import: " + id);
  };
  vm.runInNewContext(code, { module, exports, require: requireStub }, { filename: file });
  return module.exports;
}
try {
  const ui = load("UI/src/mods/texte.ts");
  const cases = JSON.parse(fs.readFileSync(path.join(root, ".codex-build/sprachpruefung/mehrzahl.json"), "utf8"));
  for (const c of cases) {
    const full = JSON.parse(fs.readFileSync(path.join(root, "Lang", c.sprache + ".json"), "utf8").replace(/^\uFEFF/, ""));
    const d = Object.fromEntries(Object.entries(full).filter(([k]) => k.startsWith("ui.")).map(([k,v]) => [k.slice(3),v]));
    if (c.tabelle !== null) d["#mehrzahl"] = c.tabelle;
    json = JSON.stringify(d);
    assert.equal(ui.useTexte().listeAnzahl(c.n), c.erwartet, `${c.sprache}/${c.n}`); assertions++;
    if (c.form === ".few" || c.form === ".many") {
      delete d["listeAnzahl" + c.form];
      json = JSON.stringify(d);
      assert.equal(ui.useTexte().listeAnzahl(c.n), c.rueckfall, `Rueckfall ${c.sprache}/${c.n}`); assertions++;
    }
  }
  // Ohne Tabelle bleibt der bisherige UI-Vertrag erhalten, auch bei Dezimalwerten.
  const selector = load("UI/src/mods/mehrzahl.ts").mehrzahlEndung;
  for (const n of [0,1,2,-1,1.5,NaN,Infinity]) {
    assert.equal(selector(undefined, n), n === 1 ? ".one" : ".other"); assertions++;
  }
  console.log(`UI-Mehrzahl: ${assertions} Pruefungen, 0 Fehler.`);
} catch (error) {
  console.error("FEHLER UI-Mehrzahl: " + (error.stack || error));
  process.exitCode = 1;
}
