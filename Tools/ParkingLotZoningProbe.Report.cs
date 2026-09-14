using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Katalog, Messprotokoll, Zonierung und restloses Aufraeumen der Sonde. */
    public sealed partial class ParkingLotToolSystem
    {
        private void ZpSchreibeStrassenkatalog()
        {
            var lines = new List<string>();
            using var entities = _zpRoadPrefabs.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (_prefabSystem == null
                    || !_prefabSystem.TryGetPrefab<RoadPrefab>(entity,
                        out var roadPrefab)
                    || roadPrefab == null || roadPrefab.m_ZoneBlock == null)
                    continue;
                var road = EntityManager.GetComponentData<RoadData>(entity);
                var geometry = EntityManager.GetComponentData<NetGeometryData>(entity);
                ZpLaneCounts(entity, out var cars, out var pedestrian,
                    out var composition);
                lines.Add($"'{ZpPrefabName(entity)}': Breite "
                    + $"{geometry.m_DefaultWidth:F2} m, Fahrspuren {cars}, "
                    + $"Gehweg {(pedestrian ? "ja" : "nein")}, "
                    + $"ZoneBlock='{roadPrefab.m_ZoneBlock.name}' "
                    + $"(Entity {ZpPrefabName(road.m_ZoneBlockPrefab)}), "
                    + $"Querschnitt={composition}");
            }
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            ZpLog($"1 KATALOG: {lines.Count} zoningfaehige RoadPrefab(s); "
                + "je eine Folgezeile mit Name, Breite, Fahrspuren und Gehweg.");
            for (var i = 0; i < lines.Count; i++)
                ZpLog($"1 KATALOG ROAD {i}: {lines[i]}");
            foreach (var wanted in new[] { "Alley", "Gravel Road" })
            {
                var exact = lines.FirstOrDefault(line => line.StartsWith(
                    "'" + wanted + "':", StringComparison.OrdinalIgnoreCase));
                ZpLog($"1 WUNSCH '{wanted}': "
                    + (exact ?? "nicht als zoningfaehiges RoadPrefab gefunden"));
            }
        }

        private void ZpLaneCounts(Entity roadPrefab, out int cars,
                                  out bool pedestrian, out string compositionName)
        {
            cars = 0;
            pedestrian = false;
            compositionName = "kein Default-Querschnitt";
            if (!EntityManager.HasBuffer<NetGeometryComposition>(roadPrefab)) return;
            var compositions = EntityManager.GetBuffer<NetGeometryComposition>(
                roadPrefab, true);
            if (compositions.Length == 0) return;
            var composition = compositions[0].m_Composition;
            /*
             * `m_Mask` IST KEIN ENUM, SONDERN EIN STRUCT.
             *
             * Hier stand `Convert.ToUInt64(compositions[i].m_Mask, ...)`, und
             * das warf im Spiel sofort eine `InvalidCastException` - der
             * Sondenlauf brach ab, bevor er die erste Zeile schreiben
             * konnte. `CompositionFlags` hat drei Felder:
             *
             *     public General m_General;   // enum : uint
             *     public Side    m_Left;      // enum : uint
             *     public Side    m_Right;
             *
             * Gesucht ist der DEFAULT-Querschnitt, also der ohne jede
             * Zusatzbedingung - dafuer muessen alle drei null sein.
             */
            for (var i = 0; i < compositions.Length; i++)
            {
                var maske = compositions[i].m_Mask;
                if (maske.m_General != 0 || maske.m_Left != 0
                    || maske.m_Right != 0) continue;
                composition = compositions[i].m_Composition;
                break;
            }
            compositionName = ZpPrefabName(composition);
            if (composition == Entity.Null
                || !EntityManager.HasBuffer<NetCompositionLane>(composition)) return;
            var lanes = EntityManager.GetBuffer<NetCompositionLane>(composition, true);
            for (var i = 0; i < lanes.Length; i++)
            {
                var flags = lanes[i].m_Flags;
                if ((flags & LaneFlags.Pedestrian) != 0) pedestrian = true;
                if ((flags & LaneFlags.Road) != 0
                    && (flags & (LaneFlags.Pedestrian | LaneFlags.Parking)) == 0)
                    cars++;
            }
        }

        private void ZpSchreibeZonenkatalog()
        {
            var zones = new List<(string Name, ZoneData Data, Entity Entity)>();
            using var entities = _zpZonePrefabs.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                zones.Add((ZpPrefabName(entity),
                    EntityManager.GetComponentData<ZoneData>(entity), entity));
            }
            zones.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            ZpLog($"1 ZONENTYPEN: {zones.Count}; je eine Folgezeile.");
            for (var i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                ZpLog($"1 ZONENTYP {i}: '{z.Name}'=Index "
                    + $"{z.Data.m_ZoneType.m_Index}, AreaType "
                    + $"{z.Data.m_AreaType}, Flags [{z.Data.m_ZoneFlags}].");
            }

            _zpZonePrefab = Entity.Null;
            var selected = zones.FirstOrDefault(z =>
                z.Data.m_ZoneType.m_Index != 0
                && z.Name.IndexOf("Residential Low", StringComparison.OrdinalIgnoreCase) >= 0);
            if (selected.Entity == Entity.Null)
                selected = zones.FirstOrDefault(z => z.Data.m_ZoneType.m_Index != 0
                    && z.Name.IndexOf("Residential", StringComparison.OrdinalIgnoreCase) >= 0);
            if (selected.Entity == Entity.Null)
                selected = zones.FirstOrDefault(z => z.Data.m_ZoneType.m_Index != 0);
            _zpZonePrefab = selected.Entity;
            if (_zpZonePrefab != Entity.Null)
                ZpLog($"1 ZONENTYP GEWAEHLT: '{selected.Name}', Index "
                    + $"{selected.Data.m_ZoneType.m_Index}, AreaType "
                    + $"{selected.Data.m_AreaType}, Flags [{selected.Data.m_ZoneFlags}].");
            else
                ZpLog("1 ZONENTYP GEWAEHLT: KEIN verwendbarer ZoneData-Typ.");
        }

        private void LadeZpStandAusWelt()
        {
            using var carriers = _zpCarriers.ToEntityArray(Allocator.Temp);
            if (carriers.Length == 0)
            {
                _zpCarrier = Entity.Null;
                _zpEdge = Entity.Null;
                return;
            }
            _zpCarrier = carriers[0];
            var marker = EntityManager.GetComponentData<ParkingLotZoningProbeMarker>(
                _zpCarrier);
            _zpRoadPrefab = marker.RoadPrefab;
            _zpEdge = marker.Edge;
            _zpStart = marker.Start;
            _zpEnd = marker.End;
            if ((_zpEdge == Entity.Null || !EntityManager.Exists(_zpEdge))
                && EntityManager.HasBuffer<Game.Net.SubNet>(_zpCarrier))
            {
                var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(_zpCarrier, true);
                for (var i = 0; i < subNets.Length; i++)
                {
                    var candidate = subNets[i].m_SubNet;
                    if (candidate == Entity.Null || !EntityManager.Exists(candidate)
                        || !EntityManager.HasComponent<Game.Net.Edge>(candidate)) continue;
                    _zpEdge = candidate;
                    break;
                }
            }
            if (carriers.Length > 1)
                ZpLog($"5 WARNUNG: {carriers.Length} Sondentraeger gefunden; "
                    + $"gemessen wird {ZpEntity(_zpCarrier)}.");
        }

        private void ZpMesseAlles(string step)
        {
            LadeZpStandAusWelt();
            if (_zpCarrier == Entity.Null || _zpEdge == Entity.Null
                || !EntityManager.Exists(_zpEdge)
                || EntityManager.HasComponent<Deleted>(_zpEdge))
            {
                ZpLog($"{step}: keine dauerhafte Sondenkante gefunden.");
                return;
            }

            var components = ZpComponents(_zpEdge);
            var hasSubBlock = EntityManager.HasBuffer<SubBlock>(_zpEdge);
            var hasResource = EntityManager.HasComponent<ResourceAvailability>(_zpEdge);
            var hasLandValue = EntityManager.HasComponent<LandValue>(_zpEdge);
            ZpLog($"{step} KANTE {ZpEntity(_zpEdge)} '{ZpPrefabName(_zpRoadPrefab)}': "
                + $"Komponenten [{string.Join(", ", components)}].");
            ZpLog($"{step} KANTENMERKMALE: SubBlock={hasSubBlock}, "
                + $"ResourceAvailability={hasResource}, LandValue={hasLandValue}.");

            var blocks = ZpBlocks();
            ZpLog($"{step} BLOECKE: {blocks.Count} insgesamt; "
                + $"links {blocks.Count(b => b.Side == "links")}, "
                + $"rechts {blocks.Count(b => b.Side == "rechts")}, "
                + $"mittig {blocks.Count(b => b.Side == "mittig")}.");
            for (var i = 0; i < blocks.Count; i++) ZpReportBlock(step, i, blocks[i]);
            ZpReportBuildings(step);
            ZpLog($"6 LADEVERGLEICH: {ZpSignature(blocks)}");
        }

        private sealed class ZpBlockInfo
        {
            internal Entity Entity;
            internal Block Block;
            internal string Side;
            internal bool HasValidArea;
            internal int4 ValidArea;
            internal string Row0;
            internal string Row1;
            internal string Vacant;
            internal int VacantCount;
        }

        private List<ZpBlockInfo> ZpBlocks()
        {
            var result = new List<ZpBlockInfo>();
            if (!EntityManager.HasBuffer<SubBlock>(_zpEdge)) return result;
            var subBlocks = EntityManager.GetBuffer<SubBlock>(_zpEdge, true);
            var middle = (_zpStart + _zpEnd) * 0.5f;
            var direction = math.normalizesafe(_zpEnd.xz - _zpStart.xz,
                new float2(1f, 0f));
            var left = new float2(-direction.y, direction.x);
            for (var i = 0; i < subBlocks.Length; i++)
            {
                var entity = subBlocks[i].m_SubBlock;
                if (entity == Entity.Null || !EntityManager.Exists(entity)
                    || EntityManager.HasComponent<Deleted>(entity)
                    || !EntityManager.HasComponent<Block>(entity)) continue;
                var block = EntityManager.GetComponentData<Block>(entity);
                var cross = math.dot(block.m_Position.xz - middle.xz, left);
                var info = new ZpBlockInfo
                {
                    Entity = entity,
                    Block = block,
                    Side = cross > 0.05f ? "links" : cross < -0.05f ? "rechts" : "mittig",
                    HasValidArea = EntityManager.HasComponent<ValidArea>(entity),
                };
                if (info.HasValidArea)
                    info.ValidArea = EntityManager.GetComponentData<ValidArea>(entity).m_Area;
                if (EntityManager.HasBuffer<Cell>(entity))
                {
                    var cells = EntityManager.GetBuffer<Cell>(entity, true);
                    info.Row0 = ZpCellRow(cells, block.m_Size, 0);
                    info.Row1 = ZpCellRow(cells, block.m_Size, 1);
                }
                else info.Row0 = info.Row1 = "kein Cell-Puffer";
                if (EntityManager.HasBuffer<VacantLot>(entity))
                {
                    var lots = EntityManager.GetBuffer<VacantLot>(entity, true);
                    info.VacantCount = lots.Length;
                    var descriptions = new List<string>();
                    for (var j = 0; j < lots.Length; j++)
                    {
                        var area = lots[j].m_Area;
                        descriptions.Add($"{ZpInt4(area)}=>"
                            + $"{area.y - area.x}x{area.w - area.z}, "
                            + $"Zone {lots[j].m_Type.m_Index}, Flags [{lots[j].m_Flags}]");
                    }
                    info.Vacant = descriptions.Count == 0
                        ? "keine" : string.Join("; ", descriptions);
                }
                else info.Vacant = "kein VacantLot-Puffer";
                result.Add(info);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Side, b.Side));
            return result;
        }

        private void ZpReportBlock(string step, int index, ZpBlockInfo info)
        {
            var block = info.Block;
            ZpLog($"{step} BLOCK {index} {ZpEntity(info.Entity)} Seite={info.Side}, "
                + $"Position={ZpFloat3(block.m_Position)}, "
                + $"Size={block.m_Size.x}x{block.m_Size.y}, "
                + $"Direction=({block.m_Direction.x:F5}/{block.m_Direction.y:F5}), "
                + $"ValidArea={(info.HasValidArea ? ZpInt4(info.ValidArea) : "FEHLT")}.");
            ZpLog($"{step} BLOCK {index} CELL-REIHE 0: {info.Row0}");
            ZpLog($"{step} BLOCK {index} CELL-REIHE 1: {info.Row1}");
            ZpLog($"{step} BLOCK {index} VACANTLOTS: {info.VacantCount}; {info.Vacant}.");
        }

        private static string ZpCellRow(DynamicBuffer<Cell> cells, int2 size, int row)
        {
            if (row < 0 || row >= size.y) return "entfaellt";
            var values = new List<string>();
            for (var x = 0; x < size.x; x++)
            {
                var index = row * size.x + x;
                if (index >= cells.Length) break;
                var cell = cells[index];
                values.Add($"x{x}:[{cell.m_State}]/Zone{cell.m_Zone.m_Index}");
            }
            return values.Count == 0 ? "leer" : string.Join(", ", values);
        }

        private void ZpSetzeZonentyp()
        {
            if (_zpZonePrefab == Entity.Null || !EntityManager.Exists(_zpZonePrefab))
                ZpSchreibeZonenkatalog();
            if (_zpZonePrefab == Entity.Null)
            {
                ZpLog("4 ZONIERUNG abgebrochen: kein ZoneData-Typ gefunden.");
                return;
            }
            var zone = EntityManager.GetComponentData<ZoneData>(_zpZonePrefab);
            var blocks = ZpBlocks();
            var changed = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                var entity = blocks[i].Entity;
                if (!EntityManager.HasBuffer<Cell>(entity)) continue;
                var cells = EntityManager.GetBuffer<Cell>(entity);
                for (var j = 0; j < cells.Length; j++)
                {
                    var cell = cells[j];
                    if ((cell.m_State & (CellFlags.Visible | CellFlags.Roadside)) == 0
                        || (cell.m_State & CellFlags.Blocked) != 0) continue;
                    cell.m_Zone = zone.m_ZoneType;
                    cells[j] = cell;
                    changed++;
                }
                if (!EntityManager.HasComponent<Updated>(entity))
                    EntityManager.AddComponent<Updated>(entity);
            }
            ZpLog($"4 ZONIERUNG: '{ZpPrefabName(_zpZonePrefab)}' / Index "
                + $"{zone.m_ZoneType.m_Index} in {changed} sichtbare, nicht "
                + $"blockierte Cells aus {blocks.Count} Bloecken geschrieben; "
                + "jeder Block auf Updated gesetzt; Nachmessung nach 24 Frames.");
        }

        private void ZpReportBuildings(string step)
        {
            var found = new List<Entity>();
            using var entities = _zpBuildings.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                var building = EntityManager.GetComponentData<Game.Buildings.Building>(
                    entities[i]);
                if (building.m_RoadEdge == _zpEdge) found.Add(entities[i]);
            }
            ZpLog($"{step} GEBAEUDE: {found.Count} mit Building.m_RoadEdge="
                + $"{ZpEntity(_zpEdge)}.");
            for (var i = 0; i < found.Count; i++)
            {
                var entity = found[i];
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                var building = EntityManager.GetComponentData<Game.Buildings.Building>(entity);
                var transform = EntityManager.GetComponentData<Game.Objects.Transform>(entity);
                var size = EntityManager.HasComponent<ObjectGeometryData>(prefab)
                    ? EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Size
                    : new float3(float.NaN);
                var lot = EntityManager.HasComponent<BuildingData>(prefab)
                    ? EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize
                    : new int2(-1);
                var forward = math.mul(transform.m_Rotation, new float3(0f, 0f, 1f));
                ZpLog($"{step} GEBAEUDE {i} {ZpEntity(entity)} "
                    + $"'{ZpPrefabName(prefab)}': Groesse={ZpFloat3(size)}, "
                    + $"Lot={lot.x}x{lot.y} Cells, Position={ZpFloat3(transform.m_Position)}, "
                    + $"Vorne={ZpFloat3(forward)}, RoadEdge={ZpEntity(building.m_RoadEdge)}, "
                    + $"CurvePosition={building.m_CurvePosition:F5}.");
            }
        }

        private string ZpSignature(List<ZpBlockInfo> blocks)
        {
            var pieces = new List<string>
            {
                $"Road='{ZpPrefabName(_zpRoadPrefab)}'",
                $"SubBlock={EntityManager.HasBuffer<SubBlock>(_zpEdge)}",
                $"ResourceAvailability={EntityManager.HasComponent<ResourceAvailability>(_zpEdge)}",
                $"LandValue={EntityManager.HasComponent<LandValue>(_zpEdge)}",
                $"Blocks={blocks.Count}",
            };
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                pieces.Add($"B{i}={b.Side}/{b.Block.m_Size.x}x{b.Block.m_Size.y}/"
                    + $"Dir{b.Block.m_Direction.x:F5},{b.Block.m_Direction.y:F5}/"
                    + $"Valid{(b.HasValidArea ? ZpInt4(b.ValidArea) : "FEHLT")}/"
                    + $"R0{{{b.Row0}}}/R1{{{b.Row1}}}/Vacant{b.VacantCount}{{{b.Vacant}}}");
            }
            var buildingCount = 0;
            using (var entities = _zpBuildings.ToEntityArray(Allocator.Temp))
                for (var i = 0; i < entities.Length; i++)
                    if (EntityManager.GetComponentData<Game.Buildings.Building>(entities[i])
                            .m_RoadEdge == _zpEdge)
                        buildingCount++;
            pieces.Add($"Buildings={buildingCount}");
            return string.Join(" | ", pieces);
        }

        private void ZpBeginneAufraeumen()
        {
            LadeZpStandAusWelt();
            _zpCleanupTargets.Clear();
            if (_zpCarrier == Entity.Null)
            {
                if (_zpPhase == ZpPhase.TempSuchen) applyMode = ApplyMode.Clear;
                _zpPhase = ZpPhase.Idle;
                ZpLog("7 AUFRAEUMEN: keine Sonde gefunden.");
                return;
            }
            var blocks = ZpBlocks();
            var buildingCount = 0;
            using (var buildings = _zpBuildings.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < buildings.Length; i++)
                {
                    var entity = buildings[i];
                    if (EntityManager.GetComponentData<Game.Buildings.Building>(entity)
                            .m_RoadEdge != _zpEdge) continue;
                    if (ZpMarkDeleted(entity)) buildingCount++;
                }
            }
            for (var i = 0; i < blocks.Count; i++) ZpMarkDeleted(blocks[i].Entity);
            var nodeCount = 0;
            var edgeCount = 0;
            if (_zpEdge != Entity.Null && EntityManager.Exists(_zpEdge)
                && EntityManager.HasComponent<Game.Net.Edge>(_zpEdge))
            {
                var edge = EntityManager.GetComponentData<Game.Net.Edge>(_zpEdge);
                if (ZpMarkDeleted(edge.m_Start)) nodeCount++;
                if (ZpMarkDeleted(edge.m_End)) nodeCount++;
                if (ZpMarkDeleted(_zpEdge)) edgeCount++;
            }
            if (_zpPhase == ZpPhase.TempSuchen) applyMode = ApplyMode.Clear;
            _zpPhase = ZpPhase.Aufraeumen;
            _zpPhaseFrame = 0;
            ZpLog($"7 AUFRAEUMEN MARKIERT: {edgeCount} Kante(n), {nodeCount} Knoten, "
                + $"{blocks.Count} Bloecke und {buildingCount} gewachsene "
                + "Gebaeude; Temp-Entities wurden ausdruecklich nicht mit "
                + "Deleted versehen. Traeger folgt nach 24 Frames.");
        }

        private bool ZpMarkDeleted(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity)
                || EntityManager.HasComponent<Temp>(entity)) return false;
            if (!EntityManager.HasComponent<Deleted>(entity))
                EntityManager.AddComponent<Deleted>(entity);
            if (!_zpCleanupTargets.Contains(entity)) _zpCleanupTargets.Add(entity);
            return true;
        }

        /** Wahr heisst: CS2 hat markierte Teile noch nicht entfernt, weiter warten. */
        private bool ZpBeendeAufraeumen()
        {
            var remaining = 0;
            for (var i = 0; i < _zpCleanupTargets.Count; i++)
                if (EntityManager.Exists(_zpCleanupTargets[i])) remaining++;
            if (remaining != 0 && _zpPhaseFrame < 240) return true;

            if (_zpCarrier != Entity.Null && EntityManager.Exists(_zpCarrier))
                EntityManager.DestroyEntity(_zpCarrier);
            var carrierStillExists = _zpCarrier != Entity.Null
                && EntityManager.Exists(_zpCarrier);
            ZpLog($"7 AUFRAEUMEN FERTIG: { _zpCleanupTargets.Count } Teile "
                + $"markiert, davon nach {_zpPhaseFrame} Frames noch {remaining} "
                + $"existent; Traeger nach DestroyEntity existent={carrierStillExists}."
                + (remaining == 0 && !carrierStillExists
                    ? " Sonde restlos entfernt."
                    : " ACHTUNG: Die genannten Rest-Entities stehen noch in der Welt."));
            _zpCarrier = Entity.Null;
            _zpRoadPrefab = Entity.Null;
            _zpEdge = Entity.Null;
            _zpZonePrefab = Entity.Null;
            _zpPhase = ZpPhase.Idle;
            _zpPhaseFrame = 0;
            _zpCleanupTargets.Clear();
            return false;
        }

        private void ZpLoescheTraegerSofort()
        {
            if (_zpCarrier != Entity.Null && EntityManager.Exists(_zpCarrier))
                EntityManager.DestroyEntity(_zpCarrier);
            _zpCarrier = Entity.Null;
            _zpEdge = Entity.Null;
        }

        private List<string> ZpComponents(Entity entity)
        {
            var result = new List<string>();
            if (entity == Entity.Null || !EntityManager.Exists(entity)) return result;
            var types = EntityManager.GetChunk(entity).Archetype
                .GetComponentTypes(Allocator.Temp);
            for (var i = 0; i < types.Length; i++)
                result.Add(types[i].GetManagedType()?.Name ?? types[i].ToString());
            types.Dispose();
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private string ZpPrefabName(Entity prefabEntity)
        {
            if (prefabEntity == Entity.Null || !EntityManager.Exists(prefabEntity))
                return "Entity.Null";
            if (_prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefabEntity, out var prefab)
                && prefab != null)
                return prefab.name;
            return ZpEntity(prefabEntity);
        }

        private static string ZpInt4(int4 value) =>
            $"({value.x}/{value.y}/{value.z}/{value.w})";
    }
}
