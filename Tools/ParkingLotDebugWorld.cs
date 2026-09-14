using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private const int WorldSearchCandidateLimit = 50000;
        private const int GlobalAreaScanLimit = 50000;
        private const int CurveSubdivisions = 32;

        private struct NetDebugIterator
            : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public int Limit;
            public int Matches;

            public bool Intersect(QuadTreeBoundsXZ bounds)
                => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (!MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) return;
                Matches++;
                if (Results.Length < Limit) Results.Add(entity);
            }
        }

        private struct AreaDebugIterator
            : INativeQuadTreeIterator<AreaSearchItem, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<AreaSearchItem> Results;
            public int Limit;
            public int Matches;

            public bool Intersect(QuadTreeBoundsXZ bounds)
                => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, AreaSearchItem item)
            {
                if (!MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) return;
                Matches++;
                if (Results.Length < Limit) Results.Add(item);
            }
        }

        private DebugWorld CaptureExistingWorld(float2[] polygon)
        {
            var timer = Stopwatch.StartNew();
            var result = new DebugWorld
            {
                Captured = false,
                CandidateLimitPerSpatialSearch = WorldSearchCandidateLimit,
                GlobalAreaScanLimit = GlobalAreaScanLimit,
                CurveSubdivisions = CurveSubdivisions,
            };
            if (polygon == null || polygon.Length < 3)
            {
                result.Note = "Ohne Polygon wurden vorhandene Weltobjekte nicht abgefragt.";
                result.CaptureMilliseconds = timer.Elapsed.TotalMilliseconds;
                return result;
            }

            var bounds = PolygonBounds(polygon);
            var searchBounds = new Bounds2(bounds.min - 0.1f, bounds.max + 0.1f);
            var nets = CaptureExistingNets(polygon, searchBounds,
                out var netMatches, out var netStored, out var netsTruncated);
            var areas = CaptureExistingAreas(polygon, searchBounds,
                out var areaEntityCount, out var areasScanned,
                out var areaMatches, out var areaStored,
                out var areasTruncated, out var areaMethod);

            timer.Stop();
            result.Captured = true;
            result.SelectionMethod = "Netze: CS2-Netzsuchbaum und konservative "
                + "EdgeGeometry-Grenze; Flächen: " + areaMethod + ".";
            result.NetCandidateMatches = netMatches;
            result.NetCandidatesStored = netStored;
            result.NetsTruncated = netsTruncated;
            result.NetsIncluded = nets.Count;
            result.AreaEntityCount = areaEntityCount;
            result.AreasScanned = areasScanned;
            result.AreaCandidateMatches = areaMatches;
            result.AreaCandidatesStored = areaStored;
            result.AreasTruncated = areasTruncated;
            result.AreasIncluded = areas.Count;
            result.CaptureMilliseconds = timer.Elapsed.TotalMilliseconds;
            result.Nets = nets.ToArray();
            result.Areas = areas.ToArray();
            result.Note = netsTruncated || areasTruncated
                ? "Mindestens eine dokumentierte Abfragegrenze oder deren räumlicher "
                    + "Fallback griff; die betroffene Liste ist ausdrücklich als "
                    + "truncated markiert."
                : "Keine Abfragegrenze wurde erreicht. Bounds-only bei Netzen ist "
                    + "bewusst konservativ: lieber ein nahes Netz zusätzlich als eine "
                    + "Straßenüberdeckung zu übersehen.";
            return result;
        }

        private List<DebugNet> CaptureExistingNets(
            float2[] polygon,
            Bounds2 bounds,
            out int matches,
            out int stored,
            out bool truncated)
        {
            var tree = _netSearchSystem.GetNetSearchTree(
                readOnly: true, out var dependencies);
            dependencies.Complete();
            using var candidates = new NativeList<Entity>(256, Allocator.TempJob);
            var iterator = new NetDebugIterator
            {
                Bounds = bounds,
                Results = candidates,
                Limit = WorldSearchCandidateLimit,
            };
            tree.Iterate(ref iterator);
            matches = iterator.Matches;
            stored = candidates.Length;
            truncated = iterator.Matches > candidates.Length;

            var output = new List<DebugNet>();
            var seen = new HashSet<Entity>();
            for (var i = 0; i < candidates.Length; i++)
            {
                var entity = candidates[i];
                if (!seen.Add(entity)) continue;
                var item = CaptureNet(entity, polygon);
                if (item != null) output.Add(item);
            }
            return output;
        }

        private DebugNet CaptureNet(Entity entity, float2[] polygon)
        {
            if (!EntityManager.Exists(entity)
                || EntityManager.HasComponent<Temp>(entity)
                || EntityManager.HasComponent<Deleted>(entity)
                || !EntityManager.HasComponent<Edge>(entity)
                || !EntityManager.HasComponent<Curve>(entity)
                || !EntityManager.HasComponent<PrefabRef>(entity))
                return null;

            var curveData = EntityManager.GetComponentData<Curve>(entity);
            var curve = curveData.m_Bezier;
            Bounds3 geometryBounds;
            if (EntityManager.HasComponent<EdgeGeometry>(entity))
                geometryBounds = EntityManager.GetComponentData<EdgeGeometry>(entity).m_Bounds;
            else
                geometryBounds = CurveBounds(curve);
            if (!RectangleIntersectsPolygon(geometryBounds.xz, polygon)) return null;

            float? compositionWidth = null;
            if (EntityManager.HasComponent<Composition>(entity))
            {
                var composition = EntityManager.GetComponentData<Composition>(entity);
                if (composition.m_Edge != Entity.Null
                    && EntityManager.HasComponent<NetCompositionData>(composition.m_Edge))
                {
                    compositionWidth = EntityManager
                        .GetComponentData<NetCompositionData>(composition.m_Edge).m_Width;
                }
            }

            var prefabEntity = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            var prefabExists = prefabEntity != Entity.Null
                && EntityManager.Exists(prefabEntity);
            float? defaultWidth = null;
            if (prefabExists
                && EntityManager.HasComponent<NetGeometryData>(prefabEntity))
                defaultWidth = EntityManager
                    .GetComponentData<NetGeometryData>(prefabEntity).m_DefaultWidth;

            var edge = EntityManager.GetComponentData<Edge>(entity);
            return new DebugNet
            {
                Entity = DebugEntity.From(entity),
                Prefab = DescribePrefab(prefabEntity),
                Kind = NetKind(entity),
                IntersectionEvidence = NetIntersectionEvidence(curve, polygon,
                    compositionWidth ?? defaultWidth ?? 0f),
                Owner = EntityManager.HasComponent<Owner>(entity)
                    ? DebugEntity.From(EntityManager.GetComponentData<Owner>(entity).m_Owner)
                    : null,
                StartNode = DebugEntity.From(edge.m_Start),
                StartNodePosition = NetNodePosition(edge.m_Start),
                EndNode = DebugEntity.From(edge.m_End),
                EndNodePosition = NetNodePosition(edge.m_End),
                Curve = new DebugBezier
                {
                    A = DebugPoint3.From(curve.a),
                    B = DebugPoint3.From(curve.b),
                    C = DebugPoint3.From(curve.c),
                    D = DebugPoint3.From(curve.d),
                },
                CurveLength = curveData.m_Length,
                CompositionWidth = compositionWidth,
                PrefabDefaultWidth = defaultWidth,
                GeometryBounds = ToDebugBounds(geometryBounds),
            };
        }

        private List<DebugExistingArea> CaptureExistingAreas(
            float2[] polygon,
            Bounds2 bounds,
            out int entityCount,
            out int scanned,
            out int matches,
            out int stored,
            out bool truncated,
            out string method)
        {
            entityCount = _worldAreaQuery.CalculateEntityCount();
            matches = 0;
            stored = 0;
            truncated = false;
            var output = new List<DebugExistingArea>();

            if (entityCount <= GlobalAreaScanLimit)
            {
                method = "vollständige EntityQuery über alle permanenten Areas";
                using var entities = _worldAreaQuery.ToEntityArray(Allocator.TempJob);
                scanned = entities.Length;
                for (var i = 0; i < entities.Length; i++)
                {
                    var item = CaptureArea(entities[i], polygon);
                    if (item != null) output.Add(item);
                }
                return output;
            }

            method = "begrenzter CS2-Area-Suchbaum, weil die globale Area-Zahl "
                + $"die Grenze {GlobalAreaScanLimit} überschreitet; nicht triangulierte "
                + "Areas stehen in diesem Suchbaum nicht zur Verfügung";
            var tree = _areaSearchSystem.GetSearchTree(
                readOnly: true, out var dependencies);
            dependencies.Complete();
            using var candidates =
                new NativeList<AreaSearchItem>(512, Allocator.TempJob);
            var iterator = new AreaDebugIterator
            {
                Bounds = bounds,
                Results = candidates,
                Limit = WorldSearchCandidateLimit,
            };
            tree.Iterate(ref iterator);
            matches = iterator.Matches;
            stored = candidates.Length;
            // Bereits das Überschreiten der globalen Grenze macht die Liste
            // unvollständig, selbst wenn der lokale Suchbaum sein Limit nicht trifft.
            truncated = true;

            var seen = new HashSet<Entity>();
            for (var i = 0; i < candidates.Length; i++)
            {
                var entity = candidates[i].m_Area;
                if (!seen.Add(entity)) continue;
                var item = CaptureArea(entity, polygon);
                if (item != null) output.Add(item);
            }
            scanned = seen.Count;
            return output;
        }

        private DebugExistingArea CaptureArea(Entity entity, float2[] polygon)
        {
            if (!EntityManager.Exists(entity)
                || EntityManager.HasComponent<Temp>(entity)
                || EntityManager.HasComponent<Deleted>(entity)
                || !EntityManager.HasComponent<Game.Areas.Area>(entity)
                || !EntityManager.HasBuffer<Game.Areas.Node>(entity)
                || !EntityManager.HasComponent<PrefabRef>(entity))
                return null;

            var nodes = EntityManager.GetBuffer<Game.Areas.Node>(entity, true);
            var nodeCount = OpenAreaNodeCount(nodes);
            if (nodeCount < 3) return null;
            var positions = new float3[nodeCount];
            var elevations = new float[nodeCount];
            var polygon2 = new float2[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                positions[i] = nodes[i].m_Position;
                elevations[i] = nodes[i].m_Elevation;
                polygon2[i] = nodes[i].m_Position.xz;
            }
            var evidence = PolygonIntersectionEvidence(polygon, polygon2);
            if (evidence == null) return null;

            var area = EntityManager.GetComponentData<Game.Areas.Area>(entity);
            var prefabEntity = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            var prefab = DescribePrefab(prefabEntity);
            var hasGeometry = EntityManager.HasComponent<Game.Areas.Geometry>(entity);
            var geometry = hasGeometry
                ? EntityManager.GetComponentData<Game.Areas.Geometry>(entity)
                : default;
            return new DebugExistingArea
            {
                Entity = DebugEntity.From(entity),
                Prefab = prefab,
                Kind = prefabEntity != Entity.Null
                    && EntityManager.Exists(prefabEntity)
                    && EntityManager.HasComponent<SurfaceData>(prefabEntity)
                    ? "Area mit SurfaceData"
                    : "Area / " + (prefab.Type ?? "Prefabtyp nicht auflösbar"),
                IntersectionEvidence = evidence,
                Owner = EntityManager.HasComponent<Owner>(entity)
                    ? DebugEntity.From(EntityManager.GetComponentData<Owner>(entity).m_Owner)
                    : null,
                Nodes = DebugAreaNode.From(positions, elevations),
                AreaFlags = area.m_Flags.ToString(),
                AreaFlagsValue = (int)area.m_Flags,
                TriangleCount = EntityManager.HasBuffer<Triangle>(entity)
                    ? (int?)EntityManager.GetBuffer<Triangle>(entity, true).Length
                    : null,
                GeometryBounds = hasGeometry ? ToDebugBounds(geometry.m_Bounds) : null,
                GeometryCenter = hasGeometry ? DebugPoint3.From(geometry.m_CenterPosition) : null,
                SurfaceArea = hasGeometry ? (float?)geometry.m_SurfaceArea : null,
            };
        }

        private string NetKind(Entity entity)
        {
            var kinds = new List<string>();
            if (EntityManager.HasComponent<Game.Net.Road>(entity)) kinds.Add("Road");
            if (EntityManager.HasComponent<TrainTrack>(entity)) kinds.Add("TrainTrack");
            if (EntityManager.HasComponent<TramTrack>(entity)) kinds.Add("TramTrack");
            if (EntityManager.HasComponent<SubwayTrack>(entity)) kinds.Add("SubwayTrack");
            if (EntityManager.HasComponent<Waterway>(entity)) kinds.Add("Waterway");
            if (EntityManager.HasComponent<Taxiway>(entity)) kinds.Add("Taxiway");
            return kinds.Count == 0 ? "Net.Edge" : string.Join(", ", kinds);
        }

        private DebugPoint3 NetNodePosition(Entity node)
        {
            return node != Entity.Null && EntityManager.Exists(node)
                && EntityManager.HasComponent<Game.Net.Node>(node)
                ? DebugPoint3.From(
                    EntityManager.GetComponentData<Game.Net.Node>(node).m_Position)
                : null;
        }

        private static string NetIntersectionEvidence(
            Bezier4x3 curve,
            float2[] polygon,
            float width)
        {
            var previous = curve.a.xz;
            if (PointInPolygonInclusive(previous, polygon))
                return "Kurvenpunkt liegt im Polygon";
            var halfWidthSquared = width > 0f ? width * width * 0.25f : 0f;
            for (var step = 1; step <= CurveSubdivisions; step++)
            {
                var current = MathUtils.Position(
                    curve, step / (float)CurveSubdivisions).xz;
                if (PointInPolygonInclusive(current, polygon))
                    return "Kurvenpunkt liegt im Polygon";
                for (var edge = 0; edge < polygon.Length; edge++)
                {
                    var next = (edge + 1) % polygon.Length;
                    if (SegmentsIntersect(previous, current,
                        polygon[edge], polygon[next]))
                        return "abgetastete Netzmittellinie schneidet Polygonkante";
                }
                if (halfWidthSquared > 0f)
                {
                    for (var point = 0; point < polygon.Length; point++)
                    {
                        if (PointSegmentDistanceSquared(
                            polygon[point], previous, current) <= halfWidthSquared)
                            return "Polygonpunkt liegt innerhalb der gemeldeten Netzbreite";
                    }
                }
                previous = current;
            }
            return "nur EdgeGeometry-Bounds schneiden das Polygon (konservativ)";
        }

        private static string PolygonIntersectionEvidence(float2[] a, float2[] b)
        {
            for (var i = 0; i < b.Length; i++)
                if (PointInPolygonInclusive(b[i], a))
                    return "Area-Knoten liegt im gezeichneten Polygon";
            for (var i = 0; i < a.Length; i++)
                if (PointInPolygonInclusive(a[i], b))
                    return "Polygonpunkt liegt in der Area";
            for (var i = 0; i < a.Length; i++)
            {
                var ai = (i + 1) % a.Length;
                for (var j = 0; j < b.Length; j++)
                {
                    var bj = (j + 1) % b.Length;
                    if (SegmentsIntersect(a[i], a[ai], b[j], b[bj]))
                        return "Polygon- und Area-Kanten schneiden sich";
                }
            }
            return null;
        }

        private static bool RectangleIntersectsPolygon(Bounds2 bounds, float2[] polygon)
        {
            var rectangle = new[]
            {
                bounds.min,
                new float2(bounds.max.x, bounds.min.y),
                bounds.max,
                new float2(bounds.min.x, bounds.max.y),
            };
            return PolygonIntersectionEvidence(polygon, rectangle) != null;
        }

        private static bool PointInPolygonInclusive(float2 point, float2[] polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[j];
                var b = polygon[i];
                if (PointSegmentDistanceSquared(point, a, b) <= 1e-8f) return true;
                if ((a.y > point.y) == (b.y > point.y)) continue;
                var crossing = a.x + (point.y - a.y) / (b.y - a.y) * (b.x - a.x);
                if (point.x < crossing) inside = !inside;
            }
            return inside;
        }

        private static bool SegmentsIntersect(float2 a, float2 b, float2 c, float2 d)
        {
            var abC = Cross(b - a, c - a);
            var abD = Cross(b - a, d - a);
            var cdA = Cross(d - c, a - c);
            var cdB = Cross(d - c, b - c);
            const float epsilon = 1e-5f;
            if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon))
                && ((cdA > epsilon && cdB < -epsilon)
                    || (cdA < -epsilon && cdB > epsilon)))
                return true;
            return math.abs(abC) <= epsilon && PointOnSegment(c, a, b)
                || math.abs(abD) <= epsilon && PointOnSegment(d, a, b)
                || math.abs(cdA) <= epsilon && PointOnSegment(a, c, d)
                || math.abs(cdB) <= epsilon && PointOnSegment(b, c, d);
        }

        private static bool PointOnSegment(float2 point, float2 a, float2 b)
        {
            const float epsilon = 1e-5f;
            return point.x >= math.min(a.x, b.x) - epsilon
                && point.x <= math.max(a.x, b.x) + epsilon
                && point.y >= math.min(a.y, b.y) - epsilon
                && point.y <= math.max(a.y, b.y) + epsilon;
        }

        private static float PointSegmentDistanceSquared(float2 point, float2 a, float2 b)
        {
            var delta = b - a;
            var lengthSquared = math.lengthsq(delta);
            if (lengthSquared < 1e-10f) return math.lengthsq(point - a);
            var t = math.clamp(math.dot(point - a, delta) / lengthSquared, 0f, 1f);
            return math.lengthsq(point - (a + delta * t));
        }

        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        private static Bounds2 PolygonBounds(float2[] polygon)
        {
            var minimum = polygon[0];
            var maximum = polygon[0];
            for (var i = 1; i < polygon.Length; i++)
            {
                minimum = math.min(minimum, polygon[i]);
                maximum = math.max(maximum, polygon[i]);
            }
            return new Bounds2(minimum, maximum);
        }

        private static Bounds3 CurveBounds(Bezier4x3 curve)
        {
            var minimum = math.min(math.min(curve.a, curve.b), math.min(curve.c, curve.d));
            var maximum = math.max(math.max(curve.a, curve.b), math.max(curve.c, curve.d));
            return new Bounds3(minimum, maximum);
        }

        private static DebugBounds3 ToDebugBounds(Bounds3 bounds) => new DebugBounds3
        {
            Minimum = DebugPoint3.From(bounds.min),
            Maximum = DebugPoint3.From(bounds.max),
        };

        private static int OpenAreaNodeCount(DynamicBuffer<Game.Areas.Node> nodes)
        {
            var count = nodes.Length;
            if (count > 1 && math.all(nodes[0].m_Position == nodes[count - 1].m_Position))
                count--;
            return count;
        }
    }
}
