using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry.Zellen;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * Die Randachse ist Vorgabe fuer die Ringkonstruktion samt Querarmen.
         * Mutation zur alten Ringkonstruktion, mit ungerundetem Reihenwinkel:
         * 01:51 -> 8 Kanten, 4 Ueberlappungen; 01:52 -> 5 Kanten, 2 Netze.
         * Im gespeicherten NetLine-Plan von 01:52 endet der Querarm noch
         * 0,158 mm neben der RZ-Achse: unter der 1-mm-Verlaengerungsschwelle,
         * aber ohne echten Schnitt fuer ZellenSchneideStrassen. Die entfernte
         * Ringseite hinterliess also keinen gemeinsamen Kreuzungspunkt.
         * Zwei 8-m-Fahrbahnen haben erst ab 8 m Achsabstand eigenen Platz.
         * Deshalb wird im gemeinsamen Fahrbahnband eine Achse verwendet;
         * die Ecken entstehen als Schnitt der gewaehlten Traegergeraden.
         */
        private static Punkt[] ZoningRingMitRandachse(Zoningvorgabe flaeche,
            IReadOnlyList<(Punkt A, Punkt B)> randachsen)
        {
            var ring = flaeche.Strassenring();
            if (randachsen == null || randachsen.Count == 0) return ring;
            var traeger = new (Punkt A, Punkt B)[4];
            for (var i = 0; i < 4; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 4];
                traeger[i] = (a, b);
                var laenge = Geometrie.Laenge(b - a);
                if (laenge < 1e-6) continue;
                var r = (b - a) * (1 / laenge);
                var bester = double.PositiveInfinity;
                foreach (var achse in randachsen)
                {
                    var l = Geometrie.Laenge(achse.B - achse.A);
                    if (l < 1e-6) continue;
                    var q = (achse.B - achse.A) * (1 / l);
                    // Nur dieselbe Laengsrichtung; Querarme bleiben Querarme.
                    if (Math.Abs(Geometrie.Kreuz(r, q)) > 1e-3) continue;
                    var da = Math.Abs(Geometrie.Kreuz(a - achse.A, q));
                    var db = Math.Abs(Geometrie.Kreuz(b - achse.A, q));
                    var abstand = Math.Max(da, db);
                    if (abstand >= ZoningStrassenbreite - 1e-3
                        || abstand >= bester) continue;
                    var ta = Geometrie.Skalar(a - achse.A, q);
                    var tb = Geometrie.Skalar(b - achse.A, q);
                    if (Math.Min(Math.Max(ta, tb), l)
                        - Math.Max(Math.Min(ta, tb), 0) <= 1e-3) continue;
                    traeger[i] = achse;
                    bester = abstand;
                }
            }
            var ecken = new Punkt[4];
            for (var i = 0; i < 4; i++)
            {
                var vorher = traeger[(i + 3) % 4];
                var jetzt = traeger[i];
                var r = vorher.B - vorher.A;
                var s = jetzt.B - jetzt.A;
                var nenner = Geometrie.Kreuz(r, s);
                ecken[i] = Math.Abs(nenner) < 1e-9 ? ring[i]
                    : vorher.A + r * (Geometrie.Kreuz(jetzt.A - vorher.A, s) / nenner);
            }
            return ecken;
        }
    }
}
