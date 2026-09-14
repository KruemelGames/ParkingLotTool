using System;
using System.Linq;
using System.Text;
using Unity.Mathematics;

/**
 * DIE SCHWARMFORMEN SICHTBAR MACHEN.
 *
 * Ansage des Nutzers am 2026-08-18: er will die 60 Formen ansehen koennen, um
 * zu beurteilen, ob sie ueberhaupt Sinn ergeben oder "kompletter Schnulli"
 * sind. Bisher existierten sie nirgends - sie wurden bei jedem Lauf aus
 * `SchwarmForm` neu gerechnet.
 *
 * WICHTIG: sie duerfen NICHT im Browser nachgebaut werden. Prototyp und Mod
 * haben unterschiedliche Zufallsfolgen (JS verliert bei seed * 1103515245 an
 * Genauigkeit, C# rechnet in long) - Form 21 dort waere nicht Form 21 hier.
 * Deshalb schreibt der Export sie aus C# heraus, und alles andere liest mit.
 *
 * Aufruf: dotnet run -c Release -- --formen-export [Anzahl]
 */
internal static partial class Program
{
    internal static int RunFormenExport(int anzahl)
    {
        var jsonPfad = FormenDatei();
        var htmlPfad = System.IO.Path.ChangeExtension(jsonPfad, ".html");

        /**
         * VORHANDENE FORMEN GEWINNEN.
         *
         * Sobald der Nutzer im Browser Formen geloescht oder eigene gezeichnet
         * hat, ist die JSON-Datei die Wahrheit - nicht mehr der Startwert.
         * Sonst wuerde ein Aufruf zum Auffrischen der Seite seine Arbeit
         * ueberschreiben. Nur wenn es die Datei nicht gibt, wird gesaet.
         */
        var formen = FormenAusDatei();
        if (formen.Length == 0)
        {
            _schwarmSeed = 12345;
            formen = Enumerable.Range(0, anzahl).Select(SchwarmForm).ToArray();
            Console.WriteLine($"Keine Datei gefunden - {anzahl} Formen neu gesaet.");
        }
        else
        {
            Console.WriteLine($"{formen.Length} Formen aus der Datei uebernommen.");
        }

        var json = new StringBuilder("[\n");
        for (var i = 0; i < formen.Length; i++)
        {
            json.Append("  [");
            json.Append(string.Join(",", formen[i].Select(p =>
                "[" + Z(p.x) + "," + Z(p.y) + "]")));
            json.Append(i + 1 < formen.Length ? "],\n" : "]\n");
        }
        json.Append("]\n");
        System.IO.File.WriteAllText(jsonPfad, json.ToString(), new UTF8Encoding(false));

        System.IO.File.WriteAllText(htmlPfad, BaueUebersicht(formen),
            new UTF8Encoding(false));
        Console.WriteLine($"{formen.Length} Formen geschrieben:");
        Console.WriteLine($"  {jsonPfad}");
        Console.WriteLine($"  {htmlPfad}");
        return 0;
    }

    /** Wo die bearbeitbare Formenliste liegt. */
    internal static string FormenDatei()
        => System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "StreetBlockAlgo", "schwarm-formen.json"));

    /**
     * Die Formen aus der Datei lesen - leer, wenn es sie nicht gibt.
     *
     * Bewusst ein winziger Leser statt einer JSON-Bibliothek: das Format ist
     * eine Liste von Punktlisten, mehr steht nicht drin.
     */
    internal static float2[][] FormenAusDatei()
    {
        var pfad = FormenDatei();
        if (!System.IO.File.Exists(pfad)) return Array.Empty<float2[]>();
        var text = System.IO.File.ReadAllText(pfad);
        var formen = new System.Collections.Generic.List<float2[]>();
        foreach (System.Text.RegularExpressions.Match form in
                 System.Text.RegularExpressions.Regex.Matches(
                     text, @"\[\s*(\[[^\]]*\]\s*,?\s*)+\]"))
        {
            var punkte = new System.Collections.Generic.List<float2>();
            foreach (System.Text.RegularExpressions.Match paar in
                     System.Text.RegularExpressions.Regex.Matches(
                         form.Value, @"\[\s*(-?[\d.eE+-]+)\s*,\s*(-?[\d.eE+-]+)\s*\]"))
                punkte.Add(new float2(
                    float.Parse(paar.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(paar.Groups[2].Value,
                        System.Globalization.CultureInfo.InvariantCulture)));
            if (punkte.Count >= 3) formen.Add(punkte.ToArray());
        }
        return formen.ToArray();
    }

    private static string Z(float v)
        => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /** Alle Formen als Raster, drei je Reihe - so wie der Nutzer es wollte. */
    private static string BaueUebersicht(float2[][] formen)
    {
        var html = new StringBuilder();
        html.Append(@"<!doctype html><html lang=""de""><head><meta charset=""utf-8"">
<title>Schwarmformen</title><style>
:root{--bg:#f6f6f4;--fg:#1a1a18;--karte:#fff;--rand:#d8d8d2;--flaeche:#c9dcc0;--linie:#2f6b3a}
@media (prefers-color-scheme:dark){:root{--bg:#16171a;--fg:#e8e8e4;--karte:#1e2024;--rand:#33363c;--flaeche:#2c4230;--linie:#7fc08c}}
body{margin:0;padding:24px;background:var(--bg);color:var(--fg);
font-family:system-ui,sans-serif}
h1{font-size:20px;margin:0 0 4px}
p.hint{margin:0 0 20px;opacity:.75;font-size:14px}
.raster{display:flex;flex-wrap:wrap;gap:16px}
.karte{background:var(--karte);border:1px solid var(--rand);border-radius:10px;
padding:10px;width:calc(33.333% - 11px);box-sizing:border-box}
@media (max-width:900px){.karte{width:calc(50% - 8px)}}
@media (max-width:600px){.karte{width:100%}}
.kopf{display:flex;justify-content:space-between;font-size:13px;margin-bottom:6px}
.kopf b{font-weight:600}
.kopf span{opacity:.7}
svg{width:100%;height:auto;display:block}
polygon{fill:var(--flaeche);stroke:var(--linie);stroke-width:2;
vector-effect:non-scaling-stroke}
circle{fill:var(--linie)}
</style></head><body>
<h1>Schwarmformen des Qualitaetstests</h1>
<p class=""hint"">Dieselben Formen, die <code>--qualitaet</code> rechnet
(Startwert 12345). Jede Karte zeigt Nummer, Eckenzahl und Flaeche.</p>
<div class=""raster"">
");
        for (var i = 0; i < formen.Length; i++)
        {
            var f = formen[i];
            double minX = f.Min(p => p.x), maxX = f.Max(p => p.x);
            double minY = f.Min(p => p.y), maxY = f.Max(p => p.y);
            var rand = 6.0;
            var b = maxX - minX + 2 * rand;
            var h = maxY - minY + 2 * rand;
            // y spiegeln, damit die Form so liegt wie im Spiel
            var punkte = string.Join(" ", f.Select(p =>
                $"{(p.x - minX + rand).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"{(maxY - p.y + rand).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"));
            var kreise = string.Join("", f.Select(p =>
                $"<circle cx=\"{(p.x - minX + rand).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\" " +
                $"cy=\"{(maxY - p.y + rand).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\" r=\"1.8\"/>"));
            html.Append($@"<div class=""karte"">
<div class=""kopf""><b>Form {i}</b><span>{f.Length} Ecken &middot; {FlaecheVon(f):F0} m&sup2;</span></div>
<svg viewBox=""0 0 {b.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {h.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"" preserveAspectRatio=""xMidYMid meet"">
<polygon points=""{punkte}""/>{kreise}</svg></div>
");
        }
        html.Append("</div></body></html>\n");
        return html.ToString();
    }
}
