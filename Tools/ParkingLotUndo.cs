using static ParkingLotTool.Tools.ParkingLotTexte;
using System.Linq;
using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace ParkingLotTool.Tools
{
    internal sealed class ParkingLotUndoSnapshot
    {
        internal float2[] Points;
        internal float3[] WorldPoints;
        internal float2[] SnapAxes;
        internal Entrance[] Entrances;
        internal ParkingLotDraftSettingsSnapshot Settings;
        internal bool Closed;
        internal bool PolygonTouched;
        internal bool EntranceMode;
        internal Zufahrtsart EntranceKind;
        /*
         * Die Bezugslinie gehoert in den Schnappschuss, sonst luegt das
         * Rueckgaengig: das Waehlen legt einen Schritt an, und ein Strg+Z
         * darauf haette die Punkte zurueckgeholt, den Winkel aber behalten.
         */
        internal ParkingLotToolSystem.Ausrichtzuweisung[] Ausrichtungen;
        /*
         * Die Handschnitte aus demselben Grund: sie bestimmen, WELCHE
         * Teilflaechen es gibt. Ohne sie im Schnappschuss holte ein Strg+Z
         * die Punkte zurueck, liesse aber die Trennung stehen - und die haengt
         * an genau diesen Punkten.
         */
        internal Teilflaechenschnitt[] Trennschnitte;
        /*
         * Die Zoning-Flaechen aus demselben Grund wie die Handschnitte: sie
         * sind ein Bedienvorgang wie jeder andere, und ein Strg+Z darauf muss
         * sie zurueckholen. Ohne sie im Schnappschuss waere Setzen,
         * Verschieben und Loeschen der einzige Vorgang ohne Rueckgaengig.
         */
        internal ParkingGeometry.Zoningflaeche[] Zoningflaechen;
        internal ParkingGeometry.RandzoningLinie[] Randzoning;
    }

    /** Schnappschussbasierter Rueckgaengig-Stapel des gemeinsamen Entwurfs. */
    public sealed partial class ParkingLotToolSystem
    {
        private const int UndoCapacity = 50;

        private sealed class UndoEntry
        {
            internal ParkingLotUndoSnapshot Snapshot;
            internal string Action;
        }

        private readonly UndoEntry[] _undoEntries = new UndoEntry[UndoCapacity];
        private int _undoStart;
        private int _undoCount;
        /*
         * WIEDERHERSTELLEN IST DERSELBE STAPEL, NUR ANDERSHERUM.
         *
         * Rueckgaengig nimmt oben vom Undo-Stapel und legt den Zustand, den
         * es gerade verlaesst, auf den Redo-Stapel. Wiederherstellen macht
         * genau das Umgekehrte. Weil beide Seiten VOLLE Schnappschuesse
         * halten, kann keine Richtung "danebengreifen" - es gibt keine
         * Umkehroperation, die falsch sein koennte.
         *
         * JEDE NEUE AENDERUNG LEERT DEN REDO-STAPEL. Sonst liefe man nach
         * einer Aenderung "vorwaerts" in einen Zweig, den es nicht mehr gibt.
         * Das ist die uebliche Regel und die einzige ohne Ueberraschungen.
         */
        private readonly UndoEntry[] _redoEntries = new UndoEntry[UndoCapacity];
        private int _redoStart;
        private int _redoCount;
        private ParkingLotUndoSnapshot _pointDragUndo;
        private ParkingLotUndoSnapshot _edgeDragUndo;
        private ParkingLotUndoSnapshot _entranceDragUndo;

        internal ParkingLotUndoSnapshot CaptureUndoState()
        {
            if (m_ToolSystem == null || m_ToolSystem.activeTool != this
                || _buildStage != BuildStage.Idle || _dragPoint >= 0
                || _dragEdge >= 0 || _dragEntrance >= 0) return null;
            SyncSnapAxes();
            return new ParkingLotUndoSnapshot
            {
                Points = _points.ToArray(),
                WorldPoints = _worldPoints.ToArray(),
                SnapAxes = _pointAxes.ToArray(),
                Entrances = CopyEntrances(_entrances),
                Settings = _uiSystem?.CaptureDraftSettings(),
                Closed = _closed,
                PolygonTouched = _polygonTouched,
                EntranceMode = _entranceMode,
                EntranceKind = _zufahrtsart,
                Ausrichtungen = _ausrichtungen
                    .Select(z => new Ausrichtzuweisung
                    {
                        Anker = z.Anker,
                        LinieA = z.LinieA,
                        LinieB = z.LinieB,
                        Winkel = z.Winkel,
                    }).ToArray(),
                Trennschnitte = _trennschnitte
                    .Select(sch => new Teilflaechenschnitt
                    {
                        A = sch.A,
                        B = sch.B,
                    }).ToArray(),
                Zoningflaechen = _zoningflaechen
                    .Select(f => new ParkingGeometry.Zoningflaeche
                    {
                        Ecke = f.Ecke,
                        Spalten = f.Spalten,
                        Reihen = f.Reihen,
                        Winkel = f.Winkel,
                        // Der Rand gehoert dazu - ohne ihn faellt das
                        // aeussere Bauland bei jedem Rueckgaengig auf den
                        // Standardwert zurueck. Seit dem 2026-09-21 stehen
                        // die Baender in `Aussentiefen`, und fuer die gilt
                        // dasselbe: ein Rueckgaengig ohne sie raeumt sie ab.
                        Rand = f.Rand,
                        Aussentiefen = f.Aussentiefen?.Clone() as double[],
                    }).ToArray(),
                Randzoning = _randzoning.Select(l => l.Clone()).ToArray(),
            };
        }

        internal void CommitUndoState(ParkingLotUndoSnapshot snapshot, string action)
        {
            if (snapshot == null) return;
            Lege(_undoEntries, ref _undoStart, ref _undoCount, snapshot, action);
            // Eine neue Aenderung macht den Vorwaertszweig ungueltig.
            LeereRedo();
            _uiSystem?.SetUndoAvailable(true);
        }

        private static void Lege(UndoEntry[] eintraege, ref int start,
            ref int anzahl, ParkingLotUndoSnapshot snapshot, string action)
        {
            if (anzahl == UndoCapacity)
            {
                eintraege[start] = null;
                start = (start + 1) % UndoCapacity;
                anzahl--;
            }
            var index = (start + anzahl) % UndoCapacity;
            eintraege[index] = new UndoEntry
            {
                Snapshot = snapshot,
                Action = string.IsNullOrEmpty(action)
                    ? T("Schritt", "step") : action,
            };
            anzahl++;
        }

        private static UndoEntry Nimm(UndoEntry[] eintraege, int start,
            ref int anzahl)
        {
            if (anzahl == 0) return null;
            var index = (start + anzahl - 1) % UndoCapacity;
            var eintrag = eintraege[index];
            eintraege[index] = null;
            anzahl--;
            return eintrag;
        }

        private void LeereRedo()
        {
            Array.Clear(_redoEntries, 0, _redoEntries.Length);
            _redoStart = 0;
            _redoCount = 0;
            _uiSystem?.SetRedoAvailable(false);
        }

        private void ClearUndoHistory()
        {
            Array.Clear(_undoEntries, 0, _undoEntries.Length);
            _undoStart = 0;
            _undoCount = 0;
            _pointDragUndo = null;
            _edgeDragUndo = null;
            _entranceDragUndo = null;
            _uiSystem?.SetUndoAvailable(false);
            LeereRedo();
        }

        private bool UndoShortcutPressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null
                && keyboard.zKey.wasPressedThisFrame
                && (keyboard.leftCtrlKey.isPressed
                    || keyboard.rightCtrlKey.isPressed);
        }

        /**
         * Strg+Y oder Strg+Umschalt+Z - beide sind gaengig, und wer aus
         * anderen Werkzeugen kommt, probiert erfahrungsgemaess genau eines
         * von beiden. Gelesen wird wie beim Rueckgaengig direkt von der
         * Tastatur, nicht ueber eine ProxyAction: CS2 maskiert die, solange
         * ein Modifikator gehalten wird.
         */
        private bool RedoShortcutPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            var strg = keyboard.leftCtrlKey.isPressed
                || keyboard.rightCtrlKey.isPressed;
            if (!strg) return false;
            var umschalt = keyboard.leftShiftKey.isPressed
                || keyboard.rightShiftKey.isPressed;
            return keyboard.yKey.wasPressedThisFrame
                || (umschalt && keyboard.zKey.wasPressedThisFrame);
        }

        internal void UndoFromPanel() => UndoLastStep();

        internal void RedoFromPanel() => RedoLastStep();

        private void RedoLastStep()
        {
            if (_dragPoint >= 0 || _dragEdge >= 0 || _dragEntrance >= 0
                || _buildStage != BuildStage.Idle) return;
            if (_redoCount == 0)
            {
                _uiSystem?.SetStatus(T("Nichts zum Wiederherstellen.",
                    "Nothing to redo."));
                _uiSystem?.SetRedoAvailable(false);
                return;
            }

            var zurueck = CaptureUndoState();
            var eintrag = Nimm(_redoEntries, _redoStart, ref _redoCount);
            if (zurueck != null)
            {
                Lege(_undoEntries, ref _undoStart, ref _undoCount, zurueck,
                    eintrag.Action);
                _uiSystem?.SetUndoAvailable(true);
            }
            RestoreUndoState(eintrag.Snapshot);
            _uiSystem?.SetRedoAvailable(_redoCount > 0);
            _uiSystem?.SetStatus(T("Wiederhergestellt: " + eintrag.Action + ".",
                "Restored: " + eintrag.Action + "."));
            Mod.log.Info("PLT-Wiederherstellen: " + eintrag.Action
                + "; verbleibende Tiefe " + _redoCount + ".");
        }

        private void UndoLastStep()
        {
            // Ein Zug ist erst beim Loslassen ein Schritt. Davor bleibt sein
            // Ausgangsschnappschuss unangetastet und Strg+Z wirkungslos.
            if (_dragPoint >= 0 || _dragEdge >= 0 || _dragEntrance >= 0
                || _buildStage != BuildStage.Idle) return;
            if (_undoCount == 0)
            {
                _uiSystem?.SetStatus(T("Nichts zum Rückgängigmachen.",
                    "Nothing to undo."));
                _uiSystem?.SetUndoAvailable(false);
                return;
            }

            // Der Zustand, den wir gerade verlassen, ist der Vorwaertsschritt.
            var vorwaerts = CaptureUndoState();
            var entry = Nimm(_undoEntries, _undoStart, ref _undoCount);
            if (vorwaerts != null)
            {
                Lege(_redoEntries, ref _redoStart, ref _redoCount, vorwaerts,
                    entry.Action);
                _uiSystem?.SetRedoAvailable(true);
            }
            RestoreUndoState(entry.Snapshot);
            _uiSystem?.SetUndoAvailable(_undoCount > 0);
            _uiSystem?.SetStatus(T("Rückgängig: " + entry.Action + ".",
                "Undone: " + entry.Action + "."));
            Mod.log.Info("PLT-Rueckgaengig: " + entry.Action
                + "; verbleibende Tiefe " + _undoCount + ".");
        }

        private void RestoreUndoState(ParkingLotUndoSnapshot snapshot)
        {
            if (snapshot == null) return;
            _uiSystem?.RestoreDraftSettings(snapshot.Settings);
            _seenSettingsRevision = _uiSystem?.Revision ?? 0;

            _points.Clear();
            _points.AddRange(snapshot.Points ?? Array.Empty<float2>());
            _worldPoints.Clear();
            _worldPoints.AddRange(snapshot.WorldPoints ?? Array.Empty<float3>());
            _pointAxes.Clear();
            _pointAxes.AddRange(snapshot.SnapAxes ?? Array.Empty<float2>());
            SyncSnapAxes();
            _entrances.Clear();
            _entrances.AddRange(CopyEntrances(snapshot.Entrances));
            _closed = snapshot.Closed;
            _polygonTouched = snapshot.PolygonTouched;
            _entranceMode = snapshot.EntranceMode && snapshot.Closed;
            _zufahrtsart = snapshot.EntranceKind;
            SetzeAusrichtungen(snapshot.Ausrichtungen);
            SetzeTrennschnitte(snapshot.Trennschnitte);
            SetzeZoningflaechen(snapshot.Zoningflaechen);
            SetzeRandzoning(snapshot.Randzoning);
            _uiSystem?.SetZoningZahlen(_zoningflaechen.Count,
                _zoningflaechen.Sum(f => f.Parzellen));

            _dragPoint = -1;
            _dragEdge = -1;
            _dragEntrance = -1;
            _extrudeStart = -1;
            _entrancePositionsBeforePointDrag = null;
            ClearEntranceGesture();
            ClearSnapFeedback();
            _hoverPoint = -1;
            _hoverEdge = -1;
            _buildRequestedWhenReady = false;
            _geometryRevision++;
            _layoutDirty = _closed;
            _overlay?.ClearLayout();
            ClearAreaPreviewLayout("undo");
            PublishEntranceState();
        }
    }
}
