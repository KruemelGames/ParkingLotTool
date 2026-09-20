using Unity.Mathematics;
namespace ParkingLotTool.Geometry
{
    // Konservative Vorauswahl: Kugel um den lokalen Ursprung umfasst auch
    // versetzte Kollisionsboxen und beliebige Rotation. Hoehe bleibt absichtlich
    // unbeschnitten, da die Suchposition dem Terrain folgt.
    public static class ChargerCandidates
    {
        public static float Radius(float3 min, float3 max)
            => math.length(math.max(math.abs(min), math.abs(max)));
        public static bool MayIntersect(float2 start, float2 end, float radius,
                                        float2 position, float objectRadius)
        {
            float padding=radius+objectRadius+0.05f;
            var lo=math.min(start,end)-padding;
            var hi=math.max(start,end)+padding;
            // Unbekannte/unendliche Werte niemals als sicheren Ausschluss werten.
            return !(position.x<lo.x || position.x>hi.x || position.y<lo.y || position.y>hi.y);
        }
    }
}
