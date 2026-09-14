using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Messhilfe fuer ANTWORT-REGLER.md.
     *
     * Sie veraendert keinen Produktionscode. Fuer jeden Panelwert entstehen
     * zwei getrennte Layouts; derselbe Fall laeuft im Zellen- und im alten Weg.
     * Die drei Setzschalter sitzen absichtlich hinter der Geometrie. Dort gibt
     * die Hilfe deshalb sowohl das unveraenderte Layout als auch die Zahl der
     * nach derselben Bool-Entscheidung tatsaechlich zu setzenden Ringe/Objekte
     * aus.
     */
    private sealed class ReglerResult
    {
        internal ParkingLayout Layout;
        internal LayoutSettings Settings;
        internal Exception Error;
    }

    private static readonly float2[] ReglerRechteck =
    {
        new float2(0, 0), new float2(175, 0),
        new float2(175, 105), new float2(0, 105),
    };

    private static readonly float2[] ReglerSchraeg =
    {
        new float2(0, 0), new float2(150, 0),
        new float2(180, 90), new float2(30, 90),
    };

    private static readonly float2[] ReglerBreit =
    {
        new float2(0, 0), new float2(300, 0),
        new float2(300, 105), new float2(0, 105),
    };

    private static int RunRegler()
    {
        Console.WriteLine("REGLERPRUEFUNG");
        Console.WriteLine("  Standardform: Rechteck 175 x 105 m; Winkel/Zufahrt-Ecke: "
            + "Parallelogramm (0,0;150,0;180,90;30,90)");
        Console.WriteLine("  B=Buchten, G=Fahrgassen, Q=logische Querstrassenlinien, "
            + "Gr/As=Gras-/Asphaltringe und -flaeche, GF/QF=Fahrgassen-/"
            + "Querstrassenflaeche, BR=kleinster Buchtabstand zum Rand, "
            + "M=Mittelgruenringe/-flaeche, K/KQ=Kappen/Querstrassenkappen, "
            + "BM=Buchtmass, GA=kleinster Fahrgassenabstand, "
            + "QR=kleinster Querstrassenabstand.");

        Paar("Randabstand", "1,0 m", "4,0 m", ReglerRechteck,
            s => s.Es = 1, s => s.Es = 4);
        Paar("Fahrgassenbreite", "5,0 m", "9,0 m", ReglerRechteck,
            s => s.Ai = 5, s => s.Ai = 9);
        Paar("Querstrassenbreite", "3,0 m", "7,0 m", ReglerRechteck,
            s => s.Cw = 3, s => s.Cw = 7);
        Paar("Verbindung alle N Buchten", "N=3 (Cr=15 m)",
            "N=20 (Cr=66 m)", ReglerBreit,
            s => s.Cr = (3 + 2) * s.Sw,
            s => s.Cr = (20 + 2) * s.Sw);
        VerbindungSweep();
        Paar("Mittelgruen-Schalter", "AUS (Md=0)", "AN (Md=2,5 m)",
            ReglerRechteck, s => s.Md = 0, s => s.Md = 2.5);
        Paar("Gruenstreifentiefe", "0,5 m", "6,0 m", ReglerRechteck,
            s => s.Md = 0.5, s => s.Md = 6);
        Paar("Kappen an Querstrassen", "AUS", "AN", ReglerRechteck,
            s => s.Qk = false, s => s.Qk = true);

        Paar("Reihenwinkel (Fest)", "0 Grad", "35 Grad", ReglerSchraeg,
            s => { s.AngleMode = "fixed"; s.Auto = false; s.Angle = 0; },
            s => { s.AngleMode = "fixed"; s.Auto = false; s.Angle = 35; });

        DreiWinkelmodi();

        // Kein Panelregler mehr: direkte Kerninjektion belegt, dass die Felder
        // selbst weiter verdrahtet sind, waehrend CurrentSettings sie fest aus
        // LayoutSettings.Cs2 uebernimmt.
        Paar("Buchtbreite (kein Panelregler)", "2,5 m", "3,0 m",
            ReglerRechteck, s => s.Sw = 2.5, s => s.Sw = 3);
        Paar("Buchttiefe (kein Panelregler)", "5,0 m", "5,9 m",
            ReglerRechteck, s => s.Sl = 5, s => s.Sl = 5.9);

        Paar("Zufahrten", "1 Zufahrt", "2 Zufahrten", ReglerRechteck,
            s => s.Entrances = new[]
                { new Entrance { Edge = 0, Along = 30 } },
            s => s.Entrances = new[]
            {
                new Entrance { Edge = 0, Along = 30 },
                new Entrance { Edge = 0, Along = 115 },
            });
        var eckSettings = ReglerSettings(false);
        if (!ParkingGeometry.TryEntranceCornerPlacement(ReglerSchraeg,
                eckSettings, 0, true, out var ecklage))
            throw new InvalidOperationException(
                "Die Messform besitzt keinen verwendbaren Zufahrt-Eckenfang.");
        Paar("Zufahrt-Eckenfang", "ohne Corner", "Corner=start",
            ReglerSchraeg,
            s => s.Entrances = new[]
                { new Entrance { Edge = 0, Along = 30 } },
            s => s.Entrances = new[]
                { new Entrance { Edge = 0, Along = ecklage.Along,
                    Corner = "start" } });

        Flaechenauswahl();
        Flaechenschalter();
        Buchtsymbole();
        Rechenweg();
        return 0;
    }

    private static LayoutSettings ReglerSettings(bool zellen)
    {
        var settings = LayoutSettings.Cs2;
        settings.Zellen = zellen;
        settings.AngleMode = "edge";
        settings.Auto = false;
        settings.AutomaticEntrances = false;
        settings.Entrances = new[] { new Entrance { Edge = 0, Along = 30 } };
        return settings;
    }

    private static ReglerResult BaueRegler(float2[] site, bool zellen,
        Action<LayoutSettings> aenderung)
    {
        var settings = ReglerSettings(zellen);
        aenderung?.Invoke(settings);
        try
        {
            return new ReglerResult
            {
                Settings = settings,
                Layout = ParkingGeometry.Build(site, settings),
            };
        }
        catch (Exception error)
        {
            return new ReglerResult { Settings = settings, Error = error };
        }
    }

    private static void Paar(string name, string wertA, string wertB,
        float2[] site, Action<LayoutSettings> a, Action<LayoutSettings> b)
    {
        Console.WriteLine($"\n{name}: A={wertA}; B={wertB}");
        foreach (var zellen in new[] { true, false })
        {
            var ergebnisA = BaueRegler(site, zellen, a);
            var ergebnisB = BaueRegler(site, zellen, b);
            Console.WriteLine($"  {(zellen ? "NEU" : "ALT")} A | "
                + ReglerZeile(ergebnisA, site));
            Console.WriteLine($"  {(zellen ? "NEU" : "ALT")} B | "
                + ReglerZeile(ergebnisB, site));
            if (ergebnisA.Layout != null && ergebnisB.Layout != null)
                Console.WriteLine($"  {(zellen ? "NEU" : "ALT")} A=B Geometrie: "
                    + (ReglerGeometrieGleich(ergebnisA.Layout, ergebnisB.Layout)
                        ? "JA" : "NEIN"));
        }
    }

    private static string ReglerZeile(ReglerResult result, float2[] site)
    {
        if (result.Error != null)
            return "FEHLER: " + result.Error.GetType().Name + ": "
                + result.Error.Message;
        var layout = result.Layout;
        var grassArea = ReglerRingsArea(layout.GrassSurface);
        var asphaltArea = ReglerRingsArea(layout.AsphaltSurface);
        var aisleArea = ReglerRingsArea(layout.AisleQuad);
        var crossArea = ReglerRingsArea(layout.CrossQuad);
        var entranceArea = ReglerRingsArea(layout.EntranceQuad);
        var medianArea = ReglerRingsArea(layout.Median);
        var crossCaps = layout.CapKind.Count(kind => kind == "cross");
        var aisleSpacing = ParkingGeometry.CrossRouteSpacing(layout.AisleLine);
        var bayDistance = layout.Bay.Length == 0
            ? double.NaN
            : layout.Bay.SelectMany(bay => bay)
                .Min(point => DistanceToBoundary(point, site));
        var bayMeasure = ReglerBayMeasure(layout);
        var spacing = ParkingGeometry.CrossRouteSpacing(layout.CrossRouteLine);
        return $"B {layout.Stalls}; G {layout.Aisles}; Q "
            + $"{layout.CrossRouteLine.Length}; Gr {layout.GrassSurface.Length}/"
            + $"{ReglerZahl(grassArea)} m2; As {layout.AsphaltSurface.Length}/"
            + $"{ReglerZahl(asphaltArea)} m2; GF {ReglerZahl(aisleArea)} m2; "
            + $"GA {ReglerZahl(aisleSpacing)} m; "
            + $"QF {ReglerZahl(crossArea)} m2; "
            + $"ZF {ReglerZahl(entranceArea)} m2; "
            + $"M {layout.Median.Length}/{ReglerZahl(medianArea)} m2; "
            + $"K {layout.Cap.Length}; KQ {crossCaps}; "
            + $"BR {ReglerZahl(bayDistance)} m; BM {bayMeasure}; "
            + $"QR {ReglerZahl(spacing)} m; Winkel {ReglerZahl(layout.Angle)}; "
            + $"Zuf {layout.EntranceLine.Length}; Warn {layout.Warnings.Length}"
            + (layout.Warnings.Length == 0 ? string.Empty
                : " [" + string.Join(" | ", layout.Warnings) + "]");
    }

    private static double ReglerRingsArea(IEnumerable<float2[]> rings)
        => rings?.Sum(ring => Math.Abs(SignedArea(ring))) ?? 0;

    private static string ReglerBayMeasure(ParkingLayout layout)
    {
        var index = Enumerable.Range(0, layout.Bay.Length).FirstOrDefault(i =>
            i >= layout.BayRole.Length || layout.BayRole[i] == BayRole.Normal);
        if (layout.Bay.Length == 0 || layout.Bay[index] == null
            || layout.Bay[index].Length < 4) return "-";
        var edges = Enumerable.Range(0, 4).Select(i =>
        {
            var edge = layout.Bay[index][(i + 1) % 4] - layout.Bay[index][i];
            return Math.Sqrt((double)edge.x * edge.x + (double)edge.y * edge.y);
        }).OrderBy(value => value).ToArray();
        return ReglerZahl(edges[0]) + "x" + ReglerZahl(edges[3]) + " m";
    }

    private static string ReglerZahl(double value)
    {
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        if (double.IsNaN(value)) return "nan";
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool ReglerGeometrieGleich(ParkingLayout a, ParkingLayout b)
    {
        bool Polygone(float2[][] x, float2[][] y) => x.Length == y.Length
            && x.Zip(y, (p, q) => p.Length == q.Length
                && p.Zip(q, (u, v) => math.all(u == v)).All(g => g)).All(g => g);
        return Polygone(a.Bay, b.Bay)
            && a.BayKind.SequenceEqual(b.BayKind)
            && a.BayRole.SequenceEqual(b.BayRole)
            && a.ElectricPair.SequenceEqual(b.ElectricPair)
            && Polygone(a.Cap, b.Cap)
            && a.CapKind.SequenceEqual(b.CapKind)
            && Polygone(a.Median, b.Median)
            && Polygone(a.Green, b.Green)
            && Polygone(a.Fill, b.Fill)
            && Polygone(a.FillHole, b.FillHole)
            && Polygone(a.CrossPavement, b.CrossPavement)
            && Polygone(a.GrassSurface, b.GrassSurface)
            && Polygone(a.AsphaltSurface, b.AsphaltSurface)
            && Polygone(a.PerimeterQuad, b.PerimeterQuad)
            && Polygone(a.EntranceQuad, b.EntranceQuad)
            && Polygone(a.AisleLine, b.AisleLine)
            && Polygone(a.AisleQuad, b.AisleQuad)
            && Polygone(a.CrossLine, b.CrossLine)
            && Polygone(a.CrossRouteLine, b.CrossRouteLine)
            && Polygone(a.CrossQuad, b.CrossQuad)
            && Polygone(a.PerimeterLine, b.PerimeterLine)
            && Polygone(a.EntranceLine, b.EntranceLine)
            && a.NetLine.SequenceEqual(b.NetLine)
            && a.Ring.SequenceEqual(b.Ring)
            && a.Stalls == b.Stalls
            && a.PerimeterStalls == b.PerimeterStalls
            && a.InnerStalls == b.InnerStalls
            && a.Angle == b.Angle
            && a.Aisles == b.Aisles;
    }

    private static void VerbindungSweep()
    {
        Console.WriteLine("  Sweep ueber gueltige Panelwerte auf 300 x 105 m:");
        foreach (var n in new[] { 3, 4, 5, 9, 20, 30 })
        {
            var neu = BaueRegler(ReglerBreit, true,
                s => s.Cr = (n + 2) * s.Sw);
            var alt = BaueRegler(ReglerBreit, false,
                s => s.Cr = (n + 2) * s.Sw);
            string Kurz(ReglerResult r) => r.Layout == null
                ? "FEHLER " + r.Error?.Message
                : $"B {r.Layout.Stalls}, Q {r.Layout.CrossRouteLine.Length}, "
                    + $"QR {ReglerZahl(ParkingGeometry.CrossRouteSpacing(
                        r.Layout.CrossRouteLine))} m";
            Console.WriteLine($"    N={n,2}: NEU {Kurz(neu)} | ALT {Kurz(alt)}");
        }
    }

    private static void DreiWinkelmodi()
    {
        Console.WriteLine("\nWinkelmodus: A=Kante; B=Fest 25 Grad; C=Meiste "
            + "(gespeicherter Reihenwinkel ebenfalls 25 Grad)");
        foreach (var zellen in new[] { true, false })
        {
            var edge = BaueRegler(ReglerSchraeg, zellen,
                s => { s.AngleMode = "edge"; s.Auto = false; s.Angle = 25; });
            var fixedAngle = BaueRegler(ReglerSchraeg, zellen,
                s => { s.AngleMode = "fixed"; s.Auto = false; s.Angle = 25; });
            var automatic = BaueRegler(ReglerSchraeg, zellen,
                s => { s.AngleMode = "auto"; s.Auto = true; s.Angle = 25; });
            var weg = zellen ? "NEU" : "ALT";
            Console.WriteLine($"  {weg} A | " + ReglerZeile(edge, ReglerSchraeg));
            Console.WriteLine($"  {weg} B | " + ReglerZeile(fixedAngle, ReglerSchraeg));
            Console.WriteLine($"  {weg} C | " + ReglerZeile(automatic, ReglerSchraeg));
            if (fixedAngle.Layout != null && automatic.Layout != null)
                Console.WriteLine($"  {weg} B=C Geometrie: "
                    + (ReglerGeometrieGleich(fixedAngle.Layout, automatic.Layout)
                        ? "JA" : "NEIN"));
        }
    }

    private static void Flaechenauswahl()
    {
        Console.WriteLine("\nFahrflaechen-Auswahl: A=Road Pavement, Deko Grass; "
            + "B=Road Grass, Deko Grass");
        foreach (var zellen in new[] { true, false })
        {
            var verschieden = BaueRegler(ReglerRechteck, zellen,
                s => s.EineFlaeche = false);
            var gleich = BaueRegler(ReglerRechteck, zellen,
                s => s.EineFlaeche = true);
            var weg = zellen ? "NEU" : "ALT";
            Console.WriteLine($"  {weg} A | Road='Pavement Surface 01'; "
                + "Decoration='Grass Surface 01'; "
                + ReglerZeile(verschieden, ReglerRechteck));
            Console.WriteLine($"  {weg} B | Road='Grass Surface 01'; "
                + "Decoration='Grass Surface 01'; "
                + ReglerZeile(gleich, ReglerRechteck));
        }

        Console.WriteLine("\nZwischenflaechen-Auswahl: A=Road Pavement, Deko Grass; "
            + "B=Road Pavement, Deko Pavement");
        foreach (var zellen in new[] { true, false })
        {
            var verschieden = BaueRegler(ReglerRechteck, zellen,
                s => s.EineFlaeche = false);
            var gleich = BaueRegler(ReglerRechteck, zellen,
                s => s.EineFlaeche = true);
            var weg = zellen ? "NEU" : "ALT";
            Console.WriteLine($"  {weg} A | Road='Pavement Surface 01'; "
                + "Decoration='Grass Surface 01'; "
                + ReglerZeile(verschieden, ReglerRechteck));
            Console.WriteLine($"  {weg} B | Road='Pavement Surface 01'; "
                + "Decoration='Pavement Surface 01'; "
                + ReglerZeile(gleich, ReglerRechteck));
        }
    }

    private static void Flaechenschalter()
    {
        Console.WriteLine("\nFlaechen-Setzschalter: Geometrie bleibt fertig; "
            + "gesetzt wird je aktivem Schalter");
        foreach (var zellen in new[] { true, false })
        foreach (var gleicheNamen in new[] { false, true })
        {
            var result = BaueRegler(ReglerRechteck, zellen,
                s => s.EineFlaeche = gleicheNamen);
            if (result.Layout == null)
            {
                Console.WriteLine("  FEHLER: " + result.Error?.Message);
                continue;
            }
            Console.WriteLine("  " + (zellen ? "NEU" : "ALT") + ", Namen "
                + (gleicheNamen ? "GLEICH" : "VERSCHIEDEN")
                + " | geplant Gr " + result.Layout.GrassSurface.Length + "/"
                + ReglerZahl(ReglerRingsArea(result.Layout.GrassSurface))
                + " m2; As " + result.Layout.AsphaltSurface.Length + "/"
                + ReglerZahl(ReglerRingsArea(result.Layout.AsphaltSurface)) + " m2");
            foreach (var schalter in new[]
            {
                (Road: true, Decoration: true, Name: "beide AN"),
                (Road: false, Decoration: true, Name: "Fahrflaeche AUS"),
                (Road: true, Decoration: false, Name: "Zwischenflaeche AUS"),
                (Road: false, Decoration: false, Name: "beide AUS"),
            })
            {
                // Dieselbe Auswahl wie der Setz-Ort messen. Bei einem
                // zusammengefassten Layout sind die sichtbaren Listen allein
                // gerade nicht mehr aussagekraeftig.
                result.Layout.SurfacesForPlacement(
                    schalter.Road, schalter.Decoration,
                    out var grass, out var asphalt);
                var grassRings = grass.Length;
                var asphaltRings = asphalt.Length;
                var grassArea = ReglerRingsArea(grass);
                var asphaltArea = ReglerRingsArea(asphalt);
                Console.WriteLine($"    {schalter.Name,-22} -> gesetzt Gr "
                    + $"{grassRings}/{ReglerZahl(grassArea)} m2; As "
                    + $"{asphaltRings}/{ReglerZahl(asphaltArea)} m2; "
                    + $"B {result.Layout.Stalls}; G {result.Layout.Aisles}; "
                    + $"Q {result.Layout.CrossRouteLine.Length}");
            }
        }
    }

    private static void Buchtsymbole()
    {
        Console.WriteLine("\nBuchtmarkierung (unsichtbares Prefab ist bereit): "
            + "A=AN; B=AUS");
        foreach (var zellen in new[] { true, false })
        {
            var result = BaueRegler(ReglerRechteck, zellen, null);
            if (result.Layout == null)
            {
                Console.WriteLine("  FEHLER: " + result.Error?.Message);
                continue;
            }
            var plan = ParkingBayDecals.Plan(result.Layout, result.Settings);
            var weg = zellen ? "NEU" : "ALT";
            Console.WriteLine($"  {weg} A | sichtbar {plan.Placements.Length}; "
                + $"unsichtbar 0; Ladesaeulen {plan.Chargers.Length}; "
                + $"B {result.Layout.Stalls}; G {result.Layout.Aisles}; "
                + $"Q {result.Layout.CrossRouteLine.Length}");
            Console.WriteLine($"  {weg} B | sichtbar 0; unsichtbar "
                + $"{plan.Placements.Length}; Ladesaeulen 0; "
                + $"B {result.Layout.Stalls}; G {result.Layout.Aisles}; "
                + $"Q {result.Layout.CrossRouteLine.Length}");
        }
    }

    private static void Rechenweg()
    {
        Console.WriteLine("\nRechenweg: A=Neu; B=Alt");
        var neu = BaueRegler(ReglerRechteck, true, null);
        var alt = BaueRegler(ReglerRechteck, false, null);
        Console.WriteLine("  A | " + ReglerZeile(neu, ReglerRechteck));
        Console.WriteLine("  B | " + ReglerZeile(alt, ReglerRechteck));
    }
}
