using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using Game.Common;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * VERSUCH, standardmaessig AUS - einzuschalten mit Shift+P.
     *
     * Der Parkplatz laesst sich bisher nur auf den Decals anklicken. Ursache
     * gemessen: `DefaultToolSystem` gibt dem Strahl `AreaTypeMask.Lots` und
     * NIE `Surfaces`, und die Art kommt aus `AreaGeometryData.m_Type` am
     * Prefab. Unsere Flaechen sind `Surface`.
     *
     * Alle 9 Lot-Prefabs des Spiels sind Industrie-Areale (Foerderung,
     * Landwirtschaft, Deponie) und braechten eigene Wirtschaftslogik mit -
     * gemessen aus dem Abzug. Bleibt: ein eigenes anmelden.
     *
     * WIE ES EINGESTELLT IST, und warum jeder Wert so gewaehlt ist
     * (`AreaInitializeSystem`, Lot-Zweig):
     *   m_AllowOverlap = true   nimmt GeometryFlags.PhysicalGeometry weg, die
     *                           Flaeche kollidiert also nicht mit unseren
     *                           eigenen Flaechen.
     *   m_AllowEditing = true   MUSS true bleiben. Bei false setzt CS2
     *                           GeometryFlags.HiddenIngame, und
     *                           `Areas.RaycastJobs.ValidateResult` verwirft
     *                           versteckte Flaechen ausserhalb des Editors -
     *                           unsichtbar hiesse dann auch unanklickbar.
     *   Farben transparent      erledigt das Unsichtbarmachen stattdessen
     *                           ueber AreaColorData. Die Auswahlfarben
     *                           bleiben sichtbar, damit die Auswahl etwas
     *                           anzeigt.
     *
     * OFFEN und genau der Grund fuer den Schalter: ob CS2 mit einer
     * Lot-Flaeche ohne zugehoeriges Gebaeude auf Dauer klarkommt, und ob ein
     * moddefiniertes Prefab im Spielstand Aerger macht, wenn der Mod fehlt.
     * Deshalb erst in einem Wegwerf-Spielstand ausprobieren.
     */
    public sealed partial class ParkingLotToolSystem
    {
        internal const string LotOwnerPrefabName = "PLT Parkplatzflaeche";
        internal const string LotOwnerRecordKind = "LotOwner";

        private Entity _lotOwnerPrefab = Entity.Null;
        private bool _lotOwnerPrefabFailed;

        /**
         * STANDARD AN, seit der Speichern-Test bestanden ist (2026-08-11).
         *
         * Gemessen an der Bauteilliste NACH dem Neuladen: das angewaehlte
         * Objekt ist "PLT Parkplatzflaeche" [LotPrefab], es traegt
         * `UI.CustomName`, und daran haengen weiterhin 24 Flaechen, 50
         * Wegteile und 337 Objekte. Im Log der Ladesitzung kein
         * `Unknown prefab ID`, kein `Duplicate prefab ID`, keine Ausnahme.
         *
         * Shift+P bleibt als AUSschalter, falls sich doch noch etwas zeigt.
         */
        internal bool UseLotAreaOwner { get; private set; } = true;

        internal void ToggleLotAreaOwner()
        {
            UseLotAreaOwner = !UseLotAreaOwner;
            _lastPreviewSig = long.MinValue;
            var state = UseLotAreaOwner ? "AN" : "AUS";
            // Die erste Fassung sagte nur "Gilt ab dem naechsten Bau", und
            // genau daran ging der erste Testlauf vorbei: umgeschaltet wurde
            // NACH dem Bauen, geprueft am schon stehenden Parkplatz. Deshalb
            // steht jetzt ausdruecklich da, was als naechstes zu tun ist.
            Mod.log.Info($"PLT-Versuch 'Lot-Fläche als Besitzer': {state}. "
                + "Wirkt NUR auf den nächsten neu gebauten Parkplatz - jetzt "
                + "Polygon ziehen und Enter. Schon stehende Parkplätze ändern "
                + "sich dadurch nicht.");
            _debugTooltipSystem?.Show(T(
                $"Lot-Fläche als Besitzer: {state} - gilt für den nächsten Parkplatz",
                $"Lot area as owner: {state} - applies to the next parking lot"));
            if (!UseLotAreaOwner) return;
            var missing = LotOwnerPrefabState(_lotOwnerPrefab);
            if (missing != null)
                Mod.log.Warn($"PLT: '{LotOwnerPrefabName}' ist noch nicht "
                    + $"benutzbar ({missing}). Der nächste Bau fällt auf eine "
                    + "Fläche als Besitzer zurück.");
        }

        /**
         * Anmelden, sobald das System entsteht - NICHT erst beim Einschalten.
         *
         * Zweiter Anlauf, gemessen: beim Einschalten angemeldet wurde das
         * Prefab in hunderten Frames nie fertig. Grund ist die Abfrage von
         * `AreaInitializeSystem`: sie verlangt `Created`, und dieses Flag
         * raeumt CS2 am Frame-Ende weg. Ein Prefab, das WAEHREND des
         * Werkzeug-Updates dazukommt, verpasst die Prefab-Initialisierung
         * dieses Frames und traegt im naechsten kein `Created` mehr - es
         * bekommt also nie einen Archetyp.
         *
         * OnCreate laeuft beim Laden des Mods, also zur selben Zeit, zu der
         * das Spiel seine eigenen Prefabs anmeldet.
         */
        private void InitializeLotArea()
        {
            TryResolveLotOwnerPrefab(out _);
        }

        /**
         * Ist das Prefab inzwischen fertig? Dann die Vorschau neu erzeugen.
         *
         * Ohne das bliebe eine Vorschau stehen, die erzeugt wurde, als der
         * Archetyp noch nicht stand - und die haette keine Besitzerflaeche.
         */
        private void PollLotOwnerPrefab()
        {
            if (_lotOwnerPrefabAnnounced) return;
            if (!LotOwnerPrefabReady(_lotOwnerPrefab)) return;
            _lotOwnerPrefabAnnounced = true;
            _lastPreviewSig = long.MinValue;
            Mod.log.Info($"PLT: '{LotOwnerPrefabName}' ist einsatzbereit "
                + "(Archetyp steht, Art ist Lot).");
        }

        private bool _lotOwnerPrefabAnnounced;

        /**
         * ABSTURZURSACHE, teuer gelernt am 2026-08-11: das Prefab wurde in
         * DEMSELBEN Frame benutzt, in dem es angemeldet wurde (Log: anmelden
         * 13:01:25,135, Flaechen erzeugen ,166). CS2 fuellt
         * `AreaData.m_Archetype` aber erst in seiner Prefab-Initialisierung,
         * also einen Frame spaeter. Eine Entity aus einem ungueltigen
         * Archetyp zu erzeugen beendet den Prozess nativ - im Player.log
         * stand "Got a UNKNOWN while executing native code" ohne jeden
         * verwalteten Stack.
         *
         * Fuer die Vanilla-Flaechen prueft `HasUsableSurfacePrefab` genau das
         * seit jeher. Hier fehlte es. Anmelden und Benutzen sind deshalb jetzt
         * getrennt: angemeldet wird beim Einschalten, benutzt erst, wenn der
         * Archetyp steht.
         */
        private bool _lotClearsObjects;

        private bool LotOwnerPrefabReady(Entity prefab)
        {
            if (LotOwnerPrefabState(prefab) != null) return false;
            EnsureLotClearsObjects(prefab);
            return true;
        }

        /**
         * BAEUME UND FELSEN UNTER DEM PARKPLATZ AUSBLENDEN.
         *
         * Nutzerbefund 2026-08-14: manche Baeume bleiben stehen, andere nicht,
         * "es gibt keinen genauen Anhaltspunkt". Gemessen war die Erklaerung
         * nicht die Groesse, sondern die Lage - KEINE unserer Flaechen raeumt:
         *
         *   PLT Parkplatzflaeche : Flags PseudoRandom  -> raeumt NEIN
         *   Grass Surface 01     : Flags 0             -> raeumt NEIN
         *   Pavement Surface 01  : Flags 0             -> raeumt NEIN
         *
         * Was verschwand, raeumten die STRASSEN weg, wie jede Vanilla-Strasse
         * ihre Trasse freiraeumt. Alles daneben blieb stehen.
         *
         * Das Flag gehoert ans LOT und nicht an die Einzelflaechen: ein
         * Polygon statt hunderter Gras- und Belagstuecke, und es deckt auch
         * die Zwischenraeume ab.
         *
         * `OverrideSystem` (Game.dll, Zeile 1319) steigt ohne dieses Flag
         * sofort aus. Es setzt `Overridden`, NICHT `Deleted` - die Baeume
         * werden versteckt und kommen beim Bulldozern zurueck, genau wie bei
         * einem Gebaeude. Ein eigener Loeschweg waere endgueltig gewesen.
         *
         * WARUM DIREKT GESCHRIEBEN: `AreaInitializeSystem` (Zeile 308) gibt
         * einem Lot das Flag nur, wenn das Prefab `StorageAreaData` traegt -
         * das braechte Lager- und Wirtschaftslogik mit, die ein Parkplatz
         * nicht will. Hier wird nur das eine Bit auf UNSEREM eigenen Prefab
         * gesetzt; kein Vanilla-Prefab wird angefasst.
         */
        /**
         * TESTSCHALTER fuer das Hover-Flackern.
         *
         * Der Nutzer beschreibt am 2026-08-17: zeigt er auf eine
         * Stellplatzmarkierung, flackern ALLE Markierungen DESSELBEN Typs kurz
         * an einer falschen Stelle - die E-Plaetze, wenn er auf einen E-Platz
         * zeigt, die normalen bleiben ruhig. Die Originale bleiben dabei
         * stehen; es bewegt sich eine Kopie.
         *
         * Der Bewegungswaechter bestaetigt das: nach dem Bau meldet er 13
         * Objekte mit 3 cm (das Einrasten aufs Gelaende), danach NICHTS mehr.
         * Es bewegt sich also wirklich nichts - es ist die
         * Instanz-Darstellung, die je Prefab gebuendelt ist.
         *
         * VERDACHT, nicht bewiesen: unsere Aufkleber tragen `Overridden`, weil
         * die Lot-Flaeche `CanOverrideObjects` traegt (um Baeume zu
         * verstecken). `PreCullingSystem` liest das CHUNK-weise (`flag4`,
         * Zeile 700) und behandelt solche Objekte beim Sichtbarkeitswechsel
         * anders.
         *
         * Steht dieser Schalter auf false, bekommt die Flaeche das Flag NICHT.
         * Dann sollte das Flackern verschwinden - und die Baeume unter dem
         * Parkplatz stehenbleiben. Genau dieser Tausch ist die Frage, die der
         * Test beantwortet.
         */
        /**
         * AUS - SEIT DEM 2026-09-16, UND NICHT MEHR ALS VERSUCH.
         *
         * Der Schalter war fuer eine Flackerfrage gedacht, die sich anders
         * erledigt hat. Jetzt entscheidet er etwas anderes, und zwar gemessen.
         *
         * Der Nutzer hat dreimal dasselbe gebaut:
         *
         *   frisch gebaut, dann Stadion ans Zoning   -> Requisiten weg
         *   eine Minute gewartet, dann Stadion       -> Requisiten weg
         *   Spielstand geladen, nicht gebaut         -> Requisiten bleiben
         *
         * Den Unterschied nennt das Log:
         *
         *   17:10:28  ===== Parking Lot Tool geladen =====
         *   17:10:59  PLT-Parkplatzflaeche raeumt jetzt Objekte
         *
         * Das Flag kommt erst beim ersten Bauen (`LotOwnerPrefabReady`). Wer
         * nur laedt, hat es nicht - und dann versteckt die Flaeche nichts.
         *
         * `OverrideSystem.AreaIterator` versteckt jedes `Overridable` Objekt
         * im Polygon, dessen Besitzerkette nicht bei derselben Flaeche endet.
         * Fuer Baeume und Felsen unter dem Asphalt ist das gewollt. Fuer ein
         * Stadion auf der Zoningflaeche nicht - und fuer jedes Haus, das dort
         * waechst, ebensowenig; die verlieren nach derselben Regel ihre
         * Zaeune und Vorgartenbaeume.
         *
         * Das Zoning auszusparen geht nicht: eine CS2-Flaeche ist EIN Ring,
         * und die Lot-Flaeche muss eine Entity bleiben - sie besitzt alle
         * Teile ueber ihre SubObject- und SubNet-Puffer. Ein Loch gibt es
         * dafuer nicht, und ein aufgeschnittener Ring, der sich selbst
         * beruehrt, verwirft CS2 ganz.
         *
         * Der Tausch ist damit: vorher verschwanden Baeume unter dem Asphalt -
         * und still auch die Requisiten jedes fremden Gebaeudes auf dem
         * Parkplatz. Jetzt verschwindet nichts, und wer die Baeume nicht will,
         * bulldozert sie vorher. Ein sichtbarer Mangel, den der Nutzer selbst
         * beheben kann, statt eines unsichtbaren, den niemand findet.
         *
         * DER SAUBERE WEG BLEIBT OFFEN: den BELAG raeumen lassen statt der
         * Lot-Flaeche. Der liegt genau dort, wo wir bauen, und nicht auf den
         * Zoningparzellen. Dafuer muessten die gewaehlten Flaechen ueber Klone
         * laufen wie schon die Vorflaeche - eine eigene Runde Arbeit.
         */
        private static readonly bool FlaecheVerstecktObjekte = false;

        private void EnsureLotClearsObjects(Entity prefab)
        {
            if (!FlaecheVerstecktObjekte)
            {
                if (_lotClearsObjects) return;
                _lotClearsObjects = true;
                Mod.log.Info("PLT-Parkplatzflaeche raeumt KEINE Objekte "
                    + "(FlaecheVerstecktObjekte = false). Baeume und Felsen "
                    + "unter dem Parkplatz bleiben stehen - dafuer behalten "
                    + "fremde Gebaeude auf der Zoningflaeche ihre Requisiten.");
                return;
            }
            if (_lotClearsObjects) return;
            _lotClearsObjects = true;
            var geometry = EntityManager.GetComponentData<AreaGeometryData>(prefab);
            if ((geometry.m_Flags & Game.Areas.GeometryFlags.CanOverrideObjects) != 0)
                return;
            geometry.m_Flags |= Game.Areas.GeometryFlags.CanOverrideObjects;
            EntityManager.SetComponentData(prefab, geometry);
            Mod.log.Info("PLT-Parkplatzflaeche raeumt jetzt Objekte: Flags "
                + geometry.m_Flags + ". Baeume und Felsen darunter werden "
                + "VERSTECKT (Overridden), nicht geloescht - beim Bulldozern "
                + "kommen sie zurueck.");
        }

        /**
         * Was noch fehlt - oder null, wenn es benutzbar ist.
         *
         * Als Text statt als bool, weil der erste Anlauf still nie fertig
         * wurde und im Log nichts stand, woran man es haette festmachen
         * koennen.
         */
        private string LotOwnerPrefabState(Entity prefab)
        {
            if (prefab == Entity.Null) return "noch nicht angemeldet";
            if (!EntityManager.Exists(prefab)) return "Prefab-Entity existiert nicht";
            if (!EntityManager.HasComponent<AreaData>(prefab))
                return "AreaData fehlt";
            if (!EntityManager.GetComponentData<AreaData>(prefab).m_Archetype.Valid)
                return "AreaData.m_Archetype noch ungültig "
                    + "(CS2s Prefab-Initialisierung lief noch nicht)";
            if (!EntityManager.HasComponent<AreaGeometryData>(prefab))
                return "AreaGeometryData fehlt";
            // Erst wenn AreaInitializeSystem gelaufen ist, steht die Art auf
            // Lot - und nur darauf reagiert der Auswahlstrahl.
            var type = EntityManager.GetComponentData<AreaGeometryData>(prefab).m_Type;
            return type == Game.Areas.AreaType.Lot
                ? null : $"Art ist {type} statt Lot";
        }

        private bool TryResolveLotOwnerPrefab(out Entity prefab)
        {
            prefab = _lotOwnerPrefab;
            if (prefab != Entity.Null) return LotOwnerPrefabReady(prefab);
            if (_lotOwnerPrefabFailed) return false;

            try
            {
                var lot = ScriptableObject.CreateInstance<LotPrefab>();
                lot.name = LotOwnerPrefabName;
                lot.m_MaxRadius = 500f;
                lot.m_AllowOverlap = true;
                lot.m_AllowEditing = true;
                lot.m_OnWater = false;
                // Unsichtbar ueber die Farben statt ueber HiddenIngame - siehe
                // die Begruendung oben am Typ.
                lot.m_Color = new Color(0f, 0f, 0f, 0f);
                lot.m_EdgeColor = new Color(0f, 0f, 0f, 0f);
                lot.m_SelectionColor = new Color(1f, 1f, 1f, 0.15f);
                lot.m_SelectionEdgeColor = new Color(1f, 1f, 1f, 0.6f);

                /**
                 * DIE FLAECHE WIRD EINE ECHTE PARKANLAGE.
                 *
                 * Auftrag des Nutzers am 2026-08-25: CS2 soll den Parkplatz
                 * mit Kapazitaet und Auslastung kennen und ihn in der
                 * Infoansicht "Parken" zeigen. Ausdruecklich OHNE Wirtschaft.
                 *
                 * WARUM DAS OHNE GEBAEUDE GEHT - gemessen, nicht vermutet:
                 * `ParkingFacilityAISystem` ist das System, das Parkanlagen
                 * betreibt. Seine Query heisst zwar `m_BuildingQuery`,
                 * verlangt aber nur `Game.Buildings.ParkingFacility` und
                 * schliesst `ServiceUpgrade`, `Temp` und `Deleted` aus - kein
                 * `Building`, kein `BuildingData`, kein `PrefabRef` auf ein
                 * `BuildingPrefab`. Eine Flaeche mit dieser Komponente wird
                 * ganz normal verarbeitet.
                 *
                 * WARUM GENAU HIER, VOR `AddPrefab`:
                 * `AreaPrefab.LateInitialize` sammelt die Archetypkomponenten
                 * EINMAL ueber alle Bauteile des Prefabs und legt daraus
                 * `AreaData.m_Archetype` an. Was hier nicht dransteht, fehlt
                 * der fertigen Flaeche fuer immer - nachtraeglich an der
                 * Instanz waere der falsche Weg.
                 *
                 * Was daraus entsteht: `ParkingFacilityData` und
                 * `UpdateFrameData` am Prefab, `Game.Buildings.ParkingFacility`
                 * und `CarParkingFacility` an der gebauten Flaeche. NICHT
                 * dabei: `Building`, `BuildingData`, `CityServiceBuilding`,
                 * `Efficiency`, `Buildings.Lot`, `Object`, `Transform`. Genau
                 * dieser Ballast hat die zwei Gebaeude-Anlaeufe im Juli
                 * gekostet (Terrainloch, Absturz beim Bulldozern).
                 *
                 * Die Kapazitaet muss NICHT eingetragen werden: der Tickjob
                 * zaehlt die echten `ParkingLane`-Spuren ueber die SubNet- und
                 * SubLane-Puffer des Besitzers. Deshalb bleibt
                 * `m_GarageMarkerCapacity` auf 0 - das Feld gilt nur fuer
                 * Parkhausmarker, die wir nicht haben.
                 *
                 * 0,5 Komfort ist der Wert von `ParkingLot01` (im Abzug vom
                 * 2026-08-25 gemessen). Er wirkt auf die ANLAGE; der Komfort
                 * der einzelnen Spuren kommt weiterhin aus
                 * `ParkingLotComfortSystem`, weil die Vanilla-Uebernahme in
                 * `ParkingLane` gebaeudegebunden ist und bleibt.
                 */
                var parkanlage = lot.AddComponent<ParkingFacility>();
                parkanlage.m_RoadTypes = Game.Net.RoadTypes.Car;
                parkanlage.m_ComfortFactor = 0.5f;
                parkanlage.m_GarageMarkerCapacity = 0;

                /*
                 * EIN Prefab fuer alle Groessen. `CityServiceBuilding` muss
                 * vor `AddPrefab` dranstehen, weil `AreaPrefab` den
                 * Instanzarchetyp genau einmal aus den Prefabbauteilen baut.
                 * Der Zweierpotenz-Basisbetrag laesst jeden ganzzahligen
                 * Sollwert bis zum Generatorlimit ohne Float-Rundungsrest als
                 * ServiceUsage darstellen; die Instanz setzt den Faktor.
                 */
                var wirtschaft = lot.AddComponent<CityServiceBuilding>();
                wirtschaft.m_Upkeeps = new[]
                {
                    new ServiceUpkeepItem
                    {
                        m_Resources = new ResourceStackInEditor
                        {
                            m_Resource = ResourceInEditor.Money,
                            m_Amount = ParkingLotEconomySystem.UpkeepBasis,
                        },
                        m_ScaleWithUsage = true,
                    },
                };

                if (!_prefabSystem.AddPrefab(lot))
                {
                    _lotOwnerPrefabFailed = true;
                    Mod.log.Warn($"PLT konnte '{LotOwnerPrefabName}' nicht anmelden.");
                    return false;
                }
                _lotOwnerPrefab = _prefabSystem.GetEntity(lot);
                prefab = _lotOwnerPrefab;
                Mod.log.Info($"PLT hat '{LotOwnerPrefabName}' angemeldet. "
                    + "Benutzt wird es erst, wenn CS2 seinen Archetyp gebaut "
                    + "hat - frühestens im nächsten Frame.");
                // NICHT in diesem Frame benutzen. Siehe die Absturzbegruendung
                // an LotOwnerPrefabReady.
                return false;
            }
            catch (Exception exception)
            {
                _lotOwnerPrefabFailed = true;
                Mod.log.Error(exception,
                    $"PLT konnte '{LotOwnerPrefabName}' nicht anlegen.");
                return false;
            }
        }

        /**
         * Legt die Besitzerflaeche ueber das gezeichnete Polygon.
         *
         * Bewusst das SITE-Polygon und nicht die Huelle der erzeugten
         * Flaechen: anklickbar sein soll genau das, was der Nutzer gezogen
         * hat, samt Gruenrand.
         */
        private int CreateLotOwnerDefinition(ref TerrainHeightData heightData)
        {
            if (!UseLotAreaOwner) return 0;
            if (_points.Count < 3) return 0;
            if (!TryResolveLotOwnerPrefab(out var prefab)) return 0;

            var polygon = new float2[_points.Count];
            for (var i = 0; i < _points.Count; i++) polygon[i] = _points[i];
            return CreateAreaPreviewDefinition(LotOwnerRecordKind, 0, polygon,
                prefab, ref heightData) ? 1 : 0;
        }
    }
}
