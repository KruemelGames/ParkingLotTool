using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * LATERNEN IM WERKZEUG (Plan vom 2026-10-04).
     *
     * Ein Plan fuer alles: dieselbe Rechnung (Geometry/ParkingLanterns) fuettert
     * die Vorschau, die Freihaltung der Pflanzen und den Bau. Die Pflanzen
     * weichen den Laternen aus, deshalb entsteht der Laternenplan immer zuerst.
     *
     * Gesetzt werden die Laternen wie Pflanzen und Aufkleber: als
     * Objektdefinition in derselben Vorschau-Transaktion, danach haengt
     * `HefteObjekteAnTraeger` jedes aufgezeichnete Objekt mit Besitzer,
     * Attached und Teilrelation an den Traeger. Der Besitzer haelt das
     * Baumwerkzeug fern (Brushable greift nur ohne Owner) und schuetzt vor
     * unseren raeumenden Flaechen (gleiche Besitzerwurzel). Gedreht wird nur
     * um die Hochachse: keine Neigung.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private static readonly LaternenOptionen StandardLaternen = new LaternenOptionen();

        /** Laternen des laufenden Baus - auch die Pflanzenplanung des Baus haelt sie frei. */
        private List<LaternenKoerper> _bauLaternenKoerper;
        private int _laternenCount;
        private readonly Dictionary<string, Entity> _laternenPrefabs = new Dictionary<string, Entity>();

        /** Die Laternenwahl eines Hintergrundbaus: aus dem Bauzettel, nie aus dem Panel. */
        private LaternenOptionen _baulaternen;

        internal LaternenOptionen AktuelleLaternen => _baulaternen ?? _uiSystem?.Laternen ?? StandardLaternen;

        /** Bauzettel-Text 6: die Laternenwahl beim Bau (seit 2026-10-04). Fehlt er, null. */
        internal LaternenOptionen LaternenVon(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot) || !EntityManager.HasBuffer<ParkingLotBuildText>(lot)) return null;
            if (!TryReadBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot, true), 6, out var json) || string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<LaternenOptionen>(json); }
            catch (Exception e) { Mod.log.Warn("Laternenzettel unlesbar: " + e.Message); return null; }
        }

        /** Ersetzt Bauzettel-Text 6 am fertigen Lot (Sync-Schritt 11). */
        internal void SchreibeLaternenZettel(Entity lot, LaternenOptionen optionen)
        {
            var text = EntityManager.HasBuffer<ParkingLotBuildText>(lot)
                ? EntityManager.GetBuffer<ParkingLotBuildText>(lot) : EntityManager.AddBuffer<ParkingLotBuildText>(lot);
            for (var i = text.Length - 1; i >= 0; i--) if (text[i].Kind == 6) text.RemoveAt(i);
            AddBuildText(text, 6, JsonConvert.SerializeObject(optionen));
            if (LaternenVon(lot) == null)
                throw new InvalidOperationException("Laternenzettel von Lot " + lot.Index + " liest nach dem Schreiben nicht zurueck.");
        }

        /**
         * Sync-Schritt 11: Parkplatz ohne Laternenzettel und ohne Laternen, und
         * der Standard des Spielers setzt welche. Wer schon Laternen hat (vor
         * dem Zettel gebaut), braucht nichts - sonst stuenden sie doppelt.
         */
        internal bool BrauchtLaternenNachruesten(Entity lot, List<Entity> teile)
        {
            if (LaternenVon(lot) != null) return false;
            var standard = _uiSystem?.LaternenStandardOptionen
                ?? World.GetOrCreateSystemManaged<ParkingLotUISystem>().LaternenStandardOptionen;
            if (!standard.Enabled) return false;
            foreach (var t in teile)
                if (EntityManager.Exists(t) && EntityManager.HasComponent<StreetLight>(t)) return false;
            return true;
        }

        /**
         * Sync-Schritt 12: der Zettel will Laternen, gebaut ist keine. Am
         * Bestand geprueft, nicht an der Version - ein Parkplatz, den der
         * Spieler mit Laternen gebaut hat, hat welche und braucht nichts.
         */
        internal bool BrauchtLaternenNachZettel(Entity lot, List<Entity> teile)
        {
            var zettel = LaternenVon(lot);
            if (zettel == null || !zettel.Enabled) return false;
            foreach (var t in teile)
                if (EntityManager.Exists(t) && EntityManager.HasComponent<StreetLight>(t)) return false;
            return true;
        }

        /** Beim Bearbeiten: die Wahl des Parkplatzes ins Fenster, ohne Zettel der Standard. */
        private void LoadLaternen(Entity lot)
        {
            var optionen = LaternenVon(lot);
            _uiSystem?.RestoreLaternen(optionen);
            Mod.log.Info("PLT-Laternen laden Lot " + lot.Index + ": "
                + (optionen != null ? JsonConvert.SerializeObject(optionen) : "kein Laternenzettel; Standard"));
        }

        internal static LaternenPlan LaternenPlanFuer(ParkingLayout layout, LaternenOptionen optionen)
            => optionen != null && optionen.Enabled && layout != null
                ? layout.Fahrrad != null ? FahrradDekoration.Laternen(layout, optionen.Abstand)
                    : ParkingLanterns.Plan(layout, optionen.Abstand)
                : new LaternenPlan();

        /**
         * Lage, Drehung und Mast jeder Laterne aus den Daten ihres Prefabs fuer
         * die Pflanzenplanung. Ohne gefundenes Prefab wird die Laterne nicht
         * gebaut - dann haelt auch niemand Platz fuer sie frei.
         */
        internal List<LaternenKoerper> LaternenKoerperFuer(LaternenPlan plan, LaternenOptionen optionen)
        {
            var liste = new List<LaternenKoerper>();
            foreach (var platz in plan.Laternen)
            {
                var name = optionen.ModellFuer(platz);
                var prefab = LaternenPrefab(name);
                if (prefab == Entity.Null || !EntityManager.HasComponent<ObjectGeometryData>(prefab)) continue;
                var g = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                var bauart = LaternenKatalog.Modell(name)?.Bauart
                    ?? (platz.Doppelt ? LaternenBauart.Doppelt : LaternenBauart.Einseitig);
                liste.Add(new LaternenKoerper
                {
                    Position = platz.Position,
                    Vorwaerts = math.normalizesafe(LaternenKatalog.Vorwaerts(platz, bauart)),
                    Bein = (g.m_Flags & GeometryFlags.Standing) != 0 ? g.m_LegSize : float3.zero,
                    Max = g.m_Bounds.max,
                });
            }
            return liste;
        }

        /** Laternen und Pflanzen der Vorschau neu, ohne neue Layoutrechnung. */
        internal void RefreshLaternenPreview() => RefreshVegetationPreview();

        internal Entity LaternenPrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;
            if (_laternenPrefabs.TryGetValue(name, out var e) && e != Entity.Null && EntityManager.Exists(e)) return e;
            e = _prefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), name), out var basis) && basis != null
                ? _prefabSystem.GetEntity(basis) : Entity.Null;
            _laternenPrefabs[name] = e;
            return e;
        }

        /**
         * DIE DEFINITION EINER LATERNE - fuer den Bau UND das Nachruesten per
         * Sync (Schritt 11). `prefab` ist Entity.Null, wenn das Modell fehlt;
         * die Definition ist Entity.Null auch, wenn das Gelaende keine Hoehe hat.
         */
        internal Entity LaternenDefinition(LaternenPlatz platz, LaternenOptionen optionen, ref TerrainHeightData heightData,
            out Entity prefab, out string name, out float3 position)
        {
            name = optionen.ModellFuer(platz);
            prefab = LaternenPrefab(name);
            position = new float3(platz.Position.x, 0f, platz.Position.y);
            if (prefab == Entity.Null) return Entity.Null;
            var bauart = LaternenKatalog.Modell(name)?.Bauart
                ?? (platz.Doppelt ? LaternenBauart.Doppelt : LaternenBauart.Einseitig);
            position.y = TerrainUtils.SampleHeight(ref heightData, position);
            if (!math.all(math.isfinite(position))) return Entity.Null;
            var vorn = LaternenKatalog.Vorwaerts(platz, bauart);
            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = prefab,
                // Aus der Lage: dieselbe Laterne bekommt bei jedem Bau
                // denselben Wuerfel (Schaltschwelle in der Daemmerung).
                m_RandomSeed = (int)math.hash(new int2((int)math.round(position.x * 16f), (int)math.round(position.z * 16f))),
            });
            EntityManager.AddComponent<Updated>(definition);
            EntityManager.AddComponentData(definition, new ObjectDefinition
            {
                m_Position = position,
                m_Rotation = quaternion.LookRotationSafe(new float3(vorn.x, 0f, vorn.y), math.up()),
                m_Probability = 100, m_PrefabSubIndex = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_ParentMesh = -1,
            });
            return definition;
        }

        private IEnumerable<int> CreateLanternDefinitionsSchritte(ParkingLayout layout, TerrainHeightData heightData)
        {
            var optionen = AktuelleLaternen;
            var plan = LaternenPlanFuer(layout, optionen);
            _bauLaternenKoerper = LaternenKoerperFuer(plan, optionen);
            _laternenCount = 0;
            if (plan.Laternen.Count == 0) yield break;
            var fehlend = new HashSet<string>();
            foreach (var platz in plan.Laternen)
            {
                yield return 0;
                var definition = LaternenDefinition(platz, optionen, ref heightData, out var prefab, out var name, out var position);
                if (prefab == Entity.Null) { fehlend.Add(name); continue; }
                if (definition == Entity.Null) continue;
                RecordObjectDefinition("Laterne", _laternenCount++, prefab, definition, position);
                yield return 1;
            }
            var arten = string.Join(", ", plan.Laternen.GroupBy(l => l.Art).Select(g => g.Key + " " + g.Count()));
            Mod.log.Info($"PLT-Laternen: {_laternenCount} von {plan.Laternen.Count} gesetzt ({arten}); Abstand "
                + $"{optionen.Abstand:F0} m, Einzeln {optionen.Einzeln}, Doppelt {optionen.Doppelt}"
                + (fehlend.Count > 0 ? "; NICHT GEFUNDEN: " + string.Join(", ", fehlend) : "") + ".");
        }
    }
}
