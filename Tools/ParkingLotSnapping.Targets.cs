using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Die drei Fangarten, die an echter Geometrie haengen.
     *
     * Alle drei liefern neben der Lage auch eine TANGENTE. Die ist nicht
     * Beiwerk: sie geht als Bezugsachse in den Punkt ein und ist damit die
     * Grundlage, auf der der Achsenfang beim naechsten Punkt rechnet. Wer den
     * ersten Punkt an einen Bordstein setzt, bekommt so beim zweiten Punkt
     * "laengs der Strasse" und "quer zur Strasse" zum Einrasten angeboten.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Suchradius fuer Fahrbahnkanten. Deutlich groesser als der
         * Fangradius: der Cursor liegt an einer Aussenecke schon eine halbe
         * Strassenbreite von der Mittellinie entfernt, nach der der Suchbaum
         * indiziert.
         */
        private const float RoadSearchRadius = 48f;

        // `_netSearchSystem` und `_areaSearchSystem` gehoeren dem Debug-Abzug
        // und werden hier mitbenutzt; nur der Objektbaum ist neu.
        private Game.Objects.SearchSystem _objectSearchSystem;
        private Game.Zones.SearchSystem _zoneSearchSystem;

        private struct EntityIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;

            public bool Intersect(QuadTreeBoundsXZ bounds)
                => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds))
                    Results.Add(entity);
            }
        }

        private struct AreaItemIterator
            : INativeQuadTreeIterator<AreaSearchItem, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;

            public bool Intersect(QuadTreeBoundsXZ bounds)
                => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, AreaSearchItem item)
            {
                // Der Baum ist nach DREIECKEN indiziert; uns interessiert die
                // Flaeche, zu der sie gehoeren. Doppelte siebt der Aufrufer aus.
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds))
                    Results.Add(item.m_Area);
            }
        }

        private void InitializeSnapping()
        {
            _objectSearchSystem
                = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            _zoneSearchSystem
                = World.GetOrCreateSystemManaged<Game.Zones.SearchSystem>();
        }

        private struct ZoneIterator : INativeQuadTreeIterator<Entity, Bounds2>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;

            public bool Intersect(Bounds2 bounds)
                => MathUtils.Intersect(bounds, Bounds);

            public void Iterate(Bounds2 bounds, Entity entity)
            {
                if (MathUtils.Intersect(bounds, Bounds)) Results.Add(entity);
            }
        }

        /**
         * FAHRBAHNKANTE.
         *
         * Aus `EdgeGeometry` und den Knotengeometrien - denselben Kurven, die
         * auch das Vanilla-Arealwerkzeug fuer seinen NetSide-Fang benutzt. Sie
         * enthalten Kurven, Aufweitungen und Knotenuebergaenge bereits in
         * ihrer tatsaechlichen Lage. Die Mittellinie plus halbe Breite waere
         * an jeder Kreuzung und jeder Aufweitung falsch.
         */
        private void CollectRoadEdges(float3 raw, ref SnapCandidate best)
        {
            if (_netSearchSystem == null) return;

            var tree = _netSearchSystem.GetNetSearchTree(readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = new Bounds2(raw.xz - RoadSearchRadius,
                                     raw.xz + RoadSearchRadius),
                Results = results,
            };
            tree.Iterate(ref iterator);

            /**
             * Auf einer Kopie sammeln und am Ende zurueckschreiben: C# laesst
             * einen `ref`-Parameter nicht in eine lokale Funktion hinein
             * (CS1628). Das aendert nichts am Ergebnis - `Offer` uebernimmt
             * nur echte Verbesserungen, die Reihenfolge ist also egal, und
             * die Geradenliste fuer die Schnittpunkte liegt ohnehin im Feld.
             */
            var found = best;

            void Consider(Bezier4x3 curve)
            {
                if (!math.all(math.isfinite(curve.a)) || !math.all(math.isfinite(curve.d))
                    || math.distancesq(curve.a, curve.d) < 0.01f) return;
                if (MathUtils.Distance(curve.xz, raw.xz, out var t) >= SnapDistance)
                    return;
                var direction = math.normalizesafe(MathUtils.Tangent(curve, t).xz);
                if (math.lengthsq(direction) < 0.5f) return;
                RegisterSnap(ref found, raw, LevelNet, SnapKind.RoadEdge,
                    MathUtils.Position(curve, t), direction);
            }

            /**
             * Die Knotenkurve wird auf ihre Anfangstangente begradigt - genau
             * so macht es `AreaToolSystem.SnapNodeCurve`. Die gekruemmte
             * Knotengeometrie selbst waere als Arealkante unbrauchbar.
             */
            void ConsiderNodeCurve(Bezier4x3 curve)
            {
                var direction = MathUtils.StartTangent(curve);
                direction = MathUtils.Normalize(direction, direction.xz);
                direction.y = math.clamp(direction.y, -1f, 1f);
                var end = curve.a + direction * math.dot(curve.d - curve.a, direction);
                if (!math.all(math.isfinite(end))
                    || math.distancesq(curve.a, end) < 0.01f) return;
                var line = new Line3.Segment(curve.a, end);
                if (MathUtils.Distance(line.xz, raw.xz, out var t) >= SnapDistance)
                    return;
                var axis = math.normalizesafe(direction.xz);
                if (math.lengthsq(axis) < 0.5f) return;
                RegisterSnap(ref found, raw, LevelNet, SnapKind.RoadEdge,
                    MathUtils.Position(line, t), axis);
            }

            void ConsiderNodeGeometry(EdgeNodeGeometry geometry)
            {
                if (geometry.m_MiddleRadius > 0f)
                {
                    ConsiderNodeCurve(geometry.m_Left.m_Left);
                    ConsiderNodeCurve(geometry.m_Left.m_Right);
                    ConsiderNodeCurve(geometry.m_Right.m_Left);
                    ConsiderNodeCurve(geometry.m_Right.m_Right);
                }
                else
                {
                    ConsiderNodeCurve(geometry.m_Left.m_Left);
                    ConsiderNodeCurve(geometry.m_Right.m_Right);
                }
            }

            // Ein Tunnel liegt unter uns und ist keine Arealkante.
            bool Snappable(Entity composition) =>
                composition == Entity.Null
                || !EntityManager.HasComponent<NetCompositionData>(composition)
                || (EntityManager.GetComponentData<NetCompositionData>(composition)
                    .m_Flags.m_General & CompositionFlags.General.Tunnel) == 0;

            var seen = new HashSet<Entity>();
            for (var i = 0; i < results.Length; i++)
            {
                var entity = results[i];
                if (!EntityManager.HasComponent<Game.Net.Edge>(entity)
                    || !EntityManager.HasComponent<Game.Net.Road>(entity)
                    // Unterelemente fremder Gebaeude und laufende Vorschauen
                    // sind keine Strassen, an die man ein Areal legt.
                    || EntityManager.HasComponent<Owner>(entity)
                    || EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)) continue;
                if (!seen.Add(entity)) continue;

                var composition = EntityManager.HasComponent<Composition>(entity)
                    ? EntityManager.GetComponentData<Composition>(entity)
                    : default;

                if (Snappable(composition.m_Edge)
                    && EntityManager.HasComponent<EdgeGeometry>(entity))
                {
                    var geometry = EntityManager.GetComponentData<EdgeGeometry>(entity);
                    Consider(geometry.m_Start.m_Left);
                    Consider(geometry.m_Start.m_Right);
                    Consider(geometry.m_End.m_Left);
                    Consider(geometry.m_End.m_Right);
                }
                if (EntityManager.HasComponent<StartNodeGeometry>(entity)
                    && Snappable(composition.m_StartNode))
                    ConsiderNodeGeometry(EntityManager
                        .GetComponentData<StartNodeGeometry>(entity).m_Geometry);
                if (EntityManager.HasComponent<EndNodeGeometry>(entity)
                    && Snappable(composition.m_EndNode))
                    ConsiderNodeGeometry(EntityManager
                        .GetComponentData<EndNodeGeometry>(entity).m_Geometry);
            }

            best = found;
        }

        /**
         * KANTEN BESTEHENDER GRUNDSTUECKE.
         *
         * Nur Flaechen mit `Game.Areas.Lot` - also Grundstuecksumrisse,
         * einschliesslich der Parkplaetze, die dieses Werkzeug selbst anlegt.
         * Die Belagsflaechen darin (`Surface`) bleiben aussen vor: das sind
         * Dutzende kleiner Vielecke pro Parkplatz, und an deren Innenkanten
         * einzurasten waere kein Fang mehr, sondern ein Zittern.
         *
         * Gefangen wird am Vieleck aus dem `Node`-Puffer, nicht an der
         * Dreieckszerlegung wie in Vanilla - die Diagonalen der Zerlegung sind
         * keine sichtbaren Kanten.
         */
        private void CollectAreaEdges(float3 raw, ref SnapCandidate best)
        {
            if (_areaSearchSystem == null) return;

            var tree = _areaSearchSystem.GetSearchTree(readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new AreaItemIterator
            {
                Bounds = new Bounds2(raw.xz - SnapDistance, raw.xz + SnapDistance),
                Results = results,
            };
            tree.Iterate(ref iterator);

            var seen = new HashSet<Entity>();
            for (var i = 0; i < results.Length; i++)
            {
                var entity = results[i];
                if (entity == Entity.Null || !seen.Add(entity)) continue;
                if (!EntityManager.HasComponent<Game.Areas.Lot>(entity)
                    || EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)) continue;
                if (!EntityManager.HasBuffer<Game.Areas.Node>(entity)) continue;

                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(entity, true);
                if (nodes.Length < 3) continue;

                for (var n = 0; n < nodes.Length; n++)
                {
                    var from = nodes[n].m_Position;
                    var to = nodes[(n + 1) % nodes.Length].m_Position;
                    var line = new Line3.Segment(from, to);
                    if (MathUtils.Distance(line.xz, raw.xz, out var t) >= SnapDistance)
                        continue;
                    var direction = math.normalizesafe(to.xz - from.xz);
                    if (math.lengthsq(direction) < 0.5f) continue;

                    /**
                     * Die Ecke schlaegt die Kante, wenn man nah genug an ihr
                     * ist - sonst kaeme man nie genau auf den Eckpunkt. So
                     * macht es auch `AreaIterator.CheckLine`.
                     */
                    var toStart = math.distance(from.xz, raw.xz);
                    var toEnd = math.distance(to.xz, raw.xz);
                    float3 position;
                    if (toStart <= SnapDistance && toStart <= toEnd) position = from;
                    else if (toEnd <= SnapDistance) position = to;
                    else position = MathUtils.Position(line, t);

                    RegisterSnap(ref best, raw, LevelArea, SnapKind.AreaEdge,
                        position, direction);
                }
            }
        }

        /**
         * GEBAEUDEKANTE.
         *
         * Ein Parkplatz liegt oft buendig an einer Hauswand. Gefangen wird am
         * Grundriss-Rechteck, das `ObjectUtils.CalculateBaseCorners` liefert.
         *
         * Bei Gebaeuden zaehlt das GRUNDSTUECK, nicht der Baukoerper: Vanilla
         * ersetzt die Abmessungen durch `m_LotSize * 4` (die Zellen sind 8 m,
         * also halbe Kantenlaenge je Richtung). Sonst raste man an der Wand
         * ein und stuende mitten im Vorgarten, der dem Haus gehoert.
         *
         * Runde Objekte bleiben aussen vor - ein Kreis hat keine Kante, an der
         * sich ein rechteckiger Parkplatz ausrichten liesse.
         */
        private void CollectObjectSides(float3 raw, ref SnapCandidate best)
        {
            if (_objectSearchSystem == null) return;

            var tree = _objectSearchSystem.GetStaticSearchTree(
                readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = new Bounds2(raw.xz - SnapDistance, raw.xz + SnapDistance),
                Results = results,
            };
            tree.Iterate(ref iterator);

            /**
             * Auf einer Kopie sammeln und am Ende zurueckschreiben: C# laesst
             * einen `ref`-Parameter nicht in eine lokale Funktion hinein
             * (CS1628). Das aendert nichts am Ergebnis - `Offer` uebernimmt
             * nur echte Verbesserungen, die Reihenfolge ist also egal, und
             * die Geradenliste fuer die Schnittpunkte liegt ohnehin im Feld.
             */
            var found = best;

            var heightData = _terrainSystem.GetHeightData();

            /**
             * `Line2` statt `Line2.Segment`: die Fassadenlinie gilt auch ein
             * Stueck ueber die Hausecke hinaus, sonst bricht die Flucht
             * genau dort ab, wo man sie am ehesten braucht. Genau so rechnet
             * `ObjectIterator.CheckLine`.
             */
            void ConsiderSide(Line3 side)
            {
                var line = new Line2(side.a.xz, side.b.xz);
                if (math.distancesq(line.a, line.b) < 0.01f) return;
                if (MathUtils.Distance(line, raw.xz, out var t) >= SnapDistance)
                    return;
                var direction = math.normalizesafe(line.b - line.a);
                if (math.lengthsq(direction) < 0.5f) return;
                var flat = MathUtils.Position(line, t);
                // Die Hoehe des Gebaeudesockels taugt nicht: der Fang schiebt
                // bis zu 8 m weit, und am Hang liegt der Boden dort anders.
                var height = Game.Simulation.TerrainUtils.SampleHeight(
                    ref heightData, new float3(flat.x, raw.y, flat.y));
                RegisterSnap(ref found, raw, LevelObject, SnapKind.ObjectSide,
                    new float3(flat.x, height, flat.y), direction);
            }

            var seen = new HashSet<Entity>();
            for (var i = 0; i < results.Length; i++)
            {
                var entity = results[i];
                if (!seen.Add(entity)) continue;
                if (!EntityManager.HasComponent<Game.Objects.Transform>(entity)
                    || !EntityManager.HasComponent<PrefabRef>(entity)
                    // Unterobjekte gehoeren zu ihrem Besitzer und haben keine
                    // eigene Kante, an der man ein Grundstueck ausrichtet.
                    || EntityManager.HasComponent<Owner>(entity)
                    || EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)) continue;

                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (!EntityManager.HasComponent<ObjectGeometryData>(prefab)) continue;
                var geometry = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                if ((geometry.m_Flags & Game.Objects.GeometryFlags.Circular) != 0)
                    continue;

                var bounds = geometry.m_Bounds;
                if (EntityManager.HasComponent<BuildingData>(prefab))
                {
                    // m_LotSize zaehlt Zellen, keine Meter - eine Zelle ist
                    // 8 m breit, die halbe Kantenlaenge also Zellen * 4.
                    var lotSize = (float2)EntityManager
                        .GetComponentData<BuildingData>(prefab).m_LotSize;
                    bounds.min.xz = lotSize * -4f;
                    bounds.max.xz = lotSize * 4f;
                }

                var transform = EntityManager
                    .GetComponentData<Game.Objects.Transform>(entity);
                var corners = ObjectUtils.CalculateBaseCorners(
                    transform.m_Position, transform.m_Rotation, bounds);
                ConsiderSide(corners.ab);
                ConsiderSide(corners.bc);
                ConsiderSide(corners.cd);
                ConsiderSide(corners.da);
            }

            best = found;
        }
    
        /**
         * ZELLENRASTER.
         *
         * Ein Parkplatz neben bebautem Land soll auf demselben Raster sitzen
         * wie die Grundstuecke daneben, sonst steht er schief in der Strasse.
         * CS2 verwaltet dieses Raster in ZONENBLOECKEN: jeder Block hat eine
         * Lage, eine Richtung und ein Feld aus 8-m-Zellen.
         *
         * Nachbau von `NetToolSystem.HandleZoneGrid`: die naechste SICHTBARE
         * Zelle suchen, den Versatz zu ihr auf 8 m runden - laengs und quer -
         * und beide Achsen als Gerade melden. Damit rastet die Ecke aufs
         * Raster ein und kann sich zugleich mit einem Bordstein zu einer
         * exakten Ecke schneiden.
         *
         * MIT VERSATZ 4, damit die Ecke auf der ZELLENKANTE landet.
         *
         * Hier stand das Gegenteil im Code, obwohl der Kommentar schon immer
         * "eine Grundstuecksecke gehoert auf die Zellenkante" sagte:
         * `GetCellPosition` liefert die MITTE der Zelle - der Versatz ist
         * `(size - 2*index - 1) * 4`, also stets ein UNGERADES Vielfaches von
         * 4 -, und ein Runden auf 8 m von dort aus landet wieder auf Mitten.
         * Die Ecke sass damit 4 m INNERHALB der Zelle.
         *
         * Nachgerechnet mit CS2s eigenen Formeln, Blocktiefe 6 Zellen:
         * Mitten liegen bei 20/12/4/-4/-12/-20 relativ zur Blockmitte, Kanten
         * bei 16/8/0/-8/-16/-24. Der Fehler ist konstant 4 m, in BEIDEN
         * Achsen - dem Nutzer fiel er an einer auf, weil laengs der Strasse
         * meist der Bordsteinfang gewinnt und die Abweichung verdeckt.
         *
         * Vanillas `offset = 4` fuer Strassen mit gerader Zellenzahl legt die
         * FAHRBAHN mittig auf die Zelle; fuer eine Grundstuecksecke ist
         * derselbe Versatz genau richtig, nur mit anderer Begruendung.
         */
        private void CollectZoneGrid(float3 raw, ref SnapCandidate best)
        {
            if (_zoneSearchSystem == null) return;

            // Reichweite wie Vanilla: eine Zelldiagonale plus Rand.
            const float radius = 8f * 1.4142136f + SnapDistance;
            var tree = _zoneSearchSystem.GetSearchTree(readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(8, Allocator.Temp);
            var iterator = new ZoneIterator
            {
                Bounds = new Bounds2(raw.xz - radius, raw.xz + radius),
                Results = results,
            };
            tree.Iterate(ref iterator);

            var bestDistance = radius;
            var cellPosition = float3.zero;
            var cellDirection = float2.zero;
            for (var i = 0; i < results.Length; i++)
            {
                var entity = results[i];
                if (!EntityManager.HasComponent<Game.Zones.Block>(entity)
                    || !EntityManager.HasBuffer<Game.Zones.Cell>(entity)) continue;
                var block = EntityManager.GetComponentData<Game.Zones.Block>(entity);
                var cells = EntityManager.GetBuffer<Game.Zones.Cell>(entity, true);
                var index = math.clamp(Game.Zones.ZoneUtils.GetCellIndex(block, raw.xz),
                    0, block.m_Size - 1);
                if (index.x < 0 || index.y < 0
                    || index.x >= block.m_Size.x || index.y >= block.m_Size.y) continue;
                var cell = cells[index.x + index.y * block.m_Size.x];
                // Unsichtbare Zellen sind kein Raster, an dem sich jemand
                // ausrichtet - sie liegen unter Wasser oder ausserhalb.
                if ((cell.m_State & Game.Zones.CellFlags.Visible) == 0) continue;
                var position = Game.Zones.ZoneUtils.GetCellPosition(block, index);
                var distance = math.distance(position.xz, raw.xz);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                cellPosition = position;
                cellDirection = block.m_Direction;
            }
            if (math.lengthsq(cellDirection) < 0.5f) return;

            var across = MathUtils.Right(cellDirection);
            var delta = raw.xz - cellPosition.xz;
            var alongStep = MathUtils.Snap(math.dot(delta, cellDirection), 8f, 4f);
            var acrossStep = MathUtils.Snap(math.dot(delta, across), 8f, 4f);
            var snapped = cellPosition.xz + cellDirection * alongStep + across * acrossStep;

            var heightData = _terrainSystem.GetHeightData();
            var world = new float3(snapped.x,
                Game.Simulation.TerrainUtils.SampleHeight(
                    ref heightData, new float3(snapped.x, raw.y, snapped.y)),
                snapped.y);

            // Beide Achsen melden, nicht nur den Punkt: erst dadurch kann sich
            // das Raster mit einem Bordstein zu einer echten Ecke schneiden.
            RegisterSnap(ref best, raw, LevelZoneGrid, SnapKind.ZoneGrid,
                world, cellDirection, isGuide: true);
            RegisterSnap(ref best, raw, LevelZoneGrid, SnapKind.ZoneGrid,
                world, across, isGuide: true);
        }
    }
}
