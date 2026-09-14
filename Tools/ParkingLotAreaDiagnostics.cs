using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<AreaTransferRecord> _areaTransferRecords =
            new List<AreaTransferRecord>();
        private EntityQuery _tempAreaDebugQuery;
        private TerrainTransferRecord _areaTerrainTransfer =
            new TerrainTransferRecord { LimitMeters = MaxCourseHeightDeviation };
        private int _areaPreviewPlannedSurfaceCount;
        private int? _areaTransferRevision;
        private int _areaTransferCreatedFrame = -1;
        private string _areaTransferNote;

        private void InitializeAreaDiagnostics()
        {
            _tempAreaDebugQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        private void BeginAreaTransfer(
            ParkingLayout layout,
            float2[][] grass = null,
            float2[][] asphalt = null)
        {
            _areaTransferRecords.Clear();
            ResetPartRecords();
            _areaTransferRevision = layout == null ? (int?)null : _lastPreviewRevision;
            _areaPreviewPlannedSurfaceCount = CountAreaPolygons(
                    grass ?? layout?.GrassSurface)
                + CountAreaPolygons(asphalt ?? layout?.AsphaltSurface);
            _areaTerrainTransfer = new TerrainTransferRecord
            {
                LimitMeters = MaxCourseHeightDeviation,
                Note = _areaPreviewPlannedSurfaceCount == 0
                    ? "The layout contains no polygon surface that could be handed over."
                    : "Terrainabtastung noch nicht abgeschlossen.",
            };
            _areaTransferCreatedFrame = -1;
            _areaTransferNote = null;
        }

        private void SetAreaTerrainTransfer(
            int count,
            float minimum,
            float maximum,
            float span,
            bool limitTriggered,
            string note)
        {
            _areaTerrainTransfer = new TerrainTransferRecord
            {
                SampleCount = count,
                Minimum = count > 0 ? (float?)minimum : null,
                Maximum = count > 0 ? (float?)maximum : null,
                Span = count > 0 ? (float?)span : null,
                FiftyMeterLimitTriggered = limitTriggered,
                LimitMeters = MaxCourseHeightDeviation,
                Note = note,
            };
            if (limitTriggered)
                _areaTransferNote = "No surface was handed to CS2 because the measured terrain span "
                    + "exceeded the 50 m limit.";
        }

        private Entity _lotOwner = Entity.Null;

        /**
         * Haengt alle Vorschau-Flaechen an EINEN gemeinsamen Besitzer.
         *
         * Belegt im Dekompilat: der Bulldozer laeuft die `Owner`-Kette nach
         * oben (`while (HasComponent<Owner>) e = Owner(e)`) und raeumt den
         * ganzen Baum ab - ein Klick auf ein Teilstueck trifft also das Ganze.
         * Den `SubArea`-Puffer am Besitzer fuellt CS2 selbst, sobald eine
         * Flaeche mit `Owner` als `Created` durchlaeuft
         * (SubAreaReferencesSystem). Wir muessen nur den Besitzer setzen.
         *
         * Muss VOR ApplyMode.Apply laufen: eingetragen wird beim Uebergang von
         * Temp auf dauerhaft.
         *
         * Der Besitzer ist bewusst eine schlichte Entity mit Transform und
         * SubArea-Puffer, ohne Prefab. Ob das Speichern und Laden uebersteht,
         * ist die offene Frage - der Abzug berichtet sie.
         */
        private int AttachSurfacesToLotOwner()
        {
            RefreshAreaTransferAudit();
            var candidates = new List<AreaTransferRecord>();
            foreach (var record in _areaTransferRecords)
                if (record.MaterializedEntity != Entity.Null
                    && EntityManager.Exists(record.MaterializedEntity)
                    && !EntityManager.HasComponent<Owner>(record.MaterializedEntity))
                    candidates.Add(record);
            /**
             * EINE FLAECHE REICHT - DIESELBE ZWEIERHUERDE WIE OBEN.
             *
             * Hier stand `candidates.Count < 2`. Mit beiden Flaechenschaltern
             * aus bleibt nur die Besitzerflaeche selbst uebrig; der Besitzer
             * waere dann auf Entity.Null gesetzt worden, und der Parkplatz
             * haette weder einen Anker zum Anklicken noch einen zum
             * Wegbaggern gehabt. Genau das, was der Nutzer NICHT will.
             *
             * Ohne jede Flaeche gibt es dagegen wirklich keinen Anker: dann
             * traegt kein Kandidat ein `PrefabRef`, und das Infofenster
             * oeffnet nicht (siehe die Begruendung darunter). Erst da ist
             * Aufgeben richtig.
             */
            if (candidates.Count == 0)
            {
                _lotOwner = Entity.Null;
                return 0;
            }

            /**
             * Besitzer ist die GROESSTE eigene Flaeche, kein eigens erzeugtes
             * Objekt.
             *
             * Der erste Versuch war eine schlichte Entity mit Transform und
             * SubArea-Puffer. Gruppieren und Abreissen ging damit, aber es kam
             * kein Infofenster: SelectedInfoUISystem oeffnet nur, wenn die
             * gewaehlte Entity ein `PrefabRef` hat -
             * `if (TryGetSelection(out var entity) && TryGetComponent<PrefabRef>(entity, ...))`.
             * Eine selbstgebaute Entity hat keins.
             *
             * Eine unserer Flaechen hat eins, ist eine regulaer gespeicherte
             * Entity und beantwortet damit zugleich die offene Frage nach
             * Speichern und Laden. Genommen wird die groesste, damit der
             * Anker nicht an einem Schnipsel haengt.
             */
            var owner = candidates[0];
            // Der Versuch mit eigener Lot-Flaeche schlaegt die Groessenwahl:
            // sie deckt das ganze Polygon, ist im ganzen Umriss anklickbar und
            // liefert dem Auswahlpfeil die richtige Mitte. Siehe
            // ParkingLotLotArea; standardmaessig aus.
            var lotArea = candidates.Find(
                record => record.Kind == LotOwnerRecordKind);
            if (lotArea != null)
            {
                AssignLotOwner(candidates, lotArea);
                return AttachChildren(candidates);
            }

            var bestSpan = -1.0;
            foreach (var record in candidates)
            {
                var nodes = record.SentNodes ?? Array.Empty<float3>();
                if (nodes.Length == 0) continue;
                var min = new float2(float.MaxValue, float.MaxValue);
                var max = new float2(float.MinValue, float.MinValue);
                foreach (var node in nodes)
                {
                    min = math.min(min, node.xz);
                    max = math.max(max, node.xz);
                }
                var span = (double)(max.x - min.x) * (max.y - min.y);
                if (span <= bestSpan) continue;
                bestSpan = span;
                owner = record;
            }

            AssignLotOwner(candidates, owner);
            return AttachChildren(candidates);
        }

        private void AssignLotOwner(List<AreaTransferRecord> candidates,
                                    AreaTransferRecord owner)
        {
            _lotOwner = owner.MaterializedEntity;
            // Legt SubArea, SubNet UND SubObject an. Die beiden letzteren sind
            // nicht optional: ihre Referenzsysteme greifen ungeprueft zu,
            // sobald ein Kind dauerhaft wird - siehe ParkingLotLotOwner.
            EnsureOwnerBuffers(_lotOwner);
        }

        private int AttachChildren(List<AreaTransferRecord> candidates)
        {
            var attached = 0;
            foreach (var record in candidates)
            {
                if (record.MaterializedEntity == _lotOwner) continue;
                EntityManager.AddComponentData(record.MaterializedEntity,
                    new Owner(_lotOwner));
                attached++;
            }
            return attached;
        }

        private void RecordAreaDefinition(
            string kind,
            int index,
            Entity prefab,
            Entity definition,
            float3[] sentNodes)
        {
            _areaTransferRecords.Add(new AreaTransferRecord
            {
                Kind = kind,
                Index = index,
                Prefab = prefab,
                Definition = definition,
                SentNodes = Copy(sentNodes),
                CreatedFrame = UnityEngine.Time.frameCount,
                DefinitionExists = true,
                DefinitionHasCreationDefinition = true,
                DefinitionHasNodeBuffer = true,
                DefinitionHasUpdated = true,
                Status = "definition-created",
                Reason = "Wartet auf die Auswertung durch CS2.",
            });
        }

        /**
         * Sind aus den Definitionen schon Temp-Entities geworden?
         *
         * DIE ZWEI WAR EINE STILLE SPERRE. Hier stand
         * `ready >= 2 && ready >= Count` - mit der Begruendung, es muessten
         * mindestens zwei Flaechen da sein, sonst gebe es niemanden zu
         * gruppieren. Das stimmte, solange es IMMER Gras und Belag gab.
         *
         * Seit dem 2026-08-22 kann der Nutzer beide Flaechen abschalten. Dann
         * bleibt genau eine Flaeche uebrig: die Besitzerflaeche selbst. `ready`
         * kam nie ueber 1, die Bedingung war nie erfuellt, und der Bau lief in
         * den 30-Frame-Zeitabbruch - fuer den Nutzer sah es aus, als tue Enter
         * gar nichts. Gemeldet am 2026-08-24.
         *
         * Eine Flaeche REICHT: `AttachSurfacesToLotOwner` nimmt die
         * Lot-Flaeche als Besitzer und haengt null Kinder an - Wege und
         * Aufkleber kommen ohnehin ueber `AttachPartsToLotOwner`. Genau das
         * will der Nutzer: "der Parkplatz sollte trotzdem auswaehlbar sein und
         * loeschbar."
         */
        private bool AreaTransferMaterialized()
        {
            AreaTransferStand(out var ready, out var gesamt);
            return gesamt > 0 && ready >= gesamt;
        }

        /**
         * Wieviele der gesendeten Flaechen sind schon Entities?
         *
         * Getrennt herausgezogen, damit der Zeitabbruch die Zahlen NENNEN
         * kann. Vorher meldete er nur "nicht zu Entities geworden" - das war
         * bei jeder Ursache derselbe Satz.
         */
        private void AreaTransferStand(out int ready, out int gesamt)
        {
            ready = 0;
            gesamt = _areaTransferRecords.Count;
            if (gesamt == 0) return;
            RefreshAreaTransferAudit();
            foreach (var record in _areaTransferRecords)
                if (record.MaterializedEntity != Entity.Null
                    && EntityManager.Exists(record.MaterializedEntity))
                    ready++;
        }

        private void RefreshAreaTransferAudit()
        {
            if (_areaTransferRecords.Count == 0) return;
            try
            {
                using var areas = _tempAreaDebugQuery.ToEntityArray(Allocator.TempJob);
                var used = new HashSet<Entity>();
                for (var recordIndex = 0;
                    recordIndex < _areaTransferRecords.Count; recordIndex++)
                {
                    var record = _areaTransferRecords[recordIndex];
                    record.DefinitionExists = record.Definition != Entity.Null
                        && EntityManager.Exists(record.Definition);
                    record.DefinitionHasCreationDefinition = record.DefinitionExists
                        ? (bool?)EntityManager.HasComponent<CreationDefinition>(record.Definition)
                        : null;
                    record.DefinitionHasNodeBuffer = record.DefinitionExists
                        ? (bool?)EntityManager.HasBuffer<Game.Areas.Node>(record.Definition)
                        : null;
                    record.DefinitionHasUpdated = record.DefinitionExists
                        ? (bool?)EntityManager.HasComponent<Updated>(record.Definition)
                        : null;

                    Entity match = Entity.Null;
                    for (var areaIndex = 0; areaIndex < areas.Length; areaIndex++)
                    {
                        var candidate = areas[areaIndex];
                        if (used.Contains(candidate)) continue;
                        var prefab = EntityManager.GetComponentData<PrefabRef>(candidate).m_Prefab;
                        if (prefab != record.Prefab) continue;
                        var nodes = EntityManager.GetBuffer<Game.Areas.Node>(candidate, true);
                        if (!AreaNodesMatch(record.SentNodes, nodes)) continue;
                        match = candidate;
                        break;
                    }

                    if (match != Entity.Null)
                    {
                        used.Add(match);
                        AuditMaterializedArea(record, match);
                        continue;
                    }

                    // Nach einem bewussten Clear ist der letzte, vorher kopierte
                    // Zustand aussagekräftiger als "Entity inzwischen gelöscht".
                    if (!_ghostsActive && record.DefinitionMaterialized.HasValue)
                        continue;

                    record.MaterializedEntity = Entity.Null;
                    record.MaterializedNodes = null;
                    record.MaterializedElevations = null;
                    record.TriangleCount = null;
                    record.TriangleIndicesValid = null;
                    var age = UnityEngine.Time.frameCount - record.CreatedFrame;
                    if (record.DefinitionExists || age <= 2)
                    {
                        record.DefinitionMaterialized = null;
                        record.GeometryAccepted = null;
                        record.Status = "awaiting-materialization";
                        record.Reason = record.DefinitionExists
                            ? "The definition still exists; CS2 has not finished it yet."
                            : "The handover is at most two frames old; the outcome is not certain yet.";
                    }
                    else
                    {
                        record.DefinitionMaterialized = false;
                        record.GeometryAccepted = false;
                        record.Status = "not-materialized";
                        record.Reason = "No matching temp area found and the definition is gone. CS2 exposes "
                            + "no more precise reason for the rejection.";
                    }
                }
            }
            catch (Exception exception)
            {
                RecordPreviewDiagnostic("Error",
                    "Could not read the CS2 state of the created surfaces.",
                    exception);
                Mod.log.Error(exception,
                    "PLT konnte den CS2-Zustand der erzeugten Flächen nicht lesen.");
            }
        }

        private void AuditMaterializedArea(AreaTransferRecord record, Entity entity)
        {
            record.DefinitionMaterialized = true;
            record.MaterializedEntity = entity;
            var area = EntityManager.GetComponentData<Game.Areas.Area>(entity);
            record.AreaFlags = area.m_Flags.ToString();
            record.AreaFlagsValue = (int)area.m_Flags;
            if (EntityManager.HasComponent<Temp>(entity))
            {
                var temp = EntityManager.GetComponentData<Temp>(entity);
                record.TempFlags = temp.m_Flags.ToString();
                record.TempFlagsValue = (int)temp.m_Flags;
            }

            var nodes = EntityManager.GetBuffer<Game.Areas.Node>(entity, true);
            record.MaterializedNodes = new float3[nodes.Length];
            record.MaterializedElevations = new float[nodes.Length];
            for (var i = 0; i < nodes.Length; i++)
            {
                record.MaterializedNodes[i] = nodes[i].m_Position;
                record.MaterializedElevations[i] = nodes[i].m_Elevation;
            }

            if ((area.m_Flags & Game.Areas.AreaFlags.NoTriangles) != 0)
            {
                record.GeometryAccepted = false;
                record.Status = "rejected-no-triangles";
                record.Reason = "CS2 setzte AreaFlags.NoTriangles.";
                record.TriangleCount = EntityManager.HasBuffer<Game.Areas.Triangle>(entity)
                    ? (int?)EntityManager.GetBuffer<Game.Areas.Triangle>(entity, true).Length
                    : null;
                record.TriangleIndicesValid = false;
                return;
            }

            if (!EntityManager.HasBuffer<Game.Areas.Triangle>(entity))
            {
                record.GeometryAccepted = false;
                record.Status = "rejected-missing-triangle-buffer";
                record.Reason = "Die materialisierte Area besitzt keinen Triangle-Buffer.";
                record.TriangleCount = null;
                record.TriangleIndicesValid = false;
                return;
            }

            var triangles = EntityManager.GetBuffer<Game.Areas.Triangle>(entity, true);
            record.TriangleCount = triangles.Length;
            if (triangles.Length == 0)
            {
                record.GeometryAccepted = null;
                record.Status = "materialized-awaiting-triangles";
                record.Reason = "Area exists, triangle buffer still empty; this frame allows no final "
                    + "verdict on acceptance or rejection.";
                record.TriangleIndicesValid = null;
                return;
            }

            var nodeCount = OpenAreaNodeCount(nodes);
            var indicesValid = triangles.Length == nodeCount - 2;
            for (var triangleIndex = 0;
                triangleIndex < triangles.Length && indicesValid; triangleIndex++)
            {
                var indices = triangles[triangleIndex].m_Indices;
                if (math.any(indices < 0) || math.any(indices >= nodes.Length))
                    indicesValid = false;
            }
            record.TriangleIndicesValid = indicesValid;
            record.GeometryAccepted = indicesValid;
            record.Status = indicesValid ? "accepted" : "rejected-invalid-triangles";
            record.Reason = indicesValid
                ? null
                : $"Triangle buffer inconsistent: {triangles.Length} triangles for "
                    + $"{nodeCount} open nodes, or invalid indices.";
        }

        private DebugAreaTransfer CaptureAreaTransferSnapshot()
        {
            var grassPrefabExists = _grassSurfacePrefab != Entity.Null
                && EntityManager.Exists(_grassSurfacePrefab);
            var pavementPrefabExists = _pavementSurfacePrefab != Entity.Null
                && EntityManager.Exists(_pavementSurfacePrefab);
            var prefabExists = grassPrefabExists && pavementPrefabExists;
            var surfaces = new DebugGeneratedSurface[_areaTransferRecords.Count];
            for (var i = 0; i < _areaTransferRecords.Count; i++)
            {
                var source = _areaTransferRecords[i];
                surfaces[i] = new DebugGeneratedSurface
                {
                    Kind = source.Kind,
                    Index = source.Index,
                    Prefab = DescribePrefab(source.Prefab),
                    DefinitionEntity = DebugEntity.From(source.Definition),
                    DefinitionStillExists = source.DefinitionExists,
                    DefinitionHasCreationDefinition =
                        source.DefinitionHasCreationDefinition,
                    DefinitionHasNodeBuffer = source.DefinitionHasNodeBuffer,
                    DefinitionHasUpdated = source.DefinitionHasUpdated,
                    NodesSentToCs2 = DebugAreaNode.From(
                        source.SentNodes, float.MinValue),
                    CreatedFrame = source.CreatedFrame,
                    DefinitionMaterialized = source.DefinitionMaterialized,
                    MaterializedEntity = DebugEntity.From(source.MaterializedEntity),
                    GeometryAccepted = source.GeometryAccepted,
                    Status = source.Status,
                    Reason = source.Reason,
                    AreaFlags = source.AreaFlags,
                    AreaFlagsValue = source.AreaFlagsValue,
                    TempFlags = source.TempFlags,
                    TempFlagsValue = source.TempFlagsValue,
                    TriangleCount = source.TriangleCount,
                    TriangleIndicesValid = source.TriangleIndicesValid,
                    MaterializedNodes = DebugAreaNode.From(
                        source.MaterializedNodes, source.MaterializedElevations),
                };
            }

            return new DebugAreaTransfer
            {
                PrefabName = $"{GrassSurfaceName} + {PavementSurfaceName}",
                Prefab = DescribePrefab(_grassSurfacePrefab),
                GeometryRevision = _areaTransferRevision,
                PrefabEntityExists = prefabExists,
                HasSurfaceData = prefabExists
                    && EntityManager.HasComponent<SurfaceData>(_grassSurfacePrefab)
                    && EntityManager.HasComponent<SurfaceData>(_pavementSurfacePrefab),
                HasAreaData = prefabExists
                    && EntityManager.HasComponent<AreaData>(_grassSurfacePrefab)
                    && EntityManager.HasComponent<AreaData>(_pavementSurfacePrefab),
                HasAreaGeometryData = prefabExists
                    && EntityManager.HasComponent<AreaGeometryData>(_grassSurfacePrefab)
                    && EntityManager.HasComponent<AreaGeometryData>(_pavementSurfacePrefab),
                AreaArchetypeValid = prefabExists
                    && EntityManager.HasComponent<AreaData>(_grassSurfacePrefab)
                    && EntityManager.HasComponent<AreaData>(_pavementSurfacePrefab)
                    && EntityManager.GetComponentData<AreaData>(
                        _grassSurfacePrefab).m_Archetype.Valid
                    && EntityManager.GetComponentData<AreaData>(
                        _pavementSurfacePrefab).m_Archetype.Valid,
                PlannedSurfaceCount = _areaPreviewPlannedSurfaceCount,
                SentSurfaceCount = _areaTransferRecords.Count,
                TerrainSamples = new DebugTerrainSamples
                {
                    SampleCount = _areaTerrainTransfer.SampleCount,
                    Minimum = _areaTerrainTransfer.Minimum,
                    Maximum = _areaTerrainTransfer.Maximum,
                    Span = _areaTerrainTransfer.Span,
                    LimitMeters = _areaTerrainTransfer.LimitMeters,
                    FiftyMeterLimitTriggered =
                        _areaTerrainTransfer.FiftyMeterLimitTriggered,
                    Note = _areaTerrainTransfer.Note,
                },
                Surfaces = surfaces,
                AcceptanceMeaning = "DefinitionMaterialized says whether CS2 turned the definition into a "
                    + "temp area. GeometryAccepted also requires a non-empty triangle "
                    + "buffer with valid indices and without AreaFlags.NoTriangles; "
                    + "null means pending or not reliably queryable.",
                Note = _areaTransferNote ?? (_areaTransferRecords.Count == 0
                    ? "No surface was handed to CS2 for the last preview run."
                    : "Prefab holds the grass prefab for compatibility; the actual mapping "
                      + "of each grass and pavement ring is in Surfaces."),
            };
        }

        private static bool AreaNodesMatch(
            float3[] sent,
            DynamicBuffer<Game.Areas.Node> materialized)
        {
            if (sent == null) return false;
            var sentCount = sent.Length;
            if (sentCount > 1 && math.all(sent[0] == sent[sentCount - 1])) sentCount--;
            var materializedCount = OpenAreaNodeCount(materialized);
            if (sentCount != materializedCount || sentCount < 3) return false;

            const float toleranceSquared = 0.0001f;
            for (var offset = 0; offset < materializedCount; offset++)
            {
                if (math.lengthsq(sent[0].xz
                    - materialized[offset].m_Position.xz) > toleranceSquared)
                    continue;
                var forward = true;
                var reverse = true;
                for (var i = 1; i < sentCount && (forward || reverse); i++)
                {
                    var forwardIndex = (offset + i) % materializedCount;
                    var reverseIndex = (offset - i + materializedCount) % materializedCount;
                    if (math.lengthsq(sent[i].xz
                        - materialized[forwardIndex].m_Position.xz) > toleranceSquared)
                        forward = false;
                    if (math.lengthsq(sent[i].xz
                        - materialized[reverseIndex].m_Position.xz) > toleranceSquared)
                        reverse = false;
                }
                if (forward || reverse) return true;
            }
            return false;
        }

        private static int CountAreaPolygons(float2[][] polygons)
        {
            if (polygons == null) return 0;
            var count = 0;
            for (var i = 0; i < polygons.Length; i++)
                if (OpenNodeCount(polygons[i]) >= 3) count++;
            return count;
        }
    }
}
