using System;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * WERKSWERTE, keine Konstanten mehr.
         *
         * Der Nutzer waehlt beide Flaechen frei - *"Wenn die Gras auf der
         * Strasse wollen und Asphalt als Flaeche 2 ist das denen ueberlassen."*
         * Diese zwei Namen gelten nur, solange nichts eingestellt ist.
         */
        private const string GrassSurfaceWerk = "Grass Surface 01";
        private const string PavementSurfaceWerk = "Pavement Surface 01";

        private string GrassSurfaceName
            => _uiSystem?.FlaecheDekoration ?? GrassSurfaceWerk;

        /**
         * Der Boden unter den Zoning-Parzellen.
         *
         * OHNE WAHL WIRD GAR NICHTS GESETZT.
         *
         * Bis zum 2026-09-03 fiel er auf die Dekoflaeche zurueck. Der
         * Nutzer will das ausdruecklich anders: *"Wenn keine Flaeche bei
         * Parcel Ground ausgewaehlt wurde, dann bitte auch kein Gras
         * platzieren."* Leer heisst jetzt AUS, nicht "wie Dekoration".
         *
         * Der Name wird trotzdem aufgeloest - so bleibt die Pruefung auf
         * vorhandene Prefabs unveraendert, und beim Wiedereinschalten muss
         * nichts nachgeladen werden.
         */
        private bool ZoningFlaecheAus
            => string.IsNullOrEmpty(_uiSystem?.FlaecheZoning);

        private string ZoningSurfaceName
            => ZoningFlaecheAus ? GrassSurfaceName : _uiSystem.FlaecheZoning;
        private string PavementSurfaceName
            => _uiSystem?.FlaecheStrasse ?? PavementSurfaceWerk;

        /** Merkt, mit welchen Namen die Prefabs aufgeloest wurden. */
        private string _aufgeloestGras;
        private string _aufgeloestBelag;
        private string _aufgeloestZoning;
        private Entity _zoningSurfacePrefab;
        private bool _missingZoningPrefabLogged;
        private const float MaxCourseHeightDeviation = 50f;

        private PrefabSystem _prefabSystem;
        private EntityQuery _surfacePrefabQuery;
        private EntityQuery _definitionQuery;
        private Entity _grassSurfacePrefab = Entity.Null;
        private Entity _vorflaechenPrefab = Entity.Null;
        private bool _vorflaechenPrefabAusstehend;
        private bool _vorflaechenPrefabFehlgeschlagen;
        private Entity _pavementSurfacePrefab = Entity.Null;

        /**
         * Wieviel hoeher der Zoningbelag zeichnet als sein Vorbild.
         *
         * `ManagedBatchSystem`: renderQueue = shader.renderQueue +
         * m_RendererPriority. Hoeher heisst spaeter und damit sichtbar oben.
         *
         * PLUS EINS, UND ZWAR GEMESSEN.
         *
         * Die vollstaendige Aufstellung aller 26 Vanilla-Flaechen vom
         * 2026-09-02 zeigt eine Skala von nur sechs Werten:
         *
         *     -100  Agriculture, Ore, Oil, Forestry, Landfill
         *      -99  Grass
         *      -98  Sand
         *      -97  Concrete
         *      -96  Pavement          <- unser Vorbild
         *      -95  Tiles             <- hoechster Wert, den CS2 vergibt
         *
         * Der erste Anlauf nahm 10, blind gewaehlt. Das ergab -86 und lag
         * damit weit ausserhalb dessen, was das Spiel ueberhaupt benutzt -
         * die Flaeche war im Spiel nicht hoeher, sondern GANZ WEG. Befund
         * des Nutzers: *"Zoning-Strasse hat keine Flaeche bekommen."*
         *
         * ZIELWERT -94, AM GEGNER GEMESSEN.
         *
         * -95 war der hoechste von CS2 vergebene Wert, und ich hielt ihn
         * deshalb fuer sicher. Am 2026-09-03 hat die Flaechenwache im Spiel
         * nachgesehen, WER dort eigentlich oben liegt:
         *
         *     Tiles Surface 03 (Prioritaet -95, gehoert zu
         *     NA_CommercialLow01_L1_3x6 [GEBAEUDE])
         *
         * Das Gebaeude pflastert mit demselben Wert wie wir. Bei
         * Gleichstand entscheidet die Reihenfolge nichts mehr - mal gewinnt
         * die eine Flaeche, mal die andere. Genau das sah der Nutzer.
         *
         * -94 ist eine Stufe darueber und damit die kleinste Aenderung, die
         * den Gleichstand aufloest. Weiter zu gehen waere wieder Raten:
         * dieselbe Wache nennt jederzeit den Wert, gegen den wir antreten.
         *
         * ABSOLUT, NICHT ALS AUFSCHLAG. Zuerst stand hier "+2", also ein
         * Zuschlag auf den Wert des Vorbilds. Dasselbe "+2" landete damit je
         * nach Flaeche woanders: Pavement (-96) kam auf -94, Gras (-99) nur
         * auf -97. Der Nutzer verlangte fuer BEIDE -94 - mit einem Aufschlag
         * haette ich ihm etwas zugesagt, was der Code nicht liefert.
         */
        private const int ZoningBelagPrioritaet = -94;
        private Entity _zoningBelagPrefab = Entity.Null;
        private Entity _dekoBelagPrefab = Entity.Null;
        private Entity _zoningBodenPrefab = Entity.Null;
        private ParkingLayout _areaPreviewLayout;
        private LayoutSettings _areaPreviewSettings;
        private bool _ghostsActive;
        private bool _missingGrassPrefabLogged;
        private bool _missingPavementPrefabLogged;
        private long _lastPreviewSig = long.MinValue;

        private void InitializeAreaPreview()
        {
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _apronPrefabSystem = World
                .GetOrCreateSystemManaged<ParkingLotApronPrefabSystem>();
            _definitionQuery = GetDefinitionQuery();
            _surfacePrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<SurfaceData>(),
                ComponentType.ReadOnly<AreaData>(),
                ComponentType.ReadOnly<AreaGeometryData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());
            InitializeAreaDiagnostics();
        }

        private void SetAreaPreviewLayout(ParkingLayout layout, LayoutSettings settings)
        {
            _areaPreviewLayout = layout;
            _areaPreviewSettings = settings;
            BeginAreaTransfer(layout);
        }

        private void ClearAreaPreviewLayout(string context)
        {
            _areaPreviewLayout = null;
            _areaPreviewSettings = null;
            ClearAreaPreviewGhosts(context);
        }

        private void SyncAreaPreview()
        {
            var wantPreview = _closed && HasPreviewPolygons(_areaPreviewLayout);
            if (!wantPreview)
            {
                if (_ghostsActive) ClearAreaPreviewGhosts("preview no longer requested");
                return;
            }

            if (!ResolveSurfacePrefabs())
            {
                _areaTransferNote = $"Die Prefabs '{GrassSurfaceName}' und "
                    + $"'{PavementSurfaceName}' sind nicht beide auflösbar; "
                    + "keine Fläche wurde an CS2 übergeben.";
                if (_ghostsActive) ClearAreaPreviewGhosts("surface prefab unavailable");
                return;
            }

            /*
             * NICHT OHNE VORFLAECHE WEITERBAUEN.
             *
             * Die Anforderung entsteht in ToolUpdate. Angemeldet wird der
             * Klon absichtlich erst im folgenden PrefabUpdate; danach muss
             * AreaBatchSystem noch den eigenen Materialstapel bauen. Solange
             * einer dieser Schritte fehlt, werden auch die uebrigen
             * Flaechendefinitionen nicht erzeugt. Sonst wuerde Enter den
             * Parkplatz still ohne Vorflaeche festschreiben.
             */
            /*
             * Der Klon wird AUCH fuer die Zoning-Strasse gebraucht, nicht nur
             * fuer die Vorflaeche. Beide liegen auf einer Strasse, und dort
             * ist eine Flaeche mit der Ebenenmaske `Terrain` unsichtbar. Es
             * ist derselbe Klon desselben Belagprefabs - er wird einmal
             * angemeldet und von beiden benutzt.
             */
            var zoningstrasseVorhanden =
                _areaPreviewLayout?.ZoningRoadSurface != null
                && _areaPreviewLayout.ZoningRoadSurface.Length > 0;
            var vorflaecheGewuenscht = (_uiSystem?.VorflaecheAn ?? true)
                && _vorflaechen != null && _vorflaechen.Length > 0;
            _vorflaechenPrefabFehlgeschlagen = false;
            var vorflaecheAufgegeben = false;
            _vorflaechenPrefab = vorflaecheGewuenscht
                ? VorflaechenPrefab(_pavementSurfacePrefab,
                    out _vorflaechenPrefabFehlgeschlagen,
                    out vorflaecheAufgegeben)
                : Entity.Null;
            /*
             * EIGENER KLON FUER DEN ZONINGBELAG.
             *
             * Er braucht dieselbe Decal-Ebene `Roads` wie die Vorflaeche,
             * aber eine HOEHERE Zeichenprioritaet: sonst deckt die Flaeche
             * eines gewachsenen Gebaeudes ihn zu. Befund des Nutzers vom
             * 2026-09-02.
             *
             * 10 ist bewusst grosszuegig gewaehlt und nicht am Vanillawert
             * gemessen - der Aufschlag steht im Log, und falls er nicht
             * reicht, sieht man dort sofort, von welchem Ausgangswert aus.
             */
            _zoningBelagPrefab = zoningstrasseVorhanden
                ? VorflaechenPrefab(_pavementSurfacePrefab,
                    out _, out _, ZoningBelagPrioritaet)
                : Entity.Null;
            _dekoBelagPrefab = VorflaechenPrefab(_grassSurfacePrefab,
                out _, out _, ZoningBelagPrioritaet);
            /*
             * DER PARZELLENBODEN GEHOERT AUCH DAZU.
             *
             * Der Nutzer nach dem ersten Erfolg: *"Das Gleiche dann fuer
             * Surface, also den Belag fuer die Zoningflaeche selbst, also
             * Parcel Ground."* Dieselbe Behandlung, derselbe Zielwert -
             * dort sitzt das Gebaeude ja unmittelbar drauf.
             */
            _zoningBodenPrefab = ZoningFlaecheAus
                ? Entity.Null
                : VorflaechenPrefab(_zoningSurfacePrefab,
                    out _, out _, ZoningBelagPrioritaet);
            /*
             * WARTEN JA, ABER NICHT EWIG.
             *
             * Kommt der Klon nach der Aufgabegrenze nicht zustande, gilt er
             * weder als ausstehend noch als Fehler: der Parkplatz wird ohne
             * Vorflaeche gebaut. Ein Werkzeug, das gar nicht mehr baut, waere
             * der schlimmere Ausfall - der Parkplatz ist die Hauptsache, die
             * Vorflaeche ist Zierde. Das Log nennt dabei jeden einzelnen
             * Abnahmewert, der Ausfall ist also nicht still.
             */
            _vorflaechenPrefabAusstehend = vorflaecheGewuenscht
                && _vorflaechenPrefab == Entity.Null
                && !_vorflaechenPrefabFehlgeschlagen
                && !vorflaecheAufgegeben;
            if (_vorflaechenPrefabAusstehend
                || _vorflaechenPrefabFehlgeschlagen)
            {
                _areaTransferNote = _vorflaechenPrefabFehlgeschlagen
                    ? "Das eigene Vorflächen-Prefab ist fehlgeschlagen; "
                        + "es wurde keine Fläche an CS2 übergeben."
                    : "Das eigene Vorflächen-Prefab wird von CS2 "
                        + "initialisiert; es wurde noch keine Fläche übergeben.";
                if (_ghostsActive)
                    ClearAreaPreviewGhosts("apron prefab not ready");
                return;
            }

            var signature = AreaPreviewSignature(_areaPreviewLayout);
            if (signature == _lastPreviewSig) return;

            // Clear verwirft die Temp-Areas des vorigen Stands. Die neuen
            // Definitionen bleiben absichtlich unapplied und damit Vorschau.
            applyMode = ApplyMode.Clear;
            if (!TryDestroyDefinitionEntities("area preview replacement")) return;

            try
            {
                ProtokolliereBauschritt("CreateAreaPreviewDefinitions");
                var created = CreateAreaPreviewDefinitions(_areaPreviewLayout);
                _ghostsActive = created > 0;
                _areaTransferCreatedFrame = created > 0
                    ? UnityEngine.Time.frameCount : -1;
                // Auch eine wegen unplausibler Hoehen verworfene Vorschau wird
                // erst nach einer Geometrieaenderung erneut abgetastet. Sonst
                // wuerde waitForPending samt Warnung in jedem Frame laufen.
                _lastPreviewSig = signature;
                // `created` sind ALLE Vorschau-Entitaeten - Flaechen, Wege,
                // Aufkleber, Besitzer. Die Flaechen allein nennt die Zeile
                // aus `CreateAreaPreviewDefinitions`.
                Mod.log.Info($"PLT-Flächenvorschau erzeugt: {created} "
                    + "Vorschau-Objekte (Flächen, Wege, Aufkleber) mit "
                    + $"'{GrassSurfaceName}' und '{PavementSurfaceName}'.");
                ParkingLotLiveLog.Zeile("vorschau " + created
                    + " objekte gesamt");
            }
            catch (Exception exception)
            {
                applyMode = ApplyMode.Clear;
                var definitionsCleared = TryDestroyDefinitionEntities(
                    "failed area preview creation");
                _ghostsActive = !definitionsCleared;
                _lastPreviewSig = long.MinValue;
                _areaTransferNote = "Die Flächendefinitionen konnten nicht vollständig "
                    + "erzeugt werden; Details stehen in Diagnostics.PreviewMessages.";
                RecordPreviewDiagnostic("Error",
                    "PLT-Flächenvorschau konnte nicht erzeugt werden.", exception);
                Mod.log.Error(exception, "PLT-Flächenvorschau konnte nicht erzeugt werden.");
            }
        }

        private void ClearAreaPreviewGhosts(string context)
        {
            RefreshAreaTransferAudit();
            // Temp-Areas werden ausschließlich über die Tool-Transaktion
            // verworfen. Direktes Löschen in PostTool würde mit CS2s eigener
            // SubElementDeleteSystem-Kaskade konkurrieren.
            applyMode = ApplyMode.Clear;
            var definitionsCleared = TryDestroyDefinitionEntities(context);
            // Bei einem transienten ECS-Fehler bleibt der Merker gesetzt, damit
            // ein weiterlaufendes Werkzeug im nächsten Frame erneut aufräumt.
            _ghostsActive = !definitionsCleared;
            _lastPreviewSig = long.MinValue;
        }

        private bool TryDestroyDefinitionEntities(string context)
        {
            try
            {
                if (!_definitionQuery.IsEmptyIgnoreFilter)
                    EntityManager.DestroyEntity(_definitionQuery);
                return true;
            }
            catch (Exception exception)
            {
                RecordPreviewDiagnostic("Error",
                    $"Vorschau-Definitionen konnten nach '{context}' nicht entfernt werden.",
                    exception);
                Mod.log.Error(exception,
                    $"PLT konnte Vorschau-Definitionen nach '{context}' nicht entfernen; "
                    + "ApplyMode.Clear bleibt gesetzt.");
                return false;
            }
        }

        private bool ResolveSurfacePrefabs()
        {
            // Wechselt der Nutzer die Flaeche, ist der gemerkte Entity von
            // gestern - ohne diese zwei Zeilen baute der Mod stur weiter mit
            // dem alten Prefab, und die Einstellung waere wirkungslos.
            if (_aufgeloestGras != GrassSurfaceName)
            {
                _grassSurfacePrefab = Entity.Null;
                _missingGrassPrefabLogged = false;
                _aufgeloestGras = GrassSurfaceName;
            }
            if (_aufgeloestBelag != PavementSurfaceName)
            {
                _pavementSurfacePrefab = Entity.Null;
                _missingPavementPrefabLogged = false;
                _aufgeloestBelag = PavementSurfaceName;
            }
            if (_aufgeloestZoning != ZoningSurfaceName)
            {
                _zoningSurfacePrefab = Entity.Null;
                _missingZoningPrefabLogged = false;
                _aufgeloestZoning = ZoningSurfaceName;
            }
            var grass = ResolveSurfacePrefab(GrassSurfaceName, ref _grassSurfacePrefab,
                ref _missingGrassPrefabLogged);
            var pavement = ResolveSurfacePrefab(PavementSurfaceName,
                ref _pavementSurfacePrefab, ref _missingPavementPrefabLogged);
            var zoning = ResolveSurfacePrefab(ZoningSurfaceName,
                ref _zoningSurfacePrefab, ref _missingZoningPrefabLogged);
            return grass && pavement && zoning;
        }

        private bool ResolveSurfacePrefab(string prefabName, ref Entity resolved,
                                          ref bool missingLogged)
        {
            if (HasUsableSurfacePrefab(resolved)) return true;

            resolved = Entity.Null;
            using var prefabs = _surfacePrefabQuery.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < prefabs.Length; i++)
            {
                var entity = prefabs[i];
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                    || prefab == null || !prefab.isBuiltin
                    || !string.Equals(prefab.name, prefabName,
                        StringComparison.OrdinalIgnoreCase)
                    || !HasUsableSurfacePrefab(entity))
                    continue;

                resolved = entity;
                missingLogged = false;
                Mod.log.Info($"PLT-Flächenvorschau verwendet '{prefabName}'.");
                return true;
            }

            if (!missingLogged)
            {
                missingLogged = true;
                RecordPreviewDiagnostic("Warning",
                    $"Flächenvorschau wartet auf das Vanilla-Prefab '{prefabName}'.");
                Mod.log.Warn($"PLT-Flächenvorschau wartet auf das Vanilla-Prefab "
                    + $"'{prefabName}'.");
            }
            return false;
        }

        private bool HasUsableSurfacePrefab(Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                || !EntityManager.HasComponent<AreaData>(prefab))
                return false;

            var areaData = EntityManager.GetComponentData<AreaData>(prefab);
            return areaData.m_Archetype.Valid;
        }

        private int CreateAreaPreviewDefinitions(ParkingLayout layout)
        {
            var strasseAn = _uiSystem?.FlaecheStrasseAn ?? true;
            var dekoAn = _uiSystem?.FlaecheDekoAn ?? true;
            layout.SurfacesForPlacement(
                strasseAn, dekoAn, out var gras, out var asphalt);
            /*
             * Die Vorflaeche braucht ihr eigenes Prefab (Decal-Ebene `Roads`,
             * siehe ParkingLotApronPrefab). Wenn es hier eine Vorflaeche gibt,
             * ist ihr Prefab bereits samt eigenem Materialstapel geprueft;
             * andernfalls haette SyncAreaPreview vor diesem Aufruf gewartet.
             */
            var vorflaechenPrefab = _vorflaechenPrefab;
            var vorflaechen = vorflaechenPrefab != Entity.Null
                ? _vorflaechen : Array.Empty<float2[]>();

            /*
             * EINE FLAECHE, NICHT ZWEI - HIER WIRD SIE ES.
             *
             * Die Vorflaeche wird in den Asphaltring eingesetzt, statt an ihn
             * angelegt. Danach gibt es an der Polygonkante keine zwei Raender
             * mehr, die aneinanderstossen koennten. Was sich nicht einsetzen
             * laesst, bleibt eigenstaendig - das steht dann in der Meldung
             * und ist nicht still.
             *
             * Der verschmolzene Ring reicht ueber die Polygonkante hinaus bis
             * auf die Strasse. Er braucht deshalb das Vorflaechen-Prefab mit
             * der Decal-Ebene `Roads`; der Vanilla-Belag wuerde dort
             * unsichtbar bleiben. Der uebrige Asphalt behaelt sein Prefab.
             */
            var verschmolzen = Array.Empty<float2[]>();
            var einzelneVorflaechen = vorflaechen;
            if (vorflaechen.Length > 0)
            {
                VerschmelzeVorflaechen(asphalt, out verschmolzen,
                    out var uebrigerAsphalt, out einzelneVorflaechen, out _);
                asphalt = uebrigerAsphalt;
            }

            BeginAreaTransfer(layout, gras, asphalt);
            // Ohne das Warten kann der CPU-Schnappschuss noch unterwegs sein
            // und fuer nahe Punkte voellig verschiedene Hoehen liefern.
            var heightData = _terrainSystem.GetHeightData(waitForPending: true);
            var sampled = 0;
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            MeasureAreaPreviewGroup(gras, ref heightData,
                ref sampled, ref minimum, ref maximum);
            MeasureAreaPreviewGroup(asphalt, ref heightData,
                ref sampled, ref minimum, ref maximum);
            // Die Vorflaeche liegt AUSSERHALB des Polygons, also ausserhalb
            // des schon gemessenen Bereichs. Ohne diese Zeile fehlten ihre
            // Knoten in der Terrainpruefung.
            MeasureAreaPreviewGroup(einzelneVorflaechen, ref heightData,
                ref sampled, ref minimum, ref maximum);
            MeasureAreaPreviewGroup(verschmolzen, ref heightData,
                ref sampled, ref minimum, ref maximum);

            if (sampled == 0)
            {
                SetAreaTerrainTransfer(0, 0f, 0f, 0f,
                    limitTriggered: false,
                    "Keine übergabefähigen Flächenknoten vorhanden.");
                return 0;
            }

            var span = maximum - minimum;
            SetAreaTerrainTransfer(sampled, minimum, maximum, span,
                span > MaxCourseHeightDeviation,
                "TerrainUtils.SampleHeight an jedem offenen Knoten der geplanten Flächen.");
            Mod.log.Info($"PLT-Flächenvorschau Terrainhöhen: {sampled} Knoten, "
                + $"Minimum {minimum:F2} m, Maximum {maximum:F2} m, "
                + $"Spanne {span:F2} m.");
            if (span > MaxCourseHeightDeviation)
            {
                // Eine solche Spanne innerhalb eines Parkplatzes kennzeichnet
                // einen unbrauchbaren Schnappschuss. Keine falschen Knoten an
                // CS2 weitergeben; die Warnung macht den Ausfall sichtbar.
                RecordPreviewDiagnostic("Warning",
                    $"Flächenvorschau wegen Terrainspanne verworfen: {span:G9} m > "
                    + $"{MaxCourseHeightDeviation:G9} m (Minimum {minimum:G9} m, "
                    + $"Maximum {maximum:G9} m).");
                Mod.log.Warn($"PLT-Flächenvorschau wegen unplausibler Terrainhöhen "
                    + $"verworfen: {span:F2} m > {MaxCourseHeightDeviation:F0} m "
                    + $"(Minimum {minimum:F2} m, Maximum {maximum:F2} m).");
                return 0;
            }

            var created = 0;
            // Zuerst, damit die Besitzerflaeche schon steht, wenn die Kinder
            // kommen - die Reihenfolge im Puffer ist zwar egal, aber so
            // taucht sie im Abzug oben auf.
            ProtokolliereBauschritt("CreateLotOwnerDefinition");
            created += CreateLotOwnerDefinition(ref heightData);
            /**
             * HIER wirken die Flaechenschalter unabhaengig - und NUR hier.
             *
             * Das Layout ist zu diesem Zeitpunkt fertig gerechnet; Buchten,
             * Wege, Zufahrten und Aufkleber entstehen unabhaengig davon. Wer
             * eine Flaeche abschaltet, bekommt denselben Parkplatz auf dem
             * vorhandenen Boden - fuer alle, die lieber mit dem Terrain-Brush
             * arbeiten.
             */
            /*
             * FLAECHEN GETRENNT ZAEHLEN.
             *
             * `created` ist die Zahl ALLER Vorschau-Entitaeten: Besitzer,
             * Flaechen, Wege, Aufkleber. Die Logzeile darunter nannte sie
             * bis zum 2026-09-01 "Polygonflaechen" - und meldete damit 445,
             * wo 47 Flaechen geplant waren. Das sah nach einem schweren
             * Fehler aus und war einer im Bericht, nicht im Bau.
             */
            var flaechen = 0;
            /*
             * DIE DEKOFLAECHE BEKOMMT DIESELBE BEHANDLUNG WIE DER
             * ZONINGBELAG - auf Wunsch des Nutzers vom 2026-09-03.
             *
             * Also den Klon mit `Terrain | Roads` und der hoeheren
             * Zeichenprioritaet. MIT RUECKFALL: ist der Klon nicht fertig,
             * wird wie bisher mit dem Vanilla-Prefab gebaut. Ohne diesen
             * Rueckfall haenge das Gras jedes Parkplatzes daran, dass ein
             * Laufzeitprefab rechtzeitig entsteht - ein zu grosser Einsatz
             * fuer eine Verbesserung an einer Stelle.
             *
             * ZWEIFEL, AUSDRUECKLICH: der Nutzer hat selbst beschrieben,
             * dass ein Gebaeude seine Flaeche bis zur Strasse AUFZIEHT.
             * Dagegen hilft keine Einstellung an unserer Flaeche - sie wird
             * nicht verdraengt, sondern ueberwachsen. Ob das hier etwas
             * bringt, ist offen.
             */
            var grasPrefab = _dekoBelagPrefab != Entity.Null
                ? _dekoBelagPrefab
                : _grassSurfacePrefab;
            if (dekoAn)
                flaechen += CreateAreaPreviewGroup("Grass", gras,
                    grasPrefab, ref heightData);
            if (strasseAn)
                flaechen += CreateAreaPreviewGroup("Asphalt", asphalt,
                    _pavementSurfacePrefab, ref heightData);
            /*
             * DAS BAULAND HAT SEINEN EIGENEN SCHALTER NICHT.
             *
             * Es haengt weder an "Strasse" noch an "Dekoration": wer die
             * Dekoflaechen abschaltet, will kahlen Asphalt - aber die
             * Parzellen sind kein Schmuck, sie sind der Grund, warum dort
             * kein Parkplatz ist. Sie werden deshalb immer gesetzt.
             */
            if (!ZoningFlaecheAus
                && layout.ZoningSurface != null
                && layout.ZoningSurface.Length > 0)
                flaechen += CreateAreaPreviewGroup("Zoning",
                    layout.ZoningSurface,
                    // Mit Rueckfall wie bei der Dekoflaeche: klappt der Klon
                    // nicht, wird mit dem Vanilla-Prefab gebaut statt gar
                    // nicht.
                    _zoningBodenPrefab != Entity.Null
                        ? _zoningBodenPrefab
                        : _zoningSurfacePrefab,
                    ref heightData);
            /*
             * Immer gesetzt, wie die Parzellen selbst: ohne diesen Belag
             * saehe man Autos ueber Gras fahren, denn die Zoning-Strasse ist
             * unsichtbar. Das ist keine Zierde, sondern die Fahrbahn.
             */
            if (layout.ZoningRoadSurface != null
                && layout.ZoningRoadSurface.Length > 0)
            {
                if (_zoningBelagPrefab != Entity.Null)
                {
                    flaechen += CreateAreaPreviewGroup("Zoningstrasse",
                        layout.ZoningRoadSurface, _zoningBelagPrefab,
                        ref heightData);
                }
                else
                {
                    /*
                     * NICHT STILL UEBERSPRINGEN.
                     *
                     * Genau diese Bauart - eine Flaeche faellt aus, und
                     * nirgends steht warum - hat am 2026-09-02 einen halben
                     * Nachmittag gekostet. Der Nutzer fragte daraufhin, ob
                     * der Bauzettel so etwas ueberhaupt meldet. Tut er
                     * nicht; er haelt die gewaehlten Flaechen fest, damit
                     * sich derselbe Parkplatz nachbauen laesst. Ein Ausfall
                     * gehoert deshalb mindestens ins Log und in den Abzug.
                     */
                    RecordPreviewDiagnostic("Warning",
                        $"Belag der Zoning-Straße nicht gesetzt: "
                        + $"{layout.ZoningRoadSurface.Length} Ring(e) "
                        + "warten auf den Prefabklon mit der Ebene Roads. "
                        + "Die Straße bleibt befahrbar, sieht aber wie Gras "
                        + "aus.");
                    Mod.log.Warn("PLT-Zoningstrasse: Belag nicht gesetzt - "
                        + layout.ZoningRoadSurface.Length
                        + " Ring(e), aber der Prefabklon ist noch nicht "
                        + "benutzbar.");
                }
            }
            if (strasseAn && verschmolzen.Length > 0)
                flaechen += CreateAreaPreviewGroup("Asphalt mit Vorflaeche",
                    verschmolzen, vorflaechenPrefab, ref heightData);
            if (strasseAn && einzelneVorflaechen.Length > 0)
                flaechen += CreateAreaPreviewGroup("Vorflaeche",
                    einzelneVorflaechen, vorflaechenPrefab, ref heightData);
            created += flaechen;
            // Die beiden Zoning-Listen zaehlen mit: sie werden immer
            // gesetzt, unabhaengig von den Schaltern fuer Strasse und
            // Dekoration. Fehlten sie im Soll, meldete der Zaehler
            // "vollstaendig", waehrend Parzellen oder Fahrbahn fehlten.
            var geplant = (dekoAn ? gras.Length : 0)
                + (strasseAn ? asphalt.Length + verschmolzen.Length
                    + einzelneVorflaechen.Length : 0)
                + (ZoningFlaecheAus ? 0 : layout.ZoningSurface?.Length ?? 0)
                + (layout.ZoningRoadSurface?.Length ?? 0);
            ParkingLotLiveLog.Zeile("flaechen " + flaechen + "/" + geplant
                + " gesetzt | gras " + gras.Length
                + " asphalt " + asphalt.Length
                + " bauland " + (layout.ZoningSurface?.Length ?? 0)
                + " zoningstrasse " + (layout.ZoningRoadSurface?.Length ?? 0)
                + " vorflaeche " + (verschmolzen.Length
                    + einzelneVorflaechen.Length)
                + " | entitaeten gesamt folgt");
            // Wege und Decals haengen an derselben Vorschau-Transaktion. Ein
            // einziges ApplyMode.Apply macht spaeter alles zusammen dauerhaft;
            // getrennte Durchgaenge wuerden auseinanderlaufende Zustaende
            // erzeugen, sobald einer davon fehlschlaegt.
            ProtokolliereBauschritt("CreateNetDefinitions");
            created += CreateNetDefinitions(layout, _areaPreviewSettings,
                ref heightData);
            ProtokolliereBauschritt("CreateBayDecalDefinitions");
            created += CreateBayDecalDefinitions(layout, _areaPreviewSettings,
                ref heightData);
            ProtokolliereBauschritt("CreateVegetationDefinitions");
            created += CreateVegetationDefinitions(dekoAn ? gras : Array.Empty<float2[]>(), ref heightData);
            ProtokolliereBauschritt("CreateEntranceArrowDefinitions");
            created += CreateEntranceArrowDefinitions(layout, ref heightData);
            return created;
        }

        private static void MeasureAreaPreviewGroup(
            float2[][] polygons,
            ref TerrainHeightData heightData,
            ref int sampled,
            ref float minimum,
            ref float maximum)
        {
            if (polygons == null) return;

            for (var polygonIndex = 0; polygonIndex < polygons.Length; polygonIndex++)
            {
                var polygon = polygons[polygonIndex];
                var nodeCount = OpenNodeCount(polygon);
                if (nodeCount < 3) continue;

                for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
                {
                    var point = polygon[nodeIndex];
                    if (!math.all(math.isfinite(point)))
                        throw new InvalidOperationException(
                            "Eine Polygonfläche enthält eine nicht-endliche Koordinate.");

                    var height = TerrainUtils.SampleHeight(
                        ref heightData, new float3(point.x, 0f, point.y));
                    if (!math.isfinite(height))
                        throw new InvalidOperationException(
                            "Die Terrain-Abtastung einer Polygonfläche ist nicht endlich.");

                    minimum = math.min(minimum, height);
                    maximum = math.max(maximum, height);
                    sampled++;
                }
            }
        }

        private int CreateAreaPreviewGroup(
            string kind,
            float2[][] polygons,
            Entity prefab,
            ref TerrainHeightData heightData)
        {
            if (polygons == null) return 0;

            ProtokolliereBauschritt("CreateAreaPreviewGroup " + kind);
            var created = 0;
            for (var i = 0; i < polygons.Length; i++)
                if (CreateAreaPreviewDefinition(
                    kind, i, polygons[i], prefab, ref heightData))
                    created++;
            return created;
        }

        private bool CreateAreaPreviewDefinition(
            string kind,
            int index,
            float2[] polygon,
            Entity prefab,
            ref TerrainHeightData heightData)
        {
            var nodeCount = OpenNodeCount(polygon);
            if (nodeCount < 3) return false;

            for (var i = 0; i < nodeCount; i++)
                if (!math.all(math.isfinite(polygon[i])))
                    throw new InvalidOperationException(
                        "Eine Polygonfläche enthält eine nicht-endliche Koordinate.");

            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = prefab,
            });
            EntityManager.AddComponent<Updated>(definition);

            // Wie CS2s AreaToolSystem: beliebig viele, auch konkave Knoten;
            // der letzte Puffereintrag schließt den Ring explizit.
            var nodes = EntityManager.AddBuffer<Game.Areas.Node>(definition);
            nodes.ResizeUninitialized(nodeCount + 1);
            var sentNodes = new float3[nodeCount + 1];
            for (var i = 0; i < nodeCount; i++)
            {
                var point = polygon[i];
                var height = TerrainUtils.SampleHeight(
                    ref heightData, new float3(point.x, 0f, point.y));
                sentNodes[i] = new float3(point.x, height, point.y);
                nodes[i] = new Game.Areas.Node(sentNodes[i], float.MinValue);
            }
            nodes[nodeCount] = nodes[0];
            sentNodes[nodeCount] = sentNodes[0];
            RecordAreaDefinition(kind, index, prefab,
                definition, sentNodes);
            return true;
        }

        private long AreaPreviewSignature(ParkingLayout layout)
        {
            unchecked
            {
                var signature = 1125899906842597L;
                signature = AppendSettings(signature, _areaPreviewSettings);
                // Die gemerkte Reglerbreite bleibt auch bei abgeschaltetem
                // Mittelgruen Teil des Bauzettels. Md allein ist dann immer 0
                // und koennte eine echte Einstellungsänderung nicht erkennen.
                signature = AppendDouble(signature,
                    _uiSystem?.AktuelleMedianbreite ?? _areaPreviewSettings?.Md ?? 0);
                signature = signature * 31
                    + ((_uiSystem?.MittelgruenAn ?? (_areaPreviewSettings?.Md > 0))
                        ? 1 : 0);
                signature = AppendDouble(signature,
                    _uiSystem?.AktuelleQuerbuchten ?? 0);
                signature = AppendPolygonGroup(signature, layout.GrassSurface);
                signature = AppendPolygonGroup(signature, layout.AsphaltSurface);
                signature = AppendPolygonGroup(signature, layout.ZoningRoadSurface);
                signature = AppendPolygonGroup(signature, layout.ZoningSurface);
                signature = AppendPolygonGroup(signature, _vorflaechen);
                signature = signature * 31 + ((_uiSystem?.VorflaecheAn ?? true) ? 1 : 0);
                signature = AppendText(signature, GrassSurfaceName);
                signature = AppendText(signature, PavementSurfaceName);
                signature = signature * 31 + _vorflaechenPrefab.Index;
                signature = signature * 31 + _vorflaechenPrefab.Version;
                // Ohne diese zwei Zeilen bliebe die Vorschau stehen, wenn nur
                // ein Schalter umgelegt wird: die Geometrie ist ja dieselbe.
                signature = signature * 31 + ((_uiSystem?.FlaecheStrasseAn ?? true) ? 1 : 0);
                signature = signature * 31 + ((_uiSystem?.FlaecheDekoAn ?? true) ? 1 : 0);
                signature = signature * 31 + ((_uiSystem?.Buchtsymbole ?? true) ? 1 : 0);
                signature = signature * 31 + (_uiSystem?.VegetationJson.GetHashCode() ?? 0);
                signature = signature * 31 + _grassSurfacePrefab.Index;
                signature = signature * 31 + _grassSurfacePrefab.Version;
                signature = signature * 31 + _zoningSurfacePrefab.Index;
                signature = signature * 31 + _zoningSurfacePrefab.Version;
                // Ein Zustand, der das Ergebnis aendert, gehoert in den
                // Schluessel - sonst bliebe die Vorschau nach dem Waehlen
                // einer Bezugslinie stehen.
                signature = signature * 31
                    + (Ausrichtwinkel?.GetHashCode() ?? 0);
                signature = signature * 31 + _pavementSurfacePrefab.Index;
                signature = signature * 31 + _pavementSurfacePrefab.Version;
                // Wege und Decals folgen demselben Layout, haengen aber an
                // eigenen Prefabs. Ohne sie bliebe eine Vorschau stehen, die
                // erzeugt wurde, bevor diese Prefabs aufloesbar waren.
                signature = AppendPolygonGroup(signature, layout.AisleLine);
                signature = AppendPolygonGroup(signature, layout.CrossLine);
                signature = AppendPolygonGroup(signature, layout.PerimeterLine);
                signature = AppendPolygonGroup(signature, layout.EntranceLine);
                signature = AppendPolygonGroup(signature, layout.Bay);
                signature = signature * 31 + _pathPrefabs.Count;
                signature = signature * 31 + _bayDecalPrefab.Index;
                signature = signature * 31 + _bayDecalPrefab.Version;
                signature = signature * 31 + _disabledDecalPrefab.Index;
                signature = signature * 31 + _electricDecalPrefab.Index;
                return signature;
            }
        }

        private static long AppendSettings(long signature, LayoutSettings settings)
        {
            unchecked
            {
                if (settings == null) return signature * 31 - 1;
                signature = AppendDouble(signature, settings.Es);
                signature = AppendDouble(signature, settings.Ai);
                signature = AppendDouble(signature, settings.Cw);
                signature = AppendDouble(signature, settings.Sl);
                signature = AppendDouble(signature, settings.Sw);
                signature = AppendDouble(signature, settings.Md);
                signature = AppendDouble(signature, settings.Cr);
                signature = AppendDouble(signature, settings.Angle);
                signature = AppendDouble(signature, settings.KantenVersatz);
                signature = signature * 31 + (settings.Qk ? 1 : 0);
                signature = signature * 31 + (settings.Randstrassen ? 1 : 0);
                signature = signature * 31 + (settings.Auto ? 1 : 0);
                signature = signature * 31 + (settings.AutomaticEntrances ? 1 : 0);
                signature = signature * 31 + (settings.Zellen ? 1 : 0);
                signature = signature * 31 + (settings.EineFlaeche ? 1 : 0);
                signature = signature * 31 + (settings.NoNotch ? 1 : 0);
                signature = signature * 31 + (settings.Single ? 1 : 0);
                signature = signature * 31 + (settings.NoHalf ? 1 : 0);
                signature = AppendText(signature, settings.AngleMode);
                /*
                 * DIE BAULANDFLAECHEN GEHOEREN IN DIE SIGNATUR.
                 *
                 * Ohne sie rechnet die Vorschau nicht neu, wenn eine Flaeche
                 * gesetzt, verschoben oder gedreht wird - und genau das hat
                 * der Nutzer gemeldet: "die Strassen-Preview aenderte sich
                 * nicht nach dem Erstellen der Flaeche". Die Signatur ist die
                 * Stelle, an der eine neue Einstellung am leisesten
                 * verlorengeht: alles rechnet richtig, nur sieht es niemand.
                 */
                var bauland = settings.Zoningflaechen
                    ?? Array.Empty<ParkingGeometry.Zoningflaeche>();
                signature = signature * 31 + bauland.Length;
                foreach (var flaeche in bauland)
                {
                    if (flaeche == null) continue;
                    signature = AppendDouble(signature, flaeche.Ecke.x);
                    signature = AppendDouble(signature, flaeche.Ecke.y);
                    signature = signature * 31 + flaeche.Spalten;
                    signature = signature * 31 + flaeche.Reihen;
                    signature = AppendDouble(signature, flaeche.Winkel);
                }
                var entrances = settings.Entrances ?? Array.Empty<Entrance>();
                signature = signature * 31 + entrances.Length;
                for (var i = 0; i < entrances.Length; i++)
                {
                    var entrance = entrances[i];
                    if (entrance == null)
                    {
                        signature = signature * 31 - 1;
                        continue;
                    }
                    signature = signature * 31 + entrance.Edge;
                    signature = AppendDouble(signature, entrance.Along);
                    signature = AppendText(signature, entrance.Corner);
                    signature = signature * 31 + (int)entrance.Art;
                }
                var ausrichtungen = settings.TeilflaechenAusrichtungen
                    ?? Array.Empty<TeilflaechenAusrichtung>();
                signature = signature * 31 + ausrichtungen.Length;
                for (var i = 0; i < ausrichtungen.Length; i++)
                {
                    var ausrichtung = ausrichtungen[i];
                    if (ausrichtung == null)
                    {
                        signature = signature * 31 - 1;
                        continue;
                    }
                    signature = AppendDouble(signature, ausrichtung.Anker.x);
                    signature = AppendDouble(signature, ausrichtung.Anker.y);
                    signature = AppendDouble(signature, ausrichtung.Winkel);
                }
                return signature;
            }
        }

        private static long AppendDouble(long signature, double value)
        {
            unchecked
            {
                var bits = BitConverter.DoubleToInt64Bits(value);
                signature = signature * 31 + (int)bits;
                return signature * 31 + (int)(bits >> 32);
            }
        }

        private static long AppendText(long signature, string value)
        {
            unchecked
            {
                if (value == null) return signature * 31 - 1;
                signature = signature * 31 + value.Length;
                for (var i = 0; i < value.Length; i++)
                    signature = signature * 31 + value[i];
                return signature;
            }
        }

        private static long AppendPolygonGroup(long signature, float2[][] polygons)
        {
            unchecked
            {
                if (polygons == null) return signature * 31 - 1;
                signature = signature * 31 + polygons.Length;
                for (var i = 0; i < polygons.Length; i++)
                {
                    var polygon = polygons[i];
                    if (polygon == null)
                    {
                        signature = signature * 31 - 1;
                        continue;
                    }

                    signature = signature * 31 + polygon.Length;
                    for (var j = 0; j < polygon.Length; j++)
                    {
                        signature = signature * 31 + math.asint(polygon[j].x);
                        signature = signature * 31 + math.asint(polygon[j].y);
                    }
                }
                return signature;
            }
        }

        private static bool HasPreviewPolygons(ParkingLayout layout)
        {
            return layout != null
                && (HasPreviewPolygon(layout.ZoningSurface)
                    || HasPreviewPolygon(layout.GrassSurface)
                    || HasPreviewPolygon(layout.AsphaltSurface)
                    || HasPreviewPolygon(layout.ZoningRoadSurface));
        }

        private static bool HasPreviewPolygon(float2[][] polygons)
        {
            if (polygons == null) return false;
            for (var i = 0; i < polygons.Length; i++)
                if (OpenNodeCount(polygons[i]) >= 3) return true;
            return false;
        }

        private static int OpenNodeCount(float2[] polygon)
        {
            if (polygon == null) return 0;
            var count = polygon.Length;
            if (count > 1 && math.all(polygon[0] == polygon[count - 1])) count--;
            return count;
        }
    }
}
