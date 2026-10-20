using System.Collections.Generic;
using ParkingLotTool.Geometry.Zellen;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        internal const double KurvenzufahrtsMindestkante = NetzMindestlaenge;

        internal static Punkt Kurvenzufahrtsziel(Punkt start, Punkt richtung, IReadOnlyList<Punkt> achse)
        {
            var ziel = ZellenStrahlTrifftRing(start, richtung, achse);
            // Derselbe Vorrang wie festeEcken in ZellenVerschmelzeNaheKnoten.
            // Der Anschluss darf die buchtsichere Randstrasse nicht verschieben.
            foreach (var ecke in achse)
                if (Geometrie.Laenge(ziel - ecke) < KurvenzufahrtsMindestkante) return ecke;
            return ziel;
        }
    }
}
