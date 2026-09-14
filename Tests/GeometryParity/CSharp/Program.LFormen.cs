using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Systematischer Test fuer einspringende Ecken.
     *
     * Ansage des Nutzers am 2026-08-21:
     *
     *   "Also die L form ist egal wie die aussieht, es geht nur darum dass sie
     *    eine oder mehrere Ecken hat die mehr als 180 Grad sind. Wie breit oder
     *    Lang oder so sollte voellig egal sein. Sogar egal ob das L auf dem Kopf
     *    steht oder ein Umgedrehtes L ist also Spiegelverkehrt."
     *
     * `--zellenlauf` misst nur die Formen, die der Nutzer zufaellig gezogen
     * hat. Das ist eine Stichprobe, keine Abdeckung: 15 gescheiterte L-Formen
     * sagen nichts darueber, WELCHE Eigenschaft sie scheitern laesst.
     *
     * Hier wird deshalb systematisch aufgespannt - Grundform, welche Ecke
     * fehlt, Seitenverhaeltnis, Drehwinkel, Lage im Weltkoordinatensystem und
     * Zeichenrichtung - und am Ende steht, welches Merkmal mit den Abbruechen
     * zusammenfaellt. Das ist der Unterschied zwischen "es scheitert" und
     * "es scheitert AN diesem hier".
     */
    private static int RunLFormen()
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Zellen = true;
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var formen = LFormenMatrix();
        Console.WriteLine($"L-FORMENTEST ueber {formen.Count} Formen, Zellenweg, "
            + "Modus edge, mit Zufahrt");
        Console.WriteLine("  Aufgespannt ueber Grundform x Ecke x Seitenverhaeltnis "
            + "x Drehung x Lage x Zeichenrichtung");
        Console.WriteLine();

        var ergebnisse = new List<(Lform Form, string Meldung, double Zeit, int Buchten)>();
        foreach (var form in formen)
        {
            var uhr = Stopwatch.StartNew();
            try
            {
                var layout = ParkingGeometry.Build(form.Site, einstellungen);
                uhr.Stop();
                ergebnisse.Add((form, layout.Stalls == 0 ? "0 Buchten (keine Ausnahme)" : null,
                    uhr.Elapsed.TotalMilliseconds, layout.Stalls));
            }
            catch (Exception ausnahme)
            {
                uhr.Stop();
                while (ausnahme.InnerException != null)
                    ausnahme = ausnahme.InnerException;
                ergebnisse.Add((form, ausnahme.Message,
                    uhr.Elapsed.TotalMilliseconds, 0));
            }
        }

        var schlecht = ergebnisse.Where(e => e.Meldung != null).ToList();
        Console.WriteLine($"  {ergebnisse.Count - schlecht.Count} von "
            + $"{ergebnisse.Count} gebaut, {schlecht.Count} abgebrochen "
            + $"({100d * schlecht.Count / ergebnisse.Count:F1} %)");
        Console.WriteLine();

        Console.WriteLine("  ABBRUECHE nach Meldung:");
        foreach (var gruppe in schlecht.GroupBy(e => e.Meldung)
                     .OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {gruppe.Count(),4}x  {gruppe.Key}");
        if (schlecht.Count == 0) Console.WriteLine("        keine");
        Console.WriteLine();

        // DAS ist der eigentliche Zweck des Laufs: nicht die Zahl, sondern
        // welches Merkmal mit ihr zusammenfaellt.
        Console.WriteLine("  Ausfallquote je Merkmal (nur so findet man die Ursache):");
        Merkmal("Grundform", e => e.Form.Grundform);
        Merkmal("fehlende Ecke", e => e.Form.Ecke.ToString());
        Merkmal("Seitenverhaeltnis", e => e.Form.Verhaeltnis);
        Merkmal("Drehung", e => $"{e.Form.Winkel,3} Grad");
        Merkmal("Lage", e => e.Form.Lage);
        Merkmal("Zeichenrichtung", e => e.Form.Richtung);
        Merkmal("einspringende Ecken", e => EinspringendeEcken(e.Form.Site).ToString());
        Console.WriteLine();

        void Merkmal(string name,
                     Func<(Lform Form, string Meldung, double Zeit, int Buchten), string> schluessel)
        {
            var zeilen = ergebnisse.GroupBy(schluessel).OrderBy(g => g.Key)
                .Select(g =>
                {
                    var aus = g.Count(e => e.Meldung != null);
                    return $"{g.Key} {100d * aus / g.Count():F0} % ({aus}/{g.Count()})";
                });
            Console.WriteLine($"    {name,-22} " + string.Join(" | ", zeilen));
        }

        // Ein paar Abbrueche namentlich, damit sie sich einzeln nachstellen lassen.
        foreach (var fall in schlecht.Take(8))
            Console.WriteLine($"  {fall.Form.Name}: " + string.Join(";",
                fall.Form.Site.Select(p => $"{p.x.ToString("0.###", Kultur)},"
                    + $"{p.y.ToString("0.###", Kultur)}")));

        // Der Nutzer verlangt ausdruecklich, dass JEDE dieser Formen geht.
        // Der Lauf ist deshalb rot, solange auch nur eine scheitert.
        return schlecht.Count == 0 ? 0 : 1;
    }

    /**
     * Arm- und Lueckenbreite fuer T und U, nie unter 45 m.
     *
     * 45 m traegt die Randstrasse (2 x 13,9 m) plus Rest. Die Luecke des U
     * ist b - 2w und bleibt damit ebenfalls ueber 45 m, solange b >= 135 ist -
     * das kleinste Mass in der Tabelle ist 140.
     */
    private static double ArmBreite(double breite) => Math.Max(45, breite * 0.28);

    private sealed class Lform
    {
        internal string Name;
        internal float2[] Site;
        internal string Grundform;
        internal int Ecke;
        internal string Verhaeltnis;
        internal int Winkel;
        internal string Lage;
        internal string Richtung;
    }

    /**
     * Alle Kombinationen, die laut Nutzer keinen Unterschied machen duerfen.
     *
     * Die Lage "fern" liegt dort, wo seine echten Parkplaetze liegen
     * (x um -1300, y um +200). Float hat dort rund 1e-4 m Aufloesung - falls
     * die Zerlegung daran haengt, trennt genau diese Spalte die Faelle.
     */
    private static List<Lform> LFormenMatrix()
    {
        var formen = new List<Lform>();
        // WARUM T UND U EIGENE MASSE BRAUCHEN.
        //
        // Der Ring liegt 13,9 m innerhalb der Arealkante (Es + Sl + Ai). Ein
        // Arm, der schmaler als 2 x 13,9 = 27,8 m ist, kann also gar keine
        // Randstrasse enthalten - der eingerueckte Rand stuelpt sich dort um.
        // Beim zweiten Lauf dieses Tests waren 340 der 452 Abbrueche genau
        // das: Arme von 15 bis 20 m Breite. Der Test verlangte etwas
        // geometrisch Unmoegliches.
        //
        // Hier stehen deshalb Arme und Luecken von mindestens 45 m. Was der
        // Zellenweg bei ZU SCHMALEN Armen tun soll - gar nichts bauen oder
        // nur den breiten Teil - ist eine offene Frage an den Nutzer und
        // gehoert nicht in diesen Lauf.
        var grundformen = new (string Name, Func<double, double, double, double, List<double[]>> Bauen)[]
        {
            ("L", (b, h, ab, ah) => new List<double[]>
            {
                new[] { 0.0, 0.0 }, new[] { b, 0.0 }, new[] { b, h - ah },
                new[] { b - ab, h - ah }, new[] { b - ab, h }, new[] { 0.0, h },
            }),
            // Der Steg ist bewusst halb so breit wie der L-Ausschnitt: mit
            // `ab` selbst faellt bei "schmal" (ab = b/2) der linke auf den
            // rechten Rand, das Polygon hat dann Nullkanten und Doppelpunkte.
            // Der erste Lauf dieses Tests hat genau daran 192 Formen
            // "abgebrochen" - ein Fehler im Test, nicht im Zellenweg.
            ("T", (b, h, ab, ah) =>
            {
                var w = ArmBreite(b);
                return new List<double[]>
                {
                    new[] { (b - w) / 2, 0.0 }, new[] { (b + w) / 2, 0.0 },
                    new[] { (b + w) / 2, h - ah }, new[] { b, h - ah },
                    new[] { b, h }, new[] { 0.0, h },
                    new[] { 0.0, h - ah }, new[] { (b - w) / 2, h - ah },
                };
            }),
            ("U", (b, h, ab, ah) =>
            {
                var w = ArmBreite(b);
                return new List<double[]>
                {
                    new[] { 0.0, 0.0 }, new[] { b, 0.0 }, new[] { b, h },
                    new[] { b - w, h }, new[] { b - w, ah },
                    new[] { w, ah }, new[] { w, h }, new[] { 0.0, h },
                };
            }),
        };
        // Schmal, breit, fast quadratisch, langgezogen - der Nutzer sagt
        // ausdruecklich, dass das egal sein soll.
        var masse = new (string Name, double B, double H, double Ab, double Ah)[]
        {
            ("schmal ", 140, 100, 70, 50),
            ("dick   ", 200, 140, 50, 45),
            ("lang   ", 300, 110, 150, 50),
            ("knapp  ", 160, 120, 55, 50),
        };
        var winkel = new[] { 0, 7, 23, 45, 61, 137 };
        var lagen = new (string Name, double X, double Y)[]
        {
            ("Ursprung", 0, 0),
            ("fern    ", -1300, 200),
        };

        foreach (var grund in grundformen)
        foreach (var mass in masse)
        foreach (var ecke in new[] { 0, 1, 2, 3 })
        foreach (var grad in winkel)
        foreach (var lage in lagen)
        foreach (var richtung in new[] { "CCW", "CW " })
        {
            var punkte = grund.Bauen(mass.B, mass.H, mass.Ab, mass.Ah);
            // Ecke 0..3: gespiegelt in x, in y, oder in beidem. Damit stehen
            // L, gespiegeltes L, L auf dem Kopf und die vierte Lage drin.
            if ((ecke & 1) != 0)
                punkte = punkte.Select(p => new[] { mass.B - p[0], p[1] }).ToList();
            if ((ecke & 2) != 0)
                punkte = punkte.Select(p => new[] { p[0], mass.H - p[1] }).ToList();
            // Jede einzelne Spiegelung dreht den Umlaufsinn um.
            if (((ecke & 1) != 0) ^ ((ecke & 2) != 0)) punkte.Reverse();

            var bogen = grad * Math.PI / 180;
            var cos = Math.Cos(bogen);
            var sin = Math.Sin(bogen);
            var mx = punkte.Average(p => p[0]);
            var my = punkte.Average(p => p[1]);
            var gedreht = punkte.Select(p =>
            {
                var x = p[0] - mx;
                var y = p[1] - my;
                return new float2(
                    (float)(lage.X + x * cos - y * sin),
                    (float)(lage.Y + x * sin + y * cos));
            }).ToList();
            if (richtung == "CW ") gedreht.Reverse();

            // SELBSTPRUEFUNG. Ohne sie misst der Lauf die eigenen Fehler und
            // schreibt sie dem Zellenweg zu - beim ersten Lauf am 2026-08-21
            // waren 192 der 448 "Abbrueche" entartete Polygone aus diesem
            // Generator.
            var erwartet = grund.Name == "L" ? 1 : 2;
            var tatsaechlich = EinspringendeEcken(gedreht.ToArray());
            if (tatsaechlich != erwartet)
                throw new InvalidOperationException(
                    $"Testgenerator: {grund.Name} {mass.Name} Ecke{ecke} hat "
                    + $"{tatsaechlich} einspringende Ecken statt {erwartet}.");
            for (var i = 0; i < gedreht.Count; i++)
            {
                var a = gedreht[i];
                var b2 = gedreht[(i + 1) % gedreht.Count];
                if (math.distance(a, b2) < 1e-3f)
                    throw new InvalidOperationException(
                        $"Testgenerator: {grund.Name} {mass.Name} Ecke{ecke} hat "
                        + $"eine Nullkante bei Punkt {i}.");
            }

            formen.Add(new Lform
            {
                Name = $"{grund.Name} {mass.Name} Ecke{ecke} {grad,3}Grad "
                    + $"{lage.Name} {richtung}",
                Site = gedreht.ToArray(),
                Grundform = grund.Name,
                Ecke = ecke,
                Verhaeltnis = mass.Name,
                Winkel = grad,
                Lage = lage.Name,
                Richtung = richtung,
            });
        }
        return formen;
    }
}
