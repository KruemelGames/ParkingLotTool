using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /*
     * FAHRWEGE TAUSCHEN STATT NEU BAUEN (Sync-Schritt 9, 2026-10-04).
     *
     * Fuer die 25-km/h-Fahrwege muss an einem alten Parkplatz nur eines
     * passieren: die unsichtbaren VANILLA-Wege bekommen unseren Klon als
     * Prefab. Gassen und Zoningstrassen sind schon Klone und frischen ihre
     * Spuren ueber `Updated` auf.
     *
     * Vanilla tauscht ein Netz-Prefab ueber das Ersetzen des Strassenwerkzeugs
     * (NetToolSystem.CreateReplacement: CreationDefinition.m_Original = Kante,
     * neues Prefab, Align|SubElevation). Das geht NUR ueber Temp + Apply: eine
     * Permanent-Definition mit m_Original legt eine ZWEITE Kante an
     * (GenerateEdges, TryGetOldEntity -> CreateEntity). ApplyNet uebernimmt
     * dann Owner und PrefabRef von der Vorschau auf das Original und behaelt
     * die Kante samt Knoten (Replace nur bei Zoning-Wechsel, GenerateEdges
     * 1615ff). Deshalb bekommt jede Vorschau den Owner des alten Weges -
     * ohne ihn entfernt ApplyNet den Owner am Original (UpdateComponent).
     *
     * Ein Temp-Tausch aendert NICHTS, bis er uebernommen wird. Wechselt der
     * Nutzer mittendrin das Werkzeug, wird die Vorschau verworfen und alles
     * bleibt wie es war - kein Abriss, kein Rueckweg.
     */
    public sealed partial class ParkingLotFahrwegTauschWerkzeug : ToolBaseSystem
    {
        public const string Id = "PLT Fahrwegtausch";
        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        private enum Stufe { Frei, Anlegen, Warten, Uebernehmen, Pruefen }

        private Stufe _stufe;
        private Entity _lot;
        private readonly List<(Entity Kante, Entity Ziel, Entity Besitzer)> _plan = new();
        private readonly List<Entity> _definitionen = new();
        private int _seit;
        /** Lief OnUpdate seit dem Start schon einmal? Sonst ist das Werkzeug nie aktiv geworden. */
        private bool _lief, _befundGemeldet;
        private EntityQuery _fremdeDefinitionen;
        private EntityQuery _alteDefinitionen;
        private EntityQuery _temps;
        internal System.Action<Entity, bool, string> Fertig;

        internal bool Beschaeftigt => _stufe != Stufe.Frei;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _fremdeDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
            _alteDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.Exclude<Updated>(), ComponentType.Exclude<ParkingLotAuftragsdefinition>());
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
        }

        internal void Starte(Entity lot, List<(Entity Kante, Entity Ziel, Entity Besitzer)> plan)
        {
            _lot = lot;
            _plan.Clear();
            _plan.AddRange(plan);
            _definitionen.Clear();
            _stufe = Stufe.Anlegen;
            _seit = UnityEngine.Time.frameCount;
            _lief = false;
            _befundGemeldet = false;
        }

        /*
         * WENN EIN ANDERES WERKZEUG AKTIV IST (1.0.6) - wie beim Bestandstausch:
         * ein abgeschaltetes Werkzeug bekommt kein OnUpdate mehr, der alte
         * Abbruchzweig war tot. Vor dem Apply verwerfen, danach pruefen.
         */
        internal void PflegeOhneWerkzeug()
        {
            if (_stufe == Stufe.Frei || m_ToolSystem.activeTool == this) return;
            if (!BestandGueltig()) { Beende(false, "Bestand verschwunden"); return; }
            var bild = UnityEngine.Time.frameCount;
            if (_stufe == Stufe.Pruefen)
            {
                if (bild - _seit < 3) return;
                var ok = Pruefe(out var text);
                Beende(ok, text);
                return;
            }
            if (!_lief && bild - _seit < 10) return;
            Beende(false, _lief ? "Werkzeug gewechselt, Tausch verworfen" : "Werkzeug wurde nicht aktiv, Tausch verworfen");
        }

        /** Beim Laden: alles vergessen, ohne Rueckmeldung (der Sync startet neu). */
        internal void Zuruecksetzen()
        {
            _definitionen.Clear();
            _plan.Clear();
            _stufe = Stufe.Frei;
            _lot = Entity.Null;
            _lief = false;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            var bild = UnityEngine.Time.frameCount;
            // Nur noch der Fall "im selben Bild umgeschaltet"; den Wechsel
            // davor faengt PflegeOhneWerkzeug ab (abgeschaltete Systeme
            // bekommen kein OnUpdate).
            if (m_ToolSystem.activeTool != this) return inputDeps;
            if (_stufe != Stufe.Frei && !BestandGueltig())
            {
                if (_stufe != Stufe.Pruefen) applyMode = ApplyMode.Clear;
                Beende(false, "Bestand verschwunden; keine Uebernahme");
                return inputDeps;
            }
            if (_stufe != Stufe.Frei) _lief = true;
            switch (_stufe)
            {
                case Stufe.Anlegen:
                    // Nur in einem Bild ohne fremde Entwuerfe: unsere Vorschau
                    // soll nie mit einer anderen gemeinsam uebernommen werden.
                    if (_fremdeDefinitionen.CalculateEntityCount() > 0 || _temps.CalculateEntityCount() > 0)
                    {
                        RaeumeFremdeEntwuerfe();
                        applyMode = ApplyMode.Clear;
                        // Einmal je Tausch: was haelt ihn auf (ParkingLotEntwurfsbefund).
                        if (bild - _seit > 60 && !_befundGemeldet)
                        {
                            _befundGemeldet = true;
                            Mod.log.Warn("PLT-Fahrwegtausch: wartet seit " + (bild - _seit) + " Bildern auf fremde Entwuerfe - "
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
                        // Angelegt wurde nur in einem Bild ohne fremde Temps;
                        // alles, was jetzt Temp ist, stammt von uns (auch
                        // Nachbarkopien fuer die Knotenform).
                        var eigene = ZaehleEigeneTemps();
                        if (eigene >= _plan.Count) { _stufe = Stufe.Uebernehmen; break; }
                        if (bild - _seit > 30) { applyMode = ApplyMode.Clear; Beende(false, $"nur {eigene}/{_plan.Count} Tauschvorschauen entstanden"); }
                        break;
                    }
                case Stufe.Uebernehmen:
                    ParkingLotSchrittmarke.Aenderung("Fahrwegtausch: Lot " + _lot.Index + " wird uebernommen (Apply)");
                    applyMode = ApplyMode.Apply;
                    _stufe = Stufe.Pruefen;
                    _seit = bild;
                    break;
                case Stufe.Pruefen:
                    if (bild - _seit < 3) break;
                    var ok = Pruefe(out var text);
                    Beende(ok, text);
                    break;
            }
            return inputDeps;
        }

        private bool BestandGueltig()
        {
            if (!EntityManager.Exists(_lot) || !EntityManager.HasComponent<ParkingLotCarrierReference>(_lot)) return false;
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(_lot).Carrier;
            if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, _lot, traeger)) return false;
            foreach (var (kante, ziel, besitzer) in _plan)
            {
                if (!EntityManager.Exists(ziel) || EntityManager.HasComponent<Deleted>(ziel)) return false;
                if (_stufe == Stufe.Pruefen) continue;
                if (!EntityManager.Exists(kante) || EntityManager.HasComponent<Deleted>(kante)
                    || !EntityManager.HasComponent<Owner>(kante)
                    || EntityManager.GetComponentData<Owner>(kante).m_Owner != besitzer) return false;
            }
            return true;
        }

/*
         * LIEGENGEBLIEBENE FREMDE ENTWUERFE WEGRAEUMEN (1.0.6).
         *
         * Wird von einem PLT-Entwurf direkt auf dieses Werkzeug umgeschaltet,
         * laeuft das Standardwerkzeug nie - und genau das raeumt sonst die
         * Vorschau-Definitionen des vorigen Werkzeugs weg (Vanilla
         * `ToolBaseSystem.DestroyDefinitions`: alle Definitionen ohne
         * `Updated`). Gewartet wurde darauf vergeblich; nach 600 Bildern
         * brach der Tausch ab und der Sync lief ohne Wirkung durch (Nutzer
         * 2026-10-06: kein Fahrwegtausch, keine Laternen).
         *
         * Jetzt wie jedes Vanilla-Werkzeug: Definitionen ohne `Updated`
         * zerstoeren, deren Temps mit `ApplyMode.Clear` raeumen lassen.
         * Unsere eigenen Hintergrund-Definitionen (Auftragsmarke) bleiben.
         */
        private void RaeumeFremdeEntwuerfe()
        {
            using var alte = _alteDefinitionen.ToEntityArray(Allocator.Temp);
            if (alte.Length == 0) return;
            foreach (var d in alte) EntityManager.DestroyEntity(d);
            Mod.log.Info("PLT-Fahrwegtausch: " + alte.Length + " liegengebliebene fremde Vorschau-Definition(en) entfernt.");
        }

        private void LegeDefinitionenAn()
        {
            // Bis zu 600 Bilder liegen zwischen Plan und Anlegen; was der
            // Spieler inzwischen abgerissen hat, faellt raus (sonst Ausnahme
            // in jedem Bild und ein haengender Sync).
            var vorher = _plan.Count;
            _plan.RemoveAll(p => !EntityManager.Exists(p.Kante) || EntityManager.HasComponent<Deleted>(p.Kante)
                || !EntityManager.HasComponent<Edge>(p.Kante) || !EntityManager.HasComponent<Curve>(p.Kante));
            if (_plan.Count < vorher)
                Mod.log.Info($"PLT-Fahrwegtausch: Lot {_lot.Index}: {vorher - _plan.Count} Wege inzwischen entfernt, uebersprungen.");
            for (var i = 0; i < _plan.Count; i++)
            {
                var (kante, ziel, besitzer) = _plan[i];
                var edge = EntityManager.GetComponentData<Edge>(kante);
                var curve = EntityManager.GetComponentData<Curve>(kante);
                var d = EntityManager.CreateEntity();
                EntityManager.AddComponentData(d, new CreationDefinition
                {
                    m_Original = kante,
                    m_Prefab = ziel,
                    m_Owner = besitzer,
                    m_Flags = CreationFlags.Align | CreationFlags.SubElevation,
                });
                EntityManager.AddComponent<Updated>(d);
                var kurs = new NetCourse
                {
                    m_Curve = curve.m_Bezier,
                    m_Length = curve.m_Length,
                    m_FixedIndex = EntityManager.HasComponent<Fixed>(kante)
                        ? EntityManager.GetComponentData<Fixed>(kante).m_Index : -1,
                };
                kurs.m_StartPosition.m_Entity = edge.m_Start;
                kurs.m_StartPosition.m_Position = curve.m_Bezier.a;
                kurs.m_StartPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve.m_Bezier));
                kurs.m_StartPosition.m_CourseDelta = 0f;
                kurs.m_StartPosition.m_Flags |= CoursePosFlags.IsFirst;
                kurs.m_EndPosition.m_Entity = edge.m_End;
                kurs.m_EndPosition.m_Position = curve.m_Bezier.d;
                kurs.m_EndPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve.m_Bezier));
                kurs.m_EndPosition.m_CourseDelta = 1f;
                kurs.m_EndPosition.m_Flags |= CoursePosFlags.IsLast;
                EntityManager.AddComponentData(d, kurs);
                _definitionen.Add(d);
            }
            ParkingLotSchrittmarke.Aenderung("Fahrwegtausch: Lot " + _lot.Index + ", " + _plan.Count + " Ersetzungs-Definitionen angelegt");
            Mod.log.Info($"PLT-Fahrwegtausch: Lot {_lot.Index}: {_plan.Count} Ersetzungs-Definitionen (Vanilla-Ersetzen, Temp + Apply).");
        }

        private int ZaehleEigeneTemps()
        {
            var originale = new HashSet<Entity>();
            foreach (var p in _plan) originale.Add(p.Kante);
            using var alle = _temps.ToEntityArray(Allocator.Temp);
            int eigene = 0;
            foreach (var e in alle)
                if (EntityManager.HasComponent<Edge>(e)
                    && originale.Contains(EntityManager.GetComponentData<Temp>(e).m_Original)) eigene++;
            return eigene;
        }

        private bool Pruefe(out string text)
        {
            int getauscht = 0, besitzOk = 0;
            foreach (var (kante, ziel, besitzer) in _plan)
            {
                if (!EntityManager.Exists(kante) || EntityManager.HasComponent<Deleted>(kante)) continue;
                if (EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab == ziel) getauscht++;
                if (EntityManager.HasComponent<Owner>(kante)
                    && EntityManager.GetComponentData<Owner>(kante).m_Owner == besitzer) besitzOk++;
            }
            text = $"{getauscht}/{_plan.Count} Wege mit neuem Prefab, Besitzer erhalten {besitzOk}/{_plan.Count}";
            return getauscht == _plan.Count && besitzOk == _plan.Count;
        }

        private void Beende(bool ok, string text)
        {
            foreach (var d in _definitionen)
                if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d);
            _definitionen.Clear();
            var lot = _lot;
            _stufe = Stufe.Frei;
            _lot = Entity.Null;
            _plan.Clear();
            Mod.log.Info($"PLT-Fahrwegtausch: Lot {lot.Index}: {(ok ? "fertig" : "abgebrochen")} - {text}.");
            ParkingLotSchrittmarke.Aenderung("Fahrwegtausch: Lot " + lot.Index + (ok ? " fertig" : " abgebrochen"));
            Fertig?.Invoke(lot, ok, text);
        }
    }
}
