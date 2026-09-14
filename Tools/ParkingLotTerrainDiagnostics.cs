using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Misst den Terrainwechsel eines Baus an denselben Weltpunkten und nennt
     * den Entity-Pfad, der ihn ausloest.
     *
     * Der entscheidende Unterschied zu einer nachtraeglichen Objektkorrektur:
     * Hier wird nichts versetzt. Vor `Apply` werden nur Hoehen und die von
     * LocalConnect erzeugten Temp-Kopien fremder Netze gelesen. Nach `Apply`
     * wird derselbe CPU-Hoehenpuffer erneut gelesen. Die Messung kann dadurch
     * Ursache und Folge auseinanderhalten, ohne den beobachteten Ablauf zu
     * veraendern.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class TerrainTraceSample
        {
            internal string Group;
            internal string Kind;
            internal int Index;
            internal int Ordinal;
            internal float2 Point;
            internal float Before;
            internal float After;
            internal Entity ObjectEntity;
            internal float3 ObjectBeforeApply;
            internal int ActorIndex = -1;
        }

        private sealed class TerrainTraceActor
        {
            internal Entity TempEntity;
            internal Entity Original;
            internal Entity Prefab;
            internal string PrefabName;
            internal Game.Net.GeometryFlags GeometryFlags;
            internal TempFlags TempFlags;
            internal Bezier4x3 Curve;
            internal bool TerrainActive;
            internal bool ConnectionProven;
            internal Entity TriggerNode;
            internal string TriggerKind;
            internal int TriggerIndex;
            internal string TriggerEndpoint;
            internal float TriggerDistance;
        }

        private sealed class TerrainBuildTrace
        {
            internal int Revision;
            internal int CapturedFrame;
            internal int ApplyObservedFrame = -1;
            internal bool AfterCaptured;
            internal readonly List<TerrainTraceSample> Samples =
                new List<TerrainTraceSample>();
            internal readonly List<TerrainTraceActor> Actors =
                new List<TerrainTraceActor>();
            internal readonly HashSet<Entity> MovementLogged =
                new HashSet<Entity>();
        }

        private sealed class OwnNetPrefabSummary
        {
            internal Entity Prefab;
            internal string Name;
            internal Game.Net.GeometryFlags Flags;
            internal int Courses;
            internal readonly HashSet<string> Kinds =
                new HashSet<string>(StringComparer.Ordinal);
        }

        private EntityQuery _terrainTempEdgeQuery;
        private TerrainBuildTrace _terrainBuildTrace;

        private void InitializeTerrainDiagnostics()
        {
            _terrainTempEdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Edge>(),
                    ComponentType.ReadOnly<Curve>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        /**
         * Letzter Zustand vor dem einzigen `ApplyMode.Apply` des Baus.
         *
         * Die Temp-Netze koennen das Terrain nicht veraendern; gleichzeitig
         * sind hier die von GenerateEdgesSystem erzeugten Kopien bestehender
         * Stadtstrassen schon sichtbar. Deren `Temp.m_Original` ist der exakte
         * Verweis auf die Entity, die ApplyNetSystem als `Updated` markieren
         * wird.
         */
        private void CaptureTerrainBeforeApply()
        {
            try
            {
                if (_terrainBuildTrace != null
                    && !_terrainBuildTrace.AfterCaptured)
                {
                    Mod.log.Warn("PLT-Terrainmessung: Der vorige Bau erhielt "
                        + "vor dem naechsten Bau keine Nachhermessung; seine "
                        + "Vorherdaten werden jetzt ersetzt.");
                }

                var trace = new TerrainBuildTrace
                {
                    Revision = _areaTransferRevision ?? _lastPreviewRevision,
                    CapturedFrame = UnityEngine.Time.frameCount,
                };
                var heightData = _terrainSystem.GetHeightData(waitForPending: true);
                AddObjectTerrainSamples(trace, ref heightData);
                AddNetTerrainSamples(trace, ref heightData);
                AddAreaTerrainSamples(trace, ref heightData);

                var ownTerrainPrefabs = LogOwnNetTerrainFlags();
                var terrainAreas = CountTerrainActiveAreas();
                CaptureForeignTerrainActors(trace, ref heightData);
                _terrainBuildTrace = trace;
                BeginGroundHeightTrace(trace);

                var activeActors = CountTerrainActiveActors(trace);
                Mod.log.Info("PLT-Terrainmessung VOR Apply: "
                    + $"Stand {trace.Revision}, {trace.Samples.Count} feste "
                    + "Messstellen; terrainaktive eigene Flaechen "
                    + $"{terrainAreas}/{_areaTransferRecords.Count}, "
                    + $"terrainaktive eigene Netzprefabs {ownTerrainPrefabs}, "
                    + $"regenerierte fremde Netzkanten {trace.Actors.Count}, "
                    + $"davon terrainaktiv {activeActors}. Alle Hoehen stammen "
                    + "aus GetHeightData(waitForPending: true), noch vor dem "
                    + "einzigen Apply.");

                if (activeActors == 0)
                    Mod.log.Info("PLT-Terrainmessung: Vor Apply ist kein "
                        + "terrainaktiver Netzakteur in der Transaktion sichtbar. "
                        + "Eine Nachhermessung wird nur bei einem beobachteten "
                        + "Terrain-/Objektereignis geschrieben; daraus wird nicht "
                        + "geschlossen, dass ausserhalb der Messstellen nichts geschah.");
            }
            catch (Exception exception)
            {
                _terrainBuildTrace = null;
                // Eine Diagnose darf den bestaetigten Bauweg nicht blockieren.
                Mod.log.Error(exception, "PLT-Terrainmessung VOR Apply "
                    + "fehlgeschlagen; der Bau wird unveraendert fortgesetzt.");
            }
        }

        private void AddObjectTerrainSamples(TerrainBuildTrace trace,
                                              ref TerrainHeightData heightData)
        {
            using var entities = _tempObjectQuery.ToEntityArray(Allocator.Temp);
            var used = new HashSet<Entity>();
            for (var recordIndex = 0;
                recordIndex < _objectRecords.Count; recordIndex++)
            {
                var record = _objectRecords[recordIndex];
                var best = Entity.Null;
                var bestDistance = float.PositiveInfinity;
                var bestPosition = record.From;
                for (var entityIndex = 0;
                    entityIndex < entities.Length; entityIndex++)
                {
                    var candidate = entities[entityIndex];
                    if (used.Contains(candidate)
                        || !EntityManager.Exists(candidate)) continue;
                    var prefab = EntityManager
                        .GetComponentData<PrefabRef>(candidate).m_Prefab;
                    if (prefab != record.Prefab) continue;
                    var position = EntityManager
                        .GetComponentData<Game.Objects.Transform>(candidate)
                        .m_Position;
                    var distance = math.distancesq(position, record.From);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = candidate;
                    bestPosition = position;
                }
                if (best != Entity.Null) used.Add(best);
                AddTerrainSample(trace, "Objekt", record.Kind, record.Index, 0,
                    record.From.xz, best, bestPosition, -1, ref heightData);
            }
        }

        private void AddNetTerrainSamples(TerrainBuildTrace trace,
                                           ref TerrainHeightData heightData)
        {
            for (var i = 0; i < _netRecords.Count; i++)
            {
                var record = _netRecords[i];
                AddTerrainSample(trace, "Netz", record.Kind, record.Index, 0,
                    record.From.xz, Entity.Null, default(float3), -1,
                    ref heightData);
                AddTerrainSample(trace, "Netz", record.Kind, record.Index, 1,
                    (record.From.xz + record.To.xz) * 0.5f,
                    Entity.Null, default(float3), -1, ref heightData);
                AddTerrainSample(trace, "Netz", record.Kind, record.Index, 2,
                    record.To.xz, Entity.Null, default(float3), -1,
                    ref heightData);
            }
        }

        private void AddAreaTerrainSamples(TerrainBuildTrace trace,
                                            ref TerrainHeightData heightData)
        {
            for (var recordIndex = 0;
                recordIndex < _areaTransferRecords.Count; recordIndex++)
            {
                var record = _areaTransferRecords[recordIndex];
                var nodes = record.SentNodes ?? Array.Empty<float3>();
                var count = TerrainOpenNodeCount(nodes);
                for (var nodeIndex = 0; nodeIndex < count; nodeIndex++)
                    AddTerrainSample(trace, "Flaeche", record.Kind, record.Index,
                        nodeIndex, nodes[nodeIndex].xz, Entity.Null,
                        default(float3), -1, ref heightData);
            }
        }

        private void AddTerrainSample(
            TerrainBuildTrace trace,
            string group,
            string kind,
            int index,
            int ordinal,
            float2 point,
            Entity objectEntity,
            float3 objectBeforeApply,
            int actorIndex,
            ref TerrainHeightData heightData)
        {
            var before = TerrainUtils.SampleHeight(ref heightData,
                new float3(point.x, 0f, point.y));
            if (!math.isfinite(before))
            {
                Mod.log.Warn("PLT-Terrainmessung: nicht-endliche Vorherhoehe "
                    + $"fuer {group} {kind} #{index}/{ordinal} bei "
                    + $"{point.x:F2}/{point.y:F2}; Messstelle ausgelassen.");
                return;
            }
            trace.Samples.Add(new TerrainTraceSample
            {
                Group = group,
                Kind = kind,
                Index = index,
                Ordinal = ordinal,
                Point = point,
                Before = before,
                After = before,
                ObjectEntity = objectEntity,
                ObjectBeforeApply = objectBeforeApply,
                ActorIndex = actorIndex,
            });
        }

        private int LogOwnNetTerrainFlags()
        {
            var summaries = new Dictionary<Entity, OwnNetPrefabSummary>();
            for (var i = 0; i < _netRecords.Count; i++)
            {
                var record = _netRecords[i];
                if (!summaries.TryGetValue(record.Prefab, out var summary))
                {
                    summary = new OwnNetPrefabSummary
                    {
                        Prefab = record.Prefab,
                        Name = TerrainPrefabName(record.Prefab),
                        Flags = TerrainNetFlags(record.Prefab),
                    };
                    summaries.Add(record.Prefab, summary);
                }
                summary.Courses++;
                summary.Kinds.Add(record.Kind ?? "(ohne Art)");
            }

            var terrainActive = 0;
            foreach (var summary in summaries.Values)
            {
                var active = IsTerrainActive(summary.Flags);
                if (active) terrainActive++;
                Mod.log.Info("PLT-Terrainprefab Netz: "
                    + $"'{summary.Name}', {summary.Courses} Kurse "
                    + $"[{string.Join(", ", summary.Kinds)}], Flags "
                    + $"{summary.Flags} -> FlattenTerrain "
                    + $"{JaNein((summary.Flags & Game.Net.GeometryFlags.FlattenTerrain) != 0)}, "
                    + "ClipTerrain "
                    + JaNein((summary.Flags & Game.Net.GeometryFlags.ClipTerrain) != 0));
            }
            return terrainActive;
        }

        private int CountTerrainActiveAreas()
        {
            var count = 0;
            for (var i = 0; i < _areaTransferRecords.Count; i++)
            {
                var prefab = _areaTransferRecords[i].Prefab;
                if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                    || !EntityManager.HasComponent<AreaGeometryData>(prefab))
                    continue;
                var flags = EntityManager
                    .GetComponentData<AreaGeometryData>(prefab).m_Flags;
                if ((flags & Game.Areas.GeometryFlags.ShiftTerrain) != 0)
                    count++;
            }
            return count;
        }

        private void CaptureForeignTerrainActors(
            TerrainBuildTrace trace,
            ref TerrainHeightData heightData)
        {
            using var edges = _terrainTempEdgeQuery
                .ToEntityArray(Allocator.Temp);
            var originals = new HashSet<Entity>();
            for (var i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                var temp = EntityManager.GetComponentData<Temp>(edge);
                if (temp.m_Original == Entity.Null
                    || !EntityManager.Exists(temp.m_Original)
                    || originals.Contains(temp.m_Original)) continue;
                var prefab = EntityManager
                    .GetComponentData<PrefabRef>(edge).m_Prefab;
                if (IsOwnPathPrefab(prefab)) continue;

                originals.Add(temp.m_Original);
                var curve = EntityManager.GetComponentData<Curve>(edge).m_Bezier;
                var actor = new TerrainTraceActor
                {
                    TempEntity = edge,
                    Original = temp.m_Original,
                    Prefab = prefab,
                    PrefabName = TerrainPrefabName(prefab),
                    GeometryFlags = TerrainNetFlags(prefab),
                    TempFlags = temp.m_Flags,
                    Curve = curve,
                };
                actor.TerrainActive = IsTerrainActive(actor.GeometryFlags);
                FindTriggerCourse(edge, actor);
                var actorIndex = trace.Actors.Count;
                trace.Actors.Add(actor);

                AddTerrainSample(trace, "Fremdnetz", actor.PrefabName,
                    actor.Original.Index, 0, curve.a.xz, Entity.Null,
                    default(float3), actorIndex, ref heightData);
                var middle = MathUtils.Position(curve, 0.5f);
                AddTerrainSample(trace, "Fremdnetz", actor.PrefabName,
                    actor.Original.Index, 1, middle.xz, Entity.Null,
                    default(float3), actorIndex, ref heightData);
                AddTerrainSample(trace, "Fremdnetz", actor.PrefabName,
                    actor.Original.Index, 2, curve.d.xz, Entity.Null,
                    default(float3), actorIndex, ref heightData);

                var source = actor.ConnectionProven
                    ? "ConnectedNode belegt"
                    : "nur raeumlich naechster Kurs";
                Mod.log.Info("PLT-Terrainakteur VOR Apply: Temp-Kante "
                    + $"{Show(actor.TempEntity)} -> bestehendes Original "
                    + $"{Show(actor.Original)}, Prefab '{actor.PrefabName}', "
                    + $"Flags {actor.GeometryFlags}, TempFlags "
                    + $"{actor.TempFlags}; terrainaktiv "
                    + $"{JaNein(actor.TerrainActive)}. Ausloeserbezug "
                    + $"({source}): {actor.TriggerKind} "
                    + $"#{actor.TriggerIndex} Endpunkt "
                    + $"{actor.TriggerEndpoint}, Abstand "
                    + $"{actor.TriggerDistance:F3} m"
                    + (actor.TriggerNode == Entity.Null
                        ? "."
                        : $", Knoten {Show(actor.TriggerNode)}."));
            }
        }

        private void FindTriggerCourse(Entity foreignEdge,
                                       TerrainTraceActor actor)
        {
            PartTransferRecord bestRecord = null;
            var bestDistance = float.PositiveInfinity;
            var bestEndpoint = "?";
            var bestNode = Entity.Null;

            if (EntityManager.HasBuffer<ConnectedNode>(foreignEdge))
            {
                var connected = EntityManager
                    .GetBuffer<ConnectedNode>(foreignEdge, true);
                for (var i = 0; i < connected.Length; i++)
                {
                    var node = connected[i].m_Node;
                    if (node == Entity.Null || !EntityManager.Exists(node)
                        || !EntityManager.HasComponent<PrefabRef>(node)
                        || !EntityManager.HasComponent<Node>(node)) continue;
                    var nodePrefab = EntityManager
                        .GetComponentData<PrefabRef>(node).m_Prefab;
                    if (!IsOwnPathPrefab(nodePrefab)) continue;
                    var point = EntityManager.GetComponentData<Node>(node).m_Position;
                    var record = NearestCourseEndpoint(point.xz, nodePrefab,
                        out var endpoint, out var distance);
                    if (record == null || distance >= bestDistance) continue;
                    bestRecord = record;
                    bestDistance = distance;
                    bestEndpoint = endpoint;
                    bestNode = node;
                }
            }

            if (bestRecord != null)
            {
                actor.ConnectionProven = true;
                actor.TriggerNode = bestNode;
                actor.TriggerKind = bestRecord.Kind;
                actor.TriggerIndex = bestRecord.Index;
                actor.TriggerEndpoint = bestEndpoint;
                actor.TriggerDistance = bestDistance;
                return;
            }

            // Ein fehlender ConnectedNode-Puffer ist kein Beweis. Dann wird
            // ausschliesslich der raeumlich naechste Kurs benannt und im Log
            // genau als solcher markiert.
            for (var i = 0; i < _netRecords.Count; i++)
            {
                var record = _netRecords[i];
                float t;
                var fromDistance = MathUtils.Distance(
                    actor.Curve.xz, record.From.xz, out t);
                if (fromDistance < bestDistance)
                {
                    bestRecord = record;
                    bestDistance = fromDistance;
                    bestEndpoint = "A";
                }
                var toDistance = MathUtils.Distance(
                    actor.Curve.xz, record.To.xz, out t);
                if (toDistance < bestDistance)
                {
                    bestRecord = record;
                    bestDistance = toDistance;
                    bestEndpoint = "B";
                }
            }
            actor.TriggerKind = bestRecord?.Kind ?? "(kein PLT-Kurs)";
            actor.TriggerIndex = bestRecord?.Index ?? -1;
            actor.TriggerEndpoint = bestEndpoint;
            actor.TriggerDistance = bestDistance;
        }

        private PartTransferRecord NearestCourseEndpoint(
            float2 point,
            Entity preferredPrefab,
            out string endpoint,
            out float distance)
        {
            PartTransferRecord best = null;
            endpoint = "?";
            var bestSquared = float.PositiveInfinity;
            for (var pass = 0; pass < 2 && best == null; pass++)
            {
                for (var i = 0; i < _netRecords.Count; i++)
                {
                    var record = _netRecords[i];
                    if (pass == 0 && record.Prefab != preferredPrefab) continue;
                    var from = math.distancesq(point, record.From.xz);
                    if (from < bestSquared)
                    {
                        best = record;
                        bestSquared = from;
                        endpoint = "A";
                    }
                    var to = math.distancesq(point, record.To.xz);
                    if (to < bestSquared)
                    {
                        best = record;
                        bestSquared = to;
                        endpoint = "B";
                    }
                }
            }
            distance = math.sqrt(bestSquared);
            return best;
        }

        /**
         * Beobachtet den echten Lebenszyklus statt eine Wartezeit zu raten.
         *
         * ParkingLotComfortSystem laeuft in ModificationEnd vor TerrainSystem.
         * Im ersten Zyklus sieht es daher das von ApplyNetSystem gesetzte
         * `Applied/Updated`; erst TerrainSystem dahinter kann daraus den
         * Renderauftrag erzeugen. Im folgenden Zyklus ist
         * `heightMapRenderRequired` der sichtbare Beleg dieses Auftrags.
         */
        internal void PollTerrainAfterApply()
        {
            var trace = _terrainBuildTrace;
            if (trace == null || trace.AfterCaptured) return;
            try
            {
                var activeActors = CountTerrainActiveActors(trace);
                if (activeActors == 0) return;

                if (trace.ApplyObservedFrame < 0)
                {
                    var updated = 0;
                    for (var i = 0; i < trace.Actors.Count; i++)
                    {
                        var actor = trace.Actors[i];
                        if (!actor.TerrainActive
                            || actor.Original == Entity.Null
                            || !EntityManager.Exists(actor.Original)) continue;
                        if (EntityManager.HasComponent<Updated>(actor.Original)
                            || EntityManager.HasComponent<Applied>(actor.Original)
                            || EntityManager.HasComponent<Created>(actor.Original))
                            updated++;
                    }
                    if (updated == 0) return;
                    trace.ApplyObservedFrame = UnityEngine.Time.frameCount;
                    Mod.log.Info("PLT-Terrainmessung APPLY beobachtet: "
                        + $"{updated}/{activeActors} terrainaktive bestehende "
                        + "Netz-Originale tragen Applied/Updated/Created. "
                        + "TerrainSystem laeuft planmaessig erst hinter dieser "
                        + "Messstelle; Nachherhoehen folgen nach seinem "
                        + "tatsaechlichen Renderauftrag.");
                    return;
                }

                if (UnityEngine.Time.frameCount == trace.ApplyObservedFrame
                    || !_terrainSystem.heightMapRenderRequired) return;
                CaptureTerrainAfterApply(trace,
                    "heightMapRenderRequired nach dem beobachteten Apply");
            }
            catch (Exception exception)
            {
                trace.AfterCaptured = true;
                Mod.log.Error(exception, "PLT-Terrainmessung NACH Apply "
                    + "fehlgeschlagen; keine Bau-Entity wurde veraendert.");
            }
        }

        private void CaptureTerrainAfterApply(TerrainBuildTrace trace,
                                               string evidence)
        {
            var heightData = _terrainSystem.GetHeightData(waitForPending: true);
            var changed = 0;
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var i = 0; i < trace.Samples.Count; i++)
            {
                var sample = trace.Samples[i];
                sample.After = TerrainUtils.SampleHeight(ref heightData,
                    new float3(sample.Point.x, 0f, sample.Point.y));
                if (!math.isfinite(sample.After)) continue;
                var delta = sample.After - sample.Before;
                if (delta == 0f) continue;
                changed++;
                minimum = math.min(minimum, delta);
                maximum = math.max(maximum, delta);
            }
            trace.AfterCaptured = true;

            Mod.log.Info("PLT-Terrainmessung NACH Apply: "
                + $"{changed}/{trace.Samples.Count} identische Weltstellen "
                + "haben eine andere CPU-Terrainhoehe"
                + (changed == 0
                    ? "."
                    : $"; Delta {minimum:+0.0000;-0.0000;0.0000} bis "
                      + $"{maximum:+0.0000;-0.0000;0.0000} m.")
                + $" Beleg fuer den Zeitpunkt: {evidence}. Dies ist keine "
                + "Aussage ueber ungemessene Stellen.");

            LogTerrainGroup(trace, "Objekt");
            LogTerrainGroup(trace, "Netz");
            LogTerrainGroup(trace, "Flaeche");
            LogTerrainGroup(trace, "Fremdnetz");
            LogTerrainActorsAfterApply(trace);
            LogChangedObjectTerrain(trace);
        }

        private void LogTerrainGroup(TerrainBuildTrace trace, string group)
        {
            var total = 0;
            var changed = 0;
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var i = 0; i < trace.Samples.Count; i++)
            {
                var sample = trace.Samples[i];
                if (!string.Equals(sample.Group, group,
                        StringComparison.Ordinal)) continue;
                total++;
                var delta = sample.After - sample.Before;
                if (delta == 0f) continue;
                changed++;
                minimum = math.min(minimum, delta);
                maximum = math.max(maximum, delta);
            }
            Mod.log.Info($"PLT-Terrainmessung Gruppe {group}: "
                + $"{changed}/{total} Stellen geaendert"
                + (changed == 0
                    ? "."
                    : $", Delta {minimum:+0.0000;-0.0000;0.0000} bis "
                      + $"{maximum:+0.0000;-0.0000;0.0000} m."));
        }

        private void LogTerrainActorsAfterApply(TerrainBuildTrace trace)
        {
            for (var actorIndex = 0;
                actorIndex < trace.Actors.Count; actorIndex++)
            {
                var actor = trace.Actors[actorIndex];
                if (!actor.TerrainActive) continue;
                var changed = 0;
                var minimum = float.PositiveInfinity;
                var maximum = float.NegativeInfinity;
                for (var i = 0; i < trace.Samples.Count; i++)
                {
                    var sample = trace.Samples[i];
                    if (sample.ActorIndex != actorIndex) continue;
                    var delta = sample.After - sample.Before;
                    if (delta == 0f) continue;
                    changed++;
                    minimum = math.min(minimum, delta);
                    maximum = math.max(maximum, delta);
                }
                var exists = actor.Original != Entity.Null
                    && EntityManager.Exists(actor.Original);
                var updated = exists
                    && EntityManager.HasComponent<Updated>(actor.Original);
                var applied = exists
                    && EntityManager.HasComponent<Applied>(actor.Original);
                Mod.log.Info("PLT-Terrainakteur NACH Apply: Original "
                    + $"{Show(actor.Original)} '{actor.PrefabName}', Flags "
                    + $"{actor.GeometryFlags}, Updated {JaNein(updated)}, "
                    + $"Applied {JaNein(applied)}; Terrain auf "
                    + $"{changed}/3 Achsmessstellen geaendert"
                    + (changed == 0
                        ? "."
                        : $", Delta {minimum:+0.0000;-0.0000;0.0000} bis "
                          + $"{maximum:+0.0000;-0.0000;0.0000} m."));
            }
        }

        private void LogChangedObjectTerrain(TerrainBuildTrace trace)
        {
            for (var i = 0; i < trace.Samples.Count; i++)
            {
                var sample = trace.Samples[i];
                if (!string.Equals(sample.Group, "Objekt",
                        StringComparison.Ordinal)) continue;
                var delta = sample.After - sample.Before;
                if (delta == 0f) continue;
                var current = sample.ObjectBeforeApply;
                if (sample.ObjectEntity != Entity.Null
                    && EntityManager.Exists(sample.ObjectEntity)
                    && EntityManager.HasComponent<Game.Objects.Transform>(
                        sample.ObjectEntity))
                    current = EntityManager
                        .GetComponentData<Game.Objects.Transform>(
                            sample.ObjectEntity).m_Position;
                var actor = NearestTerrainActor(trace, sample.Point,
                    out var actorDistance);
                Mod.log.Info("PLT-Terrainstelle Objekt: "
                    + $"{sample.Kind} #{sample.Index} "
                    + $"{Show(sample.ObjectEntity)} bei "
                    + $"{sample.Point.x:F2}/{sample.Point.y:F2}; Terrain "
                    + $"{sample.Before:F4} -> {sample.After:F4} m "
                    + $"({delta:+0.0000;-0.0000;0.0000} m), Objekt-Y vor "
                    + $"Apply {sample.ObjectBeforeApply.y:F4}, jetzt "
                    + $"{current.y:F4} m; naechste terrainaktive "
                    + (actor == null
                        ? "Fremdnetzkante: keine erfasst."
                        : $"Fremdnetzkante {Show(actor.Original)} "
                          + $"'{actor.PrefabName}' in {actorDistance:F3} m."));
            }
        }

        internal void NoteTerrainObjectMovement(Entity entity,
                                                float3 before,
                                                float3 after)
        {
            var trace = _terrainBuildTrace;
            if (trace == null || trace.MovementLogged.Contains(entity)) return;
            try
            {
                if (!trace.AfterCaptured)
                    CaptureTerrainAfterApply(trace,
                        "GroundHeightSystem hat ein PLT-Objekt bereits versetzt");
                TerrainTraceSample match = null;
                for (var i = 0; i < trace.Samples.Count; i++)
                    if (trace.Samples[i].ObjectEntity == entity)
                    {
                        match = trace.Samples[i];
                        break;
                    }
                if (match == null) return;
                trace.MovementLogged.Add(entity);
                var terrainDelta = match.After - match.Before;

                /*
                 * NUR BEI EINER BEWEGUNG, DIE MAN SEHEN KANN - dieselbe
                 * Schranke wie beim Hoehenbezug und aus demselben Grund: je
                 * Bucht-Aufkleber eine Zeile, und der typische Wert ist
                 * Rechenrest.
                 */
                if (math.abs(after.y - before.y) < 0.01f
                    && math.abs(terrainDelta) < 0.01f) return;

                Mod.log.Info("PLT-Terrainfolge Objekt: "
                    + $"{match.Kind} #{match.Index} {Show(entity)}, "
                    + $"Objekt-Y {before.y:F4} -> {after.y:F4} m "
                    + $"({after.y - before.y:+0.0000;-0.0000;0.0000} m); "
                    + $"Terrain an demselben X/Z {match.Before:F4} -> "
                    + $"{match.After:F4} m "
                    + $"({terrainDelta:+0.0000;-0.0000;0.0000} m).");
            }
            catch (Exception exception)
            {
                // Der bestehende Bewegungswaechter muss auch dann weiterlaufen.
                Mod.log.Error(exception,
                    "PLT-Terrainfolge eines bewegten Objekts nicht lesbar.");
            }
        }

        private TerrainTraceActor NearestTerrainActor(
            TerrainBuildTrace trace,
            float2 point,
            out float distance)
        {
            TerrainTraceActor best = null;
            distance = float.PositiveInfinity;
            for (var i = 0; i < trace.Actors.Count; i++)
            {
                var actor = trace.Actors[i];
                if (!actor.TerrainActive) continue;
                float t;
                var candidate = MathUtils.Distance(actor.Curve.xz, point, out t);
                if (candidate >= distance) continue;
                best = actor;
                distance = candidate;
            }
            return best;
        }

        private int CountTerrainActiveActors(TerrainBuildTrace trace)
        {
            var count = 0;
            for (var i = 0; i < trace.Actors.Count; i++)
                if (trace.Actors[i].TerrainActive) count++;
            return count;
        }

        private Game.Net.GeometryFlags TerrainNetFlags(Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                || !EntityManager.HasComponent<NetGeometryData>(prefab))
                return (Game.Net.GeometryFlags)0;
            return EntityManager.GetComponentData<NetGeometryData>(prefab).m_Flags;
        }

        private static bool IsTerrainActive(Game.Net.GeometryFlags flags)
            => (flags & (Game.Net.GeometryFlags.FlattenTerrain
                         | Game.Net.GeometryFlags.ClipTerrain)) != 0;

        private string TerrainPrefabName(Entity prefab)
        {
            if (prefab != Entity.Null && EntityManager.Exists(prefab)
                && _prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var found)
                && found != null)
                return found.name;
            return prefab == Entity.Null ? "Entity.Null" : "Prefab " + prefab.Index;
        }

        private static string JaNein(bool value) => value ? "JA" : "NEIN";

        private static int TerrainOpenNodeCount(float3[] nodes)
        {
            if (nodes == null) return 0;
            var count = nodes.Length;
            if (count > 1 && math.all(nodes[0] == nodes[count - 1])) count--;
            return count;
        }
    }
}
