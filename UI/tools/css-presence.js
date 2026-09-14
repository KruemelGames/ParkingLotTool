const { Compilation, sources } = require("webpack");
const { RawSource } = sources;

/*
 * CS2 laedt die Stylesheet-Datei eines Mods NUR, wenn dessen Modul ein
 * `hasCSS` exportiert. Aus dem Spielbundle, Content/Game/UI/index.js:
 *
 *     import(e).then(t => {
 *       if (a.push(t.default), t.hasCSS) {
 *         const t = e.replace(".mjs", ".css");   // ... und laedt sie
 *       }
 *     })
 *
 * Ohne das Flag rendert der Mod also fehlerfrei - vollkommen ohne Stile.
 * Genau das ist am 2026-08-26 passiert und sah aus wie ein Layoutfehler:
 * roher Text im Dokumentfluss, der die Vanilla-Leiste wegschob.
 *
 * DIE URSACHE WAR EIN KOMMENTAR. Die alte Fassung schrieb das Flag per
 * `source.replace("export {", ...)` hinein - in die ERSTE Fundstelle im
 * gesamten Buendel. Sobald irgendeine Quelldatei die Zeichenfolge
 * "export {" enthielt, und sei es in einem Kommentar, landete das Flag
 * dort. Der Minifizierer entfernte den Kommentar samt Flag, und der Mod
 * ging ohne Stile ins Spiel. Der Bau blieb dabei gruen.
 *
 * Zwei Konsequenzen, beide hier eingebaut:
 *
 *   1. ANHAENGEN STATT ERSETZEN. Ein `export const hasCSS` am Dateiende
 *      braucht keine Fundstelle im fremden Text und kann nicht danebengreifen.
 *   2. NACH DEM MINIFIZIEREN. Stufe REPORT (5000) liegt hinter Terser (300),
 *      die alte Stufe lag davor - also konnte Terser das Flag noch verlieren.
 *      Die Konstante MUSS von der Klasse `Compilation` kommen: als
 *      `compilation.PROCESS_ASSETS_STAGE_*` ist sie `undefined`, webpack
 *      liest das als Stufe 0, und der Schritt liefe wieder vor Terser. Genau
 *      daran erkennt man es auch: dann steht am Ende `hasCSS=!0` statt
 *      `hasCSS=true`.
 *
 * Und geprueft wird es: fehlt das Flag am Ende doch, bricht der Bau ab,
 * statt ein stilloses Buendel auszuliefern.
 */
exports.CSSPresencePlugin = class CSSPresencePlugin {
  apply(compiler) {
    compiler.hooks.compilation.tap("CSSPresencePlugin", (compilation) => {
      compilation.hooks.processAssets.tap(
        {
          name: "CSSPresencePlugin",
          stage: Compilation.PROCESS_ASSETS_STAGE_REPORT,
        },
        () => {
          const hasCSS = Object.keys(compilation.assets).some((asset) =>
            asset.endsWith(".css")
          );

          let module = 0;
          for (const chunk of compilation.chunks) {
            for (const file of chunk.files) {
              if (!file.endsWith(".mjs")) continue;
              module++;

              const quelle = compilation
                .getAsset(file)
                .source.source()
                .toString();
              compilation.updateAsset(
                file,
                new RawSource(`${quelle}\nexport const hasCSS=${hasCSS};\n`)
              );

              const danach = compilation
                .getAsset(file)
                .source.source()
                .toString();
              if (!/\bexport const hasCSS=(true|false);/.test(danach)) {
                compilation.errors.push(
                  new Error(
                    `CSSPresencePlugin: ${file} traegt kein hasCSS. CS2 wuerde ` +
                      `die Stylesheet-Datei nicht laden und der Mod erschiene ohne Stile.`
                  )
                );
              }
            }
          }

          if (hasCSS && module === 0) {
            compilation.errors.push(
              new Error(
                "CSSPresencePlugin: kein .mjs gefunden, in das hasCSS gehoert. " +
                  "Die gebaute Stylesheet-Datei wuerde im Spiel nie geladen."
              )
            );
          }
        }
      );
    });
  }
};
