using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * `--teilen`: AUSRICHTEN MIT HANDSCHNITTEN - Formen x Schnitte x Winkel.
 *
 * Anlass (2026-10-08): der Nutzer teilte ein Rechteck zwischen zwei
 * Zusatzpunkten auf den langen Seiten und gab der zweiten Haelfte eine
 * senkrechte Linie - "Ein Materialrand ist offen", keine Vorschau mehr.
 * Ein Winkeldurchlauf zeigte 10 von 24 Winkelpaaren mit Absturz. Seine
 * Vorgabe dazu: es muss egal sein, ob die Schnittenden konkave, konvexe
 * oder gestreckte (180-Grad-)Ecken sind.
 *
 * Geprueft wird je Form, Schnitt, Randstrassenmodus und Winkelpaar:
 *   - der Bau wirft nicht;
 *   - jede Teilflaeche meldet den Winkel, den sie bekommen hat;
 *   - keine Bucht ueberlappt eine andere, liegt auf der Fahrbahn oder
 *     ragt aus dem Umriss;
 *   - gleiche Richtung (auch 0,0008 Grad daneben oder 180 Grad gedreht)
 *     baut genau wie ungeteilt;
 *   - eine Teilflaeche ab 2.000 m2 bekommt Buchten.
 */
internal static partial class Program
{
    private sealed class Teilfall
    {
        internal string Name;
        internal float2[] Umriss;
        internal (int A, int B)[] Schnitte;
    }

    /**
     * GEGENBEISPIEL VON CODEX (2026-10-08): ein konvexes 10x10-Quadrat mit
     * einer 1-mm-Abschraegung verwirft CS2 komplett, obwohl ein Kreis mit
     * 5 m Radius hineinpasst. Die Rettung darf daraus keinen Totalverlust
     * machen: nahezu die ganze Flaeche muss baubar herauskommen. Dazu der
     * Spielfall: zwei Rasenrechtecke an einer 18-mm-Verbindung.
     */
    private static int PruefeRettung()
    {
        var fehler = 0;
        void Fall(string name, float2[] ring, double mindestens)
        {
            var vorher = Cs2Triangulierung.Dreiecke(ring);
            var verloren = new List<float2[]>();
            var teile = ParkingGeometry.RetteFuerCs2(ring, verloren);
            var flaeche = teile.Sum(t => Math.Abs(Ringflaeche(t)));
            var alleBaubar = teile.All(t => Cs2Triangulierung.Dreiecke(t) != 0);
            var ok = vorher == 0 && alleBaubar && flaeche >= mindestens;
            Console.WriteLine($"Rettung {name}: vorher {(vorher == 0 ? "verworfen" : "angenommen")}, "
                + $"{teile.Count} Teile, {flaeche:F3} m2 baubar (Soll >= {mindestens}), "
                + $"{verloren.Count} verloren{(ok ? "" : " - FEHLER")}");
            if (!ok) fehler++;
        }
        var o = new float2(-600, -600);
        Fall("Quadrat mit 1-mm-Abschraegung", new[]
        {
            o + new float2(0, 0), o + new float2(10, 0), o + new float2(10, 9.999f),
            o + new float2(9.999f, 10), o + new float2(0, 10),
        }, 99.9);
        Fall("Spielfall 18-mm-Verbindung", new[]
        {
            new float2(-563.31165f, -621.49445f), new float2(-568.82715f, -619.39935f),
            new float2(-562.43536f, -602.57245f), new float2(-556.91986f, -604.66754f),
            new float2(-555.50867f, -600.95233f), new float2(-561.0411f, -598.8509f),
            new float2(-570.2555f, -623.10815f), new float2(-564.7229f, -625.2097f),
        }, 46.9);
        return fehler;
    }

    private static int RunTeilen()
    {
        var rettungsfehler = PruefeRettung();
        var faelle = new[]
        {
            new Teilfall
            {
                Name = "Nutzerrechteck, gestreckte Enden",
                Umriss = new[]
                {
                    new float2(-567.5618286132812f, -718.0654296875f),
                    new float2(-626.7417602539062f, -695.5858154296875f),
                    new float2(-681.9384765625f, -674.6193237304688f),
                    new float2(-644.566650390625f, -576.2373046875f),
                    new float2(-589.3700561523438f, -597.2040405273438f),
                    new float2(-530.1917724609375f, -619.6842041015625f),
                },
                Schnitte = new[] { (1, 4) },
            },
            new Teilfall
            {
                Name = "Rechteck achsparallel, gestreckte Enden",
                Umriss = new[]
                {
                    new float2(0, 0), new float2(60, 0), new float2(120, 0),
                    new float2(120, 90), new float2(60, 90), new float2(0, 90),
                },
                Schnitte = new[] { (1, 4) },
            },
            new Teilfall
            {
                Name = "Rechteck Diagonale, konvexe Enden",
                Umriss = new[]
                {
                    new float2(0, 0), new float2(140, 0),
                    new float2(140, 90), new float2(0, 90),
                },
                Schnitte = new[] { (0, 2) },
            },
            new Teilfall
            {
                Name = "U-Form, konvex nach konkav",
                Umriss = new[]
                {
                    new float2(0, 0), new float2(120, 0), new float2(120, 120),
                    new float2(80, 120), new float2(80, 40), new float2(40, 40),
                    new float2(40, 120), new float2(0, 120),
                },
                Schnitte = new[] { (0, 4) },
            },
            new Teilfall
            {
                Name = "L-Form, konkav nach gestreckt",
                Umriss = new[]
                {
                    new float2(0, 0), new float2(180, 0), new float2(180, 60),
                    new float2(90, 60), new float2(90, 120), new float2(0, 120),
                    new float2(0, 60),
                },
                Schnitte = new[] { (3, 6) },
            },
            new Teilfall
            {
                Name = "Sechseck, zwei Schnitte aus einer Ecke",
                Umriss = new[]
                {
                    new float2(0, 0), new float2(120, 0), new float2(170, 70),
                    new float2(120, 140), new float2(0, 140), new float2(-50, 70),
                },
                Schnitte = new[] { (0, 2), (0, 3) },
            },
        };
        var differenzen = new[]
        {
            0.0, 0.0008, 7.5, 15, 22.5, 30, 37.5, 45, 52.5, 60, 67.5, 75,
            82.5, 89.999, 90, 90.001, 97.5, 105, 120, 135, 150, 165, 172.5,
            179.9995, 180,
        };

        var fehler = 0;
        var laeufe = 0;
        foreach (var fall in faelle)
            foreach (var randstrassen in new[] { true, false })
            {
                var schnitte = fall.Schnitte.Select(s => new Teilflaechenschnitt
                {
                    A = fall.Umriss[s.A],
                    B = fall.Umriss[s.B],
                }).ToArray();
                var teile = ParkingGeometry.TeilflaechenAusSchnitten(
                    fall.Umriss.Select(p => new double2(p.x, p.y)).ToArray(),
                    schnitte);
                var anker = teile.Select(TeilAnker).ToArray();
                var kante = fall.Umriss[1] - fall.Umriss[0];
                var grundwinkel = math.degrees(math.atan2(kante.y, kante.x));

                LayoutSettings Grund()
                {
                    var s = LayoutSettings.Cs2;
                    s.Zellen = true;
                    s.AngleMode = "edge";
                    s.Auto = false;
                    s.AutomaticEntrances = false;
                    s.Entrances = Array.Empty<Entrance>();
                    s.Randstrassen = randstrassen;
                    return s;
                }

                var ungeteilt = Grund();
                ungeteilt.Ausrichtwinkel = grundwinkel;
                ParkingLayout basis;
                try { basis = ParkingGeometry.Build(fall.Umriss, ungeteilt); }
                catch (Exception e)
                {
                    Console.WriteLine($"  {fall.Name}, Rand {randstrassen}: ungeteilt FEHLER {e.Message}");
                    fehler++;
                    continue;
                }

                var basisStrasse = BayOnRoad(basis, ungeteilt);
                var zeilen = new List<string>();
                foreach (var differenz in differenzen)
                {
                    laeufe++;
                    var settings = Grund();
                    settings.Ausrichtwinkel = grundwinkel;
                    settings.Teilflaechenschnitte = schnitte;
                    // Das erste Teil behaelt die Grundrichtung, jedes weitere
                    // dreht um die Differenz (beim Sechseck das dritte um die
                    // doppelte), damit mehrere Naehte zusammentreffen.
                    settings.TeilflaechenAusrichtungen = anker.Select((a, i) =>
                        new TeilflaechenAusrichtung
                        {
                            Anker = a,
                            Winkel = (float)(grundwinkel + i * differenz),
                        }).ToArray();
                    var name = $"{fall.Name}, Rand {(randstrassen ? "an" : "aus")}, +{differenz}°";
                    ParkingLayout layout;
                    try { layout = ParkingGeometry.Build(fall.Umriss, settings); }
                    catch (Exception e)
                    {
                        zeilen.Add($"  FEHLER {name}: {e.Message}");
                        fehler++;
                        continue;
                    }

                    var probleme = new List<string>();
                    var ueber = OverlapCount(layout.Bay);
                    var strasse = BayOnRoad(layout, settings);
                    var aussen = EndsOutside(layout, fall.Umriss);
                    if (ueber != 0) probleme.Add($"{ueber} Ueberlappungen");
                    if (strasse > basisStrasse) probleme.Add($"{strasse} Buchten auf Fahrbahn (ungeteilt {basisStrasse})");
                    if (strasse > basisStrasse && Environment.GetEnvironmentVariable("PLT_STRASSE") == "1")
                    {
                        foreach (var s in schnitte)
                        {
                            var d = math.normalize(s.B - s.A);
                            float Abst(float2 p) => math.abs(d.x * (p.y - s.A.y) - d.y * (p.x - s.A.x));
                            foreach (var n in layout.NetLine.Where(n => Abst(n.A) < 4 && Abst(n.B) < 4 && math.distance(n.A, n.B) > 0.5f))
                                zeilen.Add($"    Naht-Netzlinie {n.Kind} {n.Art} ({n.A.x:F1}/{n.A.y:F1})->({n.B.x:F1}/{n.B.y:F1}) Abstand {Abst(n.A):F2}/{Abst(n.B):F2}");
                        }
                        var arten = new List<(string Art, float2[] Quad)>();
                        foreach (var q in layout.PerimeterQuad) arten.Add(("Randquad", q.ToArray()));
                        foreach (var q in layout.EntranceQuad) arten.Add(("Zufahrt", q.ToArray()));
                        foreach (var l in layout.PerimeterLine.Skip(layout.PerimeterQuad.Length)) arten.Add(("Randlinie", Corridor(l[0], l[1], settings.Ai)));
                        foreach (var l in layout.AisleLine) arten.Add(($"Gasse ({l[0].x:F1}/{l[0].y:F1})->({l[1].x:F1}/{l[1].y:F1})", Corridor(l[0], l[1], settings.Ai)));
                        foreach (var l in layout.CrossLine) arten.Add(($"Quer ({l[0].x:F1}/{l[0].y:F1})->({l[1].x:F1}/{l[1].y:F1})", Corridor(l[0], l[1], settings.Cw)));
                        foreach (var bucht in layout.Bay)
                            foreach (var (art, quad) in arten)
                                if (quad != null && QuadsOverlap(bucht, quad, 0.05))
                                    zeilen.Add($"    Bucht bei ({Buchtmitte(bucht).x:F1}/{Buchtmitte(bucht).y:F1}) auf {art}");
                    }
                    if (Environment.GetEnvironmentVariable("PLT_UARM") == "1" && fall.Name.StartsWith("U-Form"))
                        zeilen.Add($"  U-Arm +{differenz}: {layout.Bay.Count(b => { var m = Buchtmitte(b); return m.x > 80 && m.y > 40; })} Buchten im rechten Arm, gesamt {layout.Stalls}");
                    if (aussen != 0) probleme.Add($"{aussen} ragen hinaus");

                    var gleicheRichtung = differenz % 180 < 0.01
                        || 180 - differenz % 180 < 0.01;
                    if (gleicheRichtung && (layout.Stalls != basis.Stalls
                        || layout.NetLine.Length != basis.NetLine.Length))
                        probleme.Add($"gleiche Richtung, aber {layout.Stalls}/"
                            + $"{layout.NetLine.Length} statt {basis.Stalls}/"
                            + $"{basis.NetLine.Length} (Buchten/Netzlinien)");

                    for (var t = 0; t < teile.Length; t++)
                    {
                        var ring = teile[t].Select(p => new float2((float)p.x, (float)p.y)).ToArray();
                        var flaeche = Math.Abs(Ringflaeche(ring));
                        var drin = layout.Bay.Count(bucht => PointIn(Buchtmitte(bucht), ring));
                        if (flaeche >= 2000 && drin == 0)
                            probleme.Add($"Teil {t} ({flaeche:F0} m2) ohne Buchten");
                    }
                    var svgZiel2 = Environment.GetEnvironmentVariable("PLT_SVG");
                    if (svgZiel2 != null && name.StartsWith(Environment.GetEnvironmentVariable("PLT_SVG_FALL") ?? "~"))
                    {
                        // Noch einmal bauen und dabei mitschreiben, was der Kern
                        // weglaesst - die Ringe stehen nur im Live-Log.
                        var verlust = new List<float2[]>();
                        var vorher = ParkingGeometry.LiveSchreiber;
                        // Den Fall als Berichtsdatei: `--bericht` rechnet ihn danach allein nach.
                        var jsonZiel = Environment.GetEnvironmentVariable("PLT_SVG_JSON");
                        if (jsonZiel != null)
                        {
                            // Nur die Werte, die dieser Lauf setzt; den Rest nimmt
                            // die Vorlage (ein echter Bericht mit CS2-Standardmassen).
                            var inv = System.Globalization.CultureInfo.InvariantCulture;
                            string P(float2 p) => "[" + p.x.ToString("R", inv) + "," + p.y.ToString("R", inv) + "]";
                            System.IO.File.WriteAllText(jsonZiel, "{\"Umriss\":[" + string.Join(",", fall.Umriss.Select(P))
                                + "],\"Randstrassen\":" + (settings.Randstrassen ? "true" : "false")
                                + ",\"Ausrichtwinkel\":" + settings.Ausrichtwinkel.Value.ToString("R", inv)
                                + ",\"Schnitte\":[" + string.Join(",", settings.Teilflaechenschnitte.Select(t => "[" + P(t.A) + "," + P(t.B) + "]"))
                                + "],\"Ausrichtungen\":[" + string.Join(",", settings.TeilflaechenAusrichtungen.Select(t => "[" + P(t.Anker) + "," + t.Winkel.ToString("R", inv) + "]"))
                                + "]}");
                        }
                        var liveDatei = Environment.GetEnvironmentVariable("PLT_SVG_LIVE");
                        var liveZeilen = new List<string>();
                        ParkingGeometry.LiveSchreiber = zeile =>
                        {
                            liveZeilen.Add(zeile);
                            if (liveDatei != null) System.IO.File.WriteAllLines(liveDatei, liveZeilen);
                            if (!zeile.Contains("verworfener ring") && !zeile.Contains("haarriss ring")) return;
                            var punkte = zeile.Split('|')[3].Trim().Split(' ')
                                .Select(p => p.Split(','))
                                .Select(p => new float2(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                                    float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
                                .ToArray();
                            verlust.Add(punkte);
                        };
                        try { ParkingGeometry.Build(fall.Umriss, settings); }
                        finally { ParkingGeometry.LiveSchreiber = vorher; }
                        TeilenSvg(layout, fall.Umriss, svgZiel2, verlust);
                        if (verlust.Count > 0)
                        {
                            var groesster = verlust.OrderByDescending(r => Math.Abs(Ringflaeche(r))).First();
                            var mitte = groesster.Aggregate(float2.zero, (a, p) => a + p) / groesster.Length;
                            // Ein Ende des Streifens, 6 x 4 m - dort sieht man die Breite.
                            var ende = groesster.OrderBy(p => math.distance(p, mitte)).Last();
                            TeilenSvg(layout, fall.Umriss, svgZiel2.Replace(".svg", "-zoom.svg"), verlust,
                                (ende - new float2(3, 2), ende + new float2(3, 2)));
                        }
                    }
                    var (_, aufFalsch, aufGemischt) = Aufkleberbefund(layout, settings);
                    if (aufFalsch + aufGemischt != 0) probleme.Add($"Aufkleber: {aufFalsch} zur Seite ohne Fahrbahn, {aufGemischt} gegen den Reihennachbarn");
                    var (verworfen, fehlend) = TeilenVerworfen(layout, name);
                    if (verworfen != 0) probleme.Add($"{verworfen} Flaeche(n) verwirft CS2");
                    // Mehr als 1 m2 ist ein sichtbares Loch (wie in `--bericht`).
                    if (fehlend > 1.0) probleme.Add($"{fehlend:F2} m2 Flaeche fehlen");
                    var gemeldet = layout.Teilflaechen?.Length ?? 0;
                    if (gemeldet != teile.Length)
                        probleme.Add($"{gemeldet} Teilflaechen gemeldet statt {teile.Length}");

                    if (probleme.Count != 0)
                    {
                        zeilen.Add($"  FEHLER {name}: {string.Join("; ", probleme)}");
                        fehler++;
                    }
                }
                Console.WriteLine($"{fall.Name}, Rand {(randstrassen ? "an" : "aus")}: "
                    + $"{teile.Length} Teile, ungeteilt {basis.Stalls} Buchten");
                foreach (var zeile in zeilen) Console.WriteLine(zeile);
            }

        /*
         * AUSRICHTEN OHNE SCHNITT - das, was der Spieler am haeufigsten tut.
         * Jede Form in 7,5-Grad-Schritten ganz herum: kein Wurf, keine
         * Ueberlappung, keine Bucht auf der Fahrbahn, jede Bucht erreichbar.
         */
        foreach (var fall in faelle)
            foreach (var randstrassen in new[] { true, false })
            {
                var zeilen = new List<string>();
                var summe = 0;
                for (var winkel = 0.0; winkel < 180; winkel += 7.5)
                {
                    laeufe++;
                    var settings = LayoutSettings.Cs2;
                    settings.Zellen = true;
                    settings.AngleMode = "edge";
                    settings.Auto = false;
                    settings.AutomaticEntrances = false;
                    settings.Entrances = Array.Empty<Entrance>();
                    settings.Randstrassen = randstrassen;
                    settings.Ausrichtwinkel = winkel;
                    var name = $"{fall.Name} ohne Schnitt, Rand {(randstrassen ? "an" : "aus")}, {winkel}°";
                    try
                    {
                        var layout = ParkingGeometry.Build(fall.Umriss, settings);
                        summe += layout.Stalls;
                        var probleme = new List<string>();
                        var ueber = OverlapCount(layout.Bay);
                        var strasse = BayOnRoad(layout, settings);
                        var aussen = EndsOutside(layout, fall.Umriss);
                        var unerreichbar = UnservedBays(layout);
                        if (ueber != 0) probleme.Add($"{ueber} Ueberlappungen");
                        if (strasse != 0) probleme.Add($"{strasse} Buchten auf Fahrbahn");
                        if (aussen != 0) probleme.Add($"{aussen} ragen hinaus");
                        if (unerreichbar != 0) probleme.Add($"{unerreichbar} unerreichbar");
                        var svgZiel = Environment.GetEnvironmentVariable("PLT_SVG");
                        if (svgZiel != null && name.StartsWith(Environment.GetEnvironmentVariable("PLT_SVG_FALL") ?? "~"))
                            TeilenSvg(layout, fall.Umriss, svgZiel);
                        if (unerreichbar != 0 && Environment.GetEnvironmentVariable("PLT_ERREICH") == "1")
                        {
                            var alle = layout.PerimeterQuad.Concat(layout.AisleQuad).Concat(layout.CrossQuad).Concat(layout.EntranceQuad).ToArray();
                            var mitQuer = UnservedBays(new ParkingLayout { Bay = layout.Bay, PerimeterQuad = layout.PerimeterQuad.Concat(layout.CrossQuad).Concat(layout.EntranceQuad).ToArray(), AisleQuad = layout.AisleQuad });
                            var strassenQ = layout.PerimeterQuad.Concat(layout.AisleQuad).Select(q => q.ToArray()).ToArray();
                            float Spalt(float2 p) => strassenQ.Min(q => PointInRing(p, q) ? 0f : (float)DistanceToBoundary(p, q));
                            var einzelnL = new ParkingLayout { PerimeterQuad = layout.PerimeterQuad, AisleQuad = layout.AisleQuad };
                            var spalte = layout.Bay.Where(b => { einzelnL.Bay = new[] { b }; return UnservedBays(einzelnL) != 0; })
                                .Select(b => Math.Min(Spalt((b[0] + b[1]) / 2), Spalt((b[2] + b[3]) / 2))).OrderBy(x => x).ToArray();
                            probleme.Add($"[Spalt Vorderkante min {spalte.First():F4} / median {spalte[spalte.Length / 2]:F4} / max {spalte.Last():F4} m]");
                            probleme.Add($"[mit Quer/Zufahrt {mitQuer}; Gassenquads {layout.AisleQuad.Length}, Gassenlinien {layout.AisleLine.Length}, Randquads {layout.PerimeterQuad.Length}, Randlinien {layout.PerimeterLine.Length}, Querquads {layout.CrossQuad.Length}]");
                        }
                        if (layout.Stalls == 0) probleme.Add("keine Buchten");
                        var (_, aufFalsch, aufGemischt) = Aufkleberbefund(layout, settings);
                        if (aufFalsch + aufGemischt != 0) probleme.Add($"Aufkleber: {aufFalsch} zur Seite ohne Fahrbahn, {aufGemischt} gegen den Reihennachbarn");
                        var (verworfen, fehlend) = TeilenVerworfen(layout, name);
                        if (verworfen != 0) probleme.Add($"{verworfen} Flaeche(n) verwirft CS2");
                        if (fehlend > 1.0) probleme.Add($"{fehlend:F2} m2 Flaeche fehlen");
                        if (probleme.Count != 0)
                        {
                            zeilen.Add($"  FEHLER {name}: {string.Join("; ", probleme)}");
                            fehler++;
                        }
                    }
                    catch (Exception e)
                    {
                        zeilen.Add($"  FEHLER {name}: {e.Message}");
                        fehler++;
                    }
                }
                Console.WriteLine($"{fall.Name} ohne Schnitt, Rand {(randstrassen ? "an" : "aus")}: "
                    + $"24 Winkel, im Mittel {summe / 24} Buchten");
                foreach (var zeile in zeilen) Console.WriteLine(zeile);
            }

        fehler += rettungsfehler;
        Console.WriteLine($"Fehlende Flaeche ueber alle Laeufe: {_teilenVerlust:F2} m2");
        Console.WriteLine($"Teilen: {laeufe} Laeufe, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    private static void TeilenSvg(ParkingLayout layout, float2[] umriss, string pfad,
        List<float2[]> verlust = null, (float2 Min, float2 Max)? fenster = null)
    {
        var minX = umriss.Min(p => p.x) - 5; var maxX = umriss.Max(p => p.x) + 5;
        var minY = umriss.Min(p => p.y) - 5; var maxY = umriss.Max(p => p.y) + 5;
        var massstab = 6f;
        if (fenster.HasValue)
        {
            minX = fenster.Value.Min.x; maxX = fenster.Value.Max.x;
            minY = fenster.Value.Min.y; maxY = fenster.Value.Max.y;
            massstab = 900f / (maxX - minX);
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string Pts(float2[] r) => string.Join(" ", r.Select(p => ((p.x - minX) * massstab).ToString("F1", inv) + "," + ((maxY - p.y) * massstab).ToString("F1", inv)));
        var sb = new System.Text.StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{(maxX - minX) * massstab:F0}' height='{(maxY - minY) * massstab:F0}' style='background:#fff'>");
        sb.Append($"<polygon points='{Pts(umriss)}' fill='#c96' stroke='#000'/>");
        // Boden braun: was weder Gras noch Belag deckt, faellt sofort auf.
        foreach (var r in layout.AsphaltSurface ?? Array.Empty<float2[]>())
            sb.Append($"<polygon points='{Pts(r)}' fill='#888' stroke='#555' stroke-width='0.3'/>");
        foreach (var r in layout.GrassSurface ?? Array.Empty<float2[]>())
            sb.Append($"<polygon points='{Pts(r)}' fill='#9d8' stroke='#363' stroke-width='0.3'/>");
        foreach (var q in layout.PerimeterQuad) sb.Append($"<polygon points='{Pts(q.ToArray())}' fill='#99c' fill-opacity='0.6'/>");
        foreach (var q in layout.AisleQuad) sb.Append($"<polygon points='{Pts(q.ToArray())}' fill='#69f' fill-opacity='0.6'/>");
        foreach (var q in layout.CrossQuad) sb.Append($"<polygon points='{Pts(q.ToArray())}' fill='#f96' fill-opacity='0.6'/>");
        var einzeln = new ParkingLayout { PerimeterQuad = layout.PerimeterQuad, AisleQuad = layout.AisleQuad };
        foreach (var b in layout.Bay)
        {
            einzeln.Bay = new[] { b };
            var rot = UnservedBays(einzeln) != 0;
            sb.Append($"<polygon points='{Pts(b)}' fill='{(rot ? "#e33" : "#6c6")}' stroke='#333' stroke-width='0.5'/>");
        }
        foreach (var n in layout.NetLine) sb.Append($"<line x1='{(n.A.x - minX) * massstab:F1}' y1='{(maxY - n.A.y) * massstab:F1}' x2='{(n.B.x - minX) * massstab:F1}' y2='{(maxY - n.B.y) * massstab:F1}' stroke='#000' stroke-width='1.5'/>");
        foreach (var r in verlust ?? new List<float2[]>())
            sb.Append($"<polygon points='{Pts(r)}' fill='#e00' stroke='#e00' stroke-width='{(fenster.HasValue ? 1 : 3)}'/>");
        sb.Append("</svg>");
        System.IO.File.WriteAllText(pfad, sb.ToString().Replace(",0 ", ",0 "));
    }

    /** Fehlende Flaeche ueber alle Laeufe, fuer die Schlusszeile. */
    private static double _teilenVerlust;

    /**
     * WAS IM SPIEL FEHLEN WIRD, in Quadratmetern.
     *
     * Zwei Quellen: Ringe in der Ausgabe, die CS2 nach dem 0,1-m-Versatz
     * nicht triangulieren kann (die darf es seit der Ausgangsgarantie gar
     * nicht mehr geben - jeder zaehlt als Fehler), und Ringe, die der Kern
     * selbst weglaesst (Warnungen "left out", unbaubar und Haarrisse).
     * Mit PLT_RINGE=1 werden verworfene Ringe ausgegeben.
     */
    private static (int Verworfen, double Fehlend) TeilenVerworfen(ParkingLayout layout, string name)
    {
        var verworfen = 0;
        var fehlend = 0.0;
        foreach (var (art, ringe) in new[] { ("Gras", layout.GrassSurface), ("Belag", layout.AsphaltSurface) })
            foreach (var r in ringe ?? Array.Empty<float2[]>())
            {
                if (r == null || r.Length < 3 || Cs2Triangulierung.Dreiecke(r) != 0) continue;
                verworfen++;
                fehlend += Math.Abs(Ringflaeche(r));
                if (Environment.GetEnvironmentVariable("PLT_RINGE") == "1")
                    Console.WriteLine($"    RING {name} {art} {r.Length} Ecken: "
                        + string.Join(" ", r.Select(p => $"{p.x:R}/{p.y:R}")));
            }
        foreach (var w in layout.Warnings ?? Array.Empty<string>())
        {
            var m = System.Text.RegularExpressions.Regex.Match(w,
                @"[0-9]+ (surface|hairline) ring\(s\) with ([0-9]+[.,][0-9]+) m2 left out");
            if (m.Success)
                fehlend += double.Parse(m.Groups[2].Value.Replace(',', '.'),
                    System.Globalization.CultureInfo.InvariantCulture);
        }
        _teilenVerlust += fehlend;
        return (verworfen, fehlend);
    }

    /** Ein Punkt sicher im Teil: Schwerpunkt, sonst Mitte einer inneren Diagonale. */
    private static float2 TeilAnker(double2[] teil)
    {
        var ring = teil.Select(p => new float2((float)p.x, (float)p.y)).ToArray();
        var summe = float2.zero;
        foreach (var p in ring) summe += p;
        var mitte = summe / ring.Length;
        if (PointIn(mitte, ring)) return mitte;
        for (var i = 0; i < ring.Length; i++)
            for (var j = i + 2; j < ring.Length; j++)
            {
                var m = (ring[i] + ring[j]) / 2;
                if (PointIn(m, ring)) return m;
            }
        return mitte;
    }

    private static float2 Buchtmitte(float2[] quad)
    {
        var summe = float2.zero;
        foreach (var p in quad) summe += p;
        return summe / quad.Length;
    }

    private static double Ringflaeche(float2[] ring)
    {
        var summe = 0.0;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            summe += (double)a.x * b.y - (double)b.x * a.y;
        }
        return summe / 2;
    }
}
