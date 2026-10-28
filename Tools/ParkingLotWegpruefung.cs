using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game;
using Game.Common;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * GEBAUTE PARKPLAETZE MIT FALSCH VERLEGTEN WEGEN (Issue #10, 2026-10-09).
     *
     * Bis 1.0.x klappte die Randstrasse in einem schmalen Arm zu einem Punkt
     * zusammen. Die Kante ohne Laenge galt beim Teilen der Wege als
     * kollinear mit jeder anderen, und ihr Punkt wurde in weit entfernte
     * Randstrassenkanten eingesetzt: Wege liefen als Stern quer ueber den
     * Innenhof einer U-Form, bis zu 36 m ausserhalb des Parkplatzes. Der Kern
     * plant das nicht mehr - schon gebaute Parkplaetze behalten es aber.
     *
     * Dieses System findet sie und bietet in der Liste "Reparieren" an: ein
     * Neubau im Hintergrund wie bei fehlenden Assets, mit derselben Form und
     * denselben Einstellungen.
     *
     * ZWEI STUFEN, DAMIT DER KNOPF NIE LUEGT.
     *
     *  1. Vorpruefung in der Welt, billig, fuer jeden Parkplatz: ein Weg am
     *     Traeger laeuft mehr als 10 m ausserhalb des Umrisses (Anfang, Ende
     *     und drei Stellen dazwischen), oder an einem Knoten treffen sich
     *     mindestens 5 seiner Wege. Beides plant der Kern nicht.
     *  2. Gegenprobe nur fuer Verdachtsfaelle: der Parkplatz wird aus seinem
     *     Bauzettel mit dem laufenden Kern neu gerechnet (im Hintergrund,
     *     wie die Vorschau). Gemeldet wird er nur, wenn DIESER Plan sauber
     *     ist - sonst haette der Neubau dasselbe Ergebnis, und der Knopf
     *     stuende fuer immer da.
     *
     * Bleibt ein Parkplatz nach dem Neubau auffaellig, wird er nicht noch
     * einmal angeboten (Schutz vor Endlosschleife mit "Automatisch
     * reparieren"); das Log nennt ihn.
     *
     * Die Automatik repariert nur, was seit dem Bau unveraendert ist (oder
     * vor dem Fingerabdruck im Bauprotokoll gebaut wurde). Hat jemand die
     * Wege nachtraeglich verschoben, entscheidet der Spieler per Klick.
     */
    public sealed partial class ParkingLotWegpruefungSystem : GameSystemBase
    {
        private const int Takt = 300;

        /** Dieselbe Grenze wie der Wegvergleich im Bericht. */
        private const float Grenze = 10f;

        /** Mehr Wege an einem Knoten plant der Kern nicht (Kreuzung = 4). */
        private const int Hoechstgrad = 5;

        /**
         * 1 = Wege ausserhalb des Umrisses, 2 = zu viele Wege an einem Knoten,
         * 3 = gar kein Wegenetz (ParkingLotWaisenSystem `OhneWegenetz`, 28.10.).
         */
        internal const int Ausserhalb = 1, Knoten = 2, Fehlt = 3;

        private sealed class Stand
        {
            internal long Abdruck;
            internal string LotId;
            internal int Verdacht;
            internal bool Geprueft;
            internal int Befund;
            internal bool OhneNetz;
        }

        private EntityQuery _lots;
        private readonly Dictionary<Entity, Stand> _staende = new();
        /** Umrisse, deren Neubau dieses System angestossen hat. */
        private readonly HashSet<string> _repariert = new(StringComparer.Ordinal);
        /** Nach dem Neubau weiter auffaellig: nicht mehr anbieten. */
        private readonly HashSet<string> _bleibt = new(StringComparer.Ordinal);
        /** Nach dem Bau veraendert - einmal gemeldet, dass die Automatik sie auslaesst. */
        private readonly HashSet<string> _autoAusgelassen = new(StringComparer.Ordinal);
        /** Schon gemeldet: Parkplatz ohne Wegenetz. */
        private readonly HashSet<string> _gemeldetOhneNetz = new(StringComparer.Ordinal);
        private Entity _probeLot = Entity.Null;
        private long _probeAbdruck;
        private Task<int> _probe;
        private int _bilder, _stand = -1;

        internal int Anzahl => _staende.Count(p => p.Value.Befund != 0);

        internal bool Betrifft(Entity lot) => Befund(lot) != 0;

        /** Fuer die Sync-Aufnahme: der Neubau dieses Parkplatzes kam von hier. */
        internal bool InReparatur(Entity lot)
            => _staende.TryGetValue(lot, out var s) && s.LotId != null && _repariert.Contains(s.LotId);

        /** 0 = in Ordnung, sonst `Ausserhalb` oder `Knoten`. */
        internal int Befund(Entity lot)
            => _staende.TryGetValue(lot, out var s) ? s.Befund : 0;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<ParkingLotBuildReceipt>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _staende.Clear();
            _repariert.Clear();
            _bleibt.Clear();
            _autoAusgelassen.Clear();
            _gemeldetOhneNetz.Clear();
            _probe = null;
            _probeLot = Entity.Null;
            _stand = -1;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            if (_probe != null && _probe.IsCompleted) Auswerten();
            var stand = _lots.CalculateEntityCount();
            if (stand == _stand && ++_bilder < Takt) return;
            _stand = stand;
            _bilder = 0;
            Erfasse();
            if (_probe == null) StarteProbe();
            if (Mod.Optionen?.WaisenAutomatischReparieren == true) RepariereAutomatisch();
        }

        // ------------------------------------------------------------------
        // Stufe 1: Welt
        // ------------------------------------------------------------------

        private void Erfasse()
        {
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            var lebend = new HashSet<Entity>();
            var waisen = World.GetOrCreateSystemManaged<ParkingLotWaisenSystem>();
            foreach (var lot in lots)
            {
                lebend.Add(lot);
                var wege = WegeDesTraegers(lot);
                var abdruck = Abdruck(wege);
                var ohne = waisen.OhneWegenetz.Contains(lot);
                if (_staende.TryGetValue(lot, out var alt) && alt.Abdruck == abdruck && alt.OhneNetz == ohne) continue;
                var neu = new Stand { Abdruck = abdruck, OhneNetz = ohne };
                _staende[lot] = neu;
                /*
                 * KEIN WEGENETZ (28.10.2026): "Parking Lot (192 spaces)" hatte
                 * seit Anfang Oktober keinen einzigen Weg, nur Zoning, Flaechen
                 * und Bauzettel. Eine Gegenprobe braucht es nicht - es gibt
                 * nichts, was der Kern anders planen koennte.
                 */
                if (ohne)
                {
                    if (ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var k0, out _, melden: false))
                        neu.LotId = ParkingLotToolSystem.LotId(k0.Punkte.Select(p => p.xz).ToArray());
                    neu.Geprueft = true;
                    if (neu.LotId != null && _bleibt.Contains(neu.LotId)) continue;
                    neu.Befund = Fehlt;
                    if (neu.LotId == null || _gemeldetOhneNetz.Add(neu.LotId))
                        Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + neu.LotId + ") hat kein Wegenetz - "
                            + "Reparieren wird angeboten (erst Gassen mit Stadtteilung, dann Neubau im Hintergrund).");
                    continue;
                }
                if (wege.Count == 0) continue;
                if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var kontext, out _, melden: false))
                    continue;
                var umriss = kontext.Punkte.Select(p => p.xz).ToArray();
                neu.LotId = ParkingLotToolSystem.LotId(umriss);
                neu.Verdacht = Auffaellig(wege, umriss, out var grund);
                if (neu.Verdacht != 0)
                {
                    Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + neu.LotId + ") auffaellig - "
                        + grund + ". Gegenprobe mit dem laufenden Kern folgt.");
                    if (neu.Verdacht == Ausserhalb) BeschreibeAusserhalb(lot, umriss, lots);
                }
            }
            foreach (var weg in _staende.Keys.Where(k => !lebend.Contains(k)).ToArray())
                _staende.Remove(weg);
        }

        private List<(float2 A, float2 B)> WegeDesTraegers(Entity lot)
        {
            var wege = new List<(float2 A, float2 B)>();
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (traeger == Entity.Null || !EntityManager.Exists(traeger)
                || !EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return wege;
            var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < puffer.Length; i++)
            {
                var kante = puffer[i].m_SubNet;
                if (!EntityManager.Exists(kante) || EntityManager.HasComponent<Deleted>(kante)
                    || !EntityManager.HasComponent<Game.Net.Edge>(kante)) continue;
                var edge = EntityManager.GetComponentData<Game.Net.Edge>(kante);
                if (!EntityManager.HasComponent<Game.Net.Node>(edge.m_Start)
                    || !EntityManager.HasComponent<Game.Net.Node>(edge.m_End)) continue;
                if (IstStrasse(kante)) continue;
                wege.Add((EntityManager.GetComponentData<Game.Net.Node>(edge.m_Start).m_Position.xz,
                    EntityManager.GetComponentData<Game.Net.Node>(edge.m_End).m_Position.xz));
            }
            return wege;
        }

        /*
         * WAS LIEGT DA AUSSERHALB? (28.10.2026) Nach der Reparatur von
         * "Parking Lot (192 spaces)" meldete die Pruefung "19 Weg(e) bis
         * 211,8 m ausserhalb" - Wege dieses Parkplatzes koennen das nicht sein.
         * Steht ein fremder Weg im SubNet des Traegers, nimmt CS2 ihn beim
         * Abriss mit. Deshalb je Ausreisser: Prefab, Besitzerkette und der
         * Parkplatz, in dessen Umriss er liegt.
         */
        private void BeschreibeAusserhalb(Entity lot, float2[] umriss, NativeArray<Entity> lots)
        {
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return;
            var prefabs = World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
            var umrisse = new List<(Entity Lot, float2[] Ring)>();
            foreach (var l in lots)
                if (ParkingLotBaukontextLeser.TryRead(EntityManager, l, out var k, out _, melden: false))
                    umrisse.Add((l, k.Punkte.Select(p => p.xz).ToArray()));
            var funde = new List<(float Abstand, string Text)>();
            var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < puffer.Length; i++)
            {
                var e = puffer[i].m_SubNet;
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                    || !EntityManager.HasComponent<Game.Net.Curve>(e) || IstStrasse(e)) continue;
                var c = EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
                var mitte = Colossal.Mathematics.MathUtils.Position(c, 0.5f).xz;
                var weit = math.max(AbstandAusserhalb(c.a.xz, umriss), math.max(AbstandAusserhalb(mitte, umriss), AbstandAusserhalb(c.d.xz, umriss)));
                if (weit <= Grenze) continue;
                var name = EntityManager.HasComponent<Game.Prefabs.PrefabRef>(e)
                    && prefabs.TryGetPrefab<Game.Prefabs.PrefabBase>(EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab, out var p)
                    && p != null ? p.name : "?";
                var kette = new List<string>();
                var o = e;
                for (var n = 0; n < 4 && EntityManager.HasComponent<Owner>(o); n++)
                {
                    o = EntityManager.GetComponentData<Owner>(o).m_Owner;
                    if (o == Entity.Null) break;
                    kette.Add(o.Index.ToString());
                }
                var inLot = "keinem";
                foreach (var (l, ring) in umrisse) if (AbstandAusserhalb(mitte, ring) == 0) { inLot = l.Index.ToString(); break; }
                funde.Add((weit, $"{e.Index}:{e.Version} '{name}' {weit:F1} m, Owner {(kette.Count > 0 ? string.Join(">", kette) : "keiner")}, "
                    + $"Mitte ({mitte.x:F1}/{mitte.y:F1}) im Umriss von Lot {inLot}"));
            }
            Mod.log.Info($"PLT-Wegpruefung: Lot {lot.Index}, Traeger {traeger.Index}: {funde.Count} Ausreisser im SubNet - "
                + string.Join(" | ", funde.OrderByDescending(f => f.Abstand).Take(6).Select(f => f.Text)) + ".");
        }

        /** Fuer den Sync-Bericht: jeder Parkplatz mit Befund. */
        internal string Zustandsbericht()
        {
            var text = new System.Text.StringBuilder();
            var vorbau = World.GetOrCreateSystemManaged<ParkingLotGassenvorbauSystem>();
            foreach (var paar in _staende.Where(p => p.Value.Befund != 0))
                text.AppendLine("  lot " + paar.Key.Index + " (" + paar.Value.LotId + "): finding "
                    + (paar.Value.Befund == Fehlt ? "no path network" : paar.Value.Befund == Knoten ? "too many paths at one node" : "paths outside")
                    + (paar.Value.LotId != null && _repariert.Contains(paar.Value.LotId) ? ", repair started" : "")
                    + (paar.Value.LotId != null && _bleibt.Contains(paar.Value.LotId) ? ", still wrong after repair" : "")
                    + (vorbau.Offen(paar.Key) ? ", alley pre-build queued" : ""));
            return text.Length == 0 ? "  none\n" : text.ToString();
        }

        /**
         * GASSEN UND ZONINGSTRASSEN ZAEHLEN NICHT (Spielerlog 11.10.2026).
         *
         * Sie sind echte Strassen, und CS2 verbindet eine Strasse an der
         * MITTE der Stadtstrasse: im Bericht 6B5R reicht "PLT Zufahrtsgasse
         * Einbahn" 16 m ueber den Umriss - so gewollt. Der Plan endet am
         * Strassenrand, die Gegenprobe war deshalb sauber, und "Reparieren"
         * erschien fuer vier Parkplaetze, die ein Neubau genauso baute.
         * Geprueft werden nur unsere unsichtbaren Wege (keine RoadData).
         */
        private bool IstStrasse(Entity kante)
        {
            if (!EntityManager.HasComponent<Game.Prefabs.PrefabRef>(kante)) return false;
            var prefab = EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(kante).m_Prefab;
            /*
             * VERSORGUNGSLEITUNGEN AUCH NICHT (28.10.2026): Nach der Reparatur
             * von "Parking Lot (192 spaces)" meldete die Pruefung "19 Weg(e)
             * bis 211,8 m ausserhalb" - die Messung zeigte Low-voltage Ground
             * Cable und Combined Small Pipe des eigenen Traegers, die zum
             * Stadtnetz laufen. Nach jedem Bau mit langer Leitung haette die
             * Liste sonst "Reparieren" angeboten.
             */
            return EntityManager.HasComponent<Game.Prefabs.RoadData>(prefab)
                || EntityManager.HasComponent<Game.Prefabs.ElectricityConnectionData>(prefab)
                || EntityManager.HasComponent<Game.Prefabs.WaterPipeConnectionData>(prefab);
        }

        /** Unabhaengig von der Reihenfolge; aendert sich, sobald ein Weg wandert. */
        private static long Abdruck(List<(float2 A, float2 B)> wege)
        {
            unchecked
            {
                long summe = wege.Count;
                foreach (var (a, b) in wege)
                {
                    long Schluessel(float2 p) => (long)math.round(p.x * 100) * 73856093L
                        ^ (long)math.round(p.y * 100) * 19349663L;
                    var ka = Schluessel(a);
                    var kb = Schluessel(b);
                    summe += (ka + kb) * 31 + (ka ^ kb);
                }
                return summe;
            }
        }

        /** Gemeinsame Pruefung fuer Welt und Plan; 0 heisst unauffaellig. */
        internal static int Auffaellig(IReadOnlyList<(float2 A, float2 B)> wege, float2[] umriss, out string grund)
        {
            grund = null;
            var draussen = 0;
            var weitester = 0f;
            foreach (var (a, b) in wege)
            {
                var groesster = 0f;
                foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                    groesster = math.max(groesster, AbstandAusserhalb(math.lerp(a, b, t), umriss));
                if (groesster <= Grenze) continue;
                draussen++;
                weitester = math.max(weitester, groesster);
            }
            if (draussen > 0)
            {
                grund = draussen + " Weg(e) bis " + weitester.ToString("F1") + " m ausserhalb des Umrisses";
                return Ausserhalb;
            }
            var grad = new Dictionary<(long, long), int>();
            foreach (var (a, b) in wege)
                foreach (var p in new[] { a, b })
                {
                    var k = ((long)math.round(p.x * 100), (long)math.round(p.y * 100));
                    grad[k] = grad.TryGetValue(k, out var n) ? n + 1 : 1;
                }
            var hoechster = grad.Count == 0 ? 0 : grad.Values.Max();
            if (hoechster >= Hoechstgrad)
            {
                grund = hoechster + " Wege an einem Knoten";
                return Knoten;
            }
            return 0;
        }

        /** 0 innerhalb, sonst der Abstand zum Umriss. */
        private static float AbstandAusserhalb(float2 p, float2[] umriss)
        {
            if (umriss == null || umriss.Length < 3) return 0;
            var innen = false;
            var naechster = float.MaxValue;
            for (int i = 0, j = umriss.Length - 1; i < umriss.Length; j = i++)
            {
                var a = umriss[i];
                var b = umriss[j];
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    innen = !innen;
                var ab = b - a;
                var t = math.clamp(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-9f), 0f, 1f);
                naechster = math.min(naechster, math.distance(p, a + ab * t));
            }
            return innen ? 0 : naechster;
        }

        // ------------------------------------------------------------------
        // Stufe 2: Gegenprobe mit dem laufenden Kern
        // ------------------------------------------------------------------

        private void StarteProbe()
        {
            foreach (var paar in _staende)
            {
                var s = paar.Value;
                if (s.Verdacht == 0 || s.Geprueft) continue;
                if (!ParkingLotBaukontextLeser.TryRead(EntityManager, paar.Key, out var kontext, out var grund, melden: false))
                {
                    s.Geprueft = true;
                    Mod.log.Warn("PLT-Wegpruefung: Lot " + paar.Key.Index + " ohne lesbaren Bauzettel, keine Gegenprobe: " + grund);
                    continue;
                }
                var settings = ParkingLotToolSystem.LayoutEinstellungen(kontext);
                var umriss = kontext.Punkte.Select(p => p.xz).ToArray();
                _probeLot = paar.Key;
                _probeAbdruck = s.Abdruck;
                _probe = Task.Run(() =>
                {
                    var layout = ParkingGeometry.Build(umriss, settings);
                    // Dieselbe Auswahl wie in der Welt: ohne Gassen und Zoningstrassen.
                    var wege = layout.NetLine
                        .Where(n => n.Kind != "zoning" && !Zufahrtsarten.IstGasse(n.Art))
                        .Select(n => (n.A, n.B)).ToList();
                    return Auffaellig(wege, umriss, out _);
                });
                return;
            }
        }

        private void Auswerten()
        {
            var probe = _probe;
            var lot = _probeLot;
            _probe = null;
            _probeLot = Entity.Null;
            // Inzwischen abgerissen, neu gebaut oder verschoben: der naechste Takt prueft neu.
            if (!_staende.TryGetValue(lot, out var s) || s.Abdruck != _probeAbdruck) return;
            s.Geprueft = true;
            if (probe.IsFaulted)
            {
                Mod.log.Warn("PLT-Wegpruefung: Gegenprobe fuer Lot " + lot.Index + " fehlgeschlagen: "
                    + probe.Exception?.GetBaseException().Message);
                return;
            }
            if (probe.Result != 0)
            {
                Mod.log.Warn("PLT-Wegpruefung: Lot " + lot.Index + " (" + s.LotId + ") - auch der laufende Kern plant "
                    + "es so; ein Neubau aenderte nichts, deshalb kein Reparieren-Angebot.");
                return;
            }
            if (s.LotId != null && _repariert.Contains(s.LotId))
            {
                _bleibt.Add(s.LotId);
                Mod.log.Warn("PLT-Wegpruefung: Lot " + lot.Index + " (" + s.LotId + ") ist nach dem Neubau weiter "
                    + "auffaellig, obwohl der Plan sauber ist - kein weiteres Reparieren-Angebot.");
                return;
            }
            if (s.LotId != null && _bleibt.Contains(s.LotId)) return;
            s.Befund = s.Verdacht;
            Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + s.LotId + ") - der laufende Kern plant es "
                + "sauber; Reparieren wird angeboten.");
        }

        // ------------------------------------------------------------------
        // Reparieren
        // ------------------------------------------------------------------

        internal void ReparierenAlle()
        {
            var sync = World.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
            if (sync.SyncLaeuft) { sync.SperrtWegenArbeit("Reparieren"); return; }
            foreach (var lot in _staende.Where(p => p.Value.Befund != 0).Select(p => p.Key).ToArray())
                Reparieren(lot);
        }

        private void RepariereAutomatisch()
        {
            if (World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().SyncLaeuft) return;
            var werkzeug = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            foreach (var paar in _staende.Where(p => p.Value.Befund != 0).ToArray())
            {
                if (paar.Value.LotId == null || _repariert.Contains(paar.Value.LotId)) continue;
                // Ohne Wegenetz IST die Veraenderung der Schaden - die Automatik darf.
                if (paar.Value.Befund != Fehlt && werkzeug.WegeSeitBauVeraendert(paar.Key))
                {
                    if (_autoAusgelassen.Add(paar.Value.LotId))
                        Mod.log.Info("PLT-Wegpruefung: Lot " + paar.Key.Index + " wurde nach dem Bau veraendert - "
                            + "keine automatische Reparatur, nur per Klick.");
                    continue;
                }
                Reparieren(paar.Key);
            }
        }

        /** Neubau im Hintergrund mit derselben Form und denselben Einstellungen. */
        internal void Reparieren(Entity lot)
        {
            if (!_staende.TryGetValue(lot, out var s) || s.Befund == 0) return;
            var hintergrund = World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
            if (hintergrund.Gesperrt(lot)) return;
            var sync = World.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
            if (sync.SyncLaeuft) { sync.SperrtWegenArbeit("Reparieren"); return; }
            if (s.LotId != null) _repariert.Add(s.LotId);
            if (s.Befund == Fehlt)
            {
                /*
                 * Der Hintergrund-Neubau darf keine Stadtstrasse teilen. Die
                 * Gassen, die das brauchen, baut deshalb zuerst der
                 * Gassenvorbau wie das Edit; danach findet der Neubau sie als
                 * unveraenderte Gassen. Scheitert der Vorbau, bleibt der
                 * Parkplatz, wie er ist.
                 */
                var vorbau = World.GetOrCreateSystemManaged<ParkingLotGassenvorbauSystem>();
                if (vorbau.Offen(lot)) return;
                Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + s.LotId + ") ohne Wegenetz: "
                    + "erst Gassen mit Stadtteilung, dann Neubau im Hintergrund.");
                vorbau.Einreihen(lot, (ok, text) =>
                {
                    if (!ok)
                    {
                        Mod.log.Warn("PLT-Wegpruefung: Lot " + lot.Index + " - Gassenvorbau gescheitert (" + text
                            + "); kein Neubau, Parkplatz unveraendert.");
                        return;
                    }
                    if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)) return;
                    Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + s.LotId + ") wird im Hintergrund neu gebaut.");
                    World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>().Einreihen(lot);
                });
                return;
            }
            Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + s.LotId + ") wird im Hintergrund neu gebaut.");
            hintergrund.Einreihen(lot);
        }
    }
}
