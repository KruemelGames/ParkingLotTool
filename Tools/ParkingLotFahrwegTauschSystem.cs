using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /*
     * STEUERUNG DES FAHRWEGTAUSCHS (Sync-Schritt 9).
     *
     * Wunsch des Nutzers (2026-10-04): der Sync schliesst das Panel selbst,
     * zeigt seinen Fortschritt, tauscht, und oeffnet das Panel danach
     * wieder. Deshalb wird kurz das Tauschwerkzeug aktiv (das schliesst ein
     * offenes PLT-Panel regulaer) und danach das vorige Werkzeug wieder.
     * Gestartet wird nur, wenn das PLT-Werkzeug gerade nichts zeichnet oder
     * bearbeitet - ein Werkzeugwechsel wuerde das sonst verwerfen.
     */
    public sealed partial class ParkingLotFahrwegTauschSystem : GameSystemBase
    {
        private readonly List<Entity> _warteschlange = new();
        private readonly Dictionary<Entity, int> _wartetSeit = new();
        private readonly Dictionary<Entity, Auftragskennung> _auftraege = new();
        private ToolSystem _toolSystem;
        private ParkingLotFahrwegTauschWerkzeug _werkzeug;
        private ParkingLotToolSystem _plt;
        private PrefabSystem _prefabs;
        private ToolBaseSystem _vorher;
        private bool _alleOk = true;
        private int _ruheBis;

        internal int Offen => _warteschlange.Count + (_werkzeug != null && _werkzeug.Beschaeftigt ? 1 : 0);

        /** Nur ein laufender Tausch im Werkzeug sperrt den Bestandstausch (1.0.6, siehe dort). */
        internal bool WerkzeugLaeuft => _werkzeug != null && _werkzeug.Beschaeftigt;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _werkzeug = World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschWerkzeug>();
            _plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        internal bool Einreihen(Entity lot, Auftragskennung id)
        {
            if (_auftraege.ContainsKey(lot)) return false;
            if (!World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().Annehmen(id, lot, Auftragsarbeiter.Fahrweg)) return false;
            _auftraege.Add(lot, id);
            _warteschlange.Add(lot);
            _wartetSeit[lot] = UnityEngine.Time.frameCount;
            return true;
        }

        /** Die alten Vanilla-Fahrwege eines Lots samt Ziel-Klon und Besitzer. */
        internal List<(Entity Kante, Entity Ziel, Entity Besitzer)> Plan(Entity lot)
        {
            var plan = new List<(Entity, Entity, Entity)>();
            var fahr = World.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>();
            foreach (var (kante, name, besitzer) in AlteFahrwege(lot))
            {
                // Ein fehlender Klon bedeutet "noch nicht bereit", niemals
                // "nichts zu tun". Auch keine halbe Migration uebernehmen.
                if (!fahr.TryWeg(name, out var ziel) || !EntityManager.Exists(ziel)) return null;
                plan.Add((kante, ziel, besitzer));
            }
            return plan;
        }

        private List<(Entity Kante, string Name, Entity Besitzer)> AlteFahrwege(Entity lot)
        {
            var plan = new List<(Entity, string, Entity)>();
            if (!EntityManager.Exists(lot) || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return plan;
            if (EntityManager.HasComponent<Deleted>(lot)) return plan;
            var gesehen = new HashSet<Entity>();
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            foreach (var besitzer in new[] { lot, traeger })
            {
                if (!EntityManager.Exists(besitzer) || !EntityManager.HasBuffer<Game.Net.SubNet>(besitzer)) continue;
                var netze = EntityManager.GetBuffer<Game.Net.SubNet>(besitzer, true);
                for (var i = 0; i < netze.Length; i++)
                {
                    var e = netze[i].m_SubNet;
                    if (!gesehen.Add(e) || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                        || EntityManager.HasComponent<Temp>(e) || !EntityManager.HasComponent<Edge>(e)
                        || !EntityManager.HasComponent<PrefabRef>(e) || !EntityManager.HasComponent<Owner>(e)) continue;
                    var pf = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                    if (owner != lot && owner != traeger) continue;
                    if (!EntityManager.Exists(pf) || !_prefabs.TryGetPrefab<PrefabBase>(pf, out var value)
                        || value == null || !ParkingLotFahrregeln.IstFahrweg(value.name)) continue;
                    plan.Add((e, value.name, owner));
                }
            }
            return plan;
        }

        internal bool BrauchtTausch(Entity lot) => AlteFahrwege(lot).Count > 0;

        /** Beim Laden alles vergessen (1.0.6): Entities der alten Welt, haengender Tausch. */
        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            if (_warteschlange.Count > 0 || _werkzeug.Beschaeftigt)
                Mod.log.Info($"PLT-Fahrwegtausch: Laden - {_warteschlange.Count} eingereiht, Werkzeug "
                    + $"{(_werkzeug.Beschaeftigt ? "beschaeftigt" : "frei")} verworfen.");
            _warteschlange.Clear();
            _wartetSeit.Clear();
            _auftraege.Clear();
            _vorher = null;
            _alleOk = true;
            _ruheBis = 0;
            _werkzeug.Zuruecksetzen();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            if (_werkzeug.Beschaeftigt) { _werkzeug.PflegeOhneWerkzeug(); return; }
            if (_warteschlange.Count == 0) return;
            // Nie zwei Tauschwerkzeuge gleichzeitig: jedes setzt das aktive Werkzeug.
            if (World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().WerkzeugLaeuft) return;
            if (World.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>().Laeuft) return;
            if (UnityEngine.Time.frameCount < _ruheBis) return;
            // Ein Werkzeugwechsel wuerde einen laufenden Entwurf verwerfen.
            if (_toolSystem.activeTool == _plt && _plt.ArbeitetGerade) return;
            var lot = _warteschlange[0];
            var id = _auftraege[lot];
            var auftraege = World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
            if (!auftraege.Gilt(id))
            {
                _warteschlange.RemoveAt(0); _wartetSeit.Remove(lot); _auftraege.Remove(lot);
                return;
            }
            List<(Entity Kante, Entity Ziel, Entity Besitzer)> plan = null;
            System.Exception planFehler = null;
            try { plan = Plan(lot); }
            catch (System.Exception x) { planFehler = x; }
            if (planFehler != null)
            {
                _warteschlange.RemoveAt(0); _wartetSeit.Remove(lot);
                auftraege.Starte(id);
                Fertig(id, lot, false, "Planung fehlgeschlagen: " + planFehler.Message);
                return;
            }
            if (plan == null)
            {
                if (UnityEngine.Time.frameCount - _wartetSeit[lot] < 600) return;
                _warteschlange.RemoveAt(0); _wartetSeit.Remove(lot);
                auftraege.Starte(id);
                Fertig(id, lot, false, "Ziel-Prefab nach 600 Bildern nicht bereit; kein Weg ersetzt");
                return;
            }
            _warteschlange.RemoveAt(0); _wartetSeit.Remove(lot);
            if (!auftraege.Starte(id)) return;
            if (plan.Count == 0) { Fertig(id, lot, true, "nichts zu tauschen"); return; }
            if (_toolSystem.activeTool != _werkzeug) _vorher = _toolSystem.activeTool;
            try
            {
                _werkzeug.Fertig = (e, ok, text) => Fertig(id, e, ok, text);
                _werkzeug.Starte(lot, plan, id);
                _toolSystem.activeTool = _werkzeug;
            }
            catch (System.Exception x)
            {
                _werkzeug.Zuruecksetzen();
                Fertig(id, lot, false, "Uebergabe fehlgeschlagen: " + x.Message);
            }
        }

        private void Fertig(Auftragskennung id, Entity lot, bool ok, string text)
        {
            var auftraege = World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
            if (!_auftraege.TryGetValue(lot, out var erwartet) || erwartet != id || !auftraege.DarfAnwenden(id)) return;
            _auftraege.Remove(lot);
            try { Mod.log.Info($"PLT-Fahrwegtausch: Lot {lot.Index}, Erfolg={ok}: {text}."); }
            catch (System.Exception) { }
            if (!ok) _alleOk = false;
            try { auftraege.Beende(id, lot, ok); }
            finally { WerkzeugFreigeben(); }
        }

        private void WerkzeugFreigeben()
        {
            _ruheBis = UnityEngine.Time.frameCount + 10;
            if (_warteschlange.Count > 0) return;
            // Alles erledigt: voriges Werkzeug zurueck (oeffnet ein vorher
            // offenes PLT-Panel wieder). Nach einem Fehlschlag bleibt das
            // Panel zu, damit die Meldung sichtbar bleibt.
            // Nie zurueck auf ein Tauschwerkzeug: das stuende danach leer und aktiv.
            var zurueck = _alleOk && _vorher != null && _vorher != _werkzeug
                && !(_vorher is ParkingLotBestandsTauschWerkzeug) ? _vorher : null;
            if (_toolSystem.activeTool == _werkzeug)
                _toolSystem.activeTool = zurueck ?? World.GetOrCreateSystemManaged<DefaultToolSystem>();
            _vorher = null;
            _alleOk = true;
        }
    }

    public sealed partial class ParkingLotToolSystem
    {
        /** Zeichnet oder bearbeitet das Werkzeug gerade? Dann darf kein Sync-Tausch das Werkzeug wechseln. */
        internal bool ArbeitetGerade => IsEditing || _pendingEditLot != Entity.Null || _points.Count > 0
            || _buildStage != BuildStage.Idle || _buildTask != null;
    }
}
