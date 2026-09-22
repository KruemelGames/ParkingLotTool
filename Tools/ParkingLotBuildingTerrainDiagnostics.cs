using System;
using System.Collections.Generic;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Misst den Gebaeude-Terrainpfad am echten PLT-Lot und Begleiter.
     *
     * Das System laeuft unmittelbar vor `TerrainSystem`. Es schreibt nichts
     * an Entities oder Terrain, sondern kopiert die vorhandenen Komponenten
     * und die CPU-Heightmapzellen im aus dem Prefab abgeleiteten Wirkradius.
     */
    public sealed partial class ParkingLotBuildingTerrainBeforeSystem
        : GameSystemBase
    {
        private sealed class TerrainSnapshot
        {
            internal Entity Companion;
            internal Entity Lot;
            internal int CapturedFrame;
            internal int2 MinCell;
            internal int2 MaxCell;
            internal int ResolutionX;
            internal ushort[] Heights;
            internal bool FirstPassLogged;
            internal bool TerraformPresent;
            internal bool DontRaise;
            internal bool DontLower;
            internal float3 CompanionPosition;
            internal quaternion CompanionRotation;
            internal float FlatX0;
            internal float FlatZ0;
            internal float FlatX1;
            internal float FlatZ1;
            internal int RenderRequestedFrame = -1;
            internal int ObservedPasses;
        }

        private TerrainSystem _terrain;
        private WaterSystem _water;
        private PrefabSystem _prefabSystem;
        private EntityQuery _lots;
        private EntityQuery _companions;
        private readonly HashSet<Entity> _loggedLots = new HashSet<Entity>();
        private readonly HashSet<Entity> _loggedCompanions =
            new HashSet<Entity>();
        private readonly Dictionary<Entity, TerrainSnapshot> _snapshots =
            new Dictionary<Entity, TerrainSnapshot>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _water = World.GetOrCreateSystemManaged<WaterSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            _companions = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        /**
         * WAS AUS DEM SPIELSTAND KOMMT, WURDE NICHT GERADE GEBAUT.
         *
         * Diese Pruefung beantwortet eine einzige Frage: *wir haben eben
         * etwas gesetzt - hat die Sperre gehalten?* Dafuer merkt sie sich in
         * `_loggedCompanions`, wen sie schon kennt, und nimmt von jedem
         * Unbekannten einen Abzug der Heightmap.
         *
         * Diese Menge ist beim Start leer. Nach dem Laden ist damit JEDER
         * vorhandene Begleiter "unbekannt" - und genau in dem Moment
         * schreibt CS2 die Heightmap ohnehin neu und laesst alle Lots
         * planieren. Die Pruefung sah also eine Aenderung, die sie gar nicht
         * meint, und meldete sie als Fehler.
         *
         * Ein Tester hat am 2026-09-22 direkt beim Laden 19 Stueck bekommen,
         * eins je Parkplatz in seinem Spielstand. Das ist die Zahl seiner
         * Parkplaetze, nicht die Zahl seiner Probleme.
         *
         * Hier werden die vorhandenen Begleiter deshalb als bekannt
         * eingetragen, ohne Abzug. Geprueft wird ab jetzt nur noch, was
         * WAEHREND dieser Sitzung dazukommt - also das, wofuer die Sperre da
         * ist.
         */
        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _loggedLots.Clear();
            _loggedCompanions.Clear();
            _snapshots.Clear();
            if (mode != GameMode.Game) return;

            _companions.CompleteDependency();
            using var vorhanden = _companions.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < vorhanden.Length; i++)
                _loggedCompanions.Add(vorhanden[i]);
            if (vorhanden.Length > 0)
                Mod.log.Info("PLT-Gebaeudepfad: " + vorhanden.Length
                    + " Begleiter aus dem Spielstand uebernommen und NICHT "
                    + "auf die Terraform-Sperre geprueft - beim Laden "
                    + "planiert CS2 alle Lots neu, eine Abweichung dort "
                    + "sagt nichts ueber unsere Sperre. Geprueft wird, was "
                    + "in dieser Sitzung gebaut wird.");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            _lots.CompleteDependency();
            _companions.CompleteDependency();
            LogNewLots();
            CaptureNewCompanions();
        }

        private void LogNewLots()
        {
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                if (_loggedLots.Contains(lot) || !IsOwnLot(lot)) continue;
                _loggedLots.Add(lot);

                var prefab = EntityManager.GetComponentData<PrefabRef>(lot)
                    .m_Prefab;
                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
                NodeStatistics(nodes, out var minimum, out var maximum,
                    out var mean);
                var geometry = EntityManager.HasComponent<Game.Areas.Geometry>(
                        lot)
                    ? EntityManager.GetComponentData<Game.Areas.Geometry>(lot)
                    : default(Game.Areas.Geometry);

                Mod.log.Info("PLT-Gebaeudepfad LOT-IST " + Show(lot)
                    + ": Komponenten (" + ComponentCount(lot) + ") = "
                    + ComponentList(lot) + ". Prefab-Komponenten ("
                    + ComponentCount(prefab) + ") = " + ComponentList(prefab)
                    + ". Kritische Typen: Building="
                    + YesNo(EntityManager.HasComponent<Building>(lot))
                    + ", BuildingData am Prefab="
                    + YesNo(EntityManager.HasComponent<BuildingData>(prefab))
                    + ", CityServiceUpkeep(runtime aus Prefab-CityServiceBuilding)="
                    + YesNo(EntityManager.HasComponent<CityServiceUpkeep>(lot))
                    + ", Buildings.Lot="
                    + YesNo(EntityManager.HasComponent<Game.Buildings.Lot>(lot))
                    + ", Object="
                    + YesNo(EntityManager.HasComponent<Game.Objects.Object>(lot))
                    + ", Transform="
                    + YesNo(EntityManager.HasComponent<
                        Game.Objects.Transform>(lot)) + ".");
                Mod.log.Info("PLT-Gebaeudepfad LOT-HOEHE " + Show(lot)
                    + ": " + nodes.Length + " materialisierte Randknoten, Y "
                    + minimum.ToString("F4") + ".." + maximum.ToString("F4")
                    + " m, arithmetisches Knotenmittel "
                    + mean.y.ToString("F4") + " m; Geometry-Mittelpunkt "
                    + geometry.m_CenterPosition.x.ToString("F2") + "/"
                    + geometry.m_CenterPosition.z.ToString("F2") + " bei Y "
                    + geometry.m_CenterPosition.y.ToString("F4")
                    + " m. Das Lot besitzt keinen Transform; diese Werte sind "
                    + "Area-Daten.");
            }
        }

        private void CaptureNewCompanions()
        {
            using var companions = _companions.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < companions.Length; i++)
            {
                var companion = companions[i];
                if (_loggedCompanions.Contains(companion)) continue;
                var relation = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(companion);
                if (relation.Lot == Entity.Null
                    || !EntityManager.Exists(relation.Lot)
                    || !IsOwnLot(relation.Lot))
                    continue;

                _loggedCompanions.Add(companion);
                LogCompanion(companion, relation.Lot);
                var snapshot = CaptureTerrain(companion, relation.Lot);
                if (snapshot != null) _snapshots[companion] = snapshot;
            }
        }

        private void LogCompanion(Entity companion, Entity lot)
        {
            var prefab = EntityManager.GetComponentData<PrefabRef>(companion)
                .m_Prefab;
            var transform = EntityManager
                .GetComponentData<Game.Objects.Transform>(companion);
            var hasObject = EntityManager.HasComponent<Game.Objects.Object>(
                companion);
            var hasBuildingLot = EntityManager
                .HasComponent<Game.Buildings.Lot>(companion);
            var hasChange = EntityManager.HasComponent<Created>(companion)
                || EntityManager.HasComponent<Updated>(companion)
                || EntityManager.HasComponent<Deleted>(companion);
            var queryMatch = hasObject && hasBuildingLot && hasChange
                && !EntityManager.HasComponent<Temp>(companion);

            var nodes = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
            NodeStatistics(nodes, out var nodeMinimum, out var nodeMaximum,
                out var nodeMean);

            var heightData = _terrain.GetHeightData(waitForPending: true);
            var waterData = _water.GetSurfaceData(out var waterDependencies);
            waterDependencies.Complete();
            var placeableData = GetComponentLookup<PlaceableObjectData>(true);
            var geometryData = GetComponentLookup<ObjectGeometryData>(true);
            var elevation = EntityManager.HasComponent<Game.Objects.Elevation>(
                    companion)
                ? EntityManager.GetComponentData<Game.Objects.Elevation>(
                    companion)
                : default(Game.Objects.Elevation);
            var adjusted = Game.Objects.ObjectUtils.AdjustPosition(
                transform, ref elevation, prefab, out var angledSample,
                ref heightData, ref waterData, ref placeableData,
                ref geometryData);

            var geometryText = "ObjectGeometryData FEHLT";
            if (EntityManager.HasComponent<ObjectGeometryData>(prefab))
            {
                var geometry = EntityManager
                    .GetComponentData<ObjectGeometryData>(prefab);
                var size = geometry.m_Bounds.max - geometry.m_Bounds.min;
                geometryText = "Objekt-Bounds " + size.x.ToString("F2")
                    + " x " + size.z.ToString("F2") + " m, Flags "
                    + geometry.m_Flags;
            }

            var buildingText = "BuildingData FEHLT";
            var lotHeightText = "Gebaeude-Lotdaten FEHLEN";
            if (EntityManager.HasComponent<BuildingData>(prefab))
            {
                var building = EntityManager.GetComponentData<BuildingData>(
                    prefab);
                var extents = new float2(building.m_LotSize) * 4f;
                buildingText = "Building-Lot " + building.m_LotSize.x + "x"
                    + building.m_LotSize.y + ", Halbausdehnung "
                    + extents.x.ToString("F2") + " x "
                    + extents.y.ToString("F2") + " m";
                lotHeightText = BuildingLotHeightText(companion, prefab,
                    transform, building, ref heightData);
            }

            var terraformText = "BuildingTerraformData FEHLT";
            if (EntityManager.HasComponent<BuildingTerraformData>(prefab))
            {
                var terraform = EntityManager
                    .GetComponentData<BuildingTerraformData>(prefab);
                terraformText = "BuildingTerraformData vorhanden, DontRaise="
                    + YesNo(terraform.m_DontRaise) + ", DontLower="
                    + YesNo(terraform.m_DontLower) + ", Smooth "
                    + terraform.m_Smooth.x.ToString("F2") + "/"
                    + terraform.m_Smooth.y.ToString("F2") + ".."
                    + terraform.m_Smooth.z.ToString("F2") + "/"
                    + terraform.m_Smooth.w.ToString("F2");
            }

            Mod.log.Info("PLT-Gebaeudepfad BEGLEITER-IST " + Show(companion)
                + " fuer Lot " + Show(lot) + ": Komponenten ("
                + ComponentCount(companion) + ") = " + ComponentList(companion)
                + ". Prefab-Komponenten (" + ComponentCount(prefab) + ") = "
                + ComponentList(prefab) + ". TerrainSystem-Gebaeudequery="
                + YesNo(queryMatch) + " [Object=" + YesNo(hasObject)
                + ", Buildings.Lot=" + YesNo(hasBuildingLot)
                + ", Created|Updated|Deleted=" + YesNo(hasChange)
                + ", Temp=NEIN]. " + buildingText + "; " + geometryText
                + "; " + terraformText + "; " + lotHeightText + ".");
            Mod.log.Info("PLT-Gebaeudepfad BEGLEITER-HOEHE "
                + Show(companion) + ": Transform "
                + transform.m_Position.x.ToString("F2") + "/"
                + transform.m_Position.z.ToString("F2") + " bei Y "
                + transform.m_Position.y.ToString("F4")
                + " m; ObjectUtils-Ziel "
                + adjusted.m_Position.y.ToString("F4") + " m ("
                + (angledSample
                    ? "Vier-Ecken-Mittel des Objektgrundrisses mit Neigung"
                    : "Punkt-/Sonderprobe") + "); Lot-Randknoten Y "
                + nodeMinimum.ToString("F4") + ".."
                + nodeMaximum.ToString("F4") + " m, Knotenmittel "
                + nodeMean.y.ToString("F4") + " m.");
        }

        private string BuildingLotHeightText(
            Entity companion,
            Entity prefab,
            Game.Objects.Transform transform,
            BuildingData building,
            ref TerrainHeightData heightData)
        {
            if (!EntityManager.HasComponent<Game.Buildings.Lot>(companion))
                return "Gebaeude-Lotdaten FEHLEN";

            var lot = EntityManager.GetComponentData<Game.Buildings.Lot>(
                companion);
            var elevation = EntityManager.HasComponent<Game.Objects.Elevation>(
                    companion)
                ? EntityManager.GetComponentData<Game.Objects.Elevation>(
                    companion)
                : default(Game.Objects.Elevation);
            var upgrades = EntityManager.HasBuffer<InstalledUpgrade>(companion)
                ? EntityManager.GetBuffer<InstalledUpgrade>(companion, true)
                : default(DynamicBuffer<InstalledUpgrade>);
            var transforms = GetComponentLookup<Game.Objects.Transform>(true);
            var prefabRefs = GetComponentLookup<PrefabRef>(true);
            var objectGeometry = GetComponentLookup<ObjectGeometryData>(true);
            var terraform = GetComponentLookup<BuildingTerraformData>(true);
            var extensions = GetComponentLookup<BuildingExtensionData>(true);
            var extents = new float2(building.m_LotSize) * 4f;
            var prefabRef = new PrefabRef { m_Prefab = prefab };
            var lotInfo = Game.Buildings.BuildingUtils.CalculateLotInfo(
                extents, transform, elevation, lot, prefabRef, upgrades,
                transforms, prefabRefs, objectGeometry, terraform, extensions,
                defaultNoSmooth: false, out _);
            var corners = Game.Buildings.BuildingUtils.CalculateCorners(
                transform, building.m_LotSize);
            var centerTarget = Game.Buildings.BuildingUtils.SampleHeight(
                ref lotInfo, transform.m_Position);
            var targetA = Game.Buildings.BuildingUtils.SampleHeight(
                ref lotInfo, corners.a);
            var targetB = Game.Buildings.BuildingUtils.SampleHeight(
                ref lotInfo, corners.b);
            var targetC = Game.Buildings.BuildingUtils.SampleHeight(
                ref lotInfo, corners.c);
            var targetD = Game.Buildings.BuildingUtils.SampleHeight(
                ref lotInfo, corners.d);
            var terrainA = Game.Simulation.TerrainUtils.SampleHeight(
                ref heightData, corners.a);
            var terrainB = Game.Simulation.TerrainUtils.SampleHeight(
                ref heightData, corners.b);
            var terrainC = Game.Simulation.TerrainUtils.SampleHeight(
                ref heightData, corners.c);
            var terrainD = Game.Simulation.TerrainUtils.SampleHeight(
                ref heightData, corners.d);
            var targetMinimum = math.min(math.min(targetA, targetB),
                math.min(targetC, targetD));
            var targetMaximum = math.max(math.max(targetA, targetB),
                math.max(targetC, targetD));
            var terrainMinimum = math.min(math.min(terrainA, terrainB),
                math.min(terrainC, terrainD));
            var terrainMaximum = math.max(math.max(terrainA, terrainB),
                math.max(terrainC, terrainD));
            var terrainMean = (terrainA + terrainB + terrainC + terrainD) * 0.25f;

            return "Vanilla-LotInfo Ziel-Y Mitte "
                + centerTarget.ToString("F4") + " m, Ecken "
                + targetMinimum.ToString("F4") + ".."
                + targetMaximum.ToString("F4") + " m; Terrain an denselben "
                + "8x8-m-Ecken " + terrainMinimum.ToString("F4") + ".."
                + terrainMaximum.ToString("F4") + " m, Vier-Ecken-Mittel "
                + terrainMean.ToString("F4") + " m; Lot-Offsets F/R/B/L "
                + lot.m_FrontHeights + "/" + lot.m_RightHeights + "/"
                + lot.m_BackHeights + "/" + lot.m_LeftHeights;
        }

        private TerrainSnapshot CaptureTerrain(Entity companion, Entity lot)
        {
            var prefab = EntityManager.GetComponentData<PrefabRef>(companion)
                .m_Prefab;
            if (!EntityManager.HasComponent<BuildingData>(prefab)) return null;

            var transform = EntityManager
                .GetComponentData<Game.Objects.Transform>(companion);
            var building = EntityManager.GetComponentData<BuildingData>(prefab);
            var terraformPresent = EntityManager
                .HasComponent<BuildingTerraformData>(prefab);
            var terraform = terraformPresent
                ? EntityManager.GetComponentData<BuildingTerraformData>(prefab)
                : default(BuildingTerraformData);
            var extents = new float2(building.m_LotSize) * 4f;
            var radius = math.length(extents)
                + Game.Objects.ObjectUtils.GetTerrainSmoothingWidth(extents * 2f);
            var heightData = _terrain.GetHeightData(waitForPending: true);
            var minWorld = transform.m_Position
                - new float3(radius, 0f, radius);
            var maxWorld = transform.m_Position
                + new float3(radius, 0f, radius);
            var minCell = (int2)math.floor(Game.Simulation.TerrainUtils
                .ToHeightmapSpace(ref heightData, minWorld).xz);
            var maxCell = (int2)math.ceil(Game.Simulation.TerrainUtils
                .ToHeightmapSpace(ref heightData, maxWorld).xz);
            minCell = math.clamp(minCell, 0, heightData.resolution.xz - 1);
            maxCell = math.clamp(maxCell, 0, heightData.resolution.xz - 1);
            var width = maxCell.x - minCell.x + 1;
            var height = maxCell.y - minCell.y + 1;
            if (width <= 0 || height <= 0) return null;

            var values = new ushort[width * height];
            CopyHeightCells(ref heightData, minCell, maxCell, values);
            /*
             * `BuildingUtils.CalculateLotInfo` ueberschreibt mit den beiden
             * Sperren NUR `m_MinLimit`/`m_MaxLimit` (die Glaettung). Die
             * Flachzone `m_FlatX0..m_FlatZ1` reicht es unveraendert durch.
             * Deshalb wird sie hier mitgemessen, statt sie zu vermuten.
             * Ebenso die beiden anderen Wege, auf denen `TerrainSystem`
             * zusaetzliche Planierbereiche einreiht: `GeometryFlags.Standing`
             * und der Puffer `AdditionalBuildingTerraformElement`.
             */
            var standing = false;
            var geometrieText = "keine ObjectGeometryData";
            if (EntityManager.HasComponent<ObjectGeometryData>(prefab))
            {
                var geometrie = EntityManager
                    .GetComponentData<ObjectGeometryData>(prefab);
                standing = (geometrie.m_Flags & GeometryFlags.Standing) != 0;
                geometrieText = "Bounds X "
                    + geometrie.m_Bounds.min.x.ToString("F2") + ".."
                    + geometrie.m_Bounds.max.x.ToString("F2") + ", Z "
                    + geometrie.m_Bounds.min.z.ToString("F2") + ".."
                    + geometrie.m_Bounds.max.z.ToString("F2");
            }

            var zusatzbereiche = EntityManager
                .HasBuffer<AdditionalBuildingTerraformElement>(prefab)
                ? EntityManager
                    .GetBuffer<AdditionalBuildingTerraformElement>(prefab)
                    .Length
                : 0;
            Mod.log.Info("PLT-Gebaeudepfad PLANIERWEGE "
                + Show(companion) + ": Flachzone X "
                + terraform.m_FlatX0.y.ToString("F3") + ".."
                + terraform.m_FlatX1.y.ToString("F3") + " m, Z "
                + terraform.m_FlatZ0.y.ToString("F3") + ".."
                + terraform.m_FlatZ1.y.ToString("F3") + " m (Randwerte X0 "
                + terraform.m_FlatX0.x.ToString("F3") + "/"
                + terraform.m_FlatX0.z.ToString("F3") + ", X1 "
                + terraform.m_FlatX1.x.ToString("F3") + "/"
                + terraform.m_FlatX1.z.ToString("F3") + "); Glaettung "
                + terraform.m_Smooth.x.ToString("F2") + "/"
                + terraform.m_Smooth.y.ToString("F2") + ".."
                + terraform.m_Smooth.z.ToString("F2") + "/"
                + terraform.m_Smooth.w.ToString("F2") + "; Hoehenversatz "
                + terraform.m_HeightOffset.ToString("F3") + " m; Standing="
                + YesNo(standing) + "; Zusatz-Terraformbereiche "
                + zusatzbereiche + "; " + geometrieText + "; Begleiter steht "
                + "bei " + transform.m_Position.x.ToString("F2") + "/"
                + transform.m_Position.z.ToString("F2") + ".");
            Mod.log.Info("PLT-Gebaeudepfad TERRAIN-VORHER "
                + Show(companion) + ": " + values.Length
                + " echte CPU-Heightmapzellen im aus BuildingData und "
                + "ObjectUtils.GetTerrainSmoothingWidth berechneten Radius "
                + radius.ToString("F3") + " m kopiert; Transform-Y "
                + transform.m_Position.y.ToString("F4") + " m, Lot "
                + Show(lot) + "; Sperrvertrag BuildingTerraformData="
                + YesNo(terraformPresent) + ", DontRaise="
                + YesNo(terraform.m_DontRaise) + ", DontLower="
                + YesNo(terraform.m_DontLower) + ".");
            return new TerrainSnapshot
            {
                Companion = companion,
                Lot = lot,
                CapturedFrame = UnityEngine.Time.frameCount,
                MinCell = minCell,
                MaxCell = maxCell,
                ResolutionX = heightData.resolution.x,
                Heights = values,
                TerraformPresent = terraformPresent,
                DontRaise = terraform.m_DontRaise,
                DontLower = terraform.m_DontLower,
                CompanionPosition = transform.m_Position,
                CompanionRotation = transform.m_Rotation,
                FlatX0 = terraform.m_FlatX0.y,
                FlatZ0 = terraform.m_FlatZ0.y,
                FlatX1 = terraform.m_FlatX1.y,
                FlatZ1 = terraform.m_FlatZ1.y,
            };
        }

        internal void ObserveAfterTerrainSystem()
        {
            if (_snapshots.Count == 0) return;
            var heightData = _terrain.GetHeightData(waitForPending: true);
            var finished = new List<Entity>();
            foreach (var pair in _snapshots)
            {
                var snapshot = pair.Value;
                snapshot.ObservedPasses++;
                if (_terrain.heightMapRenderRequired
                    && snapshot.RenderRequestedFrame < 0)
                    snapshot.RenderRequestedFrame = UnityEngine.Time.frameCount;
                var width = snapshot.MaxCell.x - snapshot.MinCell.x + 1;
                var changed = 0;
                var minimum = float.PositiveInfinity;
                var maximum = float.NegativeInfinity;
                var changedMin = new int2(int.MaxValue);
                var changedMax = new int2(int.MinValue);
                var offset = 0;
                for (var z = snapshot.MinCell.y; z <= snapshot.MaxCell.y; z++)
                {
                    var row = z * heightData.resolution.x;
                    for (var x = snapshot.MinCell.x;
                        x <= snapshot.MaxCell.x; x++, offset++)
                    {
                        var deltaRaw = (int)heightData.heights[row + x]
                            - snapshot.Heights[offset];
                        if (deltaRaw == 0) continue;
                        changed++;
                        var delta = deltaRaw / heightData.scale.y;
                        minimum = math.min(minimum, delta);
                        maximum = math.max(maximum, delta);
                        changedMin = math.min(changedMin, new int2(x, z));
                        changedMax = math.max(changedMax, new int2(x, z));
                    }
                }

                if (changed != 0)
                {
                    var worldMin = Game.Simulation.TerrainUtils.ToWorldSpace(
                        ref heightData,
                        new float3(changedMin.x, 0f, changedMin.y));
                    var worldMax = Game.Simulation.TerrainUtils.ToWorldSpace(
                        ref heightData,
                        new float3(changedMax.x, 0f, changedMax.y));
                    var message = "PLT-Gebaeudepfad TERRAIN-NACHHER "
                        + Show(snapshot.Companion) + ": " + changed + "/"
                        + snapshot.Heights.Length
                        + " kopierte CPU-Heightmapzellen geaendert; Delta "
                        + minimum.ToString("+0.0000;-0.0000;0.0000") + ".."
                        + maximum.ToString("+0.0000;-0.0000;0.0000")
                        + " m, geaenderter Weltbereich "
                        + worldMin.x.ToString("F2") + "/"
                        + worldMin.z.ToString("F2") + ".."
                        + worldMax.x.ToString("F2") + "/"
                        + worldMax.z.ToString("F2") + "; im Lotsystem des "
                        + "Begleiters X " + LokalText(snapshot, worldMin,
                            worldMax, true) + " m, Z "
                        + LokalText(snapshot, worldMin, worldMax, false)
                        + " m gegenueber der Flachzone X "
                        + snapshot.FlatX0.ToString("F2") + ".."
                        + snapshot.FlatX1.ToString("F2") + ", Z "
                        + snapshot.FlatZ0.ToString("F2") + ".."
                        + snapshot.FlatZ1.ToString("F2") + ". Beleg: Vergleich "
                        + "derselben Heightmapzellen vor und nach "
                        + "TerrainSystem; Sperrvertrag BuildingTerraformData="
                        + YesNo(snapshot.TerraformPresent) + ", DontRaise="
                        + YesNo(snapshot.DontRaise) + ", DontLower="
                        + YesNo(snapshot.DontLower) + ".";
                    if (snapshot.TerraformPresent && snapshot.DontRaise
                        && snapshot.DontLower)
                        Mod.log.Error(message + " TERRAFORM-SPERRE VERLETZT.");
                    else
                        Mod.log.Info(message);
                    finished.Add(pair.Key);
                }
                else if (snapshot.TerraformPresent && snapshot.DontRaise
                    && snapshot.DontLower
                    && snapshot.RenderRequestedFrame >= 0
                    && UnityEngine.Time.frameCount
                        > snapshot.RenderRequestedFrame
                    && !_terrain.heightMapRenderRequired)
                {
                    /*
                     * GetHeightData(waitForPending: true) oberhalb wartet auf
                     * die CPU-Rueckgabe. Erst der Wechsel vom beobachteten
                     * Renderauftrag zu "nicht mehr angefordert" beendet die
                     * Messung; eine geratene Framezahl ist nicht noetig.
                     */
                    Mod.log.Info("PLT-Gebaeudepfad TERRAFORM-SPERRE BELEGT "
                        + Show(snapshot.Companion) + ": BuildingTerraformData "
                        + "vorhanden, DontRaise=JA, DontLower=JA; 0/"
                        + snapshot.Heights.Length + " identische CPU-"
                        + "Heightmapzellen nach " + snapshot.ObservedPasses
                        + " TerrainSystem-Durchgaengen geaendert. Der "
                        + "beobachtete Renderauftrag ist abgeschlossen.");
                    finished.Add(pair.Key);
                }
                else if (!snapshot.FirstPassLogged)
                {
                    snapshot.FirstPassLogged = true;
                    Mod.log.Info("PLT-Gebaeudepfad TERRAIN-NACHHER "
                        + Show(snapshot.Companion) + ": 0/"
                        + snapshot.Heights.Length + " Zellen unmittelbar "
                        + "nach diesem TerrainSystem-Durchgang geaendert; "
                        + "Messung bleibt fuer eine spaetere CPU-Rueckgabe "
                        + "aktiv. Sperrvertrag BuildingTerraformData="
                        + YesNo(snapshot.TerraformPresent) + ", DontRaise="
                        + YesNo(snapshot.DontRaise) + ", DontLower="
                        + YesNo(snapshot.DontLower)
                        + "; heightMapRenderRequired="
                        + _terrain.heightMapRenderRequired + ".");
                }
            }
            for (var i = 0; i < finished.Count; i++)
                _snapshots.Remove(finished[i]);
        }

        /**
         * Rechnet den geaenderten Weltbereich in das gedrehte Lotsystem des
         * Begleiters um. Nur dort ist er mit der Flachzone vergleichbar,
         * denn `m_FlatX0..m_FlatZ1` sind lokale Masse.
         */
        private static string LokalText(
            TerrainSnapshot snapshot,
            float3 worldMin,
            float3 worldMax,
            bool xAchse)
        {
            var inverse = math.inverse(snapshot.CompanionRotation);
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var ecke = 0; ecke < 4; ecke++)
            {
                var welt = new float3(
                    (ecke & 1) == 0 ? worldMin.x : worldMax.x,
                    snapshot.CompanionPosition.y,
                    (ecke & 2) == 0 ? worldMin.z : worldMax.z);
                var lokal = math.mul(inverse,
                    welt - snapshot.CompanionPosition);
                var wert = xAchse ? lokal.x : lokal.z;
                minimum = math.min(minimum, wert);
                maximum = math.max(maximum, wert);
            }

            return minimum.ToString("F2") + ".." + maximum.ToString("F2");
        }

        private static void CopyHeightCells(
            ref TerrainHeightData data,
            int2 minCell,
            int2 maxCell,
            ushort[] target)
        {
            var offset = 0;
            for (var z = minCell.y; z <= maxCell.y; z++)
            {
                var row = z * data.resolution.x;
                for (var x = minCell.x; x <= maxCell.x; x++)
                    target[offset++] = data.heights[row + x];
            }
        }

        private bool IsOwnLot(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasComponent<PrefabRef>(lot)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(lot).m_Prefab;
            return _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var value)
                && value != null
                && value.name == ParkingLotToolSystem.LotOwnerPrefabName;
        }

        private static void NodeStatistics(
            DynamicBuffer<Game.Areas.Node> nodes,
            out float minimum,
            out float maximum,
            out float3 mean)
        {
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            mean = float3.zero;
            if (nodes.Length == 0)
            {
                minimum = 0f;
                maximum = 0f;
                return;
            }
            for (var i = 0; i < nodes.Length; i++)
            {
                var position = nodes[i].m_Position;
                mean += position;
                minimum = math.min(minimum, position.y);
                maximum = math.max(maximum, position.y);
            }
            mean /= nodes.Length;
        }

        private int ComponentCount(Entity entity)
        {
            using var types = EntityManager.GetComponentTypes(
                entity, Allocator.Temp);
            return types.Length;
        }

        private string ComponentList(Entity entity)
        {
            using var types = EntityManager.GetComponentTypes(
                entity, Allocator.Temp);
            var names = new List<string>(types.Length);
            for (var i = 0; i < types.Length; i++)
                names.Add(types[i].GetManagedType()?.FullName
                    ?? types[i].ToString());
            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names);
        }

        private static string Show(Entity entity)
            => "#" + entity.Index + "." + entity.Version;

        private static string YesNo(bool value) => value ? "JA" : "NEIN";
    }

    /** Liest die vor `TerrainSystem` kopierten Zellen nach dessen Lauf. */
    public sealed partial class ParkingLotBuildingTerrainAfterSystem
        : GameSystemBase
    {
        private ParkingLotBuildingTerrainBeforeSystem _before;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _before = World.GetOrCreateSystemManaged<
                ParkingLotBuildingTerrainBeforeSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            _before?.ObserveAfterTerrainSystem();
        }
    }
}
