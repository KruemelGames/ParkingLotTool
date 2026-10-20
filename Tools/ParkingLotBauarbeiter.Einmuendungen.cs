using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using CarLane = Game.Net.CarLane;
using SubLane = Game.Net.SubLane;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private readonly HashSet<Entity> _hintergrundRoutenpunkte = new HashSet<Entity>();
        private IEnumerable<int> HintergrundEinmuendungsschritte()
        {
            foreach (var plan in _gassenplan)
            {
                // Ein Stummel muendet nirgends ein; es gibt keine Einmuendung zu pruefen.
                if (plan.Strasse == Entity.Null)
                {
                    ParkingLotNetzRueckweg.Melde($"Einmuendung {plan.Index}: Stummel ohne Stadtstrasse, nichts zu pruefen.");
                    yield return 0;
                    continue;
                }
                Entity gasse = Entity.Null, knoten = Entity.Null;
                var kandidaten = new HashSet<Entity>(_eigeneDauerteile);
                kandidaten.UnionWith(_erhalteneNetzteile);
                foreach (var e in kandidaten)
                {
                    if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e) || !EntityManager.HasComponent<Edge>(e)
                        || !EntityManager.HasComponent<PrefabRef>(e)
                        || EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != plan.Gassenprefab) continue;
                    var k = EntityManager.GetComponentData<Edge>(e);
                    foreach (var n in new[] {k.m_Start,k.m_End})
                        if (EntityManager.HasComponent<Node>(n)
                            && math.distance(EntityManager.GetComponentData<Node>(n).m_Position.xz,plan.Mitte) <= .05f)
                        { gasse = e; knoten = n; }
                }
                bool hin = false, zurueck = false;
                var stadt = new HashSet<int>();
                if (EntityManager.HasBuffer<ConnectedEdge>(knoten))
                    foreach (var c in EntityManager.GetBuffer<ConnectedEdge>(knoten,true))
                    {
                        var e = c.m_Edge;
                        if (e == gasse) zurueck = true;
                        if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e) || !EntityManager.HasComponent<Edge>(e)) continue;
                        var k = EntityManager.GetComponentData<Edge>(e);
                        if (k.m_Start != knoten && k.m_End != knoten) continue;
                        if (e == gasse) hin = true;
                        else if (!EntityManager.HasComponent<Owner>(e) && EntityManager.HasComponent<Road>(e)) stadt.Add(e.Index);
                    }
                int rein = 0, raus = 0, oeffentlich = 0;
                if (EntityManager.HasBuffer<SubLane>(knoten))
                    foreach (var s in EntityManager.GetBuffer<SubLane>(knoten,true))
                    {
                        var e = s.m_SubLane;
                        if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e) || !EntityManager.HasComponent<Lane>(e)
                            || !EntityManager.HasComponent<CarLane>(e)) continue;
                        var l = EntityManager.GetComponentData<Lane>(e);
                        int a = l.m_StartNode.GetOwnerIndex(), b = l.m_EndNode.GetOwnerIndex();
                        bool hinein = stadt.Contains(a) && b == gasse.Index;
                        bool hinaus = a == gasse.Index && stadt.Contains(b);
                        if (!hinein && !hinaus) continue;
                        var c = EntityManager.GetComponentData<CarLane>(e);
                        bool auto = EntityManager.HasComponent<PrefabRef>(e)
                            && EntityManager.HasComponent<CarLaneData>(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)
                            && (EntityManager.GetComponentData<CarLaneData>(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab).m_RoadTypes & RoadTypes.Car) != 0;
                        if (HintergrundSpurregel.PrivateAutos(auto,(c.m_Flags & CarLaneFlags.PublicOnly) != 0,
                            (c.m_Flags & CarLaneFlags.Forbidden) != 0))
                        { if (hinein) rein++; if (hinaus) raus++; }
                        else oeffentlich++;
                    }
                bool erwartetRein = plan.Art != Zufahrtsart.GasseAus;
                bool erwartetRaus = plan.Art != Zufahrtsart.GasseEin;
                bool gut = HintergrundSpurregel.Einmuendung(hin && stadt.Count > 0,zurueck,erwartetRein,erwartetRaus,rein,raus);
                if (!gut) _spurenRichtig = false;
                ParkingLotNetzRueckweg.Melde($"Einmuendung {plan.Index}: Gasse {gasse}, Knoten {knoten}, Stadtarme {stadt.Count}, Edge/ConnectedEdge {hin}/{zurueck}; private Autospuren rein/raus {rein}/{raus}, eingeschraenkte Spuren {oeffentlich}, Soll {erwartetRein}/{erwartetRaus}; gueltig={gut}.");
                yield return 0;
            }
            // Buslinien werden nur beobachtet. Vanilla WaypointConnection
            // 89-99/1210-1345 erneuert RouteLane nach Deleted/Updated selbst.
            int routen = 0, offen = 0;
            foreach (var w in _hintergrundRoutenpunkte)
            {
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,w) && EntityManager.HasComponent<Game.Routes.RouteLane>(w))
                {
                    routen++;
                    var r = EntityManager.GetComponentData<Game.Routes.RouteLane>(w);
                    bool gut = LebendeAutospur(r.m_StartLane) && LebendeAutospur(r.m_EndLane);
                    if (!gut) offen++;
                    ParkingLotNetzRueckweg.Melde($"Netzerhalt RouteLane: Wegpunkt {w}, Start/Ende {r.m_StartLane}/{r.m_EndLane}, lebende Autospuren={gut}; 0 Routen-Aenderungen.");
                }
                yield return 0;
            }
            ParkingLotNetzRueckweg.Melde($"Routenbefund nach Netzerhalt: {routen} RouteLane-Wegpunkte, {offen} mit fehlender/geloeschter Autospur; Ursache fuer Linienbefund nur bei Ingame-Messung belegbar.");
        }
        private void MerkeAnschlussrouten(Entity knoten)
        {
            var teile = new HashSet<Entity> { knoten };
            if (EntityManager.HasBuffer<ConnectedEdge>(knoten))
                foreach (var e in EntityManager.GetBuffer<ConnectedEdge>(knoten,true)) teile.Add(e.m_Edge);
            foreach (var e in teile)
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e) && EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(e))
                    foreach (var r in EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(e,true)) _hintergrundRoutenpunkte.Add(r.m_Waypoint);
        }
        private bool LebendeAutospur(Entity e)
            => ParkingLotNetzRueckweg.Lebt(EntityManager,e) && EntityManager.HasComponent<CarLane>(e);
    }
}
