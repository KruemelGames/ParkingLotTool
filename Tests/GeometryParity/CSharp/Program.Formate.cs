using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/**
 * FORMATWAECHTER fuer alles, was PLT in den Spielstand schreibt.
 *
 * CS2 rahmt je Komponententyp EINEN Block und prueft nur dessen
 * Gesamtlaenge; die Instanzen liegen darin ohne eigene Laenge
 * hintereinander. Liest eine andere PLT-Fassung pro Instanz ein Feld mehr
 * oder weniger, verrutscht jeder folgende Datensatz und der Spielstand laedt
 * nicht. Deshalb wird hier jede Serialize-/Deserialize-Methode im Quelltext
 * gefunden, normalisiert und mit dem eingefrorenen Stand in `formate.txt`
 * verglichen.
 *
 * Rot heisst NICHT "falsch", sondern "Entscheidung noetig":
 *  - neue Funktion mit neuen Daten -> bevorzugt einen NEUEN Komponententyp
 *    (dann ist nur der neue Typ "NEU", die alten bleiben gleich);
 *  - ein versionierter Typ darf hinten Felder anhaengen und die Version
 *    hochzaehlen - schuetzt neue Leser, NICHT den Downgrade;
 *  - ein Typ ohne Versionsfeld wird nie veraendert.
 * Ist die Entscheidung gefallen: `--formate-aufnehmen` schreibt den neuen
 * Stand. Ein entfernter Typ ist immer rot: alte Spielstaende tragen ihn noch.
 */
internal static partial class Program
{
    private sealed class Format
    {
        internal string Name;
        internal string Datei;
        internal string Koerper;
        internal bool Versioniert;
        internal string Fingerabdruck;
    }

    private static int PruefeFormate(bool aufnehmen)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
            "ParkingLotTool.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException(
            "ParkingLotTool.csproj nicht gefunden");
        var soll = Path.Combine(root.FullName, "Tests", "GeometryParity",
            "formate.txt");

        var gefunden = SammleFormate(root.FullName);
        var fehler = 0;
        foreach (var gruppe in gefunden.GroupBy(f => f.Name).Where(g => g.Count() > 1))
        {
            Console.WriteLine("DOPPELT: " + gruppe.Key);
            fehler++;
        }
        if (gefunden.Count < 10)
        {
            // Schutz gegen einen Sucher, der still nichts mehr findet.
            Console.WriteLine("ZU WENIG gefunden (" + gefunden.Count
                + ") - der Sucher ist kaputt, nicht der Quelltext.");
            return 1;
        }

        if (aufnehmen)
        {
            File.WriteAllLines(soll, gefunden.OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => f.Name + " " + f.Fingerabdruck
                    + (f.Versioniert ? " versioniert" : " eingefroren")));
            Console.WriteLine("FORMATE: " + gefunden.Count + " Typen aufgenommen in "
                + soll);
            return fehler == 0 ? 0 : 1;
        }

        if (!File.Exists(soll))
        {
            Console.WriteLine("formate.txt fehlt - einmal mit --formate-aufnehmen anlegen.");
            return 1;
        }
        var bekannt = File.ReadAllLines(soll)
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Select(z => z.Split(' '))
            .ToDictionary(t => t[0], t => t[1]);
        foreach (var f in gefunden.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (!bekannt.TryGetValue(f.Name, out var alt))
            {
                Console.WriteLine("NEU: " + f.Name + " (" + f.Datei
                    + ") - neuer Typ ist der sichere Weg; aufnehmen.");
                fehler++;
            }
            else if (alt != f.Fingerabdruck)
            {
                Console.WriteLine("GEAENDERT: " + f.Name + " (" + f.Datei + ") - "
                    + (f.Versioniert
                        ? "versioniert: nur hinten anhaengen + Version hoch; "
                          + "Downgrade laedt danach NICHT mehr."
                        : "OHNE Versionsfeld: darf nie geaendert werden, "
                          + "neuen Komponententyp anlegen."));
                fehler++;
            }
        }
        foreach (var name in bekannt.Keys.Except(gefunden.Select(f => f.Name)))
        {
            Console.WriteLine("ENTFERNT: " + name + " - alte Spielstaende "
                + "tragen ihn noch; nie entfernen, hoechstens stilllegen.");
            fehler++;
        }
        Console.WriteLine("FORMATE: " + gefunden.Count + " Typen, "
            + gefunden.Count(f => !f.Versioniert) + " eingefroren, "
            + fehler + " Abweichung(en).");
        return fehler == 0 ? 0 : 1;
    }

    private static List<Format> SammleFormate(string root)
    {
        var liste = new List<Format>();
        var dateien = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !Regex.IsMatch(p.Replace('\\', '/'),
                "/(bin|obj|Tests|node_modules)/"));
        var kopf = new Regex(
            @"\bstruct\s+(\w+)\s*:[^{]*\bISerializable\b[^{]*\{",
            RegexOptions.Singleline);
        foreach (var datei in dateien)
        {
            var text = OhneKommentare(File.ReadAllText(datei));
            foreach (Match m in kopf.Matches(text))
            {
                var start = m.Index + m.Length - 1;
                var ende = PassendeKlammer(text, start);
                var rumpf = text.Substring(start, ende - start + 1);
                var ser = Methodenkoerper(rumpf, "Serialize");
                var deser = Methodenkoerper(rumpf, "Deserialize");
                if (ser == null || deser == null)
                    throw new InvalidOperationException(m.Groups[1].Value
                        + ": Serialize/Deserialize nicht gefunden");
                // Auch Konstanten, die das Format steuern (Versionsnummer).
                var konstanten = string.Join(";", Regex.Matches(rumpf,
                    @"\bconst\s+\w+\s+\w+\s*=\s*[^;]+;").Select(k => k.Value));
                var koerper = Normalisiere(ser + "|" + deser + "|" + konstanten);
                liste.Add(new Format
                {
                    Name = m.Groups[1].Value,
                    Datei = Path.GetFileName(datei),
                    Koerper = koerper,
                    Versioniert = Regex.IsMatch(ser, @"writer\.Write\(\w*Version\w*\)"),
                    Fingerabdruck = Hash(koerper),
                });
            }
        }
        return liste;
    }

    private static string Methodenkoerper(string rumpf, string name)
    {
        var m = Regex.Match(rumpf, @"\bvoid\s+" + name + @"\s*<[^>]*>\s*\([^)]*\)[^{=]*");
        if (!m.Success) return null;
        var i = m.Index + m.Length;
        if (rumpf[i] == '=')
            return rumpf.Substring(i, rumpf.IndexOf(';', i) - i + 1);
        return rumpf.Substring(i, PassendeKlammer(rumpf, i) - i + 1);
    }

    private static int PassendeKlammer(string text, int auf)
    {
        var tiefe = 0;
        for (var i = auf; i < text.Length; i++)
        {
            if (text[i] == '{') tiefe++;
            else if (text[i] == '}' && --tiefe == 0) return i;
        }
        throw new InvalidOperationException("Klammer ohne Ende");
    }

    private static string OhneKommentare(string text)
        => Regex.Replace(text, @"/\*.*?\*/|//[^\n]*", "", RegexOptions.Singleline);

    private static string Normalisiere(string s) => Regex.Replace(s, @"\s+", "");

    private static string Hash(string s)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s))
            .Take(8).Select(b => b.ToString("x2")));
    }
}
