using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int MesseSpielnetz()
    {
        var kurz=0;var gesamt=0;
        foreach(var layout in Layouts)
        foreach(var f in Formen)
        foreach(var grad in new[]{0,17,90,203})
        foreach(var ort in new[]{new float2(0,0),new float2(-1250,842),new float2(8000,-8000)})
        foreach(var rueckwaerts in new[]{false,true})
        {
            var a=grad*Math.PI/180;
            var ring=f.Ring.Select(p=>new float2((float)(p.x*Math.Cos(a)-p.y*Math.Sin(a)),
                (float)(p.x*Math.Sin(a)+p.y*Math.Cos(a)))+ort).ToArray();
            if(rueckwaerts)Array.Reverse(ring);
            var plan=FahrradGeometrie.Baue(ring,LayoutSettings.Fahrrad(layout));
            var netz=FahrradBaukurse.Plan(plan);
            foreach(var w in netz)
                if(math.distance(w.A,w.B)<ParkingGeometry.NetzMindestlaenge)
                {kurz++;Console.WriteLine($"KURZ {layout}/{f.Name}/{grad}/{ort}/{rueckwaerts}: {w.A} -> {w.B}: {math.distance(w.A,w.B)} m");}
            foreach(var w in plan.Wege.Where(w=>w.Art==Fahrradwegart.Fahrradweg))
                foreach(var p in new[]{w.A,w.B,(w.A+w.B)/2})
                    if(!netz.Any(n=>Abstand(p,n.A,n.B)<.005))
                    {kurz++;Console.WriteLine("FEHLER: Baukurse verlieren ein Wegstueck");}
            gesamt+=netz.Length;
        }
        Console.WriteLine($"SPIELNETZ: {gesamt} Kurse, {kurz} zu kurz");
        return kurz==0?0:1;
    }
}
