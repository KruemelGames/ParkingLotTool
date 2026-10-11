using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotBuildingEconomySystem
    {
        private sealed class Anschluss
        {
            internal int Seit = UnityEngine.Time.frameCount;
            internal int NaechstePruefung;
            internal bool Sync;
            internal bool Platziert;
            internal int Versuche;
            internal int NaechstePlatzierung;
        }

        private readonly Dictionary<Entity, Anschluss> _anschluesse = new();
        internal int OffeneStrassensyncs
        {
            get { var n = 0; foreach (var a in _anschluesse.Values) if (a.Sync) n++; return n; }
        }

        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            _anschluesse.Clear();
            _strassenWartet.Clear();
            _strassenGemeldet.Clear();
            _announcedCompanions.Clear();
            _incompleteCompanionsLogged.Clear();
            _companionPrefab = Entity.Null;
            _companionPrefabConfigured = _companionPrefabSafe = false;
            _prefabFailureLogged = _flachzoneGemeldet = _archetypNachgeholt = false;
            _beurteilungen = 0;
            _letzterSteckbrief = string.Empty;
        }

        internal bool BrauchtStrassensync(Entity lot)
        {
            if (!Mod.WirtschaftAn || !Lebt(lot)) return false;
            var begleiter = BegleiterVon(lot);
            return !HatStrasse(begleiter) && Stadtstrassen(lot).Count > 0;
        }

        internal void StrassensyncEinreihen(Entity lot)
        {
            if (!_anschluesse.TryGetValue(lot, out var a))
                _anschluesse.Add(lot, a = new Anschluss());
            a.Sync = true;
            a.Seit = UnityEngine.Time.frameCount;
            a.NaechstePruefung = 0;
            a.Platziert = false;
            a.Versuche = a.NaechstePlatzierung = 0;
        }

        private Entity BegleiterVon(Entity lot)
        {
            using var teile = _companions.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (var i = 0; i < teile.Length; i++)
                if (EntityManager.GetComponentData<ParkingLotPartRelation>(teile[i]).Lot == lot) return teile[i];
            return Entity.Null;
        }

        private bool Lebt(Entity e) => e != Entity.Null && EntityManager.Exists(e)
            && !EntityManager.HasComponent<Deleted>(e) && !EntityManager.HasComponent<Temp>(e);

        private bool HatStrasse(Entity begleiter)
        {
            if (!Lebt(begleiter) || !EntityManager.HasComponent<Building>(begleiter)) return false;
            var strasse = EntityManager.GetComponentData<Building>(begleiter).m_RoadEdge;
            if (!Lebt(strasse) || !EntityManager.HasBuffer<ConnectedBuilding>(strasse)) return false;
            var verbunden = EntityManager.GetBuffer<ConnectedBuilding>(strasse, true);
            for (var i = 0; i < verbunden.Length; i++)
                if (verbunden[i].m_Building == begleiter) return true;
            return false;
        }

        /** Nur Stadtstrassen am gemeinsamen Zufahrtsknoten, keine Suche ueber die ganze Karte. */
        private List<(Entity Strasse, float3 Anschluss)> Stadtstrassen(Entity lot)
        {
            var funde = new List<(Entity, float3)>();
            if (!Lebt(lot) || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return funde;
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            var eigene = new HashSet<Entity>();
            foreach (var besitzer in new[] { lot, traeger })
            {
                if (!Lebt(besitzer) || !EntityManager.HasBuffer<Game.Net.SubNet>(besitzer)) continue;
                var netze = EntityManager.GetBuffer<Game.Net.SubNet>(besitzer, true);
                for (var i = 0; i < netze.Length; i++) eigene.Add(netze[i].m_SubNet);
            }
            var gesehen = new HashSet<Entity>();
            foreach (var netz in eigene)
            {
                if (!Lebt(netz) || !EntityManager.HasComponent<Edge>(netz)) continue;
                var kante = EntityManager.GetComponentData<Edge>(netz);
                foreach (var knoten in new[] { kante.m_Start, kante.m_End })
                {
                    if (!Lebt(knoten) || !EntityManager.HasComponent<Game.Net.Node>(knoten)
                        || !EntityManager.HasBuffer<ConnectedEdge>(knoten)) continue;
                    var verbunden = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
                    for (var i = 0; i < verbunden.Length; i++)
                    {
                        var fremd = verbunden[i].m_Edge;
                        if (eigene.Contains(fremd) || !Lebt(fremd)
                            || !EntityManager.HasComponent<Game.Net.Road>(fremd)
                            || !EntityManager.HasComponent<Edge>(fremd)) continue;
                        // Auch fremde PLT-Netze sind keine Stadtstrasse.
                        if (EntityManager.HasComponent<Owner>(fremd))
                        {
                            var o = EntityManager.GetComponentData<Owner>(fremd).m_Owner;
                            if (Lebt(o) && (EntityManager.HasComponent<ParkingLotCarrierReference>(o)
                                || EntityManager.HasComponent<ParkingLotPartRelation>(fremd))) continue;
                        }
                        if (gesehen.Add(fremd))
                            funde.Add((fremd, EntityManager.GetComponentData<Game.Net.Node>(knoten).m_Position));
                    }
                }
            }
            return funde;
        }

        private bool Strassenlage(Entity lot, Entity prefab, out Game.Objects.Transform lage)
        {
            lage = default;
            if (!EntityManager.HasBuffer<Game.Areas.Node>(lot)
                || !Lebt(prefab) || !EntityManager.HasComponent<BuildingData>(prefab)) return false;
            var lotSeite = AreaCenter(lot).xz;
            var tiefe = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize.y;
            foreach (var (strasse, anschluss) in Stadtstrassen(lot))
            {
                // Ein Netz in Initialisierung liefert noch keine fertigen Aussenkanten.
                if (!EntityManager.HasBuffer<ConnectedBuilding>(strasse)
                    || !EntityManager.HasComponent<Curve>(strasse)
                    || !EntityManager.HasComponent<EdgeGeometry>(strasse)
                    || !EntityManager.HasComponent<StartNodeGeometry>(strasse)
                    || !EntityManager.HasComponent<EndNodeGeometry>(strasse)) continue;
                if (EntityManager.HasComponent<Composition>(strasse))
                {
                    var p = EntityManager.GetComponentData<Composition>(strasse).m_Edge;
                    if (!Lebt(p) || !EntityManager.HasComponent<NetCompositionData>(p)) continue;
                    var flags = EntityManager.GetComponentData<NetCompositionData>(p).m_Flags.m_General;
                    if ((flags & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) continue;
                }
                var kurve = EntityManager.GetComponentData<Curve>(strasse).m_Bezier;
                MathUtils.Distance(kurve.xz, anschluss.xz, out var t);
                // Neben die Zufahrt, weg vom Kreuzungszentrum. Bei kurzen Kanten
                // bleibt der Punkt innerhalb der mittleren Haelfte.
                var randabstand = math.min(0.25f, 8f / math.max(1f, MathUtils.Length(kurve)));
                t = math.clamp(t, randabstand, 1f - randabstand);
                var achse = MathUtils.Position(kurve, t).xz;
                var geo = EntityManager.GetComponentData<EdgeGeometry>(strasse);
                var bester = float.MaxValue;
                var mitte = default(float2);
                var vorne = default(float2);
                foreach (var rand in new[] { geo.m_Start.m_Left, geo.m_Start.m_Right,
                    geo.m_End.m_Left, geo.m_End.m_Right })
                {
                    MathUtils.Distance(rand.xz, achse, out var r);
                    var punkt = MathUtils.Position(rand, r).xz;
                    if (!BegleiterAnbindung.Lage(punkt, achse, lotSeite, tiefe, out var m, out var v)) continue;
                    var d = math.distancesq(punkt, achse);
                    if (d >= bester) continue;
                    bester = d; mitte = m; vorne = v;
                }
                if (bester == float.MaxValue) continue;
                var position = new float3(mitte.x, 0f, mitte.y);
                var hoehe = _terrain.GetHeightData();
                position.y = Game.Simulation.TerrainUtils.SampleHeight(ref hoehe, position);
                lage = new Game.Objects.Transform(position,
                    quaternion.LookRotationSafe(new float3(vorne.x, 0f, vorne.y), math.up()));
                return true;
            }
            return false;
        }

        /** Begrenzte Anlaufpruefung: hoechstens 20 Versuche im Abstand von 30 Bildern. */
        private void PflegeAnschluesse(Dictionary<Entity, Entity> begleiterJeLot)
        {
            var bild = UnityEngine.Time.frameCount;
            foreach (var lot in new List<Entity>(_anschluesse.Keys))
            {
                var a = _anschluesse[lot];
                if (bild < a.NaechstePruefung) continue;
                a.NaechstePruefung = bild + 30;
                var traeger = Lebt(lot) && EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                    ? EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier : Entity.Null;
                if (!Lebt(lot) || !Lebt(traeger) || !Mod.WirtschaftAn)
                { AnschlussBeendet(lot, false, "Parkplatz/Traeger nicht mehr gueltig oder Wirtschaft aus"); continue; }
                begleiterJeLot.TryGetValue(lot, out var begleiter);
                if (HatStrasse(begleiter))
                { AnschlussBeendet(lot, true, "CS2 bestaetigt RoadEdge und ConnectedBuilding"); continue; }
                if (bild - a.Seit >= 600)
                { AnschlussBeendet(lot, false, "nach 600 Bildern keine bestaetigte Strassenanbindung"); continue; }
                // Kein Updated fuer das alte, unvollstaendige Building-Skelett.
                if (!Lebt(begleiter) || !EntityManager.HasComponent<Game.Objects.Object>(begleiter)
                    || !EntityManager.HasComponent<Game.Objects.Transform>(begleiter)
                    || !EntityManager.HasComponent<Building>(begleiter)
                    || !EntityManager.HasComponent<PrefabRef>(begleiter)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(begleiter).m_Prefab;
                if (a.Platziert && (a.Versuche >= 3 || bild < a.NaechstePlatzierung)) continue;
                if (!Strassenlage(lot, prefab, out var lage)) continue;
                EntityManager.SetComponentData(begleiter, lage);
                if (!EntityManager.HasComponent<Updated>(begleiter)) EntityManager.AddComponent<Updated>(begleiter);
                a.Platziert = true;
                a.Versuche++;
                a.NaechstePlatzierung = bild + 120;
                ParkingLotSchrittmarke.Setze($"Begleiteranschluss: Lot {lot.Index}, Begleiter {begleiter.Index}, Updated");
                Mod.log.Info($"PLT-Begleiteranschluss: Lot {lot.Index}, Begleiter {begleiter.Index} an Strassenrand {lage.m_Position}; wartet auf CS2.");
            }
        }

        private void AnschlussBeendet(Entity lot, bool ok, string grund)
        {
            var a = _anschluesse[lot];
            _anschluesse.Remove(lot);
            Mod.log.Info($"PLT-Begleiteranschluss: Lot {lot.Index}, Erfolg={ok}: {grund}.");
            if (a.Sync) World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(lot, lot, ok);
        }
    }
}
