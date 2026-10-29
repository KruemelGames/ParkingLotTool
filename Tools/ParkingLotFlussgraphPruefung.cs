using System.Collections.Generic;
using System.Text;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * TOTE VERWEISE IM VERSORGUNGSGRAPHEN - NUR LESEN.
     *
     * Bis 879139f markierte der Leitungsabriss alte Leitungen erst in
     * Modification2; Vanillas GraphDelete laeuft schon in Modification1. Die
     * Flussgraph-Eintraege dieser Leitungen blieben stehen und werden im
     * Spielstand mitgespeichert. Verdacht (2026-10-03): eine neue Leitung an
     * derselben Stelle laesst CS2 so einen Eintrag lesen -> nativer Absturz im
     * Bild der Uebernahme. Diese Pruefung zaehlt die Reste einmal nach jedem
     * Laden; sie aendert nichts.
     */
    public sealed partial class ParkingLotFlussgraphPruefungSystem : GameSystemBase
    {
        private int _pruefenAb = -1;

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode.IsGame()) _pruefenAb = UnityEngine.Time.frameCount + 60;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_pruefenAb < 0 || UnityEngine.Time.frameCount < _pruefenAb) return;
            _pruefenAb = -1;
            Pruefe();
        }

        internal void Pruefe()
        {
            var text = new StringBuilder();
            var beispiele = new List<string>();
            int strom = Kanten<ElectricityFlowEdge, ElectricityFlowNode>(
                e => (e.m_Start, e.m_End), "Strom", beispiele, out var stromAlle);
            int wasser = Kanten<WaterPipeEdge, WaterPipeNode>(
                e => (e.m_Start, e.m_End), "Wasser", beispiele, out var wasserAlle);
            int puffer = Flusspuffer(beispiele, out var pufferAlle);
            int anschluss = Anschluesse<ElectricityNodeConnection>(c => c.m_ElectricityNode, "Strom-Knotenanschluss", beispiele)
                + Anschluesse<WaterPipeNodeConnection>(c => c.m_WaterPipeNode, "Wasser-Knotenanschluss", beispiele)
                + Anschluesse<ElectricityValveConnection>(c => c.m_ValveNode, "Strom-Ventil", beispiele)
                + Anschluesse<WaterPipeValveConnection>(c => c.m_ValveNode, "Wasser-Ventil", beispiele);
            var summe = strom + wasser + puffer + anschluss;
            text.Append("PLT-Flussgraph-Pruefung (nur lesen): ")
                .Append(stromAlle).Append(" Strom-Flusskanten, davon ").Append(strom).Append(" mit totem Ende; ")
                .Append(wasserAlle).Append(" Wasser-Flusskanten, davon ").Append(wasser).Append(" mit totem Ende; ")
                .Append(pufferAlle).Append(" Flussknoten-Puffer, ").Append(puffer).Append(" tote Eintraege; ")
                .Append(anschluss).Append(" Anschluesse an tote Flussknoten.");
            if (beispiele.Count > 0) text.Append(" Beispiele: ").Append(string.Join(" | ", beispiele));
            if (summe > 0) Mod.log.Warn(text.ToString()); else Mod.log.Info(text.ToString());
            ParkingLotSchrittmarke.Setze("Flussgraph: " + summe + " tote Verweise");
            PruefeNetzbesitzer();
        }

        /**
         * Besitzer ohne SubNet-Puffer = nativer Absturz, sobald Vanilla das Netz
         * verarbeitet (Memory "cs2-subnet-subobject-puffer"; nackte Traeger
         * verlieren den Puffer beim Laden, "cs2-nackter-traeger"). Messung
         * 2026-10-03: am grossen Parkplatz haengen alte Vanilla-Wege mit
         * fremdem Besitzer 63223 an der Zoningstrasse. Nur lesen.
         */
        private void PruefeNetzbesitzer()
        {
            using var q = EntityManager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Game.Common.Owner>(), ComponentType.ReadOnly<Game.Prefabs.PrefabRef>() },
                    Any = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Node>() },
                    None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
                });
            using var netze = q.ToEntityArray(Allocator.Temp);
            var besitzer = new Dictionary<Entity, int>();
            int tot = 0, ohnePuffer = 0;
            foreach (var n in netze)
            {
                var o = EntityManager.GetComponentData<Game.Common.Owner>(n).m_Owner;
                if (o == Entity.Null) continue;
                bool lebt = Lebt(o);
                if (lebt && EntityManager.HasBuffer<Game.Net.SubNet>(o)) continue;
                if (!lebt) tot++; else ohnePuffer++;
                besitzer[o] = besitzer.TryGetValue(o, out var c) ? c + 1 : 1;
            }
            var text = new StringBuilder("PLT-Netzbesitzer-Pruefung (nur lesen): ")
                .Append(netze.Length).Append(" Netzteile mit Besitzer; ")
                .Append(tot).Append(" mit totem Besitzer, ")
                .Append(ohnePuffer).Append(" mit Besitzer OHNE SubNet-Puffer.");
            int i = 0;
            foreach (var p in besitzer)
            {
                if (i++ >= 6) break;
                text.Append(" | Besitzer ").Append(p.Key).Append(" (").Append(p.Value).Append(" Netzteile): ");
                if (!Lebt(p.Key)) { text.Append("existiert nicht"); continue; }
                using var typen = EntityManager.GetComponentTypes(p.Key, Allocator.Temp);
                var namen = new List<string>();
                foreach (var t in typen) namen.Add(t.GetManagedType()?.Name ?? t.ToString());
                text.Append(string.Join(",", namen));
            }
            if (tot + ohnePuffer > 0) Mod.log.Warn(text.ToString()); else Mod.log.Info(text.ToString());
            ParkingLotSchrittmarke.Setze("Netzbesitzer: " + (tot + ohnePuffer) + " ohne SubNet-Puffer/tot");
            PruefeWegbesitzer(netze);
            PruefeKantenObjekte();
        }

        /**
         * CompositionSelectSystem (Mod3) liest PrefabRef und Transform der
         * SubObject-Eintraege einer aktualisierten Kante OHNE Pruefung. Unsere
         * besitzerlosen PLT-Haltestellen stehen ueber AttachSystem in genau
         * dieser Liste. Ein toter Eintrag dort = nativer Absturz in Mod3, und
         * dort endet die Absturzspur (2026-10-03). Nur lesen.
         */
        private void PruefeKantenObjekte()
        {
            using var q = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Objects.SubObject>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            int eintraege = 0, tot = 0, ohnePrefab = 0, ohneTransform = 0, fremdAttached = 0;
            var beispiele = new List<string>();
            foreach (var k in kanten)
            {
                var puffer = EntityManager.GetBuffer<Game.Objects.SubObject>(k, true);
                for (var i = 0; i < puffer.Length; i++)
                {
                    var o = puffer[i].m_SubObject;
                    eintraege++;
                    string fehler = null;
                    if (!EntityManager.Exists(o)) { tot++; fehler = "existiert nicht"; }
                    else if (!EntityManager.HasComponent<Game.Prefabs.PrefabRef>(o)) { ohnePrefab++; fehler = "ohne PrefabRef"; }
                    else if (!EntityManager.HasComponent<Game.Objects.Transform>(o)) { ohneTransform++; fehler = "ohne Transform"; }
                    else if (EntityManager.HasComponent<Game.Objects.Attached>(o)
                             && !EntityManager.HasComponent<Game.Common.Owner>(o)
                             && EntityManager.GetComponentData<Game.Objects.Attached>(o).m_Parent != k)
                    { fremdAttached++; fehler = "Attached zeigt auf " + EntityManager.GetComponentData<Game.Objects.Attached>(o).m_Parent; }
                    if (fehler != null && beispiele.Count < 8) beispiele.Add($"Kante {k} -> {o}: {fehler}");
                }
            }
            var summe = tot + ohnePrefab + ohneTransform + fremdAttached;
            var text = $"PLT-Kantenobjekte (nur lesen): {kanten.Length} Kanten mit SubObject-Liste, {eintraege} Eintraege; "
                + $"{tot} tot, {ohnePrefab} ohne PrefabRef, {ohneTransform} ohne Transform, "
                + $"{fremdAttached} besitzerlos mit fremdem Attached-Parent."
                + (beispiele.Count > 0 ? " Beispiele: " + string.Join(" | ", beispiele) : "");
            if (summe > 0) Mod.log.Warn(text); else Mod.log.Info(text);
            ParkingLotSchrittmarke.Setze("Kantenobjekte: " + summe + " fehlerhafte Eintraege");
        }

        /**
         * Wem gehoeren unsichtbare und PLT-Wege? Messung 2026-10-03: zwei
         * Vanilla-'Invisible Car Path' IM grossen Parkplatz mit Besitzer 63223,
         * der nicht der aktuelle Traeger ist. Je Besitzer: Bauteile und ob er
         * zu einem lebenden Lot gehoert (Lot.CarrierReference zeigt auf ihn).
         *
         * KEIN BEFUND (29.10.2026): der Zoninganker eines lebenden Lots - er
         * traegt die Zoningstrassen, die Diagnose ist aelter als er - und
         * Spielgebaeude, deren Asset eigene unsichtbare Wege mitbringt. Beide
         * standen als FREMD in jedem Log (69 von 69 in einem Spielerstand).
         */
        private void PruefeWegbesitzer(NativeArray<Entity> netze)
        {
            var prefabs = World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
            var gruppen = new Dictionary<Entity, int>();
            var mitPlt = new HashSet<Entity>();
            foreach (var n in netze)
            {
                if (!EntityManager.HasComponent<Game.Net.Edge>(n)) continue;
                var name = prefabs.GetPrefabName(EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(n).m_Prefab);
                if (!name.Contains("Invisible") && !name.StartsWith("PLT ", System.StringComparison.Ordinal)) continue;
                var o = EntityManager.GetComponentData<Game.Common.Owner>(n).m_Owner;
                gruppen[o] = gruppen.TryGetValue(o, out var c) ? c + 1 : 1;
                if (name.StartsWith("PLT ", System.StringComparison.Ordinal)) mitPlt.Add(o);
            }
            var text = new StringBuilder("PLT-Wegbesitzer (nur lesen): ").Append(gruppen.Count).Append(" Besitzer.");
            int fremd = 0, gebaeude = 0;
            foreach (var p in gruppen)
            {
                var o = p.Key;
                bool gueltig = false;
                if (Lebt(o) && EntityManager.HasComponent<ParkingLotPartRelation>(o))
                {
                    var lot = EntityManager.GetComponentData<ParkingLotPartRelation>(o).Lot;
                    gueltig = Lebt(lot) && EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                        && EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier == o
                        || Lebt(lot) && EntityManager.HasComponent<ParkingLotZoninganker>(o)
                           && ParkingLotBesitz.GehoertZu(EntityManager, o, lot);
                }
                if (Lebt(o) && EntityManager.HasComponent<ParkingLotCarrierReference>(o)) gueltig = true;
                if (gueltig) continue;
                if (Lebt(o) && !mitPlt.Contains(o) && EntityManager.HasComponent<Game.Buildings.Building>(o))
                {
                    gebaeude++;
                    continue;
                }
                fremd++;
                text.Append(" | FREMD ").Append(o).Append(" (").Append(p.Value).Append(" Wegkanten): ");
                if (!Lebt(o)) { text.Append("existiert nicht"); continue; }
                using var typen = EntityManager.GetComponentTypes(o, Allocator.Temp);
                var namen = new List<string>();
                foreach (var t in typen) namen.Add(t.GetManagedType()?.Name ?? t.ToString());
                text.Append(string.Join(",", namen));
                if (EntityManager.HasComponent<Game.Common.Owner>(o))
                    text.Append(" Owner=").Append(EntityManager.GetComponentData<Game.Common.Owner>(o).m_Owner);
                if (EntityManager.HasComponent<Game.Prefabs.PrefabRef>(o))
                    text.Append(" Prefab='").Append(prefabs.GetPrefabName(
                        EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(o).m_Prefab)).Append("'");
            }
            text.Append(" Spielgebaeude mit eigenen unsichtbaren Wegen: ").Append(gebaeude).Append('.');
            text.Append(" Davon ohne lebenden Parkplatz: ").Append(fremd).Append('.');
            if (fremd > 0) Mod.log.Warn(text.ToString()); else Mod.log.Info(text.ToString());
        }

        private bool Lebt(Entity e)
            => e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);

        private int Kanten<TKante, TKnoten>(System.Func<TKante, (Entity, Entity)> enden, string art,
            List<string> beispiele, out int alle)
            where TKante : unmanaged, IComponentData where TKnoten : unmanaged, IComponentData
        {
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TKante>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            alle = kanten.Length;
            int tot = 0;
            foreach (var k in kanten)
            {
                var (a, b) = enden(EntityManager.GetComponentData<TKante>(k));
                bool aOk = Lebt(a) && EntityManager.HasComponent<TKnoten>(a);
                bool bOk = Lebt(b) && EntityManager.HasComponent<TKnoten>(b);
                if (aOk && bOk) continue;
                tot++;
                if (beispiele.Count < 8) beispiele.Add($"{art}-Kante {k} Start {a}{(aOk ? "" : "!")} Ende {b}{(bOk ? "" : "!")}");
            }
            return tot;
        }

        private int Flusspuffer(List<string> beispiele, out int alle)
        {
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ConnectedFlowEdge>());
            using var knoten = q.ToEntityArray(Allocator.Temp);
            alle = knoten.Length;
            int tot = 0;
            foreach (var n in knoten)
            {
                var puffer = EntityManager.GetBuffer<ConnectedFlowEdge>(n, true);
                for (var i = 0; i < puffer.Length; i++)
                {
                    var k = puffer[i].m_Edge;
                    if (Lebt(k) && (EntityManager.HasComponent<ElectricityFlowEdge>(k)
                        || EntityManager.HasComponent<WaterPipeEdge>(k))) continue;
                    tot++;
                    if (beispiele.Count < 8) beispiele.Add($"Flussknoten {n} -> tote Kante {k}");
                }
            }
            return tot;
        }

        private int Anschluesse<T>(System.Func<T, Entity> ziel, string art, List<string> beispiele)
            where T : unmanaged, IComponentData
        {
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>(),
                ComponentType.Exclude<Deleted>());
            using var teile = q.ToEntityArray(Allocator.Temp);
            int tot = 0;
            foreach (var e in teile)
            {
                var n = ziel(EntityManager.GetComponentData<T>(e));
                if (n == Entity.Null || Lebt(n)) continue;
                tot++;
                if (beispiele.Count < 8) beispiele.Add($"{art} {e} -> toter Flussknoten {n}");
            }
            return tot;
        }
    }
}
