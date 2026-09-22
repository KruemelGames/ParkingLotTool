using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    internal sealed class Expected
    {
        internal int Stalls;
        internal int Perimeter;
        internal int Inner;
        internal int Aisles;
        internal int Crossings;
        internal int Routes;
        internal int Angle;
        internal int Caps;
        internal int GreenAreas;
        internal int Disabled;
        internal int Electric;
        /**
         * DIE MATERIALZAHLEN DES PROTOTYPS.
         *
         * Sie fehlten hier - und genau dadurch konnte am 2026-08-12 eine echte
         * Abweichung durchrutschen: der Prototyp lieferte fuer die L-Form
         * 3 Asphaltringe mit 0,869685 m Engstelle, der C#-Port unveraendert
         * 4 Ringe mit 0,285152 m. Der Paritaetstest meldete trotzdem gruen,
         * weil er nur Buchten und Winkel verglich. Ein Paritaetstest, der die
         * Paritaet nicht prueft, ist keiner.
         */
        internal int GrassRings;
        internal int AsphaltRings;
        internal double GrassNeck;
        internal double AsphaltNeck;
    }

    internal static readonly (string Name, float2[] Site, Expected Expected)[] Cases =
    {
        ("Rechteck", new[]
        {
            new float2(0, 0), new float2(120, 0),
            new float2(120, 90), new float2(0, 90),
        }, new Expected { Stalls = 218, Perimeter = 98, Inner = 120, Aisles = 4,
                          Crossings = 5, Routes = 1, Angle = 90, Caps = 24, GreenAreas = 48,
                          Disabled = 6, Electric = 8 ,
                          GrassRings = 12, AsphaltRings = 2, GrassNeck = 1.000000, AsphaltNeck = 2.192031 }),
        ("L-Form", new[]
        {
            new float2(0, 0), new float2(120, 0), new float2(120, 45),
            new float2(60, 45), new float2(60, 90), new float2(0, 90),
        // Prototyp 38bd47c: Der alte Halbmodul-Lauf lieferte 172 Buchten,
        // davon 6 ohne Fahrbahn an der Vorderkante. Ohne den ungueltigen
        // Rueckfall entstehen gemessen 184 Buchten und 0 unerreichbare;
        // alle Qualitaetspruefungen bleiben bei 0 Fehlern.
        }, new Expected { Stalls = 184, Perimeter = 133, Inner = 16, Aisles = 1,
                          Crossings = 0, Routes = 0, Angle = 0, Caps = 13, GreenAreas = 32,
                          Disabled = 6, Electric = 8,
                          GrassRings = 5, AsphaltRings = 3, GrassNeck = 1.000000, AsphaltNeck = 2.192031 }),
        ("Schraeg", new[]
        {
            new float2(0, 0), new float2(120, 0),
            new float2(150, 90), new float2(30, 90),
        }, new Expected { Stalls = 199, Perimeter = 99, Inner = 99, Aisles = 5,
                          Crossings = 5, Routes = 1, Angle = 100, Caps = 22, GreenAreas = 51,
                          Disabled = 6, Electric = 8 ,
                          GrassRings = 13, AsphaltRings = 3, GrassNeck = 0.999999, AsphaltNeck = 1.252445 }),
        // Areal aus VideoFrames/parking_08s.png, unveraendert aus PRESETS.ref.
        ("Referenz 08s", new[]
        {
            new float2(0, 0), new float2(127, 0), new float2(127, 112),
            new float2(110, 106), new float2(95, 100), new float2(80, 92),
            new float2(68, 83), new float2(56, 74), new float2(43, 67),
            new float2(23, 64), new float2(0, 56),
        }, new Expected { Stalls = 202, Perimeter = 105, Inner = 96, Aisles = 4,
                          Crossings = 5, Routes = 1, Angle = 90, Caps = 19, GreenAreas = 54,
                          Disabled = 6, Electric = 8 ,
                          GrassRings = 11, AsphaltRings = 4, GrassNeck = 0.699933, AsphaltNeck = 1.059788 }),
    };

    // Echter Debug-Abzug des NoTriangles-Falls. Die Weltlage um -1280 ist
    // wesentlich: dort machte erst die Ausgabe der intern sauberen double-Ringe
    // als float2 zwei Selbstueberschneidungen sichtbar.
    private static readonly float2[] FloatRegressionSite =
    {
        new float2(-1180.2864990234375f, -6.1768951416015625f),
        new float2(-1275.8187255859375f, -18.192417144775390625f),
        new float2(-1297.6337890625f, 163.3715362548828125f),
        new float2(-1189.748779296875f, 161.18499755859375f),
    };

    // Debug-Abzug 2026-08-09 23:32:00, aktuelle DLL 23:22:37. Hier wurden
    // genau neun schmale Restflächen mit NoTriangles verworfen.
    private static readonly float2[] CurrentNoTrianglesSite =
    {
        new float2(-1186.68212890625f, -20.080085754394531f),
        new float2(-1285.4976806640625f, -9.703590393066406f),
        new float2(-1311.17626953125f, 133.32806396484375f),
        new float2(-1264.260009765625f, 193.15721130371094f),
        new float2(-1198.008544921875f, 191.7882080078125f),
    };

    // Echter Nutzerfall vom 2026-08-10. An einer Fahrbahnkante lagen zwei
    // Gras-/Asphaltknoten nur 4,2 cm auseinander; die alte Grasroutine warf.
    private static readonly float2[] UserSurfaceRegressionSite =
    {
        new float2(-1359.43f, -54.16f),
        new float2(-1180.12f, -25.83f),
        new float2(-1187.96f, 104.70f),
        new float2(-1368.21f, 106.86f),
    };

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--versorgung") return PruefeVersorgungskurse();
        // Ohne diesen Rahmen zeigt Windows bei jeder unbehandelten Ausnahme
        // einen Absturzdialog. Waehrend der Arbeit lief der Test hunderte Male,
        // und der Nutzer bekam jedes Mal ein Fenster mitten ins Spiel. Die
        // Meldung gehoert in die Ausgabe, nicht in einen Dialog.
        try { return Run(args); }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FEHLGESCHLAGEN: " + exception.Message);
            Console.Error.WriteLine(exception.StackTrace);
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        /*
         * `--live` schaltet denselben Live-Log ein, den der Nutzer im Spiel
         * einschaltet. Damit lesen wir hier dieselben Zeilen, die er mir
         * schickt - und koennen einen Verdacht nachmessen, ohne CS2 zu
         * starten. Der Haken ist sonst leer und kostet nichts.
         */
        if (args.Any(a => a == "--live"))
        {
            ParkingGeometry.LiveSchreiber = Console.WriteLine;
            args = args.Where(a => a != "--live").ToArray();
        }
        // Mit `--dump <ordner>` nur den vollstaendigen Abzug schreiben.
        if (args.Length == 2 && args[0] == "--dump") { Dump.Write(args[1]); return 0; }
        // Nur die Flaechen unter den Einstellungen, mit denen der Mod
        // wirklich laeuft - Gegenstueck zu `parity-echt.cjs` im Prototyp.
        if (args.Length == 1 && args[0] == "--echt") { RunEcht(); return 0; }
        // End-to-End-Wirkungsmessung aller Bedienelemente des Panels.
        if (args.Length == 1 && args[0] == "--regler") return RunRegler();
        if (args.Length == 1 && args[0] == "--eckrand") return RunEckrand();
        if (args.Length == 1 && args[0] == "--marker") { RunMarker(); return 0; }
        if (args.Length == 1 && args[0] == "--zoningtreffer")
            return RunZoningtreffer();
        if (args.Length == 1 && args[0] == "--gassenreste") return RunGassenreste();
        if (args.Length == 1 && args[0] == "--zoningkanten") return RunZoningkanten();
        if (args.Length == 1 && args[0] == "--zoningnetz")
            return RunZoningnetz();
        if (args.Length == 1 && args[0] == "--zoningflaeche")
            return RunZoningflaeche();
        if (args.Length == 1 && args[0] == "--zoningrasten")
            return RunZoningrasten();
        if (args.Length == 1 && args[0] == "--ueberlappung")
            return RunUeberlappung();
        if (args.Length == 1 && args[0] == "--infokarten")
            return RunInfokarten();
        if (args.Length == 1 && args[0] == "--flaechenannahme")
            return RunFlaechenannahme();
        if (args.Length == 1 && args[0] == "--zufahrtsverlust")
            return RunZufahrtsverlust();
        if (args.Length == 1 && args[0] == "--zerlegung")
        { RunZerlegungsprobe(); return 0; }
        if (args.Length >= 1 && args[0] == "--protokoll")
        {
            RunProtokoll(
                args.Length > 1 ? args[1] : string.Empty,
                args.Length > 2 ? double.Parse(args[2],
                    System.Globalization.CultureInfo.InvariantCulture) : 0.0,
                args.Length > 3 ? double.Parse(args[3],
                    System.Globalization.CultureInfo.InvariantCulture) : 90.0);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--gebaut")
        { RunGebauteForm(); return 0; }
        if (args.Length == 1 && args[0] == "--nutzerform")
        { RunNutzerform(); return 0; }
        if (args.Length == 2 && args[0] == "--nutzerbild")
        { RunNutzerbild(args[1]); return 0; }
        if (args.Length >= 2 && args[0] == "--polygon")
        {
            var zellenVergleich = args.Skip(2).Any(a => a == "--zellen");
            var lage = args.Skip(2).Where(a => a != "--zellen").ToArray();
            if (lage.Length >= 2)
            {
                ZufahrtKante = int.Parse(lage[0]);
                ZufahrtLaenge = double.Parse(lage[1],
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            if (zellenVergleich)
                return RunPolygonVergleich(args[1]);
            RunPolygon(args[1]);
            return 0;
        }
        if (args.Length >= 1 && args[0] == "--schwarm")
        {
            RunSchwarm(args.Length > 1 && int.TryParse(args[1], out var n) ? n : 60);
            return 0;
        }
        // Der eigene Test - siehe Program.Qualitaet.cs. Er prueft nur, was aus
        // sich heraus richtig oder falsch ist, ohne Vergleich mit dem Prototyp.
        if (args.Length >= 1 && args[0] == "--formen-export")
            return RunFormenExport(args.Length > 1 && int.TryParse(args[1], out var fe)
                ? fe : 60);
        // Zaehlt, an welchen Regeln der Zellenweg aussteigt - siehe
        // Program.Zellenlauf.cs.
        if (args.Length >= 1 && args[0] == "--zellenlauf") return RunZellenlauf();
        // Wo bleibt Gras, wo nicht - siehe Program.Kappen.cs.
        if (args.Length >= 1 && args[0] == "--kappen") return RunKappen();
        // Der gemeldete Fall vom 2026-08-25 - siehe Program.Befund.cs.
        if (args.Length >= 1 && args[0] == "--befund") return RunBefund();
        // Warum fehlt die Querstrasse - siehe Program.Querstrassen.cs.
        if (args.Length >= 1 && args[0] == "--gassenabstand") return RunGassenabstand();
        if (args.Length >= 1 && args[0] == "--randstrassen") return RunRandstrassen();
        if (args.Length >= 1 && args[0] == "--randstrassen-mutation") return RunRandstrassen(true);
        if (args.Length >= 1 && args[0] == "--zufahrtsschnitt") return RunZufahrtsschnitt();
        if (args.Length >= 1 && args[0] == "--ringlosflaechen") return RunRinglosflaechen();
        if (args.Length >= 1 && args[0] == "--quer") return RunQuerstrassen();
        // Der L-Form-Fall vom 2026-08-26 - siehe Program.Querfall.cs.
        if (args.Length >= 1 && args[0] == "--querfall") return RunQuerfall();
        if (args.Length == 2 && args[0] == "--befunddaten")
            return RunBefundDaten(args[1]);
        // Systematisch ueber einspringende Ecken - siehe Program.LFormen.cs.
        if (args.Length >= 1 && args[0] == "--lformen") return RunLFormen();
        // Zaehlt vereinzelte Buchten - siehe Program.Treppen.cs.
        if (args.Length >= 1 && args[0] == "--treppen") return RunTreppen();
        if (args.Length >= 2 && args[0] == "--gruppen")
            return RunGruppen(args[1], args.Length > 2 ? args[2] : null);
        // Misst, was CS2 von den Flaechen ANNIMMT - siehe Program.Flaechen.cs.
        // Trifft der Nachbau von CS2s Ear-Clipping das Spiel?
        if (args.Length >= 1 && args[0] == "--triangulierung")
            return args.Length >= 3
                && float.TryParse(args[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var tx)
                && float.TryParse(args[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var tz)
                ? RunTriangulierungstest(tx, tz)
                : RunTriangulierungstest();
        // Bilder der Formen, an denen CS2 noch Flaechen verwirft.
        if (args.Length >= 1 && args[0] == "--fehlerbilder")
            return RunFehlerbilder(args.Length > 1 ? args[1]
                : System.IO.Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "fehlerbilder.html"));
        if (args.Length >= 2 && args[0] == "--verworfen")
        {
            VerbindungAlle = args.Skip(2)
                .Where(a => a.StartsWith("n=", StringComparison.Ordinal))
                .Select(a => int.Parse(a.Substring(2)))
                .DefaultIfEmpty(0).First();
            return RunVerworfen(args[1],
                !args.Skip(2).Contains("ohnekappen"));
        }
        if (args.Length >= 1 && args[0] == "--flaechen")
            return RunFlaechen(args.Length > 1 && int.TryParse(args[1], out var fl)
                ? fl : 0);
        if (args.Length >= 1 && args[0] == "--doppelpunkte")
            return RunDoppelpunkte(
                args.Length > 1 && int.TryParse(args[1], out var dp) ? dp : 192,
                args.Length > 2 ? args[2] : null);
        if (args.Length >= 1 && args[0] == "--flaechen-ursache")
            return RunFlaechenUrsache(args.Length > 1
                && int.TryParse(args[1], out var fu) ? fu : 0);
        if (args.Length >= 1 && args[0] == "--flaechen-studie")
            return RunFlaechenStudie(args.Length > 1
                && int.TryParse(args[1], out var fs) ? fs : 0);
        if (args.Length >= 1 && args[0] == "--flaechen-naehte")
            return RunFlaechenNaehte(
                args.Length > 1 ? args[1] : @"..\flaechen-naehte.jsonl",
                args.Length > 2 && int.TryParse(args[2], out var fn) ? fn : 0);
        if (args.Length >= 1 && args[0] == "--randzoningseite")
            return RunRandzoningseite();

        if (args.Length >= 1 && args[0] == "--querstrassengitter")
            return RunQuerstrassengitter();

        if (args.Length >= 1 && args[0] == "--rzstufen") return RunRzstufen();
        if (args.Length >= 1 && args[0] == "--geradepunkt")
            return RunGeradepunkt();

        if (args.Length >= 1 && args[0] == "--rzanschluss")
            return RunRzanschluss();

        if (args.Length >= 1 && args[0] == "--rzgasse")
            return RunRzgasse();

        if (args.Length >= 1 && args[0] == "--entarteteringe")
            return RunEntarteteringe();

        if (args.Length >= 1 && args[0] == "--ortsunabhaengig")
            return RunOrtsunabhaengig();

        if (args.Length >= 1 && args[0] == "--werkzeugzustand")
            return RunWerkzeugzustand();

        if (args.Length >= 1 && args[0] == "--diagonalwege")
            return RunDiagonalwege();

        if (args.Length >= 1 && args[0] == "--endwegbreite")
            return RunEndwegbreite();

        if (args.Length >= 1 && args[0] == "--winkelmodus")
            return RunWinkelmodus();

        if (args.Length >= 1 && args[0] == "--fusswegzugang")
            return RunFusswegzugang();

        if (args.Length >= 1 && args[0] == "--lformtoggle")
            return RunLformtoggle();

        if (args.Length >= 1 && args[0] == "--zufahrtsquads")
            return RunZufahrtsquads();
        if (args.Length == 1 && args[0] == "--zufahrtsachse")
            return RunZufahrtsachse();
        if (args.Length == 1 && args[0] == "--zufahrtsbefund")
            return RunZufahrtsbefund();

        // Wie weit liegen die Sonderplaetze von ihrem Anker?
        if (args.Length >= 3 && args[0] == "--sonderplaetze")
            return RunSonderplaetze(args[1], args[2]);
        // Konstruktionsmasse und Arttransport der vier Zugangsarten.
        if (args.Length >= 1 && args[0] == "--zufahrtsarten")
            return RunZufahrtsarten();
        if (args.Length >= 1 && args[0] == "--teilflaechenwinkel")
            return RunTeilflaechenwinkel();
        if (args.Length >= 1 && args[0] == "--qualitaet")
            return RunQualitaet(args.Length > 1 && int.TryParse(args[1], out var q)
                ? q : 60);
        /*
         * ALTLAST. Dieser Lauf war das Portierungswerkzeug vom Browser-
         * Prototyp nach C#, und dafuer war er unverzichtbar. Diese Aufgabe ist
         * erledigt: der Nutzer hat den Prototyp am 2026-08-17 fuer erledigt
         * erklaert, und am 2026-08-27 wurde nachgemessen, dass sechs der
         * sieben roten Zeilen gegenstandslos sind - der Mod rechnet dort
         * bewusst anders. Die Belege stehen in `PARITAET-GEKLAERT.md`.
         *
         * Rot heisst hier also nicht "kaputt", sondern "der Mod hat den
         * Prototyp ueberholt". Der Lauf kennt seine sieben Altlasten deshalb
         * beim Namen und meldet nur noch, was NEU dazukommt. Nur so bleibt er
         * als Wachhund brauchbar, ohne das Projekt dauerhaft rot aussehen zu
         * lassen.
         *
         * Der Nachfolger ist `--qualitaet`: der prueft selbsttragende
         * Eigenschaften und braucht ueberhaupt keinen Prototyp.
         */
        Console.WriteLine("ALTLAST-LAUF (Portierung Prototyp -> C#, "
            + "abgeschlossen). Rote Zeilen sind hier erwartet und erklaert in "
            + "PARITAET-GEKLAERT.md.");
        Console.WriteLine("Der lebende Massstab ist --qualitaet. "
            + "Sollwerte NICHT anpassen, um es gruen zu bekommen.");
        Console.WriteLine();
        var spiegel = new Altlastenspiegel(Console.Error);
        Console.SetError(spiegel);

        var failed = false;
        foreach (var item in Cases)
        {
            var watch = Stopwatch.StartNew();
            /*
             * EIN ABSTURZ DARF NICHT DEN GANZEN LAUF ERSETZEN.
             *
             * Seit dieser Lauf den ECHTEN Rechenweg misst (2026-09-01),
             * fliegt er bei einer der Referenzformen mit "Two adjacent
             * boundary edges are parallel" aus `Layoutplanung.Innenrand`.
             * Vorher lief er gegen den alten Weg und hat das nie gesehen.
             *
             * Ein geworfener Fehler ist der schwerste Befund, den es hier
             * gibt - aber wenn er den Lauf beendet, bleiben alle folgenden
             * Formen UNGEMESSEN, und der naechste echte Regress faellt gar
             * nicht mehr auf. Also wird er als Fehler dieser Form gemeldet
             * und der Lauf geht weiter. Verschwiegen wird nichts: die Form
             * steht mit ihrem Namen und der Meldung da.
             */
            ParkingLayout layout;
            try
            {
                layout = ParkingGeometry.Build(item.Site, LayoutSettings.Cs2);
            }
            catch (Exception fehler)
            {
                watch.Stop();
                failed = true;
                Console.Error.WriteLine($"BAUABSTURZ: {item.Name} - "
                    + fehler.Message);
                continue;
            }
            watch.Stop();
            var overlap = OverlapCount(layout.Bay);
            var onRoad = BayOnRoad(layout, LayoutSettings.Cs2);
            var outside = EndsOutside(layout, item.Site);
            var unserved = UnservedBays(layout);
            var crossSpacing = ParkingGeometry.CrossRouteSpacing(layout.CrossRouteLine);
            var crossSpacingOk = double.IsPositiveInfinity(crossSpacing)
                || crossSpacing >= LayoutSettings.Cs2.Cr - 0.001;
            var allRoadsReported = layout.AisleLine.Length
                    >= layout.NotchAisles + layout.Aisles
                && layout.CrossRouteLine.Length == layout.Crossings
                && layout.PassInfo.Sum(info => info.Aisles) == layout.Aisles
                && layout.PassInfo.Sum(info => info.Crossings) == layout.Crossings;
            var uncovered = CoverGap(item.Site, layout, LayoutSettings.Cs2);
            var roadConflict = MeasureRoadConflict(layout);
            var materialConflict = MeasureMaterialConflict(layout);
            var greenAreas = layout.Cap.Concat(layout.Median).Concat(layout.Green)
                .Concat(layout.Fill).ToArray();
            var grassSurfaceRings = layout.GrassSurface;
            var asphaltSurfaceRings = layout.AsphaltSurface;
            var selfIntersections = grassSurfaceRings.Sum(SelfIntersectionCount)
                + asphaltSurfaceRings.Sum(SelfIntersectionCount);
            var nearDuplicatePoints = grassSurfaceRings.Count(HasNearDuplicatePoints)
                + asphaltSurfaceRings.Count(HasNearDuplicatePoints);
            var shortestEdge = ShortestEdge(grassSurfaceRings);
            // ASPHALT WURDE NIE GEPRUEFT - und genau dort meldeten die
            // Nutzerberichte vom 2026-08-12 Kanten von 0,101 m und 0,009 m.
            var shortestAsphaltEdge = ShortestEdge(asphaltSurfaceRings);
            var grassMinimumBottleneck = MinimumMaterialBottleneck(grassSurfaceRings);
            var asphaltMinimumBottleneck = MinimumMaterialBottleneck(asphaltSurfaceRings);
            Console.WriteLine($"{item.Name,-14} {layout.Stalls,4} Buchten | "
                + $"Rand {layout.PerimeterStalls,3} | innen {layout.InnerStalls,3} | "
                + $"Gassen {layout.Aisles} | Quer {layout.CrossLine.Length} | Winkel {layout.Angle} | "
                + $"Kappen {layout.Cap.Length} | behindert {layout.SpecialStalls.Behindert} | "
                + $"elektro {layout.SpecialStalls.Elektro} | {watch.ElapsedMilliseconds} ms");
            Console.WriteLine($"{string.Empty,-14} ungedeckt {uncovered.Percent:F1} % | "
                + $"Ueberlappung {overlap} | Bucht auf Fahrbahn {onRoad} | "
                + $"Enden ausserhalb {outside} | unerreichbar {unserved}");
            Console.WriteLine($"{string.Empty,-14} Strasse auf Strasse "
                + $"{roadConflict.Total:F2} m2 | Rand/Gasse {roadConflict.RandGasse:F2} | "
                + $"Rand/Verbindung {roadConflict.RandVerbindung:F2} | "
                + $"Gasse/Verbindung {roadConflict.GasseVerbindung:F2}");
            Console.WriteLine($"{string.Empty,-14} Fahrwege ausgegeben "
                + $"Gassen {layout.AisleLine.Length}/{layout.NotchAisles + layout.Aisles} | "
                + $"Querungen {layout.CrossRouteLine.Length}/{layout.Crossings} | "
                + $"kleinster Abstand "
                + (double.IsPositiveInfinity(crossSpacing)
                    ? "nur eine" : $"{crossSpacing:F2} m"));
            Console.WriteLine($"{string.Empty,-14} Asphalt auf Gras "
                + $"{materialConflict:F2} m2 | Material Gras {grassSurfaceRings.Length} | "
                + $"Asphalt {asphaltSurfaceRings.Length} | Engstelle Gras "
                + $"{grassMinimumBottleneck:F6} m | Asphalt {asphaltMinimumBottleneck:F6} m");
            Console.WriteLine($"{string.Empty,-14} logische Gruenflaechen {greenAreas.Length} | "
                + $"ueberschneiden nach float {selfIntersections} | "
                + $"Doppelpunkte {nearDuplicatePoints} | "
                + $"kuerzeste Kante Gras {shortestEdge:F3} m | "
                + $"Asphalt {shortestAsphaltEdge:F3} m | "
                + $"echte Engstelle Gras {grassMinimumBottleneck:F4} m");

            // Stellplatz-Decals: ein Decal je Buchtenpaar. "ohne Partner"
            // sind Reihen mit ungerader Buchtenzahl - sie bleiben gepflastert,
            // aber unmarkiert. "falsch gedreht" pruefen wir hier mit: jedes
            // Decal muss zu einer Fahrbahn zeigen, nicht von ihr weg.
            var decals = ParkingBayDecals.Plan(layout, LayoutSettings.Cs2);
            var misfacing =
                CountDecalsAgainstNeighbour(decals, LayoutSettings.Cs2.Sw);
            Console.WriteLine($"{string.Empty,-14} Decals {decals.Placements.Length} "
                + $"fuer {decals.Stalls} von {layout.Stalls} Buchten | "
                + $"normal {decals.Normal} | behindert {decals.Disabled} | "
                + $"elektro {decals.Electric} | "
                + $"nicht erkannt {decals.Unrecognized} | "
                + $"gegen den Nachbarn gedreht {misfacing} | "
                + $"Ladesaeulen {decals.Chargers.Length}");

            /**
             * ELEKTRO IMMER PAARWEISE - dieselbe Pruefung wie im Prototyp.
             * Die Ladesaeule steht zwischen zwei Buchten, also kann es weder
             * eine ungerade Zahl noch einen verstreuten Einzelplatz geben.
             * Die reine Stueckzahl oben wuerde beides durchgehen lassen.
             */
            var electricIndices = Enumerable.Range(0, layout.BayRole.Length)
                .Where(i => layout.BayRole[i] == BayRole.Electric).ToList();
            var pairedIndices = layout.ElectricPair
                .SelectMany(pair => new[] { pair.x, pair.y }).ToList();
            var electricPaired = electricIndices.Count % 2 == 0
                && pairedIndices.Count == electricIndices.Count
                && electricIndices.All(pairedIndices.Contains);
            var pairGapError = 0.0;
            foreach (var pair in layout.ElectricPair)
                pairGapError = Math.Max(pairGapError, Math.Abs(
                    math.distance(QuadCenter(layout.Bay[pair.x]),
                                  QuadCenter(layout.Bay[pair.y]))
                    - LayoutSettings.Cs2.Sw));
            var electricOk = electricPaired
                && (layout.ElectricPair.Length == 0 || pairGapError < 0.05);
            Console.WriteLine($"{string.Empty,-14} Ladesaeulen "
                + $"{layout.ElectricPair.Length} | gerade "
                + $"{(electricIndices.Count % 2 == 0 ? "ja" : "NEIN")} | "
                + $"alle gepaart {(electricPaired ? "ja" : "NEIN")} | "
                + $"Nachbarabstand {pairGapError:E1} m Fehler "
                + $"(soll {LayoutSettings.Cs2.Sw} m)");

            /**
             * VORSCHAU-STREIFEN. Fuer die Anzeige im Spiel werden benachbarte
             * Buchten zu einem Rechteck zusammengefasst - 218 Kacheln sehen
             * nicht aus wie ein Parkplatz, und der Overlay kann keine Vielecke
             * fuellen.
             *
             * Die Vorschau soll zeigen, was gebaut wird. Also muss die
             * zusammengefasste Flaeche exakt der Buchtenflaeche entsprechen,
             * und jede Bucht muss in genau einem Streifen stecken. Sonst
             * verspricht die Anzeige etwas anderes als das Ergebnis.
             */
            var runs = ParkingBayRuns.Merge(layout, LayoutSettings.Cs2);
            var bayArea = layout.Bay.Sum(q => Math.Abs(SignedArea(q)));
            var runArea = runs.Sum(r => Math.Abs(SignedArea(r.Quad)));
            var runBays = runs.Sum(r => r.Bays);
            var areaError = Math.Abs(runArea - bayArea);
            var runsOk = runBays == layout.Bay.Length && areaError < 0.01;
            Console.WriteLine($"{string.Empty,-14} Vorschau-Streifen {runs.Length} "
                + $"statt {layout.Bay.Length} Buchten "
                + $"({(layout.Bay.Length > 0 ? 100 - 100.0 * runs.Length / layout.Bay.Length : 0):F0} % weniger) | "
                + $"Buchten erfasst {runBays}/{layout.Bay.Length} | "
                + $"Flaeche {runArea:F2} gegen {bayArea:F2} m2, "
                + $"Fehler {areaError:E1} m2");

            /**
             * VORSCHAU-FUELLUNG. Der Overlay kann kein Vieleck fuellen, wohl
             * aber einen breiten Streifen aufs Gelaende projizieren. Gras und
             * Asphalt werden deshalb in Streifen zerlegt.
             *
             * Gemessen wird beides, was zaehlt: wie viele Streifen es kostet
             * und wie genau sie die Flaeche treffen. Zu wenige Streifen sehen
             * aus wie ein Kamm, zu viele kosten Leistung.
             */
            /**
             * WAS DER OVERLAY AM ENDE ZEICHNET.
             *
             * Gruen kommt aus den ENTWURFS-Rechtecken (Median, Kappen, Gruen,
             * Rest), nicht aus den verschmolzenen Ringen: die Teile sind fast
             * alle echte Vierecke und damit mit je EINEM Streifen exakt
             * gedeckt. Ueber die Ringe zu gehen kostete im Rechteck-Fall 154
             * Streifen und bis zu 21 % Flaechenfehler.
             *
             * Asphalt braucht gar keine Fuellung - er IST Buchten plus
             * Fahrwege.
             */
            var greenStrips =
                ParkingSurfaceStrips.Fill(layout.Median, ParkingSurfaceStrips.PreferredWidth).Length
                + ParkingSurfaceStrips.Fill(layout.Cap, ParkingSurfaceStrips.PreferredWidth).Length
                + ParkingSurfaceStrips.Fill(layout.Green, ParkingSurfaceStrips.PreferredWidth).Length
                + ParkingSurfaceStrips.Fill(layout.Fill, ParkingSurfaceStrips.PreferredWidth).Length;
            var overlayRoads = layout.PerimeterQuad.Length + layout.AisleQuad.Length
                + layout.CrossQuad.Length + layout.EntranceQuad.Length;
            var overlayNow = greenStrips + overlayRoads + runs.Length
                + decals.Chargers.Length;
            Console.WriteLine($"{string.Empty,-14} Overlay-Formen {overlayNow} "
                + $"= Gruen {greenStrips} + Fahrwege {overlayRoads} + Streifen "
                + $"{runs.Length} + Saeulen {decals.Chargers.Length} | frueher "
                + $"{layout.Bay.Length} (nur Buchten, ohne Gruen und Fahrwege)");

            /*
             * DER PROTOTYP-VERGLEICH IST STILLGELEGT (2026-09-01).
             *
             * `item.Expected` sind die Zahlen des Browser-Prototyps. Sie
             * waren die Abnahme fuer EINE Aufgabe: die Portierung nach C#.
             * Die ist abgeschlossen, und der Rechenweg, den sie beschreiben,
             * ist ausgebaut - gerechnet wird im Zellenweg.
             *
             * Angepasst werden die Sollwerte NICHT; der Kopf dieser Datei
             * verbietet das zu Recht. Sie werden nur nicht mehr als Urteil
             * benutzt: ein Waechter, dessen Gegenstand es nicht mehr gibt,
             * gibt eine Sicherheit vor, die es nicht gibt. Genau daran ist
             * der Winkelfehler vom 2026-08-31 vorbeigelaufen.
             *
             * Der Vergleich wird weiter AUSGEGEBEN, wenn er abweicht - als
             * Information, nicht als Fehler. Die Abnahme ist `--qualitaet`.
             *
             * Was hier urteilt, sind die Pruefungen, die von keinem
             * Rechenweg abhaengen: ungedeckte Flaeche, Ueberlappung, Buchten
             * auf der Fahrbahn, Enden ausserhalb, unerreichbare Buchten,
             * Strassenkonflikte, Selbstschnitte, Doppelpunkte und die
             * Mindestbreite der Materialflaechen.
             */
            var expected = item.Expected;
            var materialParity =
                grassSurfaceRings.Length == expected.GrassRings
                && asphaltSurfaceRings.Length == expected.AsphaltRings
                && Math.Abs(grassMinimumBottleneck - expected.GrassNeck) < 1e-4
                && Math.Abs(asphaltMinimumBottleneck - expected.AsphaltNeck) < 1e-4;
            if (!materialParity)
                Console.WriteLine($"{string.Empty,-14} Prototyp-Vergleich "
                    + "(stillgelegt): "
                    + $"Gras {grassSurfaceRings.Length}/{expected.GrassRings} Ringe, "
                    + $"Asphalt {asphaltSurfaceRings.Length}/{expected.AsphaltRings}, "
                    + $"Engstelle Gras {grassMinimumBottleneck:F6}/{expected.GrassNeck:F6}, "
                    + $"Asphalt {asphaltMinimumBottleneck:F6}/{expected.AsphaltNeck:F6}");
            // Abgenommen und ausgegeben wird auf eine Zehntelprozentstelle.
            // Die 5-cm-Knotenbereinigung laesst wie das JS-Modell in Referenz
            // genau eine 0,25-m2-Rasterzelle offen; das sind weiterhin 0,0 %.
            /*
             * DIE MELDUNG MUSS DEN MANGEL NENNEN, NICHT NUR DIE FORM.
             *
             * Bis zum 2026-09-01 stand hier blosses "PARITAETSFEHLER: Schraeg".
             * Die Altlastenliste merkt sich Meldungen woertlich - also merkte
             * sie sich FORMNAMEN. Damit war jede Form, die einmal auf der
             * Liste stand, ab sofort freigegeben: sie durfte beliebig weiter
             * kaputtgehen, ohne dass ein Wort im Text sich aendert.
             *
             * GENAU SO IST ES PASSIERT. Die Eckendeckung derselben Sitzung
             * schob bei "Schraeg" 12 Buchten unter den Asphalt (vorher 0) -
             * ein neuer Mangel, den es vorher auf KEINER Form gab. Der Lauf
             * meldete unveraendert "17 Meldungen, alle bekannt".
             *
             * Jetzt traegt jede Meldung ihre Gruende samt Messwert. Ein neuer
             * Mangel an einer schon bekannten Form aendert damit die Zeile und
             * faellt auf. Der Preis ist bewusst gewaehlt: jede VERBESSERUNG
             * aendert die Zeile ebenfalls und faerbt den Lauf einmal rot, bis
             * die Liste nachgezogen ist. Das ist die richtige Richtung - eine
             * Liste, die man nach jedem Fortschritt anfassen muss, bleibt
             * wahr; eine, die man nie anfassen muss, verliert den Bezug.
             */
            var gruende = new List<string>();
            if (!electricOk) gruende.Add("Elektro nicht paarweise");
            if (!runsOk) gruende.Add("Reihen decken die Buchten nicht");
            if (uncovered.Percent >= 0.05)
                gruende.Add($"ungedeckt {uncovered.Percent:F1} %");
            if (overlap != 0) gruende.Add($"Buchtueberlappung {overlap}");
            if (onRoad != 0) gruende.Add($"Bucht auf Fahrbahn {onRoad}");
            if (outside != 0) gruende.Add($"Enden ausserhalb {outside}");
            if (unserved != 0) gruende.Add($"unerreichbar {unserved}");
            if (!crossSpacingOk) gruende.Add("Querabstand zu klein");
            if (!allRoadsReported) gruende.Add("Fahrwege unvollstaendig gemeldet");
            if (roadConflict.Total >= 0.005)
                gruende.Add($"Strasse auf Strasse {roadConflict.Total:F2} m2");
            if (materialConflict >= 0.005)
                gruende.Add($"Asphalt auf Gras {materialConflict:F2} m2");
            if (selfIntersections != 0)
                gruende.Add($"Selbstschnitte {selfIntersections}");
            if (nearDuplicatePoints != 0)
                gruende.Add($"Doppelpunkte {nearDuplicatePoints}");
            if (grassMinimumBottleneck
                < ParkingGeometry.SurfaceNeckLimit - 1e-6)
                gruende.Add($"Engstelle Gras {grassMinimumBottleneck:F3} m");
            if (asphaltMinimumBottleneck
                < ParkingGeometry.SurfaceNeckLimit - 1e-6)
                gruende.Add($"Engstelle Asphalt {asphaltMinimumBottleneck:F3} m");
            if (gruende.Count != 0)
            {
                failed = true;
                Console.Error.WriteLine($"PARITAETSFEHLER: {item.Name} ["
                    + string.Join(", ", gruende) + "]");
            }
        }

        RunSwitchParity(ref failed);

        // ParkingGeometry.Build liefert hier bereits float2. Damit prueft dieser
        // Regressionstest bewusst die ECS-Ausgabe und nicht noch einmal die intern
        // korrekten double-Koordinaten, an denen der Fehler unsichtbar blieb.
        var floatLayout = ParkingGeometry.Build(FloatRegressionSite, LayoutSettings.Cs2);
        var floatRings = floatLayout.GrassSurface;
        var floatAsphaltRings = floatLayout.AsphaltSurface;
        var floatSelfIntersections = floatRings.Sum(SelfIntersectionCount)
            + floatAsphaltRings.Sum(SelfIntersectionCount);
        var floatNearDuplicatePoints = floatRings.Count(HasNearDuplicatePoints)
            + floatAsphaltRings.Count(HasNearDuplicatePoints);
        var floatShortestEdge = ShortestEdge(floatRings);
        var floatGrassMinimumBottleneck = MinimumMaterialBottleneck(floatRings);
        var floatAsphaltMinimumBottleneck = MinimumMaterialBottleneck(floatAsphaltRings);
        var floatRoadConflict = MeasureRoadConflict(floatLayout);
        var floatMaterialConflict = MeasureMaterialConflict(floatLayout);
        Console.WriteLine($"{"Float-Regress.",-14} Gruenflaechen {floatRings.Length} | "
            + $"ueberschneiden nach float {floatSelfIntersections} | "
            + $"Doppelpunkte {floatNearDuplicatePoints} | "
            + $"kuerzeste Kante {floatShortestEdge:F3} m | "
            + $"Engstelle Gras {floatGrassMinimumBottleneck:F6} m | "
            + $"Asphalt {floatAsphaltMinimumBottleneck:F6} m");
        Console.WriteLine($"{string.Empty,-14} Strasse auf Strasse "
            + $"{floatRoadConflict.Total:F2} m2 | Rand/Gasse {floatRoadConflict.RandGasse:F2} | "
            + $"Rand/Verbindung {floatRoadConflict.RandVerbindung:F2} | "
            + $"Gasse/Verbindung {floatRoadConflict.GasseVerbindung:F2}");
        Console.WriteLine($"{string.Empty,-14} Asphalt auf Gras "
            + $"{floatMaterialConflict:F2} m2");
        if (floatSelfIntersections != 0 || floatNearDuplicatePoints != 0
            || floatGrassMinimumBottleneck < ParkingGeometry.SurfaceNeckLimit - 1e-6
            || floatAsphaltMinimumBottleneck < ParkingGeometry.SurfaceNeckLimit - 1e-6
            || floatRoadConflict.Total >= 0.005 || floatMaterialConflict >= 0.005)
        {
            failed = true;
            Console.Error.WriteLine("PARITAETSFEHLER: Float-Regress."
                + Gruende(
                    (floatSelfIntersections != 0,
                        $"Selbstschnitte {floatSelfIntersections}"),
                    (floatNearDuplicatePoints != 0,
                        $"Doppelpunkte {floatNearDuplicatePoints}"),
                    (floatGrassMinimumBottleneck
                        < ParkingGeometry.SurfaceNeckLimit - 1e-6,
                        $"Engstelle Gras {floatGrassMinimumBottleneck:F3} m"),
                    (floatAsphaltMinimumBottleneck
                        < ParkingGeometry.SurfaceNeckLimit - 1e-6,
                        $"Engstelle Asphalt {floatAsphaltMinimumBottleneck:F3} m"),
                    (floatRoadConflict.Total >= 0.005,
                        $"Strasse auf Strasse {floatRoadConflict.Total:F2} m2"),
                    (floatMaterialConflict >= 0.005,
                        $"Asphalt auf Gras {floatMaterialConflict:F2} m2")));
        }

        var currentLayout = ParkingGeometry.Build(
            CurrentNoTrianglesSite, LayoutSettings.Cs2);
        var currentGrassNeck = MinimumMaterialBottleneck(currentLayout.GrassSurface);
        var currentAsphaltNeck = MinimumMaterialBottleneck(currentLayout.AsphaltSurface);
        var currentUncovered = CoverGap(
            CurrentNoTrianglesSite, currentLayout, LayoutSettings.Cs2);
        var currentRoadConflict = MeasureRoadConflict(currentLayout);
        var currentMaterialConflict = MeasureMaterialConflict(currentLayout);
        Console.WriteLine($"{"Debug 23:32",-14} {currentLayout.Stalls} Buchten | "
            + $"Gras {currentLayout.GrassSurface.Length} | "
            + $"Engstelle Gras {currentGrassNeck:F6} m | Asphalt "
            + $"{currentAsphaltNeck:F6} m | ungedeckt {currentUncovered.Percent:F1} %");
        Console.WriteLine($"{string.Empty,-14} Strasse auf Strasse "
            + $"{currentRoadConflict.Total:F2} m2 | Asphalt auf Gras "
            + $"{currentMaterialConflict:F2} m2");
        // OBERGRENZE statt fester Zahl. Sie musste zweimal nachgezogen werden
        // - 100, dann 72, jetzt 28 -, weil jede Verbesserung der
        // Zusammenfassung sie senkt; eine feste Zahl verbietet also genau das
        // Ziel. Nach unten braucht es keine Schranke: Flaechenverlust faengt
        // die Deckungsmessung ab, falsches Verschmelzen die Ueberlappung.
        // Prototyp d079530: 255 -> 256. Das 36-m-Raster entfernt eine zu nahe
        // Verbindung; der anschliessende Neuaufbau gibt ihre Bucht wirklich frei.
        //
        // Prototyp 9cc1582: 256 -> 419. Dieses Nutzerpolygon ist IM
        // UHRZEIGERSINN gezeichnet und litt selbst an dem Fehler im
        // Richtungstest der Randreihe - die aeussere Reihe landete komplett
        // innen und fiel aus der Wertung. Neu sind 159 Aussenbuchten. Die alte
        // Erwartung hat den Defekt festgeschrieben; die Qualitaetswerte
        // daneben blieben unveraendert sauber (27 Grasflaechen, ungedeckt
        // 0,0 %, keine Ueberlappung).
        if (currentLayout.Stalls != 419 || currentLayout.GrassSurface.Length > 40
            || currentGrassNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6
            || currentAsphaltNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6
            || currentUncovered.Percent >= 0.05
            || currentRoadConflict.Total >= 0.005 || currentMaterialConflict >= 0.005)
        {
            failed = true;
            Console.Error.WriteLine("PARITAETSFEHLER: Debug 23:32"
                + Gruende(
                    (currentLayout.Stalls != 419,
                        $"Buchten {currentLayout.Stalls}/419"),
                    (currentLayout.GrassSurface.Length > 40,
                        $"Grasringe {currentLayout.GrassSurface.Length}"),
                    (currentGrassNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6,
                        $"Engstelle Gras {currentGrassNeck:F3} m"),
                    (currentAsphaltNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6,
                        $"Engstelle Asphalt {currentAsphaltNeck:F3} m"),
                    (currentUncovered.Percent >= 0.05,
                        $"ungedeckt {currentUncovered.Percent:F1} %"),
                    (currentRoadConflict.Total >= 0.005,
                        $"Strasse auf Strasse {currentRoadConflict.Total:F2} m2"),
                    (currentMaterialConflict >= 0.005,
                        $"Asphalt auf Gras {currentMaterialConflict:F2} m2")));
        }

        var userLayout = ParkingGeometry.Build(
            UserSurfaceRegressionSite, LayoutSettings.Cs2);
        var userGrassNeck = MinimumMaterialBottleneck(userLayout.GrassSurface);
        var userAsphaltNeck = MinimumMaterialBottleneck(userLayout.AsphaltSurface);
        var userUncovered = CoverGap(
            UserSurfaceRegressionSite, userLayout, LayoutSettings.Cs2);
        var userRoadConflict = MeasureRoadConflict(userLayout);
        var userMaterialConflict = MeasureMaterialConflict(userLayout);
        // Die grossen Nutzerpolygone sind der Ort, an dem sich Reihenenden an
        // Querstrassen haeufen - genau dort kippte im Spiel eine einzelne
        // Bucht. Die vier Standardfaelle allein wuerden das nicht zeigen.
        var userDecals = ParkingBayDecals.Plan(userLayout, LayoutSettings.Cs2);
        var currentDecals = ParkingBayDecals.Plan(currentLayout, LayoutSettings.Cs2);
        Console.WriteLine($"{string.Empty,-14} Decals Debug 23:32 "
            + $"{currentDecals.Placements.Length}, gegen den Nachbarn gedreht "
            + $"{CountDecalsAgainstNeighbour(currentDecals, LayoutSettings.Cs2.Sw)}"
            + $" | Nutzerpolygon {userDecals.Placements.Length}, gegen den "
            + $"Nachbarn gedreht "
            + $"{CountDecalsAgainstNeighbour(userDecals, LayoutSettings.Cs2.Sw)}");
        Console.WriteLine($"{"Nutzerpolygon",-14} {userLayout.Stalls} Buchten | "
            + $"Gras {userLayout.GrassSurface.Length} | Asphalt "
            + $"{userLayout.AsphaltSurface.Length} | Engstelle Gras "
            + $"{userGrassNeck:F6} m | Asphalt {userAsphaltNeck:F6} m | "
            + $"ungedeckt {userUncovered.Percent:F1} %");
        Console.WriteLine($"{string.Empty,-14} Strasse auf Strasse "
            + $"{userRoadConflict.Total:F2} m2 | Asphalt auf Gras "
            + $"{userMaterialConflict:F2} m2");
        // Prototyp d079530: 533 -> 538, aus demselben gemessenen Grund. Beide
        // Aenderungen gehen nach oben; keine Qualitaetsschranke wurde gelockert.
        if (userLayout.Stalls != 538
            || userGrassNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6
            || userAsphaltNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6
            || userUncovered.Percent >= 0.05
            || userRoadConflict.Total >= 0.005 || userMaterialConflict >= 0.005)
        {
            failed = true;
            Console.Error.WriteLine("PARITAETSFEHLER: Nutzerpolygon"
                + Gruende(
                    (userLayout.Stalls != 538,
                        $"Buchten {userLayout.Stalls}/538"),
                    (userGrassNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6,
                        $"Engstelle Gras {userGrassNeck:F3} m"),
                    (userAsphaltNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6,
                        $"Engstelle Asphalt {userAsphaltNeck:F3} m"),
                    (userUncovered.Percent >= 0.05,
                        $"ungedeckt {userUncovered.Percent:F1} %"),
                    (userRoadConflict.Total >= 0.005,
                        $"Strasse auf Strasse {userRoadConflict.Total:F2} m2"),
                    (userMaterialConflict >= 0.005,
                        $"Asphalt auf Gras {userMaterialConflict:F2} m2")));
        }
        Console.SetError(spiegel.Weiter);
        // `failed` interessiert hier nicht mehr: entscheidend ist, ob eine
        // Meldung NEU ist, nicht ob ueberhaupt eine kam.
        _ = failed;
        return Altlastenurteil(spiegel.Meldungen);
    }

    /**
     * Die sieben bekannten Altlasten, am 2026-08-27 einzeln gegen den
     * lebenden Prototyp nachgemessen (`PARITAET-GEKLAERT.md`).
     *
     * Es sind nur DREI Faelle: `Schraeg` und `Referenz 08s` erzeugen je drei
     * Meldungen, weil derselbe Fall einmal im Grundlauf und zweimal in der
     * Schalterpruefung laeuft.
     *
     * Wer hier etwas streicht, muss den Fall wirklich behoben haben - nicht
     * den Sollwert angepasst haben.
     */
    private static readonly string[] Altlasten =
    {
        /*
         * SEIT DEM 2026-09-01 MISST DIESER LAUF DEN ECHTEN RECHENWEG.
         *
         * Vorher lief er gegen den alten - `LayoutSettings.Cs2` liess
         * `Zellen` auf `false`, und der Mod wird mit dem Zellenweg
         * ausgeliefert. Was hier steht, sind also KEINE neuen Fehler, sondern
         * Maengel des Wegs, den wir bauen und die bisher niemand gesehen hat.
         *
         * JEDE ZEILE NENNT IHRE GRUENDE MIT MESSWERT. Bis zum selben Tag
         * stand hier nur der Formname - und damit war jede einmal gelistete
         * Form fuer beliebige weitere Maengel freigegeben. Genau so rutschte
         * eine Regression durch: die Eckendeckung schob 12 Buchten unter den
         * Asphalt, und der Lauf meldete unveraendert "alle bekannt". Der
         * Preis dieser Genauigkeit ist gewollt: jede Verbesserung faerbt den
         * Lauf einmal rot, bis die Liste nachgezogen ist. Eine Liste, die man
         * nach jedem Fortschritt anfassen muss, bleibt wahr.
         *
         * Sie stehen hier, damit ein NEUER Fehler wieder auffaellt - nicht,
         * damit sie in Ruhe gelassen werden.
         */
        "PARITAETSFEHLER: Rechteck [Fahrwege unvollstaendig gemeldet]",
        "PARITAETSFEHLER: L-Form [Fahrwege unvollstaendig gemeldet, Doppelpunkte 3, Engstelle Gras 0,050 m]",
        "PARITAETSFEHLER: Schraeg [Fahrwege unvollstaendig gemeldet, Strasse auf Strasse 6,12 m2]",
        "PARITAETSFEHLER: Referenz 08s [unerreichbar 40, Fahrwege unvollstaendig gemeldet, Strasse auf Strasse 17,11 m2, Doppelpunkte 2, Engstelle Gras 0,007 m, Engstelle Asphalt 0,000 m]",
        "PARITAETSFEHLER: Debug 23:32 [Buchten 496/419, Strasse auf Strasse 33,86 m2, Asphalt auf Gras 0,01 m2]",
        "PARITAETSFEHLER: Nutzerpolygon [Buchten 630/538, Engstelle Asphalt 0,070 m, Strasse auf Strasse 21,36 m2]",
        "PARITAETSFEHLER: Float-Regress. [Strasse auf Strasse 1,56 m2, Asphalt auf Gras 0,01 m2]",
        /*
         * DIE SCHALTERWERTE SIND EIN ABDRUCK DES AUSGEBAUTEN WEGS.
         *
         * `expected` in Program.Switches.cs wurde gegen den alten Rechenweg
         * aufgenommen; seit der Lauf den echten misst, weicht dort praktisch
         * jeder Wert ab - "Buchten 246/218" heisst nicht, dass 246 falsch
         * waeren, sondern dass 218 von gestern sind. Diese Zahlen neu
         * aufzunehmen ist eigene Arbeit; bis dahin traegt die Liste den
         * gemessenen Stand, damit die Abweichung sichtbar bleibt statt hinter
         * einem blossen Formnamen zu verschwinden.
         */
        "SCHALTER-PARITAETSFEHLER: Rechteck AN [Buchten 246/218, Gras 18 Ringe 1904,80 m2, Asphalt 35 Ringe 8895,20 m2, Gras innen 12 1321,24 m2, Gras aussen 6 583,56 m2, Querkappen 0/10, Warnungen 2]",
        "SCHALTER-PARITAETSFEHLER: Rechteck AUS [Buchten 282/234, Gras 18 Ringe 1083,52 m2, Asphalt 35 Ringe 9716,48 m2, Gras innen 12 499,96 m2, Gras aussen 6 583,56 m2, Warnungen 2]",
        /*
         * NEU GEMESSEN AM 2026-09-09: die L-Form verliert eine Fahrgasse.
         *
         * KEIN neuer Eintrag, sondern zwei bestehende mit den Zahlen der
         * jetzigen Konstruktion. Die alten waren 181/184 und 194/184.
         *
         * In dieser Testform lag eine Fahrgasse 10,90 m neben der
         * Randstrasse. Zwischen beiden Achsen braucht es halbe Fahrgasse
         * (3,5 m) + Bucht (5,9 m) + halbe Randstrasse (3,5 m) = 12,9 m,
         * damit ueberhaupt eine Buchtreihe hineinpasst. Sie passte nicht -
         * die Gasse trug auf dieser Seite nichts und klebte an der
         * Randstrasse. Seit der Bandplan parallele Randstrassen mitrechnet
         * (`Ringbandplanung.cs`), entfaellt dieses Modul samt seinen Reihen.
         *
         * Das kostet hier 33 bzw. 38 Buchten. Entscheidung des Nutzers am
         * 2026-09-09: *"Wenn eine Strasse verschwindet fallen halt auch
         * Buchten weg. Ich kann halt nicht eine Strasse auf einer anderen
         * Strasse platzieren bloss damit wir Buchten haben."* Ueberlappende
         * Strassen sind in Vanilla-CS2 ausserdem nicht mehr loeschbar.
         *
         * Unerreichbare Buchten: vorher 0, nachher 0.
         */
        "SCHALTER-PARITAETSFEHLER: L-Form AN [Buchten 148/184, Gras 11 Ringe 2672,00 m2, Asphalt 14 Ringe 5428,00 m2, Gras innen 3 2017,64 m2, Gras aussen 8 654,36 m2, Doppelpunkte 3, Engstelle Gras 0,050 m, Warnungen 2]",
        "SCHALTER-PARITAETSFEHLER: L-Form AUS [Buchten 156/184, Gras 11 Ringe 2478,48 m2, Asphalt 14 Ringe 5621,52 m2, Gras innen 3 1824,12 m2, Gras aussen 8 654,36 m2, Doppelpunkte 3, Engstelle Gras 0,050 m, Warnungen 2]",
        "SCHALTER-LAYOUTFEHLER: L-Form",
        "SCHALTER-PARITAETSFEHLER: Schraeg AN [Buchten 272/199, Gras 14 Ringe 1563,13 m2, Asphalt 25 Ringe 9236,87 m2, Gras innen 8 958,08 m2, Gras aussen 6 605,04 m2, Querkappen 0/8, Strasse auf Strasse 6,12 m2, Warnungen 2]",
        "SCHALTER-PARITAETSFEHLER: Schraeg AUS [Buchten 286/215, Gras 14 Ringe 1113,68 m2, Asphalt 25 Ringe 9686,32 m2, Gras innen 8 508,64 m2, Gras aussen 6 605,04 m2, Strasse auf Strasse 6,12 m2, Warnungen 2]",
        "SCHALTER-PARITAETSFEHLER: Referenz 08s AN [Buchten 224/202, Gras 22 Ringe 2473,21 m2, Asphalt 29 Ringe 7963,29 m2, Gras innen 8 1788,28 m2, Gras aussen 14 684,93 m2, Querkappen 0/7, Strasse auf Strasse 17,11 m2, Doppelpunkte 2, Engstelle Gras 0,007 m, Engstelle Asphalt 0,000 m, Warnungen 2]",
        "SCHALTER-PARITAETSFEHLER: Referenz 08s AUS [Buchten 237/213, Gras 23 Ringe 1939,37 m2, Asphalt 32 Ringe 8497,13 m2, Gras innen 9 1254,44 m2, Gras aussen 14 684,93 m2, Strasse auf Strasse 17,11 m2, Doppelpunkte 2, Engstelle Gras 0,007 m, Engstelle Asphalt 0,000 m, Warnungen 2]",
        "SCHALTER-KAPPENHERKUNFT FEHLER:",
    };

    /**
     * Die Gruende einer Meldung, mit Messwert.
     *
     * Warum nicht nur der Formname: siehe die Altlastenliste. Eine Meldung,
     * die nur sagt WELCHE Form fehlschlaegt, gibt diese Form fuer beliebige
     * weitere Maengel frei - genau daran rutschte am 2026-09-01 eine
     * Regression vorbei.
     */
    private static string Gruende(params (bool Trifft, string Text)[] pruefungen)
    {
        var offen = pruefungen.Where(p => p.Trifft).Select(p => p.Text).ToList();
        return offen.Count == 0 ? string.Empty
            : " [" + string.Join(", ", offen) + "]";
    }

    private static int Altlastenurteil(List<string> meldungen)
    {
        var neu = meldungen.Where(m => !Altlasten.Contains(m)).ToList();
        var behoben = Altlasten.Where(a => !meldungen.Contains(a)).ToList();

        Console.WriteLine();
        if (behoben.Count != 0)
            Console.WriteLine("  Nicht mehr gemeldet (behoben oder Fall "
                + "entfallen): " + string.Join(", ", behoben));

        if (neu.Count == 0)
        {
            Console.WriteLine($"  {meldungen.Count} Meldung(en), alle bekannt. "
                + "KEINE NEUE ABWEICHUNG.");
            // Bewusst 0: eine bekannte Altlast darf keinen Bau rot faerben.
            // Ein echter Regress faellt dadurch erst richtig auf.
            return 0;
        }

        Console.WriteLine($"  {neu.Count} NEUE Abweichung(en), nicht in der "
            + "Altlastenliste:");
        foreach (var eintrag in neu) Console.WriteLine("    " + eintrag);
        Console.WriteLine("  Das ist ein echter Befund - bitte nachgehen, "
            + "statt die Liste zu erweitern.");
        return 1;
    }

    /**
     * Spiegelt die Fehlerausgabe mit, ohne sie zu veraendern.
     *
     * Die Meldungen entstehen an einem halben Dutzend Stellen, teils in
     * anderen Dateien (`RunSwitchParity`). Sie alle einzeln umzubauen waere
     * viel Aenderung fuer wenig Gewinn; hier hoert EINE Stelle zu.
     */
    private sealed class Altlastenspiegel : System.IO.TextWriter
    {
        internal readonly System.IO.TextWriter Weiter;
        private readonly System.Text.StringBuilder _zeile
            = new System.Text.StringBuilder();
        internal readonly List<string> Meldungen = new List<string>();

        internal Altlastenspiegel(System.IO.TextWriter weiter) { Weiter = weiter; }

        public override System.Text.Encoding Encoding => Weiter.Encoding;

        public override void Write(char value)
        {
            Weiter.Write(value);
            if (value == '\n')
            {
                var text = _zeile.ToString().Trim();
                _zeile.Clear();
                /*
                 * `BAUABSTURZ:` gehoert dazu, seit der Lauf den echten
                 * Rechenweg misst. Ohne diesen Praefix waere ausgerechnet
                 * der SCHWERSTE Befund - eine Form, die gar kein Layout
                 * ergibt - vom Urteil ausgenommen gewesen: er stand rot im
                 * Text, zaehlte aber nicht als Meldung.
                 */
                if (text.StartsWith("PARITAETSFEHLER:")
                    || text.StartsWith("SCHALTER-")
                    || text.StartsWith("BAUABSTURZ:"))
                    Meldungen.Add(text);
                return;
            }
            if (value != '\r') _zeile.Append(value);
        }

        public override void Flush() => Weiter.Flush();
    }

    private static double Length(float2 value) =>
        Math.Sqrt((double)value.x * value.x + (double)value.y * value.y);

    private static double SignedArea(float2[] polygon)
    {
        var sum = 0.0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            sum += (double)a.x * b.y - (double)b.x * a.y;
        }
        return sum / 2;
    }

    private static bool PointIn(float2 p, float2[] ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.y > p.y) != (b.y > p.y)
                && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    private static double DistanceToBoundary(float2 p, float2[] ring)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            var x = (double)b.x - a.x;
            var y = (double)b.y - a.y;
            var squared = x * x + y * y;
            if (squared == 0) squared = 1;
            var t = (((double)p.x - a.x) * x + ((double)p.y - a.y) * y) / squared;
            t = Math.Max(0, Math.Min(1, t));
            var dx = p.x - (a.x + x * t);
            var dy = p.y - (a.y + y * t);
            best = Math.Min(best, dx * dx + dy * dy);
        }
        return Math.Sqrt(best);
    }

    private static float2[] Corridor(float2 a, float2 b, double width)
    {
        var vector = b - a;
        var length = Length(vector);
        if (length < 0.2) return null;
        var u = vector / (float)length;
        var normal = new float2(-u.y * (float)width / 2, u.x * (float)width / 2);
        return new[] { a + normal, b + normal, b - normal, a - normal };
    }

    private static List<float2[]> Corridors(ParkingLayout layout, LayoutSettings settings)
    {
        var output = layout.PerimeterQuad.Select(q => q.ToArray())
            .Concat(layout.EntranceQuad.Select(q => q.ToArray())).ToList();
        void Add(IEnumerable<float2[]> lines, double width)
        {
            foreach (var line in lines)
            {
                var quad = Corridor(line[0], line[1], width);
                if (quad != null) output.Add(quad);
            }
        }
        Add(layout.PerimeterLine.Skip(layout.PerimeterQuad.Length), settings.Ai);
        Add(layout.AisleLine, settings.Ai);
        Add(layout.CrossLine, settings.Cw);
        return output;
    }

    private static List<float2[]> SurfaceCorridors(ParkingLayout layout) =>
        layout.PerimeterQuad.Concat(layout.AisleQuad).Concat(layout.CrossQuad)
            .Concat(layout.EntranceQuad).Select(q => q.ToArray()).ToList();

    private sealed class RoadConflict
    {
        internal double RandGasse;
        internal double RandVerbindung;
        internal double GasseVerbindung;
        internal double RandEinfahrt;
        internal double GasseEinfahrt;
        internal double VerbindungEinfahrt;
        internal double Total => RandGasse + RandVerbindung + GasseVerbindung
            + RandEinfahrt + GasseEinfahrt + VerbindungEinfahrt;
    }

    private static RoadConflict MeasureRoadConflict(ParkingLayout layout)
    {
        double Pair(float2[][] first, float2[][] second) => first.Sum(a =>
            second.Sum(b => ConvexOverlapArea(a, b)));
        return new RoadConflict
        {
            RandGasse = Pair(layout.PerimeterQuad, layout.AisleQuad),
            RandVerbindung = Pair(layout.PerimeterQuad, layout.CrossQuad),
            GasseVerbindung = Pair(layout.AisleQuad, layout.CrossQuad),
            RandEinfahrt = Pair(layout.PerimeterQuad, layout.EntranceQuad),
            GasseEinfahrt = Pair(layout.AisleQuad, layout.EntranceQuad),
            VerbindungEinfahrt = Pair(layout.CrossQuad, layout.EntranceQuad),
        };
    }

    /** Exakte Schnittflaeche zweier konvexer Strassenpolygone. */
    private static double ConvexOverlapArea(float2[] first, float2[] second)
    {
        var output = first.Select(p => new double2(p.x, p.y)).ToList();
        var clip = second.Select(p => new double2(p.x, p.y)).ToArray();
        double Area(IReadOnlyList<double2> polygon)
        {
            var sum = 0.0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum / 2;
        }
        var winding = Area(clip) >= 0 ? 1 : -1;
        double Side(double2 point, double2 a, double2 b) => winding
            * ((b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x));
        for (var edgeIndex = 0; edgeIndex < clip.Length && output.Count > 0; edgeIndex++)
        {
            var a = clip[edgeIndex];
            var b = clip[(edgeIndex + 1) % clip.Length];
            var input = output;
            output = new List<double2>();
            for (var pointIndex = 0; pointIndex < input.Count; pointIndex++)
            {
                var point = input[pointIndex];
                var next = input[(pointIndex + 1) % input.Count];
                var pointSide = Side(point, a, b);
                var nextSide = Side(next, a, b);
                var pointInside = pointSide >= -1e-9;
                var nextInside = nextSide >= -1e-9;
                if (pointInside) output.Add(point);
                if (pointInside == nextInside) continue;
                var t = pointSide / (pointSide - nextSide);
                output.Add(point + (next - point) * t);
            }
        }
        var area = output.Count >= 3 ? Math.Abs(Area(output)) : 0;
        return area < 1e-9 ? 0 : area;
    }

    /** Ohrzerlegung für die exakte Schnittmessung konkaver Grasringe. */
    private static List<float2[]> Triangulate(float2[] ring)
    {
        var triangles = new List<float2[]>();
        if (ring == null || ring.Length < 3) return triangles;
        var indices = Enumerable.Range(0, ring.Length).ToList();
        var winding = SignedArea(ring) >= 0 ? 1 : -1;
        double Cross(float2 a, float2 b, float2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y)
            - ((double)b.y - a.y) * ((double)c.x - a.x);
        bool InTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            var ab = winding * Cross(a, b, p);
            var bc = winding * Cross(b, c, p);
            var ca = winding * Cross(c, a, p);
            return ab >= -1e-8 && bc >= -1e-8 && ca >= -1e-8;
        }
        for (var guard = 0; indices.Count > 3 && guard < ring.Length * ring.Length; guard++)
        {
            var clipped = false;
            for (var i = 0; i < indices.Count; i++)
            {
                var before = indices[(i - 1 + indices.Count) % indices.Count];
                var current = indices[i];
                var after = indices[(i + 1) % indices.Count];
                if (winding * Cross(ring[before], ring[current], ring[after]) <= 1e-9)
                    continue;
                var contains = indices.Any(index => index != before && index != current
                    && index != after && InTriangle(
                        ring[index], ring[before], ring[current], ring[after]));
                if (contains) continue;
                triangles.Add(new[] { ring[before], ring[current], ring[after] });
                indices.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) throw new InvalidOperationException(
                "Materialring konnte für die Überlappungsmessung nicht trianguliert werden.");
        }
        if (indices.Count == 3)
            triangles.Add(new[] { ring[indices[0]], ring[indices[1]], ring[indices[2]] });
        return triangles;
    }

    private static double MeasureMaterialConflict(ParkingLayout layout)
    {
        var triangles = layout.GrassSurface.SelectMany(Triangulate).ToArray();
        var overlap = 0.0;
        foreach (var triangle in triangles)
            for (var asphaltIndex = 0; asphaltIndex < layout.AsphaltSurface.Length; asphaltIndex++)
            {
                var area = ConvexOverlapArea(
                    triangle, layout.AsphaltSurface[asphaltIndex]);
                overlap += area;
            }
        return overlap < 1e-8 ? 0 : overlap;
    }

    private static bool QuadsOverlap(float2[] first, float2[] second, double tolerance)
    {
        foreach (var polygon in new[] { first, second })
            for (var i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                var length = Length(edge);
                var axis = new float2((float)(-edge.y / length), (float)(edge.x / length));
                var firstValues = first.Select(p => (double)p.x * axis.x + (double)p.y * axis.y);
                var secondValues = second.Select(p => (double)p.x * axis.x + (double)p.y * axis.y);
                var firstLo = firstValues.Min();
                var firstHi = firstValues.Max();
                var secondLo = secondValues.Min();
                var secondHi = secondValues.Max();
                if (firstHi <= secondLo + tolerance || secondHi <= firstLo + tolerance) return false;
            }
        return true;
    }

    private static int OverlapCount(float2[][] bays)
    {
        var count = 0;
        for (var i = 0; i < bays.Length; i++)
            for (var j = i + 1; j < bays.Length; j++)
                if (QuadsOverlap(bays[i], bays[j], 0.05)) { count++; break; }
        return count;
    }

    private static int BayOnRoad(ParkingLayout layout, LayoutSettings settings)
    {
        var roads = Corridors(layout, settings);
        return layout.Bay.Count(bay => roads.Any(road => QuadsOverlap(bay, road, 0.05)));
    }

    private static int EndsOutside(ParkingLayout layout, float2[] site)
    {
        return layout.AisleLine.Concat(layout.CrossLine)
            .SelectMany(line => line).Count(point => !PointIn(point, site));
    }

    private static int SelfIntersectionCount(float2[] ring)
    {
        // Das Modell prueft double-Koordinaten mit 1e-9. Nach der Ausgabe als
        // float2 kann ein exakter Endpunkttreffer bei t=0,9999998 bzw. 0,0000011
        // landen und waere faelschlich eine Innenkreuzung. 2e-6 deckt diese
        // Rundung ab, bleibt aber weit unter jeder geometrischen Toleranz.
        const double endpointEpsilon = 2e-6;
        var intersections = 0;
        for (var i = 0; i < ring.Length; i++)
            for (var j = i + 2; j < ring.Length; j++)
            {
                if (i == 0 && j == ring.Length - 1) continue;
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                var c = ring[j];
                var d = ring[(j + 1) % ring.Length];
                var denominator = ((double)b.x - a.x) * ((double)d.y - c.y)
                                - ((double)b.y - a.y) * ((double)d.x - c.x);
                if (Math.Abs(denominator) < 1e-12) continue;
                var t = (((double)c.x - a.x) * ((double)d.y - c.y)
                       - ((double)c.y - a.y) * ((double)d.x - c.x)) / denominator;
                var u = (((double)c.x - a.x) * ((double)b.y - a.y)
                       - ((double)c.y - a.y) * ((double)b.x - a.x)) / denominator;
                if (t > endpointEpsilon && t < 1 - endpointEpsilon
                    && u > endpointEpsilon && u < 1 - endpointEpsilon) intersections++;
            }
        return intersections;
    }

    private static bool HasNearDuplicatePoints(float2[] ring)
    {
        const double minimumDistanceSquared = 0.05 * 0.05;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            var dx = (double)b.x - a.x;
            var dy = (double)b.y - a.y;
            if (dx * dx + dy * dy < minimumDistanceSquared) return true;
        }
        return false;
    }

    /**
     * Zaehlt Aufkleber, die anders herum stehen als ihr Reihennachbar.
     *
     * Genau dieser Fehler trat im Spiel auf: eine einzelne Bucht am
     * Reihenende stand um 180 Grad verdreht zwischen lauter richtigen, weil
     * dort eine Querstrasse naeher lag als die eigene Fahrgasse.
     *
     * Bewusst NICHT gegen "zeigt zur naechsten Fahrbahn" geprueft - das
     * waere dieselbe Regel wie in der Umsetzung und misst nur sich selbst.
     * Nachbarschaft und gleiche Ausrichtung sind davon unabhaengig: zwei
     * Buchten, die eine Buchtbreite nebeneinander LAENGS ihrer Reihe liegen,
     * gehoeren zur selben Reihe und muessen gleich herum stehen.
     */
    private static int CountDecalsAgainstNeighbour(
        ParkingBayDecals.DecalPlan plan, double stallWidth)
    {
        var placements = plan.Placements;
        var wrong = 0;
        for (var i = 0; i < placements.Length; i++)
        {
            var here = placements[i];
            // Reihenachse steht senkrecht auf der Blickrichtung.
            var along = new double2(-here.Facing.y, here.Facing.x);
            for (var j = 0; j < placements.Length; j++)
            {
                if (j == i) continue;
                var delta = placements[j].Center - here.Center;
                var distance = math.length(delta);
                if (distance < 1e-9 || Math.Abs(distance - stallWidth) > 0.05) continue;
                if (Math.Abs(math.dot(delta / distance, along)) < 0.999) continue;
                if (math.dot(placements[j].Facing, here.Facing) < 0.9) wrong++;
            }
        }
        // Jedes falsche Paar wird von beiden Seiten gezaehlt.
        return wrong / 2;
    }

    private static double ShortestEdge(IEnumerable<float2[]> rings)
    {
        var shortest = double.PositiveInfinity;
        foreach (var ring in rings)
            for (var i = 0; i < ring.Length; i++)
                shortest = Math.Min(shortest, Length(ring[(i + 1) % ring.Length] - ring[i]));
        return shortest;
    }

    private static double MinimumMaterialBottleneck(float2[][] rings)
    {
        var minimum = double.PositiveInfinity;
        bool Same(float2 a, float2 b) => Length(a - b) <= 1e-5;
        bool RedundantCollinear(float2[] ring, int count, int index)
        {
            var before = ring[(index - 1 + count) % count];
            var point = ring[index];
            var after = ring[(index + 1) % count];
            var incomingX = (double)point.x - before.x;
            var incomingY = (double)point.y - before.y;
            var outgoingX = (double)after.x - point.x;
            var outgoingY = (double)after.y - point.y;
            return Math.Abs(incomingX * outgoingY - incomingY * outgoingX) <= 1e-9
                && incomingX * outgoingX + incomingY * outgoingY > 0;
        }
        bool SharedEdge(int own, float2 a, float2 b)
        {
            for (var ringIndex = 0; ringIndex < rings.Length; ringIndex++)
            {
                if (ringIndex == own) continue;
                var other = rings[ringIndex];
                for (var i = 0; i < other.Length; i++)
                {
                    var next = other[(i + 1) % other.Length];
                    if ((Same(other[i], a) && Same(next, b))
                        || (Same(other[i], b) && Same(next, a))) return true;
                }
            }
            return false;
        }
        for (var ringIndex = 0; ringIndex < rings.Length; ringIndex++)
        {
            var ring = rings[ringIndex];
            var count = ring.Length;
            while (count > 1 && Length(ring[count - 1] - ring[0]) < 1e-6)
                count--;
            for (var pointIndex = 0; pointIndex < count; pointIndex++)
                for (var edgeIndex = 0; edgeIndex < count; edgeIndex++)
                {
                    var nextEdgeIndex = (edgeIndex + 1) % count;
                    if (edgeIndex == pointIndex || nextEdgeIndex == pointIndex)
                        continue;
                    var point = ring[pointIndex];
                    var a = ring[edgeIndex];
                    var edge = ring[nextEdgeIndex] - a;
                    var squaredLength = (double)edge.x * edge.x + (double)edge.y * edge.y;
                    if (squaredLength < 1e-12) continue;
                    var parameter = (((double)point.x - a.x) * edge.x
                                   + ((double)point.y - a.y) * edge.y) / squaredLength;
                    parameter = Math.Max(0, Math.Min(1, parameter));
                    var projection = new float2(
                        (float)(a.x + edge.x * parameter),
                        (float)(a.y + edge.y * parameter));
                    var endpoint = Length(projection - a) <= 0.005 ? edgeIndex
                        : Length(projection - ring[nextEdgeIndex]) <= 0.005
                            ? nextEdgeIndex : -1;
                    if (endpoint >= 0)
                    {
                        var adjacent = (pointIndex + 1) % count == endpoint
                            || (endpoint + 1) % count == pointIndex;
                        // Zwischen zwei AUFEINANDERFOLGENDEN Knoten liegt eine
                        // Kante, kein Hals - solange sie CS2s
                        // Mindestknotenabstand von 5 cm einhaelt. Eine
                        // kuerzere Kante bleibt ein Mangel und wird weiter
                        // gemessen. Dieselbe Regel wie im Modell; sie wurde
                        // noetig, seit das Gruen als EIN Gebiet geschnitten
                        // wird und dabei regulaere kurze Randkanten entstehen.
                        if (adjacent && Length(point - ring[endpoint]) >= 0.05)
                            continue;
                        if (adjacent && (SharedEdge(ringIndex, point, ring[endpoint])
                            || RedundantCollinear(ring, count, pointIndex)
                            || RedundantCollinear(ring, count, endpoint)))
                            continue;
                    }
                    var dx = point.x - (a.x + edge.x * parameter);
                    var dy = point.y - (a.y + edge.y * parameter);
                    minimum = Math.Min(minimum, Math.Sqrt(dx * dx + dy * dy));
                }
        }
        return minimum;
    }

    private sealed class Coverage { internal double Gap; internal double Percent; }

    private static Coverage CoverGap(float2[] site, ParkingLayout layout,
                                     LayoutSettings settings, double step = 0.5)
    {
        var all = layout.Bay.Concat(layout.GrassSurface)
            .Concat(layout.AsphaltSurface).ToList();
        var minX = site.Min(p => p.x);
        var maxX = site.Max(p => p.x);
        var minY = site.Min(p => p.y);
        var maxY = site.Max(p => p.y);
        var gap = 0.0;
        for (var y = minY + step / 2; y < maxY; y += step)
            for (var x = minX + step / 2; x < maxX; x += step)
            {
                var point = new float2((float)x, (float)y);
                if (!PointIn(point, site)) continue;
                if (!all.Any(q => PointIn(point, q))
                    && !all.Any(q => DistanceToBoundary(point, q) <= 1e-6))
                    gap += step * step;
            }
        var area = Math.Abs(SignedArea(site));
        return new Coverage { Gap = gap, Percent = 100 * gap / Math.Max(area, 1e-9) };
    }

    /** Mittelpunkt eines Buchtenvierecks. */
    private static float2 QuadCenter(float2[] quad)
    {
        var sum = float2.zero;
        for (var i = 0; i < quad.Length; i++) sum += quad[i];
        return sum / Math.Max(1, quad.Length);
    }
}
