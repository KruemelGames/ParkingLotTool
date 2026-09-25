using System;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Lage einer Haltestelle auf genau einem geplanten Zoning-Strassenkurs. */
    public struct BusStopPlacement
    {
        public float2 A;
        public float2 B;
        public float Along;
        public bool Left;

        public float2 Position => math.lerp(A, B, Along);
    }

    public static class BusStopSnap
    {
        public static float2 SignPosition(BusStopPlacement stop)
        {
            var direction = math.normalizesafe(stop.B - stop.A);
            return stop.Position + (stop.Left ? 1f : -1f)
                * new float2(-direction.y, direction.x) * 4f;
        }

        public static float2 TravelDirection(BusStopPlacement stop,
            bool leftHandTraffic)
        {
            var direction = math.normalizesafe(stop.B - stop.A);
            return stop.Left == leftHandTraffic ? direction : -direction;
        }

        public static bool IsDuplicate(BusStopPlacement a, BusStopPlacement b)
            => a.Left == b.Left
                && math.distancesq(a.Position, b.Position) < 1f;

        /** 1 m Suchraum; sowohl geteilte als auch vereinte Kanten passen. */
        public static bool TryProjectToEdge(BusStopPlacement stop,
            float2 edgeA, float2 edgeB, out float along, out bool reversed,
            out float distanceSquared)
        {
            along = 0f;
            reversed = false;
            distanceSquared = float.PositiveInfinity;
            var planned = stop.B - stop.A;
            var built = edgeB - edgeA;
            var plannedLength2 = math.lengthsq(planned);
            var builtLength2 = math.lengthsq(built);
            if (plannedLength2 < 144f || builtLength2 < 1f) return false;
            var cosine = math.dot(planned, built)
                / math.sqrt(plannedLength2 * builtLength2);
            if (math.abs(cosine) < .995f) return false;
            along = math.dot(stop.Position - edgeA, built) / builtLength2;
            if (along < .05f || along > .95f) return false;
            distanceSquared = math.distancesq(stop.Position,
                edgeA + along * built);
            reversed = cosine < 0f;
            return distanceSquared <= 1f;
        }

        /** Halbe Breite einer kreuzenden Strasse, grosszuegig fuer alle Arten. */
        public const float KreuzungHalbeBreite = 4f;

        /**
         * Abstand der Haltestelle zur Kreuzungs- bzw. Muendungskante.
         * Nutzer, 2026-09-25: *"darf gerne noch etwas naeher an die Kreuzung
         * nur nicht auf die Kreuzung ... 0.5m vor der Kreuzung ist Halt"*.
         * Vorher 4 m.
         */
        public const float KreuzungFreiraum = 0.5f;

        /** So nah (in m entlang der Strasse) rastet ein Halt gegenueber ein. */
        public const float GegenueberFang = 8f;

        /**
         * Nur NetLine mit Kind zoning nehmen. Die 8-m-Schranke und 6-m-
         * Endreserve sind Bedienmasse; der Test misst Treffer und Ablehnung.
         *
         * KREUZUNGEN WERDEN UEBERSPRUNGEN (Nutzer, 2026-09-24): wo eine
         * Querstrasse oder Fahrgasse die Zoning-Strasse kreuzt oder an sie
         * stoesst, erscheint KEIN Halt - der Punkt springt an die naechste
         * erlaubte Stelle davor oder dahinter. Vorher wurde er dort
         * angezeigt und der Bau scheiterte.
         *
         * GEGENUEBER EINRASTEN: liegt auf der anderen Strassenseite schon
         * ein Halt, rastet der neue auf gleicher Hoehe ein. `fangAus`
         * (Shift, wie bei den Zufahrten) schaltet das ab - nicht aber das
         * Ueberspringen der Kreuzungen, das ist keine Hilfe, sondern eine
         * Grenze.
         */
        public static bool TryFind(ParkingLayout layout, float2 cursor,
            float maxDistance, double fahrbreite, double querbreite,
            out BusStopPlacement result,
            System.Collections.Generic.IReadOnlyList<BusStopPlacement> vorhandene = null,
            bool fangAus = false)
        {
            result = default;
            if (layout?.NetLine == null || !math.all(math.isfinite(cursor)))
                return false;
            var best = maxDistance * maxDistance;
            var found = false;
            foreach (var line in layout.NetLine)
            {
                if (!string.Equals(line.Kind, "zoning", StringComparison.Ordinal))
                    continue;
                var direction = line.B - line.A;
                var length2 = math.lengthsq(direction);
                if (length2 < 144f) continue;
                var laenge = math.sqrt(length2);
                var t0 = math.dot(cursor - line.A, direction) / length2;
                var lot = line.A + math.clamp(t0, 0f, 1f) * direction;
                var distance2 = math.lengthsq(cursor - lot);
                if (distance2 > best) continue;
                var side = direction.x * (cursor.y - lot.y)
                    - direction.y * (cursor.x - lot.x);
                if (math.abs(side) < 0.01f) continue;
                var links = side > 0f;

                var sperren = Sperrbereiche(layout, line, laenge, fahrbreite,
                    querbreite);
                if (!TryErlaubt(t0, sperren, out var t)) continue;

                if (!fangAus && vorhandene != null
                    && TryGegenueber(line, laenge, links, t, vorhandene,
                        out var tGegenueber)
                    && Frei(tGegenueber, sperren))
                    t = tGegenueber;

                best = distance2;
                result = new BusStopPlacement
                {
                    A = line.A, B = line.B, Along = t, Left = links,
                };
                found = true;
            }
            return found;
        }

        /**
         * Gesperrte Anteile [von, bis] auf der Zoning-Linie: beide Enden
         * (6 m, wie bisher) und jede Stelle, an der eine andere Linie des
         * Layouts sie kreuzt oder mit einem Ende beruehrt.
         */
        public static System.Collections.Generic.List<float2> Sperrbereiche(
            ParkingLayout layout, NetSegment linie, float laenge,
            double fahrbreite, double querbreite)
        {
            var sperren = new System.Collections.Generic.List<float2>();
            var rand = 6f / laenge;
            sperren.Add(new float2(float.NegativeInfinity, rand));
            sperren.Add(new float2(1f - rand, float.PositiveInfinity));
            var d = linie.B - linie.A;
            var richtung = d / laenge;
            foreach (var andere in layout.NetLine)
            {
                // NetSegment ist ein struct: ReferenceEquals waere immer
                // falsch (Boxing). Die Linie selbst faellt ueber die Endpunkte.
                if (math.all(andere.A == linie.A) && math.all(andere.B == linie.B))
                    continue;
                var e = andere.B - andere.A;
                var el = math.length(e);
                if (el < 0.5f) continue;
                var sinus = math.abs(richtung.x * e.y - richtung.y * e.x) / el;
                // Parallel laufende Linien kreuzen nicht; die Endreserve
                // deckt ihre gemeinsamen Enden ab.
                if (sinus < 0.2f) continue;
                if (TryBeruehrung(linie.A, d, andere.A, e, out var t))
                {
                    var halb = (KreuzungHalbeBreite / sinus + KreuzungFreiraum) / laenge;
                    sperren.Add(new float2(t - halb, t + halb));
                    continue;
                }
                /*
                 * DIE MUENDUNG ZAEHLT GENAUSO.
                 *
                 * Fahrgassen und Querwege beruehren die Zoning-Strasse nie:
                 * ihr freies Ende liegt (Zoning-Breite + eigene Breite) / 2
                 * vor der Achse, ihr Belag stoesst an den Strassenrand
                 * (gemessen 2026-09-25: 7,50 m / 5,50 m). Die Beruehrung auf
                 * 1 m fand deshalb keine einzige Einmuendung, und der Halt
                 * blieb mitten in ihr stehen. Gesperrt wird die Breite der
                 * Muendung plus der Freiraum.
                 */
                if (!ZoningMuendung.Muendet(layout.NetLine, andere, linie,
                        fahrbreite, querbreite, out var tm)) continue;
                var halbMuendung = ((float)ZoningMuendung.EigeneBreite(andere,
                    fahrbreite, querbreite) / 2f / sinus + KreuzungFreiraum) / laenge;
                sperren.Add(new float2(tm - halbMuendung, tm + halbMuendung));
            }
            return sperren;
        }

        /**
         * Schneidet Segment p+t*d das Segment q+u*e - mit 1 m Spiel an beiden
         * Enden, damit auch eine Strasse zaehlt, die an der Zoning-Strasse
         * endet? Liefert den Anteil t auf p..p+d.
         */
        private static bool TryBeruehrung(float2 p, float2 d, float2 q, float2 e,
            out float t)
        {
            t = 0f;
            var nenner = d.x * e.y - d.y * e.x;
            if (math.abs(nenner) < 1e-6f) return false;
            var w = q - p;
            var tt = (w.x * e.y - w.y * e.x) / nenner;
            var uu = (w.x * d.y - w.y * d.x) / nenner;
            var tolT = 1f / math.length(d);
            var tolU = 1f / math.length(e);
            if (tt < -tolT || tt > 1f + tolT || uu < -tolU || uu > 1f + tolU)
                return false;
            t = tt;
            return true;
        }

        /** Liegt t frei, bleibt es; sonst die naechste freie Grenze. */
        private static bool TryErlaubt(float t,
            System.Collections.Generic.List<float2> sperren, out float frei)
        {
            frei = t;
            if (Frei(t, sperren)) return true;
            var bester = float.PositiveInfinity;
            var gefunden = false;
            foreach (var sperre in sperren)
                foreach (var grenze in new[] { sperre.x - 1e-4f, sperre.y + 1e-4f })
                {
                    if (float.IsInfinity(grenze) || grenze < 0f || grenze > 1f) continue;
                    if (!Frei(grenze, sperren)) continue;
                    var abstand = math.abs(grenze - t);
                    if (abstand >= bester) continue;
                    bester = abstand;
                    frei = grenze;
                    gefunden = true;
                }
            return gefunden;
        }

        public static bool Frei(float t, System.Collections.Generic.List<float2> sperren)
        {
            foreach (var s in sperren)
                if (t > s.x && t < s.y) return false;
            return true;
        }

        /**
         * Ein vorhandener Halt auf der ANDEREN Seite derselben Linie, nahe
         * genug? Die Linie kann im gespeicherten Halt andersherum stehen -
         * deshalb ueber die Weltposition und die Seite zur Linie, nicht ueber
         * Along und Left.
         */
        private static bool TryGegenueber(NetSegment linie, float laenge,
            bool links, float t,
            System.Collections.Generic.IReadOnlyList<BusStopPlacement> vorhandene,
            out float tGegenueber)
        {
            tGegenueber = t;
            var d = linie.B - linie.A;
            var bester = GegenueberFang;
            var gefunden = false;
            foreach (var halt in vorhandene)
            {
                if (!DieselbeLinie(halt, linie)) continue;
                var p = halt.Position;
                var schild = SignPosition(halt);
                var te = math.dot(p - linie.A, d) / (laenge * laenge);
                var zeichen = d.x * (schild.y - p.y) - d.y * (schild.x - p.x);
                if ((zeichen > 0f) == links) continue;
                var abstand = math.abs(te - t) * laenge;
                if (abstand >= bester) continue;
                bester = abstand;
                tGegenueber = te;
                gefunden = true;
            }
            return gefunden;
        }

        private static bool DieselbeLinie(BusStopPlacement halt, NetSegment linie)
            => (math.distancesq(halt.A, linie.A) < 0.25f
                    && math.distancesq(halt.B, linie.B) < 0.25f)
                || (math.distancesq(halt.A, linie.B) < 0.25f
                    && math.distancesq(halt.B, linie.A) < 0.25f);
    }
}
