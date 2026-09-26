using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Misst nach dem Bau, warum eine gesetzte Ladesaeule nicht gerendert wird.
     *
     * Die Definition-Entities helfen hier nicht: `Overridden` entsteht erst am
     * dauerhaften Objekt. Deshalb werden die geplanten Lagen vor dem Reset
     * kopiert und acht Frames nach Apply wieder mit permanenten Entities
     * zusammengefuehrt. Jede lesende Query schliesst `Temp` UND `Deleted` aus.
     * Das ist absichtlich strenger als fuer eine reine Anzeige noetig: am
     * 2026-08-12 trug schon die Bulldozer-Vorschau `Deleted` zusammen mit
     * `Temp`, und eine zu breite Query erfasste dadurch 354 echte Decals.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const int ChargerAuditDelayFrames = 8;
        private const int ChargerAuditTimeoutFrames = 120;
        private const float ChargerMatchDistance = 0.25f;

        private sealed class ChargerAuditTarget
        {
            internal int Index;
            internal float3 Position;
        }

        private enum ObjectCollisionResult
        {
            None,
            Exact,
            UnsupportedGeometry,
        }

        private EntityQuery _permanentObjectQuery;
        private readonly List<ChargerAuditTarget> _chargerAuditTargets =
            new List<ChargerAuditTarget>();
        private int _chargerAuditDueFrame = -1;
        private int _chargerAuditDeadlineFrame = -1;
        private bool _chargerPrefabSurveyLogged;

        private void InitializeChargerDiagnostics()
        {
            _permanentObjectQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Object>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
        }

        /**
         * Inventur fuer Weg 4: alle platzierbaren StaticObject-Prefabs, deren
         * Name auf eine Ladesaeule deutet. Ein einzelner Treffer belegt, dass
         * es in genau diesem geladenen Prefab-Satz keinen Namens-Kandidaten
         * zum Austauschen gibt; er beweist nicht, dass DLC-fremde Assets nie
         * existieren koennen.
         */
        private void LogChargerPrefabSurvey(NativeArray<Entity> prefabs)
        {
            if (_chargerPrefabSurveyLogged) return;

            var lines = new List<string>();
            for (var i = 0; i < prefabs.Length; i++)
            {
                var entity = prefabs[i];
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var asset)
                    || asset == null
                    || asset.name.IndexOf("charg", StringComparison.OrdinalIgnoreCase) < 0
                    || !EntityManager.HasComponent<ObjectGeometryData>(entity)) continue;

                var geometry = EntityManager.GetComponentData<ObjectGeometryData>(entity);
                var size = geometry.m_Bounds.max - geometry.m_Bounds.min;
                var vulnerable = (geometry.m_Flags
                    & (GeometryFlags.Overridable | GeometryFlags.DeleteOverridden))
                    == GeometryFlags.Overridable;
                lines.Add($"'{asset.name}' {Show(entity)}: {size.x:F2} x "
                    + $"{size.z:F2} x {size.y:F2} m, Flags {geometry.m_Flags}, "
                    + (vulnerable ? "bei Kollision unsichtbar" : "nicht selbst überschreibbar"));
            }

            lines.Sort(StringComparer.Ordinal);
            _chargerPrefabSurveyLogged = true;
            Mod.log.Info("PLT-Ladesäulen-Prefabinventur: " + lines.Count
                + " Namens-Kandidat(en) unter den geladenen platzierbaren "
                + "StaticObjects" + (lines.Count == 0 ? "." : ":\n  "
                + string.Join("\n  ", lines)));
        }

        /** Kopiert nur die Ladesaeulen; die Part-Liste wird beim naechsten Bau geleert. */
        private void RequestChargerAudit()
        {
            // Derselbe Zeitpunkt nach Apply ist auch der Startpunkt der
            // allgemeinen Ueberlappungsdiagnose. Anders als die
            // Ladesaeulenmessung darf sie nicht davon abhaengen, ob dieser
            // Parkplatz ueberhaupt eine Saeule geplant hat.
            PlaneUeberlappungsdiagnose(_lotOwner);
            _chargerAuditTargets.Clear();
            for (var i = 0; i < _objectRecords.Count; i++)
            {
                var record = _objectRecords[i];
                if (!string.Equals(record.Kind, "Charger", StringComparison.Ordinal))
                    continue;
                _chargerAuditTargets.Add(new ChargerAuditTarget
                {
                    Index = record.Index,
                    Position = record.From,
                });
            }

            if (_chargerAuditTargets.Count == 0)
            {
                _chargerAuditDueFrame = -1;
                _chargerAuditDeadlineFrame = -1;
                return;
            }

            var frame = UnityEngine.Time.frameCount;
            _chargerAuditDueFrame = frame + ChargerAuditDelayFrames;
            _chargerAuditDeadlineFrame = frame + ChargerAuditTimeoutFrames;
            Mod.log.Info($"PLT-Ladesäulen-Messung vorgemerkt: "
                + $"{_chargerAuditTargets.Count} Säule(n), Auswertung "
                + $"{ChargerAuditDelayFrames} Frames nach Apply. "
                + "Erwartet: nach dem gemessenen Rückversatz Overridden=NEIN "
                + "und Owner=NEIN.");
        }

        /**
         * Laeuft auch weiter, wenn das PLT nach Enter nicht mehr aktiv ist.
         * Sonst waere gerade der dauerhafte Nachzustand nicht beobachtbar.
         */
        private void PollChargerAudit()
        {
            // `PollChargerAudit` wird im Toolsystem VOR der Aktivitaetsweiche
            // gerufen. Dadurch funktioniert auch der spaetere Debug-Knopf,
            // waehrend ein anderes Spielwerkzeug aktiv ist.
            PflegeUeberlappungsdiagnose();
            if (_chargerAuditDueFrame < 0
                || UnityEngine.Time.frameCount < _chargerAuditDueFrame) return;

            try
            {
                var complete = TryFindChargers(out var chargers);
                if (!complete
                    && UnityEngine.Time.frameCount < _chargerAuditDeadlineFrame)
                {
                    _chargerAuditDueFrame = UnityEngine.Time.frameCount + 1;
                    return;
                }

                WriteChargerAudit(chargers, complete);
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception, "PLT-Ladesäulen-Messung fehlgeschlagen.");
            }
            _chargerAuditDueFrame = -1;
            _chargerAuditDeadlineFrame = -1;
            _chargerAuditTargets.Clear();
        }

        private bool TryFindChargers(out Entity[] matches)
        {
            matches = new Entity[_chargerAuditTargets.Count];
            if (_chargerPrefab == Entity.Null || !EntityManager.Exists(_chargerPrefab))
                return false;

            using var entities = _permanentObjectQuery.ToEntityArray(Allocator.Temp);
            var used = new HashSet<Entity>();
            for (var targetIndex = 0; targetIndex < _chargerAuditTargets.Count;
                 targetIndex++)
            {
                var target = _chargerAuditTargets[targetIndex];
                var bestDistance = ChargerMatchDistance * ChargerMatchDistance;
                var best = Entity.Null;
                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (used.Contains(entity)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (prefab != _chargerPrefab) continue;
                    var transform = EntityManager
                        .GetComponentData<Game.Objects.Transform>(entity);
                    var distance = math.distancesq(transform.m_Position.xz,
                        target.Position.xz);
                    if (distance > bestDistance) continue;
                    bestDistance = distance;
                    best = entity;
                }
                if (best == Entity.Null) continue;
                matches[targetIndex] = best;
                used.Add(best);
            }

            for (var i = 0; i < matches.Length; i++)
                if (matches[i] == Entity.Null) return false;
            return true;
        }

        /**
         * DIE SAEULE VOR DER EIGENEN RAEUMENDEN FLAECHE SCHUETZEN.
         *
         * Seit die Parkplatzflaeche `CanOverrideObjects` traegt, versteckt sie
         * alles `Overridable` darunter - gewollt fuer Baeume, aber die Saeule
         * traegt dasselbe Flag (gemessen: Overridable, Physical, Brushable,
         * HasBase, Builtin, ReadOnly) und steht im Gruenstreifen, also unter
         * dem Lot-Polygon. Ohne Schutz waere sie wieder unsichtbar, kaum dass
         * sie es nicht mehr war. Aufkleber brauchen nichts, die tragen
         * `Overridable` nicht.
         *
         * `OverrideSystem` (Game.dll, Zeile 1307-1317) laeuft die
         * BESITZERKETTE der raeumenden Flaeche hoch und steigt aus, sobald sie
         * beim selben Wesen endet wie das Objekt:
         *
         *     while (m_OwnerData.TryGetComponent(entity, out componentData) ...)
         *         entity = componentData.m_Owner;
         *     if (m_TopLevelEntity == entity) return;
         *
         * Ein `Owner` auf der Saeule genuegt dafuer. Der `SubObject`-
         * Puffereintrag NICHT - und genau der ist gefaehrlich:
         * `SubObjectSystem.RelocateSubObjects` verstreut die Puffereintraege
         * einer Flaeche zufaellig neu. Deshalb hier ausdruecklich nur die
         * Komponente, kein Puffereintrag.
         *
         * `Updated` erzwingt, dass `OverrideSystem` neu urteilt - sonst bliebe
         * ein bereits gesetztes `Overridden` stehen.
         */
        private void ProtectChargersFromOwnLot(Entity[] chargers)
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner)) return;
            var geschuetzt = 0;
            for (var i = 0; i < chargers.Length; i++)
            {
                var entity = chargers[i];
                if (entity == Entity.Null || !EntityManager.Exists(entity)) continue;
                if (EntityManager.HasComponent<Owner>(entity)) continue;
                EntityManager.AddComponentData(entity,
                    new Owner { m_Owner = _lotOwner });
                if (!EntityManager.HasComponent<Updated>(entity))
                    EntityManager.AddComponent<Updated>(entity);
                geschuetzt++;
            }
            if (geschuetzt > 0)
                Mod.log.Info("PLT: " + geschuetzt + " Ladesaeule(n) an die "
                    + "Parkplatzflaeche gebunden - nur Owner, KEIN "
                    + "SubObject-Puffereintrag. Sie duerfen deshalb nicht "
                    + "verstreut werden und nicht verschwinden.");
        }

        private void WriteChargerAudit(Entity[] chargers, bool complete)
        {
            ProtectChargersFromOwnLot(chargers);
            var found = 0;
            for (var i = 0; i < chargers.Length; i++)
                if (chargers[i] != Entity.Null) found++;
            Mod.log.Info($"PLT-Ladesäulen-Messung: {found} von {chargers.Length} "
                + "permanenten Säulen anhand Prefab und Lage gefunden; "
                + (complete ? "Materialisierung vollständig." : "120 Frames "
                + "nach Apply noch unvollständig."));

            for (var i = 0; i < chargers.Length; i++)
            {
                var target = _chargerAuditTargets[i];
                var charger = chargers[i];
                if (charger == Entity.Null)
                {
                    Mod.log.Warn($"PLT-Ladesäule {target.Index}: keine permanente "
                        + $"Entity innerhalb {ChargerMatchDistance:F2} m um "
                        + $"{target.Position.x:F2}/{target.Position.z:F2} gefunden.");
                    continue;
                }

                var overridden = EntityManager.HasComponent<Overridden>(charger);
                // Einzelheiten nur, wenn CS2 die Saeule wirklich verdraengt
                // hat - im Normalfall genuegt die Summenzeile oben.
                if (!overridden) continue;
                var owner = EntityManager.HasComponent<Owner>(charger)
                    ? EntityManager.GetComponentData<Owner>(charger).m_Owner
                    : Entity.Null;
                var transform = EntityManager
                    .GetComponentData<Game.Objects.Transform>(charger);
                Mod.log.Info($"PLT-Ladesäule {target.Index} {Show(charger)} bei "
                    + $"{transform.m_Position.x:F2}/{transform.m_Position.z:F2}: "
                    + $"Overridden={(overridden ? "JA" : "NEIN")}, "
                    + $"Owner={(owner == Entity.Null ? "NEIN" : Show(owner))}; "
                    + $"Komponenten: {ComponentList(charger)}");
                var objectResult = WriteObjectCollisionPartners(charger);
                if (objectResult != 0) continue;
                var netResult = WriteNetCollisionPartners(charger);
                if (netResult != 0) continue;
                WriteAreaCollisionPartners(charger);
            }
        }

        /**
         * Spiegelt den Objekt-Zweig aus `Game.Objects.OverrideSystem`
         * (Game.dll 1.6.0f1, dekompiliert: ObjectIterator Zeilen 383 und
         * 403-917). Dort kommen Objekte vor Netzen und Flaechen. Sobald hier
         * ein nicht ueberschreibbarer exakter Treffer steht, sind Randstrasse,
         * Fahrgasse und Belag fuer diese Saeule als Ursache ausgeschlossen.
         * `SubLane` kann nie direkt auftauchen: der Zweig iteriert ausschliesslich
         * den `Game.Objects.SearchSystem`-Baum, keine Lane-Entities.
         */
        private int WriteObjectCollisionPartners(Entity charger)
        {
            if (_objectSearchSystem == null
                || !TryGetSimpleObject(charger, out var chargerTransform,
                    out var chargerGeometry, out var chargerMask))
            {
                Mod.log.Warn("  Kollisionspartner: Säulengeometrie nicht lesbar.");
                return -1;
            }

            var worldBounds = ObjectUtils.CalculateBounds(chargerTransform.m_Position,
                chargerTransform.m_Rotation, chargerGeometry);
            var tree = _objectSearchSystem.GetStaticSearchTree(
                readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = worldBounds.xz,
                Results = results,
            };
            tree.Iterate(ref iterator);

            var candidates = new List<Entity>();
            var seen = new HashSet<Entity>();
            for (var i = 0; i < results.Length; i++)
            {
                var other = results[i];
                if (other == charger || !seen.Add(other)
                    || other == Entity.Null || !EntityManager.Exists(other)
                    || EntityManager.HasComponent<Temp>(other)
                    || EntityManager.HasComponent<Deleted>(other)) continue;
                candidates.Add(other);
            }
            candidates.Sort((a, b) => a.Index.CompareTo(b.Index));

            var blockers = 0;
            var softHits = 0;
            var unsupported = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                var other = candidates[i];
                var result = CollisionLikeOverride(charger, chargerTransform,
                    chargerGeometry, chargerMask, other, out var flags,
                    out var reason);
                if (result == ObjectCollisionResult.None) continue;
                if (result == ObjectCollisionResult.UnsupportedGeometry)
                {
                    unsupported++;
                    Mod.log.Warn($"  Prüf-Kandidat {Show(other)} "
                        + $"'{PrefabNameOf(other) ?? "ohne Prefabnamen"}': {reason}");
                    continue;
                }

                var overridable = (flags
                    & (GeometryFlags.Overridable | GeometryFlags.DeleteOverridden))
                    == GeometryFlags.Overridable;
                if (overridable) softHits++; else blockers++;
                Mod.log.Info($"  EXAKTER Objekt-Treffer {Show(other)} "
                    + $"'{PrefabNameOf(other) ?? "ohne Prefabnamen"}': Flags "
                    + $"{flags}; setzt Säulen-Kollision="
                    + (overridable ? "NEIN (Treffer selbst Overridable)" : "JA")
                    + $"; {reason}");
            }

            Mod.log.Info($"  Objektzweig-Ergebnis: {blockers} nicht "
                + $"überschreibbare Kollisionspartner, {softHits} weiche "
                + $"Treffer, {unsupported} geometrische Sonderfälle. "
                + (blockers > 0
                    ? "OverrideSystem erreicht danach weder Netz- noch Flächenzweig."
                    : unsupported > 0
                        ? "Sondergeometrie verhindert eine sichere Zweigentscheidung."
                        : "Kein Objektblocker; der Netzzweig wird geprüft."));
            return blockers > 0 ? blockers : unsupported > 0 ? -1 : 0;
        }

        /**
         * Zweiter Zweig aus `OverrideSystem.NetIterator` (Game.dll 1.6.0f1,
         * Zeilen 1042-1250). Das Spiel prueft echte `Net.Edge`-Geometrie;
         * eine `SubLane` der Decals kommt auch hier nicht vor.
         */
        private int WriteNetCollisionPartners(Entity charger)
        {
            if (_netSearchSystem == null
                || !TryGetSimpleObject(charger, out var transform,
                    out var geometry, out var mask)) return -1;
            if (ObjectUtils.GetStandingLegCount(geometry, out _)
                || (geometry.m_Flags & GeometryFlags.Circular) != 0
                || EntityManager.HasComponent<Game.Objects.Stack>(charger))
            {
                Mod.log.Warn("  Netzzweig: Säule hat eine nicht unterstützte "
                    + "Stack-/Bein-/Kreisgeometrie.");
                return -1;
            }

            var worldBounds = ObjectUtils.CalculateBounds(transform.m_Position,
                transform.m_Rotation, geometry);
            var tree = _netSearchSystem.GetNetSearchTree(readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = worldBounds.xz,
                Results = results,
            };
            tree.Iterate(ref iterator);

            var seen = new HashSet<Entity>();
            var blockers = 0;
            for (var i = 0; i < results.Length; i++)
            {
                var edge = results[i];
                if (!seen.Add(edge) || edge == Entity.Null
                    || !EntityManager.Exists(edge)
                    || EntityManager.HasComponent<Temp>(edge)
                    || EntityManager.HasComponent<Deleted>(edge)) continue;
                if (!NetCollisionLikeOverride(charger, transform, geometry, mask,
                        worldBounds, edge, out var reason)) continue;
                blockers++;
                Mod.log.Info($"  EXAKTER Netz-Treffer {Show(edge)} "
                    + $"'{PrefabNameOf(edge) ?? "ohne Prefabnamen"}': {reason}");
            }

            Mod.log.Info($"  Netzzweig-Ergebnis: {blockers} Kollisionspartner. "
                + (blockers > 0
                    ? "OverrideSystem erreicht danach den Flächenzweig nicht."
                    : "Kein Netztreffer; der Flächenzweig wird geprüft."));
            return blockers;
        }

        private bool NetCollisionLikeOverride(Entity charger,
            Game.Objects.Transform objectTransform,
            ObjectGeometryData objectGeometry,
            CollisionMask objectMask,
            Bounds3 objectWorldBounds,
            Entity edgeEntity,
            out string reason)
        {
            reason = string.Empty;
            if (!EntityManager.HasComponent<Game.Net.Edge>(edgeEntity)
                || !EntityManager.HasComponent<Game.Net.EdgeGeometry>(edgeEntity)
                || !EntityManager.HasComponent<Game.Net.StartNodeGeometry>(edgeEntity)
                || !EntityManager.HasComponent<Game.Net.EndNodeGeometry>(edgeEntity)
                || !EntityManager.HasComponent<Game.Net.Composition>(edgeEntity))
                return false;
            if (TopLevelObject(edgeEntity) == TopLevelObject(charger)) return false;

            var edge = EntityManager.GetComponentData<Game.Net.Edge>(edgeEntity);
            var edgeGeometry = EntityManager
                .GetComponentData<Game.Net.EdgeGeometry>(edgeEntity);
            var startGeometry = EntityManager
                .GetComponentData<Game.Net.StartNodeGeometry>(edgeEntity).m_Geometry;
            var endGeometry = EntityManager
                .GetComponentData<Game.Net.EndNodeGeometry>(edgeEntity).m_Geometry;
            var composition = EntityManager
                .GetComponentData<Game.Net.Composition>(edgeEntity);
            if (!TryGetComposition(composition.m_Edge, out var edgeData,
                    out var edgeMask)
                || !TryGetComposition(composition.m_StartNode, out var startData,
                    out var startMask)
                || !TryGetComposition(composition.m_EndNode, out var endData,
                    out var endMask)) return false;
            var allMasks = edgeMask | startMask | endMask;
            if ((objectMask & allMasks) == 0) return false;

            var netBounds = edgeGeometry.m_Bounds;
            netBounds |= startGeometry.m_Bounds;
            netBounds |= endGeometry.m_Bounds;
            var broad = (objectMask & CollisionMask.OnGround) != 0
                ? MathUtils.Intersect(netBounds.xz, objectWorldBounds.xz)
                : MathUtils.Intersect(netBounds, objectWorldBounds);
            if (!broad) return false;

            var center = MathUtils.Center(netBounds);
            var collisionBounds = CollisionBounds(objectGeometry);
            var offset = math.mul(math.inverse(objectTransform.m_Rotation),
                objectTransform.m_Position - center);
            var box = new Box3
            {
                bounds = collisionBounds + offset,
                rotation = objectTransform.m_Rotation,
            };
            var areas = default(DynamicBuffer<NetCompositionArea>);
            var intersection3 = default(Bounds3);
            if ((objectMask & CollisionMask.OnGround) == 0
                || MathUtils.Intersect(netBounds, objectWorldBounds))
            {
                Game.Net.ValidationHelpers.Check3DCollisionMasks(edgeMask,
                    objectMask, edgeData, out var edge3D);
                Game.Net.ValidationHelpers.Check3DCollisionMasks(startMask,
                    objectMask, startData, out var start3D);
                Game.Net.ValidationHelpers.Check3DCollisionMasks(endMask,
                    objectMask, endData, out var end3D);
                if ((edgeMask & objectMask) != 0
                    && Game.Net.ValidationHelpers.Intersect(edge, charger,
                        edgeGeometry, -center, box, objectWorldBounds, edge3D,
                        areas, ref intersection3))
                {
                    reason = "Kante, 3D-Schnitt wie OverrideSystem";
                    return true;
                }
                if ((startMask & objectMask) != 0
                    && Game.Net.ValidationHelpers.Intersect(edge.m_Start, charger,
                        startGeometry, -center, box, objectWorldBounds, start3D,
                        areas, ref intersection3))
                {
                    reason = "Startknoten, 3D-Schnitt wie OverrideSystem";
                    return true;
                }
                if ((endMask & objectMask) != 0
                    && Game.Net.ValidationHelpers.Intersect(edge.m_End, charger,
                        endGeometry, -center, box, objectWorldBounds, end3D,
                        areas, ref intersection3))
                {
                    reason = "Endknoten, 3D-Schnitt wie OverrideSystem";
                    return true;
                }
            }

            if (!CommonUtils.ExclusiveGroundCollision(objectMask, allMasks))
                return false;
            var quad = ObjectUtils.CalculateBaseCorners(
                objectTransform.m_Position - center, objectTransform.m_Rotation,
                objectGeometry.m_Bounds).xz;
            var intersection2 = default(Bounds2);
            if (CommonUtils.ExclusiveGroundCollision(objectMask, edgeMask)
                && Game.Net.ValidationHelpers.Intersect(edge, charger,
                    edgeGeometry, -center.xz, quad, objectWorldBounds.xz,
                    edgeData, areas, ref intersection2))
            {
                reason = "Kante, 2D-ExclusiveGround-Schnitt wie OverrideSystem";
                return true;
            }
            if (CommonUtils.ExclusiveGroundCollision(objectMask, startMask)
                && Game.Net.ValidationHelpers.Intersect(edge.m_Start, charger,
                    startGeometry, -center.xz, quad, objectWorldBounds.xz,
                    startData, areas, ref intersection2))
            {
                reason = "Startknoten, 2D-ExclusiveGround-Schnitt wie OverrideSystem";
                return true;
            }
            if (CommonUtils.ExclusiveGroundCollision(objectMask, endMask)
                && Game.Net.ValidationHelpers.Intersect(edge.m_End, charger,
                    endGeometry, -center.xz, quad, objectWorldBounds.xz,
                    endData, areas, ref intersection2))
            {
                reason = "Endknoten, 2D-ExclusiveGround-Schnitt wie OverrideSystem";
                return true;
            }
            return false;
        }

        private bool TryGetComposition(Entity prefab,
            out NetCompositionData data, out CollisionMask mask)
        {
            data = default;
            mask = default;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                || !EntityManager.HasComponent<NetCompositionData>(prefab))
                return false;
            data = EntityManager.GetComponentData<NetCompositionData>(prefab);
            // Normaler Spielmodus: `OverrideSystem.NetIterator` reicht hier
            // ebenfalls ignoreMarkers=true an `NetUtils` weiter.
            mask = Game.Net.NetUtils.GetCollisionMask(data, true);
            return true;
        }

        /** Exakter dritter Zweig aus `AreaIterator`, Game.dll Zeilen 1321-1341. */
        private int WriteAreaCollisionPartners(Entity charger)
        {
            if (_areaSearchSystem == null
                || !TryGetSimpleObject(charger, out var transform,
                    out var geometry, out var mask)) return -1;
            var worldBounds = ObjectUtils.CalculateBounds(transform.m_Position,
                transform.m_Rotation, geometry);
            var tree = _areaSearchSystem.GetSearchTree(readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new AreaItemIterator
            {
                Bounds = worldBounds.xz,
                Results = results,
            };
            tree.Iterate(ref iterator);

            var seen = new HashSet<Entity>();
            var blockers = 0;
            for (var i = 0; i < results.Length; i++)
            {
                var area = results[i];
                if (!seen.Add(area) || area == Entity.Null
                    || !EntityManager.Exists(area)
                    || EntityManager.HasComponent<Temp>(area)
                    || EntityManager.HasComponent<Deleted>(area)
                    || !AreaCollisionLikeOverride(charger, transform, geometry,
                        mask, area, out var flags, out var triangle)) continue;
                blockers++;
                Mod.log.Info($"  EXAKTER Flächen-Treffer {Show(area)} "
                    + $"'{PrefabNameOf(area) ?? "ohne Prefabnamen"}': Flags "
                    + $"{flags}, Dreieck {triangle}; 2D-Schnitt wie OverrideSystem.");
            }
            Mod.log.Info($"  Flächenzweig-Ergebnis: {blockers} "
                + "Kollisionspartner mit CanOverrideObjects.");
            return blockers;
        }

        private bool AreaCollisionLikeOverride(Entity charger,
            Game.Objects.Transform objectTransform,
            ObjectGeometryData objectGeometry,
            CollisionMask objectMask,
            Entity area,
            out Game.Areas.GeometryFlags flags,
            out int triangleIndex)
        {
            flags = 0;
            triangleIndex = -1;
            if (!EntityManager.HasComponent<PrefabRef>(area)
                || !EntityManager.HasBuffer<Game.Areas.Node>(area)
                || !EntityManager.HasBuffer<Game.Areas.Triangle>(area)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(area).m_Prefab;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                || !EntityManager.HasComponent<AreaGeometryData>(prefab)) return false;
            var areaGeometry = EntityManager.GetComponentData<AreaGeometryData>(prefab);
            flags = areaGeometry.m_Flags;
            if ((flags & Game.Areas.GeometryFlags.CanOverrideObjects) == 0
                || (objectMask & Game.Areas.AreaUtils.GetCollisionMask(areaGeometry)) == 0
                || TopLevelObject(area) == TopLevelObject(charger)) return false;

            var objectQuad = ObjectUtils.CalculateBaseCorners(
                objectTransform.m_Position, objectTransform.m_Rotation,
                objectGeometry.m_Bounds).xz;
            var nodes = EntityManager.GetBuffer<Game.Areas.Node>(area, true);
            var triangles = EntityManager.GetBuffer<Game.Areas.Triangle>(area, true);
            for (var i = 0; i < triangles.Length; i++)
            {
                var triangle = Game.Areas.AreaUtils
                    .GetTriangle3(nodes, triangles[i]).xz;
                if (!MathUtils.Intersect(objectQuad, triangle)) continue;
                triangleIndex = i;
                return true;
            }
            return false;
        }

        private bool TryGetSimpleObject(Entity entity,
            out Game.Objects.Transform transform,
            out ObjectGeometryData geometry,
            out CollisionMask mask)
        {
            transform = default;
            geometry = default;
            mask = default;
            if (!EntityManager.HasComponent<Game.Objects.Transform>(entity)
                || !EntityManager.HasComponent<PrefabRef>(entity)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                || !EntityManager.HasComponent<ObjectGeometryData>(prefab)) return false;

            transform = EntityManager.GetComponentData<Game.Objects.Transform>(entity);
            geometry = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
            // Der gemessene Einsatz ist das normale Spiel, nicht der Editor;
            // OverrideSystem setzt dort `ignoreMarkers` ebenfalls auf true.
            mask = EntityManager.HasComponent<Game.Objects.Elevation>(entity)
                ? ObjectUtils.GetCollisionMask(geometry,
                    EntityManager.GetComponentData<Game.Objects.Elevation>(entity), true)
                : ObjectUtils.GetCollisionMask(geometry, true);
            return true;
        }

        private ObjectCollisionResult CollisionLikeOverride(
            Entity charger,
            Game.Objects.Transform chargerTransform,
            ObjectGeometryData chargerGeometry,
            CollisionMask chargerMask,
            Entity other,
            out GeometryFlags otherFlags,
            out string reason)
        {
            otherFlags = 0;
            reason = string.Empty;
            if (!TryGetSimpleObject(other, out var otherTransform,
                    out var otherGeometry, out var otherMask)
                || (chargerMask & otherMask) == 0) return ObjectCollisionResult.None;
            otherFlags = otherGeometry.m_Flags;

            if (TopLevelObject(charger) == TopLevelObject(other))
                return ObjectCollisionResult.None;
            var otherTop = TopLevelObject(other);
            if (EntityManager.HasComponent<Attachment>(otherTop)
                && EntityManager.GetComponentData<Attachment>(otherTop).m_Attached
                    == TopLevelObject(charger)) return ObjectCollisionResult.None;

            var chargerWorld = ObjectUtils.CalculateBounds(
                chargerTransform.m_Position, chargerTransform.m_Rotation,
                chargerGeometry);
            var otherWorld = ObjectUtils.CalculateBounds(otherTransform.m_Position,
                otherTransform.m_Rotation, otherGeometry);
            var broad = (chargerMask & CollisionMask.OnGround) != 0
                ? MathUtils.Intersect(chargerWorld.xz, otherWorld.xz)
                : MathUtils.Intersect(chargerWorld, otherWorld);
            if (!broad) return ObjectCollisionResult.None;

            if (EntityManager.HasComponent<Game.Objects.Stack>(charger)
                || EntityManager.HasComponent<Game.Objects.Stack>(other)
                || ObjectUtils.GetStandingLegCount(chargerGeometry, out _)
                || ObjectUtils.GetStandingLegCount(otherGeometry, out _)
                || (chargerGeometry.m_Flags & GeometryFlags.Circular) != 0
                || (otherGeometry.m_Flags & GeometryFlags.Circular) != 0)
            {
                reason = "AABB trifft, aber Stack-/Bein-/Kreisgeometrie braucht "
                    + "den vollständigen Sonderzweig des Spiels";
                return ObjectCollisionResult.UnsupportedGeometry;
            }

            var center = MathUtils.Center(otherWorld);
            var precise3D = (chargerMask & CollisionMask.OnGround) == 0
                || MathUtils.Intersect(chargerWorld, otherWorld);
            if (precise3D && ShrunkBoxesIntersect(
                    chargerTransform.m_Position, chargerTransform.m_Rotation,
                    chargerGeometry, otherTransform.m_Position,
                    otherTransform.m_Rotation, otherGeometry))
            {
                reason = "3D-Boxschnitt wie OverrideSystem, beidseitig 0,01 m "
                    + "geschrumpft";
                return ObjectCollisionResult.Exact;
            }

            if (!CommonUtils.ExclusiveGroundCollision(chargerMask, otherMask))
                return ObjectCollisionResult.None;
            var chargerQuad = ObjectUtils.CalculateBaseCorners(
                chargerTransform.m_Position - center, chargerTransform.m_Rotation,
                MathUtils.Expand(chargerGeometry.m_Bounds, -0.01f)).xz;
            var otherQuad = ObjectUtils.CalculateBaseCorners(
                otherTransform.m_Position - center, otherTransform.m_Rotation,
                MathUtils.Expand(otherGeometry.m_Bounds, -0.01f)).xz;
            if (!MathUtils.Intersect(chargerQuad, otherQuad, out _))
                return ObjectCollisionResult.None;
            reason = "2D-ExclusiveGround-Schnitt wie OverrideSystem, beidseitig "
                + "0,01 m geschrumpft";
            return ObjectCollisionResult.Exact;
        }

        private static Bounds3 CollisionBounds(ObjectGeometryData geometry)
        {
            var bounds = geometry.m_Bounds;
            if ((geometry.m_Flags & GeometryFlags.IgnoreBottomCollision) != 0)
                bounds.min.y = math.max(bounds.min.y, 0f);
            return bounds;
        }

        private Entity TopLevelObject(Entity entity)
        {
            var remaining = 64;
            while (remaining-- > 0 && EntityManager.HasComponent<Owner>(entity)
                && !EntityManager.HasComponent<Game.Buildings.Building>(entity))
                entity = EntityManager.GetComponentData<Owner>(entity).m_Owner;
            return entity;
        }
    }
}
