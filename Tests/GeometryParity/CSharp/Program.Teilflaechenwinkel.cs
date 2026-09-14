using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * EINE TEILFLAECHE, EINE BEZUGSLINIE - der Alltagsfall des Nutzers.
     *
     * Am 2026-09-01 hat der Live-Log gezeigt: seine Parkplaetze haben fast
     * immer `teile 1`, und trotzdem lief der Bau ueber den Teilflaechen-
     * Rasterweg statt ueber das gewoehnliche Bandraster - bei gleichem
     * Ergebnis 618 statt 237 ms, an einer groesseren Form 3328 ms.
     *
     * Dieser Test haelt fest, WARUM man den Rasterweg dort weglassen darf:
     * liegt jede Teilflaeche im selben Winkel wie der globale Bezug, muss
     * dasselbe Layout herauskommen. Er gilt unabhaengig von der Abkuerzung -
     * vor dem Umbau nimmt die linke Seite den Rasterweg, danach nicht mehr,
     * und beide Male muss sie mit der rechten uebereinstimmen.
     */
    private static bool PruefeEinheitlicherWinkel()
    {
        var site = new[]
        {
            new float2(0, 0), new float2(160, 0),
            new float2(160, 90), new float2(0, 90),
        };
        var teile = ParkingGeometry.Teilflaechen(site
            .Select(p => new double2(p.x, p.y)).ToArray());
        var summe = double2.zero;
        foreach (var punkt in teile[0]) summe += punkt;
        var mitte = summe / teile[0].Length;

        LayoutSettings Grund()
        {
            var s = LayoutSettings.Cs2;
            s.Zellen = true;
            s.AngleMode = "edge";
            s.Auto = false;
            s.AutomaticEntrances = false;
            s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
            s.Ausrichtwinkel = 20;
            return s;
        }

        var mitVorgabe = Grund();
        mitVorgabe.TeilflaechenAusrichtungen = new[]
        {
            new TeilflaechenAusrichtung
            {
                Anker = new float2((float)mitte.x, (float)mitte.y),
                Winkel = 20,
            },
        };
        var ohneVorgabe = Grund();

        // Einmal warmlaufen. Ohne das misst der erste Bau die JIT-Uebersetzung
        // mit und sieht doppelt so teuer aus wie der zweite - egal, welchen
        // Weg er nimmt.
        ParkingGeometry.Build(site, ohneVorgabe);
        var uhrA = System.Diagnostics.Stopwatch.StartNew();
        var a = ParkingGeometry.Build(site, mitVorgabe);
        uhrA.Stop();
        var uhrB = System.Diagnostics.Stopwatch.StartNew();
        var b = ParkingGeometry.Build(site, ohneVorgabe);
        uhrB.Stop();

        var gleich = a.Stalls == b.Stalls
            && a.GrassSurface.Length == b.GrassSurface.Length
            && a.AsphaltSurface.Length == b.AsphaltSurface.Length
            && a.Aisles == b.Aisles;
        Console.WriteLine($"Einheitlicher Winkel: mit Vorgabe {a.Stalls} Buchten, "
            + $"Gras {a.GrassSurface.Length}, Asphalt {a.AsphaltSurface.Length}, "
            + $"{uhrA.ElapsedMilliseconds} ms | ohne Vorgabe {b.Stalls} Buchten, "
            + $"Gras {b.GrassSurface.Length}, Asphalt {b.AsphaltSurface.Length}, "
            + $"{uhrB.ElapsedMilliseconds} ms | gleich {(gleich ? "ja" : "NEIN")}");
        if (!gleich)
            Console.Error.WriteLine("TEILFLAECHENFEHLER: eine Teilflaeche im "
                + "globalen Winkel liefert ein anderes Layout als gar keine "
                + "Vorgabe.");
        return gleich;
    }

    private static int RunTeilflaechenwinkel()
    {
        var site = new[]
        {
            new float2(0, 0), new float2(180, 0), new float2(180, 60),
            new float2(90, 60), new float2(90, 120), new float2(0, 120),
        };
        var teile = ParkingGeometry.Teilflaechen(site
            .Select(p => new double2(p.x, p.y)).ToArray());
        var anker = teile.Select(teil =>
        {
            var summe = double2.zero;
            foreach (var punkt in teil) summe += punkt;
            var mitte = summe / teil.Length;
            return new float2((float)mitte.x, (float)mitte.y);
        }).ToArray();

        var fehler = !PruefeEinheitlicherWinkel();
        var basisSettings = LayoutSettings.Cs2;
        basisSettings.Zellen = true;
        basisSettings.AutomaticEntrances = false;
        basisSettings.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
        var basis = ParkingGeometry.Build(site, basisSettings);
        Console.WriteLine($"Zellen-Basis unerreichbar {UnservedBays(basis)}, "
            + $"Buchten {basis.Stalls}");
        foreach (var zellen in new[] { false, true })
        {
            var settings = LayoutSettings.Cs2;
            settings.AngleMode = "edge";
            settings.Auto = false;
            settings.Zellen = zellen;
            settings.AutomaticEntrances = false;
            settings.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
            settings.Ausrichtwinkel = 0;
            settings.TeilflaechenAusrichtungen = new[]
            {
                new TeilflaechenAusrichtung { Anker = anker[0], Winkel = 15 },
                new TeilflaechenAusrichtung { Anker = anker[1], Winkel = 75 },
            };
            try
            {
                var layout = ParkingGeometry.Build(site, settings);
                var winkel = string.Join(", ", layout.Teilflaechen.Select(
                    teil => $"{teil.Index}:{teil.Winkel:F2}/{teil.Innenbuchten}"));
                var ueberlappung = OverlapCount(layout.Bay);
                var aufFahrbahn = BayOnRoad(layout, settings);
                var ausserhalb = EndsOutside(layout, site);
                var unerreichbar = UnservedBays(layout);
                var unerreichbarInnen = UnservedBays(new ParkingLayout
                {
                    Bay = layout.Bay.Where((_, index) =>
                        layout.BayKind[index] == BayKind.Inner).ToArray(),
                    PerimeterQuad = layout.PerimeterQuad,
                    AisleQuad = layout.AisleQuad,
                });
                var unerreichbarJeTeil = teile.Select((teil, teilIndex) =>
                {
                    var teilBuchten = layout.Bay.Where((bucht, index) =>
                    {
                        if (layout.BayKind[index] != BayKind.Inner) return false;
                        var mitte = bucht.Aggregate(float2.zero, (summe, p) =>
                            summe + p) / bucht.Length;
                        return PointInRing(mitte, teil.Select(p =>
                            new float2((float)p.x, (float)p.y)).ToArray());
                    }).ToArray();
                    return $"{teilIndex}:{UnservedBays(new ParkingLayout
                    {
                        Bay = teilBuchten,
                        PerimeterQuad = layout.PerimeterQuad,
                        AisleQuad = layout.AisleQuad,
                    })}/{teilBuchten.Length}";
                });
                var ungedeckt = CoverGap(site, layout, settings);
                var konflikt = MeasureRoadConflict(layout);
                var grasAufBucht = GrassOnBayArea(layout);
                var grasAufGasse = GrasUnterGassen(layout);
                Console.WriteLine($"{(zellen ? "Zellen" : "Alt"),-8} "
                    + $"Teile {layout.Teilflaechen.Length}: {winkel} | "
                    + $"Buchten {layout.Stalls} | Nahtstrassen "
                    + $"{layout.TeilflaechenVerbindungen} | ungedeckt "
                    + $"{ungedeckt.Percent:F1} % | Ueberlappung {ueberlappung} | "
                    + $"auf Fahrbahn {aufFahrbahn} | ausserhalb {ausserhalb} | "
                    + $"unerreichbar {unerreichbar} | Strassenkonflikt "
                    + $"{konflikt.Total:F3} m2 | Gras/Bucht "
                    + $"{grasAufBucht:F2} m2 | Gras/Gasse "
                    + $"{grasAufGasse:F2} m2 | Innen unerreichbar "
                    + $"{unerreichbarInnen}/{layout.InnerStalls}, Gassenquads "
                    + $"{layout.AisleQuad.Length}, je Teil "
                    + string.Join(",", unerreichbarJeTeil));
                if (zellen)
                {
                    bool Erreichbar(float2[] bucht)
                    {
                        var strassen = layout.PerimeterQuad.Concat(
                            layout.AisleQuad).ToArray();
                        bool AufStrasse(float2 p) => strassen.Any(strasse =>
                            PointInRing(p, strasse)
                            || DistanceToBoundary(p, strasse) <= 0.001);
                        bool Kante(float2 a, float2 b) => new[]
                            { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f }
                            .All(t => AufStrasse(a + (b - a) * t));
                        return Kante(bucht[0], bucht[1])
                            || Kante(bucht[2], bucht[3]);
                    }
                    Console.WriteLine("  Unerreichbar nach Art/Rolle: "
                        + string.Join(", ", layout.Bay
                            .Select((b, i) => (Bucht: b, Index: i))
                            .Where(x => !Erreichbar(x.Bucht))
                            .GroupBy(x => (layout.BayKind[x.Index],
                                layout.BayRole[x.Index]))
                            .Select(g => $"{g.Key}:{g.Count()}")));
                    Console.WriteLine("  Unerreichbare Randbuchten: "
                        + string.Join(" | ", layout.Bay
                            .Select((b, i) => (Bucht: b, Index: i))
                            .Where(x => layout.BayKind[x.Index] == BayKind.Perimeter
                                && !Erreichbar(x.Bucht))
                            .Select(x => string.Join(";", x.Bucht.Select(
                                p => $"{p.x:F1}/{p.y:F1}")))));
                    Console.WriteLine("  Gassen: " + string.Join(" | ",
                        layout.AisleQuad.Select(q => string.Join(";", q.Select(
                            p => $"{p.x:F1}/{p.y:F1}")))));
                    Console.WriteLine("  Teil-1-Buchten: " + string.Join(" | ",
                        layout.Bay.Where((b, i) => layout.BayKind[i] == BayKind.Inner
                            && b.Average(p => p.x) > 60).Take(12)
                            .Select(q => string.Join(";", q.Select(
                                p => $"{p.x:F1}/{p.y:F1}")))));
                }
                var ok = layout.Teilflaechen.Length == 2
                    && Math.Abs(layout.Teilflaechen[0].Winkel - 15) < 1e-6
                    && Math.Abs(layout.Teilflaechen[1].Winkel - 75) < 1e-6
                    && layout.TeilflaechenVerbindungen == 1
                    && ungedeckt.Percent < 0.05
                    && ueberlappung == 0 && aufFahrbahn == 0
                    && ausserhalb == 0 && unerreichbar == 0
                    && konflikt.Total < 0.005
                    && grasAufBucht < 0.05;
                if (!ok) fehler = true;
            }
            catch (Exception ausnahme)
            {
                fehler = true;
                Console.WriteLine($"{(zellen ? "Zellen" : "Alt"),-8} FEHLER: "
                    + ausnahme.Message);
            }
        }
        return fehler ? 1 : 0;
    }
}
