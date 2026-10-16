using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Quellen vor Ausgabe, Bindung nach den Generator-Barrieren im selben Bild. */
    internal sealed partial class ParkingLotErzeugungsSystem : GameSystemBase
    {
        private EntityQuery _auftraege, _leitungen, _halte, _definitionen;
        // Bis hierher jedes Bild binden; danach reicht einmal je Sekunde,
        // denn Schild und Leitung bleiben an ihrer Stelle bindbar. Ein
        // haengender Auftrag kostet so keinen Vollscan aller Kanten je Bild.
        private int _jedesBildBis;
        protected override void OnCreate()
        {
            base.OnCreate();
            _auftraege = GetEntityQuery(ComponentType.ReadOnly<ParkingLotErzeugungsauftrag>(),
                ComponentType.ReadOnly<ParkingLotCarrierReference>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            _leitungen = GetEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<PseudoRandomSeed>(),
                ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>());
            _halte = GetEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<PseudoRandomSeed>(),
                ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.Exclude<Deleted>());
            _definitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
        }

        internal int Bestelle(Entity lot, Entity carrier, Entity prefab, ParkingLotErzeugungsart art,
            float3 a, float3 d, Entity bezug = default)
        {
            if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, lot, carrier)) return 0;
            var name = ParkingLotPrefabname.Von(EntityManager, prefab);
            if (name.IsEmpty) return 0;
            // Erst gespeicherte, noch nicht materialisierte Bestellung wiederverwenden.
            if (EntityManager.HasBuffer<ParkingLotErzeugungsauftrag>(lot))
            {
                var vorher = EntityManager.GetBuffer<ParkingLotErzeugungsauftrag>(lot, true);
                for (int i = 0; i < vorher.Length; i++)
                {
                    var q = vorher[i];
                    if (!q.Erledigt && q.Art == art && q.Prefabname.Equals(name) && q.Bezug == bezug
                        && math.distancesq(q.A, a) < .000001f && math.distancesq(q.D, d) < .000001f) return q.Seed;
                }
            }
            // Native bewahrt nur 16 Bit. Kein vorhandener Seed desselben
            // Prefabs und keine parallele Definition darf als Quelle gelten.
            var belegt = new HashSet<int>();
            using (var alle = (art == ParkingLotErzeugungsart.Bushalt ? _halte : _leitungen).ToEntityArray(Allocator.Temp))
                foreach (var e in alle)
                    if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab == prefab)
                        belegt.Add(EntityManager.GetComponentData<PseudoRandomSeed>(e).m_Seed);
            using (var alle = _definitionen.ToEntityArray(Allocator.Temp))
                foreach (var e in alle)
                {
                    var cd = EntityManager.GetComponentData<CreationDefinition>(e);
                    if (cd.m_Prefab == prefab) belegt.Add((ushort)cd.m_RandomSeed);
                }
            using (var alle = _auftraege.ToEntityArray(Allocator.Temp))
                foreach (var l in alle)
                {
                    var p = EntityManager.GetBuffer<ParkingLotErzeugungsauftrag>(l, true);
                    for (int i = 0; i < p.Length; i++)
                        if (p[i].Prefabname.Equals(name)) belegt.Add(p[i].Seed);
                }
            int seed = 1;
            while (seed <= ushort.MaxValue && belegt.Contains(seed)) seed++;
            if (seed > ushort.MaxValue) return 0;
            var puffer = EntityManager.HasBuffer<ParkingLotErzeugungsauftrag>(lot)
                ? EntityManager.GetBuffer<ParkingLotErzeugungsauftrag>(lot)
                : EntityManager.AddBuffer<ParkingLotErzeugungsauftrag>(lot);
            puffer.Add(new ParkingLotErzeugungsauftrag { Art = art, Prefabname = name, Seed = seed, A = a, D = d, Bezug = bezug });
            _jedesBildBis = UnityEngine.Time.frameCount + 120;
            return seed;
        }

        protected override void OnUpdate()
        {
            if (!World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().Register.Bereit) return;
            if (_auftraege.IsEmptyIgnoreFilter) return;
            int bild = UnityEngine.Time.frameCount;
            if (bild <= _jedesBildBis || bild % 60 == 0) BindeErgebnisse();
        }

        internal void BindeErgebnisse()
        {
            using var lots = _auftraege.ToEntityArray(Allocator.Temp);
            using var leitungen = _leitungen.ToEntityArray(Allocator.Temp);
            using var halte = _halte.ToEntityArray(Allocator.Temp);
            foreach (var lot in lots)
            {
                var carrier = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
                if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, lot, carrier)) continue;
                // AddComponent am Ergebnis invalidiert geliehene Puffer.
                using var auftraege = EntityManager.GetBuffer<ParkingLotErzeugungsauftrag>(lot, true).ToNativeArray(Allocator.Temp);
                for (int i = 0; i < auftraege.Length; i++)
                {
                    var q = auftraege[i];
                    if (q.Erledigt) continue;
                    var prefab = ParkingLotPrefabname.Aufloesen(EntityManager, q.Prefabname);
                    if (prefab == Entity.Null) continue;
                    var treffer = new List<Entity>();
                    foreach (var e in q.Art == ParkingLotErzeugungsart.Bushalt ? halte : leitungen)
                    {
                        if (!Passt(e, q, prefab, lot, carrier)) continue;
                        treffer.Add(e);
                    }
                    // Eine Haltbestellung erzeugt genau ein Schild.
                    if (q.Art == ParkingLotErzeugungsart.Bushalt)
                    {
                        if (!BindeBushalt(lot, carrier, prefab, ref q, treffer)) continue;
                        var e = q.Ergebnis;
                        var puffer = EntityManager.GetBuffer<ParkingLotErzeugungsauftrag>(lot);
                        puffer[i] = q;
                        Diagnose(() => Mod.log.Info($"PLT-Bushalt Herkunft: Lot {lot.Index}, Entity {e.Index}, Seed {q.Seed}; Owner bleibt leer."));
                    }
                    else
                        foreach (var e in treffer)
                        {
                            BindeLeitung(lot, carrier, prefab, q, e);
                        }
                }
                EntferneQuittierte(lot);
            }
        }

        private void EntferneQuittierte(Entity lot)
        {
            if (!EntityManager.HasBuffer<ParkingLotErzeugungsauftrag>(lot)) return;
            var p = EntityManager.GetBuffer<ParkingLotErzeugungsauftrag>(lot);
            for (int i = p.Length - 1; i >= 0; i--) if (p[i].Erledigt) p.RemoveAt(i);
            if (p.Length == 0) EntityManager.RemoveComponent<ParkingLotErzeugungsauftrag>(lot);
        }

        internal void BestaetigeVersorgung(Entity lot) => Bestaetige(lot, false);
        internal void BestaetigeBushalte(Entity lot) => Bestaetige(lot, true);

        private void Bestaetige(Entity lot, bool bushalt)
        {
            if (!EntityManager.Exists(lot) || !EntityManager.HasBuffer<ParkingLotErzeugungsauftrag>(lot)) return;
            var p = EntityManager.GetBuffer<ParkingLotErzeugungsauftrag>(lot);
            for (int i = 0; i < p.Length; i++)
            {
                var q = p[i]; if ((q.Art == ParkingLotErzeugungsart.Bushalt) != bushalt) continue;
                q.Erledigt = true; p[i] = q;
            }
            EntferneQuittierte(lot);
        }

        private static void Diagnose(Action aktion) { try { aktion(); } catch (Exception) { } }
    }
}
