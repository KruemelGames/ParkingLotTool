using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotHintergrundDiagnose
    {
        // Diagnose liegt ausserhalb der Erzeugung. TryGet und diese letzte
        // Schranke verhindern einen zweiten Produktionsabbruch durch Messcode.
        internal static void Sicher(Action messen)
        {
            try { messen(); }
            catch (Exception e) { ParkingLotNetzRueckweg.Melde("Diagnose unvollstaendig: " + e.Message); }
        }
        internal static string E(Entity e) => e == Entity.Null ? "(kein)" : $"{e.Index}.{e.Version}";
        internal static string P(float3 p) => FormattableString.Invariant($"({p.x:F4}/{p.y:F4}/{p.z:F4})");
        internal static string Name(PrefabSystem prefabs, Entity e)
            => $"{E(e)} '" + (prefabs.TryGetPrefab<PrefabBase>(e,out var p) && p != null ? p.name : "unbekannt") + "'";

        internal static void Kurs(EntityManager em, PrefabSystem prefabs, string phase, int index,
            string art, Entity prefab, Entity besitzer, Bezier4x3 soll, string grund,
            Entity start = default, Entity ende = default, Func<Entity,bool,string> zusatz = null, bool innenhoeheVanilla = false)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>());
            using var kandidaten = q.ToEntityArray(Allocator.Temp);
            Entity naechster = Entity.Null; float abstand = float.PositiveInfinity;
            bool gleicherPrefab = false;
            var sollMin = math.min(math.min(soll.a.xz,soll.b.xz),math.min(soll.c.xz,soll.d.xz))-200;
            var sollMax = math.max(math.max(soll.a.xz,soll.b.xz),math.max(soll.c.xz,soll.d.xz))+200;
            foreach (var e in kandidaten)
            {
                var curve = em.GetComponentData<Curve>(e).m_Bezier;
                var min = math.min(math.min(curve.a.xz,curve.b.xz),math.min(curve.c.xz,curve.d.xz));
                var max = math.max(math.max(curve.a.xz,curve.b.xz),math.max(curve.c.xz,curve.d.xz));
                if (math.any(max < sollMin) || math.any(min > sollMax)) continue;
                bool gleich = em.GetComponentData<PrefabRef>(e).m_Prefab == prefab;
                float d = ParkingLotKursabgleich.Abstand(soll,curve,out _,innenhoeheVanilla);
                if (!math.isfinite(d) || gleicherPrefab && !gleich || gleich == gleicherPrefab && d >= abstand) continue;
                naechster = e; abstand = d; gleicherPrefab = gleich;
            }
            string ist = "kein Ist-Kandidat; Abstand=unendlich";
            if (naechster != Entity.Null)
            {
                var c = em.GetComponentData<Curve>(naechster).m_Bezier;
                var edge = em.GetComponentData<Edge>(naechster);
                var owner = em.HasComponent<Owner>(naechster) ? em.GetComponentData<Owner>(naechster).m_Owner : Entity.Null;
                ParkingLotKursabgleich.Abstand(soll,c,out var bereich,innenhoeheVanilla);
                float roh = ParkingLotKursabgleich.Abstand(soll,c,out _);
                bool um = bereich.x > bereich.y;
                var gr = new List<string>();
                if (!gleicherPrefab) gr.Add("Prefab verschieden");
                if (owner != besitzer) gr.Add("Besitzer verschieden");
                if (!(abstand <= .05f)) gr.Add("Kurvenlage ueber 5 cm");
                if (start != Entity.Null && start != (um ? edge.m_End : edge.m_Start)) gr.Add("Start-ID verschieden");
                if (ende != Entity.Null && ende != (um ? edge.m_Start : edge.m_End)) gr.Add("End-ID verschieden");
                bool Verbunden(Entity node)
                {
                    if (!em.HasBuffer<ConnectedEdge>(node)) return false;
                    foreach (var v in em.GetBuffer<ConnectedEdge>(node,true)) if (v.m_Edge == naechster) return true;
                    return false;
                }
                if (!Verbunden(edge.m_Start) || !Verbunden(edge.m_End)) gr.Add("ConnectedEdge-Gegenrichtung fehlt");
                var nodeA = em.HasComponent<Game.Net.Node>(edge.m_Start) ? P(em.GetComponentData<Game.Net.Node>(edge.m_Start).m_Position) : "fehlt";
                var nodeD = em.HasComponent<Game.Net.Node>(edge.m_End) ? P(em.GetComponentData<Game.Net.Node>(edge.m_End).m_Position) : "fehlt";
                ist = $"Ist {E(naechster)}, Prefab {Name(prefabs,em.GetComponentData<PrefabRef>(naechster).m_Prefab)}, "
                    + $"Besitzer {E(owner)}, {P(c.a)} -> {P(c.d)}, Kontrollen {P(c.b)}/{P(c.c)}, "
                    + $"Knoten {E(edge.m_Start)}/{E(edge.m_End)}, Teilparameter {bereich.x:F6}..{bereich.y:F6}, "
                    + $"Richtung {(um ? "umgekehrt" : "gleich")}, Pruefabstand {abstand:F5} m, 3D-Rohabstand {roh:F5} m, Innenhoehe Vanilla={innenhoeheVanilla}; "
                    + string.Join(", ",gr) + $"; Knotenlagen {nodeA}/{nodeD}; " + (zusatz?.Invoke(naechster,um) ?? "");
            }
            ParkingLotNetzRueckweg.Melde($"{phase} Soll-Kurs {index} ({art}), Prefab {Name(prefabs,prefab)}, "
                + $"Besitzer {E(besitzer)}, {P(soll.a)} -> {P(soll.d)}, Kontrollen {P(soll.b)}/{P(soll.c)}, "
                + $"Originalknoten {E(start)}/{E(ende)}; FEHLT: {grund}; naechster im 200-m-Suchfeld {ist}.");
        }
    }

    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<PartTransferRecord> _fehlendeHintergrundobjekte = new List<PartTransferRecord>();
        private readonly List<PartTransferRecord> _fehlendeHintergrundkurse = new List<PartTransferRecord>();
        private readonly List<ParkingLotRueckwegkurs> _fehlendeErhalteneKurse = new List<ParkingLotRueckwegkurs>();
        private readonly Dictionary<PartTransferRecord,(float3 Position,bool Erlaubt)> _hintergrundObjektboden
            = new Dictionary<PartTransferRecord,(float3,bool)>();

        private IEnumerable<int> BerechneHintergrundObjektbodenSchritte()
        {
            _hintergrundObjektboden.Clear();
            foreach (var r in _objectRecords)
            {
                // Lookups nie ueber ein yield halten: zwischen Portionen
                // verschieben Vanilla-Generatoren die Prefab-/Objektchunks.
                var height = _terrainSystem.GetHeightData(waitForPending: !_bauarbeiter);
                var wasser = World.GetOrCreateSystemManaged<WaterSystem>().GetSurfaceData(out var deps); deps.Complete();
                var platz = GetComponentLookup<PlaceableObjectData>(true);
                var geometrie = GetComponentLookup<ObjectGeometryData>(true);
                var p = r.From; bool erlaubt = false;
                if (r.Objekt.HasValue && geometrie.TryGetComponent(r.Prefab,out var g))
                {
                    var d = r.Objekt.Value;
                    // Dieselbe Zulassung wie GroundHeightSystem 592-602.
                    erlaubt = d.m_ParentMesh == -1 && d.m_Elevation == 0
                        && (g.m_Flags & Game.Objects.GeometryFlags.DeleteOverridden) == 0
                        && (g.m_Flags & (Game.Objects.GeometryFlags.Overridable | Game.Objects.GeometryFlags.Marker
                            | Game.Objects.GeometryFlags.Brushable)) != 0;
                    if (erlaubt)
                    {
                        var elevation = default(Game.Objects.Elevation);
                        p = Game.Objects.ObjectUtils.AdjustPosition(new Game.Objects.Transform(r.From,d.m_Rotation),
                            ref elevation,r.Prefab,out _,ref height,ref wasser,ref platz,ref geometrie).m_Position;
                    }
                }
                _hintergrundObjektboden.Add(r,(p,erlaubt));
                yield return 0;
            }
        }

        private bool HintergrundSollanschluss(PartTransferRecord r, Entity start, Entity ende)
        {
            if (!r.Kurs.HasValue) return false;
            var c = r.Kurs.Value;
            return (!EntityManager.HasComponent<Game.Net.Node>(c.m_StartPosition.m_Entity) || c.m_StartPosition.m_Entity == start)
                && (!EntityManager.HasComponent<Game.Net.Node>(c.m_EndPosition.m_Entity) || c.m_EndPosition.m_Entity == ende);
        }

        private HintergrundPortion _fehlstellenportion;
        internal bool HintergrundFehlstellen()
        {
            _fehlstellenportion ??= new HintergrundPortion(HintergrundFehlstellenschritte(),_hintergrundTempo);
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            try { _fehlstellenportion.Weiter(() => uhr.Elapsed.TotalMilliseconds); }
            catch (Exception e) { ParkingLotNetzRueckweg.Melde("Diagnose unvollstaendig: " + e.Message); return true; }
            return _fehlstellenportion.Fertig;
        }
        private IEnumerable<int> HintergrundFehlstellenschritte()
        {
                foreach (var r in _fehlendeErhalteneKurse.Take(4))
                {
                    yield return 0;
                    ParkingLotHintergrundDiagnose.Kurs(EntityManager,_prefabSystem,"Erhalt",r.Kante.Index,
                        "Originalkante",r.Prefab,r.Besitzer,r.Kurs.m_Curve,
                        "Abnahme jetzt: " + ParkingLotNetzerhalt.Gruende(EntityManager,r),r.Start,r.Ende);
                }
                foreach (var r in _fehlendeHintergrundkurse.Take(4))
                {
                    yield return 0;
                    var c = r.Kurs.GetValueOrDefault();
                    bool innen = ParkingLotKursabgleich.InnenhoeheVanilla(EntityManager,r.Prefab,c,_lotOwner)
                        && ParkingLotKursabgleich.InnenhoeheNachGeneratoren(EntityManager,r.Prefab,c,_lotOwner);
                    var teile = ParkingLotKursabgleich.Sammle(EntityManager,_eigeneDauerteile,c.m_Curve,r.Prefab,_lotOwner,innenhoeheVanilla:innen);
                    bool kette = ParkingLotKursabgleich.Kette(teile,out var start,out var ende);
                    string grund = !kette ? $"keine volle verbundene Kurvenkette ({teile.Count} Abschnitte innerhalb 5 cm)"
                        : !HintergrundSollanschluss(r,start,ende) ? "erhaltener Anschlussknoten nicht benutzt" : "17 Lageproben/mehrfache Ergebniszuordnung";
                    ParkingLotHintergrundDiagnose.Kurs(EntityManager,_prefabSystem,"Stufe B",r.Index,r.Kind,
                        r.Prefab,_lotOwner,c.m_Curve,grund,
                        EntityManager.HasComponent<Game.Net.Node>(c.m_StartPosition.m_Entity) ? c.m_StartPosition.m_Entity : Entity.Null,
                        EntityManager.HasComponent<Game.Net.Node>(c.m_EndPosition.m_Entity) ? c.m_EndPosition.m_Entity : Entity.Null,
                        innenhoeheVanilla:innen);
                }
                Entity[] objekte;
                using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>()))
                using (var a = q.ToEntityArray(Allocator.Temp)) objekte = a.ToArray();
                foreach (var r in _fehlendeHintergrundobjekte.Take(4))
                {
                    yield return 0;
                    Entity best = Entity.Null; float abstand = float.PositiveInfinity; bool gleichPrefab = false;
                    var boden = _hintergrundObjektboden[r];
                    foreach (var e in objekte)
                    {
                        if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e)) continue;
                        bool gleich = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab == r.Prefab;
                        var p = EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position;
                        float d = math.distance(p,r.From);
                        if (boden.Erlaubt) d = math.min(d,math.distance(p,boden.Position));
                        if (!math.isfinite(d) || gleichPrefab && !gleich || gleich == gleichPrefab && d >= abstand) continue;
                        best = e; abstand = d; gleichPrefab = gleich;
                    }
                    string ist = "kein Ist-Kandidat; Abstand=unendlich";
                    if (best != Entity.Null)
                    {
                        var p = EntityManager.GetComponentData<Game.Objects.Transform>(best).m_Position;
                        var owner = EntityManager.HasComponent<Owner>(best) ? EntityManager.GetComponentData<Owner>(best).m_Owner : Entity.Null;
                        var attached = EntityManager.HasComponent<Game.Objects.Attached>(best)
                            ? EntityManager.GetComponentData<Game.Objects.Attached>(best).m_Parent : Entity.Null;
                        ist = $"Ist {ParkingLotHintergrundDiagnose.E(best)}, Prefab "
                            + ParkingLotHintergrundDiagnose.Name(_prefabSystem,EntityManager.GetComponentData<PrefabRef>(best).m_Prefab)
                            + $", Position {ParkingLotHintergrundDiagnose.P(p)}, delta {ParkingLotHintergrundDiagnose.P(p-r.From)}, "
                            + $"Abstand {abstand:F5} m, Besitzer {owner}, Attached {attached}; Grund: "
                            + (!gleichPrefab ? "Prefab verschieden" : owner != _lotCarrier ? "Besitzer verschieden"
                                : attached != _lotCarrier ? "Attached verschieden" : abstand > .05f ? "Lage ueber 5 cm" : "Ergebnis bereits zugeordnet");
                    }
                    ParkingLotNetzRueckweg.Melde($"Stufe B Soll-Objekt {r.Index} ({r.Kind}), Prefab "
                        + ParkingLotHintergrundDiagnose.Name(_prefabSystem,r.Prefab)
                        + $", Besitzer {_lotCarrier}, Position {ParkingLotHintergrundDiagnose.P(r.From)}, "
                        + $"Vanilla-Bodensoll {ParkingLotHintergrundDiagnose.P(boden.Position)} (zugelassen={boden.Erlaubt}); "
                        + $"FEHLT; naechster {ist}.");
                }
            ParkingLotNetzRueckweg.Melde($"Fehlstellen gesamt: neue Kurse {_fehlendeHintergrundkurse.Count}, erhaltene Kanten {_fehlendeErhalteneKurse.Count}, Objekte {_fehlendeHintergrundobjekte.Count}; Detailgrenze jeweils 4, eine Suche je Einheit.");
        }
    }
}
