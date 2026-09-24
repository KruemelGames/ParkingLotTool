using System.Collections.Generic;
using System.Diagnostics;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Bestandsaufnahme verwaister Parkplaetze beim Laden. VERAENDERT NICHTS.
     *
     * Eine Waise entsteht, wenn ein Spielstand OHNE PLT gespeichert wurde:
     * CS2 ueberspringt dann unsere Komponententypen als "obsolete" und laesst
     * sie beim Speichern weg. Die Lot-Flaeche selbst ueberlebt, denn ihr
     * Prefab wird ueber den Namen gemerkt und ist beim naechsten Start mit
     * PLT wieder da. Weg sind Traegerverweis, Bauzettel und Teilrelationen.
     *
     * Bevor irgendetwas gerettet wird, braucht es Fakten: was genau an so
     * einer Waise noch haengt. Diese Aufnahme zaehlt es und schreibt es ins
     * Log - Puffer der Flaeche, Kinder ueber Owner, herrenlose Traeger und
     * den passenden Eintrag im Bauprotokoll (fuer ein spaeteres "neu bauen").
     *
     * Der Traeger ist erkennbar, obwohl sein Verweis fehlt: er traegt das
     * PrefabRef der Lot-Flaeche, hat aber keine Area.
     */
    public sealed partial class ParkingLotWaisenSystem : GameSystemBase
    {
        private EntityQuery _mitPrefab;
        private EntityQuery _mitBesitzer;
        private PrefabSystem _prefabSystem;

        /** Lot-Flaechen ohne Traegerverweis UND ohne Bauzettel. */
        internal readonly List<Entity> Waisen = new List<Entity>();

        /** Nur eins von beiden fehlt - sollte es nicht geben. */
        internal readonly List<Entity> Halbwaisen = new List<Entity>();

        /** Traeger, auf die keine Lot-Flaeche mehr verweist. */
        internal readonly List<Entity> HerrenloseTraeger = new List<Entity>();

        /** Kennung -> Bauprotokoll, nur fuer gefundene Waisen. */
        internal readonly Dictionary<Entity, ParkingLotToolSystem.BuildRecord>
            Bauplaene = new Dictionary<Entity, ParkingLotToolSystem.BuildRecord>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _mitPrefab = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<Game.Net.Node>(),
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                },
            });
            _mitBesitzer = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Owner>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            // Kein Enabled=false: ob CS2 den Ladeabschluss auch an
            // abgeschaltete Systeme meldet, ist nicht belegt.
        }

        [Preserve]
        protected override void OnUpdate()
        {
            AutomatischWeiter();
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            Waisen.Clear();
            Halbwaisen.Clear();
            HerrenloseTraeger.Clear();
            Bauplaene.Clear();
            _traegerVon.Clear();
            _begleiterVon.Clear();
            _grund.Clear();
            OhneBauzettel.Clear();
            _gescheitert.Clear();
            _autoOffen = false;
            if (mode != GameMode.Game) return;
            try
            {
                Aufnehmen();
                Zuordnen();
                SammleOhneBauzettel();
                _autoOffen = Waisen.Count > 0 || OhneBauzettel.Count > 0;
            }
            catch (System.Exception e)
            {
                // Eine Diagnose darf das Laden nie stoeren.
                Mod.log.Warn("PLT-Waisen: Bestandsaufnahme abgebrochen: " + e);
            }
        }

        private bool IstLotPrefab(Entity prefab, Dictionary<Entity, bool> merk)
        {
            if (merk.TryGetValue(prefab, out var ist)) return ist;
            string name = null;
            try
            {
                if (_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p)
                    && p != null)
                    name = p.name;
                else
                    name = _prefabSystem.GetPrefabName(prefab);
            }
            catch { name = null; }
            ist = name == ParkingLotToolSystem.LotOwnerPrefabName;
            merk[prefab] = ist;
            return ist;
        }

        private void Aufnehmen()
        {
            var uhr = Stopwatch.StartNew();
            var merk = new Dictionary<Entity, bool>();
            var lots = new List<Entity>();
            var traeger = new List<Entity>();

            using (var entities = _mitPrefab.ToEntityArray(Allocator.Temp))
            using (var refs = _mitPrefab.ToComponentDataArray<PrefabRef>(
                       Allocator.Temp))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    if (!IstLotPrefab(refs[i].m_Prefab, merk)) continue;
                    if (EntityManager.HasComponent<Game.Areas.Area>(entities[i]))
                        lots.Add(entities[i]);
                    else
                        traeger.Add(entities[i]);
                }
            }

            var verwiesen = new HashSet<Entity>();
            var vollstaendig = 0;
            foreach (var lot in lots)
            {
                var hatVerweis = EntityManager.HasComponent<
                    ParkingLotCarrierReference>(lot);
                var hatZettel = EntityManager.HasComponent<
                    ParkingLotBuildReceipt>(lot);
                if (hatVerweis)
                    verwiesen.Add(EntityManager.GetComponentData<
                        ParkingLotCarrierReference>(lot).Carrier);
                if (hatVerweis && hatZettel) vollstaendig++;
                else if (!hatVerweis && !hatZettel) Waisen.Add(lot);
                else Halbwaisen.Add(lot);
            }
            foreach (var t in traeger)
                if (!verwiesen.Contains(t)) HerrenloseTraeger.Add(t);

            Mod.log.Info("PLT-Waisen: " + lots.Count + " Lot-Flaeche(n): "
                + vollstaendig + " vollstaendig, " + Waisen.Count
                + " verwaist (ohne Traegerverweis und Bauzettel), "
                + Halbwaisen.Count + " halb; " + traeger.Count
                + " Traeger, davon " + HerrenloseTraeger.Count
                + " herrenlos.");
            if (Waisen.Count == 0 && Halbwaisen.Count == 0
                && HerrenloseTraeger.Count == 0)
            {
                Mod.log.Info("PLT-Waisen: nichts gefunden ("
                    + uhr.ElapsedMilliseconds + " ms).");
                return;
            }

            // Kinder zaehlen: EIN Durchlauf ueber alle Owner, gezaehlt wird
            // nur fuer die gesuchten Besitzer.
            var gesucht = new HashSet<Entity>(Waisen);
            gesucht.UnionWith(Halbwaisen);
            gesucht.UnionWith(HerrenloseTraeger);
            var kinder = new Dictionary<Entity, int[]>();
            using (var entities = _mitBesitzer.ToEntityArray(Allocator.Temp))
            using (var owners = _mitBesitzer.ToComponentDataArray<Owner>(
                       Allocator.Temp))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var besitzer = owners[i].m_Owner;
                    if (!gesucht.Contains(besitzer)) continue;
                    if (!kinder.TryGetValue(besitzer, out var z))
                        kinder[besitzer] = z = new int[5];
                    var kind = entities[i];
                    if (EntityManager.HasComponent<Game.Areas.Area>(kind)) z[0]++;
                    else if (EntityManager.HasComponent<Game.Net.Edge>(kind)) z[1]++;
                    else if (EntityManager.HasComponent<Game.Net.Node>(kind)) z[2]++;
                    else if (EntityManager.HasComponent<Game.Objects.Object>(kind)) z[3]++;
                    else
                    {
                        if (z[4] == 0) BeschreibeSonstiges(besitzer, kind);
                        z[4]++;
                    }
                }
            }

            var protokoll = ParkingLotToolSystem.LeseBauprotokoll();
            foreach (var lot in Waisen) BeschreibeLot("Waise", lot, kinder, protokoll);
            foreach (var lot in Halbwaisen) BeschreibeLot("Halbwaise", lot, kinder, protokoll);
            foreach (var t in HerrenloseTraeger)
            {
                kinder.TryGetValue(t, out var z);
                Mod.log.Info("PLT-Waisen: herrenloser Traeger " + t.Index
                    + ": SubObject=" + Puffer<Game.Objects.SubObject>(t)
                    + " SubNet=" + Puffer<Game.Net.SubNet>(t)
                    + " Kinder[Flaechen/Kanten/Knoten/Objekte/sonst]="
                    + Zeile(z) + ".");
            }
            Mod.log.Info("PLT-Waisen: Aufnahme in " + uhr.ElapsedMilliseconds
                + " ms; " + Bauplaene.Count + " von " + (Waisen.Count + Halbwaisen.Count)
                + " betroffenen Parkplaetzen mit vollem Bauplan im Protokoll. "
                + "Nichts veraendert.");
        }

        private void BeschreibeLot(string art, Entity lot,
            Dictionary<Entity, int[]> kinder,
            Dictionary<string, ParkingLotToolSystem.BuildRecord> protokoll)
        {
            kinder.TryGetValue(lot, out var z);
            var polygon = new List<float2>();
            if (EntityManager.HasBuffer<Game.Areas.Node>(lot))
            {
                var knoten = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
                for (var i = 0; i < knoten.Length; i++)
                    polygon.Add(knoten[i].m_Position.xz);
            }
            var mitte = float2.zero;
            foreach (var p in polygon) mitte += p;
            if (polygon.Count > 0) mitte /= polygon.Count;
            var kennung = ParkingLotToolSystem.LotId(polygon);
            string bauplan = "kein Eintrag";
            if (protokoll.TryGetValue(kennung, out var eintrag))
            {
                bauplan = "Eintrag vom " + eintrag.When + " ("
                    + (eintrag.OwnerName ?? "?") + ")"
                    + (eintrag.VollerBauplan ? ", voller Bauplan"
                        : ", OHNE Bauwerte (zu alt) - nicht wiederherstellbar");
                if (eintrag.VollerBauplan) Bauplaene[lot] = eintrag;
            }
            Mod.log.Info("PLT-Waisen: " + art + " " + lot.Index + " "
                + kennung + " bei (" + mitte.x.ToString("0.0") + ", "
                + mitte.y.ToString("0.0") + "), " + polygon.Count
                + " Ecken; Verweis="
                + EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                + " Bauzettel="
                + EntityManager.HasComponent<ParkingLotBuildReceipt>(lot)
                + " SubArea=" + Puffer<Game.Areas.SubArea>(lot)
                + " SubNet=" + Puffer<Game.Net.SubNet>(lot)
                + " SubObject=" + Puffer<Game.Objects.SubObject>(lot)
                + " Kinder[Flaechen/Kanten/Knoten/Objekte/sonst]=" + Zeile(z)
                + "; Bauprotokoll: " + bauplan + ".");
        }

        /** Was ist das eine Kind, das weder Flaeche, Netz noch Objekt ist? */
        private void BeschreibeSonstiges(Entity besitzer, Entity kind)
        {
            using var typen = EntityManager.GetComponentTypes(kind, Allocator.Temp);
            var namen = new List<string>();
            for (var i = 0; i < typen.Length; i++)
                namen.Add(typen[i].GetManagedType()?.Name ?? typen[i].ToString());
            Mod.log.Info("PLT-Waisen: sonstiges Kind " + kind.Index + " von "
                + besitzer.Index + ": " + string.Join(", ", namen));
        }

        /** Laenge des Puffers, oder "fehlt" - das Fehlen ist der Befund. */
        private string Puffer<T>(Entity e) where T : unmanaged, IBufferElementData
            => EntityManager.HasBuffer<T>(e)
                ? EntityManager.GetBuffer<T>(e, true).Length.ToString()
                : "fehlt";

        private static string Zeile(int[] z)
            => z == null ? "0/0/0/0/0"
                : z[0] + "/" + z[1] + "/" + z[2] + "/" + z[3] + "/" + z[4];
    }
}
