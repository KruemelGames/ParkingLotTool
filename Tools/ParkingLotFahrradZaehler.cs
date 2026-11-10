using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Testzaehler 02.11.2026, nur lesend: Fahren Raeder ueber die
     * Walking-Area-Flaeche eines PLT-Fahrradparkplatzes und stellen sie
     * sich an die Staender? Vanilla-Fahrradparkplaetze haben innen kein
     * Wegenetz (sonde-1.log), nur Belag + Walking Area. Gezaehlt wird je
     * Besitzerwurzel, Vanilla-Anlagen als Vergleich. Logzeile nur bei
     * Aenderung.
     */
    public sealed partial class ParkingLotFahrradZaehler : GameSystemBase
    {
        private const string Prefix = "PLT-Fahrradzaehler ";
        private const int AlleFrames = 512;

        private PrefabSystem _prefabs;
        private EntityQuery _abgestellt, _fahrend, _flaechen, _parkspuren, _verbindungen, _orte, _marker;
        private int _frames, _runde;
        private bool _aktiv;
        private readonly Dictionary<Entity, string> _letzte = new();
        private readonly Dictionary<Entity, int[]> _ueberlapp = new();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            var ohne = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() };
            _abgestellt = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Bicycle>(), ComponentType.ReadOnly<ParkedCar>() },
                None = ohne,
            });
            _fahrend = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Bicycle>(), ComponentType.ReadOnly<CarCurrentLane>() },
                None = ohne,
            });
            _parkspuren = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.ParkingLane>(), ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<PrefabRef>() },
                None = ohne,
            });
            _orte = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Objects.SpawnLocation>(), ComponentType.ReadOnly<PrefabRef>() },
                None = ohne,
            });
            _marker = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Routes.TakeoffLocation>(), ComponentType.ReadOnly<Game.Routes.AccessLane>(),
                    ComponentType.ReadOnly<Game.Routes.RouteLane>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _verbindungen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.LaneConnection>() },
                None = ohne,
            });
            _flaechen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Net.SubLane>() },
                None = ohne,
            });
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _aktiv = mode == GameMode.Game;
            _letzte.Clear();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!_aktiv || ++_frames < AlleFrames) return;
            _frames = 0;
            var stand = new Dictionary<Entity, int[]>();   // abgestellt, fahrend, Flaechenspuren, davon Rad

            using (var raeder = _abgestellt.ToComponentDataArray<ParkedCar>(Allocator.Temp))
                foreach (var r in raeder) Zaehle(stand, r.m_Lane, 0);
            using (var raeder = _fahrend.ToComponentDataArray<CarCurrentLane>(Allocator.Temp))
                foreach (var r in raeder) Zaehle(stand, r.m_Lane, 1);

            using (var flaechen = _flaechen.ToEntityArray(Allocator.Temp))
                foreach (var flaeche in flaechen)
                {
                    if (Name(flaeche) != ParkingLotFahrradPrefabBau.Gehname) continue;
                    var wurzel = Wurzel(flaeche);
                    if (!stand.TryGetValue(wurzel, out var z)) stand[wurzel] = z = new int[4];
                    var spuren = EntityManager.GetBuffer<Game.Net.SubLane>(flaeche, true);
                    for (var i = 0; i < spuren.Length; i++)
                    {
                        z[2]++;
                        if (EntityManager.HasComponent<Game.Net.ConnectionLane>(spuren[i].m_SubLane)
                            && (EntityManager.GetComponentData<Game.Net.ConnectionLane>(spuren[i].m_SubLane).m_RoadTypes
                                & RoadTypes.Bicycle) != 0) z[3]++;
                    }
                }

            if (++_runde % 8 == 1) Anschluesse(stand.Keys);

            foreach (var paar in stand)
            {
                var art = Art(paar.Key);
                if (art == null) continue;
                var z = paar.Value;
                var text = $"abgestellt {z[0]}, fahrend {z[1]}, Flaechenspuren {z[2]} (davon fuer Rad {z[3]})";
                if (_letzte.TryGetValue(paar.Key, out var alt) && alt == text) continue;
                _letzte[paar.Key] = text;
                Mod.log.Info($"{Prefix}{art} #{paar.Key.Index}: {text}");
            }
        }

        /**
         * Glieder der Kette Rad -> Staender (LaneConnectionSystem.cs:472-800):
         * Die Staenderspur sucht an der Wurzel (erster Building-Besitzer, sonst
         * oberster) in SubLane/SubNet/SubArea; Netzspuren schliessen an eine
         * Flaeche an, wenn ihr Endpunkt in einem Flaechendreieck liegt.
         */
        private void Anschluesse(IEnumerable<Entity> wurzeln)
        {
            var gesucht = new HashSet<Entity>();
            foreach (var w in wurzeln) if (Art(w) != null) gesucht.Add(w);
            if (gesucht.Count == 0) return;
            _ueberlapp.Clear();
            var alle = new Dictionary<Entity, int[]>();          // Spuren, mit Verbindung, Start, Ende
            var proben = new Dictionary<Entity, List<string>>();
            using (var spuren = _parkspuren.ToEntityArray(Allocator.Temp))
                foreach (var spur in spuren)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(spur).m_Prefab;
                    if (!EntityManager.HasComponent<ParkingLaneData>(prefab)
                        || (EntityManager.GetComponentData<ParkingLaneData>(prefab).m_RoadTypes & RoadTypes.Bicycle) == 0) continue;
                    var wurzel = Wurzel(spur);
                    if (!gesucht.Contains(wurzel)) continue;
                    if (!alle.TryGetValue(wurzel, out var z)) alle[wurzel] = z = new int[4];
                    if (!proben.TryGetValue(wurzel, out var pr)) proben[wurzel] = pr = new List<string>();
                    z[0]++;
                    var hat = EntityManager.HasComponent<Game.Net.LaneConnection>(spur);
                    var v = hat ? EntityManager.GetComponentData<Game.Net.LaneConnection>(spur) : default;
                    if (hat) z[1]++;
                    if (v.m_StartLane != Entity.Null) z[2]++;
                    if (v.m_EndLane != Entity.Null) z[3]++;
                    // Ueberlappungen sperren Stellplaetze (VehicleUtils.FindFreeParkingSpace).
                    var ueber = EntityManager.HasBuffer<Game.Net.LaneOverlap>(spur) ? EntityManager.GetBuffer<Game.Net.LaneOverlap>(spur, true) : default;
                    var nUeber = ueber.IsCreated ? ueber.Length : 0;
                    if (!_ueberlapp.ContainsKey(wurzel)) _ueberlapp[wurzel] = new int[2];
                    _ueberlapp[wurzel][0] += nUeber > 0 ? 1 : 0;
                    _ueberlapp[wurzel][1] += nUeber;
                    if (pr.Count < 2)
                    {
                        var partner = new List<string>();
                        for (var i = 0; i < nUeber && i < 4; i++)
                            partner.Add($"{Spurtext(ueber[i].m_Other)} [{ueber[i].m_ThisStart}-{ueber[i].m_ThisEnd}; {ueber[i].m_Flags}]");
                        pr.Add($"Kette [{Kette(spur)}] Flags {EntityManager.GetComponentData<Game.Net.ParkingLane>(spur).m_Flags}"
                            + $" Frei {EntityManager.GetComponentData<Game.Net.ParkingLane>(spur).m_FreeSpace:F2}"
                            + $" Start->{Spurtext(v.m_StartLane)} Ende->{Spurtext(v.m_EndLane)}"
                            + $" Ueberlappungen {nUeber}: {string.Join(" | ", partner)}");
                    }
                }
            // Wer haengt an unseren Flaechenspuren?
            var flaechenspuren = new Dictionary<Entity, Entity>();
            var flaechenText = new Dictionary<Entity, List<string>>();
            using (var flaechen = _flaechen.ToEntityArray(Allocator.Temp))
                foreach (var f in flaechen)
                {
                    var w = Wurzel(f);
                    if (!gesucht.Contains(w)) continue;
                    var b = EntityManager.GetBuffer<Game.Net.SubLane>(f, true);
                    for (var i = 0; i < b.Length; i++) flaechenspuren[b[i].m_SubLane] = w;
                    // Flaeche und ihre Spuren Feld fuer Feld (Vergleich Vanilla/PLT).
                    var prefab = EntityManager.GetComponentData<PrefabRef>(f).m_Prefab;
                    var zeile = $"{Name(f)}";
                    if (EntityManager.HasComponent<NavigationAreaData>(prefab))
                    {
                        var nav = EntityManager.GetComponentData<NavigationAreaData>(prefab);
                        zeile += $" Nav[{nav.m_ConnectionType}/{nav.m_SecondaryType}/{nav.m_RoadTypes}]";
                    }
                    var spuren = new List<string>();
                    for (var i = 0; i < b.Length && i < 6; i++)
                    {
                        var s = b[i].m_SubLane;
                        var txt = Name(s);
                        if (EntityManager.HasComponent<Game.Net.ConnectionLane>(s))
                        {
                            var c = EntityManager.GetComponentData<Game.Net.ConnectionLane>(s);
                            txt += $" [{c.m_Flags}; {c.m_RoadTypes}]";
                        }
                        if (EntityManager.HasComponent<Game.Net.Lane>(s))
                        {
                            var l = EntityManager.GetComponentData<Game.Net.Lane>(s);
                            txt += $" Knoten {l.m_StartNode.GetOwnerIndex()}/{l.m_EndNode.GetOwnerIndex()}";
                        }
                        spuren.Add(txt);
                    }
                    zeile += $" Spuren {b.Length}: {string.Join(" | ", spuren)}";
                    if (!flaechenText.TryGetValue(w, out var ft)) flaechenText[w] = ft = new List<string>();
                    ft.Add(zeile);
                }
            var angebunden = new Dictionary<Entity, List<string>>();
            using (var verb = _verbindungen.ToEntityArray(Allocator.Temp))
                foreach (var e in verb)
                {
                    var v = EntityManager.GetComponentData<Game.Net.LaneConnection>(e);
                    foreach (var ziel in new[] { v.m_StartLane, v.m_EndLane })
                    {
                        if (ziel == Entity.Null || !flaechenspuren.TryGetValue(ziel, out var w)) continue;
                        if (!angebunden.TryGetValue(w, out var l)) angebunden[w] = l = new List<string>();
                        l.Add("LaneConnection " + Spurtext(e));
                    }
                }
            // Gebaeude haengen ihr Inneres ueber SpawnLocation.m_ConnectedLane1/2 an
            // (RoadConnectionSystem). Wer zeigt so auf unsere Flaechenspuren?
            using (var orte = _orte.ToEntityArray(Allocator.Temp))
                foreach (var o in orte)
                {
                    var s = EntityManager.GetComponentData<Game.Objects.SpawnLocation>(o);
                    foreach (var ziel in new[] { s.m_ConnectedLane1, s.m_ConnectedLane2 })
                    {
                        if (ziel == Entity.Null || !flaechenspuren.TryGetValue(ziel, out var w)) continue;
                        if (!angebunden.TryGetValue(w, out var l)) angebunden[w] = l = new List<string>();
                        l.Add("SpawnLocation " + Name(o));
                    }
                }
            // Vanilla-Bruecke Strasse <-> Gehflaeche: woran haengt jeder Zugangsmarker?
            var marker = new Dictionary<Entity, List<string>>();
            using (var ms = _marker.ToEntityArray(Allocator.Temp))
                foreach (var m in ms)
                {
                    var w = Wurzel(m);
                    if (!gesucht.Contains(w)) continue;
                    var a = EntityManager.GetComponentData<Game.Routes.AccessLane>(m);
                    var r = EntityManager.GetComponentData<Game.Routes.RouteLane>(m);
                    if (!marker.TryGetValue(w, out var l)) marker[w] = l = new List<string>();
                    l.Add($"Zugang {Spur(a.m_Lane)}, Start {Spur(r.m_StartLane)}, Ende {Spur(r.m_EndLane)}");
                }
            foreach (var w in gesucht)
            {
                if (!alle.TryGetValue(w, out var z)) z = new int[4];
                angebunden.TryGetValue(w, out var an);
                proben.TryGetValue(w, out var pr);
                _ueberlapp.TryGetValue(w, out var ul);
                var text = $"Wurzel {Wurzeltext(w)}; Staenderspuren {z[0]}, mit LaneConnection {z[1]}, Start {z[2]}, Ende {z[3]}, "
                    + $"mit Ueberlappung {(ul == null ? 0 : ul[0])} (gesamt {(ul == null ? 0 : ul[1])}); "
                    + $"an Flaeche angebunden: {(an == null ? "nichts" : Gruppen(an))}; eigene Spuren der Wurzel: {EigeneSpuren(w)}";
                if (flaechenText.TryGetValue(w, out var ftext)) text += "; Flaechen: " + string.Join(" || ", ftext);
                if (pr != null) text += "; Proben: " + string.Join(" || ", pr);
                text += "; Marker: " + (marker.TryGetValue(w, out var mt) ? string.Join(" || ", mt) : "keine");
                var schluessel = new Entity { Index = -w.Index, Version = w.Version };
                if (_letzte.TryGetValue(schluessel, out var alt) && alt == text) continue;
                _letzte[schluessel] = text;
                Mod.log.Info($"{Prefix}ANSCHLUSS {Art(w)} #{w.Index}: {text}");
            }
        }

        private string Spur(Entity e) => e == Entity.Null ? "-" : !EntityManager.Exists(e) ? "tot"
            : Name(e) + " von " + Name(EntityManager.HasComponent<Owner>(e) ? EntityManager.GetComponentData<Owner>(e).m_Owner : Entity.Null);

        private static string Gruppen(List<string> l)
        {
            var z = new SortedDictionary<string, int>();
            foreach (var s in l) { z.TryGetValue(s, out var n); z[s] = n + 1; }
            var teile = new List<string>();
            foreach (var paar in z) teile.Add(paar.Value + "x " + paar.Key);
            return string.Join(" | ", teile);
        }

        private string EigeneSpuren(Entity w)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(w)) return "kein Puffer";
            var b = EntityManager.GetBuffer<Game.Net.SubLane>(w, true);
            var l = new List<string>();
            for (var i = 0; i < b.Length; i++)
            {
                var s = b[i].m_SubLane;
                var text = Spurtext(s);
                if (EntityManager.HasComponent<Game.Net.ConnectionLane>(s))
                {
                    var c = EntityManager.GetComponentData<Game.Net.ConnectionLane>(s);
                    text += $" [{c.m_Flags}; {c.m_RoadTypes}]";
                }
                l.Add(text);
            }
            return b.Length == 0 ? "0" : Gruppen(l);
        }

        private string Kette(Entity e)
        {
            var teile = new List<string>();
            for (var tiefe = 0; tiefe < 8 && EntityManager.HasComponent<Owner>(e); tiefe++)
            {
                e = EntityManager.GetComponentData<Owner>(e).m_Owner;
                if (e == Entity.Null || !EntityManager.Exists(e)) break;
                teile.Add(Name(e) + (EntityManager.HasComponent<Game.Buildings.Building>(e) ? "(Building)" : ""));
            }
            return string.Join(" > ", teile);
        }

        private string Spurtext(Entity spur)
        {
            if (spur == Entity.Null) return "keine";
            if (!EntityManager.Exists(spur)) return "tot";
            var besitzer = EntityManager.HasComponent<Owner>(spur) ? EntityManager.GetComponentData<Owner>(spur).m_Owner : Entity.Null;
            var art = EntityManager.HasComponent<Game.Net.ConnectionLane>(spur) ? "Verbindung"
                : EntityManager.HasComponent<Game.Net.PedestrianLane>(spur) ? "Fuss"
                : EntityManager.HasComponent<Game.Net.CarLane>(spur) ? "Fahr" : "?";
            return $"{art} {Name(spur)} an {Name(besitzer)}";
        }

        private string Wurzeltext(Entity w)
        {
            var subnet = EntityManager.HasBuffer<Game.Net.SubNet>(w) ? EntityManager.GetBuffer<Game.Net.SubNet>(w, true).Length : -1;
            var namen = new List<string>();
            if (EntityManager.HasBuffer<Game.Areas.SubArea>(w))
            {
                var b = EntityManager.GetBuffer<Game.Areas.SubArea>(w, true);
                for (var i = 0; i < b.Length; i++) namen.Add(Name(b[i].m_Area));
            }
            var subobj = EntityManager.HasBuffer<Game.Objects.SubObject>(w) ? EntityManager.GetBuffer<Game.Objects.SubObject>(w, true).Length : -1;
            return $"{Name(w)} Building={EntityManager.HasComponent<Game.Buildings.Building>(w)} SubNet={subnet} SubObject={subobj} SubArea=[{string.Join(", ", namen)}]";
        }

        private void Zaehle(Dictionary<Entity, int[]> stand, Entity spur, int feld)
        {
            if (spur == Entity.Null || !EntityManager.Exists(spur)) return;
            var wurzel = Wurzel(spur);
            if (Art(wurzel) == null) return;
            if (!stand.TryGetValue(wurzel, out var z)) stand[wurzel] = z = new int[4];
            z[feld]++;
        }

        /** PLT-Lot, PLT-Traeger, Vanilla-Anlage; sonst null (nicht zaehlen). */
        private string Art(Entity wurzel)
        {
            if (wurzel == Entity.Null || !EntityManager.Exists(wurzel)) return null;
            if (ParkingLotFahrradPrefabBau.IstLotname(Name(wurzel))) return "PLT " + Name(wurzel);
            if (!EntityManager.HasComponent<Game.Buildings.BicycleParkingFacility>(wurzel)) return null;
            return EntityManager.HasComponent<Game.Objects.Transform>(wurzel)
                ? "Vanilla " + Name(wurzel) : "PLT-Traeger";
        }

        private Entity Wurzel(Entity e)
        {
            for (var tiefe = 0; tiefe < 16 && EntityManager.HasComponent<Owner>(e)
                && !EntityManager.HasComponent<Game.Buildings.Building>(e); tiefe++)
            {
                var besitzer = EntityManager.GetComponentData<Owner>(e).m_Owner;
                if (besitzer == Entity.Null || !EntityManager.Exists(besitzer)) break;
                e = besitzer;
            }
            return e;
        }

        private string Name(Entity e)
        {
            if (!EntityManager.HasComponent<PrefabRef>(e)) return "";
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            return _prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) ? p.name : "";
        }
    }
}
