using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Economy;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * POST-SONDE (Issue #15, 2026-10-12).
     *
     * Ein Spieler meldete: mit PLT schicken die Postaemter nach und nach
     * keine Wagen mehr ("Mail to deliver 0", gesammelte Post steigt, 0/20
     * Wagen) - ohne einen einzigen PLT-Parkplatz. Weder im Code noch im
     * Spiel liess sich eine Verbindung finden; bei uns fahren die Wagen.
     * Diese Sonde misst beim Spieler, an welcher Bedingung der Versand
     * haengt (Ablauf: `PostFacilityAISystem` -> Flags/Ressourcen/Wagen,
     * `PostVanDispatchSystem` -> Anfragen mit Fehlerzaehler, Briefkaesten).
     *
     * NUR LESEN. Schreibt alle 30 s einen Schnappschuss in eine eigene,
     * kleine Datei; keine Spielstanddaten, keine Namen, keine Pfade ausser
     * der eigenen Datei. Hoert nach 3 MB oder 3 Stunden von selbst auf.
     */
    public sealed partial class ParkingLotPostsondeSystem : GameSystemBase
    {
        private const string DateiName = "ParkingLotTool-post.log";
        private const float AbstandSekunden = 30f;
        private const float HoechstdauerSekunden = 3 * 60 * 60;
        private const long HoechstBytes = 3L * 1024 * 1024;

        private static StreamWriter _schreiber;
        private static float _start, _naechste;
        internal static bool Aktiv { get; private set; }

        internal static string Pfad
        {
            get
            {
                var ordner = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(ordner);
                return Path.Combine(ordner, DateiName);
            }
        }

        private EntityQuery _aemter, _wagenAnfragen, _transfers, _briefkaesten, _wagen, _wagenPrefabs, _lots;
        private PrefabSystem _prefabs;
        private SimulationSystem _simulation;

        internal static void Schalte(bool an)
        {
            if (an == Aktiv) return;
            if (an)
            {
                try
                {
                    _schreiber = new StreamWriter(new FileStream(Pfad, FileMode.Create,
                        FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                    Aktiv = true;
                    _start = UnityEngine.Time.realtimeSinceStartup;
                    _naechste = _start;
                    _schreiber.WriteLine("PLT post probe " + typeof(Mod).Assembly.GetName().Version
                        + "  started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    Mod.log.Info("PLT-Postsonde: an, schreibt nach " + Pfad);
                }
                catch (Exception x)
                {
                    Aktiv = false;
                    _schreiber = null;
                    Mod.log.Warn("PLT-Postsonde: Datei nicht zu oeffnen - " + x.Message);
                }
                return;
            }
            Aus("stopped");
        }

        private static void Aus(string grund)
        {
            Aktiv = false;
            try
            {
                _schreiber?.WriteLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + grund);
                _schreiber?.Dispose();
            }
            catch (Exception x) { Mod.log.Warn("PLT-Postsonde: Schliessen fehlgeschlagen - " + x.Message); }
            _schreiber = null;
            Mod.log.Info("PLT-Postsonde: aus (" + grund + ").");
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            var ohne = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() };
            _aemter = GetEntityQuery(new EntityQueryDesc
            { All = new[] { ComponentType.ReadOnly<Game.Buildings.PostFacility>() }, None = ohne });
            _wagenAnfragen = GetEntityQuery(ComponentType.ReadOnly<PostVanRequest>(), ComponentType.ReadOnly<ServiceRequest>());
            _transfers = GetEntityQuery(ComponentType.ReadOnly<MailTransferRequest>(), ComponentType.ReadOnly<ServiceRequest>());
            _briefkaesten = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Routes.MailBox>() },
                None = new[] { ComponentType.ReadOnly<Game.Buildings.PostFacility>(),
                    ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _wagen = GetEntityQuery(new EntityQueryDesc
            { All = new[] { ComponentType.ReadOnly<Game.Vehicles.PostVan>() }, None = ohne });
            _wagenPrefabs = GetEntityQuery(ComponentType.ReadOnly<PostVanData>(), ComponentType.ReadOnly<PrefabData>());
            _lots = GetEntityQuery(ComponentType.ReadOnly<ParkingLotCarrierReference>());
        }

        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            if (Aktiv) Zeile("--- loading a save ---");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!Aktiv) return;
            var spiel = Game.SceneFlow.GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            var jetzt = UnityEngine.Time.realtimeSinceStartup;
            if (jetzt < _naechste) return;
            _naechste = jetzt + AbstandSekunden;
            if (jetzt - _start > HoechstdauerSekunden) { Aus("stopped automatically after 3 hours"); return; }
            try
            {
                if (_schreiber != null && _schreiber.BaseStream.Length > HoechstBytes)
                { Aus("stopped automatically at 3 MB"); return; }
                Schnappschuss();
            }
            catch (Exception x)
            {
                Mod.log.Warn("PLT-Postsonde: Schnappschuss fehlgeschlagen - " + x);
                Zeile("snapshot failed: " + x.GetType().Name + ": " + x.Message);
            }
        }

        private static void Zeile(string text)
        {
            if (_schreiber == null) return;
            try { _schreiber.WriteLine(text); } catch { }
        }

        private void Schnappschuss()
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
              .Append("  frame ").Append(_simulation.frameIndex)
              .Append("  PLT lots ").Append(_lots.CalculateEntityCount())
              .Append("  van prefabs ").Append(_wagenPrefabs.CalculateEntityCount());
            Zeile(sb.ToString());

            // Anfragen: wie viele, wie viele scheitern wiederholt (FailCount/Cooldown).
            Zeile("  van requests " + Anfragen(_wagenAnfragen) + "  |  transfer requests " + Anfragen(_transfers));

            // Briefkaesten.
            using (var kaesten = _briefkaesten.ToComponentDataArray<Game.Routes.MailBox>(Allocator.Temp))
            {
                int mitAnfrage = 0, post = 0, maxPost = 0;
                foreach (var k in kaesten)
                {
                    if (k.m_CollectRequest != Entity.Null) mitAnfrage++;
                    post += k.m_MailAmount;
                    maxPost = Math.Max(maxPost, k.m_MailAmount);
                }
                Zeile($"  mailboxes {kaesten.Length}  with collect request {mitAnfrage}  mail total {post}  max {maxPost}");
            }

            // Wagen nach Zustand.
            using (var wagen = _wagen.ToComponentDataArray<Game.Vehicles.PostVan>(Allocator.Temp))
            {
                var zustaende = new SortedDictionary<string, int>();
                foreach (var w in wagen)
                {
                    var name = w.m_State == 0 ? "none" : w.m_State.ToString();
                    zustaende[name] = zustaende.TryGetValue(name, out var n) ? n + 1 : 1;
                }
                var teile = new List<string>();
                foreach (var z in zustaende) teile.Add(z.Key + " " + z.Value);
                Zeile($"  post vans {wagen.Length}" + (teile.Count > 0 ? "  [" + string.Join(", ", teile) + "]" : ""));
            }

            // Je Amt.
            using var aemter = _aemter.ToEntityArray(Allocator.Temp);
            foreach (var amt in aemter) Zeile("  " + Amt(amt));
        }

        private string Anfragen(EntityQuery abfrage)
        {
            using var anfragen = abfrage.ToComponentDataArray<ServiceRequest>(Allocator.Temp);
            int gescheitert = 0, wartet = 0, maxFehler = 0;
            foreach (var a in anfragen)
            {
                if (a.m_FailCount > 0) gescheitert++;
                if (a.m_Cooldown > 0) wartet++;
                maxFehler = Math.Max(maxFehler, a.m_FailCount);
            }
            return $"{anfragen.Length} (failed {gescheitert}, cooling down {wartet}, max fails {maxFehler})";
        }

        private string Amt(Entity amt)
        {
            var em = EntityManager;
            var pf = em.GetComponentData<Game.Buildings.PostFacility>(amt);
            var sb = new StringBuilder();
            sb.Append("office ").Append(amt.Index).Append(' ').Append(PrefabName(amt));
            if (em.HasComponent<Owner>(amt)) sb.Append(" (upgrade of ").Append(em.GetComponentData<Owner>(amt).m_Owner.Index).Append(')');
            sb.Append(" | flags ").Append(pf.m_Flags);
            if (em.HasBuffer<Efficiency>(amt))
            {
                var eff = em.GetBuffer<Efficiency>(amt, true);
                sb.Append(" | efficiency ").Append(BuildingUtils.GetEfficiency(eff).ToString("0.00", CultureInfo.InvariantCulture));
                for (var i = 0; i < eff.Length; i++)
                    if (eff[i].m_Efficiency < 0.999f)
                        sb.Append(' ').Append(eff[i].m_Factor).Append('=')
                          .Append(eff[i].m_Efficiency.ToString("0.00", CultureInfo.InvariantCulture));
            }
            if (em.HasBuffer<Game.Economy.Resources>(amt))
            {
                var res = em.GetBuffer<Game.Economy.Resources>(amt, true);
                sb.Append(" | local ").Append(EconomyUtils.GetResources(Resource.LocalMail, res))
                  .Append(" unsorted ").Append(EconomyUtils.GetResources(Resource.UnsortedMail, res))
                  .Append(" outgoing ").Append(EconomyUtils.GetResources(Resource.OutgoingMail, res));
            }
            if (em.HasComponent<Game.Routes.MailBox>(amt)) sb.Append(" box ").Append(em.GetComponentData<Game.Routes.MailBox>(amt).m_MailAmount);
            if (em.HasBuffer<OwnedVehicle>(amt))
            {
                var eigene = em.GetBuffer<OwnedVehicle>(amt, true);
                int lebend = 0, vans = 0;
                for (var i = 0; i < eigene.Length; i++)
                {
                    var v = eigene[i].m_Vehicle;
                    if (!em.Exists(v) || em.HasComponent<Deleted>(v)) continue;
                    lebend++;
                    if (em.HasComponent<Game.Vehicles.PostVan>(v)) vans++;
                }
                sb.Append(" | vehicles ").Append(eigene.Length).Append(" (alive ").Append(lebend).Append(", vans ").Append(vans).Append(')');
            }
            if (em.HasBuffer<ServiceDispatch>(amt)) sb.Append(" | dispatches ").Append(em.GetBuffer<ServiceDispatch>(amt, true).Length);
            sb.Append(" | target ").Append(Anfrage(pf.m_TargetRequest))
              .Append(" | deliver ").Append(Anfrage(pf.m_MailDeliverRequest))
              .Append(" | receive ").Append(Anfrage(pf.m_MailReceiveRequest));
            return sb.ToString();
        }

        private string Anfrage(Entity e)
        {
            if (e == Entity.Null) return "-";
            var em = EntityManager;
            if (!em.Exists(e)) return "dead";
            var sb = new StringBuilder();
            sb.Append(e.Index);
            if (em.HasComponent<ServiceRequest>(e))
            {
                var r = em.GetComponentData<ServiceRequest>(e);
                sb.Append(" fails ").Append(r.m_FailCount).Append(" cooldown ").Append(r.m_Cooldown).Append(' ').Append(r.m_Flags);
            }
            if (em.HasComponent<PostVanRequest>(e)) sb.Append(" van ").Append(em.GetComponentData<PostVanRequest>(e).m_Flags);
            if (em.HasComponent<MailTransferRequest>(e))
            {
                var t = em.GetComponentData<MailTransferRequest>(e);
                sb.Append(" transfer ").Append(t.m_Flags).Append(" amount ").Append(t.m_Amount);
            }
            if (em.HasComponent<PathInformation>(e)) sb.Append(" path ").Append(em.GetComponentData<PathInformation>(e).m_State);
            if (em.HasComponent<Dispatched>(e)) sb.Append(" dispatched to ").Append(em.GetComponentData<Dispatched>(e).m_Handler.Index);
            return sb.ToString();
        }

        private string PrefabName(Entity e)
        {
            if (!EntityManager.HasComponent<PrefabRef>(e)) return "?";
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            return _prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null ? p.name : "?";
        }
    }
}
