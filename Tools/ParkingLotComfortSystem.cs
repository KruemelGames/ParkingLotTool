using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * UNSER PARKPLATZ WIRBT NICHT UM AUTOS - dieses System holt das nach.
     *
     * Am Dekompilat belegt, `Game.Pathfind.ParkingLaneDataSystem.GetParkingStats`:
     *
     *     comfort = 0;
     *     Owner owner2 = owner;
     *     while (m_OwnerData.TryGetComponent(owner2.m_Owner, out next))
     *         owner2 = next;                      // hoch zum Wurzelbesitzer
     *     if (m_BuildingData.HasComponent(owner2.m_Owner))   // NUR Gebaeude
     *     {
     *         if (!flag) parkingFacilityData.m_ComfortFactor = 0.8f;
     *         if (m_ParkingFacilityData.TryGetComponent(...))
     *             parkingFacilityData.m_ComfortFactor = componentData9.m_ComfortFactor;
     *         comfort = (ushort)(m_ComfortFactor * 65535f);
     *     }
     *
     * Unser Wurzelbesitzer ist das eigene `LotPrefab`, also eine AREA und kein
     * Gebaeude. Die Abfrage schlaegt fehl, `m_ComfortFactor` bleibt 0. Ein
     * Vanilla-Parkplatz bekommt seinen `ParkingFacilityData.m_ComfortFactor`,
     * und selbst ein beliebiges Wohnhaus mit Stellplaetzen bekommt 0,8 - weil
     * es diesen Zweig ueberhaupt betritt.
     *
     * WARUM NICHT ALS GEBAEUDE: genau das ist am 2026-07-26 und am 27./28.
     * zweimal gescheitert - Loch im Gelaende an der Einfahrt, Absturz beim
     * Editieren, Absturz beim Bulldozern nach dem dritten Loeschbatch. Siehe
     * die Notiz `streetblock-building-controller`. Hier wird deshalb nur EIN
     * Zahlenfeld auf unseren eigenen Spuren nachgezogen: keine neue Komponente,
     * kein neuer Besitzer, keine Vanilla-Entity angefasst. Schlimmster Fall,
     * wenn es nicht wirkt: es bleibt beim alten Verhalten.
     *
     * ZEITPUNKT ist Pflicht, nicht Geschmack. `ParkingLaneDataSystem` laeuft in
     * `ModificationEnd` (Game.Common.SystemOrder Zeile 251) und rechnet den
     * Wert bei jedem `Updated` oder `PathfindUpdated` neu. Wer davor schreibt,
     * wird ueberschrieben. Direkt danach - und noch vor `LanesModifiedSystem`,
     * das in derselben Phase folgt - ist der Wert gesetzt, bevor ihn jemand
     * liest.
     *
     * NICHT ANGEFASST werden `ParkingLaneFlags.AllowEnter` und `AllowExit`.
     * Die setzt derselbe Gebaeude-Zweig, unsere Spuren haben sie also auch
     * nicht. Wer sie liest, ist noch nicht gefunden - die zwei Treffer in
     * `VehicleUtils` waren `CarLaneFlags.AllowEnter`, ein anderes Enum. Bis das
     * geklaert ist, werden sie nur GEZAEHLT und gemeldet.
     */
    public sealed partial class ParkingLotComfortSystem : GameSystemBase
    {
        /**
         * Der Zielwert als Anteil von 0 bis 1, wie `ParkingFacilityData` ihn
         * fuehrt.
         *
         * 0,50 ist GEMESSEN, nicht gewaehlt: die Parkspur des Vanilla-
         * Parkplatzes `ParkingLot03` fuehrte im Steckbrief vom 2026-08-17 den
         * Wert 33095, also exakt 0,50. Ueber alle 52 Vanilla-Prefabs mit
         * `ParkingFacilityData` liegt der Schnitt bei 0,45 (min 0,00,
         * max 1,00).
         *
         * Vorher stand hier 0,8 - der Wert, den CS2 jedem beliebigen Gebaeude
         * ohne eigene Parkplatzdaten gibt. Das war eine willkuerliche
         * Obergrenze und haette unseren Parkplatz gegenueber Vanilla
         * BEVORZUGT. Ziel ist Gleichstand, nicht Bevorzugung.
         */
        internal const float ZielKomfort = 0.5f;

        /**
         * Wie oft die Belegung gemeldet wird. `ModificationEnd` laeuft je Bild,
         * 2000 Bilder sind also grob eine halbe Minute. Der Zensus laeuft ueber
         * ALLE Parkspuren der Stadt - das ist fuer eine Diagnose in Ordnung,
         * aber nicht fuer jedes Bild.
         */
        private const int BerichtAlleFrames = 2000;

        /**
         * BEWEGEN SICH DIE AUFKLEBER WIRKLICH, oder sieht es nur so aus?
         *
         * Der Nutzer beschreibt am 2026-08-17: zeigt er auf eine
         * Stellplatzmarkierung und fuehrt den Zeiger wieder weg, sitzen alle
         * uebrigen Markierungen kurz gebuendelt an dieser einen Stelle und
         * wandern von dort an ihre Plaetze zurueck. Hinterher liegt alles
         * richtig.
         *
         * Ich kann nicht auf seinen Bildschirm sehen - also merkt sich dieser
         * Waechter die Position JEDES unserer Objekte und meldet, sobald eine
         * sich wirklich aendert. Damit ist die Frage entschieden, statt
         * gedeutet:
         *
         *   keine Meldung -> reine Darstellung, die Objekte stehen still
         *   Meldung       -> sie werden tatsaechlich versetzt, und dann muss
         *                    die Ursache weg, bevor sie einmal NICHT
         *                    zurueckfinden
         */
        private readonly System.Collections.Generic.Dictionary<Entity, float3> _letztePos =
            new System.Collections.Generic.Dictionary<Entity, float3>();
        private EntityQuery _bewegtQuery;
        private int _bewegtGemeldet;

        private ParkingLotToolSystem _tool;
        private PrefabSystem _prefabSystem;
        private EntityQuery _laneQuery;
        private EntityQuery _alleSpuren;
        private bool _vanillaGemeldet;
        private bool _steckbriefGemeldet;
        private int _zuletztGesetzt = -1;
        private int _frames;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            // Dieselben Ausloeser wie CS2 selbst: nur Spuren anfassen, die
            // gerade neu gerechnet wurden. Alles andere waere jeden Frame ein
            // Durchlauf ueber saemtliche Parkspuren der Stadt.
            _laneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<Game.Net.ParkingLane>(),
                    ComponentType.ReadOnly<Owner>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<PathfindUpdated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            // Fuer die Belegungszaehlung: ALLE Parkspuren, nicht nur die gerade
            // neu gerechneten. Laeuft nur alle `BerichtAlleFrames` Bilder.
            _alleSpuren = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.ParkingLane>(),
                    ComponentType.ReadOnly<Owner>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            // Unsere Objekte, sobald CS2 sie anfasst. `Updated` bekommen sie
            // auch beim blossen Hovern - genau der Moment, um den es geht.
            _bewegtQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            // KEIN RequireForUpdate: die Belegungszaehlung muss auch laufen,
            // wenn gerade keine Spur neu gerechnet wird - sonst meldet sie sich
            // ausgerechnet im eingeschwungenen Zustand nie.
        }

        [Preserve]
        /** Der zuletzt gemeldete Belegungsstand - siehe die Meldung unten. */
        private string _letzterBelegungsstand;

        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Komfort);
            if (_tool == null) return;
            if (!_tool.TryGetCleanupPrefabs(out var lotPrefab, out var objektPrefabs)) return;

            MeldeVanillaWerteEinmal();
            // TerrainSystem laeuft am Ende derselben Phase HINTER diesem
            // System. Der Trace beobachtet deshalb erst den Applied/Updated-
            // Auftrag und liest im folgenden Zyklus den abgeschlossenen
            // Terrain-Readback. Keine geratenen Warteframes.
            _tool.PollTerrainAfterApply();
            _tool.PflegeNachbauOhneWerkzeug();
            PruefeBewegung(objektPrefabs);

            if (++_frames >= BerichtAlleFrames)
            {
                _frames = 0;
                MeldeBelegung(lotPrefab, objektPrefabs);
            }

            if (_laneQuery.IsEmptyIgnoreFilter) return;
            var spuren = _laneQuery.ToEntityArray(Allocator.Temp);
            try
            {
                var unsere = 0;
                var gesetzt = 0;
                var warNull = 0;
                var mitAllowEnter = 0;
                var mitAllowExit = 0;
                var gebuehrMin = int.MaxValue;
                var gebuehrMax = int.MinValue;
                ushort vorherMax = 0;

                for (var i = 0; i < spuren.Length; i++)
                {
                    var spur = spuren[i];
                    if (!TryGetParkingLot(spur, lotPrefab, out var lot)) continue;
                    unsere++;

                    var lane = EntityManager.GetComponentData<Game.Net.ParkingLane>(spur);
                    if (lane.m_ComfortFactor == 0) warNull++;
                    if (lane.m_ComfortFactor > vorherMax) vorherMax = lane.m_ComfortFactor;
                    if ((lane.m_Flags & Game.Net.ParkingLaneFlags.AllowEnter) != 0)
                        mitAllowEnter++;
                    if ((lane.m_Flags & Game.Net.ParkingLaneFlags.AllowExit) != 0)
                        mitAllowExit++;

                    /*
                     * DIE PARKGEBUEHR GEHOERT HIERHIN, NICHT AN EIN GEBAEUDE.
                     *
                     * Vanilla schreibt den Preis ans Gebaeude und sucht ihn von
                     * der Bucht aus nach OBEN. Unsere Spuren haengen am nackten
                     * Traeger, und der Wirtschafts-Begleiter steht daneben statt
                     * darueber - die Suche laeuft an ihm vorbei und kommt mit
                     * null zurueck. Am 2026-08-26 belegt, samt der zwei
                     * Auswege, die beide ausscheiden: Flaeche oder Traeger zum
                     * Gebaeude machen kostet Auswahlfenster, Bulldozer und die
                     * Lage der Aufkleber.
                     *
                     * Das Bezahlen selbst braucht das Gebaeude aber gar nicht.
                     * `PersonalCarAISystem` (628-680) haengt die ganze Kette an
                     * EINER Bedingung - `ParkingLane.m_ParkingFee > 0` auf der
                     * Spur, auf der das Auto parkt:
                     *
                     *     m_Payer     = Haushalt
                     *     m_Recipient = m_City
                     *     m_Resource  = PlayerResource.Parking
                     *
                     * Der Haushalt zahlt, die Stadt kassiert, und es landet in
                     * der richtigen Einnahmekategorie "Parken". Wir muessen
                     * also nichts selbst verbuchen - nur die Zahl setzen.
                     *
                     * Der Zeitpunkt ist derselbe wie beim Komfort und aus
                     * demselben Grund Pflicht: `ParkingLaneDataSystem` rechnet
                     * beide Werte aus der Besitzerkette neu und wuerde uns
                     * sonst ueberschreiben.
                     */
                    // Vor dem ersten Wirtschaftslauf darf fuer genau einen
                    // Bauframe der Baustandard gelten. Sobald die Instanzdaten
                    // stehen, kommt der Wert ausschliesslich vom Parkplatz.
                    /*
                     * AM HAUPTSCHALTER HAENGEN BEIDE WERTE.
                     *
                     * Ist die Wirtschaft aus, kostet Parken nichts und der
                     * Parkplatz wirbt nicht mehr um Autos: Gebuehr und Komfort
                     * gehen auf null. Erreichbar bleibt er trotzdem - das
                     * haengt an der Anbindung der Spur, nicht am Komfort.
                     *
                     * Der gespeicherte Gebuehrenwert am Parkplatz wird dabei
                     * NICHT angefasst. Er steht in `ParkingLotEconomyData` und
                     * kommt beim Wiedereinschalten unveraendert zurueck.
                     */
                    var wirtschaftAn = Mod.WirtschaftAn;
                    var zielGebuehrInt = !wirtschaftAn
                        ? 0
                        : EntityManager.HasComponent<ParkingLotEconomyData>(lot)
                        ? EntityManager.GetComponentData<ParkingLotEconomyData>(lot)
                            .ParkingFee
                        : Mod.Optionen?.Parkgebuehr ?? 10;
                    zielGebuehrInt = UnityEngine.Mathf.Clamp(zielGebuehrInt, 0, 65535);
                    if (zielGebuehrInt < gebuehrMin) gebuehrMin = zielGebuehrInt;
                    if (zielGebuehrInt > gebuehrMax) gebuehrMax = zielGebuehrInt;
                    var zielGebuehr = (ushort)zielGebuehrInt;
                    var gebuehrAnders = lane.m_ParkingFee != zielGebuehr;

                    var ziel = !wirtschaftAn ? (ushort)0 : (ushort)UnityEngine.Mathf.Clamp(
                        UnityEngine.Mathf.RoundToInt(ZielKomfort * 65535f), 0, 65535);
                    if (lane.m_ComfortFactor == ziel && !gebuehrAnders) continue;
                    lane.m_ComfortFactor = ziel;
                    lane.m_ParkingFee = zielGebuehr;
                    EntityManager.SetComponentData(spur, lane);
                    gesetzt++;
                }

                // Nur melden, wenn sich die Lage aendert - sonst schreibt das
                // System bei jedem Nachrechnen dieselbe Zeile ins Log.
                if (unsere > 0 && gesetzt != _zuletztGesetzt)
                {
                    _zuletztGesetzt = gesetzt;
                    Mod.log.Info($"PLT-Komfort: {unsere} eigene Parkspuren gerechnet, "
                        + $"davon {warNull} mit Komfort 0 (hoechster Wert vorher "
                        + $"{vorherMax}). {gesetzt} auf {ZielKomfort:0.00} gesetzt. "
                        + $"AllowEnter {mitAllowEnter}, AllowExit {mitAllowExit} "
                        + "(nur gezaehlt, nicht gesetzt). Parkgebuehr "
                        + (gebuehrMin == gebuehrMax
                            ? gebuehrMin.ToString()
                            : gebuehrMin + " bis " + gebuehrMax)
                        + " je Parkplatz und Vorgang.");
                }
            }
            finally
            {
                spuren.Dispose();
            }
        }

        /** Siehe die Begruendung an `_letztePos`. */
        private void PruefeBewegung(Entity[] objektPrefabs)
        {
            if (_bewegtQuery.IsEmptyIgnoreFilter) return;
            var entities = _bewegtQuery.ToEntityArray(Allocator.Temp);
            try
            {
                var bewegt = 0;
                var neu = 0;
                var maxWeg = 0f;
                var beispiel = float3.zero;
                for (var i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    var unser = false;
                    for (var k = 0; k < objektPrefabs.Length; k++)
                        if (objektPrefabs[k] == prefab) { unser = true; break; }
                    if (!unser) continue;
                    /*
                     * NUR OBJEKTE UNSERER PARKPLAETZE.
                     *
                     * Das Prefab allein reicht nicht: Buchtaufkleber und
                     * Pfeile sind Vanilla-Prefabs, die auch Vanilla-
                     * Parkplaetze und Gebaeude tragen. Am 2026-09-24 meldete
                     * der Waechter "45 Objekte versetzt, groesster Weg 97,84 m"
                     * in einem Spielstand OHNE aelteren PLT-Parkplatz - die
                     * Bewegten lagen ausserhalb des neuen Parkplatzes und
                     * gehoerten also dem Spiel.
                     */
                    if (!EntityManager.HasComponent<ParkingLotPartRelation>(e)) continue;

                    var pos = EntityManager
                        .GetComponentData<Game.Objects.Transform>(e).m_Position;
                    if (!_letztePos.TryGetValue(e, out var alt)) { _letztePos[e] = pos; neu++; continue; }
                    var weg = math.distance(alt, pos);
                    if (weg > 0.01f)
                    {
                        bewegt++;
                        if (weg > maxWeg) { maxWeg = weg; beispiel = pos; }
                        _tool.NoteTerrainObjectMovement(e, alt, pos);
                    }
                    _letztePos[e] = pos;
                }
                // Nur die ersten Male melden - sonst flutet ein Bau das Log.
                if (bewegt > 0 && _bewegtGemeldet < 20)
                {
                    _bewegtGemeldet++;
                    Mod.log.Info($"PLT-Bewegung: {bewegt} unserer Objekte haben ihre "
                        + $"Position GEAENDERT (groesster Weg {maxWeg:F2} m, "
                        + $"z.B. nach {beispiel.x:F1}/{beispiel.z:F1}). "
                        + "Das ist keine Darstellung, die Objekte werden versetzt.");
                }
                // Beim Neubau wachsen sonst die Eintraege ins Unendliche.
                if (_letztePos.Count > 20000) _letztePos.Clear();
            }
            finally
            {
                entities.Dispose();
            }
        }

        /**
         * Die Besitzerkette hoch bis zur Wurzel - genau wie CS2 es in
         * `GetParkingStats` tut. Die Schrittzahl ist begrenzt: eine kaputte
         * Kette darf das Spiel nicht einfrieren.
         */
        private Entity WurzelVon(Entity spur)
        {
            if (!EntityManager.HasComponent<Owner>(spur)) return Entity.Null;
            var aktuell = EntityManager.GetComponentData<Owner>(spur).m_Owner;
            for (var schritt = 0; schritt < 8; schritt++)
            {
                if (aktuell == Entity.Null || !EntityManager.Exists(aktuell)) return Entity.Null;
                if (!EntityManager.HasComponent<Owner>(aktuell)) break;
                aktuell = EntityManager.GetComponentData<Owner>(aktuell).m_Owner;
            }
            return EntityManager.Exists(aktuell) ? aktuell : Entity.Null;
        }

        /**
         * Fuehrt eine Parkspur zu genau ihrer fachlichen PLT-Flaeche zurueck.
         *
         * Wege enden mit ihrer Besitzerkette direkt an der Lot-Flaeche.
         * Aufkleberspuren enden am besitzerlosen Aufkleber; dessen gespeicherte
         * `ParkingLotPartRelation` ist der exakte Rueckweg. Kein raeumlicher
         * Treffer und kein fremdes Parkplatz-Prefab werden dafuer benutzt.
         */
        private bool TryGetParkingLot(Entity spur, Entity lotPrefab, out Entity lot)
        {
            lot = Entity.Null;
            var wurzel = WurzelVon(spur);
            if (wurzel == Entity.Null) return false;

            if (EntityManager.HasComponent<ParkingLotPartRelation>(wurzel))
            {
                var relation = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(wurzel);
                if (relation.Lot != Entity.Null
                    && EntityManager.Exists(relation.Lot)
                    && EntityManager.HasComponent<ParkingLotCarrierReference>(relation.Lot))
                {
                    lot = relation.Lot;
                    return true;
                }
                return false;
            }

            if (!EntityManager.HasComponent<PrefabRef>(wurzel)) return false;
            if (EntityManager.GetComponentData<PrefabRef>(wurzel).m_Prefab != lotPrefab)
                return false;
            if (!EntityManager.HasComponent<ParkingLotCarrierReference>(wurzel))
                return false;
            lot = wurzel;
            return true;
        }

        /**
         * Alle Parkspuren, die zu einem PLT-Parkplatz gehoeren.
         *
         * Fuer `ParkingLotStatistikSystem`. Der Rueckweg von der Spur zum Lot
         * ist hier bereits geloest und hat zwei Sorten (Strassenspur ueber die
         * Besitzerkette, Aufkleberspur ueber `ParkingLotPartRelation`) - genau
         * deshalb wird er nicht ein zweites Mal nachgebaut, sondern geteilt.
         *
         * Beide Listen sind gleich lang; Eintrag i gehoert zu Eintrag i.
         */
        internal void SammleUnsereParkspuren(
            System.Collections.Generic.List<Entity> spurAus,
            System.Collections.Generic.List<Entity> lotAus)
        {
            spurAus.Clear();
            lotAus.Clear();
            if (_tool == null) return;
            if (!_tool.TryGetCleanupPrefabs(out var lotPrefab, out _)) return;

            using var spuren = _alleSpuren.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < spuren.Length; i++)
            {
                if (!TryGetParkingLot(spuren[i], lotPrefab, out var lot)) continue;
                spurAus.Add(spuren[i]);
                lotAus.Add(lot);
            }
        }

        /** Schreibt eine UI-Aenderung sofort auf die Spuren dieses Lots. */
        internal int SetParkingFeeImmediately(Entity lot, int fee)
        {
            if (_tool == null || lot == Entity.Null || !EntityManager.Exists(lot))
                return 0;
            if (!_tool.TryGetCleanupPrefabs(out var lotPrefab, out _)) return 0;

            var changed = 0;
            using var spuren = _alleSpuren.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < spuren.Length; i++)
            {
                if (!TryGetParkingLot(spuren[i], lotPrefab, out var spurLot)
                    || spurLot != lot)
                    continue;
                var lane = EntityManager
                    .GetComponentData<Game.Net.ParkingLane>(spuren[i]);
                var target = (ushort)UnityEngine.Mathf.Clamp(fee, 0, 65535);
                if (lane.m_ParkingFee == target) continue;
                lane.m_ParkingFee = target;
                EntityManager.SetComponentData(spuren[i], lane);
                changed++;
            }
            return changed;
        }

        /**
         * Gehoert diese Wurzel uns?
         *
         * ZWEI SORTEN, und die zweite hatte ich am 2026-08-17 zunaechst
         * uebersehen: Spuren unserer STRASSEN landen ueber die Besitzerkette
         * bei der Lot-Flaeche. Die Parkspuren der AUFKLEBER dagegen enden beim
         * Aufkleber selbst - er hat absichtlich KEINEN Besitzer, sonst
         * verstreut `SubObjectSystem.RelocateSubObjects` ihn ueber die Flaeche,
         * sobald nebenan gebaut wird (siehe `ParkingLotLotOwner`). Wer nur auf
         * die Lot-Flaeche prueft, zaehlt genau die Spuren nicht mit, auf denen
         * geparkt werden soll.
         */
        private bool IstUnser(Entity wurzel, Entity lotPrefab, Entity[] objektPrefabs)
        {
            if (wurzel == Entity.Null) return false;
            if (!EntityManager.HasComponent<PrefabRef>(wurzel)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(wurzel).m_Prefab;
            if (prefab == lotPrefab) return true;
            if (objektPrefabs == null) return false;
            for (var i = 0; i < objektPrefabs.Length; i++)
                if (objektPrefabs[i] == prefab) return true;
            return false;
        }

        /**
         * DER EIGENTLICHE BEWEIS: werden unsere Buchten benutzt?
         *
         * "Gefuehlt kein Unterschied" laesst sich nicht nachpruefen. Diese
         * Zaehlung stellt unsere Spuren neben die der Vanilla-Parkplaetze
         * DERSELBEN Stadt - gleiche Einwohner, gleiche Wege, gleicher Zeitpunkt.
         * Steht bei uns 0 belegt und bei Vanilla 60 %, liegt es nicht am
         * Komfortwert, sondern daran, dass unsere Buchten gar nicht in Frage
         * kommen. Stehen beide aehnlich, wirkt der Wert bereits.
         *
         * Gezaehlt werden geparkte Fahrzeuge ueber den `LaneObject`-Puffer der
         * Spur - dieselbe Quelle, aus der CS2 selbst den freien Platz rechnet.
         */
        private void MeldeBelegung(Entity lotPrefab, Entity[] objektPrefabs)
        {
            var spuren = _alleSpuren.ToEntityArray(Allocator.Temp);
            try
            {
                int unsSpuren = 0, unsAutos = 0, unsKomfortSumme = 0;
                int vanSpuren = 0, vanAutos = 0, vanKomfortSumme = 0;
                // ECHTE Parkspuren getrennt von Einsteigespuren zaehlen.
                // `VirtualLane` heisst Boarding, nicht Parken - siehe
                // `PathUtils.GetParkingSpaceSpecification`. Am 2026-08-17 hat
                // mein erster Steckbrief auf BEIDEN Seiten eine Einsteigespur
                // erwischt (erkennbar an Bucht 0,00 x 0,00 m) und war wertlos.
                int unsEcht = 0, vanEcht = 0, unsGesperrt = 0, vanGesperrt = 0;
                float unsPlatz = 0f, vanPlatz = 0f;
                var beispielUnser = Entity.Null;
                var beispielVanilla = Entity.Null;
                for (var i = 0; i < spuren.Length; i++)
                {
                    var spur = spuren[i];
                    var wurzel = WurzelVon(spur);
                    if (wurzel == Entity.Null) continue;

                    var unser = IstUnser(wurzel, lotPrefab, objektPrefabs);
                    var vanilla = !unser
                        && EntityManager.HasComponent<Game.Buildings.ParkingFacility>(wurzel);
                    if (!unser && !vanilla) continue;

                    var autos = EntityManager.HasBuffer<Game.Net.LaneObject>(spur)
                        ? EntityManager.GetBuffer<Game.Net.LaneObject>(spur).Length : 0;
                    var komfort = EntityManager
                        .GetComponentData<Game.Net.ParkingLane>(spur).m_ComfortFactor;
                    var lane = EntityManager.GetComponentData<Game.Net.ParkingLane>(spur);
                    var virtuell = (lane.m_Flags & Game.Net.ParkingLaneFlags.VirtualLane) != 0;
                    var gesperrt = (lane.m_Flags & Game.Net.ParkingLaneFlags.ParkingDisabled) != 0;
                    if (unser)
                    {
                        unsSpuren++; unsAutos += autos; unsKomfortSumme += komfort;
                        if (!virtuell) { unsEcht++; unsPlatz += lane.m_FreeSpace; }
                        if (gesperrt) unsGesperrt++;
                        // Als Beispiel NUR eine echte Parkspur nehmen.
                        if (!virtuell && beispielUnser == Entity.Null) beispielUnser = spur;
                    }
                    else
                    {
                        vanSpuren++; vanAutos += autos; vanKomfortSumme += komfort;
                        if (!virtuell) { vanEcht++; vanPlatz += lane.m_FreeSpace; }
                        if (gesperrt) vanGesperrt++;
                        if (!virtuell && beispielVanilla == Entity.Null) beispielVanilla = spur;
                    }
                }
                // Sobald BEIDE Sorten in derselben Stadt stehen, einmal den
                // vollen Steckbrief nebeneinander legen. Das ist die Antwort
                // auf "was brauchen wir eigentlich noch".
                if (!_steckbriefGemeldet
                    && beispielUnser != Entity.Null && beispielVanilla != Entity.Null)
                {
                    _steckbriefGemeldet = true;
                    Mod.log.Info("PLT-Steckbrief: eine unserer Parkspuren gegen eine "
                        + "eines Vanilla-Parkplatzes, gleiche Stadt, gleicher Moment.");
                    Steckbrief("UNSER  ", beispielUnser);
                    Steckbrief("VANILLA", beispielVanilla);
                }
                // HAELT DAS `Attached` UEBERHAUPT?
                //
                // `Game.Objects.AttachSystem` verarbeitet JEDES Objekt mit
                // `Object` + `Attached`, sobald es `Updated` bekommt - schon
                // ein Hover reicht. Es rechnet den Elternteil per `FindParent`
                // NEU aus und setzt `Attached` auf default, wenn es keinen
                // passenden findet. Dann waere unsere Strassenanbindung wieder
                // weg (siehe cs2-parkspur-braucht-attached).
                //
                // Der Nutzer meldete am 2026-08-17, dass Aufkleber beim Hovern
                // kurz zu springen scheinen. Diese Zaehlung sagt, ob es beim
                // Schein bleibt oder ob der Elternteil wirklich verlorengeht.
                int mitTraeger = 0, ohneEltern = 0, falscheEltern = 0;
                for (var i = 0; i < spuren.Length; i++)
                {
                    if (!EntityManager.HasComponent<Owner>(spuren[i])) continue;
                    var objekt = EntityManager.GetComponentData<Owner>(spuren[i]).m_Owner;
                    if (objekt == Entity.Null || !EntityManager.Exists(objekt)
                        || !EntityManager.HasComponent<ParkingLotPartRelation>(objekt))
                        continue;
                    var relation = EntityManager
                        .GetComponentData<ParkingLotPartRelation>(objekt);
                    if (!EntityManager.HasComponent<Game.Objects.Attached>(objekt))
                    { ohneEltern++; continue; }
                    var eltern = EntityManager
                        .GetComponentData<Game.Objects.Attached>(objekt).m_Parent;
                    if (eltern == Entity.Null) ohneEltern++;
                    else if (eltern == relation.Carrier) mitTraeger++;
                    else falscheEltern++;
                }
                /*
                 * NUR MELDEN, WENN ETWAS FEHLT.
                 *
                 * Diese Zeile lief alle 29 Sekunden durch, solange ein
                 * Parkplatz stand - dauerhaft, und fast immer mit
                 * "0 haben KEINEN Elternteil mehr". Sie ist ein Waechter,
                 * kein Bericht: interessant ist ausschliesslich der Fall, dass
                 * `AttachSystem` die Anbindung umgeschrieben hat.
                 */
                if (ohneEltern + falscheEltern > 0)
                    Mod.log.Warn($"PLT-Anheftung: {mitTraeger} Aufkleberspuren zeigen auf "
                        + $"ihren Traeger, {ohneEltern} haben KEINEN Elternteil mehr, "
                        + $"{falscheEltern} zeigen woanders hin."
                        + "  <== AttachSystem hat sie umgeschrieben, die Anbindung ist futsch.");

                if (unsSpuren == 0 && vanSpuren == 0) return;

                /*
                 * NUR BEI AENDERUNG.
                 *
                 * Auch diese Zeile lief alle 29 Sekunden, meist wortgleich.
                 * Was sie sagen soll, ist eine VERAENDERUNG - Fahrzeuge,
                 * freier Platz, gesperrte Spuren. Bleibt alles gleich, sagt
                 * eine weitere identische Zeile nichts.
                 */
                var stand = $"{unsSpuren}/{unsEcht}/{unsGesperrt}/{unsAutos}/"
                    + $"{unsPlatz:0.0}|{vanSpuren}/{vanEcht}/{vanGesperrt}/"
                    + $"{vanAutos}/{vanPlatz:0.0}";
                if (stand == _letzterBelegungsstand) return;
                _letzterBelegungsstand = stand;

                Mod.log.Info("PLT-Belegung: UNSER "
                    + $"{unsSpuren} Spuren (davon {unsEcht} echte Parkspuren, "
                    + $"{unsGesperrt} gesperrt) / {unsAutos} Fahrzeuge / freier Platz "
                    + $"{unsPlatz:0.0} m / Komfort "
                    + $"{(unsSpuren > 0 ? unsKomfortSumme / (float)unsSpuren / 65535f : 0f):0.00}"
                    + "  ||  VANILLA "
                    + $"{vanSpuren} Spuren (davon {vanEcht} echte Parkspuren, "
                    + $"{vanGesperrt} gesperrt) / {vanAutos} Fahrzeuge / freier Platz "
                    + $"{vanPlatz:0.0} m / Komfort "
                    + $"{(vanSpuren > 0 ? vanKomfortSumme / (float)vanSpuren / 65535f : 0f):0.00}");
            }
            finally
            {
                spuren.Dispose();
            }
        }

        /**
         * Der volle Steckbrief EINER Parkspur: was sie ist, was sie traegt, und
         * wem sie ueber die ganze Besitzerkette gehoert.
         *
         * Genau daran laesst sich ablesen, was unserem Parkplatz gegenueber
         * einem Vanilla-Parkplatz noch fehlt - ohne zu raten, welche Komponente
         * es sein koennte.
         */
        private void Steckbrief(string titel, Entity spur)
        {
            var lane = EntityManager.GetComponentData<Game.Net.ParkingLane>(spur);
            var autos = EntityManager.HasBuffer<Game.Net.LaneObject>(spur)
                ? EntityManager.GetBuffer<Game.Net.LaneObject>(spur).Length : 0;
            Mod.log.Info($"  {titel} Spur {spur.Index}: Prefab '{NameVon(spur)}' | "
                + $"Komfort {lane.m_ComfortFactor} ({lane.m_ComfortFactor / 65535f:0.00}) | "
                + $"Gebuehr {lane.m_ParkingFee} | freier Platz {lane.m_FreeSpace:0.0} m | "
                + $"geparkt {autos} | Zugang "
                + (lane.m_AccessRestriction == Entity.Null
                    ? "frei (-1)" : lane.m_AccessRestriction.Index.ToString())
                + $" | Flags {lane.m_Flags}");

            if (EntityManager.HasComponent<PrefabRef>(spur))
            {
                var lanePrefab = EntityManager.GetComponentData<PrefabRef>(spur).m_Prefab;
                if (EntityManager.HasComponent<ParkingLaneData>(lanePrefab))
                {
                    var d = EntityManager.GetComponentData<ParkingLaneData>(lanePrefab);
                    Mod.log.Info($"  {titel}   Spurdaten: Bucht {d.m_SlotSize.x:0.00} x "
                        + $"{d.m_SlotSize.y:0.00} m | Winkel {d.m_SlotAngle:0.0} | "
                        + $"Abstand {d.m_SlotInterval:0.00} m | max. Laenge "
                        + $"{d.m_MaxCarLength:0.0} m | Verkehrsarten {d.m_RoadTypes}");
                }
            }

            // Die Besitzerkette Stufe fuer Stufe - dort steckt der Unterschied.
            var aktuell = spur;
            for (var stufe = 0; stufe < 8; stufe++)
            {
                if (!EntityManager.HasComponent<Owner>(aktuell)) break;
                aktuell = EntityManager.GetComponentData<Owner>(aktuell).m_Owner;
                if (aktuell == Entity.Null || !EntityManager.Exists(aktuell)) break;
                var merkmale = string.Empty;
                if (EntityManager.HasComponent<Game.Buildings.Building>(aktuell))
                    merkmale += " Building";
                if (EntityManager.HasComponent<Game.Buildings.ParkingFacility>(aktuell))
                {
                    var pf = EntityManager
                        .GetComponentData<Game.Buildings.ParkingFacility>(aktuell);
                    merkmale += $" ParkingFacility(Komfort {pf.m_ComfortFactor:0.00}, "
                        + $"Flags {pf.m_Flags})";
                }
                if (EntityManager.HasComponent<Game.Areas.Area>(aktuell)) merkmale += " Area";
                if (EntityManager.HasComponent<Game.Objects.Transform>(aktuell))
                    merkmale += " Objekt";
                if (EntityManager.HasComponent<Game.Net.Edge>(aktuell)) merkmale += " Netzkante";
                if (merkmale.Length == 0) merkmale = " (nichts davon)";
                Mod.log.Info($"  {titel}   Besitzer {stufe + 1}: {aktuell.Index} "
                    + $"'{NameVon(aktuell)}' |{merkmale}");
            }
        }

        private string NameVon(Entity entity)
        {
            try
            {
                if (!EntityManager.HasComponent<PrefabRef>(entity)) return "ohne PrefabRef";
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (_prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var basis)
                    && basis != null)
                    return basis.name;
                return "Prefab " + prefab.Index;
            }
            catch
            {
                // Ein Steckbrief darf niemals das Spiel kosten.
                return "unlesbar";
            }
        }

        /**
         * Was fuehren echte Parkplaetze eigentlich? Einmal messen statt raten -
         * danach laesst sich `ZielKomfort` mit einer Zahl begruenden statt mit
         * der Untergrenze 0,8, die CS2 jedem beliebigen Gebaeude gibt.
         */
        private void MeldeVanillaWerteEinmal()
        {
            if (_vanillaGemeldet) return;
            _vanillaGemeldet = true;
            var query = GetEntityQuery(ComponentType.ReadOnly<ParkingFacilityData>(),
                                       ComponentType.ReadOnly<PrefabData>());
            var prefabs = query.ToEntityArray(Allocator.Temp);
            try
            {
                var min = float.MaxValue;
                var max = float.MinValue;
                var summe = 0f;
                for (var i = 0; i < prefabs.Length; i++)
                {
                    var wert = EntityManager
                        .GetComponentData<ParkingFacilityData>(prefabs[i]).m_ComfortFactor;
                    if (wert < min) min = wert;
                    if (wert > max) max = wert;
                    summe += wert;
                }
                if (prefabs.Length == 0)
                {
                    Mod.log.Info("PLT-Komfort: kein einziges Prefab mit ParkingFacilityData "
                        + "gefunden - Vergleichswert unbekannt.");
                    return;
                }
                Mod.log.Info($"PLT-Komfort: {prefabs.Length} Vanilla-Prefabs mit "
                    + $"ParkingFacilityData. Komfort min {min:0.000}, max {max:0.000}, "
                    + $"Mittel {summe / prefabs.Length:0.000}. Unser Zielwert: "
                    + $"{ZielKomfort:0.000}.");
            }
            finally
            {
                prefabs.Dispose();
            }
        }
    }
}
