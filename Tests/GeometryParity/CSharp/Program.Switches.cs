using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private sealed class SwitchExpected
    {
        internal string Mode;
        internal bool Qk;
        internal int Stalls;
        internal int GrassRings;
        internal double GrassArea;
        internal int AsphaltRings;
        internal double AsphaltArea;
        internal int InnerGrassRings;
        internal double InnerGrassArea;
        internal int OuterGrassRings;
        internal double OuterGrassArea;
        internal int CrossCaps;
    }

    private sealed class MaterialSides
    {
        internal int InsideCount;
        internal double InsideArea;
        internal int OutsideCount;
        internal double OutsideArea;
        internal int Mixed;
    }

    private sealed class MaterialTriangle
    {
        internal float2[] Points;
        internal int Weight;
    }

    private static readonly Dictionary<string, SwitchExpected[]> SwitchExpectations =
        new Dictionary<string, SwitchExpected[]>
        {
            ["Rechteck"] = new[]
            {
                Switch("AN", true, 218, 12, 2482.20, 2, 8317.80,
                    10, 1676.64, 2, 805.56, 10),
                Switch("AUS", false, 234, 12, 1443.06, 2, 9356.94,
                    10, 637.50, 2, 805.56, 0),
            },
            ["L-Form"] = new[]
            {
                // Prototyp 38bd47c: Ohne die ungueltige Zerlegung bleiben die
                // zwei Aussenringe und ihre 871,56 m2 unveraendert; nur das
                // innere Layout wechselt. AN und AUS haben beide 184 Buchten.
                Switch("AN", true, 184, 5, 1915.70, 3, 6184.30,
                    3, 1044.14, 2, 871.56, 0),
                Switch("AUS", false, 184, 5, 1194.06, 3, 6905.94,
                    3, 322.50, 2, 871.56, 0),
            },
            ["Schraeg"] = new[]
            {
                Switch("AN", true, 199, 13, 2715.73, 3, 8084.27,
                    11, 1817.89, 2, 897.84, 8),
                Switch("AUS", false, 215, 8, 1227.84, 2, 9572.16,
                    6, 330.00, 2, 897.84, 0),
            },
            ["Referenz 08s"] = new[]
            {
                Switch("AN", true, 202, 11, 2603.75, 4, 7832.74,
                    10, 1613.12, 1, 990.63, 7),
                Switch("AUS", false, 213, 5, 1260.63, 3, 9175.87,
                    4, 270.00, 1, 990.63, 0),
            },
        };

    private static SwitchExpected Switch(string mode, bool qk, int stalls,
        int grassRings, double grassArea, int asphaltRings, double asphaltArea,
        int innerGrassRings, double innerGrassArea,
        int outerGrassRings, double outerGrassArea, int crossCaps) =>
        new SwitchExpected
        {
            Mode = mode, Qk = qk, Stalls = stalls,
            GrassRings = grassRings, GrassArea = grassArea,
            AsphaltRings = asphaltRings, AsphaltArea = asphaltArea,
            InnerGrassRings = innerGrassRings, InnerGrassArea = innerGrassArea,
            OuterGrassRings = outerGrassRings, OuterGrassArea = outerGrassArea,
            CrossCaps = crossCaps,
        };

    private static void RunSwitchParity(ref bool failed)
    {
        Console.WriteLine("\nSchalter Kappen an Querstrassen:");
        var crossCapsOn = 0;
        var crossCapsOff = 0;
        var entranceCapsOn = 0;
        var entranceCapsOff = 0;
        var capKinds = new Dictionary<string, int>();

        foreach (var item in Cases)
        {
            var results = new Dictionary<string, ParkingLayout>();
            foreach (var expected in SwitchExpectations[item.Name])
            {
                var settings = LayoutSettings.Cs2;
                settings.Qk = expected.Qk;
                // Wie im Hauptlauf: ein geworfener Fehler beendet nicht den
                // ganzen Durchgang, sonst blieben alle folgenden Formen
                // ungemessen. Er wird als Befund dieser Form gemeldet.
                ParkingLayout layout;
                try
                {
                    layout = ParkingGeometry.Build(item.Site, settings);
                }
                catch (Exception fehler)
                {
                    failed = true;
                    Console.Error.WriteLine($"SCHALTER-BAUABSTURZ: {item.Name} "
                        + $"{expected.Mode} - {fehler.Message}");
                    continue;
                }
                results[expected.Mode] = layout;

                var grassArea = RingsArea(layout.GrassSurface);
                var asphaltArea = RingsArea(layout.AsphaltSurface);
                var sides = SplitMaterialSides(layout.GrassSurface, layout.Ring);
                var crossCaps = CrossRoadCaps(layout);
                var uncovered = CoverGap(item.Site, layout, settings);
                var overlap = OverlapCount(layout.Bay);
                var onRoad = BayOnRoad(layout, settings);
                var outside = EndsOutside(layout, item.Site);
                var roads = MeasureRoadConflict(layout);
                var material = MeasureMaterialConflict(layout);
                var grassOverlap = SameMaterialOverlap(layout.GrassSurface);
                var asphaltOverlap = SameMaterialOverlap(layout.AsphaltSurface);
                var grassOnBay = GrassOnBayArea(layout);
                /*
                 * GRAS UNTER EINER FAHRGASSE.
                 *
                 * Am 2026-09-01 lag ueber jedem ausgerichteten Bau des
                 * Nutzers zwischen 106 und 193 m2 Fahrgasse auf Gras -
                 * genau die Anschlussstuecke zur Randstrasse und zur
                 * Verbindungsstrasse. Kein einziger Wert des Waechters hat
                 * das gesehen. Er sieht es jetzt.
                 */
                var grassOnAisle = GrasUnterGassen(layout);
                /*
                 * WIEVIELE FLAECHEN WIRD CS2 WEGWERFEN?
                 *
                 * Der Mod warnt davor im Spiel, der Waechter hat es bis zum
                 * 2026-09-01 nie gemessen. Genau dieser Wert entscheidet, ob
                 * an einer Stelle nackter Boden bleibt.
                 */
                var verworfen = Haarkanten(layout).Verdaechtig;
                var grassNeck = MinimumMaterialBottleneck(layout.GrassSurface);
                var asphaltNeck = MinimumMaterialBottleneck(layout.AsphaltSurface);
                var crossSpacing = ParkingGeometry.CrossRouteSpacing(layout.CrossRouteLine);
                var intersections = layout.GrassSurface.Sum(SelfIntersectionCount)
                    + layout.AsphaltSurface.Sum(SelfIntersectionCount);
                var duplicates = layout.GrassSurface.Count(HasNearDuplicatePoints)
                    + layout.AsphaltSurface.Count(HasNearDuplicatePoints);
                var capKindsValid = layout.CapKind.Length == layout.Cap.Length
                    && layout.CapKind.All(kind => kind == "cross" || kind == "ring"
                        || kind == "clip" || kind == "entrance" || kind == "aisle");
                var sectionsValid = SectionsValid(layout, settings);

                Console.WriteLine($"  {item.Name,-13} {expected.Mode,-7} | "
                    + $"{layout.Stalls,3} Buchten | Gras {layout.GrassSurface.Length,2} / "
                    + $"{grassArea,8:F2} m2 | innen {sides.InsideCount,2} / "
                    + $"{sides.InsideArea,7:F2} m2 | aussen {sides.OutsideCount,2} / "
                    + $"{sides.OutsideArea,6:F2} m2 | Asphalt "
                    + $"{layout.AsphaltSurface.Length,2} / {asphaltArea,8:F2} m2");
                Console.WriteLine($"  {string.Empty,-21} ungedeckt {uncovered.Percent:F1} % | "
                    + $"Buchtueberlappung {overlap} | Bucht auf Fahrbahn {onRoad} | "
                    + $"Enden ausserhalb {outside} | Strasse {roads.Total:F2} m2 | "
                    + $"Asphalt/Gras {material:F2} m2 | Gras/Gras {grassOverlap:F2} m2 | "
                    + $"Asphalt/Asphalt {asphaltOverlap:F2} m2 | Gras/Bucht {grassOnBay:F2} m2 | "
                    + $"Gras/Gasse {grassOnAisle:F2} m2 | "
                    + $"CS2 verwirft {verworfen} | "
                    + $"Engstelle {grassNeck:F3}/{asphaltNeck:F3} m");

                var valuesMatch = layout.Stalls == expected.Stalls
                    && layout.GrassSurface.Length == expected.GrassRings
                    && NearHundredth(grassArea, expected.GrassArea)
                    && layout.AsphaltSurface.Length == expected.AsphaltRings
                    && NearHundredth(asphaltArea, expected.AsphaltArea)
                    && sides.InsideCount == expected.InnerGrassRings
                    && NearHundredth(sides.InsideArea, expected.InnerGrassArea)
                    && sides.OutsideCount == expected.OuterGrassRings
                    && NearHundredth(sides.OutsideArea, expected.OuterGrassArea)
                    && sides.Mixed == 0 && crossCaps == expected.CrossCaps;
                var quality = uncovered.Percent < 0.05 && overlap == 0 && onRoad == 0
                    && outside == 0 && roads.Total < 0.005 && material < 0.005
                    && grassOverlap < 0.005 && asphaltOverlap < 0.005
                    && grassOnBay < 0.05 && grassOnAisle < 0.05
                    && verworfen == 0
                    && intersections == 0 && duplicates == 0
                    && grassNeck >= ParkingGeometry.SurfaceNeckLimit - 1e-6
                    && asphaltNeck >= ParkingGeometry.SurfaceNeckLimit - 1e-6
                    && (double.IsPositiveInfinity(crossSpacing)
                        || crossSpacing >= settings.Cr - 0.001)
                    && capKindsValid && sectionsValid && layout.Warnings.Length == 0;
                // Dieselbe Blindstelle wie beim Hauptlauf, siehe dort: eine
                // Meldung, die nur die Form nennt, gibt jede einmal bekannte
                // Form fuer beliebige weitere Maengel frei.
                var gruende = new List<string>();
                if (layout.Stalls != expected.Stalls)
                    gruende.Add($"Buchten {layout.Stalls}/{expected.Stalls}");
                if (layout.GrassSurface.Length != expected.GrassRings
                    || !NearHundredth(grassArea, expected.GrassArea))
                    gruende.Add($"Gras {layout.GrassSurface.Length} Ringe "
                        + $"{grassArea:F2} m2");
                if (layout.AsphaltSurface.Length != expected.AsphaltRings
                    || !NearHundredth(asphaltArea, expected.AsphaltArea))
                    gruende.Add($"Asphalt {layout.AsphaltSurface.Length} Ringe "
                        + $"{asphaltArea:F2} m2");
                if (sides.InsideCount != expected.InnerGrassRings
                    || !NearHundredth(sides.InsideArea, expected.InnerGrassArea))
                    gruende.Add($"Gras innen {sides.InsideCount} "
                        + $"{sides.InsideArea:F2} m2");
                if (sides.OutsideCount != expected.OuterGrassRings
                    || !NearHundredth(sides.OutsideArea, expected.OuterGrassArea))
                    gruende.Add($"Gras aussen {sides.OutsideCount} "
                        + $"{sides.OutsideArea:F2} m2");
                if (sides.Mixed != 0) gruende.Add($"Gras gemischt {sides.Mixed}");
                if (crossCaps != expected.CrossCaps)
                    gruende.Add($"Querkappen {crossCaps}/{expected.CrossCaps}");
                if (uncovered.Percent >= 0.05)
                    gruende.Add($"ungedeckt {uncovered.Percent:F1} %");
                if (overlap != 0) gruende.Add($"Buchtueberlappung {overlap}");
                if (onRoad != 0) gruende.Add($"Bucht auf Fahrbahn {onRoad}");
                if (outside != 0) gruende.Add($"Enden ausserhalb {outside}");
                if (roads.Total >= 0.005)
                    gruende.Add($"Strasse auf Strasse {roads.Total:F2} m2");
                if (material >= 0.005)
                    gruende.Add($"Asphalt auf Gras {material:F2} m2");
                if (verworfen != 0)
                    gruende.Add($"CS2 verwirft {verworfen} Flaeche(n)");
                if (grassOnAisle >= 0.05)
                    gruende.Add($"Gras unter Gasse {grassOnAisle:F2} m2");
                if (grassOverlap >= 0.005)
                    gruende.Add($"Gras auf Gras {grassOverlap:F2} m2");
                if (asphaltOverlap >= 0.005)
                    gruende.Add($"Asphalt auf Asphalt {asphaltOverlap:F2} m2");
                if (grassOnBay >= 0.05)
                    gruende.Add($"Gras auf Bucht {grassOnBay:F2} m2");
                if (intersections != 0)
                    gruende.Add($"Selbstschnitte {intersections}");
                if (duplicates != 0) gruende.Add($"Doppelpunkte {duplicates}");
                if (grassNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6)
                    gruende.Add($"Engstelle Gras {grassNeck:F3} m");
                if (asphaltNeck < ParkingGeometry.SurfaceNeckLimit - 1e-6)
                    gruende.Add($"Engstelle Asphalt {asphaltNeck:F3} m");
                if (!(double.IsPositiveInfinity(crossSpacing)
                    || crossSpacing >= settings.Cr - 0.001))
                    gruende.Add($"Querabstand {crossSpacing:F2} m");
                if (!capKindsValid) gruende.Add("Kappenarten");
                if (!sectionsValid) gruende.Add("Abschnitte");
                if (layout.Warnings.Length != 0)
                    gruende.Add($"Warnungen {layout.Warnings.Length}");
                if (!valuesMatch || !quality)
                {
                    failed = true;
                    Console.Error.WriteLine(
                        $"SCHALTER-PARITAETSFEHLER: {item.Name} {expected.Mode} ["
                        + string.Join(", ", gruende) + "]");
                    foreach (var warning in layout.Warnings)
                        Console.Error.WriteLine("  WARNUNG: " + warning);
                }
            }

            // Ist einer der beiden Durchgaenge abgestuerzt, gibt es nichts
            // zu vergleichen - der Absturz selbst steht schon als Befund da.
            if (!results.TryGetValue("AN", out var onOn)
                || !results.TryGetValue("AUS", out var offOn)) continue;
            if (item.Name == "L-Form" && !SameLayout(onOn, offOn))
            {
                failed = true;
                Console.Error.WriteLine($"SCHALTER-LAYOUTFEHLER: {item.Name}");
            }

            crossCapsOn += CrossRoadCaps(onOn);
            crossCapsOff += CrossRoadCaps(offOn);
            entranceCapsOn += onOn.CapKind.Count(kind => kind == "entrance");
            entranceCapsOff += offOn.CapKind.Count(kind => kind == "entrance");
            foreach (var kind in onOn.CapKind)
                capKinds[kind] = capKinds.TryGetValue(kind, out var count) ? count + 1 : 1;
        }

        var expectedKinds = new Dictionary<string, int>
        {
            // Prototyp 38bd47c: In der L-Form werden vier Schnittkappen zu
            // Randstrassenkappen und eine weitere Randkappe kommt hinzu.
            ["ring"] = 25, ["cross"] = 25, ["entrance"] = 12,
            ["clip"] = 15, ["aisle"] = 1,
        };
        if (crossCapsOn != 25 || crossCapsOff != 0
            || entranceCapsOn != 12 || entranceCapsOff != 12
            || expectedKinds.Any(pair => !capKinds.TryGetValue(pair.Key, out var count)
                || count != pair.Value)
            || capKinds.Count != expectedKinds.Count)
        {
            failed = true;
            Console.Error.WriteLine("SCHALTER-KAPPENHERKUNFT FEHLER: "
                + string.Join(", ", capKinds.Select(pair => $"{pair.Key} {pair.Value}")));
        }
    }

    private static double RingsArea(IEnumerable<float2[]> rings) =>
        rings.Sum(ring => Math.Abs(SignedArea(ring)));

    private static bool NearHundredth(double actual, double expected) =>
        Math.Abs(actual - expected) < 0.015;

    /** Flaechenanteilig; ein Schwerpunkt waere beim geschlossenen Randband falsch. */
    private static MaterialSides SplitMaterialSides(float2[][] grass, float2[] ring)
    {
        var result = new MaterialSides();
        var ringTriangles = TriangulateMaterialRegion(ring);
        foreach (var polygon in grass)
        {
            var area = Math.Abs(SignedArea(polygon));
            var inside = TriangulateMaterialRegion(polygon).Sum(part => part.Weight
                * ringTriangles.Sum(region => region.Weight
                    * ConvexOverlapArea(part.Points, region.Points)));
            inside = Math.Min(area, Math.Max(0, inside));
            var outside = Math.Max(0, area - inside);
            if (inside > 0.005 && outside > 0.005) result.Mixed++;
            if (inside >= outside)
            {
                result.InsideCount++;
                result.InsideArea += inside;
            }
            else
            {
                result.OutsideCount++;
                result.OutsideArea += outside;
            }
        }
        return result;
    }

    private static int CrossRoadCaps(ParkingLayout layout) =>
        layout.Cap.Count(cap => layout.CrossQuad.Any(road => SharesBoundary(cap, road)));

    private static bool SharesBoundary(float2[] first, float2[] second)
    {
        for (var firstEdge = 0; firstEdge < first.Length; firstEdge++)
        {
            var a = first[firstEdge];
            var b = first[(firstEdge + 1) % first.Length];
            var vector = b - a;
            var length = Length(vector);
            if (length < 1e-8) continue;
            var u = vector / (float)length;
            for (var secondEdge = 0; secondEdge < second.Length; secondEdge++)
            {
                var c = second[secondEdge];
                var d = second[(secondEdge + 1) % second.Length];
                var other = d - c;
                var otherLength = Length(other);
                if (otherLength < 1e-8
                    || Math.Abs((double)u.x * other.y - (double)u.y * other.x)
                        > 1e-5 * otherLength) continue;
                var normal = new float2(-u.y, u.x);
                if (Math.Abs(math.dot(c - a, normal)) > 1e-4
                    || Math.Abs(math.dot(d - a, normal)) > 1e-4) continue;
                var lo = Math.Max(0, Math.Min(math.dot(c - a, u), math.dot(d - a, u)));
                var hi = Math.Min(length, Math.Max(math.dot(c - a, u), math.dot(d - a, u)));
                if (hi - lo > 0.01) return true;
            }
        }
        return false;
    }

    private static bool SectionsValid(ParkingLayout layout, LayoutSettings settings)
    {
        foreach (var section in layout.Sections)
        {
            if (Math.Abs(section.Cap0 + section.Cap1
                + section.Bays * settings.Sw - section.L) > 1e-6) return false;
            foreach (var end in new[]
            {
                (Kind: section.End0, CapLength: section.Cap0),
                (Kind: section.End1, CapLength: section.Cap1),
            })
            {
                if (end.Kind == "cross" && !settings.Qk)
                {
                    if (end.CapLength < -1e-6
                        || end.CapLength >= settings.Sw - 1e-6) return false;
                }
                else if (end.CapLength < settings.Sw - 1e-6
                    || end.CapLength >= 2 * settings.Sw + 1e-6) return false;
            }
        }
        return true;
    }

    private static double SameMaterialOverlap(float2[][] rings)
    {
        var triangles = new List<MaterialTriangle>[rings.Length];
        for (var index = 0; index < rings.Length; index++)
        {
            try
            {
                triangles[index] = TriangulateMaterialRegion(rings[index]);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    $"Materialring {index} mit {rings[index].Length} Knoten und "
                    + $"Flaeche {Math.Abs(SignedArea(rings[index])):F9} "
                    + $"({error.Message}): "
                    + string.Join("; ", rings[index].Select(point =>
                        $"({point.x:R},{point.y:R})")), error);
            }
        }
        var area = 0.0;
        for (var first = 0; first < rings.Length; first++)
            for (var second = first + 1; second < rings.Length; second++)
                foreach (var a in triangles[first])
                    foreach (var b in triangles[second])
                        area += a.Weight * b.Weight
                            * ConvexOverlapArea(a.Points, b.Points);
        return area < 1e-8 ? 0 : area;
    }

    private static double GrassOnBayArea(ParkingLayout layout)
    {
        var area = layout.GrassSurface.SelectMany(TriangulateMaterialRegion).Sum(triangle =>
            triangle.Weight * layout.Bay.Sum(bay =>
                ConvexOverlapArea(triangle.Points, bay)));
        return area < 1e-8 ? 0 : area;
    }

    /**
     * Nach float-Rundung fallen die zwei Seiten einer Lochbrücke exakt zusammen.
     * Die Teilringe werden deshalb mit Vorzeichen trianguliert; ihre Summe bleibt
     * die tatsächlich gefüllte Materialfläche.
     */
    private static List<MaterialTriangle> TriangulateMaterialRegion(float2[] ring)
    {
        var sourceArea = Math.Abs(SignedArea(ring));
        var sourceWinding = SignedArea(ring) >= 0 ? 1 : -1;
        var loops = SplitWeakMaterialRing(InsertMaterialJunctions(ring));
        var result = new List<MaterialTriangle>();
        foreach (var loop in loops)
        {
            var clean = RemoveRedundantMaterialPoints(loop);
            if (clean.Length < 3 || Math.Abs(SignedArea(clean)) < 1e-9) continue;
            List<float2[]> triangles;
            try
            {
                triangles = Triangulate(clean);
            }
            catch (InvalidOperationException)
            {
                try
                {
                    triangles = TriangulateWeakRing(clean);
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException(
                        $"Teilring mit {clean.Length} Knoten ({error.Message}): "
                        + string.Join("; ", clean.Select(point =>
                            $"({point.x:R},{point.y:R})")), error);
                }
            }
            var weight = (SignedArea(clean) >= 0 ? 1 : -1) * sourceWinding;
            result.AddRange(triangles.Select(points => new MaterialTriangle
            {
                Points = points,
                Weight = weight,
            }));
        }

        var measuredArea = result.Sum(triangle => triangle.Weight
            * Math.Abs(SignedArea(triangle.Points)));
        if (Math.Abs(sourceArea - measuredArea) > 1e-4)
            throw new InvalidOperationException(
                $"Triangulationsfläche weicht um {measuredArea - sourceArea:R} m2 ab.");
        return result;
    }

    /** Lochbrücken können einen vorhandenen Knoten mitten auf einer Kante treffen. */
    private static float2[] InsertMaterialJunctions(float2[] ring)
    {
        var output = new List<float2>();
        for (var edge = 0; edge < ring.Length; edge++)
        {
            var a = ring[edge];
            var b = ring[(edge + 1) % ring.Length];
            var vector = b - a;
            var lengthSquared = (double)vector.x * vector.x
                + (double)vector.y * vector.y;
            output.Add(a);
            if (lengthSquared < 1e-12) continue;
            var length = Math.Sqrt(lengthSquared);
            var junctions = new List<(double T, float2 Point)>();
            foreach (var point in ring)
            {
                var relative = point - a;
                var t = ((double)relative.x * vector.x
                    + (double)relative.y * vector.y) / lengthSquared;
                var cross = (double)vector.x * relative.y
                    - (double)vector.y * relative.x;
                if (t <= 1e-8 || t >= 1 - 1e-8
                    || Math.Abs(cross) > 2e-6 * length) continue;
                if (junctions.Any(item => math.distancesq(item.Point, point) < 1e-12f))
                    continue;
                junctions.Add((t, point));
            }
            foreach (var junction in junctions.OrderBy(item => item.T))
                output.Add(junction.Point);
        }
        return output.ToArray();
    }

    private static List<float2[]> SplitWeakMaterialRing(float2[] ring)
    {
        var pending = new Stack<List<float2>>();
        var result = new List<float2[]>();
        pending.Push(ring.ToList());
        while (pending.Count > 0)
        {
            var points = pending.Pop();
            var split = false;
            for (var first = 0; first < points.Count && !split; first++)
                for (var second = first + 2; second < points.Count; second++)
                {
                    var span = second - first;
                    if (points.Count - span < 2
                        || !math.all(points[first] == points[second])) continue;
                    pending.Push(points.GetRange(first, span));
                    var remainder = points.GetRange(0, first + 1);
                    remainder.AddRange(points.GetRange(second + 1,
                        points.Count - second - 1));
                    pending.Push(remainder);
                    split = true;
                    break;
                }
            if (!split) result.Add(points.ToArray());
        }
        return result;
    }

    /** Kollineare Zwischenknoten tragen keine Fläche, blockieren aber Ohr-Clipping. */
    private static float2[] RemoveRedundantMaterialPoints(float2[] ring)
    {
        var points = ring.ToList();
        for (var pass = 0; pass < ring.Length && points.Count > 2; pass++)
        {
            var removed = false;
            for (var i = 0; i < points.Count; i++)
            {
                var before = points[(i - 1 + points.Count) % points.Count];
                var point = points[i];
                var after = points[(i + 1) % points.Count];
                var incoming = point - before;
                var outgoing = after - point;
                var scale = Math.Max(1, Length(incoming) * Length(outgoing));
                var cross = (double)incoming.x * outgoing.y
                    - (double)incoming.y * outgoing.x;
                var dot = (double)incoming.x * outgoing.x
                    + (double)incoming.y * outgoing.y;
                if (Math.Abs(cross) > 1e-7 * scale || dot <= 0) continue;
                points.RemoveAt(i);
                removed = true;
                break;
            }
            if (!removed) break;
        }
        return points.ToArray();
    }

    /**
     * Vereinigte Materialflächen kodieren Löcher mit deckungsgleichen Brückenkanten.
     * Punkte auf einer möglichen Ohrdiagonale sind deshalb keine inneren Punkte.
     */
    private static List<float2[]> TriangulateWeakRing(float2[] ring)
    {
        var triangles = new List<float2[]>();
        if (ring == null || ring.Length < 3) return triangles;
        var indices = Enumerable.Range(0, ring.Length).ToList();
        var winding = SignedArea(ring) >= 0 ? 1 : -1;
        double Cross(float2 a, float2 b, float2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y)
            - ((double)b.y - a.y) * ((double)c.x - a.x);
        bool StrictlyInTriangle(float2 p, float2 a, float2 b, float2 c) =>
            winding * Cross(a, b, p) > 1e-8
            && winding * Cross(b, c, p) > 1e-8
            && winding * Cross(c, a, p) > 1e-8;

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
                if (indices.Any(index => index != before && index != current
                    && index != after && StrictlyInTriangle(
                        ring[index], ring[before], ring[current], ring[after]))) continue;
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

        var sourceArea = Math.Abs(SignedArea(ring));
        var triangleArea = triangles.Sum(triangle => Math.Abs(SignedArea(triangle)));
        if (Math.Abs(sourceArea - triangleArea) > 1e-4)
            throw new InvalidOperationException(
                $"Triangulationsfläche weicht um {triangleArea - sourceArea:R} m2 ab.");
        return triangles;
    }

    private static bool SameLayout(ParkingLayout first, ParkingLayout second)
    {
        bool Polygons(float2[][] a, float2[][] b) => a.Length == b.Length
            && a.Zip(b, (x, y) => x.Length == y.Length
                && x.Zip(y, (p, q) => math.all(p == q)).All(equal => equal)).All(equal => equal);
        bool Entrances() => first.Entrances.Length == second.Entrances.Length
            && first.Entrances.Zip(second.Entrances, (a, b) =>
                a.Edge == b.Edge && a.Along == b.Along && a.Corner == b.Corner)
                .All(equal => equal);
        bool Sections() => first.Sections.Length == second.Sections.Length
            && first.Sections.Zip(second.Sections, (a, b) => a.L == b.L
                && a.Bays == b.Bays && a.Cap0 == b.Cap0 && a.Cap1 == b.Cap1
                && a.End0 == b.End0 && a.End1 == b.End1).All(equal => equal);
        return Polygons(first.Bay, second.Bay)
            && first.BayKind.SequenceEqual(second.BayKind)
            && first.BayRole.SequenceEqual(second.BayRole)
            && first.ElectricPair.SequenceEqual(second.ElectricPair)
            && Polygons(first.Cap, second.Cap)
            && first.CapKind.SequenceEqual(second.CapKind)
            && Polygons(first.Median, second.Median)
            && Polygons(first.Green, second.Green)
            && Polygons(first.Fill, second.Fill)
            && Polygons(first.FillHole, second.FillHole)
            && Polygons(first.CrossPavement, second.CrossPavement)
            && Polygons(first.PerimeterQuad, second.PerimeterQuad)
            && Polygons(first.EntranceQuad, second.EntranceQuad)
            && Polygons(first.AisleLine, second.AisleLine)
            && Polygons(first.AisleQuad, second.AisleQuad)
            && Polygons(first.CrossLine, second.CrossLine)
            && Polygons(first.CrossQuad, second.CrossQuad)
            && Polygons(first.PerimeterLine, second.PerimeterLine)
            && Polygons(first.EntranceLine, second.EntranceLine)
            && first.Ring.Length == second.Ring.Length
            && first.Ring.Zip(second.Ring, (a, b) => math.all(a == b)).All(equal => equal)
            && Entrances() && Sections()
            && first.Stalls == second.Stalls
            && first.PerimeterStalls == second.PerimeterStalls
            && first.InnerPerimeterStalls == second.InnerPerimeterStalls
            && first.InnerStalls == second.InnerStalls
            && first.ExtraStalls == second.ExtraStalls
            && first.SpecialStalls.Behindert == second.SpecialStalls.Behindert
            && first.SpecialStalls.Elektro == second.SpecialStalls.Elektro
            && first.Angle == second.Angle && first.Aisles == second.Aisles
            && first.Parts == second.Parts && first.NotchAisles == second.NotchAisles;
    }
}
