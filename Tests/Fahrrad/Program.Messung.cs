using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    // Unabhaengige Messung: keine Raum-/Kollisions-/Routinghilfe der
    // Produktion. Enthaltensein wird auch entlang ALLER Kanten gemessen.
    private static Dictionary<string,bool> Messe(FahrradPlan plan, float2[] ring)
    {
        var result = new Dictionary<string,bool>();
        // Nutzer 02.11.2026: alle Wege innen sind EIN Wegtyp (Fahrradweg, 3 m), auch bei D.
        result["Wegevertrag"] = plan.Wege.All(w => math.distance(w.A,w.B) >= .0005
            && w.ErlaubtFahrrad && w.Art==Fahrradwegart.Fahrradweg && Math.Abs(w.Breite-3)<.001);
        result["baut"] = plan.Objekte.Count > 0 && plan.Wege.Count > 0;
        result["im Polygon"] = plan.Objekte.All(o => ImRing(o.Grundriss,ring))
            && plan.Wege.All(w => ImRing(Umriss(w),ring));
        result["keine Ueberlappung"] = !plan.Objekte.Where((a,i) => plan.Objekte.Skip(i+1)
            .Any(b => Schneidet(a.Grundriss,b.Grundriss))).Any()
            && !plan.Objekte.Any(o => plan.Wege.Any(w => Schneidet(o.Grundriss, Umriss(w))));
        var erreichbar = new HashSet<int>();
        var queue = new Queue<int>();
        for (var i=0;i<plan.Wege.Count;i++)
            if (plan.Wege[i].Zufahrt && plan.Wege[i].Art == Fahrradwegart.Fahrradweg)
            { erreichbar.Add(i); queue.Enqueue(i); }
        while(queue.Count>0)
        {
            var a=plan.Wege[queue.Dequeue()];
            for(var j=0;j<plan.Wege.Count;j++)
            {
                var b=plan.Wege[j];
                if(!b.ErlaubtFahrrad || erreichbar.Contains(j)) continue;
                if(new[]{a.A,a.B}.Any(p => new[]{b.A,b.B}.Any(q => math.distance(p,q)<.005)))
                { erreichbar.Add(j); queue.Enqueue(j); }
            }
        }
        // D wie Vanilla (sonde-1.log, BicycleParkingArea01): Buegel stehen auf der
        // Gehflaeche ueber dem ganzen Lot, keine Wegspur je Reihe. A/C weiter am Weg.
        result["erreichbar"] = plan.Objekte.All(o => plan.Layout==Fahrradlayout.D ? Innen(o.Zugang,ring)
                : erreichbar.Any(i => Abstand(o.Zugang,plan.Wege[i].A,plan.Wege[i].B)<.005))
            && erreichbar.Count==plan.Wege.Count && erreichbar.Count>0;
        result["Flaechen"] = plan.Flaechen.Count>0 && plan.Flaechen.All(r => Cs2Triangulierung.Dreiecke(r)==r.Length-2)
            && plan.Objekte.Where(o=>o.BodenGrundriss!=null).All(o=>Cs2Triangulierung.Dreiecke(o.BodenGrundriss)==2);
        result["Kapazitaet"] = plan.Kapazitaet>0 && plan.Kapazitaet == plan.Objekte.Sum(o=>o.Art==Fahrradobjektart.Unterstand?8:o.Art==Fahrradobjektart.Buegel?2:4);
        result["Objektvertrag"] = plan.Objekte.All(o => o.Prefab == (o.Art==Fahrradobjektart.Unterstand?"BicycleShelter01":o.Art==Fahrradobjektart.Buegel?"CommercialBikeRack01":"BikeRack03")
            && math.distance(o.Position,(o.Grundriss[0]+o.Grundriss[2])/2)<.002 && !double.IsNaN(o.Drehung)
            && (o.Art!=Fahrradobjektart.Unterstand || o.BodenPrefab=="BicycleParkingLotDecal01"));
        result["Drehung und Masse"] = plan.Objekte.All(o =>
        {
            var richtung=new float2((float)Math.Cos(o.Drehung),(float)Math.Sin(o.Drehung));
            var normal=new float2(-richtung.y,richtung.x);
            var x=o.Grundriss.Select(p=>math.dot(p-o.Position,richtung)).ToArray();
            var y=o.Grundriss.Select(p=>math.dot(p-o.Position,normal)).ToArray();
            var breite=o.Art==Fahrradobjektart.Unterstand?4:o.Art==Fahrradobjektart.Buegel?.75:1.5;
            var tiefe=o.Art==Fahrradobjektart.Unterstand?2.5:2;
            return Math.Abs(x.Max()-x.Min()-breite)<.003 && Math.Abs(y.Max()-y.Min()-tiefe)<.003
                && math.dot(normal,o.Zugang-o.Position)>0;
        });
        // Baeume nur in Restflaechen ab 3 x 3 m (Restflaechenregel 02.11.), daher keine Pflicht.
        result["Licht und Baeume"] = plan.Laternen.Count>0
            && plan.Laternen.Concat(plan.Baeume).All(p=>Innen(p,ring) && !plan.Objekte.Any(o=>Innen(p,o.Grundriss)));
        return result;
    }

    private static double Abstand(float2 p,float2 a,float2 b)
    {
        var ab=(double2)b-(double2)a; var d=(double2)p-(double2)a;
        var t=math.lengthsq(ab)<1e-12?0:math.clamp(math.dot(d,ab)/math.lengthsq(ab),0,1);
        return math.length(d-ab*t);
    }
    private static bool Innen(float2 p,float2[] r)
    {
        var innen=false;
        for(int i=0,j=r.Length-1;i<r.Length;j=i++)
        {
            if(Abstand(p,r[j],r[i])<.003) return true;
            if((r[i].y>p.y)!=(r[j].y>p.y) && p.x < (double)(r[j].x-r[i].x)*(p.y-r[i].y)/(r[j].y-r[i].y)+r[i].x) innen=!innen;
        }
        return innen;
    }
    private static bool ImRing(float2[] r,float2[] site)
    {
        for(var i=0;i<r.Length;i++)
        {
            var a=r[i]; var b=r[(i+1)%r.Length];
            var n=Math.Max(1,(int)Math.Ceiling(math.distance(a,b)/.1));
            for(var j=0;j<=n;j++) if(!Innen(math.lerp(a,b,(float)j/n),site)) return false;
        }
        return true;
    }
    private static float2[] Umriss(Fahrradweg w)
    {
        var d=math.normalizesafe(w.B-w.A); var n=new float2(-d.y,d.x)*(float)(w.Breite/2);
        return new[]{w.A-n,w.B-n,w.B+n,w.A+n};
    }
    private static bool Schneidet(float2[] a,float2[] b)
    {
        // Flaeche des konvexen Schnitts durch eigenes Sutherland-Hodgman;
        // Produktion benutzt den Trennachsensatz.
        var offset=(double2)a[0];
        var poly=a.Select(p=>(double2)p-offset).ToList();
        var clip=b.Select(p=>(double2)p-offset).ToArray();
        double Cross(double2 x,double2 y)=>x.x*y.y-x.y*y.x;
        for(var i=0;i<clip.Length && poly.Count>0;i++)
        {
            var c=clip[i];var d=clip[(i+1)%clip.Length]-c;var next=new List<double2>();
            for(var j=0;j<poly.Count;j++)
            {
                var p=poly[j];var q=poly[(j+1)%poly.Count];
                var dp=Cross(d,p-c);var dq=Cross(d,q-c);
                if(dp>=0)next.Add(p);
                if((dp>=0)!=(dq>=0))next.Add(p+(q-p)*(dp/(dp-dq)));
            }
            poly=next;
        }
        var area=0.0;
        for(var i=0;i<poly.Count;i++)area+=Cross(poly[i],poly[(i+1)%poly.Count]);
        return Math.Abs(area)/2>.003;
    }
}
