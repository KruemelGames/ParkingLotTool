using System.Diagnostics;
using System;
using static ParkingLotTool.Tools.ParkingLotTexte;
using Game.Tools;

namespace ParkingLotTool.Tools
{
    /**
     * Der Bauvorgang in Schritten.
     *
     * WARUM ES DAS JETZT GIBT: Frueher lag waehrend der ganzen Vorschau ein
     * kompletter Satz Temp-Entities in der Welt - Flaechen, Wege, Aufkleber,
     * Ladesaeulen. CS2 prueft jede davon einzeln und pinnt ihr bei Bedarf ein
     * Warnsymbol an. Bei 320 Stellplaetzen war der Parkplatz unter Symbolen
     * und roten Raendern nicht mehr zu erkennen, und jede Reglerbewegung baute
     * den ganzen Satz neu auf.
     *
     * Die Vorschau zeichnet deshalb nur noch der Overlay
     * (`ParkingLotOverlay`). Temp-Entities entstehen erst, wenn wirklich
     * gebaut wird - und dann nur fuer die zwei, drei Frames bis zum Anwenden.
     *
     * ZWEI SCHRITTE SIND PFLICHT, nicht Bequemlichkeit: eine
     * `CreationDefinition` ist noch keine Entity. CS2s Generate*Systems machen
     * daraus erst im naechsten Frame Temp-Entities. Der Besitzer laesst sich
     * aber nur an einer EXISTIERENDEN Entity setzen
     * (`AttachSurfacesToLotOwner` verlangt `MaterializedEntity != Null`).
     * Wer im selben Frame anwendet, bekommt einen Parkplatz ohne
     * gemeinsamen Besitzer - also ohne Gruppierung, ohne Infofenster und
     * ohne dass der Bulldozer ihn als Ganzes erwischt.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private enum BuildStage
        {
            /** Nichts los - die Vorschau ist reines Overlay. */
            Idle,
            /** Enter kam. Im naechsten Schritt entstehen die Definitionen. */
            CreateDefinitions,
            /** Definitionen liegen; wir warten auf die Temp-Entities. */
            AwaitMaterialization,
        }

        /**
         * Wie lange auf die Materialisierung gewartet wird.
         *
         * Normal ist EIN Frame. 30 ist grosszuegig gegen einen Ruckler und
         * kurz genug, dass ein echter Fehler nicht als Haenger erscheint.
         */
        private const int MaterializationTimeoutFrames = 30;

        private BuildStage _buildStage = BuildStage.Idle;
        private int _buildStageFrame;
        private bool _buildRequestedWhenReady;

        /**
         * Der Bauknopf aus der Leiste.
         *
         * Er setzt nur diese Fahne, statt selbst zu bauen: `applyMode` darf
         * ausschliesslich im Werkzeug-Durchlauf gesetzt werden, und das Panel
         * laeuft in der UI-Phase. Ueber die Fahne nimmt der Knopf danach
         * denselben Weg wie Enter - mit denselben Pruefungen auf
         * geschlossenes Polygon und vorhandene Zufahrt.
         */
        private bool _panelBuildRequest;

        internal void RequestBuildFromPanel() => _panelBuildRequest = true;

        /*
         * WIE LANGE WARTET EIN KLICK AUF "BAUEN" - UND WORAUF.
         *
         * Nutzer, 2026-09-24: *"beim normalen ersten Mal bauen hat das auch
         * lange gebraucht."* Der Bau selbst lief in 0,4 s, die Leitungen in
         * 1 s - der Log hatte aber keinen Zeitpunkt des KLICKS. Die knapp
         * 5 s davor waren damit nicht zuzuordnen. Jetzt steht der Klick drin,
         * jeder Wartegrund (nur wenn er wechselt) und der Start mit Dauer.
         */
        private long _bauwunschSeit;
        private string _bauwunschGrund;

        private void BauwunschWartet(string grund)
        {
            if (_bauwunschSeit == 0 || grund == _bauwunschGrund) return;
            _bauwunschGrund = grund;
            Mod.log.Info("PLT-Bauwunsch wartet (" + BauwunschMs() + " ms): " + grund);
        }

        private long BauwunschMs() => _bauwunschSeit == 0 ? 0
            : (long)((Stopwatch.GetTimestamp() - _bauwunschSeit) * 1000.0
                / Stopwatch.Frequency);

        private void BauwunschEnde(string wie)
        {
            if (_bauwunschSeit == 0) return;
            Mod.log.Info("PLT-Bauwunsch " + wie + " nach " + BauwunschMs() + " ms.");
            _bauwunschSeit = 0;
            _bauwunschGrund = null;
        }

        /**
         * Ein Schritt je Frame. Rueckgabe `true` heisst: dieser Frame gehoert
         * dem Bauvorgang, der Aufrufer bricht seine restliche Arbeit ab.
         */
        private bool ProcessBuild()
        {
            switch (_buildStage)
            {
                case BuildStage.Idle:
                    var requestedNow = BuildRequested() || _panelBuildRequest;
                    _panelBuildRequest = false;
                    if (!requestedNow && !_buildRequestedWhenReady) return false;
                    if (requestedNow)
                    {
                        if (!_closed)
                        {
                            _buildRequestedWhenReady = false;
                            Mod.log.Warn("PLT: Enter ohne geschlossenes Polygon; "
                                + "nichts gebaut.");
                            return false;
                        }
                        if (!DarfBauen)
                        {
                            _buildRequestedWhenReady = false;
                            RequestMissingEntrance();
                            return false;
                        }
                        _buildRequestedWhenReady = true;
                        if (_bauwunschSeit == 0)
                        {
                            _bauwunschSeit = Stopwatch.GetTimestamp();
                            _bauwunschGrund = null;
                            Mod.log.Info("PLT-Bauwunsch angenommen ("
                                + (IsEditing ? "Umbau" : "Neubau") + ").");
                        }
                    }

                    // Ein Klick setzt die Zufahrt sofort, die Geometrie laeuft
                    // aber im Hintergrund. Enter merkt sich den Bauwunsch, bis
                    // genau dieser Stand fertig ist; gemessen braucht das bei
                    // den vier Referenzen 0,7 bis 3,0 s statt nur eines Frames.
                    if (!_closed || !DarfBauen)
                    {
                        _buildRequestedWhenReady = false;
                        RequestMissingEntrance();
                        return false;
                    }
                    if (_dragPoint >= 0 || _dragEntrance >= 0 || _layoutDirty
                        || _buildTask != null || _areaPreviewLayout == null
                        || _editBaselinePending
                        || _lastPreviewRevision != _geometryRevision
                        || _areaPreviewLayout.Entrances == null
                        || _areaPreviewLayout.Entrances.Length == 0)
                    {
                        BauwunschWartet(_dragPoint >= 0 || _dragEntrance >= 0
                            ? "es wird gezogen"
                            : _buildTask != null ? "Vorschau rechnet"
                            : _layoutDirty ? "Vorschau muss neu rechnen"
                            : _areaPreviewLayout == null ? "keine Vorschau"
                            : _editBaselinePending ? "Edit-Ausgangsstand fehlt"
                            : _lastPreviewRevision != _geometryRevision
                                ? "Vorschau gehoert zu altem Stand"
                            : "Vorschau ohne Zufahrt");
                        _uiSystem?.SetStatus(T("Vorschau wird berechnet.", "Calculating preview."));
                        return false;
                    }
                    if (TryFinishUnchangedEdit())
                    {
                        BauwunschEnde("ohne Aenderung beendet");
                        return true;
                    }
                    if (IsEditing)
                    {
                        // Im Log vom 24.09. wurden zweimal je 54 Wegteile
                        // geloescht, bevor ein fehlender Flaechenklon den
                        // Neubau abbrach. Vor dem Abriss nur Prefabs pruefen.
                        SyncAreaPreview(prefabsOnly: true);
                        if (!_areaPreviewPrefabsReady)
                        {
                            if (_unaufloesbareFlaeche != null
                                || _vorflaechenPrefabFehlgeschlagen
                                || !HasPreviewPolygons(_areaPreviewLayout))
                            {
                                _buildRequestedWhenReady = false;
                                _uiSystem?.SetStatus(_unaufloesbareFlaeche != null
                                    ? T($"Nichts gebaut: Fläche '{_unaufloesbareFlaeche}' "
                                        + "ist nicht benutzbar.",
                                        $"Nothing built: surface '{_unaufloesbareFlaeche}' "
                                        + "is unavailable.")
                                    : T("Nichts gebaut: die Flächenvorbereitung "
                                        + "ist fehlgeschlagen.",
                                        "Nothing built: surface preparation failed."));
                                Mod.log.Warn("PLT-Bearbeiten: Neubau vor dem "
                                    + "Abriss gestoppt; Flaechenprefab "
                                    + "nicht benutzbar. Alte Wege bleiben.");
                            }
                            else
                            {
                                BauwunschWartet("Flaechenprefabs werden vorbereitet");
                                _uiSystem?.SetStatus(T(
                                    "Flächenprefabs werden vorbereitet.",
                                    "Preparing surface prefabs."));
                            }
                            return false;
                        }
                    }
                    // Beim Umbau muessen die alten Wege JETZT fallen - die
                    // Definitionen entstehen erst im naechsten Durchgang.
                    EntferneAlteNetzeVorDemNeubau();
                    _buildStage = BuildStage.CreateDefinitions;
                    _buildStageFrame = UnityEngine.Time.frameCount;
                    return false;

                case BuildStage.CreateDefinitions:
                    // Eine nach Enter begonnene Aenderung widerruft den alten
                    // Bauwunsch. So werden nie Definitionen eines ueberholten
                    // Polygons mit dem neuen Bedienzustand vermischt.
                    if (!_buildRequestedWhenReady || !_closed
                        || !DarfBauen || _layoutDirty
                        || _buildTask != null || _areaPreviewLayout == null
                        || _lastPreviewRevision != _geometryRevision)
                    {
                        _buildStage = BuildStage.Idle;
                        return false;
                    }
                    // Beim Edit erst, wenn das Gelaende ohne die alten Wege
                    // neu gerechnet ist (ParkingLotEditHeight.cs).
                    if (IsEditing && !GelaendeNachAbrissFertig())
                    {
                        BauwunschWartet("Gelaende nach dem Abriss der alten Wege");
                        return false;
                    }
                    // Erzwingt das Anlegen, auch wenn die Signatur seit einem
                    // frueheren Versuch unveraendert ist.
                    _lastPreviewSig = long.MinValue;
                    var uhrVorschau = ParkingLotMessung.Start();
                    SyncAreaPreview();
                    ParkingLotMessung.Ende(
                        ParkingLotMessung.Punkt.Vorschau, uhrVorschau);
                    if (_vorflaechenPrefabFehlgeschlagen)
                    {
                        _uiSystem?.SetStatus(T(
                            "Nichts gebaut: Das Vorflächen-Prefab konnte nicht initialisiert werden.",
                            "Nothing built: The apron prefab could not be initialized."));
                        _buildStage = BuildStage.Idle;
                        _buildRequestedWhenReady = false;
                        if (IsEditing)
                            AbortEdit("Vorflächen-Prefab des Neubaus fehlgeschlagen",
                                "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                                "Rebuild failed; the old parking lot was restored.");
                        return false;
                    }
                    if (_vorflaechenPrefabAusstehend)
                    {
                        BauwunschWartet("Vorflaechen-Prefab wird initialisiert");
                        _uiSystem?.SetStatus(T(
                            "Vorflächen-Prefab wird initialisiert.",
                            "Initializing apron prefab."));
                        return false;
                    }
                    if (!_ghostsActive)
                    {
                        // Ohne diese Zeile war der Abbruch fuer den Nutzer
                        // unsichtbar: Enter, und scheinbar passiert nichts.
                        // Wenn die Flaechenwahl der Grund ist, gehoert sie in
                        // den Satz. Alles andere schickt den Nutzer suchen.
                        var flaeche = _unaufloesbareFlaeche;
                        _uiSystem?.SetStatus(flaeche != null
                            ? T($"Nichts gebaut: die Fläche '{flaeche}' lässt "
                                + "sich nicht verwenden. Bitte eine andere wählen.",
                                $"Nothing built: the surface '{flaeche}' cannot "
                                + "be used. Please pick another one.")
                            : T("Nichts gebaut: es entstanden keine Bauteile.",
                                "Nothing built: no build parts were created."));
                        Mod.log.Warn("PLT: Es entstanden keine Bau-Definitionen; "
                            + "nichts gebaut."
                            + (flaeche != null
                                ? " Ursache: die Flaeche '" + flaeche
                                  + "' liess sich nicht aufloesen."
                                : string.Empty));
                        _buildStage = BuildStage.Idle;
                        _buildRequestedWhenReady = false;
                        if (IsEditing)
                            AbortEdit("Neubau erzeugte keine Bauteile",
                                "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                                "Rebuild failed; the old parking lot was restored.");
                        return false;
                    }
                    VerwerfeEdithoehen();
                    _buildRequestedWhenReady = false;
                    ProtokolliereBauschritt("AwaitMaterialization — Übergabe der Definitionen an CS2");
                    _buildStage = BuildStage.AwaitMaterialization;
                    _buildStageFrame = UnityEngine.Time.frameCount;
                    return false;

                case BuildStage.AwaitMaterialization:
                    if (!AreaTransferMaterialized())
                    {
                        if (UnityEngine.Time.frameCount - _buildStageFrame
                            <= MaterializationTimeoutFrames) return false;
                        // MIT ZAHLEN. Der Satz allein hat am 2026-08-24
                        // nicht gereicht: der Nutzer sah nur, dass Enter nichts
                        // tut, und im Log stand kein Hinweis darauf, dass genau
                        // eine von einer Flaeche fertig war und die alte
                        // Zweierhuerde sie nie durchliess.
                        AreaTransferStand(out var fertig, out var gesamt);
                        _uiSystem?.SetStatus(T(
                            $"Nichts gebaut: {fertig} von {gesamt} Flächen wurden "
                            + "rechtzeitig übernommen.",
                            $"Nothing built: {fertig} of {gesamt} surfaces were "
                            + "accepted in time."));
                        Mod.log.Warn($"PLT: Nur {fertig} von {gesamt} Flächen "
                            + $"wurden nach {MaterializationTimeoutFrames} Frames "
                            + "zu Entities. Abgebrochen, damit kein Parkplatz "
                            + "ohne gemeinsamen Besitzer entsteht.");
                        ClearAreaPreviewGhosts("materialization timed out");
                        _buildStage = BuildStage.Idle;
                        if (IsEditing)
                            AbortEdit("Bauteile des Neubaus wurden nicht materialisiert",
                                "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                                "Rebuild failed; the old parking lot was restored.");
                        return false;
                    }
                    ProtokolliereBauschritt("WendeMaterialisiertenBauAn");
                    return WendeMaterialisiertenBauAn();

                default:
                    _buildStage = BuildStage.Idle;
                    return false;
            }
        }

        private bool WendeMaterialisiertenBauAn()
        {
            MeldeZoningKnotenbau();

            bool built;
            try
            {
                built = TryApplyAreaPreview();
                BauwunschEnde(built ? "gebaut" : "ohne Bau beendet");
            }
            catch (Exception exception) when (IsEditing)
            {
                Mod.log.Error(exception,
                    "PLT-Umbau: unerwarteter Fehler im vorhandenen Apply-Weg.");
                AbortEdit("Ausnahme beim Bau des Ersatz-Lots",
                    "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                    "Rebuild failed; the old parking lot was restored.");
                built = false;
            }
            _buildStage = BuildStage.Idle;
            // Erst jetzt gibt es ueberhaupt etwas nachzumessen. Die Messung
            // selbst folgt ein paar Bilder spaeter - eine Strassenteilung
            // braucht mehr als ein Bild.
            if (built) MeldeGassenbefundAn();
            if (built) _fusswegBefundAb = UnityEngine.Time.frameCount + 30;
            return built;
        }

        /** Bricht einen laufenden Bauvorgang ab, etwa beim Zuruecksetzen. */
        private void CancelBuildStage()
        {
            VerwerfeEdithoehen();
            _buildRequestedWhenReady = false;
            if (_buildStage == BuildStage.Idle) return;
            _buildStage = BuildStage.Idle;
            if (_ghostsActive) ClearAreaPreviewGhosts("build cancelled");
            else applyMode = ApplyMode.None;
        }
    }
}
