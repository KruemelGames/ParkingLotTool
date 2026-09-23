using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * WELCHE STRASSE ERREICHT EINE GASSE - IN IHRER FANGRICHTUNG.
     *
     * Die Spezifikation des Nutzers vom 2026-09-24, nachdem ich die 16 m
     * zweimal falsch verstanden hatte (erst als Stueckelung, dann als
     * Kappung):
     *
     *   *"Ich habe z.B. eine Randstrasse, die ist schraeg, an die snappe ich
     *   eine Alley. Wenn die Alley max 1 m sein duerfte, wuerde sie sich
     *   eventuell mit der Strasse verbinden, wenn die orthogonal liegt, aber
     *   nicht, wenn sie schraeg ist, weil da die Entfernung laenger ist.
     *   Ebenfalls wollte ich erreichen, dass Strassen erreicht werden koennen,
     *   die nicht direkt am Parkplatzrand liegen, sondern etwas weiter weg."*
     *
     * Gemessen wird ab dem POLYGONRAND, in FANGRICHTUNG, bis zum BORDSTEIN.
     * Die Richtung wird nicht verhandelt: die Gasse dreht sich nie zur
     * Strasse hin.
     *
     * Bis dahin suchte der Bau den NAECHSTEN Punkt der naechsten Strasse im
     * Umkreis von 40 m (`SucheStadtstrasseFuerGasse`). An einer schraegen
     * Strasse liegt der aber senkrecht zur Strasse, nicht in Fangrichtung -
     * die Gasse knickte vom Fang weg. Diese Klasse ersetzt die Suche.
     *
     * Reine Rechnung, damit `--gassenreichweite` sie pruefen kann. Die
     * Strassen kommen als Polylinie der Mittellinie mit halber Breite; das
     * Abtasten der Spielkurven macht `Tools/`.
     */
    public static class Gassenreichweite
    {
        /** Hoechstens so weit darf der Bordstein vom Polygonrand liegen. */
        public const float Reichweite = 16f;

        /**
         * Unter diesem Sinus zwischen Strahl und Strasse gilt die Gasse als
         * parallel. 0,1 entspricht knapp 6 Grad; flacher ist keine Einmuendung.
         */
        public const float MinSinus = 0.1f;

        public struct Strasse
        {
            public float2[] Mittellinie;
            public float HalbeBreite;
            public int Kennung;
        }

        public struct Treffer
        {
            public int Kennung;
            /** Index des Mittellinienstuecks und Anteil darauf (0..1). */
            public int Stueck;
            public float Anteil;
            /** Wo der Strahl die Mittellinie trifft. */
            public float2 Mitte;
            /** Strahllaenge vom Polygonrand bis zur Mittellinie. */
            public float BisMitte;
            /** Strahllaenge vom Polygonrand bis zum Bordstein. */
            public float BisBordstein;
            /** Halbe Breite, in Strahlrichtung gemessen (Mitte - Bordstein). */
            public float HalbeBreiteImStrahl;
        }

        /**
         * `rand` liegt auf dem Polygonrand, `nachAussen` zeigt vom Parkplatz
         * weg (normiert oder nicht). Genommen wird die NAECHSTE Strasse
         * entlang des Strahls, deren Bordstein innerhalb der Reichweite liegt.
         */
        public static bool Finde(float2 rand, float2 nachAussen,
            IReadOnlyList<Strasse> strassen, out Treffer treffer,
            float reichweite = Reichweite)
        {
            treffer = default;
            var d = math.normalizesafe(nachAussen);
            if (math.lengthsq(d) < 0.5f || strassen == null) return false;

            var gefunden = false;
            var bester = float.MaxValue;
            for (var s = 0; s < strassen.Count; s++)
            {
                var linie = strassen[s].Mittellinie;
                if (linie == null || linie.Length < 2) continue;
                for (var k = 0; k + 1 < linie.Length; k++)
                {
                    var p = linie[k];
                    var q = linie[k + 1];
                    var e = q - p;
                    var laenge = math.length(e);
                    if (laenge < 1e-4f) continue;

                    // rand + u*d = p + v*e
                    var nenner = Kreuz(d, e);
                    if (math.abs(nenner) < 1e-6f) continue;
                    var w = p - rand;
                    var u = Kreuz(w, e) / nenner;
                    var v = Kreuz(w, d) / nenner;
                    if (v < -1e-4f || v > 1f + 1e-4f) continue;
                    if (u < 0f) continue;

                    var sinus = math.abs(nenner) / laenge;
                    if (sinus < MinSinus) continue;
                    var imStrahl = strassen[s].HalbeBreite / sinus;
                    var bordstein = u - imStrahl;
                    if (bordstein > reichweite) continue;
                    if (u >= bester) continue;

                    bester = u;
                    gefunden = true;
                    treffer = new Treffer
                    {
                        Kennung = strassen[s].Kennung,
                        Stueck = k,
                        Anteil = math.clamp(v, 0f, 1f),
                        Mitte = rand + d * u,
                        BisMitte = u,
                        BisBordstein = bordstein,
                        HalbeBreiteImStrahl = imStrahl,
                    };
                }
            }
            return gefunden;
        }

        private static float Kreuz(float2 a, float2 b) => a.x * b.y - a.y * b.x;
    }
}
