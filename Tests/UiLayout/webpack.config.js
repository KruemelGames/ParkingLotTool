/*
 * Baut das ECHTE Panel (UI/src) fuer den Layout-Pruefstand. Die cs2/*-Module
 * des Spiels werden durch Attrappen ersetzt (mocks/), alles andere ist der
 * Quelltext, der auch ins Spiel geht - dieselben SCSS-Module, dieselben Klassen.
 */
const path = require("path");
const UI = path.resolve(__dirname, "../../UI");
const attrappe = (n) => path.join(__dirname, "mocks", n);

module.exports = {
  mode: "development",
  devtool: false,
  stats: "errors-warnings",
  entry: path.join(__dirname, "harness.tsx"),
  output: { path: path.join(__dirname, "out"), filename: "harness.js" },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: [path.join(UI, "node_modules"), path.join(UI, "src")],
    alias: {
      "cs2/api": attrappe("cs2-api.tsx"),
      "cs2/ui": attrappe("cs2-ui.tsx"),
      "cs2/utils": attrappe("cs2-utils.tsx"),
      "cs2/l10n": attrappe("leer.tsx"),
      "cs2/bindings": attrappe("leer.tsx"),
      "cs2/input": attrappe("leer.tsx"),
      "cs2/modding": attrappe("leer.tsx"),
      "mod.json": path.join(UI, "mod.json"),
    },
  },
  resolveLoader: { modules: [path.join(UI, "node_modules")] },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: { loader: "ts-loader", options: { transpileOnly: true, configFile: path.join(UI, "tsconfig.json") } },
      },
      {
        test: /\.s?css$/,
        use: [
          "style-loader",
          {
            loader: "css-loader",
            options: {
              url: false,
              importLoaders: 1,
              modules: { auto: true, exportLocalsConvention: "camelCase", localIdentName: "[local]_[hash:base64:3]" },
            },
          },
          { loader: "sass-loader", options: { api: "modern" } },
        ],
      },
      { test: /\.(png|jpe?g|gif|svg)$/i, type: "asset/inline" },
    ],
  },
};
