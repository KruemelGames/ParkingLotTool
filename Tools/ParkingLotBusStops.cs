using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.City;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * Vanilla-Weg: Game.Tools.GenerateObjectsSystem.cs:1235-1269 erzeugt
     * ObjectData.m_Archetype mit Owner und Attached; 1437-1445 berechnet
     * die Kurvenposition. Deshalb bestellen wir dieselbe Definition nach
     * der Netzmaterialisierung. Der Alley-Abzug vom 24.09. nennt 7 Sections
     * und je Schulter 1 Boarding- und 1 Fussgaengerspur. Die Pruefung vor
     * dem Bestellen zaehlt diese Spuren erneut an der gebauten Kante.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<BusStopPlacement> _busStops = new List<BusStopPlacement>();
        private bool _busStopMode;
        private bool _hasBusStopCandidate;
        private BusStopPlacement _busStopCandidate;
        private EntityQuery _busStopPrefabQuery;
        private Entity _busStopPrefab = Entity.Null;
        private string _busStopPrefabName;
        private BusStopPlacement[] _pendingBusStops = Array.Empty<BusStopPlacement>();
        private Entity _pendingBusLot = Entity.Null;
        private Entity _pendingBusCarrier = Entity.Null;
        private int _busStopAuditFrame = -1;
        /**
         * Restliche AUFRUFE des Aufpassers, nicht eine Bildnummer: ruht die
         * Nacharbeit waehrend eines Abrisses (`PflegeNacharbeit`), ruht diese
         * Frist mit. Mit `frameCount + 90` lief sie weiter und haette nach
         * einem langen Abriss die Halte verworfen, bevor sie gebaut werden
         * konnten (Codex, 2026-09-25). -1 = keine Frist.
         */
        private int _busStopBuildDeadline = -1;

        private void InitializeBusStops()
        {
            _busStopPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<ObjectData>());
        }

        internal void SetBusStopModeFromPanel(bool on)
        {
            if (on && (!_closed || _reiter != Werkzeugreiter.Entwurf)) return;
            if (_busStopMode == on) return;
            if (on)
            {
                SetEntranceModeFromPanel(false);
                SetzeZoningModus(false);
                _busStopMode = true;
                _uiSystem?.SetStatus(T("Bushaltestelle auf eine Zoning-Strasse setzen.",
                    "Place this on a zoning road."));
            }
            else
            {
                _busStopMode = false;
                _hasBusStopCandidate = false;
                _debugTooltipSystem?.ClearEntranceHint();
            }
            _uiSystem?.SetBusStopMode(_busStopMode);
        }

        private void ResetBusStopEditing(bool clear)
        {
            if (clear) _busStops.Clear();
            _busStopMode = false;
            _hasBusStopCandidate = false;
            _debugTooltipSystem?.ClearEntranceHint();
            _uiSystem?.SetBusStopMode(false);
        }

        private bool HandleBusStopInput(bool left, bool right, bool escape)
        {
            if (!_busStopMode) return false;
            if (escape)
            {
                SetBusStopModeFromPanel(false);
                return true;
            }
            // Shift schaltet das Gegenueber-Einrasten ab, wie bei den
            // Zufahrten. Kreuzungen werden trotzdem uebersprungen.
            // Die Breiten entscheiden, wo eine Fahrgasse in die Zoning-
            // Strasse muendet (ZoningMuendung) - aus denselben Einstellungen
            // wie das Layout, das hier gezeigt wird.
            var breiten = _areaPreviewSettings ?? LayoutSettings.Cs2;
            _hasBusStopCandidate = _hasHover && BusStopSnap.TryFind(
                _areaPreviewLayout, _hoverPosition.xz, 8f, breiten.Ai, breiten.Cw,
                out _busStopCandidate, _busStops, ShiftGehalten());
            _debugTooltipSystem?.SetEntranceHint(T(
                _hasBusStopCandidate
                    ? "Linksklick setzt den Halt auf dieser Strassenseite; Rechtsklick entfernt ihn."
                    : "Auf eine Zoning-Strasse setzen",
                _hasBusStopCandidate
                    ? "Left click places a stop on this side; right click removes it."
                    : "Place this on a zoning road"));
            if (left && _hasBusStopCandidate)
            {
                var neu = _busStopCandidate;
                var doppelt = false;
                foreach (var alt in _busStops)
                    if (BusStopSnap.IsDuplicate(alt, neu))
                        doppelt = true;
                if (!doppelt)
                {
                    _busStops.Add(neu);
                    _layoutDirty = true;
                    _geometryRevision++;
                }
            }
            if (right && _hasHover)
            {
                var index = -1;
                var best = 36f;
                for (var i = 0; i < _busStops.Count; i++)
                {
                    var d = math.distancesq(BusStopSnap.SignPosition(_busStops[i]),
                        _hoverPosition.xz);
                    if (d >= best) continue;
                    best = d;
                    index = i;
                }
                if (index >= 0)
                {
                    _busStops.RemoveAt(index);
                    _layoutDirty = true;
                    _geometryRevision++;
                }
            }
            return true;
        }

        private void DrawBusStops(ParkingLotPreviewBuffer buffer)
        {
            var height = _terrainSystem.GetHeightData();
            var city = World.GetExistingSystemManaged<CityConfigurationSystem>();
            var leftHandTraffic = city != null && city.leftHandTraffic;
            void Draw(BusStopPlacement stop, UnityEngine.Color color)
            {
                var sign = BusStopSnap.SignPosition(stop);
                var center = stop.Position;
                var along = BusStopSnap.TravelDirection(stop,
                    leftHandTraffic) * 2f;
                float3 World(float2 p)
                {
                    var point = new float3(p.x, 0f, p.y);
                    point.y = TerrainUtils.SampleHeight(ref height, point);
                    return point;
                }
                buffer.DrawLine(color, new Line3.Segment(World(center),
                    World(sign)), 0.45f);
                buffer.DrawCircle(color, World(sign), 3f);
                var tip = sign + along;
                var arrowSide = new float2(-along.y, along.x);
                var wing = tip - along * .4f;
                arrowSide = math.normalizesafe(arrowSide) * .6f;
                buffer.DrawLine(color, new Line3.Segment(World(sign),
                    World(tip)), 0.6f);
                buffer.DrawLine(color, new Line3.Segment(World(wing + arrowSide),
                    World(tip)), 0.6f);
                buffer.DrawLine(color, new Line3.Segment(World(wing - arrowSide),
                    World(tip)), 0.6f);
            }
            foreach (var stop in _busStops) Draw(stop, ParkingLotPreviewStyle.BusStopColor);
            if (_busStopMode && _hasBusStopCandidate)
                Draw(_busStopCandidate, ParkingLotPreviewStyle.BusStopHoverColor);
        }

        /**
         * SUCHE NACH FUNKTION, NICHT NACH NAMEN.
         *
         * Der erste Entwurf verlangte "BusStopSign" im Prefabnamen - ein
         * geratener Name, im Dekompilat nicht belegt. Im Spiel des Nutzers
         * am 2026-09-24 um 20:26 fand er nichts:
         *
         *     PLT-Bushalt: kein funktionsfaehiges Vanilla-Bus-Stop-Sign-
         *     Prefab mit Strassenanbindung gefunden.
         *
         * und sagte nicht, woran es lag. Jetzt entscheidet, was das Prefab
         * KANN (belegt in ANTWORT-BUSHALTESTELLE.md): Bushaltestelle mit
         * Fahrgaesten (`TransportStopData`), Anbindung an eine Strasse fuer
         * Autos (`RouteConnectionData`), am Strassenrand setzbar
         * (`PlacementFlags.RoadSide`), mit gueltigem Archetyp. Der Name
         * entscheidet nur noch zwischen mehreren Treffern: ein "Sign" wird
         * einem Unterstand vorgezogen, weil der Nutzer das Schild will.
         *
         * JEDER Bus-Kandidat steht mit seinem Ergebnis im Log - beim
         * naechsten Fehlschlag ist der Grund damit sofort zu lesen.
         */
        private bool ResolveBusStopPrefab()
        {
            if (_busStopPrefab != Entity.Null && EntityManager.Exists(_busStopPrefab))
                return true;
            using var prefabs = _busStopPrefabQuery.ToEntityArray(Allocator.Temp);
            var best = -1;
            var found = Entity.Null;
            var foundName = string.Empty;
            var city = World.GetExistingSystemManaged<CityConfigurationSystem>();
            var theme = city != null ? city.defaultTheme : Entity.Null;
            var protokoll = new List<string>();
            for (var i = 0; i < prefabs.Length; i++)
            {
                var candidate = prefabs[i];
                if (!EntityManager.HasComponent<TransportStopData>(candidate)) continue;
                if (EntityManager.GetComponentData<TransportStopData>(candidate)
                        .m_TransportType != TransportType.Bus) continue;
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(candidate, out var p)
                    || p == null) continue;
                var grund = PruefeBusKandidat(candidate, p, out var score, out var gewaehlt);
                protokoll.Add("'" + p.name + "'" + (gewaehlt != candidate
                    && _prefabSystem.TryGetPrefab<PrefabBase>(gewaehlt, out var v)
                    && v != null ? " -> '" + v.name + "'" : "") + ": " + grund);
                if (score < 0) continue;
                if (theme != Entity.Null
                    && EntityManager.HasBuffer<ObjectRequirementElement>(gewaehlt))
                {
                    var anf = EntityManager.GetBuffer<ObjectRequirementElement>(gewaehlt, true);
                    for (var k = 0; k < anf.Length; k++)
                        if (anf[k].m_Requirement == theme) score += 2;
                }
                if (score <= best) continue;
                best = score;
                found = gewaehlt;
                foundName = _prefabSystem.TryGetPrefab<PrefabBase>(gewaehlt,
                    out var actual) && actual != null ? actual.name : p.name;
            }
            Mod.log.Info("PLT-Bushalt Kandidaten (" + protokoll.Count + "): "
                + (protokoll.Count == 0 ? "keine Bus-TransportStopData im Spiel"
                    : string.Join(" | ", protokoll)));
            if (found == Entity.Null)
            {
                Mod.log.Warn("PLT-Bushalt: kein Prefab erfuellt die Bedingungen; "
                    + "Gruende stehen in 'PLT-Bushalt Kandidaten'.");
                return false;
            }
            _busStopPrefab = found;
            _busStopPrefabName = foundName;
            var themeName = theme != Entity.Null
                && _prefabSystem.TryGetPrefab<PrefabBase>(theme, out var themePrefab)
                && themePrefab != null ? themePrefab.name : "unbekannt";
            Mod.log.Info("PLT-Bushalt: Vanilla-Prefab '" + foundName
                + "' gewaehlt; Stadtthema='" + themeName + "'.");
            return true;
        }

        /** "ok ..." oder der erste Grund, warum der Kandidat nicht taugt. */
        private string PruefeBusKandidat(Entity candidate, PrefabBase p,
            out int score, out Entity gewaehlt)
        {
            score = -1;
            gewaehlt = candidate;
            if (!p.isBuiltin) return "kein Spiel-Prefab";
            // Die Haltestelle IN Gebaeuden (Busbahnhof, Depot) ist kein Schild
            // und ohne ihr Gebaeude unsichtbar.
            if (p.name.IndexOf("Integrated", StringComparison.OrdinalIgnoreCase) >= 0)
                return "in Gebaeude integriert";
            var variante = WaehleThemenvariante(candidate);
            if (variante != Entity.Null) gewaehlt = variante;
            if (!EntityManager.HasComponent<TransportStopData>(gewaehlt))
                return "Variante ohne TransportStopData";
            var data = EntityManager.GetComponentData<TransportStopData>(gewaehlt);
            if (!data.m_PassengerTransport) return "keine Fahrgaeste";
            if (!EntityManager.HasComponent<RouteConnectionData>(gewaehlt))
                return "keine RouteConnectionData";
            var route = EntityManager.GetComponentData<RouteConnectionData>(gewaehlt);
            if (route.m_RouteConnectionType != RouteConnectionType.Road)
                return "Anbindung " + route.m_RouteConnectionType + " statt Road";
            if ((route.m_RouteRoadType & RoadTypes.Car) == 0)
                return "Strassentyp " + route.m_RouteRoadType + " ohne Car";
            if (!EntityManager.HasComponent<PlaceableObjectData>(gewaehlt))
                return "nicht setzbar (keine PlaceableObjectData)";
            var place = EntityManager.GetComponentData<PlaceableObjectData>(gewaehlt);
            /*
             * RoadEdge, nicht RoadSide. Im Spiel des Nutzers am 2026-09-24
             * tragen alle Vanilla-Haltestellen (NA_/EU_BusStop01/02 und die
             * Fahrradvarianten) "OnGround, NetObject, RoadEdge, Attached" -
             * RoadSide hat keine. Der erste Entwurf verlangte RoadSide und
             * verwarf damit jede einzelne.
             */
            if ((place.m_Flags & (Game.Objects.PlacementFlags.RoadEdge
                    | Game.Objects.PlacementFlags.RoadSide)) == 0)
                return "nicht an der Strasse setzbar (Placement " + place.m_Flags + ")";
            if (!EntityManager.HasComponent<ObjectData>(gewaehlt)
                || !EntityManager.GetComponentData<ObjectData>(gewaehlt).m_Archetype.Valid)
                return "kein gueltiger Archetyp";
            /*
             * SCHILD ODER UNTERSTAND - DIE GRUNDFLAECHE ENTSCHEIDET.
             *
             * Die Namen verraten es nicht (BusStop01, BusStop02). Ein Schild
             * ist ein Pfosten, ein Unterstand mehrere Meter breit. Der Nutzer
             * will das Schild. Fahrrad-Varianten und Paket-Inhalte ("Pack7-")
             * nur, wenn nichts anderes passt.
             */
            var flaeche = -1f;
            if (EntityManager.HasComponent<ObjectGeometryData>(gewaehlt))
            {
                var g = EntityManager.GetComponentData<ObjectGeometryData>(gewaehlt);
                var groesse = g.m_Bounds.max - g.m_Bounds.min;
                flaeche = groesse.x * groesse.z;
            }
            score = 10;
            if (flaeche >= 0f && flaeche < 4f) score += 8;
            if (p.name.IndexOf("Bicycle", StringComparison.OrdinalIgnoreCase) >= 0) score -= 6;
            if (p.name.StartsWith("Pack", StringComparison.OrdinalIgnoreCase)) score -= 3;
            if (EntityManager.HasBuffer<PlaceholderObjectElement>(candidate)) score += 1;
            return "ok (Punkte " + score + ", Grundflaeche "
                + (flaeche < 0f ? "?" : flaeche.ToString("F1")) + " m2, Access "
                + route.m_AccessConnectionType + ")";
        }

        private void PlanBusStopBuild(Entity lot, Entity carrier)
        {
            _pendingBusStops = _busStops.ToArray();
            _pendingBusLot = lot;
            _pendingBusCarrier = carrier;
            _busStopBuildDeadline = 90;
        }

        private void BuildBusStopsOnRoads(Entity carrier)
        {
            if (_pendingBusStops.Length == 0) return;
            if (carrier != _pendingBusCarrier || !EntityManager.Exists(carrier)
                || !EntityManager.HasBuffer<Game.Net.SubNet>(carrier)) return;
            if (!ResolveBusStopPrefab()) return;
            var subnets = EntityManager.GetBuffer<Game.Net.SubNet>(carrier, true);
            var routeData = EntityManager.GetComponentData<RouteConnectionData>(
                _busStopPrefab);
            var built = 0;
            for (var i = 0; i < _pendingBusStops.Length; i++)
            {
                var stop = _pendingBusStops[i];
                var target = stop.Position;
                var edge = Entity.Null;
                var best = 1f;
                var along = 0f;
                var reversed = false;
                for (var k = 0; k < subnets.Length; k++)
                {
                    var candidate = subnets[k].m_SubNet;
                    if (!EntityManager.Exists(candidate)
                        || !EntityManager.HasComponent<PrefabRef>(candidate)
                        || !EntityManager.HasComponent<Curve>(candidate)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(candidate).m_Prefab;
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var road)
                        || road == null || !road.name.StartsWith("PLT Zoningstrasse"))
                        continue;
                    var curve = EntityManager.GetComponentData<Curve>(candidate).m_Bezier;
                    var a = curve.a.xz;
                    var b = curve.d.xz;
                    if (!BusStopSnap.TryProjectToEdge(stop, a, b,
                        out var t, out var flipped, out var distance)) continue;
                    if (distance >= best) continue;
                    best = distance;
                    edge = candidate;
                    along = t;
                    reversed = flipped;
                }
                if (edge == Entity.Null)
                {
                    Mod.log.Warn($"PLT-Bushalt {i}: keine gebaute Zoning-Kante "
                        + $"bei {target.x:F1}/{target.y:F1}; Schild ausgelassen.");
                    continue;
                }
                var carLanes = 0;
                var walkLanes = 0;
                if (EntityManager.HasBuffer<Game.Net.SubLane>(edge))
                {
                    var lanes = EntityManager.GetBuffer<Game.Net.SubLane>(edge, true);
                    for (var k = 0; k < lanes.Length; k++)
                    {
                        var lane = lanes[k].m_SubLane;
                        if (EntityManager.HasComponent<Game.Net.CarLane>(lane)
                            && EntityManager.HasComponent<PrefabRef>(lane))
                        {
                            var carLane = EntityManager.GetComponentData<
                                Game.Net.CarLane>(lane);
                            var lanePrefab = EntityManager.GetComponentData<
                                PrefabRef>(lane).m_Prefab;
                            if ((carLane.m_Flags & CarLaneFlags.Unsafe) == 0
                                && EntityManager.HasComponent<CarLaneData>(lanePrefab)
                                && (EntityManager.GetComponentData<CarLaneData>(
                                    lanePrefab).m_RoadTypes
                                    & routeData.m_RouteRoadType) != 0)
                                carLanes++;
                        }
                        if (EntityManager.HasComponent<Game.Net.PedestrianLane>(lane))
                        {
                            var pedestrian = EntityManager.GetComponentData<
                                Game.Net.PedestrianLane>(lane);
                            if ((pedestrian.m_Flags & (PedestrianLaneFlags.AllowMiddle
                                | PedestrianLaneFlags.OnWater))
                                == PedestrianLaneFlags.AllowMiddle)
                                walkLanes++;
                        }
                    }
                }
                if (carLanes == 0 || walkLanes == 0)
                {
                    Mod.log.Warn($"PLT-Bushalt {i}: Zoning-Kante {edge.Index} "
                        + $"hat CarLane={carLanes}, PedestrianLane={walkLanes}; "
                        + "kein funktionsloses Schild gebaut.");
                    continue;
                }
                var realSide = reversed ? !stop.Left : stop.Left;
                var sign = BusStopSnap.SignPosition(stop);
                var point = new float3(sign.x, 0f, sign.y);
                var height = _terrainSystem.GetHeightData();
                point.y = TerrainUtils.SampleHeight(ref height, point);
                var city = World.GetExistingSystemManaged<CityConfigurationSystem>();
                var travel = BusStopSnap.TravelDirection(stop,
                    city != null && city.leftHandTraffic);
                var direction3 = new float3(travel.x, 0f, travel.y);
                var definition = EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition, new CreationDefinition
                {
                    m_Prefab = _busStopPrefab,
                    /*
                     * KEIN BESITZER IN DER DEFINITION.
                     *
                     * Mit `m_Owner = Lot` traegt CS2 das Schild in die
                     * SubObject-Liste der Lot-FLAECHE ein. Genau diese Liste
                     * verteilt `SubObjectSystem.RelocateSubObjects` zufaellig
                     * neu, sobald die Flaeche `Updated` wird - so sind am
                     * 2026-08-12 die Buchtaufkleber ueber den Parkplatz
                     * gewandert, als nebenan eine Strasse gebaut wurde.
                     *
                     * Eine frei gesetzte Vanilla-Haltestelle hat ebenfalls
                     * keinen Besitzer, nur `Attached` an der Kante - und
                     * genau so bleibt es, sonst ist sie im Linienwerkzeug
                     * nicht anwaehlbar (siehe `AuditBuiltBusStops`).
                     */
                    m_Owner = Entity.Null,
                    m_Attached = edge,
                    m_Flags = CreationFlags.Attach | CreationFlags.Permanent,
                    m_RandomSeed = math.max(1, definition.Index),
                });
                EntityManager.AddComponentData(definition, new ObjectDefinition
                {
                    m_Position = point,
                    m_Rotation = quaternion.LookRotationSafe(direction3, math.up()),
                    m_Probability = 100,
                    m_PrefabSubIndex = -1,
                    m_Scale = 1f,
                    m_Intensity = 1f,
                    m_ParentMesh = -1,
                });
                EntityManager.AddComponent<Updated>(definition);
                built++;
                Mod.log.Info($"PLT-Bushalt {i}: Kante {edge.Index}, "
                    + $"{(realSide ? "links" : "rechts")}, t={along:F3}, "
                    + $"Prefab '{_busStopPrefabName}', Definition=Attach/Permanent; "
                    + "Spurpruefung folgt.");
            }
            Mod.log.Info("PLT-Bushalt: " + built + "/"
                + _pendingBusStops.Length + " an Zoning-Kanten bestellt.");
            _busStopAuditFrame = UnityEngine.Time.frameCount + 8;
            _busStopBuildDeadline = -1;
            _pendingBusStops = Array.Empty<BusStopPlacement>();
        }

        private void AuditBuiltBusStops()
        {
            if (_pendingBusStops.Length > 0
                && _busStopBuildDeadline > 0
                && --_busStopBuildDeadline == 0)
            {
                Mod.log.Warn("PLT-Bushalt: " + _pendingBusStops.Length
                    + " Platzierung(en) ohne fertige Zoning-Kanten nach 90 Frames; "
                    + "kein Schild gebaut. Siehe Bauzettel.");
                _pendingBusStops = Array.Empty<BusStopPlacement>();
                _busStopBuildDeadline = -1;
            }
            if (_busStopAuditFrame < 0) return;
            var finalAudit = UnityEngine.Time.frameCount >= _busStopAuditFrame;
            if (finalAudit) _busStopAuditFrame = -1;
            if (_pendingBusLot == Entity.Null
                || !EntityManager.Exists(_pendingBusLot)) return;
            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Routes.TransportStop>(),
                ComponentType.ReadOnly<Attached>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            using var stops = query.ToEntityArray(Allocator.Temp);
            var count = 0;
            for (var i = 0; i < stops.Length; i++)
            {
                var entity = stops[i];
                var attached = EntityManager.GetComponentData<Attached>(entity);
                var edge = attached.m_Parent;
                // UNSER Schild erkennt man an der Kante: sie ist eine
                // Zoning-Kante dieses Parkplatzes (Owner = Lot). Einen
                // eigenen Besitzer hat das Schild bis hierher nicht.
                var unsere = EntityManager.Exists(edge)
                    && EntityManager.HasComponent<Owner>(edge)
                    && EntityManager.GetComponentData<Owner>(edge).m_Owner
                        == _pendingBusLot;
                var schonZugeordnet = EntityManager
                        .HasComponent<ParkingLotPartRelation>(entity)
                    && EntityManager.GetComponentData<ParkingLotPartRelation>(
                        entity).Lot == _pendingBusLot;
                if (!unsere && !schonZugeordnet) continue;
                /*
                 * KEIN BESITZER - WIE EINE FREI GESETZTE VANILLA-HALTESTELLE.
                 *
                 * Mit dem Traeger als Besitzer war das Schild im Linienwerkzeug
                 * nicht anwaehlbar (Nutzer, 2026-09-24): ohne `SubElements`
                 * meldet der Raycast den OBERSTEN Besitzer, hier also die
                 * Lot-Flaeche, und `RouteToolSystem.FindWaypointLocation`
                 * findet von dort keinen Halt (Dekompilat, Zeile 268-360).
                 * Verstreut wird ohne Besitzer nichts - das trifft nur Kinder
                 * einer FLAECHE -, und der Vegetationspinsel nimmt nur
                 * `Brushable`-Deko. Die Teilrelation darunter braucht der
                 * Abriss.
                 */
                var car = 0;
                var walk = 0;
                if (EntityManager.Exists(edge)
                    && EntityManager.HasBuffer<Game.Net.SubLane>(edge))
                {
                    var lanes = EntityManager.GetBuffer<Game.Net.SubLane>(edge, true);
                    for (var k = 0; k < lanes.Length; k++)
                    {
                        var lane = lanes[k].m_SubLane;
                        if (EntityManager.HasComponent<Game.Net.CarLane>(lane)) car++;
                        if (EntityManager.HasComponent<Game.Net.PedestrianLane>(lane)) walk++;
                    }
                }
                if (!EntityManager.HasComponent<ParkingLotPartRelation>(entity))
                    EntityManager.AddComponentData(entity, new ParkingLotPartRelation
                    {
                        Lot = _pendingBusLot,
                        Carrier = _pendingBusCarrier,
                    });
                if (!finalAudit) continue;
                var routeCount = EntityManager.HasBuffer<ConnectedRoute>(entity)
                    ? EntityManager.GetBuffer<ConnectedRoute>(entity, true).Length : 0;
                var connectedLanes = 0;
                if (routeCount > 0)
                {
                    var routes = EntityManager.GetBuffer<ConnectedRoute>(entity,
                        true);
                    for (var k = 0; k < routes.Length; k++)
                    {
                        var waypoint = routes[k].m_Waypoint;
                        if (!EntityManager.Exists(waypoint)
                            || !EntityManager.HasComponent<RouteLane>(waypoint)
                            || !EntityManager.HasComponent<AccessLane>(waypoint))
                            continue;
                        var lane = EntityManager.GetComponentData<RouteLane>(waypoint);
                        var access = EntityManager.GetComponentData<AccessLane>(waypoint);
                        if (lane.m_StartLane != Entity.Null
                            && access.m_Lane != Entity.Null) connectedLanes++;
                    }
                }
                Mod.log.Info($"PLT-Bushalt gebaut: Entity {entity.Index}, "
                    + $"Kante {edge.Index}, CarLane={car}, PedestrianLane={walk}, "
                    + $"TransportStop=ja, Linien={routeCount}, "
                    + $"Linien mit RouteLane und AccessLane={connectedLanes}; "
                    + (routeCount == 0 ? "Linien-Spurverbindung erst nach Linienbau pruefbar"
                        : connectedLanes == routeCount ? "Linien-Spurverbindung=ja"
                        : "WARNUNG: Linien-Spurverbindung fehlt"));
                count++;
            }
            if (finalAudit)
                Mod.log.Info("PLT-Bushalt Schlusspruefung: " + count
                    + " TransportStop-Entities mit Besitzer und Kantenanbindung.");
        }
    }
}
