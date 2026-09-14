using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Fasst nebeneinanderliegende Buchten zu STREIFEN zusammen.
     *
     * WOZU: Die Vorschau im Spiel kann keine Vielecke fuellen. Der
     * `OverlayRenderSystem.Buffer` kennt nur Kreise, Linien, Kurven und ein
     * paar feste Fertigformen - kein Fuellen eines Umrisses. Bisher wurde
     * deshalb JEDE Bucht als eine breite Linie gezeichnet, und bei 218
     * Buchten sieht ein Parkplatz dann aus wie ein Kachelmuster statt wie
     * eine Flaeche.
     *
     * Eine Reihe aus 18 Buchten ist aber geometrisch dasselbe wie EIN
     * Rechteck von 18 Buchtbreiten Laenge. Genau das entsteht hier: aus 218
     * Vierecken werden gut ein Dutzend Streifen. Weniger zu zeichnen, und es
     * sieht endlich nach zusammenhaengendem Belag aus.
     *
     * DIE FLAECHE MUSS DABEI EXAKT DIESELBE BLEIBEN - sonst zeigt die
     * Vorschau nicht mehr, was gebaut wird. Der Paritaetstest rechnet das
     * nach: Streifenflaeche gegen Buchtenflaeche.
     *
     * Getrennt wird an drei Stellen:
     *   - Reihenwechsel (andere Achse oder anderer Querversatz)
     *   - Luecke (Nachbarabstand ungleich Buchtbreite)
     *   - Rollenwechsel (normal / behindert / elektro haben eigene Farben)
     */
    public static class ParkingBayRuns
    {
        /** Wie genau zwei Nachbarn beieinanderstehen muessen. */
        private const double GapTolerance = 0.05;

        public sealed class Run
        {
            /** Das zusammengefasste Rechteck, im selben Umlaufsinn wie eine Bucht. */
            public float2[] Quad { get; internal set; } = Array.Empty<float2>();
            public BayRole Role { get; internal set; }
            /** Wie viele Buchten in diesem Streifen stecken. */
            public int Bays { get; internal set; }
        }

        private sealed class Item
        {
            internal int Index;
            internal double2 Center;
            internal double2 Along;
            internal double2 Across;
            internal double Width;
            internal double Depth;
            internal BayRole Role;
            internal double T;
        }

        public static Run[] Merge(ParkingLayout layout, LayoutSettings settings)
        {
            if (layout?.Bay == null || layout.Bay.Length == 0 || settings == null)
                return Array.Empty<Run>();

            var groups = new Dictionary<string, List<Item>>();
            for (var i = 0; i < layout.Bay.Length; i++)
            {
                var role = layout.BayRole != null && i < layout.BayRole.Length
                    ? layout.BayRole[i] : BayRole.Normal;
                var item = Describe(layout.Bay[i], role, settings);
                if (item == null) continue;
                item.Index = i;

                // Die Rolle gehoert in den Schluessel: ein Elektropaar mitten
                // in einer normalen Reihe muss ein eigener Streifen werden,
                // sonst verschwindet seine Farbe.
                var key = role + "|"
                    + Math.Atan2(item.Along.y, item.Along.x)
                        .ToString("F4", CultureInfo.InvariantCulture) + "|"
                    + math.dot(item.Center, item.Across)
                        .ToString("F2", CultureInfo.InvariantCulture) + "|"
                    + item.Depth.ToString("F2", CultureInfo.InvariantCulture);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<Item>();
                list.Add(item);
            }

            var runs = new List<Run>();
            foreach (var group in groups.Values)
            {
                group.Sort((a, b) => a.T.CompareTo(b.T));
                var start = 0;
                for (var k = 1; k <= group.Count; k++)
                {
                    var split = k == group.Count
                        || Math.Abs(group[k].T - group[k - 1].T - group[k].Width)
                            > GapTolerance
                        // Unterschiedlich breite Nachbarn gehoeren nicht in
                        // denselben Streifen; die Behindertenbucht ist 5,0 m
                        // breit, eine normale 3,0 m.
                        || Math.Abs(group[k].Width - group[k - 1].Width) > GapTolerance;
                    if (!split) continue;
                    runs.Add(Build(group, start, k));
                    start = k;
                }
            }
            return runs.ToArray();
        }

        /**
         * Zerlegt ein Buchtenviereck in Mitte, Achsen und die TATSAECHLICH
         * gemessenen Kantenlaengen.
         *
         * Gemessen statt aus den Einstellungen genommen: die Behindertengruppe
         * rechnet ihre Breite aus dem verfuegbaren Raster (`group.Width`) und
         * trifft die nominellen 5,0 m nicht immer auf den Millimeter.
         */
        private static Item Describe(float2[] quad, BayRole role,
                                     LayoutSettings settings)
        {
            var nominal = role == BayRole.Disabled
                ? ParkingGeometry.DisabledWidth : settings.Sw;
            if (!ParkingBayDecals.TryDescribeBay(quad, nominal, out var frame))
                return null;

            // Achse kanonisch drehen, sonst landen dieselbe Reihe von links
            // und von rechts gelesen in zwei verschiedenen Gruppen.
            var along = frame.Along;
            if (along.x < -1e-9 || (Math.Abs(along.x) <= 1e-9 && along.y < 0))
                along = -along;
            var across = new double2(-along.y, along.x);

            double minAlong = double.MaxValue, maxAlong = double.MinValue;
            double minAcross = double.MaxValue, maxAcross = double.MinValue;
            for (var c = 0; c < 4; c++)
            {
                var delta = new double2(quad[c].x, quad[c].y) - frame.Center;
                var a = math.dot(delta, along);
                var d = math.dot(delta, across);
                minAlong = Math.Min(minAlong, a);
                maxAlong = Math.Max(maxAlong, a);
                minAcross = Math.Min(minAcross, d);
                maxAcross = Math.Max(maxAcross, d);
            }

            return new Item
            {
                Center = frame.Center,
                Along = along,
                Across = across,
                Width = maxAlong - minAlong,
                Depth = maxAcross - minAcross,
                Role = role,
                T = math.dot(frame.Center, along),
            };
        }

        private static Run Build(List<Item> group, int start, int end)
        {
            var first = group[start];
            var last = group[end - 1];
            var from = first.T - first.Width / 2;
            var to = last.T + last.Width / 2;
            var half = first.Depth / 2;
            // Der Querversatz ist in der ganzen Gruppe gleich - er steht im
            // Schluessel. Deshalb reicht der der ersten Bucht.
            var offset = math.dot(first.Center, first.Across);

            float2 Corner(double alongValue, double acrossValue)
            {
                var point = first.Along * alongValue + first.Across * acrossValue;
                return new float2((float)point.x, (float)point.y);
            }

            return new Run
            {
                Quad = new[]
                {
                    Corner(from, offset - half),
                    Corner(to, offset - half),
                    Corner(to, offset + half),
                    Corner(from, offset + half),
                },
                Role = first.Role,
                Bays = end - start,
            };
        }
    }
}
