using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry.Zellen;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        // Derselbe 5-mm-Vergleich wie bei RandzoningGeteilt. Beide Kurse
        // erhalten den bereits konstruierten Knoten, keine neue Verbindung.
        private static Punkt RandzoningQuerEnde(Punkt ende, Punkt anderesEnde,
            IReadOnlyList<(Punkt A, Punkt B)> achsen)
        {
            foreach (var achse in achsen)
            {
                if (!RandzoningQuerZurAchse(anderesEnde - ende, achse.B - achse.A)) continue;
                if (Geometrie.Laenge(ende - achse.A) <= 0.005) return achse.A;
                if (Geometrie.Laenge(ende - achse.B) <= 0.005) return achse.B;
            }
            return ende;
        }

        private static bool RandzoningQuerZurAchse(Punkt quer, Punkt achse)
            => Math.Abs(Geometrie.Kreuz(quer, achse))
                > 0.02 * Geometrie.Laenge(quer) * Geometrie.Laenge(achse);

        private static bool RandzoningGemeinsamerKnoten(Punkt a, Punkt b, Punkt za, Punkt zb)
            => RandzoningQuerZurAchse(b - a, zb - za)
                && (Geometrie.Laenge(a - za) < 1e-9 || Geometrie.Laenge(a - zb) < 1e-9
                    || Geometrie.Laenge(b - za) < 1e-9 || Geometrie.Laenge(b - zb) < 1e-9);
    }
}
