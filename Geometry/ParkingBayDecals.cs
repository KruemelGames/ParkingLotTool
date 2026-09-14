using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Wo die Stellplatz-Decals hingehoeren.
     *
     * Bewusst ohne ECS: die Platzierung ist reine Geometrie und wird damit
     * vom Paritaetstest miterfasst. Im Spiel setzt ParkingLotBayObjects nur
     * noch die hier berechneten Punkte.
     *
     * EIN AUFKLEBER JE BUCHT. Der erste Anlauf paarte je zwei Buchten, weil
     * die `ParkingLotDecal01` des Vanilla-Parkplatzes in ihrer Reihe 5,94 m
     * auseinanderstanden. Das war eine Fehldeutung: die Reihe hat Luecken
     * und enthaelt zusaetzlich `ParkingLotDecal04`, und aus dem Abzug waren
     * nur 8 von 24 Positionen sichtbar. Im Spiel nachgesehen fehlte darauf
     * jede zweite Markierung. Alle drei Aufkleber decken genau EINEN Platz.
     */
    public static class ParkingBayDecals
    {
        /**
         * Toleranz, ab der ein Viereck nicht mehr als Bucht durchgeht.
         *
         * Grosszuegig gegen das Gleitkommarauschen der float-Ecken (bei
         * Weltkoordinaten um 1300 m rund 1,2e-4 m), eng genug, dass die
         * 5,0 m breite Behindertenbucht nicht als normale 3,0-m-Bucht
         * durchrutscht.
         */
        private const double WidthTolerance = 0.25;

        public readonly struct BayFrame
        {
            public BayFrame(double2 center, double2 along, double2 depth)
            {
                Center = center;
                Along = along;
                Depth = depth;
            }

            /** Mitte der Bucht. */
            public double2 Center { get; }
            /** Laengs der Reihe, also in Richtung der Nachbarbucht. */
            public double2 Along { get; }
            /** Quer zur Reihe. Vorzeichen offen, die Fahrgasse entscheidet. */
            public double2 Depth { get; }
        }

        /**
         * Welcher Aufkleber an diese Stelle gehoert.
         *
         * Alle drei decken einen Platz, unterscheiden sich aber in der
         * Breite:
         * - `ParkingLotDecal01` und `ParkingLotElectricDecal01` sind genau
         *   so breit wie eine normale Bucht (Spur 2,9x5,9).
         * - `ParkingLotDisabledDecal01` ist gemessen 5,0 x 6,2 gross und
         *   traegt eine 4,7x5,9-Spur. Deshalb rechnet ParkingGeometry fuer
         *   diese Buchten mit DisabledWidth = 5,0.
         */
        public enum DecalKind
        {
            Normal,
            Disabled,
            Electric,
        }

        public sealed class DecalPlacement
        {
            public double2 Center { get; internal set; }
            /** Zeigt zur naechsten Fahrbahn. */
            public double2 Facing { get; internal set; }
            public DecalKind Kind { get; internal set; }
            public int Bay { get; internal set; }
        }

        /**
         * Anker einer Ladesaeule zwischen zwei Elektrobuchten.
         *
         * `Center` liegt auf der gemeinsamen hinteren Buchtenkante. Das ist
         * absichtlich nur der geometrische Bezugspunkt: Im Spiel sucht
         * `ParkingLotChargerPlacement.cs` von dort den kleinsten
         * kollisionsfreien Rueckversatz in den Gruenstreifen. Der Layoutkern
         * selbst bleibt dabei unveraendert.
         */
        public sealed class ChargerPlacement
        {
            public double2 Center { get; internal set; }
            /** Zeigt zur Fahrgasse, also zu den Autos hin. */
            public double2 Facing { get; internal set; }
            public int BayA { get; internal set; }
            public int BayB { get; internal set; }
        }

        public sealed class DecalPlan
        {
            public DecalPlacement[] Placements { get; internal set; }
                = Array.Empty<DecalPlacement>();
            public ChargerPlacement[] Chargers { get; internal set; }
                = Array.Empty<ChargerPlacement>();
            public int Normal { get; internal set; }
            public int Disabled { get; internal set; }
            public int Electric { get; internal set; }
            /** Vierecke, die zu keiner bekannten Buchtbreite passten. */
            public int Unrecognized { get; internal set; }
            public int Stalls => Placements.Length;
        }

        public static DecalPlan Plan(ParkingLayout layout, LayoutSettings settings)
        {
            if (layout?.Bay == null || settings == null) return new DecalPlan();

            var roads = CollectRoads(layout);
            var placements = new List<DecalPlacement>(layout.Bay.Length);
            var unrecognized = 0;
            for (var i = 0; i < layout.Bay.Length; i++)
            {
                var role = i < layout.BayRole.Length ? layout.BayRole[i] : BayRole.Normal;
                var width = role == BayRole.Disabled
                    ? ParkingGeometry.DisabledWidth : settings.Sw;
                if (!TryDescribeBay(layout.Bay[i], width, out var frame))
                {
                    unrecognized++;
                    continue;
                }

                placements.Add(new DecalPlacement
                {
                    Center = frame.Center,
                    Facing = OrientTowardsRoad(frame.Center, frame.Depth, roads),
                    Kind = KindFor(role),
                    Bay = i,
                });
            }

            return new DecalPlan
            {
                Placements = placements.ToArray(),
                Chargers = PlanChargers(layout, settings, placements),
                Normal = placements.Count(p => p.Kind == DecalKind.Normal),
                Disabled = placements.Count(p => p.Kind == DecalKind.Disabled),
                Electric = placements.Count(p => p.Kind == DecalKind.Electric),
                Unrecognized = unrecognized,
            };
        }

        /**
         * Eine Ladesaeule je Buchtenpaar.
         *
         * Welche zwei Buchten zusammengehoeren, kommt aus dem Layout
         * (`ElectricPair`) und wird hier NICHT aus der Lage erraten: liegen
         * vier Elektroplaetze am Stueck, sind (0,1)+(2,3) und (1,2)
         * geometrisch nicht zu unterscheiden.
         *
         * Die Blickrichtung wird von den beiden Aufklebern uebernommen, statt
         * sie neu zu bestimmen. So kann die Saeule gar nicht anders herum
         * stehen als die Buchten, die sie bedient.
         */
        private static ChargerPlacement[] PlanChargers(ParkingLayout layout,
            LayoutSettings settings, List<DecalPlacement> placements)
        {
            if (layout.ElectricPair == null || layout.ElectricPair.Length == 0)
                return Array.Empty<ChargerPlacement>();

            var byBay = new Dictionary<int, DecalPlacement>(placements.Count);
            foreach (var placement in placements) byBay[placement.Bay] = placement;

            var chargers = new List<ChargerPlacement>(layout.ElectricPair.Length);
            foreach (var pair in layout.ElectricPair)
            {
                if (!byBay.TryGetValue(pair.x, out var a)
                    || !byBay.TryGetValue(pair.y, out var b)) continue;
                var facing = a.Facing;
                if (math.lengthsq(facing) < 1e-12) continue;
                facing = math.normalize(facing);
                // Anker von der Mitte zwischen beiden Buchten nach HINTEN,
                // also gegen die Fahrgasse, auf die hintere Buchtenkante.
                // Den Ausstattungsversatz rechnet erst das Spiel mit den dort
                // wirklich geladenen Prefab-Boxen.
                var center = (a.Center + b.Center) * 0.5 - facing * (settings.Sl / 2);
                chargers.Add(new ChargerPlacement
                {
                    Center = center,
                    Facing = facing,
                    BayA = pair.x,
                    BayB = pair.y,
                });
            }
            return chargers.ToArray();
        }

        private static DecalKind KindFor(BayRole role)
        {
            switch (role)
            {
                case BayRole.Disabled: return DecalKind.Disabled;
                case BayRole.Electric: return DecalKind.Electric;
                default: return DecalKind.Normal;
            }
        }

        /**
         * Zerlegt das Buchtenviereck in seine beiden Achsen.
         *
         * Die Kante nahe der Buchtbreite gibt die Reihenrichtung. Verlassen
         * wird sich NICHT auf die Punktreihenfolge - die Buchten kommen aus
         * mehreren Erzeugern mit unterschiedlichem Umlaufsinn.
         */
        public static bool TryDescribeBay(float2[] quad, double stallWidth,
                                          out BayFrame frame)
        {
            frame = default;
            if (quad == null || quad.Length < 4) return false;

            var center = double2.zero;
            for (var i = 0; i < 4; i++) center += new double2(quad[i].x, quad[i].y);
            center *= 0.25;

            var bestScore = double.MaxValue;
            var along = double2.zero;
            for (var i = 0; i < 4; i++)
            {
                var a = new double2(quad[i].x, quad[i].y);
                var b = new double2(quad[(i + 1) % 4].x, quad[(i + 1) % 4].y);
                var edge = b - a;
                var length = math.length(edge);
                if (length < 1e-6) continue;
                var score = Math.Abs(length - stallWidth);
                if (score >= bestScore) continue;
                bestScore = score;
                along = edge / length;
            }
            if (bestScore > WidthTolerance) return false;

            frame = new BayFrame(center, along, new double2(-along.y, along.x));
            return true;
        }

        private static List<(double2 A, double2 B)> CollectRoads(ParkingLayout layout)
        {
            var roads = new List<(double2, double2)>();
            void Add(float2[][] lines)
            {
                if (lines == null) return;
                foreach (var line in lines)
                {
                    if (line == null || line.Length < 2) continue;
                    roads.Add((new double2(line[0].x, line[0].y),
                               new double2(line[1].x, line[1].y)));
                }
            }
            Add(layout.AisleLine);
            Add(layout.PerimeterLine);
            Add(layout.CrossLine);
            return roads;
        }

        /**
         * Wie parallel eine Fahrbahn zur Buchtenreihe liegen muss, damit sie
         * als die bediendende Gasse in Frage kommt. 30 Grad.
         */
        private static readonly double AlongRowCos = Math.Cos(30 * Math.PI / 180);

        /**
         * Dreht die Bucht zu der Fahrbahn, die ihre Reihe bedient.
         *
         * Das Viereck allein sagt nicht, auf welcher Seite die Fahrgasse
         * liegt - beide Tiefenrichtungen sehen gleich aus.
         *
         * Gezaehlt werden NUR Fahrbahnen, die PARALLEL zur Reihe laufen. Der
         * erste Anlauf nahm schlicht die naechstgelegene, und im Spiel stand
         * daraufhin eine einzelne Bucht am Reihenende um 180 Grad verdreht
         * zwischen lauter richtigen: dort war eine Querstrasse naeher als die
         * eigene Fahrgasse, und die laeuft quer zur Reihe. Eine Gasse, die
         * eine Reihe bedient, liegt immer laengs zu ihr.
         *
         * Ob das Prefab dann mit +Z oder -Z dorthin zeigt, entscheidet
         * BayDecalFacesAisle in ParkingLotBayObjects.
         */
        public static double2 OrientTowardsRoad(double2 center, double2 depth,
                                                List<(double2 A, double2 B)> roads)
        {
            if (roads == null || roads.Count == 0) return depth;

            // depth steht senkrecht auf der Reihe, also ist das hier die
            // Reihenachse.
            var along = new double2(-depth.y, depth.x);
            var target = center + depth;
            if (!TryNearestRoad(center, roads, along, out target)
                // Notnagel: findet sich keine parallele Fahrbahn, ist die
                // naechstgelegene immer noch besser als gar keine Drehung.
                && !TryNearestRoad(center, roads, double2.zero, out target))
                return depth;

            var towards = target - center;
            if (math.lengthsq(towards) < 1e-12) return depth;
            return math.dot(towards, depth) >= 0 ? depth : -depth;
        }

        private static bool TryNearestRoad(double2 center,
                                           List<(double2 A, double2 B)> roads,
                                           double2 along, out double2 target)
        {
            var requireParallel = math.lengthsq(along) > 1e-12;
            var best = double.MaxValue;
            target = default;
            foreach (var road in roads)
            {
                var edge = road.B - road.A;
                var lengthSquared = math.lengthsq(edge);
                if (lengthSquared < 1e-12) continue;
                if (requireParallel
                    && Math.Abs(math.dot(edge / math.sqrt(lengthSquared), along))
                        < AlongRowCos)
                    continue;

                var t = math.clamp(math.dot(center - road.A, edge) / lengthSquared, 0, 1);
                var foot = road.A + edge * t;
                var distance = math.distancesq(center, foot);
                if (distance >= best) continue;
                best = distance;
                target = foot;
            }
            return best < double.MaxValue;
        }
    }
}
