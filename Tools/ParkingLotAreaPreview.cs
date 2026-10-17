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
            => _baukontext?.FlaecheDekoration ?? _uiSystem?.FlaecheDekoration ?? GrassSurfaceWerk;

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
            => string.IsNullOrEmpty(_baukontext?.FlaecheZoning ?? _uiSystem?.FlaecheZoning);

        private string ZoningSurfaceName
            => ZoningFlaecheAus ? GrassSurfaceName : _baukontext?.FlaecheZoning ?? _uiSystem.FlaecheZoning;
        private string PavementSurfaceName
            => _baukontext?.FlaecheStrasse ?? _uiSystem?.FlaecheStrasse ?? PavementSurfaceWerk;

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
        /** Klon des Hauptbelags, der auch auf Strassen zeichnet. */
        private Entity _asphaltBelagPrefab = Entity.Null;
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
        // Wert steht in Flaechenklonname: er ist Teil der Klonnamen im Spielstand.
        private const int ZoningBelagPrioritaet = ParkingLotTool.Geometry.Flaechenklonname.Zoning;

        /**
         * EIGENER WERT FUER DIE VORFLAECHE, und das ist Absicht.
         *
         * Die -94 des Zoningbelags hat der Nutzer im September ausdruecklich
         * bestellt; sie bleiben unangetastet. Die Vorflaeche hat seit dem
         * 2026-09-18 eine andere Aufgabe: sie liegt ueber einer SICHTBAREN
         * Strasse.
         *
         * Gemessen an diesem Tag: derselbe Aufschlag, dasselbe Decal, und
         * trotzdem liegt sie mit "Pavement Area Material 01" ueber der
         * Strasse und mit "Sand Area Material 01" darunter. Da
         * `renderQueue = shader.renderQueue + m_RendererPriority` gilt und
         * der Aufschlag gleich war, muss die Queue des MATERIALS
         * unterschiedlich sein.
         *
         * -90 ist der naechste Messpunkt, kein erwiesener Wert. Meine Notiz
         * vom 2026-08-25 sagt, dass Flaechen ausserhalb von etwa -100..-95
         * ganz verschwinden; -94 laeuft trotzdem, die Notiz ist also nicht
         * die ganze Wahrheit. Verschwindet die Flaeche bei -90, ist die
         * Grenze gefunden und wir gehen zurueck.
         */
        // Wert steht in Flaechenklonname: er ist Teil der Klonnamen im Spielstand.
        private const int VorflaechePrioritaet = ParkingLotTool.Geometry.Flaechenklonname.Vorflaeche;
        private Entity _zoningBelagPrefab = Entity.Null;
        private Entity _dekoBelagPrefab = Entity.Null;
        private Entity _zoningBodenPrefab = Entity.Null;
        private ParkingLayout _areaPreviewLayout;

        /*
         * VORVERSUCH. Siehe ParkingLotFlaechennetz.cs; mit Alt+F an und aus.
         *
         * Gefuettert wird genau dort, wo die Vorschau ihr Ergebnis bekommt -
         * also einmal je Lauf und nicht je Bild. Genau das ist die Behauptung,
         * die der Versuch pruefen soll: die Rechenzeit faellt beim Bauen des
         * Netzes an, nicht beim Zeichnen.
         */
        private ParkingLotFlaechennetzSystem _flaechennetz;

        private ParkingLotFlaechennetzSystem Flaechennetz
            => _flaechennetz ??= World
                .GetOrCreateSystemManaged<ParkingLotFlaechennetzSystem>();

        /** Uebernimmt das Netz die Fuellung? Nur wenn es zeichnen kann. */
        private bool FuellungAlsNetz => Flaechennetz.Einsatzbereit;

        /**
         * Gibt dem Netz die GRASRINGE - die Form, die gebaut werden wird.
         *
         * Nicht die Entwurfsteile. Die sind Vierecke, und ein Teppich aus
         * Vierecken ist genau das, was der Nutzer nicht mehr sehen will.
         * `GrassSurface` ist die verschmolzene Form, aus der beim Bauen
         * `Grass Surface 01` entsteht, und sie haengt am Schalter
         * "Dekoration": ist er aus, gibt es nichts zu fuellen, weil nichts
         * gebaut wird.
         *
         * Die Farbe kommt aus dem vorhandenen Vorschau-Stil, nicht aus einem
         * eigenen Wert. Ansage des Nutzers zum Vorversuch: *"Aber halt sehr
         * Gruen waere halt gut wenn das vom aussehen zu dem Passen wuerde was
         * wir derzeit schon haben."* Also genau `GreenColor` - dieselbe
         * Farbe, die die Streifen vorher hatten.
         */
        /**
         * Gibt dem Netz die VERSCHMOLZENEN Ringe - das, was gebaut wird.
         *
         * Nicht die Entwurfsteile. Die ueberlappen sich im Plan, und genau
         * das sah man vorher: Strassenrechtecke uebereinander, Gras unter
         * Buchten, Luecken an schraegen Ecken. Aufgeloest wird das erst beim
         * Verschmelzen, und diese Listen sind das Ergebnis davon.
         *
         * Die Reihenfolge ist die Zeichenreihenfolge: Belag unten, Gras
         * darueber, Zoning zuoberst. Die Vorflaechen laufen mit der Farbe des
         * Belags - sie sind seine Fortsetzung bis zur Strasse und stehen nur
         * deshalb in einer eigenen Liste, weil sie ein anderes Prefab
         * brauchen.
         */
        /**
         * Gibt die Pflanzenliste des Overlays an das Netz weiter.
         *
         * EIGENE METHODE, WEIL ES ZWEI WEGE GIBT, auf denen sich die Liste
         * aendert: ein fertiger Vorschaulauf und der Schalter "Vegetation"
         * im Panel. Der zweite ging beim Umbau auf Instanzen verloren - die
         * Liste fuellte sich, das Netz bekam nichts, und der Nutzer sah
         * keinen einzigen Kreis. Gemeldet am 2026-09-17, gefunden hat es
         * der Zaehler: "keine Pflanzen im Plan | geplant 0".
         *
         * Solange das Overlay die Kreise selbst zeichnete, fiel das nicht
         * auf: dort genuegte die Liste.
         */
        private void FuettereePflanzen()
        {
            if (Flaechennetz == null) return;
            Flaechennetz.FuegePflanzen(_overlay.Pflanzen,
                ParkingLotPreviewStyle.VegetationTreeColor,
                ParkingLotPreviewStyle.VegetationShrubColor,
                ParkingLotPreviewStyle.VegetationTreeDiameter,
                ParkingLotPreviewStyle.VegetationShrubDiameter);
        }

        private void FuettereFlaechennetz(ParkingLayout layout)
        {
            if (!FuellungAlsNetz) return;
            // Die Buchtlinien gehen mit jedem Vorschaulauf mit - sie aendern
            // sich nur, wenn das Layout neu gerechnet wurde.
            Flaechennetz?.FuegeStriche(_overlay.Buchtlinien());
            // Das Overlay faerbt damit die Teilflaechen in ihrer echten Form
            // ein. Es holt sich das System nicht selbst - es ist kein System
            // und hat keine Welt.
            _overlay.Flaechennetz = Flaechennetz;

            FuettereePflanzen();

            /*
             * DIE FARBE KOMMT AUS DER AUSWAHL, NICHT AUS EINER KONSTANTEN.
             *
             * Waehlt der Nutzer Sand als Dekoflaeche, soll die Vorschau
             * sandfarben sein - vor dem Bauen, nicht erst danach. Die Deckung
             * bleibt dabei fest: der Farbton sagt, WAS es wird, die Deckung
             * sorgt dafuer, dass man den Boden darunter noch sieht. Sonst
             * waere eine helle Flaeche gut lesbar und eine dunkle nicht.
             *
             * UND AUS HEISST AUS. Ist ein Schalter in "Surfaces" aus, wird
             * dort nichts gebaut - also darf die Vorschau dort auch nichts
             * fuellen. Der Nutzer am 2026-09-16: *"Die Flaechen ausblenden
             * wir eigentlich schon eingebaut wenn die Surface disabled wird.
             * Musst das glaube noch nachholen."*
             */
            const float deckung = ParkingLotPreviewStyle.FlaechennetzDeckung;
            var belagfarbe = ParkingLotFlaechenfarbe.Hole(PavementSurfaceName,
                ParkingLotPreviewStyle.FlaechennetzBelag, deckung);
            var dekofarbe = ParkingLotFlaechenfarbe.Hole(GrassSurfaceName,
                ParkingLotPreviewStyle.FlaechennetzGruen, deckung);
            var zoningfarbe = ParkingLotFlaechenfarbe.Hole(ZoningSurfaceName,
                ParkingLotPreviewStyle.FlaechennetzZoning, deckung);

            var belagAn = _baukontext?.Zettel.SurfaceRoadOn ?? _uiSystem?.FlaecheStrasseAn ?? true;
            var dekoAn = _baukontext?.Zettel.SurfaceDecorationOn ?? _uiSystem?.FlaecheDekoAn ?? true;
            var vorflaecheAn = belagAn && (_baukontext?.Zettel.SurfaceApronOn ?? _uiSystem?.VorflaecheAn ?? true);

            var netz = Flaechennetz;
            netz.SetzeMuster(_surfaceMusterMitte);
            netz.BeginneFlaechen();
            if (layout != null)
            {
                if (belagAn)
                    netz.FuegeFlaechen(layout.AsphaltSurface, belagfarbe, 0f);
                if (vorflaecheAn)
                    netz.FuegeFlaechen(_vorflaechenSicht, belagfarbe, 0f);
                if (belagAn)
                    netz.FuegeFlaechen(layout.ZoningRoadSurface,
                        belagfarbe, 1f);
                if (dekoAn)
                    netz.FuegeFlaechen(layout.GrassSurface, dekofarbe, 2f);
                if (!ZoningFlaecheAus)
                    netz.FuegeFlaechen(layout.ZoningSurface, zoningfarbe, 3f);
            }
            netz.SchliesseFlaechen();
        }
        private LayoutSettings _areaPreviewSettings;
        private bool _ghostsActive;
        /** Name der Flaeche, die sich nicht aufloesen laesst. */
        private string _unaufloesbareFlaeche;
        private bool _missingGrassPrefabLogged;
        private bool _missingPavementPrefabLogged;
        private long _lastPreviewSig = long.MinValue;

        private void InitializeAreaPreview()
        {
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _apronPrefabSystem = World
                .GetOrCreateSystemManaged<ParkingLotApronPrefabSystem>();
            if (!_bauarbeiter) _definitionQuery = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.ReadOnly<ParkingLotAuftragsdefinition>());
            _surfacePrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<SurfaceData>(),
                ComponentType.ReadOnly<AreaData>(),
                ComponentType.ReadOnly<AreaGeometryData>(),
                ComponentType.Exclude<PlaceholderObjectElement>(),
                // Ein im laufenden Spiel abgewaehltes Asset-Pack: CS2 setzt
                // Deleted und gibt den Prefab-Index einem anderen Prefab.
                ComponentType.Exclude<Deleted>());
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
            EntferneSurfacePreview(context);
            // Kein Layout, keine Fuellung. Steht hier und nicht an den elf
            // Aufrufstellen: `_overlay.ClearLayout()` und diese Methode laufen
            // immer zusammen, und eine von beiden vergisst man sonst.
            _flaechennetz?.Leere();
            _areaPreviewLayout = null;
            _areaPreviewSettings = null;
            ClearAreaPreviewGhosts(context);
        }

        private bool _areaPreviewPrefabsReady;

        private void SyncAreaPreview(bool prefabsOnly = false)
        {
            _areaPreviewPrefabsReady = false;
            var wantPreview = _closed && _points.Count >= MinPolygonPoints && HasPreviewPolygons(_areaPreviewLayout);
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
            var vorflaecheGewuenscht = (_baukontext?.Zettel.SurfaceApronOn ?? _uiSystem?.VorflaecheAn ?? true)
                && _vorflaechen != null && _vorflaechen.Length > 0;
            _vorflaechenPrefabFehlgeschlagen = false;
            var vorflaecheAufgegeben = false;
            /*
             * DIE VORFLAECHE BEKOMMT DENSELBEN ZIELWERT.
             *
             * Bis zum 2026-09-18 lief sie mit 0, also unveraendert auf -96.
             * Das genuegte, solange unter ihr nur Gelaende lag. Seit die
             * Zufahrtsgasse eine SICHTBARE Strasse ist, ueber der unser
             * Belag liegen soll, zeichnet die Strasse ihre Knotengeometrie
             * darueber - im Bild des Nutzers als Halbkreis in der
             * Einmuendung.
             *
             * -94 ist derselbe Wert, den Zoningbelag und Parzellenboden seit
             * dem 2026-09-02 tragen, und er ist erprobt.
             */
            _vorflaechenPrefab = vorflaecheGewuenscht
                ? VorflaechenPrefab(_pavementSurfacePrefab,
                    out _vorflaechenPrefabFehlgeschlagen,
                    out vorflaecheAufgegeben, VorflaechePrioritaet)
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
                out var dekoRaeumerFehler, out var dekoRaeumerAufgegeben,
                ZoningBelagPrioritaet, raeumt: true);
            /*
             * HAUPTBELAG MIT EIGENEM RAEUM-KLON. Die Zeichenprioritaet ist
             * dieselbe wie bei der Vorflaeche; das Raeumflag darf die
             * einzelne Vorflaeche ausserhalb des Polygons nicht erben.
             */
            _asphaltBelagPrefab = VorflaechenPrefab(_pavementSurfacePrefab,
                out var asphaltRaeumerFehler,
                out var asphaltRaeumerAufgegeben,
                VorflaechePrioritaet, raeumt: true);
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

            // 120 Prefabzyklen sind die bestehende Aufgabegrenze. Bis dahin
            // darf Enter keinen Vanilla-Belag ohne Raeumflag festschreiben.
            // Bei Fehler/Aufgabe bleibt der bestehende baubare Rueckfall.
            _areaPreviewLayout.SurfacesForPlacement(
                _baukontext?.Zettel.SurfaceRoadOn ?? _uiSystem?.FlaecheStrasseAn ?? true,
                _baukontext?.Zettel.SurfaceDecorationOn ?? _uiSystem?.FlaecheDekoAn ?? true,
                out var geplantesGras, out var geplanterAsphalt);
            var dekoRaeumerNoetig = geplantesGras.Length > 0;
            var asphaltRaeumerNoetig = geplanterAsphalt.Length > 0;
            var raeumerAusstehend = dekoRaeumerNoetig
                && _dekoBelagPrefab == Entity.Null
                && !dekoRaeumerFehler && !dekoRaeumerAufgegeben
                || asphaltRaeumerNoetig
                && _asphaltBelagPrefab == Entity.Null
                && !asphaltRaeumerFehler && !asphaltRaeumerAufgegeben;
            if (raeumerAusstehend)
            {
                _areaTransferNote = "Gras- und Asphalt-Raeumbelag werden "
                    + "von CS2 initialisiert; noch keine Flaeche uebergeben.";
                if (_ghostsActive)
                    ClearAreaPreviewGhosts("clearing surface prefab not ready");
                return;
            }

            _areaPreviewPrefabsReady = true;
            if (prefabsOnly) return;
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
                // ToolBase.GetDefinitionQuery schliesst Updated aus und ist
                // global. Ein Reset im Erzeugungsbild verfehlte eigene
                // Eingaben; ApplySystem 118-127 beseitigt nur Temp/Warning/
                // Override, keine CreationDefinition. Jetzt: eigene Temp-
                // Eingaben am Zustandswechsel beenden, einschliesslich Updated.
                using var definitionen = _definitionQuery.ToEntityArray(Allocator.Temp);
                int n = 0;
                foreach (var e in definitionen)
                {
                    bool eigen = EntityManager.GetComponentData<ParkingLotAuftragsdefinition>(e).Auftrag == 0;
                    bool permanent = (EntityManager.GetComponentData<CreationDefinition>(e).m_Flags & CreationFlags.Permanent) != 0;
                    if (!HintergrundTakt.WerkzeugdefinitionBeenden(eigen,permanent,false)) continue;
                    EntityManager.DestroyEntity(e); n++;
                }
                if (n > 0) ParkingLotNetzRueckweg.Melde($"Werkzeugeingaben beendet ({context}): {n} eigene Temp-Definitionen; 0 fremde/Permanent-Definitionen.");
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

            /*
             * WELCHE Flaeche klemmt - damit der Abbruch das sagen kann.
             *
             * "Nichts gebaut: es entstanden keine Bauteile" war fuer den
             * Tester am 2026-09-22 nicht zu gebrauchen; er sah nicht, dass
             * seine Flaechenwahl der Grund war. Der Name steht hier ohnehin
             * fest, also wird er gemerkt.
             */
            _unaufloesbareFlaeche = !grass ? GrassSurfaceName
                : !pavement ? PavementSurfaceName
                : !zoning ? ZoningSurfaceName
                : null;
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
                /*
                 * KEIN `isBuiltin` MEHR - das war ein Widerspruch zur Liste.
                 *
                 * `VeroeffentlicheFlaechenliste` bietet jede Flaeche mit
                 * `AreaType.Surface` an, ausdruecklich auch die aus
                 * Asset-Mods ("was NICHT aus dem Grundspiel kommt, ist immer
                 * dabei"). Hier stand die Gegenbedingung, und eine gewaehlte
                 * Mod-Flaeche loeste deshalb nie auf: keine Definitionen,
                 * kein Bauteil, und im Panel nur "no build parts were
                 * created". Ein Tester am 2026-09-22 mit einer Flaeche aus
                 * dem ExtraAssetsImporter ist genau darueber gestolpert -
                 * waehlbar, aber nicht baubar.
                 *
                 * Was wirklich zaehlt, prueft die Zeile darunter:
                 * `HasUsableSurfacePrefab` verlangt einen gueltigen
                 * Archetyp. Der entscheidet, ob CS2 aus dem Prefab eine
                 * Flaeche machen kann - und das ist bei einer Mod-Flaeche
                 * nicht anders als bei einer eingebauten.
                 *
                 * Unsere eigenen Klone koennen hier nicht hereinrutschen:
                 * verglichen wird mit dem NAMEN aus der Auswahl, und die
                 * Liste laesst "PLT ..." gar nicht erst zur Wahl zu.
                 */
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                    || prefab == null
                    || !string.Equals(prefab.name, prefabName,
                        StringComparison.OrdinalIgnoreCase)
                    || !HasUsableSurfacePrefab(entity))
                    continue;

                resolved = entity;
                missingLogged = false;
                /*
                 * MIT DEN MERKMALEN, NICHT NUR MIT DEM NAMEN.
                 *
                 * Seit Mod-Flaechen erlaubt sind, ist die interessante
                 * Frage nicht mehr "welche wurde gewaehlt", sondern "was
                 * ist das fuer eine". Ein Tester schickt einen Log, nicht
                 * seinen Rechner - was hier nicht draufsteht, muss man
                 * erfragen.
                 */
                Mod.log.Info($"PLT-Flächenvorschau verwendet '{prefabName}': "
                    + (prefab.isBuiltin ? "aus dem Grundspiel" : "aus einem Mod")
                    + ", "
                    + (EntityManager.HasComponent<RenderedAreaData>(entity)
                        ? "gerendert" : "OHNE RenderedAreaData")
                    + ", "
                    + (EntityManager.HasComponent<SurfaceData>(entity)
                        ? "SurfaceData vorhanden" : "OHNE SurfaceData")
                    + ".");
                return true;
            }

            if (!missingLogged)
            {
                missingLogged = true;
                RecordPreviewDiagnostic("Warning",
                    $"Flächenvorschau findet das Prefab '{prefabName}' nicht "
                    + "oder es hat keinen gültigen Archetyp.");
                Mod.log.Warn($"PLT-Flächenvorschau findet das Prefab "
                    + $"'{prefabName}' nicht oder es hat keinen gueltigen "
                    + "Archetyp. Solange das so ist, entstehen KEINE "
                    + "Bauteile und der Bau meldet 'nichts gebaut'.");
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

    }
}
