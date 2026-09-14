using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;

internal static partial class Program
{
    /**
     * WARUM WIRD EINE QUERSTRASSE IM EINEN BAND GEBAUT UND IM NAECHSTEN NICHT?
     *
     * Nutzerbefund am 2026-08-26, sechs Markierungen in
     * `ParkingLotTool-report-20260826-154813.txt`:
     *
     *   *"Markierung 1 zeigt eine Querstrasse, die richtig nicht erweitert auf
     *   Markierung 2, laut Regel um Konfliktpunkte zu vermeiden. Das ist
     *   richtig. Markierung 3 bis 4 und 5 bis 6: da waere eine Erweiterung
     *   bzw. normale Verlegung der Querstrasse moeglich, wird aber durch die
     *   zu strenge Regel verhindert."*
     *
     * Die Markierungen liegen paarweise auf drei Hoehen (y ~ 761, 725, 689)
     * und auf zwei senkrechten Linien (x ~ -1412 und x ~ -1381). Genau so
     * sieht es aus, wenn DIESELBE Querstrasse in einem Modul bleibt und im
     * naechsten wegfaellt - `Bleibt` wird je Modul entschieden.
     *
     * Polygon und Einstellungen sind aus dem Bericht abgeschrieben, nicht
     * nachempfunden. Der Bericht nennt die Zufahrt nur der Zahl nach ("1
     * entrance"), nicht ihre Kante; deshalb laeuft der Fall hier zusaetzlich
     * OHNE Zufahrt, damit man sieht, ob sie ueberhaupt etwas daran aendert.
     */
    private static readonly Punkt[] QuerfallPolygon =
    {
        new Punkt(-1265.96167, 753.8883),
        new Punkt(-1371.76624, 752.661133),
        new Punkt(-1370.484, 642.03),
        new Punkt(-1456.336, 641.035034),
        new Punkt(-1457.618, 751.666),
        new Punkt(-1459.937, 951.653),
        new Punkt(-1268.27881, 953.87616),
    };

    private static int RunQuerfall()
    {
        Console.WriteLine("QUERSTRASSEN-FALL vom 2026-08-26 (L-Form, 1048 Buchten)");
        Console.WriteLine("  Sechs Markierungen auf drei Hoehen, zwei senkrechte Linien.");
        Console.WriteLine("  Die Regel: eine Querstrasse bleibt nur, wenn ALLE VIER");
        Console.WriteLine("  Nachbarzahlen >= 5 sind (Minimum der vier).");
        Console.WriteLine();

        foreach (var mitZufahrt in new[] { true, false })
        {
            var e = new Zelleneinstellungen
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

            var zufahrten = mitZufahrt
                ? new List<Zufahrtsvorgabe> { new Zufahrtsvorgabe(0, 40) }
                : new List<Zufahrtsvorgabe>();

            Bauergebnis bau;
            try
            {
                bau = Layoutbauer.Baue(
                    new Formdefinition("Querfall", QuerfallPolygon.ToList()),
                    e, zufahrten);
            }
            catch (Exception ausnahme)
            {
                Console.WriteLine($"  {(mitZufahrt ? "MIT" : "OHNE")} Zufahrt: "
                    + $"ABBRUCH {ausnahme.Message}");
                continue;
            }

            var pruefungen = bau.Querstrassenpruefungen
                ?? new List<Querstrassenpruefung>();
            var buchten = bau.Zellen.Where(z => z.BuchtId.HasValue)
                .Select(z => z.BuchtId.Value).Distinct().Count();

            Console.WriteLine($"  === {(mitZufahrt ? "MIT" : "OHNE")} Zufahrt: "
                + $"{buchten} Buchten, {pruefungen.Count} Pruefungen, "
                + $"{pruefungen.Count(p => p.Bleibt)} geblieben ===");
            Console.WriteLine();

            /*
             * DIE MODULE ZUERST. Wenn eine Querstrasse an "erste 0/0"
             * scheitert, liegt es nicht an der Schwelle 5, sondern daran, dass
             * die erste Reihe dieses Moduls dort ueberhaupt keine Bucht traegt.
             * Ohne die Ausdehnungen sieht man das nicht.
             */
            foreach (var m in bau.Modulspaltenplaene ?? new List<Modulspaltenplan>())
            {
                var erste = m.Modul.ErsteReihe;
                var zweite = m.Modul.ZweiteReihe;
                Console.WriteLine($"    Modul {m.Modul.Id,3}  "
                    + $"quer {m.Modul.Anfang,9:F2} .. {m.Modul.Ende,9:F2}  "
                    + $"laengs {m.MinX,9:F2} .. {m.MaxX,9:F2}  "
                    + $"({m.MaxX - m.MinX,7:F2} m)  "
                    + $"erste {erste.Anfang,8:F2}..{erste.Ende,8:F2}  "
                    + $"zweite {zweite.Anfang,8:F2}..{zweite.Ende,8:F2}");
            }
            Console.WriteLine();

            /*
             * NACH QUERSTRASSE GRUPPIEREN, nicht nach Modul. Genau so hat der
             * Nutzer es gesehen: eine senkrechte Linie, die mal da ist und mal
             * nicht. Wer nach Modul sortiert, sieht das Muster nicht.
             */
            foreach (var gruppe in pruefungen.GroupBy(p => p.QuerstrassenId)
                                             .OrderBy(g => g.Key))
            {
                var bleibt = gruppe.Count(p => p.Bleibt);
                var gesamt = gruppe.Count();
                if (bleibt == gesamt) continue;      // durchgehend, unauffaellig

                Console.WriteLine($"    Querstrasse {gruppe.Key,2}: "
                    + $"{bleibt} von {gesamt} Modulen behalten "
                    + (bleibt == 0 ? "(ueberall weg)" : "(UNTERBROCHEN)"));
                foreach (var p in gruppe.OrderBy(p => p.ModulId))
                {
                    var knapp = p.Minimum < 5 && p.Minimum >= 3;
                    Console.WriteLine($"        Modul {p.ModulId,3}  "
                        + $"erste {p.ErsteLinks,3}/{p.ErsteRechts,-3} "
                        + $"zweite {p.ZweiteLinks,3}/{p.ZweiteRechts,-3}  "
                        + $"Minimum {p.Minimum,3}  "
                        + (p.Bleibt ? "bleibt" : "WEG")
                        + (knapp && !p.Bleibt ? "   <- knapp, 3 oder 4" : ""));
                }
                Console.WriteLine();
            }

            /*
             * DIE GEBAUTEN STUECKE. Erst hier wirkt die Eckenregel: sie
             * entscheidet nicht, ob eine Querstrasse bleibt, sondern ob ihr
             * Anschluss AN DIE RANDSTRASSE gebaut wird.
             */
            var stuecke = bau.Querstrassenstuecke
                ?? new List<Querstrassenstueck>();
            Console.WriteLine($"    Gebaute Querstrassenstuecke: {stuecke.Count}");
            foreach (var gruppe in stuecke.GroupBy(s => s.Querstrasse.Id)
                                          .OrderBy(g => g.Key))
            {
                Console.Write($"      Querstrasse {gruppe.Key,2} (Mitte "
                    + $"{gruppe.First().Querstrasse.Mitte,8:F2}):");
                foreach (var s in gruppe.OrderBy(s => s.Anfang.Y))
                    Console.Write($"  [{s.Anfang.Y,8:F2} .. {s.Ende.Y,8:F2}]");
                Console.WriteLine();
            }
            Console.WriteLine();

            /*
             * ERKENNT DIE ECKENREGEL DIE ECKEN UEBERHAUPT?
             *
             * Dieselbe Rechnung wie in `Planung.AbstandZurNaechstenEcke`, nur
             * hier sichtbar: wie viele Punkte der Randstrassenmittellinie
             * gelten als Ecke, und wie weit ist jeder Anschluss von der
             * naechsten entfernt. Wer das nicht ausgibt, sucht den Fehler
             * spaeter im falschen Teil.
             */
            var ring = bau.Randstrassenmittellinie;
            if (ring != null && ring.Count >= 3)
            {
                var ecken = new List<Punkt>();
                for (var i = 0; i < ring.Count; i++)
                {
                    var vor = ring[(i - 1 + ring.Count) % ring.Count];
                    var hier = ring[i];
                    var nach = ring[(i + 1) % ring.Count];
                    var eX = hier.X - vor.X; var eY = hier.Y - vor.Y;
                    var aX = nach.X - hier.X; var aY = nach.Y - hier.Y;
                    var lE = Math.Sqrt(eX * eX + eY * eY);
                    var lA = Math.Sqrt(aX * aX + aY * aY);
                    if (lE <= 1e-9 || lA <= 1e-9) continue;
                    var cos = Math.Max(-1.0, Math.Min(1.0,
                        (eX * aX + eY * aY) / (lE * lA)));
                    if (Math.Acos(cos) * 180.0 / Math.PI >= 20.0)
                        ecken.Add(hier);
                }
                Console.WriteLine($"    Randstrassenmittellinie: {ring.Count} "
                    + $"Punkte, davon {ecken.Count} als Ecke erkannt");
                foreach (var ecke in ecken)
                    Console.WriteLine($"        Ecke  X {ecke.X,9:F2}  Y {ecke.Y,9:F2}");
                foreach (var s in stuecke.OrderBy(s => s.Querstrasse.Id))
                {
                    double Naechste(Punkt p) => ecken.Count == 0
                        ? double.PositiveInfinity
                        : ecken.Min(ecke => Math.Sqrt(
                              (p.X - ecke.X) * (p.X - ecke.X)
                              + (p.Y - ecke.Y) * (p.Y - ecke.Y)));
                    Console.WriteLine($"      Quer {s.Querstrasse.Id,2}  "
                        + $"Anfang zur Ecke {Naechste(s.Anfang),8:F2} m  "
                        + $"Ende zur Ecke {Naechste(s.Ende),8:F2} m");
                }
                Console.WriteLine();
            }

            /*
             * Wie oft scheitert es nur KNAPP? Das ist die Zahl, die entscheidet,
             * ob eine Lockerung ueberhaupt etwas braechte - oder ob die
             * verworfenen Querstrassen wirklich zu eng liegen.
             */
            var weg = pruefungen.Where(p => !p.Bleibt).ToList();
            Console.WriteLine($"    Verworfen gesamt: {weg.Count}");
            foreach (var schwelle in new[] { 1, 2, 3, 4 })
                Console.WriteLine($"      davon mit Minimum = {schwelle}: "
                    + weg.Count(p => p.Minimum == schwelle));
            Console.WriteLine($"      davon mit Minimum = 0: "
                + weg.Count(p => p.Minimum == 0));
            Console.WriteLine();
        }

        Console.WriteLine("  Lesehilfe: 'erste'/'zweite' sind die beiden Buchtreihen");
        Console.WriteLine("  des Moduls, die Zahlen links/rechts der Querstrasse.");
        return 0;
    }
}
