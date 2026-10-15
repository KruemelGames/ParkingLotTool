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
     * 2026-10-15, zweite Stufe: Der erste Lauf zeigte alle Transfer-
     * Anfragen der Aemter bei EINEM Lieferanten wartend (Entity 950404),
     * ohne Fehler und ohne Fortschritt. `ValidateHandler` prueft nur, ob
     * die Anfrage in dessen `TripNeeded`/`ServiceDispatch` steht, nicht ob
     * je ein Lkw kommt. Deshalb schreibt die Sonde jetzt je Lieferant
     * Art, Warteschlange, Fahrzeuggrenze und eigene Fahrzeuge, dazu die
     * Post-Lkw unterwegs und einmal die PLT-Reste im Spielstand (die
     * Lot-Zaehlung sieht nur Parkplaetze mit Traeger-Verweis).
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
        private static bool _resteGeschrieben;
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
        private EntityQuery _lkw, _mitPrefab;
        private const Resource Post = Resource.LocalMail | Resource.UnsortedMail | Resource.OutgoingMail;
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
                    _resteGeschrieben = false;
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
            _lkw = GetEntityQuery(new EntityQueryDesc
            { All = new[] { ComponentType.ReadOnly<Game.Vehicles.DeliveryTruck>() }, None = ohne });
            _mitPrefab = GetEntityQuery(new EntityQueryDesc
            { All = new[] { ComponentType.ReadOnly<PrefabRef>() }, None = ohne });
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

            if (!_resteGeschrieben) { _resteGeschrieben = true; Zeile("  " + PltReste()); }
            Zeile("  " + PostLkw());

            // Lieferanten: jeder Handler, bei dem eine Transfer-Anfrage steht.
            var lieferanten = new Dictionary<Entity, List<Entity>>();
            using (var anfragen = _transfers.ToEntityArray(Allocator.Temp))
            {
                foreach (var a in anfragen)
                {
                    if (!EntityManager.HasComponent<Dispatched>(a)) continue;
                    var h = EntityManager.GetComponentData<Dispatched>(a).m_Handler;
                    if (!lieferanten.TryGetValue(h, out var liste)) lieferanten[h] = liste = new List<Entity>();
                    liste.Add(a);
                }
            }
            foreach (var l in lieferanten) Zeile("  " + Lieferant(l.Key, l.Value));
        }

        /** Ein Lieferant: was er ist, ob TripNeededSystem ihn bearbeitet, Schlange, Fahrzeuge. */
        private string Lieferant(Entity h, List<Entity> anfragen)
        {
            var em = EntityManager;
            var sb = new StringBuilder();
            sb.Append("supplier ").Append(h.Index).Append(':').Append(h.Version);
            if (!em.Exists(h)) return sb.Append(" dead | post requests here ").Append(anfragen.Count).ToString();
            sb.Append(' ').Append(PrefabName(h));
            if (em.HasComponent<Game.Objects.OutsideConnection>(h)) sb.Append(" outside-connection");
            if (em.HasComponent<Game.Companies.StorageCompany>(h)) sb.Append(" storage");
            if (em.HasComponent<Game.Buildings.PostFacility>(h)) sb.Append(" post-facility");
            if (em.HasComponent<Deleted>(h)) sb.Append(" DELETED");
            // Abfrage von TripNeededSystem: TripNeeded, PrefabRef, Resources, OwnedVehicle.
            var fehlt = new List<string>();
            if (!em.HasBuffer<Game.Citizens.TripNeeded>(h)) fehlt.Add("TripNeeded");
            if (!em.HasBuffer<Game.Economy.Resources>(h)) fehlt.Add("Resources");
            if (!em.HasBuffer<OwnedVehicle>(h)) fehlt.Add("OwnedVehicle");
            sb.Append(fehlt.Count == 0 ? " | trip system ok" : " | trip system MISSING " + string.Join("+", fehlt));
            var ort = h;
            if (em.HasComponent<PropertyRenter>(h))
            {
                ort = em.GetComponentData<PropertyRenter>(h).m_Property;
                sb.Append(" | property ").Append(ort.Index).Append(' ').Append(PrefabName(ort));
            }
            sb.Append(em.HasComponent<Game.Objects.Transform>(ort) ? " transform" : " NO transform");
            if (em.HasBuffer<Game.Economy.Resources>(h))
            {
                var res = em.GetBuffer<Game.Economy.Resources>(h, true);
                sb.Append(" | local ").Append(EconomyUtils.GetResources(Resource.LocalMail, res))
                  .Append(" unsorted ").Append(EconomyUtils.GetResources(Resource.UnsortedMail, res));
            }
            if (em.HasComponent<PrefabRef>(h))
            {
                var prefab = em.GetComponentData<PrefabRef>(h).m_Prefab;
                if (em.HasComponent<Game.Companies.TransportCompanyData>(prefab))
                    sb.Append(" | max trucks (prefab) ").Append(em.GetComponentData<Game.Companies.TransportCompanyData>(prefab).m_MaxTransports);
                else sb.Append(" | no truck limit");
            }
            if (em.HasBuffer<Efficiency>(h))
                sb.Append(" efficiency ").Append(BuildingUtils.GetEfficiency(em.GetBuffer<Efficiency>(h, true)).ToString("0.00", CultureInfo.InvariantCulture));
            if (em.HasBuffer<OwnedVehicle>(h))
            {
                var eigene = em.GetBuffer<OwnedVehicle>(h, true);
                int tot = 0, postLkw = 0, wartend = 0;
                for (var i = 0; i < eigene.Length; i++)
                {
                    var v = eigene[i].m_Vehicle;
                    if (!em.Exists(v) || em.HasComponent<Deleted>(v)) { tot++; continue; }
                    if (IstPostLkw(v)) postLkw++;
                    if (em.HasComponent<Game.Objects.Unspawned>(v)) wartend++;
                }
                sb.Append(" | vehicles ").Append(eigene.Length).Append(" (dead ").Append(tot)
                  .Append(", mail trucks ").Append(postLkw).Append(", unspawned ").Append(wartend).Append(')');
            }
            if (em.HasBuffer<Game.Citizens.TripNeeded>(h))
            {
                var fahrten = em.GetBuffer<Game.Citizens.TripNeeded>(h, true);
                sb.Append(" | queue ").Append(fahrten.Length).Append(" [");
                var zwecke = new SortedDictionary<string, int>();
                var plaetze = new List<int>();
                for (var i = 0; i < fahrten.Length; i++)
                {
                    var f = fahrten[i];
                    var k = f.m_Purpose + "/" + f.m_Resource;
                    zwecke[k] = zwecke.TryGetValue(k, out var n) ? n + 1 : 1;
                    if (anfragen.Contains(f.m_TargetAgent)) plaetze.Add(i);
                }
                var teile = new List<string>();
                foreach (var z in zwecke) teile.Add(z.Key + " " + z.Value);
                sb.Append(string.Join(", ", teile)).Append("] post requests at ")
                  .Append(plaetze.Count == 0 ? "-" : string.Join(",", plaetze));
            }
            if (em.HasBuffer<ServiceDispatch>(h)) sb.Append(" | dispatches ").Append(em.GetBuffer<ServiceDispatch>(h, true).Length);
            sb.Append(" | post requests here ").Append(anfragen.Count);
            return sb.ToString();
        }

        private bool IstPostLkw(Entity v)
        {
            var em = EntityManager;
            if (!em.HasComponent<Game.Vehicles.DeliveryTruck>(v)) return false;
            if ((em.GetComponentData<Game.Vehicles.DeliveryTruck>(v).m_Resource & Post) != 0) return true;
            return em.HasComponent<ReturnLoad>(v) && (em.GetComponentData<ReturnLoad>(v).m_Resource & Post) != 0;
        }

        /** Alle Post-Lkw unterwegs: wie viele warten auf den Start, haengen oder fanden keinen Weg. */
        private string PostLkw()
        {
            var em = EntityManager;
            int n = 0, wartend = 0, fehlgeschlagen = 0, steckt = 0;
            var besitzer = new Dictionary<int, int>();
            using var lkw = _lkw.ToEntityArray(Allocator.Temp);
            foreach (var v in lkw)
            {
                if (!IstPostLkw(v)) continue;
                n++;
                if (em.HasComponent<Game.Objects.Unspawned>(v)) wartend++;
                if (em.HasComponent<PathOwner>(v))
                {
                    var p = em.GetComponentData<PathOwner>(v).m_State;
                    if ((p & PathFlags.Failed) != 0) fehlgeschlagen++;
                    if ((p & PathFlags.Stuck) != 0) steckt++;
                }
                if (em.HasComponent<Owner>(v))
                {
                    var o = em.GetComponentData<Owner>(v).m_Owner.Index;
                    besitzer[o] = besitzer.TryGetValue(o, out var c) ? c + 1 : 1;
                }
            }
            var teile = new List<string>();
            foreach (var b in besitzer) teile.Add(b.Key + " " + b.Value);
            return "mail trucks " + n + " (unspawned " + wartend + ", path failed " + fehlgeschlagen + ", stuck " + steckt + ")"
                + (teile.Count > 0 ? "  owners [" + string.Join(", ", teile) + "]" : "");
        }

        /** Einmal je Lauf: Objekte aus PLT-Prefabs im Spielstand, auch verwaiste. */
        private string PltReste()
        {
            var je = new Dictionary<Entity, int>();
            using (var refs = _mitPrefab.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                foreach (var r in refs) je[r.m_Prefab] = je.TryGetValue(r.m_Prefab, out var c) ? c + 1 : 1;
            var teile = new List<string>();
            foreach (var p in je)
            {
                if (!_prefabs.TryGetPrefab<PrefabBase>(p.Key, out var pb) || pb == null) continue;
                if (pb.name.StartsWith("PLT", StringComparison.Ordinal)) teile.Add(pb.name + " " + p.Value);
            }
            return "PLT objects in save: " + (teile.Count == 0 ? "none" : string.Join(", ", teile));
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
