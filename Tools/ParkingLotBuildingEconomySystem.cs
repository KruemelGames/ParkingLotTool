using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Policies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Baut je PLT-Flaeche eine getrennte, unsichtbare Building-Entity.
     *
     * Die fachliche Lot-Flaeche bleibt eine reine Area. Der Begleiter traegt
     * die Building-Komponenten und verweist auf ein echtes Vanilla-
     * BuildingPrefab. Seine gespeicherte Teilrelation und sein `Attached` zum
     * nackten Traeger geben ihm denselben Lade-, Auswahl- und Abrissweg wie den
     * uebrigen PLT-Objekten.
     */
    public sealed partial class ParkingLotBuildingEconomySystem : GameSystemBase
    {
        internal const string CompanionPrefabName = "PLT Wirtschaftsbegleiter";
        internal const string CompanionSourcePrefabName = "ParkingLot04";

        private EntityQuery _lots;
        private EntityQuery _companions;
        private EntityQuery _legacyLots;
        private EntityQuery _buildingPrefabs;
        private PrefabSystem _prefabSystem;
        private TerrainSystem _terrain;
        private ElectricityRoadConnectionGraphSystem _electricityRoadGraph;
        private Entity _companionPrefab = Entity.Null;
        private bool _companionPrefabConfigured;
        private bool _flachzoneGemeldet;
        private bool _companionPrefabSafe;
        private bool _prefabFailureLogged;
        // Wie oft das Prefabrezept schon fuer untauglich befunden wurde. Erst
        // nach dieser Zahl ist es ein Fehler und keine Anlaufverzoegerung.
        private const int BeurteilungenBisMeldung = 600;
        private int _beurteilungen;
        private bool _archetypNachgeholt;
        private bool? _lastEnabled;
        /*
         * DIE EINE FRAGE, DIE ZWEI BEFUNDE AUF EINMAL ERKLAERT.
         *
         * Nutzerbefund 2026-08-26: ueber einem Parkplatz stand "Mangel an
         * Arbeitskraeften", obwohl die Stadt genug Leute hat. Und der Strom
         * kam vorher nie an. Beides braucht dasselbe: einen Begleiter, den
         * CS2 an eine STRASSE angebunden hat. Buerger muessen zur Arbeit
         * laufen koennen, Strom fliesst ueber die Strassenkante.
         *
         * Ob `RoadConnectionSystem` unserem Begleiter eine Kante gibt, hat
         * bisher niemand nachgesehen - wir haben nur vermutet. Diese zwei
         * Felder sorgen dafuer, dass die Antwort einmal je Begleiter im Log
         * steht, so oder so.
         */
        private readonly Dictionary<Entity, int> _strassenWartet =
            new Dictionary<Entity, int>();
        private readonly HashSet<Entity> _strassenGemeldet = new HashSet<Entity>();

        private readonly HashSet<Entity> _announcedCompanions =
            new HashSet<Entity>();
        private readonly HashSet<Entity> _incompleteCompanionsLogged =
            new HashSet<Entity>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _electricityRoadGraph = World.GetOrCreateSystemManaged<
                ElectricityRoadConnectionGraphSystem>();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<ParkingLotEconomyData>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    // Harte Schranke: eine bereits beschaedigte Lot-Flaeche
                    // wird niemals als Quelle fuer einen Begleiter akzeptiert.
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
            });
            _companions = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                ComponentType.ReadOnly<ParkingLotPartRelation>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            _legacyLots = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                ComponentType.ReadOnly<Area>(),
                ComponentType.Exclude<ParkingLotPartRelation>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            _buildingPrefabs = GetEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<PollutionData>(),
                ComponentType.ReadOnly<DestructibleObjectData>(),
                ComponentType.ReadOnly<ObjectGeometryData>(),
                ComponentType.ReadOnly<ConsumptionData>(),
                ComponentType.ReadOnly<WorkplaceData>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Begleiter);
            if (!Auftraege.Register.Bereit) return;
            DisableLegacyLots();
            var enabled = Mod.WirtschaftAn;
            if (_lastEnabled != enabled)
            {
                Mod.log.Info("PLT-Building-Wirtschaft (Begleiter): "
                    + (enabled ? "EIN" : "AUS") + ".");
                _lastEnabled = enabled;
            }
            if (!enabled)
            {
                foreach (var lot in new List<Entity>(_anschluesse.Keys))
                {
                    var a = _anschluesse[lot];
                    if (AnschlussDarfArbeiten(lot, a)) AnschlussBeendet(lot, a.Auftrag, false, "Wirtschaft ausgeschaltet");
                }
                DisableAll();
                return;
            }
            var byLot = CollectCompanionsByLot();
            NimmOffeneAnschluesseWiederAuf();
            PflegeAnschluesse(byLot);
            if (!TryResolveCompanionPrefab(out var prefab)) return;
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                var lotPrefab = EntityManager.GetComponentData<PrefabRef>(lot)
                    .m_Prefab;
                if (!IsOwnLotPrefab(lotPrefab)) continue;
                byLot.TryGetValue(lot, out var companion);
                EnableLot(lot, companion, prefab);
            }

            MeldeSteckbriefe(lots, byLot);
        }

        private string _letzterSteckbrief = string.Empty;

        /** Arbeitsuchende laufen zur Gebaeudevorderseite: die Kante braucht eine Fussgaengerspur. */
        private bool HatGehweg(Entity kante)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(kante)) return false;
            var spuren = EntityManager.GetBuffer<Game.Net.SubLane>(kante, true);
            // Wie Vanilla (PathfindTargetSeeker.AddEdgeTargets): Fussgaenger-
            // Pfadmethode UND Gehweg- oder Fussgaenger-Verbindungsspur.
            for (var i = 0; i < spuren.Length; i++)
            {
                if ((spuren[i].m_PathMethods & Game.Pathfind.PathMethod.Pedestrian) == 0) continue;
                var spur = spuren[i].m_SubLane;
                if (EntityManager.HasComponent<Game.Net.PedestrianLane>(spur)) return true;
                if (EntityManager.HasComponent<Game.Net.ConnectionLane>(spur)
                    && (EntityManager.GetComponentData<Game.Net.ConnectionLane>(spur).m_Flags
                        & Game.Net.ConnectionLaneFlags.Pedestrian) != 0) return true;
            }
            return false;
        }

        /** "2/4" - besetzt / Plaetze, wie das Infofenster. */
        private string Personal(Entity begleiter)
        {
            var plaetze = EntityManager.HasComponent<WorkProvider>(begleiter)
                ? EntityManager.GetComponentData<WorkProvider>(begleiter).m_MaxWorkers : 0;
            var besetzt = EntityManager.HasBuffer<Employee>(begleiter)
                ? EntityManager.GetBuffer<Employee>(begleiter, true).Length : 0;
            return besetzt + "/" + plaetze;
        }

        /**
         * Eine Zeile je Parkplatz: was sein Infofenster fuellt.
         *
         * Gemeldet wird nur, wenn sich etwas geaendert hat - sonst stuende
         * dieselbe Liste bei jedem Durchgang im Log.
         */
        private void MeldeSteckbriefe(NativeArray<Entity> lots,
                                      Dictionary<Entity, Entity> byLot)
        {
            var zeilen = new List<string>();
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                if (!IsOwnLotPrefab(EntityManager
                        .GetComponentData<PrefabRef>(lot).m_Prefab))
                    continue;

                var anlage = EntityManager
                    .HasComponent<Game.Buildings.ParkingFacility>(lot);
                byLot.TryGetValue(lot, out var companion);

                var anbindung = "ohne Begleiter";
                if (companion != Entity.Null && EntityManager.Exists(companion))
                {
                    var strasse = EntityManager.HasComponent<Building>(companion)
                        ? EntityManager.GetComponentData<Building>(companion)
                            .m_RoadEdge
                        : Entity.Null;
                    anbindung = strasse != Entity.Null
                                && EntityManager.Exists(strasse)
                        ? "Begleiter " + companion.Index + " an Strasse " + strasse.Index
                            + (HatGehweg(strasse) ? " mit Gehweg" : " OHNE Gehweg")
                        : "Begleiter " + companion.Index + " OHNE Strasse";
                    anbindung += ", Personal " + Personal(companion);
                }

                zeilen.Add("Lot " + lot.Index + " '" + LotName(lot) + "': "
                    + "ParkingFacility " + (anlage ? "JA" : "NEIN")
                    + ", " + anbindung);
            }
            if (zeilen.Count == 0) return;

            // Feste Reihenfolge: die Abfrage liefert sie wechselnd, und jeder
            // Wechsel schrieb dieselbe Liste noch einmal ins Log.
            zeilen.Sort(System.StringComparer.Ordinal);
            var text = string.Join(" | ", zeilen);
            if (text == _letzterSteckbrief) return;
            _letzterSteckbrief = text;
            Mod.log.Info("PLT-Parkplatz-Steckbrief: " + text
                + ". Fehlt ParkingFacility, ist die Flaeche vor dem "
                + "2026-08-25 gebaut worden und muss neu gebaut werden; "
                + "Belegungsbalken und Parkgebuehr haengen daran.");
        }

        /**
         * Der Name, den der Nutzer im Infofenster liest.
         *
         * Er traegt die Stellplatzzahl - "Parking Lot (134 spaces)" - und
         * ist damit das Einzige, woran sich ein Parkplatz im Log und auf
         * dem Bildschirm derselbe ist. Ein erster Versuch hat stattdessen
         * die Buchten unter der Flaeche gezaehlt und kam ueberall auf 0:
         * die Spuren haengen am nackten Traeger, nicht an der Flaeche.
         */
        private string LotName(Entity lot)
        {
            try
            {
                var nameSystem = World
                    .GetOrCreateSystemManaged<Game.UI.NameSystem>();
                if (nameSystem.TryGetCustomName(lot, out var name)
                    && !string.IsNullOrEmpty(name))
                    return name;
            }
            catch (System.Exception) { }
            return "ohne Namen";
        }

        private Dictionary<Entity, Entity> CollectCompanionsByLot()
        {
            var result = new Dictionary<Entity, Entity>();
            using var companions = _companions.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < companions.Length; i++)
            {
                var companion = companions[i];
                var relation = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(companion);
                if (relation.Lot == Entity.Null
                    || !EntityManager.Exists(relation.Lot))
                    continue;
                if (!result.ContainsKey(relation.Lot))
                    result.Add(relation.Lot, companion);
            }
            return result;
        }

        private void EnableLot(Entity lot, Entity companion, Entity prefab)
        {
            var carrier = EntityManager
                .GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (carrier == Entity.Null || !EntityManager.Exists(carrier))
            {
                Mod.log.Warn("PLT-Begleiter nicht angelegt: Lot " + lot.Index
                    + " hat keinen gueltigen nackten Traeger.");
                return;
            }

            var created = companion == Entity.Null
                || !EntityManager.Exists(companion);
            if (created)
            {
                var objectData = EntityManager.GetComponentData<ObjectData>(prefab);
                companion = EntityManager.CreateEntity(objectData.m_Archetype);
                ParkingLotSchrittmarke.Aenderung("Begleiter: Lot " + lot.Index + " bekommt Begleiter " + companion.Index);
            }
            else if (!EntityManager.HasComponent<Game.Objects.Object>(companion))
            {
                /*
                 * Ein gespeichertes Skelett darf nie per Updated in das neue
                 * Rezept kippen. Genau dieser Zwitter ist am 2026-08-26 nativ
                 * abgestuerzt. Der bestehende Parkplatz bleibt benutzbar, aber
                 * fuer den Vollarchetyp muss er neu gebaut werden.
                 */
                if (_incompleteCompanionsLogged.Add(companion))
                    Mod.log.Warn("PLT-Begleiter " + companion.Index + " fuer Lot "
                        + lot.Index + " stammt aus dem alten Einzelkomponenten-"
                        + "Weg. Er erhaelt absichtlich KEIN Updated; fuer Strasse, "
                        + "Strom und Angestellte diesen Parkplatz neu bauen.");
                return;
            }

            // Das Prefabrezept enthaelt bereits den gesamten Vanilla-Vertrag.
            // Hier werden nur Werte gesetzt, Hidden und PLTs zwei Relationen
            // ergaenzt. Hidden muss auch nach dem Laden wieder gesetzt werden:
            // der leere Werkzeugmarker ist nicht serialisiert.
            if (!EntityManager.HasComponent<Hidden>(companion))
            {
                EntityManager.AddComponent<Hidden>(companion);
                // Bei einer geladenen Entity kann der Renderbatch schon
                // bestehen. Dann muss CS2 den neuen Hidden-Zustand einlesen.
                if (!EntityManager.HasComponent<BatchesUpdated>(companion))
                    EntityManager.AddComponent<BatchesUpdated>(companion);
            }
            SetOrAdd(companion, new PrefabRef { m_Prefab = prefab });
            SetOrAdd(companion, new ParkingLotPartRelation
            {
                Lot = lot,
                Carrier = carrier,
            });
            SetOrAdd(companion,
                new Game.Objects.Attached(carrier, Entity.Null, 0f));
            if (!EntityManager.HasComponent<ParkingLotBuildingEconomyEnabled>(
                    companion))
            {
                ParkingLotSchrittmarke.Aenderung("Begleiter: Begleiter " + companion.Index + " fuer Lot " + lot.Index + " eingeschaltet");
                EntityManager.AddComponent<ParkingLotBuildingEconomyEnabled>(
                    companion);
            }

            var economy = EntityManager.GetComponentData<ParkingLotEconomyData>(lot);
            if (created)
            {
                // Erst die fertige Strassenkante verwenden; fehlt sie noch,
                // wird genau dieser Neubau begrenzt nachgefuehrt. Geladene
                // Begleiter bleiben bis zum ausdruecklichen Sync unveraendert.
                var anschluss = Normalanschluss(lot);
                MarkiereAnschlussOffen(companion);
                var lage = new Game.Objects.Transform(AreaCenter(lot), quaternion.identity);
                var platziert = Strassenlage(lot, prefab, out var strassenlage);
                if (platziert) lage = strassenlage;
                else
                {
                    var hoehe = _terrain.GetHeightData();
                    lage.m_Position.y = Game.Simulation.TerrainUtils.SampleHeight(ref hoehe, lage.m_Position);
                }
                EntityManager.SetSharedComponent(companion,
                    new UpdateFrame((uint)companion.Index & 0xFu));
                EntityManager.SetComponentData(companion, lage);
                EntityManager.SetComponentData(companion, new CurrentDistrict(Entity.Null));
                EntityManager.SetComponentData(companion,
                    new PseudoRandomSeed((ushort)math.clamp(companion.Index, 1, ushort.MaxValue)));
                if (anschluss != null)
                {
                    anschluss.Platziert = platziert;
                    anschluss.Versuche = platziert ? 1 : 0;
                    anschluss.NaechstePlatzierung = UnityEngine.Time.frameCount + 120;
                }
            }
            /*
             * KEINE KOPIE DES SubNet MEHR.
             *
             * Der Begleiter trug bis zum 2026-08-26 eine Kopie des
             * SubNet-Puffers der Flaeche - im Nutzerbau 81 Netzteile. Damit
             * verweisen ZWEI Besitzer auf dieselben Wege: die Flaeche samt
             * Traeger und der Begleiter.
             *
             * Mehrere Vanilla-Systeme laufen ueber das SubNet eines Gebaeudes,
             * und die Richtlinien-Verarbeitung sucht genau so nach Fahrspuren.
             * Ein Gebaeude, das fremde Wege als seine eigenen ausgibt, ist die
             * letzte strukturelle Auffaelligkeit, die nach vier Abstuerzen
             * uebrig war.
             *
             * Gebraucht wurde die Kopie nur fuer den Strom: dessen Abfrage
             * verlangte `ElectricityConsumer` UND `SubNet` am selben Gebaeude.
             * Seit der Strom die Strasse gar nicht mehr anfasst, braucht sie
             * niemand mehr.
             */

            var baseNoise = EntityManager.GetComponentData<PollutionData>(prefab)
                .m_NoisePollution;
            EntityManager.SetComponentData(companion, new PollutionEmitModifier
            {
                // Der Modifikator ist relativ zum echten Vanilla-Prefabwert.
                m_NoisePollutionModifier = NoiseFor(economy.Capacity)
                    / baseNoise - 1f,
            });

            PruefeStrassenanbindung(companion, lot);

            if (_announcedCompanions.Add(companion))
            {
                Mod.log.Info("PLT-Begleiter " + companion.Index + " fuer Lot "
                    + lot.Index + (created ? " angelegt" : " wiedergefunden")
                    + ": Attached an Traeger " + carrier.Index + ", Prefab "
                    + CompanionPrefabName
                    + ", ohne eigenes SubNet"
                    + ", Laerm " + NoiseFor(economy.Capacity)
                    + ", tatsaechliche Lage "
                    + (EntityManager.HasComponent<Game.Objects.Transform>(companion)
                        ? EntityManager.GetComponentData<Game.Objects.Transform>(companion).m_Position.ToString()
                        : "Transform fehlt")
                    + "; vollstaendig aus ObjectData.m_Archetype, Created="
                    + EntityManager.HasComponent<Created>(companion)
                    + ", Updated="
                    + EntityManager.HasComponent<Updated>(companion)
                    + ", Hidden="
                    + EntityManager.HasComponent<Hidden>(companion)
                    + ", Marker-Objekt ohne SubMesh und mit beidseitiger "
                    + "Terraform-Sperre.");
            }
        }

        private bool TryResolveCompanionPrefab(out Entity prefab)
        {
            if (_companionPrefab != Entity.Null
                && EntityManager.Exists(_companionPrefab))
            {
                ConfigureCompanionPrefab(_companionPrefab);
                if (!CompanionPrefabReady(_companionPrefab))
                {
                    prefab = Entity.Null;
                    return false;
                }
                prefab = _companionPrefab;
                return true;
            }

            using var prefabs = _buildingPrefabs.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < prefabs.Length; i++)
            {
                var candidate = prefabs[i];
                if (!_prefabSystem.TryGetPrefab<BuildingPrefab>(candidate,
                        out var value) || value == null)
                    continue;
                if (value.name == CompanionPrefabName)
                {
                    _companionPrefab = candidate;
                    ConfigureCompanionPrefab(candidate);
                    if (!CompanionPrefabReady(candidate))
                    {
                        prefab = Entity.Null;
                        return false;
                    }
                    prefab = candidate;
                    return true;
                }
            }

            prefab = Entity.Null;
            if (!_prefabFailureLogged)
            {
                _prefabFailureLogged = true;
                Mod.log.Error("PLT-Begleiter bleibt AUS: das typkorrekte "
                    + "Vanilla-Rezept '" + CompanionSourcePrefabName
                    + "' wurde nicht gefunden. Der Lot-Flaeche werden ersatzweise "
                    + "weder Building noch Transform angeheftet.");
            }
            return false;
        }

        private bool CompanionPrefabReady(Entity prefab)
            => _companionPrefabSafe
               && EntityManager.HasComponent<ObjectData>(prefab)
               && EntityManager.GetComponentData<ObjectData>(prefab)
                   .m_Archetype.Valid
               && EntityManager.HasComponent<BuildingData>(prefab)
               && EntityManager.HasComponent<PollutionData>(prefab)
               && EntityManager.HasComponent<DestructibleObjectData>(prefab)
               && EntityManager.HasComponent<ObjectGeometryData>(prefab)
               && EntityManager.HasComponent<ConsumptionData>(prefab)
               && EntityManager.HasComponent<WorkplaceData>(prefab)
               && TerraformGesperrt(prefab);

        /**
         * Sagt EINMAL je Begleiter, ob CS2 ihn an eine Strasse angebunden hat.
         *
         * `RoadConnectionSystem` laeuft nicht im selben Frame wie unser Bau,
         * deshalb wird ein paar hundert Durchgaenge lang gewartet, bevor ein
         * Fehlen gemeldet wird. Sonst stuende bei jedem Bau erst "keine
         * Strasse" und Sekunden spaeter das Gegenteil.
         */
        private void PruefeStrassenanbindung(Entity companion, Entity lot)
        {
            if (_strassenGemeldet.Contains(companion)) return;

            var strasse = EntityManager.HasComponent<Building>(companion)
                ? EntityManager.GetComponentData<Building>(companion).m_RoadEdge
                : Entity.Null;

            if (strasse != Entity.Null && EntityManager.Exists(strasse))
            {
                var eingetragen = EntityManager
                        .HasBuffer<ConnectedBuilding>(strasse)
                    && Enthaelt(EntityManager.GetBuffer<ConnectedBuilding>(
                        strasse, true), companion);
                _strassenGemeldet.Add(companion);
                _strassenWartet.Remove(companion);
                if (eingetragen) SchliesseAnschlussOffen(companion);
                Mod.log.Info("PLT-Begleiter " + companion.Index + " fuer Lot "
                    + lot.Index + ": Strassenanbindung STEHT, Kante "
                    + strasse.Index + ", im ConnectedBuilding-Puffer der "
                    + "Strasse: " + (eingetragen ? "ja" : "NEIN") + ".");
                return;
            }

            _strassenWartet.TryGetValue(companion, out var versuche);
            versuche++;
            _strassenWartet[companion] = versuche;
            // 180 Durchgaenge, rund drei Sekunden. Vorher standen 600 hier,
            // also gut zehn - der Nutzer hatte das Spiel nach acht Sekunden
            // geschlossen und bekam gar kein Urteil. Ein Messwerkzeug, das
            // laenger braucht als die Geduld, misst nichts.
            if (versuche < 180) return;

            _strassenGemeldet.Add(companion);
            _strassenWartet.Remove(companion);
            Mod.log.Warn("PLT-Begleiter " + companion.Index + " fuer Lot "
                + lot.Index + ": KEINE Strassenanbindung nach " + versuche
                + " Durchgaengen. Damit koennen Buerger nicht zur Arbeit "
                + "laufen (daher 'Mangel an Arbeitskraeften') und Strom kann "
                + "nicht fliessen. Steht der Parkplatz an einer Strasse?");
        }

        private static bool Enthaelt(
            DynamicBuffer<ConnectedBuilding> puffer, Entity gesucht)
        {
            for (var i = 0; i < puffer.Length; i++)
                if (puffer[i].m_Building == gesucht) return true;
            return false;
        }

        private void DisableAll()
        {
            if (_companions.IsEmptyIgnoreFilter) return;
            using var companions = _companions.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < companions.Length; i++) DisableLot(companions[i]);
        }

        /**
         * Entgiftet ausschliesslich Spielstaende aus dem verworfenen Weg B.
         *
         * Dessen serialisierter Marker sitzt auf der Area und hat keine
         * Teilrelation. Diese eindeutige Kombination kann kein neuer Begleiter
         * besitzen. Der Rueckbau stellt die harte Projektgrenze wieder her,
         * bevor irgendein neuer Begleiter erzeugt wird.
         */
        private void DisableLegacyLots()
        {
            if (_legacyLots.IsEmptyIgnoreFilter) return;
            using var lots = _legacyLots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                if (EntityManager.HasBuffer<Employee>(lot)
                    && EntityManager.GetBuffer<Employee>(lot).Length != 0)
                {
                    if (EntityManager.HasComponent<WorkProvider>(lot))
                    {
                        var provider = EntityManager
                            .GetComponentData<WorkProvider>(lot);
                        provider.m_MaxWorkers = 0;
                        EntityManager.SetComponentData(lot, provider);
                    }
                    continue;
                }

                ParkingLotSchrittmarke.Aenderung("Begleiter: Altparkplatz " + lot.Index + " abgeschaltet");
                DisconnectElectricity(lot);
                MarkParkingLanesUpdated(lot);
                RemoveIfPresent<Building>(lot);
                RemoveIfPresent<ElectricityConsumer>(lot);
                RemoveIfPresent<WorkProvider>(lot);
                RemoveIfPresent<FreeWorkplaces>(lot);
                RemoveIfPresent<PollutionEmitModifier>(lot);
                RemoveIfPresent<Game.Objects.Transform>(lot);
                RemoveIfPresent<CurrentDistrict>(lot);
                RemoveIfPresent<CitizenPresence>(lot);
                RemoveIfPresent<Game.Buildings.Lot>(lot);
                RemoveIfPresent<ParkingLotBuildingEconomyEnabled>(lot);
                RemoveBufferIfPresent<Game.Net.SubLane>(lot);
                RemoveBufferIfPresent<Policy>(lot);
                RemoveBufferIfPresent<BuildingModifier>(lot);
                RemoveBufferIfPresent<Employee>(lot);

                if (EntityManager.HasComponent<PrefabRef>(lot))
                    RemoveLegacyPrefabData(EntityManager
                        .GetComponentData<PrefabRef>(lot).m_Prefab);
                Mod.log.Warn("PLT-Altlast entfernt: Building und Transform des "
                    + "verworfenen Flaechen-Anlaufs wurden von Lot " + lot.Index
                    + " abgebaut. Die Area ist wieder eine reine Flaeche.");
            }
        }

        private void RemoveLegacyPrefabData(Entity prefab)
        {
            if (!IsOwnLotPrefab(prefab)) return;
            RemoveIfPresent<BuildingData>(prefab);
            RemoveIfPresent<PollutionData>(prefab);
            RemoveIfPresent<DestructibleObjectData>(prefab);
            RemoveIfPresent<ObjectGeometryData>(prefab);
            RemoveIfPresent<ConsumptionData>(prefab);
            RemoveIfPresent<WorkplaceData>(prefab);
            RemoveBufferIfPresent<DefaultPolicyData>(prefab);
        }

        private void DisableLot(Entity companion)
        {
            if (EntityManager.HasBuffer<Employee>(companion)
                && EntityManager.GetBuffer<Employee>(companion).Length != 0)
            {
                if (EntityManager.HasComponent<WorkProvider>(companion))
                {
                    var provider = EntityManager
                        .GetComponentData<WorkProvider>(companion);
                    provider.m_MaxWorkers = 0;
                    EntityManager.SetComponentData(companion, provider);
                }
                return;
            }

            ParkingLotSchrittmarke.Aenderung("Begleiter: Begleiter " + companion.Index + " wird abgebaut");
            DisconnectElectricity(companion);
            if (EntityManager.HasComponent<ParkingLotPartRelation>(companion))
            {
                var lot = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(companion).Lot;
                MarkParkingLanesUpdated(lot);
            }
            // Nicht in einen nackten Rest zerlegen: `Deleted` laesst Vanilla
            // den bis zuletzt vollstaendigen Building-Vertrag geordnet abbauen.
            EntityManager.AddComponent<Deleted>(companion);
            _announcedCompanions.Remove(companion);
            Mod.log.Info("PLT-Begleiter " + companion.Index
                + " vollstaendig zum Rueckbau vorgemerkt.");
        }

        private void MarkParkingLanesUpdated(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasBuffer<Game.Net.SubNet>(lot)) return;
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(lot, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var net = subNets[i].m_SubNet;
                if (!EntityManager.Exists(net)
                    || !EntityManager.HasBuffer<Game.Net.SubLane>(net))
                    continue;
                var subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(net,
                    true);
                for (var j = 0; j < subLanes.Length; j++)
                {
                    var lane = subLanes[j].m_SubLane;
                    if (!EntityManager.Exists(lane)
                        || !EntityManager.HasComponent<Game.Net.ParkingLane>(lane)
                        || EntityManager.HasComponent<PathfindUpdated>(lane))
                        continue;
                    EntityManager.AddComponent<PathfindUpdated>(lane);
                }
            }
        }

        private void DisconnectElectricity(Entity companion)
        {
            if (!EntityManager.HasComponent<Building>(companion)) return;
            var road = EntityManager.GetComponentData<Building>(companion)
                .m_RoadEdge;
            if (road == Entity.Null || !EntityManager.Exists(road)) return;
            if (EntityManager.HasBuffer<ConnectedBuilding>(road))
            {
                var connected = EntityManager.GetBuffer<ConnectedBuilding>(road);
                for (var i = connected.Length - 1; i >= 0; i--)
                    if (connected[i].m_Building == companion)
                        connected.RemoveAt(i);
            }
            var queue = _electricityRoadGraph.GetEdgeUpdateQueue(out var deps);
            deps.Complete();
            queue.Enqueue(road);
        }

        private bool IsOwnLotPrefab(Entity prefab)
            => prefab != Entity.Null && EntityManager.Exists(prefab)
               && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var value)
               && value != null
               && value.name == ParkingLotToolSystem.LotOwnerPrefabName;

        private float3 AreaCenter(Entity lot)
        {
            var nodes = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
            if (nodes.Length == 0) return float3.zero;
            var count = nodes.Length;
            if (count > 1 && math.distancesq(nodes[0].m_Position,
                    nodes[count - 1].m_Position) < 1e-8f)
                count--;
            var sum = float3.zero;
            for (var i = 0; i < count; i++) sum += nodes[i].m_Position;
            return count == 0 ? float3.zero : sum / count;
        }

        internal static int NoiseFor(int capacity)
            => capacity <= 15 ? 1000 : capacity < 130 ? 2500 : 5000;

        private void SetOrAdd<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(entity))
                EntityManager.SetComponentData(entity, value);
            else
                EntityManager.AddComponentData(entity, value);
        }

        private static bool ArchetypeEnthaelt(EntityArchetype archetype,
            ComponentType gesucht)
        {
            var komponenten = archetype.GetComponentTypes(Allocator.Temp);
            try
            {
                for (var i = 0; i < komponenten.Length; i++)
                    if (komponenten[i].TypeIndex == gesucht.TypeIndex)
                        return true;
                return false;
            }
            finally
            {
                komponenten.Dispose();
            }
        }

        private void RemoveIfPresent<T>(Entity entity)
            where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(entity))
                EntityManager.RemoveComponent<T>(entity);
        }

        private void RemoveBufferIfPresent<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (EntityManager.HasBuffer<T>(entity))
                EntityManager.RemoveComponent<T>(entity);
        }
    }
}
