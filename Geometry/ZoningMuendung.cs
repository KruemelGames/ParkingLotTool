using System;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * MUENDET EIN WEG IN EINE ZONING-STRASSE?
     *
     * Fahrgassen und Querwege sind mit der Zoning-Strasse NICHT ueber einen
     * gemeinsamen Knoten verbunden. Ihr freies Ende liegt so, dass ihr Belag
     * genau an den Rand der Zoning-Strasse stoesst: Abstand zur Achse =
     * (Zoning-Breite + eigene Breite) / 2. Gemessen am 2026-09-25 an einem
     * echten Parkplatz: Fahrgasse 7 m -> 7,50 m, Querweg 3 m -> 5,50 m.
     *
     * Die Regel stand zuerst nur in `RandstrassenErreichbarkeit` (dort
     * verbindet eine Muendung zwei Netze). Die Bushaltestellen brauchen
     * dieselbe Frage - "hier oeffnet sich eine Gasse zur Strasse, kein Halt"
     * - und fragten stattdessen nach einer Beruehrung auf 1 m. Die gab es an
     * keiner einzigen Einmuendung, also sprang der Halt nie weiter. Jetzt
     * steht die Regel nur hier.
     */
    public static class ZoningMuendung
    {
        /**
         * 1 cm. Die erste Fassung (1 mm) lief nur im Test mit lokalen
         * Koordinaten. Im Spiel liegen sie bei ueber 4000 m, float ist dort
         * auf rund 0,5 mm genau: gemessen 0,43 mm Abweichung (2026-09-25).
         * Der naechste fremde Abstand lag bei 5,84 statt 5,50 m.
         */
        private const double Toleranz = 0.01;

        /** Breite des Belags einer Netzlinie, nach ihrer Art. */
        public static double EigeneBreite(NetSegment weg, double fahrbreite,
            double querbreite)
            => string.Equals(weg.Kind, "cross", StringComparison.Ordinal)
                ? querbreite : fahrbreite;

        /**
         * Muendet `weg` mit einem freien Ende in `strasse`? `t` ist dann der
         * Anteil entlang der Strasse (0..1), an dem er muendet.
         *
         * Frei heisst: kein anderer Weg (ausser Zoning) teilt diesen
         * Endpunkt. Das andere Ende muss weiter weg liegen - sonst waere es
         * ein parallel laufender Weg.
         */
        public static bool Muendet(NetSegment[] netz, NetSegment weg,
            NetSegment strasse, double fahrbreite, double querbreite,
            out float t)
        {
            t = 0f;
            if (string.Equals(weg.Kind, "zoning", StringComparison.Ordinal))
                return false;
            var halb = (ParkingGeometry.ZoningStrassenbreite
                + EigeneBreite(weg, fahrbreite, querbreite)) / 2;
            var d = strasse.B - strasse.A;
            var l2 = math.lengthsq(d);
            if (l2 < 1e-6f) return false;
            foreach (var p in new[] { weg.A, weg.B })
            {
                var grad = netz.Count(n => !string.Equals(n.Kind, "zoning",
                        StringComparison.Ordinal)
                    && (Nah(p, n.A) || Nah(p, n.B)));
                if (grad != 1) continue;
                var tt = math.dot(p - strasse.A, d) / l2;
                if (tt < 0 || tt > 1) continue;
                var abstand = Abstand(p, strasse.A, strasse.B);
                var anderes = Nah(p, weg.A) ? weg.B : weg.A;
                if (Math.Abs(abstand - halb) <= Toleranz
                    && Abstand(anderes, strasse.A, strasse.B) > abstand + Toleranz)
                {
                    t = tt;
                    return true;
                }
            }
            return false;
        }

        private static bool Nah(float2 a, float2 b) => math.distance(a, b) <= 0.001;

        private static double Abstand(float2 p, float2 a, float2 b)
            => Geometrie.AbstandPunktStrecke(new Punkt(p.x, p.y),
                new Punkt(a.x, a.y), new Punkt(b.x, b.y));
    }
}
