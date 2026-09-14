using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * DIE FORM AUS DEM BAUPROTOKOLL - NICHT AUS EINER NACHFRAGE.
     *
     * Der Nutzer zeichnet frei und kann dieselbe Form NICHT ein zweites Mal
     * ziehen. Er hat mir das mehrfach gesagt, und ich habe ihn trotzdem
     * wieder danach gefragt. Das ist der Grund, warum es diese Datei gibt:
     * jeder gebaute Parkplatz steht mit seinem Umriss im Bauprotokoll
     *
     *     %LOCALLOW%/Colossal Order/Cities Skylines II/Logs/
     *         ParkingLotTool-bauten.jsonl
     *
     * Von dort holt sich dieser Lauf die Form. Ab jetzt gilt: wer eine
     * gemeldete Form nachrechnen will, nimmt sie hier heraus und fragt
     * niemanden.
     */
    private sealed class Protokolleintrag
    {
        internal string Kennung;
        internal string Zeit;
        internal float2[] Polygon;
        internal string Einstellungen;
        internal string Ergebnis;
    }

    /** Abstand eines Punktes zu einer Strecke. */
    private static double AbstandZurStrecke(float2 p, float2 a, float2 b)
    {
        var ab = new double2(b.x - a.x, b.y - a.y);
        var ap = new double2(p.x - a.x, p.y - a.y);
        var laenge = ab.x * ab.x + ab.y * ab.y;
        var t = laenge <= 1e-12 ? 0 : Math.Max(0, Math.Min(1,
            (ap.x * ab.x + ap.y * ab.y) / laenge));
        var lot = new double2(a.x + ab.x * t, a.y + ab.y * t);
        return Math.Sqrt((p.x - lot.x) * (p.x - lot.x)
            + (p.y - lot.y) * (p.y - lot.y));
    }

    private static string ProtokollPfad()
    {
        var vorgegeben = Environment.GetEnvironmentVariable(
            "PLT_BAUTEN_PROTOKOLL");
        return string.IsNullOrWhiteSpace(vorgegeben)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
                "Logs", "ParkingLotTool-bauten.jsonl")
            : vorgegeben;
    }

    /**
     * Liest das Protokoll - mit einem winzigen eigenen Leser.
     *
     * Eine JSON-Bibliothek waere hier eine Abhaengigkeit fuer drei Felder.
     * Gebraucht werden Kennung, Zeit, Polygon, Einstellungen und Ergebnis;
     * alle stehen als flache Zeichenketten oder als Zahlenpaare da.
     */
    private static List<Protokolleintrag> LiesProtokoll(string pfad)
    {
        var ausgabe = new List<Protokolleintrag>();
        if (!File.Exists(pfad)) return ausgabe;
        foreach (var zeile in File.ReadLines(pfad))
        {
            if (string.IsNullOrWhiteSpace(zeile)) continue;
            var eintrag = new Protokolleintrag
            {
                Kennung = Feld(zeile, "kennung"),
                Zeit = Feld(zeile, "zeit"),
                Einstellungen = Feld(zeile, "einstellungen"),
                Ergebnis = Feld(zeile, "ergebnis"),
                Polygon = ProtokollPunkte(zeile),
            };
            if (eintrag.Polygon != null && eintrag.Polygon.Length >= 3)
                ausgabe.Add(eintrag);
        }
        return ausgabe;
    }

    private static string Feld(string zeile, string name)
    {
        var marke = "\"" + name + "\":\"";
        var start = zeile.IndexOf(marke, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += marke.Length;
        var ende = zeile.IndexOf('"', start);
        return ende < 0 ? string.Empty : zeile.Substring(start, ende - start);
    }

    private static float2[] ProtokollPunkte(string zeile)
    {
        var marke = "\"polygon\":[";
        var start = zeile.IndexOf(marke, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marke.Length;
        var ende = zeile.IndexOf("]]", start, StringComparison.Ordinal);
        if (ende < 0) return null;
        var roh = zeile.Substring(start, ende - start + 1);
        var punkte = new List<float2>();
        foreach (var stueck in roh.Split(new[] { "],[" }, StringSplitOptions.None))
        {
            var sauber = stueck.Trim('[', ']');
            var teile = sauber.Split(',');
            if (teile.Length != 2) continue;
            if (!double.TryParse(teile[0], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var x)) continue;
            if (!double.TryParse(teile[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var y)) continue;
            punkte.Add(new float2((float)x, (float)y));
        }
        return punkte.ToArray();
    }

    /**
     * Was kostet die Ausrichtung an DIESER Form?
     *
     * Rechnet den letzten gebauten Parkplatz einmal ohne Ausrichtung und
     * einmal mit jedem moeglichen Schnitt bei zwei Winkeln. Die Differenz
     * ist der Preis der Trennung - die Frage, die ich dem Nutzer nicht mehr
     * stellen muss.
     */
    private static void RunProtokoll(string kennung, double winkelA,
        double winkelB)
    {
        var pfad = ProtokollPfad();
        var eintraege = LiesProtokoll(pfad);
        if (eintraege.Count == 0)
        {
            Console.WriteLine("Kein Bauprotokoll unter " + pfad);
            return;
        }
        var eintrag = string.IsNullOrEmpty(kennung)
            ? eintraege[eintraege.Count - 1]
            : eintraege.LastOrDefault(e => e.Kennung == kennung);
        if (eintrag == null)
        {
            Console.WriteLine("Keine Kennung " + kennung + " im Protokoll.");
            return;
        }

        Console.WriteLine(eintrag.Kennung + "  " + eintrag.Zeit + "  "
            + eintrag.Polygon.Length + " Ecken");
        Console.WriteLine("  " + eintrag.Einstellungen);
        Console.WriteLine("  im Spiel: " + eintrag.Ergebnis);
        Console.WriteLine();

        var site = eintrag.Polygon;
        var doppel = site.Select(p => new double2(p.x, p.y)).ToArray();

        LayoutSettings Grund()
        {
            var s = LayoutSettings.Cs2;
            s.Zellen = true;
            s.AngleMode = "edge";
            s.Auto = false;
            s.AutomaticEntrances = false;
            s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
            return s;
        }

        Teilflaechenschnitt aktuellerSchnitt = null;
        // Form UND Winkel je Teilflaeche - beides braucht die Mittigkeit.
        var aktuelleTeile = new List<(float2[] Form, double Winkel)>();
        void Messe(string name, LayoutSettings settings)
        {
            try
            {
                var layout = ParkingGeometry.Build(site, settings);
                /*
                 * ASPHALT, DER NICHTS TRAEGT.
                 *
                 * Der Nutzer zeigt auf grosse gepflasterte Felder, auf denen
                 * nichts steht. Das ist keine Frage der Buchtenzahl, sondern
                 * der Richtigkeit: der Belag folgt den Rasterbaendern, nicht
                 * den tatsaechlich gesetzten Buchten. Gemessen wird deshalb
                 * die Asphaltflaeche abzueglich dessen, was darauf steht -
                 * Buchten, Fahrgassen, Querstrassen, Randstrasse, Zufahrten.
                 */
                double Summe(float2[][] ringe)
                {
                    var g = 0.0;
                    foreach (var r in ringe ?? Array.Empty<float2[]>())
                        if (r != null && r.Length >= 3) g += Math.Abs(Flaeche(
                            r.Select(p => new double2(p.x, p.y)).ToArray()));
                    return g;
                }
                var belag = Summe(layout.AsphaltSurface);
                var buchten = Summe(layout.Bay);
                var gassen = Summe(layout.AisleQuad);
                var quer = Summe(layout.CrossQuad);
                var rand = Summe(layout.PerimeterQuad);
                var zufahrt = Summe(layout.EntranceQuad);
                var genutzt = buchten + gassen + quer + rand + zufahrt;
                var leer = Math.Max(0, belag - genutzt);
                /*
                 * GRAS UNTER EINER BUCHT.
                 *
                 * Der Nutzer hat ein Decal gefunden, das halb auf Asphalt und
                 * halb auf Gras liegt. Das heisst: die Bucht steht da, aber
                 * die Flaeche unter ihr sagt Gras. Bucht und Belag
                 * widersprechen sich - und genau das kann meine neue Regel
                 * ausgeloest haben, die Fahrgassenzellen gruen faerbt.
                 */
                ZeigeMittigkeit(name, aktuelleTeile, layout);
                var grasAufBucht = GrassOnBayArea(layout);
                /*
                 * IMMER AUSGEBEN. Gerade der Sollwert 0,00 ist hier der
                 * Beleg; eine nur bei Fehlern sichtbare Zeile machte Vorher-
                 * /Nachher-Messungen unnoetig mehrdeutig.
                 */
                var betroffen = 0;
                var naechster = double.MaxValue;
                for (var b = 0; b < layout.Bay.Length; b++)
                {
                    var einzeln = GrassOnBayArea(new ParkingLayout
                    {
                        Bay = new[] { layout.Bay[b] },
                        GrassSurface = layout.GrassSurface,
                    });
                    if (einzeln <= 0.01) continue;
                    betroffen++;
                    if (aktuellerSchnitt == null) continue;
                    var mitte = float2.zero;
                    foreach (var pt in layout.Bay[b]) mitte += pt;
                    mitte /= layout.Bay[b].Length;
                    var d = AbstandZurStrecke(mitte,
                        aktuellerSchnitt.A, aktuellerSchnitt.B);
                    if (d < naechster) naechster = d;
                }
                Console.WriteLine($"{name,-34} Gras unter Buchten "
                    + $"{grasAufBucht,6:F2} m2 | betroffene Buchten "
                    + $"{betroffen} | naechste zum Schnitt "
                    + (naechster == double.MaxValue ? "-"
                        : naechster.ToString("F1")) + " m");
                var grasAufGasse = GrasUnterGassen(layout);
                var enden = OffeneGassenenden(layout);
                var haar = Haarkanten(layout);
                Console.WriteLine($"{string.Empty,-34} Gras unter Gassen "
                    + $"{grasAufGasse,6:F0} m2 | offene Gassenenden "
                    + $"{enden.Offen}/{enden.Gesamt} | groesste Luecke "
                    + $"{enden.Schlimmster,5:F1} m | CS2 verwirft "
                    + $"{haar.Verdaechtig} Ring(e) (Ear-Clipping), kuerzeste Kante "
                    + $"{haar.KuerzesteKante:F3} m");
                if (haar.Verdaechtig > 0 && aktuellerSchnitt != null)
                    ZeigeVerworfeneRinge(layout,
                        aktuellerSchnitt.A, aktuellerSchnitt.B);
                Console.WriteLine($"{string.Empty,-34} davon Buchten {buchten,6:F0}"
                    + $" | Gassen {gassen,6:F0} | Quer {quer,5:F0}"
                    + $" | Rand {rand,6:F0} | Zufahrt {zufahrt,5:F0}");
                Console.WriteLine($"{name,-34} {layout.Stalls,4} Buchten "
                    + $"(Rand {layout.PerimeterStalls,3}, "
                    + $"innen {layout.InnerStalls,3}) | "
                    + $"Gassen {layout.Aisles,2} | "
                    + $"unerreichbar {UnservedBays(layout),3} | "
                    + $"Asphalt {belag,7:F0} m2, davon ungenutzt "
                    + $"{leer,6:F0} m2 = {(belag <= 0 ? 0 : leer / belag * 100),4:F0} %");
            }
            catch (Exception fehler)
            {
                Console.WriteLine($"{name,-34} ABSTURZ: {fehler.Message}");
            }
        }

        aktuelleTeile.Add((site, 0));
        Messe("ohne Ausrichtung", Grund());
        aktuelleTeile.Clear();
        aktuelleTeile.Add((site, winkelA));
        var einer = Grund();
        einer.Ausrichtwinkel = winkelA;
        Messe($"ein Winkel {winkelA:F1}", einer);
        Console.WriteLine();

        for (var i = 0; i < site.Length; i++)
            for (var j = i + 2; j < site.Length; j++)
            {
                if (i == 0 && j == site.Length - 1) continue;
                if (!ParkingGeometry.SchnittLiegtInnen(doppel, i, j)) continue;
                var schnitt = new Teilflaechenschnitt { A = site[i], B = site[j] };
                var teile = ParkingGeometry.TeilflaechenAusSchnitten(
                    doppel, new[] { schnitt });
                if (teile.Length != 2) continue;
                float2 Anker(double2[] teil)
                {
                    var summe = double2.zero;
                    foreach (var p in teil) summe += p;
                    var m = summe / teil.Length;
                    return new float2((float)m.x, (float)m.y);
                }
                var s = Grund();
                s.Ausrichtwinkel = winkelA;
                s.Teilflaechenschnitte = new[] { schnitt };
                s.TeilflaechenAusrichtungen = new[]
                {
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[0]), Winkel = winkelB },
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[1]), Winkel = winkelA },
                };
                float2[] AlsFloat(double2[] teil) => teil
                    .Select(punkt => new float2((float)punkt.x, (float)punkt.y))
                    .ToArray();
                aktuelleTeile.Clear();
                aktuelleTeile.Add((AlsFloat(teile[0]), winkelB));
                aktuelleTeile.Add((AlsFloat(teile[1]), winkelA));
                aktuellerSchnitt = schnitt;
                Messe($"Schnitt {i}-{j} · {winkelB:F1}/{winkelA:F1}", s);
                aktuellerSchnitt = null;
            }
    }
}
