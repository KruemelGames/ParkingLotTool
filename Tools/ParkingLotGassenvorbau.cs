using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /*
     * GASSEN VORBAUEN, DIE EINE STADTSTRASSE TEILEN (28.10.2026).
     *
     * "Parking Lot (192 spaces)" stand seit Anfang Oktober ohne Wegenetz da.
     * Reparieren ist der Hintergrund-Neubau, und der darf keine Stadtstrasse
     * teilen. Die Gasse bei 69,41 m braucht aber genau das, denn ihr Knoten
     * an der Small Road ist mit dem Netz verschwunden. Am 28.10. scheiterte
     * der Neubau deshalb dreimal mit 47 von 48 Kursen.
     *
     * Dieses Werkzeug baut nur diese Gassen, so wie das Edit sie baut:
     * Entwurf (Temp) mit Kante und Teilungsstelle, CS2 teilt die Strasse in
     * der Vorschau, der Parkplatz wird Besitzer, dann Anwenden. Danach liegt
     * an der Strasse ein Knoten, der Hintergrund-Neubau findet die Gasse als
     * unveraenderte Gasse (FindeNetzerhalt) und baut den Rest.
     *
     * Ein Entwurf aendert NICHTS, bis er angewendet wird. Wechselt der
     * Spieler mittendrin das Werkzeug, verfaellt er - kein Abriss, kein
     * Rueckweg. Muster: ParkingLotFahrwegTauschWerkzeug.
     */
    public sealed partial class ParkingLotGassenvorbauWerkzeug : ToolBaseSystem
    {
        public const string Id = "PLT Gassenvorbau";
        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        private enum Stufe { Frei, Anlegen, Warten, Pruefen }

        private Stufe _stufe;
        private Entity _lot, _traeger;
        private readonly List<ParkingLotToolSystem.Vorbaugasse> _kurse = new();
        private readonly List<Entity> _definitionen = new();
        private readonly HashSet<Entity> _prefabs = new();
        /** Innere Gassenenden: dort haengen die Wege, der gemeinsame Knoten bekommt Elevation 0. */
        private readonly List<float2> _innen = new();
        private int _seit;
        private bool _lief, _befundGemeldet;
        private EntityQuery _fremdeDefinitionen, _alteDefinitionen, _temps, _tempNetze, _kanten;
        internal Action<Entity, bool, string> Fertig;

        internal bool Beschaeftigt => _stufe != Stufe.Frei;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _fremdeDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
            _alteDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.Exclude<Updated>(), ComponentType.Exclude<ParkingLotAuftragsdefinition>());
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
            // Dieselbe Auswahl wie ParkingLotLotOwner `_tempNetQuery`.
            _tempNetze = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Game.Net.Node>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            _kanten = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>());
        }

        internal void Starte(Entity lot, List<ParkingLotToolSystem.Vorbaugasse> kurse)
        {
            _lot = lot;
            _traeger = EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                ? EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier : Entity.Null;
            _kurse.Clear();
            _kurse.AddRange(kurse);
            _prefabs.Clear();
            _innen.Clear();
            foreach (var k in kurse)
            {
                _prefabs.Add(k.Prefab);
                if (k.Gasse) _innen.Add((k.StadtAmAnfang ? k.Nach : k.Von).xz);
            }
            _definitionen.Clear();
            _stufe = Stufe.Anlegen;
            _seit = UnityEngine.Time.frameCount;
            _lief = false;
            _befundGemeldet = false;
        }

        /** Ein abgeschaltetes Werkzeug bekommt kein OnUpdate (1.0.6): vor dem Apply verwerfen, danach pruefen. */
        internal void PflegeOhneWerkzeug()
        {
            if (_stufe == Stufe.Frei || m_ToolSystem.activeTool == this) return;
            var bild = UnityEngine.Time.frameCount;
            if (_stufe == Stufe.Pruefen)
            {
                if (bild - _seit < 3) return;
                PruefeUndBeende();
                return;
            }
            if (!_lief && bild - _seit < 10) return;
            Beende(false, _lief ? "Werkzeug gewechselt, Entwurf verworfen" : "Werkzeug wurde nicht aktiv, Entwurf verworfen");
        }

        /** Beim Laden: alles vergessen, ohne Rueckmeldung. */
        internal void Zuruecksetzen()
        {
            _definitionen.Clear();
            _kurse.Clear();
            _prefabs.Clear();
            _innen.Clear();
            _stufe = Stufe.Frei;
            _lot = _traeger = Entity.Null;
            _lief = false;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            if (m_ToolSystem.activeTool != this) return inputDeps;
            var bild = UnityEngine.Time.frameCount;
            if (_stufe != Stufe.Frei && _stufe != Stufe.Pruefen && !BestandGueltig())
            {
                applyMode = ApplyMode.Clear;
                Beende(false, "Parkplatz oder Stadtstrasse verschwunden; nichts angewendet");
                return inputDeps;
            }
            if (_stufe != Stufe.Frei) _lief = true;
            switch (_stufe)
            {
                case Stufe.Anlegen:
                    // Nie zusammen mit einem fremden Entwurf anwenden.
                    if (_fremdeDefinitionen.CalculateEntityCount() > 0 || _temps.CalculateEntityCount() > 0)
                    {
                        RaeumeFremdeEntwuerfe();
                        applyMode = ApplyMode.Clear;
                        if (bild - _seit > 60 && !_befundGemeldet)
                        {
                            _befundGemeldet = true;
                            Mod.log.Warn("PLT-Gassenvorbau: wartet seit " + (bild - _seit) + " Bildern auf fremde Entwuerfe - "
                                + ParkingLotEntwurfsbefund.Beschreibe(EntityManager, World.GetOrCreateSystemManaged<PrefabSystem>()));
                        }
                        if (bild - _seit > 600) Beende(false, "fremde Entwuerfe blieben 600 Bilder");
                        break;
                    }
                    LegeDefinitionenAn();
                    _stufe = Stufe.Warten;
                    _seit = bild;
                    break;
                case Stufe.Warten:
                    {
                        var eigene = ZaehleEigeneTempKanten();
                        if (eigene >= _kurse.Count)
                        {
                            var gebunden = BindeAnParkplatz();
                            var geschuetzt = SchuetzeGassenknoten();
                            ParkingLotSchrittmarke.Aenderung("Gassenvorbau: Lot " + _lot.Index + " wird angewendet (Apply)");
                            Mod.log.Info($"PLT-Gassenvorbau: Lot {_lot.Index}: {eigene} Vorschau-Kante(n), {gebunden} Teile "
                                + $"an den Parkplatz gebunden, {geschuetzt}/{_innen.Count} innere Gassenknoten mit Elevation 0, wird angewendet.");
                            applyMode = ApplyMode.Apply;
                            _stufe = Stufe.Pruefen;
                            _seit = bild;
                            break;
                        }
                        if (bild - _seit > 30)
                        {
                            applyMode = ApplyMode.Clear;
                            Beende(false, $"nur {eigene}/{_kurse.Count} Gassen-Vorschauen entstanden; nichts angewendet");
                        }
                        break;
                    }
                case Stufe.Pruefen:
                    if (bild - _seit < 3) break;
                    PruefeUndBeende();
                    break;
            }
            return inputDeps;
        }

        private bool BestandGueltig()
        {
            if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, _lot, _traeger)) return false;
            if (_stufe != Stufe.Anlegen) return true;
            foreach (var k in _kurse)
            {
                // Nur Gassen teilen eine Stadtstrasse; Wege docken hoechstens an einen Gassenknoten.
                if (k.Gasse)
                {
                    if (!EntityManager.Exists(k.Kante) || EntityManager.HasComponent<Deleted>(k.Kante)
                        || !EntityManager.HasComponent<Edge>(k.Kante)) return false;
                    continue;
                }
                foreach (var n in new[] { k.StartKnoten, k.EndKnoten })
                    if (n != Entity.Null && (!EntityManager.Exists(n) || EntityManager.HasComponent<Deleted>(n))) return false;
            }
            return true;
        }

        /** Wie das Fahrwegtausch-Werkzeug: liegengebliebene Vorschau-Definitionen anderer Werkzeuge. */
        private void RaeumeFremdeEntwuerfe()
        {
            using var alte = _alteDefinitionen.ToEntityArray(Allocator.Temp);
            if (alte.Length == 0) return;
            foreach (var d in alte) EntityManager.DestroyEntity(d);
            Mod.log.Info("PLT-Gassenvorbau: " + alte.Length + " liegengebliebene fremde Vorschau-Definition(en) entfernt.");
        }

        /**
         * Dieselbe Definition wie ParkingLotNetCourse `CreateCourseDefinition`
         * im Werkzeugpfad: ohne Besitzer (sonst nimmt CourseSplitSystem einen
         * anderen Hoehenweg), RemoveCrosswalk an der Gasse, am Strassenende
         * Kante und Teilungsstelle.
         */
        private void LegeDefinitionenAn()
        {
            var zufall = new Unity.Mathematics.Random((uint)math.max(1, UnityEngine.Time.frameCount));
            foreach (var k in _kurse)
            {
                var kurve = NetUtils.StraightCurve(k.Von, k.Nach);
                var d = EntityManager.CreateEntity();
                EntityManager.AddComponentData(d, new CreationDefinition { m_Prefab = k.Prefab, m_RandomSeed = zufall.NextInt() });
                EntityManager.AddComponent<Updated>(d);
                if (k.Gasse)
                    EntityManager.AddComponentData(d, new Upgraded
                    {
                        m_Flags = new CompositionFlags(default, CompositionFlags.Side.RemoveCrosswalk, CompositionFlags.Side.RemoveCrosswalk),
                    });
                EntityManager.AddComponentData(d, new NetCourse
                {
                    m_Curve = kurve,
                    m_Length = math.distance(k.Von, k.Nach),
                    m_FixedIndex = -1,
                    m_Elevation = float2.zero,
                    m_StartPosition = new CoursePos
                    {
                        m_Entity = k.Gasse ? (k.StadtAmAnfang ? k.Kante : Entity.Null) : k.StartKnoten,
                        m_SplitPosition = k.Gasse && k.StadtAmAnfang ? k.Teilung : 0f,
                        m_Position = k.Von,
                        m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(kurve)),
                        m_CourseDelta = 0f,
                        m_Elevation = float2.zero,
                        m_Flags = CoursePosFlags.IsFirst,
                        m_ParentMesh = -1,
                    },
                    m_EndPosition = new CoursePos
                    {
                        m_Entity = k.Gasse ? (!k.StadtAmAnfang ? k.Kante : Entity.Null) : k.EndKnoten,
                        m_SplitPosition = k.Gasse && !k.StadtAmAnfang ? k.Teilung : 0f,
                        m_Position = k.Nach,
                        m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(kurve)),
                        m_CourseDelta = 1f,
                        m_Elevation = float2.zero,
                        m_Flags = CoursePosFlags.IsLast,
                        m_ParentMesh = -1,
                    },
                });
                _definitionen.Add(d);
            }
            ParkingLotSchrittmarke.Aenderung("Gassenvorbau: Lot " + _lot.Index + ", " + _kurse.Count + " Gassen-Definitionen angelegt");
            Mod.log.Info($"PLT-Gassenvorbau: Lot {_lot.Index}: {_innen.Count} Gasse(n) mit Stadtteilung und "
                + $"{_kurse.Count - _innen.Count} Weg(e) an ihren inneren Enden als Definition (Temp + Apply).");
        }

        private int ZaehleEigeneTempKanten()
        {
            using var alle = _tempNetze.ToEntityArray(Allocator.Temp);
            var n = 0;
            foreach (var e in alle)
                if (EntityManager.HasComponent<Edge>(e) && _prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)
                    && EntityManager.GetComponentData<Temp>(e).m_Original == Entity.Null) n++;
            return n;
        }

        /** Wie ParkingLotLotOwner `AttachByPrefab`: Besitzer Lot, Kanten zusaetzlich in den SubNet des Traegers. */
        private int BindeAnParkplatz()
        {
            using var alle = _tempNetze.ToEntityArray(Allocator.Temp);
            var n = 0;
            foreach (var e in alle)
            {
                if (!_prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)) continue;
                EntityManager.AddComponentData(e, new Owner(_lot));
                if (EntityManager.HasComponent<Edge>(e) && _traeger != Entity.Null && EntityManager.Exists(_traeger)
                    && EntityManager.HasBuffer<Game.Net.SubNet>(_traeger))
                {
                    var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(_traeger);
                    var schon = false;
                    for (var i = 0; i < puffer.Length && !schon; i++) schon = puffer[i].m_SubNet == e;
                    if (!schon) puffer.Add(new Game.Net.SubNet(e));
                }
                n++;
            }
            return n;
        }

        /**
         * Wie ParkingLotNetBuilder `SchuetzeGassenknoten` im Edit: der
         * gemeinsame Temp-Knoten am inneren Gassenende bekommt Elevation 0.
         * GroundHeightSystem laesst jeden Knoten mit Elevation aus - sonst
         * zieht ein Weg-Prefab ihn auf das von der Gasse beschnittene Gelaende.
         */
        private int SchuetzeGassenknoten()
        {
            if (_innen.Count == 0) return 0;
            using var alle = _tempNetze.ToEntityArray(Allocator.Temp);
            var n = 0;
            var gefunden = new HashSet<int>();
            foreach (var e in alle)
            {
                if (!EntityManager.HasComponent<Game.Net.Node>(e)) continue;
                if (EntityManager.GetComponentData<Temp>(e).m_Original != Entity.Null) continue;
                var lage = EntityManager.GetComponentData<Game.Net.Node>(e).m_Position.xz;
                for (var i = 0; i < _innen.Count; i++)
                {
                    if (math.distance(_innen[i], lage) >= 0.05f || !gefunden.Add(i)) continue;
                    if (!EntityManager.HasComponent<Elevation>(e)) EntityManager.AddComponentData(e, new Elevation(float2.zero));
                    n++;
                }
            }
            return n;
        }

        private void PruefeUndBeende()
        {
            bool ok;
            string text;
            try { ok = Pruefe(out text); }
            catch (Exception x) { ok = false; text = "Pruefung fehlgeschlagen: " + x.Message; }
            Beende(ok, text);
        }

        /** Je Gasse eine dauerhafte Kante mit Prefab, Besitzer Lot und beiden Enden an ihrer Stelle, die Strasse dort geteilt. */
        private bool Pruefe(out string text)
        {
            using var alle = _kanten.ToEntityArray(Allocator.Temp);
            int da = 0, geteilt = 0;
            foreach (var k in _kurse)
            {
                foreach (var e in alle)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != k.Prefab
                        || EntityManager.GetComponentData<Owner>(e).m_Owner != _lot) continue;
                    var c = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    if (math.distance(c.a.xz, k.Von.xz) > .15f || math.distance(c.d.xz, k.Nach.xz) > .15f) continue;
                    da++;
                    if (!k.Gasse) break;
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    var stadt = k.StadtAmAnfang ? edge.m_Start : edge.m_End;
                    if (EntityManager.HasBuffer<ConnectedEdge>(stadt) && EntityManager.GetBuffer<ConnectedEdge>(stadt, true).Length >= 3)
                        geteilt++;
                    break;
                }
            }
            text = $"{da}/{_kurse.Count} Kanten dauerhaft am Parkplatz, {geteilt}/{_innen.Count} Gassen mit geteilter Stadtstrasse";
            return da == _kurse.Count && geteilt == _innen.Count;
        }

        private void Beende(bool ok, string text)
        {
            if (_stufe == Stufe.Frei) return;
            var lot = _lot;
            _stufe = Stufe.Frei;
            _lot = _traeger = Entity.Null;
            _kurse.Clear();
            _prefabs.Clear();
            _innen.Clear();
            foreach (var d in _definitionen)
            {
                try { if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d); }
                catch (Exception) { /* Eine kaputte Definition darf die Rueckmeldung nicht verhindern. */ }
            }
            _definitionen.Clear();
            try
            {
                Mod.log.Info($"PLT-Gassenvorbau: Lot {lot.Index}: {(ok ? "fertig" : "abgebrochen")} - {text}.");
                ParkingLotSchrittmarke.Aenderung("Gassenvorbau: Lot " + lot.Index + (ok ? " fertig" : " abgebrochen"));
            }
            catch (Exception) { }
            Fertig?.Invoke(lot, ok, text);
        }
    }

    /*
     * STEUERUNG DES GASSENVORBAUS - Planung im isolierten Bauarbeiter, dann
     * kurz das Werkzeug aktiv, danach das vorige wieder (wie beim
     * Fahrwegtausch). Gestartet wird nur, wenn kein anderer Tausch, kein
     * Hintergrund-Neubau und kein Entwurf des Spielers laeuft.
     */
    public sealed partial class ParkingLotGassenvorbauSystem : GameSystemBase
    {
        private const int Planfrist = 600;

        private readonly List<(Entity Lot, Action<bool, string> Danach)> _warteschlange = new();
        private ToolSystem _toolSystem;
        private ParkingLotGassenvorbauWerkzeug _werkzeug;
        private ParkingLotToolSystem _plt, _planer;
        private ToolBaseSystem _vorher;
        private Task<ParkingLayout> _rechnung;
        private Action<bool, string> _danach;
        private int _seit, _ruheBis;

        /** Fuer den Sync-Bericht. */
        internal string Zustand => _warteschlange.Count + " queued, tool " + (_werkzeug.Beschaeftigt ? "busy" : "free")
            + (_rechnung != null ? ", planning lot " + _warteschlange[0].Lot.Index : "");

        internal bool Offen(Entity lot)
        {
            foreach (var (l, _) in _warteschlange) if (l == lot) return true;
            return false;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _werkzeug = World.GetOrCreateSystemManaged<ParkingLotGassenvorbauWerkzeug>();
            _plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
        }

        internal void Einreihen(Entity lot, Action<bool, string> danach)
        {
            if (Offen(lot)) return;
            _warteschlange.Add((lot, danach));
            Mod.log.Info($"PLT-Gassenvorbau: Lot {lot.Index} eingereiht; offen {_warteschlange.Count}.");
        }

        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            _warteschlange.Clear();
            _rechnung = null;
            _danach = null;
            _vorher = null;
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
            var (lot, danach) = _warteschlange[0];
            var bild = UnityEngine.Time.frameCount;
            if (_rechnung == null)
            {
                if (bild < _ruheBis || NichtJetzt()) return;
                try
                {
                    _planer ??= ParkingLotToolSystem.Bauarbeiter(World);
                    _rechnung = _planer.BereiteFlaechen(lot);
                    _seit = bild;
                }
                catch (Exception x) { Abschliessen(false, "Bauzettel nicht lesbar: " + x.Message); }
                return;
            }
            if (!_rechnung.IsCompleted)
            {
                if (bild - _seit > 6 * Planfrist) Abschliessen(false, "Layoutrechnung dauert zu lange");
                return;
            }
            if (_rechnung.IsFaulted || _rechnung.IsCanceled)
            {
                Abschliessen(false, "Layoutrechnung gescheitert: " + _rechnung.Exception?.GetBaseException().Message);
                return;
            }
            if (NichtJetzt()) return;
            List<ParkingLotToolSystem.Vorbaugasse> kurse;
            string grund;
            try { kurse = _planer.PlaneGassenvorbau(lot, _rechnung.Result, out grund); }
            catch (Exception x) { Abschliessen(false, "Planung fehlgeschlagen: " + x.Message); return; }
            if (kurse == null)
            {
                if (bild - _seit > Planfrist) Abschliessen(false, "nicht bereit: " + grund);
                return;
            }
            if (kurse.Count == 0) { Abschliessen(true, "keine Gasse braucht eine Stadtteilung, keine steht ohne Wege"); return; }
            if (_toolSystem.activeTool != _werkzeug) _vorher = _toolSystem.activeTool;
            _danach = danach;
            _warteschlange.RemoveAt(0);
            _rechnung = null;
            try
            {
                _werkzeug.Fertig = (e, ok, text) => Rueckmeldung(ok, text);
                _werkzeug.Starte(lot, kurse);
                _toolSystem.activeTool = _werkzeug;
            }
            catch (Exception x)
            {
                _werkzeug.Zuruecksetzen();
                Rueckmeldung(false, "Uebergabe fehlgeschlagen: " + x.Message);
            }
        }

        /** Ein Werkzeugwechsel wuerde einen laufenden Entwurf oder Tausch verwerfen. */
        private bool NichtJetzt()
            => World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().WerkzeugLaeuft
               || World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().WerkzeugLaeuft
               || World.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>().Laeuft
               || World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>().Laeuft
               || _toolSystem.activeTool == _plt && _plt.ArbeitetGerade;

        /** Noch in der Planung: Eintrag weg, Rueckmeldung, kein Werkzeugwechsel. */
        private void Abschliessen(bool ok, string text)
        {
            var (lot, danach) = _warteschlange[0];
            _warteschlange.RemoveAt(0);
            _rechnung = null;
            _ruheBis = UnityEngine.Time.frameCount + 10;
            try { Mod.log.Info($"PLT-Gassenvorbau: Lot {lot.Index}, Erfolg={ok}: {text}."); }
            catch (Exception) { }
            danach?.Invoke(ok, text);
        }

        private void Rueckmeldung(bool ok, string text)
        {
            var danach = _danach;
            _danach = null;
            _ruheBis = UnityEngine.Time.frameCount + 10;
            // Voriges Werkzeug zurueck; nie ein Tauschwerkzeug und nie das
            // PLT-Werkzeug: "Reparieren" hat es gerade selbst geschlossen. Ein
            // wieder geoeffnetes Panel liess den Neubau auf seinen Entwurf
            // warten (28.10., "Finish the current tool draft").
            var zurueck = ok && _vorher != null && _vorher != _werkzeug && !(_vorher is ParkingLotToolSystem)
                && !(_vorher is ParkingLotBestandsTauschWerkzeug) && !(_vorher is ParkingLotFahrwegTauschWerkzeug) ? _vorher : null;
            if (_toolSystem.activeTool == _werkzeug)
                _toolSystem.activeTool = zurueck ?? World.GetOrCreateSystemManaged<DefaultToolSystem>();
            _vorher = null;
            danach?.Invoke(ok, text);
        }
    }
}
