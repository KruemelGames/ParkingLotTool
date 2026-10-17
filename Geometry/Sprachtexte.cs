using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ParkingLotTool.Geometry
{
    /**
     * ALLE SICHTBAREN TEXTE KOMMEN AUS DEN SPRACHDATEIEN (Nutzer 2026-10-06).
     *
     * Vorher standen sie an vier Stellen im Code - `T("deutsch", "english")`,
     * `_deutsch ? … : …` in den Einstellungen, De/En-Paare im Kern und zwei
     * Woerterbuecher in der Oberflaeche. Eine dritte Sprache haette jede
     * davon gebraucht, und kein Uebersetzer haette ohne Build etwas
     * verbessern koennen.
     *
     * Jetzt: eine flache JSON-Datei je Sprache (`Lang/en-US.json`,
     * `Lang/de-DE.json`, ...), Schluessel -> Vorlage. Platzhalter heissen
     * `{name}`; ein woertliches `{` schreibt sich `{{`. Fehlt ein Schluessel
     * in einer Sprache, gilt Englisch, fehlt er auch dort, steht der
     * Schluessel in eckigen Klammern da - sichtbar, nie still leer.
     *
     * Diese Klasse kennt kein Unity und kein JSON: der Mod (Newtonsoft) und
     * der Testlauf (System.Text.Json) fuellen `Sprachen` selbst.
     */
    public static class Sprachtexte
    {
        public const string Rueckfall = "en-US";

        /** Sprachkennung ("en-US", "de-DE", "zh-HANS") -> Schluessel -> Vorlage. */
        public static readonly Dictionary<string, Dictionary<string, string>> Sprachen =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /** Die Sprache, in der gerade angezeigt wird. */
        public static string Aktiv = Rueckfall;

        /**
         * Vorrangige Quelle, z. B. das Woerterbuch des Spiels (dort koennen
         * Mods wie I18n Everywhere Texte ersetzen). Null heisst: nur Dateien.
         */
        public static Func<string, string> Ueberschreibung;

        public static string Text(string schluessel, params (string Name, object Wert)[] werte)
            => Format(Roh(schluessel), werte);

        public static string TextIn(string sprache, string schluessel, params (string Name, object Wert)[] werte)
            => Format(RohIn(sprache, schluessel), werte);

        /**
         * Ganze Anzahlen: Russisch/Polnisch nutzen .one/.few/.many; sonst
         * bleibt .one fuer genau 1 und .other fuer alle anderen Werte.
         * Fehlendes .few/.many faellt auf .other zurueck. `{n}` ist immer da.
         */
        public static string Anzahl(string schluessel, long n, params (string Name, object Wert)[] werte)
        {
            var alle = new (string Name, object Wert)[(werte?.Length ?? 0) + 1];
            alle[0] = ("n", n);
            if (werte != null) Array.Copy(werte, 0, alle, 1, werte.Length);
            return Format(Roh(MehrzahlSchluessel(Aktiv, schluessel, n)), alle);
        }

        /** CLDR fuer ganze Zahlen; bestehende Sprachen behalten ihre bisherige Regel. */
        public static string Mehrzahlform(string sprache, long n)
        {
            var ru = string.Equals(sprache, "ru-RU", StringComparison.OrdinalIgnoreCase);
            var pl = string.Equals(sprache, "pl-PL", StringComparison.OrdinalIgnoreCase);
            if (!ru && !pl) return n == 1 ? ".one" : ".other";
            // Rest zuerst: Math.Abs(long.MinValue) waere ein Ueberlauf.
            var rest = Math.Abs(n % 100);
            var ende = rest % 10;
            if (ru ? ende == 1 && rest != 11 : n == 1 || n == -1) return ".one";
            return ende >= 2 && ende <= 4 && (rest < 12 || rest > 14) ? ".few" : ".many";
        }

        private static string MehrzahlSchluessel(string sprache, string schluessel, long n)
        {
            var form = Mehrzahlform(sprache, n);
            if ((form == ".few" || form == ".many") && !Hat(sprache, schluessel + form)) form = ".other";
            return schluessel + form;
        }

        /** Die UI liest dieselbe Regel als Tabelle: genau +/-1 separat, sonst Rest modulo 100. */
        public static string Mehrzahltabelle(string sprache)
        {
            if (Mehrzahlform(sprache, 0) == ".other") return null;
            var formen = new string[100];
            for (var i = 0; i < formen.Length; i++) formen[i] = Mehrzahlform(sprache, 100 + i);
            return string.Join(",", formen);
        }

        /**
         * Eine Dezimalzahl mit dem Trennzeichen der angezeigten Sprache.
         *
         * Nicht `CurrentCulture`: die gehoert zum Rechner, nicht zur Wahl im
         * Mod - ein deutscher Windows-Rechner zeigte sonst "42,5 m² per space"
         * im englischen Panel. Das Zeichen steht in der Sprachdatei
         * (`ui.dezimaltrenner`), dieselbe Stelle wie fuer die Oberflaeche.
         */
        public static string Dezimal(double wert, int stellen)
        {
            var text = wert.ToString("F" + stellen, CultureInfo.InvariantCulture);
            var trenner = Roh("ui.dezimaltrenner");
            return trenner == "." || trenner.StartsWith("[") ? text : text.Replace(".", trenner);
        }

        public static string Roh(string schluessel)
        {
            var anders = Ueberschreibung?.Invoke(schluessel);
            return !string.IsNullOrEmpty(anders) ? anders : RohIn(Aktiv, schluessel);
        }

        public static string RohIn(string sprache, string schluessel)
        {
            if (sprache != null && Sprachen.TryGetValue(sprache, out var d)
                && d.TryGetValue(schluessel, out var t)) return t;
            if (Sprachen.TryGetValue(Rueckfall, out var en) && en.TryGetValue(schluessel, out var e)) return e;
            return "[" + schluessel + "]";
        }

        public static bool Hat(string sprache, string schluessel)
            => Sprachen.TryGetValue(sprache, out var d) && d.ContainsKey(schluessel);

        /** Setzt `{name}` ein; `{{` und `}}` werden zu `{` und `}`. Unbekannte Namen bleiben stehen. */
        public static string Format(string vorlage, (string Name, object Wert)[] werte)
        {
            if (string.IsNullOrEmpty(vorlage) || vorlage.IndexOf('{') < 0 && vorlage.IndexOf('}') < 0) return vorlage;
            var sb = new StringBuilder(vorlage.Length + 16);
            for (var i = 0; i < vorlage.Length; i++)
            {
                var c = vorlage[i];
                if (c == '{' && i + 1 < vorlage.Length && vorlage[i + 1] == '{') { sb.Append('{'); i++; continue; }
                if (c == '}' && i + 1 < vorlage.Length && vorlage[i + 1] == '}') { sb.Append('}'); i++; continue; }
                if (c == '{')
                {
                    var ende = vorlage.IndexOf('}', i + 1);
                    if (ende > i)
                    {
                        var name = vorlage.Substring(i + 1, ende - i - 1);
                        var gefunden = false;
                        if (werte != null)
                            foreach (var w in werte)
                                if (w.Name == name)
                                {
                                    sb.Append(Convert.ToString(w.Wert, CultureInfo.CurrentCulture));
                                    gefunden = true;
                                    break;
                                }
                        if (!gefunden) sb.Append('{').Append(name).Append('}');
                        i = ende;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /**
         * EIN SCHON ANGEZEIGTER TEXT IN DER NEUEN SPRACHE.
         *
         * Die Statuszeile haelt fertigen Text, keinen Schluessel. Nach einem
         * Sprachwechsel stand dort deshalb weiter "Ready." (Nutzer
         * 2026-10-06). Statt alle Aufrufer umzubauen, wird der Text hier auf
         * seine Vorlage zurueckgefuehrt: die Vorlage der alten Sprache, deren
         * feste Teile am laengsten uebereinstimmen, gewinnt; die eingesetzten
         * Werte werden herausgelesen, selbst wieder umgesetzt (Mehrzahl,
         * verschachtelte Saetze) und in die Vorlage der neuen Sprache gesetzt.
         *
         * Findet sich keine Vorlage - etwa bei einem Dateipfad -, bleibt der
         * Text, wie er ist.
         */
        public static string Umsetzen(string text, string von, string nach)
        {
            if (string.IsNullOrEmpty(text) || string.Equals(von, nach, StringComparison.OrdinalIgnoreCase)
                || von == null || nach == null || !Sprachen.TryGetValue(von, out var alt))
                return text;
            return Umsetzen(text, alt, von, nach, 0);
        }

        private static readonly Dictionary<string, List<(string Schluessel, System.Text.RegularExpressions.Regex Muster, string[] Namen, int Fest)>> _muster =
            new Dictionary<string, List<(string, System.Text.RegularExpressions.Regex, string[], int)>>(StringComparer.OrdinalIgnoreCase);

        private static string Umsetzen(string text, Dictionary<string, string> alt, string von, string nach, int tiefe)
        {
            if (tiefe > 3 || string.IsNullOrEmpty(text)) return text;
            if (!_muster.TryGetValue(von, out var liste))
            {
                liste = new List<(string, System.Text.RegularExpressions.Regex, string[], int)>();
                foreach (var paar in alt)
                {
                    var namen = new List<string>();
                    var fest = 0;
                    var re = new StringBuilder("^");
                    var v = paar.Value;
                    for (var i = 0; i < v.Length; i++)
                    {
                        var c = v[i];
                        if ((c == '{' || c == '}') && i + 1 < v.Length && v[i + 1] == c)
                        { re.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString())); fest++; i++; continue; }
                        if (c == '{')
                        {
                            var ende = v.IndexOf('}', i + 1);
                            if (ende > i)
                            {
                                namen.Add(v.Substring(i + 1, ende - i - 1));
                                re.Append("(.*?)");
                                i = ende;
                                continue;
                            }
                        }
                        re.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                        fest++;
                    }
                    // Eine Vorlage ohne feste Teile ("{text}") passt auf alles.
                    if (fest == 0) continue;
                    // Reine Satzzeichen ohne Platzhalter (".", ", ", " und ")
                    // sind Bausteine, keine Zeilen - "." ist zugleich Satzende
                    // und englischer Dezimaltrenner und wuerde zum Komma.
                    if (namen.Count == 0 && !System.Linq.Enumerable.Any(v, char.IsLetterOrDigit)) continue;
                    re.Append('$');
                    liste.Add((paar.Key, new System.Text.RegularExpressions.Regex(re.ToString(),
                        System.Text.RegularExpressions.RegexOptions.Singleline), namen.ToArray(), fest));
                }
                liste.Sort((a, b) => b.Item4.CompareTo(a.Item4));
                _muster[von] = liste;
            }
            foreach (var (schluessel, muster, namen, _) in liste)
            {
                var treffer = muster.Match(text);
                if (!treffer.Success) continue;
                // EN/DE schreiben im Singular teils woertlich "1"/"ein".
                // Neue Zielvorlagen brauchen dafuer trotzdem den gelieferten Zaehler.
                var eins = schluessel.EndsWith(".one") && Array.IndexOf(namen, "n") < 0;
                var werte = new (string Name, object Wert)[namen.Length + (eins ? 1 : 0)];
                if (eins) werte[namen.Length] = ("n", 1L);
                var ziel = schluessel;
                for (var i = 0; i < namen.Length; i++)
                {
                    var wert = treffer.Groups[i + 1].Value;
                    werte[i] = (namen[i], Umsetzen(wert, alt, von, nach, tiefe + 1));
                    // Mehrzahl nach der Zahl waehlen, nicht nach dem Wortlaut:
                    // "species" ist im Englischen beides, "Art/Arten" nicht.
                    if (namen[i] == "n" && (schluessel.EndsWith(".one") || schluessel.EndsWith(".other")
                        || schluessel.EndsWith(".few") || schluessel.EndsWith(".many")))
                    {
                        var basis = schluessel.Substring(0, schluessel.LastIndexOf('.'));
                        ziel = long.TryParse(wert.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var zahl)
                            ? MehrzahlSchluessel(nach, basis, zahl)
                            : basis + (wert.Trim() == "1" ? ".one" : ".other");
                    }
                }
                return Format(RohIn(nach, ziel), werte);
            }
            return text;
        }

        /** Die Platzhalternamen einer Vorlage - fuer den Pruefslauf. */
        public static List<string> Platzhalter(string vorlage)
        {
            var namen = new List<string>();
            if (string.IsNullOrEmpty(vorlage)) return namen;
            for (var i = 0; i < vorlage.Length; i++)
            {
                if (vorlage[i] == '{' && i + 1 < vorlage.Length && vorlage[i + 1] == '{') { i++; continue; }
                if (vorlage[i] == '}' && i + 1 < vorlage.Length && vorlage[i + 1] == '}') { i++; continue; }
                if (vorlage[i] != '{') continue;
                var ende = vorlage.IndexOf('}', i + 1);
                if (ende < 0) break;
                namen.Add(vorlage.Substring(i + 1, ende - i - 1));
                i = ende;
            }
            namen.Sort(StringComparer.Ordinal);
            return namen;
        }
    }
}
