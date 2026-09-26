using System.Collections.Generic;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * BILD FUER BILD: WAS PASSIERT AM INNEREN GASSENKNOTEN? (nur mit Live-Log)
     *
     * Stand 2026-09-26 abends: der Knoten traegt `Elevation 0`, damit laesst
     * ihn `GroundHeightSystem` nachweislich aus - und trotzdem sinken genau
     * die Knoten mit Weg-Prefab. Ein anderes System verschiebt sie. Diese
     * Zeilen zeigen fuer 120 Bilder nach dem Apply jede Aenderung: Hoehe und
     * Prefab des Knotens, die Kurvenenden jeder angeschlossenen Kante an
     * diesem Knoten, ob Knoten/Kante gerade `Updated` tragen, und das
     * CPU-Gelaende darunter. Wer zuerst wandert - Kantenende oder Knoten -
     * verraet das System.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const int GassenverlaufBilder = 120;

        private int _gvStart;
        private readonly List<Entity> _gvKnoten = new();
        private readonly List<string> _gvZuletzt = new();

        private void StarteGassenverlauf()
        {
            _gvStart = 0;
            _gvKnoten.Clear();
            _gvZuletzt.Clear();
            if (!ParkingLotLiveLog.Aktiv || _gassenhoehen.Count == 0) return;
            _gvStart = UnityEngine.Time.frameCount;
            foreach (var g in _gassenhoehen)
            {
                _gvKnoten.Add(Entity.Null);
                _gvZuletzt.Add(null);
            }
            ParkingLotLiveLog.Zeile($"gassenverlauf start | {_gassenhoehen.Count} innere Ende(n) | Bild {_gvStart} (Apply in diesem Bild)");
        }

        private void PflegeGassenverlauf()
        {
            if (_gvStart == 0) return;
            var bild = UnityEngine.Time.frameCount - _gvStart;
            if (bild > GassenverlaufBilder || !ParkingLotLiveLog.Aktiv
                || _gvKnoten.Count != _gassenhoehen.Count)
            {
                ParkingLotLiveLog.Zeile("gassenverlauf ende | Bild +" + bild);
                _gvStart = 0;
                return;
            }
            var gelaende = _terrainSystem.GetHeightData();
            for (var i = 0; i < _gvKnoten.Count; i++)
            {
                var knoten = _gvKnoten[i];
                if (knoten == Entity.Null || !EntityManager.Exists(knoten)
                    || EntityManager.HasComponent<Deleted>(knoten))
                {
                    KantenAmPunkt(_gassenhoehen[i].Lage, out knoten, out var versatz);
                    if (knoten == Entity.Null || versatz > 0.05f) continue;
                    _gvKnoten[i] = knoten;
                }
                var zeile = GvBeschreibe(knoten, ref gelaende);
                if (zeile == _gvZuletzt[i]) continue;
                _gvZuletzt[i] = zeile;
                var lage = _gassenhoehen[i].Lage;
                ParkingLotLiveLog.Zeile($"gassenknoten +{bild} | ({ParkingLotLiveLog.Zahl(lage.x)}/{ParkingLotLiveLog.Zahl(lage.y)}) "
                    + $"geplant {ParkingLotLiveLog.Zahl(_gassenhoehen[i].Geplant, 2)} | {zeile}");
            }
        }

        private string GvBeschreibe(Entity knoten, ref Game.Simulation.TerrainHeightData gelaende)
        {
            var b = new StringBuilder();
            var p = EntityManager.GetComponentData<Node>(knoten).m_Position;
            var boden = Game.Simulation.TerrainUtils.SampleHeight(ref gelaende, p);
            b.Append("#").Append(knoten.Index)
             .Append(" y ").Append(ParkingLotLiveLog.Zahl(p.y, 2))
             .Append(" boden ").Append(ParkingLotLiveLog.Zahl(boden, 2))
             .Append(" | ").Append(GvPrefab(knoten))
             .Append(EntityManager.HasComponent<Elevation>(knoten) ? " elev" : " OHNE-elev")
             .Append(EntityManager.HasComponent<Standalone>(knoten) ? " standalone" : "")
             .Append(EntityManager.HasComponent<Updated>(knoten) ? " UPDATED" : "")
             .Append(EntityManager.HasComponent<Game.Tools.Temp>(knoten) ? " TEMP" : "");
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return b.ToString();
            var kanten = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            for (var k = 0; k < kanten.Length; k++)
            {
                var kante = kanten[k].m_Edge;
                if (!EntityManager.HasComponent<Edge>(kante) || !EntityManager.HasComponent<Curve>(kante)) continue;
                var e = EntityManager.GetComponentData<Edge>(kante);
                var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
                float ende;
                if (e.m_Start == knoten) ende = kurve.a.y;
                else if (e.m_End == knoten) ende = kurve.d.y;
                else continue;
                var mitte = Colossal.Mathematics.MathUtils.Position(kurve, 0.5f).y;
                b.Append(" || ").Append(GvPrefab(kante))
                 .Append(" ende ").Append(ParkingLotLiveLog.Zahl(ende, 2))
                 .Append(" mitte ").Append(ParkingLotLiveLog.Zahl(mitte, 2))
                 .Append(EntityManager.HasComponent<Elevation>(kante) ? " elev" : "")
                 .Append(EntityManager.HasComponent<Updated>(kante) ? " UPDATED" : "");
            }
            return b.ToString();
        }

        private string GvPrefab(Entity e)
        {
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            if (GassenPrefab.Ist(_prefabSystem, prefab)) return "Gasse";
            if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var pb) || pb == null) return "?";
            var name = pb.name;
            if (name.StartsWith("Invisible Road Path", System.StringComparison.Ordinal)) return "WegRoad";
            if (name.StartsWith("Invisible Car Path", System.StringComparison.Ordinal)) return "WegCar";
            return name;
        }
    }
}
