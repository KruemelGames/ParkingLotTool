using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class GeometryPresets
    {
        public static readonly float2[] Rectangle =
        {
            new float2(0, 0), new float2(120, 0),
            new float2(120, 90), new float2(0, 90),
        };

        public static readonly float2[] LShape =
        {
            new float2(0, 0), new float2(120, 0), new float2(120, 45),
            new float2(60, 45), new float2(60, 90), new float2(0, 90),
        };

        public static readonly float2[] Skew =
        {
            new float2(0, 0), new float2(120, 0),
            new float2(150, 90), new float2(30, 90),
        };

        // Areal aus VideoFrames/parking_08s.png: zwischen French Pl, E 32nd St
        // und Edgewood Ave; unten links von Breeze Ter abgeschnitten.
        // Rund 0,3 m je Bildpunkt ergeben 127 x 112 m.
        public static readonly float2[] Reference08s =
        {
            new float2(0, 0), new float2(127, 0), new float2(127, 112),
            new float2(110, 106), new float2(95, 100), new float2(80, 92),
            new float2(68, 83), new float2(56, 74), new float2(43, 67),
            new float2(23, 64), new float2(0, 56),
        };
    }
}
