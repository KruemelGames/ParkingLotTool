using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private static float3[] AvKontrollpunkte(Bezier4x3 b) => new[] { b.a, b.b, b.c, b.d };

        private bool AvKollision(Bezier4x3 a, Entity pa, Bezier4x3 b, Entity pb)
        {
            var ga = EntityManager.GetComponentData<NetGeometryData>(pa);
            var gb = EntityManager.GetComponentData<NetGeometryData>(pb);
            return Versorgungsabstand.Beruehrt(AvKontrollpunkte(a), ga.m_DefaultWidth,
                new float2(ga.m_DefaultHeightRange.min, ga.m_DefaultHeightRange.max),
                AvKontrollpunkte(b), gb.m_DefaultWidth,
                new float2(gb.m_DefaultHeightRange.min, gb.m_DefaultHeightRange.max));
        }

        private bool AvPruefeLeitungskollisionen()
        {
            var query = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using var permanent = query.ToEntityArray(Allocator.Temp);
            var paare = 0;
            var kollisionen = 0;
            for (var i = 0; i < _avKurse.Count; i++)
            {
                var kurs = _avKurse[i];
                foreach (var e in kurs.Kanten)
                {
                    var a = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    foreach (var fremd in permanent)
                    {
                        var prefab = EntityManager.GetComponentData<PrefabRef>(fremd).m_Prefab;
                        if (EntityManager.HasComponent<RoadData>(prefab)
                            || !EntityManager.HasComponent<NetGeometryData>(prefab)
                            || (!EntityManager.HasComponent<ElectricityConnectionData>(prefab)
                                && !EntityManager.HasComponent<WaterPipeConnectionData>(prefab))) continue;
                        paare++;
                        if (!AvKollision(a, kurs.Prefab, EntityManager.GetComponentData<Curve>(fremd).m_Bezier, prefab)) continue;
                        kollisionen++;
                        Mod.log.Warn($"PLT-Autoversorgung KOLLISION [{kurs.Name}]: Temp {e} "
                            + $"beruehrt vorhandene Leitung {fremd} '{PrefabAssetName(prefab)}'.");
                    }
                    for (var j = i + 1; j < _avKurse.Count; j++)
                        foreach (var andere in _avKurse[j].Kanten)
                        {
                            paare++;
                            if (!AvKollision(a, kurs.Prefab, EntityManager.GetComponentData<Curve>(andere).m_Bezier, _avKurse[j].Prefab)) continue;
                            kollisionen++;
                            Mod.log.Warn($"PLT-Autoversorgung KOLLISION: {kurs.Name} / {_avKurse[j].Name}.");
                        }
                }
            }
            Mod.log.Info($"PLT-Autoversorgung KOLLISIONSPRUEFUNG: {paare} Kurvenpaare, "
                + $"{kollisionen} Beruehrungen/konservative Restfaelle (5 cm).");
            return kollisionen == 0;
        }

        private bool AvTempAnStrasse(Entity knoten, IEnumerable<Entity> strassen, AvKurs kurs)
        {
            if (knoten == Entity.Null) return false;
            // CS2 erzeugt am Anschluss teils ein senkrechtes Zwischenstueck.
            // Dieses wurde bereits erfasst, bisher aber beim Nachweis ignoriert.
            // Nur echte gemeinsame Entities verbinden; Naehe reicht nicht.
            var kanten = new List<int2>();
            foreach (var e in kurs.Anschlussstuecke)
            {
                if (!EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                if (!EntityManager.HasComponent<Temp>(e)) continue;
                var t = EntityManager.GetComponentData<Temp>(e);
                if (t.m_Original != Entity.Null || (t.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0) continue;
                var k = EntityManager.GetComponentData<Edge>(e);
                kanten.Add(new int2(k.m_Start.Index, k.m_End.Index));
            }
            var erreicht = Versorgungsnetz.Erreichbar(new[] { knoten.Index }, kanten);
            // GenerateEdges schreibt zuerst ConnectedNode an die Temp-Strasse.
            // Den umgekehrten ConnectedEdge-Eintrag ergaenzt erst ApplyNetSystem.
            using var temp = AvTempQuery().ToEntityArray(Allocator.Temp);
            foreach (var e in temp)
            {
                var daten = EntityManager.GetComponentData<Temp>(e);
                if ((daten.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0) continue;
                var original = daten.m_Original;
                var passt = false;
                foreach (var strasse in strassen) if (original == strasse) passt = true;
                if (!passt) continue;
                var edge = EntityManager.GetComponentData<Edge>(e);
                if (erreicht.Contains(edge.m_Start.Index) || erreicht.Contains(edge.m_End.Index)) return true;
                if (!EntityManager.HasBuffer<ConnectedNode>(e)) continue;
                foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e, true))
                    if (erreicht.Contains(n.m_Node.Index)) return true;
            }
            return false;
        }

        private bool AvKursZusammenhaengend(AvKurs kurs, Entity start, Entity ende)
        {
            var ids = new Dictionary<Entity, int>();
            int Id(Entity n)
            {
                if (n == Entity.Null) return -1;
                if (!ids.TryGetValue(n, out var id)) { id = ids.Count; ids.Add(n, id); }
                return id;
            }
            var kanten = new List<int2>();
            foreach (var e in kurs.Kanten)
            {
                if (!EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<Deleted>(e)) return false;
                var k = EntityManager.GetComponentData<Edge>(e);
                kanten.Add(new int2(Id(k.m_Start), Id(k.m_End)));
            }
            return VersorgungskursPruefung.Zusammenhaengend(kanten, Id(start), Id(ende));
        }

        private bool AvPruefeTempAnschluesse()
        {
            var anschluesse = 0;
            foreach (var kurs in _avKurse)
            {
                var start = Entity.Null;
                var ende = Entity.Null;
                foreach (var kante in kurs.Kanten)
                {
                    var edge = EntityManager.GetComponentData<Edge>(kante);
                    foreach (var n in new[] { edge.m_Start, edge.m_End })
                    {
                        var pos = EntityManager.GetComponentData<Node>(n).m_Position;
                        if (math.distance(pos.xz, kurs.Start.xz) <= VersorgungskursPruefung.Toleranz) start = n;
                        if (math.distance(pos.xz, kurs.Ende.xz) <= VersorgungskursPruefung.Toleranz) ende = n;
                    }
                }
                var a = AvTempAnStrasse(start, kurs.Trasse.Startnetz, kurs);
                var b = AvTempAnStrasse(ende, new[] { kurs.Trasse.Zielkante }, kurs);
                AvMesseStartumfeld(kurs, start);
                AvMesseZielumfeld(kurs, ende);
                // Je Kurs merken, nicht nur zaehlen: sonst laesst sich ein
                // misslungener Kurs nicht von einem gelungenen trennen, und
                // ein einziger Fehlschlag verwirft alle.
                var zusammen = AvKursZusammenhaengend(kurs, start, ende);
                kurs.Angeschlossen = a && b && zusammen;
                anschluesse += kurs.Angeschlossen ? 2 : 0;
                Mod.log.Info($"PLT-Autoversorgung TEMP-ANSCHLUSS [{kurs.Name}]: "
                    + $"Start/Ziel {(a ? 1 : 0)}/{(b ? 1 : 0)}, zusammenhaengend {(zusammen ? 1 : 0)}, {kurs.Punkte.Count - 1} Teilstrecken, {kurs.Anschlussstuecke.Count} Anschlussstuecke mitgeprueft. Versorgungsgraph folgt erst nach Created ohne Temp.");
            }
            return anschluesse == _avKurse.Count * 2;
        }
    }
}
