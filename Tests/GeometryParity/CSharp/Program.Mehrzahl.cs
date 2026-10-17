using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;

internal static partial class Program
{
    private static string Sprachplatzhalter(string text, string key, string sprache, string referenz)
    {
        var namen = Sprachtexte.Platzhalter(text).Distinct();
        // Anzahl/tz liefern n immer. RU braucht es auch fuer 21/101 im Singular;
        // KO benutzt dieselbe Vorlage fuer beide Formen. EN sagt teils woertlich 1.
        if ((sprache == "ru-RU" || sprache == "pl-PL" || sprache == "ko-KR")
            && key.EndsWith(".one") && !Sprachtexte.Platzhalter(referenz).Contains("n"))
            namen = namen.Where(n => n != "n");
        return string.Join(",", namen.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static void PruefeMehrzahl(Action<string> fehler, string wurzel)
    {
        var en = Sprachtexte.Sprachen["en-US"];
        var gruppen = en.Keys.Where(k => k.EndsWith(".one")).Select(k => k.Substring(0, k.Length - 4)).ToArray();
        foreach (var sprache in new[] { "ru-RU", "pl-PL", "ko-KR" })
        {
            if (!Sprachtexte.Sprachen.TryGetValue(sprache, out var texte)) { fehler(sprache + ": Datei fehlt"); continue; }
            foreach (var k in gruppen)
            {
                if (sprache == "ko-KR")
                {
                    if (texte.TryGetValue(k + ".one", out var one) && texte.TryGetValue(k + ".other", out var other)
                        && one != other) fehler(sprache + ": unterschiedliche Formen bei " + k);
                    continue;
                }
                if (sprache == "ru-RU" && Sprachtexte.Platzhalter(en[k + ".other"]).Contains("n")
                    && texte.TryGetValue(k + ".one", out var singular) && !Sprachtexte.Platzhalter(singular).Contains("n"))
                    fehler(sprache + ": '" + k + ".one' braucht n auch fuer 21/101");
                foreach (var form in new[] { ".few", ".many" })
                    if (!texte.ContainsKey(k + form)) fehler(sprache + ": '" + k + form + "' fehlt");
            }
        }
        var vorher = Sprachtexte.Aktiv;
        var quelle = Sprachtexte.Ueberschreibung;
        var kultur = CultureInfo.CurrentCulture;
        var faelle = new List<object>();
        var anzahl = 0;
        void Ist(string wo, string ist, string soll)
        {
            anzahl++;
            if (ist != soll) fehler(wo + ": " + ist + ", erwartet " + soll);
        }
        try
        {
            Sprachtexte.Ueberschreibung = null;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            // Feste Erwartung, unabhaengig vom produktiven Algorithmus.
            var zahlen = new long[] { 0, 1, 2, 3, 4, 5, 10, 11, 12, 14, 20, 21, 22, 24, 25,
                101, 102, 111, 112, 121, 122, 125, 1000, 1001, -1, -2, -11, -21, long.MinValue };
            var ru = "many one few few few many many many many many many one few few many one few many many one few many many one one few many one many".Split(' ');
            var pl = "many one few few few many many many many many many many few few many many few many many many few many many many one few many many many".Split(' ');
            foreach (var sprache in Sprachtexte.Sprachen.Keys)
            {
                Sprachtexte.Aktiv = sprache;
                var tabelle = Sprachtexte.Mehrzahltabelle(sprache);
                for (var i = 0; i < zahlen.Length; i++)
                {
                    var n = zahlen[i];
                    var form = sprache == "ru-RU" ? "." + ru[i] : sprache == "pl-PL" ? "." + pl[i] : n == 1 ? ".one" : ".other";
                    Ist(sprache + "/" + n, Sprachtexte.Mehrzahlform(sprache, n), form);
                    if (n >= -9007199254740991L && n <= 9007199254740991L)
                        faelle.Add(new { sprache, n, form, tabelle,
                            erwartet = Sprachtexte.Anzahl("ui.listeAnzahl", n),
                            rueckfall = Sprachtexte.Format(Sprachtexte.RohIn(sprache, "ui.listeAnzahl.other"), new[] { ("n", (object)n) }) });
                    foreach (var k in gruppen)
                    {
                        if (!Sprachtexte.Hat(sprache, k + form)) continue;
                        var soll = Sprachtexte.Format(Sprachtexte.RohIn(sprache, k + form), new[] { ("n", (object)n) });
                        Ist(sprache + "/" + k + "/" + n, Sprachtexte.Anzahl(k, n), soll);
                    }
                }
                if (sprache == "ru-RU" || sprache == "pl-PL")
                    foreach (var n in new long[] { 1, 2, 5, 11, 12, 21, 22, 25, 101, 112, 122 })
                    {
                        var src = Sprachtexte.Format(Sprachtexte.RohIn("en-US", "ui.listeAnzahl" + (n == 1 ? ".one" : ".other")), new[] { ("n", (object)n) });
                        var ziel = Sprachtexte.Anzahl("ui.listeAnzahl", n);
                        Ist(sprache + "/Umsetzen/" + n, Sprachtexte.Umsetzen(src, "en-US", sprache), ziel);
                        Ist(sprache + "/Umsetzen-zurueck/" + n, Sprachtexte.Umsetzen(ziel, sprache, "en-US"), src);
                    }
            }
            foreach (var sprache in new[] { "ru-RU", "pl-PL", "ko-KR" })
            {
                Sprachtexte.Aktiv = sprache;
                foreach (var k in new[] { "markers.berichtGeschrieben", "ui.bauplaeneMeldung", "ui.infoLaufweite", "ui.syncFertig" })
                    Ist(sprache + "/Singular-ohne-Platzhalter/" + k,
                        Sprachtexte.Umsetzen(en[k + ".one"], "en-US", sprache), Sprachtexte.Anzahl(k, 1));
            }
            // Fehlende Formen muessen die aktive .other-Vorlage nutzen, auch in der UI.
            foreach (var sprache in new[] { "ru-RU", "pl-PL" })
            {
                Sprachtexte.Aktiv = sprache;
                var d = Sprachtexte.Sprachen[sprache];
                foreach (var (form, n) in new[] { (".few", 2L), (".many", 5L) })
                {
                    var key = "ui.listeAnzahl" + form;
                    if (!d.TryGetValue(key, out var alt)) continue;
                    try
                    {
                        d.Remove(key);
                        Ist(sprache + "/Rueckfall" + form, Sprachtexte.Anzahl("ui.listeAnzahl", n),
                            Sprachtexte.Format(d["ui.listeAnzahl.other"], new[] { ("n", (object)n) }));
                    }
                    finally { d[key] = alt; }
                }
            }
            var ablage = Path.Combine(wurzel, ".codex-build", "sprachpruefung");
            Directory.CreateDirectory(ablage);
            File.WriteAllText(Path.Combine(ablage, "mehrzahl.json"), JsonSerializer.Serialize(faelle));
        }
        finally { Sprachtexte.Aktiv = vorher; Sprachtexte.Ueberschreibung = quelle; CultureInfo.CurrentCulture = kultur; }
        Console.WriteLine("Mehrzahl: " + anzahl + " Pruefungen; UI-Faelle aus produktiver C#-Tabelle geschrieben.");
    }
}
