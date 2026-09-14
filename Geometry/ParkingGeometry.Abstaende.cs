using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Abstandsmessungen an fertigen Querstrassen.
     *
     * Diese Helfer lagen bis zum 2026-09-01 in
     * `ParkingGeometry.CrossRoutes` - also mitten im alten Rechenweg,
     * obwohl sie mit dem Bauen nichts zu tun haben: sie MESSEN nur, was
     * hinterher dasteht, und der Paritaetstest braucht sie unabhaengig
     * davon, wer gebaut hat. Beim Ausbau des alten Wegs waeren sie
     * mitgefallen; deshalb stehen sie jetzt dort, wo sie hingehoeren.
     */
    public static partial class ParkingGeometry
    {
        // Die beiden Erzeugerteile des Nutzerbaus unterschieden sich nur um
        // 0,003 Grad (93,324 / 93,327 Grad). Bis 1 Grad gelten ihre
        // Verbindungen deshalb als gleichgerichtet; rechtwinklige Aeste nicht.
        private static readonly double CrossParallelSin = Math.Sin(Math.PI / 180);
        /** Kleinster Abstand zweier gleichgerichteter logischer Verbindungen. */
        public static double CrossRouteSpacing(IEnumerable<float2[]> routes)
        {
            if (routes == null) return double.PositiveInfinity;
            var lines = new List<Line2>();
            foreach (var route in routes)
                if (route != null && route.Length >= 2)
                    lines.Add(new Line2(
                        new double2(route[0].x, route[0].y),
                        new double2(route[1].x, route[1].y)));
            return CrossRouteSpacing(lines);
        }

        private static double CrossRouteSpacing(IReadOnlyList<Line2> routes)
        {
            var distance = double.PositiveInfinity;
            for (var first = 0; first < routes.Count; first++)
                for (var second = first + 1; second < routes.Count; second++)
                    distance = Math.Min(distance,
                        RouteDistance(routes[first], routes[second]));
            return distance;
        }

        private static double RouteDistance(Line2 first, Line2 second)
        {
            var ab = first.B - first.A;
            var cd = second.B - second.A;
            var firstLength = Len(ab);
            var secondLength = Len(cd);
            if (firstLength < 0.5 || secondLength < 0.5)
                return double.PositiveInfinity;
            var firstDirection = ab / firstLength;
            var secondDirection = cd / secondLength;
            if (Math.Abs(firstDirection.x * secondDirection.y
                         - firstDirection.y * secondDirection.x) > CrossParallelSin)
                return double.PositiveInfinity;

            var denominator = ab.x * cd.y - ab.y * cd.x;
            if (Math.Abs(denominator) > 1e-12)
            {
                var ac = second.A - first.A;
                var firstT = (ac.x * cd.y - ac.y * cd.x) / denominator;
                var secondT = (ac.x * ab.y - ac.y * ab.x) / denominator;
                if (firstT >= 0 && firstT <= 1 && secondT >= 0 && secondT <= 1)
                    return 0;
            }

            return Math.Min(
                Math.Min(PointSegmentDistance(first.A, second.A, second.B),
                         PointSegmentDistance(first.B, second.A, second.B)),
                Math.Min(PointSegmentDistance(second.A, first.A, first.B),
                         PointSegmentDistance(second.B, first.A, first.B)));
        }

        private static double PointSegmentDistance(double2 point, double2 a, double2 b)
        {
            var vector = b - a;
            var lengthSquared = vector.x * vector.x + vector.y * vector.y;
            if (lengthSquared < 1e-12) return Len(point - a);
            var relative = point - a;
            var t = (relative.x * vector.x + relative.y * vector.y) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            return Len(point - a - vector * t);
        }
    }
}
