using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Game;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Jobs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem : ToolBaseSystem
    {
        public const string ToolId = "ParkingLotTool";

        private const int MinPolygonPoints = 3;
        private const float CloseDistance = 12f;
        private const float PointHitDistance = 8f;
        private const float PointMoveEpsilon = 0.01f;

        private readonly List<float2> _points = new List<float2>();
        private readonly List<float3> _worldPoints = new List<float3>();

        private OverlayRenderSystem _overlayRenderSystem;
        private TerrainSystem _terrainSystem;
        private ParkingLotOverlay _overlay;

        private bool _closed;
        private bool _hasHover;
        private float3 _hoverPosition;
        /** Die letzte Stelle im Gelaende, auf die der Zeiger wirklich zeigte. */
        private float3 _letzteWeltposition;
        private bool _letzteWeltpositionGueltig;
        /**
         * Hat der Nutzer das GESCHLOSSENE Polygon schon angefasst?
         *
         * Ansage vom 2026-08-20: solange er nur geschlossen hat, soll der
         * Rechtsklick wie eh und je zurueckgehen - das ist der gewohnte
         * Griff, wenn die Form gleich falsch war. Erst wenn er anfaengt zu
         * bearbeiten, also einen Punkt oder eine Kante verschiebt oder
         * loescht, wird der Rechtsklick zum Loeschwerkzeug und loest nichts
         * mehr auf. Sonst zerlegt ein Griff die halbe Arbeit.
         */
        private bool _polygonTouched;

        private int _hoverPoint = -1;
        private int _dragPoint = -1;
        private float2 _dragStartPoint;
        private float3 _dragStartWorld;

        private ParkingLotUISystem _uiSystem;
        private int _seenSettingsRevision;
        private int _geometryRevision;
        private int _buildRevision;
        private bool _layoutDirty;
        private Task<ParkingLayout> _buildTask;
        private LayoutSettings _buildSettings;
        private float2[] _buildSite;
        private float3[] _buildWorldSite;
        private DateTime _buildStartedUtc;
        private long _buildStartedTimestamp;

        public override string toolID => ToolId;

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _overlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _terrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            _uiSystem = World.GetOrCreateSystemManaged<ParkingLotUISystem>();
            InitialisiereSonde();
            InitialisiereZoningsonde();
            InitialisiereZoningSeiten();
            InitialisiereZoningSeitenSpeicher();
            _overlay = new ParkingLotOverlay();
            InitializeAreaPreview();
            InitialisiereMeldewahl();
            InitializeNetBuilder();
            InitializeBayObjects();
            InitializeEntranceArrows();
            InitializeLotOwner();
            InitializeTerrainDiagnostics();
            InitializeLotArea();
            InitializeAreaPrefabSurvey();
            InitializePrefabDissect();
            InitializeDebugDump();
            InitializeSnapping();
            InitializeChargerDiagnostics();
            InitializeInspector();
            InitializeEditing();
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Muss VOR allem anderen kommen: erst danach darf eine Aenderung
            // der Fangauswahl gespeichert werden.
            LadeFangauswahl();
            ResetSelection();
            InitializeRaycast();
            EnableToolActions();
            _uiSystem?.SetToolActive(true);
            Mod.log.Info("PLT aktiv: Linksklick setzt Punkte, Rechtsklick geht zurück, "
                + "Enter baut, ESC beendet.");
            Mod.log.Info("  Einrasten (Magnet-Panel der Werkzeugleiste): "
                + "Fahrbahnkante = oranger Ring, Gebäudekante = roter Ring, "
                + "Nachbargrundstück = grüner Ring, Achsenkreuz = violetter "
                + "Ring, Hilfslinie zu einer früheren Ecke = bernstein, "
                + "Zellenraster = türkis. Gestrichelt eingeblendet wird "
                + "jeweils die Linie, an der es hängt.");
            Mod.log.Info("  WEISSER Ring = zwei Bedingungen zugleich erfüllt, "
                + "z.B. am Bordstein UND rechtwinklig. Das ist die Ecke, die "
                + "ein echtes Rechteck ergibt.");
            Mod.log.Info("  Fehler melden: Alt+M schaltet den Markiermodus ein, "
                + "Linksklick markiert die Stelle (magenta), Rechtsklick nimmt "
                + "zurück, Alt+P schreibt 'ParkingLotTool-summary-*.txt' in den "
                + "Logs-Ordner.");
            // Ohne diese Zeile war nach einem Testlauf nicht zu sehen, ob der
            // Versuch ueberhaupt lief - im Log stand dazu gar nichts, und der
            // Abzug meldete hinterher nur das Ergebnis.
            Mod.log.Info("  Lot-Fläche als Besitzer: "
                + (UseLotAreaOwner ? "AN (Standard)" : "AUS")
                + " - nur damit ist der ganze Parkplatz anklickbar statt nur "
                + "die Buchten. Shift+P schaltet es ab.");
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            if (_avPhase == AvPhase.TempWarten || _avPhase == AvPhase.LotWarten)
                AvFehler("Werkzeug waehrend Anschlussvorbereitung verlassen");
            _pendingEditLot = Entity.Null;
            if (IsEditing)
                AbortEdit("Werkzeug verlassen",
                    "Bearbeitung abgebrochen; der alte Parkplatz wurde wiederhergestellt.",
                    "Edit cancelled; the old parking lot was restored.");
            _uiSystem?.SetToolActive(false);
            ResetSelection();
            // Ab hier schreibt nicht mehr der Nutzer am Magnet-Panel, sondern
            // hoechstens noch das Spiel. Was jetzt kommt, wird nicht gemerkt.
            SperreFangauswahl();
            base.OnStopRunning();
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround;
            m_ToolRaycastSystem.raycastFlags = (RaycastFlags)0;
            GetAvailableSnapMask(out m_SnapOnMask, out m_SnapOffMask);
        }

        private void EnableToolActions()
        {
            if (applyAction != null) applyAction.shouldBeEnabled = true;
            if (secondaryApplyAction != null) secondaryApplyAction.shouldBeEnabled = true;
            if (cancelAction != null) cancelAction.shouldBeEnabled = true;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var deps = base.OnUpdate(inputDeps);
            PollChargerAudit();
            // Laeuft auch, wenn das Werkzeug nach Enter nicht mehr aktiv ist -
            // gerade der Zustand NACH dem Bau ist die Frage.
            PruefeRichtungspfeile();
            /*
             * AUS DEMSELBEN GRUND WIE DIE PFEILE.
             *
             * Die Leitungsnachkontrolle stand zuerst in
             * `ProcessEditLifecycle` - also HINTER dem Ausstieg unten. Nach
             * dem Uebernehmen schaltet CS2 aber auf sein Auswahlwerkzeug um,
             * und damit lief sie genau in dem Moment nicht mehr, fuer den sie
             * gebaut ist: der Lauf am 2026-09-05 um 13:32 lieferte weder ein
             * "wieder angeknuepft" noch eine Warnung, sondern gar nichts.
             *
             * Eine Messung, die im entscheidenden Moment stumm bleibt, sieht
             * aus wie ein Befund.
             */
            PruefeVersorgungswiederherstellung();
            if (m_ToolSystem.activeTool != this)
            {
                PflegeAutoVersorgung();
                // Nach Speichern/Laden darf die reine Messtaste auch dann
                // arbeiten, wenn CS2 inzwischen sein Auswahlwerkzeug aktiviert
                // hat. Nur der BAU wartet weiterhin auf dieses Tool, weil er
                // dessen ApplyMode braucht.
                if (_zpRequest == ZpRequest.Measure
                    || _zpRequest == ZpRequest.Cleanup
                    || _zpPhase == ZpPhase.Aufraeumen)
                    PflegeZoningsonde();
                ProcessDebugDumpRequest(toolIsActive: false);
                return deps;
            }

            // Unveränderte Temp-Areas bleiben stehen. Nur eine neue Signatur oder
            // das Verlassen der Vorschau setzt weiter unten ApplyMode.Clear.
            applyMode = ApplyMode.None;
            // Apply darf nach der Versorgungspflege nicht auf None zurueckfallen.
            if ((_avPhase == AvPhase.TempWarten || _avPhase == AvPhase.ApplyWarten)
                && PflegeAutoVersorgung())
                return RenderOverlay(deps);
            // Reiner Debug-Pfad. Solange sein NetCourse materialisiert oder
            // gemessen wird, darf derselbe Frame keinen Parkplatzklick sehen.
            if (PflegeZoningsonde()) return RenderOverlay(deps);
            if (TryBeginPendingEdit()) return RenderOverlay(deps);
            if (ProcessEditLifecycle()) return RenderOverlay(deps);
            PollCompletedBuild();
            if (PflegeAutoVersorgung()) return RenderOverlay(deps);

            PollSettingsRevision();

            // Nach Enter gehoeren die zwei bis drei Materialisierungs-Frames
            // demselben bestaetigten Stand. Ohne diese Sperre koennte ein
            // Rechtsklick zwischen Definition und Apply ein anderes Polygon
            // unter die bereits erzeugten Temp-Entities legen.
            var worldInputAllowed = WorldInputAllowed()
                && _reiter != Werkzeugreiter.Liste
                && _buildStage == BuildStage.Idle;
            _hasHover = _reiter != Werkzeugreiter.Liste
                && (worldInputAllowed || _dragPoint >= 0 || _dragEdge >= 0
                          || _dragEntrance >= 0)
                && TryGetGroundPoint(out _hoverPosition);
            /**
             * DIE LETZTE GUELTIGE WELTPOSITION MERKEN.
             *
             * `_hoverPosition` steht auf 0,0,0, sobald der Zeiger die Welt
             * verlaesst - `TryGetGroundPoint` setzt seinen Ausgabewert am
             * Anfang auf `default`. Genau das passiert, wenn der Nutzer auf
             * einen Knopf im Panel klickt: der Zeiger ist dann auf dem Knopf,
             * nicht auf dem Boden.
             *
             * Der Sondenlauf am 2026-08-24 ist daran gescheitert. Mein
             * Bedienhinweis "zeige auf eine Stelle, dann starten" war gar
             * nicht ausfuehrbar - zum Klicken muss der Zeiger den Boden
             * verlassen. Deshalb wird die letzte GUELTIGE Position gemerkt.
             */
            if (_hasHover && math.all(math.isfinite(_hoverPosition)))
            {
                _letzteWeltposition = _hoverPosition;
                _letzteWeltpositionGueltig = true;
            }
            UpdatePointHover();

            var secondaryPressed = secondaryApplyAction != null
                && secondaryApplyAction.WasPressedThisFrame();
            var escapePressed = cancelAction != null
                && cancelAction.WasPressedThisFrame() && !secondaryPressed;

            /*
             * DIE LINIENAUSWAHL KOMMT VOR ALLEM ANDEREN.
             *
             * Erster Anlauf: sie stand NACH `UpdateEntranceInteraction`.
             * Damit hatte der Zugangspfad denselben Klick schon verbraucht -
             * der Nutzer meldete, er koenne "weiterhin Linien verschieben
             * oder Strassen setzen anstatt die Linie auszuwaehlen". Der
             * Hover wird vorher gebraucht (er sagt, WELCHE Kante), alles
             * danach nicht.
             */
            /*
             * DIE PARKPLATZWAHL ZUM MELDEN STEHT GANZ VORN.
             *
             * Sie laeuft nur, wenn der Nutzer sie ausdruecklich
             * eingeschaltet hat, und dann will er in diesem Moment
             * nichts anderes. Weiter unten haette derselbe Klick
             * schon einen Punkt gesetzt - derselbe Fallstrick, an dem
             * die Linienauswahl einmal gehangen hat.
             */
            PflegeMeldeLotWahl();
            if (HandleMeldeLotWahl(secondaryPressed, escapePressed))
                return RenderOverlay(deps);
            if (HandleAusrichtWahl(secondaryPressed, escapePressed))
            {
                /*
                 * DIE VORSCHAU MUSS AUCH HIER NACHZIEHEN.
                 *
                 * `StartBuildIfNeeded` steht weit unten - hinter diesem
                 * Ruecksprung. Solange die Vorschau waehrend des Ausrichtens
                 * ausgeblendet war, fiel das nicht auf; seit sie laeuft, waere
                 * sie eingefroren: man waehlt eine Linie, die Buchten drehen
                 * sich erst, wenn man den Modus verlaesst. Genau das Sehen
                 * war aber der Grund, sie wieder einzuschalten.
                 *
                 * Der Aufruf prueft selbst, ob es etwas zu tun gibt
                 * (geschlossen, schmutzig, kein laufender Lauf); das Ergebnis
                 * holt `PollCompletedBuild` weiter oben ab, der VOR diesem
                 * Tor liegt.
                 */
                StartBuildIfNeeded();
                return RenderOverlay(deps);
            }

            /*
             * DER ZONING-MODUS KOMMT VOR DER ZUFAHRT UND VOR DEM POLYGON.
             *
             * Dieselbe Lehre wie bei der Linienauswahl eine Ebene hoeher:
             * wer den Klick zuerst braucht, muss zuerst gefragt werden.
             * Stuende er weiter unten, zoege derselbe Linksklick auch noch
             * eine Polygonecke herum - und der Nutzer meldete genau das
             * schon einmal fuer die Linienauswahl.
             */
            // Der Umriss kann sich seit dem letzten Bild geaendert haben -
            // durch Ziehen, Ausstuelpen oder Rueckgaengig. Erst pruefen,
            // dann die Klicks verteilen.
            PflegeZoningBlockmessung();
            PflegeZoningStrassenplan();
            ZoningFolgeDemUmriss();
            // Der unsichtbare Strassenklon wird bestellt, sobald eine Flaeche
            // steht - nicht erst beim Bauen. Sonst faehrt der erste Bau ohne
            // Zoning-Strasse, weil das Prefab noch einen Zyklus braucht.
            if (_zoningflaechen.Count != 0 || _randzoning.Count != 0)
                WaermeZoningstrasseVor(_buildSettings?.Zoningstrasse ?? "Alley");

            /*
             * DER SEITENSCHALTER GEHT VOR - er verbraucht denselben
             * Linksklick, mit dem sonst eine Flaeche gezogen wuerde.
             */
            /*
             * JEDER ZWEIG FRAGT DEN ZUSTAND, statt sich auf seine Stelle in
             * dieser Kette zu verlassen. Die Tabelle steht in
             * `Geometry/Werkzeugzustand.cs`, wo das Testprojekt sie prueft.
             */
            if (DarfArbeiten(Weltarbeit.Zoningseite)
                && HandleZoningSeiten(
                    applyAction != null && applyAction.WasPressedThisFrame(),
                    secondaryPressed,
                    escapePressed))
            {
                return RenderOverlay(deps);
            }

            if (DarfArbeiten(Weltarbeit.Zoningflaeche)
                && HandleZoning(
                    applyAction != null && applyAction.WasPressedThisFrame(),
                    applyAction != null && applyAction.IsPressed(),
                    secondaryPressed,
                    escapePressed))
            {
                StartBuildIfNeeded();
                return RenderOverlay(deps);
            }

            if (DarfArbeiten(Weltarbeit.Zugang)) UpdateEntranceInteraction();

            if (escapePressed)
            {
                DeactivateTool();
                return deps;
            }

            // ProxyActions verschwinden in CS2, solange Strg gehalten wird.
            // Deshalb dieselbe direkte Tastaturkante wie bei den Modifier-
            // Mausklicks. Ein laufender Zug ist noch kein fertiger Schritt.
            if ((InputManager.instance == null
                    || !InputManager.instance.hasInputFieldFocus)
                && DarfArbeiten(Weltarbeit.Rueckgaengig)
                && _dragPoint < 0 && _dragEdge < 0
                && _dragEntrance < 0 && _buildStage == BuildStage.Idle)
            {
                // Wiederherstellen zuerst pruefen: Strg+Umschalt+Z erfuellt
                // sonst auch die Bedingung des Rueckgaengig und wuerde
                // rueckwaerts statt vorwaerts laufen.
                if (RedoShortcutPressed())
                {
                    RedoLastStep();
                    return RenderOverlay(deps);
                }
                if (UndoShortcutPressed())
                {
                    UndoLastStep();
                    return RenderOverlay(deps);
                }
            }

            if (MarkerMode)
            {
                // Im Markiermodus gehoeren die Klicks den Fehlerstellen, nicht
                // dem Polygon. Sonst zoege ein Meldeklick die Ecken herum.
                if (worldInputAllowed && _hasHover
                    && DarfArbeiten(Weltarbeit.Meldemarke))
                {
                    if (secondaryPressed) RemoveLastMarker();
                    else if (applyAction != null && applyAction.WasPressedThisFrame())
                        AddMarker(_hoverPosition);
                }
            }
            else if (_dragEntrance >= 0 && DarfArbeiten(Weltarbeit.Zugang))
            {
                UpdateEntranceDrag(secondaryPressed);
            }
            else if (_dragEdge >= 0 && DarfArbeiten(Weltarbeit.Umriss)) UpdateEdgeDrag();
            else if (_dragPoint >= 0 && DarfArbeiten(Weltarbeit.Umriss))
            {
                UpdateDrag();
            }
            else if (worldInputAllowed)
            {
                if (secondaryPressed)
                {
                    if (DarfArbeiten(Weltarbeit.SchrittZurueck)) StepBack();
                }
                // Auch die Zufahrtsvorschau liest Shift direkt. Der dazu
                // gehoerende Klick darf deshalb nicht ueber die von CS2 bei
                // Modifiern maskierte ProxyAction laufen. Dieser Rohmauspfad
                // ist fuer alle vier Zufahrtsarten derselbe.
                else if (_closed && _entranceMode && EntranceShiftHeldForClick()
                         && Mouse.current != null
                         && Mouse.current.leftButton.wasPressedThisFrame)
                    HandleEntranceShiftLeftClick(applyAction != null
                        && applyAction.WasPressedThisFrame());
                /**
                 * DER KLICK MIT MODIFIER WIRD DIREKT AN DER MAUS GELESEN.
                 *
                 * `applyAction` ist eine `ProxyAction` des Spiels, und CS2
                 * maskiert die unmodifizierte Bindung, sobald ein Modifier
                 * gehalten wird - Strg+Klick erreichte das Werkzeug deshalb
                 * nie. Eine andere Taste haette daran nichts geaendert; es
                 * liegt am Modifier an sich, nicht an Strg.
                 *
                 * `Mouse.current` geht am Spielsystem vorbei und sieht den
                 * Knopf so, wie er wirklich gedrueckt wird. Eng begrenzt auf
                 * genau diesen Fall: nur wenn eine Kante unter dem Zeiger
                 * liegt UND ein Modifier gehalten wird.
                 */
                // Der Modifier wird HIER noch einmal frisch abgefragt, nicht
                // nur die gemerkte Fahne. Eine Fahne kann veralten, die Taste
                // nicht - und Einfuegen darf ausschliesslich mit Strg passieren.
                else if (_insertReady && _hoverEdge >= 0 && ModifierHeld()
                         && DarfArbeiten(Weltarbeit.Umriss)
                         && Mouse.current != null
                         && Mouse.current.leftButton.wasPressedThisFrame)
                    InsertPointOnHoveredEdge();
                // Alt wird von CS2 genauso maskiert wie Strg, also derselbe
                // direkte Weg an der Maus vorbei.
                else if (_hoverEdge >= 0 && _dragEdge < 0 && AltGehalten()
                         && DarfArbeiten(Weltarbeit.Umriss)
                         && Mouse.current != null
                         && Mouse.current.leftButton.wasPressedThisFrame)
                    BeginEdgeExtrude();
                // Shift ebenso: ohne diesen Zweig kann ein Zug mit gehaltenem
                // Shift gar nicht erst beginnen.
                else if (_hoverEdge >= 0 && _dragEdge < 0 && ShiftGehalten()
                         && DarfArbeiten(Weltarbeit.Umriss)
                         && Mouse.current != null
                         && Mouse.current.leftButton.wasPressedThisFrame)
                    BeginEdgeDrag();
                else if (applyAction != null && applyAction.WasPressedThisFrame())
                    HandleLeftClick();
            }

            // Eigener Rueckgabepfad: die Bauschritte setzen ApplyMode selbst,
            // und nichts danach darf ihn im selben Frame ueberschreiben.
            if (ProcessBuild()) return RenderOverlay(deps);

            WatchStuckPreview();
            StartBuildIfNeeded();
            PollLotOwnerPrefab();
            _uiSystem?.PflegeSprache();
            VeroeffentlicheFlaechenliste();
            _uiSystem?.PflegeTraegerstand();
            // NICHT mehr anlegen, nur noch melden. Das unsichtbare Prefab
            // entsteht in `OnGameLoadingComplete`; ein Prefab im laufenden
            // Frame anzulegen hat das Spiel am 2026-08-22 zweimal nativ
            // mitgerissen, ohne Stack und ohne Logzeile. Dass der Aufruf hier
            // stehenblieb, war ein Rest aus der Zeit davor - im Log vom
            // 2026-08-23 hat ihn OnGameLoadingComplete jedes Mal ueberholt.
            MeldeUnsichtbaresDecalBereit();
            // Der Sondenlauf ist ein Zustandsautomat mit einem Schritt je
            // Frame - siehe ParkingLotProbe.cs. Zwischen Setzen und Auswerten
            // muss CS2 rechnen duerfen.
            PflegeSondenlauf();
            if (DiagnoseRequested()) RequestDebugDump("Strg+Enter");
            ProcessDebugDumpRequest(toolIsActive: true);
            return RenderOverlay(deps);
        }

        /** Arealgroesse fuer die Kennzahl "Flaeche je Bucht". */
        private static double PolygonArea(float2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return 0;
            var sum = 0.0;
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                sum += (double)a.x * b.y - (double)b.x * a.y;
            }
            return Math.Abs(sum) / 2;
        }

        private static bool WorldInputAllowed()
        {
            var input = InputManager.instance;
            return input != null && input.controlOverWorld && !input.hasInputFieldFocus;
        }

        private bool TryGetGroundPoint(out float3 world)
        {
            world = default;
            if (!GetRaycastResult(out ControlPoint controlPoint)) return false;
            world = controlPoint.m_Position;
            if (!math.all(math.isfinite(world))) return false;
            // Einrasten NUR beim Setzen und Ziehen von Punkten, nicht wenn das
            // Polygon schon steht - dort waere es nur ein zappelnder Cursor.
            /*
             * WAEHREND EINES KANTENZUGS NICHT FANGEN.
             *
             * Der Zeigerfang misst gegen die vorhandene Geometrie - und dazu
             * gehoert das Polygon, das der Zug gerade bewegt. Kante wandert,
             * Fangziel wandert mit, Kante wandert wieder: eine Rueckkopplung.
             * Im Spiel sah das aus wie ein Schwingen bei stillstehendem
             * Zeiger, mit einem Drang nach innen. Der Kantenfang auf
             * Polygonpunkte rechnet stattdessen gegen den EINGEFRORENEN
             * Ausgangszustand und schwingt deshalb nicht.
             */
            if (!_closed || _dragPoint >= 0)
            {
                var snapped = ApplySnapping(world);
                if (math.all(math.isfinite(snapped))) world = snapped;
            }
            else ClearSnapFeedback();
            return true;
        }

        private void HandleLeftClick()
        {
            if (!_hasHover) return;

            if (_closed)
            {
                /*
                 * IM ZUGANGSMODUS GEHOERT DER KLICK DEN ZUGAENGEN. IMMER.
                 *
                 * Hier stand "ein Zufahrtsgriff gewinnt nur an seiner
                 * sichtbaren Scheibe; daneben bleiben die 8,0-m-Eckgriffe
                 * erreichbar - sonst waere 'Position bei Polygonaenderung
                 * behalten' ueber die Bedienung gar nicht pruefbar". Das war
                 * eine PRUEFHILFE, und sie ist zur Bedienfalle geworden:
                 *
                 *   *"Waehrend ich gerade Zugaenge baue, dann kann ich die
                 *   Polygonpunkte verschieben."* (2026-09-09)
                 *
                 * Kanten waren laengst gesperrt - `UpdateEdgeHover` meldet im
                 * Zugangsmodus gar keine -, Punkte nicht. Jetzt entscheidet
                 * der Zustand statt der Abstand. Die Eigenschaft, die die
                 * Prüfhilfe offenhalten sollte, gehoert in einen Testlauf.
                 */
                if (_entranceMode)
                {
                    if (DarfArbeiten(Weltarbeit.Zugang)) HandleEntranceLeftClick();
                    return;
                }
                if (_hoverPoint < 0)
                {
                    // Eine Kante unter dem Zeiger wird gezogen; im
                    // Zufahrtsmodus gehoert der Klick den Zufahrten, dort
                    // meldet UpdateEdgeHover schon gar keine Kante.
                    if (_hoverEdge >= 0)
                    {
                        // Strg macht aus dem Verschieben ein Einfuegen -
                        // ohne Strg wird IMMER verschoben.
                        if (_insertReady && ModifierHeld())
                        {
                            if (DarfArbeiten(Weltarbeit.Umriss))
                                InsertPointOnHoveredEdge();
                        }
                        else if (AltGehalten())
                        {
                            if (DarfArbeiten(Weltarbeit.Umriss))
                                BeginEdgeExtrude();
                        }
                        else if (DarfArbeiten(Weltarbeit.Umriss)) BeginEdgeDrag();
                        return;
                    }
                    return;
                }
                if (!DarfArbeiten(Weltarbeit.Umriss)) return;
                _pointDragUndo = CaptureUndoState();
                _dragPoint = _hoverPoint;
                _dragStartPoint = _points[_dragPoint];
                _dragStartWorld = _worldPoints[_dragPoint];
                CaptureEntrancesForPointDrag();
                // Auch die Bezugsachse muss zurueckgenommen werden koennen,
                // sonst rechnen die Nachbarpunkte nach einem verworfenen
                // Ziehen mit einer Achse, die es nicht mehr gibt.
                _dragStartAxis = GetSnapAxis(_dragPoint);
                return;
            }

            if (!DarfArbeiten(Weltarbeit.Umriss)) return;

            if (CanCloseAtCursor())
            {
                var before = CaptureUndoState();
                _closed = true;
                _polygonTouched = false;
                _layoutDirty = true;
                _hoverPoint = -1;
                // Ob es direkt in den Zufahrt-Modus geht, entscheidet der
                // Nutzer in den Modeinstellungen. Aus heisst: das Polygon
                // bleibt erst einmal bearbeitbar.
                if (Mod.Optionen == null || Mod.Optionen.AutomatischZufahrtModus)
                    BeginEntrancePlacement(showMissingPrompt: false);
                else _uiSystem?.SetStatus(T("Polygon bearbeiten.", "Editing polygon."));
                CommitUndoState(before, "Polygon geschlossen");
                Mod.log.Info($"PLT-Polygon geschlossen: {_points.Count} Punkte.");
                return;
            }

            var point = _hoverPosition.xz;
            if (_points.Count > 0
                && math.lengthsq(point - _points[_points.Count - 1]) < 0.0625f) return;

            var pointBefore = CaptureUndoState();
            _points.Add(point);
            _worldPoints.Add(_hoverPosition);
            // Woran dieser Punkt eingerastet ist, wird zur Bezugsachse des
            // naechsten - so entsteht aus "Punkt am Bordstein" ein
            // rechtwinkliger Parkplatz an der Strasse.
            StoreSnapAxis(_points.Count - 1);
            _geometryRevision++;
            CommitUndoState(pointBefore, "Punkt gesetzt");
        }

        private void UpdateDrag()
        {
            if (applyAction != null && applyAction.IsPressed())
            {
                if (!_hasHover) return;
                var next = _hoverPosition.xz;
                if (math.lengthsq(next - _points[_dragPoint]) < 0.0001f) return;
                // KEIN ZUG, DER EINE PARZELLE HINAUSSCHIEBT. Der Punkt
                // bleibt dann einfach stehen; der Umriss klemmt an dieser
                // Kante, statt die Flaeche zu verlieren.
                if (!ZoningVertraegtPunkt(_dragPoint, next)) return;
                _points[_dragPoint] = next;
                _worldPoints[_dragPoint] = _hoverPosition;
                StoreSnapAxis(_dragPoint);
                return;
            }

            var changed = math.lengthsq(_points[_dragPoint] - _dragStartPoint)
                > PointMoveEpsilon * PointMoveEpsilon;
            if (changed)
            {
                ReprojectEntrancesAfterPointDrag();
                _geometryRevision++;
                _layoutDirty = true;
                _polygonTouched = true;
                CommitUndoState(_pointDragUndo, "Punkt verschoben");
            }
            else
            {
                _points[_dragPoint] = _dragStartPoint;
                _worldPoints[_dragPoint] = _dragStartWorld;
                RestoreSnapAxis(_dragPoint, _dragStartAxis);
            }
            _entrancePositionsBeforePointDrag = null;
            _pointDragUndo = null;
            _dragPoint = -1;
        }

        private void StepBack()
        {
            if (StepBackEntrance()) return;

            /**
             * AM GESCHLOSSENEN POLYGON LOESCHT RECHTSKLICK, ER LOEST NICHT AUF.
             *
             * Hier stand: Polygon wieder oeffnen. Wer einen Punkt loeschen
             * wollte - der naheliegende Griff - zerlegte damit seine ganze
             * Arbeit. Ansage des Nutzers am 2026-08-20: Rechtsklick loescht,
             * was unter dem Zeiger liegt, und sonst gar nichts.
             *
             * Der Zufahrt-Modus bleibt unberuehrt: dort faengt
             * `StepBackEntrance` den Klick schon oben ab, und genau darueber
             * verlaesst der Nutzer den Modus.
             */
            if (_closed && _polygonTouched)
            {
                DeleteUnderCursor();
                return;
            }

            var before = _points.Count > 0 ? CaptureUndoState() : null;
            var reopened = _closed;
            if (_closed)
            {
                _closed = false;
                ResetEntranceEditing(clearEntrances: false);
                _layoutDirty = false;
                _overlay.ClearLayout();
                ClearAreaPreviewLayout("polygon reopened");
            }

            /**
             * RECHTSKLICK BEENDET DAS WERKZEUG NIE.
             *
             * Hier stand zweimal `DeactivateTool()`: einmal bei leerem Polygon
             * und einmal, nachdem die letzte Ecke zurueckgenommen wurde. Wer
             * einmal zu oft zurueckklickte, stand ohne Fenster da und musste
             * das Werkzeug neu aufrufen, um wieder die erste Ecke setzen zu
             * koennen. Nutzeransage vom 2026-08-12: das Fenster bleibt offen,
             * die erste Ecke ist immer wieder setzbar, egal wie oft man
             * rechtsklickt. Schliessen tut ausschliesslich ESC.
             */
            if (_points.Count == 0) return;

            DropEntrancesAtRemovedLastPoint(_points.Count);
            _points.RemoveAt(_points.Count - 1);
            _worldPoints.RemoveAt(_worldPoints.Count - 1);
            _geometryRevision++;
            _hoverPoint = -1;
            CommitUndoState(before, reopened
                ? "Polygon geöffnet und letzter Punkt entfernt"
                : "Punkt entfernt");
        }

        /**
         * Loescht genau das, was unter dem Zeiger liegt - Punkt oder Kante.
         *
         * Liegt nichts darunter, passiert nichts. Das ist Absicht: ein
         * Rechtsklick ins Leere soll folgenlos sein, nicht die Form
         * verkleinern.
         */
        private void DeleteUnderCursor()
        {
            if (_hoverPoint < 0 && _hoverEdge < 0) return;
            var before = CaptureUndoState();
            var deletedPoint = _hoverPoint >= 0;
            _polygonTouched = true;
            if (deletedPoint) RemovePolygonPoint(_hoverPoint);
            else RemoveEdgePoints(_hoverEdge);
            CommitUndoState(before, deletedPoint
                ? "Polygonpunkt gelöscht" : "Polygonkante gelöscht");
        }

        /**
         * Eine KANTE loeschen heisst: BEIDE Punkte an ihren Enden fallen weg.
         * Die Nachbarn ruecken zusammen und werden direkt verbunden.
         *
         * Bis zum 2026-08-31 entstand hier stattdessen ein Punkt in der Mitte
         * der Kante. Die Ueberlegung war, die Form der alten so nah wie
         * moeglich zu halten - fuers Zeichnen ist das aber genau falsch: man
         * loescht eine Kante, um sie loszuwerden, und bekam einen neuen Punkt
         * dafuer. Der Nutzer hat das ausdruecklich anders entschieden.
         *
         * Die dritte Moeglichkeit - die Nachbarkanten bis zu ihrem
         * Schnittpunkt verlaengern - bleibt aus dem alten Grund draussen: bei
         * fast parallelen Nachbarn sprengt sie die Form ins Unendliche.
         *
         * Bleiben unter drei Punkte uebrig, faengt `NachDemLoeschen` das ab
         * und geht zurueck ins Zeichnen.
         */
        private void RemoveEdgePoints(int edge)
        {
            var a = edge;
            var b = (edge + 1) % _points.Count;
            CaptureEntrancesForPointDrag();
            // Den hoeheren Index zuerst entfernen, sonst rutscht der zweite
            // um eine Stelle weg. Bei der letzten Kante ist b = 0.
            var zuerst = math.max(a, b);
            var danach = math.min(a, b);
            EntfernePunkt(zuerst);
            EntfernePunkt(danach);
            NachDemLoeschen();
        }

        private void RemovePolygonPoint(int index)
        {
            CaptureEntrancesForPointDrag();
            EntfernePunkt(index);
            NachDemLoeschen();
        }

        private void EntfernePunkt(int index)
        {
            _points.RemoveAt(index);
            _worldPoints.RemoveAt(index);
            if (index < _pointAxes.Count) _pointAxes.RemoveAt(index);
        }

        /**
         * Unter drei Punkten gibt es kein Polygon mehr - dann geht es zurueck
         * ins Zeichnen, mit dem was uebrig ist. Ist gar nichts mehr da, steht
         * das Werkzeug wieder wie am Anfang.
         */
        private void NachDemLoeschen()
        {
            _hoverPoint = -1;
            _hoverEdge = -1;
            _geometryRevision++;

            if (_points.Count < MinPolygonPoints)
            {
                _closed = false;
                ResetEntranceEditing(clearEntrances: true);
                _layoutDirty = false;
                _overlay.ClearLayout();
                ClearAreaPreviewLayout("polygon unter drei Punkten");
                _entrancePositionsBeforePointDrag = null;
                _uiSystem?.SetStatus(_points.Count == 0
                    ? T("Linksklick setzt Punkte.", "Left click places points.")
                    : T("Weiter zeichnen: Linksklick setzt Punkte.",
                        "Keep drawing: left click places points."));
                return;
            }

            ReprojectEntrancesAfterPointDrag();
            _entrancePositionsBeforePointDrag = null;
            _layoutDirty = true;
        }

        private void UpdatePointHover()
        {
            _hoverPoint = -1;
            /*
             * IM AUSRICHTMODUS GIBT ES NUR AUSWAEHLEN.
             *
             * Der Klick war schon verbraucht - `HandleAusrichtWahl` steht vor
             * allem anderen und laesst nichts durch. Die ANZEIGE lief aber
             * weiter: Punkte leuchteten als Anfasser auf, die Kante bot ihren
             * Einfuegepunkt an, Alt und Umschalt zeigten Ausstuelpen und
             * Verschieben. Das sieht aus wie ein Angebot, ist aber keins -
             * und Umschalt hat hier seit heute eine eigene Bedeutung.
             *
             * Ansage des Nutzers: *"Wenn ich im Aligned Modus arbeite, also
             * wirklich nur Auswaehlen von Flaeche und Linie, sollte
             * unterbunden werden, dass ich Linien verschiebe usw."*
             *
             * Die Kante bleibt als EINZIGE stehen, und auch nur im
             * Linienschritt: sie ist dort das, was man anklicken soll.
             */
            if (AusrichtWahlAktiv)
            {
                _hoverEdge = -1;
                if (Ausrichtwahl == Ausrichtschritt.Linie) UpdateEdgeHover();
                // Im Trennmodus sind die PUNKTE das Ziel - dort muss man
                // sehen, welchen man gerade trifft. Ziehen geht trotzdem
                // nicht: der Klick ist vorher verbraucht.
                else if (Ausrichtwahl == Ausrichtschritt.Trennen
                    && _hasHover) _hoverPoint = PunktUnterZeiger();
                // `UpdateEdgeHover` setzt die Fahne nach dem Modifier; hier
                // darf sie nie stehen, sonst legte Strg+Klick einen Punkt an.
                _insertReady = false;
                return;
            }
            if (!_closed || !_hasHover || _dragPoint >= 0) return;

            var cursor = _hoverPosition.xz;
            var bestSquared = PointHitDistance * PointHitDistance;
            for (var i = 0; i < _points.Count; i++)
            {
                var distanceSquared = math.lengthsq(cursor - _points[i]);
                if (distanceSquared > bestSquared) continue;
                bestSquared = distanceSquared;
                _hoverPoint = i;
            }

            // Erst jetzt, denn ein getroffener Punkt schlaegt die Kante.
            UpdateEdgeHover();
        }

        private bool CanCloseAtCursor()
        {
            return !_closed && _hasHover && _points.Count >= MinPolygonPoints
                && math.lengthsq(_hoverPosition.xz - _points[0])
                    <= CloseDistance * CloseDistance;
        }

        /**
         * Ein Reglerdreh im Panel macht die Vorschau ungueltig.
         *
         * Verglichen wird die Revision, nicht der Einstellungsinhalt: das
         * UI-System zaehlt sie bei jeder echten Aenderung hoch, und so muss
         * hier niemand wissen, welche Felder es ueberhaupt gibt.
         */
        private void PollSettingsRevision()
        {
            if (_uiSystem == null) return;
            if (_uiSystem.Revision == _seenSettingsRevision) return;
            _seenSettingsRevision = _uiSystem.Revision;
            if (RevalidateEntrancesAfterSettingsChange()) _geometryRevision++;
            if (_closed) _layoutDirty = true;
        }

        private void StartBuildIfNeeded()
        {
            if (!_closed || !_layoutDirty || _dragPoint >= 0 || _dragEntrance >= 0
                || _buildTask != null) return;

            var site = new float2[_points.Count];
            for (var i = 0; i < _points.Count; i++) site[i] = _points[i];

            // Die Bezugslinien folgen dem Polygon: hat sich ein Punkt bewegt,
            // gilt der NEUE Winkel der Linie. Muss vor dem Lesen der
            // Einstellungen laufen, sonst baut der Lauf mit dem alten.
            AktualisiereAusrichtungen();
            _buildSettings = WithManualEntrances(_uiSystem != null
                ? _uiSystem.CurrentSettings() : LayoutSettings.Cs2);
            _buildSettings.Teilflaechenschnitte = _trennschnitte
                .Select(schnitt => new Teilflaechenschnitt
                {
                    A = schnitt.A,
                    B = schnitt.B,
                }).ToArray();
            // Die Baulandflaechen gehen denselben Weg wie die Schnitte: sie
            // gehoeren zum Entwurf, nicht zu den Reglern.
            _buildSettings.Zoningflaechen = _zoningflaechen
                .Select(f =>
                {
                    var kopie = f.Clone();
                    // Der Rand gilt fuer den ganzen Parkplatz und kann sich
                    // seit dem Setzen geaendert haben.
                    kopie.Rand = ZoningRand;
                    return kopie;
                }).ToArray();
            // Und die Randzoning-Linien auf demselben Weg - sie gehoeren
            // genauso zum Entwurf wie die Flaechen.
            _buildSettings.Randzoning = _randzoning
                .Select(l => l.Clone()).ToArray();
            _buildSettings.TeilflaechenAusrichtungen = _ausrichtungen
                .Select(zuweisung => new TeilflaechenAusrichtung
                {
                    Anker = zuweisung.Anker,
                    Winkel = zuweisung.Winkel,
                }).ToArray();
            _buildRevision = _geometryRevision;
            _layoutDirty = false;
            BeginPreviewDiagnostics();
            _buildSite = site;
            _buildWorldSite = new float3[_worldPoints.Count];
            for (var i = 0; i < _worldPoints.Count; i++)
                _buildWorldSite[i] = _worldPoints[i];
            _buildStartedUtc = DateTime.UtcNow;
            _buildStartedTimestamp = Stopwatch.GetTimestamp();
            // Die Startzeile nennt genau das, was den Lauf teuer machen kann:
            // wie viele Ecken, welcher Winkelmodus und - der Verdacht des
            // Nutzers - wie viele eigene Ausrichtungen im Spiel sind.
            ParkingLotLiveLog.Zeile("bau start | punkte " + site.Length
                + " | modus " + (_buildSettings.AngleMode ?? "auto")
                + " | bezug " + (_buildSettings.Ausrichtwinkel.HasValue
                    ? ParkingLotLiveLog.Zahl(_buildSettings.Ausrichtwinkel.Value)
                    : "-")
                + " | ausrichtungen "
                + _buildSettings.TeilflaechenAusrichtungen.Length
                + " | schnitte " + _buildSettings.Teilflaechenschnitte.Length
                + " | stand " + _buildRevision);
            var settings = _buildSettings;
            // Ohne diese Zeile schweigt das Log, solange gerechnet wird. Genau
            // das machte einen Vorfall unlesbar: der Nutzer meldete "nichts
            // gebaut", und im Log stand zwischen "Polygon geschlossen" und dem
            // Debug-Abzug keine einzige Zeile - laufender Lauf, verworfener
            // Lauf und nie gestarteter Lauf sahen identisch aus.
            // MIT KOORDINATEN. Ohne sie war der Haenger vom 2026-08-12 23:12
            // nicht nachrechenbar: der Bau kam nie zustande, stand also auch
            // nicht im Bauprotokoll, und die Startzeile nannte nur "5 Punkte".
            Mod.log.Info($"PLT-Vorschau gestartet: {site.Length} Punkte, "
                + $"Stand {_buildRevision}. Polygon: "
                + string.Join(" | ", site.Select(p =>
                    $"{p.x.ToString("0.#####", CultureInfo.InvariantCulture)} / "
                    + $"{p.y.ToString("0.#####", CultureInfo.InvariantCulture)}")));
            _buildTask = Task.Run(() => ParkingGeometry.Build(site, settings));
            // Der Nutzer konnte bis zum 2026-08-21 nicht unterscheiden, ob
            // gerechnet wird oder ob sich der Generator weigert - im Panel
            // stand in beiden Faellen noch das Ergebnis von vorher.
            _uiSystem?.SetStatus(T("Vorschau wird berechnet …", "Calculating preview …"));
        }

        /**
         * Wie lange ein Vorschaulauf dauern darf.
         *
         * NACHGEMESSEN, nachdem der Nutzer "etwas langsamer geworden" meldete:
         * an identischen Eingaben ist nichts langsamer (329->324, 1905->1876,
         * 443->439, 954->840, 689->686 ms). Die Rechenzeit haengt an der FORM -
         * im Log des Nutzers stehen 1055, 1406, 2474, 9197 und 9851 ms bei
         * fast gleicher Buchtenzahl. Die Layoutsuche probiert je nach Polygon
         * verschieden viele Varianten durch.
         *
         * Damit war das erste Zeitlimit von 20 s zu knapp: nur das Doppelte
         * des langsamsten GUELTIGEN Laufs. Eine sperrigere Form haette eine
         * korrekte Vorschau abgewuergt. Jetzt 45 s zum Abbrechen, dazu eine
         * Warnung ab 10 s - so werden langsame, aber gueltige Laeufe sichtbar,
         * ohne dass sie sterben.
         */
        private const double PreviewSlowSeconds = 10;
        private const double PreviewTimeoutSeconds = 45;
        private bool _previewSlowLogged;
        private bool _previewTimeoutLogged;

        /**
         * Ein haengender Lauf darf das Werkzeug NICHT dauerhaft blockieren.
         *
         * Am 2026-08-12 blieb `ParkingGeometry.Build` bei einer Form haengen.
         * `StartBuildIfNeeded` steigt bei `_buildTask != null` sofort aus, also
         * startete danach KEINE Vorschau mehr - fuer den Rest der Sitzung.
         * Im Panel stand endlos "Vorschau wird berechnet.", das Log schwieg,
         * und der Nutzer hielt seine Form fuer unbaubar.
         *
         * Der Lauf selbst laesst sich nicht abbrechen (`Task.Run` ohne
         * Abbruchtoken, und die Geometrie fragt keines ab). Wir lassen ihn
         * deshalb laufen und HAENGEN IHN AB: `_buildTask` wird freigegeben,
         * damit die naechste Vorschau starten kann. Sein Ergebnis interessiert
         * niemanden mehr - der Stand ist dann ohnehin veraltet.
         */
        private void WatchStuckPreview()
        {
            if (_buildTask == null || _buildTask.IsCompleted)
            {
                _previewTimeoutLogged = false;
                _previewSlowLogged = false;
                return;
            }
            var seconds = (Stopwatch.GetTimestamp() - _buildStartedTimestamp)
                / (double)Stopwatch.Frequency;
            if (seconds >= PreviewSlowSeconds && !_previewSlowLogged)
            {
                _previewSlowLogged = true;
                Mod.log.Info($"PLT-Vorschau rechnet seit {seconds:F0} s an Stand "
                    + $"{_buildRevision}. Noch kein Abbruch - gueltige Laeufe "
                    + "brauchen gemessen bis zu 10 s.");
            }
            if (seconds < PreviewTimeoutSeconds || _previewTimeoutLogged) return;

            _previewTimeoutLogged = true;
            var site = _buildSite;
            Mod.log.Warn($"PLT-Vorschau haengt: Stand {_buildRevision} rechnet seit "
                + $"{seconds:F0} s und wird aufgegeben, damit das Werkzeug wieder "
                + "reagiert. Die Form terminiert im Generator nicht - bitte melden. "
                + "Polygon: " + (site == null ? "unbekannt" : string.Join(" | ",
                    site.Select(p =>
                        $"{p.x.ToString("0.#####", CultureInfo.InvariantCulture)} / "
                        + $"{p.y.ToString("0.#####", CultureInfo.InvariantCulture)}"))));
            _buildTask = null;
            _buildSettings = null;
            _buildSite = null;
            _buildWorldSite = null;
            _layoutDirty = false;
            if (IsEditing && _buildRequestedWhenReady)
            {
                AbortEdit("Vorschaurechnung des Neubaus lief in die Zeitgrenze",
                    "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                    "Rebuild failed; the old parking lot was restored.");
                return;
            }
            _uiSystem?.SetStatus(T(
                "Vorschau abgebrochen: diese Form bringt den Generator zum "
                + "Haengen. Ecke verschieben und neu versuchen.",
                "Preview aborted: this shape makes the generator hang. Move a "
                + "corner and try again."));
        }

        private void PollCompletedBuild()
        {
            if (_buildTask == null || !_buildTask.IsCompleted) return;

            var completed = _buildTask;
            var revision = _buildRevision;
            var settings = _buildSettings;
            var site = _buildSite;
            var worldSite = _buildWorldSite;
            var startedUtc = _buildStartedUtc;
            var elapsedMilliseconds = (Stopwatch.GetTimestamp() - _buildStartedTimestamp)
                * 1000d / Stopwatch.Frequency;
            _buildTask = null;
            _buildSettings = null;
            _buildSite = null;
            _buildWorldSite = null;

            try
            {
                var layout = completed.GetAwaiter().GetResult();
                if (!_closed || revision != _geometryRevision)
                {
                    var reason = !_closed ? "Polygon wieder geoeffnet"
                        : $"Stand inzwischen {_geometryRevision}";
                    RecordPreviewDiagnostic("Info",
                        $"Vorschaulauf zu Stand {revision} verworfen: {reason}.", null);
                    Mod.log.Info($"PLT-Vorschau zu Stand {revision} verworfen "
                        + $"nach {elapsedMilliseconds:F0} ms: {reason}.");
                    return;
                }
                if (!AcceptBuiltEntrances(layout, settings))
                {
                    _overlay.ClearLayout();
                    ClearAreaPreviewLayout("Zufahrtsabstand nach Kernkorrektur");
                    _uiSystem?.SetStatus(T("Zufahrtsabstand wird neu berechnet.", "Recalculating entrance distance."));
                    return;
                }
                // Erst hier, nicht im Geometrielauf: die Vorflaeche misst die
                // Stadtstrasse, und der Lauf steckt in einem Hintergrundtask
                // ohne Zugriff auf den EntityManager.
                ParkingLotLiveLog.Zeile("bau fertig "
                    + ParkingLotLiveLog.Zahl(elapsedMilliseconds, 0)
                    + " ms | buchten " + layout.Stalls
                    + " | gras " + layout.GrassSurface.Length
                    + " | asphalt " + layout.AsphaltSurface.Length
                    + " | warnungen " + layout.Warnings.Length);
                ErgaenzeVorflaechen(layout, settings, site);
                RememberCompletedPreview(site, worldSite, settings, layout,
                    revision, startedUtc, DateTime.UtcNow, elapsedMilliseconds);
                _overlay.SetLayout(layout, settings, _terrainSystem,
                    _vorflaechenSicht, _vorflaechenArt, FuellungAlsNetz);
                FuettereFlaechennetz(layout);
                SetVegetationPreview(layout);
                SetAreaPreviewLayout(layout, settings);
                CaptureEditBaselineIfNeeded(layout);
                _uiSystem?.ShowResult(layout, PolygonArea(site));
                _uiSystem?.SetStatus(T($"{layout.Stalls} Stellplätze berechnet.", $"{layout.Stalls} stalls calculated."));
                /*
                 * Nicht jede Warnung ist eine Nachricht an den Nutzer. Zwei
                 * Meldungen ueber weggelassene Nullflaechen standen am
                 * 2026-09-08 nach einem GELUNGENEN Bau als rote Fehler in der
                 * Statusleiste. Sie bleiben im Bauzettel; nur die Anzeige
                 * filtert. Begruendung und Schwelle in `Hinweisfilter`.
                 */
                _uiSystem?.SetHinweise(
                    ParkingLotTool.Geometry.Hinweisfilter.Sichtbare(
                        layout.Warnings));
                // Derselbe Lauf, andere Leserschaft: die Statusleiste filtert,
                // der Meldereiter nicht. Siehe `SetzeBaubefund`.
                _uiSystem?.SetzeBaubefund(layout, PolygonArea(site));
                // MIT DEN ECHTEN ZAHLEN AUS DEM SPIEL. Ein CPU-Profil des
                // Prototyps unter node sagt wenig ueber Mono; diese Zeile misst
                // dort, wo es zaehlt.
                var rufe = ParkingGeometry.OverlapCalls;
                var raus = ParkingGeometry.OverlapRejectedByBounds;
                // Fuer die Statuszeile beim Ausrichten: dort wartet man
                // darauf, und ohne Zahl sieht Warten wie Stillstand aus.
                _letzteVorschaudauerMs = elapsedMilliseconds;
                Mod.log.Info($"PLT-Vorschau berechnet: {layout.Stalls} Buchten in "
                    + $"{elapsedMilliseconds:F0} ms. Ueberschneidungstests: "
                    + $"{rufe:N0}, davon {raus:N0} per Rechteckvergleich verworfen"
                    + (rufe > 0 ? $" ({100d * raus / rufe:F1} %)" : "")
                    + $" | {(rufe > 0 ? elapsedMilliseconds * 1000 / rufe : 0):F2} us je "
                    + "Test (Gesamtzeit geteilt durch Aufrufe, KEINE Messung der "
                    + "Funktion selbst).");
                Mod.log.Info("PLT-Teilflaechen: "
                    + string.Join("; ", (layout.Teilflaechen
                            ?? Array.Empty<TeilflaechenBauInfo>())
                        .Select(teil => $"Teil {teil.Index}: Winkel "
                            + teil.Winkel.ToString("F2", CultureInfo.InvariantCulture)
                            + $" Grad, {teil.Innenbuchten} Innenbuchten"
                            + (teil.EigeneZuweisung ? " (eigene Zuweisung)" : "")))
                    + "; Verbindungsstrasse "
                    + (layout.TeilflaechenVerbindungen > 0
                        ? $"ja ({layout.TeilflaechenVerbindungen})" : "nein"));
                LogSurfaceHealth(layout);
            }
            catch (Exception exception)
            {
                if (revision == _geometryRevision)
                {
                    _layoutDirty = false;
                    _overlay.ClearLayout();
                    ClearAreaPreviewLayout("layout build failed");
                }
                _uiSystem?.SetHinweise(null);
                RecordPreviewDiagnostic("Error",
                    "PLT-Vorschau konnte nicht berechnet werden.", exception);
                Mod.log.Error(exception, "PLT-Vorschau konnte nicht berechnet werden.");
                // OHNE DIESE ZEILE SIEHT DER NUTZER NICHTS. Der Abbruch stand
                // nur im Log; im Panel blieb die alte Meldung stehen, und die
                // Vorschau verschwand kommentarlos.
                if (revision == _geometryRevision)
                {
                    if (IsEditing && _buildRequestedWhenReady)
                    {
                        AbortEdit("Vorschaurechnung des Neubaus ist fehlgeschlagen",
                            "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                            "Rebuild failed; the old parking lot was restored.");
                        return;
                    }
                    /*
                     * GEMESSEN AM 2026-08-26 an einem Nutzerfall: die Vorschau
                     * brach dreimal ab mit
                     *
                     *   Materialreparatur ueberschreitet 7,0 s in SlabFill
                     *   mit 1627 Grenzen; die Flaechen bleiben unrepariert.
                     *
                     * Das ist die bekannte Schwaeche des ALTEN Rechenwegs, und
                     * der Ausweg ist ein Klick - nur stand er nirgends. Der
                     * Nutzer las rohen Ausnahmetext und konnte damit nichts
                     * anfangen. Der Hinweis gehoert genau hierhin.
                     */
                    var altweg = InnersteMeldung(exception)
                        .Contains("Materialreparatur");
                    var hinweisDe = altweg
                        ? "  ·  Das ist die Zeitgrenze des ALTEN Rechenwegs. "
                            + "Stell den Rechenweg auf „Neu“."
                        : "  ·  Strg+Enter schreibt einen Geometrie-Abzug.";
                    var hinweisEn = altweg
                        ? "  ·  That is the OLD engine hitting its time limit. "
                            + "Switch the engine to “New”."
                        : "  ·  Ctrl+Enter writes a geometry dump.";
                    _uiSystem?.SetStatus(T(
                        "Vorschau abgebrochen: " + InnersteMeldung(exception)
                            + hinweisDe,
                        "Preview aborted: " + InnersteMeldung(exception)
                            + hinweisEn));
                }
            }
        }

        private JobHandle RenderOverlay(JobHandle inputDeps)
        {
            /*
             * DIE VERWALTUNG ZEIGT FERTIGE PARKPLAETZE, KEINEN ENTWURF.
             *
             * Zustand behalten: beim Rueckweg nach Draft ist der Umriss noch
             * da. Nur das ZEICHNEN faellt hier aus.
             *
             * DAS FLAECHENNETZ MUSS MIT. Es haengt nicht am Overlay, sondern
             * zeichnet in der Rendering-Phase selbst - und hat dieses Tor
             * deshalb nicht mitbekommen. Im Reiter "Parking lots" verschwand
             * die ganze Vorschau und das Gras blieb allein stehen. Der Nutzer
             * am 2026-09-15: *"derzeit sehe ich nur gruen."*
             */
            if (!Werkzeugzustand.ZeigtWerkzeugvorschau(_reiter))
            {
                _flaechennetz?.Leere();
                return inputDeps;
            }
            var buffer = _overlayRenderSystem.GetBuffer(out var overlayDeps);
            var deps = JobHandle.CombineDependencies(inputDeps, overlayDeps);
            // GetBuffer liefert die noch laufenden Schreiber derselben NativeLists.
            // PLT zeichnet auf dem Hauptthread, nicht in einem abhaengigen Job:
            // deshalb VOR dem ersten Draw warten, nicht erst das Handle zurueckgeben.
            // Sonst koennen List.Add/Reallokation mit CS2-Schreibern konkurrieren.
            deps.Complete();

            // Alle Buffer-Aufrufe bleiben synchron zwischen GetBuffer und Rückgabe;
            // weder Buffer noch temporäre Native-Daten werden über Frames gehalten.
            /*
             * DIE ZEIGERHILFEN DES ENTWURFS GEHOEREN IN DEN ENTWURF.
             *
             * Ansage des Nutzers am 2026-09-04: *"Immer noch ist das
             * Hover-Interface des Linienziehens an im Zoning-Reiter, also die
             * 2 Linien mit 2 Kreisen in Gelb. Das gehoert nicht dahin als
             * User-Feedback, sondern nur zu Draft. Ebenso das Hovern ueber
             * Polypunkte."*
             *
             * Er hat recht, und zwar aus einem Grund, der ueber die Optik
             * hinausgeht: im Zoning-Reiter tun diese Hilfen auch NICHTS - der
             * Linksklick gehoert dort den Flaechen und den Straussenseiten.
             * Eine Anzeige, die etwas verspricht, das der Klick nicht
             * einloest, ist schlimmer als gar keine.
             */
            var entwurfshilfen = Werkzeugzustand.ZeigtUmrisshilfe(_reiter, AktuellerModus);

            _overlay.Draw(buffer, _worldPoints, _closed, (entwurfshilfen || MarkerMode) && _hasHover,
                _hoverPosition, CanCloseAtCursor(),
                entwurfshilfen ? _hoverPoint : -1, entwurfshilfen ? _dragPoint : -1,
                LastSnap, entwurfshilfen && HasSnapGuide, SnapGuide, _markers, MarkerMode,
                _entranceOverlay,
                // Die Kante wird auch beim Ausrichten gezeigt - dort ist sie
                // das Auswahlziel. Das ZIEHEN bleibt gesperrt, deshalb haengt
                // nur die Anzeige um, nicht `_dragEdge`.
                entwurfshilfen || Ausrichtwahl == Ausrichtschritt.Linie
                    ? _hoverEdge : -1,
                entwurfshilfen ? _dragEdge : -1,
                entwurfshilfen && _insertReady, _insertPosition,
                entwurfshilfen && _edgeSnapped, _edgeSnapPoint,
                _edgeGuide,
                // Ausstuelpen und Kantenschieben gibt es beim Ausrichten
                // nicht - und Umschalt gehoert dort den Teilflaechen.
                entwurfshilfen && !AusrichtWahlAktiv && _hoverEdge >= 0 && _dragEdge < 0
                    && AltGehalten(),
                entwurfshilfen && !AusrichtWahlAktiv && _hoverEdge >= 0 && ShiftGehalten(),
                Ausrichtwahl == Ausrichtschritt.Linie,
                Teilflaechen,
                Ausrichtwahl == Ausrichtschritt.Flaeche,
                AusrichtFlaeche,
                AusrichtWahlAktiv ? ZugewieseneTeilflaechen() : null,
                HervorgehobeneTeilflaeche(),
                ZeigerTeilflaeche(),
                AusrichtBlinkKante(),
                TrennmodusAktiv,
                TrennmodusAktiv ? Trennlinien() : null,
                _trennAnfang,
                _zoningflaechen,
                ZoningVorschau,
                _zoningHover,
                ZoningAuswahl,
                ZoningSeite,
                _zoningStrassenAktuell,
                ZoningSeitenVorschau(out var seiteA, out var seiteB,
                        out var seiteLinks)
                    ? (seiteA, seiteB, seiteLinks)
                    : ((float2, float2, bool)?)null,
                ZoningSeitenModus,
                _randzoning.Select(l => (l.A, l.B)).ToArray(),
                RandzoningStrassenAchsen.Select(r => (r.A, r.B)).ToArray(),
                RandzoningVorschau(out var randA, out var randB,
                        out var randZustand)
                    ? (randA, randB, randZustand)
                    : ((float2, float2, int)?)null);
            return deps;
        }

        /** Enter, und zwar genau Enter - ohne Strg, Shift oder Alt. */
        private static bool BuildRequested()
        {
            var keyboard = Keyboard.current;
            var input = InputManager.instance;
            if (keyboard == null || input == null || input.hasInputFieldFocus) return false;
            if (!keyboard.enterKey.wasPressedThisFrame
                && !keyboard.numpadEnterKey.wasPressedThisFrame) return false;
            return !keyboard.leftCtrlKey.isPressed && !keyboard.rightCtrlKey.isPressed
                && !keyboard.leftShiftKey.isPressed && !keyboard.rightShiftKey.isPressed
                && !keyboard.leftAltKey.isPressed && !keyboard.rightAltKey.isPressed;
        }

        /**
         * Strg+Enter, und zwar genau das - ohne Shift oder Alt.
         *
         * Ausdruecklicher Wunsch des Nutzers am 2026-08-21: einen Abzug
         * schreiben koennen, OHNE zu bauen, genau in dem Moment, in dem die
         * Vorschau nichts anzeigt. Alt+P kann dasselbe, aber Strg+Enter liegt
         * neben dem Bau-Enter und wird deshalb im Zweifel auch gefunden.
         *
         * Gelesen wird direkt an `Keyboard.current`, nicht ueber eine
         * ProxyAction: CS2 maskiert unveraenderte Bindungen, solange ein
         * Modifier gehalten wird - daran ist im Projekt schon einmal ein
         * Strg-Kuerzel gescheitert.
         */
        private static bool DiagnoseRequested()
        {
            var keyboard = Keyboard.current;
            var input = InputManager.instance;
            if (keyboard == null || input == null || input.hasInputFieldFocus) return false;
            if (!keyboard.enterKey.wasPressedThisFrame
                && !keyboard.numpadEnterKey.wasPressedThisFrame) return false;
            return (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                && !keyboard.leftShiftKey.isPressed && !keyboard.rightShiftKey.isPressed
                && !keyboard.leftAltKey.isPressed && !keyboard.rightAltKey.isPressed;
        }

        /**
         * Die unterste Ursache einer Ausnahmekette als kurzer Satz.
         *
         * `Task` verpackt jede Ausnahme in eine AggregateException; deren
         * eigene Meldung lautet "One or more errors occurred" und sagt gar
         * nichts. Die Meldung ganz unten nennt dagegen die Regel, an der der
         * Generator ausgestiegen ist.
         */
        private static string InnersteMeldung(Exception exception)
        {
            if (exception == null) return "unbekannte Ursache";
            while (exception.InnerException != null)
                exception = exception.InnerException;
            var text = exception.Message;
            return string.IsNullOrWhiteSpace(text)
                ? exception.GetType().Name
                : text.Trim();
        }

        /**
         * Macht aus den Vorschau-Flaechen dauerhafte.
         *
         * Erst danach zeichnet CS2 ihr Material. Belegt im Dekompilat: das
         * AreaBatchSystem, das Flaechen ihr Material gibt, hat in beiden
         * Queries `None = { Temp }`; fuer Temp-Flaechen laeuft nur das
         * AreaBorderRenderSystem, das ausdruecklich Temp behandelt und
         * ausschliesslich den Rand zeichnet. Eine Vorschau kann in CS2 also
         * gar nicht gefuellt aussehen - der Nutzer sah zu Recht nur Umrisse.
         */
        private bool TryApplyAreaPreview()
        {
            if (!_closed || !_ghostsActive || _areaPreviewLayout == null)
            {
                Mod.log.Warn("PLT: Enter ohne fertige Vorschau; nichts gebaut.");
                return false;
            }

            // Erst die Flaechen einem gemeinsamen Besitzer unterstellen, dann
            // anwenden. Nach dem Anwenden waere es zu spaet: CS2 traegt sie
            // beim Uebergang von Temp auf dauerhaft in den SubArea-Puffer ein.
            ProtokolliereBauschritt("AttachSurfacesToLotOwner");
            var attached = AttachSurfacesToLotOwner();
            ProtokolliereBauschritt("WriteBuildReceipt");
            if (!WriteBuildReceipt(_lotOwner, _areaPreviewSettings,
                    _worldPoints.ToArray()))
            {
                _uiSystem?.SetStatus(T(
                    "Nichts gebaut: der Bauzettel konnte nicht gespeichert werden.",
                    "Nothing built: the build receipt could not be saved."));
                ClearAreaPreviewGhosts("build receipt could not be stored");
                if (IsEditing)
                    AbortEdit("Bauzettel des Neubaus fehlgeschlagen",
                        "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                        "Rebuild failed; the old parking lot was restored.");
                return false;
            }
            ProtokolliereBauschritt("CreateLotCarrier");
            if (!CreateLotCarrier())
            {
                _uiSystem?.SetStatus(T(
                    "Nichts gebaut: der Parkplatztraeger konnte nicht angelegt werden.",
                    "Nothing built: the parking lot carrier could not be created."));
                ClearAreaPreviewGhosts("carrier could not be created");
                if (IsEditing)
                    AbortEdit("Träger des Neubaus fehlgeschlagen",
                        "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                        "Rebuild failed; the old parking lot was restored.");
                return false;
            }
            ProtokolliereBauschritt("AttachPartsToLotOwner");
            var parts = AttachPartsToLotOwner();
            ProtokolliereBauschritt("NameLotOwner");
            NameLotOwner(_areaPreviewLayout.Stalls);

            // Noch sind alle Teile Temp-Entities und kein Terrainverbraucher
            // kann auf sie reagieren. Das ist der letzte kausal saubere
            // Zeitpunkt fuer die Vorherhoehen und fuer die Liste der
            // Stadtstrassen, die LocalConnect beim Apply aktualisieren wird.
            ProtokolliereBauschritt("CaptureTerrainBeforeApply");
            CaptureTerrainBeforeApply();
            // Der letzte Moment, an dem noch nichts umgewandelt ist. Stuerzt
            // CS2 gleich beim Apply ab, liegt der Bauzettel trotzdem vor.
            ProtokolliereBauschritt("WriteVorbauDump");
            RecordPreviewDiagnostic("Info", ZoningGassenabstand.Beschreibe(
                _areaPreviewLayout, _areaPreviewSettings ?? LayoutSettings.Cs2));
            RecordPreviewDiagnostic("Info", "Vegetation: " + (_vegetationReceipt?.Options ?? "aus") + "; neu=" + _vegetationCount + "; uebernehmen=" + _vegetationPreserve);
            WriteVorbauDump();
            ProtokolliereBauschritt("ApplyMode.Apply — Übergabe an CS2");
            applyMode = ApplyMode.Apply;
            /*
             * HIER, WEIL HIER JEDER BAU VORBEIKOMMT.
             *
             * Der automatische Versorgungsanschluss hing zuerst am
             * Abschluss des UMBAUS - und lief beim Neubau deshalb nie.
             * Diese Stelle durchlaeuft beides.
             */
            // Beim Umbau erst nach Commit und Abriss der alten Leitungen starten.
            if (!IsEditing) MerkeAutoVersorgung(_lotCarrier);
            /**
             * GEPLANT UND GESETZT SIND SEIT DEN FLAECHENSCHALTERN ZWEIERLEI.
             *
             * Diese Zeile meldete die Zahlen aus dem LAYOUT, also das
             * Geplante. Solange immer alles gesetzt wurde, war das dasselbe.
             * Mit abgeschalteten Flaechen log sie: im Lauf vom 2026-08-24,
             * 12:55:03, stand "13 Gras- und 9 Asphaltflaechen dauerhaft
             * gesetzt" direkt unter "PLT-Gruppe: 0 Flaechen".
             *
             * Das ist genau die Sorte Zeile, die eine Fehlersuche in die
             * falsche Richtung schickt - und dieselbe Zahl geht ins
             * Bauprotokoll, mit dem sich ein Bau spaeter nachstellen laesst.
             */
            var grassGeplant = _areaPreviewLayout.GrassSurface?.Length ?? 0;
            var asphaltGeplant = _areaPreviewLayout.AsphaltSurface?.Length ?? 0;
            var dekoAn = _uiSystem?.FlaecheDekoAn ?? true;
            var strasseAn = _uiSystem?.FlaecheStrasseAn ?? true;
            var grass = dekoAn ? grassGeplant : 0;
            var asphalt = strasseAn ? asphaltGeplant : 0;
            Mod.log.Info($"PLT-Gruppe: {attached} Flächen, {parts.Nets} Wegteile "
                + $"und {parts.Objects} Objekte (Aufkleber und Ladesäulen) an "
                + "die PLT-Fläche und ihren nackten Träger gebunden. "
                + $"Träger-SubNet: "
                + $"{EntityManager.GetBuffer<Game.Net.SubNet>(_lotCarrier, true).Length} "
                + "Netzkanten.");
            // Die Besitzerart gehoert in DIESE Zeile, nicht nur in den Abzug.
            // Gemeldet wird das TATSAECHLICHE Prefab, nicht der Schalter: ist
            // das eigene Lot-Prefab noch nicht fertig, faellt der Bau still
            // auf eine Flaeche zurueck, und die Zeile darf das nicht
            // verschweigen.
            var ownerPrefab = OwnerPrefabDisplayName() ?? "unbekannt";
            var expected = UseLotAreaOwner ? "AN" : "AUS";
            /*
             * DIE ZONING-FLAECHE GEHOERT IN DIESE ZEILE.
             *
             * Der Nutzer meldete am 2026-09-02 einen braunen Fleck statt
             * einer Flaeche, und das Log konnte nicht helfen: es nannte nur
             * Gras und Belag. Ob die dritte Flaeche gesetzt wurde und mit
             * WELCHEM Prefab, liess sich nur aus der Gesamtzahl der
             * gebundenen Flaechen erschliessen. Das ist keine Auskunft,
             * das ist Kopfrechnen.
             */
            var zoningGeplant = string.IsNullOrEmpty(_uiSystem?.FlaecheZoning)
                ? 0
                : _areaPreviewLayout.ZoningSurface?.Length ?? 0;
            /*
             * DIE FAHRBAHN ZAEHLT MIT.
             *
             * Der Nutzer meldete zweimal "Zoning-Strasse hat keine Flaeche",
             * und ich konnte aus dem Log nicht entscheiden, ob gar keine
             * Ringe geplant waren oder ob sie geplant und unsichtbar waren.
             * Das sind zwei voellig verschiedene Fehler. Seitdem steht die
             * Zahl hier.
             */
            var zoningstrasseGeplant =
                _areaPreviewLayout.ZoningRoadSurface?.Length ?? 0;
            /*
             * FLAECHE UND ECKENZAHL JE RING.
             *
             * Der Nutzer sieht den Belag nicht, obwohl Ringe geplant sind
             * und das Prefab da ist. Die Testformen sind alle gueltig - der
             * Fehler steckt also in SEINER Form, und dafuer brauche ich die
             * echten Masse. Zu erwarten ist rings um eine 6x6-Flaeche ein
             * Korridor von rund 1792 m2; kommt hier etwas ganz anderes,
             * liegt es an der Geometrie und nicht an der Darstellung.
             */
            if (zoningstrasseGeplant > 0)
            {
                var masse = _areaPreviewLayout.ZoningRoadSurface
                    .Select(ring =>
                    {
                        var flaeche = 0.0;
                        for (var i = 0; i < ring.Length; i++)
                        {
                            var a = ring[i];
                            var b = ring[(i + 1) % ring.Length];
                            flaeche += a.x * b.y - b.x * a.y;
                        }
                        return $"{Math.Abs(flaeche) / 2:F0} m2/{ring.Length} Ecken";
                    });
                Mod.log.Info("PLT-Zoningbelag Ringe: "
                    + string.Join(" | ", masse));
            }
            var zoningFlaeche = string.IsNullOrEmpty(_uiSystem?.FlaecheZoning)
                ? "aus"
                : _uiSystem.FlaecheZoning;
            Mod.log.Info($"PLT gebaut: {grass} von {grassGeplant} Gras- und "
                + $"{asphalt} von {asphaltGeplant} Asphaltflächen dauerhaft "
                + "gesetzt"
                + (zoningGeplant > 0
                    ? $", dazu {zoningGeplant} Zoning-Fläche(n) mit "
                        + $"'{zoningFlaeche}'"
                    : $", kein Parzellenboden ({zoningFlaeche})")
                + $", Fahrbahn der Zoning-Straße: {zoningstrasseGeplant} Ring(e)"
                + (zoningstrasseGeplant > 0
                    ? _zoningBelagPrefab != Entity.Null
                        ? " (Prefab da)"
                        : " (PREFAB FEHLT - deshalb unsichtbar)"
                    : "")
                + (dekoAn && strasseAn ? "" :
                    dekoAn ? " (Fahrfläche abgeschaltet)" :
                    strasseAn ? " (Zwischenfläche abgeschaltet)"
                             : " (beide Flächenschalter aus)")
                + $". Besitzer ist \"{ownerPrefab}\" "
                + $"(Versuch {expected}). "
                + (ownerPrefab == LotOwnerPrefabName
                    ? "Ganzer Umriss anklickbar."
                    : "Nur die Buchten sind anklickbar."));
            // Der Abzug soll das GEBAUTE zeigen, nicht die Vorschau. Er wird
            // deshalb angefordert statt sofort geschrieben: CS2 wandelt die
            // Temp-Flaechen erst nach diesem Werkzeug-Update um.
            NoteApplied(grass, asphalt, parts.Nets, parts.Objects);
            RequestChargerAudit();
            // Das REZEPT festhalten, solange es noch da ist: gleich danach
            // wird zurueckgesetzt, und aus dem gebauten Parkplatz laesst sich
            // hinterher nicht mehr ablesen, aus welchem Polygon und welchen
            // Einstellungen er entstanden ist.
            // `_worldPoints` steht hier noch - zurueckgesetzt wird erst
            // weiter unten.
            AppendBuildJournal(_areaPreviewLayout, _areaPreviewSettings,
                _worldPoints.ToArray(), OwnerDisplayName(_lotOwner),
                parts.Nets, parts.Objects, grass, asphalt);
            RequestDebugDump("Enter");
            BeginReplacementCommit(_lotOwner, _lotCarrier);

            // Zuruecksetzen OHNE ClearAreaPreviewLayout - dessen
            // ApplyMode.Clear wuerde das eben Gebaute verwerfen.
            _points.Clear();
            _worldPoints.Clear();
            _pointAxes.Clear();
            ClearSnapFeedback();
            _closed = false;
            ResetEntranceEditing(clearEntrances: true);
            _hasHover = false;
            _hoverPoint = -1;
            _dragPoint = -1;
            _layoutDirty = false;
            _geometryRevision++;
            _overlay?.ClearLayout();
            _areaPreviewLayout = null;
            _ghostsActive = false;
            _lastPreviewSig = long.MinValue;
            VergissAusrichtung();
            ClearUndoHistory();
            return true;
        }

        private void ResetSelection()
        {
            // Ein halb fertiger Bauvorgang darf nicht ueber das Zuruecksetzen
            // hinweg weiterlaufen - sonst wendet er im naechsten Frame ein
            // Layout an, das es nicht mehr gibt.
            CancelBuildStage();
            _points.Clear();
            _worldPoints.Clear();
            _pointAxes.Clear();
            ClearSnapFeedback();
            _closed = false;
            ResetEntranceEditing(clearEntrances: true);
            _hasHover = false;
            _hoverPoint = -1;
            _dragPoint = -1;
            _layoutDirty = false;
            _geometryRevision++;
            _overlay?.ClearLayout();
            ClearAreaPreviewLayout("selection reset");
            VergissAusrichtung();
            ClearUndoHistory();
        }


        /**
         * WIRD DIESER BAU LOECHER HABEN? - noch bevor der Nutzer sie sucht.
         *
         * CS2 verwirft Flaechen mit zu kurzen Kanten; gemessen an den drei
         * Abzuegen der aktuellen DLL liegt die Grenze bei 0,375 m. Genau daran
         * ist am 2026-08-12/13 dreimal etwas gescheitert, jedes Mal in anderer
         * Gestalt: gar kein Asphalt (kuerzeste Kante 0,0055 m), fehlendes Gras
         * an einer Nutzermarkierung (0,013 m) und 99 zersplitterte Grasringe
         * statt der sonst ueblichen 13.
         *
         * Rechnet ausschliesslich am FERTIGEN Ergebnis - fasst die Geometrie
         * nicht an und kann deshalb nichts kaputtmachen.
         */
        private const double Cs2MinEdge = 0.375;

        /**
         * WAS CS2 WIRKLICH VERWERFEN WIRD - nicht mehr geschaetzt.
         *
         * Die Regel darunter (Kante oder Hals unter 0,375 m) war eine
         * Schaetzung, und am 2026-08-24 hat der Sondentest im Spiel gezeigt,
         * dass sie in beide Richtungen falsch liegt: CS2 nimmt einen Hals von
         * 0,0008 m an und verwirft ein Viereck mit 0,14 m kuerzester Kante.
         *
         * Der Grund steht im Dekompilat: `GeometrySystem` versetzt jeden
         * Knoten um -0,1 m nach innen und trianguliert das Ergebnis per
         * Ear-Clipping. Scheitert ein einziges Ohr, wird die GANZE Flaeche
         * verworfen. `Cs2Triangulierung` baut genau das nach; die Gegenprobe
         * gegen fuenf Ingame-Messungen trifft bitgenau
         * (`dotnet run -- --triangulierung`).
         */
        private static int VerworfeneRinge(float2[][] ringe)
        {
            if (ringe == null) return 0;
            var zahl = 0;
            foreach (var ring in ringe)
                if (ring != null && ring.Length >= 3
                    && Cs2Triangulierung.Dreiecke(ring) == 0) zahl++;
            return zahl;
        }

        private void LogSurfaceHealth(ParkingLayout layout)
        {
            if (layout == null) return;
            var wegGras = VerworfeneRinge(layout.GrassSurface);
            var wegBelag = VerworfeneRinge(layout.AsphaltSurface);
            if (wegGras + wegBelag > 0)
                Mod.log.Warn($"PLT-Triangulierung: CS2 wird {wegGras} Gras- und "
                    + $"{wegBelag} Belagflaeche(n) VERWERFEN - dort bleibt "
                    + "nackter Boden.");
            else
                Mod.log.Info("PLT-Triangulierung: CS2 nimmt alle Flaechen an.");
            var gras = SurfaceHealth(layout.GrassSurface);
            var belag = SurfaceHealth(layout.AsphaltSurface);
            var verdaechtig = gras.Bad + belag.Bad;
            var text = $"PLT-Flaechenpruefung: Gras {gras.Rings} Ringe (kuerzeste "
                + $"Kante {gras.MinEdge:F3} m, kleinste Flaeche {gras.MinArea:F2} m2), "
                + $"Belag {belag.Rings} Ringe (kuerzeste {belag.MinEdge:F3} m, "
                + $"kleinste {belag.MinArea:F2} m2). Unter CS2s Mindestkante "
                + $"{Cs2MinEdge:F3} m: {verdaechtig} Ring(e)";
            if (verdaechtig > 0)
                Mod.log.Warn(text + " - die wird CS2 voraussichtlich VERWERFEN, an "
                    + "ihrer Stelle bleibt eine Luecke.");
            else Mod.log.Info(text + " - keiner.");
        }

        private static (int Rings, double MinEdge, double MinArea, int Bad)
            SurfaceHealth(float2[][] ringe)
        {
            if (ringe == null || ringe.Length == 0) return (0, 0, 0, 0);
            var minEdge = double.PositiveInfinity;
            var minArea = double.PositiveInfinity;
            var bad = 0;
            foreach (var ring in ringe)
            {
                var kuerzeste = double.PositiveInfinity;
                double flaeche = 0;
                for (var i = 0; i < ring.Length; i++)
                {
                    var b = ring[(i + 1) % ring.Length];
                    kuerzeste = Math.Min(kuerzeste, math.length(b - ring[i]));
                    flaeche += ring[i].x * b.y - b.x * ring[i].y;
                }
                flaeche = Math.Abs(flaeche) / 2;
                if (kuerzeste < Cs2MinEdge) bad++;
                minEdge = Math.Min(minEdge, kuerzeste);
                minArea = Math.Min(minArea, flaeche);
            }
            return (ringe.Length, minEdge, minArea, bad);
        }

        private void DeactivateTool()
        {
            ClearAreaPreviewLayout("tool deactivation");
            if (m_ToolSystem != null && m_DefaultToolSystem != null)
                m_ToolSystem.activeTool = m_DefaultToolSystem;
        }
    }

    /// <summary>UI-unabhängiger Einstieg über exakte P-Tastenkombinationen.</summary>
    public sealed partial class ParkingLotToolActivationSystem : GameSystemBase
    {
        private enum PShortcut
        {
            None,
            ToggleTool,
            DebugDump,
            Inspect,
            ToggleLotAreaOwner,
        }

        private ToolSystem _toolSystem;
        private DefaultToolSystem _defaultToolSystem;
        private ParkingLotToolSystem _parkingLotTool;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _defaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            _parkingLotTool = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
        }

        [Preserve]
        /**
         * Die einstellbare Belegung. Wird beim ersten Blick geholt und
         * eingeschaltet - eine `ProxyAction` liefert sonst nie etwas.
         */
        private Game.Input.ProxyAction _werkzeugAktion;

        private Game.Input.ProxyAction Werkzeugaktion()
        {
            if (_werkzeugAktion != null) return _werkzeugAktion;
            _werkzeugAktion = Mod.Optionen?.GetAction(Setting.AktionWerkzeug);
            if (_werkzeugAktion != null) _werkzeugAktion.shouldBeEnabled = true;
            return _werkzeugAktion;
        }

        protected override void OnUpdate()
        {
            _parkingLotTool?.PflegeAutoVersorgungsmessung();
            // Muss VOR dem Ausstieg bei Eingabefokus stehen: die Nachschau
            // haengt an keiner Taste, sie haengt an verstrichenen Bildern.
            _parkingLotTool?.PflegeVegetationsnachschau();
            var keyboard = Keyboard.current;
            var input = InputManager.instance;
            if (keyboard == null || input == null || input.hasInputFieldFocus) return;

            /*
             * ZUERST DIE EINSTELLBARE BELEGUNG.
             *
             * Sie ersetzt das fruehere handgeschriebene Strg+P. Der Weg ueber
             * `ProxyAction` hat den Vorteil, um den es eigentlich geht: CS2
             * kennt die Belegung, zeigt sie in der Tastenuebersicht und macht
             * Konflikte dort sichtbar. Am 2026-08-25 hat uns genau diese
             * Unsichtbarkeit ueber eine Stunde gekostet - Find It hoerte auf
             * dieselbe Taste, und im Spiel sah es nur so aus, als sei der Mod
             * kaputt.
             */
            var aktion = Werkzeugaktion();
            if (aktion != null && aktion.WasPerformedThisFrame())
            {
                Schalte();
                return;
            }

            // Alt+M schaltet den Markiermodus. Eigene Taste, weil der
            // Meldeweg fuer Nutzer gedacht ist und nicht zwischen den
            // P-Kuerzeln untergehen soll.
            if (keyboard.mKey.wasPressedThisFrame
                && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)
                && !keyboard.leftCtrlKey.isPressed && !keyboard.rightCtrlKey.isPressed
                && !keyboard.leftShiftKey.isPressed && !keyboard.rightShiftKey.isPressed)
            {
                _parkingLotTool.ToggleMarkerMode();
                return;
            }
            /*
             * HIER STAND ALT+F.
             *
             * Der Vorversuch fuer das gefuellte Flaechennetz hing an dieser
             * Taste. Seit dem 2026-09-15 laeuft das Netz immer mit, sobald
             * eine Vorschau steht - ein Schalter fuer etwas, das man immer
             * will, ist nur eine Falle fuer den, der ihn nicht kennt.
             */
            if (!keyboard.pKey.wasPressedThisFrame) return;

            var control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            var alt = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;

            var shortcut = ClassifyPShortcut(control, shift, alt);
            if (shortcut == PShortcut.ToggleLotAreaOwner)
            {
                _parkingLotTool.ToggleLotAreaOwner();
                return;
            }
            if (shortcut == PShortcut.Inspect)
            {
                _parkingLotTool.WriteSelectionInspection();
                return;
            }
            if (shortcut == PShortcut.DebugDump)
            {
                if (_toolSystem.activeTool == _parkingLotTool)
                    _parkingLotTool.RequestDebugDump();
                else
                    _parkingLotTool.WriteDebugDumpNow();
                return;
            }

            // P allein sowie Strg+Shift+P und Alt+Shift+P gehoeren anderen
            // Bedienkontexten und werden bewusst nicht ausgewertet.
            if (shortcut != PShortcut.ToggleTool) return;

            Schalte();
        }

        /** Werkzeug an oder aus - der einzige Ort, der das entscheidet. */
        private void Schalte()
        {
            if (_toolSystem.activeTool == _parkingLotTool)
            {
                Mod.log.Info("PLT-Hotkey: beendet das Werkzeug.");
                _toolSystem.activeTool = _defaultToolSystem;
                return;
            }

            // Der Hotkey ist global, solange kein Textfeld den Fokus hat. Die
            // fruehere `controlOverWorld`-Schranke machte Strg+P vom Ort des
            // Mauszeigers abhaengig; das Werkzeug selbst sperrt Weltklicks ueber
            // der UI bereits mit `WorldInputAllowed`.
            // Welches Werkzeug vorher aktiv war, ist die wichtigste Zeile
            // dieser Datei: genau sie hat am 2026-08-25 FindIt.Picker als
            // Gegenspieler entlarvt.
            Mod.log.Info("PLT-Hotkey: aktiviert das Werkzeug; vorher aktiv: "
                + (_toolSystem.activeTool?.toolID ?? "<keins>"));
            _toolSystem.activeTool = _parkingLotTool;
        }

        private static PShortcut ClassifyPShortcut(bool control, bool shift, bool alt)
        {
            // Strg+P ist RAUS: Find It hoert auf dieselbe Kombination und
            // behielt das Werkzeug. Das Umschalten laeuft jetzt ueber die
            // einstellbare Belegung in den Modoptionen, nicht mehr hier.
            if (control && !shift && !alt) return PShortcut.None;
            if (alt && !control && !shift) return PShortcut.DebugDump;
            // Strg+Alt+P schreibt auf, woraus ein angewaehltes Objekt besteht.
            if (control && alt && !shift) return PShortcut.Inspect;
            // Shift+P schaltet den Versuch mit eigener Lot-Besitzerflaeche um.
            // Strg+Shift+P und Alt+Shift+P bleiben frei, die gehoeren anderen
            // Bedienkontexten.
            if (shift && !control && !alt) return PShortcut.ToggleLotAreaOwner;
            return PShortcut.None;
        }
    }
}
