using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Findet fuer jede Ladesaeule den kleinsten sicheren Rueckversatz.
     *
     * Die Diagnose vom 2026-08-14 hat an allen vier Saeulen denselben Grund
     * gemessen: an der hinteren Buchtenkante schneiden ihre 1,34 x 0,83 m
     * grossen Boxen die beiden 3,20 x 6,20 m grossen Elektro-Aufkleber. Die
     * Aufkleber sind nicht ueberschreibbar, die Saeule schon; deshalb bekam
     * sie `Overridden` und keine Render-Ebene.
     *
     * Das hier veraendert keine Bucht und keinen Fahrweg. Nur die Ausstattung
     * wandert in 0,01-m-Schritten nach hinten. Der Boxschnitt ist dieselbe
     * Funktion, die auch ParkingLotChargerDiagnostics fuer den Nachzustand
     * benutzt: beide 3D-Boxen werden um 0,01 m geschrumpft.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const double ChargerBackStep = 0.01;
        private const double ChargerPlacementEpsilon = 0.001;

        private sealed class PlannedDecalBox
        {
            internal ObjectGeometryData Geometry;
            internal CollisionMask Mask;
            internal float3 Position;
            internal quaternion Rotation;
        }

        private sealed class ChargerSearchResult
        {
            internal double2 Center;
            internal double Offset;
            internal double NearDepth;
            internal double FarDepth;
            internal double PreviousOffset;
            internal int PreviousBlockers;
        }

        /**
         * Setzt nur Saeulen, deren kompletter Grundriss in vorhandenem Gruen
         * liegt und weder Bucht noch Fahrweg schneidet.
         *
         * `md = 0` oder ein zu schmaler Streifen bedeutet bewusst: die beiden
         * Elektro-Aufkleber bleiben, aber es entsteht keine unsichtbare oder
         * falsch stehende Saeule. Ein `Owner` wird weiterhin nirgends gesetzt;
         * `SubObjectSystem` wuerde sie sonst ueber die Flaeche wuerfeln.
         */
        private int CreateChargerDefinitions(ParkingLayout layout,
            LayoutSettings settings, ParkingBayDecals.DecalPlan plan,
            ref TerrainHeightData heightData)
        {
            if (_chargerPrefab == Entity.Null || plan.Chargers.Length == 0) return 0;
            if (!TryGetPrefabCollision(_chargerPrefab, out var chargerGeometry,
                    out var chargerMask))
            {
                Mod.log.Warn("PLT-Ladesäulen ausgelassen: die Kollisionsbox des "
                    + $"Prefabs '{ChargerName}' ist nicht lesbar.");
                return 0;
            }
            if (settings.Md <= ChargerPlacementEpsilon)
            {
                Mod.log.Info($"PLT-Ladesäulen: 0 von {plan.Chargers.Length} gesetzt. "
                    + $"Der Grünstreifen ist abgeschaltet (md={settings.Md:F2} m); "
                    + "die Elektro-Aufkleber bleiben ohne Säule sichtbar.");
                return 0;
            }
            if (GrassOverridesCharger(chargerMask, out var grassFlags))
            {
                // Innerhalb des Gruens waere der Flaechenzweig dann der
                // naechste sichere Override-Treffer. Keine Saeule ist besser
                // als eine Entity, die erneut gebaut, aber nicht gerendert wird.
                Mod.log.Warn($"PLT-Ladesäulen: 0 von {plan.Chargers.Length} "
                    + "gesetzt. Die geladene Grasfläche kann diese "
                    + $"Kollisionsmaske überschreiben (Flags {grassFlags}); "
                    + "die Elektro-Aufkleber bleiben ohne Säule sichtbar.");
                return 0;
            }

            var decalBoxes = BuildPlannedDecalBoxes(plan, ref heightData);
            var created = 0;
            for (var i = 0; i < plan.Chargers.Length; i++)
            {
                var charger = plan.Chargers[i];
                if (!TryFindChargerPosition(layout, settings, charger,
                        chargerGeometry, chargerMask, decalBoxes, ref heightData,
                        out var found, out var reason))
                {
                    Mod.log.Info($"PLT-Ladesäule {i} ausgelassen: {reason} "
                        + "Die Elektro-Aufkleber bleiben ohne Säule sichtbar.");
                    continue;
                }

                Mod.log.Info($"PLT-Ladesäule {i}: Rückversatz {found.Offset:F2} m "
                    + "gewählt (kleinster gültiger 0,01-m-Schritt); bei "
                    + $"{found.PreviousOffset:F2} m noch {found.PreviousBlockers} "
                    + "nicht überschreibbare Aufklebertreffer, jetzt 0. "
                    + $"Der Grundriss belegt {found.NearDepth:F2} bis "
                    + $"{found.FarDepth:F2} m des {settings.Md:F2}-m-Grünstreifens.");

                var facing = ChargerFacesAisle ? charger.Facing : -charger.Facing;
                /**
                 * FESTER ZUFALLSWERT fuer die Saeule.
                 *
                 * `GenerateObjectsSystem` schreibt unseren
                 * `CreationDefinition.m_RandomSeed` als `PseudoRandomSeed` an
                 * das Objekt, und `MeshGroupSystem` waehlt damit aus dem
                 * `MeshGroup`-Puffer des Prefabs ein Modell aus. Ein frischer
                 * Zufallswert gab jeder Saeule ein anderes, teils falsches
                 * Aussehen; Seed 1 ist der bereits verwendete feste Messstand.
                 */
                var fixedSeed = new Unity.Mathematics.Random(ChargerVariantSeed);
                if (CreateBayDecalDefinition("Charger", i, _chargerPrefab,
                        found.Center, facing, ref heightData, ref fixedSeed))
                    created++;
            }
            return created;
        }

        private List<PlannedDecalBox> BuildPlannedDecalBoxes(
            ParkingBayDecals.DecalPlan plan, ref TerrainHeightData heightData)
        {
            var result = new List<PlannedDecalBox>(plan.Placements.Length);
            for (var i = 0; i < plan.Placements.Length; i++)
            {
                var placement = plan.Placements[i];
                // Immer die SICHTBAREN Aufkleber vermessen: dieser Weg laeuft
                // nur, wenn ueberhaupt Saeulen gesetzt werden, und das tut er
                // seit dem 2026-08-24 nur noch bei eingeschalteter Markierung.
                var prefab = PrefabFor(placement.Kind, unsichtbar: false);
                if (!TryGetPrefabCollision(prefab, out var geometry, out var mask))
                    continue;
                var facing = BayDecalFacesAisle
                    ? placement.Facing : -placement.Facing;
                if (!TryMeasureObjectPose(placement.Center, facing, ref heightData,
                        out var position, out var rotation)) continue;
                result.Add(new PlannedDecalBox
                {
                    Geometry = geometry,
                    Mask = mask,
                    Position = position,
                    Rotation = rotation,
                });
            }
            return result;
        }

        private bool TryFindChargerPosition(ParkingLayout layout,
            LayoutSettings settings, ParkingBayDecals.ChargerPlacement charger,
            ObjectGeometryData chargerGeometry, CollisionMask chargerMask,
            List<PlannedDecalBox> decalBoxes, ref TerrainHeightData heightData,
            out ChargerSearchResult result, out string reason)
        {
            result = null;
            reason = string.Empty;
            var facing = math.normalizesafe(new float2(
                (float)charger.Facing.x, (float)charger.Facing.y));
            if (math.lengthsq(facing) < 0.5f)
            {
                reason = "die Richtung zur Fahrgasse ist nicht bestimmbar.";
                return false;
            }
            var back = -facing;
            var modelFacing = ChargerFacesAisle ? facing : -facing;
            var rotation = quaternion.LookRotationSafe(
                new float3(modelFacing.x, 0f, modelFacing.y), math.up());
            var anchor = new double2(charger.Center.x, charger.Center.y);
            var maximumStep = (int)Math.Floor(
                (settings.Md + ChargerPlacementEpsilon) / ChargerBackStep);
            var previousOffset = 0.0;
            var previousBlockers = 0;
            var collisionFreeSeen = false;
            var collisionFreeStripFitSeen = false;
            var collisionFreeGrassFitSeen = false;

            for (var step = 0; step <= maximumStep; step++)
            {
                // Multiplikation statt Aufsummieren: sonst koennte ein
                // Gleitkommarest einen nominellen 0,55-m-Schritt verschieben.
                var offset = step * ChargerBackStep;
                var center = anchor + new double2(back.x, back.y) * offset;
                if (!TryMeasureObjectPose(center, modelFacing, ref heightData,
                        out var position, out _)) continue;

                // Dieser Test laeuft an JEDEM Schritt, auch wenn derselbe
                // Kandidat danach an der Flaechengrenze verworfen wird.
                var blockers = CountBlockingDecals(position, rotation,
                    chargerGeometry, chargerMask, decalBoxes);
                if (blockers == 0)
                {
                    collisionFreeSeen = true;
                    // Die teureren Polygonpruefungen sind erst sinnvoll, wenn
                    // die 3D-Box wirklich frei ist. Die Kollisionsmessung
                    // selbst lief trotzdem an jedem einzelnen Zentimeter.
                    var footprint = ObjectFootprint(
                        position, rotation, chargerGeometry);
                    var fits = FitsChargerArea(layout, settings, anchor, back,
                        footprint, out var nearDepth, out var farDepth,
                        out var fitsStrip, out var fitsGrass);
                    collisionFreeStripFitSeen |= fitsStrip;
                    collisionFreeGrassFitSeen |= fitsStrip && fitsGrass;
                    if (fits)
                    {
                        result = new ChargerSearchResult
                        {
                            Center = center,
                            Offset = offset,
                            NearDepth = nearDepth,
                            FarDepth = farDepth,
                            PreviousOffset = previousOffset,
                            PreviousBlockers = previousBlockers,
                        };
                        return true;
                    }
                }

                previousOffset = offset;
                previousBlockers = blockers;
            }

            if (!collisionFreeSeen)
                reason = $"kein Schritt bis {settings.Md:F2} m trennt ihre "
                    + "0,01 m geschrumpfte 3D-Box von den Aufklebern.";
            else if (!collisionFreeStripFitSeen)
                reason = $"der {settings.Md:F2}-m-Grünstreifen ist für Box und "
                    + "kollisionsfreien Rückversatz zu schmal.";
            else if (!collisionFreeGrassFitSeen)
                reason = "hinter diesem Buchtenpaar liegt keine vollständig "
                    + "deckende Grünfläche.";
            else
                reason = "kein kollisionsfreier Schritt liegt vollständig im "
                    + "Areal und außerhalb von Buchten und Fahrwegen.";
            return false;
        }

        private bool TryGetPrefabCollision(Entity prefab,
            out ObjectGeometryData geometry, out CollisionMask mask)
        {
            geometry = default;
            mask = default;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                || !EntityManager.HasComponent<ObjectGeometryData>(prefab))
                return false;
            geometry = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
            // Wie OverrideSystem im normalen Spiel: Marker zaehlen nicht.
            mask = ObjectUtils.GetCollisionMask(geometry, true);
            return true;
        }

        private bool GrassOverridesCharger(CollisionMask chargerMask,
            out Game.Areas.GeometryFlags flags)
        {
            flags = 0;
            if (_grassSurfacePrefab == Entity.Null
                || !EntityManager.Exists(_grassSurfacePrefab)
                || !EntityManager.HasComponent<AreaGeometryData>(_grassSurfacePrefab))
                return false;
            var geometry = EntityManager
                .GetComponentData<AreaGeometryData>(_grassSurfacePrefab);
            flags = geometry.m_Flags;
            return (flags & Game.Areas.GeometryFlags.CanOverrideObjects) != 0
                && (chargerMask
                    & Game.Areas.AreaUtils.GetCollisionMask(geometry)) != 0;
        }

        private static bool TryMeasureObjectPose(double2 center, double2 facing,
            ref TerrainHeightData heightData, out float3 position,
            out quaternion rotation)
        {
            var point = new float2((float)center.x, (float)center.y);
            position = default;
            rotation = quaternion.identity;
            if (!math.all(math.isfinite(point))) return false;
            var height = TerrainUtils.SampleHeight(
                ref heightData, new float3(point.x, 0f, point.y));
            if (!math.isfinite(height)) return false;
            position = new float3(point.x, height, point.y);
            var forward = new float3((float)facing.x, 0f, (float)facing.y);
            if (math.lengthsq(forward) >= 1e-9f)
                rotation = quaternion.LookRotationSafe(math.normalize(forward), math.up());
            return true;
        }

        private static int CountBlockingDecals(float3 chargerPosition,
            quaternion chargerRotation, ObjectGeometryData chargerGeometry,
            CollisionMask chargerMask, List<PlannedDecalBox> decals)
        {
            var blockers = 0;
            for (var i = 0; i < decals.Count; i++)
            {
                var decal = decals[i];
                if ((chargerMask & decal.Mask) == 0
                    || !ShrunkBoxesIntersect(chargerPosition, chargerRotation,
                        chargerGeometry, decal.Position, decal.Rotation,
                        decal.Geometry)) continue;
                var overridable = (decal.Geometry.m_Flags
                    & (GeometryFlags.Overridable | GeometryFlags.DeleteOverridden))
                    == GeometryFlags.Overridable;
                if (!overridable) blockers++;
            }
            return blockers;
        }

        /** Dieselbe 3D-Rechnung wie der Objektzweig der Diagnose. */
        private static bool ShrunkBoxesIntersect(float3 firstPosition,
            quaternion firstRotation, ObjectGeometryData firstGeometry,
            float3 secondPosition, quaternion secondRotation,
            ObjectGeometryData secondGeometry)
        {
            var secondWorld = ObjectUtils.CalculateBounds(
                secondPosition, secondRotation, secondGeometry);
            var center = MathUtils.Center(secondWorld);
            var firstOffset = math.mul(math.inverse(firstRotation),
                firstPosition - center);
            var secondOffset = math.mul(math.inverse(secondRotation),
                secondPosition - center);
            var firstBox = new Box3
            {
                bounds = MathUtils.Expand(
                    CollisionBounds(firstGeometry) + firstOffset, -0.01f),
                rotation = firstRotation,
            };
            var secondBox = new Box3
            {
                bounds = MathUtils.Expand(
                    CollisionBounds(secondGeometry) + secondOffset, -0.01f),
                rotation = secondRotation,
            };
            return MathUtils.Intersect(firstBox, secondBox, out _, out _);
        }

        private static double2[] ObjectFootprint(float3 position,
            quaternion rotation, ObjectGeometryData geometry)
        {
            var corners = ObjectUtils.CalculateBaseCorners(
                position, rotation, geometry.m_Bounds);
            return new[]
            {
                new double2(corners.a.x, corners.a.z),
                new double2(corners.b.x, corners.b.z),
                new double2(corners.c.x, corners.c.z),
                new double2(corners.d.x, corners.d.z),
            };
        }

        private bool FitsChargerArea(ParkingLayout layout, LayoutSettings settings,
            double2 anchor, float2 back, double2[] footprint,
            out double nearDepth, out double farDepth,
            out bool fitsStrip, out bool fitsGrass)
        {
            nearDepth = double.PositiveInfinity;
            farDepth = double.NegativeInfinity;
            var backDouble = new double2(back.x, back.y);
            for (var i = 0; i < footprint.Length; i++)
            {
                var depth = math.dot(footprint[i] - anchor, backDouble);
                nearDepth = Math.Min(nearDepth, depth);
                farDepth = Math.Max(farDepth, depth);
            }
            fitsStrip = nearDepth >= -ChargerPlacementEpsilon
                && farDepth <= settings.Md + ChargerPlacementEpsilon;
            fitsGrass = CoveredByOnePolygon(footprint, layout.GrassSurface);
            if (!fitsStrip || !fitsGrass) return false;

            // Das gezeichnete Areal steht im Werkzeug weiter unveraendert zur
            // Verfuegung. Die Randlinie des Layouts ist bereits nach innen
            // versetzt und waere deshalb das falsche Messobjekt.
            if (!CoveredByPolygon(footprint, _points)) return false;
            if (OverlapsAny(footprint, layout.Bay)
                || OverlapsAny(footprint, layout.PerimeterQuad)
                || OverlapsAny(footprint, layout.AisleQuad)
                || OverlapsAny(footprint, layout.CrossQuad)
                || OverlapsAny(footprint, layout.EntranceQuad)) return false;
            return true;
        }

        private static bool CoveredByOnePolygon(double2[] footprint,
            float2[][] polygons)
        {
            if (polygons == null) return false;
            for (var i = 0; i < polygons.Length; i++)
                if (CoveredByPolygon(footprint, polygons[i])) return true;
            return false;
        }

        private static bool CoveredByPolygon(double2[] footprint,
            IReadOnlyList<float2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;
            for (var i = 0; i < footprint.Length; i++)
                if (!PointInsideOrOn(footprint[i], polygon)) return false;

            // Nur Eckpunkte zu pruefen reicht bei einem konkaven Polygon nicht:
            // eine Rechteckkante kann es verlassen und spaeter wieder betreten.
            for (var i = 0; i < footprint.Length; i++)
            {
                var a = footprint[i];
                var b = footprint[(i + 1) % footprint.Length];
                for (var j = 0; j < polygon.Count; j++)
                {
                    var c = (double2)polygon[j];
                    var d = (double2)polygon[(j + 1) % polygon.Count];
                    if (ProperlyIntersects(a, b, c, d)) return false;
                }
            }
            return true;
        }

        private static bool PointInsideOrOn(double2 point,
            IReadOnlyList<float2> polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = (double2)polygon[j];
                var b = (double2)polygon[i];
                if (DistanceToSegmentSquared(point, a, b)
                    <= ChargerPlacementEpsilon * ChargerPlacementEpsilon)
                    return true;
                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y)
                        / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static double DistanceToSegmentSquared(double2 point,
            double2 a, double2 b)
        {
            var edge = b - a;
            var lengthSquared = math.lengthsq(edge);
            if (lengthSquared < 1e-18) return math.distancesq(point, a);
            var t = math.clamp(math.dot(point - a, edge) / lengthSquared, 0, 1);
            return math.distancesq(point, a + edge * t);
        }

        private static bool ProperlyIntersects(double2 a, double2 b,
            double2 c, double2 d)
        {
            var abC = Cross(b - a, c - a);
            var abD = Cross(b - a, d - a);
            var cdA = Cross(d - c, a - c);
            var cdB = Cross(d - c, b - c);
            const double epsilon = 1e-9;
            return ((abC > epsilon && abD < -epsilon)
                    || (abC < -epsilon && abD > epsilon))
                && ((cdA > epsilon && cdB < -epsilon)
                    || (cdA < -epsilon && cdB > epsilon));
        }

        private static double Cross(double2 a, double2 b) =>
            a.x * b.y - a.y * b.x;

        private static bool OverlapsAny(double2[] footprint, float2[][] polygons)
        {
            if (polygons == null) return false;
            for (var i = 0; i < polygons.Length; i++)
            {
                var polygon = polygons[i];
                if (polygon == null || polygon.Length < 3) continue;
                var converted = new double2[polygon.Length];
                for (var j = 0; j < polygon.Length; j++) converted[j] = polygon[j];
                if (ConvexOverlap(footprint, converted)) return true;
            }
            return false;
        }

        /** Trennachsen-Test; Beruehrung bis 1 mm ist keine Flaechenbelegung. */
        private static bool ConvexOverlap(double2[] first, double2[] second)
        {
            for (var set = 0; set < 2; set++)
            {
                var polygon = set == 0 ? first : second;
                for (var i = 0; i < polygon.Length; i++)
                {
                    var edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                    var axis = math.normalizesafe(new double2(-edge.y, edge.x));
                    if (math.lengthsq(axis) < 0.5) continue;
                    Project(first, axis, out var firstMin, out var firstMax);
                    Project(second, axis, out var secondMin, out var secondMax);
                    if (firstMax <= secondMin + ChargerPlacementEpsilon
                        || secondMax <= firstMin + ChargerPlacementEpsilon)
                        return false;
                }
            }
            return true;
        }

        private static void Project(double2[] polygon, double2 axis,
            out double minimum, out double maximum)
        {
            minimum = double.PositiveInfinity;
            maximum = double.NegativeInfinity;
            for (var i = 0; i < polygon.Length; i++)
            {
                var value = math.dot(polygon[i], axis);
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
    }
}
