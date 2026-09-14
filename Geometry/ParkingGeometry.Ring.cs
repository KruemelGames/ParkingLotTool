using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /** Abstand eines Punktes zur Strecke a-b. */
        private static double DistToSeg(double2 p, double2 a, double2 b)
        {
            var ab = b - a;
            var l2 = ab.x * ab.x + ab.y * ab.y;
            if (l2 < 1e-12) return Len(p - a);
            var t = ((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / l2;
            t = Math.Max(0, Math.Min(1, t));
            return Len(p - (a + ab * t));
        }

        /**
         * Seitlicher Abstand nur, wenn der Lotpunkt wirklich auf dem Segment liegt.
         * An der konkaven Ecke ist eine fluchtende Linie die FORTSETZUNG; der Abstand
         * zu ihrem Endpunkt liess sie faelschlich wie eine Nebenstrasse aussehen.
         */
        private static double SideGap(double2 p, double2 a, double2 b)
        {
            var ab = b - a;
            var l2 = ab.x * ab.x + ab.y * ab.y;
            if (l2 < 1e-12) return double.PositiveInfinity;
            var t = ((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / l2;
            if (t < 0 || t > 1) return double.PositiveInfinity;
            return Math.Abs((p.x - a.x) * ab.y - (p.y - a.y) * ab.x) / Math.Sqrt(l2);
        }

        private static double2? LineIntersect(double2 p1, double2 d1, double2 p2, double2 d2)
        {
            var denominator = d1.x * d2.y - d1.y * d2.x;
            if (Math.Abs(denominator) < 1e-9) return null;
            var delta = p2 - p1;
            var t = (delta.x * d2.y - delta.y * d2.x) / denominator;
            return p1 + d1 * t;
        }

        /**
         * Echter Offset-Ring mit Gehrung und Zuordnung der Punkte zu Arealecken.
         * Frueher schossen einzelne Kantenrechtecke an Ecken uebereinander hinaus.
         */
        private static RingParts OffsetRingParts(double2[] site, double distance)
        {
            var ccw = SignedArea(site) > 0;
            var count = site.Length;
            var lines = new List<OffsetLine>();
            for (var i = 0; i < count; i++)
            {
                var a = site[i];
                var b = site[(i + 1) % count];
                var direction = Norm(b - a);
                var normal = ccw
                    ? new double2(-direction.y, direction.x)
                    : new double2(direction.y, -direction.x);
                lines.Add(new OffsetLine { P = a + normal * distance, Direction = direction, Normal = normal });
            }

            var output = new List<double2>();
            var first = new int[count];
            var last = new int[count];
            for (var i = 0; i < count; i++)
            {
                first[i] = output.Count;
                var previous = lines[(i - 1 + count) % count];
                var current = lines[i];
                var hit = LineIntersect(previous.P, previous.Direction, current.P, current.Direction);
                // An einer spitzen Kerbe lag die Gehrung gemessen 9,8 m AUSSERHALB
                // und 32 m Ringlaenge liefen daneben. Der alte Deckel 6*d (68 m)
                // griff viel zu spaet.
                var good = hit.HasValue && PointIn(hit.Value, site)
                    && DistToBoundary(hit.Value, site) >= distance - 0.05
                    && Len(hit.Value - site[i]) < distance * 3;
                if (good)
                {
                    output.Add(hit.Value);
                    last[i] = output.Count - 1;
                    continue;
                }

                // Der korrekte Versatz ist ein Kreisbogen. Eine Sehne kam der Kante
                // gemessen auf 6,6 m statt 11,4 m nahe. 20 Grad Schrittweite haelt
                // den Stich unter 0,2 m.
                var corner = site[i];
                var a1 = Math.Atan2(previous.Normal.y, previous.Normal.x);
                var a2 = Math.Atan2(current.Normal.y, current.Normal.x);
                var deltaAngle = a2 - a1;
                while (deltaAngle <= -Math.PI) deltaAngle += 2 * Math.PI;
                while (deltaAngle > Math.PI) deltaAngle -= 2 * Math.PI;
                double2 Middle(double delta) => new double2(
                    corner.x + distance * Math.Cos(a1 + delta / 2),
                    corner.y + distance * Math.Sin(a1 + delta / 2));
                if (!PointIn(Middle(deltaAngle), site))
                    deltaAngle += deltaAngle > 0 ? -2 * Math.PI : 2 * Math.PI;
                var steps = Math.Max(1,
                    (int)Math.Ceiling(Math.Abs(deltaAngle) / (20 * Math.PI / 180)));
                for (var step = 0; step <= steps; step++)
                {
                    var angle = a1 + deltaAngle * step / steps;
                    output.Add(new double2(corner.x + distance * Math.Cos(angle),
                                           corner.y + distance * Math.Sin(angle)));
                }
                last[i] = output.Count - 1;
            }
            return new RingParts { Points = output.ToArray(), First = first, Last = last };
        }

        private sealed class OffsetLine
        {
            internal double2 P;
            internal double2 Direction;
            internal double2 Normal;
        }

        private static double2[] OffsetRing(double2[] site, double distance) =>
            OffsetRingParts(site, distance).Points;

        /**
         * Halbebenenschnitt eines Segments mit einem konvexen Viereck. Der
         * rangniedrigere Weg endet an der KANTE; dort zaehlt der Zentimeter.
         */
        private static double[] SegInConvex(double2 a, double2 direction, double length,
                                            double2[] quad)
        {
            var winding = SignedArea(quad) > 0 ? 1 : -1;
            var t0 = 0.0;
            var t1 = length;
            for (var i = 0; i < quad.Length; i++)
            {
                var p0 = quad[i];
                var edge = quad[(i + 1) % quad.Length] - p0;
                var nx = -edge.y * winding;
                var ny = edge.x * winding;
                var denominator = direction.x * nx + direction.y * ny;
                var numerator = (a.x - p0.x) * nx + (a.y - p0.y) * ny;
                if (Math.Abs(denominator) < 1e-12)
                {
                    if (numerator < 0) return null;
                    continue;
                }
                var t = -numerator / denominator;
                if (denominator > 0) t0 = Math.Max(t0, t);
                else t1 = Math.Min(t1, t);
                if (t0 > t1) return null;
            }
            return new[] { Math.Max(0, t0), Math.Min(length, t1) };
        }

        private static List<Crossing> LineCrossings(double2 origin, double2 direction,
                                                    double2[] polygon)
        {
            var output = new List<Crossing>();
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                var edge = b - a;
                var denominator = direction.x * edge.y - direction.y * edge.x;
                if (Math.Abs(denominator) < 1e-9) continue;
                var delta = a - origin;
                var u = (delta.x * direction.y - delta.y * direction.x) / denominator;
                if (u < -1e-9 || u > 1 + 1e-9) continue;
                // Eine halbe Fahrbahnbreite quer entspricht laengs h/dn. Sehr
                // flache Schnitte werden gedeckelt; DnRaw bleibt fuer die Regel.
                var edgeNormal = Norm(new double2(-edge.y, edge.x));
                var dn = Math.Abs(direction.x * edgeNormal.x + direction.y * edgeNormal.y);
                output.Add(new Crossing
                {
                    T = (delta.x * edge.y - delta.y * edge.x) / denominator,
                    Dn = Math.Max(dn, 0.25),
                    DnRaw = dn,
                });
            }
            output.Sort((x, y) => x.T.CompareTo(y.T));
            var unique = new List<Crossing>();
            foreach (var crossing in output)
                if (unique.Count == 0 || crossing.T - unique[unique.Count - 1].T > 1e-6)
                    unique.Add(crossing);
            return unique;
        }

        /**
         * Schneidet auf das laengste zusammenhaengende Stueck im Areal.
         * Bei einer konkaven Form zog erster-bis-letzter die Gasse gemessen 6,8 m
         * ausserhalb und 28,8 m auf die Randstrasse.
         */
        private static Line2 ClipSegment(double2 a, double2 b, double2[] site, double clearance)
        {
            var length = Len(b - a);
            if (length < 0.5) return null;
            var direction = (b - a) / length;
            double? lo = null, hi = null, currentLo = null, currentHi = null;
            for (var t = 0.0; t <= length + 1e-9; t += 0.25)
            {
                if (InsideBy(a + direction * t, site, clearance))
                {
                    if (!currentLo.HasValue) currentLo = t;
                    currentHi = t;
                    continue;
                }
                if (currentLo.HasValue && (!lo.HasValue || currentHi - currentLo > hi - lo))
                {
                    lo = currentLo;
                    hi = currentHi;
                }
                currentLo = null;
            }
            if (currentLo.HasValue && (!lo.HasValue || currentHi - currentLo > hi - lo))
            {
                lo = currentLo;
                hi = currentHi;
            }
            if (!lo.HasValue || hi - lo < 0.5) return null;

            // Die Abtastung lag an drei Gassen 0,03 / 0,09 / 0,23 m vom Rand.
            // Deshalb nur die Enden per Intervallhalbierung auf <0,1 mm schaerfen.
            bool Inside(double t) => InsideBy(a + direction * t, site, clearance);
            double Sharpen(double inside, double outside)
            {
                if (outside < 0 || outside > length) return inside;
                for (var k = 0; k < 40; k++)
                {
                    var middle = (inside + outside) / 2;
                    if (Inside(middle)) inside = middle;
                    else outside = middle;
                }
                return inside;
            }
            var exactLo = Sharpen(lo.Value, lo.Value - 0.25);
            var exactHi = Sharpen(hi.Value, Math.Min(hi.Value + 0.25, length));
            if (exactHi - exactLo < 0.5) return null;
            return new Line2(a + direction * exactLo, a + direction * exactHi);
        }

        private static bool IsReflex(double2[] polygon, int i)
        {
            var count = polygon.Length;
            var a = polygon[(i - 1 + count) % count];
            var b = polygon[i];
            var c = polygon[(i + 1) % count];
            var cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            return SignedArea(polygon) > 0 ? cross < 0 : cross > 0;
        }

        private static bool EntranceCornerCanSnap(double2[] polygon, int edge, bool atStart)
        {
            var count = polygon.Length;
            var corner = atStart ? edge : (edge + 1) % count;
            if (IsReflex(polygon, corner)) return false;
            var u = Norm(polygon[(edge + 1) % count] - polygon[edge]);
            var adjacent = atStart
                ? Norm(polygon[edge] - polygon[(edge - 1 + count) % count])
                : Norm(polygon[(edge + 2) % count] - polygon[(edge + 1) % count]);
            var normal = new double2(-u.y, u.x);
            // Nicht den rechten Winkel verlangen: Schraeg hat 71,6 Grad und fiel
            // mit der alten 5-Grad-Sperre durch, obwohl die Ecke konvex ist.
            return Math.Abs(adjacent.x * normal.x + adjacent.y * normal.y)
                >= MinCornerSnapSin;
        }

        /**
         * Zufahrtsflaeche. An der schraegen Ecke ist sie ein PARALLELOGRAMM;
         * als Rechteck blieben gemessen 8,7 m2 bzw. 8,2 % als Keile frei.
         */
        private static double2[] EntranceQuadOf(double2 start, double2 direction, double length,
                                                double2 edgeU, double projection, double width)
        {
            var end = start + direction * length;
            var edge = projection != 0 && projection < 1 - 1e-9
                ? edgeU * (width / 2 / projection)
                : new double2(-direction.y, direction.x) * (width / 2);
            return new[] { start + edge, end + edge, end - edge, start - edge };
        }

        private static EntranceFit EntranceCornerFit(double2[] site, LayoutSettings s,
                                                     int edgeIndex, bool atStart)
        {
            if (site == null || site.Length < 3 || !EntranceCornerCanSnap(site, edgeIndex, atStart))
                return null;
            var count = site.Length;
            var a = site[edgeIndex];
            var b = site[(edgeIndex + 1) % count];
            var vector = b - a;
            var length = Len(vector);
            if (length < FitEps) return null;
            var u = vector / length;
            var winding = SignedArea(site) >= 0 ? 1 : -1;
            var ringDistance = s.Es + s.Sl + s.Ai / 2;
            var corner = atStart ? a : b;
            var adjacent = atStart
                ? Norm(site[edgeIndex] - site[(edgeIndex - 1 + count) % count])
                : Norm(site[(edgeIndex + 2) % count] - site[(edgeIndex + 1) % count]);
            var direction = atStart ? -adjacent : adjacent;
            var adjacentNormal = new double2(-adjacent.y, adjacent.x) * winding;
            var normal = new double2(-u.y, u.x) * winding;
            var projection = Math.Abs(direction.x * normal.x + direction.y * normal.y);
            if (projection <= 0.3) return null;
            var hit = LineIntersect(corner + adjacentNormal * ringDistance, direction, a, u);
            if (!hit.HasValue) return null;
            var along = (hit.Value.x - a.x) * u.x + (hit.Value.y - a.y) * u.y;
            if (double.IsNaN(along) || double.IsInfinity(along)
                || along < -s.Ai || along > length + s.Ai) return null;
            return new EntranceFit
            {
                Along = along,
                Direction = direction,
                Projection = projection,
                Length = (ringDistance - s.Ai / 2) / projection,
            };
        }


        /**
         * Die Teilflaechen eines Umrisses - allein aus der FORM.
         *
         * Genau das braucht die Zuweisung je Teilflaeche: der Nutzer waehlt
         * eine Flaeche, BEVOR es einen Winkel gibt. Die Zerlegung darf also
         * nicht vom Raster abhaengen, sondern nur von den einspringenden
         * Ecken - ein Rechteck, Dreieck oder eine Raute bleibt ein Stueck,
         * ein L wird zwei.
         *
         * Der Zellen-Rechenweg zerlegt heute ANDERSHERUM: erst dreht er den
         * ganzen Umriss in die Reihenrichtung, dann schneidet er. Seine Teile
         * sind damit ein Ergebnis des Winkels und taugen nicht als Auswahl.
         * Diese Zerlegung hier ist der gemeinsame Nenner fuer beide.
         */
        internal static double2[][] Teilflaechen(double2[] site)
            => site == null || site.Length < 3
                ? new[] { site ?? Array.Empty<double2>() }
                : Decompose(site).ToArray();

        /** Zerlegt an konkaven Ecken; ohne das blieb der zweite L-Schenkel leer. */
        private static List<double2[]> Decompose(double2[] polygon, int depth = 0)
        {
            if (depth > 4 || polygon.Length < 4) return new List<double2[]> { polygon };
            var count = polygon.Length;
            for (var i = 0; i < count; i++)
            {
                if (!IsReflex(polygon, i)) continue;
                var a = polygon[(i - 1 + count) % count];
                var b = polygon[i];
                var direction = Norm(b - a);
                var bestT = double.PositiveInfinity;
                var bestJ = -1;
                var bestPoint = default(double2);
                for (var j = 0; j < count; j++)
                {
                    if (j == i || (j + 1) % count == i) continue;
                    var q1 = polygon[j];
                    var q2 = polygon[(j + 1) % count];
                    var length = Len(q2 - q1);
                    if (length < 0.001) continue;
                    var hit = LineIntersect(b, direction, q1, (q2 - q1) / length);
                    if (!hit.HasValue) continue;
                    var t = (hit.Value.x - b.x) * direction.x
                          + (hit.Value.y - b.y) * direction.y;
                    if (t < 0.5 || t >= bestT) continue;
                    var u = ((hit.Value.x - q1.x) * (q2.x - q1.x)
                           + (hit.Value.y - q1.y) * (q2.y - q1.y)) / (length * length);
                    if (u < 0 || u > 1) continue;
                    bestT = t;
                    bestJ = j;
                    bestPoint = hit.Value;
                }
                if (bestJ < 0) continue;
                var first = new List<double2>();
                var second = new List<double2>();
                for (var k = i; ; k = (k + 1) % count)
                {
                    first.Add(polygon[k]);
                    if (k == bestJ) break;
                }
                first.Add(bestPoint);
                second.Add(bestPoint);
                for (var k = (bestJ + 1) % count; ; k = (k + 1) % count)
                {
                    second.Add(polygon[k]);
                    if (k == i) break;
                }
                if (first.Count < 3 || second.Count < 3) continue;
                var result = Decompose(first.ToArray(), depth + 1);
                result.AddRange(Decompose(second.ToArray(), depth + 1));
                return result;
            }
            return new List<double2[]> { polygon };
        }

        private static int JsRound(double value) => (int)Math.Floor(value + 0.5);

        /**
         * Richtung der laengsten Kante, in Grad, 0..180.
         *
         * NICHT auf ganze Grad runden. Das drehte den gemeldeten Nutzerfall
         * von 93,326 auf 93 Grad; Reihen und Polygonkante liefen dadurch
         * auseinander, fuenf markierte Stellen lagen auf 5,86-5,92 m langen
         * Kappen statt auf Buchten, und es entstanden 402 statt 408 Buchten.
         * Die Kante selbst gibt die genauere Phase vor.
         */
        /**
         * DER WIRKSAME REIHENWINKEL - AN GENAU EINER STELLE.
         *
         * Bis zum 2026-09-01 leitete JEDER Rechenweg ihn selbst ab, und die
         * beiden waren sich nicht einig: der alte rechnete seit kurzem ab der
         * laengsten Kante, der Zellenweg nahm bei allem ausser `edge` den
         * absoluten Reglerwert. Folge im Spiel: von "Kante" auf "Fest" sprang
         * der Parkplatz um 90 Grad, "Quer" drehte gar nicht, und die gewaehlte
         * Bezugslinie wurde ueberhaupt nicht gelesen. Drei Meldungen, eine
         * Ursache - dieselbe Ableitung zweimal geschrieben.
         *
         * Jetzt gibt es sie einmal, und beide Wege fragen hier:
         *
         *     Bezug  = gewaehlte Linie, sonst laengste Kante
         *     normal = Bezug
         *     quer   = Bezug + 90
         *     fest   = Bezug + Reglerwert
         *
         * `fest` schliesst eine gewaehlte Linie aus (die Oberflaeche verwirft
         * sie beim Umschalten), sein Bezug ist also immer die laengste Kante.
         * Damit liefert der Regler auf 0 genau das, was "Kante" liefert - was
         * der Nutzer zu Recht erwartet hatte.
         */
        internal static double Reihenwinkel(LayoutSettings s, double laengsteKante)
        {
            var bezug = s.Ausrichtwinkel ?? laengsteKante;
            var zusatz =
                string.Equals(s.AngleMode, "quer", StringComparison.Ordinal) ? 90.0
                : string.Equals(s.AngleMode, "fixed", StringComparison.Ordinal) ? s.Angle
                : 0.0;
            return bezug + zusatz + s.KantenVersatz;
        }

        /** Die laengste Kante eines Umrisses in Grad - oeffentlich, weil auch
            der Zellenweg den Bezug braucht. */
        internal static double LaengsteKante(double2[] site)
            => LongestEdgeAngle(site);

        private static double LongestEdgeAngle(double2[] site)
        {
            /*
             * DERSELBE LAUFMESSER WIE IM ZELLENWEG.
             *
             * Hier stand dieselbe Rechnung wie in `Geometrie.Reihenrahmen` -
             * die laengste gespeicherte Kante - und damit auch derselbe
             * Fehler: ein Punkt auf der Geraden zerlegt die laengste Kante,
             * und der Bezugswinkel springt. Zwei Messgeraete fuer dieselbe
             * Groesse gehen frueher oder spaeter auseinander; deshalb fragen
             * jetzt beide Wege dieselbe Funktion. Die Begruendung fuer die
             * 5-cm-Toleranz steht dort.
             */
            if (site.Length == 0) return 0.0;
            var punkte = new Zellen.Punkt[site.Length];
            for (var i = 0; i < site.Length; i++)
                punkte[i] = new Zellen.Punkt(site[i].x, site[i].y);
            var bestDegrees = Zellen.Geometrie.LaengsteGerade(punkte).Grad;
            // VERSUCHT UND VERWORFEN: den Winkel um 0,002 Grad neben die Kante
            // zu legen. Am Fuenfeck mit 16.430 m2 half es (2208 -> 20
            // Fuellteile), aber der Schwarm zeigte sofort den Preis: Form 0,
            // vorher in 324 ms fertig, blieb danach haengen - und zwar bei nur
            // 63 Grasteilen, also OHNE Zersplitterung. Der Versatz verschiebt
            // die Entartung nur auf andere Formen.
            //
            // Die Lehre: die Zersplitterung macht den Haenger wahrscheinlicher,
            // ist aber nicht seine Bedingung. Deshalb wird stattdessen die
            // Materialreparatur zeitlich begrenzt - siehe BuildMaterialSurfaces.
            return bestDegrees;
        }
    }
}
