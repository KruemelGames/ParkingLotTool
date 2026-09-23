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
        private bool LotOwnerPrefabReady(Entity prefab)
        {
            if (LotOwnerPrefabState(prefab) != null) return false;
            MeldeLotOhneRaeumung(prefab);
            return true;
        }

        /*
         * LOT BLEIBT OHNE RAEUMFLAG.
         *
         * Nutzerbefund 2026-09-16: mit CanOverrideObjects am ganzen Lot
         * verschwanden bei 3 von 3 Stadionversuchen die Requisiten auf der
         * Zoningflaeche. OverrideSystem.AreaIterator (Game.dll:1307-1323)
         * nimmt nur Objekte mit derselben Besitzerwurzel aus. Seit 23.09
         * raeumen deshalb eigene Gras- und Asphaltklone, nie das Lot.
         */
        private bool _lotRaeumungGemeldet;

        private void MeldeLotOhneRaeumung(Entity prefab)
        {
            if (_lotRaeumungGemeldet) return;
            _lotRaeumungGemeldet = true;
            var geometry = EntityManager.GetComponentData<AreaGeometryData>(prefab);
            if ((geometry.m_Flags & Game.Areas.GeometryFlags.CanOverrideObjects)
                != 0)
            {
                geometry.m_Flags &= ~Game.Areas.GeometryFlags.CanOverrideObjects;
                EntityManager.SetComponentData(prefab, geometry);
                Mod.log.Warn("PLT-Lot: unerwartetes CanOverrideObjects entfernt. "
                    + "Nur Gras- und Asphaltklone duerfen raeumen.");
            }
            Mod.log.Info("PLT-Lot raeumt keine Objekte; eigene Gras- und "
                + "Asphaltklone raeumen ausserhalb der Zoningparzellen.");
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
