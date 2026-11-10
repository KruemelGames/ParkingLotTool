using System;
using System.Collections.Generic;
using Game;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Wie Fahrprefabs: Anmeldung VOR PrefabInitialize, Freigabe NACH NetInitialize. */
    public sealed partial class ParkingLotFahrradPrefabSystem : GameSystemBase
    {
        private PrefabSystem _prefabs;
        private EntityQuery _wege;
        private readonly Dictionary<string, (Entity Entity, int Bild)> _klone = new();
        private readonly HashSet<string> _bereit = new();
        private readonly HashSet<string> _breitenfehler = new();
        private Entity _lot;
        private readonly Dictionary<Fahrradlayout, Entity> _lots = new();
        private Entity _gehen;
        private int _lotBild;
        private bool _fehler;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _wege = GetEntityQuery(ComponentType.ReadOnly<PathwayData>(), ComponentType.ReadOnly<NetData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_fehler) return;
            try
            {
                if (_lot == Entity.Null)
                {
                    foreach (var layout in new[] { Fahrradlayout.A, Fahrradlayout.C, Fahrradlayout.D })
                    {
                        var lot = ParkingLotFahrradPrefabBau.Lot(layout);
                        if (!_prefabs.AddPrefab(lot)) throw new InvalidOperationException("Fahrrad-Lot abgelehnt");
                        var entity = _prefabs.GetEntity(lot);
                        EntityManager.AddComponentData(entity, new ParkingLotFahrradLayout { Layout = layout });
                        _lots.Add(layout, entity);
                    }
                    _lot = _lots[Fahrradlayout.A]; _lotBild = UnityEngine.Time.frameCount;
                    var gehen = ParkingLotFahrradPrefabBau.Gehflaeche();
                    if (!_prefabs.AddPrefab(gehen)) throw new InvalidOperationException("Fahrrad-Gehflaeche abgelehnt");
                    _gehen = _prefabs.GetEntity(gehen);
                }
                if (_klone.Count == 3) return;
                using var alle = _wege.ToEntityArray(Allocator.Temp);
                foreach (var e in alle)
                {
                    if (!_prefabs.TryGetPrefab<PathwayPrefab>(e, out var original)
                        || !original.isBuiltin || original.name != "TwoWayBikePath") continue;
                    foreach (var paar in new[] { (ParkingLotFahrradPrefabBau.Zugang, 3f),
                        (ParkingLotFahrradPrefabBau.Innen, 3f), (ParkingLotFahrradPrefabBau.Schmal, 2.5f) })
                    {
                        if (_klone.ContainsKey(paar.Item1)) continue;
                        var klon = ParkingLotFahrradPrefabBau.Weg(original, paar.Item1, paar.Item2, Registriere);
                        _klone.Add(paar.Item1, (_prefabs.GetEntity(klon), UnityEngine.Time.frameCount));
                    }
                    return;
                }
            }
            catch (Exception e)
            { _fehler = true; Mod.log.Error(e, "PLT-Bauzettel Fahrradprefabs: Startregistrierung fehlgeschlagen."); }
        }

        private void Registriere(PrefabBase quelle, PrefabBase klon)
        {
            if (!_prefabs.AddPrefab(klon)) throw new InvalidOperationException("Fahrradklon abgelehnt: " + klon.name);
            if (_prefabs.TryGetEntity(klon, out _))
                World.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>().Beobachte(klon);
        }

        internal void Abschluss()
        {
            foreach (var p in _klone)
            {
                var e = p.Value.Entity;
                if (UnityEngine.Time.frameCount <= p.Value.Bild
                    || !EntityManager.HasComponent<NetGeometryData>(e)
                    || !EntityManager.HasComponent<NetData>(e)
                    || !EntityManager.HasComponent<LocalConnectData>(e)) continue;
                var netz = EntityManager.GetComponentData<NetData>(e);
                if (!netz.m_NodeArchetype.Valid || !netz.m_EdgeArchetype.Valid) continue;
                var ist = EntityManager.GetComponentData<NetGeometryData>(e).m_DefaultWidth;
                var soll = p.Key == ParkingLotFahrradPrefabBau.Schmal ? 2.5f : 3f;
                if (Math.Abs(ist-soll)>.005f)
                {
                    if (_breitenfehler.Add(p.Key)) Mod.log.Warn($"PLT-Bauzettel Fahrradprefab {p.Key}: Querschnitt {ist:F3} statt {soll:F3} m; noch nicht freigegeben.");
                    continue;
                }
                var local = EntityManager.GetComponentData<LocalConnectData>(e);
                if ((local.m_Flags & LocalConnectFlags.RequireDeadend) == 0) continue;
                // Nur der aeussere Zugang sucht nach der Stadt. Suchweite
                // 4 m und RequireDeadend bleiben Vanilla (NetInitialize:2087).
                if (p.Key != ParkingLotFahrradPrefabBau.Zugang && local.m_Layers != Layer.None)
                { local.m_Layers = Layer.None; EntityManager.SetComponentData(e, local); }
                if (_bereit.Add(p.Key))
                    Mod.log.Info("PLT-Bauzettel Fahrradprefab bereit: " + p.Key + ", LocalConnect=" + local.m_Layers);
            }
        }

        internal bool TryWeg(bool zugang, double breite, out Entity e)
        {
            var name = zugang ? ParkingLotFahrradPrefabBau.Zugang
                : breite < 2.75 ? ParkingLotFahrradPrefabBau.Schmal : ParkingLotFahrradPrefabBau.Innen;
            e = Entity.Null;
            if (!_bereit.Contains(name)) return false;
            e = _klone[name].Entity; return true;
        }

        internal bool TryLot(out Entity e)
            => TryLot(Fahrradlayout.A, out e);

        internal bool TryLot(Fahrradlayout layout, out Entity e)
        {
            _lots.TryGetValue(layout, out e);
            return e != Entity.Null && UnityEngine.Time.frameCount > _lotBild
                && EntityManager.HasComponent<AreaData>(e)
                && EntityManager.HasComponent<AreaGeometryData>(e)
                && EntityManager.GetComponentData<AreaGeometryData>(e).m_Type == Game.Areas.AreaType.Lot
                && EntityManager.GetComponentData<AreaData>(e).m_Archetype.Valid
                && TryGehen(out _);
        }

        internal bool TryGehen(out Entity e)
        {
            e = _gehen;
            return e != Entity.Null && UnityEngine.Time.frameCount > _lotBild
                && EntityManager.HasComponent<AreaData>(e)
                && EntityManager.HasComponent<NavigationAreaData>(e)
                && EntityManager.HasComponent<AreaGeometryData>(e)
                && EntityManager.GetComponentData<AreaGeometryData>(e).m_Type == Game.Areas.AreaType.Space
                && EntityManager.GetComponentData<AreaData>(e).m_Archetype.Valid;
        }
    }

    public sealed partial class ParkingLotFahrradPrefabAbschlussSystem : GameSystemBase
    {
        [Preserve] protected override void OnUpdate()
            => World.GetOrCreateSystemManaged<ParkingLotFahrradPrefabSystem>().Abschluss();
    }
}
