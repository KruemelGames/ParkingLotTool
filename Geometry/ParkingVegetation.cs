using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public sealed class VegetationSpecies
    {
        public string Id;
        public bool Tree;
        public float Spacing;
    }
    public sealed class VegetationOptions
    {
        public bool Enabled;
        public bool Line;
        public int Density = 50;
        public int Ages = 6;
        public string[] Species = Array.Empty<string>();
    }
    public struct VegetationPlacement
    {
        public float2 Position;
        public int Species;
        public uint Seed;
    }
    public sealed class VegetationPlan
    {
        public readonly List<VegetationPlacement> Plants = new List<VegetationPlacement>();
        public int Candidates;
        public bool Limited;
    }
    /** Lokale Koordinaten und feste Hashes: gleiche Flaechen erzeugen gleiche
     * Pflanzen. 100 Prozent ist die Abstandsvorgabe, keine Vollbelegung.
     * Baeume werden zuerst verteilt; Buesche fuellen bevorzugt Randbereiche.
     */
    public static class ParkingVegetation
    {
        public static int SelectAge(int mask, uint seed)
        {
            mask &= 63;
            if(mask == 0) return 2;
            int count=0;
            for(int i=0;i<6;i++) if((mask & (1<<i))!=0) count++;
            int selected=(int)(seed % (uint)count);
            for(int i=0;i<6;i++) if((mask & (1<<i))!=0 && selected--==0) return i;
            return 2;
        }
        public static float AgeValue(int index)
            => index==0 ? .05f : index==1 ? .175f : index==2 ? .425f : index==3 ? .775f : 1f;

        public static VegetationPlan Plan(float2[][] rings, VegetationOptions options,
            VegetationSpecies[] species)
        {
            var result = new VegetationPlan();
            if (!options.Enabled || options.Density <= 0 || species.Length == 0) return result;
            var occupied = new Dictionary<long, List<int>>();
            foreach (var ring in rings ?? Array.Empty<float2[]>())
            {
                if (ring == null || ring.Length < 3) continue;
                var origin = ring[0];
                var axis = new float2(1, 0); float longest = 0;
                for (int i = 0; i < ring.Length; i++)
                {
                    var d = ring[(i + 1) % ring.Length] - ring[i];
                    float length = math.lengthsq(d);
                    if (length > longest) { longest = length; axis = math.normalizesafe(d); }
                }
                if (longest < 0.01f) continue;
                var across = new float2(-axis.y, axis.x);
                var local = new float2[ring.Length];
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                for (int i = 0; i < ring.Length; i++)
                {
                    var d = ring[i] - origin;
                    local[i] = new float2(math.dot(d, axis), math.dot(d, across));
                    lo = math.min(lo, local[i]); hi = math.max(hi, local[i]);
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    var pool = new List<int>();
                    for (int i = 0; i < species.Length; i++)
                        if (species[i].Tree == (pass == 0)) pool.Add(i);
                    if (pool.Count == 0) continue;
                    float step = 2f / (float)Math.Sqrt(3);
                    if(options.Line) foreach(int i in pool) step=math.max(step,Spacing(species[i],options.Line));
                    int nx = Math.Max(1, (int)Math.Floor((hi.x - lo.x) / step));
                    int ny = options.Line ? Math.Max(1, (int)Math.Floor((hi.y - lo.y) / step))
                        : Math.Max(1, (int)Math.Floor((hi.y - lo.y) / step));
                    if (options.Line) { if(nx % 2 == 0) nx--; if(ny % 2 == 0) ny--; }
                    long total=(long)nx*ny, stride=65537;
                    while(Gcd(stride,total)!=1) stride+=2;
                    for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                    {
                        if (++result.Candidates > 100000) { result.Limited = true; return Thin(result,options.Density); }
                        // Permutierte Besuchsreihenfolge verhindert regelmaessige
                        // Pflanzbaender durch eine zeilenweise Abstandssuche.
                        long cellIndex=((long)y*nx+x)*stride%total;
                        int ix=options.Line ? x : (int)(cellIndex%nx);
                        int iy=options.Line ? y : (int)(cellIndex/nx);
                        uint seed = Hash((uint)(ix + iy * 65537 + pass * 104729 + 1));
                        int which = pool[(int)(Hash(seed) % (uint)pool.Count)];
                        var kind = species[which];
                        int px = options.Line ? nx/2 + (x % 2 == 0 ? x/2 : -(x+1)/2) : ix;
                        int py = options.Line ? ny/2 + (y % 2 == 0 ? y/2 : -(y+1)/2) : iy;
                        var p = new float2(lo.x + (hi.x - lo.x) * (px + .5f) / nx,
                            lo.y + (hi.y - lo.y) * (py + .5f) / ny);
                        if (!options.Line) p += new float2(Unit(Hash(seed + 7)) - .5f,
                            Unit(Hash(seed + 11)) - .5f) * 1.5f;
                        float edge = EdgeDistance(p, local);
                        float margin = kind.Tree ? .65f : .35f;
                        if (!Inside(p, local) || edge < margin) continue;
                        if (!options.Line)
                        {
                            float chance = kind.Tree ? math.lerp(.3f, 1f, math.saturate(edge / 4f))
                                : math.lerp(1f, .25f, math.saturate(edge / 4f));
                            if (Unit(Hash(seed + 23)) > chance) continue;
                        }
                        var world = origin + axis * p.x + across * p.y;
                        int gx = (int)Math.Floor(world.x / 16f), gy = (int)Math.Floor(world.y / 16f);
                        bool clear = true;
                        for (int dy = -1; dy <= 1 && clear; dy++) for (int dx = -1; dx <= 1 && clear; dx++)
                            if (occupied.TryGetValue(Key(gx + dx, gy + dy), out var neighbours))
                                foreach (int n in neighbours)
                                {
                                    var other = result.Plants[n];
                                    float distance = (Spacing(kind,options.Line) + Spacing(species[other.Species],options.Line)) * .5f;
                                    if (math.distancesq(world, other.Position) < distance * distance - .001f)
                                    { clear = false; break; }
                                }
                        if (!clear) continue;
                        long key = Key(gx, gy);
                        if (!occupied.TryGetValue(key, out var cell)) occupied[key] = cell = new List<int>();
                        cell.Add(result.Plants.Count);
                        result.Plants.Add(new VegetationPlacement { Position = world, Species = which, Seed = seed });
                    }
                }
            }
            return Thin(result,options.Density);
        }
        private static VegetationPlan Thin(VegetationPlan plan,int density)
        {
            plan.Plants.RemoveAll(p=>Unit(Hash(p.Seed+97)) >= math.clamp(density,0,100)/100f);
            return plan;
        }
        public static float Spacing(VegetationSpecies species, bool line = false)
            => math.clamp(species.Spacing, species.Tree ? 6f : 2f, species.Tree ? 14f : 4f)
                / (line ? 3f : (float)Math.Sqrt(3));
        private static long Gcd(long a,long b) {while(b!=0){long next=a%b;a=b;b=next;}return a;}
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        private static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); }
        private static float Unit(uint x) => (x & 0xffffff) / 16777216f;
        public static bool Inside(float2 p, float2[] ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[i]; var b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)
                    inside = !inside;
            }
            return inside;
        }
        public static float EdgeDistance(float2 p, float2[] ring)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i]; var d = ring[(i + 1) % ring.Length] - a;
                float t = math.saturate(math.dot(p - a, d) / math.max(.000001f, math.lengthsq(d)));
                nearest = math.min(nearest, math.distance(p, a + t * d));
            }
            return nearest;
        }
    }
}
