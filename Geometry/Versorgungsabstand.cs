using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Konservative Bezier-Huellen: keine Rasterprobe, zwischen deren Punkten
    // eine Kreuzung verschwinden kann. Restunsicherheit unter 5 cm wird als
    // Beruehrung behandelt; die Vanilla-Pruefung bleibt zusaetzlich aktiv.
    internal static class Versorgungsabstand
    {
        internal static bool Beruehrt(float3[] a, float breiteA, float2 hoeheA,
            float3[] b, float breiteB, float2 hoeheB)
            => Beruehrt(a, b, (breiteA + breiteB) * 0.5f, hoeheA, hoeheB, 0);

        private static bool Beruehrt(float3[] a, float3[] b, float radius,
            float2 ha, float2 hb, int tiefe)
        {
            Grenzen(a, out var amin, out var amax);
            Grenzen(b, out var bmin, out var bmax);
            if (amax.y + ha.y < bmin.y + hb.x - 0.001f
                || bmax.y + hb.y < amin.y + ha.x - 0.001f) return false;
            var luecke = math.max(new float2(0), math.max(amin.xz - bmax.xz, bmin.xz - amax.xz));
            if (math.length(luecke) > radius + 0.001f) return false;
            var ausdehnungA = math.length(amax - amin);
            var ausdehnungB = math.length(bmax - bmin);
            if (tiefe >= 24 || math.max(ausdehnungA, ausdehnungB) <= 0.05f) return true;
            if (ausdehnungA > ausdehnungB)
            {
                Teile(a, out var links, out var rechts);
                return Beruehrt(links, b, radius, ha, hb, tiefe + 1)
                    || Beruehrt(rechts, b, radius, ha, hb, tiefe + 1);
            }
            Teile(b, out var l, out var r);
            return Beruehrt(a, l, radius, ha, hb, tiefe + 1)
                || Beruehrt(a, r, radius, ha, hb, tiefe + 1);
        }

        private static void Grenzen(float3[] p, out float3 min, out float3 max)
        {
            min = max = p[0];
            for (var i = 1; i < 4; i++) { min = math.min(min, p[i]); max = math.max(max, p[i]); }
        }

        private static void Teile(float3[] p, out float3[] links, out float3[] rechts)
        {
            var ab = (p[0] + p[1]) * 0.5f;
            var bc = (p[1] + p[2]) * 0.5f;
            var cd = (p[2] + p[3]) * 0.5f;
            var abc = (ab + bc) * 0.5f;
            var bcd = (bc + cd) * 0.5f;
            var mitte = (abc + bcd) * 0.5f;
            links = new[] { p[0], ab, abc, mitte };
            rechts = new[] { mitte, bcd, cd, p[3] };
        }
    }
}
