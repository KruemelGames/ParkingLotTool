using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private sealed class PolygonErgebnis
    {
        internal string Name;
        internal ParkingLayout Layout;
        internal double BauzeitMs;
        internal Coverage Ungedeckt;
        internal Anschlusspruefung Anschluesse;
        internal int Buchtueberlappungen;
        internal int BuchtenAufFahrbahn;
        internal int BuchteckenAusserhalb;
        internal Exception Fehler;
    }

    private sealed class Anschlusspruefung
    {
        internal Anschlussgruppe Zufahrten;
        internal Anschlussgruppe Fahrgassenenden;
        internal Anschlussgruppe Querstrassenenden;

        internal IEnumerable<Anschlussgruppe> Gruppen
        {
            get
            {
                yield return Zufahrten;
                yield return Fahrgassenenden;
                yield return Querstrassenenden;
            }
        }

        internal bool Vollstaendig => Gruppen.All(gruppe => gruppe.Vollstaendig);
    }

    private sealed class Anschlussgruppe
    {
        internal string Name;
        internal int Gesamt;
        internal List<Anschlussabweichung> Abweichungen =
            new List<Anschlussabweichung>();
        internal int Exakt => Gesamt - Abweichungen.Count;
        internal bool Vollstaendig => Abweichungen.Count == 0;
    }

    private sealed class Anschlussabweichung
    {
        internal float2 Punkt;
        internal double Linienabstand;
        internal double Knotenabstand;
        internal string Grund;
    }

    /**
     * Baut exakt dieselbe Eingabe nacheinander mit beiden Rechenwegen.
     * Die Bauzeit endet vor dem 0,1-m-Raster, damit die Messung nur den
     * Generator und nicht das bewusst feine Messgeraet enthaelt.
     */
    internal static int RunPolygonVergleich(string beschreibung)
    {
        var punkte = PolygonPunkte(beschreibung);
        ParkingGeometry.PhaseLog = false;
        WaermePolygonVergleich();
        var ergebnisse = new[]
        {
            BauePolygon("alt", punkte, false),
            BauePolygon("zellen", punkte, true),
        };

        Console.WriteLine($"POLYGONVERGLEICH | {punkte.Length} Ecken | "
            + $"{FlaecheVon(punkte):F0} m2 | Zufahrt Kante {ZufahrtKante} "
            + $"bei {ZufahrtLaenge:F3} m | ungedeckt im 0,1-m-Raster");
        Console.WriteLine("  Bauzeit: Median aus drei Neubauten; bei Fehler der Abbruchlauf");
        Console.WriteLine(
            "  Rechenweg   Buchten  davon Rand  Fahrgassen  Randlinie   ungedeckt             Bauzeit");
        foreach (var ergebnis in ergebnisse)
        {
            if (ergebnis.Fehler != null)
            {
                Console.WriteLine($"  {ergebnis.Name,-10}  FEHLER: "
                    + $"{Fehlerkette(ergebnis.Fehler)} ({ergebnis.BauzeitMs:F1} ms)");
                continue;
            }
            Console.WriteLine($"  {ergebnis.Name,-10}  {ergebnis.Layout.Stalls,7}  "
                + $"{ergebnis.Layout.PerimeterStalls,10}  "
                + $"{ergebnis.Layout.Aisles,11}  "
                + $"{ergebnis.Layout.PerimeterLine.Length,10}   "
                + $"{ergebnis.Ungedeckt.Gap,8:F1} m2 "
                + $"({ergebnis.Ungedeckt.Percent,5:F2} %)   {ergebnis.BauzeitMs,9:F1} ms");
        }

        Console.WriteLine("  Anschluesse: gesamt / mit exaktem gemeinsamem float2-Netzknoten");
        foreach (var ergebnis in ergebnisse.Where(e => e.Fehler == null))
        {
            Console.WriteLine($"  {ergebnis.Name,-10}  "
                + string.Join(" | ", ergebnis.Anschluesse.Gruppen.Select(
                    gruppe => $"{gruppe.Name} {gruppe.Gesamt}/{gruppe.Exakt}")));
            foreach (var gruppe in ergebnis.Anschluesse.Gruppen)
                foreach (var abweichung in gruppe.Abweichungen)
                    Console.WriteLine($"    ABWEICHUNG {gruppe.Name}: "
                        + $"{Punkttext(abweichung.Punkt)}, {abweichung.Grund}; "
                        + $"Abstand Linie {abweichung.Linienabstand:R} m, "
                        + $"Knoten {abweichung.Knotenabstand:R} m");
            Console.WriteLine($"    Buchtenpruefung: Ueberlappungen "
                + $"{ergebnis.Buchtueberlappungen}, auf Fahrbahn "
                + $"{ergebnis.BuchtenAufFahrbahn}, Ecken ausserhalb "
                + $"{ergebnis.BuchteckenAusserhalb}");
        }
        return ergebnisse.Any(e => e.Fehler != null
            || e.Anschluesse == null || !e.Anschluesse.Vollstaendig
            || (e.Name == "zellen" && (e.Buchtueberlappungen != 0
                || e.BuchtenAufFahrbahn != 0
                || e.BuchteckenAusserhalb != 0))) ? 1 : 0;
    }

    private static string Fehlerkette(Exception fehler)
    {
        var meldungen = new List<string>();
        for (var aktuell = fehler; aktuell != null; aktuell = aktuell.InnerException)
            meldungen.Add(aktuell.Message);
        return string.Join(" -> ", meldungen);
    }

    /** Ein gemeinsamer kleiner Vorlauf nimmt den einmaligen JIT-Anteil aus beiden Zeiten. */
    private static void WaermePolygonVergleich()
    {
        var probe = new[]
        {
            new float2(0, 0), new float2(80, 0),
            new float2(80, 70), new float2(0, 70),
        };
        foreach (var zellen in new[] { false, true })
        {
            var einstellungen = LayoutSettings.Cs2;
            einstellungen.AngleMode = "edge";
            einstellungen.Auto = false;
            einstellungen.Zellen = zellen;
            einstellungen.Entrances = new[]
                { new Entrance { Edge = 0, Along = 20 } };
            ParkingGeometry.Build(probe, einstellungen);
        }
    }

    private static PolygonErgebnis BauePolygon(string name, float2[] punkte,
                                                bool zellen)
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Zellen = zellen;
        einstellungen.Entrances = new[]
            { new Entrance { Edge = ZufahrtKante, Along = ZufahrtLaenge } };
        var ergebnis = new PolygonErgebnis { Name = name };
        var zeiten = new List<double>();
        for (var lauf = 0; lauf < 3; lauf++)
        {
            var uhr = Stopwatch.StartNew();
            try
            {
                ergebnis.Layout = ParkingGeometry.Build(punkte, einstellungen);
            }
            catch (Exception exception)
            {
                ergebnis.Fehler = exception;
            }
            finally
            {
                uhr.Stop();
                zeiten.Add(uhr.Elapsed.TotalMilliseconds);
            }
            // Eine Ausnahme ist selbst das Messergebnis. Sie noch zweimal zu
            // wiederholen wuerde beim 7-s-Budget nur 14 s ohne neue Aussage
            // anhaengen.
            if (ergebnis.Fehler != null) break;
        }
        zeiten.Sort();
        ergebnis.BauzeitMs = zeiten[zeiten.Count / 2];
        if (ergebnis.Layout != null)
        {
            ergebnis.Anschluesse = PruefeAnschluesse(ergebnis.Layout);
            ergebnis.Buchtueberlappungen = OverlapCount(ergebnis.Layout.Bay);
            ergebnis.BuchtenAufFahrbahn = BayOnRoad(
                ergebnis.Layout, einstellungen);
            ergebnis.BuchteckenAusserhalb = ergebnis.Layout.Bay
                .SelectMany(bucht => bucht)
                .Count(punkt => !PointIn(punkt, punkte)
                    && DistanceToBoundary(punkt, punkte) > 1e-6);
            ergebnis.Ungedeckt = CoverGap(punkte, ergebnis.Layout,
                                          einstellungen, 0.1);
        }
        return ergebnis;
    }

    private static Anschlusspruefung PruefeAnschluesse(ParkingLayout layout)
    {
        var randsegmente = layout.NetLine
            .Where(segment => segment.Kind == "perimeter")
            .ToArray();
        var randknoten = new HashSet<(float X, float Y)>();
        foreach (var segment in randsegmente)
        {
            randknoten.Add(Schluessel(segment.A));
            randknoten.Add(Schluessel(segment.B));
        }
        // Vor dem stueckweisen Wegfall liefen alle Querstrassen von Rand zu
        // Rand; deshalb pruefte das alte Messgeraet nur Randknoten. Ein korrekt
        // abgeschnittenes Stueck darf seit der Nutzerregel vom 2026-08-21 auch
        // an einer Fahrgasse enden. Exakt derselbe float2-Knoten bleibt Pflicht.
        var befahrbareKnoten = new HashSet<(float X, float Y)>(randknoten);
        foreach (var segment in layout.NetLine.Where(
                     segment => segment.Kind == "aisle"))
        {
            befahrbareKnoten.Add(Schluessel(segment.A));
            befahrbareKnoten.Add(Schluessel(segment.B));
        }

        var zufahrtspunkte = new List<float2>();
        foreach (var linie in layout.EntranceLine)
        {
            if (linie == null || linie.Length < 2) continue;
            zufahrtspunkte.Add(AbstandZuRandlinie(linie[0], layout.PerimeterLine)
                <= AbstandZuRandlinie(linie[linie.Length - 1], layout.PerimeterLine)
                    ? linie[0]
                    : linie[linie.Length - 1]);
        }
        return new Anschlusspruefung
        {
            Zufahrten = PruefeAnschlussgruppe(
                "Zufahrten", "entrance", zufahrtspunkte, layout,
                randknoten, layout.PerimeterLine, "Randstrassenknoten"),
            Fahrgassenenden = PruefeAnschlussgruppe(
                "Fahrgassenenden", "aisle",
                Linienenden(layout.AisleLine), layout,
                randknoten, layout.PerimeterLine, "Randstrassenknoten"),
            Querstrassenenden = PruefeAnschlussgruppe(
                "Querstrassenenden", "cross",
                Linienenden(layout.CrossRouteLine), layout,
                befahrbareKnoten,
                layout.PerimeterLine.Concat(layout.AisleLine).ToArray(),
                "befahrbaren Knoten"),
        };
    }

    private static Anschlussgruppe PruefeAnschlussgruppe(
        string name,
        string kind,
        IReadOnlyList<float2> erwarteteEnden,
        ParkingLayout layout,
        HashSet<(float X, float Y)> anschlussknoten,
        float2[][] anschlusslinien,
        string anschlussname)
    {
        var grad = new Dictionary<(float X, float Y), int>();
        foreach (var segment in layout.NetLine.Where(segment => segment.Kind == kind))
        {
            ErhoeheGrad(grad, segment.A);
            ErhoeheGrad(grad, segment.B);
        }
        var freieNetzenden = grad
            .Where(paar => paar.Value == 1)
            .Select(paar => new float2(paar.Key.X, paar.Key.Y))
            .ToList();
        var gruppe = new Anschlussgruppe { Name = name, Gesamt = erwarteteEnden.Count };
        foreach (var erwartet in erwarteteEnden)
        {
            var besterIndex = -1;
            var besterAbstand = double.PositiveInfinity;
            for (var i = 0; i < freieNetzenden.Count; i++)
            {
                var abstand = Netzabstand(erwartet, freieNetzenden[i]);
                if (abstand >= besterAbstand) continue;
                besterAbstand = abstand;
                besterIndex = i;
            }
            if (besterIndex < 0)
            {
                gruppe.Abweichungen.Add(new Anschlussabweichung
                {
                    Punkt = erwartet,
                    Linienabstand = AbstandZuRandlinie(
                        erwartet, anschlusslinien),
                    Knotenabstand = AbstandZuRandknoten(
                        erwartet, anschlussknoten),
                    Grund = "kein freies Netzende dieser Strassenart",
                });
                continue;
            }

            var netzende = freieNetzenden[besterIndex];
            freieNetzenden.RemoveAt(besterIndex);
            if (anschlussknoten.Contains(Schluessel(netzende))) continue;
            gruppe.Abweichungen.Add(new Anschlussabweichung
            {
                Punkt = netzende,
                Linienabstand = AbstandZuRandlinie(
                    netzende, anschlusslinien),
                Knotenabstand = AbstandZuRandknoten(
                    netzende, anschlussknoten),
                Grund = $"kein identischer {anschlussname}",
            });
        }
        return gruppe;
    }

    private static List<float2> Linienenden(IEnumerable<float2[]> linien)
    {
        var ausgabe = new List<float2>();
        foreach (var linie in linien)
        {
            if (linie == null || linie.Length < 2) continue;
            ausgabe.Add(linie[0]);
            ausgabe.Add(linie[linie.Length - 1]);
        }
        return ausgabe;
    }

    private static void ErhoeheGrad(
        Dictionary<(float X, float Y), int> grad,
        float2 punkt)
    {
        var schluessel = Schluessel(punkt);
        grad.TryGetValue(schluessel, out var bisher);
        grad[schluessel] = bisher + 1;
    }

    private static (float X, float Y) Schluessel(float2 punkt) =>
        (punkt.x, punkt.y);

    private static double Netzabstand(float2 a, float2 b)
    {
        var dx = (double)a.x - b.x;
        var dy = (double)a.y - b.y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double AbstandZuRandlinie(float2 punkt, float2[][] linien)
    {
        var minimum = double.PositiveInfinity;
        foreach (var linie in linien)
        {
            if (linie == null || linie.Length < 2) continue;
            minimum = Math.Min(minimum, AbstandPunktStrecke(
                punkt, linie[0], linie[linie.Length - 1]));
        }
        return minimum;
    }

    private static double AbstandZuRandknoten(
        float2 punkt,
        IEnumerable<(float X, float Y)> randknoten)
    {
        var minimum = double.PositiveInfinity;
        foreach (var randknotenpunkt in randknoten)
            minimum = Math.Min(minimum, Netzabstand(
                punkt, new float2(randknotenpunkt.X, randknotenpunkt.Y)));
        return minimum;
    }

    private static double AbstandPunktStrecke(float2 punkt, float2 a, float2 b)
    {
        var abX = (double)b.x - a.x;
        var abY = (double)b.y - a.y;
        var quadrat = abX * abX + abY * abY;
        if (quadrat == 0) return Netzabstand(punkt, a);
        var t = (((double)punkt.x - a.x) * abX
            + ((double)punkt.y - a.y) * abY) / quadrat;
        t = Math.Max(0, Math.Min(1, t));
        var q = new float2(
            (float)(a.x + abX * t),
            (float)(a.y + abY * t));
        return Netzabstand(punkt, q);
    }

    private static string Punkttext(float2 punkt) => string.Format(
        CultureInfo.InvariantCulture, "({0:R}, {1:R})", punkt.x, punkt.y);

    private static float2[] PolygonPunkte(string beschreibung)
    {
        return beschreibung.Split(';')
            .Select(t => t.Split(','))
            .Select(t => new float2(
                float.Parse(t[0], CultureInfo.InvariantCulture),
                float.Parse(t[1], CultureInfo.InvariantCulture)))
            .ToArray();
    }
}
