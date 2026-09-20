using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Bedienung und Zustand der manuell gesetzten Zufahrten. */
    public sealed partial class ParkingLotToolSystem
    {
        /*
         * Fuenfzehn statt zehn, Ansage des Nutzers am 2026-08-27. Die Grenze
         * gilt fuer ALLE Sorten zusammen - Zufahrt, Einfahrt, Ausfahrt und
         * Fussgaengerzugang teilen sich dasselbe Kontingent.
         */
        internal const int MaxEntranceCount = 15;
        // 12,0 m entsprechen dem bereits vertrauten Fangkreis zum Schliessen;
        // der 9,0-m-Griff bleibt 1,0 m groesser als eine Polygonecke. Strassen
        // fangen enger bei 5,0 m, damit eine freie Zufahrt weiterhin moeglich ist.
        private const float EntranceBoundarySnapDistance = 12f;
        private const float EntranceHandleHitDistance = 9f;
        private const float EntranceRoadSnapDistance = 5f;
        private const float EntranceLineEpsilon = 0.0001f;
        private const float EntranceSnapCos = 0.9961947f; // cos(5 Grad)

        private readonly List<Entrance> _entrances = new List<Entrance>();
        private readonly ParkingLotEntranceOverlayState _entranceOverlay =
            new ParkingLotEntranceOverlayState();

        private bool _entranceMode;

        /*
         * MESSUNG FUER SHIFT+KLICK.
         *
         * Diese Zaehler trennen den Fangpfad vom Klickpfad. Genau diese
         * Trennung hat den Fehler verdeckt: die Vorschau sah Shift, der von
         * CS2 maskierte ProxyAction-Klick kam aber nicht bis zum Werkzeug.
         * Geloggt wird nur beim wirklichen Rohmaus-Klick, nicht je Frame.
         */
        private long _entranceShiftCandidateReads;
        private long _entranceShiftCandidateHeld;
        private long _entranceShiftSnapTargetsDiscarded;
        private long _entranceShiftClickReads;
        private long _entranceShiftClickHeld;
        private long _entranceShiftRawClicks;
        private long _entranceShiftProxyClicks;
        private long _entranceShiftForwardedClicks;

        /**
         * WELCHE ART DER NAECHSTE KLICK SETZT.
         *
         * Der Modus allein reicht seit dem 2026-08-27 nicht mehr: es gibt vier
         * Sorten, und der Knopf im Panel waehlt sie. Der Zustand liegt hier
         * und nicht im Panel, weil auch Tastenkuerzel und das Wiederherstellen
         * nach dem Laden ihn brauchen.
         */
        private Zufahrtsart _zufahrtsart = Zufahrtsart.Zufahrt;

        internal Zufahrtsart AktuelleZufahrtsart => _zufahrtsart;

        /** Gehoert der naechste Klick den Zugaengen? */
        internal bool EntranceModeAktiv => _entranceMode;

        /**
         * WANN DARF GEBAUT WERDEN - EINGANG IST NICHT GLEICH EINGANG.
         *
         * Bis zum 2026-08-27 genuegte IRGENDEINE gesetzte Zufahrt. Seit es
         * vier Arten gibt, ist das falsch:
         *
         *     Fussweg allein     kein Auto kommt hinein
         *     Einfahrt allein    Autos kommen rein und nie wieder raus
         *     Ausfahrt allein    niemand kommt hinein
         *
         * Erlaubt ist deshalb: mindestens EINE Zufahrt (die faehrt in beide
         * Richtungen), ODER mindestens je eine Einfahrt UND eine Ausfahrt.
         *
         * Entschieden wird nach der RICHTUNG, nicht nach der Bauart. Die
         * zweispurige Gasse zaehlt als Zufahrt, die einspurigen zaehlen als
         * Ein- und Ausfahrt.
         * Fusswege zaehlen dabei nie mit - sie sind eine Ergaenzung, kein
         * Zugang fuer Autos.
         *
         * Der Grund, warum es nicht geht, wird mitgeliefert: "Zufahrt fehlt"
         * hilft niemandem, der zwei Einfahrten gesetzt hat und nicht sieht,
         * dass ihm die Ausfahrt fehlt.
         */
        internal bool DarfBauen => FehlenderZugang() == null;

        internal string FehlenderZugang()
        {
            var zufahrt = 0;
            var einfahrt = 0;
            var ausfahrt = 0;
            foreach (var eintrag in _entrances)
            {
                if (eintrag == null) continue;
                switch (eintrag.Art)
                {
                    // Die Gasse ist eine vollwertige Zufahrt; sie hat beide
                    // Richtungen und zaehlt deshalb genauso.
                    case Zufahrtsart.Gasse:
                    case Zufahrtsart.Zufahrt: zufahrt++; break;
                    /*
                     * DIE EINSPURIGEN GASSEN ZAEHLEN WIE IHRE UNSICHTBAREN
                     * GESCHWISTER.
                     *
                     * Was diese Pruefung wissen will, ist eine Frage der
                     * RICHTUNG, nicht der Bauart: kommt ein Auto herein, und
                     * kommt es wieder heraus. Eine Gasse hinein leistet
                     * dasselbe wie eine Einfahrt. Ohne diese beiden Zeilen
                     * darf niemand bauen, der eine Gasse rein und eine Gasse
                     * raus gesetzt hat - und sieht nicht, warum.
                     */
                    case Zufahrtsart.GasseEin:
                    case Zufahrtsart.Einfahrt: einfahrt++; break;
                    case Zufahrtsart.GasseAus:
                    case Zufahrtsart.Ausfahrt: ausfahrt++; break;
                }
            }

            if (zufahrt > 0) return null;
            if (einfahrt > 0 && ausfahrt > 0) return null;
            if (einfahrt > 0) return "ausfahrt";
            if (ausfahrt > 0) return "einfahrt";
            return "zugang";
        }

        /**
         * Der Panelknopf waehlt die Art UND schaltet den Setzmodus ein.
         *
         * Zwei Klicks fuer "Einfahrt setzen" waeren einer zu viel - wer die
         * Art waehlt, will offensichtlich eine setzen. Ein erneuter Klick auf
         * dieselbe Art schaltet wieder aus; so bleibt jeder Knopf sein eigener
         * Umschalter, ohne dass man den Modus getrennt suchen muss.
         */
        internal void SetZufahrtsartFromPanel(int art)
        {
            // Kein Vergleich gegen den letzten Wert: der wandert, sobald
            // eine Art dazukommt, und tut es stumm.
            var gewaehlt = Enum.IsDefined(typeof(Zufahrtsart), art)
                ? (Zufahrtsart)art
                : Zufahrtsart.Zufahrt;

            if (_entranceMode && gewaehlt == _zufahrtsart)
            {
                SetEntranceModeFromPanel(false);
                return;
            }

            _zufahrtsart = gewaehlt;
            if (!_entranceMode) SetEntranceModeFromPanel(true);
            else PublishEntranceState();
        }
        private bool _entranceMissingPrompt;
        private int _hoverEntrance = -1;
        private int _dragEntrance = -1;
        private Entrance _dragEntranceStart;
        private bool _dragEntranceMoved;
        private EntranceCandidate _entranceCandidate;
        private bool _hasEntranceCandidate;
        private float2[] _entrancePositionsBeforePointDrag;

        private enum EntranceBlockReason
        {
            None,
            Maximum,
            Spacing,
            NoRoom,
            /** Hier liegt Bauland - Zoningflaeche oder Randzoning. */
            Bauland,
        }

        private struct BoundaryProjection
        {
            internal int Edge;
            internal float2 Point;
            internal float2 Tangent;
            internal float2 Inward;
            internal float Along;
            internal float Arc;
            internal float Perimeter;
            internal float EdgeLength;
        }

        private struct EntranceCandidate
        {
            internal Entrance Entrance;
            internal float2 Point;
            internal float2 Tangent;
            internal float2 Direction;
            internal float Length;
            internal float Width;
            internal float MinimumSpacing;
            internal bool Valid;
            internal bool HasGuide;
            internal float2 GuideA;
            internal float2 GuideB;
            internal EntranceBlockReason BlockReason;
        }

        /** Das Panel darf nur in ein bereits geschlossenes Polygon wechseln. */
        internal void PlaceEntranceFromPanel()
        {
            if (!_closed) return;
            BeginEntrancePlacement(showMissingPrompt: false);
        }

        /**
         * Der Umschaltknopf im Panel. Er nennt immer das ZIEL, nicht den
         * aktuellen Zustand - deshalb genuegt ihm ein Wort, und die Frage
         * "welcher Knopf ist an" stellt sich gar nicht erst.
         */
        internal void SetEntranceModeFromPanel(bool on)
        {
            if (!_closed) return;
            if (on == _entranceMode) return;
            if (on) BeginEntrancePlacement(showMissingPrompt: false);
            else LeaveEntrancePlacement();
        }

        private void BeginEntrancePlacement(bool showMissingPrompt)
        {
            // Entweder oder: der Zugangsmodus und das Zoning wollen denselben
            // Linksklick. Zoning schaltete diesen hier schon aus; jetzt auch
            // andersherum.
            SetzeZoningModus(false);
            _entranceMode = true;
            /*
             * Die Meldung sagt, WAS fehlt, nicht nur DASS etwas fehlt.
             * Zwei Einfahrten und kein Weiterkommen ist der Fall, in dem
             * "Bitte Zufahrt platzieren" in die Irre fuehrt - dort fehlt
             * die Ausfahrt, nicht die Zufahrt. Dieselbe Regel wie am
             * Bauen-Knopf; sie steht nur an einer Stelle.
             */
            var fehlt = FehlenderZugang();
            _entranceMissingPrompt = showMissingPrompt && fehlt != null;
            ClearEntranceGesture();
            PublishEntranceState();
            UpdateEntranceHint();
            _uiSystem?.SetStatus(!_entranceMissingPrompt
                ? T("Zufahrt am Polygonrand platzieren.",
                    "Place an entrance on the polygon edge.")
                : fehlt == "ausfahrt"
                ? T("Eine Einfahrt ohne Ausfahrt - die Autos kämen nicht heraus.",
                    "An entry without an exit - cars could not leave.")
                : fehlt == "einfahrt"
                ? T("Eine Ausfahrt ohne Einfahrt - bitte eine Einfahrt setzen.",
                    "An exit without an entry - place an entry.")
                : T("Es fehlt eine Zufahrt, oder je eine Ein- und Ausfahrt.",
                    "Needs a two-way entrance, or one entry and one exit."));
        }

        private void LeaveEntrancePlacement()
        {
            _entranceMode = false;
            _entranceMissingPrompt = false;
            ClearEntranceGesture();
            _entranceOverlay.Clear();
            _debugTooltipSystem?.ClearEntranceHint();
            PublishEntranceState();
            _uiSystem?.SetStatus(T("Polygon bearbeiten.", "Editing polygon."));
        }

        /** Enter ohne ausreichenden Zugang fuehrt ohne Panelklick hierher. */
        private void RequestMissingEntrance()
        {
            BeginEntrancePlacement(showMissingPrompt: true);
            Mod.log.Info("PLT: Enter ohne ausreichenden Zugang ("
                + (FehlenderZugang() ?? "-") + " fehlt); Setzmodus wieder aktiv.");
        }

        private void PublishEntranceState()
            => _uiSystem?.SetDraftState(_closed, _entranceMode, _entrances.Count,
                (int)_zufahrtsart, MaxEntranceCount, FehlenderZugang());

        private void ClearEntranceGesture()
        {
            _hoverEntrance = -1;
            _dragEntrance = -1;
            _dragEntranceStart = null;
            _dragEntranceMoved = false;
            _hasEntranceCandidate = false;
            _entranceCandidate = default;
        }

        private void ResetEntranceEditing(bool clearEntrances)
        {
            if (clearEntrances) _entrances.Clear();
            _entranceMode = false;
            _entranceMissingPrompt = false;
            _entrancePositionsBeforePointDrag = null;
            ClearEntranceGesture();
            _entranceOverlay.Clear();
            _debugTooltipSystem?.ClearEntranceHint();
            PublishEntranceState();
        }

        /**
         * Das Werkzeug setzt den Schalter auch bei null Zufahrten. Sonst
         * wuerde `Entrances = []` wieder die Automatik aktivieren und der
         * Nutzer saehe nach dem Schliessen ungefragt 1 bis 2 Zufahrten.
         */
        private LayoutSettings WithManualEntrances(LayoutSettings settings)
        {
            settings ??= LayoutSettings.Cs2;
            settings.AutomaticEntrances = false;
            settings.Entrances = CopyEntrances(_entrances);
            return settings;
        }

        private static Entrance[] CopyEntrances(IReadOnlyList<Entrance> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<Entrance>();
            var copy = new Entrance[source.Count];
            for (var i = 0; i < source.Count; i++) copy[i] = CopyEntrance(source[i]);
            return copy;
        }

        private static Entrance CopyEntrance(Entrance source) => source == null
            ? null
            : new Entrance
            {
                Edge = source.Edge, Along = source.Along,
                Corner = source.Corner, Art = source.Art,
            };

        /** Der Baukern darf eine Vorgabe innerhalb derselben Kante korrigieren. */
        private bool AcceptBuiltEntrances(ParkingLayout layout, LayoutSettings settings)
        {
            var accepted = layout?.Entrances ?? Array.Empty<Entrance>();
            var spaced = new List<Entrance>(accepted.Length);
            var rejected = 0;
            for (var i = 0; i < accepted.Length; i++)
            {
                var copy = CopyEntrance(accepted[i]);
                if (copy == null || spaced.Count >= MaxEntranceCount
                    || !HasEntranceSpacingAgainst(copy, spaced, -1, settings, out _))
                {
                    rejected++;
                    continue;
                }
                spaced.Add(copy);
            }

            if (rejected > 0)
            {
                _entrances.Clear();
                _entrances.AddRange(spaced);
                settings.AutomaticEntrances = false;
                settings.Entrances = CopyEntrances(_entrances);
                _geometryRevision++;
                _layoutDirty = _closed;
                PublishEntranceState();
                // Der Kern darf stufenlose Positionen an ein bebaubares
                // Randstueck schieben. Falls dabei mehr als die 0,000001-m-
                // Rechentoleranz als Kapselueberdeckung entstuende, wird der Stand
                // nicht gezeichnet oder gebaut, sondern einmal neu gerechnet.
                Mod.log.Info($"PLT-Zufahrten nach Kernkorrektur: {rejected} "
                    + "wegen Kapselabstand verworfen; Vorschau wird erneuert.");
                return false;
            }

            var changed = accepted.Length != _entrances.Count;
            if (!changed)
                for (var i = 0; i < accepted.Length; i++)
                    if (!SameEntrance(accepted[i], _entrances[i]))
                    {
                        changed = true;
                        break;
                    }

            if (changed)
            {
                var requested = _entrances.Count;
                _entrances.Clear();
                for (var i = 0; i < accepted.Length; i++)
                    _entrances.Add(CopyEntrance(accepted[i]));
                Mod.log.Info($"PLT-Zufahrten vom Baukern normalisiert: "
                    + $"{requested} Vorgaben, {accepted.Length} verwendbar.");
            }

            settings.AutomaticEntrances = false;
            settings.Entrances = CopyEntrances(_entrances);
            if (_entrances.Count > 0) _entranceMissingPrompt = false;
            PublishEntranceState();
            return true;
        }

        private static bool SameEntrance(Entrance a, Entrance b)
        {
            if (a == null || b == null) return a == b;
            return a.Edge == b.Edge && Math.Abs(a.Along - b.Along) <= 1e-6
                && string.Equals(a.Corner, b.Corner, StringComparison.Ordinal)
                && a.Art == b.Art;
        }

        private LayoutSettings CurrentEntranceSettings()
            => _uiSystem != null ? _uiSystem.CurrentSettings() : LayoutSettings.Cs2;

        private float MinimumEntranceSpacing(LayoutSettings settings)
            => (float)(settings.Ai + 2 * settings.Sw);

        private void UpdateEntranceInteraction()
        {
            _hoverEntrance = -1;
            _hasEntranceCandidate = false;
            _entranceCandidate = default;

            if (!_closed || !_entranceMode || MarkerMode)
            {
                _entranceOverlay.Clear();
                if (!_entranceMode) _debugTooltipSystem?.ClearEntranceHint();
                return;
            }

            if (_dragPoint >= 0)
            {
                RefreshEntranceOverlay();
                UpdateEntranceHint();
                return;
            }

            if (_dragEntrance >= 0)
                _hoverEntrance = _dragEntrance;
            else if (_hasHover)
                _hoverEntrance = FindEntranceAt(_hoverPosition.xz);

            if (_hasHover && (_dragEntrance >= 0 || _hoverEntrance < 0))
                _hasEntranceCandidate = TryBuildEntranceCandidate(
                    _hoverPosition.xz, _dragEntrance, out _entranceCandidate);

            RefreshEntranceOverlay();
            UpdateEntranceHint();
        }

        private int FindEntranceAt(float2 cursor)
        {
            var best = EntranceHandleHitDistance * EntranceHandleHitDistance;
            var found = -1;
            for (var i = 0; i < _entrances.Count; i++)
            {
                if (!TryEntranceBoundary(_entrances[i], out var gate)) continue;
                var distance = math.distancesq(cursor, gate.Point);
                if (distance > best) continue;
                best = distance;
                found = i;
            }
            return found;
        }

        private bool EntranceHandleIsCloserThanPoint()
        {
            if (_hoverEntrance < 0) return false;
            if (_hoverPoint < 0) return true;
            if (!TryEntranceBoundary(_entrances[_hoverEntrance], out var gate))
                return false;
            var cursor = _hoverPosition.xz;
            // Bei gleicher Lage gewinnt die Ecke. Am rechten Standardwinkel
            // liegt der Zufahrtsgriff durch 1,0 + 5,9 + 3,5 = 10,4 m Versatz
            // ohnehin ausserhalb des 8,0-m-Eckgriffs; sonst entscheidet die
            // tatsaechlich kuerzere Cursorstrecke.
            return math.distancesq(cursor, gate.Point)
                < math.distancesq(cursor, _points[_hoverPoint]);
        }

        private void HandleEntranceLeftClick()
        {
            if (!_hasHover) return;
            if (_hoverEntrance >= 0)
            {
                _entranceDragUndo = CaptureUndoState();
                _dragEntrance = _hoverEntrance;
                _dragEntranceStart = CopyEntrance(_entrances[_dragEntrance]);
                _dragEntranceMoved = false;
                return;
            }

            if (!_hasEntranceCandidate || !_entranceCandidate.Valid) return;
            var before = CaptureUndoState();
            _entrances.Add(CopyEntrance(_entranceCandidate.Entrance));
            _entranceMissingPrompt = false;
            MarkEntrancesChanged("gesetzt");
            CommitUndoState(before, T("Zugang gesetzt", "access placed"));
        }

        /**
         * Shift maskiert CS2s unmodifizierte ProxyAction. Deshalb muss genau
         * dieser bereits erkannte Rohmaus-Klick denselben fachlichen Weg wie
         * ein normaler Klick nehmen. Der Fang selbst bleibt unangetastet.
         */
        private void HandleEntranceShiftLeftClick(bool proxyPressed)
        {
            _entranceShiftRawClicks++;
            if (proxyPressed) _entranceShiftProxyClicks++;

            var countBefore = _entrances.Count;
            var dragBefore = _dragEntrance;
            var hadCandidate = _hasEntranceCandidate;
            var validCandidate = hadCandidate && _entranceCandidate.Valid;
            HandleLeftClick();
            _entranceShiftForwardedClicks++;

            var result = _entrances.Count > countBefore
                ? "gesetzt"
                : dragBefore < 0 && _dragEntrance >= 0
                    ? "Verschieben begonnen"
                    : "nicht gesetzt";
            Mod.log.Info("PLT-Shift-Zufahrt: "
                + $"Art {_zufahrtsart}, Rohmaus {_entranceShiftRawClicks}, "
                + $"ProxyAction jetzt {(proxyPressed ? "ja" : "nein")} "
                + $"(Summe {_entranceShiftProxyClicks}), "
                + $"weitergereicht {_entranceShiftForwardedClicks}; "
                + $"Kandidaten gelesen {_entranceShiftCandidateReads}, "
                + $"davon mit Shift {_entranceShiftCandidateHeld}, "
                + $"Fangziele verworfen {_entranceShiftSnapTargetsDiscarded}; "
                + $"Klickpfad gelesen {_entranceShiftClickReads}, "
                + $"davon mit Shift {_entranceShiftClickHeld}; "
                + $"Kandidat {(hadCandidate ? "ja" : "nein")}, "
                + $"gueltig {(validCandidate ? "ja" : "nein")}, Ergebnis {result}.");
        }

        private bool EntranceShiftHeldForClick()
        {
            _entranceShiftClickReads++;
            var held = ShiftGehalten();
            if (held) _entranceShiftClickHeld++;
            return held;
        }

        private void UpdateEntranceDrag(bool cancel)
        {
            if (_dragEntrance < 0) return;
            if (cancel)
            {
                _entrances[_dragEntrance] = CopyEntrance(_dragEntranceStart);
                _dragEntrance = -1;
                _dragEntranceMoved = false;
                _entranceDragUndo = null;
                return;
            }

            if (applyAction != null && applyAction.IsPressed())
            {
                if (!_hasEntranceCandidate || !_entranceCandidate.Valid) return;
                if (SameEntrance(_entrances[_dragEntrance], _entranceCandidate.Entrance))
                    return;
                _entrances[_dragEntrance] = CopyEntrance(_entranceCandidate.Entrance);
                _dragEntranceMoved = true;
                return;
            }

            var movedIndex = _dragEntrance;
            _dragEntrance = -1;
            if (_dragEntranceMoved)
            {
                var artChanged = _dragEntranceStart != null
                    && _dragEntranceStart.Art
                        != _entrances[movedIndex].Art;
                MarkEntrancesChanged("verschoben");
                CommitUndoState(_entranceDragUndo, artChanged
                    ? "Zugangsart geändert" : "Zugang verschoben");
            }
            _dragEntranceMoved = false;
            _entranceDragUndo = null;
        }

        /**
         * Rechtsklick-Kaskade fuer den geschlossenen Zufahrt-Modus.
         *
         * Zeigt der Cursor auf eine Zufahrt, loescht der Rechtsklick GENAU
         * DIESE. Vorher traf es immer die zuletzt gesetzte - um an eine
         * fruehere heranzukommen, musste man alle danach gesetzten mit
         * wegwerfen.
         *
         * `_hoverEntrance` ist an dieser Stelle aktuell: `Update` ruft erst
         * `UpdateEntranceInteraction`, danach `StepBack`.
         */
        private bool StepBackEntrance()
        {
            if (!_closed || !_entranceMode) return false;
            var target = _hoverEntrance >= 0 && _hoverEntrance < _entrances.Count
                ? _hoverEntrance
                : _entrances.Count - 1;
            if (target < 0)
            {
                LeaveEntrancePlacement();
                return true;
            }

            var before = CaptureUndoState();
            var wasLastPlaced = target == _entrances.Count - 1;
            _entrances.RemoveAt(target);
            _entranceMissingPrompt = false;
            // `MarkEntrancesChanged` raeumt den Zeigerzustand ab. Der NAECHSTE
            // Rechtsklick findet damit keine Zufahrt mehr unter dem Cursor und
            // faellt in den leeren Fall - das Loeschen der letzten Zufahrt
            // beendet den Modus also nicht, erst der Klick danach.
            MarkEntrancesChanged(wasLastPlaced ? "letzte geloescht"
                : "unter dem Zeiger geloescht");
            CommitUndoState(before, T("Zugang entfernt", "access removed"));
            return true;
        }

        private void MarkEntrancesChanged(string action)
        {
            _geometryRevision++;
            _layoutDirty = _closed;
            _buildRequestedWhenReady = false;
            ClearEntranceGesture();
            PublishEntranceState();
            _uiSystem?.SetStatus(EntranceCountText());
            Mod.log.Info($"PLT-Zufahrt {action}: {_entrances.Count}/{MaxEntranceCount} gesetzt.");
        }

        private string EntranceCountText()
            => _entrances.Count == 1
                ? T("1 Zufahrt gesetzt.", "1 entrance placed.")
                : T($"{_entrances.Count} Zufahrten gesetzt.",
                    $"{_entrances.Count} entrances placed.");

        private bool TryBuildEntranceCandidate(float2 raw, int ignoredIndex,
            out EntranceCandidate candidate)
        {
            candidate = default;
            if (!TryProjectToBoundary(raw, EntranceBoundarySnapDistance, out var gate))
                return false;

            var settings = CurrentEntranceSettings();
            var minimumSpacing = MinimumEntranceSpacing(settings);
            var hasBounds = TryNormalEntranceBounds(gate.Edge, settings,
                out var minimumAlong, out var maximumAlong);
            var rawAlong = gate.Along;
            var along = rawAlong;
            string corner = null;
            var direction = gate.Inward;
            var length = (float)(settings.Es + settings.Sl);
            var snapped = false;
            var hasGuide = false;
            var guideA = float2.zero;
            var guideB = float2.zero;
            var bestDelta = EntranceRoadSnapDistance + 1e-4f;
            var bestSpatial = float.PositiveInfinity;

            /**
             * SHIFT SCHALTET DEN FANG AB.
             *
             * Wie ueberall im Spiel: gedrueckte Umschalttaste heisst "ich will
             * genau hierhin, nicht dorthin, wo du es fuer richtig haeltst".
             * Der Randfang bleibt - eine Zufahrt gehoert immer auf die Kante -
             * aber weder Strasse noch Ecke ziehen dann noch.
             */
            _entranceShiftCandidateReads++;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var freehand = keyboard != null
                && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (freehand) _entranceShiftCandidateHeld++;

            void Consider(double targetAlong, float2 targetDirection,
                double targetLength, string targetCorner, float2 a, float2 b,
                float spatial, bool road)
            {
                if (freehand)
                {
                    _entranceShiftSnapTargetsDiscarded++;
                    return;
                }
                if (targetCorner == null && (!hasBounds
                    || targetAlong < minimumAlong - ParkingGeometry.FitEps
                    || targetAlong > maximumAlong + ParkingGeometry.FitEps)) return;
                var delta = math.abs((float)targetAlong - rawAlong);
                if (delta > EntranceRoadSnapDistance) return;
                /**
                 * DIE NAECHSTE STRASSE GEWINNT, nicht die bequemste.
                 *
                 * Vorher entschied `delta` - der Versatz ENTLANG der
                 * Polygonkante - und die Entfernung zur Strasse war nur
                 * Stichentscheid. Damit konnte eine weiter entfernte, womoeglich
                 * schief liegende Strasse gewinnen, nur weil sie die Zufahrt
                 * weniger verschob. Im Spiel gemeldet als "snappt an die
                 * uebernaechste".
                 *
                 * Jetzt umgekehrt: `spatial` entscheidet, `delta` ist der
                 * Stichentscheid bei praktisch gleicher Entfernung.
                 */
                if (spatial > bestSpatial + 1e-4f) return;
                if (math.abs(spatial - bestSpatial) <= 1e-4f
                    && delta >= bestDelta - 1e-5f) return;
                bestDelta = delta;
                bestSpatial = spatial;
                along = (float)targetAlong;
                direction = targetDirection;
                length = (float)targetLength;
                corner = targetCorner;
                snapped = road || targetCorner != null;
                hasGuide = true;
                guideA = a;
                guideB = b;
            }

            void ConsiderRoads(float2[][] roads)
            {
                if (roads == null) return;
                for (var i = 0; i < roads.Length; i++)
                {
                    var line = roads[i];
                    if (line == null || line.Length < 2) continue;
                    var vector = line[1] - line[0];
                    var roadLength = math.length(vector);
                    if (roadLength < 0.5f) continue;
                    var roadDirection = vector / roadLength;
                    if (math.abs(math.dot(roadDirection, gate.Inward))
                        < EntranceSnapCos) continue;
                    var denominator = EntranceCross(gate.Tangent, roadDirection);
                    if (math.abs(denominator) <= EntranceLineEpsilon) continue;
                    var targetAlong = EntranceCross(
                        line[0] - _points[gate.Edge], roadDirection)
                        / denominator;
                    if (!math.isfinite(targetAlong)) continue;
                    var point = _points[gate.Edge] + gate.Tangent * targetAlong;
                    var spatial = DistanceToSegment(point, line[0], line[1]);
                    Consider(targetAlong, gate.Inward, settings.Es + settings.Sl,
                        null, line[0], line[1], spatial, road: true);
                }
            }

            if (_areaPreviewLayout != null)
            {
                ConsiderRoads(_areaPreviewLayout.AisleLine);
                ConsiderRoads(_areaPreviewLayout.CrossLine);
                // Die eigene Randstrasse fehlte im HTML-Array; hier ist sie
                // ausdruecklich dabei, damit auch ihr verlaengerter Ast faengt.
                ConsiderRoads(_areaPreviewLayout.PerimeterLine);
            }

            var site = _points.ToArray();
            for (var end = 0; end < 2; end++)
                if (ParkingGeometry.TryEntranceCornerPlacement(site, settings,
                    gate.Edge, end == 0, out var fit))
                {
                    var start = _points[gate.Edge] + gate.Tangent * (float)fit.Along;
                    var finish = start + fit.Direction * (float)fit.Length;
                    Consider(fit.Along, fit.Direction, fit.Length,
                        end == 0 ? "start" : "end", start, finish, 0f, road: false);
                }

            var blockReason = EntranceBlockReason.None;
            if (!snapped)
            {
                if (!hasBounds)
                {
                    along = math.clamp(rawAlong, 0f, gate.EdgeLength);
                    blockReason = EntranceBlockReason.NoRoom;
                }
                else
                {
                    along = math.clamp(rawAlong, minimumAlong, maximumAlong);
                }
            }

            var entrance = new Entrance
            {
                Edge = gate.Edge, Along = along, Corner = corner,
                Art = _zufahrtsart,
            };
            var pointAtBoundary = _points[gate.Edge] + gate.Tangent * along;
            var valid = blockReason == EntranceBlockReason.None;
            if (ignoredIndex < 0 && _entrances.Count >= MaxEntranceCount)
            {
                valid = false;
                blockReason = EntranceBlockReason.Maximum;
            }
            else if (valid && !HasEntranceSpacing(entrance, ignoredIndex,
                settings, out _))
            {
                valid = false;
                blockReason = EntranceBlockReason.Spacing;
            }
            /*
             * KEINE ZUFAHRT INS BAULAND.
             *
             * Ansage des Nutzers: *"Zufahrten sowie Fusswege an ZF und RZ
             * verhindern bzw. andersherum."* Die andere Richtung - Bauland
             * weicht der Zufahrt nicht - steht in `ZoningLiegtImUmriss`; das
             * hier ist die Gegenprobe beim Setzen.
             *
             * Beim RANDZONING ist es zwangslaeufig: sein Bauland belegt das
             * ganze Aussenband der Linie, und dort quert jede Zufahrt.
             */
            else if (valid && ZufahrtTrifftBauland(entrance))
            {
                valid = false;
                blockReason = EntranceBlockReason.Bauland;
            }

            candidate = new EntranceCandidate
            {
                Entrance = entrance,
                Point = pointAtBoundary,
                Tangent = gate.Tangent,
                Direction = direction,
                Length = length,
                Width = (float)settings.Ai,
                MinimumSpacing = minimumSpacing,
                Valid = valid,
                HasGuide = hasGuide,
                GuideA = guideA,
                GuideB = guideB,
                BlockReason = blockReason,
            };
            return true;
        }

        private bool TryNormalEntranceBounds(int edge, LayoutSettings settings,
            out float minimum, out float maximum)
        {
            minimum = maximum = 0f;
            if (edge < 0 || edge >= _points.Count) return false;
            var length = math.distance(_points[edge], _points[(edge + 1) % _points.Count]);
            if (length < EntranceLineEpsilon) return false;
            var site = _points.ToArray();
            var startCorner = ParkingGeometry.TryEntranceCornerPlacement(
                site, settings, edge, true, out _);
            var endCorner = ParkingGeometry.TryEntranceCornerPlacement(
                site, settings, edge, false, out _);
            var normalMargin = (float)(settings.Ai / 2 + 2 * settings.Sw);
            minimum = startCorner ? 0f : normalMargin;
            maximum = length - (endCorner ? 0f : normalMargin);
            return minimum <= maximum + ParkingGeometry.FitEps;
        }

        /**
         * Standardmass: 7,0 m Zufahrt plus zwei 3,0-m-Kapseln = 13,0 m.
         * Gleichheit ist erlaubt; nur echte Ueberdeckung wird blockiert.
         */
        private bool HasEntranceSpacing(Entrance candidate, int ignoredIndex,
            LayoutSettings settings, out float nearestDistance)
            => HasEntranceSpacingAgainst(candidate, _entrances, ignoredIndex,
                settings, out nearestDistance);

        private bool HasEntranceSpacingAgainst(Entrance candidate,
            IReadOnlyList<Entrance> existingEntrances, int ignoredIndex,
            LayoutSettings settings, out float nearestDistance)
        {
            nearestDistance = float.PositiveInfinity;
            if (!TryEntranceBoundary(candidate, out var candidateGate)) return false;
            var minimumArc = MinimumEntranceSpacing(settings);
            var minimumDirect = (float)settings.Ai;
            for (var i = 0; i < existingEntrances.Count; i++)
            {
                if (i == ignoredIndex) continue;
                if (!TryEntranceBoundary(existingEntrances[i], out var existing)) continue;
                var arc = math.abs(candidateGate.Arc - existing.Arc);
                arc = math.min(arc, candidateGate.Perimeter - arc);
                nearestDistance = math.min(nearestDistance, arc);
                if (arc < minimumArc - ParkingGeometry.FitEps
                    || math.distance(candidateGate.Point, existing.Point)
                        < minimumDirect - ParkingGeometry.FitEps)
                    return false;
            }
            return true;
        }

        private bool TryProjectToBoundary(float2 point, float maximumDistance,
            out BoundaryProjection projection)
        {
            projection = default;
            if (_points.Count < MinPolygonPoints) return false;
            var perimeter = PolygonPerimeter();
            if (perimeter < 1f) return false;

            var bestDistance = float.PositiveInfinity;
            var prefix = 0f;
            for (var edge = 0; edge < _points.Count; edge++)
            {
                var a = _points[edge];
                var b = _points[(edge + 1) % _points.Count];
                var vector = b - a;
                var lengthSquared = math.lengthsq(vector);
                var length = math.sqrt(lengthSquared);
                if (lengthSquared <= EntranceLineEpsilon)
                {
                    prefix += length;
                    continue;
                }
                var along = math.clamp(math.dot(point - a, vector) / lengthSquared,
                    0f, 1f) * length;
                var tangent = vector / length;
                var nearest = a + tangent * along;
                var distance = math.distancesq(point, nearest);
                if (distance < bestDistance)
                {
                    var winding = SignedPolygonArea() >= 0 ? 1f : -1f;
                    projection = new BoundaryProjection
                    {
                        Edge = edge,
                        Point = nearest,
                        Tangent = tangent,
                        Inward = new float2(-tangent.y, tangent.x) * winding,
                        Along = along,
                        Arc = prefix + along,
                        Perimeter = perimeter,
                        EdgeLength = length,
                    };
                    bestDistance = distance;
                }
                prefix += length;
            }
            return bestDistance <= maximumDistance * maximumDistance;
        }

        private bool TryEntranceBoundary(Entrance entrance,
            out BoundaryProjection projection)
        {
            projection = default;
            if (entrance == null || entrance.Edge < 0 || entrance.Edge >= _points.Count)
                return false;
            var a = _points[entrance.Edge];
            var b = _points[(entrance.Edge + 1) % _points.Count];
            var vector = b - a;
            var length = math.length(vector);
            if (length < EntranceLineEpsilon) return false;
            var tangent = vector / length;
            var perimeter = PolygonPerimeter();
            var arc = 0f;
            for (var i = 0; i < entrance.Edge; i++)
                arc += math.distance(_points[i], _points[(i + 1) % _points.Count]);
            arc = Repeat(arc + (float)entrance.Along, perimeter);
            var winding = SignedPolygonArea() >= 0 ? 1f : -1f;
            projection = new BoundaryProjection
            {
                Edge = entrance.Edge,
                Point = a + tangent * (float)entrance.Along,
                Tangent = tangent,
                Inward = new float2(-tangent.y, tangent.x) * winding,
                Along = (float)entrance.Along,
                Arc = arc,
                Perimeter = perimeter,
                EdgeLength = length,
            };
            return true;
        }

        private float PolygonPerimeter()
        {
            var perimeter = 0f;
            for (var i = 0; i < _points.Count; i++)
                perimeter += math.distance(_points[i], _points[(i + 1) % _points.Count]);
            return perimeter;
        }

        private double SignedPolygonArea()
        {
            var area = 0.0;
            for (var i = 0; i < _points.Count; i++)
            {
                var a = _points[i];
                var b = _points[(i + 1) % _points.Count];
                area += (double)a.x * b.y - (double)b.x * a.y;
            }
            return area / 2;
        }

        private static float Repeat(float value, float length)
        {
            if (length <= 0f) return value;
            value %= length;
            return value < 0f ? value + length : value;
        }

        private static float EntranceCross(float2 a, float2 b)
            => a.x * b.y - a.y * b.x;

        private static float DistanceToSegment(float2 point, float2 a, float2 b)
        {
            var vector = b - a;
            var lengthSquared = math.lengthsq(vector);
            if (lengthSquared < EntranceLineEpsilon) return math.distance(point, a);
            var t = math.clamp(math.dot(point - a, vector) / lengthSquared, 0f, 1f);
            return math.distance(point, a + vector * t);
        }

        private void RefreshEntranceOverlay()
        {
            _entranceOverlay.Clear();
            if (!_closed || !_entranceMode || _terrainSystem == null) return;
            var heightData = _terrainSystem.GetHeightData();
            float3 World(float2 point)
            {
                var world = new float3(point.x, 0f, point.y);
                world.y = TerrainUtils.SampleHeight(ref heightData, world);
                return world;
            }

            for (var i = 0; i < _entrances.Count; i++)
                if (TryEntranceBoundary(_entrances[i], out var gate))
                    _entranceOverlay.Handles.Add(World(gate.Point));
            _entranceOverlay.HoverIndex = _hoverEntrance;
            _entranceOverlay.DragIndex = _dragEntrance;
            if (!_hasEntranceCandidate) return;

            var candidate = _entranceCandidate;
            var roadEnd = candidate.Point + candidate.Direction * candidate.Length;
            var halfSpacing = candidate.MinimumSpacing / 2;
            _entranceOverlay.HasCandidate = true;
            _entranceOverlay.CandidateValid = candidate.Valid;
            _entranceOverlay.CandidateWidth = candidate.Width;
            _entranceOverlay.CandidateRoad = new Line3.Segment(
                World(candidate.Point), World(roadEnd));
            _entranceOverlay.CandidateSpacing = new Line3.Segment(
                World(candidate.Point - candidate.Tangent * halfSpacing),
                World(candidate.Point + candidate.Tangent * halfSpacing));
            _entranceOverlay.HasSnapGuide = candidate.HasGuide;
            if (candidate.HasGuide)
                _entranceOverlay.SnapGuide = new Line3.Segment(
                    World(candidate.GuideA), World(candidate.GuideB));
        }

        private void UpdateEntranceHint()
        {
            if (!_closed || !_entranceMode)
            {
                _debugTooltipSystem?.ClearEntranceHint();
                return;
            }

            string text;
            if (_entranceMissingPrompt && _entrances.Count == 0)
                text = T("Bitte eine Zufahrt setzen",
                    "Place an entrance");
            else if (_hasEntranceCandidate
                && _entranceCandidate.BlockReason == EntranceBlockReason.Maximum)
                text = T($"Höchstens {MaxEntranceCount} Zufahrten",
                    $"At most {MaxEntranceCount} entrances");
            else if (_hasEntranceCandidate
                && _entranceCandidate.BlockReason == EntranceBlockReason.Spacing)
                text = T($"Mindestens {_entranceCandidate.MinimumSpacing:F1} m Abstand",
                    $"At least {_entranceCandidate.MinimumSpacing:F1} m apart");
            else if (_hasEntranceCandidate
                && _entranceCandidate.BlockReason == EntranceBlockReason.NoRoom)
                text = T("Zu wenig Rand für eine Zufahrt",
                    "Not enough edge for an entrance");
            else if (_hasEntranceCandidate
                && _entranceCandidate.BlockReason == EntranceBlockReason.Bauland)
                text = T("Hier liegt Bauland - Zoningfläche oder Randzoning",
                    "Building land here - a zoning patch or edge zoning");
            else if (_hoverEntrance >= 0)
                text = T("Zufahrt anklicken und am Rand verschieben",
                    "Click the entrance and drag it along the edge");
            else if (_entrances.Count >= MaxEntranceCount)
                text = T($"Höchstens {MaxEntranceCount} Zufahrten",
                    $"At most {MaxEntranceCount} entrances");
            else
                text = T("Linksklick setzt eine Zufahrt auf den Umriss",
                    "Left click places an entrance on the outline");
            _debugTooltipSystem?.SetEntranceHint(text);
        }

    }
}
