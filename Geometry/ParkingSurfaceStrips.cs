using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Zerlegt eine Flaeche in STREIFEN, damit die Vorschau sie fuellen kann.
     *
     * WARUM UEBERHAUPT: Der Overlay des Spiels kann kein Vieleck fuellen. Er
     * kennt genau eine Grundform - eine Kurve mit Breite. Was er aber kann:
     * diese Form aufs GELAENDE PROJIZIEREN. Ein breiter, projizierter Streifen
     * ist also eine gefuellte Flaeche, die jeder Steigung folgt.
     *
     * Ein Vieleck laesst sich damit fuellen, indem man es in parallele
     * Streifen schneidet - dasselbe Verfahren, mit dem eine Grafikkarte seit
     * jeher Dreiecke rastert.
     *
     * ZWEI WEGE, und der erste ist fast immer der richtige:
     *
     *   RECHTECK  -> EIN Streifen, exakt. Die meisten unserer Gruenflaechen
     *                sind Mittelstreifen und Kappen, also Rechtecke. 70 Ringe
     *                im Abzug, davon haben 4 bis 16 Punkte - der grosse Teil
     *                ist mit einem einzigen Streifen exakt getroffen.
     *
     *   SONST     -> Abtastzeilen. Das trifft vor allem den Asphalt: im Abzug
     *                nur 4 Ringe, aber mit bis zu 122 Punkten.
     *
     * Die Abtastrichtung ist die LAENGSTE KANTE des Vielecks. Bei unseren
     * langgezogenen Streifen und Fahrbahnen liegt sie damit laengs, und es
     * entstehen wenige lange statt vieler kurzer Streifen.
     */
    public static class ParkingSurfaceStrips
    {
        /**
         * Angestrebte Streifenbreite. Grob genug, dass ein Parkplatz mit
         * wenigen Dutzend Streifen auskommt, fein genug, dass eine schraege
         * Kante nicht als Treppe auffaellt.
         */
        public const double PreferredWidth = 4.0;

        /** Ab wann ein Viereck als Rechteck durchgeht. */
        private const double RectangleTolerance = 0.05;

        public readonly struct Strip
        {
            public Strip(double2 from, double2 to, double width)
            {
                From = from;
                To = to;
                Width = width;
            }

            /** Mittellinie des Streifens. */
            public double2 From { get; }
            public double2 To { get; }
            public double Width { get; }
        }

        public static Strip[] Fill(float2[][] polygons, double preferredWidth)
        {
            var strips = new List<Strip>();
            if (polygons == null) return Array.Empty<Strip>();
            foreach (var polygon in polygons) Fill(polygon, preferredWidth, strips);
            return strips.ToArray();
        }

        private static void Fill(float2[] polygon, double preferredWidth,
                                 List<Strip> strips)
        {
            if (polygon == null || polygon.Length < 3) return;
            var points = new double2[polygon.Length];
            for (var i = 0; i < polygon.Length; i++)
                points[i] = new double2(polygon[i].x, polygon[i].y);

            if (TryRectangle(points, strips)) return;

            // Abtastrichtung: die laengste Kante. Sie liegt bei unseren
            // Formen fast immer parallel zur Hauptausdehnung.
            var along = LongestEdgeDirection(points);
            var across = new double2(-along.y, along.x);

            double minAcross = double.MaxValue, maxAcross = double.MinValue;
            foreach (var point in points)
            {
                var value = math.dot(point, across);
                minAcross = Math.Min(minAcross, value);
                maxAcross = Math.Max(maxAcross, value);
            }
            var span = maxAcross - minAcross;
            if (span <= 1e-6) return;

            var count = Math.Max(1, (int)Math.Round(span / preferredWidth));
            var width = span / count;

            var crossings = new List<double>();
            for (var k = 0; k < count; k++)
            {
                var level = minAcross + width * (k + 0.5);
                ScanLine(points, across, along, level, crossings);
                // Paarweise: zwischen dem ersten und zweiten Schnitt liegt
                // Flaeche, zwischen dem zweiten und dritten nicht.
                for (var m = 0; m + 1 < crossings.Count; m += 2)
                {
                    var a = crossings[m];
                    var b = crossings[m + 1];
                    if (b - a <= 1e-6) continue;
                    strips.Add(new Strip(
                        along * a + across * level,
                        along * b + across * level,
                        width));
                }
            }
        }

        /**
         * Ein achsentreues Rechteck braucht keine Abtastung - ein Streifen
         * deckt es exakt. Geprueft wird ueber die Diagonalen: bei einem
         * Rechteck sind sie gleich lang und halbieren einander.
         */
        private static bool TryRectangle(double2[] points, List<Strip> strips)
        {
            if (points.Length != 4) return false;
            var diagonal1 = math.length(points[2] - points[0]);
            var diagonal2 = math.length(points[3] - points[1]);
            if (Math.Abs(diagonal1 - diagonal2) > RectangleTolerance) return false;
            var center1 = (points[0] + points[2]) * 0.5;
            var center2 = (points[1] + points[3]) * 0.5;
            if (math.length(center1 - center2) > RectangleTolerance) return false;

            var edge0 = math.length(points[1] - points[0]);
            var edge1 = math.length(points[2] - points[1]);
            if (edge0 < 1e-6 || edge1 < 1e-6) return false;

            // Die Mittellinie laeuft entlang der laengeren Seite, damit der
            // Streifen so kurz und so breit wie moeglich wird.
            if (edge0 >= edge1)
                strips.Add(new Strip((points[0] + points[3]) * 0.5,
                                     (points[1] + points[2]) * 0.5, edge1));
            else
                strips.Add(new Strip((points[0] + points[1]) * 0.5,
                                     (points[3] + points[2]) * 0.5, edge0));
            return true;
        }

        private static double2 LongestEdgeDirection(double2[] points)
        {
            var best = new double2(1, 0);
            var bestLength = 0.0;
            for (var i = 0; i < points.Length; i++)
            {
                var edge = points[(i + 1) % points.Length] - points[i];
                var length = math.length(edge);
                if (length <= bestLength) continue;
                bestLength = length;
                best = edge / length;
            }
            return best;
        }

        /**
         * Schnittpunkte einer Abtastzeile mit dem Rand, sortiert.
         *
         * Die Zeile liegt bei `level` auf der Querachse. Gezaehlt wird eine
         * Kante nur, wenn die Zeile sie ECHT kreuzt - Kanten, die genau auf
         * der Zeile liegen, wuerden sonst zwei Schnittpunkte an derselben
         * Stelle liefern und die Paarbildung verschieben. Deshalb der
         * halboffene Vergleich, wie bei jedem Rasterverfahren.
         */
        private static void ScanLine(double2[] points, double2 across,
                                     double2 along, double level,
                                     List<double> crossings)
        {
            crossings.Clear();
            for (var i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                var levelA = math.dot(a, across);
                var levelB = math.dot(b, across);
                if (levelA <= level == levelB <= level) continue;
                var t = (level - levelA) / (levelB - levelA);
                var point = a + (b - a) * t;
                crossings.Add(math.dot(point, along));
            }
            crossings.Sort();
        }
    }
}
