using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * ECHTE CS2-Formregel fuer Surface-Areas, nicht das alte Ersatzmass
         * "kurze Kante": m_SnapDistance * 0,5 = 0,75 / 2 = 0,375 m zwischen
         * einem Knoten und einer nicht benachbarten Kante desselben Rings.
         * Gemessen vor der Reparatur: Rechteck/Schraeg/Referenz je 0,
         * L-Form 9 Verstoesse.
         */
        private const double SurfaceCs2NodeEdgeLimit = 0.375;

        /**
         * Ein Besitzwechsel bleibt ein Keil, keine nachtraegliche Umplanung.
         * Die drei erfolgreichen L-Form-Schritte bewegen 0,021263, 2,906096
         * und 2,130532 m2; der groesste liegt damit unter 3 m2. Alles darueber
         * bleibt unangetastet.
         */
        private const double SurfaceCs2MaxTransferArea = 3;

        private sealed class SurfaceTransferCandidate
        {
            internal List<double2[]> Grass;
            internal List<double2[]> Asphalt;
            internal double Score;
            internal double TransferredArea;
            internal double2[] Triangle;
            internal bool SourceIsGrass;
            internal bool NeighborIsGrass;
        }

        private sealed class Cs2MaterialCandidate
        {
            internal List<double2[]> Grass;
            internal List<double2[]> Asphalt;
            internal double After;
        }

        private sealed class Cs2SplitCandidate
        {
            internal int Ring;
            internal double2[] First;
            internal double2[] Second;
            internal int After;
            internal double SmallArea;
        }

        /**
         * Variante von surfaceTransferVertex mit optionaler Bewertung. Der
         * normale Engstellenlauf maximiert weiterhin seine alte Bewertung;
         * die neue CS2-Stufe minimiert dagegen die Zahl und Tiefe der
         * Knoten-Gegenkanten-Verstoesse und benoetigt die Metadaten des Keils.
         */
        private static SurfaceTransferCandidate FindSurfaceTransferVertex(
            List<double2[]> grass, List<double2[]> asphalt,
            bool sourceIsGrass, SurfaceFeature feature,
            Func<List<double2[]>, List<double2[]>, double> evaluate = null)
        {
            var sourceRings = sourceIsGrass ? grass : asphalt;
            var source = sourceRings[feature.Ring];
            var candidateIndices = new[]
            {
                feature.Point,
                feature.Edge,
                (feature.Edge + 1) % source.Length,
            }.Distinct();
            SurfaceTransferCandidate best = null;

            foreach (var candidateIndex in candidateIndices)
            {
                if (source.Length <= 3) continue;
                var triangle = new[]
                {
                    source[(candidateIndex - 1 + source.Length) % source.Length],
                    source[candidateIndex],
                    source[(candidateIndex + 1) % source.Length],
                };
                var reduced = source.Where((_, i) => i != candidateIndex).ToArray();
                var transferredArea = Math.Abs(SignedArea(source))
                                    - Math.Abs(SignedArea(reduced));
                if (transferredArea <= 1e-10 || SelfIntersects(reduced, 2e-6))
                    continue;
                var changedSource = SurfaceCcw(reduced);

                // Die Reihenfolge Gras, Asphalt ist Teil der JS-Auswahl bei
                // exakt gleicher Bewertung und bleibt deshalb ausdruecklich.
                foreach (var neighborIsGrass in new[] { true, false })
                {
                    var neighborRings = neighborIsGrass ? grass : asphalt;
                    for (var neighbor = 0; neighbor < neighborRings.Count; neighbor++)
                    {
                        if (neighborIsGrass == sourceIsGrass
                            && neighbor == feature.Ring) continue;
                        if (!TrySurfaceUnion(triangle, neighborRings[neighbor], out var joined)
                            || !SurfaceCs2Compatible(changedSource)
                            || !SurfaceCs2Compatible(joined)) continue;
                        var before = Math.Abs(SignedArea(source))
                                   + Math.Abs(SignedArea(neighborRings[neighbor]));
                        var after = Math.Abs(SignedArea(changedSource))
                                  + Math.Abs(SignedArea(joined));
                        if (Math.Abs(after - before) > Math.Max(1e-7, before * 1e-10))
                            continue;

                        var candidateGrass = grass.Where((_, i) =>
                            !(sourceIsGrass && i == feature.Ring)
                            && !(neighborIsGrass && i == neighbor)).ToList();
                        var candidateAsphalt = asphalt.Where((_, i) =>
                            !(!sourceIsGrass && i == feature.Ring)
                            && !(!neighborIsGrass && i == neighbor)).ToList();
                        (sourceIsGrass ? candidateGrass : candidateAsphalt)
                            .Add(changedSource);
                        (neighborIsGrass ? candidateGrass : candidateAsphalt)
                            .Add(joined);

                        double score;
                        if (evaluate != null)
                            score = evaluate(candidateGrass, candidateAsphalt);
                        else
                        {
                            var changedRings = sourceIsGrass
                                ? candidateGrass : candidateAsphalt;
                            var targetRings = neighborIsGrass
                                ? candidateGrass : candidateAsphalt;
                            var changedFeature = MinimumSurfaceFeature(changedRings);
                            var targetFeature = MinimumSurfaceFeature(targetRings);
                            score = Math.Min(changedFeature?.Distance ?? 1e5, 1e5)
                                  + Math.Min(targetFeature?.Distance ?? 1e5, 1e5) * 1e-3;
                        }
                        var improves = best == null || (evaluate != null
                            ? score < best.Score : score > best.Score);
                        if (!improves) continue;
                        best = new SurfaceTransferCandidate
                        {
                            Grass = candidateGrass,
                            Asphalt = candidateAsphalt,
                            Score = score,
                            TransferredArea = transferredArea,
                            Triangle = triangle,
                            SourceIsGrass = sourceIsGrass,
                            NeighborIsGrass = neighborIsGrass,
                        };
                    }
                }
            }
            return best;
        }

        private static List<SurfaceFeature> SurfaceCs2NodeEdgeFeatures(
            List<double2[]> rings)
        {
            var output = new List<SurfaceFeature>();
            for (var ringIndex = 0; ringIndex < rings.Count; ringIndex++)
            {
                var ring = rings[ringIndex];
                var n = ring.Length;
                for (var pointIndex = 0; pointIndex < n; pointIndex++)
                    for (var edgeIndex = 0; edgeIndex < n; edgeIndex++)
                    {
                        if (edgeIndex == pointIndex
                            || (edgeIndex + 1) % n == pointIndex
                            || edgeIndex == (pointIndex + 1) % n) continue;
                        var a = ring[edgeIndex];
                        var edge = ring[(edgeIndex + 1) % n] - a;
                        var lengthSquared = edge.x * edge.x + edge.y * edge.y;
                        if (lengthSquared < 1e-12) continue;
                        var rawT = ((ring[pointIndex].x - a.x) * edge.x
                                  + (ring[pointIndex].y - a.y) * edge.y)
                                 / lengthSquared;
                        var t = Math.Max(0, Math.Min(1, rawT));
                        var projection = a + edge * t;
                        var distance = Len(ring[pointIndex] - projection);
                        if (distance >= SurfaceCs2NodeEdgeLimit) continue;
                        output.Add(new SurfaceFeature
                        {
                            Ring = ringIndex,
                            Point = pointIndex,
                            Edge = edgeIndex,
                            T = t,
                            Projection = projection,
                            Distance = distance,
                            Interior = t > 1e-7 && t < 1 - 1e-7,
                        });
                    }
            }
            // Array.sort ist stabil: OrderBy bewahrt bei gleichem Abstand die
            // Besuchsreihenfolge point -> edge der Wahrheitsquelle.
            return output.OrderBy(feature => feature.Distance).ToList();
        }

        private static double SurfaceCs2NodeEdgeScore(
            List<double2[]> grass, List<double2[]> asphalt)
        {
            var features = SurfaceCs2NodeEdgeFeatures(grass);
            features.AddRange(SurfaceCs2NodeEdgeFeatures(asphalt));
            return features.Count * 1e6 + features.Sum(feature =>
                SurfaceCs2NodeEdgeLimit - feature.Distance);
        }

        private static void TransferCs2Slivers(
            List<double2[]> inputGrass, List<double2[]> inputAsphalt,
            List<double2[]> bays, out List<double2[]> resultGrass,
            out List<double2[]> resultAsphalt)
        {
            /**
             * Nicht denselben Knoten in allen Ringen loeschen: bei
             * 20,328/25,127 ist er im grossen Asphaltumriss ein 0,160-m-Zacken,
             * im zweiten Asphaltband aber eine echte Ecke. Stattdessen wechseln
             * nur kantengenau anschliessbare Dreiecke den Materialbesitzer;
             * danach duerfen gleichartige Nachbarn verlustfrei verschmelzen.
             * An der L-Form sind das zusammen 5,057891 m2 Gras -> Asphalt, und
             * die Asphaltengstelle waechst 0,285152 -> 0,869685 m.
             */
            var grass = inputGrass;
            var asphalt = inputAsphalt;
            for (var guard = 0; guard < 32; guard++)
            {
                var before = SurfaceCs2NodeEdgeScore(grass, asphalt);
                if (before < 1e6) break;
                Cs2MaterialCandidate best = null;

                double Evaluate(List<double2[]> candidateGrass,
                                List<double2[]> candidateAsphalt) =>
                    SurfaceCs2NodeEdgeScore(
                        MergeAdjacentSurfaces(candidateGrass),
                        MergeAdjacentSurfaces(candidateAsphalt));

                void Check(SurfaceTransferCandidate candidate)
                {
                    if (candidate == null
                        || candidate.TransferredArea > SurfaceCs2MaxTransferArea
                        || (candidate.NeighborIsGrass
                            && bays.Any(bay => QuadsOverlap(candidate.Triangle, bay, 0))))
                        return;
                    var mergedGrass = MergeAdjacentSurfaces(candidate.Grass);
                    var mergedAsphalt = MergeAdjacentSurfaces(candidate.Asphalt);
                    var after = SurfaceCs2NodeEdgeScore(mergedGrass, mergedAsphalt);
                    if (after >= before || best != null && after >= best.After) return;
                    best = new Cs2MaterialCandidate
                    {
                        Grass = mergedGrass,
                        Asphalt = mergedAsphalt,
                        After = after,
                    };
                }

                foreach (var sourceIsGrass in new[] { true, false })
                {
                    var source = sourceIsGrass ? grass : asphalt;
                    foreach (var feature in SurfaceCs2NodeEdgeFeatures(source))
                        Check(FindSurfaceTransferVertex(
                            grass, asphalt, sourceIsGrass, feature, Evaluate));
                }

                // Ein zu enger Anschluss kann auf der Gegenseite bereits ein
                // gueltiger Ring sein. Dann taucht sein gemeinsamer Knoten nicht
                // in den Merkmalen auf, obwohl genau dessen Nachbardreieck den
                // Anschluss verbreitert. Nur EXAKT geteilte Materialknoten
                // kommen als solche Gegenkandidaten in Frage.
                foreach (var sourceIsGrass in new[] { true, false })
                {
                    var source = sourceIsGrass ? grass : asphalt;
                    var other = sourceIsGrass ? asphalt : grass;
                    foreach (var feature in SurfaceCs2NodeEdgeFeatures(source))
                    {
                        var ring = source[feature.Ring];
                        var shared = new[]
                        {
                            ring[feature.Point],
                            ring[feature.Edge],
                            ring[(feature.Edge + 1) % ring.Length],
                        };
                        for (var otherRing = 0; otherRing < other.Count; otherRing++)
                            for (var point = 0; point < other[otherRing].Length; point++)
                            {
                                if (!shared.Any(candidate =>
                                    SurfaceSamePoint(candidate, other[otherRing][point])))
                                    continue;
                                var n = other[otherRing].Length;
                                foreach (var edge in new[]
                                {
                                    point,
                                    (point - 1 + n) % n,
                                })
                                    Check(FindSurfaceTransferVertex(
                                        grass, asphalt, !sourceIsGrass,
                                        new SurfaceFeature
                                        {
                                            Ring = otherRing,
                                            Point = point,
                                            Edge = edge,
                                        }, Evaluate));
                            }
                    }
                }
                if (best == null) break;
                grass = best.Grass;
                asphalt = best.Asphalt;
            }
            resultGrass = grass;
            resultAsphalt = asphalt;
        }

        private static List<double2[]> SplitCs2NodeEdges(List<double2[]> rings)
        {
            /**
             * Ist kein Besitzwechsel noetig, reicht eine echte Flaechenteilung:
             * beide Teile behalten exakt die Ausgangsflaeche, aber Knoten und
             * Gegenkante liegen nicht mehr im selben CS2-Ring. Bei der L-Form
             * wird der verbliebene 108,845640-m2-Grasring in 40,335048 +
             * 68,510592 m2 geteilt. Seine alte Engstelle bleibt 0,219353 m und
             * damit ueber SurfaceNeckLimit 0,19 m.
             */
            var output = rings.Select(ring => ring.ToArray()).ToList();
            for (var guard = 0; guard < 64; guard++)
            {
                var features = SurfaceCs2NodeEdgeFeatures(output);
                if (features.Count == 0) break;
                Cs2SplitCandidate best = null;
                foreach (var feature in features)
                {
                    var ring = output[feature.Ring];
                    var n = ring.Length;
                    var before = SurfaceCs2NodeEdgeFeatures(
                        new List<double2[]> { ring }).Count;
                    foreach (var pair in new[]
                    {
                        (feature.Edge, feature.Point),
                        ((feature.Edge + 1) % n, feature.Point),
                    })
                    {
                        var a = Math.Min(pair.Item1, pair.Item2);
                        var b = Math.Max(pair.Item1, pair.Item2);
                        if (b - a < 2 || a == 0 && b == n - 1) continue;
                        var first = ring.Skip(a).Take(b - a + 1).ToArray();
                        var second = ring.Skip(b).Concat(ring.Take(a + 1)).ToArray();
                        if (first.Length < 3 || second.Length < 3
                            || SelfIntersects(first, 2e-6)
                            || SelfIntersects(second, 2e-6)) continue;
                        var wanted = Math.Abs(SignedArea(ring));
                        var firstArea = Math.Abs(SignedArea(first));
                        var secondArea = Math.Abs(SignedArea(second));
                        if (firstArea < 1e-10 || secondArea < 1e-10
                            || Math.Abs(firstArea + secondArea - wanted)
                                > Math.Max(1e-6, wanted * 1e-9)
                            || !SurfaceCs2Compatible(first)
                            || !SurfaceCs2Compatible(second)) continue;
                        var neck = MinimumSurfaceFeature(
                            new List<double2[]> { first, second });
                        if (neck != null
                            && neck.Distance < SurfaceNeckLimit - 1e-9) continue;
                        var after = SurfaceCs2NodeEdgeFeatures(
                            new List<double2[]> { first, second }).Count;
                        if (after >= before) continue;
                        var smallArea = Math.Min(firstArea, secondArea);
                        if (best != null && !(after < best.After
                            || after == best.After && smallArea < best.SmallArea))
                            continue;
                        best = new Cs2SplitCandidate
                        {
                            Ring = feature.Ring,
                            First = first,
                            Second = second,
                            After = after,
                            SmallArea = smallArea,
                        };
                    }
                }
                if (best == null) break;
                output.RemoveAt(best.Ring);
                output.Insert(best.Ring, best.Second);
                output.Insert(best.Ring, best.First);
            }
            return output;
        }

        private static void RepairCs2NodeEdges(
            List<double2[]> bays, ref List<double2[]> grass,
            ref List<double2[]> asphalt)
        {
            var grassNeckBefore = MinimumSurfaceFeature(grass)?.Distance
                ?? double.PositiveInfinity;
            var asphaltNeckBefore = MinimumSurfaceFeature(asphalt)?.Distance
                ?? double.PositiveInfinity;
            TransferCs2Slivers(grass, asphalt, bays,
                out var candidateGrass, out var candidateAsphalt);
            candidateGrass = SplitCs2NodeEdges(candidateGrass);
            candidateAsphalt = SplitCs2NodeEdges(candidateAsphalt);

            // Nur eine VOLLSTAENDIGE Reparatur uebernehmen. Am Nutzerpolygon
            // wurden aus 5 Verstoessen nur 2; die Teilreparatur machte daraus
            // 18 statt 17 Asphaltflaechen. Dann bleibt die bewiesene
            // Ausgangskachelung unangetastet.
            var grassNeckAfter = MinimumSurfaceFeature(candidateGrass)?.Distance
                ?? double.PositiveInfinity;
            var asphaltNeckAfter = MinimumSurfaceFeature(candidateAsphalt)?.Distance
                ?? double.PositiveInfinity;
            if (SurfaceCs2NodeEdgeFeatures(candidateGrass).Count == 0
                && SurfaceCs2NodeEdgeFeatures(candidateAsphalt).Count == 0
                && candidateGrass.Count <= grass.Count
                && candidateAsphalt.Count <= asphalt.Count
                && grassNeckAfter >= grassNeckBefore - 1e-9
                && asphaltNeckAfter >= asphaltNeckBefore - 1e-9)
            {
                grass = candidateGrass;
                asphalt = candidateAsphalt;
            }
        }
    }
}
