using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ParkingLotTool.Geometry;

/**
 * `--sprache`: STEHT WIRKLICH ALLES IN DEN SPRACHDATEIEN?
 *
 * Anlass (Nutzer 2026-10-06): bei der ersten Uebersetzung ins Englische
 * blieben Texte liegen, die erst im Spiel auffielen. Dieser Lauf prueft, was
 * sich ohne Spiel pruefen laesst:
 *
 *   1. Jede Sprachdatei hat nur Schluessel, die es auf Englisch gibt;
 *      Deutsch und Portugiesisch (pt-BR) haben ALLE. Platzhalter sind je Schluessel in allen Sprachen
 *      dieselben, Klammern sind geschlossen, Mehrzahl hat .one UND .other.
 *   2. Jeder Schluessel, den der Code benutzt, steht in en-US.json - und
 *      jeder Schluessel in en-US.json wird benutzt.
 *   3. Die alten Muster sind weg: T("deutsch", "english"), `_deutsch`,
 *      De/En-Paare, t("…","…") und `=== "de"` in der Oberflaeche.
 *
 * Was er NICHT sieht: einen Text, der als Literal am Schluessel vorbei in die
 * Oberflaeche geht. Dafuer gibt es die Regel in AGENTS.md und die Durchsicht
 * der Ausgaenge (Bindungen, Tooltips, Statuszeile).
 */
internal static partial class Program
{
    private static int PruefeSprache()
    {
        var fehler = 0;
        void Fehler(string was) { fehler++; Console.WriteLine("FEHLER Sprache: " + was); }

        LadeSprachdateien();
        var wurzel = Directory.GetParent(Sprachordner()).FullName;
        if (!Sprachtexte.Sprachen.TryGetValue("en-US", out var en)) { Fehler("en-US.json fehlt"); return 1; }

        // --- 1. Dateien ------------------------------------------------------
        foreach (var (sprache, texte) in Sprachtexte.Sprachen)
        {
            foreach (var (k, v) in texte)
            {
                if (!en.ContainsKey(k)) Fehler($"{sprache}: '{k}' gibt es auf Englisch nicht");
                else if (string.Join(",", Sprachtexte.Platzhalter(v).Distinct())
                         != string.Join(",", Sprachtexte.Platzhalter(en[k]).Distinct()))
                    Fehler($"{sprache}: '{k}' hat andere Platzhalter als Englisch: '{v}'");
                if (!KlammernOk(v)) Fehler($"{sprache}: '{k}' hat eine offene oder leere Klammer: '{v}'");
                if (string.IsNullOrWhiteSpace(v) && !k.EndsWith("listentrenner")) Fehler($"{sprache}: '{k}' ist leer");
            }
        }
        // Doppelte Schluessel verschwinden beim Einlesen still (der letzte
        // gewinnt) - deshalb hier im Rohtext zaehlen.
        foreach (var datei in Directory.GetFiles(Sprachordner(), "*.json"))
        {
            var gesehen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(File.ReadAllText(datei), @"^\s*""((?:[^""\\]|\\.)*)""\s*:", RegexOptions.Multiline))
                if (!gesehen.Add(m.Groups[1].Value))
                    Fehler($"{Path.GetFileName(datei)}: '{m.Groups[1].Value}' steht doppelt");
        }
        if (Sprachtexte.Sprachen.TryGetValue("de-DE", out var de))
        {
            foreach (var k in en.Keys) if (!de.ContainsKey(k)) Fehler($"de-DE: '{k}' fehlt");
        }
        else Fehler("de-DE.json fehlt");
        if (Sprachtexte.Sprachen.TryGetValue("pt-BR", out var pt))
        {
            foreach (var k in en.Keys) if (!pt.ContainsKey(k)) Fehler($"pt-BR: '{k}' fehlt");
        }
        else Fehler("pt-BR.json fehlt");
        foreach (var k in en.Keys)
        {
            if (k.EndsWith(".one") && !en.ContainsKey(k.Substring(0, k.Length - 4) + ".other"))
                Fehler($"'{k}' ohne .other");
            if (k.EndsWith(".other") && !en.ContainsKey(k.Substring(0, k.Length - 6) + ".one"))
                Fehler($"'{k}' ohne .one");
        }

        // --- 2. Benutzt <-> vorhanden ---------------------------------------
        var benutzt = BenutzteSprachschluessel(wurzel, en, out var stellen);
        foreach (var k in benutzt.OrderBy(x => x, StringComparer.Ordinal))
            if (!en.ContainsKey(k)) Fehler($"'{k}' wird benutzt ({stellen[k]}), steht aber nicht in en-US.json");
        foreach (var k in en.Keys.OrderBy(x => x, StringComparer.Ordinal))
            if (!benutzt.Contains(k)) Fehler($"'{k}' steht in en-US.json, wird aber nirgends benutzt");

        // --- 3. Alte Muster ---------------------------------------------------
        var csAlt = new[]
        {
            (new Regex(@"(?<![\w.])T\(\s*""[^""]*""\s*,\s*""[^""]*""\s*\)"), "T(\"deutsch\", \"english\")"),
            (new Regex(@"\b_deutsch\b"), "_deutsch"),
            (new Regex(@"\(string De, string En\)"), "(string De, string En)"),
            (new Regex(@"SprachKuerzel\("), "SprachKuerzel()"),
        };
        foreach (var datei in CsDateien(wurzel))
        {
            var zeilen = File.ReadAllLines(datei);
            for (var i = 0; i < zeilen.Length; i++)
                foreach (var (re, name) in csAlt)
                    if (re.IsMatch(zeilen[i]) && !zeilen[i].TrimStart().StartsWith("*") && !zeilen[i].TrimStart().StartsWith("//"))
                        Fehler($"altes Muster {name}: {Path.GetFileName(datei)}:{i + 1}");
        }
        var uiAlt = new[]
        {
            (new Regex(@"(?<![\w.])t\(\s*""[^""]*""\s*,\s*""[^""]*""\s*\)"), "t(\"deutsch\", \"english\")"),
            (new Regex(@"===\s*""de"""), "=== \"de\""),
        };
        foreach (var datei in UiDateien(wurzel))
        {
            var zeilen = File.ReadAllLines(datei);
            for (var i = 0; i < zeilen.Length; i++)
                foreach (var (re, name) in uiAlt)
                    if (re.IsMatch(zeilen[i])) Fehler($"altes Muster {name}: {Path.GetFileName(datei)}:{i + 1}");
        }

        // --- 4. Aufrufe gegen Vorlagen (Codex-Audit 2026-10-06) -------------
        // Jeder Aufruf mit Schluessel als Literal: der Schluessel muss es
        // geben (auch in einer neuen Gruppe), und die uebergebenen Namen
        // muessen genau die Platzhalter der Vorlage sein - ein vertippter
        // Name liesse sonst "{name}" im Spiel stehen.
        var aufrufe = 0;
        var csAufruf = new Regex(@"(?<![\w.])(?:ParkingLotTexte\.|Sprachtexte\.)?(T|TN|S|Text|Anzahl)\(\s*""([a-zA-Z]+(?:\.[A-Za-z0-9_\-]+)+)""");
        foreach (var datei in CsDateien(wurzel))
        {
            var src = File.ReadAllText(datei);
            foreach (Match m in csAufruf.Matches(src))
            {
                if (ImKommentar(src, m.Index)) continue;
                // Praefix einer Schluesselfamilie ("x.seite" + ...): oben ausdruecklich.
                if (src.Substring(m.Index + m.Length).TrimStart().StartsWith("+")) continue;
                var art = m.Groups[1].Value;
                var k = m.Groups[2].Value;
                var ort = Path.GetFileName(datei) + ":" + Zeile(src, m.Index);
                var argumente = Argumente(src, src.IndexOf('(', m.Index));
                var namen = new HashSet<string>(argumente.Skip(1)
                    .Select(a => Regex.Match(a.Trim(), @"^\(\s*""([A-Za-z]\w*)""\s*,"))
                    .Where(x => x.Success).Select(x => x.Groups[1].Value));
                var mehrzahl = art == "TN" || art == "Anzahl";
                var vorlagen = mehrzahl ? new[] { k + ".one", k + ".other" } : new[] { k };
                aufrufe++;
                // WERTE MIT FESTEN WOERTERN (Codex-Audit 2026-10-06, "AN/AUS").
                // Ein Wert, der selbst ein Wort-Literal enthaelt - ausserhalb
                // eines eigenen T(...)-Aufrufs -, landet unuebersetzt im Satz.
                foreach (var arg in argumente.Skip(mehrzahl ? 2 : 1))
                {
                    var wertTeil = Regex.Replace(arg.Trim(), @"^\(\s*""[A-Za-z]\w*""\s*,", "");
                    var ohneUebersetzt = Regex.Replace(wertTeil,
                        @"(?:ParkingLotTexte\.|Sprachtexte\.)?(?:T|TN|Text|Anzahl|Dezimal)\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)", "");
                    foreach (Match lit in Regex.Matches(ohneUebersetzt, @"\$?""((?:[^""\\]|\\.)*)"""))
                        if (Regex.IsMatch(Regex.Replace(lit.Groups[1].Value, @"\{[^}]*\}", ""), "[A-Za-zÄÖÜäöüß]{2,}")
                            && !Regex.IsMatch(lit.Groups[1].Value, @"^[a-zA-Z]+(\.[A-Za-z0-9_\-]+)+$")
                            && !Regex.IsMatch(lit.Groups[1].Value, @"^[FNXGDfnxgd][0-9]*$")
                            // Datums-/Uhrzeitformate wie "HH:mm:ss" sind keine Woerter.
                            && !Regex.IsMatch(lit.Groups[1].Value, @"^[HhmsdMyf:.\-/ ]+$"))
                            Fehler($"{ort}: Wert mit festem Wort in '{k}': {lit.Value}");
                    if (Regex.IsMatch(ohneUebersetzt, @"\.Message\b"))
                        Fehler($"{ort}: Ausnahmetext als Wert in '{k}' - erreicht den Spieler roh");
                }
                foreach (var v in vorlagen)
                {
                    if (!en.TryGetValue(v, out var text)) { Fehler($"{ort}: '{v}' steht nicht in en-US.json"); continue; }
                    var soll = new HashSet<string>(Sprachtexte.Platzhalter(text));
                    if (mehrzahl) soll.Remove("n");
                    foreach (var p in soll.Where(p => !namen.Contains(p)))
                        Fehler($"{ort}: '{v}' braucht {{{p}}}, der Aufruf liefert es nicht");
                    if (!mehrzahl || v.EndsWith(".other"))
                        foreach (var p in namen.Where(p => !soll.Contains(p) && !(mehrzahl && p == "n")))
                            if (!vorlagen.Any(x => en.TryGetValue(x, out var t2) && Sprachtexte.Platzhalter(t2).Contains(p)))
                                Fehler($"{ort}: '{k}' bekommt ('{p}', …), die Vorlage hat kein {{{p}}}");
                }
            }
        }
        // Bedingte Schluessel: T(bedingung ? "a.x" : "b.y") - beide muessen existieren,
        // auch wenn ihre Gruppe neu ist.
        // Die Bedingung darf Methodenaufrufe enthalten (eine Klammerebene).
        // Geprueft werden auch die Platzhalter beider Zweige gegen die
        // uebergebenen Namen.
        var csBedingt = new Regex(@"(?<![\w.])(?:ParkingLotTexte\.)?(T|TN|S)\(\s*(?:[^""();]|\([^()]*\))*\?\s*""([a-zA-Z]+(?:\.[A-Za-z0-9_\-]+)+)""\s*:\s*""([a-zA-Z]+(?:\.[A-Za-z0-9_\-]+)+)""");
        foreach (var datei in CsDateien(wurzel))
        {
            var src = File.ReadAllText(datei);
            foreach (Match m in csBedingt.Matches(src))
            {
                if (ImKommentar(src, m.Index)) continue;
                var ort = $"{Path.GetFileName(datei)}:{Zeile(src, m.Index)}";
                var mehrzahl = m.Groups[1].Value == "TN";
                var argumente = Argumente(src, src.IndexOf('(', m.Index));
                var namen = new HashSet<string>(argumente.Skip(mehrzahl ? 2 : 1)
                    .Select(a => Regex.Match(a.Trim(), @"^\(\s*""([A-Za-z]\w*)""\s*,"))
                    .Where(x => x.Success).Select(x => x.Groups[1].Value));
                aufrufe++;
                for (var g = 2; g <= 3; g++)
                {
                    var k = m.Groups[g].Value;
                    var vorlage = en.TryGetValue(k, out var t1) ? t1
                        : en.TryGetValue(k + ".other", out var t2) ? t2 : null;
                    if (vorlage == null) { Fehler($"{ort}: '{k}' steht nicht in en-US.json"); continue; }
                    var soll = new HashSet<string>(Sprachtexte.Platzhalter(vorlage));
                    if (mehrzahl) soll.Remove("n");
                    foreach (var p in soll.Where(p => !namen.Contains(p)))
                        Fehler($"{ort}: '{k}' braucht {{{p}}}, der Aufruf liefert es nicht");
                }
            }
        }
        var uiAufruf = new Regex(@"\b(tx|tf|tz|satz|t|zahl)\(\s*""([A-Za-z0-9_.\-]+)""");
        foreach (var datei in UiDateien(wurzel))
        {
            var src = File.ReadAllText(datei);
            var istTexte = Path.GetFileName(datei) == "texte.ts";
            foreach (Match m in uiAufruf.Matches(src))
            {
                var art = m.Groups[1].Value;
                if (istTexte != (art == "tx" || art == "tf" || art == "tz" || art == "satz")) continue;
                if (!istTexte && !m.Groups[2].Value.Contains('.')) continue;
                var k = "ui." + m.Groups[2].Value;
                var ort = Path.GetFileName(datei) + ":" + Zeile(src, m.Index);
                var argumente = Argumente(src, src.IndexOf('(', m.Index));
                var namen = new HashSet<string>();
                // tz/zahl(schluessel, n, werte): die Werte stehen im DRITTEN Argument.
                var werteArg = art == "tz" || art == "zahl" ? 2 : 1;
                if (argumente.Count > werteArg && argumente[werteArg].TrimStart().StartsWith("{"))
                    foreach (var teil in Argumente(argumente[werteArg], argumente[werteArg].IndexOf('{'), '{', '}'))
                    {
                        var name = Regex.Match(teil.Trim(), @"^([A-Za-z_]\w*)");
                        if (name.Success) namen.Add(name.Groups[1].Value);
                    }
                var mehrzahl = art == "tz" || art == "zahl";
                var vorlagen = mehrzahl ? new[] { k + ".one", k + ".other" } : new[] { k };
                aufrufe++;
                foreach (var v in vorlagen)
                {
                    if (!en.TryGetValue(v, out var text)) { Fehler($"{ort}: '{v}' steht nicht in en-US.json"); continue; }
                    var soll = new HashSet<string>(Sprachtexte.Platzhalter(text));
                    if (mehrzahl) soll.Remove("n");
                    if (art == "satz") soll.Remove("ort");
                    // t("k") in Komponenten ohne Werte: Vorlage darf keine Platzhalter haben.
                    if (art == "t" && argumente.Count < 2 && soll.Count > 0)
                        Fehler($"{ort}: '{v}' hat Platzhalter, der Aufruf liefert keine Werte");
                    if (art == "tx" && soll.Count > 0)
                        Fehler($"{ort}: '{v}' hat Platzhalter, tx(...) setzt keine ein");
                    // Mehrzahl ohne Werte-Argument: dann darf die Vorlage nur {n} haben -
                    // das prueft die Schleife darunter mit leerer Namensliste.
                    if (art == "tx" || (art == "t" && argumente.Count < 2)) continue;
                    foreach (var p in soll.Where(p => !namen.Contains(p)))
                        Fehler($"{ort}: '{v}' braucht {{{p}}}, der Aufruf liefert es nicht");
                }
            }
        }

        // --- 4b. Umsetzen beim Sprachwechsel ------------------------------------
        // Jede Vorlage auf Englisch erzeugen, nach Deutsch umsetzen und mit der
        // direkt deutsch erzeugten vergleichen. Zwei Vorlagen mit gleichem
        // englischem Wortlaut duerfen nur dann verschieden uebersetzt sein, wenn
        // das Umsetzen sie trotzdem unterscheiden kann - sonst ist das ein Fund.
        var umgesetzt = 0;
        if (de != null)
            foreach (var (k, vorlage) in en)
            {
                // Keine angezeigten Zeilen: der rohe Durchreicher und das Trennzeichen.
                if (k == "hinweis.roh" || k == "ui.dezimaltrenner" || !de.ContainsKey(k)) continue;
                var namen = Sprachtexte.Platzhalter(vorlage).Distinct().ToArray();
                // Mehrzahl realistisch: .one mit 1, sonst 7, 18, ...
                var werte = namen.Select((n, i) => (n, (object)(n == "n" && k.EndsWith(".one") ? 1 : 7 + i * 11))).ToArray();
                var englisch = Sprachtexte.Format(vorlage, werte);
                if (Sprachtexte.Platzhalter(vorlage).Count > 0 && Regex.Replace(vorlage, @"\{[A-Za-z]\w*\}", "").Trim().Length == 0) continue;
                var soll = Sprachtexte.Format(de[k], werte);
                var ist = Sprachtexte.Umsetzen(englisch, "en-US", "de-DE");
                umgesetzt++;
                if (ist == soll) continue;
                // Gleicher englischer Text unter mehreren Schluesseln: nur ein Fund,
                // wenn das Deutsche wirklich abweicht.
                Fehler($"Umsetzen '{k}': \"{englisch}\" wurde \"{ist}\", erwartet \"{soll}\"");
            }

        // --- 5. Tote Baukasten-Eintraege ---------------------------------------
        // Ein Eintrag in texte.ts, den keine Komponente liest, haelt seine
        // Vorlage kuenstlich am Leben. Benutzt heisst: `.name` oder "name"
        // (fuer Tabellen wie ARTEN in panel.tsx, die per t[art.name] lesen).
        var texteTs = File.ReadAllText(Path.Combine(wurzel, "UI", "src", "mods", "texte.ts"));
        var komponenten = string.Join("\n", UiDateien(wurzel)
            .Where(d => Path.GetFileName(d) != "texte.ts").Select(File.ReadAllText));
        var rumpf = texteTs.Substring(texteTs.IndexOf("  return {", StringComparison.Ordinal));
        foreach (Match m in Regex.Matches(rumpf, @"^    ([A-Za-z0-9_]+):", RegexOptions.Multiline))
        {
            var name = m.Groups[1].Value;
            if (!Regex.IsMatch(komponenten, @"\.\s*" + name + @"\b|""" + name + @""""))
                Fehler($"texte.ts: Eintrag '{name}' liest keine Komponente");
        }

        Console.WriteLine($"Sprache: {Sprachtexte.Sprachen.Count} Dateien, {en.Count} Schluessel, "
            + $"{benutzt.Count} benutzt, {aufrufe} Aufrufe geprueft, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }

    private static int Zeile(string src, int index) => src.Take(index).Count(c => c == '\n') + 1;

    private static bool ImKommentar(string src, int index)
    {
        var anfang = src.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var vorne = src.Substring(anfang, index - anfang).TrimStart();
        return vorne.StartsWith("//") || vorne.StartsWith("*") || vorne.StartsWith("/*");
    }

    /**
     * Die Argumente eines Aufrufs ab der oeffnenden Klammer, auf oberster
     * Ebene an Kommas getrennt. Zeichenketten werden uebersprungen; das genuegt
     * fuer unsere Aufrufe (keine Klammern in Zeichen-Literalen).
     */
    private static List<string> Argumente(string src, int auf, char offen = '(', char zu = ')')
    {
        var aus = new List<string>();
        if (auf < 0) return aus;
        var tiefe = 0;
        var start = auf + 1;
        for (var i = auf; i < src.Length; i++)
        {
            var c = src[i];
            if (c == '"' || c == '`')
            {
                var j = i + 1;
                while (j < src.Length && src[j] != c) { if (src[j] == '\\') j++; j++; }
                i = j;
                continue;
            }
            if (c == '(' || c == '{' || c == '[') { tiefe++; continue; }
            if (c == ')' || c == '}' || c == ']')
            {
                tiefe--;
                if (tiefe == 0) { aus.Add(src.Substring(start, i - start)); return aus; }
                continue;
            }
            if (c == ',' && tiefe == 1) { aus.Add(src.Substring(start, i - start)); start = i + 1; }
        }
        return aus;
    }

    private static bool KlammernOk(string v)
    {
        for (var i = 0; i < v.Length; i++)
        {
            var c = v[i];
            if ((c == '{' || c == '}') && i + 1 < v.Length && v[i + 1] == c) { i++; continue; }
            if (c == '}') return false;
            if (c != '{') continue;
            var ende = v.IndexOf('}', i + 1);
            if (ende < 0) return false;
            var name = v.Substring(i + 1, ende - i - 1);
            if (!Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9]*$")) return false;
            i = ende;
        }
        return true;
    }

    private static IEnumerable<string> CsDateien(string wurzel)
    {
        foreach (var ordner in new[] { "Tools", "Geometry" })
            foreach (var f in Directory.GetFiles(Path.Combine(wurzel, ordner), "*.cs", SearchOption.AllDirectories))
                yield return f;
        yield return Path.Combine(wurzel, "Setting.cs");
        yield return Path.Combine(wurzel, "Mod.cs");
    }

    private static IEnumerable<string> UiDateien(string wurzel)
        => Directory.GetFiles(Path.Combine(wurzel, "UI", "src"), "*.ts*", SearchOption.AllDirectories);

    /**
     * Schluessel im Code. C#: Literale in Schluesselform, deren erste Silbe eine
     * Gruppe aus en-US.json ist; `TN(`/`Anzahl(` -> .one/.other; ein Literal mit
     * `+` dahinter ist das Praefix einer Familie (die steht unten ausdruecklich).
     * UI: `tx/tf/tz/satz("…")` in texte.ts, `t/zahl("…")` in den Komponenten.
     */
    private static HashSet<string> BenutzteSprachschluessel(string wurzel,
        Dictionary<string, string> en, out Dictionary<string, string> stellen)
    {
        var benutzt = new HashSet<string>(StringComparer.Ordinal);
        var wo = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string k, string ort) { benutzt.Add(k); if (!wo.ContainsKey(k)) wo[k] = ort; }
        var gruppen = new HashSet<string>(en.Keys.Select(k => k.Split('.')[0]).Where(g => g != "ui"));

        var literal = new Regex(@"(?<![@$])""([a-zA-Z]+(?:\.[A-Za-z0-9_\-]+)+)""");
        foreach (var datei in CsDateien(wurzel))
        {
            var src = File.ReadAllText(datei);
            foreach (Match m in literal.Matches(src))
            {
                var k = m.Groups[1].Value;
                if (!gruppen.Contains(k.Split('.')[0])) continue;
                var zeilenAnfang = src.LastIndexOf('\n', m.Index) + 1;
                var vorne = src.Substring(zeilenAnfang, m.Index - zeilenAnfang).TrimStart();
                if (vorne.StartsWith("//") || vorne.StartsWith("*")) continue;
                var danach = src.Substring(m.Index + m.Length, Math.Min(20, src.Length - m.Index - m.Length)).TrimStart();
                if (danach.StartsWith("+")) continue;
                var davor = src.Substring(Math.Max(0, m.Index - 40), Math.Min(40, m.Index));
                var ort = Path.GetFileName(datei) + ":" + (src.Take(m.Index).Count(c => c == '\n') + 1);
                if (Regex.IsMatch(davor, @"\b(TN|Anzahl)\(\s*$")) { Add(k + ".one", ort); Add(k + ".other", ort); }
                else Add(k, ort);
            }
        }
        // Familien: Mitglieder aus dem Code gelesen, nicht geraten.
        var ui = File.ReadAllText(Path.Combine(wurzel, "Tools", "ParkingLotUISystem.cs"));
        var bekannt = Regex.Match(ui, @"BekannteEinstellungen = new[^{]*\{([^}]*)\}");
        foreach (Match m in Regex.Matches(bekannt.Groups[1].Value, "\"([A-Za-z]+)\""))
            Add("einstellung." + m.Groups[1].Value, "Einstellungsname");
        foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(wurzel, "Geometry", "ParkingLanterns.cs")),
                     @"new LaternenSet \{ Id = ""([^""]+)"""))
            Add("laternenSet." + m.Groups[1].Value, "LaternenKatalog");
        foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(wurzel, "Tools", "ParkingLotVegetationUI.cs")),
                     @"AddVegetationSet\(""([^""]+)"""))
            Add("vegetationSet." + m.Groups[1].Value, "AddVegetationSet");
        foreach (var s in new[] { "LinksAn", "LinksAus", "RechtsAn", "RechtsAus" })
            Add("zoningRoadEdit.seite" + s, "ZoningRoadEdit");
        foreach (var z in new[] { "lesen", "schreiben", "ausfuehren" })
            foreach (var n in new[] { "", "Null" })
                Add("absturz.art." + z + n, "FehlerartAnzeige");

        var texte = new Regex(@"\b(tx|tf|tz|satz)\(\s*""([A-Za-z0-9_.\-]+)""");
        var bedingt = new Regex(@"\b(?:tx|tf)\(\s*[^""()]*\?\s*""([A-Za-z0-9_.\-]+)""\s*:\s*(?:[^""()]*\?\s*""([A-Za-z0-9_.\-]+)""\s*:\s*)?""([A-Za-z0-9_.\-]+)""");
        var komponente = new Regex(@"(?<![\w.])(t|zahl|text)\(\s*""([a-zA-Z]+\.[A-Za-z0-9_.\-]+)""");
        foreach (var datei in UiDateien(wurzel))
        {
            var src = File.ReadAllText(datei);
            var name = Path.GetFileName(datei);
            var istTexte = name == "texte.ts";
            foreach (Match m in (istTexte ? texte : komponente).Matches(src))
            {
                var k = "ui." + m.Groups[2].Value;
                if (m.Groups[1].Value == "tz" || m.Groups[1].Value == "zahl") { Add(k + ".one", name); Add(k + ".other", name); }
                else Add(k, name);
            }
            if (istTexte)
                foreach (Match m in bedingt.Matches(src))
                    for (var g = 1; g <= 3; g++)
                        if (m.Groups[g].Success && m.Groups[g].Value != "") Add("ui." + m.Groups[g].Value, name);
        }
        stellen = wo;
        return benutzt;
    }
}
