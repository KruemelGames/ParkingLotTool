using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Game;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Bestandsaufnahme Lauf 1: ausschliesslich lesen, einmal je Spielstand.
     * NetInitializeSystem:1572-1604 berechnet SlotInterval und Spurbreite erst
     * aus den geladenen Assets. Namen im Blob belegen diese Werte nicht.
     * Wie die Laternen-Inventur am Ladeabschluss; Ausgabe ueber Mod.log wie
     * Probe/DebugDump, aber ohne deren Setzen/Loeschen von Sondenflaechen.
     */
    public sealed partial class ParkingLotFahrradSonde : GameSystemBase
    {
        private const string Prefix = "PLT-Fahrradsonde ";
        private static readonly string[] Erwartet =
        {
            "BicycleParkingLotDecal01", "BicycleParkingLotDecal02",
            "BicycleParkingArea02", "BicycleParkingArea03",
            "BicycleParkingHall01", "BicycleParkingHall02", "BicycleParkingHall03",
            "BikeRack03", "CommercialBikeRack01", "CommercialBikeRack02", "CommercialBikeRack03",
            "BicycleShelter01", "BicycleShelter02", "BicycleShelter03",
            "BicycleStorage01", "BicycleStorage02", "BicycleStorage03",
            "BikePathPavement300cm", "BikePathPavement800cm", "BikeLane150cm",
            "BikePathPavementMiddle300cm", "BikePathPavementMiddle800cm",
            "SidewalkBike350cm", "SidewalkBike700cm", "BicycleIconDecal01",
        };

        private PrefabSystem _prefabs;
        private EntityQuery _katalog, _bestand;
        private bool _faellig;
        internal static string LetzterStand = "noch kein Spielstand gemessen";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _katalog = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            _bestand = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>() },
                Any = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.BicycleParkingFacility>(),
                    ComponentType.ReadOnly<Game.Routes.BicycleParking>(),
                },
                None = new[] { ComponentType.ReadOnly<Game.Common.Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _faellig = mode == GameMode.Game;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!_faellig) return;
            _faellig = false;
            try { Messe(); }
            catch (Exception fehler)
            {
                LetzterStand = "Messung unvollstaendig: " + fehler.Message;
                Mod.log.Warn(Prefix + LetzterStand);
            }
        }

        private bool Fahrradspur(Entity e)
            => EntityManager.HasComponent<ParkingLaneData>(e)
               && (EntityManager.GetComponentData<ParkingLaneData>(e).m_RoadTypes & RoadTypes.Bicycle) != 0;

        private bool Fahrradplatz(Entity e)
        {
            if (EntityManager.HasComponent<ParkingFacilityData>(e)
                && (EntityManager.GetComponentData<ParkingFacilityData>(e).m_RoadTypes & RoadTypes.Bicycle) != 0) return true;
            if (EntityManager.HasComponent<TransportStopData>(e)
                && EntityManager.GetComponentData<TransportStopData>(e).m_TransportType == TransportType.Bicycle) return true;
            if (!EntityManager.HasBuffer<Game.Prefabs.SubLane>(e)) return false;
            var spuren = EntityManager.GetBuffer<Game.Prefabs.SubLane>(e, true);
            for (var i = 0; i < spuren.Length; i++) if (Fahrradspur(spuren[i].m_Prefab)) return true;
            return false;
        }

        private void Messe()
        {
            // GetParkingData liest ueber Lookups, nicht ueber EntityManager:
            // ohne diesen Abschluss koennten noch Jobs an den Spuren schreiben.
            EntityManager.CompleteAllTrackedJobs();
            var wurzeln = new SortedDictionary<string, Entity>(StringComparer.Ordinal);
            // Eltern nur ueber SubObject gefunden (z. B. Gewerbebau mit Staender):
            // davon koennen es hunderte sein, also nur die ersten voll ausgeben.
            var eltern = new SortedDictionary<string, Entity>(StringComparer.Ordinal);
            const int ElternVoll = 25;
            var namen = new HashSet<string>(StringComparer.Ordinal);
            using var katalog = _katalog.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < katalog.Length; i++)
            {
                var e = katalog[i];
                if (!_prefabs.TryGetPrefab<PrefabBase>(e, out var asset) || asset == null) continue;
                namen.Add(asset.name);
                if (asset.name.StartsWith("PLT ", StringComparison.Ordinal)) continue;
                if (asset.name.IndexOf("Bicycle", StringComparison.OrdinalIgnoreCase) >= 0
                    || asset.name.IndexOf("Bike", StringComparison.OrdinalIgnoreCase) >= 0
                    || Fahrradplatz(e) || Fahrradspur(e)) wurzeln[Katalogschluessel(asset, e)] = e;
            }
            // Auch namenlose Eltern erfassen: z.B. ein Gebaeude mit BikeRack
            // als Unterobjekt. Fixpunkt statt nur einer Ebene, damit die Liste
            // nicht faelschlich vollstaendig aussieht (wie die alten Nulltests).
            var relevant = new HashSet<Entity>(wurzeln.Values);
            bool neu;
            do
            {
                neu = false;
                for (var i = 0; i < katalog.Length; i++)
                {
                    var e = katalog[i];
                    if (relevant.Contains(e) || !EntityManager.HasBuffer<Game.Prefabs.SubObject>(e)) continue;
                    var kinder = EntityManager.GetBuffer<Game.Prefabs.SubObject>(e, true);
                    for (var k = 0; k < kinder.Length; k++)
                    {
                        if (!relevant.Contains(kinder[k].m_Prefab)) continue;
                        if (!_prefabs.TryGetPrefab<PrefabBase>(e, out var asset) || asset == null
                            || asset.name.StartsWith("PLT ", StringComparison.Ordinal)) break;
                        relevant.Add(e);
                        eltern[Katalogschluessel(asset, e)] = e;
                        neu = true;
                        break;
                    }
                }
            } while (neu);
            Mod.log.Info(Prefix + "BEGINN; Game=" + typeof(GameSystemBase).Assembly.ManifestModule.ModuleVersionId
                + "; Prefabs=" + katalog.Length + "; Kandidaten=" + wurzeln.Count
                + "; Unterobjekt-Eltern=" + eltern.Count + " (davon voll ausgegeben: " + Math.Min(eltern.Count, ElternVoll)
                + "); Koordinaten prefab-lokal; Winkel ECS in Radiant, Asset in Grad.");
            foreach (var name in Erwartet)
                Mod.log.Info(Prefix + "Erwartet '" + name + "': " + (namen.Contains(name) ? "GELADEN" : "NICHT GELADEN (kein Nullwert)"));
            var gesehen = new HashSet<Entity>();
            var fehlerzahl = 0;
            var voll = new List<Entity>(wurzeln.Values);
            var nurName = new List<string>();
            foreach (var paar in eltern)
            {
                if (voll.Count - wurzeln.Count < ElternVoll) voll.Add(paar.Value);
                else nurName.Add(Name(paar.Value));
            }
            foreach (var e in voll)
            {
                var text = new StringBuilder();
                try { Prefab(e, text, gesehen, 0); }
                catch (Exception fehler) { fehlerzahl++; text.AppendLine("UNVOLLSTAENDIG: " + fehler.Message); }
                if (text.Length > 0) Mod.log.Info(Prefix + text.ToString().Replace("\n", "\n" + Prefix));
            }
            if (nurName.Count > 0)
                Mod.log.Info(Prefix + "Weitere Unterobjekt-Eltern nur mit Namen (" + nurName.Count + "): "
                    + string.Join(", ", nurName));
            Bestand();
            LetzterStand = wurzeln.Count + " Kandidaten, " + eltern.Count + " Eltern, " + gesehen.Count
                + " Prefabs gelesen, " + fehlerzahl + " Lesefehler";
            Mod.log.Info(Prefix + "ENDE; " + LetzterStand + "; 0 Welt-/Prefab-Schreibzugriffe, kein Speichern.");
        }

        private string Name(Entity e)
            => e == Entity.Null ? "null" : _prefabs.TryGetPrefab<PrefabBase>(e, out var asset) && asset != null
                ? asset.name + " (#" + e.Index + ")" : e.ToString();

        private static string Katalogschluessel(PrefabBase asset, Entity e)
            => asset.GetType().FullName + "/" + asset.name + "#" + e.Index;

        private static string Typen(EntityArchetype archetyp)
        {
            using var typen = archetyp.GetComponentTypes(Allocator.Temp);
            var namen = new List<string>();
            for (var i = 0; i < typen.Length; i++) namen.Add(typen[i].GetManagedType()?.FullName ?? typen[i].ToString());
            namen.Sort(StringComparer.Ordinal);
            return string.Join(", ", namen);
        }

        // Reflection liest NUR oeffentliche Datenfelder, keine Properties,
        // Methoden oder Systeme. Entity-Verweise werden als Prefabnamen notiert.
        private string Wert(object wert, int tiefe = 0)
        {
            if (wert == null) return "null";
            if (wert is Entity entity) return Name(entity);
            if (wert is PrefabBase prefab) return prefab.name;
            if (wert is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (wert is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            var typ = wert.GetType();
            if (typ.IsEnum || wert is string || typ.IsPrimitive || wert is decimal)
                return Convert.ToString(wert, CultureInfo.InvariantCulture);
            if (wert is EntityArchetype) return "Archetyp (separat)";
            if (tiefe >= 5) return wert.ToString() + " [Feldtiefe begrenzt]";
            if (wert is Array array)
            {
                var teile = new List<string>();
                foreach (var item in array) teile.Add(Wert(item, tiefe + 1));
                return "[" + string.Join("; ", teile) + "]";
            }
            var felder = typ.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var aus = new List<string>();
            foreach (var feld in felder) aus.Add(feld.Name + "=" + Wert(feld.GetValue(wert), tiefe + 1));
            return "{" + string.Join(", ", aus) + "}";
        }

        private void Daten<T>(Entity e, StringBuilder text) where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(e))
                text.AppendLine(typeof(T).FullName + " " + Wert(EntityManager.GetComponentData<T>(e)));
        }

        private void Puffer<T>(Entity e, StringBuilder text) where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(e)) return;
            var puffer = EntityManager.GetBuffer<T>(e, true);
            text.AppendLine(typeof(T).FullName + " Anzahl=" + puffer.Length);
            for (var i = 0; i < puffer.Length; i++) text.AppendLine("  [" + i + "] " + Wert(puffer[i]));
        }

        private void Kinder<T>(Entity e, StringBuilder text, HashSet<Entity> gesehen, int tiefe, Func<T, Entity> ziel)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(e)) return;
            var puffer = EntityManager.GetBuffer<T>(e, true);
            // Kein LINQ auf DynamicBuffer; pro Eintrag die Quelle der Beziehung
            // ausgeben, auch wenn das Ziel bereits an anderer Stelle steht.
            text.AppendLine(Name(e) + " -> " + typeof(T).Name + " Anzahl=" + puffer.Length);
            for (var i = 0; i < puffer.Length; i++)
            {
                var eintrag = puffer[i];
                text.AppendLine("  [" + i + "] " + Wert(eintrag));
                Prefab(ziel(eintrag), text, gesehen, tiefe + 1);
            }
        }

        private void Prefab(Entity e, StringBuilder text, HashSet<Entity> gesehen, int tiefe)
        {
            if (e == Entity.Null || !EntityManager.Exists(e)) { text.AppendLine("FEHLEND " + e); return; }
            if (gesehen.Contains(e)) { text.AppendLine("Verweis: " + Name(e) + " (bereits ausgegeben)"); return; }
            if (tiefe > 16) { text.AppendLine("UNVOLLSTAENDIG: Tiefe > 16 bei " + Name(e)); return; }
            gesehen.Add(e);
            text.AppendLine("PREFAB " + Name(e) + "; Komponenten: " + Typen(EntityManager.GetChunk(e).Archetype));
            if (EntityManager.HasComponent<ObjectData>(e))
                text.AppendLine("Objekt-Archetyp: " + Typen(EntityManager.GetComponentData<ObjectData>(e).m_Archetype));
            if (EntityManager.HasComponent<NetData>(e))
            {
                var netz = EntityManager.GetComponentData<NetData>(e);
                text.AppendLine("Knoten-Archetyp: " + Typen(netz.m_NodeArchetype));
                text.AppendLine("Kanten-Archetyp: " + Typen(netz.m_EdgeArchetype));
            }
            Daten<ObjectGeometryData>(e, text);
            Daten<ParkingLaneData>(e, text);
            Daten<NetLaneData>(e, text);
            Daten<CarLaneData>(e, text);
            Daten<PedestrianLaneData>(e, text);
            Daten<ConnectionLaneData>(e, text);
            Daten<ParkingFacilityData>(e, text);
            Daten<ParkingSpaceData>(e, text);
            Daten<BuildingData>(e, text);
            Daten<TransportStopData>(e, text);
            Daten<RouteConnectionData>(e, text);
            Daten<SpawnLocationData>(e, text);
            Daten<NetGeometryData>(e, text);
            Daten<NetData>(e, text);
            Daten<PathwayData>(e, text);
            Daten<LocalConnectData>(e, text);
            Daten<NetPieceData>(e, text);
            Daten<RoadData>(e, text);
            Daten<WorkplaceData>(e, text);
            Daten<ServiceBudget>(e, text);
            Daten<ServiceObjectData>(e, text);
            Daten<ConsumptionData>(e, text);
            Daten<ContentPrerequisiteData>(e, text);
            Puffer<ServiceUpkeepData>(e, text);
            Puffer<DefaultPolicyData>(e, text);
            Puffer<Game.Prefabs.SubAreaNode>(e, text);
            Puffer<PlaceholderObjectElement>(e, text);
            if (_prefabs.TryGetPrefab<PrefabBase>(e, out var asset) && asset != null)
            {
                text.AppendLine("Assetklasse=" + asset.GetType().FullName);
                text.AppendLine("PrefabID=" + asset.GetPrefabID().ToUrlSegment());
                var komponenten = new List<ComponentBase>();
                asset.GetComponents(komponenten);
                foreach (var komponente in komponenten)
                {
                    var typ = komponente.GetType();
                    text.AppendLine("Assetkomponente=" + typ.FullName);
                    if (komponente is ParkingFacility || komponente is ParkingSpace
                        || komponente is Game.Prefabs.ParkingLane || komponente is Game.Prefabs.CarLane
                        || komponente is Game.Prefabs.PedestrianLane
                        || komponente is TransportStop || komponente is DlcRequirement
                        || komponente is Workplace || komponente is CityServiceBuilding || komponente is ServiceConsumption
                        || komponente is ContentPrerequisite || komponente is AssetPackItem)
                        text.AppendLine("Assetwerte " + typ.Name + " " + Wert(komponente));
                }
            }
            Kinder<Game.Prefabs.SubLane>(e, text, gesehen, tiefe, x => x.m_Prefab);
            Kinder<Game.Prefabs.SubObject>(e, text, gesehen, tiefe, x => x.m_Prefab);
            Kinder<Game.Prefabs.SubArea>(e, text, gesehen, tiefe, x => x.m_Prefab);
            Kinder<Game.Prefabs.SubNet>(e, text, gesehen, tiefe, x => x.m_Prefab);
            Kinder<NetGeometrySection>(e, text, gesehen, tiefe, x => x.m_Section);
            Kinder<NetSubSection>(e, text, gesehen, tiefe, x => x.m_SubSection);
            Kinder<NetSectionPiece>(e, text, gesehen, tiefe, x => x.m_Piece);
            Kinder<NetPieceLane>(e, text, gesehen, tiefe, x => x.m_Lane);
            Kinder<AssetPackElement>(e, text, gesehen, tiefe, x => x.m_Pack);
            if (EntityManager.HasComponent<ContentPrerequisiteData>(e))
                Prefab(EntityManager.GetComponentData<ContentPrerequisiteData>(e).m_ContentPrerequisite,
                    text, gesehen, tiefe + 1);
        }

        private void Bestandsspuren(Entity e, StringBuilder text, HashSet<Entity> gesehen)
        {
            if (e == Entity.Null || !EntityManager.Exists(e) || !gesehen.Add(e)) return;
            if (EntityManager.HasComponent<Game.Net.ParkingLane>(e))
            {
                text.AppendLine("Parkspur " + e);
                Daten<PrefabRef>(e, text);
                Daten<Game.Common.Owner>(e, text);
                Daten<Game.Net.ParkingLane>(e, text);
                Daten<Game.Net.Curve>(e, text);
                Puffer<Game.Net.LaneObject>(e, text);
            }
            if (EntityManager.HasBuffer<Game.Net.SubLane>(e))
            {
                var p = EntityManager.GetBuffer<Game.Net.SubLane>(e, true);
                for (var i = 0; i < p.Length; i++) Bestandsspuren(p[i].m_SubLane, text, gesehen);
            }
            if (EntityManager.HasBuffer<Game.Net.SubNet>(e))
            {
                var p = EntityManager.GetBuffer<Game.Net.SubNet>(e, true);
                for (var i = 0; i < p.Length; i++) Bestandsspuren(p[i].m_SubNet, text, gesehen);
            }
            if (EntityManager.HasBuffer<Game.Objects.SubObject>(e))
            {
                var p = EntityManager.GetBuffer<Game.Objects.SubObject>(e, true);
                for (var i = 0; i < p.Length; i++) Bestandsspuren(p[i].m_SubObject, text, gesehen);
            }
        }

        private void Bestand()
        {
            using var bestand = _bestand.ToEntityArray(Allocator.Temp);
            var proPrefab = new HashSet<Entity>();
            Mod.log.Info(Prefix + "gebaute Fahrradpark-Wurzeln=" + bestand.Length + "; je Prefab eine Stichprobe; fehlend bedeutet NICHT 0 Unterhalt/Personal/Gebuehr.");
            for (var i = 0; i < bestand.Length; i++)
            {
                var e = bestand[i];
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!proPrefab.Add(prefab)) continue;
                var text = new StringBuilder("BESTAND " + e + " '" + Name(prefab) + "'\n");
                text.AppendLine("Komponenten: " + Typen(EntityManager.GetChunk(e).Archetype));
                Daten<Game.Buildings.ServiceUsage>(e, text);
                Daten<Game.Buildings.Building>(e, text);
                Daten<Game.Buildings.ParkingFacility>(e, text);
                Daten<Game.Companies.WorkProvider>(e, text);
                Puffer<Game.Companies.Employee>(e, text);
                var laneCount = 0; var kapazitaet = 0; var belegt = 0; var gebuehr = 0;
                Game.Vehicles.VehicleUtils.GetParkingData(this, e, ref laneCount, ref kapazitaet, ref belegt, ref gebuehr);
                text.AppendLine("VehicleUtils: Spuren=" + laneCount + "; Kapazitaet=" + kapazitaet
                    + "; belegt=" + belegt + "; GebuehrenSUMME=" + gebuehr);
                Bestandsspuren(e, text, new HashSet<Entity>());
                Mod.log.Info(Prefix + text.ToString().Replace("\n", "\n" + Prefix));
            }
        }
    }
}
