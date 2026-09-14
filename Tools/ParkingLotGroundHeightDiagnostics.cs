using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Liest die PLT-Objekttransforms unmittelbar vor GroundHeightSystem.
     *
     * Der Zeitpunkt ist der Messgegenstand: ein allgemeiner Wächter hinter
     * allen Modifikationsphasen sieht zwar eine Bewegung, kann ihren Schreiber
     * aber nicht benennen. Dieses System und sein Gegenstück rahmen genau den
     * statisch verdächtigen Spielpfad ein und verändern keine Entity.
     */
    public sealed partial class ParkingLotGroundHeightBeforeSystem : GameSystemBase
    {
        private ParkingLotToolSystem _tool;
        private EntityQuery _parts;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _parts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_tool == null || !_tool.HasActiveGroundHeightTrace) return;
            _parts.CompleteDependency();
            _tool.CaptureBeforeGroundHeightSystem();
        }
    }

    /**
     * Liest dieselben Transforms unmittelbar nach GroundHeightSystem.
     *
     * Nur wenn sich innerhalb dieser engen Klammer wirklich ein Transform
     * ändert, werden die Vergleichshöhen ermittelt. `ObjectUtils` wird dabei
     * ausschließlich auf lokalen Kopien aufgerufen; sein Ergebnis wird nie an
     * die Entity zurückgeschrieben.
     */
    public sealed partial class ParkingLotGroundHeightAfterSystem : GameSystemBase
    {
        private ParkingLotToolSystem _tool;
        private TerrainSystem _terrain;
        private WaterSystem _water;
        private PrefabSystem _prefabSystem;
        private EntityQuery _parts;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _water = World.GetOrCreateSystemManaged<WaterSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _parts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_tool == null || !_tool.HasActiveGroundHeightTrace) return;

            // GroundHeightSystem schreibt Transform in einem Job. Die Abfrage
            // wartet ausschließlich auf Abhängigkeiten dieser PLT-Teile,
            // bevor der Nachherwert gelesen wird.
            _parts.CompleteDependency();
            if (_tool.CollectAfterGroundHeightSystem() == 0) return;

            var heightData = _terrain.GetHeightData();
            var waterData = _water.GetSurfaceData(out var waterDependencies);
            waterDependencies.Complete();
            var placeableData = GetComponentLookup<PlaceableObjectData>(true);
            var objectGeometryData = GetComponentLookup<ObjectGeometryData>(true);
            _tool.LogGroundHeightSystemChanges(
                ref heightData,
                ref waterData,
                ref placeableData,
                ref objectGeometryData);
        }
    }

    public sealed partial class ParkingLotToolSystem
    {
        private sealed class GroundHeightChange
        {
            internal Entity Entity;
            internal Game.Objects.Transform Before;
            internal Game.Objects.Transform After;
        }

        private sealed class GroundHeightTrace
        {
            internal TerrainBuildTrace Build;
            internal readonly Dictionary<Entity, Game.Objects.Transform> Before =
                new Dictionary<Entity, Game.Objects.Transform>();
            internal readonly List<GroundHeightChange> Changes =
                new List<GroundHeightChange>();
            internal readonly HashSet<Entity> Logged = new HashSet<Entity>();
        }

        private GroundHeightTrace _groundHeightTrace;

        internal bool HasActiveGroundHeightTrace
            => _groundHeightTrace != null
               && ReferenceEquals(_groundHeightTrace.Build, _terrainBuildTrace);

        private void BeginGroundHeightTrace(TerrainBuildTrace build)
        {
            _groundHeightTrace = new GroundHeightTrace { Build = build };
        }

        internal void CaptureBeforeGroundHeightSystem()
        {
            var trace = _groundHeightTrace;
            if (trace == null || !ReferenceEquals(trace.Build, _terrainBuildTrace))
                return;

            trace.Before.Clear();
            for (var i = 0; i < trace.Build.Samples.Count; i++)
            {
                var sample = trace.Build.Samples[i];
                if (!string.Equals(sample.Group, "Objekt",
                        StringComparison.Ordinal)
                    || sample.ObjectEntity == Entity.Null
                    || trace.Logged.Contains(sample.ObjectEntity)
                    || !EntityManager.Exists(sample.ObjectEntity)
                    || EntityManager.HasComponent<Temp>(sample.ObjectEntity)
                    || !EntityManager.HasComponent<Game.Objects.Transform>(
                        sample.ObjectEntity))
                    continue;

                trace.Before[sample.ObjectEntity] = EntityManager
                    .GetComponentData<Game.Objects.Transform>(sample.ObjectEntity);
            }
        }

        internal int CollectAfterGroundHeightSystem()
        {
            var trace = _groundHeightTrace;
            if (trace == null || !ReferenceEquals(trace.Build, _terrainBuildTrace))
                return 0;

            trace.Changes.Clear();
            foreach (var pair in trace.Before)
            {
                var entity = pair.Key;
                if (!EntityManager.Exists(entity)
                    || !EntityManager.HasComponent<Game.Objects.Transform>(entity))
                    continue;
                var after = EntityManager
                    .GetComponentData<Game.Objects.Transform>(entity);
                if (after.Equals(pair.Value)) continue;
                trace.Changes.Add(new GroundHeightChange
                {
                    Entity = entity,
                    Before = pair.Value,
                    After = after,
                });
            }
            return trace.Changes.Count;
        }

        internal void LogGroundHeightSystemChanges(
            ref TerrainHeightData heightData,
            ref WaterSurfaceData<SurfaceWater> waterData,
            ref ComponentLookup<PlaceableObjectData> placeableData,
            ref ComponentLookup<ObjectGeometryData> objectGeometryData)
        {
            var trace = _groundHeightTrace;
            if (trace == null || !ReferenceEquals(trace.Build, _terrainBuildTrace))
                return;

            for (var i = 0; i < trace.Changes.Count; i++)
            {
                var change = trace.Changes[i];
                if (trace.Logged.Contains(change.Entity)) continue;
                try
                {
                    LogGroundHeightSystemChange(change, trace.Build,
                        ref heightData, ref waterData,
                        ref placeableData, ref objectGeometryData);
                    trace.Logged.Add(change.Entity);
                }
                catch (Exception exception)
                {
                    Mod.log.Error(exception,
                        "PLT-Hoehenbezug nach GroundHeightSystem nicht lesbar; "
                        + "keine Entity wurde veraendert.");
                }
            }
            trace.Changes.Clear();
        }

        private void LogGroundHeightSystemChange(
            GroundHeightChange change,
            TerrainBuildTrace build,
            ref TerrainHeightData heightData,
            ref WaterSurfaceData<SurfaceWater> waterData,
            ref ComponentLookup<PlaceableObjectData> placeableData,
            ref ComponentLookup<ObjectGeometryData> objectGeometryData)
        {
            if (!EntityManager.HasComponent<PrefabRef>(change.Entity)) return;
            var prefab = EntityManager
                .GetComponentData<PrefabRef>(change.Entity).m_Prefab;
            var elevation = EntityManager.HasComponent<Game.Objects.Elevation>(
                    change.Entity)
                ? EntityManager.GetComponentData<Game.Objects.Elevation>(change.Entity)
                : default(Game.Objects.Elevation);

            // Der Aufruf arbeitet nur auf Transform- und Elevation-Kopien.
            // Er liefert den Bezug, den GroundHeightSystem selbst benutzt,
            // ohne dessen proprietäre Rechnung im Mod nachzubauen.
            var predicted = Game.Objects.ObjectUtils.AdjustPosition(
                change.Before,
                ref elevation,
                prefab,
                out var angledSample,
                ref heightData,
                ref waterData,
                ref placeableData,
                ref objectGeometryData);

            var point = change.After.m_Position.xz;
            var centerTerrain = TerrainUtils.SampleHeight(
                ref heightData, change.After.m_Position);
            var objectSample = FindTerrainObjectSample(build, change.Entity);
            var label = objectSample == null
                ? "Objekt ?"
                : $"{objectSample.Kind} #{objectSample.Index}";
            var xzMovement = math.distance(
                change.Before.m_Position.xz, change.After.m_Position.xz);
            var areaProjection = OwnAreaProjectionAt(point, centerTerrain);
            var attachment = GroundAttachmentAt(change.Entity, point);
            var reference = GroundReferenceDescription(prefab,
                angledSample, ref placeableData, ref objectGeometryData);

            /*
             * NUR BEI EINER BEWEGUNG, DIE MAN SEHEN KANN.
             *
             * Hier stand eine Zeile je Bucht-Aufkleber - bei einem Parkplatz
             * mit 400 Buchten also 400 Zeilen je Bau. Gemessen am
             * 2026-09-14 bewegte sich der typische Aufkleber um 0,0009 m;
             * das ist Rechenrest, kein Befund. Interessant ist, wenn CS2 ein
             * Objekt WIRKLICH versetzt.
             *
             * Ein Zentimeter ist die Schranke: darunter sieht es im Spiel
             * niemand, darueber schon.
             */
            if (math.abs(change.After.m_Position.y - change.Before.m_Position.y)
                < 0.01f) return;

            Mod.log.Info("PLT-Hoehenbezug NACH GroundHeightSystem: "
                + $"{label} {Show(change.Entity)}, Objekt-Y "
                + $"{change.Before.m_Position.y:F4} -> "
                + $"{change.After.m_Position.y:F4} m; "
                + $"ObjectUtils-Ziel {predicted.m_Position.y:F4} m "
                + $"(Ist-Ziel "
                + $"{change.After.m_Position.y - predicted.m_Position.y:+0.0000;-0.0000;0.0000} m); "
                + $"Terrain Mitte {centerTerrain:F4} m; "
                + $"ECS-Flaechenprojektion am selben X/Z {areaProjection}; "
                + $"X/Z-Versatz {xzMovement:F4} m; Bezug {reference}; "
                + $"Zeichenprioritaet Aufkleber {ObjektPrioritaet(prefab)}; "
                + attachment + ".");
        }

        private static TerrainTraceSample FindTerrainObjectSample(
            TerrainBuildTrace trace,
            Entity entity)
        {
            for (var i = 0; i < trace.Samples.Count; i++)
            {
                var sample = trace.Samples[i];
                if (sample.ObjectEntity == entity
                    && string.Equals(sample.Group, "Objekt",
                        StringComparison.Ordinal))
                    return sample;
            }
            return null;
        }

        /**
         * Trennt die Node-Referenzebene von dem Projektionsvolumen, das CS2
         * fuer die sichtbare Surface benutzt. Die fruehere Meldung nannte den
         * baryzentrischen Node-Wert kurz "Flaechenhoehe". Das war als Zahl
         * richtig, aber als Bedeutung falsch: `Triangle.m_HeightRange` traegt
         * bereits die Terrainabweichung des ganzen Dreiecks, und das
         * Render-Prefab erweitert diesen Bereich nochmals vertikal.
         *
         * Hier wird nichts nachgebaut und nichts veraendert. Gemeldet werden
         * nur die drei Werte, die CS2 selbst im fertigen ECS-Zustand fuehrt.
         */
        private string OwnAreaProjectionAt(float2 point, float terrainHeight)
        {
            var hits = new List<string>();
            var readableAreas = 0;
            var checkedTriangles = 0;
            for (var recordIndex = 0;
                recordIndex < _areaTransferRecords.Count; recordIndex++)
            {
                var record = _areaTransferRecords[recordIndex];
                var entity = record.MaterializedEntity;
                if (entity == Entity.Null || !EntityManager.Exists(entity)
                    || !EntityManager.HasBuffer<Game.Areas.Node>(entity)
                    || !EntityManager.HasBuffer<Game.Areas.Triangle>(entity))
                    continue;

                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(entity, true);
                var triangles = EntityManager
                    .GetBuffer<Game.Areas.Triangle>(entity, true);
                readableAreas++;
                for (var triangleIndex = 0;
                    triangleIndex < triangles.Length; triangleIndex++)
                {
                    var triangle = triangles[triangleIndex];
                    var indices = triangle.m_Indices;
                    if (!math.all((indices >= 0) & (indices < nodes.Length)))
                        continue;
                    checkedTriangles++;
                    var triangle3 = Game.Areas.AreaUtils
                        .GetTriangle3(nodes, triangle);
                    if (!MathUtils.Intersect(triangle3.xz, point, out var weights))
                        continue;
                    var referenceHeight = MathUtils.Position(triangle3.y, weights);
                    var terrainDelta = terrainHeight - referenceHeight;
                    var range = triangle.m_HeightRange;
                    var renderContract = AreaRenderContract(record.Prefab, entity);
                    hits.Add($"{record.Kind} #{record.Index} {Show(entity)} "
                        + $"'{TerrainPrefabName(record.Prefab)}': "
                        + $"Node-Referenz {referenceHeight:F4} m, "
                        + $"Terrain-Referenz "
                        + $"{terrainDelta:+0.0000;-0.0000;0.0000} m, "
                        + $"CS2-Dreieckbereich "
                        + $"{range.min:+0.0000;-0.0000;0.0000}.."
                        + $"{range.max:+0.0000;-0.0000;0.0000} m, "
                        + renderContract);
                    break;
                }
            }
            return hits.Count == 0
                ? $"kein eigenes Dreieck ({readableAreas}/"
                    + $"{_areaTransferRecords.Count} eigene Areas lesbar, "
                    + $"{checkedTriangles} gültige Dreiecke geprüft)"
                : string.Join(" | ", hits);
        }

        /**
         * Trennt den Materialvertrag einer RenderedArea vom allgemeinen
         * Area-Overlay. Die alte Meldung "Projektionsrand nicht lesbar"
         * machte aus einem fehlenden Bauteil einen unbekannten Wert. Beim
         * nackten Lot ist aber gerade das Fehlen von RenderedAreaData und
         * Batch der entscheidende Befund. Das vorhandene Snapmaß wird nur als
         * ECS-Rohwert gemeldet; die Renderformel des Spiels wird nicht in den
         * Mod kopiert.
         */
        /**
         * WER WIRD ZULETZT GEZEICHNET? Das ist die letzte ungemessene Groesse.
         *
         * Aufkleber und Flaeche sind BEIDE Decals und projizieren auf
         * dasselbe Gelaende - der Aufkleber mit +/-0,50 m Volumen, die
         * Flaeche mit +/-1,50 m Projektionsrand. Sie streiten also nicht um
         * die Tiefe (1,6 cm Versatz liegen tief in beiden Volumen), sondern
         * um die REIHENFOLGE.
         *
         * Die entscheidet `m_RendererPriority`. `ManagedBatchSystem` rechnet
         * sie bei Objektdecals in die Renderqueue ein:
         *
         *     renderQueue = shader.renderQueue + decalProperties.m_RendererPriority
         *
         * `AreaBatchSystem` reicht sie bei Flaechen entsprechend an den Batch
         * weiter. Sind beide Werte gleich, ist die Zeichenreihenfolge
         * undefiniert - und undefinierte Reihenfolge sieht genau so aus, wie
         * der Nutzer es beschreibt: mal so, mal so, mal nur auf einer Seite.
         *
         * Beide Werte sind MANAGED, stehen also am Prefab und nicht in ECS.
         */
        private string FlaechenPrioritaet(Entity prefab)
        {
            try
            {
                if (_prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var basis)
                    && basis != null)
                {
                    var gerendert = basis.GetComponent<RenderedArea>();
                    if (gerendert != null)
                        return gerendert.m_RendererPriority.ToString();
                }
            }
            catch (Exception)
            {
                // Eine Diagnose darf nie den Bau mitreissen.
            }
            return "nicht lesbar";
        }

        /**
         * Die Zeichenprioritaet eines Objektdecals. Sie steht nicht am
         * Objektprefab, sondern an den `DecalProperties` seiner Meshes -
         * deshalb der Umweg ueber die Meshliste. Mehrere Meshes werden alle
         * genannt, statt eines davon zu waehlen.
         */
        private string ObjektPrioritaet(Entity objektPrefab)
        {
            try
            {
                if (_prefabSystem == null
                    || !_prefabSystem.TryGetPrefab<PrefabBase>(objektPrefab, out var basis)
                    || basis == null)
                    return "nicht lesbar";
                if (!(basis is StaticObjectPrefab statisch)
                    || statisch.m_Meshes == null || statisch.m_Meshes.Length == 0)
                    return "kein Mesh";
                var werte = new List<string>();
                foreach (var mesh in statisch.m_Meshes)
                {
                    var render = mesh?.m_Mesh;
                    if (render == null) continue;
                    var decal = render.GetComponent<DecalProperties>();
                    werte.Add(decal != null
                        ? $"{render.name}={decal.m_RendererPriority}"
                        : $"{render.name}=kein Decal");
                }
                return werte.Count > 0 ? string.Join(", ", werte) : "kein Mesh";
            }
            catch (Exception)
            {
                return "nicht lesbar";
            }
        }

        private string AreaRenderContract(Entity prefab, Entity area)
        {
            var hasBatch = area != Entity.Null && EntityManager.Exists(area)
                && EntityManager.HasComponent<Game.Areas.Batch>(area);
            if (prefab != Entity.Null && EntityManager.Exists(prefab)
                && EntityManager.HasComponent<RenderedAreaData>(prefab))
            {
                var data = EntityManager.GetComponentData<RenderedAreaData>(prefab);
                return $"RenderedArea-Projektionsrand +/-"
                    + $"{data.m_HeightOffset:F4} m, Instanz-Batch "
                    + JaNein(hasBatch)
                    + $", Zeichenprioritaet {FlaechenPrioritaet(prefab)}";
            }

            if (prefab != Entity.Null && EntityManager.Exists(prefab)
                && EntityManager.HasComponent<AreaGeometryData>(prefab))
            {
                var geometry = EntityManager
                    .GetComponentData<AreaGeometryData>(prefab);
                return $"kein RenderedArea-Material, Instanz-Batch "
                    + $"{JaNein(hasBatch)}, Area-Typ {geometry.m_Type}, "
                    + $"ECS-Snapmass {geometry.m_SnapDistance:F4} m";
            }

            return $"kein RenderedArea-Material, Instanz-Batch "
                + $"{JaNein(hasBatch)}, AreaGeometryData nicht lesbar";
        }

        private string GroundAttachmentAt(Entity entity, float2 point)
        {
            if (!EntityManager.HasComponent<Game.Objects.Attached>(entity))
                return "Attached fehlt";
            var parent = EntityManager
                .GetComponentData<Game.Objects.Attached>(entity).m_Parent;
            if (parent == Entity.Null || !EntityManager.Exists(parent))
                return $"Attached-Elternteil {Show(parent)} fehlt";

            var node = EntityManager.HasComponent<Game.Net.Node>(parent);
            var composition = EntityManager.HasComponent<Game.Net.Composition>(parent);
            var nearest = NearestCarrierNetAt(parent, point);
            return $"Attached-Elternteil {Show(parent)}, Node {JaNein(node)}, "
                + $"Composition {JaNein(composition)}; {nearest}";
        }

        private string NearestCarrierNetAt(Entity carrier, float2 point)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(carrier))
                return "Traeger hat keinen SubNet-Puffer";
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(carrier, true);
            var best = Entity.Null;
            var bestDistance = float.PositiveInfinity;
            var bestPosition = default(float3);
            for (var i = 0; i < subNets.Length; i++)
            {
                var net = subNets[i].m_SubNet;
                if (net == Entity.Null || !EntityManager.Exists(net)
                    || !EntityManager.HasComponent<Game.Net.Curve>(net))
                    continue;
                var curve = EntityManager
                    .GetComponentData<Game.Net.Curve>(net).m_Bezier;
                var distance = MathUtils.Distance(curve.xz, point, out var t);
                if (distance >= bestDistance) continue;
                best = net;
                bestDistance = distance;
                bestPosition = MathUtils.Position(curve, t);
            }
            if (best == Entity.Null) return "kein Traeger-Netz mit Kurve";

            var compositionText = "keine Composition-Oberkante";
            if (EntityManager.HasComponent<Game.Net.Composition>(best))
            {
                var composition = EntityManager
                    .GetComponentData<Game.Net.Composition>(best).m_Edge;
                if (composition != Entity.Null
                    && EntityManager.Exists(composition)
                    && EntityManager.HasComponent<NetCompositionData>(composition))
                {
                    var data = EntityManager
                        .GetComponentData<NetCompositionData>(composition);
                    compositionText = $"Composition-Surface.max "
                        + $"{data.m_SurfaceHeight.max:+0.0000;-0.0000;0.0000} m, "
                        + $"Achse+Offset {bestPosition.y + data.m_SurfaceHeight.max:F4} m, "
                        + $"Breite {data.m_Width:F2} m";
                }
            }
            var prefabName = EntityManager.HasComponent<PrefabRef>(best)
                ? TerrainPrefabName(EntityManager
                    .GetComponentData<PrefabRef>(best).m_Prefab)
                : "ohne PrefabRef";
            return $"naechstes Traeger-Netz {Show(best)} '{prefabName}' "
                + $"in {bestDistance:F3} m, Achse-Y {bestPosition.y:F4} m, "
                + compositionText;
        }

        private static string GroundReferenceDescription(
            Entity prefab,
            bool angledSample,
            ref ComponentLookup<PlaceableObjectData> placeableData,
            ref ComponentLookup<ObjectGeometryData> objectGeometryData)
        {
            var placement = placeableData.TryGetComponent(prefab, out var placeable)
                ? placeable.m_Flags.ToString()
                : "ohne PlaceableObjectData";
            if (!objectGeometryData.TryGetComponent(prefab, out var geometry))
                return $"{(angledSample ? "gewinkelt" : "Punkt")}, "
                    + $"Geometrie fehlt, PlacementFlags {placement}";

            var mode = !angledSample
                ? "Punkt-/Wasserprobe"
                : (geometry.m_Flags & Game.Objects.GeometryFlags.HasBase) != 0
                    ? "Terrain-Grundriss, Maximum"
                    : "Terrain-Grundriss, Mittel und Neigung";
            var size = geometry.m_Bounds.max - geometry.m_Bounds.min;
            /*
             * DIE Y-AUSDEHNUNG FEHLTE, UND SIE ENTSCHEIDET EINE OFFENE FRAGE.
             *
             * Ein Aufkleber ist ein Decal; `DecalProperties` hat KEIN
             * Tiefenfeld, das Projektionsvolumen kommt allein aus den
             * Meshgrenzen. Gemessen sitzt der Aufkleber nach
             * `GroundHeightSystem` rund 1,6 cm unter der Flaeche, auf der er
             * liegen soll. Ob das ueberhaupt ein Problem ist, haengt genau
             * daran: liegt die Flaeche noch INNERHALB seines Volumens, wird
             * sie bemalt; liegt sie an dessen Rand, streiten beide um die
             * Tiefe - und das waere das gemeldete Flattern.
             *
             * Ohne die Y-Grenzen war das nicht zu entscheiden, sondern nur zu
             * vermuten. Deshalb stehen sie jetzt daneben, mitsamt dem
             * Abstand des Aufklebermittelpunkts zu Ober- und Unterkante.
             */
            return $"{mode}, Bounds {size.x:F2} x {size.z:F2} m, "
                + $"Y {geometry.m_Bounds.min.y:F3}..{geometry.m_Bounds.max.y:F3} m "
                + $"(Hoehe {size.y:F3} m), "
                + $"GeometryFlags {geometry.m_Flags}, PlacementFlags {placement}";
        }
    }
}
