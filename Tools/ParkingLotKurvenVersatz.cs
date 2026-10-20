using static ParkingLotTool.Tools.ParkingLotTexte;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * EINE KURVE ALS GANZES VERSCHIEBEN (Nutzer, 19.10.).
     *
     * Eine gerade Kante wandert auf IHRER Normalen; eine Kurve hat an jeder
     * Stelle eine andere. Deshalb wird sie parallel versetzt: jeder
     * Zwischenpunkt rueckt auf der Winkelhalbierenden so weit, dass jede
     * Sehne genau den Versatz von ihrer alten Lage hat. Die beiden Enden
     * gleiten dabei immer auf ihren Nachbarkanten - das Shift-Verhalten des
     * Kantenziehens ist hier der Standard ("alles andere macht keinen Sinn").
     *
     * Der Zug laeuft ueber `_dragEdge` (die angefasste Sehne), damit Undo,
     * Vorschau und Eingabesperren ihn wie einen Kantenzug behandeln.
     *
     * Fang wie beim Kantenzug (Bordstein, Strasse); Shift schaltet ihn ab.
     *
     * Grenzen: nur Kurven, deren Enden an geraden Kanten haengen. Nach dem
     * Versatz liegt die Kurve nicht mehr am Bordstein; ein spaeter gezogenes
     * Ende findet dort keinen Fang und macht die Kante gerade.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class Kurvenversatzzug
        {
            internal Kurvenkante Kurve;
            internal int[] Kette;
            internal int Vor, Nach;
            internal float2 Normale;
            internal float2 Von, Bis;
            internal float2[] Zwischen;
        }

        private int _hoverKurve = -1;
        private Kurvenversatzzug _kurvenzug;

        /** Die Kurve zur Sehne `sehne`, wenn sie sich als Ganzes ziehen laesst. */
        private int ZiehbareKurve(int sehne)
        {
            int n = _points.Count;
            var a = _points[sehne];
            var b = _points[(sehne + 1) % n];
            for (int i = 0; i < _kurvenkanten.Count; i++)
            {
                var k = _kurvenkanten[i];
                if (!GehoertZurKurve(k, a) || !GehoertZurKurve(k, b)) continue;
                // Haengt ein Ende an einer zweiten Kurve, risse der Versatz sie auf.
                for (int j = 0; j < _kurvenkanten.Count; j++)
                {
                    if (j == i) continue;
                    var o = _kurvenkanten[j];
                    if (GehoertZurKurve(o, k.Von) || GehoertZurKurve(o, k.Bis)) return -1;
                }
                // Zum Versetzen braucht die Kurve mindestens einen Punkt ausserhalb (Nachbarkanten).
                return KurvenKette(k) == null || n < k.Zwischen.Length + 3 ? -1 : i;
            }
            return -1;
        }

        private static bool GehoertZurKurve(Kurvenkante k, float2 p)
        {
            if (math.all(k.Von == p) || math.all(k.Bis == p)) return true;
            foreach (var z in k.Zwischen) if (math.all(z == p)) return true;
            return false;
        }

        /** Punktnummern von `Von` ueber alle Zwischenpunkte bis `Bis`. */
        private int[] KurvenKette(Kurvenkante k)
        {
            int n = _points.Count;
            int von = _points.IndexOf(k.Von);
            // Ein U aus nur EINER Kurve plus Schliesskante hat keinen Punkt daneben (20.10.).
            if (von < 0 || k.Zwischen.Length == 0 || n < k.Zwischen.Length + 2) return null;
            int richtung = math.all(_points[(von + 1) % n] == k.Zwischen[0]) ? 1 : -1;
            var kette = new int[k.Zwischen.Length + 2];
            for (int i = 0; i < kette.Length; i++) kette[i] = ((von + richtung * i) % n + n) % n;
            for (int i = 0; i < k.Zwischen.Length; i++)
                if (!math.all(_points[kette[i + 1]] == k.Zwischen[i])) return null;
            return math.all(_points[kette[kette.Length - 1]] == k.Bis) ? kette : null;
        }

        private void BeginKurvenzug()
        {
            var k = _kurvenkanten[_hoverKurve];
            var kette = KurvenKette(k);
            if (kette == null) return;
            if (_edgeDragUndo == null) _edgeDragUndo = CaptureUndoState();
            CaptureEntrancesForPointDrag();
            int n = _points.Count;
            int richtung = kette[1] == (kette[0] + 1) % n ? 1 : -1;
            _kurvenzug = new Kurvenversatzzug
            {
                Kurve = k, Kette = kette,
                Vor = ((kette[0] - richtung) % n + n) % n,
                Nach = ((kette[kette.Length - 1] + richtung) % n + n) % n,
                Von = k.Von, Bis = k.Bis, Zwischen = (float2[])k.Zwischen.Clone(),
            };
            _dragEdge = _hoverEdge;
            _dragEdgeStartPoints = _points.ToArray();
            _dragEdgeStartWorld = _worldPoints.ToArray();
            _dragEdgeStartCursor = _hoverPosition.xz;
            var a = _points[_dragEdge];
            var b = _points[(_dragEdge + 1) % n];
            var lot = AufGitter(NearestOnSegment(_hoverPosition.xz, a, b));
            _dragEdgeInsertPoint = new float3(lot.x, _worldPoints[_dragEdge].y, lot.y);
            _edgeGuide = _dragEdgeInsertPoint;
            _dragEdgeOffset = 0f;
            // Linke Normale in Kettenrichtung - dieselbe, auf die sich der Versatz bezieht.
            var sehne = richtung == 1 ? b - a : a - b;
            _kurvenzug.Normale = math.normalizesafe(new float2(-sehne.y, sehne.x));
        }

        private void UpdateKurvenzug()
        {
            if (LinkeMausGehalten())
            {
                if (!_hasHover) return;
                var roh = math.dot(_hoverPosition.xz - _dragEdgeStartCursor, _kurvenzug.Normale);
                // Shift braucht die Kurve nicht zum Gleiten (das ist hier Standard);
                // es schaltet den Fang ab, wie beim Setzen der Zufahrten.
                _edgeSnapped = false;
                var versatz = ShiftGehalten() ? roh : FangeKurvenversatz(roh);
                SetzeKurvenversatz(SichererKurvenversatz(versatz));
                return;
            }
            var z = _kurvenzug;
            _kurvenzug = null;
            if (math.abs(_dragEdgeOffset) > EdgeMoveEpsilon)
            {
                if (z.Kurve.Zettel != null)
                    z.Kurve.Zettel = z.Kurve.Zettel.MitEnden(z.Kurve.Von, z.Kurve.Bis);
                ReprojectEntrancesAfterPointDrag();
                _geometryRevision++;
                _layoutDirty = true;
                _polygonTouched = true;
                CommitUndoState(_edgeDragUndo, () => T("undo.kanteVerschoben"));
                Mod.log.Info($"PLT-Kurve: als Ganzes um {_dragEdgeOffset:F2} m versetzt, {z.Zwischen.Length} Zwischenpunkte.");
            }
            else
            {
                RestoreEdgeStart();
                SetzeKurvenpunkte(z.Kurve, z.Von, z.Bis, z.Zwischen);
            }
            _entrancePositionsBeforePointDrag = null;
            _edgeDragUndo = null;
            _dragEdge = -1;
            _dragEdgeStartPoints = null;
            _dragEdgeStartWorld = null;
            _edgeSnapped = false;
        }

        /**
         * FANG WIE BEIM KANTENZUG: der Versatz ist die eine Zahl, die gefangen
         * wird. Getastet wird am Anfasspunkt und in der Mitte der ersten und
         * letzten Sehne, jeweils auf deren eigener Normalen - eine Kurve hat
         * keine gemeinsame. Es gewinnt der Treffer, der dem Wunsch am
         * naechsten liegt.
         */
        private float FangeKurvenversatz(float roh)
        {
            var z = _kurvenzug;
            int m = z.Kette.Length;
            var c0 = _dragEdgeStartPoints[z.Kette[0]];
            var c1 = _dragEdgeStartPoints[z.Kette[1]];
            var d0 = _dragEdgeStartPoints[z.Kette[m - 2]];
            var d1 = _dragEdgeStartPoints[z.Kette[m - 1]];
            var u0 = math.normalizesafe(c1 - c0);
            var u1 = math.normalizesafe(d1 - d0);
            var sonden = new[]
            {
                (_dragEdgeInsertPoint.xz, z.Normale),
                ((c0 + c1) * 0.5f, new float2(-u0.y, u0.x)),
                ((d0 + d1) * 0.5f, new float2(-u1.y, u1.x)),
            };
            var beste = roh;
            var abstand = float.PositiveInfinity;
            foreach (var (basis, normale) in sonden)
            {
                var sonde = new float3(basis.x + normale.x * roh, _dragEdgeInsertPoint.y,
                    basis.y + normale.y * roh);
                var gefangen = ApplySnapping(sonde, nurWelt: true);
                // Ob getroffen wurde, sagt `LastSnap`, nicht der Rueckgabewert.
                if (LastSnap == SnapKind.None) continue;
                var kandidat = math.dot(gefangen.xz - basis, normale);
                var weg = math.abs(kandidat - roh);
                if (weg >= abstand) continue;
                abstand = weg;
                beste = kandidat;
                _edgeSnapped = true;
                _edgeSnapPoint = gefangen;
            }
            return beste;
        }

        /** Die Kette bei Versatz `d`, aus den Startpunkten gerechnet. */
        private float2[] Kurvenversatz(float d)
        {
            var z = _kurvenzug;
            int m = z.Kette.Length;
            var c = new float2[m];
            for (int i = 0; i < m; i++) c[i] = _dragEdgeStartPoints[z.Kette[i]];
            var normalen = new float2[m - 1];
            for (int i = 0; i < m - 1; i++)
            {
                var u = math.normalizesafe(c[i + 1] - c[i]);
                normalen[i] = new float2(-u.y, u.x);
            }
            var neu = new float2[m];
            neu[0] = GleiteEnde(c[0], _dragEdgeStartPoints[z.Vor], normalen[0], d);
            neu[m - 1] = GleiteEnde(c[m - 1], _dragEdgeStartPoints[z.Nach], normalen[m - 2], d);
            for (int i = 1; i < m - 1; i++)
            {
                var a = normalen[i - 1];
                var b = normalen[i];
                // Schnitt beider versetzter Sehnengeraden: auf jeder Normalen genau `d`.
                neu[i] = c[i] + (a + b) * (d / (1f + math.dot(a, b)));
            }
            return neu;
        }

        // Wie `EckeVerschieben` mit Shift: auf der Nachbarkante, bis die Sehne `d` weit liegt.
        private static float2 GleiteEnde(float2 ende, float2 nachbar, float2 normale, float d)
        {
            var richtung = ende - nachbar;
            var laenge = math.length(richtung);
            if (laenge < 1e-6f) return ende + normale * d;
            richtung /= laenge;
            var anteil = math.dot(richtung, normale);
            if (math.abs(anteil) < 0.25f) return ende + normale * d;
            return ende + richtung * (d / anteil);
        }

        private void SetzeKurvenversatz(float d)
        {
            var z = _kurvenzug;
            var neu = Kurvenversatz(d);
            for (int i = 0; i < neu.Length; i++)
            {
                int index = z.Kette[i];
                neu[i] = AufGitter(neu[i]);
                _points[index] = neu[i];
                _worldPoints[index] = new float3(neu[i].x, _dragEdgeStartWorld[index].y, neu[i].y);
            }
            var zwischen = new float2[neu.Length - 2];
            for (int i = 0; i < zwischen.Length; i++) zwischen[i] = neu[i + 1];
            SetzeKurvenpunkte(z.Kurve, neu[0], neu[neu.Length - 1], zwischen);
            _dragEdgeOffset = d;
            var shift = z.Normale * d;
            _edgeGuide = new float3(_dragEdgeInsertPoint.x + shift.x, _dragEdgeInsertPoint.y,
                _dragEdgeInsertPoint.z + shift.y);
        }

        private void SetzeKurvenpunkte(Kurvenkante k, float2 von, float2 bis, float2[] zwischen)
        {
            foreach (var p in k.Zwischen) _kurvenZwischen.Remove(p);
            k.Von = von;
            k.Bis = bis;
            k.Zwischen = zwischen;
            foreach (var p in zwischen) _kurvenZwischen.Add(p);
        }

        /** Der groesste Versatz bis `wanted`, bei dem Umriss und Kurve heil bleiben. */
        private float SichererKurvenversatz(float wanted)
        {
            if (KurvenversatzGueltig(wanted)) return wanted;
            float low = 0f, high = wanted;
            for (int step = 0; step < 12; step++)
            {
                float middle = (low + high) * 0.5f;
                if (KurvenversatzGueltig(middle)) low = middle;
                else high = middle;
            }
            return low;
        }

        private bool KurvenversatzGueltig(float d)
        {
            var z = _kurvenzug;
            var neu = Kurvenversatz(d);
            // Keine Sehne darf umklappen (Versatz ueber den Kruemmungsradius) und
            // kein Ende ueber seine Nachbarkante hinaus gleiten.
            for (int i = 0; i + 1 < neu.Length; i++)
            {
                var alt = _dragEdgeStartPoints[z.Kette[i + 1]] - _dragEdgeStartPoints[z.Kette[i]];
                if (math.dot(neu[i + 1] - neu[i], alt) <= 0f) return false;
            }
            var vor = _dragEdgeStartPoints[z.Vor];
            var nach = _dragEdgeStartPoints[z.Nach];
            if (math.dot(neu[0] - vor, _dragEdgeStartPoints[z.Kette[0]] - vor) <= 0f) return false;
            if (math.dot(neu[neu.Length - 1] - nach,
                    _dragEdgeStartPoints[z.Kette[z.Kette.Length - 1]] - nach) <= 0f) return false;
            var probe = (float2[])_dragEdgeStartPoints.Clone();
            for (int i = 0; i < neu.Length; i++) probe[z.Kette[i]] = neu[i];
            return PolygonSimple(probe);
        }

        /** Die ganze Kurve leuchtet, nicht nur die Sehne unter dem Zeiger. */
        private void ZeichneKurvenzug(ParkingLotPreviewBuffer buffer)
        {
            int[] kette = null;
            if (_kurvenzug != null) kette = _kurvenzug.Kette;
            else if (_hoverKurve >= 0 && _hoverKurve < _kurvenkanten.Count && _dragEdge < 0)
                kette = KurvenKette(_kurvenkanten[_hoverKurve]);
            if (kette == null) return;
            int n = _worldPoints.Count;
            for (int i = 0; i + 1 < kette.Length; i++)
                buffer.DrawLine(ParkingLotPreviewStyle.ShiftColor,
                    new Colossal.Mathematics.Line3.Segment(_worldPoints[kette[i]], _worldPoints[kette[i + 1]]),
                    ParkingLotPreviewStyle.SelectedLineWidth, false);
            int richtung = kette[1] == (kette[0] + 1) % n ? 1 : -1;
            var vor = _worldPoints[((kette[0] - richtung) % n + n) % n];
            var nach = _worldPoints[((kette[kette.Length - 1] + richtung) % n + n) % n];
            buffer.DrawDashedLine(ParkingLotPreviewStyle.ShiftColor,
                new Colossal.Mathematics.Line3.Segment(vor, _worldPoints[kette[0]]),
                ParkingLotPreviewStyle.PreviewLineWidth, ParkingLotPreviewStyle.DashLength, ParkingLotPreviewStyle.DashGap);
            buffer.DrawDashedLine(ParkingLotPreviewStyle.ShiftColor,
                new Colossal.Mathematics.Line3.Segment(_worldPoints[kette[kette.Length - 1]], nach),
                ParkingLotPreviewStyle.PreviewLineWidth, ParkingLotPreviewStyle.DashLength, ParkingLotPreviewStyle.DashGap);
        }
    }
}
