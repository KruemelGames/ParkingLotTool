using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * CS2 sucht vom Gebaeudeeingang (lokal +Z, Tiefe * 4 m), nicht von
     * der Strassenachse. Bei einer 32-m-Strasse lag der alte Marker damit
     * bis zu 16 m vom Rand entfernt; CS2 sucht dort nur 8,4 m weit.
     */
    public static class BegleiterAnbindung
    {
        public static bool Lage(float2 rand, float2 achse, float2 lotSeite,
            int tiefe, out float2 mitte, out float2 vorne)
        {
            mitte = vorne = default;
            if (!math.all(math.isfinite(rand)) || !math.all(math.isfinite(achse))
                || !math.all(math.isfinite(lotSeite)) || tiefe < 1) return false;
            var aussen = math.normalizesafe(rand - achse);
            if (math.lengthsq(aussen) < 0.5f
                || math.dot(aussen, lotSeite - achse) <= 0f) return false;
            vorne = -aussen;
            // Der Eingang liegt 0,5 m AUSSERHALB der fertigen Strassenkante.
            mitte = rand + aussen * (tiefe * 4f + 0.5f);
            return true;
        }
    }
}
