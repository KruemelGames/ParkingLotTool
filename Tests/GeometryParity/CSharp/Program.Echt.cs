using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/// <summary>
/// DIE EINSTELLUNGEN, MIT DENEN DER MOD WIRKLICH LAEUFT.
///
/// Der uebrige Parity-Test faehrt ausschliesslich `LayoutSettings.Cs2` - in
/// `sollwerte.json` steht das sogar als Hinweis: "CS2_SETTINGS UNVERAENDERT,
/// also auto:true". Damit prueft er den einen Zustand, in dem der Mod NIE
/// laeuft: das UI setzt immer `AngleMode = "edge"`, und der Nutzer verstellt
/// Fahrgassenbreite und Verbindungsabstand.
///
/// Genau in dieser Luecke ist am 2026-08-12 ein Fehler durchgerutscht: der
/// Nutzer meldete "es wurde NIRGENDS Asphalt platziert", waehrend der
/// Parity-Lauf 0 Unterschiede meldete. Ursache ist eine Weiche, die es nur im
/// Prototyp gibt - `calibrated` in `finalizeMaterialSurfaces`. Ist sie falsch,
/// nimmt das JS-Modell einen Rueckfallweg (Asphalt = die Fahrbahn-Vierecke),
/// waehrend der Mod immer die strikte Pipeline faehrt. Bei abweichender
/// Fahrgassenbreite oder gesetztem Winkelmodus rechnen beide Seiten also
/// verschiedene Flaechen aus, ohne dass es jemandem auffiel.
///
/// Diese Datei gibt die Flaechenzahlen fuer dieselben Faelle aus, die das
/// Node-Skript `parity-echt.cjs` im Prototyp liefert. Wer beide Ausgaben
/// gegeneinander diffed, sieht die Luecke.
/// </summary>
internal static partial class Program
{
    internal static readonly (string Name, float2[] Site, bool NurCs2Masse)[] EchtSites =
        BuildEchtSites();

    /**
     * Zaehlt je Fahrweg-Ende, ob es mit einem fremden ENDPUNKT zusammen-
     * faellt (CS2 verschmilzt dann zu EINEM Knoten - das ist die Kreuzung),
     * ob es MITTEN auf einer fremden Kante liegt (CS2 teilt dort nicht,
     * also kein Knoten) oder ob es voellig FREI haengt.
     *
     * Vor dem Fix vom 2026-08-16 hingen im Nutzerbau 20 von 32 Enden frei,
     * jedes exakt Ai/2 = 3,50 m neben seinem Ziel.
     */
    private static (int Stuecke, int Verschmolzen, int Mittendrin, int Frei,
        int UnerlaubtFrei) NetzBefund(ParkingLayout layout)
    {
        const double eps = 0.05;
        var netz = layout.NetLine;
        double Abstand(float2 a, float2 b) =>
            Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
        double ZuStrecke(float2 p, float2 a, float2 b, out double t)
        {
            double vx = b.x - a.x, vy = b.y - a.y, l2 = vx * vx + vy * vy;
            if (l2 < 1e-12) { t = 0; return Abstand(p, a); }
            t = Math.Max(0, Math.Min(1, ((p.x - a.x) * vx + (p.y - a.y) * vy) / l2));
            return Math.Sqrt(Math.Pow(p.x - (a.x + t * vx), 2)
                + Math.Pow(p.y - (a.y + t * vy), 2));
        }
        int verschmolzen = 0, mittendrin = 0, frei = 0, unerlaubt = 0;
        for (var i = 0; i < netz.Length; i++)
        {
            foreach (var p in new[] { netz[i].A, netz[i].B })
            {
                var amEnde = false;
                for (var j = 0; j < netz.Length && !amEnde; j++)
                {
                    if (j == i) continue;
                    if (Abstand(p, netz[j].A) <= eps || Abstand(p, netz[j].B) <= eps)
                        amEnde = true;
                }
                if (amEnde) { verschmolzen++; continue; }
                var innen = false;
                for (var j = 0; j < netz.Length && !innen; j++)
                {
                    if (j == i) continue;
                    var d = ZuStrecke(p, netz[j].A, netz[j].B, out var t);
                    var laenge = Abstand(netz[j].A, netz[j].B);
                    if (t * laenge <= eps || (1 - t) * laenge <= eps) continue;
                    if (d <= 0.6) innen = true;
                }
                if (innen) { mittendrin++; continue; }
                frei++;
                // Nur das AEUSSERE Zufahrtsende darf frei bleiben.
                if (!string.Equals(netz[i].Kind, "entrance", StringComparison.Ordinal))
                    unerlaubt++;
            }
        }
        return (netz.Length, verschmolzen, mittendrin, frei, unerlaubt);
    }

    private static (string, float2[], bool)[] BuildEchtSites()
    {
        var liste = Cases.Select(c => (c.Name, c.Site, false)).ToList();
        // Nutzerpolygon aus dem Bericht vom 2026-08-12 22:43, Bau PLT-90E34B07.
        // Genau der Bau, bei dem im Spiel kein Asphalt lag.
        liste.Add(("PLT-90E34B07", new[]
        {
            new float2(-1168.40942f, 84.05939f),
            new float2(-1047.90479f, 91.51279f),
            new float2(-1055.71f, 217.635834f),
            new float2(-1115.85571f, 213.913513f),
            new float2(-1119.29016f, 272.996826f),
            new float2(-1179.1897f, 269.514984f),
        }, false));
        // Vollstaendige float-Koordinaten aus PLT-94305602. Gerundete Werte
        // erzeugen ein anderes Layout, in dem die 11 unerreichbaren Buchten
        // des Ausgangsstands nicht vorkommen.
        liste.Add(("PLT-94305602", new[]
        {
            new float2(-1043.9124755859375f, 91.7597427368164f),
            new float2(-1049.6221923828125f, 184.02146911621094f),
            new float2(-1087.1756591796875f, 181.698486328125f),
            new float2(-1093.0277099609375f, 276.2612609863281f),
            new float2(-1179.2716064453125f, 270.9263916015625f),
            new float2(-1168.418212890625f, 84.058837890625f),
        }, false));
        // Verbindungsabstand PLT-75B80478. Jeder Dezimalwert ist die volle
        // Genauigkeit des aus CS2 gelesenen float; gerundete Punkte erzeugen
        // ein anderes Layout und taugen nicht als Regressionstest.
        liste.Add(("PLT-75B80478", new[]
        {
            new float2(-1043.9124755859375f, 91.7597427368164f),
            new float2(-1168.409423828125f, 84.05939483642578f),
            new float2(-1178.9576416015625f, 265.52178955078125f),
            new float2(-1106.7637939453125f, 269.71942138671875f),
            new float2(-1100.939453125f, 169.4395294189453f),
            new float2(-1048.906982421875f, 172.464111328125f),
        }, false));
        // Bau PLT-BEA5AC77 vom 2026-08-13 mit VOLLER float-Genauigkeit. Im
        // gemeldeten edge-Lauf blieben 1 046/2 251 Rasterpunkte leer (46,5 %),
        // weil der 24 167-m2-Teil keinen Lauf bekam. Soll nach dem Fix:
        // 816/2 251 = 36,3 %.
        liste.Add(("PLT-BEA5AC77", new[]
        {
            new float2(-1184.3790283203125f, 83.07158660888672f),
            new float2(-1304.1810302734375f, 75.66153717041016f),
            new float2(-1297.2183837890625f, -36.895328521728516f),
            new float2(-1402.922119140625f, -43.43232345581055f),
            new float2(-1416.48876953125f, 175.7889404296875f),
            new float2(-1190.5804443359375f, 189.7596893310547f),
        }, true));
        // Bau PLT-C213C77A vom 2026-08-13 mit der vollen Genauigkeit der aus
        // CS2 gelesenen float-Werte. Vorher: edge 308 Buchten, Teilrichtungen
        // 93,326908/3,539319 Grad und 76 unerreichbare Buchten. Gerundete
        // Koordinaten erzeugen ein anderes Layout und taugen hier nicht.
        liste.Add(("PLT-C213C77A", new[]
        {
            new float2(-1168.4093017578125f, 84.05941772460938f),
            new float2(-1043.912353515625f, 91.75973510742188f),
            new float2(-1049.8638916015625f, 187.93017578125f),
            new float2(-1117.94482421875f, 180.91973876953125f),
            new float2(-1123.0472412109375f, 268.7716979980469f),
            new float2(-1178.9580078125f, 265.5242614746094f),
        }, true));
        return liste.ToArray();
    }

    /// <summary>Die Einstellungsvarianten, die im Spiel wirklich vorkommen.</summary>
    private static IEnumerable<(string Name, LayoutSettings S)> EchtSettings()
    {
        var standard = LayoutSettings.Cs2;
        yield return ("Standard          ", standard);

        var edge = LayoutSettings.Cs2;
        edge.AngleMode = "edge";
        edge.Auto = false;
        yield return ("UI-Standard edge  ", edge);

        var schmal = LayoutSettings.Cs2;
        schmal.AngleMode = "edge";
        schmal.Auto = false;
        schmal.Ai = 6;
        schmal.Cr = 62;
        yield return ("edge, ai 6, cr 62 ", schmal);
    }

    internal static void RunEcht()
    {
        var autoExpected = new Dictionary<string, (int Stalls, int Routes, int Unserved)>
        {
            ["Rechteck"] = (218, 1, 0),
            ["L-Form"] = (184, 0, 0),
            ["Schraeg"] = (199, 1, 0),
            ["Referenz 08s"] = (202, 1, 0),
            ["PLT-90E34B07"] = (393, 3, 0),
            ["PLT-94305602"] = (384, 2, 0),
            ["PLT-75B80478"] = (161, 1, 0),
            ["PLT-BEA5AC77"] = (526, 4, 0),
            ["PLT-C213C77A"] = (313, 2, 0),
        };
        var edgeExpected = new Dictionary<string, (int Stalls, int Routes, int Unserved)>
        {
            ["Rechteck"] = (230, 2, 0),
            ["L-Form"] = (184, 0, 0),
            ["Schraeg"] = (200, 2, 0),
            ["Referenz 08s"] = (209, 2, 1),
            ["PLT-90E34B07"] = (407, 2, 0),
            ["PLT-94305602"] = (466, 2, 0),
            ["PLT-75B80478"] = (204, 4, 0),
            ["PLT-BEA5AC77"] = (528, 5, 0),
            ["PLT-C213C77A"] = (334, 2, 0),
        };
        Console.WriteLine();
        Console.WriteLine("Flaechen unter den Einstellungen, mit denen der Mod laeuft:");
        Console.WriteLine("  (gegen `dotnet run -- --echt` im Mod diffen; die ms-Spalte");
        Console.WriteLine("   vorher herausfiltern, die weicht immer ab)");
        foreach (var (name, S) in EchtSettings())
            foreach (var (fall, site, nurCs2Masse) in EchtSites)
            {
                // Fuer PLT-BEA5AC77 gelten Reproduktion und Abnahme nur fuer
                // den exakten CS2-Satz; ai=6/cr=62 ist ein anderes Modul.
                if (nurCs2Masse
                    && (S.Ai != LayoutSettings.Cs2.Ai || S.Cr != LayoutSettings.Cs2.Cr))
                    continue;
                var watch = Stopwatch.StartNew();
                var layout = ParkingGeometry.Build(site, S);
                watch.Stop();
                var unserved = UnservedBays(layout);
                var empty = EmptyShare(site, layout);
                var routeSpacing = ParkingGeometry.CrossRouteSpacing(layout.CrossRouteLine);
                var routeSpacingText = double.IsPositiveInfinity(routeSpacing)
                    ? "nur eine"
                    : routeSpacing.ToString("F2", CultureInfo.InvariantCulture) + " m";
                var emptyPercent = (empty.Share * 100)
                    .ToString("F1", CultureInfo.InvariantCulture).PadLeft(4);
                var directions = fall == "PLT-C213C77A"
                    ? " | Richtungen " + string.Join(", ", layout.PassInfo
                        .OrderBy(info => info.PartIndex)
                        .Select(info => $"Teil {info.PartIndex + 1} "
                            + $"{info.Deg.ToString("F6", CultureInfo.InvariantCulture)} deg"))
                    : "";
                var netz = NetzBefund(layout);
                Console.WriteLine(
                    $"  {name} {fall,-13} {layout.Stalls,4} Buchten | "
                    + $"unerreichbar {unserved,2} | {watch.ElapsedMilliseconds,5} ms | "
                    + $"Fahrgassen {layout.Aisles,2} | "
                    + $"Verbindungen {layout.CrossRouteLine.Length,2} | "
                    + $"kleinster Abstand {routeSpacingText,8} | "
                    + $"leer {emptyPercent} % ({empty.Empty}/{empty.Total}) | "
                    + $"Teile {layout.Parts} / Laeufe {layout.PassInfo.Length} / "
                    + $"Halb {layout.PassInfo.Count(info => info.Half)} | "
                    + $"Gras {layout.GrassSurface.Length,3} / {Flaeche(layout.GrassSurface),9} m2 | "
                    + $"Belag {layout.AsphaltSurface.Length,3} / {Flaeche(layout.AsphaltSurface),9} m2 | "
                    + $"kuerzeste Kante Belag {KuerzesteKante(layout.AsphaltSurface)} | "
                    + $"Netz {netz.Stuecke,3} / {netz.Verschmolzen,3} verschmolzen / "
                    + $"{netz.Mittendrin,2} mittendrin / {netz.Frei,2} frei"
                    + directions);

                // Ein freies Ende INNEN heisst: dort kann kein Auto abbiegen.
                // Erlaubt ist nur das aeussere Zufahrtsende, das per
                // Road->Pathway-LocalConnect an die Stadtstrasse geht.
                if (netz.UnerlaubtFrei > 0)
                    throw new InvalidOperationException(
                        $"{name} {fall}: {netz.UnerlaubtFrei} Fahrweg-Enden haengen frei, "
                        + "dort entsteht keine Kreuzung.");
                if (netz.Mittendrin > 0)
                    throw new InvalidOperationException(
                        $"{name} {fall}: {netz.Mittendrin} Enden liegen MITTEN auf einer "
                        + "fremden Kante; CS2 teilt dort nicht und legt keinen Knoten an.");

                if (!double.IsPositiveInfinity(routeSpacing)
                    && routeSpacing < S.Cr - 0.001)
                    throw new InvalidOperationException(
                        $"{name.Trim()} {fall}: gleichgerichtete Verbindungen liegen nur "
                        + $"{routeSpacing:F2} m auseinander (Soll >= {S.Cr:F2} m).");

                var mode = name.Trim();
                var expectations = mode == "Standard" ? autoExpected
                    : mode == "UI-Standard edge" ? edgeExpected : null;
                if (expectations != null && expectations.TryGetValue(fall, out var expected)
                    && (layout.Stalls != expected.Stalls
                        || layout.CrossRouteLine.Length != expected.Routes
                        || unserved != expected.Unserved))
                    throw new InvalidOperationException(
                        $"{mode} {fall}: {layout.Stalls} Buchten, "
                        + $"{layout.CrossRouteLine.Length} Verbindungen, "
                        + $"{unserved} unerreichbar; erwartet {expected.Stalls}/"
                        + $"{expected.Routes}/{expected.Unserved}.");

                // PLT-C213C77A muss in auto UND edge ein gemeinsames Raster
                // erhalten. Gemessen: auto 313 Buchten bei 95 Grad, edge 334
                // bei 93,326908 Grad; jeweils 3 Gassen, 2 Verbindungen und
                // 0 unerreichbare Buchten. Nur die Buchtenzahl abzunehmen
                // wuerde den alten edge-Fehler mit 76 unerreichbaren uebersehen.
                if ((mode == "Standard" || mode == "UI-Standard edge")
                    && fall == "PLT-C213C77A")
                {
                    var parts = layout.PassInfo.OrderBy(info => info.PartIndex).ToArray();
                    var expectedDirection = mode == "Standard" ? 95 : 93.326908;
                    var directionGap = 0.0;
                    for (var first = 0; first < parts.Length; first++)
                        for (var second = 0; second < first; second++)
                        {
                            var raw = Math.Abs(parts[first].Deg - parts[second].Deg) % 180;
                            directionGap = Math.Max(directionGap, Math.Min(raw, 180 - raw));
                        }
                    var bayOverlap = OverlapCount(layout.Bay);
                    var bayOnRoad = BayOnRoad(layout, S);
                    var outside = EndsOutside(layout, site);
                    var uncovered = CoverGap(site, layout, S).Percent;
                    var roadOverlap = MeasureRoadConflict(layout).Total;
                    var directionsCorrect = parts.Length == 2
                        && parts.All(info =>
                            Math.Abs(info.Deg - expectedDirection) <= 0.00001);
                    if (layout.Aisles != 3 || layout.CrossRouteLine.Length != 2
                        || unserved != 0 || !directionsCorrect || directionGap > 0.01
                        || bayOverlap != 0 || bayOnRoad != 0 || outside != 0
                        || uncovered >= 0.05 || roadOverlap >= 0.005)
                        throw new InvalidOperationException(
                            $"PLT-C213C77A {mode} weicht von der Richtungsabnahme ab: "
                            + $"{layout.Stalls} Buchten, {layout.Aisles} Gassen, "
                            + $"{layout.CrossRouteLine.Length} Verbindungen, "
                            + $"{unserved} unerreichbar, Richtungsabstand "
                            + $"{directionGap:F6} Grad, Qualitaet {bayOverlap}/"
                            + $"{bayOnRoad}/{outside}/{uncovered:F1}/{roadOverlap:F3}.");
                }

                // Gemeldeter edge-Nutzerfall mit voller float-Genauigkeit.
                // Prototyp 38bd47c: 466/0, zwei Teile und kein Halbmodul.
                if (name.Trim() == "UI-Standard edge" && fall == "PLT-94305602")
                {
                    var bayOverlap = OverlapCount(layout.Bay);
                    var bayOnRoad = BayOnRoad(layout, S);
                    var outside = EndsOutside(layout, site);
                    var uncovered = CoverGap(site, layout, S).Percent;
                    var roadOverlap = MeasureRoadConflict(layout).Total;
                    if (layout.Stalls != 466 || unserved != 0 || layout.Parts != 2
                        || layout.PassInfo.Any(info => info.Half) || layout.Aisles != 2
                        || layout.AisleLine.Length < layout.NotchAisles + layout.Aisles
                        || layout.Crossings != 2 || layout.CrossRouteLine.Length != 2
                        || bayOverlap != 0 || bayOnRoad != 0 || outside != 0
                        || uncovered >= 0.05 || roadOverlap >= 0.005)
                        throw new InvalidOperationException(
                            "PLT-94305602 weicht vom Prototyp ab: "
                            + $"{layout.Stalls} Buchten, {unserved} unerreichbar, "
                            + $"{layout.Parts} Teile, "
                            + $"{layout.PassInfo.Count(info => info.Half)} Halbmodule, "
                            + $"{layout.Aisles} Gassen, {layout.Crossings}/"
                            + $"{layout.CrossRouteLine.Length} Querungen, "
                            + $"Qualitaet {bayOverlap}/{bayOnRoad}/{outside}/"
                            + $"{uncovered:F1}/{roadOverlap:F3}.");
                }

                // Nutzerbau PLT-75B80478: Nicht die Lage, sondern der Erzeuger
                // entscheidet. Obwohl auch die Route aus Teil 1 mit ihrer Mitte
                // in Teil 2 liegt, muss Teil 2 mit 4 statt 1 Rohroute gewinnen.
                if (mode == "UI-Standard edge" && fall == "PLT-75B80478")
                {
                    var parts = layout.PassInfo.OrderBy(info => info.PartIndex).ToArray();
                    var bayOverlap = OverlapCount(layout.Bay);
                    var bayOnRoad = BayOnRoad(layout, S);
                    var outside = EndsOutside(layout, site);
                    var uncovered = CoverGap(site, layout, S).Percent;
                    var roadOverlap = MeasureRoadConflict(layout).Total;
                    var originCorrect = parts.Length == 2
                        && parts[0].RawCrossings == 1 && parts[0].Crossings == 0
                        && parts[1].RawCrossings == 4 && parts[1].Crossings == 4
                        && layout.CrossRouteInfo.Length == 4
                        && layout.CrossRouteInfo.All(route => route.PartIndex == 1);
                    if (layout.Stalls != 204 || layout.CrossRouteLine.Length != 4
                        || Math.Abs(routeSpacing - 36) > 0.001 || unserved != 0
                        || !originCorrect || bayOverlap != 0 || bayOnRoad != 0
                        // `edge` nimmt im Prototyp den Material-Rueckfallweg,
                        // C# dagegen die strikte Pipeline. Deren Rasterdeckung
                        // ist hier deshalb keine Paritaetszahl.
                        || outside != 0 || roadOverlap >= 0.005)
                        throw new InvalidOperationException(
                            "PLT-75B80478 weicht vom Prototyp ab: "
                            + $"{layout.Stalls} Buchten, "
                            + $"{layout.CrossRouteLine.Length} Verbindungen, "
                            + $"{routeSpacing:F2} m, {unserved} unerreichbar, "
                            + $"Herkunft {(originCorrect ? "richtig" : "FALSCH")}, "
                            + $"Qualitaet {bayOverlap}/{bayOnRoad}/{outside}/"
                            + $"{uncovered:F1}/{roadOverlap:F3}.");
                }

                // Die neue Gegenprobe ist nur dann ein Fix, wenn sie den
                // grossen leeren Teil wirklich fuellt und dabei weder Buchten
                // noch Verbindungsabstand opfert: gemessen 489 -> 528 Buchten,
                // 3 -> 7 Gassen und 1 046 -> 816 leere Rasterpunkte.
                if (mode == "UI-Standard edge" && fall == "PLT-BEA5AC77"
                    && (layout.Aisles != 7 || layout.CrossRouteLine.Length != 5
                        || layout.PassInfo.Length != 1 || layout.Parts != 1
                        || empty.Empty != 816 || empty.Total != 2251
                        || Math.Abs(routeSpacing - 36) > 0.001 || unserved != 0))
                    throw new InvalidOperationException(
                        "PLT-BEA5AC77 weicht vom Leerflaechen-Fix ab: "
                        + $"{layout.Stalls} Buchten, {layout.Aisles} Gassen, "
                        + $"{layout.CrossRouteLine.Length} Verbindungen, "
                        + $"{layout.PassInfo.Length} Laeufe, {layout.Parts} Teile, "
                        + $"leer {empty.Empty}/{empty.Total}, "
                        + $"Abstand {routeSpacing:F2} m, {unserved} unerreichbar.");
            }
    }

    /**
     * 4-m-Rasterpunkte ohne Bucht UND ohne Fahrbahn. Das Raster beginnt am
     * Bounding-Box-Minimum; damit bleibt die Zahl zwischen Prototyp und Mod
     * direkt diffbar und reproduziert fuer PLT-BEA5AC77 exakt 2 251 Punkte.
     */
    private static (int Total, int Empty, double Share) EmptyShare(
        float2[] site, ParkingLayout layout, double step = 4)
    {
        var roads = layout.PerimeterQuad.Concat(layout.AisleQuad)
            .Concat(layout.CrossQuad).Concat(layout.EntranceQuad).ToArray();
        var occupied = layout.Bay.Concat(roads).ToArray();
        var minX = site.Min(point => point.x);
        var maxX = site.Max(point => point.x);
        var minY = site.Min(point => point.y);
        var maxY = site.Max(point => point.y);
        var total = 0;
        var empty = 0;
        for (var y = (double)minY; y < maxY; y += step)
            for (var x = (double)minX; x < maxX; x += step)
            {
                var point = new float2((float)x, (float)y);
                if (!PointInRing(point, site)) continue;
                total++;
                if (!occupied.Any(ring => PointInRing(point, ring))
                    && !occupied.Any(ring => DistanceToBoundary(point, ring) <= 1e-6))
                    empty++;
            }
        return (total, empty, total > 0 ? (double)empty / total : 0);
    }

    /**
     * Eine Bucht ist nur bedient, wenn eine ihrer beiden 3,00-m-Kanten auf
     * Randstrasse oder Fahrgasse liegt. Fuenf Proben je Kante entsprechen der
     * Nachpruefung des Prototyps; Mittelpunktkontakt allein uebersah Teiltreffer.
     */
    private static int UnservedBays(ParkingLayout layout)
    {
        var roads = layout.PerimeterQuad.Concat(layout.AisleQuad).ToArray();
        // Die oeffentliche Testausgabe ist bereits float2. Bei Weltkoordinaten
        // um -1100 betraegt dessen Raster rund 0,00012 m; 0,001 m verhindert
        // deshalb reine Cast-Fehltreffer. Der Kern prueft vorher in double mit 1e-5.
        bool OnRoad(float2 point) => roads.Any(road =>
            PointInRing(point, road) || DistanceToBoundary(point, road) <= 0.001);
        bool EdgeOnRoad(float2 a, float2 b) => new[] { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f }
            .All(t => OnRoad(a + (b - a) * t));
        return layout.Bay.Count(bay =>
            !EdgeOnRoad(bay[0], bay[1]) && !EdgeOnRoad(bay[2], bay[3]));
    }

    /**
     * Deckungspruefung an gemeldeten Markierungen - auf der C#-Seite.
     *
     * Der Prototyp taugt dafuer nicht: bei `angleMode edge` nimmt er den
     * Rueckfallweg und pflastert die Buchten gar nicht, waehrend der Mod die
     * strikte Pipeline faehrt. Was der Nutzer sieht, entsteht HIER.
     */
    internal static void RunMarker()
    {
        var site = new[]
        {
            new float2(-1168.4104f, 84.05932f), new float2(-1043.91248f, 91.75974f),
            new float2(-1047.86353f, 155.637726f), new float2(-1108.24414f, 151.9027f),
            new float2(-1115.998f, 277.1949f), new float2(-1179.40771f, 273.270569f),
        };
        var S = LayoutSettings.Cs2;
        S.AngleMode = "edge";
        S.Auto = false;
        var layout = ParkingGeometry.Build(site, S);
        Console.WriteLine($"\nBau PLT-BA4DEFAB nachgerechnet: {layout.Stalls} Buchten, "
            + $"Gras {layout.GrassSurface.Length}, Belag {layout.AsphaltSurface.Length}"
            + " | im Spiel: 316 Buchten, 102 Gras, 11 Belag");

        var marker = new[]
        {
            (1, -1125.67f, 159.46f), (2, -1129.29f, 103.80f), (3, -1128.79f, 118.15f),
            (4, -1143.33f, 106.81f), (5, -1072.26f, 121.28f), (6, -1130.70f, 245.73f),
            (7, -1090.79f, 108.94f),
        };
        Console.WriteLine("\nWas deckt die Markierung?");
        foreach (var (nr, x, y) in marker)
        {
            var p = new float2(x, y);
            var gras = layout.GrassSurface.Any(r => PointInRing(p, r));
            var belag = layout.AsphaltSurface.Any(r => PointInRing(p, r));
            var bucht = layout.Bay.Any(r => PointInRing(p, r));
            var ring = layout.GrassSurface.FirstOrDefault(r => PointInRing(p, r));
            var beschreibung = ring == null ? "-"
                : $"{RingArea(ring):F1} m2, {ring.Length} Ecken, kuerzeste Kante "
                  + $"{KuerzesteKanteRing(ring):F3} m";
            var schlecht = ring != null
                && (KuerzesteKanteRing(ring) < 0.375 || RingArea(ring) < 2.0);
            Console.WriteLine($"  M{nr}: Gras {(gras ? "ja " : "NEIN")} | "
                + $"Belag {(belag ? "ja " : "NEIN")} | Ring: {beschreibung}"
                + (schlecht ? "   <== VON CS2 VERWORFEN" : ""));
        }

        // Wie zersplittert ist das Gruen? Kleine Ringe sind Narben.
        var klein = layout.GrassSurface.Count(r => RingArea(r) < 2.0);
        var winzig = layout.GrassSurface.Count(r => RingArea(r) < 0.5);
        Console.WriteLine($"\nGrasringe: {layout.GrassSurface.Length}, davon "
            + $"{klein} unter 2 m2 und {winzig} unter 0,5 m2.");
        var kurz = layout.GrassSurface.Sum(r => ZaehleKurzeKanten(r));
        Console.WriteLine($"Graskanten unter 0,375 m: {kurz}");
    }

    private static bool PointInRing(float2 p, float2[] ring)
    {
        var drin = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            if (ring[i].y > p.y != ring[j].y > p.y
                && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y)
                   / (ring[j].y - ring[i].y) + ring[i].x)
                drin = !drin;
        return drin;
    }

    private static double RingArea(float2[] ring)
    {
        double a = 0;
        for (var i = 0; i < ring.Length; i++)
        {
            var b = ring[(i + 1) % ring.Length];
            a += ring[i].x * b.y - b.x * ring[i].y;
        }
        return Math.Abs(a) / 2;
    }

    private static double KuerzesteKanteRing(float2[] ring)
    {
        var min = double.PositiveInfinity;
        for (var i = 0; i < ring.Length; i++)
            min = Math.Min(min, math.length(ring[(i + 1) % ring.Length] - ring[i]));
        return min;
    }

    private static int ZaehleKurzeKanten(float2[] ring)
    {
        var n = 0;
        for (var i = 0; i < ring.Length; i++)
            if (math.length(ring[(i + 1) % ring.Length] - ring[i]) < 0.375) n++;
        return n;
    }

    private static string Flaeche(float2[][] ringe)
    {
        double summe = 0;
        foreach (var ring in ringe)
        {
            double a = 0;
            for (var i = 0; i < ring.Length; i++)
            {
                var b = ring[(i + 1) % ring.Length];
                a += ring[i].x * b.y - b.x * ring[i].y;
            }
            summe += Math.Abs(a) / 2;
        }
        return summe.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string KuerzesteKante(float2[][] ringe)
    {
        var min = double.PositiveInfinity;
        foreach (var ring in ringe)
            for (var i = 0; i < ring.Length; i++)
                min = Math.Min(min, math.length(ring[(i + 1) % ring.Length] - ring[i]));
        return double.IsInfinity(min)
            ? "-" : min.ToString("F4", CultureInfo.InvariantCulture);
    }
}
