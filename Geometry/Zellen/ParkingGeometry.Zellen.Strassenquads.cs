using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * Baut die sichtbaren Fahrbahnpolygone aus den geplanten Achsen und
         * ihrer Rangfolge. Die Netzachsen bleiben dabei vollstaendig; nur die
         * Flaechen enden exakt an der hoeheren Fahrbahnkante.
         */
        private static void ZellenTeilflaechenStrassenquads(
            Rahmen rahmen,
            IReadOnlyList<float2[]> randstrassen,
            IReadOnlyList<ZellenStrasse> gassen,
            IReadOnlyList<ZellenStrasse> querstrassen,
            double gassenbreite,
            double querbreite,
            out float2[][] gassenquads,
            out float2[][] querquads)
        {
            var hoeher = randstrassen.Select(PolygonDouble).ToList();
            var gassenAusgabe = new List<double2[]>();
            var querAusgabe = new List<double2[]>();

            // Die Naht ist die gemeinsame, vorher bekannte Grenze beider
            // Raster. Sie wird vor den Gassen gesetzt, damit deren Enden auf
            // beiden Seiten dieselbe Cw-Kante treffen.
            foreach (var strasse in querstrassen.Where(
                         kandidat => kandidat.Teilflaechenverbindung))
                ZellenFuegeStrassenflaecheHinzu(
                    strasse, rahmen, querbreite, hoeher, querAusgabe);
            foreach (var strasse in gassen)
                ZellenFuegeStrassenflaecheHinzu(
                    strasse, rahmen, gassenbreite, hoeher, gassenAusgabe);
            foreach (var strasse in querstrassen.Where(
                         kandidat => !kandidat.Teilflaechenverbindung))
                ZellenFuegeStrassenflaecheHinzu(
                    strasse, rahmen, querbreite, hoeher, querAusgabe);

            gassenquads = gassenAusgabe.Select(PolygonFloat).ToArray();
            querquads = querAusgabe.Select(PolygonFloat).ToArray();
        }

        private static void ZellenFuegeStrassenflaecheHinzu(
            ZellenStrasse strasse,
            Rahmen rahmen,
            double breite,
            List<double2[]> hoeher,
            List<double2[]> ausgabe)
        {
            var aWelt = rahmen.NachWelt(strasse.A);
            var bWelt = rahmen.NachWelt(strasse.B);
            var a = new double2(aWelt.X, aWelt.Y);
            var b = new double2(bWelt.X, bWelt.Y);
            var vektor = b - a;
            var laenge = Len(vektor);
            if (laenge < 0.2) return;
            var richtung = vektor / laenge;
            var frei = ZellenFreieIntervalle(a, richtung, laenge, hoeher);
            foreach (var intervall in frei)
            {
                if (intervall[1] - intervall[0] < 0.2) continue;
                var linie = new Line2(
                    a + richtung * intervall[0],
                    a + richtung * intervall[1]);
                var polygon = ZellenStrassenpolygon(linie, breite, hoeher);
                if (polygon == null || polygon.Length < 3
                    || Math.Abs(SignedArea(polygon)) <= 1e-8) continue;
                ausgabe.Add(polygon);
                hoeher.Add(polygon);
            }
        }

        /** Exakte freien Achsenintervalle; kein Abtasten und keine 0,25-m-Fuge. */
        private static IReadOnlyList<double[]> ZellenFreieIntervalle(
            double2 anfang,
            double2 richtung,
            double laenge,
            IReadOnlyList<double2[]> belegt)
        {
            var intervalle = new List<double[]>();
            foreach (var polygon in belegt)
            {
                var intervall = SegInConvex(
                    anfang, richtung, laenge, polygon);
                if (intervall != null) intervalle.Add(intervall);
            }
            intervalle.Sort((a, b) => a[0].CompareTo(b[0]));
            var frei = new List<double[]>();
            var cursor = 0.0;
            foreach (var intervall in intervalle)
            {
                if (intervall[0] > cursor + 1e-8)
                    frei.Add(new[]
                    {
                        cursor,
                        Math.Min(laenge, intervall[0]),
                    });
                cursor = Math.Max(cursor, intervall[1]);
            }
            if (cursor < laenge - 1e-8)
                frei.Add(new[] { cursor, laenge });
            return frei;
        }

        /**
         * Der Rohstreifen wird an der tatsaechlich getroffenen Kante
         * geschnitten. Das ist derselbe konstruktive Gehrungsweg wie im
         * klassischen Kern, hier fuer die Zellenachsen ohne BuildContext.
         */
        private static double2[] ZellenStrassenpolygon(
            Line2 linie,
            double breite,
            IReadOnlyList<double2[]> hoeher)
        {
            var vektor = linie.B - linie.A;
            var laenge = Len(vektor);
            if (laenge < 0.2) return null;
            var u = vektor / laenge;
            var n = new double2(-u.y, u.x);
            var halb = breite / 2;
            var probe = Math.Min(0.05, laenge / 4);

            bool Trifft(double2 punkt, double2 einwaerts)
            {
                var frei = punkt + einwaerts * probe;
                var strasse = punkt - einwaerts * probe;
                return !hoeher.Any(polygon => PointIn(frei, polygon))
                    && hoeher.Any(polygon => PointIn(strasse, polygon)
                        || DistToBoundary(punkt, polygon) <= 0.05);
            }

            var trifftAnfang = Trifft(linie.A, u);
            var trifftEnde = Trifft(linie.B, -u);
            var reichweite = halb / MinJunctionSin + 0.1;
            var rohAnfang = trifftAnfang ? linie.A - u * reichweite : linie.A;
            var rohEnde = trifftEnde ? linie.B + u * reichweite : linie.B;
            var seite = n * halb;
            var polygon = new[]
            {
                rohAnfang + seite,
                rohEnde + seite,
                rohEnde - seite,
                rohAnfang - seite,
            };

            bool Vereinigungsrand(double2[] eigentuemer, double2 a, double2 b)
            {
                foreach (var kandidat in hoeher)
                {
                    if (ReferenceEquals(kandidat, eigentuemer)) continue;
                    for (var i = 0; i < kandidat.Length; i++)
                    {
                        var p = kandidat[i];
                        var q = kandidat[(i + 1) % kandidat.Length];
                        if ((Len(a - p) <= 1e-7 && Len(b - q) <= 1e-7)
                            || (Len(a - q) <= 1e-7 && Len(b - p) <= 1e-7))
                            return false;
                    }
                }
                return true;
            }

            double2[] SchneideAussen(
                double2[] quelle,
                double2[] strasse,
                double2 anker,
                double2 kontakt)
            {
                var umlauf = SignedArea(strasse) >= 0 ? 1 : -1;
                double2 randA = default;
                double2 randB = default;
                var besteSeite = double.NegativeInfinity;
                var kleinsteLuecke = double.PositiveInfinity;
                var gefunden = false;
                for (var i = 0; i < strasse.Length; i++)
                {
                    var a = strasse[i];
                    var b = strasse[(i + 1) % strasse.Length];
                    var kante = b - a;
                    var kantenlaenge = Len(kante);
                    if (kantenlaenge < 1e-9
                        || !Vereinigungsrand(strasse, a, b)) continue;
                    var ankerseite = umlauf
                        * (kante.x * (anker.y - a.y)
                            - kante.y * (anker.x - a.x)) / kantenlaenge;
                    var luecke = DistToSeg(kontakt, a, b);
                    if (ankerseite >= -1e-8
                        || (luecke >= kleinsteLuecke - 1e-9
                            && (Math.Abs(luecke - kleinsteLuecke) > 1e-9
                                || ankerseite <= besteSeite))) continue;
                    randA = a;
                    randB = b;
                    besteSeite = ankerseite;
                    kleinsteLuecke = luecke;
                    gefunden = true;
                }
                if (!gefunden) return quelle;

                double Seite(double2 punkt) => umlauf
                    * ((randB.x - randA.x) * (punkt.y - randA.y)
                        - (randB.y - randA.y) * (punkt.x - randA.x));
                var geschnitten = new List<double2>();
                for (var i = 0; i < quelle.Length; i++)
                {
                    var punkt = quelle[i];
                    var naechster = quelle[(i + 1) % quelle.Length];
                    var hier = Seite(punkt);
                    var dort = Seite(naechster);
                    var hierBehalten = hier <= 1e-9;
                    var dortBehalten = dort <= 1e-9;
                    if (hierBehalten) geschnitten.Add(punkt);
                    if (hierBehalten == dortBehalten) continue;
                    var t = hier / (hier - dort);
                    geschnitten.Add(punkt + (naechster - punkt) * t);
                }
                return geschnitten.ToArray();
            }

            for (var runde = 0; runde < 2; runde++)
            {
                var geaendert = false;
                foreach (var strasse in hoeher)
                {
                    if (!QuadsOverlap(polygon, strasse, 0)) continue;
                    var amAnfang = DistToBoundary(linie.A, strasse)
                        <= DistToBoundary(linie.B, strasse);
                    var anker = amAnfang
                        ? linie.A + u * probe
                        : linie.B - u * probe;
                    var kontakt = amAnfang ? linie.A : linie.B;
                    var geschnitten = SchneideAussen(
                        polygon, strasse, anker, kontakt);
                    if (geschnitten.Length < 3
                        || Math.Abs(SignedArea(geschnitten)) <= 1e-9) continue;
                    polygon = geschnitten;
                    geaendert = true;
                }
                if (!geaendert) break;
            }
            return polygon;
        }

        private static double2[] PolygonDouble(float2[] polygon) => polygon
            .Select(punkt => new double2(punkt.x, punkt.y)).ToArray();

        private static float2[] PolygonFloat(double2[] polygon) => polygon
            .Select(punkt => new float2((float)punkt.x, (float)punkt.y)).ToArray();
    }
}
