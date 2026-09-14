using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;

internal static partial class Program
{
    /**
     * DER GEMELDETE FALL VOM 2026-08-25, NACHGEBAUT.
     *
     * Nutzerbefund, Markierung 1: "ein einzelnes Rechteck mitten auf der
     * Strasse, hat die Breite der Strasse, aber wird nicht zur Strasse
     * hinzugefuegt."
     *
     * Aus dem Abzug des Spiels (ParkingLotTool-debug-latest.json,
     * gebaut 2026-08-25 12:32:26) sind Polygon, Einstellungen und Zufahrt
     * uebernommen - nicht nachempfunden, sondern abgeschrieben. Im Abzug ist
     * die Uebeltaeterin Flaeche 7: 7,000 x 5,900 m, Tiefe 6,900 bis 13,900 m
     * unter der Arealkante 3 - also GENAU das Band der Randstrasse -, und
     * 0,025 m von der Zufahrtsflaeche entfernt.
     *
     * Diese Hilfe misst denselben Fall im Zellenweg und beantwortet zwei
     * Fragen, die der Abzug nicht beantworten kann:
     *
     *   1. Welche ROLLE tragen die Zellen dieses Rechtecks? Zwei Nachbarn
     *      verschmelzen nur bei gleichem Material UND gleicher Rollengruppe.
     *   2. Teilen sie sich mit ihren Nachbarn eine EXAKTE Kante? Nur dann
     *      kann `Vereinigung` sie ueberhaupt finden - ein T-Stoss, bei dem
     *      die eine Seite geteilt ist und die andere nicht, faellt durch.
     *
     * Sie veraendert keinen Produktionscode.
     */
    private static readonly Punkt[] BefundPolygon =
    {
        new Punkt(-1184.379150390625, 83.07157135009766),
        new Punkt(-1194.646728515625, 259.699462890625),
        new Punkt(-1299.8468017578125, 253.58273315429688),
        new Punkt(-1289.5440673828125, 76.56694793701172),
    };

    private const int BefundZufahrtKante = 3;
    private const double BefundZufahrtAlong = 52.688690185546875;

    private static int RunBefund()
    {
        Console.WriteLine("BEFUND 1 - einzelnes Rechteck auf der Randstrasse");
        Console.WriteLine("  Fall aus ParkingLotTool-debug-latest.json, "
            + "gebaut 2026-08-25 12:32:26.");
        Console.WriteLine("  Gesucht: Tiefe 6,900..13,900 m unter Kante 3, "
            + "entlang 56,21..62,14 m.");
        Console.WriteLine();

        var bau = BaueBefund();

        // Die Kante in LOKALEN Koordinaten - Layoutbauer rechnet im Reihenrahmen.
        var areal = bau.ArealLokal;
        var a = areal.Knoten(BefundZufahrtKante).Punkt;
        var b = areal.Knoten(BefundZufahrtKante + 1).Punkt;
        var laenge = Geometrie.Laenge(b - a);
        var tangente = (b - a) * (1 / laenge);
        var normale = new Punkt(-tangente.Y, tangente.X);
        double Entlang(Punkt q) => Geometrie.Skalar(q - a, tangente);
        double Tiefe(Punkt q) => Geometrie.Skalar(q - a, normale);

        Console.WriteLine($"  Kante {BefundZufahrtKante} ist lokal {laenge:F3} m lang, "
            + $"Zufahrt bei {BefundZufahrtAlong:F3} m.");
        Console.WriteLine();

        // 1. Die ZELLEN im Randstrassenband entlang dieser Kante.
        Console.WriteLine("  Zellen im Band der Randstrasse (Tiefe 6,4..14,4 m):");
        var band = bau.Zellen
            .Select(z => new { Zelle = z, Mitte = Geometrie.Mittelwert(z.Polygon) })
            .Where(x => Tiefe(x.Mitte) > 6.4 && Tiefe(x.Mitte) < 14.4
                        && Entlang(x.Mitte) > 40 && Entlang(x.Mitte) < 75)
            .OrderBy(x => Entlang(x.Mitte))
            .ToList();
        foreach (var x in band)
            Console.WriteLine($"    entlang {Entlang(x.Mitte),7:F2}  "
                + $"tiefe {Tiefe(x.Mitte),6:F2}  {x.Zelle.Art,-13} "
                + $"{x.Zelle.Material,-8} {Flaeche(x.Zelle.Polygon),7:F2} m2"
                + (x.Zelle.QuerstrassenId.HasValue
                    ? $"  Quer {x.Zelle.QuerstrassenId}" : ""));

        /*
         * VOR und NACH der Lochtrennung.
         *
         * `Vereinigung` laeuft zuerst und fasst gleiche Rollen zusammen.
         * Danach schneidet `Lochtrennung` Korridore, damit eine Flaeche mit
         * Loch einfach zusammenhaengend wird. Erscheint das Rechteck erst
         * DANACH als eigene Flaeche, ist die Trennung die Ursache - nicht die
         * Vereinigung.
         */
        Console.WriteLine();
        Console.WriteLine("  VOR der Lochtrennung, Schwerpunkt im Streifen:");
        foreach (var f in bau.FlaechenVorTrennung)
        {
            var r0 = f.Aussenring.Knoten.Select(k => k.Punkt).ToList();
            var m0 = Mittelwert(r0);
            if (Tiefe(m0) <= 6.4 || Tiefe(m0) >= 14.4) continue;
            if (Entlang(m0) <= 40 || Entlang(m0) >= 75) continue;
            Console.WriteLine($"    entlang {Entlang(m0),7:F2}  tiefe {Tiefe(m0),6:F2}"
                + $"  {f.Material,-8} {r0.Count,3} Ecken  "
                + $"{Math.Abs(Geometrie.Vorzeichenflaeche(r0)),8:F2} m2  "
                + $"Loecher {f.Loecher.Count}");
        }
        Console.WriteLine($"    (insgesamt vor der Trennung "
            + $"{bau.FlaechenVorTrennung.Count}, nachher {bau.Flaechen.Count})");

        /*
         * DAS LOCH IST DIE URSACHE, NICHT DIE TRENNUNG.
         *
         * Die Trennung schneidet nur, WEIL eine Flaeche ein Loch hat - sie
         * macht aus dem Loch eine zweite Flaeche. Ohne Loch kein Schnitt und
         * ohne Schnitt kein einzelnes Rechteck. Also: welche Flaeche hat ein
         * Loch, wie gross ist es, und was liegt darin?
         */
        Console.WriteLine();
        Console.WriteLine("  Flaechen MIT Loch, vor der Trennung:");
        foreach (var f in bau.FlaechenVorTrennung.Where(x => x.Loecher.Count != 0))
        {
            var aussen = f.Aussenring.Knoten.Select(k => k.Punkt).ToList();
            Console.WriteLine($"    {f.Material,-8} aussen "
                + $"{Math.Abs(Geometrie.Vorzeichenflaeche(aussen)),9:F2} m2, "
                + $"{f.Loecher.Count} Loch/Loecher, {f.ZellIds.Count} Zellen");
            foreach (var loch in f.Loecher)
            {
                var lr = loch.Knoten.Select(k => k.Punkt).ToList();
                var lm = Mittelwert(lr);
                Console.WriteLine($"      Loch {Math.Abs(Geometrie.Vorzeichenflaeche(lr)),8:F2} m2"
                    + $"  {lr.Count,3} Ecken  Mitte entlang {Entlang(lm),7:F2}"
                    + $"  tiefe {Tiefe(lm),7:F2}");
                // Was liegt IM Loch? Zellen, deren Mitte darin liegt.
                var drin = bau.Zellen
                    .Where(z => Geometrie.Enthaelt(lr, Geometrie.Mittelwert(z.Polygon)))
                    .GroupBy(z => new { z.Art, z.Material })
                    .OrderByDescending(g => g.Sum(z => Flaeche(z.Polygon)));
                foreach (var g in drin)
                    Console.WriteLine($"        darin: {g.Key.Art,-13} "
                        + $"{g.Key.Material,-8} {g.Count(),3} Zellen "
                        + $"{g.Sum(z => Flaeche(z.Polygon)),8:F2} m2");
            }
        }
        if (bau.Lochtrennung != null)
            Console.WriteLine($"    Lochtrennungsbericht: Loecher "
                + $"{bau.Lochtrennung.LoecherVorher} -> "
                + $"{bau.Lochtrennung.LoecherNachher}, Flaechen "
                + $"{bau.Lochtrennung.FlaechenVorher} -> "
                + $"{bau.Lochtrennung.FlaechenNachher}, Naehte "
                + $"{bau.Lochtrennung.Trennnaehte.Count}");

        // 2. Die fertigen FLAECHEN, die diesen Streifen beruehren.
        Console.WriteLine();
        Console.WriteLine("  NACH der Lochtrennung, Schwerpunkt im Streifen:");
        foreach (var f in bau.Flaechen)
        {
            var ring = f.Aussenring.Knoten.Select(k => k.Punkt).ToList();
            var mitte = Mittelwert(ring);
            if (Tiefe(mitte) <= 6.4 || Tiefe(mitte) >= 14.4) continue;
            if (Entlang(mitte) <= 40 || Entlang(mitte) >= 75) continue;
            Console.WriteLine($"    entlang {Entlang(mitte),7:F2}  "
                + $"tiefe {Tiefe(mitte),6:F2}  {f.Material,-8} {ring.Count,3} Ecken  "
                + $"{Math.Abs(Geometrie.Vorzeichenflaeche(ring)),8:F2} m2");
            Console.WriteLine("        Kanten: " + string.Join(", ",
                Kantenlaengen(ring).Select(k => k.ToString("F3"))));
            Console.WriteLine($"        entlang {ring.Min(Entlang):F3}"
                + $" .. {ring.Max(Entlang):F3}"
                + $" | tiefe {ring.Min(Tiefe):F3} .. {ring.Max(Tiefe):F3}");
        }
        var auffaelligeFlaechen = bau.Flaechen.Where(f =>
        {
            if (f.Material != Material.Asphalt) return false;
            var ring = f.Aussenring.Knoten.Select(k => k.Punkt).ToList();
            return Math.Abs(Math.Abs(Geometrie.Vorzeichenflaeche(ring)) - 41.30)
                    < 0.01
                && Math.Abs(ring.Min(Tiefe) - 6.9) < 0.01
                && Math.Abs(ring.Max(Tiefe) - 13.9) < 0.01
                && ring.Min(Entlang) > 55
                && ring.Max(Entlang) < 63;
        }).ToList();
        Console.WriteLine($"    Exakte gemeldete 7,000-x-5,900-m-Flaechen: "
            + $"{auffaelligeFlaechen.Count}");
        var randstrassenflaechen = bau.Flaechen
            .Where(f => f.ZellIds.Any(id =>
                bau.Zellen[id].Art == Zellart.Randstrasse))
            .OrderBy(f => f.Id)
            .ToList();
        Console.WriteLine($"    Geplante Randstrassenflaechen: "
            + $"{randstrassenflaechen.Count}; Flaechen "
            + string.Join(" + ", randstrassenflaechen.Select(f =>
                f.Flaecheninhalt.ToString("F2"))) + " m2");

        /*
         * DAS RISIKO DIESES UMBAUS: aus einem Ring wurden zwei Boegen, also
         * gibt es jetzt ZWEI Stossstellen. Liegen sie nicht exakt aufeinander,
         * hat man das eine Rechteck gegen zwei Fugen getauscht - und Fugen
         * sieht der Nutzer im Spiel als Narbe. Deshalb wird hier gemessen,
         * ob die beiden Flaechen an ihren Stoessen dieselben Knoten benutzen.
         */
        if (randstrassenflaechen.Count == 2)
        {
            var r1 = randstrassenflaechen[0].Aussenring.Knoten
                .Select(k => k.Punkt).ToList();
            var r2 = randstrassenflaechen[1].Aussenring.Knoten
                .Select(k => k.Punkt).ToList();
            var gemeinsam = r1.Count(a1 => r2.Any(a2 =>
                Geometrie.Laenge(a1 - a2) < 1e-9));
            var kleinster = double.MaxValue;
            foreach (var a1 in r1)
                foreach (var a2 in r2)
                    kleinster = Math.Min(kleinster, Geometrie.Laenge(a1 - a2));
            Console.WriteLine($"    Stossstellen: {gemeinsam} exakt gemeinsame "
                + $"Knoten, kleinster Knotenabstand {kleinster:F9} m");
            Console.WriteLine(gemeinsam >= 4
                ? "      -> beide Stoesse teilen sich Knoten, keine Fuge."
                : "      -> ACHTUNG: zu wenige gemeinsame Knoten, Fuge moeglich.");
        }
        /**
         * WARUM VERSCHMILZT SIE NICHT?
         *
         * `Vereinigung` legt einen Index ueber ALLE Zellkanten und
         * vereinigt zwei Zellen nur dort, wo genau ZWEI Kanten denselben
         * Schluessel tragen. Traegt eine Kante nur einen Fund, hat die
         * Nachbarzelle an dieser Stelle keine deckungsgleiche Kante - ein
         * T-Stoss. Dann bleibt die Zelle allein, egal wie gleich ihre Rolle
         * ist.
         *
         * Hier wird derselbe Index nachgebaut und fuer jede Kante der
         * auffaelligen Zelle gezaehlt, wie viele Zellen sie teilen.
         */
        Console.WriteLine();
        Console.WriteLine("  Kantenbilanz der auffaelligen Zelle:");
        var index = new Dictionary<KantenSchluessel, List<int>>();
        foreach (var zelle in bau.Zellen)
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var kante = new GerichteteKante(
                    zelle.Polygon.Knoten(i),
                    zelle.Polygon.Knoten(i + 1),
                    zelle.Polygon.Linie(i));
                if (!index.TryGetValue(kante.Schluessel, out var liste))
                    index[kante.Schluessel] = liste = new List<int>();
                liste.Add(zelle.Id);
            }

        var uebel = bau.Zellen
            .Where(z => Math.Abs(Entlang(Geometrie.Mittelwert(z.Polygon)) - 59.18) < 0.6
                        && Math.Abs(Tiefe(Geometrie.Mittelwert(z.Polygon)) - 9.70) < 0.6)
            .ToList();
        foreach (var zelle in uebel)
        {
            Console.WriteLine($"    Zelle {zelle.Id}, {zelle.Art}, "
                + $"{Flaeche(zelle.Polygon):F2} m2:");
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var von = zelle.Polygon.Knoten(i).Punkt;
                var bis = zelle.Polygon.Knoten(i + 1).Punkt;
                var kante = new GerichteteKante(
                    zelle.Polygon.Knoten(i), zelle.Polygon.Knoten(i + 1),
                    zelle.Polygon.Linie(i));
                var teiler = index[kante.Schluessel];
                var nachbarn = teiler.Where(id => id != zelle.Id).ToList();
                var art = nachbarn.Count == 1
                    ? bau.Zellen[nachbarn[0]].Art.ToString() : "-";
                Console.WriteLine($"      Kante {Geometrie.Laenge(bis - von),6:F3} m  "
                    + $"entlang {Entlang(von),7:F2} -> {Entlang(bis),7:F2}  "
                    + $"tiefe {Tiefe(von),6:F2} -> {Tiefe(bis),6:F2}  "
                    + $"geteilt von {teiler.Count}  Nachbar {art}"
                    + (teiler.Count == 1 ? "   <-- ALLEIN, kein Partner" : ""));
            }
        }
        var lochbericht = bau.Lochtrennung;
        return auffaelligeFlaechen.Count == 0
            && lochbericht.LoecherVorher == 0
            && lochbericht.Trennnaehte.Count == 0
                ? 0 : 1;
    }

    /**
     * DIE FERTIGEN FLAECHEN DIESES FALLS ALS JSON.
     *
     * Fuer die Sichtpruefung durch den Nutzer: jede Flaeche mit ihrem
     * Aussenring, ihren Loechern, ihrem Material und den Rollen ihrer Zellen.
     * Zusammenhaengend heisst hier: EINE Flaeche, ein Eintrag - genau das
     * soll das Bild farblich zeigen.
     */
    private static Bauergebnis BaueBefund()
    {
        var einstellungen = new Zelleneinstellungen
        {
            Randabstand = 1.0,
            Fahrgassenbreite = 7.0,
            Querstrassenbreite = 3.0,
            Buchttiefe = 5.9,
            Buchtbreite = 3.0,
            Gruenstreifenbreite = 2.5,
            Querstrassenabstand = 33.0,
            Querstrassenkappen = true,
            Reihenwinkel = null,
        };
        return Layoutbauer.Baue(
            new Formdefinition("Befund1", BefundPolygon.ToList()),
            einstellungen,
            new List<Zufahrtsvorgabe>
                { new Zufahrtsvorgabe(BefundZufahrtKante, BefundZufahrtAlong) });
    }

    private static int RunBefundDaten(string ziel)
    {
        var bau = BaueBefund();
        // Als Konstante, nicht als Escape: doppelte Backslashes ueberleben
        // den Weg durch die Werkzeugkette nicht zuverlaessig.
        var Zeilenende = "\n";
        var sb = new System.Text.StringBuilder();
        var k = System.Globalization.CultureInfo.InvariantCulture;
        string Zahl(double w) => w.ToString("0.####", k);

        sb.Append("{" + Zeilenende + "  \"areal\": [");
        var areal = Enumerable.Range(0, bau.ArealLokal.Anzahl)
            .Select(i => bau.ArealLokal.Knoten(i).Punkt).ToList();
        sb.Append(string.Join(", ", areal.Select(q =>
            "[" + Zahl(q.X) + "," + Zahl(q.Y) + "]")));
        sb.Append("]," + Zeilenende + "  \"flaechen\": [" + Zeilenende);

        var erste = true;
        foreach (var f in bau.Flaechen)
        {
            if (!erste) sb.Append("," + Zeilenende);
            erste = false;
            var aussen = f.Aussenring.Knoten.Select(x => x.Punkt).ToList();
            var rollen = f.ZellIds
                .Select(id => bau.Zellen[id].Art.ToString())
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key + ":" + g.Count());
            sb.Append("    {\"id\": " + f.Id);
            sb.Append(", \"material\": \"" + f.Material + "\"");
            sb.Append(", \"flaeche\": " + Zahl(Math.Abs(f.Flaecheninhalt)));
            sb.Append(", \"zellen\": " + f.ZellIds.Count);
            sb.Append(", \"rollen\": \"" + string.Join(" ", rollen) + "\"");
            sb.Append(", \"aussen\": [" + string.Join(", ", aussen.Select(q =>
                "[" + Zahl(q.X) + "," + Zahl(q.Y) + "]")) + "]");
            sb.Append(", \"loecher\": [");
            sb.Append(string.Join(", ", f.Loecher.Select(loch =>
                "[" + string.Join(", ", loch.Knoten.Select(x =>
                    "[" + Zahl(x.Punkt.X) + "," + Zahl(x.Punkt.Y) + "]")) + "]")));
            sb.Append("]}");
        }
        sb.Append(Zeilenende + "  ]" + Zeilenende + "}" + Zeilenende);
        System.IO.File.WriteAllText(ziel, sb.ToString());
        Console.WriteLine($"{bau.Flaechen.Count} Flaechen nach {ziel} geschrieben.");
        return 0;
    }

    private static Punkt Mittelwert(IReadOnlyList<Punkt> ring)
    {
        double x = 0, y = 0;
        foreach (var q in ring) { x += q.X; y += q.Y; }
        return new Punkt(x / ring.Count, y / ring.Count);
    }

    private static List<double> Kantenlaengen(IReadOnlyList<Punkt> ring)
    {
        var aus = new List<double>();
        for (var i = 0; i < ring.Count; i++)
            aus.Add(Geometrie.Laenge(ring[(i + 1) % ring.Count] - ring[i]));
        return aus;
    }
}
