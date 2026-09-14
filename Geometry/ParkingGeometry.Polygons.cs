using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        private static double2 Sub(double2 a, double2 b) => a - b;
        private static double2 Add(double2 a, double2 b) => a + b;
        private static double2 Mul(double2 a, double k) => a * k;
        private static double Len(double2 a) => Math.Sqrt(a.x * a.x + a.y * a.y);

        private static double2 Norm(double2 a)
        {
            var length = Len(a);
            if (length == 0) length = 1;
            return a / length;
        }

        internal static double SignedArea(double2[] polygon)
        {
            var sum = 0.0;
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum / 2;
        }

        internal static bool PointIn(double2 p, double2[] ring)
        {
            var inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static double2[] Rect(double2 center, double2 direction, double2 normal,
                                      double halfDirection, double halfNormal)
        {
            return new[]
            {
                center - direction * halfDirection - normal * halfNormal,
                center + direction * halfDirection - normal * halfNormal,
                center + direction * halfDirection + normal * halfNormal,
                center - direction * halfDirection + normal * halfNormal,
            };
        }

        /**
         * Abstand eines Punktes zur naechsten Polygonkante.
         *
         * Rechnet in QUADRATEN und zieht die Wurzel nur einmal am Ende statt einmal
         * je Kante. Ausserdem ohne Hilfsvektoren: diese Funktion ist mit RectClear
         * zusammen der heisseste Pfad im Modell. Das JS-Ergebnis ist bitgleich.
         */
        internal static double DistToBoundary(double2 p, double2[] ring)
        {
            var px = p.x;
            var py = p.y;
            var best = double.PositiveInfinity;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                var abx = b.x - a.x;
                var aby = b.y - a.y;
                var l2 = abx * abx + aby * aby;
                if (l2 == 0) l2 = 1;
                var t = ((px - a.x) * abx + (py - a.y) * aby) / l2;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                var dx = px - (a.x + abx * t);
                var dy = py - (a.y + aby * t);
                var d2 = dx * dx + dy * dy;
                if (d2 < best) best = d2;
            }
            return Math.Sqrt(best);
        }

        private sealed class Box
        {
            internal double X0;
            internal double Y0;
            internal double X1;
            internal double Y1;
        }

        private static readonly ConditionalWeakTable<double2[], Box> Boxes =
            new ConditionalWeakTable<double2[], Box>();

        /** Umschliessendes Rechteck eines Polygons, einmal je Polygon gerechnet. */
        private static Box BboxOf(double2[] polygon)
        {
            return Boxes.GetValue(polygon, q =>
            {
                var box = new Box
                {
                    X0 = double.PositiveInfinity,
                    Y0 = double.PositiveInfinity,
                    X1 = double.NegativeInfinity,
                    Y1 = double.NegativeInfinity,
                };
                foreach (var p in q)
                {
                    if (p.x < box.X0) box.X0 = p.x;
                    if (p.x > box.X1) box.X1 = p.x;
                    if (p.y < box.Y0) box.Y0 = p.y;
                    if (p.y > box.Y1) box.Y1 = p.y;
                }
                return box;
            });
        }

        private static bool InsideBy(double2 p, double2[] site, double distance)
        {
            // Schnelle Abweisung ueber das umschliessende Rechteck.
            var box = BboxOf(site);
            var epsilonDistance = distance - FitEps;
            if (p.x < box.X0 + epsilonDistance || p.x > box.X1 - epsilonDistance
                || p.y < box.Y0 + epsilonDistance || p.y > box.Y1 - epsilonDistance)
                return false;
            return PointIn(p, site) && DistToBoundary(p, site) >= epsilonDistance;
        }

        private static bool RectClear(double2 center, double2 direction, double2 normal,
                                      double halfDirection, double halfNormal,
                                      double2[] site, double clearance)
        {
            for (var i = -1; i <= 1; i++)
                for (var j = -1; j <= 1; j++)
                    if (!InsideBy(center + direction * (halfDirection * i)
                                         + normal * (halfNormal * j), site, clearance))
                        return false;
            return true;
        }

        /** Ueberlappen sich zwei konvexe Vierecke? Trennachsen-Test mit Toleranz. */
        /**
         * Trennachsen-Test mit vorgeschaltetem Rechteckvergleich.
         *
         * GEMESSEN am Nutzerbau PLT-90E34B07 (438 Buchten): eine einzige
         * Vorschau ruft diese Funktion 15 000 000 mal auf, und 99,2 % der
         * Paare liegen so weit auseinander, dass schon ihre umschliessenden
         * Rechtecke disjunkt sind. Fuer die braucht es keine Achsenprojektion.
         *
         * Im Profil des Prototyps ist `quadsOverlap` mit 43,4 % der groesste
         * Einzelposten. Das Ergebnis bleibt bitgleich - der Vorfilter verwirft
         * nur Paare, die der Trennachsen-Test ohnehin verworfen haette.
         *
         * Ebenfalls weg: das `new[] { a, b }` der alten Fassung. Es legte bei
         * JEDEM Aufruf ein Array an, also 15 Millionen je Vorschau. Unter Mono
         * im Spiel ist das ein eigener Posten.
         */
        internal static bool QuadsOverlap(double2[] a, double2[] b, double tolerance = 0.02)
        {
            // LIVE-ZAEHLER. Im Spiel laeuft alles unter Mono, rund achtmal
            // langsamer als der Prototyp unter node - eine dort gemessene
            // Verteilung laesst sich also nicht einfach uebertragen. Statt zu
            // schaetzen, zaehlt der Mod im Spiel selbst mit; die Zahlen landen
            // in der Zeile "PLT-Vorschau berechnet".
            OverlapCalls++;
            if (BoundsDisjoint(a, b, tolerance)) { OverlapRejectedByBounds++; return false; }
            for (var seite = 0; seite < 2; seite++)
            {
                var polygon = seite == 0 ? a : b;
                for (var i = 0; i < polygon.Length; i++)
                {
                    var edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                    var axis = Norm(new double2(-edge.y, edge.x));
                    var aLo = double.PositiveInfinity;
                    var aHi = double.NegativeInfinity;
                    var bLo = double.PositiveInfinity;
                    var bHi = double.NegativeInfinity;
                    foreach (var p in a)
                    {
                        var projection = p.x * axis.x + p.y * axis.y;
                        aLo = Math.Min(aLo, projection);
                        aHi = Math.Max(aHi, projection);
                    }
                    foreach (var p in b)
                    {
                        var projection = p.x * axis.x + p.y * axis.y;
                        bLo = Math.Min(bLo, projection);
                        bHi = Math.Max(bHi, projection);
                    }
                    if (aHi <= bLo + tolerance || bHi <= aLo + tolerance) return false;
                }
            }
            return true;
        }

        /**
         * Liegen die umschliessenden Rechtecke sicher auseinander?
         *
         * Die Toleranz zaehlt hier mit demselben Vorzeichen wie im
         * Trennachsen-Test: dort trennt `aHi <= bLo + tolerance`, hier also
         * `aMax <= bMin + tolerance`. Ein positiver Wert macht den Test damit
         * strenger, nie lockerer - der Vorfilter kann also nichts durchlassen
         * oder verwerfen, was der volle Test anders entscheiden wuerde.
         */
        /// <summary>Aufrufe von QuadsOverlap seit dem letzten Zuruecksetzen.</summary>
        internal static long OverlapCalls;

        /// <summary>Davon vom Rechteckvergleich sofort verworfen.</summary>
        internal static long OverlapRejectedByBounds;

        internal static void ResetCounters()
        {
            OverlapCalls = 0;
            OverlapRejectedByBounds = 0;
        }

        private static bool BoundsDisjoint(double2[] a, double2[] b, double tolerance)
        {
            double axMin = a[0].x, axMax = a[0].x, ayMin = a[0].y, ayMax = a[0].y;
            for (var i = 1; i < a.Length; i++)
            {
                var p = a[i];
                if (p.x < axMin) axMin = p.x; else if (p.x > axMax) axMax = p.x;
                if (p.y < ayMin) ayMin = p.y; else if (p.y > ayMax) ayMax = p.y;
            }
            double bxMin = b[0].x, bxMax = b[0].x, byMin = b[0].y, byMax = b[0].y;
            for (var i = 1; i < b.Length; i++)
            {
                var p = b[i];
                if (p.x < bxMin) bxMin = p.x; else if (p.x > bxMax) bxMax = p.x;
                if (p.y < byMin) byMin = p.y; else if (p.y > byMax) byMax = p.y;
            }
            return axMax <= bxMin + tolerance || bxMax <= axMin + tolerance
                || ayMax <= byMin + tolerance || byMax <= ayMin + tolerance;
        }

        /**
         * Punkt-in-irgendeinem-Viereck, mit Gitterindex. Ohne ihn bedeutete ein
         * 0,5-m-Raster bei rund 500 Vierecken Millionen Punkttests.
         */
        private sealed class CoverIndex
        {
            private readonly double _cell;
            private readonly Dictionary<(int, int), List<double2[]>> _map =
                new Dictionary<(int, int), List<double2[]>>();

            internal CoverIndex(IEnumerable<double2[]> quads, double cell)
            {
                _cell = cell;
                foreach (var q in quads)
                {
                    var i0 = (int)Math.Floor(q.Min(p => p.x) / cell);
                    var i1 = (int)Math.Floor(q.Max(p => p.x) / cell);
                    var j0 = (int)Math.Floor(q.Min(p => p.y) / cell);
                    var j1 = (int)Math.Floor(q.Max(p => p.y) / cell);
                    for (var i = i0; i <= i1; i++)
                        for (var j = j0; j <= j1; j++)
                        {
                            if (!_map.TryGetValue((i, j), out var list))
                                _map[(i, j)] = list = new List<double2[]>();
                            list.Add(q);
                        }
                }
            }

            internal bool Contains(double2 p)
            {
                var key = ((int)Math.Floor(p.x / _cell), (int)Math.Floor(p.y / _cell));
                return _map.TryGetValue(key, out var list) && list.Any(q => PointIn(p, q));
            }
        }

        private sealed class SlabSegment
        {
            internal double2 A;
            internal double2 B;
        }

        private sealed class OpenTrap
        {
            internal double X0;
            internal double X1;
            internal SlabSegment Lo;
            internal SlabSegment Hi;
        }

        private sealed class Trap
        {
            internal double X0;
            internal double X1;
            internal double Lo0;
            internal double Lo1;
            internal double Hi0;
            internal double Hi1;
        }

        private sealed class OrderedSlabEdge
        {
            internal Line2 Edge;
            internal long Order;
        }

        private sealed class SlabResult
        {
            internal readonly List<double2[]> Aussen = new List<double2[]>();
            internal readonly List<double2[]> Loecher = new List<double2[]>();
            internal bool Fallback;
            internal double Fehler;
        }

        /**
         * EXAKTE Restflaeche durch Slab-Zerlegung.
         *
         * Das Raster konnte prinzipbedingt nie punktgenau sein: an jeder schraegen
         * Kante blieb ein Saum. Senkrechte Streifen an allen Eck- und Schnittpunkten
         * liefern stattdessen exakte Trapeze.
         */
        /**
         * Wie nah zwei Streifengrenzen sein duerfen, bevor sie als eine gelten.
         * Empirisch bestimmt - siehe die Begruendung in `SlabFill`.
         */
        internal static double SlabCutTol = 0.01;

        private static SlabResult SlabFill(double2[] site, IEnumerable<double2[]> quads,
                                           Func<double2, bool> blocked, double minArea)
        {
            var uhrSlab = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
            try
            {
            var segments = new List<SlabSegment>();
            var xs = new HashSet<double>();
            void AddPolygon(double2[] polygon)
            {
                for (var i = 0; i < polygon.Length; i++)
                {
                    var a = polygon[i];
                    var b = polygon[(i + 1) % polygon.Length];
                    xs.Add(a.x);
                    if (Math.Abs(a.x - b.x) < 1e-9) continue;
                    segments.Add(a.x < b.x
                        ? new SlabSegment { A = a, B = b }
                        : new SlabSegment { A = b, B = a });
                }
            }

            AddPolygon(site);
            foreach (var q in quads) AddPolygon(q);

            // Buchten und Gruen ueberlappen nie, Fahrbahnen an Einmuendungen
            // schon. Ohne diese Ereignisse tauschen Kanten im Streifen die Ordnung.
            for (var i = 0; i < segments.Count; i++)
                for (var j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    if (a.B.x <= b.A.x || b.B.x <= a.A.x) continue;
                    var r = a.B - a.A;
                    var s = b.B - b.A;
                    var den = r.x * s.y - r.y * s.x;
                    if (Math.Abs(den) < 1e-12) continue;
                    var delta = b.A - a.A;
                    var t = (delta.x * s.y - delta.y * s.x) / den;
                    var u = (delta.x * r.y - delta.y * r.x) / den;
                    if (t <= 0 || t >= 1 || u <= 0 || u >= 1) continue;
                    xs.Add(a.A.x + r.x * t);
                }

            /**
             * STREIFENGRENZEN ZUSAMMENFASSEN, die praktisch aufeinanderliegen.
             *
             * Ohne das entsteht je Koordinate ein eigener Streifen. Liegt die
             * Reihenrichtung EXAKT parallel zu einer Arealkante - und genau das
             * waehlt `angleMode = "edge"`, das der Mod immer faehrt -, landen
             * hunderte Buchtenecken auf fast demselben x und erzeugen
             * Haarrisse.
             *
             * Am 2026-08-17 gemessen, dasselbe Fuenfeck mit 16.430 m2:
             *
             *     143,2523 Grad (= edge)  ->  2208 Fuellteile
             *     143,2500 Grad           ->    20 Fuellteile
             *
             * 0,0023 Grad Unterschied, Faktor 110 - bei gleicher Buchtenzahl
             * und gleicher Gesamtflaeche (1499 gegen 1501 m2). 1863 der 2231
             * Teile lagen unter 1 m2, das kleinste bei 0,0201 m2.
             *
             * Die Folgen trafen den Nutzer doppelt: CS2 verwirft Flaechen unter
             * seiner Mindestkante von 0,375 m (Luecken im Belag), und die
             * Materialreparatur laeuft superlinear ueber alle Ringe - bei 2231
             * statt 20 blieb die Vorschau haengen, gemessen 938 s CPU ohne
             * Ergebnis.
             *
             * Die Toleranz ist bewusst klein: sie darf nur numerische
             * Doppelgaenger schlucken, keine echten Kanten verschieben. Der
             * Fehler an einer Streifengrenze ist damit hoechstens SlabCutTol.
             */
            var sortiert = xs.OrderBy(x => x).ToArray();
            var zusammengefasst = new List<double>(sortiert.Length);
            foreach (var x in sortiert)
                if (zusammengefasst.Count == 0
                    || x - zusammengefasst[zusammengefasst.Count - 1] > SlabCutTol)
                    zusammengefasst.Add(x);
            var cuts = zusammengefasst.ToArray();
            // Bei entarteten Formen wurden hier schon 2231 Streifen erzeugt.
            CheckMaterialBudget($"SlabFill mit {cuts.Length} Grenzen");
            double YAt(SlabSegment segment, double x) => segment.A.y
                + (segment.B.y - segment.A.y) * (x - segment.A.x)
                / (segment.B.x - segment.A.x);
            var open = new List<OpenTrap>();
            for (var i = 0; i + 1 < cuts.Length; i++)
            {
                var x0 = cuts[i];
                var x1 = cuts[i + 1];
                if (x1 - x0 < 1e-7) continue;
                var xm = (x0 + x1) / 2;
                var active = segments.Where(s => s.A.x <= xm && s.B.x >= xm)
                    .OrderBy(s => YAt(s, xm)).ToArray();
                if (active.Length < 2) continue;
                for (var k = 0; k + 1 < active.Length; k++)
                {
                    var lo = active[k];
                    var hi = active[k + 1];
                    var yl = YAt(lo, xm);
                    var yh = YAt(hi, xm);
                    if (yh - yl < 1e-7 || blocked(new double2(xm, (yl + yh) / 2))) continue;
                    open.Add(new OpenTrap { X0 = x0, X1 = x1, Lo = lo, Hi = hi });
                }
            }

            // Verschmelzen, solange dieselben zwei Kanten begrenzen.
            var merged = new List<OpenTrap>();
            foreach (var trap in open)
            {
                var last = merged.Count == 0 ? null : merged[merged.Count - 1];
                if (last != null && ReferenceEquals(last.Hi, trap.Hi)
                    && ReferenceEquals(last.Lo, trap.Lo) && Math.Abs(last.X1 - trap.X0) < 1e-7)
                {
                    last.X1 = trap.X1;
                    continue;
                }
                merged.Add(new OpenTrap
                    { X0 = trap.X0, X1 = trap.X1, Lo = trap.Lo, Hi = trap.Hi });
            }
            var traps = merged.Select(t => new Trap
            {
                X0 = t.X0,
                X1 = t.X1,
                Lo0 = YAt(t.Lo, t.X0),
                Lo1 = YAt(t.Lo, t.X1),
                Hi0 = YAt(t.Hi, t.X0),
                Hi1 = YAt(t.Hi, t.X1),
            }).ToList();

            // Geteilte Kanten heben sich auf. Senkrechte Kanten werden an allen
            // Nachbargrenzen geteilt; ohne das blieben 790 Schnipsel.
            long K(double value) => (long)Math.Floor(value * 1e6 + 0.5);
            var separators = new Dictionary<long, HashSet<long>>();
            void Remember(double x, double y)
            {
                var key = K(x);
                if (!separators.TryGetValue(key, out var set))
                    separators[key] = set = new HashSet<long>();
                set.Add(K(y));
            }
            foreach (var t in traps)
            {
                Remember(t.X0, t.Lo0); Remember(t.X0, t.Hi0);
                Remember(t.X1, t.Lo1); Remember(t.X1, t.Hi1);
            }

            /**
             * Gerichtete Kanten als MULTIMENGE fuehren.
             *
             * Ein Eintrag je Richtung reicht nicht: kommt dieselbe Richtung
             * zweimal vor, ueberschrieb der zweite Eintrag den ersten, und
             * eine spaetere Gegenkante loeschte damit beide auf einmal.
             * Gemessen am Prototyp (Debug 23:32, sl 5,9) geschah das 19-mal;
             * der Randgraph hatte danach 20 unausgeglichene Knoten und 2109
             * offene Verfolgungen, und die Ringflaeche wich um 282,965 m2 von
             * der Trapezflaeche ab. Paarweises Aufheben laesst 0,000009 m2.
             */
            var edges = new Dictionary<((long, long), (long, long)),
                List<OrderedSlabEdge>>();
            long nextEdgeOrder = 0;
            void AddEdge(double2 a, double2 b)
            {
                if (Math.Abs(a.x - b.x) < 1e-9 && Math.Abs(a.y - b.y) < 1e-9) return;
                var ka = (K(a.x), K(a.y));
                var kb = (K(b.x), K(b.y));
                var back = (kb, ka);
                if (edges.TryGetValue(back, out var opposite) && opposite.Count > 0)
                {
                    opposite.RemoveAt(opposite.Count - 1);
                    if (opposite.Count == 0) edges.Remove(back);
                    return;
                }
                var key = (ka, kb);
                if (!edges.TryGetValue(key, out var list))
                    edges[key] = list = new List<OrderedSlabEdge>();
                list.Add(new OrderedSlabEdge
                {
                    Edge = new Line2(a, b),
                    Order = nextEdgeOrder++,
                });
            }
            void Vertical(double x, double ya, double yb)
            {
                var split = separators.TryGetValue(K(x), out var all)
                    ? all.Select(y => y / 1e6).Where(y => y > Math.Min(ya, yb) + 1e-9
                        && y < Math.Max(ya, yb) - 1e-9).ToList()
                    : new List<double>();
                split.Sort((p, q) => ya < yb ? p.CompareTo(q) : q.CompareTo(p));
                split.Add(yb);
                var current = ya;
                foreach (var y in split)
                {
                    AddEdge(new double2(x, current), new double2(x, y));
                    current = y;
                }
            }
            foreach (var t in traps)
            {
                AddEdge(new double2(t.X0, t.Lo0), new double2(t.X1, t.Lo1));
                Vertical(t.X1, t.Lo1, t.Hi1);
                AddEdge(new double2(t.X1, t.Hi1), new double2(t.X0, t.Hi0));
                Vertical(t.X0, t.Hi0, t.Lo0);
            }

            var byStart = new Dictionary<(long, long), List<Line2>>();
            // JavaScript-Map haengt einen nach delete neu eingefuegten Eintrag
            // hinten an. Dictionary darf dagegen den geloeschten Slot nutzen;
            // ohne die explizite Ordnung zerfielen bei Schraeg zwei JS-Ringe.
            foreach (var ordered in edges.Values.SelectMany(item => item)
                         .OrderBy(item => item.Order))
            {
                var edge = ordered.Edge;
                var key = (K(edge.A.x), K(edge.A.y));
                if (!byStart.TryGetValue(key, out var list))
                    byStart[key] = list = new List<Line2>();
                list.Add(edge);
            }

            /**
             * An einem Knoten mit MEHREREN Ausgaengen nach Winkel weitergehen.
             *
             * Solange sich an einem Knoten genau zwei Kanten treffen, ist die
             * Wahl egal. An einem Beruehrpunkt zweier Randschleifen gibt es
             * aber zwei Ein- und zwei Ausgaenge, und eine beliebige Wahl
             * verschraenkt die Schleifen - der geschlossene Ring umfasst dann
             * Flaeche, die ihm nicht gehoert. Richtig ist die uebliche
             * Flaechenverfolgung: von der Gegenrichtung der Ankunft aus im
             * Uhrzeigersinn drehen und den ersten Ausgang nehmen.
             */
            Line2 NextEdge(List<Line2> list, double arrivalX, double arrivalY)
            {
                if (list.Count == 1)
                {
                    var only = list[0];
                    list.RemoveAt(0);
                    return only;
                }
                var back = Math.Atan2(-arrivalY, -arrivalX);
                var bestIndex = 0;
                var bestTurn = double.MaxValue;
                for (var i = 0; i < list.Count; i++)
                {
                    var candidate = list[i];
                    var turn = back - Math.Atan2(candidate.B.y - candidate.A.y,
                        candidate.B.x - candidate.A.x);
                    while (turn <= 1e-12) turn += 2 * Math.PI;
                    while (turn > 2 * Math.PI) turn -= 2 * Math.PI;
                    if (turn >= bestTurn) continue;
                    bestTurn = turn;
                    bestIndex = i;
                }
                var chosen = list[bestIndex];
                list.RemoveAt(bestIndex);
                return chosen;
            }

            var rings = new List<double2[]>();
            foreach (var list in byStart.Values)
                while (list.Count > 0)
                {
                    var first = list[list.Count - 1];
                    list.RemoveAt(list.Count - 1);
                    var ring = new List<double2> { first.A };
                    var previous = first.A;
                    var current = first.B;
                    var closed = false;
                    for (var guard = 0; guard < 100000; guard++)
                    {
                        if (Math.Abs(current.x - first.A.x) < 1e-6
                            && Math.Abs(current.y - first.A.y) < 1e-6)
                        {
                            closed = true;
                            break;
                        }
                        if (!byStart.TryGetValue((K(current.x), K(current.y)), out var next)
                            || next.Count == 0) break;
                        var edge = NextEdge(next, current.x - previous.x,
                            current.y - previous.y);
                        ring.Add(edge.A);
                        previous = edge.A;
                        current = edge.B;
                    }
                    // Eine offene Kette niemals ueber die implizite Schlusskante
                    // zum Ring erklaeren - das erfand Flaeche, die es nicht gibt.
                    // Die Flaechengegenprobe erzwingt dann den Notmodus.
                    if (closed && ring.Count >= 3) rings.Add(ring.ToArray());
                }

            var result = new SlabResult();
            // Umlaufsinn trennt Aussenring von LOCH. Ohne die Trennung lagen bei
            // der L-Form 30 % der Restflaeche auf Buchten und Fahrbahn.
            foreach (var ring in rings)
            {
                // CS2 verlangt einen Mindestabstand zwischen Flaechenknoten.
                // Gemessen hatten vor dieser Bereinigung 10 von 21 Fuellringen
                // der Referenz aufeinanderfolgende Punkte unter 5 cm.
                var prepared = RemoveNearDuplicatePoints(RemoveNeedles(
                    RemoveNearDuplicatePoints(MergeCollinear(ring, 1e-6))));
                if (prepared.Length < 3 || Math.Abs(SignedArea(prepared)) < minArea)
                    continue;
                foreach (var part in SplitTouchingRing(prepared))
                {
                    // Materialgrenzen werden nach dem Layout gemeinsam geteilt
                    // oder vereinigt. Ein nur auf der Grasseite verschobener
                    // Punkt erzeugt dagegen Spalt oder Asphalt-Ueberlappung.
                    // Deshalb wird hier nichts verschoben - nur ein
                    // deckungsgleicher Knoten faellt weg.
                    var q = RemoveZeroLengthEdges(part);
                    if (q.Length < 3) continue;
                    var area = SignedArea(q);
                    if (Math.Abs(area) < 1e-12) continue;
                    (area > 0 ? result.Aussen : result.Loecher).Add(q);
                }
            }

            // Die Flaechensumme allein erkennt einen selbstschneidenden Ring
            // nicht: Schraeg hatte einen, Referenz zwei, obwohl ihre Flaeche
            // zufaellig stimmen konnte. Gute Ringe bleiben erhalten; nur jeder
            // kaputte Aussenring wird durch seine eigenen Trapeze ersetzt.
            var broken = result.Aussen.Where(ring => SelfIntersects(ring)).ToList();
            if (broken.Count > 0)
            {
                for (var i = result.Aussen.Count - 1; i >= 0; i--)
                    if (SelfIntersects(result.Aussen[i])) result.Aussen.RemoveAt(i);
                foreach (var t in traps)
                {
                    var q = TrapPolygon(t);
                    var center = (q[0] + q[1] + q[2] + q[3]) / 4;
                    if (!broken.Any(ring => PointIn(center, ring))) continue;
                    var area = SignedArea(q);
                    if (Math.Abs(area) < minArea) continue;
                    result.Aussen.Add(area > 0 ? q : q.Reverse().ToArray());
                }
            }
            var brokenHole = result.Loecher.Any(ring => SelfIntersects(ring));

            // Gegenprobe: beim frei gezogenen Zehneck lieferte die Rekonstruktion
            // -126 m2 statt 258 m2 und damit 503 m2 bzw. 4,8 % unbelegte Flaeche.
            // Bei Abweichung gehen die exakten Trapeze unveraendert hinaus.
            var trapArea = traps.Sum(t => Math.Abs(SignedArea(TrapPolygon(t))));
            var ringArea = result.Aussen.Sum(q => Math.Abs(SignedArea(q)))
                         - result.Loecher.Sum(q => Math.Abs(SignedArea(q)));
            // Die Schranke war 0,1 % und richtete mehr Schaden an als der Fehler.
            // Gemessen an einem echten Nutzerpolygon: 69 saubere Umrissringe,
            // KEINER kaputt, aber die Ringflaeche wich um 1,64 m2 von 1096 m2 ab
            // - Rechenrauschen. Die Schranke lag bei 1,10 m2, also griff der
            // Rueckfall: 69 Ringe raus, 752 Trapeze rein. Davon lehnte CS2 dann
            // 450 als NoTriangles ab, und 326,7 m2 Gruen fehlten. Die Sicherung
            // kostete das Fuenfzigfache dessen, wovor sie schuetzen sollte.
            // Mit 0,5 %: 69 statt 603 Fuellstuecke, kein Gruen auf Fahrbahn oder
            // Bucht, 0,00 % ungedeckt, Standardfaelle unveraendert.
            if (brokenHole
                || Math.Abs(ringArea - trapArea) > Math.Max(0.5, trapArea * 0.005))
            {
                result.Aussen.Clear();
                result.Loecher.Clear();
                foreach (var t in traps)
                {
                    var q = TrapPolygon(t);
                    var area = SignedArea(q);
                    if (Math.Abs(area) >= minArea)
                        result.Aussen.Add(area > 0 ? q : q.Reverse().ToArray());
                }
                result.Fallback = true;
                result.Fehler = ringArea - trapArea;
            }
            return result;
            }
            finally
            {
                if (uhrSlab != null)
                {
                    uhrSlab.Stop();
                    MaterialZeiten.TryGetValue("SlabFill", out var b);
                    MaterialZeiten["SlabFill"] = b + uhrSlab.Elapsed.TotalMilliseconds;
                }
            }
        }

        private static double2[] TrapPolygon(Trap t) => new[]
        {
            new double2(t.X0, t.Lo0), new double2(t.X1, t.Lo1),
            new double2(t.X1, t.Hi1), new double2(t.X0, t.Hi0),
        };

        /**
         * Kollabiert eine Kante unter 5 cm nur auf den analytischen Punkt, bei
         * dem die Shoelace-Fläche gleich bleibt. Damit wird nicht blind einer
         * der beiden gemeinsamen Knoten gelöscht.
         */
        private static double2[] RemoveNearDuplicatePoints(double2[] ring)
        {
            var output = ring.ToList();
            double Cross(double2 a, double2 b) => a.x * b.y - a.y * b.x;
            for (var guard = 0; guard < ring.Length * 2 && output.Count > 3; guard++)
            {
                var collapsed = false;
                for (var i = 0; i < output.Count; i++)
                {
                    var j = (i + 1) % output.Count;
                    if (Len(output[i] - output[j]) >= 0.05) continue;
                    var rotated = output.Skip(i).Concat(output.Take(i)).ToList();
                    var before = rotated[rotated.Count - 1];
                    var first = rotated[0];
                    var second = rotated[1];
                    var after = rotated[2];
                    double Contribution(double2 replacement) =>
                        Cross(before, replacement) + Cross(replacement, after);
                    var wanted = Cross(before, first) + Cross(first, second)
                               + Cross(second, after);
                    var atFirst = Contribution(first);
                    var atSecond = Contribution(second);
                    var denominator = atSecond - atFirst;
                    var t = Math.Abs(denominator) < 1e-14
                        ? 0.5 : (wanted - atFirst) / denominator;
                    if (t < -0.5 || t > 1.5) continue;
                    var replacement = first + (second - first) * t;
                    if (Len(replacement - first) >= 0.05
                        || Len(replacement - second) >= 0.05) continue;
                    rotated[0] = replacement;
                    rotated.RemoveAt(1);
                    if (Math.Abs(Math.Abs(SignedArea(rotated.ToArray()))
                        - Math.Abs(SignedArea(output.ToArray()))) > 1e-8) continue;
                    output = rotated;
                    collapsed = true;
                    break;
                }
                if (!collapsed) break;
            }
            return output.ToArray();
        }

        /**
         * Entfernt nur die flaechennullige Rueckspur A-B-A. Ein bloss naher
         * Punkt wird nicht einseitig normalisiert; ihn repariert spaeter die
         * Materialstufe gemeinsam mit der angrenzenden Flaeche.
         */
        private static double2[] RemoveNeedles(double2[] ring)
        {
            var output = ring.ToList();
            for (var round = 0; round < 8; round++)
            {
                var next = new List<double2>(output.Count);
                var removed = 0;
                for (var i = 0; i < output.Count; i++)
                {
                    var before = output[(i - 1 + output.Count) % output.Count];
                    var after = output[(i + 1) % output.Count];
                    if (output.Count - removed > 3 && Len(before - after) < 1e-8)
                    {
                        removed++;
                        continue;
                    }
                    next.Add(output[i]);
                }
                if (removed == 0) break;
                output = next;
            }
            return output.ToArray();
        }

        /**
         * Trennt einen Ring an jedem Punktkontakt mit einer nicht benachbarten
         * Kante. Beide neuen Umlaeufe benutzen denselben Kontaktpunkt; ihre
         * Flaechensumme ist daher die des Ausgangsrings. Die Warteschlange ist
         * wichtig: im 21:41-Debug-Abzug enthielten einzelne Ringe mehr als eine
         * solche Beruehrung.
         *
         * 1e-5 m erfasst Rechenrauschen um einen exakten Kontakt, bleibt aber
         * deutlich unter den getrennt zu untersuchenden 0,1502-m-Haelsen.
         */
        private static IEnumerable<double2[]> SplitTouchingRing(double2[] ring)
        {
            const double touchDistanceSquared = 1e-10;
            var pending = new List<double2[]> { ring };
            var output = new List<double2[]>();
            for (var pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
            {
                var current = pending[pendingIndex];
                if (TrySplitTouchingRing(current, touchDistanceSquared,
                                         out var first, out var second))
                {
                    pending.Add(first);
                    pending.Add(second);
                }
                else
                {
                    output.Add(current);
                }
            }
            return output;
        }

        private static bool TrySplitTouchingRing(double2[] ring, double maximumDistanceSquared,
                                                  out double2[] first, out double2[] second)
        {
            first = null;
            second = null;
            var count = ring.Length;
            for (var pointIndex = 0; pointIndex < count; pointIndex++)
                for (var edgeIndex = 0; edgeIndex < count; edgeIndex++)
                {
                    // Beide Teilringe brauchen wenigstens drei Ecken. Damit sind
                    // zugleich die beiden am Punkt anliegenden Kanten ausgeschlossen.
                    var steps = (edgeIndex - pointIndex + count) % count;
                    if (steps < 2 || steps > count - 3) continue;

                    var point = ring[pointIndex];
                    var a = ring[edgeIndex];
                    var b = ring[(edgeIndex + 1) % count];
                    var edge = b - a;
                    var squaredLength = edge.x * edge.x + edge.y * edge.y;
                    if (squaredLength < 1e-12) continue;
                    var parameter = ((point.x - a.x) * edge.x
                                   + (point.y - a.y) * edge.y) / squaredLength;
                    if (parameter <= 1e-7 || parameter >= 1 - 1e-7) continue;
                    var delta = point - (a + edge * parameter);
                    if (delta.x * delta.x + delta.y * delta.y > maximumDistanceSquared)
                        continue;

                    first = new double2[steps + 1];
                    for (var i = 0; i <= steps; i++)
                        first[i] = ring[(pointIndex + i) % count];
                    second = new double2[count - steps];
                    second[0] = point;
                    for (var i = steps + 1; i < count; i++)
                        second[i - steps] = ring[(pointIndex + i) % count];

                    // Ein tangentialer Kontakt kann einen Umlauf ohne Flaeche
                    // abtrennen. Der ist keine zweite Flaeche und darf einen
                    // spaeteren echten Kontakt nicht verdecken.
                    if (Math.Abs(SignedArea(first)) >= 1e-12
                        && Math.Abs(SignedArea(second)) >= 1e-12) return true;
                    first = null;
                    second = null;
                }
            return false;
        }

        /**
         * Ein Konturring darf zwei Teilflaechen in einem exakten Beruehrpunkt
         * verbinden. Beim Cast auf float kann der Punkt auf die falsche Seite
         * der nicht benachbarten Kante runden. Verschoben wird nur dann und nur
         * um den kleinsten getesteten Betrag, der nach dem Cast die urspruengliche
         * Seite erhaelt. Beim Weltkoordinaten-Regressionsfall genuegten 0,1 mm.
         */
        /**
         * Verwirft deckungsgleiche Nachbarknoten. NICHT dasselbe wie
         * RemoveNearDuplicatePoints: dort wird ein gemeinsamer Ersatzpunkt aus
         * der Shoelace-Gleichung bestimmt, hier faellt ein Knoten ersatzlos weg.
         *
         * Gebraucht wird das NACH der Beruehrungsteilung. Die setzt den
         * Kontaktpunkt neben einen bereits vorhandenen Knoten; liegen beide
         * praktisch aufeinander, bleibt eine entartete Kante stehen, an der
         * sich der Ring rechnerisch selbst schneidet. Gemessen an einem Viereck
         * mit float-gerundeten Ecken - also genau so, wie das Spiel die Punkte
         * liefert: zwei solche Ringe mit einer Kante von 1,1e-8 m Laenge. Der
         * Rueckfall ersetzte sie durch ihre Einzeltrapeze, aus 8 Fuellstuecken
         * wurden 146 und aus 20 Grasflaechen 137; die Halsreparatur rechnete
         * daran ueber eine Minute. In doppelter Genauigkeit trat der Fall nicht
         * auf, deshalb war er in allen Testfaellen unsichtbar.
         *
         * 1e-6 m ist die Rasterweite der Konturverfolgung und liegt zwei
         * Groessenordnungen unter der float-Aufloesung von rund 1,2e-4 m an
         * dieser Stelle der Karte. Ein so verworfener Knoten kann keine
         * sichtbare Grenze verschieben.
         */
        private static double2[] RemoveZeroLengthEdges(double2[] ring)
        {
            var output = new List<double2>(ring.Length);
            foreach (var point in ring)
            {
                if (output.Count > 0
                    && Len(output[output.Count - 1] - point) < 1e-6) continue;
                output.Add(point);
            }
            while (output.Count > 1
                   && Len(output[0] - output[output.Count - 1]) < 1e-6)
                output.RemoveAt(output.Count - 1);
            return output.ToArray();
        }

        private static double2[] StabilizeForFloat(double2[] ring)
        {
            double2[] Rounded(double2[] source) => source
                .Select(point => new double2((float)point.x, (float)point.y)).ToArray();
            double Side(double2 point, double2 a, double2 b) =>
                (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

            if (!SelfIntersects(Rounded(ring), 2e-6)) return ring;
            var output = ring.ToArray();
            for (var i = 0; i < output.Length; i++)
            {
                var point = output[i];
                for (var j = 0; j < output.Length; j++)
                {
                    if (j == i || (j + 1) % output.Length == i) continue;
                    var a = output[j];
                    var b = output[(j + 1) % output.Length];
                    var edge = b - a;
                    var squaredLength = edge.x * edge.x + edge.y * edge.y;
                    if (squaredLength < 1e-12) continue;
                    var t = ((point.x - a.x) * edge.x + (point.y - a.y) * edge.y)
                          / squaredLength;
                    if (t <= 1e-7 || t >= 1 - 1e-7) continue;
                    var projection = a + edge * t;
                    var delta = point - projection;
                    if (delta.x * delta.x + delta.y * delta.y > 1e-12) continue;

                    var beforeSide = Side(output[(i - 1 + output.Length) % output.Length], a, b);
                    var afterSide = Side(output[(i + 1) % output.Length], a, b);
                    if (Math.Abs(beforeSide) < 1e-9 || beforeSide * afterSide <= 0) continue;
                    var intendedSide = beforeSide > 0 ? 1 : -1;
                    var roundedPoint = new double2((float)point.x, (float)point.y);
                    var roundedA = new double2((float)a.x, (float)a.y);
                    var roundedB = new double2((float)b.x, (float)b.y);
                    if (Side(roundedPoint, roundedA, roundedB) * intendedSide > 0) continue;

                    var normal = new double2(-edge.y, edge.x) * (intendedSide / Math.Sqrt(squaredLength));
                    var offset = 1e-7;
                    for (var attempt = 0; attempt < 19; attempt++, offset *= 2)
                    {
                        var candidate = point + normal * offset;
                        var roundedCandidate = new double2((float)candidate.x, (float)candidate.y);
                        if (Side(roundedCandidate, roundedA, roundedB) * intendedSide <= 0) continue;
                        output[i] = candidate;
                        break;
                    }
                    break;
                }
            }
            return output;
        }

        /** Echte Kreuzung zweier nicht benachbarter Polygonkanten. */
        private static bool SelfIntersects(double2[] ring) => SelfIntersects(ring, 1e-9);

        private static bool SelfIntersects(double2[] ring, double endpointEpsilon)
        {
            for (var i = 0; i < ring.Length; i++)
                for (var j = i + 2; j < ring.Length; j++)
                {
                    if (i == 0 && j == ring.Length - 1) continue;
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Length];
                    var c = ring[j];
                    var d = ring[(j + 1) % ring.Length];
                    var denominator = (b.x - a.x) * (d.y - c.y)
                                    - (b.y - a.y) * (d.x - c.x);
                    if (Math.Abs(denominator) < 1e-12) continue;
                    var t = ((c.x - a.x) * (d.y - c.y)
                           - (c.y - a.y) * (d.x - c.x)) / denominator;
                    var u = ((c.x - a.x) * (b.y - a.y)
                           - (c.y - a.y) * (b.x - a.x)) / denominator;
                    if (t > endpointEpsilon && t < 1 - endpointEpsilon
                        && u > endpointEpsilon && u < 1 - endpointEpsilon) return true;
                }
            return false;
        }

        /**
         * EINEN GEFALTETEN RING IN ZWEI SAUBERE ZERLEGEN.
         *
         * Ansage des Nutzers am 2026-08-18: eine Flaeche darf sich NIE selbst
         * ueberschneiden, und wo eine Form eine echte Engstelle hat, sollen
         * dort ruhig ZWEI Flaechen entstehen. Bisher wurde eine Kreuzung nur
         * nachtraeglich erkannt und der Ring dann verworfen oder trotzdem
         * ausgegeben.
         *
         * Gemessen an der Zufallsform
         * -1184.6735,164.74837;-1196.5623,233.17378;-1295.4437,255.52925;
         * -1306.2715,167.20323;-1252.5776,140.68623: gleich nach dem
         * Asphaltabzug sind 5 von 401 Ringen gefaltet, und zwar OHNE
         * Doppelpunkte - es sind echte Kantenkreuzungen.
         *
         * An der Kreuzung liegt der Ring zweimal an derselben Stelle. Genau
         * dort wird getrennt: der Schnittpunkt kommt in beide Haelften, und
         * aus einer Acht werden zwei Schleifen. Was danach zu klein ist, faellt
         * weg - eine Schleife ohne Flaeche ist keine.
         */
        private static List<double2[]> EntfalteRing(double2[] ring, double minFlaeche)
        {
            var fertig = new List<double2[]>();
            var offen = new List<double2[]> { ring };
            // Jede Trennung erzeugt zwei kuerzere Ringe; die Schranke haelt
            // entartete Eingaben ab, die sich endlos weiterteilen.
            for (var runde = 0; offen.Count != 0 && runde < 256; runde++)
            {
                var aktuell = offen[offen.Count - 1];
                offen.RemoveAt(offen.Count - 1);
                if (aktuell.Length < 3) continue;
                if (!TrenneErsteKreuzung(aktuell, out var a, out var b))
                {
                    if (Math.Abs(SignedArea(aktuell)) >= minFlaeche) fertig.Add(aktuell);
                    continue;
                }
                offen.Add(a);
                offen.Add(b);
            }
            // Was die Schranke reisst, geht unveraendert hinaus - lieber ein
            // gefalteter Ring als eine verschwundene Flaeche.
            foreach (var rest in offen)
                if (Math.Abs(SignedArea(rest)) >= minFlaeche) fertig.Add(rest);
            return fertig;
        }

        /** Die erste Kantenkreuzung suchen und den Ring dort auftrennen. */
        private static bool TrenneErsteKreuzung(double2[] ring,
                                                out double2[] erste,
                                                out double2[] zweite)
        {
            erste = null;
            zweite = null;
            for (var i = 0; i < ring.Length; i++)
                for (var j = i + 2; j < ring.Length; j++)
                {
                    if (i == 0 && j == ring.Length - 1) continue;
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Length];
                    var c = ring[j];
                    var d = ring[(j + 1) % ring.Length];
                    var nenner = (b.x - a.x) * (d.y - c.y) - (b.y - a.y) * (d.x - c.x);
                    if (Math.Abs(nenner) < 1e-12) continue;
                    var t = ((c.x - a.x) * (d.y - c.y)
                           - (c.y - a.y) * (d.x - c.x)) / nenner;
                    var u = ((c.x - a.x) * (b.y - a.y)
                           - (c.y - a.y) * (b.x - a.x)) / nenner;
                    if (t <= 2e-6 || t >= 1 - 2e-6
                        || u <= 2e-6 || u >= 1 - 2e-6) continue;

                    var schnitt = new double2(a.x + (b.x - a.x) * t,
                                              a.y + (b.y - a.y) * t);
                    // Schleife 1: vom Schnittpunkt ueber i+1..j zurueck.
                    var kopf = new List<double2> { schnitt };
                    for (var k = i + 1; k <= j; k++) kopf.Add(ring[k % ring.Length]);
                    // Schleife 2: vom Schnittpunkt ueber j+1..i zurueck.
                    var rumpf = new List<double2> { schnitt };
                    for (var k = j + 1; k <= i + ring.Length; k++)
                        rumpf.Add(ring[k % ring.Length]);
                    if (kopf.Count < 3 || rumpf.Count < 3) continue;
                    erste = kopf.ToArray();
                    zweite = rumpf.ToArray();
                    return true;
                }
            return false;
        }

        /**
         * Kollineare Punkte verlustfrei entfernen. Ohne diesen Schritt blieben
         * im Browser-Fall alle 830 Kanten exakt 0,5 m lang.
         */
        private static double2[] MergeCollinear(double2[] ring, double epsilon)
        {
            var output = new List<double2>();
            for (var i = 0; i < ring.Length; i++)
            {
                var a = output.Count > 0 ? output[output.Count - 1]
                    : ring[(i - 1 + ring.Length) % ring.Length];
                var b = ring[i];
                var c = ring[(i + 1) % ring.Length];
                var ab = b - a;
                var bc = c - b;
                if (Math.Abs(ab.x * bc.y - ab.y * bc.x) <= epsilon
                    && ab.x * bc.x + ab.y * bc.y > 0) continue;
                output.Add(b);
            }
            return output.Count >= 3 ? output.ToArray() : ring;
        }
    }
}
