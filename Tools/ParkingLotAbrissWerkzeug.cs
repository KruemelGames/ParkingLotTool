using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /*
     * PARKPLAETZE ABREISSEN WIE DER BULLDOZER (29.10.2026).
     *
     * Die Deinstallation markierte jedes Lot mit `Deleted` und liess CS2s
     * Kaskade (`SubElementDeleteSystem`) den Rest erledigen. Die loescht die
     * Gassen, laesst aber jeden Knoten an einer Stadtstrasse stehen - auch
     * dort, wo nach der Gasse keine Kreuzung mehr ist. Die Stadtstrasse
     * blieb an jeder frueheren Einmuendung zweigeteilt. Der Nutzer: die
     * Gassen sind durch die Mod entstanden, also soll auch das weg.
     *
     * Der Bulldozer macht es anders (`BulldozeToolSystem.AddEntity` und
     * `AddSubNets`): eine Loeschdefinition fuer das Lot mit seinen
     * Eckpunkten, je eine fuer jede Kante und jeden losen Knoten im SubNet,
     * je eine fuer die Flaechen im SubArea. Das laeuft durch die Vorschau,
     * und beim Anwenden entscheidet CS2, ob ein Knoten bleibt (Kreuzung) oder
     * die Stadtstrasse wieder ein Stueck wird. Dieses Werkzeug legt genau
     * diese Definitionen an, unsichtbar, ein Lot nach dem anderen - Muster:
     * `ParkingLotGassenvorbauWerkzeug`.
     *
     * Der Versuch vom 22.09. scheiterte an zwei Dingen, die hier stimmen:
     * die Flaechendefinition hatte keine Eckpunkte (`GenerateAreasSystem`
     * verlangt den `Game.Areas.Node`-Puffer), und es wendete kein Werkzeug an.
     *
     * Was danach passiert, ist derselbe Weg wie beim Abriss durch den Spieler:
     * das Lot faellt in ApplyTool, der Aufraeumer holt Teile und Traeger, der
     * Ankerabriss den Netzerhalt-Anker. Scheitert der Bulldozer-Weg fuer ein
     * Lot, geht genau dieses Lot ueber `Rueckfall` den alten Weg (Markieren
     * vor der Kaskade) - die Deinstallation bleibt vollstaendig.
     */
    public sealed partial class ParkingLotAbrissWerkzeug : ToolBaseSystem
    {
        public const string Id = "PLT Abriss";
        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        private enum Stufe { Frei, Anlegen, Warten, Pruefen }

        private readonly List<Entity> _warteschlange = new();
        private readonly List<Entity> _definitionen = new();
        private Stufe _stufe;
        private Entity _lot;
        private int _seit;
        private int _kanten, _knoten, _flaechen;
        private EntityQuery _fremdeDefinitionen, _alteDefinitionen, _temps;

        /** Dieses Lot geht den alten Weg (Markieren vor der Kaskade); der Grund steht dabei. */
        internal Action<Entity, string> Rueckfall;

        internal bool Laeuft => _stufe != Stufe.Frei || _warteschlange.Count > 0;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _fremdeDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
            _alteDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.Exclude<Updated>(), ComponentType.Exclude<ParkingLotAuftragsdefinition>());
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
        }

        internal void Einreihen(IEnumerable<Entity> lots)
        {
            _warteschlange.AddRange(lots);
        }

        /** Beim Laden: alles vergessen, ohne Rueckmeldung. */
        internal void Zuruecksetzen()
        {
            VerwirfDefinitionen();
            _warteschlange.Clear();
            _stufe = Stufe.Frei;
            _lot = Entity.Null;
        }

        /**
         * Ein abgeschaltetes Werkzeug bekommt kein OnUpdate. Hat der Spieler
         * ein anderes genommen, geht alles Offene den alten Weg - aus PreTool
         * gerufen, also noch vor der Kaskade dieses Bildes.
         */
        internal void PflegeOhneWerkzeug()
        {
            if (_stufe == Stufe.Pruefen)
            {
                if (UnityEngine.Time.frameCount - _seit < 3) return;
                PruefeUndBeende();
            }
            else if (_stufe != Stufe.Frei)
            {
                var lot = _lot;
                Beende(false, "Werkzeug gewechselt, Entwurf verworfen");
                Rueckfall?.Invoke(lot, "Werkzeug gewechselt");
            }
            while (_warteschlange.Count > 0)
            {
                var lot = _warteschlange[0];
                _warteschlange.RemoveAt(0);
                if (Lebt(lot)) Rueckfall?.Invoke(lot, "Werkzeug gewechselt");
            }
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            if (m_ToolSystem.activeTool != this) return inputDeps;
            var bild = UnityEngine.Time.frameCount;
            switch (_stufe)
            {
                case Stufe.Frei:
                    while (_warteschlange.Count > 0 && _stufe == Stufe.Frei)
                    {
                        var naechstes = _warteschlange[0];
                        _warteschlange.RemoveAt(0);
                        if (!Lebt(naechstes)) continue;
                        _lot = naechstes;
                        _stufe = Stufe.Anlegen;
                        _seit = bild;
                    }
                    break;
                case Stufe.Anlegen:
                    if (!Lebt(_lot)) { Beende(true, "war schon fort"); break; }
                    // Nie zusammen mit einem fremden Entwurf anwenden.
                    if (_fremdeDefinitionen.CalculateEntityCount() > 0 || _temps.CalculateEntityCount() > 0)
                    {
                        RaeumeFremdeEntwuerfe();
                        applyMode = ApplyMode.Clear;
                        if (bild - _seit > 600)
                        {
                            var lot = _lot;
                            Beende(false, "fremde Entwuerfe blieben 600 Bilder");
                            Rueckfall?.Invoke(lot, "fremde Entwuerfe");
                        }
                        break;
                    }
                    LegeDefinitionenAn();
                    _stufe = Stufe.Warten;
                    _seit = bild;
                    break;
                case Stufe.Warten:
                    if (VorschauSteht())
                    {
                        ParkingLotSchrittmarke.Aenderung("Abriss: Lot " + _lot.Index + " wird angewendet (Apply)");
                        applyMode = ApplyMode.Apply;
                        _stufe = Stufe.Pruefen;
                        _seit = bild;
                        break;
                    }
                    if (bild - _seit > 30)
                    {
                        applyMode = ApplyMode.Clear;
                        var lot = _lot;
                        Beende(false, "keine Abriss-Vorschau nach 30 Bildern; nichts angewendet");
                        Rueckfall?.Invoke(lot, "keine Abriss-Vorschau");
                    }
                    break;
                case Stufe.Pruefen:
                    if (bild - _seit < 3) break;
                    PruefeUndBeende();
                    break;
            }
            return inputDeps;
        }

        private bool Lebt(Entity e) => e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);

        /** Wie das Fahrwegtausch-Werkzeug: liegengebliebene Vorschau-Definitionen anderer Werkzeuge. */
        private void RaeumeFremdeEntwuerfe()
        {
            using var alte = _alteDefinitionen.ToEntityArray(Allocator.Temp);
            if (alte.Length == 0) return;
            foreach (var d in alte) EntityManager.DestroyEntity(d);
            Mod.log.Info("PLT-Abriss: " + alte.Length + " liegengebliebene fremde Vorschau-Definition(en) entfernt.");
        }

        /** `BulldozeToolSystem.AddEntity` fuer eine Flaeche mit `delete`, samt SubNet und SubArea. */
        private void LegeDefinitionenAn()
        {
            _kanten = _knoten = _flaechen = 0;
            LegeFlaecheAn(_lot, CreationFlags.Delete);
            var oberste = new HashSet<Entity> { _lot };
            if (EntityManager.HasBuffer<Game.Net.SubNet>(_lot)) LegeNetzeAn(oberste, _lot, default);
            if (EntityManager.HasBuffer<Game.Areas.SubArea>(_lot))
            {
                using var flaechen = EntityManager.GetBuffer<Game.Areas.SubArea>(_lot, true).ToNativeArray(Allocator.Temp);
                foreach (var f in flaechen)
                {
                    if (!EntityManager.Exists(f.m_Area) || !EntityManager.HasBuffer<Game.Areas.Node>(f.m_Area)) continue;
                    var flags = CreationFlags.Delete;
                    if (!EntityManager.HasComponent<Game.Areas.Lot>(f.m_Area)) flags |= CreationFlags.Hidden;
                    LegeFlaecheAn(f.m_Area, flags);
                    _flaechen++;
                }
            }
            ParkingLotSchrittmarke.Aenderung("Abriss: Lot " + _lot.Index + ", " + _definitionen.Count + " Definitionen angelegt");
            Mod.log.Info($"PLT-Abriss: Lot {_lot.Index}: {_definitionen.Count} Loeschdefinition(en) wie der Bulldozer - "
                + $"1 Lot, {_kanten} Kante(n), {_knoten} lose(r) Knoten, {_flaechen} Flaeche(n).");
        }

        private void LegeFlaecheAn(Entity flaeche, CreationFlags flags)
        {
            using var ecken = EntityManager.GetBuffer<Game.Areas.Node>(flaeche, true).ToNativeArray(Allocator.Temp);
            NativeArray<LocalNodeCache> cache = default;
            if (EntityManager.HasBuffer<LocalNodeCache>(flaeche))
                cache = EntityManager.GetBuffer<LocalNodeCache>(flaeche, true).ToNativeArray(Allocator.Temp);
            var d = EntityManager.CreateEntity();
            EntityManager.AddBuffer<Game.Areas.Node>(d).CopyFrom(ecken);
            if (cache.IsCreated) { EntityManager.AddBuffer<LocalNodeCache>(d).CopyFrom(cache); cache.Dispose(); }
            EntityManager.AddComponentData(d, new CreationDefinition { m_Original = flaeche, m_Flags = flags });
            EntityManager.AddComponent<Updated>(d);
            _definitionen.Add(d);
        }

        /** `BulldozeToolSystem.AddSubNets` mit `delete`. */
        private void LegeNetzeAn(HashSet<Entity> oberste, Entity besitzer, OwnerDefinition besitzerDefinition)
        {
            using var netze = EntityManager.GetBuffer<Game.Net.SubNet>(besitzer, true).ToNativeArray(Allocator.Temp);
            foreach (var eintrag in netze)
            {
                var netz = eintrag.m_SubNet;
                if (!EntityManager.Exists(netz)) continue;
                if (EntityManager.HasComponent<Game.Net.Node>(netz))
                {
                    if (HatEigeneKanteAmEnde(netz, besitzer) || oberste.Contains(netz)) continue;
                    var knoten = EntityManager.GetComponentData<Game.Net.Node>(netz);
                    LegeNetzdefinitionAn(netz, besitzerDefinition, new NetCourse
                    {
                        m_Curve = new Bezier4x3(knoten.m_Position, knoten.m_Position, knoten.m_Position, knoten.m_Position),
                        m_Length = 0f,
                        m_FixedIndex = -1,
                        m_StartPosition = new CoursePos { m_Entity = netz, m_Position = knoten.m_Position, m_Rotation = knoten.m_Rotation, m_CourseDelta = 0f },
                        m_EndPosition = new CoursePos { m_Entity = netz, m_Position = knoten.m_Position, m_Rotation = knoten.m_Rotation, m_CourseDelta = 1f },
                    });
                    _knoten++;
                }
                else if (EntityManager.HasComponent<Edge>(netz))
                {
                    var kante = EntityManager.GetComponentData<Edge>(netz);
                    if (oberste.Contains(netz) || oberste.Contains(kante.m_Start) || oberste.Contains(kante.m_End)) continue;
                    var kurve = EntityManager.GetComponentData<Curve>(netz).m_Bezier;
                    LegeNetzdefinitionAn(netz, besitzerDefinition, new NetCourse
                    {
                        m_Curve = kurve,
                        m_Length = MathUtils.Length(kurve),
                        m_FixedIndex = -1,
                        m_StartPosition = new CoursePos
                        {
                            m_Entity = kante.m_Start, m_Position = kurve.a, m_CourseDelta = 0f,
                            m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(kurve)),
                        },
                        m_EndPosition = new CoursePos
                        {
                            m_Entity = kante.m_End, m_Position = kurve.d, m_CourseDelta = 1f,
                            m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(kurve)),
                        },
                    });
                    _kanten++;
                    if (EntityManager.HasBuffer<Game.Net.SubNet>(netz))
                        LegeNetzeAn(oberste, netz, new OwnerDefinition
                        {
                            m_Prefab = EntityManager.GetComponentData<PrefabRef>(netz).m_Prefab,
                            m_Position = kurve.a,
                            m_Rotation = new float4(kurve.d, 0f),
                        });
                }
            }
        }

        private void LegeNetzdefinitionAn(Entity original, OwnerDefinition besitzerDefinition, NetCourse kurs)
        {
            var d = EntityManager.CreateEntity();
            EntityManager.AddComponentData(d, new CreationDefinition { m_Original = original, m_Flags = CreationFlags.Delete });
            EntityManager.AddComponent<Updated>(d);
            if (besitzerDefinition.m_Prefab != Entity.Null) EntityManager.AddComponentData(d, besitzerDefinition);
            EntityManager.AddComponentData(d, kurs);
            _definitionen.Add(d);
        }

        /** `BulldozeToolSystem.HasEdgeStartOrEnd`: endet hier eine Kante desselben Besitzers, nimmt sie den Knoten mit. */
        private bool HatEigeneKanteAmEnde(Entity knoten, Entity besitzer)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return false;
            var kanten = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            for (var i = 0; i < kanten.Length; i++)
            {
                var k = kanten[i].m_Edge;
                if (!EntityManager.HasComponent<Edge>(k)) continue;
                var e = EntityManager.GetComponentData<Edge>(k);
                if ((e.m_Start == knoten || e.m_End == knoten) && EntityManager.HasComponent<Owner>(k)
                    && EntityManager.GetComponentData<Owner>(k).m_Owner == besitzer) return true;
            }
            return false;
        }

        /** Steht die Vorschau? Ein Temp mit `Delete`, dessen Original das Lot ist. */
        private bool VorschauSteht()
        {
            using var temps = _temps.ToEntityArray(Allocator.Temp);
            foreach (var t in temps)
            {
                var temp = EntityManager.GetComponentData<Temp>(t);
                if (temp.m_Original == _lot && (temp.m_Flags & TempFlags.Delete) != 0) return true;
            }
            return false;
        }

        private void PruefeUndBeende()
        {
            if (Lebt(_lot))
            {
                var lot = _lot;
                Beende(false, "nach dem Anwenden noch da");
                Rueckfall?.Invoke(lot, "nach dem Anwenden noch da");
                return;
            }
            Beende(true, $"fort ({_kanten} Kante(n), {_knoten} lose(r) Knoten, {_flaechen} Flaeche(n) mit abgerissen; "
                + "ueber die Knoten an Stadtstrassen hat CS2 entschieden)");
        }

        private void VerwirfDefinitionen()
        {
            foreach (var d in _definitionen)
            {
                try { if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d); }
                catch (Exception) { /* Eine kaputte Definition darf das Weitermachen nicht verhindern. */ }
            }
            _definitionen.Clear();
        }

        private void Beende(bool ok, string text)
        {
            if (_stufe == Stufe.Frei) return;
            var lot = _lot;
            _stufe = Stufe.Frei;
            _lot = Entity.Null;
            VerwirfDefinitionen();
            try
            {
                Mod.log.Info($"PLT-Abriss: Lot {lot.Index}: {(ok ? "abgerissen" : "nicht ueber den Bulldozer-Weg")} - {text}.");
                ParkingLotSchrittmarke.Aenderung("Abriss: Lot " + lot.Index + (ok ? " fertig" : " Rueckfall"));
            }
            catch (Exception) { }
        }
    }
}
