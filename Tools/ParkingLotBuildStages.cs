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
                        _uiSystem?.SetStatus(T("Vorschau wird berechnet.", "Calculating preview."));
                        return false;
                    }
                    if (TryFinishUnchangedEdit()) return true;
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
                    // Erzwingt das Anlegen, auch wenn die Signatur seit einem
                    // frueheren Versuch unveraendert ist.
                    _lastPreviewSig = long.MinValue;
                    SyncAreaPreview();
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
                        _uiSystem?.SetStatus(T(
                            "Vorflächen-Prefab wird initialisiert.",
                            "Initializing apron prefab."));
                        return false;
                    }
                    if (!_ghostsActive)
                    {
                        // Ohne diese Zeile war der Abbruch fuer den Nutzer
                        // unsichtbar: Enter, und scheinbar passiert nichts.
                        _uiSystem?.SetStatus(T(
                            "Nichts gebaut: es entstanden keine Bauteile.",
                            "Nothing built: no build parts were created."));
                        Mod.log.Warn("PLT: Es entstanden keine Bau-Definitionen; "
                            + "nichts gebaut.");
                        _buildStage = BuildStage.Idle;
                        _buildRequestedWhenReady = false;
                        if (IsEditing)
                            AbortEdit("Neubau erzeugte keine Bauteile",
                                "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                                "Rebuild failed; the old parking lot was restored.");
                        return false;
                    }
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
            return built;
        }

        /** Bricht einen laufenden Bauvorgang ab, etwa beim Zuruecksetzen. */
        private void CancelBuildStage()
        {
            _buildRequestedWhenReady = false;
            if (_buildStage == BuildStage.Idle) return;
            _buildStage = BuildStage.Idle;
            if (_ghostsActive) ClearAreaPreviewGhosts("build cancelled");
            else applyMode = ApplyMode.None;
        }
    }
}
