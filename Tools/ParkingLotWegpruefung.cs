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

        /** 1 = Wege ausserhalb des Umrisses, 2 = zu viele Wege an einem Knoten. */
        internal const int Ausserhalb = 1, Knoten = 2;

        private sealed class Stand
        {
            internal long Abdruck;
            internal string LotId;
            internal int Verdacht;
            internal bool Geprueft;
            internal int Befund;
        }

        private EntityQuery _lots;
        private readonly Dictionary<Entity, Stand> _staende = new();
        /** Umrisse, deren Neubau dieses System angestossen hat. */
        private readonly HashSet<string> _repariert = new(StringComparer.Ordinal);
        /** Nach dem Neubau weiter auffaellig: nicht mehr anbieten. */
        private readonly HashSet<string> _bleibt = new(StringComparer.Ordinal);
        /** Nach dem Bau veraendert - einmal gemeldet, dass die Automatik sie auslaesst. */
        private readonly HashSet<string> _autoAusgelassen = new(StringComparer.Ordinal);
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
            foreach (var lot in lots)
            {
                lebend.Add(lot);
                var wege = WegeDesTraegers(lot);
                var abdruck = Abdruck(wege);
                if (_staende.TryGetValue(lot, out var alt) && alt.Abdruck == abdruck) continue;
                var neu = new Stand { Abdruck = abdruck };
                _staende[lot] = neu;
                if (wege.Count == 0) continue;
                if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var kontext, out _, melden: false))
                    continue;
                var umriss = kontext.Punkte.Select(p => p.xz).ToArray();
                neu.LotId = ParkingLotToolSystem.LotId(umriss);
                neu.Verdacht = Auffaellig(wege, umriss, out var grund);
                if (neu.Verdacht != 0)
                    Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + neu.LotId + ") auffaellig - "
                        + grund + ". Gegenprobe mit dem laufenden Kern folgt.");
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
                wege.Add((EntityManager.GetComponentData<Game.Net.Node>(edge.m_Start).m_Position.xz,
                    EntityManager.GetComponentData<Game.Net.Node>(edge.m_End).m_Position.xz));
            }
            return wege;
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
                    var wege = layout.NetLine.Select(n => (n.A, n.B)).ToList();
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
                if (werkzeug.WegeSeitBauVeraendert(paar.Key))
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
            Mod.log.Info("PLT-Wegpruefung: Lot " + lot.Index + " (" + s.LotId + ") wird im Hintergrund neu gebaut.");
            hintergrund.Einreihen(lot);
        }
    }
}
