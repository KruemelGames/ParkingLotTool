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
        private readonly List<Entity> _avAlleEigenen = new List<Entity>();
        private readonly HashSet<Entity> _avVersucht = new HashSet<Entity>();
        private HashSet<int> _avStadtStrom, _avStadtWasser;

        private void AvErfasseStadtpfade()
        {
            _avAlleEigenen.Clear();
            var query = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using var kanten = query.ToEntityArray(Allocator.Temp);
            var stromWurzeln = new HashSet<int>(); var wasserWurzeln = new HashSet<int>();
            var stromKnoten = new HashSet<int>(); var wasserKnoten = new HashSet<int>();
            void Merke(Entity e)
            {
                var s = AvFlussknoten(e, true); var w = AvFlussknoten(e, false);
                if (s != Entity.Null) stromKnoten.Add(s.Index);
                if (w != Entity.Null) wasserKnoten.Add(w.Index);
            }
            var eigeneTraegerkanten = new HashSet<Entity>(SammleUnsereKanten(_avTraeger));
            foreach (var e in kanten)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                var road = EntityManager.HasComponent<RoadData>(prefab);
                var eigen = EntityManager.HasComponent<Owner>(e);
                if ((eigen && road) || eigeneTraegerkanten.Contains(e)) _avAlleEigenen.Add(e);
                if (!road && !EntityManager.HasComponent<ElectricityConnectionData>(prefab)
                    && !EntityManager.HasComponent<WaterPipeConnectionData>(prefab)) continue;
                Merke(e);
                var edge = EntityManager.GetComponentData<Edge>(e);
                Merke(edge.m_Start); Merke(edge.m_End);
                if (EntityManager.HasBuffer<ConnectedNode>(e))
                    foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e, true)) Merke(n.m_Node);
                if (!road || eigen || !KanteNimmtVersorgung(e)) continue;
                var s = AvFlussknoten(e, true); var w = AvFlussknoten(e, false);
                if (s != Entity.Null) stromWurzeln.Add(s.Index);
                if (w != Entity.Null) wasserWurzeln.Add(w.Index);
            }
            // Globale Simulationsquellen/-senken sind keine Strassenverbindung.
            // Deshalb nur Flussknoten dauerhafter Netzkanten und ihrer Knoten.
            var sk = new List<int2>(); var wk = new List<int2>();
            using var strom = GetEntityQuery(ComponentType.ReadOnly<Game.Simulation.ElectricityFlowEdge>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()).ToEntityArray(Allocator.Temp);
            foreach (var e in strom)
            {
                var k = EntityManager.GetComponentData<Game.Simulation.ElectricityFlowEdge>(e);
                if (k.m_Capacity > 0 && !k.isDisconnected
                    && stromKnoten.Contains(k.m_Start.Index) && stromKnoten.Contains(k.m_End.Index))
                    sk.Add(new int2(k.m_Start.Index, k.m_End.Index));
            }
            using var wasser = GetEntityQuery(ComponentType.ReadOnly<Game.Simulation.WaterPipeEdge>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()).ToEntityArray(Allocator.Temp);
            foreach (var e in wasser)
            {
                var k = EntityManager.GetComponentData<Game.Simulation.WaterPipeEdge>(e);
                if (k.m_FreshCapacity > 0 && k.m_SewageCapacity > 0
                    && (k.m_Flags & (Game.Simulation.WaterPipeEdgeFlags.WaterDisconnected
                        | Game.Simulation.WaterPipeEdgeFlags.SewageDisconnected)) == 0
                    && wasserKnoten.Contains(k.m_Start.Index) && wasserKnoten.Contains(k.m_End.Index))
                    wk.Add(new int2(k.m_Start.Index, k.m_End.Index));
            }
            _avStadtStrom = Versorgungsnetz.Erreichbar(stromWurzeln, sk);
            _avStadtWasser = Versorgungsnetz.Erreichbar(wasserWurzeln, wk);
            Mod.log.Info($"PLT-Autoversorgung STADTPFAD: {stromWurzeln.Count}/{wasserWurzeln.Count} Stadtwurzeln, "
                + $"{_avStadtStrom.Count}/{_avStadtWasser.Count} erreichbare Strom-/Wasserknoten; 0 geplante Kanten als Quelle.");
        }

        private bool AvZielHatStadtpfad(Entity e)
        {
            if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                || EntityManager.HasComponent<Temp>(e) || !KanteNimmtVersorgung(e)) return false;
            // Eine Stadtstrasse ist die verlangte Wurzel. Ob dort Versorgung
            // fliesst, entscheidet weiterhin ausschliesslich die Flussabnahme.
            if (!EntityManager.HasComponent<Owner>(e)) return true;
            var s = AvFlussknoten(e, true); var w = AvFlussknoten(e, false);
            return s != Entity.Null && w != Entity.Null && _avStadtStrom.Contains(s.Index)
                && _avStadtWasser.Contains(w.Index);
        }

        private bool AvMesseNetzabdeckung()
        {
            var gruppen = SammleVersorgungsgruppen(SammleUnsereKanten(_avTraeger).FindAll(KanteNimmtVersorgung));
            var erreicht = 0;
            foreach (var gruppe in gruppen) if (gruppe.TrueForAll(AvZielHatStadtpfad)) erreicht++;
            var text = $"PLT-Autoversorgung NETZABDECKUNG: {erreicht}/{gruppen.Count} versorgungsfaehige Netze "
                + "mit Stadtpfad fuer Strom UND Wasser/Abwasser; Flussabnahme separat.";
            if (erreicht == gruppen.Count) Mod.log.Info(text); else Mod.log.Warn(text + " Anschlussnachweis unvollstaendig.");
            return erreicht == gruppen.Count;
        }

        private HashSet<int> AvZielstrassen(Entity ziel)
            => EntityManager.HasComponent<Owner>(ziel) ? new HashSet<int> { ziel.Index } : null;

        private bool AvStarttor(HashSet<int> strassen, float2 p, bool strom)
        {
            foreach (var e in _avAlleEigenen)
                if (strassen.Contains(e.Index) && AvZieltor(e, strom ? _avStromprefab : _avWasserprefab,
                    p, out _, out _)) return true;
            return false;
        }

        private List<float3> AvKantenpunkte(List<Entity> gruppe, List<(Entity Kante, Bezier4x3 Bogen)> ziele)
        {
            var r = new List<float3>();
            void Merke(Bezier4x3 b, float2 p)
            {
                MathUtils.Distance(b.xz, p, out var t);
                var punkt = MathUtils.Position(b, t);
                foreach (var alt in r) if (math.distance(alt, punkt) < 0.01f) return;
                r.Add(punkt);
            }
            foreach (var e in gruppe)
            {
                if (!KanteNimmtVersorgung(e) || !EntityManager.HasComponent<Curve>(e)) continue;
                var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                Merke(b, MathUtils.Position(b, 0.5f).xz);
                foreach (var ziel in ziele)
                {
                    float2 Projektion(Bezier4x3 kurve, float2 p)
                    {
                        MathUtils.Distance(kurve.xz, p, out var t);
                        return MathUtils.Position(kurve, t).xz;
                    }
                    foreach (var p in Versorgungsnetz.Kantenpunkte(t => MathUtils.Position(b, t).xz,
                        p => Projektion(b, p), t => MathUtils.Position(ziel.Bogen, t).xz,
                        p => Projektion(ziel.Bogen, p))) Merke(b, p);
                }
            }
            return r;
        }
    }
}
