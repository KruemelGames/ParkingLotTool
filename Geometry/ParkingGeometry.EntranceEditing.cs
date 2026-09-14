using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Die Bedienung benutzt damit exakt denselben Eckfang wie der Baukern. */
    internal readonly struct EntranceCornerPlacement
    {
        internal readonly double Along;
        internal readonly float2 Direction;
        internal readonly double Length;

        internal EntranceCornerPlacement(double along, float2 direction, double length)
        {
            Along = along;
            Direction = direction;
            Length = length;
        }
    }

    public static partial class ParkingGeometry
    {
        /**
         * Eine zweite Eckrechnung in der Bedienung hatte im Prototyp bereits
         * 18,4 Grad Abweichung falsch abgewiesen. Deshalb reicht diese kleine
         * Bruecke das Ergebnis des Baukerns unveraendert an das Werkzeug durch.
         */
        internal static bool TryEntranceCornerPlacement(float2[] site,
            LayoutSettings settings, int edge, bool atStart,
            out EntranceCornerPlacement placement)
        {
            placement = default;
            if (site == null || settings == null) return false;
            var source = site.Select(point => new double2(point.x, point.y)).ToArray();
            var fit = EntranceCornerFit(source, settings, edge, atStart);
            if (fit == null) return false;
            placement = new EntranceCornerPlacement(fit.Along,
                new float2((float)fit.Direction.x, (float)fit.Direction.y),
                fit.Length);
            return true;
        }
    }
}
