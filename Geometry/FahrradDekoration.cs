using System;
using System.Linq;
using System.Text;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Gleiche Planobjekte wie der Autobau, mit den freigehaltenen Fahrradlagen. */
    public static class FahrradDekoration
    {
        /** Vanilla-Fahrradpark (BicyclePark01) - passt zum Fahrradplatz, nicht der Strassenmast. */
        public const string Parklaterne = "LightpolePark02";

        /** D hat keinen Grasring: dessen leere Signatur erkannte beim Edit
         *  2 verschobene Baumvorschlaege nicht. Die Planpunkte sind die Quelle. */
        public static string Speicherkennung(FahrradPlan plan)
        {
            if(plan==null) return string.Empty;
            // F2: Stil und Buesche (Rahmen) gehoeren seit 03.11.2026 dazu.
            var s=new StringBuilder("F2/").Append((int)plan.Layout).Append('/').Append((int)plan.Stil);
            foreach(var p in plan.Baeume)
                s.Append('|').Append((long)Math.Round(p.x*1000.0)).Append(',').Append((long)Math.Round(p.y*1000.0));
            s.Append("|B");
            foreach(var p in plan.Buesche)
                s.Append('|').Append((long)Math.Round(p.x*1000.0)).Append(',').Append((long)Math.Round(p.y*1000.0));
            return s.ToString();
        }

        public static LaternenPlan Laternen(ParkingLayout layout, float abstand)
        {
            var plan = new LaternenPlan();
            if (layout?.Fahrrad == null) return plan;
            var mitte = layout.Grundstueck.Length == 0 ? new float2(0,0)
                : layout.Grundstueck.Aggregate(new float2(0,0),(a,b)=>a+b)/layout.Grundstueck.Length;
            foreach(var p in layout.Fahrrad.Laternen)
            {
                if(plan.Laternen.Any(l=>math.distance(l.Position,p)<abstand*.45f))continue;
                plan.Laternen.Add(new LaternenPlatz { Position=p, Art=LaternenArt.Rand,
                    Richtung=math.normalizesafe(mitte-p,new float2(0,1)), Prefab=Parklaterne });
            }
            return plan;
        }

        public static VegetationPlan Vegetation(FahrradPlan fahrrad, VegetationOptions options,
            VegetationSpecies[] species)
        {
            var plan=new VegetationPlan();
            if(fahrrad==null || options==null || !options.Enabled)return plan;
            // Baeume auf Baumplaetze, Buesche (gewaehlte niedrige Arten) in den Rahmen.
            var baeume=Enumerable.Range(0,species.Length).Where(i=>species[i].Baumartig).ToArray();
            var buesche=Enumerable.Range(0,species.Length).Where(i=>!species[i].Baumartig).ToArray();
            Pflanze(plan,fahrrad.Baeume,baeume,species,options,0);
            Pflanze(plan,fahrrad.Buesche,buesche,species,options,fahrrad.Baeume.Count);
            return plan;
        }

        private static void Pflanze(VegetationPlan plan, System.Collections.Generic.List<float2> punkte, int[] arten,
            VegetationSpecies[] species, VegetationOptions options, int versatz)
        {
            if(arten.Length==0)return;
            for(var i=0;i<punkte.Count;i++)
            {
                plan.Candidates++;
                var seed=unchecked(options.Seed+(uint)(versatz+i+1)*2654435761u);
                if(seed%100>=Math.Max(0,Math.Min(100,options.Density)))continue;
                var index=arten[seed%(uint)arten.Length];
                var p=punkte[i];
                if(plan.Plants.Any(q=>math.distance(q.Position,p)<Math.Max(species[index].Spacing,species[q.Species].Spacing)))continue;
                plan.Plants.Add(new VegetationPlacement { Position=p,Species=index,Seed=seed });
            }
        }
    }
}
