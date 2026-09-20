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
        // Im Vegetationszettel gespeichert; keine neue Wuerfelung beim Laden.
        public uint Seed;
        public bool ShouldSerializeSeed() => Seed != 0;
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
        /*
         * WARUM EIN PUNKT LEER BLEIBT - EINZELN GEZAEHLT.
         *
         * "Manche Flaechen werden nicht befuellt" liess sich bisher nicht
         * eingrenzen: drei Regeln koennen einen Kandidaten verwerfen, und das
         * Ergebnis sah bei allen dreien gleich aus. Jetzt sagt der Bauzettel,
         * welche es war.
         */
        public int AussenVerworfen, RandVerworfen, WuerfelVerworfen, AbstandVerworfen;
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

        /**
         * DIE NOTBREMSE - UND WARUM SIE NICHT MEHR DIE DICHTE BESTIMMT.
         *
         * Gezaehlt werden nur noch Rasterpunkte INNERHALB der Flaechen. Deren
         * Zahl waechst mit der Flaeche, genau wie die Pflanzenzahl - eine
         * Schranke darauf kann also nicht mehr dazu fuehren, dass ein grosser
         * Parkplatz duenner bepflanzt wird als ein kleiner. Bei 400 000
         * Punkten waere die Dekoflaeche ueber 500 000 m2 gross; wer dahin
         * kommt, hat kein Dichteproblem, sondern ein anderes.
         *
         * Vorher stand hier 100 000 auf Zellen der BOUNDING BOX. Beim
         * Parkplatz des Nutzers mit 2875 Buchten war die Grenze erreicht,
         * bevor ein Viertel der Gruenflaechen ueberhaupt besucht war.
         */
        private const int Kandidatengrenze = 400000;

        /**
         * Die Rasterzellen, deren Mitte im Umriss liegt.
         *
         * Zeilenweise am Umriss geschnitten statt Punkt fuer Punkt geprueft:
         * der Aufwand haengt damit an der Flaeche und nicht mehr an der
         * Bounding Box. Bei einem schmalen Gruenband um einen grossen
         * Parkplatz ist das der Unterschied zwischen 5 % und 100 % nutzbarer
         * Arbeit.
         *
         * Die Schnittregel ist dieselbe wie in `Inside` - ungerade Zahl von
         * Kantenschnitten links des Punktes. Beide muessen dasselbe sagen,
         * sonst sucht die Vorauswahl woanders als die Endpruefung.
         */
        private static List<int2> Flaechenzellen(float2[] ring, float2 lo, float2 hi,
            int nx, int ny)
        {
            var zellen = new List<int2>();
            var schnitte = new List<float>();
            var breite = hi.x - lo.x;
            if (breite <= 0) return zellen;
            for (int iy = 0; iy < ny; iy++)
            {
                float py = lo.y + (hi.y - lo.y) * (iy + .5f) / ny;
                schnitte.Clear();
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    var a = ring[i];
                    var b = ring[j];
                    if ((a.y > py) != (b.y > py))
                        schnitte.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                if (schnitte.Count < 2) continue;
                schnitte.Sort();
                for (int k = 0; k + 1 < schnitte.Count; k += 2)
                {
                    // Zellenmitten liegen bei (ix + 0,5) / nx - daraus die
                    // erste und letzte Mitte im offenen Abschnitt.
                    int von = (int)Math.Ceiling((schnitte[k] - lo.x) * nx / breite - .5f);
                    int bis = (int)Math.Floor((schnitte[k + 1] - lo.x) * nx / breite - .5f);
                    von = Math.Max(von, 0);
                    bis = Math.Min(bis, nx - 1);
                    for (int ix = von; ix <= bis; ix++) zellen.Add(new int2(ix, iy));
                }
            }
            return zellen;
        }

        public static VegetationPlan Plan(float2[][] rings, VegetationOptions options,
            VegetationSpecies[] species)
        {
            var result = new VegetationPlan();
            if (!options.Enabled || options.Density <= 0 || species.Length == 0) return result;
            var occupied = new Dictionary<long, List<int>>();
            var anchor = new float2(float.MaxValue);
            foreach (var area in rings ?? Array.Empty<float2[]>())
                if (area != null) foreach (var point in area) anchor = math.min(anchor, point);
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
                var offset = origin - anchor;
                uint areaSeed = Hash(options.Seed ^ Hash((uint)(int)math.round(offset.x * 16f))
                    ^ Hash((uint)(int)math.round(offset.y * 16f) + 7919u));
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
                    if (options.Line)
                        foreach (int i in pool) step = math.max(step, Spacing(species[i], options.Line));
                    else
                    {
                        // Halb so fein wie der engste Abstand im Topf: dicht
                        // genug, dass jede zulaessige Stelle einen Kandidaten
                        // hat, und bei Baeumen ein Vielfaches weniger Arbeit
                        // als das feste 1,155-m-Raster.
                        var engster = float.MaxValue;
                        foreach (var i in pool) engster = math.min(engster, Spacing(species[i], false));
                        step = math.max(step, engster * .5f);
                    }
                    int nx = Math.Max(1, (int)Math.Floor((hi.x - lo.x) / step));
                    int ny = options.Line ? Math.Max(1, (int)Math.Floor((hi.y - lo.y) / step))
                        : Math.Max(1, (int)Math.Floor((hi.y - lo.y) / step));
                    if (options.Line) { if(nx % 2 == 0) nx--; if(ny % 2 == 0) ny--; }
                    // Nur die Zellen, die in der Flaeche liegen - im freien
                    // Modus ueber die geschnittenen Zeilen, im Line-Modus wie
                    // bisher das ganze Rechteck.
                    var zellen = options.Line ? null : Flaechenzellen(local, lo, hi, nx, ny);
                    long total = options.Line ? (long)nx * ny : zellen.Count * 2L;
                    if (total == 0) continue;
                    long stride=65537;
                    while(Gcd(stride,total)!=1) stride+=2;
                    for (long schritt = 0; schritt < total; schritt++)
                    {
                        if (++result.Candidates > Kandidatengrenze) { result.Limited = true; return Thin(result,options.Density); }
                        int x = options.Line ? (int)(schritt % nx) : 0;
                        int y = options.Line ? (int)(schritt / nx) : 0;
                        // Permutierte Besuchsreihenfolge verhindert regelmaessige
                        // Pflanzbaender durch eine zeilenweise Abstandssuche.
                        int ix, iy;
                        if (options.Line) { ix = x; iy = y; }
                        else { var zelle = zellen[(int)(schritt * stride % total / 2)]; ix = zelle.x; iy = zelle.y; }
                        uint seed = Hash((uint)(ix + iy * 65537 + pass * 104729 + 1));
                        if (!options.Line) seed = Hash(seed ^ areaSeed ^ Hash((uint)(schritt * stride % total % 2) + 37u));
                        int which = pool[(int)(Hash(seed) % (uint)pool.Count)];
                        var kind = species[which];
                        int px = options.Line ? nx/2 + (x % 2 == 0 ? x/2 : -(x+1)/2) : ix;
                        int py = options.Line ? ny/2 + (y % 2 == 0 ? y/2 : -(y+1)/2) : iy;
                        var p = new float2(lo.x + (hi.x - lo.x) * (px + .5f) / nx,
                            lo.y + (hi.y - lo.y) * (py + .5f) / ny);
                        // Zwei unabhaengige Kandidaten je Suchzelle, ueber ihre
                        // ganze Flaeche verteilt. Das Raster beschleunigt nur
                        // die Suche; die Pflanzpositionen sind frei.
                        if (!options.Line) p += new float2(Unit(Hash(seed + 7)) - .5f,
                            Unit(Hash(seed + 11)) - .5f) * (hi - lo) / new float2(nx, ny);
                        if (!Inside(p, local)) { result.AussenVerworfen++; continue; }
                        float edge = EdgeDistance(p, local);
                        float margin = Randabstand(kind.Tree);
                        if (edge < margin) { result.RandVerworfen++; continue; }
                        if (!options.Line)
                        {
                            // Buesche: 1,0 an der Kante, 0,7 ab 4 m. Vorher
                            // 0,25 - das war in der Flaechenmitte die
                            // eigentliche Dichtebremse, noch vor dem Regler.
                            float chance = kind.Tree ? math.lerp(.3f, 1f, math.saturate(edge / 4f))
                                : math.lerp(1f, .7f, math.saturate(edge / 4f));
                            // Weiche, unterschiedlich grosse Gruppen statt
                            // identischer Einzelwuerfe auf jedem Gruenstreifen.
                            float group = GroupDensity((origin + axis * p.x + across * p.y - anchor)
                                / (kind.Tree ? 18f : 7f), options.Seed + (uint)pass * 313u);
                            chance *= math.lerp(.35f, 1f, group);
                            if (Unit(Hash(seed + 23)) > chance)
                            { result.WuerfelVerworfen++; continue; }
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
                        if (!clear) { result.AbstandVerworfen++; continue; }
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
        /**
         * Der Platz, den eine Pflanze fuer sich braucht - ihre eigene Groesse.
         *
         * `OverrideSystem.ObjectIterator` prueft runde Objekte mit Radius
         * `m_Size.x * 0.5` gegeneinander; zwei Pflanzen vertragen sich also
         * genau bis zur Summe ihrer halben Groessen. Der Aufrufer bildet diese
         * Summe, hier steht der Anteil je Pflanze.
         *
         * Vorher stand hier eine eigene Rechnung mit 0,8, Schranken und einer
         * Division durch sqrt(3) - zusammen 46 % dessen, was CS2 durchgehen
         * laesst. Der Rest wurde gesetzt und sofort weggeblendet: der Bauzettel
         * des Nutzers meldete `Baeume=686; Overridden=231`.
         */
        /**
         * Pflanzen je Quadratmeter, relativ zu dem, was CS2 gerade noch
         * sichtbar laesst. Kommt aus den Modeinstellungen; der Geometrieteil
         * kennt sie nicht und bekommt deshalb nur die Zahl.
         */
        public static float Dichtefaktor = 1f;

        public static float Spacing(VegetationSpecies species, bool line = false)
            => math.max(0.5f, species.Spacing)
                / (float)Math.Sqrt(math.clamp(Dichtefaktor, .25f, 4f));

        /**
         * Wie weit die Pflanzenmitte von der Kante der Gruenflaeche wegbleibt.
         *
         * Nur so viel, dass der Fusspunkt im Belag steht. Mehr bringt nichts:
         * eine grosse Krone haengt ohnehin ueber, und jeder Zentimeter hier
         * loescht schmale Gruenstreifen vollstaendig - eine Flaeche unter dem
         * doppelten Randabstand bekommt nie eine Pflanze. Vorher standen hier
         * 0,65 m fuer Baeume; ein 1,2 m breiter Streifen blieb damit immer
         * leer, egal bei welcher Dichte.
         */
        public static float Randabstand(bool baum) => baum ? .15f : .10f;
        private static long Gcd(long a,long b) {while(b!=0){long next=a%b;a=b;b=next;}return a;}
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        private static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); }
        private static float Unit(uint x) => (x & 0xffffff) / 16777216f;
        private static float GroupDensity(float2 p, uint seed)
        {
            int x = (int)math.floor(p.x), y = (int)math.floor(p.y);
            var t = p - new float2(x, y);
            t = t * t * (3f - 2f * t);
            float Value(int a, int b) => Unit(Hash((uint)a * 73856093u ^ (uint)b * 19349663u ^ seed));
            return math.lerp(math.lerp(Value(x,y), Value(x+1,y), t.x),
                math.lerp(Value(x,y+1), Value(x+1,y+1), t.x), t.y);
        }
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
