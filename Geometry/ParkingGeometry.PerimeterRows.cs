using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /** Randreihen nach derselben Kapselregel abschliessen. */
        private static void FinishPerimeterRows(WorkLayout output, double2[] site,
            LayoutSettings settings, double crossWidth, List<Line2> ringJunctionLines,
            List<double2[]> corridors, double rowDepth)
        {
            bool PerimeterKind(BayKind kind) =>
                kind == BayKind.Perimeter || kind == BayKind.InnerPerimeter;
            double2 Center(double2[] q) => q.Aggregate(new double2(0), (sum, p) => sum + p) / q.Length;
            double Dot(double2 a, double2 b) => a.x * b.x + a.y * b.y;
            var roads = new List<Road>();
            void AddRoads(IEnumerable<Line2> segments, double width,
                          bool supportsRow = false, string kind = "road")
            {
                foreach (var segment in segments)
                {
                    var vector = segment.B - segment.A;
                    var length = Len(vector);
                    if (length < 0.2) continue;
                    var u = vector / length;
                    var normal = new double2(-u.y * width / 2, u.x * width / 2);
                    roads.Add(new Road
                    {
                        Segment = segment,
                        U = u,
                        SupportsRow = supportsRow,
                        Kind = kind,
                        Quad = new[] { segment.A + normal, segment.B + normal,
                                       segment.B - normal, segment.A - normal },
                    });
                }
            }
            // Nur Ringsegmente tragen Randreihen; alle anderen koennen am Ende stehen.
            AddRoads(output.PerimeterLine, settings.Ai, true, "ring");
            AddRoads(ringJunctionLines, settings.Ai, false, "ring");
            AddRoads(output.AisleLine, settings.Ai, false, "aisle");
            AddRoads(output.CrossLine, crossWidth, false, "cross");
            AddRoads(output.EntranceLine, settings.Ai, false, "entrance");

            RowInfo RowData(double2[] quad)
            {
                var center = Center(quad);
                var u = Norm(quad[1] - quad[0]);
                var normal = Norm(quad[3] - quad[0]);
                Road support = null;
                var supportDistance = double.PositiveInfinity;
                foreach (var road in roads)
                {
                    if (!road.SupportsRow || Math.Abs(Dot(u, road.U)) < 1 - 1e-8) continue;
                    var distance = DistToSeg(center, road.Segment.A, road.Segment.B);
                    if (distance < supportDistance)
                    {
                        support = road;
                        supportDistance = distance;
                    }
                }
                if (support == null) return null;
                var plusIsRoad = PointIn(center + normal * (rowDepth / 2 + 0.1), support.Quad);
                return new RowInfo
                {
                    Center = center,
                    U = u,
                    Normal = normal,
                    Support = support,
                    Away = plusIsRoad ? -normal : normal,
                };
            }
            double2 EndEdge(RowInfo data, int sign) =>
                data.Center + data.U * (sign * settings.Sw / 2);
            double2[] Extension(double2 origin, RowInfo data, int sign,
                                double length, double depth) =>
                Rect(origin + data.U * (sign * length / 2), data.U, data.Normal,
                     length / 2, depth / 2);
            bool TouchingBay(int index, RowInfo data, int sign, HashSet<int> removed)
            {
                var probe = Extension(EndEdge(data, sign), data, sign, 0.1, rowDepth - 0.1);
                return output.Bay.Where((q, i) => i != index && !removed.Contains(i))
                    .Any(q => QuadsOverlap(probe, q, 0));
            }
            RoadHit RoadAhead(double2 origin, RowInfo data, int sign, double depth)
            {
                var maximum = 2 * settings.Sw + 0.1;
                var probe = Extension(origin, data, sign, maximum, depth);
                RoadHit best = null;
                foreach (var road in roads)
                {
                    var angle = Math.Acos(Math.Min(1, Math.Abs(Dot(data.U, road.U))))
                              * 180 / Math.PI;
                    // Unter 45 Grad laeuft die Strasse von der Reihe weg.
                    if (ReferenceEquals(road, data.Support) || angle < MinTransverseCapAngle
                        || !QuadsOverlap(probe, road.Quad, 0)) continue;
                    var lo = 0.0;
                    var hi = maximum;
                    for (var k = 0; k < 45; k++)
                    {
                        var middle = (lo + hi) / 2;
                        if (QuadsOverlap(Extension(origin, data, sign, middle, depth),
                                         road.Quad, 0)) hi = middle;
                        else lo = middle;
                    }
                    if (best == null || hi < best.Gap) best = new RoadHit { Gap = hi, Road = road };
                }
                return best;
            }
            string CapKindAt(RoadHit hit, RowInfo data)
            {
                if (hit.Road.Kind != "ring" || hit.Road.SupportsRow)
                    return hit.Road.Kind;
                var angle = Math.Acos(Math.Min(1, Math.Abs(Dot(data.U, hit.Road.U))))
                          * 180 / Math.PI;
                // Ein schraeger Ring-Eckstummel beruehrt nur eine Ecke. Die
                // gegenueberliegende Kappenseite endet an der freien Kontur.
                return angle < 90 - 1e-8 ? "clip" : "ring";
            }

            var removed = new HashSet<int>();
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var i = 0; i < output.Bay.Count; i++)
                {
                    if (removed.Contains(i) || !PerimeterKind(output.BayKind[i])) continue;
                    var data = RowData(output.Bay[i]);
                    if (data == null) continue;
                    foreach (var sign in new[] { -1, 1 })
                    {
                        if (TouchingBay(i, data, sign, removed)) continue;
                        var hit = RoadAhead(EndEdge(data, sign), data, sign, rowDepth);
                        if (!settings.Qk && hit != null && hit.Road.Kind == "cross")
                            continue;
                        if (hit == null || hit.Gap >= settings.Sw - FitEps) continue;
                        removed.Add(i);
                        changed = true;
                        break;
                    }
                }
            }

            if (removed.Count > 0)
            {
                var bays = new List<double2[]>();
                var kinds = new List<BayKind>();
                for (var i = 0; i < output.Bay.Count; i++)
                {
                    if (removed.Contains(i)) continue;
                    bays.Add(output.Bay[i]);
                    kinds.Add(output.BayKind[i]);
                }
                output.Bay = bays;
                output.BayKind = kinds;
                output.PerimeterStalls = kinds.Count(PerimeterKind);
                output.InnerPerimeterStalls = kinds.Count(x => x == BayKind.InnerPerimeter);
                output.InnerStalls = kinds.Count(x => x == BayKind.Inner);
                output.ExtraStalls = kinds.Count(x => x == BayKind.Extra);
            }

            var noneRemoved = new HashSet<int>();
            for (var i = 0; i < output.Bay.Count; i++)
            {
                if (!PerimeterKind(output.BayKind[i])) continue;
                var data = RowData(output.Bay[i]);
                if (data == null) continue;
                foreach (var sign in new[] { -1, 1 })
                {
                    if (TouchingBay(i, data, sign, noneRemoved)) continue;
                    // Beide Randreihen haben keinen Mittelstreifen hinter sich.
                    // Vorher bekam rand-innen 8,0 statt 5,5 m und lieferte 7 statt
                    // 8 Buchten je Reihe.
                    var outer = output.BayKind[i] == BayKind.Perimeter
                             || output.BayKind[i] == BayKind.InnerPerimeter;
                    var capDepth = outer ? rowDepth : rowDepth + settings.Md;
                    var origin = outer ? EndEdge(data, sign)
                        : EndEdge(data, sign) + data.Away * (settings.Md / 2);
                    var hit = RoadAhead(origin, data, sign, capDepth);
                    if (!settings.Qk && hit != null && hit.Road.Kind == "cross")
                    {
                        // Ohne Querstrassenkappe bleibt der Rasterrest Belag.
                        // Randreihen entstehen nicht in EmitRowCapsAndStrips,
                        // deshalb wird er hier eigens mitgefuehrt.
                        if (hit.Gap > FitEps)
                        {
                            var pavement = Extension(origin, data, sign, hit.Gap, capDepth);
                            if (pavement.All(p => InsideBy(p, site, settings.Es))
                                && !corridors.Any(q => QuadsOverlap(pavement, q, 0.05))
                                && !output.Bay.Any(q => QuadsOverlap(pavement, q, 0.05)))
                                output.CrossPavement.Add(pavement);
                        }
                        continue;
                    }
                    if (hit == null || hit.Gap < settings.Sw - FitEps
                        || hit.Gap >= 2 * settings.Sw - FitEps) continue;
                    var cap = Extension(origin, data, sign, hit.Gap, capDepth);
                    if (!cap.All(p => InsideBy(p, site, settings.Es))) continue;
                    if (corridors.Any(q => QuadsOverlap(cap, q, 0.05))) continue;
                    if (output.Bay.Any(q => QuadsOverlap(cap, q, 0.05))) continue;
                    if (output.Green.Concat(output.Median).Concat(output.Cap)
                        .Any(q => QuadsOverlap(cap, q, 0.05))) continue;
                    PushCap(output, cap, CapKindAt(hit, data));
                }
            }
        }

        private sealed class RowInfo
        {
            internal double2 Center;
            internal double2 U;
            internal double2 Normal;
            internal Road Support;
            internal double2 Away;
        }

        private sealed class RoadHit { internal double Gap; internal Road Road; }
    }
}
