using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Dauerhafte Akte der Zoningsonde.
     *
     * Der Traeger ist absichtlich nackt: kein Building, kein Transform, kein
     * Node und keine Edge. `BlockSystem` folgt der Owner-Kette und steigt bei
     * einem Building aus. Der SubNet-Puffer muss dagegen vor dem dauerhaften
     * Werden der Strasse da sein; sonst kann SubNetReferencesSystem beim
     * Speichern/Laden weder sauber schreiben noch wiederherstellen.
     */
    public struct ParkingLotZoningProbeMarker : IComponentData,
                                                IQueryTypeParameter,
                                                ISerializable
    {
        public Entity RoadPrefab;
        public Entity Edge;
        public float3 Start;
        public float3 End;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(RoadPrefab);
            writer.Write(Edge);
            writer.Write(Start);
            writer.Write(End);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out RoadPrefab);
            reader.Read(out Edge);
            reader.Read(out Start);
            reader.Read(out End);
        }
    }

    /**
     * Baut genau eine echte, kurze Strasse fuer eine Messung im laufenden
     * Spiel. Dieser Zustandsautomat ist kein Teil des Parkplatz-Baupfads.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const string ZpPrefix = "PLT-Zoningsonde:";
        private const int ZpWarteFrames = 24;

        private enum ZpPhase
        {
            Idle,
            /**
             * Nur beim unsichtbaren Lauf: der Prefabklon entsteht in
             * PrefabUpdate und ist erst einen Zyklus spaeter benutzbar.
             * Bauen im selben Frame waere der Archetypfehler vom 2026-08-30.
             */
            PrefabWarten,
            TempSuchen,
            StrasseMessen,
            ZoningMessen,
            Aufraeumen,
        }

        private enum ZpRequest
        {
            None,
            BuildAlley,
            BuildGravel,
            Measure,
            Cleanup,
        }

        private EntityQuery _zpRoadPrefabs;
        private EntityQuery _zpZonePrefabs;
        private EntityQuery _zpTempNets;
        private EntityQuery _zpCarriers;
        private EntityQuery _zpBuildings;
        private ZpPhase _zpPhase;
        private ZpRequest _zpRequest;
        private int _zpPhaseFrame;
        private Entity _zpCarrier;
        private Entity _zpRoadPrefab;
        /**
         * Das Vanilla-Vorbild. Beim unsichtbaren Lauf ist `_zpRoadPrefab`
         * der Klon, dieses Feld bleibt das Original - sonst fordert die
         * Sonde in der Wartephase einen Klon des Klons an.
         */
        private Entity _zpOriginalPrefab;
        private bool _zpUnsichtbar;
        private string _zpAuswahlName = string.Empty;
        private ParkingLotZoningRoadPrefabSystem _zpRoadPrefabSystem;
        private Entity _zpEdge;
        private Entity _zpZonePrefab;
        private float3 _zpStart;
        private float3 _zpEnd;
        private readonly List<Entity> _zpCleanupTargets = new List<Entity>();

        private void InitialisiereZoningsonde()
        {
            _zpRoadPrefabSystem = World
                .GetOrCreateSystemManaged<ParkingLotZoningRoadPrefabSystem>();
            _zpRoadPrefabs = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RoadData>(),
                    ComponentType.ReadOnly<NetGeometryData>(),
                    ComponentType.ReadOnly<PrefabData>(),
                },
                None = new[] { ComponentType.ReadOnly<PlaceholderObjectElement>() },
            });
            _zpZonePrefabs = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ZoneData>(),
                    ComponentType.ReadOnly<PrefabData>(),
                },
                None = new[] { ComponentType.ReadOnly<PlaceholderObjectElement>() },
            });
            _zpTempNets = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Game.Net.Curve>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _zpCarriers = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotZoningProbeMarker>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _zpBuildings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
        }

        /**
         * Die Oberflaeche haengt " (unsichtbar)" an den Strassennamen, wenn
         * der Klon mit ausgeblendeten Querschnitten gemessen werden soll.
         * Ein eigener Knopf je Kombination waere die vierte Schaltflaeche
         * fuer dieselbe Sache gewesen.
         */
        internal void StarteZoningsonde(string roadName)
        {
            const string unsichtbarKennung = " (unsichtbar)";
            _zpUnsichtbar = roadName != null
                && roadName.EndsWith(unsichtbarKennung, StringComparison.Ordinal);
            if (_zpUnsichtbar)
            {
                roadName = roadName.Substring(0,
                    roadName.Length - unsichtbarKennung.Length);
            }
            _zpRequest = string.Equals(roadName, "Gravel Road",
                StringComparison.Ordinal)
                ? ZpRequest.BuildGravel : ZpRequest.BuildAlley;
        }

        internal void MissZoningsonde() => _zpRequest = ZpRequest.Measure;

        internal void RaeumeZoningsondeAuf() => _zpRequest = ZpRequest.Cleanup;

        /** Ein wahrer Rueckgabewert reserviert diesen Frame fuer die Sonde. */
        private bool PflegeZoningsonde()
        {
            if (_zpRequest != ZpRequest.None)
            {
                var request = _zpRequest;
                _zpRequest = ZpRequest.None;
                if (request == ZpRequest.Measure)
                {
                    LadeZpStandAusWelt();
                    ZpMesseAlles("5 WACHSTUM/LADETEST");
                    return false;
                }
                if (request == ZpRequest.Cleanup)
                {
                    ZpBeginneAufraeumen();
                    return true;
                }
                if (_zpPhase != ZpPhase.Idle || ZpHatTraeger())
                {
                    ZpLog("0 START abgelehnt: Es liegt bereits eine Sonde. "
                        + "Erst 'Sonde entfernen' druecken.");
                    return false;
                }
                ZpBeginneBau(request == ZpRequest.BuildGravel
                    ? "Gravel Road" : "Alley");
                return true;
            }

            switch (_zpPhase)
            {
                case ZpPhase.PrefabWarten:
                    return ZpWartetAufPrefab();
                case ZpPhase.TempSuchen:
                    return ZpUebernimmTempStrasse();
                case ZpPhase.StrasseMessen:
                    if (++_zpPhaseFrame < ZpWarteFrames) return true;
                    ZpMesseAlles("3 NACH STRASSENBAU");
                    ZpSetzeZonentyp();
                    _zpPhase = ZpPhase.ZoningMessen;
                    _zpPhaseFrame = 0;
                    return true;
                case ZpPhase.ZoningMessen:
                    if (++_zpPhaseFrame < ZpWarteFrames) return true;
                    ZpMesseAlles("4 NACH ZONIERUNG");
                    _zpPhase = ZpPhase.Idle;
                    ZpLog("4 FERTIG: Zeit laufen lassen, dann 'Wachstum messen' "
                        + "druecken. Nach Speichern/Laden dieselbe Taste erneut druecken.");
                    return false;
                case ZpPhase.Aufraeumen:
                    if (++_zpPhaseFrame < ZpWarteFrames) return true;
                    return ZpBeendeAufraeumen();
                default:
                    return false;
            }
        }

        private void ZpBeginneBau(string prefabName)
        {
            // Der Katalog ist absichtlich die erste Ausgabe jedes Laufs.
            ZpSchreibeStrassenkatalog();
            ZpSchreibeZonenkatalog();
            if (!ZpTryRoadPrefab(prefabName, out _zpOriginalPrefab))
            {
                ZpLog($"2 START abgebrochen: RoadPrefab '{prefabName}' fehlt "
                    + "oder ist nicht zoningfaehig (RoadData.m_ZoneBlockPrefab ist leer). ");
                return;
            }
            if (!_letzteWeltpositionGueltig)
            {
                ZpLog("2 START abgebrochen: Noch keine Weltposition. Einmal mit "
                    + "der Maus auf freies Gelaende zeigen und erneut druecken.");
                return;
            }

            _zpAuswahlName = prefabName;
            _zpRoadPrefab = _zpOriginalPrefab;
            if (_zpUnsichtbar)
            {
                /*
                 * Der Klon wird hier nur BESTELLT. Gebaut wird erst, wenn
                 * ParkingLotZoningRoadPrefabSystem ihn in PrefabUpdate
                 * angemeldet und PrefabInitializeSystem seinen Archetyp
                 * gebaut hat.
                 */
                _zpRoadPrefabSystem.FordereAn(_zpOriginalPrefab,
                    Strassenklonart.Zoning, out _, out _);
                _zpPhase = ZpPhase.PrefabWarten;
                _zpPhaseFrame = 0;
                ZpLog($"2 START (unsichtbar): Klon von "
                    + $"'{ZpPrefabName(_zpOriginalPrefab)}' bestellt. Gebaut "
                    + "wird, sobald der Prefabarchetyp steht.");
                return;
            }

            ZpBaueStrasse();
        }

        /**
         * Legt Traeger, Kurve und CreationDefinition an. Getrennt vom Start,
         * weil der unsichtbare Lauf dazwischen auf sein Prefab wartet.
         */
        private void ZpBaueStrasse()
        {
            var prefabName = _zpAuswahlName;
            var center = _letzteWeltposition;
            var heightData = _terrainSystem.GetHeightData(waitForPending: true);
            _zpStart = new float3(center.x - 20f, 0f, center.z);
            _zpEnd = new float3(center.x + 20f, 0f, center.z);
            _zpStart.y = TerrainUtils.SampleHeight(ref heightData, _zpStart);
            _zpEnd.y = TerrainUtils.SampleHeight(ref heightData, _zpEnd);
            if (!math.all(math.isfinite(_zpStart)) || !math.all(math.isfinite(_zpEnd)))
            {
                ZpLog("2 START abgebrochen: Die Terrainhoehe war nicht endlich.");
                return;
            }

            _zpCarrier = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_zpCarrier,
                new PrefabRef { m_Prefab = _zpRoadPrefab });
            EntityManager.AddBuffer<Game.Net.SubNet>(_zpCarrier);
            EntityManager.AddComponentData(_zpCarrier,
                new ParkingLotZoningProbeMarker
                {
                    RoadPrefab = _zpRoadPrefab,
                    Edge = Entity.Null,
                    Start = _zpStart,
                    End = _zpEnd,
                });

            var curve = NetUtils.StraightCurve(_zpStart, _zpEnd);
            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = _zpRoadPrefab,
                m_RandomSeed = Environment.TickCount,
            });
            EntityManager.AddComponent<Updated>(definition);
            EntityManager.AddComponentData(definition, new NetCourse
            {
                m_Curve = curve,
                m_Length = math.distance(_zpStart, _zpEnd),
                m_FixedIndex = -1,
                m_Elevation = float2.zero,
                m_StartPosition = ZpCoursePos(_zpStart,
                    NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)), true),
                m_EndPosition = ZpCoursePos(_zpEnd,
                    NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)), false),
            });
            _zpPhase = ZpPhase.TempSuchen;
            _zpPhaseFrame = 0;
            ZpLog($"2 START ({(_zpUnsichtbar ? "UNSICHTBAR" : "sichtbar")}): "
                + $"Auswahl '{prefabName}', tatsaechliches Prefab "
                + $"'{ZpPrefabName(_zpRoadPrefab)}', 40,00 m von {ZpFloat3(_zpStart)} "
                + $"nach {ZpFloat3(_zpEnd)}; Ort = letzte gueltige Mausposition; "
                + $"Traeger {ZpEntity(_zpCarrier)} hat PrefabRef + SubNet + "
                + "ParkingLotZoningProbeMarker, aber kein Building/Transform/Node/Edge.");
        }

        /**
         * Wartet auf den unsichtbaren Prefabklon und baut dann. Die Grenze
         * von 300 Frames ist kein knappes Zeitfenster: das Prefabsystem
         * braucht im Normalfall zwei Zyklen.
         */
        private bool ZpWartetAufPrefab()
        {
            var klon = _zpRoadPrefabSystem.FordereAn(_zpOriginalPrefab,
                Strassenklonart.Zoning, out var fehlgeschlagen,
                out var aufgegeben);
            if (fehlgeschlagen || aufgegeben)
            {
                ZpLog("2 START abgebrochen: Der unsichtbare Klon konnte nicht "
                    + "angemeldet werden. Der Grund steht in den Zeilen mit "
                    + "'PLT-Zoningstrasse'.");
                _zpPhase = ZpPhase.Idle;
                return false;
            }
            if (klon == Entity.Null)
            {
                if (++_zpPhaseFrame <= 300) return true;
                ZpLog("2 START abgebrochen: Der unsichtbare Klon war nach 300 "
                    + "Frames noch nicht benutzbar.");
                _zpPhase = ZpPhase.Idle;
                return false;
            }

            _zpRoadPrefab = klon;
            ZpBaueStrasse();
            return true;
        }

        private static CoursePos ZpCoursePos(float3 position, quaternion rotation,
                                             bool first) => new CoursePos
        {
            m_Entity = Entity.Null,
            m_Position = position,
            m_Rotation = rotation,
            m_CourseDelta = first ? 0f : 1f,
            m_Elevation = float2.zero,
            m_Flags = first ? CoursePosFlags.IsFirst : CoursePosFlags.IsLast,
            m_ParentMesh = -1,
            m_SplitPosition = 0f,
        };

        private bool ZpUebernimmTempStrasse()
        {
            if (++_zpPhaseFrame > 60)
            {
                applyMode = ApplyMode.Clear;
                ZpLog("2 FEHLER: Nach 60 Frames keine passende Temp-Kante gefunden.");
                ZpLoescheTraegerSofort();
                _zpPhase = ZpPhase.Idle;
                return true;
            }

            using var entities = _zpTempNets.ToEntityArray(Allocator.Temp);
            Entity edgeEntity = Entity.Null;
            var best = 4f;
            var wantedMid = (_zpStart + _zpEnd) * 0.5f;
            for (var i = 0; i < entities.Length; i++)
            {
                var candidate = entities[i];
                var prefab = EntityManager.GetComponentData<PrefabRef>(candidate).m_Prefab;
                if (prefab != _zpRoadPrefab) continue;
                var curve = EntityManager.GetComponentData<Game.Net.Curve>(candidate);
                var mid = MathUtils.Position(curve.m_Bezier, 0.5f);
                var distance = math.distance(mid.xz, wantedMid.xz);
                if (distance >= best) continue;
                best = distance;
                edgeEntity = candidate;
            }
            if (edgeEntity == Entity.Null) return true;

            _zpEdge = edgeEntity;
            var edge = EntityManager.GetComponentData<Game.Net.Edge>(_zpEdge);
            ZpSetOwner(_zpEdge, _zpCarrier);
            ZpSetOwner(edge.m_Start, _zpCarrier);
            ZpSetOwner(edge.m_End, _zpCarrier);
            EntityManager.GetBuffer<Game.Net.SubNet>(_zpCarrier)
                .Add(new Game.Net.SubNet(_zpEdge));
            var marker = EntityManager.GetComponentData<ParkingLotZoningProbeMarker>(
                _zpCarrier);
            marker.Edge = _zpEdge;
            EntityManager.SetComponentData(_zpCarrier, marker);

            applyMode = ApplyMode.Apply;
            _zpPhase = ZpPhase.StrasseMessen;
            _zpPhaseFrame = 0;
            ZpLog($"2 TEMP UEBERNOMMEN: Kante {ZpEntity(_zpEdge)}, Knoten "
                + $"{ZpEntity(edge.m_Start)} und {ZpEntity(edge.m_End)} haben "
                + $"Owner={ZpEntity(_zpCarrier)}; Kante steht im SubNet-Puffer; "
                + "Apply ausgeloest, Messung folgt nach 24 Frames.");
            return true;
        }

        private void ZpSetOwner(Entity entity, Entity owner)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity)) return;
            if (EntityManager.HasComponent<Owner>(entity))
                EntityManager.SetComponentData(entity, new Owner(owner));
            else
                EntityManager.AddComponentData(entity, new Owner(owner));
        }

        private bool ZpTryRoadPrefab(string name, out Entity result)
        {
            result = Entity.Null;
            Entity fuzzy = Entity.Null;
            using var entities = _zpRoadPrefabs.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (_prefabSystem == null
                    || !_prefabSystem.TryGetPrefab<RoadPrefab>(entity,
                        out var roadPrefab)
                    || roadPrefab == null || roadPrefab.m_ZoneBlock == null
                    || EntityManager.GetComponentData<RoadData>(entity)
                        .m_ZoneBlockPrefab == Entity.Null)
                    continue;
                var actual = roadPrefab.name;
                if (string.Equals(actual, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                    return true;
                }
                // Die Auswahl nennt die Nutzerkategorie. Der Katalog nennt
                // danach den wirklichen Prefabnamen; diese enge Rueckfallwahl
                // haelt den Bau auch bei einem Namenszusatz der Spielversion
                // benutzbar, ohne irgendeine breite Strasse zu erraten.
                var matches = string.Equals(name, "Alley", StringComparison.Ordinal)
                    ? actual.IndexOf("Alley", StringComparison.OrdinalIgnoreCase) >= 0
                    : actual.IndexOf("Gravel", StringComparison.OrdinalIgnoreCase) >= 0
                      && actual.IndexOf("Road", StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches && fuzzy == Entity.Null) fuzzy = entity;
            }
            result = fuzzy;
            return result != Entity.Null;
        }

        private bool ZpHatTraeger()
        {
            using var carriers = _zpCarriers.ToEntityArray(Allocator.Temp);
            return carriers.Length != 0;
        }

        private static string ZpEntity(Entity entity) => entity == Entity.Null
            ? "Entity.Null" : $"#{entity.Index}.{entity.Version}";

        private static string ZpFloat3(float3 value) =>
            $"({value.x:F2}/{value.y:F2}/{value.z:F2})";

        private static void ZpLog(string text) => Mod.log.Info(ZpPrefix + " " + text);
    }
}
