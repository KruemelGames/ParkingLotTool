using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Messwerte zweier konvexer Grundrisse.
     *
     * Das ist bewusst nur das MESSGERAET. Ob zwei CS2-Objekte kollidieren,
     * entscheidet weiterhin deren Kollisionsmaske und der Spiel-nahe Test im
     * Werkzeugsystem. Erst fuer ein dort bestaetigtes Paar wird hier
     * ausgerechnet, wie gross seine Ueberdeckung ist und wie tief die beiden
     * Grundrisse ineinander stehen.
     */
    public readonly struct ParkingOverlapMeasure
    {
        public readonly double Area;
        public readonly double Penetration;
        public readonly double2 Center;

        public ParkingOverlapMeasure(double area, double penetration,
                                     double2 center)
        {
            Area = area;
            Penetration = penetration;
            Center = center;
        }
    }

    public static class ParkingOverlapGeometry
    {
        private const double Epsilon = 1e-9;

        /**
         * Schneidet zwei konvexe Polygone exakt an ihren Kanten.
         *
         * Die Objektgrundrisse aus `ObjectUtils.CalculateBaseCorners` sind
         * Vierecke. Sutherland-Hodgman reicht deshalb aus; keine Abtastung und
         * kein Raster koennen eine schmale Ueberdeckung uebersehen.
         */
        public static bool Measure(IReadOnlyList<double2> first,
                                   IReadOnlyList<double2> second,
                                   out ParkingOverlapMeasure measure)
        {
            measure = default;
            if (first == null || second == null
                || first.Count < 3 || second.Count < 3) return false;

            var clipped = new List<double2>(first.Count + second.Count);
            for (var i = 0; i < first.Count; i++) clipped.Add(first[i]);
            var winding = SignedArea(second) >= 0 ? 1.0 : -1.0;

            for (var edge = 0; edge < second.Count && clipped.Count > 0; edge++)
            {
                var a = second[edge];
                var b = second[(edge + 1) % second.Count];
                var input = clipped;
                clipped = new List<double2>(input.Count + 1);
                var previous = input[input.Count - 1];
                var previousInside = Inside(previous, a, b, winding);
                for (var i = 0; i < input.Count; i++)
                {
                    var current = input[i];
                    var currentInside = Inside(current, a, b, winding);
                    if (currentInside != previousInside
                        && TryLineIntersection(previous, current, a, b,
                            out var intersection))
                        clipped.Add(intersection);
                    if (currentInside) clipped.Add(current);
                    previous = current;
                    previousInside = currentInside;
                }
            }

            var area = Math.Abs(SignedArea(clipped));
            if (area <= Epsilon) return false;
            var center = PolygonCenter(clipped);
            var penetration = MinimumAxisOverlap(first, second);
            measure = new ParkingOverlapMeasure(area, penetration, center);
            return true;
        }

        private static bool Inside(double2 p, double2 a, double2 b,
                                   double winding)
            => winding * Cross(b - a, p - a) >= -Epsilon;

        private static bool TryLineIntersection(double2 a, double2 b,
                                                double2 c, double2 d,
                                                out double2 hit)
        {
            var ab = b - a;
            var cd = d - c;
            var denominator = Cross(ab, cd);
            if (Math.Abs(denominator) <= Epsilon)
            {
                hit = (a + b) * 0.5;
                return false;
            }
            var t = Cross(c - a, cd) / denominator;
            hit = a + ab * t;
            return true;
        }

        private static double SignedArea(IReadOnlyList<double2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return 0;
            var sum = 0.0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum * 0.5;
        }

        private static double2 PolygonCenter(IReadOnlyList<double2> polygon)
        {
            var twiceArea = 0.0;
            var weighted = double2.zero;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var cross = a.x * b.y - b.x * a.y;
                twiceArea += cross;
                weighted += (a + b) * cross;
            }
            if (Math.Abs(twiceArea) <= Epsilon)
            {
                var mean = double2.zero;
                for (var i = 0; i < polygon.Count; i++) mean += polygon[i];
                return mean / polygon.Count;
            }
            return weighted / (3.0 * twiceArea);
        }

        /** Kleinste positive SAT-Ueberdeckung ueber die Achsen beider Formen. */
        private static double MinimumAxisOverlap(IReadOnlyList<double2> first,
                                                 IReadOnlyList<double2> second)
        {
            var minimum = double.PositiveInfinity;
            MeasureAxes(first, first, second, ref minimum);
            MeasureAxes(second, first, second, ref minimum);
            return double.IsPositiveInfinity(minimum) ? 0 : Math.Max(0, minimum);
        }

        private static void MeasureAxes(IReadOnlyList<double2> axesFrom,
                                        IReadOnlyList<double2> first,
                                        IReadOnlyList<double2> second,
                                        ref double minimum)
        {
            for (var i = 0; i < axesFrom.Count; i++)
            {
                var edge = axesFrom[(i + 1) % axesFrom.Count] - axesFrom[i];
                var length = math.length(edge);
                if (length <= Epsilon) continue;
                var axis = new double2(-edge.y, edge.x) / length;
                Project(first, axis, out var firstMin, out var firstMax);
                Project(second, axis, out var secondMin, out var secondMax);
                minimum = Math.Min(minimum,
                    Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin));
            }
        }

        private static void Project(IReadOnlyList<double2> polygon, double2 axis,
                                    out double minimum, out double maximum)
        {
            minimum = maximum = math.dot(polygon[0], axis);
            for (var i = 1; i < polygon.Count; i++)
            {
                var value = math.dot(polygon[i], axis);
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }

        private static double Cross(double2 a, double2 b)
            => a.x * b.y - a.y * b.x;
    }
}
