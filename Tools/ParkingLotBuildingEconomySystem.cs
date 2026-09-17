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
                DisableAll();
                return;
            }
            if (!TryResolveCompanionPrefab(out var prefab)) return;

            var byLot = CollectCompanionsByLot();
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
                EntityManager.AddComponent<ParkingLotBuildingEconomyEnabled>(
                    companion);

            var economy = EntityManager.GetComponentData<ParkingLotEconomyData>(lot);
            /*
             * DER BEGLEITER STEHT AN DER ZUFAHRT, NICHT IN DER MITTE.
             *
             * GEMESSEN AM 2026-08-26, zwei Begleiter des Nutzers:
             *
             *     KEINE Strassenanbindung nach 600 Durchgaengen
             *
             * Der Grund liegt auf der Hand, sobald man ihn sieht: der
             * Begleiter ist ein 1x1-Gebaeude, und `RoadConnectionSystem` sucht
             * die Strasse im Umkreis seiner eigenen Grundstuecksgroesse
             * (`CalculateFrontPosition(transform, buildingData.m_LotSize.y)`,
             * Zeile 77). In der Mitte eines grossen Parkplatzes ist die
             * naechste Strasse 50 m weit weg - da findet er nichts.
             *
             * Ohne Anbindung koennen Buerger nicht zur Arbeit laufen ("Mangel
             * an Arbeitskraeften") und Strom kann nicht fliessen. Beide
             * Befunde des Tages hatten dieselbe Wurzel.
             *
             * Also an die Zufahrt. Der Laerm wandert damit vom Mittelpunkt an
             * den Rand - sachlich sogar naeher an der Wahrheit, denn dort
             * fahren die Autos.
             */
            var strassenpunkt = ZufahrtNaheStrasse(lot);
            var knotenmittel = AreaCenter(lot);
            var center = strassenpunkt ?? knotenmittel;
            /*
             * DIE HOEHE KOMMT VOM GELAENDE, NICHT VON DEN KNOTEN.
             *
             * Beide Quellen oben liefern eine gute XZ-Lage, aber ein Y aus
             * Netz- bzw. Flaechenknoten. Am Hang liegt das weit daneben:
             * gemessen am 2026-08-31 Y=515,09 m aus dem Randknotenmittel,
             * waehrend das Gelaende dort ueber 519 m lag.
             *
             * Das ist teuer, denn der Begleiter ist fuer CS2 ein Gebaeude:
             * sein Lot zieht das Gelaende auf seine eigene Hoehe. Im Log
             * stand deshalb ein Loch von bis zu -4,25 m an genau seiner
             * Stelle - das kurz sichtbare Loch, das der Nutzer beim Bauen auf
             * dem Huegel gesehen hat. Es verschwindet erst, wenn CS2 den
             * Begleiter selbst nachfuehrt.
             *
             * `TerrainUtils.SampleHeight` ist dieselbe Quelle, aus der auch
             * `GroundHeightSystem` seinen Zielwert nimmt. Damit stimmt die
             * Hoehe von der ersten Sekunde an, und das Lot planiert auf den
             * Wert, der ohnehin schon dort steht.
             */
            var hoehendaten = _terrain.GetHeightData();
            center.y = Game.Simulation.TerrainUtils.SampleHeight(
                ref hoehendaten, center);

            if (created)
            {
                EntityManager.SetSharedComponent(companion,
                    new UpdateFrame((uint)companion.Index & 0xFu));
                EntityManager.SetComponentData(companion,
                    new Game.Objects.Transform(center, quaternion.identity));
                EntityManager.SetComponentData(companion,
                    new CurrentDistrict(Entity.Null));
                EntityManager.SetComponentData(companion,
                    new PseudoRandomSeed((ushort)math.clamp(companion.Index, 1,
                        ushort.MaxValue)));
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
                    + ", gesetzte Hoehenquelle "
                    + (strassenpunkt.HasValue
                        ? "Zufahrtsknoten"
                        : "arithmetisches Lot-Randknotenmittel")
                    + " Y=" + center.y.ToString("F4")
                    + " m (Randknotenmittel Y="
                    + knotenmittel.y.ToString("F4") + " m)"
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

        /**
         * DAS URTEIL DARF NICHT EINMALIG SEIN.
         *
         * Bis zum 2026-08-27 stand hier ein `if (_companionPrefabConfigured)
         * return;` VOR allem, und ganz am Ende wurde das Flag gesetzt. Damit
         * fiel auch die Tauglichkeitspruefung genau einmal - und zwar 0,7
         * Sekunden nach dem Anmelden des Prefabs, als CS2 den Archetyp noch
         * gar nicht gebaut hatte. Aus dem Log des Nutzers:
         *
         *     PLT-Begleiter bleibt AUS: ... Archetyp=False, Vertrag=False,
         *     Flags=RestrictedPedestrian,... Strom=0
         *
         * Danach wurde nie wieder geprueft. Der Begleiter ist deshalb in der
         * ganzen Sitzung NIE entstanden - kein Laerm, keine Angestellten, und
         * folglich auch keine Angestellten-Zeile im Fenster. Es sah nach einem
         * Fehler in der Oberflaeche aus und war keiner.
         *
         * Getrennt wird jetzt: die Aenderungen am Prefab passieren einmal,
         * das Urteil bei jedem Durchgang neu, bis es taugt.
         */
        private void ConfigureCompanionPrefab(Entity prefab)
        {
            if (!_companionPrefabConfigured) MutierePrefab(prefab);
            NeutralisiereFlachzone(prefab);
            BeurteilePrefab(prefab);
        }

        /**
         * DIE SPERRE DECKT DIE FLACHZONE NICHT AB.
         *
         * `BuildingUtils.CalculateLotInfo` uebernimmt aus
         * `BuildingTerraformData` fuenf Groessen. `m_DontRaise` und
         * `m_DontLower` ueberschreiben davon nur `m_MinLimit`/`m_MaxLimit`,
         * also die Glaettung am Lotrand - und zwar mit einem umgedrehten
         * Rechteck `(8,8,-8,-8)`, das leer ist. Die Flachzone
         * `m_FlatX0..m_FlatZ1` reicht die Funktion unveraendert durch.
         *
         * Diese Zone entsteht in `BuildingInitializeSystem.InitializeTerraformData`
         * aus den Objektmassen des REZEPTS, das wir klonen - nicht aus unseren
         * eigenen. Sie planiert den Lotkern auf die Begleiterhoehe. Auf ebenem
         * Grund faellt das nicht auf; an Hang und Senke schon, und genau dort
         * meldete die Messung `TERRAFORM-SPERRE VERLETZT`.
         *
         * Null ist keine geratene Konstante: `InitializeTerraformData` liefert
         * bei einer Flaeche unter 2 m selbst den Mittelpunkt, und fuer ein
         * symmetrisches Lot ist der 0. Ein Markerpunkt ohne Mesh hat keine
         * Flaeche, die planiert werden will.
         *
         * Laeuft bei JEDEM Durchgang, nicht einmal: baut CS2 die Komponente
         * bei einem spaeteren `Updated` neu auf, wird sie wieder eingezogen.
         */
        private void NeutralisiereFlachzone(Entity prefab)
        {
            if (!EntityManager.HasComponent<BuildingTerraformData>(prefab))
                return;
            var terraform = EntityManager
                .GetComponentData<BuildingTerraformData>(prefab);
            if (FlachzoneNeutral(terraform)) return;

            if (!_flachzoneGemeldet)
            {
                _flachzoneGemeldet = true;
                Mod.log.Info("PLT-Begleiter: Flachzone aus dem Rezept war X "
                    + terraform.m_FlatX0.y.ToString("F3") + ".."
                    + terraform.m_FlatX1.y.ToString("F3") + " m, Z "
                    + terraform.m_FlatZ0.y.ToString("F3") + ".."
                    + terraform.m_FlatZ1.y.ToString("F3") + " m (Randwerte X "
                    + terraform.m_FlatX0.x.ToString("F3") + "/"
                    + terraform.m_FlatX0.z.ToString("F3") + ".."
                    + terraform.m_FlatX1.x.ToString("F3") + "/"
                    + terraform.m_FlatX1.z.ToString("F3") + "). Sie wird auf "
                    + "den Mittelpunkt zusammengezogen; die beiden Sperren "
                    + "erreichen sie nicht.");
            }

            terraform.m_FlatX0 = float3.zero;
            terraform.m_FlatZ0 = float3.zero;
            terraform.m_FlatX1 = float3.zero;
            terraform.m_FlatZ1 = float3.zero;
            EntityManager.SetComponentData(prefab, terraform);
        }

        private static bool FlachzoneNeutral(BuildingTerraformData terraform)
            => math.all(terraform.m_FlatX0 == 0f)
               && math.all(terraform.m_FlatZ0 == 0f)
               && math.all(terraform.m_FlatX1 == 0f)
               && math.all(terraform.m_FlatZ1 == 0f);

        /**
         * DEN ARCHETYP NACHHOLEN - `AddPrefab` BAUT IHN NICHT.
         *
         * Am 2026-08-27 stand im Log auch nach 600 Durchgaengen
         * `Archetyp=False`. Der Grund steht im Dekompilat:
         *
         *     ObjectPrefab.RefreshArchetype(...)     setzt m_Archetype
         *     ObjectPrefab.LateInitialize(...)       ruft RefreshArchetype
         *
         * Und `LateInitialize` ruft im Spiel NUR `PrefabInitializeSystem`,
         * gleich hinter seinem eigenen `AddPrefab`:
         *
         *     if (m_PrefabSystem.AddPrefab(...)) { InitializePrefab(...); }
         *     foreach (...) LateInitializePrefab(...);
         *
         * Wer wie wir `AddPrefab` selbst aufruft, bekommt die Entity mit allen
         * Prefabkomponenten - aber ohne Archetyp. Ein Prefab ohne Archetyp
         * kann nie eine Instanz erzeugen, und genau daran ist der Begleiter
         * seit dem Umbau auf das Vollrezept gescheitert.
         *
         * NICHT IM SELBEN FRAME WIE `AddPrefab`. Das steht hier nicht aus
         * Vorsicht, sondern aus Erfahrung: Anmelden und Benutzen im selben
         * Frame endet in einem nativen Absturz ohne Stapel. Dieser Aufruf
         * sitzt deshalb in `BeurteilePrefab`, also fruehestens einen Frame
         * spaeter, in einer anderen Phase.
         */
        private void HoleArchetypNach(Entity prefab)
        {
            if (_archetypNachgeholt) return;
            if (EntityManager.HasComponent<ObjectData>(prefab)
                && EntityManager.GetComponentData<ObjectData>(prefab)
                    .m_Archetype.Valid)
                return;
            if (!_prefabSystem.TryGetPrefab<BuildingPrefab>(prefab,
                    out var rezept) || rezept == null)
                return;

            _archetypNachgeholt = true;
            try
            {
                rezept.LateInitialize(EntityManager, prefab);
                var gueltig = EntityManager.HasComponent<ObjectData>(prefab)
                    && EntityManager.GetComponentData<ObjectData>(prefab)
                        .m_Archetype.Valid;
                Mod.log.Info("PLT-Begleiter: Archetyp nachgeholt ueber "
                    + "LateInitialize - jetzt gueltig: " + gueltig + ".");
            }
            catch (System.Exception fehler)
            {
                Mod.log.Error("PLT-Begleiter: LateInitialize ist "
                    + "fehlgeschlagen, der Begleiter bleibt aus. Ursache: "
                    + fehler);
            }
        }

        private void MutierePrefab(Entity prefab)
        {
            _companionPrefabConfigured = true;

            /*
             * KEIN `NoRoadConnection` MEHR - CS2 soll die Strasse selbst
             * finden und pflegen.
             *
             * Mit dem Flag steigt `RoadConnectionSystem` sofort aus (Zeile 73).
             * Damit faellt genau das System weg, das fuer ein Gebaeude
             *   - die zustaendige Strasse sucht,
             *   - `Building.m_RoadEdge` setzt,
             *   - den `ConnectedBuilding`-Eintrag anlegt UND wieder entfernt
             *     (die einzige Entfernstelle im ganzen Spiel, Zeile 628).
             *
             * Wir hatten das von Hand nachgebaut, und beide Anlaeufe sind im
             * Spiel gescheitert: erst eine INTERNE Fahrgasse als Strasse (kein
             * Strom, Warnsymbol bleibt), dann eine echte Stadtstrasse, in
             * deren Puffer wir selbst geschrieben haben (Absturz schon beim
             * Bauen). Eine fremde Datenstruktur pflegen, die man nicht besitzt,
             * geht nicht gut aus.
             *
             * Das Lot ist 1x1 statt 0x0, damit die Vanilla-Suche eine
             * Vorderseite hat, an der sie ansetzen kann.
             */
            var buildingData = EntityManager.GetComponentData<BuildingData>(prefab);
            buildingData.m_LotSize = new int2(1, 1);
            buildingData.m_Flags &= ~Game.Prefabs.BuildingFlags.NoRoadConnection;
            EntityManager.SetComponentData(prefab, buildingData);
            SetOrAdd(prefab, new PollutionData
            {
                m_NoisePollution = 5000f,
                m_ScaleWithRenters = false,
            });
            SetOrAdd(prefab, new DestructibleObjectData
            {
                // Der unsichtbare Simulationspunkt darf kein Brandziel werden.
                m_FireHazard = 0f,
                m_StructuralIntegrity = float.MaxValue,
            });
            SetOrAdd(prefab, new ObjectGeometryData
            {
                // Der Vollarchetyp enthaelt zwingend `Object`. Marker wird vom
                // normalen Raycast vor dem Mesh-Test verworfen. Unsichtbar
                // macht die Instanz `Game.Tools.Hidden`; der leere SubMesh-
                // Puffer ist die zweite, unabhaengige Schranke.
                m_Bounds = new Bounds3(new float3(-0.5f), new float3(0.5f)),
                m_Size = new float3(1f),
                m_Flags = Game.Objects.GeometryFlags.Marker,
                m_Layers = MeshLayer.Marker,
            });
            SetOrAdd(prefab, new WorkplaceData
            {
                m_MaxWorkers = 4,
                m_MinimumWorkersLimit = 0,
                m_WorkConditions = 0,
            });
            RemoveBufferIfPresent<AdditionalBuildingTerraformElement>(prefab);
        }

        private void BeurteilePrefab(Entity prefab)
        {
            if (_companionPrefabSafe) return;
            HoleArchetypNach(prefab);

            var buildingData = EntityManager.GetComponentData<BuildingData>(
                prefab);
            var objectData = EntityManager.GetComponentData<ObjectData>(prefab);
            var geometry = EntityManager.GetComponentData<ObjectGeometryData>(
                prefab);
            var subMeshes = EntityManager.HasBuffer<SubMesh>(prefab)
                ? EntityManager.GetBuffer<SubMesh>(prefab, true).Length
                : -1;
            var consumption = EntityManager.GetComponentData<ConsumptionData>(
                prefab);
            var archetype = objectData.m_Archetype;
            var archetypeVollstaendig = archetype.Valid
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Created>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Updated>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Game.Objects.Object>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Game.Objects.Transform>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Building>());
            var archetypeTerraform = archetype.Valid
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<BuildingTerraformData>());
            var terraformVorhanden = EntityManager
                .HasComponent<BuildingTerraformData>(prefab);
            var terraform = terraformVorhanden
                ? EntityManager.GetComponentData<BuildingTerraformData>(prefab)
                : default(BuildingTerraformData);
            var terraformGesperrt = terraformVorhanden
                && terraform.m_DontRaise && terraform.m_DontLower;
            /*
             * ZWEI BEDINGUNGEN SIND HIER ENTFERNT WORDEN, WEIL SIE VERALTET
             * WAREN.
             *
             *     RequireRoad             wurde nie gesetzt - die Zeile
             *                             darueber loescht nur
             *                             `NoRoadConnection`.
             *     Strom > 0               `MutierePrefab` setzt den Verbrauch
             *                             ausdruecklich auf 0.
             *
             * Beide stammen aus dem Stromweg, den der Nutzer am 2026-08-26
             * abgeblasen hat ("wir lassen es ohne strom wie jetzt"). Die
             * Schranke blieb stehen und hat danach den Begleiter dauerhaft
             * verhindert - eine Bedingung, die der eigene Code nicht mehr
             * erfuellen KANN, ist keine Sicherung, sondern ein Ausschalter.
             */
            _companionPrefabSafe = archetypeVollstaendig
                && (geometry.m_Flags & Game.Objects.GeometryFlags.Marker) != 0
                && geometry.m_Layers == MeshLayer.Marker
                && subMeshes == 0
                && !archetypeTerraform
                && terraformGesperrt
                && !EntityManager.HasBuffer<AdditionalBuildingTerraformElement>(
                    prefab);

            var noise = EntityManager.GetComponentData<PollutionData>(prefab)
                .m_NoisePollution;
            if (_companionPrefabSafe)
                Mod.log.Info("PLT-Begleiter-Prefab einsatzbereit nach "
                    + _beurteilungen + " Durchgaengen: 1x1, Strom "
                    + consumption.m_ElectricityConsumption + ", Laermbasis "
                    + noise + ", Rezept mit Created/Updated/Object/Transform/"
                    + "Building, Marker-Geometrie, SubMesh 0, Rezept-Terraform "
                    + archetypeTerraform + ", Prefab-Terraform DontRaise="
                    + terraform.m_DontRaise + "/DontLower="
                    + terraform.m_DontLower + ". Das PLT-Lot-Prefab bleibt "
                    + "unveraendert.");
            /*
             * Der Archetyp entsteht erst ein paar Frames nach dem Anmelden.
             * Solange still bleiben - sonst steht die Fehlermeldung im Log,
             * waehrend gleich darauf alles in Ordnung ist. Erst wenn es nach
             * reichlich Durchgaengen immer noch nicht taugt, ist es ein Fehler.
             */
            else if (++_beurteilungen == BeurteilungenBisMeldung)
                Mod.log.Error("PLT-Begleiter bleibt AUS: sein Prefabrezept ist "
                    + "nicht gleichzeitig vollstaendig und unsichtbar. Werte: "
                    + "Archetyp=" + archetype.Valid
                    + ", Vertrag=" + archetypeVollstaendig
                    + ", Flags=" + buildingData.m_Flags
                    + ", Strom=" + consumption.m_ElectricityConsumption
                    + ", GeometryFlags=" + geometry.m_Flags
                    + ", Layers=" + geometry.m_Layers
                    + ", SubMesh=" + subMeshes
                    + ", Rezept-Terraform=" + archetypeTerraform
                    + ", Terraform=" + terraformVorhanden
                    + ", DontRaise=" + terraform.m_DontRaise
                    + ", DontLower=" + terraform.m_DontLower
                    + " nach " + _beurteilungen + " Durchgaengen. Die "
                    + "Lot-Flaeche wird nicht angetastet.");
        }

        private bool TerraformGesperrt(Entity prefab)
        {
            if (!EntityManager.HasComponent<BuildingTerraformData>(prefab))
                return false;
            var terraform = EntityManager
                .GetComponentData<BuildingTerraformData>(prefab);
            return terraform.m_DontRaise && terraform.m_DontLower;
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

        /**
         * Der Knoten unserer Zufahrt, der an einer fremden Kante haengt.
         *
         * Unsere Zufahrt wird per `LocalConnect` mit einem Knoten der
         * Stadtstrasse verbunden (siehe `Tools/ParkingLotNetBuilder.cs`). An
         * genau diesem Knoten liegt eine Kante, die NICHT in unserem SubNet
         * steht - dort ist die Strasse, und dort gehoert der Begleiter hin.
         *
         * Liefert null, wenn der Parkplatz an keiner Strasse haengt. Dann
         * bleibt der Mittelpunkt, und die Warnung im Log sagt weiterhin, dass
         * Arbeit und Strom ausfallen - was dann auch stimmt.
         */
        private float3? ZufahrtNaheStrasse(Entity lot)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(lot)) return null;
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(lot, true);

            var eigene = new HashSet<Entity>();
            for (var i = 0; i < subNets.Length; i++)
                eigene.Add(subNets[i].m_SubNet);

            for (var i = 0; i < subNets.Length; i++)
            {
                var unsere = subNets[i].m_SubNet;
                if (!EntityManager.Exists(unsere)
                    || !EntityManager.HasComponent<Game.Net.Edge>(unsere))
                    continue;
                var kante = EntityManager
                    .GetComponentData<Game.Net.Edge>(unsere);

                foreach (var knoten in new[] { kante.m_Start, kante.m_End })
                {
                    if (knoten == Entity.Null || !EntityManager.Exists(knoten)
                        || !EntityManager.HasBuffer<Game.Net.ConnectedEdge>(knoten)
                        || !EntityManager.HasComponent<Game.Net.Node>(knoten))
                        continue;
                    var verbunden = EntityManager
                        .GetBuffer<Game.Net.ConnectedEdge>(knoten, true);
                    for (var k = 0; k < verbunden.Length; k++)
                    {
                        var fremd = verbunden[k].m_Edge;
                        if (fremd == Entity.Null || eigene.Contains(fremd)
                            || !EntityManager.Exists(fremd))
                            continue;
                        return EntityManager
                            .GetComponentData<Game.Net.Node>(knoten).m_Position;
                    }
                }
            }
            return null;
        }

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
