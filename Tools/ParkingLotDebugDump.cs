using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.City;
using Unity.Collections;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private const float DebugDumpWaitSeconds = 15f;
        private const int MaxPreviewDiagnosticRecords = 200;

        private Game.Net.SearchSystem _netSearchSystem;
        private Game.Areas.SearchSystem _areaSearchSystem;
        private CityConfigurationSystem _cityConfigurationSystem;
        private ParkingLotDebugTooltipSystem _debugTooltipSystem;
        private EntityQuery _worldAreaQuery;

        private readonly List<PreviewDiagnosticRecord> _previewDiagnostics =
            new List<PreviewDiagnosticRecord>();
        private bool _debugDumpRequested;
        private float _debugDumpRequestedAt;
        private string _debugDumpTrigger = "Alt+P";
        private int _appliedAtFrame = -1;
        private DateTime _appliedAtLocal;
        private int _appliedGrassCount;
        private int _appliedAsphaltCount;

        private float2[] _lastPreviewSite;
        private float3[] _lastPreviewWorldSite;
        private LayoutSettings _lastPreviewSettings;
        private ParkingLayout _lastPreviewLayout;
        private int _lastPreviewRevision = -1;
        private DateTime _lastPreviewStartedUtc;
        private DateTime _lastPreviewCompletedUtc;
        private double _lastPreviewBuildMilliseconds;

        private void InitializeDebugDump()
        {
            _netSearchSystem = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            _areaSearchSystem = World.GetOrCreateSystemManaged<Game.Areas.SearchSystem>();
            _cityConfigurationSystem =
                World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            _debugTooltipSystem =
                World.GetOrCreateSystemManaged<ParkingLotDebugTooltipSystem>();
            _worldAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
        }

        internal void RequestDebugDump(string trigger = "Alt+P")
        {
            _debugDumpRequested = true;
            _debugDumpTrigger = trigger;
            _debugDumpRequestedAt = UnityEngine.Time.realtimeSinceStartup;
            Mod.log.Info($"PLT-Debug-Abzug angefordert ({trigger}); es wird "
                + "gegebenenfalls auf den laufenden Vorschaulauf, den Bauvorgang "
                + "und die CS2-Flächenauswertung gewartet.");
        }

        /**
         * Nach einem Bau merken, damit der Abzug die GEBAUTEN Flaechen zeigt.
         *
         * CS2 wandelt die Temp-Flaechen erst nach dem Werkzeug-Update um. Ein
         * sofort geschriebener Abzug zeigte deshalb den Zustand davor - also
         * genau nicht das, wofuer er gemacht ist.
         */
        internal void NoteApplied(int grassCount, int asphaltCount,
                                  int attachedNets, int attachedObjects)
        {
            _appliedAtFrame = UnityEngine.Time.frameCount;
            _appliedAtLocal = DateTime.Now;
            _appliedGrassCount = grassCount;
            _appliedAsphaltCount = asphaltCount;
            _appliedNetCount = attachedNets;
            _appliedObjectCount = attachedObjects;
        }

        private int _appliedNetCount;
        private int _appliedObjectCount;

        internal void WriteDebugDumpNow()
        {
            _debugDumpRequested = false;
            WriteDebugDump(waitTimedOut: false);
            /**
             * Ohne Polygon fragt der Abzug keine Weltobjekte ab - genau das
             * braucht man aber nach dem Laden eines Spielstands, wenn man
             * wissen will, ob der Parkplatz seine Kinder behalten hat. Dafuer
             * gibt es Strg+Alt+P, aber der Unterschied zu Alt+P ist eine
             * Fussangel: einmal wurde deshalb der falsche Abzug geschrieben
             * und die Frage blieb offen. Ist etwas angewaehlt, schreibt Alt+P
             * die Bauteilliste jetzt gleich mit.
             */
            if (_points.Count >= 3) return;
            var selected = _selectedInfoSystem?.selectedEntity ?? Entity.Null;
            if (selected == Entity.Null || !EntityManager.Exists(selected)) return;
            Mod.log.Info("PLT: kein Polygon gezeichnet, aber etwas angewählt - "
                + "es wird zusätzlich die Bauteilliste geschrieben.");
            WriteSelectionInspection();
        }

        private void ProcessDebugDumpRequest(bool toolIsActive)
        {
            if (!_debugDumpRequested) return;

            var waited = UnityEngine.Time.realtimeSinceStartup - _debugDumpRequestedAt;
            var timedOut = waited >= DebugDumpWaitSeconds;
            // CS2 wandelt die Temp-Flaechen erst nach dem Werkzeug-Update um;
            // vor der Umwandlung stuende im Abzug der Zustand VOR dem Bau.
            if (!timedOut && _appliedAtFrame >= 0
                && UnityEngine.Time.frameCount - _appliedAtFrame < 3) return;
            if (!timedOut && toolIsActive && _closed)
            {
                if (_buildTask != null || _layoutDirty) return;
                if (_areaTransferCreatedFrame >= 0
                    && UnityEngine.Time.frameCount - _areaTransferCreatedFrame < 2)
                    return;
            }

            _debugDumpRequested = false;
            WriteDebugDump(timedOut);
        }

        private void BeginPreviewDiagnostics()
        {
            _previewDiagnostics.Clear();
        }

        private void RememberCompletedPreview(
            float2[] site,
            float3[] worldSite,
            LayoutSettings settings,
            ParkingLayout layout,
            int revision,
            DateTime startedUtc,
            DateTime completedUtc,
            double elapsedMilliseconds)
        {
            _lastPreviewSite = Copy(site);
            _lastPreviewWorldSite = Copy(worldSite);
            _lastPreviewSettings = settings?.Clone();
            _lastPreviewLayout = layout;
            _lastPreviewRevision = revision;
            _lastPreviewStartedUtc = startedUtc;
            _lastPreviewCompletedUtc = completedUtc;
            _lastPreviewBuildMilliseconds = elapsedMilliseconds;
        }

        private void RecordPreviewDiagnostic(
            string severity,
            string message,
            Exception exception = null)
        {
            if (_previewDiagnostics.Count >= MaxPreviewDiagnosticRecords)
                _previewDiagnostics.RemoveAt(0);
            _previewDiagnostics.Add(new PreviewDiagnosticRecord
            {
                TimestampUtc = DateTime.UtcNow,
                Severity = severity,
                Message = message,
                ExceptionType = exception?.GetType().FullName,
                ExceptionMessage = exception?.Message,
                StackTrace = exception?.StackTrace,
            });
        }

        /**
         * DER ZETTEL VOR DEM BAU.
         *
         * Befund des Nutzers am 2026-09-09: CS2 stuerzte beim Bauen nativ ab,
         * und vom abstuerzenden Bau gab es keinen Bauzettel - der letzte war
         * eine Minute alt und gehoerte zum Bau davor. Grund ist die
         * Wartelogik in `ProcessDebugDumpRequest`: der Abzug wartet
         * absichtlich, bis CS2 die Temp-Flaechen umgewandelt hat, weil sonst
         * der Zustand VOR dem Bau darin stuende. Genau dieser Zustand ist
         * aber das Einzige, was einen Absturz beim Bauen noch erklaeren kann.
         *
         * *"Waere wichtig dass wir den Bauzettel bekommen bevor es crashen
         * kann bzw bevor ueberhaupt wirklich gebaut wird."*
         *
         * Deshalb dieser zweite, schlanke Abzug. Er laeuft unmittelbar vor
         * `ApplyMode.Apply` und enthaelt alles, was den Bau nachrechenbar
         * macht: Eingabe, Einstellungen, fertiges Layout und der geplante
         * Flaechentransfer. Die vorhandene Welt wird BEWUSST nicht erfasst -
         * das ist der teure Teil, und er wuerde jeden Bau bremsen.
         *
         * Er darf unter keinen Umstaenden den Bau verhindern: alles laeuft in
         * einem eigenen try/catch, und ein Fehler beim Schreiben wird nur
         * gemeldet.
         */
        private void WriteVorbauDump() => SchreibeVorbauAbzug();

        /**
         * DER VORSCHAU-BERICHT SCHREIBT SEINEN EIGENEN ABZUG.
         *
         * Bis 2026-09-25 packte "Vorschau melden" nur die juengsten
         * VORHANDENEN Dateien ein - und die entstehen beim Bauen. Die
         * Meldung vom 15:35 enthielt deshalb den Bau von 15:30 samt "No
         * closed preview", die gemeldete Vorschau (Dekoflaeche verschwindet
         * nach dem Setzen einer Zufahrt) fehlte ganz. Jetzt schreibt die
         * Meldung den Vorab-Abzug des AKTUELLEN Stands und einen frischen
         * Markierungsbericht, und nur diese beiden gehen ins Paket.
         */
        internal List<string> SchreibeVorschauAbzug()
        {
            var dateien = new List<string>();
            var abzug = SchreibeVorbauAbzug();
            if (abzug != null) dateien.Add(abzug);
            try
            {
                var bericht = Path.Combine(Path.Combine(
                        Application.persistentDataPath, "Logs"),
                    "ParkingLotTool-summary-"
                    + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
                File.WriteAllText(bericht, BuildMarkerReport(),
                    new System.Text.UTF8Encoding(false));
                dateien.Add(bericht);
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception,
                    "PLT-Vorschaubericht: Markierungsbericht nicht geschrieben.");
            }
            return dateien;
        }

        private string SchreibeVorbauAbzug()
        {
            try
            {
                RefreshAreaTransferAudit();
                SelectDebugInput(out var inputSite, out var inputWorld,
                    out var inputSettings, out var inputSource);

                var now = DateTimeOffset.Now;
                var logsFolder = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(logsFolder);
                var timestamp = now.ToString("yyyyMMdd-HHmmss-fff");
                var outputPath = Path.Combine(logsFolder,
                    $"ParkingLotTool-prebuild-{timestamp}.json");
                var suffix = 1;
                while (File.Exists(outputPath))
                {
                    outputPath = Path.Combine(logsFolder,
                        $"ParkingLotTool-prebuild-{timestamp}-{suffix}.json");
                    suffix++;
                }
                var latestPath = Path.Combine(logsFolder,
                    "ParkingLotTool-prebuild-latest.json");

                var welt = new DebugWorld
                {
                    Captured = false,
                    Nets = null,
                    Areas = null,
                    Note = "Vorab-Abzug: die vorhandene Welt wird nicht erfasst, "
                        + "damit der Bau nicht gebremst wird.",
                };
                var document = BuildDebugDocument(now, outputPath, latestPath,
                    inputSite, inputWorld, inputSettings, inputSource,
                    welt, waitTimedOut: false);
                var json = SerializeDebugDocument(document);
                File.WriteAllText(outputPath, json, new System.Text.UTF8Encoding(false));
                File.Copy(outputPath, latestPath, overwrite: true);
                Mod.log.Info("PLT-Vorab-Bauzettel geschrieben: "
                    + Path.GetFileName(outputPath)
                    + " (feste Kopie: " + Path.GetFileName(latestPath)
                    + "), im Logs-Ordner des Spiels.");
                return outputPath;
            }
            catch (Exception exception)
            {
                // Ein Abzug darf niemals einen Bau umbringen.
                Mod.log.Error(exception,
                    "PLT-Vorab-Bauzettel konnte nicht geschrieben werden.");
                return null;
            }
        }

        private void WriteDebugDump(bool waitTimedOut)
        {
            try
            {
                RefreshAreaTransferAudit();
                SelectDebugInput(out var inputSite, out var inputWorld,
                    out var inputSettings, out var inputSource);

                DebugWorld world;
                try
                {
                    world = CaptureExistingWorld(inputSite);
                }
                catch (Exception exception)
                {
                    RecordPreviewDiagnostic("Error",
                        "Vorhandene Netze und Flächen konnten nicht vollständig gelesen werden.",
                        exception);
                    world = new DebugWorld
                    {
                        Captured = false,
                        Nets = null,
                        Areas = null,
                        Error = exception.ToString(),
                    };
                }

                var now = DateTimeOffset.Now;
                var logsFolder = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(logsFolder);
                var timestamp = now.ToString("yyyyMMdd-HHmmss-fff");
                var outputPath = Path.Combine(logsFolder,
                    $"ParkingLotTool-debug-{timestamp}.json");
                var suffix = 1;
                while (File.Exists(outputPath))
                {
                    outputPath = Path.Combine(logsFolder,
                        $"ParkingLotTool-debug-{timestamp}-{suffix}.json");
                    suffix++;
                }
                var latestPath = Path.Combine(logsFolder,
                    "ParkingLotTool-debug-latest.json");

                var document = BuildDebugDocument(now, outputPath, latestPath,
                    inputSite, inputWorld, inputSettings, inputSource,
                    world, waitTimedOut);
                var json = SerializeDebugDocument(document);
                File.WriteAllText(outputPath, json, new System.Text.UTF8Encoding(false));
                File.Copy(outputPath, latestPath, overwrite: true);

                /**
                 * Der FEHLERBERICHT daneben.
                 *
                 * Der JSON-Abzug ist fuer die Entwicklung; ein Nutzer, der
                 * eine Grasnarbe melden will, kann damit nichts anfangen und
                 * verschickt ungern eine Megabyte-Datei. Der Bericht ist
                 * Klartext, nennt zu jeder markierten Stelle die gefundenen
                 * Regelverletzungen und passt in eine Nachricht.
                 */
                var reportPath = Path.Combine(logsFolder,
                    $"ParkingLotTool-summary-{timestamp}.txt");
                File.WriteAllText(reportPath, BuildMarkerReport(),
                    new System.Text.UTF8Encoding(false));
                Mod.log.Info("PLT-Fehlerbericht geschrieben: "
                    + Path.GetFileName(reportPath) + " ("
                    + Markers.Count + " Markierung(en)), im Logs-Ordner "
                    + "des Spiels.");
                Mod.log.Info("PLT-Debug-Abzug geschrieben: "
                    + Path.GetFileName(outputPath)
                    + " (feste Kopie: " + Path.GetFileName(latestPath)
                    + "), im Logs-Ordner des Spiels.");
                ShowDebugDumpConfirmation(document);
                MeldeAbzugFertig();
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception, "PLT-Debug-Abzug konnte nicht geschrieben werden.");
            }
        }

        /**
         * Sagt der UI, dass der Abzug steht.
         *
         * Nur dafuer da, dass ein Bericht aus dem Info-Panel den FRISCHEN
         * Abzug einpackt und nicht den vorigen.
         */
        private void MeldeAbzugFertig()
            => _uiSystem?.AbzugFertig();

        private void ShowDebugDumpConfirmation(DebugDumpDocument document)
        {
            try
            {
                _debugTooltipSystem?.Show(DebugDumpConfirmation(document));
            }
            catch (Exception exception)
            {
                // Eine reine Bestätigung darf einen bereits geschriebenen Abzug
                // weder als fehlgeschlagen melden noch den Debug-Pfad unterbrechen.
                Mod.log.Error(exception,
                    "PLT-Debug-Abzug geschrieben, aber In-Game-Meldung fehlgeschlagen.");
            }
        }

        private string DebugDumpConfirmation(DebugDumpDocument document)
        {
            if (document?.Input?.MatchesResult == true && _lastPreviewLayout != null)
            {
                var surfaces = CountAreaPolygons(_lastPreviewLayout.GrassSurface)
                    + CountAreaPolygons(_lastPreviewLayout.AsphaltSurface);
                var stalls = _lastPreviewLayout.Stalls;
                return T(
                    $"Debug-Abzug geschrieben: {surfaces} "
                        + (surfaces == 1 ? "Fläche" : "Flächen")
                        + $", {stalls} " + (stalls == 1 ? "Bucht" : "Buchten"),
                    $"Debug dump written: {surfaces} "
                        + (surfaces == 1 ? "surface" : "surfaces")
                        + $", {stalls} " + (stalls == 1 ? "stall" : "stalls"));
            }

            var points = document?.Input?.PolygonXZ?.Length ?? 0;
            return T(
                $"Debug-Abzug geschrieben: {points} "
                    + (points == 1 ? "Polygonpunkt" : "Polygonpunkte"),
                $"Debug dump written: {points} "
                    + (points == 1 ? "polygon point" : "polygon points"));
        }

        private DebugDumpDocument BuildDebugDocument(
            DateTimeOffset now,
            string outputPath,
            string latestPath,
            float2[] inputSite,
            float3[] inputWorld,
            LayoutSettings inputSettings,
            string inputSource,
            DebugWorld world,
            bool waitTimedOut)
        {
            // Zuerst der Pfad aus dem Mod-Asset: `Assembly.Location` ist bei
            // CS2-Mods leer, weil das Spiel die DLL aus dem Speicher laedt.
            var dllPath = !string.IsNullOrEmpty(Mod.AssetPath)
                ? Mod.AssetPath
                : typeof(Mod).Assembly.Location;
            // Dieselbe Frage beantwortet seit dem 2026-09-23 auch die
            // Einstellungsseite. Sie steht deshalb an EINER Stelle.
            DateTime? dllBuiltUtc = Mod.Bauzeit();

            var matchesResult = _lastPreviewLayout != null
                && inputSource == "aktuelles gezeichnetes Polygon"
                && _geometryRevision == _lastPreviewRevision;
            if (inputSource == "letzter abgeschlossener Vorschaulauf")
                matchesResult = _lastPreviewLayout != null;

            var notes = new List<string>
            {
                "Layout- und ECS-Koordinaten sind die tatsächlich gespeicherten float-Werte; "
                    + "sie werden als JSON-Zahlen mit G17 ausgegeben.",
                "DynamicBuffer werden ausschließlich innerhalb dieses Aufrufs indexweise "
                    + "kopiert und nicht über einen Frame gehalten.",
            };
            if (waitTimedOut)
                notes.Add($"Der Abzug wartete {DebugDumpWaitSeconds:G17} s; laufende oder "
                    + "noch nicht triangulierte Arbeit ist deshalb als ausstehend markiert.");
            if (!matchesResult && _lastPreviewLayout != null)
                notes.Add("Das aktuelle Polygon weicht vom letzten fertigen Layout ab; "
                    + "Polygon und Ergebnisrevision sind deshalb getrennt ausgewiesen.");

            return new DebugDumpDocument
            {
                SchemaVersion = 1,
                Vegetation = Newtonsoft.Json.JsonConvert.DeserializeObject<ParkingLotTool.Geometry.VegetationOptions>(_vegetationReceipt?.Options ?? _uiSystem?.VegetationJson ?? "{}"),
                Framework = new DebugFramework
                {
                    CapturedAtUtc = now.UtcDateTime.ToString("O"),
                    CapturedAtLocal = now.ToString("O"),
                    /*
                     * NUR DIE DATEINAMEN, NICHT DIE PFADE.
                     *
                     * Jeder dieser Pfade faengt mit C:\Users\<Name> an, und
                     * dieser Abzug geht mit jeder Fehlermeldung nach aussen -
                     * seit dem 2026-09-15 in oeffentliche GitHub-Issues.
                     *
                     * Gebraucht wird davon nichts. Wer den Abzug liest, hat
                     * die Datei schon in der Hand; wissen will er, WELCHE
                     * Fassung sie erzeugt hat, und das sagen `DllBuiltAt*`
                     * und die Versionsnummer.
                     *
                     * Dieselbe Regel wie im Meldepaket: was nicht gebraucht
                     * wird, wird gar nicht erst aufgeschrieben. Nachwischen
                     * ist die schlechtere Loesung.
                     */
                    OutputPath = Path.GetFileName(outputPath),
                    LatestPath = Path.GetFileName(latestPath),
                    DllPath = Path.GetFileName(dllPath),
                    DllBuiltAtUtc = dllBuiltUtc?.ToString("O"),
                    DllBuiltAtLocal = dllBuiltUtc?.ToLocalTime().ToString("O"),
                    DllTimestampSource = "Letzte Schreibzeit der geladenen DLL-Datei "
                        + "laut File.GetLastWriteTimeUtc; kein geratener Build-Zeitpunkt.",
                    Cs2Version = Application.version,
                    GameMode = GameManager.instance?.gameMode.ToString(),
                    CityName = _cityConfigurationSystem?.cityName,
                    MapName = null,
                    MapNameNote = "Der Name des geladenen Karten-Assets wird vom hier "
                        + "verwendeten Laufzeitsystem nicht exponiert; der greifbare Stadtname "
                        + "steht separat in CityName.",
                    CoordinatePrecision = "G17, invariant; keine Dezimalrundung im Export",
                },
                Input = new DebugInput
                {
                    PolygonSource = inputSource,
                    PolygonWorld = DebugPoint3.From(inputWorld),
                    PolygonXZ = DebugPoint2.From(inputSite),
                    LayoutSettings = DebugLayoutSettings.From(inputSettings),
                    CurrentGeometryRevision = _geometryRevision,
                    ResultGeometryRevision = _lastPreviewLayout == null
                        ? (int?)null : _lastPreviewRevision,
                    MatchesResult = matchesResult,
                    Note = inputSite == null || inputSite.Length < 3
                        ? "Kein gezeichnetes Polygon mit mindestens drei Punkten verfügbar."
                        : null,
                },
                Preview = new DebugPreview
                {
                    Available = _lastPreviewLayout != null,
                    StartedAtUtc = _lastPreviewLayout == null
                        ? null : _lastPreviewStartedUtc.ToString("O"),
                    CompletedAtUtc = _lastPreviewLayout == null
                        ? null : _lastPreviewCompletedUtc.ToString("O"),
                    BuildMilliseconds = _lastPreviewLayout == null
                        ? (double?)null : _lastPreviewBuildMilliseconds,
                    PolygonWorldUsed = DebugPoint3.From(_lastPreviewWorldSite),
                    Layout = DebugLayout.From(_lastPreviewLayout),
                    Note = _lastPreviewLayout == null
                        ? "Noch kein Vorschaulauf erfolgreich abgeschlossen."
                        : null,
                },
                Cs2AreaTransfer = CaptureAreaTransferSnapshot(),
                Built = BuildBuiltSummary(world),
                ExistingWorld = world,
                AreaPrefabs = SurveyAreaPrefabs(),
                Diagnostics = BuildDiagnostics(notes),
            };
        }

        /**
         * Stellt dem Geplanten gegenueber, was danach wirklich in der Welt
         * steht. Gezaehlt werden die DAUERHAFTEN Flaechen unserer beiden
         * Prefabs im gezeichneten Polygon - `ExistingWorld` schliesst Temp
         * ausdruecklich aus, dort steht also nur Gebautes.
         *
         * Fremde Flaechen der Stadt mit denselben Prefabs wuerden mitgezaehlt.
         * Das ist bewusst so: lieber eine zu viel als das Bauergebnis
         * unvollstaendig, und die Einzelliste in ExistingWorld.Areas laesst
         * jede davon nachpruefen.
         */
        /** Woran der Parkplatz haengt - Surface oder unsere Lot-Flaeche. */
        private string OwnerPrefabDisplayName()
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner)
                || !EntityManager.HasComponent<PrefabRef>(_lotOwner))
                return null;
            var prefab = EntityManager.GetComponentData<PrefabRef>(_lotOwner).m_Prefab;
            return _prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var found)
                && found != null ? found.name : null;
        }

        private int OwnerBufferLength<T>() where T : unmanaged, IBufferElementData
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner)
                || !EntityManager.HasBuffer<T>(_lotOwner))
                return -1;
            return EntityManager.GetBuffer<T>(_lotOwner, true).Length;
        }

        private DebugBuilt BuildBuiltSummary(DebugWorld world)
        {
            if (_appliedAtFrame < 0) return null;

            var grass = 0;
            var asphalt = 0;
            var withoutTriangles = 0;
            var grassArea = 0.0;
            var asphaltArea = 0.0;
            foreach (var item in world?.Areas ?? Array.Empty<DebugExistingArea>())
            {
                var name = item.Prefab?.Name;
                var isGrass = string.Equals(name, GrassSurfaceName,
                    StringComparison.OrdinalIgnoreCase);
                var isAsphalt = string.Equals(name, PavementSurfaceName,
                    StringComparison.OrdinalIgnoreCase);
                if (!isGrass && !isAsphalt) continue;
                if (isGrass) { grass++; grassArea += item.SurfaceArea ?? 0; }
                else { asphalt++; asphaltArea += item.SurfaceArea ?? 0; }
                if ((item.TriangleCount ?? 0) == 0) withoutTriangles++;
            }

            var planned = _appliedGrassCount + _appliedAsphaltCount;
            var found = grass + asphalt;
            return new DebugBuilt
            {
                Trigger = _debugDumpTrigger,
                BuiltAtLocal = _appliedAtLocal.ToString("O"),
                BuiltAtFrame = _appliedAtFrame,
                PlannedGrass = _appliedGrassCount,
                PlannedAsphalt = _appliedAsphaltCount,
                InWorldGrass = grass,
                InWorldAsphalt = asphalt,
                InWorldWithoutTriangles = withoutTriangles,
                OwnerEntity = DebugEntity.From(_lotOwner),
                OwnerExists = _lotOwner != Entity.Null
                    && EntityManager.Exists(_lotOwner),
                OwnerSubAreaCount = OwnerBufferLength<Game.Areas.SubArea>(),
                OwnerSubNetCount = OwnerBufferLength<Game.Net.SubNet>(),
                OwnerSubObjectCount = OwnerBufferLength<Game.Objects.SubObject>(),
                ChildrenWithOwner = ZaehleKinderMitBesitzer(),
                PlannedCourses = _netRecords.Count,
                PlannedBayDecals = _objectRecords.Count,
                AttachedNets = _appliedNetCount,
                AttachedObjects = _appliedObjectCount,
                LotAreaOwner = UseLotAreaOwner,
                OwnerPrefabName = OwnerPrefabDisplayName(),
                GroupNote = "Zaehler -1 heisst: der jeweilige Puffer fehlt am "
                    + "Besitzer. SubNet und SubObject MUESSEN vorhanden sein - ihre "
                    + "Referenzsysteme greifen ungeprueft zu, sobald ein Kind "
                    + "dauerhaft wird. ChildrenWithOwner zaehlt die dauerhaften "
                    + "Flaechen im Polygon, die eine Owner-Komponente tragen.",
                InWorldGrassArea = grassArea,
                InWorldAsphaltArea = asphaltArea,
                Note = world == null || !world.Captured
                    ? "Ohne Weltaufnahme ist kein Abgleich moeglich."
                    : found < planned
                        ? $"{planned - found} von {planned} gebauten Flaechen stehen "
                            + "nicht in der Welt; Einzelfaelle in ExistingWorld.Areas."
                        : withoutTriangles > 0
                            ? $"{withoutTriangles} Flaechen ohne Dreiecke - von CS2 "
                                + "angelegt, aber nicht darstellbar."
                            : null,
            };
        }

        /** Dauerhafte Flaechen unserer Prefabs, die einen Besitzer tragen. */
        private int ZaehleKinderMitBesitzer()
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner)) return 0;
            using var alle = _worldAreaQuery.ToEntityArray(Allocator.TempJob);
            var n = 0;
            for (var i = 0; i < alle.Length; i++)
                if (EntityManager.HasComponent<Owner>(alle[i])
                    && EntityManager.GetComponentData<Owner>(alle[i]).m_Owner == _lotOwner)
                    n++;
            return n;
        }

        private DebugDiagnostics BuildDiagnostics(List<string> notes)
        {
            var messages = new PreviewDiagnosticRecordDto[_previewDiagnostics.Count];
            for (var i = 0; i < _previewDiagnostics.Count; i++)
            {
                var source = _previewDiagnostics[i];
                messages[i] = new PreviewDiagnosticRecordDto
                {
                    TimestampUtc = source.TimestampUtc.ToString("O"),
                    Severity = source.Severity,
                    Message = source.Message,
                    ExceptionType = source.ExceptionType,
                    ExceptionMessage = source.ExceptionMessage,
                    StackTrace = source.StackTrace,
                };
            }

            return new DebugDiagnostics
            {
                PreviewMessages = messages,
                LayoutWarnings = _lastPreviewLayout?.Warnings,
                Cs2LogMessages = null,
                Cs2LogMessagesNote = "Colossal ILog bietet Mods keine API zum Auslesen "
                    + "der bisherigen globalen CS2-Logeinträge. Mod-eigene Fehler und Warnungen "
                    + "des letzten Vorschaulaufs stehen in PreviewMessages; Flächenfehler werden "
                    + "über Entity-, Buffer- und AreaFlags-Zustand geprüft.",
                Notes = notes.ToArray(),
            };
        }

        /**
         * Ein GEBAUTER Parkplatz, dessen Abzug angefordert wurde.
         *
         * Ansage des Nutzers am 2026-09-14 zur Testveroeffentlichung: *"Wenn
         * er auf den Parkplatz klickt, der gebaut wurde, und im Info-Panel auf
         * 'Debug schreiben' klickt, dann halt von dem, der gebaut wurde, die
         * ganzen Infos."*
         *
         * Ohne diese Weiche haette der Abzug das genommen, was das WERKZEUG
         * gerade in der Hand hat - beim Melden ist das meistens nichts oder
         * etwas anderes. Der Parkplatz traegt seinen Bauzettel aber selbst
         * mit sich, und `TryReadBuildReceipt` liest ihn; genau denselben Weg
         * geht das Bearbeiten.
         *
         * Die Weiche gilt fuer EINEN Abzug und raeumt sich danach selbst weg.
         */
        private Entity _abzugVonLot = Entity.Null;

        internal void FordereLotAbzug(Entity lot)
        {
            _abzugVonLot = lot;
            RequestDebugDump("Info-Panel");
        }

        private void SelectDebugInput(
            out float2[] site,
            out float3[] world,
            out LayoutSettings settings,
            out string source)
        {
            var gewaehlt = _abzugVonLot;
            _abzugVonLot = Entity.Null;
            if (gewaehlt != Entity.Null && EntityManager.Exists(gewaehlt)
                && TryReadBuildReceipt(gewaehlt, out var zettel,
                    out var punkte, out var zufahrten, out _, out _, out _,
                    out _, out _, out _, out _))
            {
                world = punkte ?? Array.Empty<float3>();
                site = new float2[world.Length];
                for (var i = 0; i < world.Length; i++) site[i] = world[i].xz;
                settings = zettel.ToLayoutSettings(
                    zufahrten ?? Array.Empty<Entrance>());
                source = "gebauter Parkplatz " + gewaehlt.Index
                    + " (Bauzettel)";
                return;
            }

            if (_points.Count >= MinPolygonPoints)
            {
                site = new float2[_points.Count];
                world = new float3[_worldPoints.Count];
                for (var i = 0; i < _points.Count; i++) site[i] = _points[i];
                for (var i = 0; i < _worldPoints.Count; i++) world[i] = _worldPoints[i];
                settings = (_buildSettings ?? _lastPreviewSettings ?? LayoutSettings.Cs2).Clone();
                source = "aktuelles gezeichnetes Polygon";
                return;
            }

            site = Copy(_lastPreviewSite) ?? Array.Empty<float2>();
            world = Copy(_lastPreviewWorldSite) ?? Array.Empty<float3>();
            settings = (_lastPreviewSettings ?? LayoutSettings.Cs2).Clone();
            source = _lastPreviewLayout == null
                ? "kein Polygon verfügbar"
                : "letzter abgeschlossener Vorschaulauf";
        }

        private DebugPrefab DescribePrefab(Entity entity)
        {
            if (entity == Entity.Null)
            {
                return new DebugPrefab
                {
                    Resolved = false,
                    Note = "Keine Prefab-Entity vorhanden.",
                };
            }

            if (!EntityManager.Exists(entity))
            {
                return new DebugPrefab
                {
                    Entity = DebugEntity.From(entity),
                    Resolved = false,
                    Note = "Die referenzierte Prefab-Entity existiert nicht mehr.",
                };
            }

            try
            {
                if (_prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                    && prefab != null)
                {
                    return new DebugPrefab
                    {
                        Entity = DebugEntity.From(entity),
                        Name = prefab.name,
                        Type = prefab.GetType().FullName,
                        Resolved = true,
                    };
                }
            }
            catch (Exception exception)
            {
                return new DebugPrefab
                {
                    Entity = DebugEntity.From(entity),
                    Resolved = false,
                    Note = exception.GetType().Name + ": " + exception.Message,
                };
            }

            return new DebugPrefab
            {
                Entity = DebugEntity.From(entity),
                Resolved = false,
                Note = "PrefabSystem konnte die Entity nicht in ein Prefab auflösen.",
            };
        }

        private static float2[] Copy(float2[] source)
        {
            if (source == null) return null;
            var result = new float2[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        private static float3[] Copy(float3[] source)
        {
            if (source == null) return null;
            var result = new float3[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }
    }
}
