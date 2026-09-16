using System.Collections.Generic;
using static ParkingLotTool.Tools.ParkingLotTexte;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace ParkingLotTool.Tools
{
    /**
     * KANTEN VERSCHIEBEN, nicht nur Punkte.
     *
     * Wunsch des Nutzers am 2026-08-20, zum Testen: eine Kante millimeterweise
     * verschieben und zusehen, ab wann die Streifen auftauchen - statt jedes
     * Mal die ganze Form neu zu zeichnen.
     *
     * Die Rechnung ist aus dem Browser-Prototyp uebernommen
     * (`StreetBlockAlgo/parking-linien.html`, `findEdgeHit`,
     * `edgeDragPolygon`, `limitedEdgeDragPolygon`). Zwei Dinge daraus sind
     * nicht Beiwerk, sondern der Kern:
     *
     *   1. Ein Punkt unter dem Zeiger schlaegt IMMER die Kante. Sonst
     *      erwischt man an einer Ecke die falsche Sache, und zwar genau dort,
     *      wo beides dicht beieinanderliegt.
     *   2. Der Zug wird per Halbierungssuche dort angehalten, wo sich das
     *      Polygon selbst ueberschneiden wuerde. Ohne das zieht man die Form
     *      irgendwann durch sich selbst - und selbstueberschneidende Polygone
     *      sind genau das, was nie entstehen darf.
     *
     * WAS ES NICHT GIBT: den Zeigerwechsel des Prototyps. Der setzt je nach
     * Kantenrichtung `ew-resize`, `ns-resize`, `nwse-resize` oder
     * `nesw-resize`. Diese vier kommen in CS2s `index.css` NULL-mal vor -
     * das Spiel kennt nur `default`, `pointer`, `none` und `url(...)`, und
     * Cohtml haette sie stillschweigend verworfen. Die Rueckmeldung steht
     * deshalb in der Welt: die getroffene Kante leuchtet auf, und zwei Pfeile
     * auf ihrer Normalen zeigen, wohin sie sich bewegt. Dieselbe Aussage,
     * nur an einem Ort, an dem CS2 sie auch darstellen kann.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Wie nah der Zeiger an einer Kante sein muss. Kleiner als
         * `PointHitDistance` (8 m), damit an einer Ecke wirklich der Punkt
         * gewinnt und nicht die Kante, die dort ja ebenfalls durchlaeuft.
         */
        private const float EdgeHitDistance = 5f;

        /** Ab welcher Verschiebung der Zug als echte Aenderung zaehlt. */
        private const float EdgeMoveEpsilon = 0.05f;

        /**
         * Wie nah ein Polygonpunkt liegen muss, damit die Kante auf ihn
         * einrastet. Ein Meter: spuerbar, aber nicht klebrig - bei mehr
         * faengt die Kante Punkte ein, die man gar nicht treffen wollte.
         */
        private const float EdgeSnapDistance = 1f;

        /**
         * EINEN PUNKT AUF EINER KANTE EINFUEGEN.
         *
         * Der Prototyp macht es implizit: ein Klick auf eine Kante, der kein
         * Zug wird, fuegt ein. Das ist sparsam, aber unsichtbar - man erfaehrt
         * es nur, wenn es einem jemand sagt. Ansage des Nutzers am
         * 2026-08-20: Strg+Klick, und solange Strg gedrueckt ist, muss man an
         * den Linien SEHEN, dass jetzt etwas anderes passiert.
         *
         * Deshalb zwei getrennte Rueckmeldungen: ohne Strg leuchtet die Kante
         * mit dem Doppelpfeil ihrer Normalen (verschieben), mit Strg
         * stattdessen in der Farbe des Schliessens, mit einem Ring genau an
         * der Stelle, an der der neue Punkt entstehen wuerde.
         */
        private bool _insertReady;
        private float3 _insertPosition;

        internal bool InsertReady => _insertReady;
        internal float3 InsertPosition => _insertPosition;

        /**
         * Strg ODER Alt - beide taugen, also darf der Nutzer waehlen.
         *
         * Shift bleibt aussen vor: das benutzt CS2 in seinen eigenen
         * Werkzeugen fuer gerade Linien, und wer es hier belegt, kaempft
         * frueher oder spaeter gegen eine Gewohnheit.
         */
        private static bool ModifierHeld()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            // NUR Strg. Alt stand hier bis zum 2026-08-21 daneben, aus der
            // Zeit, als unklar war, welcher Modifier ueberhaupt durchkommt.
            // Der Nutzer: "Alt halten bringt das gleiche Ergebnis wie Strg
            // halten, darf aber nicht." Alt ist jetzt das Extrudieren.
            return keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        }

        /** Alt allein - der Modifier fuers Extrudieren einer Kante. */
        private static bool AltGehalten()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)
                && !keyboard.leftCtrlKey.isPressed && !keyboard.rightCtrlKey.isPressed;
        }

        /**
         * Auf das Millimeterraster runden - dasselbe, das die Geometrie
         * ohnehin anwendet (`ParkingGeometry.Gitter`). Hier schon zu runden
         * heisst, dass die gespeicherten Punkte und das Bauprotokoll exakt
         * das enthalten, womit spaeter gerechnet wird; sonst steht im
         * Protokoll ein anderer Wert als der, der gebaut wurde.
         */
        private static float2 AufGitter(float2 punkt)
        {
            var g = (float)ParkingGeometry.Gitter;
            return new float2(math.round(punkt.x / g) * g, math.round(punkt.y / g) * g);
        }

        private int _hoverEdge = -1;
        private int _dragEdge = -1;
        private float2[] _dragEdgeStartPoints;
        private float3[] _dragEdgeStartWorld;
        private float2 _dragEdgeNormal;
        private float2 _dragEdgeStartCursor;

        /**
         * Wo ein Klick auf diese Kante einen Punkt einfuegen wuerde.
         *
         * Gemerkt beim Greifen, nicht beim Loslassen: beim Loslassen steht
         * der Zeiger schon woanders, und der neue Punkt soll dort entstehen,
         * wo man hingeklickt hat.
         */
        private float3 _dragEdgeInsertPoint;

        /**
         * Der Anfasspunkt: wo auf der Kante der Zeiger sitzt beziehungsweise
         * gegriffen wurde. Er ist der Bezug fuer die Anzeige - der gelbe
         * Strich quer zur Linie geht durch ihn, und die beiden Pfeile sind
         * seine Enden. Ohne ihn zeigt die Anzeige nur "diese Kante", nicht
         * "hier haeltst du sie fest".
         */
        private float3 _edgeGuide;
        private float _dragEdgeOffset;

        internal float3 EdgeGuide => _edgeGuide;

        /** Der Punkt, auf dem die Kante gerade einrastet - fuer die Anzeige. */
        private bool _edgeSnapped;
        private float3 _edgeSnapPoint;

        internal bool EdgeSnapped => _edgeSnapped;
        internal float3 EdgeSnapPoint => _edgeSnapPoint;

        internal int HoverEdge => _hoverEdge;
        internal int DragEdge => _dragEdge;

        /**
         * Welche Kante liegt unter dem Zeiger?
         *
         * Laeuft NACH `UpdatePointHover`, damit `_hoverPoint` schon steht:
         * ein getroffener Punkt macht die Kantensuche sofort gegenstandslos.
         */
        private void UpdateEdgeHover()
        {
            _hoverEdge = -1;
            if (!_closed || !_hasHover || _dragPoint >= 0 || _dragEdge >= 0) return;
            if (_hoverPoint >= 0) return;
            // Im Zufahrtsmodus setzt der Linksklick Zufahrten auf genau diese
            // Kanten. Beides auf denselben Klick zu legen waere nicht zu
            // bedienen, also gilt hier das eine oder das andere.
            if (_entranceMode) return;
            if (_points.Count < MinPolygonPoints) return;

            var cursor = _hoverPosition.xz;
            var best = EdgeHitDistance * EdgeHitDistance;
            for (var i = 0; i < _points.Count; i++)
            {
                var a = _points[i];
                var b = _points[(i + 1) % _points.Count];
                var distanceSquared = math.lengthsq(cursor - NearestOnSegment(cursor, a, b));
                if (distanceSquared > best) continue;
                best = distanceSquared;
                _hoverEdge = i;
            }

            // ZURUECKSETZEN VOR DEM AUSSTIEG. Ohne diese Zeile blieb die
            // Fahne stehen, sobald der Zeiger die Kante verliess: der Nutzer
            // hielt einmal Strg ueber einer Linie, liess los, und der
            // naechste ganz normale Klick legte trotzdem einen Punkt an
            // (gemeldet am 2026-08-21).
            if (_hoverEdge < 0)
            {
                _insertReady = false;
                return;
            }
            // Die Lotrechte des Zeigers auf die Kante - sie ist zugleich der
            // Anfasspunkt fuer die Anzeige und die Stelle, an der ein neuer
            // Punkt entstehen wuerde. Auf das Gitter gerundet, nie daneben.
            var lot = AufGitter(NearestOnSegment(cursor, _points[_hoverEdge],
                _points[(_hoverEdge + 1) % _points.Count]));
            var hoehe = _worldPoints[_hoverEdge].y;
            _edgeGuide = new float3(lot.x, hoehe, lot.y);
            _insertReady = ModifierHeld();
            if (_insertReady) _insertPosition = _edgeGuide;
        }

        /**
         * Setzt den neuen Punkt hinter den Anfang seiner Kante.
         *
         * Die Zufahrten muessen mit: sie haengen an Kantennummern, und durch
         * das Einfuegen verschiebt sich jede Nummer dahinter um eins. Dieselben
         * zwei Helfer wie beim Punktziehen rechnen sie ueber ihre Weltlage
         * neu ein, statt die Nummern zu raten.
         */
        private void InsertPointOnHoveredEdge()
        {
            var before = CaptureUndoState();
            CaptureEntrancesForPointDrag();
            var stelle = _hoverEdge + 1;
            _points.Insert(stelle, _insertPosition.xz);
            _worldPoints.Insert(stelle, _insertPosition);
            _pointAxes.Insert(stelle, float2.zero);
            SyncSnapAxes();
            ReprojectEntrancesAfterPointDrag();
            _entrancePositionsBeforePointDrag = null;
            _hoverEdge = -1;
            _insertReady = false;
            _hoverPoint = stelle;
            _geometryRevision++;
            _layoutDirty = true;
            _polygonTouched = true;
            CommitUndoState(before, T("Punkt auf Kante eingefügt",
                "point inserted on edge"));
            Mod.log.Info($"PLT-Polygon: Punkt {stelle} auf einer Kante eingefuegt "
                + $"({_insertPosition.x:F3} / {_insertPosition.z:F3}); "
                + $"jetzt {_points.Count} Punkte.");
        }

        private static float2 NearestOnSegment(float2 point, float2 a, float2 b)
        {
            var ab = b - a;
            var lengthSquared = math.lengthsq(ab);
            if (lengthSquared < 1e-9f) return a;
            var t = math.clamp(math.dot(point - a, ab) / lengthSquared, 0f, 1f);
            return a + ab * t;
        }

        /** Merkt sich den Ausgangszustand; ohne ihn ist kein Zurueck moeglich. */
        /**
         * ALT + ZIEHEN AN EINER KANTE: EXTRUDIEREN, wie in Blender.
         *
         * Wunsch des Nutzers am 2026-08-21, mit seiner eigenen Skizze:
         *
         *     A o--------o B                A o--------o B----o E
         *       |        |                    |             |
         *       |        o C        ->        |        o C----o F
         *       |        |                    |        |
         *     D o--------o                  D o--------o
         *
         * Die beiden urspruenglichen Punkte bleiben liegen; an der neuen
         * Stelle entstehen zwei zusaetzliche. Aus dem Polygon waechst also ein
         * Rechteck heraus, statt dass sich die Kante verschiebt.
         *
         * Umgesetzt als VORBEREITUNG plus gewoehnlicher Kantenzug: die zwei
         * neuen Punkte werden sofort als Kopien eingesetzt, und gezogen wird
         * dann die Kante ZWISCHEN ihnen. Damit gelten Fang, Gitter und die
         * Sicherheitspruefung des normalen Zuges unveraendert - kein zweiter
         * Weg, der eigene Fehler haben kann.
         */
        private void BeginEdgeExtrude()
        {
            _edgeDragUndo = CaptureUndoState();
            var a = _hoverEdge;
            var b = (_hoverEdge + 1) % _points.Count;
            CaptureEntrancesForPointDrag();
            // Erst den hinteren einsetzen, sonst verschiebt sich sein Index.
            _points.Insert(a + 1, _points[b]);
            _worldPoints.Insert(a + 1, _worldPoints[b]);
            _pointAxes.Insert(a + 1, float2.zero);
            _points.Insert(a + 1, _points[a]);
            _worldPoints.Insert(a + 1, _worldPoints[a]);
            _pointAxes.Insert(a + 1, float2.zero);
            SyncSnapAxes();
            ReprojectEntrancesAfterPointDrag();
            _entrancePositionsBeforePointDrag = null;
            _extrudeStart = a + 1;
            _hoverEdge = a + 1;
            BeginEdgeDrag();
        }

        /**
         * Bleibt der Zug aus, muessen die zwei Punkte wieder weg.
         *
         * Sonst haette das Polygon nach einem blossen Alt-Klick zwei
         * Doppelpunkte und zwei Nullkanten - genau die entarteten Ringe, an
         * denen CS2 Flaechen verwirft.
         */
        private void VerwirfExtrude()
        {
            if (_extrudeStart < 0) return;
            CaptureEntrancesForPointDrag();
            _points.RemoveRange(_extrudeStart, 2);
            _worldPoints.RemoveRange(_extrudeStart, 2);
            _pointAxes.RemoveRange(_extrudeStart, 2);
            SyncSnapAxes();
            ReprojectEntrancesAfterPointDrag();
            _entrancePositionsBeforePointDrag = null;
            _extrudeStart = -1;
        }

        private int _extrudeStart = -1;

        private void BeginEdgeDrag()
        {
            if (_edgeDragUndo == null) _edgeDragUndo = CaptureUndoState();
            _dragEdge = _hoverEdge;
            _dragEdgeStartPoints = _points.ToArray();
            _dragEdgeStartWorld = _worldPoints.ToArray();
            _dragEdgeStartCursor = _hoverPosition.xz;
            var lot = AufGitter(NearestOnSegment(_hoverPosition.xz,
                _points[_dragEdge], _points[(_dragEdge + 1) % _points.Count]));
            _dragEdgeInsertPoint =
                new float3(lot.x, _worldPoints[_dragEdge].y, lot.y);
            _edgeGuide = _dragEdgeInsertPoint;
            _dragEdgeOffset = 0f;

            var a = _points[_dragEdge];
            var b = _points[(_dragEdge + 1) % _points.Count];
            var tangent = b - a;
            var length = math.length(tangent);
            _dragEdgeNormal = length < 1e-6f
                ? new float2(0f, 1f)
                : new float2(-tangent.y, tangent.x) / length;
            CaptureEntrancesForPointDrag();
        }

        /**
         * Ein Zug je Bild: der Zeiger bestimmt nur EINE Zahl, naemlich wie
         * weit die Kante auf ihrer Normalen wandert. Alles andere - beide
         * Endpunkte, die Grenze gegen die Selbstueberschneidung - folgt
         * daraus. Deshalb bleibt die Kante beim Ziehen auch parallel zu sich
         * selbst, egal wie der Zeiger daneben steht.
         */
        /**
         * IST DIE LINKE MAUSTASTE NOCH UNTEN?
         *
         * `applyAction` ist eine ProxyAction, und CS2 maskiert sie, solange
         * ein Modifier gehalten wird. Genau daran scheiterten Shift+Ziehen und
         * Alt+Ziehen: der Zug begann und wurde IM SELBEN FRAME wieder als
         * "kein Zug" verworfen, weil `IsPressed()` schon false meldete. Im
         * Spiel sah das aus, als passiere gar nichts.
         *
         * Bei gehaltenem Modifier wird die Taste deshalb direkt gelesen -
         * eng begrenzt auf genau diesen Fall, damit der gewoehnliche Zug
         * weiter ueber das Eingabesystem des Spiels laeuft.
         */
        private bool LinkeMausGehalten()
        {
            if (applyAction != null && applyAction.IsPressed()) return true;
            if (!ModifierHeld() && !AltGehalten() && !ShiftGehalten()) return false;
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
        }

        private void UpdateEdgeDrag()
        {
            if (LinkeMausGehalten())
            {
                if (!_hasHover) return;
                // Der Zeiger ist an dieser Stelle schon eingerastet, wenn er
                // an Strasse, Bordstein oder Nachbarflaeche haengt - die
                // Kante folgt ihm also dorthin. Der Punktfang kommt hier
                // zusaetzlich dazu.
                /**
                 * DER ANFASSPUNKT IST DIE SONDE.
                 *
                 * Genau wie ein gezogener Polygonpunkt: nicht der Zeiger
                 * rastet ein, sondern die Stelle, an der man die Kante
                 * haelt. Sie kann sich nur auf der Normalen bewegen, also
                 * wird aus jedem Fangtreffer genau eine Zahl - der Versatz.
                 *
                 * Der gewuenschte Versatz kommt aus dem ROHEN Zeiger. Damit
                 * haengt die Sonde nicht am eigenen Ergebnis, und die
                 * Schaukel von vorhin kann gar nicht erst entstehen.
                 */
                var roh = math.dot(_hoverPosition.xz - _dragEdgeStartCursor,
                                   _dragEdgeNormal);
                /**
                 * DIE GANZE KANTE ABTASTEN, NICHT NUR DEN ANFASSPUNKT.
                 *
                 * Nutzerbefund vom 2026-08-21: an einer L-foermigen Strasse
                 * rastet die Kante entlang der Strasse ein, aber nicht an den
                 * ECKEN des L. Der Grund: getastet wurde genau eine Stelle -
                 * der Anfasspunkt, verschoben auf der Normalen. Alles, was
                 * weiter vorne oder hinten an der Kante liegt, konnte gar
                 * nicht gefunden werden.
                 *
                 * Jetzt werden Anfang, Mitte und Ende der Kante getastet.
                 * Jeder Treffer wird ueber die Normale in einen Versatz
                 * umgerechnet; es gewinnt der, der dem Wunsch am naechsten
                 * liegt. Drei Sonden statt einer - mehr waere teurer, ohne
                 * mehr zu finden: eine Ecke, die keiner der drei sieht, liegt
                 * ohnehin weiter weg als der Fangradius.
                 */
                var weltTraf = false;
                var ausWelt = roh;
                var weltAbstand = float.PositiveInfinity;
                var weltPunkt = float3.zero;
                var kanteA = _dragEdgeStartPoints[_dragEdge];
                var kanteB = _dragEdgeStartPoints[
                    (_dragEdge + 1) % _dragEdgeStartPoints.Length];
                foreach (var t in new[] { 0f, 0.5f, 1f })
                {
                    var basis = math.lerp(kanteA, kanteB, t);
                    var sonde = new float3(
                        basis.x + _dragEdgeNormal.x * roh,
                        _dragEdgeInsertPoint.y,
                        basis.y + _dragEdgeNormal.y * roh);
                    var gefangen = ApplySnapping(sonde, nurWelt: true);
                    /**
                     * OB der Weltfang getroffen hat, sagt `LastSnap` - NICHT
                     * der Rueckgabewert. Findet `ApplySnapping` nichts, gibt
                     * es den Sondenpunkt unveraendert zurueck; der Abstand
                     * waere dann null und gewaenne jeden Vergleich, ohne
                     * etwas gefunden zu haben.
                     */
                    if (LastSnap == SnapKind.None) continue;
                    var kandidat = math.dot(gefangen.xz - basis, _dragEdgeNormal);
                    var weg = math.abs(kandidat - roh);
                    if (weg >= weltAbstand) continue;
                    weltAbstand = weg;
                    ausWelt = kandidat;
                    weltPunkt = gefangen;
                    weltTraf = true;
                }

                var ausPunkten = SnapEdgeOffset(roh);
                var punktTraf = _edgeSnapped;

                // Treffen beide, gewinnt der naehere - sonst zerrten zwei
                // Ziele an derselben Kante.
                var offset = roh;
                if (weltTraf && punktTraf)
                {
                    if (math.abs(ausWelt - roh) <= math.abs(ausPunkten - roh))
                    {
                        offset = ausWelt;
                        _edgeSnapped = true;
                        _edgeSnapPoint = weltPunkt;
                    }
                    else offset = ausPunkten;
                }
                else if (weltTraf)
                {
                    offset = ausWelt;
                    _edgeSnapped = true;
                    _edgeSnapPoint = weltPunkt;
                }
                else if (punktTraf) offset = ausPunkten;

                ApplyEdgeOffsetMitZoningsperre(SafeEdgeOffset(offset));
                return;
            }

            var moved = math.lengthsq(_points[_dragEdge]
                                      - _dragEdgeStartPoints[_dragEdge])
                        > EdgeMoveEpsilon * EdgeMoveEpsilon;
            if (moved)
            {
                ReprojectEntrancesAfterPointDrag();
                _geometryRevision++;
                _layoutDirty = true;
                _polygonTouched = true;
                _entrancePositionsBeforePointDrag = null;
                _dragEdge = -1;
                _extrudeStart = -1;
                CommitUndoState(_edgeDragUndo,
                    _edgeDragUndo != null && _edgeDragUndo.Points.Length
                        != _points.Count ? "Kante extrudiert" : "Kante verschoben");
                _edgeDragUndo = null;
            }
            else
            {
                /**
                 * EIN KLICK, DER KEIN ZUG WURDE, TUT NICHTS.
                 *
                 * Hier stand bis zum 2026-08-21 das Gegenteil: ein Klick ohne
                 * Zug fuegte einen Punkt ein. Das war ein Rueckfallweg aus der
                 * Zeit, als Strg+Klick das Werkzeug gar nicht erreichte - CS2
                 * maskiert unveraenderte Maustasten, solange ein Modifier
                 * gehalten wird. Seit der Umstellung auf `Mouse.current`
                 * kommt Strg+Klick zuverlaessig an, und der Rueckfallweg war
                 * nur noch stoerend:
                 *
                 *   "Ich will nur mit Strg + Klicken einen neuen Polygonpunkt
                 *    setzen, nicht per normalem Klick."
                 *
                 * Einfuegen laeuft jetzt ausschliesslich ueber
                 * `InsertPointOnHoveredEdge` an den beiden Klickpfaden, die
                 * den Modifier pruefen.
                 */
                RestoreEdgeStart();
                _dragEdge = -1;
                _dragEdgeStartPoints = null;
                _dragEdgeStartWorld = null;
                _edgeSnapped = false;
                VerwirfExtrude();
                _edgeDragUndo = null;
                return;
            }
            _dragEdgeStartPoints = null;
            _dragEdgeStartWorld = null;
            _edgeSnapped = false;
        }

        /**
         * FANG AUF POLYGONPUNKTE.
         *
         * Eine Kante hat nur EINEN Freiheitsgrad: wie weit sie auf ihrer
         * Normalen wandert. Also wird auch nur diese eine Zahl gefangen. Der
         * Versatz, bei dem die Kante genau durch einen Punkt liefe, ist der
         * vorzeichenbehaftete Abstand dieses Punktes von der Ausgangslinie -
         * das ist exakt, nicht genaehert.
         *
         * Die beiden eigenen Endpunkte bleiben aussen vor: die wandern ja mit.
         */
        private float SnapEdgeOffset(float wanted)
        {
            _edgeSnapped = false;
            var aIndex = _dragEdge;
            var bIndex = (_dragEdge + 1) % _dragEdgeStartPoints.Length;
            var anker = _dragEdgeStartPoints[aIndex];
            var beste = wanted;
            var naechste = EdgeSnapDistance;
            for (var i = 0; i < _dragEdgeStartPoints.Length; i++)
            {
                if (i == aIndex || i == bIndex) continue;
                var kandidat = math.dot(_dragEdgeStartPoints[i] - anker,
                                        _dragEdgeNormal);
                var weg = math.abs(kandidat - wanted);
                if (weg > naechste) continue;
                naechste = weg;
                beste = kandidat;
                _edgeSnapped = true;
                _edgeSnapPoint = _dragEdgeStartWorld[i];
            }
            return beste;
        }

        private void RestoreEdgeStart()
        {
            for (var i = 0; i < _points.Count; i++)
            {
                _points[i] = _dragEdgeStartPoints[i];
                _worldPoints[i] = _dragEdgeStartWorld[i];
            }
        }

        /** Setzt die Kante auf einen Versatz; die Hoehe bleibt, wie sie war. */
        /**
         * SHIFT: die Ecke rutscht auf IHRER NACHBARKANTE, statt gerade zu wandern.
         *
         * Wunsch des Nutzers am 2026-08-21, mit Verweis auf den Prototyp. Der
         * macht es in `edgeDragPolygon` genau so - und es ist NICHT das Gitter,
         * wie er vermutete:
         *
         *   ohne Shift   beide Endpunkte wandern auf der Normalen; die
         *                Nachbarkanten kippen dabei schraeg
         *   mit Shift    jeder Endpunkt gleitet die Nachbarkante entlang; die
         *                Nachbarn behalten ihre Richtung, nur ihre Laenge
         *                aendert sich
         *
         * Die Weite auf der Nachbarkante folgt aus der Bedingung, dass die
         * gezogene Kante am Ende trotzdem genau `offset` auf ihrer Normalen
         * liegt. Steht die Nachbarkante fast parallel zur gezogenen, wird der
         * Nenner winzig und die Ecke schoesse ins Nichts - dann bleibt es beim
         * geraden Weg.
         */
        private float2 EckeVerschieben(int ecke, int nachbar, float offset, float2 gerade)
        {
            var start = _dragEdgeStartPoints[ecke];
            if (!ShiftGehalten() || _dragEdgeStartPoints.Length < 3)
                return start + gerade;
            var n = _dragEdgeStartPoints.Length;
            var anderer = _dragEdgeStartPoints[((nachbar % n) + n) % n];
            var richtung = start - anderer;
            var laenge = math.length(richtung);
            if (laenge < 1e-6f) return start + gerade;
            richtung /= laenge;
            var anteil = math.dot(richtung, _dragEdgeNormal);
            // Unter 15 Grad zur gezogenen Kante wird der Weg laenger als
            // fuenf Versaetze - das ist kein Gleiten mehr, das ist ein Sprung.
            if (math.abs(anteil) < 0.25f) return start + gerade;
            return start + richtung * (offset / anteil);
        }

        /** Shift allein - haelt die Nachbarkanten in ihrer Richtung. */
        private static bool ShiftGehalten()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        /**
         * Wie `ApplyEdgeOffset`, lehnt aber ab, was eine Parzelle
         * hinausschoebe.
         *
         * Geprueft wird am ERGEBNIS, nicht an der Eingabe: der Zug schiebt
         * je nach Lage zwei Punkte, kann beim Ausstuelpen sogar welche
         * einfuegen, und was dabei herauskommt, weiss nur `ApplyEdgeOffset`
         * selbst. Deshalb ausfuehren, nachsehen und im Zweifel
         * zuruecknehmen - der Umriss hat ein Dutzend Punkte, das kostet
         * nichts.
         */
        private void ApplyEdgeOffsetMitZoningsperre(float offset)
        {
            if (Zoningflaechen.Count == 0)
            {
                ApplyEdgeOffset(offset);
                return;
            }
            var punkteVorher = _points.ToArray();
            var weltVorher = _worldPoints.ToArray();
            var achsenVorher = _pointAxes.ToArray();
            ApplyEdgeOffset(offset);
            if (ZoningVertraegtUmriss(_points.ToArray())) return;
            _points.Clear();
            _points.AddRange(punkteVorher);
            _worldPoints.Clear();
            _worldPoints.AddRange(weltVorher);
            _pointAxes.Clear();
            _pointAxes.AddRange(achsenVorher);
        }

        private void ApplyEdgeOffset(float offset)
        {
            var aIndex = _dragEdge;
            var bIndex = (_dragEdge + 1) % _points.Count;
            var shift = _dragEdgeNormal * offset;
            // Aufs Millimeterraster, wie die gezeichneten Punkte. Ohne das
            // landet eine verschobene Kante auf krummen Werten, und genau
            // daraus entstehen die haarfeinen Reststreifen.
            _points[aIndex] = AufGitter(EckeVerschieben(aIndex, aIndex - 1, offset, shift));
            _points[bIndex] = AufGitter(EckeVerschieben(bIndex, bIndex + 1, offset, shift));
            // Der Anfasspunkt wandert mit - er bleibt die Stelle, an der man
            // die Kante festhaelt, nicht ein Punkt im Nirgendwo.
            _dragEdgeOffset = offset;
            _edgeGuide = new float3(
                _dragEdgeInsertPoint.x + shift.x,
                _dragEdgeInsertPoint.y,
                _dragEdgeInsertPoint.z + shift.y);
            _worldPoints[aIndex] = new float3(_points[aIndex].x,
                _dragEdgeStartWorld[aIndex].y, _points[aIndex].y);
            _worldPoints[bIndex] = new float3(_points[bIndex].x,
                _dragEdgeStartWorld[bIndex].y, _points[bIndex].y);
        }

        /**
         * Der groesste Versatz, bei dem das Polygon noch heil ist.
         *
         * Halbierungssuche wie im Prototyp: taugt der gewuenschte Versatz,
         * wird er genommen. Sonst wird zwischen null und ihm so lange
         * halbiert, bis der Rand des Erlaubten steht. Zwoelf Schritte reichen
         * fuer Millimetergenauigkeit ueber hundert Meter (100 / 2^12 = 2,4 cm,
         * und das gilt fuer den ABSTAND zur Grenze, nicht fuer die Lage).
         */
        private float SafeEdgeOffset(float wanted)
        {
            if (EdgeOffsetValid(wanted)) return wanted;
            var low = 0f;
            var high = wanted;
            for (var step = 0; step < 12; step++)
            {
                var middle = (low + high) * 0.5f;
                if (EdgeOffsetValid(middle)) low = middle;
                else high = middle;
            }
            return low;
        }

        private bool EdgeOffsetValid(float offset)
        {
            var probe = new float2[_dragEdgeStartPoints.Length];
            for (var i = 0; i < probe.Length; i++) probe[i] = _dragEdgeStartPoints[i];
            var aIndex = _dragEdge;
            var bIndex = (_dragEdge + 1) % probe.Length;
            var shift = _dragEdgeNormal * offset;
            probe[aIndex] = _dragEdgeStartPoints[aIndex] + shift;
            probe[bIndex] = _dragEdgeStartPoints[bIndex] + shift;
            return PolygonSimple(probe);
        }

        /**
         * Ueberschneidet sich das Polygon selbst?
         *
         * Nachbarkanten teilen sich einen Endpunkt und werden deshalb
         * uebersprungen - sonst meldete jede Ecke einen Treffer. Geprueft
         * wird nur zwischen Kanten, die NICHT benachbart sind.
         */
        private static bool PolygonSimple(IReadOnlyList<float2> ring)
        {
            var n = ring.Count;
            if (n < 4) return true;
            for (var i = 0; i < n; i++)
            {
                var a1 = ring[i];
                var a2 = ring[(i + 1) % n];
                for (var j = i + 1; j < n; j++)
                {
                    // Benachbart oder identisch: kein Kandidat.
                    if (j == i || (j + 1) % n == i || (i + 1) % n == j) continue;
                    if (SegmentsCross(a1, a2, ring[j], ring[(j + 1) % n]))
                        return false;
                }
            }
            return true;
        }

        private static float Side(float2 a, float2 b, float2 p)
            => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        private static bool SegmentsCross(float2 a, float2 b, float2 c, float2 d)
        {
            var d1 = Side(a, b, c);
            var d2 = Side(a, b, d);
            var d3 = Side(c, d, a);
            var d4 = Side(c, d, b);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
    }
}
