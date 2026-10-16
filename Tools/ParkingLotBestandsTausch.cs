using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Was der Bestandstausch an einem Parkplatz erneuert. */
    internal enum Tauschart
    {
        /** Sync-Schritt 10: alle Pflanzen neu nach dem Wuchs nach Hoehe. */
        Pflanzen,
        /** Sync-Schritt 11: Laternen dazu; Pflanzen auf ihren Plaetzen weichen. */
        Laternen,
    }

    /*
     * DEN BESTAND TAUSCHEN STATT NEU BAUEN (Sync-Schritte 10 und 11, 2026-10-04).
     *
     * Am Bestand aendert sich in beiden Faellen nur ein Teil der Objekte:
     * - Pflanzen: Buesche mit Altersstufen wurden bis heute wie Baeume
     *   gepflanzt. Nutzer: "Das auch als Sync, dass auch die neue Dichte passt."
     *   Alle Pflanzen werden neu gesetzt, gleiche Einstellungen und Seed.
     * - Laternen: alte Parkplaetze bekommen sie mit dem Standard des Spielers.
     *   Nutzer: "Laterne ist wichtiger als Baum oder Busch" - Pflanzen im
     *   Freiraum einer Laterne (3 m wenn hoeher als der Mast, sonst 0,5 m)
     *   werden geloescht.
     *
     * Beides nach dem Muster des Fahrwegtauschs: loeschen wie der Bulldozer
     * (CreationDefinition m_Original + Delete) und anlegen mit derselben
     * Definition wie der Bau - als EINE Vorschau, EIN Apply. Wechselt der
     * Nutzer mittendrin das Werkzeug, wird verworfen und nichts aendert sich.
     *
     * Grundlage ist das aus dem Bauzettel nachgerechnete Layout, wie beim Bau;
     * die gebauten Flaechen verraten ihre Rolle nicht. Gegen eine seit dem
     * Bau geaenderte Geometrie schuetzt ein Abgleich mit dem Bestand: fuer
     * Pflanzen muessen die alten fast alle im neuen Gruen liegen, fuer
     * Laternen die geplanten Buchtaufkleber fast alle auf gebauten. Sonst
     * bleibt der Parkplatz unveraendert und wird gemeldet.
     */
    public sealed partial class ParkingLotBestandsTauschSystem : GameSystemBase
    {
        private const float MindestDeckung = 0.98f;
        private const float MindestAufkleber = 0.95f;

        private readonly List<(Entity Lot, Tauschart Art, Auftragskennung Kennung)> _warteschlange = new();
        private ToolSystem _toolSystem;
        private ParkingLotBestandsTauschWerkzeug _werkzeug;
        private ParkingLotToolSystem _plt;
        private ParkingLotUISystem _ui;
        private ToolBaseSystem _vorher;
        private bool _alleOk = true;
        private int _ruheBis;
        private EntityQuery _teile;

        private sealed class Ergebnis
        {
            internal VegetationPlan Pflanzen;
            internal LaternenPlan Laternen;
            internal string Fehler;
            internal string Messung;
            /*
             * BEWUSST UEBERSPRUNGEN (2026-10-16).
             *
             * Weicht der Bestand vom Bauzettel ab (Move It, eine quer gebaute
             * Strasse), tauscht der Schritt aus Vorsicht nichts. Als Fehler
             * gemeldet wurde der Schritt aber nie fertig: Parkplatz 461043
             * eines Spielstands vom 3.10. stand bei jedem Laden wieder auf
             * "braucht Sync". Die Entscheidung "nicht anfassen" ist endgueltig,
             * also gilt der Schritt als erledigt; der Bestand bleibt unveraendert.
             * Wer die Laternen/Pflanzen trotzdem will, baut den Parkplatz per
             * Edit neu. Es wird nichts erfunden, auch kein Laternenzettel.
             */
            internal bool Uebersprungen;
        }

        private sealed class Vorbereitung
        {
            internal Entity Lot, Traeger;
            internal Tauschart Art;
            internal Auftragskennung Kennung;
            internal VegetationOptions Vegetation;
            internal VegetationAsset[] Arten = Array.Empty<VegetationAsset>();
            internal LaternenOptionen Laternen;
            internal readonly List<(Entity E, float2 P, Entity Prefab)> Pflanzen = new();
            internal Task<Ergebnis> Rechnung;
        }

        private Vorbereitung _laeuft;

        /** Was gerade im Tauschwerkzeug steckt - gegen doppeltes Einreihen. */
        private (Entity Lot, Tauschart Art)? _imWerkzeug;

        /*
         * DER ZAEHLER ZAEHLT DAS LAUFENDE WERKZEUG MIT (1.0.6). Vorher fehlte
         * es: waehrend der erste Parkplatz im Werkzeug steckte, stand schon
         * "1/3" da - und blieb so, wenn er dort haengen blieb (MakaPakaUK).
         */
        internal int Offen => _warteschlange.Count + (_laeuft != null ? 1 : 0) + (_werkzeug.Beschaeftigt ? 1 : 0);

        /*
         * NUR DAS WERKZEUG SPERRT DEN ANDEREN TAUSCH (1.0.6).
         *
         * Vorher hiess "beschaeftigt" auch: rechnet gerade oder hat sein
         * Werkzeug zwischen zwei Parkplaetzen noch aktiv. Der Fahrwegtausch
         * wartete darauf, der Bestandstausch umgekehrt auf die WARTESCHLANGE
         * des Fahrwegtauschs - beide warteten fuer immer (gemischte Bestaende
         * mit Schritt 9 und 10/11). Jetzt sperrt nur, was wirklich das
         * aktive Werkzeug braucht: ein laufender Tausch im Werkzeug.
         */
        internal bool WerkzeugLaeuft => _werkzeug.Beschaeftigt;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _werkzeug = World.GetOrCreateSystemManaged<ParkingLotBestandsTauschWerkzeug>();
            _plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _ui = World.GetOrCreateSystemManaged<ParkingLotUISystem>();
            _teile = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotPartRelation>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            // Das Werkzeug laeuft nicht mehr, sobald ein anderes aktiv ist -
            // dann fuehrt es der Controller zu Ende oder bricht es ab.
            if (_werkzeug.Beschaeftigt) { _werkzeug.PflegeOhneWerkzeug(); return; }
            if (_laeuft == null)
            {
                if (_warteschlange.Count == 0 || UnityEngine.Time.frameCount < _ruheBis) return;
                var (lot, art, id) = _warteschlange[0];
                _warteschlange.RemoveAt(0);
                if (!Auftraege.Starte(id)) return;
                _kennung = id;
                /*
                 * ERST UEBERNEHMEN, WENN DIE RECHNUNG LAEUFT (Sync-Haenger, 2026-10-12).
                 * `out _laeuft` schrieb direkt ins Feld: warf `Bereite` nach dem
                 * Anlegen, aber vor `Task.Run`, blieb `_laeuft` mit Rechnung
                 * null stehen, jedes weitere Bild warf, und der Auftrag endete
                 * nie - der Sync blieb gesperrt. Jetzt endet jeder Fehler hier
                 * mit genau einem Fertig(false).
                 */
                Vorbereitung neu = null;
                string fehler;
                try { fehler = Bereite(lot, art, out neu); }
                catch (Exception x) { fehler = "Vorbereitung fehlgeschlagen: " + x.Message; }
                if (fehler == null && neu?.Rechnung == null) fehler = "Vorbereitung ohne Rechnung";
                if (fehler != null) { Fertig(lot, id, false, fehler); return; }
                neu.Kennung = id;
                _laeuft = neu;
                return;
            }
            var v = _laeuft;
            if (!RechnungLesen(v, out var e)) return;
            // Das Rechnen laeuft ohne ECS. Danach kann der Nutzer den
            // Parkplatz schon abgerissen oder der Ladepfad ihn ersetzt haben.
            if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, v.Lot, v.Traeger))
            { _laeuft = null; Fertig(v.Lot, v.Kennung, false, "Bestand waehrend der Vorbereitung verschwunden"); return; }
            if (e.Fehler != null)
            {
                _laeuft = null;
                Fertig(v.Lot, v.Kennung, e.Uebersprungen, e.Uebersprungen
                    ? "bewusst uebersprungen, Schritt gilt als erledigt (neue Laternen/Pflanzen per Edit) - " + e.Fehler
                    : e.Fehler);
                return;
            }
            // Ein Werkzeugwechsel wuerde einen laufenden Entwurf verwerfen.
            if (_toolSystem.activeTool == _plt && _plt.ArbeitetGerade) return;
            // Nie zwei Tauschwerkzeuge gleichzeitig: jedes setzt das aktive Werkzeug.
            if (World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().WerkzeugLaeuft) return;
            if (World.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>().Laeuft) return;
            _laeuft = null;
            /*
             * UEBERGABE MIT GENAU EINEM ABSCHLUSS (Sync-Haenger, 2026-10-12).
             * Warf Auftragsbau oder Werkzeugwechsel, meldete sich der Auftrag
             * nie fertig. Und `try { Fertig(.., Nachsetzen()) }` fing auch eine
             * Ausnahme AUS Fertig - dann kam nach dem Erfolg noch ein Fehlschlag.
             */
            ParkingLotBestandsTauschWerkzeug.Auftrag auftrag;
            try
            {
                auftrag = v.Art == Tauschart.Pflanzen ? PflanzenAuftrag(v, e) : LaternenAuftrag(v, e);
                auftrag.Kennung = v.Kennung;
                Mod.log.Info($"PLT-Bestandstausch: Lot {v.Lot.Index} ({v.Art}): {e.Messung}; loeschen {auftrag.Loeschen.Count}.");
            }
            catch (Exception x) { Fertig(v.Lot, v.Kennung, false, "Auftrag fehlgeschlagen: " + x.Message); return; }
            if (auftrag.Loeschen.Count == 0 && auftrag.NichtsAnzulegen)
            {
                // Nichts in der Welt zu tun - nur der Zettel.
                string bericht;
                try { bericht = auftrag.Nachsetzen(); }
                catch (Exception x) { Fertig(v.Lot, v.Kennung, false, x.Message); return; }
                Fertig(v.Lot, v.Kennung, true, bericht);
                return;
            }
            try
            {
                if (_toolSystem.activeTool != _werkzeug) _vorher = _toolSystem.activeTool;
                _imWerkzeug = (v.Lot, v.Art);
                _werkzeug.Fertig = Fertig;
                /*
                 * LATERNENZETTEL VOR DEM APPLY (16.10.). Er stand erst im Nachsetzen,
                 * 3+30 Bilder nach dem Apply. Ein Speichern dazwischen behielt die
                 * Laternen ohne Zettel - und weil Laternen standen, holte Schritt 11
                 * ihn nie nach; ein spaeteres Edit nahm den Standard statt der Wahl.
                 * Jetzt zuerst: kommt der Tausch nicht zum Apply, sagt der Zettel
                 * "an" ohne Laterne, und genau das baut Schritt 12 nach.
                 */
                if (v.Art == Tauschart.Laternen && v.Laternen != null) _plt.SchreibeLaternenZettel(v.Lot, v.Laternen);
                _werkzeug.Starte(auftrag);
                _toolSystem.activeTool = _werkzeug;
            }
            catch (Exception x)
            {
                Diagnose(() => Mod.log.Warn("PLT-Bestandstausch: Uebergabe ans Werkzeug fehlgeschlagen: " + x));
                // Laeuft das Werkzeug schon, meldet es selbst; sonst hier.
                if (!_werkzeug.BrichAb("Uebergabe fehlgeschlagen: " + x.Message))
                    Fertig(v.Lot, v.Kennung, false, "Uebergabe fehlgeschlagen: " + x.Message);
            }
        }

        /*
         * BEIM LADEN ALLES VERGESSEN (1.0.6). Warteschlange, Vorbereitung und
         * Werkzeugzustand trugen Entities der alten Welt in den neuen
         * Spielstand; ein haengender Tausch hielt so bis zum Spielneustart.
         * Der Sync baut seine Liste nach dem Laden ohnehin neu auf.
         */
        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            if (_warteschlange.Count > 0 || _laeuft != null || _werkzeug.Beschaeftigt)
                Mod.log.Info($"PLT-Bestandstausch: Laden - {_warteschlange.Count} eingereiht, "
                    + $"{(_laeuft != null ? 1 : 0)} vorbereitet, Werkzeug {(_werkzeug.Beschaeftigt ? "beschaeftigt" : "frei")} verworfen.");
            _warteschlange.Clear();
            _laeuft = null;
            _imWerkzeug = null;
            _kennung = default;
            _vorher = null;
            _alleOk = true;
            _ruheBis = 0;
            _werkzeug.Zuruecksetzen();
        }

        /** Sammelt alles auf dem Spielthread und startet die Rechnung im Hintergrund. Fehlertext oder null. */
        private string Bereite(Entity lot, Tauschart art, out Vorbereitung v)
        {
            v = null;
            if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)
                || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return "Lot existiert nicht mehr";
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (!EntityManager.Exists(traeger)) return "Traeger fehlt";
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var kontext, out var lesefehler, melden: false))
                return "Bauzettel unlesbar: " + lesefehler;
            v = new Vorbereitung { Lot = lot, Traeger = traeger, Art = art, Vegetation = _plt.VegetationVon(lot) };
            var laternen = new List<LaternenKoerper>();
            var objekte = new List<float2>();
            using (var teile = _teile.ToEntityArray(Allocator.Temp))
                foreach (var t in teile)
                {
                    if (EntityManager.GetComponentData<ParkingLotPartRelation>(t).Lot != lot) continue;
                    if (!EntityManager.HasComponent<Game.Objects.Transform>(t)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(t).m_Prefab;
                    var lage = EntityManager.GetComponentData<Game.Objects.Transform>(t);
                    if (EntityManager.HasComponent<PlantData>(prefab)) v.Pflanzen.Add((t, lage.m_Position.xz, prefab));
                    else if (EntityManager.HasComponent<StreetLight>(t) && EntityManager.HasComponent<ObjectGeometryData>(prefab))
                    {
                        var g = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                        laternen.Add(new LaternenKoerper
                        {
                            Position = lage.m_Position.xz,
                            Vorwaerts = math.normalizesafe(math.mul(lage.m_Rotation, new float3(0, 0, 1)).xz),
                            Bein = (g.m_Flags & GeometryFlags.Standing) != 0 ? g.m_LegSize : float3.zero,
                            Max = g.m_Bounds.max,
                        });
                    }
                    else objekte.Add(lage.m_Position.xz);
                }
            var settings = ParkingLotToolSystem.LayoutEinstellungen(kontext);
            var polygon = kontext.Punkte.Select(p => p.xz).ToArray();
            var altePositionen = v.Pflanzen.Select(p => p.P).ToList();
            if (art == Tauschart.Pflanzen)
            {
                var optionen = v.Vegetation;
                v.Arten = _ui.VegetationAssets.Where(a => optionen.Species.Contains(a.Id)).ToArray();
                if (v.Arten.Length == 0) return "keine der gewaehlten Arten im Katalog";
                var species = v.Arten.Select(a => new VegetationSpecies { Id = a.Id, Tree = a.Tree, Spacing = a.Spacing, Hoehe = a.Hoehe }).ToArray();
                ParkingVegetation.Dichtefaktor = Mod.Optionen?.Vegetationsdichte ?? 1f;
                v.Rechnung = Task.Run(() =>
                {
                    var layout = ParkingGeometry.Build(polygon, settings);
                    var gras = layout.GrassForVegetation ?? Array.Empty<float2[]>();
                    var deckung = Anteil(altePositionen, p => gras.Any(r => r != null && r.Length >= 3 && InRing(p, r)));
                    if (deckung < MindestDeckung)
                        return new Ergebnis { Uebersprungen = true, Fehler = $"nur {deckung:P0} der alten Pflanzen liegen im nachgerechneten Gruen - "
                            + "die Geometrie hat sich seit dem Bau geaendert; Parkplatz bleibt unveraendert" };
                    var plan = ParkingVegetation.Plan(gras, optionen, species, laternen);
                    return new Ergebnis { Pflanzen = plan, Messung = $"{altePositionen.Count} alte Pflanzen, {plan.Plants.Count} neue geplant, "
                        + $"alte im nachgerechneten Gruen {deckung:P0}, an Laternen verworfen {plan.LaternenVerworfen}" };
                });
            }
            else
            {
                v.Laternen = _plt.LaternenVon(lot) ?? _ui.LaternenStandardOptionen;
                var optionen = v.Laternen;
                v.Rechnung = Task.Run(() =>
                {
                    var layout = ParkingGeometry.Build(polygon, settings);
                    var aufkleber = ParkingBayDecals.Plan(layout, settings).Placements.Select(p => (float2)p.Center).ToList();
                    var treffer = Anteil(aufkleber, p => objekte.Any(o => math.distancesq(o, p) < 0.36f));
                    if (aufkleber.Count > 0 && treffer < MindestAufkleber)
                        return new Ergebnis { Uebersprungen = true, Fehler = $"nur {treffer:P0} der nachgerechneten Buchten treffen einen gebauten Aufkleber - "
                            + "die Geometrie hat sich seit dem Bau geaendert; Parkplatz bleibt unveraendert" };
                    var plan = ParkingLotToolSystem.LaternenPlanFuer(layout, optionen);
                    return new Ergebnis { Laternen = plan, Messung = $"{plan.Laternen.Count} Laternen geplant, "
                        + $"Buchtaufkleber getroffen {treffer:P0} von {aufkleber.Count}" };
                });
            }
            return null;
        }

        private static float Anteil(List<float2> punkte, Func<float2, bool> trifft)
            => punkte.Count == 0 ? 1f : punkte.Count(trifft) / (float)punkte.Count;

        private static bool InRing(float2 p, float2[] ring)
        {
            var innen = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y)
                    && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                    innen = !innen;
            return innen;
        }

        /** Schritt 10: alle alten Pflanzen raus, der neue Plan rein, danach Baumzustaende und Zettel Version 2. */
        private ParkingLotBestandsTauschWerkzeug.Auftrag PflanzenAuftrag(Vorbereitung v, Ergebnis e)
        {
            var ziele = new Dictionary<(Entity, float2), Game.Objects.Tree>();
            var a = new ParkingLotBestandsTauschWerkzeug.Auftrag
            {
                Lot = v.Lot, Traeger = v.Traeger, Name = "Pflanzentausch",
                Loeschen = v.Pflanzen.Select(p => p.E).ToList(),
                Prefabs = new HashSet<Entity>(v.Arten.Select(x => x.Prefab)),
                NichtsAnzulegen = e.Pflanzen.Plants.Count == 0,
            };
            a.Anlegen = (definitionen, hoehen) =>
            {
                var n = 0;
                foreach (var pflanze in e.Pflanzen.Plants)
                {
                    var asset = v.Arten[pflanze.Species];
                    var d = _plt.PflanzenDefinition(asset, pflanze, v.Vegetation, ref hoehen, out var position, out _, out var ziel);
                    if (d == Entity.Null) continue;
                    definitionen.Add(d);
                    n++;
                    if (ziel.HasValue) ziele[(asset.Prefab, position.xz)] = ziel.Value;
                }
                return n;
            };
            a.Nachsetzen = () =>
            {
                var gesetzt = _werkzeug.SetzeBaumzustaende(ziele);
                _plt.MarkiereVegetationNachWuchs(v.Lot);
                return $"{gesetzt} Baumzustaende gesetzt, Vegetationszettel Version 2";
            };
            return a;
        }

        /** Schritt 11: Laternen rein, Pflanzen in ihrem Freiraum raus, danach der Laternenzettel. */
        private ParkingLotBestandsTauschWerkzeug.Auftrag LaternenAuftrag(Vorbereitung v, Ergebnis e)
        {
            var koerper = _plt.LaternenKoerperFuer(e.Laternen, v.Laternen);
            var hoehen = _ui.VegetationAssets.Where(x => x.Prefab != Entity.Null).GroupBy(x => x.Prefab).ToDictionary(g => g.Key, g => g.First().Hoehe);
            var weichen = new List<Entity>();
            foreach (var (pflanze, p, prefab) in v.Pflanzen)
            {
                var hoehe = hoehen.TryGetValue(prefab, out var h) && h > 0 ? h
                    : EntityManager.HasComponent<TreeData>(prefab) ? float.MaxValue : 0f;
                foreach (var k in koerper)
                {
                    var frei = hoehe > k.Masthoehe ? 3f : 0.5f;
                    if (math.distancesq(p, k.Position) < frei * frei) { weichen.Add(pflanze); break; }
                }
            }
            var prefabs = new HashSet<Entity>();
            foreach (var platz in e.Laternen.Laternen)
            {
                var prefab = _plt.LaternenPrefab(v.Laternen.ModellFuer(platz));
                if (prefab != Entity.Null) prefabs.Add(prefab);
            }
            var a = new ParkingLotBestandsTauschWerkzeug.Auftrag
            {
                Lot = v.Lot, Traeger = v.Traeger, Name = "Laternen nachruesten",
                Loeschen = weichen, Prefabs = prefabs, NichtsAnzulegen = prefabs.Count == 0,
            };
            a.Anlegen = (definitionen, hoehenDaten) =>
            {
                var n = 0;
                foreach (var platz in e.Laternen.Laternen)
                {
                    var d = _plt.LaternenDefinition(platz, v.Laternen, ref hoehenDaten, out _, out _, out _);
                    if (d == Entity.Null) continue;
                    definitionen.Add(d);
                    n++;
                }
                return n;
            };
            a.Nachsetzen = () =>
            {
                _plt.SchreibeLaternenZettel(v.Lot, v.Laternen);
                return $"{weichen.Count} Pflanzen gewichen, Laternenzettel geschrieben";
            };
            return a;
        }

        private void Freigeben(bool ok)
        {
            if (!ok) _alleOk = false;
            _imWerkzeug = null;
            _ruheBis = UnityEngine.Time.frameCount + 10;
            if (_warteschlange.Count > 0 || _laeuft != null) return;
            // Alles erledigt: voriges Werkzeug zurueck (oeffnet ein vorher offenes
            // PLT-Panel wieder). Nach einem Fehlschlag bleibt das Panel zu.
            // Nie zurueck auf ein Tauschwerkzeug: das stuende danach leer und aktiv.
            var zurueck = _alleOk && _vorher != null && _vorher != _werkzeug
                && !(_vorher is ParkingLotFahrwegTauschWerkzeug) ? _vorher : null;
            if (_toolSystem.activeTool == _werkzeug)
                _toolSystem.activeTool = zurueck ?? World.GetOrCreateSystemManaged<DefaultToolSystem>();
            _vorher = null;
            _alleOk = true;
        }
    }

    public sealed partial class ParkingLotBestandsTauschWerkzeug : ToolBaseSystem
    {
        public const string Id = "PLT Bestandstausch";
        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        internal sealed class Auftrag
        {
            internal Entity Lot, Traeger;
            internal Auftragskennung Kennung;
            internal string Name;
            internal List<Entity> Loeschen = new();
            /** Prefabs der neuen Objekte - daran erkennt der Tausch seine Vorschau. */
            internal HashSet<Entity> Prefabs = new();
            internal bool NichtsAnzulegen;
            /** Legt die neuen Definitionen an und gibt ihre Zahl zurueck. */
            internal Func<List<Entity>, TerrainHeightData, int> Anlegen;
            /** Nach dem Apply am dauerhaften Bestand; Rueckgabe fuers Log, Ausnahme = gescheitert. */
            internal Func<string> Nachsetzen;
        }

        private enum Stufe { Frei, Anlegen, Warten, Uebernehmen, Pruefen, Nachsetzen }

        private Stufe _stufe;
        private Auftrag _a;
        private readonly HashSet<Entity> _loeschMenge = new();
        private readonly List<Entity> _definitionen = new();
        private int _geplant, _seit;
        /** Lief OnUpdate seit dem Start schon einmal? Sonst ist das Werkzeug nie aktiv geworden. */
        private bool _lief, _befundGemeldet;
        private EntityQuery _fremdeDefinitionen, _alteDefinitionen, _temps, _teile;
        private ParkingLotToolSystem _plt;
        private TerrainSystem _terrain;
        internal Action<Entity, Auftragskennung, bool, string> Fertig;

        internal bool Beschaeftigt => _stufe != Stufe.Frei;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _fremdeDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
            _alteDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.Exclude<Updated>(), ComponentType.Exclude<ParkingLotAuftragsdefinition>());
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
            _teile = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotPartRelation>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        internal void Starte(Auftrag auftrag)
        {
            if (!World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().DarfAnwenden(auftrag.Kennung)) return;
            _a = auftrag;
            _loeschMenge.Clear(); foreach (var e in auftrag.Loeschen) _loeschMenge.Add(e);
            _definitionen.Clear(); _geplant = 0;
            _stufe = Stufe.Anlegen;
            _seit = UnityEngine.Time.frameCount;
            _lief = false;
            _befundGemeldet = false;
        }

        /*
         * WENN EIN ANDERES WERKZEUG AKTIV IST (1.0.6).
         *
         * CS2 schaltet ein Werkzeug beim Wechsel ab (`ToolSystem.ToolUpdate`:
         * `Enabled = false`), und Unity ruft fuer ein abgeschaltetes System
         * KEIN `OnUpdate` mehr auf (`SystemBase.Update`). Der Abbruchzweig
         * oben in `OnUpdate` war deshalb tot: ein Werkzeugwechsel mitten im
         * Tausch liess ihn fuer immer "beschaeftigt", der Sync hing.
         *
         * Jetzt ruft der Controller (laeuft immer) diese Methode. Vor dem
         * Apply wird verworfen - es hat sich nichts geaendert. Nach dem Apply
         * wird zu Ende gefuehrt: Pruefen und Nachsetzen brauchen kein aktives
         * Werkzeug, nur ihre Wartezeit.
         */
        internal void PflegeOhneWerkzeug()
        {
            if (VerwirfVeraltetenAuftrag() || _stufe == Stufe.Frei || m_ToolSystem.activeTool == this) return;
            try { PflegeOhneWerkzeugKern(); }
            catch (Exception x) { BrichAbNachAusnahme(x); }
        }

        private void PflegeOhneWerkzeugKern()
        {
            var bild = UnityEngine.Time.frameCount;
            if (_stufe == Stufe.Pruefen || _stufe == Stufe.Nachsetzen) { NachDemApply(bild); return; }
            // Ein frisch gesetztes Werkzeug wird erst im naechsten Bild aktiv.
            if (!_lief && bild - _seit < 10) return;
            Beende(false, _lief ? "Werkzeug gewechselt, Tausch verworfen" : "Werkzeug wurde nicht aktiv, Tausch verworfen");
        }

        /** Beim Laden: alles vergessen, ohne Rueckmeldung (der Sync startet neu). */
        internal void Zuruecksetzen()
        {
            _definitionen.Clear();
            _loeschMenge.Clear();
            _stufe = Stufe.Frei;
            _a = null;
            _lief = false;
        }

        private void NachDemApply(int bild)
        {
            if (VerwirfVeraltetenAuftrag()) return;
            if (_a == null || !ParkingLotSyncGueltigkeit.Bestand(EntityManager, _a.Lot, _a.Traeger))
            { Beende(false, "Bestand nach Apply verschwunden; kein Nachsetzen"); return; }
            switch (_stufe)
            {
                case Stufe.Pruefen:
                    if (bild - _seit < 3) break;
                    if (!Pruefe(out var text)) { Beende(false, text); break; }
                    _stufe = Stufe.Nachsetzen;
                    _seit = bild;
                    break;
                case Stufe.Nachsetzen:
                    // Wie nach dem Bau: Zustaende gehoeren an den DAUERHAFTEN Bestand.
                    if (bild - _seit < 30) break;
                    string bericht;
                    try { bericht = _a.Nachsetzen(); }
                    catch (Exception e) { Beende(false, "Nachsetzen gescheitert: " + e.Message); break; }
                    Beende(true, $"{_a.Loeschen.Count} geloescht, {_geplant} neu angelegt, {bericht}");
                    break;
            }
        }

        /** Bricht einen laufenden Tausch mit Fehlschlag ab; false, wenn keiner lief. */
        internal bool BrichAb(string text)
        {
            if (_stufe == Stufe.Frei) return false;
            Beende(false, text);
            return true;
        }

        /*
         * JEDE AUSNAHME BEENDET DEN TAUSCH (Sync-Haenger, 2026-10-12).
         * Sonst blieb die Stufe stehen, jedes Bild warf erneut, und
         * `Beschaeftigt` sperrte den Sync fuer immer. Vor dem Apply werden
         * unsere Temps mit verworfen; danach gibt es keine mehr.
         */
        private void BrichAbNachAusnahme(Exception x)
        {
            if (VerwirfVeraltetenAuftrag()) return;
            try { Mod.log.Warn("PLT-Bestandstausch: Ausnahme in Stufe " + _stufe + ": " + x); }
            catch (Exception) { }
            if (_stufe == Stufe.Anlegen || _stufe == Stufe.Warten || _stufe == Stufe.Uebernehmen)
                applyMode = ApplyMode.Clear;
            BrichAb("Ausnahme: " + x.Message);
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            try { Schritt(); }
            catch (Exception x) { BrichAbNachAusnahme(x); }
            return inputDeps;
        }

        private void Schritt()
        {
            if (VerwirfVeraltetenAuftrag()) return;
            var bild = UnityEngine.Time.frameCount;
            // Nur noch der Fall "im selben Bild umgeschaltet"; den Wechsel
            // davor faengt PflegeOhneWerkzeug ab.
            if (m_ToolSystem.activeTool != this) return;
            if (_stufe != Stufe.Frei && !ParkingLotSyncGueltigkeit.Bestand(EntityManager, _a.Lot, _a.Traeger))
            {
                if (_stufe == Stufe.Anlegen || _stufe == Stufe.Warten || _stufe == Stufe.Uebernehmen)
                    applyMode = ApplyMode.Clear;
                Beende(false, "Bestand verschwunden; keine Uebernahme");
                return;
            }
            if (_stufe != Stufe.Frei) _lief = true;
            switch (_stufe)
            {
                case Stufe.Anlegen:
                    // Nur in einem Bild ohne fremde Entwuerfe: unsere Vorschau soll
                    // nie mit einer anderen gemeinsam uebernommen werden.
                    if (_fremdeDefinitionen.CalculateEntityCount() > 0 || _temps.CalculateEntityCount() > 0)
                    {
                        RaeumeFremdeEntwuerfe();
                        applyMode = ApplyMode.Clear;
                        // Einmal je Tausch: was haelt ihn auf (ParkingLotEntwurfsbefund).
                        if (bild - _seit > 60 && !_befundGemeldet)
                        {
                            _befundGemeldet = true;
                            Mod.log.Warn("PLT-Bestandstausch: wartet seit " + (bild - _seit) + " Bildern auf fremde Entwuerfe - "
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
                        ZaehleTemps(out var weg, out var neu);
                        if (weg >= _a.Loeschen.Count && neu >= _geplant) { _stufe = Stufe.Uebernehmen; break; }
                        if (bild - _seit > 60)
                        {
                            applyMode = ApplyMode.Clear;
                            Beende(false, $"Vorschau unvollstaendig: {weg}/{_a.Loeschen.Count} Loeschungen, {neu}/{_geplant} neue Objekte");
                        }
                        break;
                    }
                case Stufe.Uebernehmen:
                    HefteNeue();
                    ParkingLotSchrittmarke.Aenderung(_a.Name + ": Lot " + _a.Lot.Index + " wird uebernommen (Apply)");
                    applyMode = ApplyMode.Apply;
                    _stufe = Stufe.Pruefen;
                    _seit = bild;
                    break;
                case Stufe.Pruefen:
                case Stufe.Nachsetzen:
                    NachDemApply(bild);
                    break;
            }
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
            Mod.log.Info("PLT-Bestandstausch: " + alte.Length + " liegengebliebene fremde Vorschau-Definition(en) entfernt.");
        }

        private void LegeDefinitionenAn()
        {
            foreach (var alt in _a.Loeschen)
            {
                if (!EntityManager.Exists(alt) || EntityManager.HasComponent<Deleted>(alt)) continue;
                var lage = EntityManager.GetComponentData<Game.Objects.Transform>(alt);
                var d = EntityManager.CreateEntity();
                // Wie der Bulldozer (BulldozeToolSystem.AddEntity): Original, Besitzer, Delete.
                EntityManager.AddComponentData(d, new CreationDefinition
                {
                    m_Original = alt,
                    m_Owner = EntityManager.HasComponent<Owner>(alt) ? EntityManager.GetComponentData<Owner>(alt).m_Owner : Entity.Null,
                    m_Flags = CreationFlags.Delete,
                });
                EntityManager.AddComponent<Updated>(d);
                EntityManager.AddComponentData(d, new ObjectDefinition
                {
                    m_Position = lage.m_Position, m_Rotation = lage.m_Rotation,
                    m_ParentMesh = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                });
                _definitionen.Add(d);
            }
            _geplant = _a.Anlegen(_definitionen, _terrain.GetHeightData());
            ParkingLotSchrittmarke.Aenderung(_a.Name + ": Lot " + _a.Lot.Index + ", " + _definitionen.Count + " Definitionen angelegt");
            Mod.log.Info($"PLT-Bestandstausch: Lot {_a.Lot.Index} ({_a.Name}): {_a.Loeschen.Count} Loeschungen und {_geplant} neue Objekte als Vorschau (Temp + Apply).");
        }

        private void ZaehleTemps(out int weg, out int neu)
        {
            weg = 0; neu = 0;
            using var alle = _temps.ToEntityArray(Allocator.Temp);
            foreach (var e in alle)
            {
                var temp = EntityManager.GetComponentData<Temp>(e);
                if (_loeschMenge.Contains(temp.m_Original)) { weg++; continue; }
                if (temp.m_Original == Entity.Null && EntityManager.HasComponent<PrefabRef>(e)
                    && _a.Prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)) neu++;
            }
        }

        /** Wie HefteObjekteAnTraeger beim Bau: Besitzer, Anhaenger, Zuordnung - an der Vorschau, im Bild des Apply. */
        private void HefteNeue()
        {
            using var alle = _temps.ToEntityArray(Allocator.Temp);
            foreach (var e in alle)
            {
                var temp = EntityManager.GetComponentData<Temp>(e);
                if (temp.m_Original != Entity.Null || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                if (!_a.Prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)) continue;
                _plt.SetVegetationOwner(e, _a.Traeger, _a.Lot);
                EntityManager.AddComponentData(e, new Attached(_a.Traeger, Entity.Null, 0f));
                EntityManager.AddComponentData(e, new ParkingLotPartRelation { Lot = _a.Lot, Carrier = _a.Traeger });
            }
        }

        /** Die neuen Teile des Lots: Prefab aus dem Auftrag und nicht zum Loeschen bestimmt. */
        private List<Entity> NeueTeile()
        {
            var liste = new List<Entity>();
            using var teile = _teile.ToEntityArray(Allocator.Temp);
            foreach (var t in teile)
            {
                if (EntityManager.GetComponentData<ParkingLotPartRelation>(t).Lot != _a.Lot || _loeschMenge.Contains(t)) continue;
                if (_a.Prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(t).m_Prefab)) liste.Add(t);
            }
            return liste;
        }

        private bool Pruefe(out string text)
        {
            var weg = _a.Loeschen.Count(a => !EntityManager.Exists(a) || EntityManager.HasComponent<Deleted>(a));
            var neue = NeueTeile();
            var besitz = neue.Count(n => EntityManager.HasComponent<Owner>(n) && EntityManager.GetComponentData<Owner>(n).m_Owner == _a.Traeger);
            text = $"{weg}/{_a.Loeschen.Count} entfernt, {neue.Count}/{_geplant} neu, Besitzer am Traeger {besitz}/{neue.Count}";
            return weg == _a.Loeschen.Count && neue.Count == _geplant && besitz == neue.Count;
        }

        /** Baumzustand am dauerhaften Baum (Altersfeld wirkt nicht zuverlaessig, siehe Bau). */
        internal int SetzeBaumzustaende(Dictionary<(Entity, float2), Game.Objects.Tree> ziele)
        {
            var gesetzt = 0;
            foreach (var n in NeueTeile())
            {
                if (!EntityManager.HasComponent<Game.Objects.Tree>(n)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(n).m_Prefab;
                var stelle = EntityManager.GetComponentData<Game.Objects.Transform>(n).m_Position.xz;
                if (!ziele.TryGetValue((prefab, stelle), out var ziel)) continue;
                EntityManager.SetComponentData(n, ziel);
                if (!EntityManager.HasComponent<BatchesUpdated>(n)) EntityManager.AddComponent<BatchesUpdated>(n);
                gesetzt++;
            }
            return gesetzt;
        }
    }
}
