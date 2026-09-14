using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        // CS2 zieht Area-Konturen vor der Triangulierung um 0,1 m ein.
        // Aktuelle Laufzeitabzuege: schmalheitsbedingte Ablehnung bis 0,172729 m,
        // kleinste angenommene Kontur 0,186619 m. Aufgerundet ergibt das 0,19 m.
        internal const double SurfaceNeckLimit = 0.19;
        private const double SurfaceJoinEpsilon = 1e-6;

        private sealed class SurfaceFeature
        {
            internal int Ring;
            internal int Point;
            internal int Edge;
            internal double T;
            internal double2 Projection;
            internal double Distance;
            internal bool Interior;
        }

        private sealed class SurfaceUnionChoice
        {
            internal int Other;
            internal double2[] Union;
            internal double Score;
        }

        // Quadriert verglichen statt ueber die Wurzel. Bei Weltkoordinaten um
        // 1300 und einem Epsilon von 1e-6 kann die Quadratsumme weder
        // ueberlaufen noch unterlaufen; das Ergebnis ist dasselbe, die
        // Wurzel entfaellt. Dieselbe Umstellung steht im JS-Modell.
        private static bool SurfaceSamePoint(double2 a, double2 b,
                                              double epsilon = SurfaceJoinEpsilon)
        {
            var delta = a - b;
            return delta.x * delta.x + delta.y * delta.y <= epsilon * epsilon;
        }

        private static double2[] SurfaceCcw(double2[] ring) =>
            SignedArea(ring) >= 0 ? ring.ToArray() : ring.Reverse().ToArray();

        /** Entfernt nur die exakt flaechennullige Folge A-B-A. */
        private static double2[] SurfaceRemoveZeroSpurs(double2[] ring)
        {
            var output = ring.ToList();
            for (var guard = 0; guard < 32 && output.Count >= 4; guard++)
            {
                var removed = false;
                for (var i = 0; i < output.Count; i++)
                {
                    var j = (i + 1) % output.Count;
                    var k = (i + 2) % output.Count;
                    if (!SurfaceSamePoint(output[i], output[k], 1e-8)) continue;
                    var rotated = output.Skip(i).Concat(output.Take(i)).ToList();
                    rotated.RemoveRange(1, 2);
                    output = rotated;
                    removed = true;
                    break;
                }
                if (!removed) break;
            }
            return output.ToArray();
        }

        /**
         * Nachschlagewerk fuer "liegt diese Kante auch auf einem ANDEREN Ring?".
         *
         * Zuvor lief dafuer bei jedem Kandidaten ein voller Durchlauf ueber
         * alle uebrigen Ringe. Zusammen mit der O(n^2)-Kandidatensuche in
         * MinimumSurfaceFeature ergab das O(Ringe^2 * n^3): ein gemessenes
         * Viereck von rund 75 m rechnete im Spiel laenger als eine Minute,
         * die Vorschau kam nie zurueck.
         *
         * Jetzt wird einmal je Aufruf indiziert. Punkte werden ueber ein
         * Zellenraster der Groesse SurfaceJoinEpsilon zusammengefasst; beim
         * Nachschlagen wird die 3x3-Nachbarschaft geprueft und mit
         * SurfaceSamePoint exakt nachgemessen, damit die Toleranz dieselbe
         * bleibt wie zuvor.
         *
         * Je Kante wird gemerkt, ob sie in genau einem Ring vorkommt (dessen
         * Index) oder in mehreren. Damit antwortet die Abfrage genau wie die
         * alte Schleife, auch wenn ein Ring dieselbe Kante zweimal traegt.
         */
        private const int SurfaceEdgeInSeveralRings = -2;

        private sealed class SurfaceEdgeIndex
        {
            // Spalten aussen, Zeilen innen: der Index wird je Engstellensuche
            // neu aufgebaut, beim 25 000-m2-Abzug 4433-mal. Mit einem flachen
            // Tupel-Woerterbuch kostete allein dieser Aufbau 2,3 der 5,2
            // Sekunden; die verschachtelte Form braucht je Punkt nur drei
            // aeussere statt neun voller Nachschlaege. Die Probe-Reihenfolge
            // (dx aufsteigend, dy aufsteigend) bleibt exakt die des JS-Modells.
            private readonly Dictionary<long, Dictionary<long, int>> _columns =
                new Dictionary<long, Dictionary<long, int>>();
            private readonly List<double2> _points = new List<double2>();
            private readonly Dictionary<long, int> _edges = new Dictionary<long, int>();

            internal SurfaceEdgeIndex(List<double2[]> rings)
            {
                for (var ringIndex = 0; ringIndex < rings.Count; ringIndex++)
                {
                    var ring = rings[ringIndex];
                    for (var i = 0; i < ring.Length; i++)
                    {
                        var key = EdgeKey(IdOf(ring[i]), IdOf(ring[(i + 1) % ring.Length]));
                        if (!_edges.TryGetValue(key, out var carrier))
                            _edges[key] = ringIndex;
                        else if (carrier != ringIndex)
                            _edges[key] = SurfaceEdgeInSeveralRings;
                    }
                }
            }

            internal bool HasSharedEdge(int own, double2 a, double2 b)
            {
                if (!_edges.TryGetValue(EdgeKey(IdOf(a), IdOf(b)), out var carrier))
                    return false;
                return carrier == SurfaceEdgeInSeveralRings || carrier != own;
            }

            // Punkt-Ids sind nicht negativ und bleiben weit unter 2^31; der
            // zusammengesetzte Kantenschluessel ist damit eindeutig.
            private static long EdgeKey(int a, int b) =>
                a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            private int IdOf(double2 point)
            {
                var column = (long)Math.Round(point.x / SurfaceJoinEpsilon);
                var row = (long)Math.Round(point.y / SurfaceJoinEpsilon);
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (!_columns.TryGetValue(column + dx, out var rows)) continue;
                    for (var dy = -1; dy <= 1; dy++)
                        if (rows.TryGetValue(row + dy, out var hit)
                            && SurfaceSamePoint(_points[hit], point))
                            return hit;
                }
                var id = _points.Count;
                _points.Add(point);
                if (!_columns.TryGetValue(column, out var ownRows))
                    _columns[column] = ownRows = new Dictionary<long, int>();
                ownRows[row] = id;
                return id;
            }
        }

        private static bool SurfaceRedundantCollinearPoint(double2[] ring, int index)
        {
            var before = ring[(index - 1 + ring.Length) % ring.Length];
            var point = ring[index];
            var after = ring[(index + 1) % ring.Length];
            var incoming = point - before;
            var outgoing = after - point;
            return Math.Abs(incoming.x * outgoing.y - incoming.y * outgoing.x) <= 1e-9
                && incoming.x * outgoing.x + incoming.y * outgoing.y > 0;
        }

        /**
         * Kleinste noch innerhalb eines Materialrings liegende Engstelle.
         * Eine kurze Gegenkante zweier verlustfrei geteilter Flaechen ist nur
         * deren gemeinsame Fuge und wird nicht als Hals gezaehlt.
         */
        /**
         * Kandidat eines einzelnen Rings. `Rank` ist die Besuchsreihenfolge
         * (point * n + edge) des alten vollen Durchlaufs; sie entscheidet bei
         * exakt gleichem Abstand, damit dieselbe Engstelle gewinnt wie zuvor.
         */
        private sealed class RingFeatureCandidate
        {
            internal int Point;
            internal int Edge;
            internal double T;
            internal double2 Projection;
            internal double Distance;
            internal bool Interior;
            internal long Rank;
            internal double2 PointValue;
            internal double2 EndpointValue;
        }

        private sealed class RingFeatureSet
        {
            internal RingFeatureCandidate Unconditional;
            internal List<RingFeatureCandidate> Conditional;
        }

        /**
         * Je-Ring-Zwischenspeicher der Engstellen-Kandidaten.
         *
         * Reparaturschleife und Bewertungen (BestSurfaceUnion, Brueckenreparatur,
         * Halsuebergabe) rufen die Engstellensuche hunderte Male auf, aendern je
         * Schritt aber nur ein bis drei Ringe. Vorher wurde trotzdem jedes Mal
         * jeder Ring komplett neu durchgerechnet: beim 25 000-m2-Polygon aus dem
         * 12:19-Abzug 4443 volle Durchlaeufe, 5,5 von 8,0 Sekunden — im Spiel
         * eine 10,5-Sekunden-Vorschau. Ringe sind nach ihrer Erzeugung
         * unveraenderlich (jede Reparatur ersetzt das Array), deshalb traegt
         * eine ConditionalWeakTable je Ring-Array.
         *
         * Alles Ringlokale liegt im Eintrag. Nur "liegt diese Kante auch auf
         * einem ANDEREN Ring?" haengt von der Ringmenge ab; solche Kandidaten
         * stehen in `Conditional` und werden erst bei der Abfrage gegen das
         * Kantenverzeichnis der aktuellen Menge geprueft. Das JS-Modell traegt
         * denselben Zwischenspeicher (surfaceRingFeatures).
         */
        private static readonly System.Runtime.CompilerServices
            .ConditionalWeakTable<double2[], RingFeatureSet> RingFeatureCache =
            new System.Runtime.CompilerServices
                .ConditionalWeakTable<double2[], RingFeatureSet>();

        private static RingFeatureSet SurfaceRingFeatures(double2[] ring)
        {
            if (RingFeatureCache.TryGetValue(ring, out var cached)) return cached;
            var n = ring.Length;
            RingFeatureCandidate unconditional = null;
            var conditional = new List<RingFeatureCandidate>();
            for (var pointIndex = 0; pointIndex < n; pointIndex++)
                for (var edgeIndex = 0; edgeIndex < n; edgeIndex++)
                {
                    var next = (edgeIndex + 1) % n;
                    if (edgeIndex == pointIndex || next == pointIndex) continue;
                    var point = ring[pointIndex];
                    var a = ring[edgeIndex];
                    var edge = ring[next] - a;
                    var lengthSquared = edge.x * edge.x + edge.y * edge.y;
                    if (lengthSquared < 1e-12) continue;
                    var rawT = ((point.x - a.x) * edge.x
                              + (point.y - a.y) * edge.y) / lengthSquared;
                    var t = Math.Max(0, Math.Min(1, rawT));
                    var projection = a + edge * t;
                    var distance = Len(point - projection);
                    // Nach dem float-Cast kann das Lot wenige Zehntelmillimeter
                    // vor dem Endpunkt landen. Topologisch bleibt es derselbe
                    // Endpunkt; sonst zaehlt eine redundante gerade Kurzkante
                    // faelschlich als innerer Hals.
                    var endpoint = Len(projection - a) <= 0.005 ? edgeIndex
                        : Len(projection - ring[next]) <= 0.005 ? next : -1;
                    var dependsOnSharedEdge = false;
                    if (endpoint >= 0)
                    {
                        var adjacent = (pointIndex + 1) % n == endpoint
                            || (endpoint + 1) % n == pointIndex;
                        // Zwischen zwei AUFEINANDERFOLGENDEN Knoten liegt eine
                        // Kante, kein Hals. Ihre Laenge sagt nichts ueber die
                        // Breite der Flaeche - solange sie CS2s
                        // Mindestknotenabstand von 5 cm einhaelt. Eine
                        // kuerzere Kante ist sehr wohl ein Mangel und wird
                        // weiter gemeldet; genau daran haengt die
                        // CS2-Tauglichkeit.
                        //
                        // Sichtbar wurde das, seit das Gruen als EIN Gebiet
                        // geschnitten wird: dabei entstehen regulaere kurze
                        // Randkanten (gemessen 0,187 m bei Schraeg), die als
                        // zu enge Flaeche galten und keinen Reparaturweg
                        // hatten - alle verlangen einen Lotfuss MITTEN auf
                        // einer Kante.
                        if (adjacent && distance >= 0.05) continue;
                        if (adjacent)
                        {
                            if (SurfaceRedundantCollinearPoint(ring, pointIndex)
                                || SurfaceRedundantCollinearPoint(ring, endpoint))
                                continue;
                            dependsOnSharedEdge = true;
                        }
                    }
                    var candidate = new RingFeatureCandidate
                    {
                        Point = pointIndex,
                        Edge = edgeIndex,
                        T = t,
                        Projection = projection,
                        Distance = distance,
                        Interior = endpoint < 0,
                        Rank = (long)pointIndex * n + edgeIndex,
                        PointValue = point,
                        EndpointValue = endpoint >= 0 ? ring[endpoint] : default,
                    };
                    if (dependsOnSharedEdge) conditional.Add(candidate);
                    else if (unconditional == null
                             || candidate.Distance < unconditional.Distance)
                        unconditional = candidate;
                }
            conditional.Sort((x, y) =>
            {
                var byDistance = x.Distance.CompareTo(y.Distance);
                return byDistance != 0 ? byDistance : x.Rank.CompareTo(y.Rank);
            });
            var result = new RingFeatureSet
            {
                Unconditional = unconditional,
                Conditional = conditional,
            };
            RingFeatureCache.Add(ring, result);
            return result;
        }

        private static SurfaceFeature MinimumSurfaceFeature(List<double2[]> rings)
        {
            SurfaceEdgeIndex sharedEdges = null;
            RingFeatureCandidate best = null;
            var bestRing = -1;
            for (var ringIndex = 0; ringIndex < rings.Count; ringIndex++)
            {
                var features = SurfaceRingFeatures(rings[ringIndex]);
                var ringBest = features.Unconditional;
                foreach (var candidate in features.Conditional)
                {
                    // Die Liste ist nach (Abstand, Rank) sortiert: sobald ein
                    // Kandidat den bisherigen Ringbesten nicht mehr schlagen
                    // kann, kann es keiner mehr.
                    if (ringBest != null
                        && !(candidate.Distance < ringBest.Distance
                             || (candidate.Distance == ringBest.Distance
                                 && candidate.Rank < ringBest.Rank))) break;
                    sharedEdges ??= new SurfaceEdgeIndex(rings);
                    if (!sharedEdges.HasSharedEdge(ringIndex,
                            candidate.PointValue, candidate.EndpointValue))
                    {
                        ringBest = candidate;
                        break;
                    }
                }
                if (ringBest != null && (best == null || ringBest.Distance < best.Distance))
                {
                    best = ringBest;
                    bestRing = ringIndex;
                }
            }
            if (best == null) return null;
            return new SurfaceFeature
            {
                Ring = bestRing,
                Point = best.Point,
                Edge = best.Edge,
                T = best.T,
                Projection = best.Projection,
                Distance = best.Distance,
                Interior = best.Interior,
            };
        }

        private static double2[] SurfaceInsertBoundaryPoints(
            double2[] ring, IEnumerable<double2> points)
        {
            var candidates = points.ToArray();
            var output = new List<double2>();
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                output.Add(a);
                var edge = b - a;
                var lengthSquared = edge.x * edge.x + edge.y * edge.y;
                if (lengthSquared < 1e-12) continue;
                var inside = new List<(double T, double2 Point)>();
                foreach (var point in candidates)
                {
                    var t = ((point.x - a.x) * edge.x
                           + (point.y - a.y) * edge.y) / lengthSquared;
                    if (t <= 1e-8 || t >= 1 - 1e-8) continue;
                    var projection = a + edge * t;
                    if (Len(point - projection) <= SurfaceJoinEpsilon)
                        inside.Add((t, point));
                }
                foreach (var item in inside.OrderBy(x => x.T))
                    if (!SurfaceSamePoint(output[output.Count - 1], item.Point))
                        output.Add(item.Point);
            }
            return output.ToArray();
        }

        /**
         * Vereinigt kantengenau benachbarte Ringe. Eingefuegt werden nur
         * Punkte auf einer vorhandenen gemeinsamen Geraden; die Flaechensumme
         * ist eine harte Nachbedingung. Es wird kein Nahpunkt verschoben.
         */
        private static bool TrySurfaceUnion(double2[] first, double2[] second,
                                            out double2[] union)
        {
            union = null;
            var a = SurfaceCcw(first);
            var b = SurfaceCcw(second);
            a = SurfaceInsertBoundaryPoints(a, b);
            b = SurfaceInsertBoundaryPoints(b, a);
            a = SurfaceInsertBoundaryPoints(a, b);

            for (var i = 0; i < a.Length; i++)
                for (var j = 0; j < b.Length; j++)
                {
                    if (!SurfaceSamePoint(a[i], b[(j + 1) % b.Length])
                        || !SurfaceSamePoint(a[(i + 1) % a.Length], b[j])) continue;
                    var ring = new List<double2>(a.Length + b.Length - 2);
                    for (var step = 0; step < a.Length; step++)
                        ring.Add(a[(i + 1 + step) % a.Length]);
                    for (var step = 1; step < b.Length - 1; step++)
                        ring.Add(b[(j + 1 + step) % b.Length]);
                    var clean = MergeCollinear(
                        SurfaceRemoveZeroSpurs(ring.ToArray()), 1e-9);
                    var wanted = Math.Abs(SignedArea(first)) + Math.Abs(SignedArea(second));
                    var got = Math.Abs(SignedArea(clean));
                    if (SelfIntersects(clean, 2e-6)
                        || Math.Abs(got - wanted) > Math.Max(1e-6, wanted * 1e-9))
                        continue;
                    /**
                     * KEINE VEREINIGUNG, DIE EINEN SCHLITZ ERZEUGT.
                     *
                     * Um mehrere Belagstuecke zu EINER Flaeche zu machen, die
                     * Gruen-Inseln umschliesst, muesste der Ring von aussen
                     * einen Schlitz zu jeder Insel ziehen - CS2 kann keine
                     * Loecher. So ein Schlitz ist im Spiel sichtbar: der Nutzer
                     * schickte am 2026-08-18 ein Bild mit einem duennen
                     * Asphaltkeil, der spitz in die Gruenflaeche schneidet.
                     *
                     * Ein Schlitz ist eine Engstelle von nahezu null Breite.
                     * Wer sie misst, erkennt ihn - und laesst die beiden
                     * Flaechen dann lieber getrennt. Zwei saubere Flaechen sind
                     * besser als eine mit einem Keil darin.
                     */
                    var hals = MinimumSurfaceFeature(new List<double2[]> { clean });
                    if (hals != null && hals.Distance < SurfaceNeckLimit) continue;
                    union = SurfaceCcw(clean);
                    return true;
                }
            return false;
        }

        private static bool TrySplitSurfaceAtNeck(double2[] ring, SurfaceFeature feature,
                                                   out double2[] first,
                                                   out double2[] second)
        {
            first = null;
            second = null;
            if (!feature.Interior) return false;
            var steps = (feature.Edge - feature.Point + ring.Length) % ring.Length;
            if (steps < 2 || steps > ring.Length - 3) return false;
            var firstList = new List<double2>();
            for (var i = 0; i <= steps; i++)
                firstList.Add(ring[(feature.Point + i) % ring.Length]);
            firstList.Add(feature.Projection);
            var secondList = new List<double2> { ring[feature.Point], feature.Projection };
            for (var i = steps + 1; i < ring.Length; i++)
                secondList.Add(ring[(feature.Point + i) % ring.Length]);
            var a = SurfaceCcw(firstList.ToArray());
            var b = SurfaceCcw(secondList.ToArray());
            if (a.Length < 3 || b.Length < 3 || SelfIntersects(a, 2e-6)
                || SelfIntersects(b, 2e-6)) return false;
            var wanted = Math.Abs(SignedArea(ring));
            var got = Math.Abs(SignedArea(a)) + Math.Abs(SignedArea(b));
            if (Math.Abs(got - wanted) > Math.Max(1e-7, wanted * 1e-10)
                || Math.Abs(SignedArea(a)) < 1e-9 || Math.Abs(SignedArea(b)) < 1e-9)
                return false;
            first = a;
            second = b;
            return true;
        }

        /** Exakt dieselbe -0,1-m-Vorverengung wie CS2s Area-Geometriepfad. */
        private static double2[] SurfaceCs2Inset(double2[] ring)
        {
            var rounded = ring.Select(point =>
                new double2((float)point.x, (float)point.y)).ToArray();
            var ccw = SignedArea(rounded) >= 0;
            var output = new double2[rounded.Length];
            for (var i = 0; i < rounded.Length; i++)
            {
                var current = rounded[i];
                var previous = rounded[(i - 1 + rounded.Length) % rounded.Length];
                var next = rounded[(i + 1) % rounded.Length];
                var incoming = Norm(previous - current);
                var outgoing = Norm(next - current);
                var normal = ccw
                    ? new double2(-incoming.y, incoming.x)
                    : new double2(incoming.y, -incoming.x);
                var dot = Math.Max(-1, Math.Min(1,
                    incoming.x * outgoing.x + incoming.y * outgoing.y));
                var angle = Math.Acos(dot);
                var turn = Math.Sign(normal.x * outgoing.x + normal.y * outgoing.y);
                var halfTangent = Math.Tan(angle * 0.5);
                if (Math.Abs(halfTangent) >= 0.001)
                    normal += incoming * (turn / halfTangent);
                output[i] = current + normal * -0.1;
            }
            return output;
        }

        private static bool SurfaceCs2Compatible(double2[] ring)
        {
            if (ring.Length < 3 || SelfIntersects(ring, 2e-6)) return false;
            var rounded = ring.Select(point =>
                new double2((float)point.x, (float)point.y)).ToArray();
            for (var i = 0; i < rounded.Length; i++)
                if (Len(rounded[(i + 1) % rounded.Length] - rounded[i]) < 0.05)
                    return false;
            var inset = SurfaceCs2Inset(rounded);
            return Math.Abs(SignedArea(inset)) >= 1e-6
                && !SelfIntersects(inset, 2e-6);
        }

        /**
         * Fasst aneinanderliegende Flaechen DESSELBEN Materials zu moeglichst
         * wenigen zusammen.
         *
         * Bis hierher entstand je logischer Teilflaeche eine eigene CS2-Flaeche:
         * jede Kappe, jeder Mittelstreifen, jedes Randbeet und jedes Stueck der
         * Restfuellung einzeln. Im Spiel sieht man dadurch die Nahtlinien
         * zwischen direkt aneinanderliegenden Grasflaechen, und an jeder Naht
         * entstehen spitze Winkel, die es an einer durchgehenden Flaeche gar
         * nicht gaebe. Gemessen an einem Nutzerabzug: 108 Grasflaechen fuer
         * einen einzigen Parkplatz, nach dem Verschmelzen 30.
         *
         * Vereinigt wird nur, was TrySurfaceUnion verlustfrei zusammensetzen
         * kann - gemeinsame Kante, Flaechensumme als harte Nachbedingung, kein
         * Selbstschnitt. Ein Ring um eine Insel scheitert daran von selbst,
         * weil CS2 keine Loecher kann und die Flaechensumme dann nicht aufgeht.
         * Zusaetzlich muss das Ergebnis CS2-tauglich bleiben und darf keine
         * Engstelle unter der Schranke haben.
         */
        /**
         * ECKEN DER NACHBARN AUF DIE EIGENEN KANTEN LEGEN.
         *
         * TrySurfaceUnion verlangt eine EXAKT gemeinsame Kante: a[i] muss
         * b[j+1] sein und a[i+1] gleich b[j]. Teilen sich zwei Flaechen nur
         * einen TEIL einer Kante - eine 4-m-Kante liegt mitten auf einer
         * 10-m-Kante - findet er nichts, und beide bleiben getrennt. Im Spiel
         * sieht man diese Naht als Grasnarbe.
         *
         * Der Nutzer meldete am 2026-08-18 genau das: alle zwoelf Markierungen
         * lagen auf solchen Narben. Nachgerechnet an seiner Form: 23 Grasringe,
         * die nur 13 zusammenhaengende Gruppen bilden - zehn Paare beruehren
         * sich, ohne zu verschmelzen.
         *
         * Hier wird deshalb vorher jede Nachbarecke, die auf einer Kante liegt,
         * als Zwischenpunkt eingefuegt. Das ist verlustfrei: die Punkte liegen
         * auf der Kante, die Flaeche aendert sich nicht - danach passen die
         * Kanten beider Seiten aufeinander.
         */
        private static List<double2[]> TeileKantenAufNachbarn(List<double2[]> rings)
        {
            if (rings.Count < 2) return rings;
            var kasten = rings.Select(r => new double4(
                r.Min(p => p.x), r.Min(p => p.y),
                r.Max(p => p.x), r.Max(p => p.y))).ToList();

            var raus = new List<double2[]>(rings.Count);
            for (var i = 0; i < rings.Count; i++)
            {
                var punkte = new List<double2>(rings[i]);
                for (var j = 0; j < rings.Count; j++)
                {
                    if (i == j) continue;
                    // Nur Nachbarn koennen eine gemeinsame Kante haben.
                    if (kasten[i].x > kasten[j].z + 0.01
                        || kasten[j].x > kasten[i].z + 0.01
                        || kasten[i].y > kasten[j].w + 0.01
                        || kasten[j].y > kasten[i].w + 0.01) continue;

                    foreach (var fremd in rings[j])
                    {
                        if (punkte.Any(q => Len(q - fremd) < 1e-6)) continue;
                        for (var k = 0; k < punkte.Count; k++)
                        {
                            var a = punkte[k];
                            var b = punkte[(k + 1) % punkte.Count];
                            var ab = b - a;
                            var l = Len(ab);
                            if (l < 1e-9) continue;
                            var t = (fremd.x - a.x) * ab.x / l + (fremd.y - a.y) * ab.y / l;
                            if (t <= 1e-6 || t >= l - 1e-6) continue;
                            if (Len(fremd - (a + ab * (t / l))) > 1e-6) continue;
                            punkte.Insert(k + 1, fremd);
                            break;
                        }
                    }
                }
                /**
                 * KEINE DOPPELPUNKTE ZURUECKLASSEN.
                 *
                 * Die Pruefung oben vergleicht den neuen Punkt mit dem Stand
                 * ZU DIESEM ZEITPUNKT. Fuegen mehrere Nachbarn nacheinander
                 * fast denselben Punkt ein, entsteht trotzdem eine Kante der
                 * Laenge null. Der Nutzer meldete am 2026-08-18 genau das:
                 * "'Pavement Surface 01' hat eine Kante von nur 0,000 m".
                 */
                for (var k = punkte.Count - 1; k >= 0 && punkte.Count > 3; k--)
                    if (Len(punkte[k] - punkte[(k + 1) % punkte.Count]) < 1e-6)
                        punkte.RemoveAt(k);
                raus.Add(punkte.ToArray());
            }
            return raus;
        }

        /**
         * EINE FLAECHE AUS DEN ECKPUNKTEN, STATT VIELER STUECKE.
         *
         * Ansage des Nutzers vom 2026-08-17, mit Bild: die schwarz markierte
         * Flaeche zwischen den Strassen soll EINE Grasflaeche sein, gebaut aus
         * den Ecken, die wir kennen, weil wir den Asphalt selbst gesetzt haben -
         * nicht viele Stuecke, die hinterher verschmolzen werden.
         *
         * Bis dahin gab es im ganzen Mod keine einzige Vereinigungs- oder
         * Differenzfunktion. MergeAdjacentSurfaces vereinigt PAARWEISE und
         * verlangt dafuer eine exakt gemeinsame Kante; was daran scheitert,
         * bleibt als eigene Flaeche stehen und ist im Spiel als Naht sichtbar.
         *
         * Hier wird stattdessen der gemeinsame Umriss direkt bestimmt. Die
         * Teilflaechen kacheln das Gebiet, also kommt jede INNERE Kante genau
         * zweimal vor - einmal je Richtung. Streicht man diese Paare, bleiben
         * genau die Kanten des Aussenumrisses uebrig, und die lassen sich zu
         * einem Ring verketten. Voraussetzung ist, dass gemeinsame Kanten
         * dieselben Endpunkte haben; genau dafuer laeuft TeileKantenAufNachbarn
         * vorher.
         *
         * Liefert null, wenn etwas nicht aufgeht - dann bleibt es beim
         * bisherigen Weg. Ein Umriss, der nicht schliesst, waere schlimmer als
         * eine Naht.
         */
        private static string UmrissGrund;

        private static List<double2[]> VereinigeUeberKanten(List<double2[]> rings)
        {
            if (rings.Count < 2) return null;

            static long Schluessel(double2 p)
                => (long)Math.Round(p.x * 1e4) * 100000000L
                 + (long)Math.Round(p.y * 1e4);

            // Kante -> wie oft sie in dieser Richtung vorkommt.
            var kanten = new Dictionary<(long, long), int>();
            var punkte = new Dictionary<long, double2>();
            foreach (var ring in rings)
            {
                var ccw = SurfaceCcw(ring);
                for (var i = 0; i < ccw.Length; i++)
                {
                    var a = ccw[i];
                    var b = ccw[(i + 1) % ccw.Length];
                    if (Len(b - a) < 1e-9) continue;
                    var ka = Schluessel(a);
                    var kb = Schluessel(b);
                    if (ka == kb) continue;
                    punkte[ka] = a;
                    punkte[kb] = b;
                    kanten.TryGetValue((ka, kb), out var n);
                    kanten[(ka, kb)] = n + 1;
                }
            }

            // Gegenlaeufige Paare streichen - das sind die inneren Kanten.
            var offen = new Dictionary<long, List<long>>();
            foreach (var paar in kanten)
            {
                var (a, b) = paar.Key;
                kanten.TryGetValue((b, a), out var rueck);
                var uebrig = paar.Value - rueck;
                if (uebrig <= 0) continue;
                if (!offen.TryGetValue(a, out var liste))
                    offen[a] = liste = new List<long>();
                for (var i = 0; i < uebrig; i++) liste.Add(b);
            }
            if (offen.Count == 0) { UmrissGrund = "keine offenen Kanten"; return null; }

            // Verketten. An jeder Ecke muss genau eine Fortsetzung stehen;
            // gibt es mehrere, ist der Umriss mehrdeutig - dann lieber nicht.
            var ergebnis = new List<double2[]>();
            var schranke = kanten.Count + 8;
            while (offen.Count > 0)
            {
                var start = offen.Keys.First();
                var lauf = new List<double2>();
                var hier = start;
                for (var schritt = 0; schritt < schranke; schritt++)
                {
                    if (!offen.TryGetValue(hier, out var weiter) || weiter.Count == 0)
                    { UmrissGrund = "Sackgasse"; return null; }
                    /**
                     * AN EINER GABELUNG DIE RICHTIGE FORTSETZUNG WAEHLEN.
                     *
                     * Treffen sich mehrere Teilflaechen in EINEM Punkt, gibt es
                     * dort mehr als eine offene Kante. Am Nutzerbau vom
                     * 2026-08-18 waren es drei Grasstuecke (14,6 | 72,6 |
                     * 25,5 m2), die sich in einem einzigen Punkt beruehren.
                     *
                     * Aufgeben waere falsch: der Umriss ist eindeutig, man muss
                     * nur die Kante nehmen, die im selben Gebiet bleibt. Beim
                     * Umlauf gegen den Uhrzeigersinn liegt das Gebiet links,
                     * also ist das die am weitesten RECHTS liegende Fortsetzung
                     * - gemessen am Winkel zur Einlaufrichtung.
                     */
                    var naechster = weiter[0];
                    if (weiter.Count > 1)
                    {
                        var ein = lauf.Count > 0
                            ? punkte[hier] - lauf[lauf.Count - 1]
                            : new double2(1, 0);
                        var bestWinkel = double.PositiveInfinity;
                        foreach (var kandidat in weiter)
                        {
                            var aus = punkte[kandidat] - punkte[hier];
                            // Winkel von der Rueckrichtung aus im Uhrzeigersinn.
                            var w = Math.Atan2(
                                ein.x * aus.y - ein.y * aus.x,
                                -(ein.x * aus.x + ein.y * aus.y));
                            if (w < 0) w += 2 * Math.PI;
                            if (w >= bestWinkel) continue;
                            bestWinkel = w;
                            naechster = kandidat;
                        }
                    }
                    weiter.Remove(naechster);
                    if (weiter.Count == 0) offen.Remove(hier);
                    lauf.Add(punkte[hier]);
                    hier = naechster;
                    if (hier == start) break;
                }
                if (hier != start || lauf.Count < 3) { UmrissGrund = "schliesst nicht"; return null; }
                var ring = MergeCollinear(lauf.ToArray(), 1e-9);
                if (ring.Length < 3 || SelfIntersects(ring, 2e-6)) { UmrissGrund = "gefaltet"; return null; }
                /**
                 * ORIENTIERUNG NICHT ERZWINGEN.
                 *
                 * Die Verkettung liefert Aussenumrisse gegen den Uhrzeigersinn
                 * und LOECHER im Uhrzeigersinn. Zwingt man beide auf CCW, zaehlt
                 * ein Loch positiv statt negativ - gemessen kam so ein Umriss
                 * von 11.904 m2 heraus, wo die Teile 8.650 m2 ergaben.
                 * CS2 kann keine Loecher; taucht eines auf, bleibt es beim
                 * bisherigen Weg.
                 */
                if (SignedArea(ring) < 0) { UmrissGrund = "Loch im Umriss"; return null; }
                ergebnis.Add(ring);
            }

            // Flaechentreue: der Umriss muss genau so gross sein wie die Summe
            // der Teile, sonst wurde etwas verschluckt oder doppelt gezaehlt.
            var soll = rings.Sum(r => Math.Abs(SignedArea(r)));
            var ist = ergebnis.Sum(r => Math.Abs(SignedArea(r)));
            if (Math.Abs(ist - soll) > Math.Max(0.05, soll * 1e-6))
            { UmrissGrund = $"Flaeche {ist:F2} statt {soll:F2}"; return null; }
            return ergebnis;
        }

        private static List<double2[]> MergeAdjacentSurfaces(List<double2[]> rings,
                                                            bool nahtTeilen = false)
        {
            CheckMaterialBudget("MergeAdjacentSurfaces");
            // Die Kantenteilung ist teuer (jeder Ring gegen jeden). Sie zahlt
            // sich nur im LETZTEN Verschmelzen aus, wenn die Raender endgueltig
            // sind - vorher kostet sie nur Zeit und treibt grosse Formen ins
            // Zeitbudget. Gemessen an der Nutzerform: mit ihr in jedem Aufruf
            // Abbruch nach 3 s und 22 Belagringe, nur im letzten Aufruf sauber.
            if (nahtTeilen)
            {
                rings = TeileKantenAufNachbarn(rings);
                // Der direkte Umriss ist das, was der Nutzer verlangt hat:
                // EINE Flaeche aus den Eckpunkten. Gelingt er, ist das
                // paarweise Verschmelzen ueberfluessig.
                var umriss = VereinigeUeberKanten(rings);
                if (PhaseLog)
                    Console.Error.WriteLine("      [Umriss] " + rings.Count
                        + " Teile -> " + (umriss == null ? "gescheitert: " + UmrissGrund : umriss.Count + " Ringe"));
                /**
                 * NICHT AUSSTEIGEN, SONDERN WEITERGEBEN.
                 *
                 * Der Umriss liefert exakt die zusammenhaengenden Gebiete -
                 * Flaechen, die sich nur in EINEM Punkt beruehren, bleiben
                 * dabei getrennt, und das ist richtig. Danach kann das
                 * paarweise Verschmelzen aber immer noch Ringe zusammenlegen,
                 * die eine ganze Kante teilen. Wer hier zurueckspringt,
                 * verschenkt das: am Nutzerbau mit 8 Ecken blieben so 14
                 * Belagringe mit 20 Naehten stehen statt 5.
                 */
                if (umriss != null) rings = umriss;
            }
            var uhrMerge = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
            try
            {
            var output = rings.Select(ring => ring.ToArray()).ToList();
            static double4 Box(double2[] ring)
            {
                var box = new double4(double.PositiveInfinity, double.PositiveInfinity,
                    double.NegativeInfinity, double.NegativeInfinity);
                foreach (var point in ring)
                {
                    box.x = Math.Min(box.x, point.x);
                    box.y = Math.Min(box.y, point.y);
                    box.z = Math.Max(box.z, point.x);
                    box.w = Math.Max(box.w, point.y);
                }
                return box;
            }
            static bool Touches(double4 a, double4 b) =>
                a.x <= b.z + SurfaceJoinEpsilon && b.x <= a.z + SurfaceJoinEpsilon
                && a.y <= b.w + SurfaceJoinEpsilon && b.y <= a.w + SurfaceJoinEpsilon;

            var boxes = output.Select(Box).ToList();
            // Fehlversuche merken: zwei Ringe, die sich nicht vereinigen
            // liessen, koennen es erst wieder, wenn einer sich geaendert hat.
            var failed = new HashSet<(long, long)>();
            var identity = Enumerable.Range(0, output.Count).Select(i => (long)i).ToList();
            var nextIdentity = (long)output.Count;

            /**
             * VOLLE Durchlaeufe statt Neustart nach jedem Treffer. Vorher
             * begann die Suche nach jeder gelungenen Vereinigung wieder bei
             * Paar (0,1). Seit die Parkbuchten mitgepflastert werden, sind es
             * ueber 400 Belagteile, und aus 4,6 wurden 35 Sekunden.
             */
            for (var round = 0; round < 64; round++)
            {
                var merged = false;
                for (var a = 0; a < output.Count; a++)
                    for (var b = a + 1; b < output.Count; )
                    {
                        var key = identity[a] < identity[b]
                            ? (identity[a], identity[b]) : (identity[b], identity[a]);
                        if (!Touches(boxes[a], boxes[b]) || failed.Contains(key))
                        { b++; continue; }
                        if (!TrySurfaceUnion(output[a], output[b], out var union)
                            || !SurfaceCs2Compatible(union))
                        {
                            failed.Add(key);
                            b++;
                            continue;
                        }
                        var neck = MinimumSurfaceFeature(new List<double2[]> { union });
                        if (neck != null && neck.Distance < SurfaceNeckLimit - 1e-9)
                        {
                            failed.Add(key);
                            b++;
                            continue;
                        }
                        output[a] = union;
                        boxes[a] = Box(union);
                        identity[a] = nextIdentity++;
                        output.RemoveAt(b);
                        boxes.RemoveAt(b);
                        identity.RemoveAt(b);
                        merged = true;
                        // b NICHT erhoehen: dort steht jetzt der naechste Ring.
                    }
                if (!merged) break;
            }
            return output;
            }
            finally
            {
                if (uhrMerge != null)
                {
                    uhrMerge.Stop();
                    MaterialZeiten.TryGetValue("MergeAdjacentSurfaces", out var b);
                    MaterialZeiten["MergeAdjacentSurfaces"] = b + uhrMerge.Elapsed.TotalMilliseconds;
                }
            }
        }

        private static SurfaceUnionChoice BestSurfaceUnion(
            List<double2[]> rings, int index)
        {
            SurfaceUnionChoice best = null;
            for (var other = 0; other < rings.Count; other++)
            {
                if (other == index) continue;
                // Einzeln gemessen, weil `RepairMaterialCompatibility` 69 % der
                // gesamten Bauzeit frisst und der Grund in dieser Schleife
                // stecken muss - `SurfaceRingFeatures` hat schon einen Cache.
                var uhrU = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
                var vereint = TrySurfaceUnion(rings[index], rings[other], out var union);
                if (uhrU != null)
                {
                    uhrU.Stop();
                    MaterialZeiten.TryGetValue("  TrySurfaceUnion", out var bU);
                    MaterialZeiten["  TrySurfaceUnion"] = bU + uhrU.Elapsed.TotalMilliseconds;
                }
                if (!vereint) continue;
                var candidate = rings.Where((_, i) => i != index && i != other).ToList();
                candidate.Add(union);
                var uhrM = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
                var feature = MinimumSurfaceFeature(candidate);
                if (uhrM != null)
                {
                    uhrM.Stop();
                    MaterialZeiten.TryGetValue("  MinimumSurfaceFeature", out var bM);
                    MaterialZeiten["  MinimumSurfaceFeature"] = bM + uhrM.Elapsed.TotalMilliseconds;
                }
                var score = (SurfaceCs2Compatible(union) ? 1e6 : 0)
                          + Math.Min(feature?.Distance ?? 1e5, 1e5);
                if (best == null || score > best.Score)
                    best = new SurfaceUnionChoice { Other = other, Union = union, Score = score };
            }
            return best;
        }

        /**
         * Sehnen NUR vom Engstellenpunkt aus, statt aller Paare.
         *
         * `TrySurfaceDiagonalSplit` probiert jedes Punktepaar. Bei kleinen
         * Ringen ist das in Ordnung; seit die Flaechen richtig verschmelzen,
         * hat der Belag aber schon mal knapp 300 Punkte - das waeren
         * zehntausende Sehnen mit je einer Selbstschnittpruefung. Die
         * Engstelle sitzt ohnehin an einem bekannten Punkt, also reicht es,
         * von DORT aus zu suchen: O(n) statt O(n^2), und zielgerichteter,
         * weil jede Sehne die Engstelle wirklich durchtrennt.
         */
        private static bool TrySplitFromNeckPoint(double2[] ring,
                                                  SurfaceFeature feature,
                                                  out double2[] first,
                                                  out double2[] second)
        {
            first = null;
            second = null;
            var n = ring.Length;
            if (n < 4) return false;
            var wanted = Math.Abs(SignedArea(ring));
            var bestScore = double.NegativeInfinity;
            for (var step = 2; step <= n - 2; step++)
            {
                var head = new List<double2>();
                for (var i = 0; i <= step; i++) head.Add(ring[(feature.Point + i) % n]);
                var tail = new List<double2>();
                for (var i = step; i <= n; i++) tail.Add(ring[(feature.Point + i) % n]);
                if (head.Count < 3 || tail.Count < 3) continue;
                var candidateFirst = head.ToArray();
                var candidateSecond = tail.ToArray();
                if (SelfIntersects(candidateFirst, 2e-6)
                    || SelfIntersects(candidateSecond, 2e-6)) continue;
                var got = Math.Abs(SignedArea(candidateFirst))
                        + Math.Abs(SignedArea(candidateSecond));
                if (Math.Abs(got - wanted) > Math.Max(1e-7, wanted * 1e-10)) continue;
                if (!SurfaceCs2Compatible(candidateFirst)
                    || !SurfaceCs2Compatible(candidateSecond)) continue;
                var parts = new List<double2[]>
                    { SurfaceCcw(candidateFirst), SurfaceCcw(candidateSecond) };
                var score = Math.Min(MinimumSurfaceFeature(parts)?.Distance ?? 1e5, 1e5);
                if (score <= bestScore) continue;
                bestScore = score;
                first = parts[0];
                second = parts[1];
            }
            return first != null;
        }

        /**
         * Beide Sehnensuchen mit EINZELNER Verbesserungspruefung.
         *
         * Die Pruefung "wird die Engstelle echt groesser" gehoert an jeden
         * Kandidaten. Stand sie nur am Ende, verhinderte ein mittelmaessiges
         * Ergebnis der gezielten Suche die gruendliche - am Prototyp gemessen
         * fiel die L-Form dadurch von 0,219 auf 0,160 m zurueck.
         */
        private static bool TryChordSplit(double2[] ring, SurfaceFeature feature,
                                          out double2[] first, out double2[] second)
        {
            bool Better(double2[] a, double2[] b)
            {
                var neck = MinimumSurfaceFeature(new List<double2[]> { a, b });
                return neck == null || neck.Distance > feature.Distance + 1e-9;
            }

            if (TrySplitFromNeckPoint(ring, feature, out first, out second)
                && Better(first, second)) return true;
            if (ring.Length <= 64
                && TrySurfaceDiagonalSplit(ring, out first, out second)
                && Better(first, second)) return true;
            first = null;
            second = null;
            return false;
        }

        private static bool TrySurfaceDiagonalSplit(double2[] ring,
                                                     out double2[] first,
                                                     out double2[] second)
        {
            first = null;
            second = null;
            var bestScore = double.NegativeInfinity;
            for (var a = 0; a < ring.Length; a++)
                for (var b = a + 2; b < ring.Length; b++)
                {
                    if (a == 0 && b == ring.Length - 1) continue;
                    var candidateFirst = SurfaceCcw(ring.Skip(a).Take(b - a + 1).ToArray());
                    var candidateSecond = SurfaceCcw(
                        ring.Skip(b).Concat(ring.Take(a + 1)).ToArray());
                    if (candidateFirst.Length < 3 || candidateSecond.Length < 3
                        || SelfIntersects(candidateFirst, 2e-6)
                        || SelfIntersects(candidateSecond, 2e-6)
                        || !SurfaceCs2Compatible(candidateFirst)
                        || !SurfaceCs2Compatible(candidateSecond)) continue;
                    var wanted = Math.Abs(SignedArea(ring));
                    var got = Math.Abs(SignedArea(candidateFirst))
                            + Math.Abs(SignedArea(candidateSecond));
                    if (Math.Abs(got - wanted) > Math.Max(1e-7, wanted * 1e-10)) continue;
                    var parts = new List<double2[]> { candidateFirst, candidateSecond };
                    var feature = MinimumSurfaceFeature(parts);
                    var score = Math.Min(feature?.Distance ?? 1e5, 1e5) * 1e9
                              + Math.Min(Math.Abs(SignedArea(candidateFirst)),
                                         Math.Abs(SignedArea(candidateSecond)));
                    if (score <= bestScore) continue;
                    bestScore = score;
                    first = candidateFirst;
                    second = candidateSecond;
                }
            return first != null;
        }

        private static void ReplaceSurfaceUnion(List<double2[]> rings, int first,
                                                SurfaceUnionChoice choice)
        {
            var high = Math.Max(first, choice.Other);
            var low = Math.Min(first, choice.Other);
            rings.RemoveAt(high);
            rings.RemoveAt(low);
            rings.Add(choice.Union);
        }

        /**
         * Entfernt bei einem Nahpunkt genau den Kandidaten, dessen vollständiges
         * Nachbardreieck bereits in EINER Bucht/Asphaltfläche liegt. Damit wird
         * das Reststück nachweislich an diese Nachbarfläche übergeben; die andere
         * Ecke und alle nicht abgedeckten Kandidaten bleiben unangetastet.
         */
        private static bool TryTransferCoveredSurfaceVertex(
            List<double2[]> grass, IEnumerable<double2[]> covered,
            IEnumerable<double2[]> forbidden,
            SurfaceFeature feature, out double transferredArea)
        {
            transferredArea = 0;
            var ring = grass[feature.Ring];
            var candidates = new[]
            {
                feature.Point,
                feature.Edge,
                (feature.Edge + 1) % ring.Length,
            }.Distinct();
            bool InsideOrBoundary(double2 point, double2[] polygon)
            {
                if (PointIn(point, polygon)) return true;
                for (var i = 0; i < polygon.Length; i++)
                    if (DistToSeg(point, polygon[i],
                        polygon[(i + 1) % polygon.Length]) <= 1e-6) return true;
                return false;
            }

            double2[] best = null;
            var bestMagnitude = double.PositiveInfinity;
            var bestArea = 0.0;
            foreach (var candidateIndex in candidates)
            {
                if (ring.Length <= 3) continue;
                var triangle = new[]
                {
                    ring[(candidateIndex - 1 + ring.Length) % ring.Length],
                    ring[candidateIndex],
                    ring[(candidateIndex + 1) % ring.Length],
                };
                var reduced = ring.Where((_, i) => i != candidateIndex).ToArray();
                if (SelfIntersects(reduced, 2e-6)) continue;
                var area = Math.Abs(SignedArea(ring)) - Math.Abs(SignedArea(reduced));
                var handedToNeighbor = covered.Any(polygon =>
                    triangle.All(point => InsideOrBoundary(point, polygon)));
                // Eine konkave Kerbe darf nur geschlossen werden, wenn ihr
                // Platz WIRKLICH frei ist: weder von Asphalt noch von einem
                // anderen Grasring belegt. Die Gras-Pruefung fehlte - beim
                // 13:04-Abzug schluckte ein Ring dadurch in einem Schritt eine
                // 81-m2-Kerbe, die drei anderen Grasflaechen gehoerte; im
                // Spiel lagen die Flaechen sichtbar uebereinander. Der Hals
                // ist zwar unter 5 cm, die Kerbe an so einem Knoten kann aber
                // beliebig gross sein, weil seine Nachbarkanten lang sind.
                // Ein konvexes Dreieck darf weiterhin nur wegfallen, wenn eine
                // konkrete Nachbarflaeche es vollstaendig uebernimmt.
                var grownIntoOtherGrass = area < 0 && grass.Where(
                        (_, otherIndex) => otherIndex != feature.Ring)
                    .Any(other => QuadsOverlap(triangle, other, 0));
                // Auch nicht in eine BUCHT hinein: geprueft wurde nur gegen
                // Asphalt und anderes Gras, `covered` fehlte.
                var grownIntoBay = area < 0
                    && covered.Any(polygon => QuadsOverlap(triangle, polygon, 0));
                if (!handedToNeighbor && !(area < 0 && !grownIntoOtherGrass
                    && !grownIntoBay
                    && !forbidden.Any(polygon =>
                        QuadsOverlap(triangle, polygon, 0)))) continue;
                if (Math.Abs(area) >= bestMagnitude) continue;
                best = SurfaceCcw(reduced);
                bestMagnitude = Math.Abs(area);
                bestArea = area;
            }
            if (best == null) return false;
            grass[feature.Ring] = best;
            transferredArea = bestArea;
            return true;
        }

        /**
         * Kollabiert eine kurze Randkante auf den analytisch bestimmten Punkt,
         * bei dem die Shoelace-Fläche exakt gleich bleibt. Alle Materialringe,
         * die einen ihrer Endpunkte benutzen, erhalten denselben Punkt.
         */
        private static bool TryCollapseAreaPreservingSurfaceEdge(
            ref List<double2[]> grass, ref List<double2[]> asphalt,
            bool sourceIsGrass, SurfaceFeature feature)
        {
            var sourceRings = sourceIsGrass ? grass : asphalt;
            var source = sourceRings[feature.Ring];
            if (source.Length < 4) return false;
            var edgeStart = source[feature.Edge];
            var edgeEnd = source[(feature.Edge + 1) % source.Length];
            var point = source[feature.Point];
            var endpointIndex = Len(point - edgeStart) <= Len(point - edgeEnd)
                ? feature.Edge : (feature.Edge + 1) % source.Length;
            if (Len(point - source[endpointIndex]) >= SurfaceNeckLimit) return false;

            /**
             * Auch KURZE KETTEN einklappen, nicht nur direkte Nachbarn.
             *
             * Vorher musste der Engstellenpunkt unmittelbar neben dem Endpunkt
             * liegen. An einer Keilspitze stimmt das oft nicht, weil die
             * Grenzsynchronisierung dort Zwischenknoten einschiebt: am
             * Prototyp gemessen lagen die beiden Spitzenecken ZWEI Schritte
             * auseinander bei 0,3516 m Kettenlaenge. Erlaubt ist deshalb eine
             * Kette, solange sie wirklich ein Zipfel ist - hoechstens vier
             * Schritte und kuerzer als das Dreifache der Schranke.
             */
            var ringLength = source.Length;
            var forward = (endpointIndex - feature.Point + ringLength) % ringLength;
            var backward = (feature.Point - endpointIndex + ringLength) % ringLength;
            var chainStart = forward <= backward ? feature.Point : endpointIndex;
            var steps = Math.Min(forward, backward);
            if (steps < 1 || steps > 4 || ringLength - steps < 3) return false;
            var chain = new List<double2>();
            for (var i = 0; i <= steps; i++) chain.Add(source[(chainStart + i) % ringLength]);
            var span = 0.0;
            for (var i = 0; i + 1 < chain.Count; i++) span += Len(chain[i + 1] - chain[i]);
            if (span >= SurfaceNeckLimit * 3) return false;
            var first = chain[0];
            var second = chain[chain.Count - 1];
            var inputGrass = grass;
            var inputAsphalt = asphalt;
            var combinedBefore = grass.Sum(ring => Math.Abs(SignedArea(ring)))
                               + asphalt.Sum(ring => Math.Abs(SignedArea(ring)));
            void BuildCandidateAt(
                double2 canonical,
                out List<double2[]> candidateGrass,
                out List<double2[]> candidateAsphalt,
                out double area,
                out bool invalid)
            {
                var builtGrass = inputGrass.Select(ring => ring.ToArray()).ToList();
                var builtAsphalt = inputAsphalt.Select(ring => ring.ToArray()).ToList();
                var touched = new List<(bool Grass, int Ring)>();
                void ReplaceSharedPoints(List<double2[]> rings, bool grassList)
                {
                    for (var ringIndex = 0; ringIndex < rings.Count; ringIndex++)
                    {
                        var ring = rings[ringIndex];
                        var changedRing = false;
                        for (var i = 0; i < ring.Length; i++)
                            if (chain.Any(link => SurfaceSamePoint(ring[i], link)))
                            {
                                ring[i] = canonical;
                                changedRing = true;
                            }
                        if (changedRing) touched.Add((grassList, ringIndex));
                    }
                }
                ReplaceSharedPoints(builtGrass, true);
                ReplaceSharedPoints(builtAsphalt, false);
                // Die Wahrheitsquelle bereinigt ALLE beruehrten Ringe. Nur den
                // Quellring zu kuerzen liess in den Nachbarn Nullkanten stehen;
                // die Reparatur vereinigte daraufhin 3 Asphaltflaechen zu einer
                // schwach einfachen 0-m-Engstelle (L-Form qk AUS / ig AN).
                foreach (var item in touched.Distinct().ToArray())
                {
                    var rings = item.Grass ? builtGrass : builtAsphalt;
                    var clean = new List<double2>();
                    foreach (var candidatePoint in rings[item.Ring])
                        if (clean.Count == 0
                            || !SurfaceSamePoint(
                                clean[clean.Count - 1], candidatePoint, 1e-9))
                            clean.Add(candidatePoint);
                    while (clean.Count > 2
                           && SurfaceSamePoint(clean[0], clean[clean.Count - 1], 1e-9))
                        clean.RemoveAt(clean.Count - 1);
                    rings[item.Ring] = clean.ToArray();
                }
                if (!touched.Contains((sourceIsGrass, feature.Ring)))
                    touched.Add((sourceIsGrass, feature.Ring));
                invalid = false;
                foreach (var item in touched.Distinct())
                {
                    var ring = (item.Grass ? builtGrass : builtAsphalt)[item.Ring];
                    if (ring.Length >= 3 && !SelfIntersects(ring, 2e-6)) continue;
                    invalid = true;
                    break;
                }
                area = builtGrass.Sum(ring => Math.Abs(SignedArea(ring)))
                     + builtAsphalt.Sum(ring => Math.Abs(SignedArea(ring)));
                candidateGrass = builtGrass;
                candidateAsphalt = builtAsphalt;
            }
            /**
             * Den Ersatzpunkt auch QUER zur kurzen Kante suchen.
             *
             * Entlang allein reicht bei einer Keilspitze nicht: dort aendert
             * ein Verschieben die Gesamtflaeche kaum, der Nenner geht gegen
             * null, und der geloeste Punkt landet weit ausserhalb. Quer dazu
             * aendert sie sich wirklich. Die Flaeche ist im verschobenen Punkt
             * linear (Shoelace), zwei Proben genuegen also fuer die exakte
             * Loesung.
             */
            var chainLength = Len(second - first);
            var middle = (first + second) * 0.5;
            var across = chainLength < 1e-12 ? (double2?)null
                : new double2(-(second.y - first.y) / chainLength,
                              (second.x - first.x) / chainLength);
            var ways = new List<(Func<double, double2> Point, double A, double B)>
            {
                (parameter => first + (second - first) * parameter, 0, 1),
            };
            // Die Probenweite bleibt unter der Schranke, damit der geloeste
            // Punkt gar nicht erst weit weg liegen kann.
            if (across.HasValue)
                ways.Add((parameter => middle + across.Value * parameter, -0.05, 0.05));

            foreach (var way in ways)
            {
                BuildCandidateAt(way.Point(way.A), out _, out _, out var areaAtA, out _);
                BuildCandidateAt(way.Point(way.B), out _, out _, out var areaAtB, out _);
                var denominator = areaAtB - areaAtA;
                // Mathematisch flaechenneutrale Bewegungen ergaben in C# bei
                // 8100 m2 durch die andere Summationsrundung 1,82e-12 statt
                // exakt 0 im Double-Prototyp. Daraus darf kein Ersatzpunkt
                // berechnet werden; vier ULP der Gesamtflaeche trennen
                // Rundungsrauschen von einer wirklichen Flaechensteigung.
                var denominatorEpsilon = Math.Max(
                    1e-12, Math.Abs(combinedBefore) * 1e-15);
                if (Math.Abs(denominator) < denominatorEpsilon) continue;
                var x = way.A + (way.B - way.A)
                    * ((combinedBefore - areaAtA) / denominator);
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                var replacement = way.Point(x);
                BuildCandidateAt(replacement, out var candidateGrass,
                    out var candidateAsphalt, out var combinedAfter, out var invalid);
                // Gegen JEDEN Kettenpunkt pruefen, nicht nur gegen die Enden -
                // sonst duerfte ein Zwischenpunkt beliebig weit wandern.
                if (!chain.All(link => Len(replacement - link) < SurfaceNeckLimit)
                    || invalid
                    || Math.Abs(combinedAfter - combinedBefore) > 1e-6) continue;
                grass = candidateGrass;
                asphalt = candidateAsphalt;
                return true;
            }
            return false;
        }

        /**
         * Rundet Materialknoten einmalig auf die tatsaechliche ECS-Praezision
         * und kanonisiert gemeinsame Asphalt-/Graspunkte AUF BEIDEN SEITEN.
         * Anders als die alte Stabilisierung wird nie nur eine Kontur bewegt.
         */
        private static void SynchronizeMaterialBoundaries(
            ref List<double2[]> grass, ref List<double2[]> asphalt)
        {
            CheckMaterialBudget("SynchronizeMaterialBoundaries");
            double2 Rounded(double2 point) =>
                new double2((float)point.x, (float)point.y);
            grass = grass.Select(ring => ring.Select(Rounded).ToArray()).ToList();
            asphalt = asphalt.Select(ring => ring.Select(Rounded).ToArray()).ToList();
            var combinedAreaBefore = grass.Sum(ring => Math.Abs(SignedArea(ring)))
                                   + asphalt.Sum(ring => Math.Abs(SignedArea(ring)));

            var mappings = new List<(double2 Old, double2 Canonical,
                                     int Asphalt, int Edge, double T)>();
            foreach (var ring in grass)
                foreach (var point in ring)
                {
                    if (mappings.Any(item => SurfaceSamePoint(item.Old, point, 1e-7)))
                        continue;
                    var bestDistance = 0.005;
                    var best = (Found: false, Canonical: default(double2),
                                Asphalt: -1, Edge: -1, T: 0.0);
                    for (var asphaltIndex = 0; asphaltIndex < asphalt.Count; asphaltIndex++)
                    {
                        var road = asphalt[asphaltIndex];
                        for (var edgeIndex = 0; edgeIndex < road.Length; edgeIndex++)
                        {
                            var a = road[edgeIndex];
                            var edge = road[(edgeIndex + 1) % road.Length] - a;
                            var lengthSquared = edge.x * edge.x + edge.y * edge.y;
                            if (lengthSquared < 1e-12) continue;
                            var t = ((point.x - a.x) * edge.x
                                   + (point.y - a.y) * edge.y) / lengthSquared;
                            if (t < -1e-7 || t > 1 + 1e-7) continue;
                            t = Math.Max(0, Math.Min(1, t));
                            var projection = a + edge * t;
                            var distance = Len(point - projection);
                            if (distance > bestDistance) continue;
                            var canonical = Rounded(projection);
                            var canonicalT = t;
                            if (Len(projection - a) < 0.005)
                            {
                                canonical = a;
                                canonicalT = 0;
                            }
                            else if (Len(projection
                                - road[(edgeIndex + 1) % road.Length]) < 0.005)
                            {
                                canonical = road[(edgeIndex + 1) % road.Length];
                                canonicalT = 1;
                            }
                            bestDistance = distance;
                            best = (true, Rounded(canonical),
                                    asphaltIndex, edgeIndex, canonicalT);
                        }
                    }
                    if (best.Found)
                        mappings.Add((point, best.Canonical,
                                      best.Asphalt, best.Edge, best.T));
                }

            for (var ringIndex = 0; ringIndex < grass.Count; ringIndex++)
                for (var pointIndex = 0; pointIndex < grass[ringIndex].Length; pointIndex++)
                {
                    var point = grass[ringIndex][pointIndex];
                    foreach (var mapping in mappings)
                    {
                        if (!SurfaceSamePoint(mapping.Old, point, 1e-7)) continue;
                        grass[ringIndex][pointIndex] = mapping.Canonical;
                        break;
                    }
                }

            var synchronizedAsphalt = new List<double2[]>(asphalt.Count);
            for (var asphaltIndex = 0; asphaltIndex < asphalt.Count; asphaltIndex++)
            {
                var road = asphalt[asphaltIndex];
                var output = new List<double2>();
                for (var edgeIndex = 0; edgeIndex < road.Length; edgeIndex++)
                {
                    output.Add(road[edgeIndex]);
                    foreach (var mapping in mappings
                        .Where(item => item.Asphalt == asphaltIndex
                            && item.Edge == edgeIndex
                            && item.T > 1e-7 && item.T < 1 - 1e-7)
                        .OrderBy(item => item.T))
                    {
                        if (!SurfaceSamePoint(output[output.Count - 1],
                                              mapping.Canonical, 1e-7))
                            output.Add(mapping.Canonical);
                    }
                }
                synchronizedAsphalt.Add(output.ToArray());
            }
            asphalt = synchronizedAsphalt;

            /**
             * Nach dem Runden auf float koennen zwei zuvor getrennte Knoten
             * exakt zusammenfallen: bei Weltkoordinaten um 1350 betraegt die
             * float-Aufloesung rund 0,12 mm. Uebrig bleibt eine Kante der
             * Laenge null und, wenn sich der Nachbarknoten spiegelt, ein Sporn
             * A-B-A ohne Flaeche.
             *
             * Gemessen am Nutzerpolygon vom 2026-08-10 mit float-Ecken: die
             * kleinste Engstelle im Gras war danach 0,000000 m; nach dieser
             * Bereinigung 0,203445 m. CS2 verlangt einen Mindestknotenabstand,
             * und ein Ohrenschneider bleibt an so einem Ring haengen.
             *
             * Verworfen werden nur EXAKT gleiche Knoten - beide Seiten einer
             * gemeinsamen Fuge tragen nach dem Runden dieselben Werte, die
             * Bereinigung trifft sie daher gleich und kann keine Grenze
             * verschieben. Sie steht hier und nicht erst in der Ausgabe, damit
             * der zweite Reparaturdurchlauf die dadurch sichtbar werdenden
             * Haelse noch behandeln kann. Dieselbe Stelle im JS-Modell.
             */
            static double2[] WithoutDegenerateNodes(double2[] ring)
            {
                static bool Same(double2 a, double2 b) => a.x == b.x && a.y == b.y;
                var output = new List<double2>(ring.Length);
                foreach (var point in ring)
                    if (output.Count == 0 || !Same(output[output.Count - 1], point))
                        output.Add(point);
                while (output.Count > 1 && Same(output[0], output[output.Count - 1]))
                    output.RemoveAt(output.Count - 1);
                return SurfaceRemoveZeroSpurs(output.ToArray());
            }
            grass = grass.Select(WithoutDegenerateNodes)
                .Where(ring => ring.Length >= 3).ToList();
            asphalt = asphalt.Select(WithoutDegenerateNodes)
                .Where(ring => ring.Length >= 3).ToList();

            var combinedAreaAfter = grass.Sum(ring => Math.Abs(SignedArea(ring)))
                                  + asphalt.Sum(ring => Math.Abs(SignedArea(ring)));
            /**
             * DIE TOLERANZ MUSS ZUR FLAECHE PASSEN.
             *
             * 0,05 m2 fest war bei 11.080 m2 Gesamtflaeche eine Schranke von
             * 0,0005 % - jede Knotenbereinigung kippt sie. Am Nutzerbau vom
             * 2026-08-18 (PLT-58C43CF0) brach die Kette wegen 0,068 m2 ab, und
             * genau dann bleiben die Flaechen unverschmolzen: der Gruenstreifen
             * zerfaellt in duenne Baender mit nackten Luecken dazwischen. Das
             * ist der Streifen-Fehler, den der Nutzer seit zwei Tagen meldet.
             *
             * Proportional bleibt der Schutz scharf - bei 11.080 m2 sind es
             * 0,11 m2, also ein Zehntel Quadratmeter - faengt aber die
             * Rundungsreste ab, um die es hier geht.
             */
            if (Math.Abs(combinedAreaAfter - combinedAreaBefore)
                > Math.Max(0.05, combinedAreaBefore * 1e-5))
                throw new InvalidOperationException(
                    $"Material synchronisation changed {combinedAreaAfter - combinedAreaBefore:G17} m2.");
        }

        /**
         * Schneidet ALLES Gruen in EINEM Zug aus. Liefert null, wenn dabei ein
         * Loch entstuende (CS2 kann keine Loecher) oder die Konturverfolgung
         * auf ihre Trapeze zurueckfaellt - dann ist der Weg je Teilflaeche
         * besser. Gegenstueck zu surfaceSubtractAsphaltAsRegion im JS-Modell.
         */
        /**
         * NADELN GAR NICHT ERST STEHEN LASSEN.
         *
         * Ansage des Nutzers am 2026-08-18: "Niemals habe ich dir aufgetragen
         * irgendwelche super kleinen Flaechen zu erstellen" - und er hat recht.
         * Die betroffenen Flaechen sind 73,7 und 77,4 m2 gross, sie haben nur
         * IRGENDWO am Rand eine haarfeine Spitze.
         *
         * Eine Sonde durch die Materialkette zeigte, wo sie entsteht:
         *
         *     [Hals] nach Asphaltabzug        4,465E-06 m | 68 Ringe
         *     [Hals] nach fruehem Verschmelzen 5,084E-06 m
         *     [Hals] vor der Endkontrolle      6,232E-06 m
         *
         * Also gleich im ERSTEN Schritt. Die Streifenzerlegung setzt einen
         * Punkt wenige Mikrometer neben eine Kante; die Kontur laeuft dorthin
         * hinaus und unmittelbar zurueck. Der Winkel an so einer Spitze ist
         * nahezu null, und die eingeschlossene Flaeche praktisch auch. CS2 kann
         * die Flaeche dann nicht triangulieren - im Spiel bleibt nackter Boden.
         *
         * Hier fallen solche Punkte weg, sobald sie entstehen. Das kostet
         * messbar keine Flaeche und erspart der ganzen Kette dahinter die
         * Reparaturversuche.
         */
        private static double2[] EntferneNadeln(double2[] ring)
        {
            var punkte = ring.ToList();
            for (var runde = 0; runde < 64 && punkte.Count > 3; runde++)
            {
                var weg = -1;
                for (var i = 0; i < punkte.Count; i++)
                {
                    var a = punkte[(i + punkte.Count - 1) % punkte.Count] - punkte[i];
                    var b = punkte[(i + 1) % punkte.Count] - punkte[i];
                    var la = Len(a);
                    var lb = Len(b);
                    if (la < 1e-9 || lb < 1e-9) { weg = i; break; }
                    var cos = (a.x * b.x + a.y * b.y) / (la * lb);
                    // Ueber 0,999999 entspricht weniger als 0,08 Grad: die
                    // Kontur laeuft praktisch in sich selbst zurueck.
                    if (cos < 0.999999) continue;
                    var probe = new List<double2>(punkte);
                    probe.RemoveAt(i);
                    /**
                     * WIE VIEL EINE NADEL KOSTEN DARF.
                     *
                     * 1e-6 m2 war zu streng: eine Nadel von 30 m Laenge und
                     * 1,6 mm Breite kostet beim Entfernen rund 0,024 m2 - die
                     * Bedingung konnte nie erfuellt werden, und die Aufraeumung
                     * lief ins Leere. Am Nutzerbau vom 2026-08-19 blieben so
                     * Randgruen (783,5 m2) und Eckengruen (138,4 m2) mit
                     * Einschnuerungen von 0,8 und 1,6 mm stehen - CS2 konnte
                     * sie nicht triangulieren, im Spiel fehlte das ganze
                     * Randgruen.
                     *
                     * 0,05 m2 ist ein halber Quadratdezimeter: genug fuer jede
                     * Nadel, zu wenig, um eine echte Ecke zu verlieren.
                     */
                    if (Math.Abs(Math.Abs(SignedArea(probe.ToArray()))
                               - Math.Abs(SignedArea(punkte.ToArray()))) > 0.05) continue;
                    weg = i;
                    break;
                }
                if (weg < 0) break;
                punkte.RemoveAt(weg);
            }
            return punkte.Count >= 3 ? punkte.ToArray() : ring;
        }

        /**
         * DAS MILLIMETER-GITTER.
         *
         * Beschlossen mit dem Nutzer am 2026-08-19, nachdem ein Monat Arbeit
         * dasselbe Muster zeigte: jeder Fix gebar drei neue Fehler. Die Ursache
         * stand in einer einzigen Auszaehlung - ELF verschiedene Toleranzen in
         * der Geometrie, ueber 350 Stellen verteilt:
         *
         *     1e-9  88x   0.05  78x   1e-6  47x   1e-7  34x   2e-6  30x
         *     1e-12 26x   0.01  24x   1e-10 15x   1e-8  11x   0.005 8x
         *
         * Elf verschiedene Antworten auf die Frage "wann sind zwei Punkte
         * gleich?". Liegen zwei Punkte 0,000006 m auseinander, sagt das
         * Verschmelzen "verschieden", die Engstellenmessung "das ist ein Hals",
         * die Reparatur "zu klein zum Teilen". Jede hat fuer sich recht,
         * zusammen ergeben sie den Fehler.
         *
         * Auf dem Gitter gibt es "fast gleich" nicht mehr: zwei Punkte sind
         * exakt derselbe oder mindestens einen Millimeter auseinander.
         * Dazwischen existiert nichts. Die ganze Fehlerklasse verschwindet
         * KONSTRUKTIV statt durch eine weitere Pruefung:
         *
         *     Nadel 0,000006 m   -> Punkt liegt exakt auf der Kante
         *     Naht  0,000019 m   -> beide Punkte identisch, verschmilzt
         *     Loch  0,000198 m   -> gibt es nicht mehr
         *
         * Ein Millimeter ist sicher: CS2 rechnet in float, das hat bei
         * Koordinaten um -1300 eine Aufloesung von etwa 0,15 mm. Jeder
         * Gitterpunkt ist dort exakt darstellbar - und fuer einen Parkplatz
         * ist ein Millimeter unsichtbar.
         *
         * Was das Gitter NICHT loest: echte duenne Stellen im Zentimeterbereich.
         * Die sind hundertfach groesser als das Raster und bleiben.
         */
        internal const double Gitter = 0.001;

        internal static double2 AufGitter(double2 punkt) => new double2(
            Math.Round(punkt.x / Gitter) * Gitter,
            Math.Round(punkt.y / Gitter) * Gitter);

        /**
         * Einen Ring einrasten. Punkte, die dabei zusammenfallen, werden zu
         * einem - genau das ist der Zweck. Bleiben weniger als drei uebrig,
         * war es keine Flaeche.
         */
        internal static double2[] AufGitter(double2[] ring)
        {
            if (ring == null || ring.Length < 3) return ring;
            var raus = new List<double2>(ring.Length);
            foreach (var punkt in ring)
            {
                var p = AufGitter(punkt);
                if (raus.Count != 0 && Len(raus[raus.Count - 1] - p) < Gitter / 2) continue;
                raus.Add(p);
            }
            while (raus.Count > 1
                   && Len(raus[0] - raus[raus.Count - 1]) < Gitter / 2)
                raus.RemoveAt(raus.Count - 1);
            return raus.Count >= 3 ? raus.ToArray() : null;
        }

        internal static List<double2[]> AufGitter(List<double2[]> ringe)
            => ringe.Select(AufGitter).Where(r => r != null).ToList();

        private static List<double2[]> SubtractAsphaltAsRegion(
            List<double2[]> grass, List<double2[]> asphalt)
        {
            if (grass.Count == 0) return new List<double2[]>();
            var others = grass.Skip(1).Concat(asphalt).ToList();
            var parts = SlabFill(grass[0], others,
                point => !grass.Any(ring => PointIn(point, ring))
                      || asphalt.Any(road => PointIn(point, road)), 0);
            if (PhaseLog)
                Console.Error.WriteLine("      [Kontur] Quellen " + grass.Count
                    + ", Rueckfall " + parts.Fallback
                    + ", Loecher " + parts.Loecher.Count
                    + ", Aussenringe " + parts.Aussen.Count
                    + (parts.Loecher.Count > 0
                        ? ", groesstes Loch "
                          + parts.Loecher.Max(l => Math.Abs(SignedArea(l))).ToString("F2") + " m2"
                        : ""));
            if (parts.Fallback || parts.Loecher.Count != 0) return null;
            return parts.Aussen.Select(SurfaceCcw).ToList();
        }

        /** Der bisherige Weg: jede logische Teilflaeche einzeln zerschneiden. */
        private static List<double2[]> SubtractAsphaltPerPiece(
            List<double2[]> grass, List<double2[]> asphalt)
        {
            var output = new List<double2[]>();
            if (PhaseLog)
                Console.Error.WriteLine("      [PerPiece] " + grass.Count
                    + " Quellteile (" + grass.Sum(r => Math.Abs(SignedArea(r))).ToString("F0")
                    + " m2), " + asphalt.Count + " Schnittflaechen");
            foreach (var original in grass)
            {
                bool BoundsOverlap(double2[] road) =>
                    original.Max(p => p.x) >= road.Min(p => p.x) - 1e-9
                    && road.Max(p => p.x) >= original.Min(p => p.x) - 1e-9
                    && original.Max(p => p.y) >= road.Min(p => p.y) - 1e-9
                    && road.Max(p => p.y) >= original.Min(p => p.y) - 1e-9;
                var cuts = asphalt.Where(BoundsOverlap).ToList();
                if (cuts.Count == 0) { output.Add(original); continue; }
                var parts = SlabFill(original, cuts,
                    point => !PointIn(point, original)
                          || cuts.Any(road => PointIn(point, road)), 0);
                if (parts.Loecher.Count != 0)
                    throw new InvalidOperationException(
                        "Pavement cut produced a grass hole that cannot be represented.");
                output.AddRange(parts.Aussen.Select(SurfaceCcw));
            }
            return output;
        }

        /**
         * HAARRISSKANTEN AUS DEN FERTIGEN RINGEN ENTFERNEN.
         *
         * CS2 verwirft eine Flaeche, sobald EINE ihrer Kanten unter 0,375 m
         * liegt - und zwar die GANZE Flaeche, nicht nur die Kante. Der Nutzer
         * hat das am 2026-08-17 richtig beschrieben: es fehlen keine Kruemel,
         * sondern grosse Stuecke, ganze Strassen samt Buchten.
         *
         * Am Schwarm gewogen statt gezaehlt, und die Zahl ist deutlich:
         *
         *     verworfene Flaeche: im Mittel 43,0 % der Materialflaeche,
         *     schlimmstenfalls 92,6 %; groesster einzelner Ring 19.733 m2
         *
         * Meine vorherige Messung zaehlte nur RINGE und verdeckte das komplett
         * - ein 19.733-m2-Ring und ein 0,02-m2-Span zaehlten gleich.
         *
         * Hier wird deshalb nicht mehr versucht, die Entstehung der kurzen
         * Kante zu verhindern (zwei Versuche dazu sind gescheitert, siehe
         * `LongestEdgeAngle` und `SlabCutTol`). Stattdessen wird sie am ENDE
         * beseitigt: zwei Punkte, die naeher als die Mindestkante beieinander
         * liegen, werden zu einem verschmolzen. Der Umriss aendert sich dabei
         * um hoechstens diese Distanz - gegen den Totalverlust der Flaeche ist
         * das ein guter Tausch.
         */
        internal static int VerworfeneSplitter;
        internal static double VerworfeneSplitterFlaeche;
        internal static int BehalteneRinge;
        internal static double BehalteneRingFlaeche;

        internal static void MeldeSplitterBilanz()
        {
            var gesamt = VerworfeneSplitterFlaeche + BehalteneRingFlaeche;
            Console.WriteLine($"SPLITTER-BILANZ: {BehalteneRinge} Ringe behalten "
                + $"({BehalteneRingFlaeche:F0} m2), {VerworfeneSplitter} Splitter "
                + $"verworfen ({VerworfeneSplitterFlaeche:F1} m2 = "
                + $"{100 * VerworfeneSplitterFlaeche / Math.Max(gesamt, 1):F2} % "
                + "der Materialflaeche)");
        }

        /**
         * LETZTE INSTANZ: WAS CS2 NICHT BAUEN KANN, GEHT NICHT HINAUS.
         *
         * Der Nutzer meldete am 2026-08-18 wiederholt fehlendes Gras und
         * Narben daneben. Sein Bericht nannte den Grund selbst - "3 Flaeche(n)
         * ohne Dreiecke (unsichtbar)" - und der Abzug des Spiels die Ursache:
         *
         *     Gras: Engstelle 0,000015 m
         *     Gras: Engstelle 0,000271 m
         *     Gras: Engstelle 0,000006 m
         *
         * Flaechen, die irgendwo auf Mikrometer zusammengekniffen sind. CS2
         * legt sie an, kann sie aber nicht triangulieren: im Spiel bleibt dort
         * nackter Boden, und die Nachbarn sehen aus wie Narben.
         *
         * Die Reparaturlaeufe davor versuchen, so eine Stelle an eine
         * Nachbarflaeche zu uebergeben. Gelingt das nicht, gaben sie bisher auf
         * und liessen die Flaeche unveraendert hinausgehen.
         *
         * Hier wird zuletzt hart entschieden: an der Engstelle TRENNEN, sodass
         * zwei saubere Flaechen entstehen. Geht auch das nicht, faellt die
         * Flaeche weg - unsichtbar ist sie ohnehin, aber als Rest verstellt sie
         * jede weitere Messung.
         */
        internal static int EndkontrolleGeteilt;
        internal static int EndkontrolleVerworfen;
        internal static double EndkontrolleFlaeche;

        private static List<double2[]> NurWasCs2BauenKann(List<double2[]> rings)
        {
            var offen = new List<double2[]>(rings);
            var fertig = new List<double2[]>();
            var runden = 0;
            while (offen.Count != 0 && runden++ < rings.Count * 4 + 64)
            {
                var ring = offen[offen.Count - 1];
                offen.RemoveAt(offen.Count - 1);
                if (ring.Length < 3) continue;
                var hals = MinimumSurfaceFeature(new List<double2[]> { ring });
                if (hals == null || hals.Distance >= SurfaceNeckLimit)
                {
                    fertig.Add(ring);
                    continue;
                }
                /**
                 * EINE NADELSPITZE BRAUCHT KEINEN SCHNITT, NUR EINEN PUNKT
                 * WENIGER.
                 *
                 * Gemessen am Nutzerbau vom 2026-08-18: die Engstellen lagen
                 * zwischen einem PUNKT und einer fast beruehrenden Kante
                 * (6E-05 m). Das ist kein Hantelhals, den man teilen muesste,
                 * sondern eine Nadel, die aus dem Umriss heraussticht. Wird der
                 * Punkt entfernt, verschwindet sie - und die Flaeche aendert
                 * sich um praktisch nichts.
                 *
                 * TryChordSplit lehnt so einen Fall ab, weil er die Flaeche
                 * nicht erhaelt; deshalb gingen zwei Flaechen von 73,7 und
                 * 77,4 m2 verloren, obwohl nur ein Punkt zu viel war.
                 */
                /**
                 * ERST DIE NADELN WEG, DANN URTEILEN.
                 *
                 * Die Einschnuerung ist meist nur ein Punkt zu viel. Wird er
                 * entfernt, ist die Flaeche baubar - ohne Teilung und ohne
                 * Verlust. Ohne diesen Schritt blieben am Nutzerbau vom
                 * 2026-08-19 zwei grosse Gruenflaechen (783,5 und 138,4 m2)
                 * als unbaubar stehen und fehlten im Spiel komplett.
                 */
                var entnadelt = EntferneNadeln(ring);
                if (entnadelt.Length >= 3 && entnadelt.Length < ring.Length)
                {
                    var geprueft = MinimumSurfaceFeature(
                        new List<double2[]> { entnadelt });
                    if (geprueft == null || geprueft.Distance >= SurfaceNeckLimit)
                    {
                        fertig.Add(entnadelt);
                        EndkontrolleGeteilt++;
                        continue;
                    }
                    offen.Add(entnadelt);
                    continue;
                }
                if (ring.Length > 3)
                {
                    var ohneSpitze = new List<double2>(ring);
                    ohneSpitze.RemoveAt(hals.Point % ring.Length);
                    var probe = ohneSpitze.ToArray();
                    var verlust = Math.Abs(Math.Abs(SignedArea(ring))
                                         - Math.Abs(SignedArea(probe)));
                    var neuerHals = MinimumSurfaceFeature(
                        new List<double2[]> { probe });
                    if (verlust <= 0.05
                        && !SelfIntersects(probe, 2e-6)
                        && (neuerHals == null
                            || neuerHals.Distance >= SurfaceNeckLimit))
                    {
                        fertig.Add(probe);
                        EndkontrolleGeteilt++;
                        continue;
                    }
                }
                if (TryChordSplit(ring, hals, out var a, out var b))
                {
                    offen.Add(a);
                    offen.Add(b);
                    EndkontrolleGeteilt++;
                    continue;
                }
                if (PhaseLog)
                    Console.Error.WriteLine("      [Endkontrolle] verworfen: "
                        + Math.Abs(SignedArea(ring)).ToString("F1") + " m2, "
                        + ring.Length + " Punkte, Engstelle "
                        + hals.Distance.ToString("G3") + " m, Punkt " + hals.Point
                        + ", Kante " + hals.Edge + ", innen " + hals.Interior);
                /**
                 * NICHT WEGWERFEN - BEHALTEN.
                 *
                 * Der erste Anlauf verwarf, was sich nicht teilen liess. Ueber
                 * 172 Formen kostete das im Median 21,6 % ungedeckte Flaeche:
                 * ein Fuenftel des Parkplatzes ohne Belag. Eine Flaeche, die
                 * CS2 vielleicht nicht darstellt, ist immer noch besser als
                 * ein garantiertes Loch - und sie kann im Spiel durchaus
                 * ankommen, unsere Engstellenmessung ist strenger als CS2.
                 */
                fertig.Add(ring);
                EndkontrolleVerworfen++;
                EndkontrolleFlaeche += Math.Abs(SignedArea(ring));
            }
            fertig.AddRange(offen);
            return fertig;
        }

        private static List<double2[]> DropShortEdges(List<double2[]> rings)
        {
            var ergebnis = new List<double2[]>(rings.Count);
            foreach (var ring in rings)
            {
                var punkte = ring.ToList();
                // Wiederholt, weil das Verschmelzen zweier Punkte eine neue zu
                // kurze Kante erzeugen kann. Die Grenze schuetzt gegen
                // Endlosschleifen bei entarteten Eingaben.
                /**
                 * KEINE ZUSAMMENZIEHUNG, DIE DEN RING FALTET.
                 *
                 * Diese Schleife entfernt Punkte, deren Kante kuerzer als
                 * 0,375 m ist. Bei einem verwinkelten Umriss kann genau das
                 * zwei Raender aufeinanderlegen - der Ring ueberschneidet sich
                 * dann selbst und hat eine Engstelle von 0,000 m. CS2 baut so
                 * etwas nicht; im Spiel fehlt die Flaeche.
                 *
                 * Gemessen ueber 64 Formen, waehrend ich stueckweise mehr
                 * zusammenfasste: selbstueberschneidende Flaechen 1 -> 5 -> 9,
                 * Engstellen von 0,000 m 10 -> 18 -> 28. Die Zusammenfassung
                 * war richtig, ihre Nachbearbeitung hier war ungeprueft.
                 *
                 * Deshalb wird jede Zusammenziehung vorher durchgerechnet.
                 * Faltet sie den Ring, bleibt diese Kante stehen und die
                 * naechstkuerzere kommt dran. Geht keine mehr, hoert es auf -
                 * eine kurze Kante ist ein kleinerer Schaden als ein
                 * gefalteter Ring.
                 */
                for (var runde = 0; runde < 64 && punkte.Count > 3; runde++)
                {
                    var kandidaten = new List<(double Laenge, int Stelle)>();
                    for (var i = 0; i < punkte.Count; i++)
                    {
                        var laenge = Len(punkte[(i + 1) % punkte.Count] - punkte[i]);
                        if (laenge < SurfaceMinEdge) kandidaten.Add((laenge, i));
                    }
                    if (kandidaten.Count == 0) break;
                    kandidaten.Sort((x, y) => x.Laenge.CompareTo(y.Laenge));
                    var gezogen = false;
                    foreach (var (_, stelle) in kandidaten)
                    {
                        // Zuerst den spaeteren Punkt entfernen, den frueheren
                        // behalten - so wandert der Umriss nicht schleichend in
                        // eine Richtung. Faltet das den Ring, ist der fruehere
                        // Punkt der zweite Versuch: eine kurze Kante laesst sich
                        // von BEIDEN Enden schliessen, und oft geht nur eines.
                        foreach (var welcher in new[] { (stelle + 1) % punkte.Count,
                                                        stelle })
                        {
                            var versuch = new List<double2>(punkte);
                            versuch.RemoveAt(welcher);
                            if (versuch.Count < 3) continue;
                            var probe = versuch.ToArray();
                            if (SelfIntersects(probe, 2e-6)) continue;
                            punkte = versuch;
                            gezogen = true;
                            break;
                        }
                        if (gezogen) break;
                    }
                    if (!gezogen) break;
                }
                if (punkte.Count < 3) continue;
                /**
                 * WAS SICH NICHT REPARIEREN LAESST, GAR NICHT ERST AUSGEBEN.
                 *
                 * Die Schleife oben verschmilzt Punkte - ein DREIECK kann aber
                 * keinen mehr verlieren, ohne zur Linie zu werden. Genau die
                 * blieben uebrig: am 2026-08-17 ueber 60 Formen gemessen 13.352
                 * Restringe, ausnahmslos Gras, ausnahmslos Dreiecke, Flaeche im
                 * Median 0,109 m2 und hoechstens 5,7 m2, kuerzeste Kante im
                 * Median 0,102 m.
                 *
                 * CS2 verwirft sie ohnehin - sie liegen unter seiner
                 * Mindestkante. Sie auszugeben aendert am Bild also nichts,
                 * kostet aber Rechenzeit in jedem Reparaturlauf und verstellt
                 * jede Messung: ein 0,1-m2-Span zaehlte bisher genauso wie ein
                 * echter Fehler.
                 *
                 * Der Preis ist die Deckung: diese Schnipsel summieren sich auf
                 * rund 0,3 % der Materialflaeche - dieselben 0,3 %, die das
                 * Spiel heute schon wegwirft. Sichtbar aendert sich nichts.
                 */
                /**
                 * ABER NUR SPLITTER - NIEMALS EINE GROSSE FLAECHE.
                 *
                 * Die Regel darunter war fuer Dreiecke im Median 0,109 m2
                 * gedacht. Seit die Zusammenziehung oben abbricht, wenn sie den
                 * Ring falten wuerde, koennen auch GROSSE Flaechen mit einer
                 * kurzen Kante herauskommen - und die Regel warf sie mit weg. Am
                 * Nutzerbau vom 2026-08-18 kostete das den kompletten Belag:
                 * aus 16.956 m2 in 2 Ringen wurden 3 m2 in einem.
                 *
                 * Eine kurze Kante ist ein kleiner Schaden, den CS2 vielleicht
                 * noch nimmt. Eine fehlende Parkplatzflaeche ist keiner.
                 */
                var fertig = punkte.ToArray();
                if (KuerzesteKante(fertig) < SurfaceMinEdge
                    && Math.Abs(SignedArea(fertig)) <= SurfaceSplitterMaxArea)
                {
                    // Buch fuehren, damit niemand raten muss, wie viel hier
                    // verlorengeht. Der Nutzer fragte zu Recht nach, ob damit
                    // die RESTFUELLUNG betroffen ist - die Antwort muss eine
                    // Zahl sein, keine Beteuerung.
                    VerworfeneSplitter++;
                    VerworfeneSplitterFlaeche += Math.Abs(SignedArea(fertig));
                    continue;
                }
                BehalteneRinge++;
                BehalteneRingFlaeche += Math.Abs(SignedArea(fertig));
                ergebnis.Add(fertig);
            }
            return ergebnis;
        }

        /** CS2 verwirft Flaechen mit kuerzeren Kanten - am Dekompilat belegt. */
        internal const double SurfaceMinEdge = 0.375;
        /**
         * Bis hierher ist ein Ring mit kurzer Kante ein Splitter.
         *
         * Gemessen ueber 60 Formen am 2026-08-17: die verworfenen
         * Reste waren ausnahmslos Dreiecke, Flaeche im Median 0,109 m2
         * und hoechstens 5,7 m2. 10 m2 laesst Luft und liegt weit unter
         * allem, was als Flaeche gemeint ist.
         */
        internal const double SurfaceSplitterMaxArea = 10;

        private static double KuerzesteKante(double2[] ring)
        {
            var min = double.MaxValue;
            for (var i = 0; i < ring.Length; i++)
                min = Math.Min(min, Len(ring[(i + 1) % ring.Length] - ring[i]));
            return min;
        }

        /**
         * WO GEHT DIE ZEIT HIN, innerhalb der Materialphase?
         *
         * Am 2026-08-17 ueber 60 Formen gemessen: `BuildMaterialSurfaces`
         * frisst 96,2 % der gesamten Bauzeit (206,6 s von 214,8 s), das Layout
         * selbst nur 3,8 %. Diese Uhren sagen, welcher Schritt darin schuld
         * ist - vorher war das Raten.
         */
        private static readonly System.Collections.Generic.Dictionary<string, double>
            MaterialZeiten = new System.Collections.Generic.Dictionary<string, double>();

        private static void MissZeit(string name, Action tun)
        {
            if (!PhaseLog) { tun(); return; }
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            // try/finally ist hier PFLICHT, nicht Stil: das Zeitbudget wirft
            // bei genau den langsamen Formen eine Ausnahme. Ohne finally waeren
            // ausgerechnet die teuersten Laeufe unsichtbar - gemessen fehlten
            // dadurch 169 von 211 Sekunden.
            try { tun(); }
            finally
            {
                uhr.Stop();
                MaterialZeiten.TryGetValue(name, out var bisher);
                MaterialZeiten[name] = bisher + uhr.Elapsed.TotalMilliseconds;
            }
        }

        internal static void MeldeMaterialZeiten()
        {
            if (!PhaseLog || MaterialZeiten.Count == 0) return;
            var gesamt = 0.0;
            foreach (var w in MaterialZeiten.Values) gesamt += w;
            Console.Error.WriteLine("      [Material] Summe "
                + $"{gesamt / 1000:F1} s");
            foreach (var e in MaterialZeiten)
                Console.Error.WriteLine($"      [Material] {e.Key,-28} "
                    + $"{e.Value / 1000,7:F1} s  {100 * e.Value / Math.Max(gesamt, 1),5:F1} %");
            Console.Error.Flush();
        }

        private static void BuildMaterialSurfaces(
            WorkLayout output, LayoutSettings settings)
        {
            var uhrGruppe = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
            var material = GroupMaterialSurfaces(output, settings);
            if (uhrGruppe != null)
            {
                uhrGruppe.Stop();
                MaterialZeiten.TryGetValue("GroupMaterialSurfaces", out var bG);
                MaterialZeiten["GroupMaterialSurfaces"] = bG + uhrGruppe.Elapsed.TotalMilliseconds;
            }
            // ZERSPLITTERUNG MESSEN. Am 2026-08-17 blieb `Build` bei einem
            // Fuenfeck (16.430 m2) hier haengen. Verdacht: keine Endlosschleife,
            // sondern eine Explosion - derselbe Umriss ergab im Prototyp auf
            // dem Rueckfallweg 2281 Grasschnipsel, auf dem strikten nur 19.
            // Eine superlineare Reparatur ueber tausende Teile rechnet ewig.
            void Teile(string wo, int n)
            {
                if (!PhaseLog) return;
                Console.Error.WriteLine($"      [Teile] {wo,-26} {n,6}");
                Console.Error.Flush();
            }
            Teile("Gras roh", material.Grass.Count);
            Teile("Asphalt roh", material.Asphalt.Count);
            Teile("Buchten", output.Bay.Count);
            /**
             * WO ENTSTEHT DIE FALSCHE VERBINDUNG?
             *
             * Ansage des Nutzers am 2026-08-18: eine Flaeche darf sich NIE
             * selbst ueberschneiden, und wenn eine Form eine echte Engstelle
             * hat, sollen dort zwei Flaechen entstehen. Bisher wird das nur
             * nachtraeglich geprueft und im Zweifel verworfen. Um es an der
             * Quelle zu beheben, muss zuerst feststehen, welcher Schritt zwei
             * Punkte verbindet, die nicht verbunden gehoeren.
             */
            void Heil(string wo, List<double2[]> gras, List<double2[]> belag)
            {
                if (!PhaseLog) return;
                var kaputt = 0;
                var doppelt = 0;
                foreach (var ring in gras.Concat(belag))
                {
                    if (SelfIntersects(ring, 2e-6)) kaputt++;
                    for (var i = 0; i < ring.Length; i++)
                        for (var j = i + 2; j < ring.Length; j++)
                        {
                            if (i == 0 && j == ring.Length - 1) continue;
                            if (Len(ring[i] - ring[j]) >= 1e-6) continue;
                            doppelt++;
                            i = ring.Length;
                            break;
                        }
                }
                Console.Error.WriteLine($"      [Heil] {wo,-32} "
                    + $"{gras.Count + belag.Count,5} Ringe | {kaputt,3} gefaltet "
                    + $"| {doppelt,3} mit Doppelpunkt");
                Console.Error.Flush();
            }

            var originalGrass = material.Grass.Select(SurfaceCcw).ToList();
            var carriageway = output.PerimeterQuad.Concat(output.AisleQuad)
                .Concat(output.CrossQuad).Concat(output.EntranceQuad)
                .Select(SurfaceCcw).ToList();
            /**
             * Parkbuchten sind Belag, keine Wiese. Bis hierher bekamen sie gar
             * kein Material; auf grasbewachsenem Gelaende sah der ganze
             * Parkplatz dadurch gruen aus, und der Nutzer hielt die aeussere
             * Buchtenreihe fuer nicht vorhanden.
             *
             * Eine Bucht, die auf der Fahrbahn liegt, wird dort beschnitten -
             * gemessen ragen bei Referenz 08s zwei Buchten in eine Fahrgasse,
             * eine mit 29 % ihrer Flaeche. Ohne den Beschnitt laegen zwei
             * Belagflaechen uebereinander, die sich nie vereinigen lassen.
             */
            var bayPaving = new List<double2[]>();
            // Verdacht: diese Schleife ist O(Buchten x Fahrbahnvierecke) - bei
            // 600 Buchten und 50 Vierecken sind das 30.000 Ueberlappungstests,
            // und jeder ist ein Polygontest.
            var uhrBucht = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
            foreach (var raw in output.Bay)
            {
                var bay = SurfaceCcw(raw);
                var roadCuts = carriageway.Where(z => QuadsOverlap(bay, z, 0.01)).ToList();
                if (roadCuts.Count == 0) { bayPaving.Add(bay); continue; }
                var pieces = SlabFill(bay, roadCuts,
                    p => !PointIn(p, bay) || roadCuts.Any(z => PointIn(p, z)), 0.01);
                bayPaving.AddRange(pieces.Aussen);
            }
            /**
             * SOFORT verschmelzen, nicht erst am Ende. Als EIN Gebiet geht es
             * nicht: der Umriss hat gemessen immer 7 bis 11 Loecher - die
             * Grueninseln -, und CS2 kann keine Loecher. Der Unterschied zum
             * Verschmelzen am Schluss ist, dass die Reparaturlaeufe danach auf
             * wenigen grossen Ringen arbeiten statt auf ueber 200
             * Einzelteilen: Schraeg 31 -> 2 Belagflaechen, Referenz 33 -> 5,
             * und eine 0,04-m2-Ueberdeckung von Gras auf Belag verschwand.
             */
            if (uhrBucht != null)
            {
                uhrBucht.Stop();
                MaterialZeiten.TryGetValue("Buchtenbelag", out var bB);
                MaterialZeiten["Buchtenbelag"] = bB + uhrBucht.Elapsed.TotalMilliseconds;
            }
            Teile("Buchtenbelag zerlegt", bayPaving.Count);
            var basisAsphalt = MergeAdjacentSurfaces(
                carriageway.Concat(bayPaving).ToList(), nahtTeilen: true);
            Teile("Asphalt verschmolzen", basisAsphalt.Count);
            // Umgewidmete Innenteile koennen einander oder vorhandenen Belag
            // ueberdecken. Nur ihr noch nicht vertretener Anteil kommt hinzu.
            var additionalPaving = material.Asphalt.Select(SurfaceCcw).ToList();
            /**
             * AUCH DIE VORBEREITUNG BRAUCHT EIN NETZ.
             *
             * Diese Zeilen standen VOR dem `try` darunter - reisst die
             * Materialuhr hier, fliegt die Ausnahme ungefangen bis nach oben,
             * und es gibt gar keine Flaechen statt unschoener. Im Spiel heisst
             * das: keine Vorschau.
             *
             * Aufgefallen am 2026-08-20 an der schraegen L-Form des Nutzers
             * (`-65.7,-31;120,-31;120,45;76.2,52.4;60,90;-65.7,90`). Sie
             * braucht laenger als das 7-s-Budget - das ist bei ihr NICHT neu,
             * der Abbruch wurde bisher nur eine Stelle spaeter gefangen und
             * lieferte dort 405 Buchten mit unreparierten Flaechen. Sobald er
             * eine Stelle frueher zuschlug, war das ganze Ergebnis weg.
             *
             * Hier faellt es deshalb auf `basisAsphalt` zurueck: der
             * zusaetzliche Belag wird nicht eingerechnet, aber der Bau laeuft
             * weiter und der Rueckfall darunter greift wie vorgesehen.
             */
            List<double2[]> originalAsphalt;
            try
            {
                var newPaving = additionalPaving.Count == 0
                    ? new List<double2[]>()
                    : SubtractSurfaceRegion(additionalPaving, basisAsphalt);
                originalAsphalt = newPaving.Count == 0
                    ? basisAsphalt
                    : MergeAdjacentSurfaces(basisAsphalt.Concat(newPaving).ToList());
                // Siehe AbsorbThinAsphaltRings: die Reste der Restfuellung sollen
                // in der grossen Belagflaeche aufgehen, nicht daneben liegen.
                originalAsphalt = AbsorbThinAsphaltRings(originalAsphalt);
            }
            catch (Exception vorbereitung)
            {
                originalAsphalt = basisAsphalt;
                if (PhaseLog)
                    Console.Error.WriteLine("      [Abbruch] Belagvorbereitung: "
                        + vorbereitung.Message);
                var hinweis = "Belagvorbereitung abgebrochen: "
                    + vorbereitung.Message + "; nur der Grundbelag geht hinaus.";
                if (!output.Warnings.Contains(hinweis)) output.Warnings.Add(hinweis);
                System.Diagnostics.Trace.TraceWarning(hinweis);
            }
            var fallbackGrass = originalGrass;
            var fallbackAsphalt = originalAsphalt;
            try
            {
                var grass = originalGrass;
                var asphalt = originalAsphalt;

                /**
                 * Erst versuchen, ALLES Gruen in EINEM Zug auszuschneiden.
                 *
                 * Der Weg darunter zerlegt Kappe, Mittelstreifen, Randbeet und
                 * Restfuellung getrennt. Jede bekommt dabei ihre EIGENE Fassung
                 * der gemeinsamen Grenze, gemessen bis 0,2 mm auseinander.
                 * Danach war das Zusammenfassen ein Kleben mit Toleranzen: die
                 * Vereinigung verlangt 1 Mikrometer und fand keine gemeinsame
                 * Kante - im Spiel blieben sichtbar aneinanderliegende Flaechen
                 * getrennt.
                 *
                 * Hier gibt es diese Grenze gar nicht: SlabFill nimmt alle
                 * Gruenteile als Kantenquelle und entscheidet per Praedikat.
                 * Die Konturverfolgung liefert direkt die zusammenhaengenden
                 * Umrisse, es gibt nie zwei Fassungen derselben Grenze.
                 */
                /**
                 * Buchten sind kein Material, aber Gras darf nicht auf ihnen
                 * liegen. An einer konkaven Ecke reicht die Gehrung des
                 * Randbeets tiefer als `es`, waehrend Buchten nur `es`
                 * Abstand halten. Mitgeschnitten werden nur die WENIGEN
                 * Buchten, die ueberhaupt ein Beet beruehren.
                 */
                var interfering = output.Bay
                    .Where(bay => grass.Any(ring => QuadsOverlap(ring, bay, 0)))
                    .ToList();
                var blockers = interfering.Count == 0
                    ? asphalt : asphalt.Concat(interfering).ToList();
            void Hals(string wo)
            {
                if (!PhaseLog) return;
                var h = MinimumSurfaceFeature(grass);
                Console.Error.WriteLine($"      [Hals] {wo,-34} "
                    + (h == null ? "keine" : $"{h.Distance:G4} m")
                    + $" | {grass.Count} Ringe");
                Console.Error.Flush();
            }

                grass = SubtractAsphaltAsRegion(grass, blockers)
                    ?? SubtractAsphaltPerPiece(grass, blockers);
                grass = grass.Select(EntferneNadeln).ToList();
                Hals("nach Asphaltabzug");
                Heil("nach Asphaltabzug", grass, asphalt);
                /**
                 * GEFALTETE RINGE HIER AUFTRENNEN, NICHT SPAETER PRUEFEN.
                 *
                 * An dieser Stelle entstehen sie - gemessen 5 von 401 Ringen,
                 * ohne Doppelpunkte, also echte Kantenkreuzungen. Alles
                 * dahinter (Verschmelzen, Randabgleich, Reparatur) hat sie nur
                 * weitergereicht und am Ende verworfen. Aus einer Acht werden
                 * hier zwei Schleifen; genau so soll es sein, wenn eine Form
                 * eine echte Engstelle hat.
                 */
                grass = grass.SelectMany(r => EntfalteRing(r, 1e-6)).ToList();
                asphalt = asphalt.SelectMany(r => EntfalteRing(r, 1e-6)).ToList();
                Heil("nach Entfalten", grass, asphalt);
                // Der Materialwechsel kann einen kleinen Graskeil isolieren,
                // der im Standard Teil einer grossen Flaeche war. Nur in
                // diesem neuen Zweig darf er flaechentreu an Asphalt wechseln.
                if (material.MaterialChanged)
                {
                    TransferCs2Slivers(grass, asphalt, output.Bay,
                        out var transferredGrass, out var transferredAsphalt);
                    TransferThinGrassRings(transferredGrass, transferredAsphalt,
                        out grass, out asphalt);
                }
                /**
                 * ZUSAMMENFASSEN ZUERST, NICHT ZULETZT.
                 *
                 * Bisher stand `MergeAdjacentSurfaces` ganz am Ende der
                 * Materialkette - hinter den Reparaturlaeufen. Laeuft die
                 * Reparatur in ihr Zeitbudget, faellt damit auch das
                 * Zusammenfassen aus, und der Nutzer bekommt jede logische
                 * Teilflaeche einzeln in die Welt gelegt.
                 *
                 * Genau so entstand der Bau vom 2026-08-18 (465 Buchten):
                 * 97 rohe Grasteile, Abbruch in RepairMaterialNecks, am Ende
                 * 134 Grasflaechen im Spiel. Die Reparatur ist teuer und darf
                 * scheitern - das Zusammenfassen ist billig (8,7 s von 103 s
                 * ueber 60 Formen) und darf nicht davon abhaengen.
                 *
                 * Es steht weiterhin AUCH am Ende: dort arbeitet es auf den
                 * endgueltigen Knoten und faengt, was die Reparaturen noch
                 * aneinandergelegt haben.
                 */
                grass = MergeAdjacentSurfaces(grass, nahtTeilen: true);
                Hals("nach fruehem Verschmelzen");
                asphalt = MergeAdjacentSurfaces(asphalt, nahtTeilen: true);
                Heil("nach fruehem Verschmelzen", grass, asphalt);
                fallbackGrass = grass.Select(ring => ring.ToArray()).ToList();
                fallbackAsphalt = asphalt.Select(ring => ring.ToArray()).ToList();
                if (output.FillHole.Count != 0)
                    throw new InvalidOperationException(
                        "Material surfaces with a leftover hole are not decomposed yet.");

                var expectedArea = grass.Sum(ring => Math.Abs(SignedArea(ring)))
                                 + asphalt.Sum(ring => Math.Abs(SignedArea(ring)));
                // Zwischenlaeufe duerfen beim neuen Materialzweig Warnungen
                // sammeln, ohne sie schon auszugeben. Sichtbar werden sie nur,
                // wenn die fertigen Ringe weiterhin unbaubar sind.
                var repairOutput = material.MaterialChanged
                    ? new WorkLayout
                    {
                        Bay = output.Bay,
                        SilentMaterialWarnings = true,
                        AllowThinGrassTransfer = true,
                    }
                    : output;
                MissZeit("RepairMaterialNecks", () => RepairMaterialNecks(repairOutput, ref grass, ref asphalt, ref expectedArea));
                MissZeit("RepairMaterialCompatibility", () => RepairMaterialCompatibility(repairOutput, grass, asphalt));
                var materialArea = grass.Sum(ring => Math.Abs(SignedArea(ring)))
                    + asphalt.Sum(ring => Math.Abs(SignedArea(ring)));
                if (Math.Abs(materialArea - expectedArea)
                    > Math.Max(1e-5, expectedArea * 1e-9))
                    throw new InvalidOperationException(
                        $"Material area changed: {materialArea - expectedArea:G17} m2.");

                MissZeit("SynchronizeMaterialBoundaries", () => SynchronizeMaterialBoundaries(ref grass, ref asphalt));
                Hals("nach Randabgleich");
                Heil("nach Randabgleich", grass, asphalt);
                var roundedArea = grass.Sum(ring => Math.Abs(SignedArea(ring)))
                                + asphalt.Sum(ring => Math.Abs(SignedArea(ring)));
                MissZeit("RepairMaterialNecks", () => RepairMaterialNecks(repairOutput, ref grass, ref asphalt, ref roundedArea));
                MissZeit("RepairMaterialCompatibility", () => RepairMaterialCompatibility(repairOutput, grass, asphalt));
                MissZeit("SynchronizeMaterialBoundaries", () => SynchronizeMaterialBoundaries(ref grass, ref asphalt));
                /**
                 * Nach der LETZTEN Synchronisierung lief bisher kein
                 * Reparaturlauf mehr. Was ihr Runden auf float dort noch an
                 * Engstellen erzeugt, ging ungeprueft hinaus - im Nutzerfall
                 * ein Belaghals von 0,05 Millimetern, ohne jede Warnung, weil
                 * ihn niemand mehr gemessen hat.
                 */
                bool TooNarrow(List<double2[]> rings)
                {
                    var neck = MinimumSurfaceFeature(rings);
                    return neck != null && neck.Distance < SurfaceNeckLimit - 1e-9;
                }
                // Nur wenn es wirklich etwas zu reparieren gibt - messen ist
                // um Groessenordnungen billiger als ein voller Durchlauf.
                if (TooNarrow(grass) || TooNarrow(asphalt)
                    || !grass.Concat(asphalt).All(SurfaceCs2Compatible))
                {
                    var finalArea = grass.Sum(ring => Math.Abs(SignedArea(ring)))
                                  + asphalt.Sum(ring => Math.Abs(SignedArea(ring)));
                    MissZeit("RepairMaterialNecks", () => RepairMaterialNecks(repairOutput, ref grass, ref asphalt, ref finalArea));
                    MissZeit("RepairMaterialCompatibility", () => RepairMaterialCompatibility(repairOutput, grass, asphalt));
                }
                // Gemeinsame Geraden benoetigen nach der Synchronisierung keine
                // eigenen Zwischenknoten. Verlustfreies Verschmelzen verhindert,
                // dass daraus wieder eine kurze CS2-Kante wird.
                grass = grass.Select(ring => MergeCollinear(ring, 1e-9)).ToList();
                asphalt = asphalt.Select(ring => MergeCollinear(ring, 1e-9)).ToList();
                // Ganz zum Schluss, auf den endgueltigen Knoten: was
                // aneinanderliegt, wird EINE Flaeche. Vorher stand jede
                // logische Teilflaeche einzeln in der Welt, mit sichtbarer
                // Naht und unnoetig spitzen Winkeln an jeder Naht.
                grass = MergeAdjacentSurfaces(grass, nahtTeilen: true);
                asphalt = MergeAdjacentSurfaces(asphalt, nahtTeilen: true);
                /**
                 * UNBAUBAR DUENNE GRASBAENDER GEHEN AN DEN ASPHALT - IMMER.
                 *
                 * Bis 2026-08-17 lief das nur im Materialwechsel-Zweig. Der
                 * Nutzer meldete am selben Tag aus dem Spiel zwei Symptome, die
                 * in Wahrheit eines sind: sichtbare Grasstreifen und daneben
                 * leere Stellen. Sein Bau (437 Buchten, Reihenwinkel 93,33 Grad)
                 * nachgerechnet:
                 *
                 *     78 Grasteile unter 10 m2
                 *     Dicke  min 0,023 | Median 0,131 | max 0,67 m
                 *     69 davon unter CS2s Mindestkante 0,375 m
                 *     Laengsrichtung ALLER 78: 90-100 Grad
                 *
                 * Alle parallel zur Reihenrichtung - es sind die Reste, die
                 * beim Aufteilen in Reihen uebrig bleiben. Die dickeren baut
                 * CS2 als Streifen, die duenneren verwirft es; daher beides
                 * nebeneinander. Als eigene Flaeche ist keines davon baubar,
                 * also gehoeren sie an die Nachbarflaeche.
                 */
                TransferThinGrassRings(grass, asphalt, out grass, out asphalt);
                RepairCs2NodeEdges(output.Bay, ref grass, ref asphalt);
                // Die grosse neue Asphaltflaeche kann nach der letzten
                // Schlitzreparatur in zwei Ringen doppelt vertreten sein.
                // Die Vereinigung bleibt gleich, nur der Doppelbesitz endet.
                if (additionalPaving.Count > 0)
                    asphalt = MakeSurfacesDisjoint(asphalt);

                if (!ReferenceEquals(repairOutput, output)
                    && (TooNarrow(grass) || TooNarrow(asphalt)
                        || !grass.Concat(asphalt).All(SurfaceCs2Compatible)))
                    foreach (var warning in repairOutput.Warnings)
                    {
                        if (!output.Warnings.Contains(warning)) output.Warnings.Add(warning);
                        System.Diagnostics.Trace.TraceWarning(warning);
                    }

                Heil("vor DropShortEdges", grass, asphalt);
                Hals("vor der Endkontrolle");
                output.GrassSurface = DropShortEdges(
                    NurWasCs2BauenKann(AufGitter(grass)));
                output.AsphaltSurface = DropShortEdges(
                    NurWasCs2BauenKann(AufGitter(asphalt)));
            }
            catch (Exception exception)
            {
                var coordinates = fallbackGrass.FirstOrDefault()
                    ?? fallbackAsphalt.FirstOrDefault() ?? Array.Empty<double2>();
                var preview = string.Join(", ", coordinates.Take(4)
                    .Select(point => $"({point.x:G17}, {point.y:G17})"));
                if (PhaseLog)
                    Console.Error.WriteLine("      [Abbruch] Materialreparatur: "
                        + exception.Message);
                var message = $"Materialreparatur abgebrochen: {exception.Message}; "
                    + $"Flaechen bleiben unveraendert bei [{preview}].";
                if (!output.Warnings.Contains(message)) output.Warnings.Add(message);
                System.Diagnostics.Trace.TraceWarning(message);
                /**
                 * DER RUECKFALL MUSS TROTZDEM ZUSAMMENGEFASST SEIN.
                 *
                 * Am 2026-08-18 meldete der Nutzer "Dreieck nicht an
                 * Randstrasse angeschlossen". Der Abzug des Spiels zeigte 22
                 * Belagflaechen, darunter vier von exakt 159,3 m2 - die vier
                 * gekappten Ecken, unverschmolzen. Nachgestellt mit einem auf
                 * 2,0 s gesenkten Budget liefert die Nachrechnung dieselben 22.
                 *
                 * Ursache: bricht die Materialreparatur ab, gehen die
                 * Rueckfallflaechen so hinaus, wie sie vor ihr aussahen -
                 * jede logische Teilflaeche einzeln. Das Zusammenfassen selbst
                 * ist billig; es hier nachzuholen kostet nur den Abbruchfall.
                 * Ohne Budget, sonst schlaegt dieselbe Uhr sofort wieder zu.
                 */
                try
                {
                    BudgetAus = true;
                    // Erst die Raender deckungsgleich machen, dann verschmelzen.
                    // Ohne diesen Schritt scheitert TrySurfaceUnion: gemessen
                    // blieben 22 Belagringe, die einander zwar alle beruehren
                    // (31 Paare, EINE zusammenhaengende Gruppe), aber keine
                    // gemeinsame Kante mit passenden Endpunkten haben.
                    SynchronizeMaterialBoundaries(ref fallbackGrass, ref fallbackAsphalt);
                    fallbackGrass = MergeAdjacentSurfaces(fallbackGrass, nahtTeilen: true);
                    fallbackAsphalt = MergeAdjacentSurfaces(fallbackAsphalt, nahtTeilen: true);
                }
                catch (Exception zweite)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "Auch das Zusammenfassen im Rueckfall scheiterte: "
                        + zweite.Message);
                }
                finally { BudgetAus = false; }
                output.GrassSurface = DropShortEdges(
                    NurWasCs2BauenKann(AufGitter(fallbackGrass)));
                output.AsphaltSurface = DropShortEdges(
                    NurWasCs2BauenKann(AufGitter(fallbackAsphalt)));
            }
        }
    }
}
