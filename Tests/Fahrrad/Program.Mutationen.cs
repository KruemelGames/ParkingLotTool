using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static void Mutationen()
    {
        var ring=Formen[0].Ring;
        void Gegenprobe(string messung,Action<FahrradPlan> mutation, Fahrradlayout layout=Fahrradlayout.A)
        {
            var p=FahrradGeometrie.Baue(ring,LayoutSettings.Fahrrad(layout));
            if (!Messe(p,ring)["baut"])
            { Pruefe(false,"Mutation: bereits der Ausgang baut nicht: "+messung); return; }
            Pruefe(Messe(p,ring)[messung],"Mutation Ausgang gruen: "+messung);
            mutation(p);
            Pruefe(!Messe(p,ring)[messung],"Mutation erkannt: "+messung);
        }
        Gegenprobe("baut",p=>p.Objekte.Clear());
        Gegenprobe("Wegevertrag",p=>p.Wege[0].B=p.Wege[0].A);
        Gegenprobe("Wegevertrag",p=>p.Wege[0].Breite=7);
        Gegenprobe("Wegevertrag",p=>p.Wege.First(w=>!w.Zufahrt).Art=Fahrradwegart.Fussweg,Fahrradlayout.D);
        Gegenprobe("im Polygon",p=>p.Objekte[0].Grundriss=p.Objekte[0].Grundriss.Select(v=>v+new float2(100,0)).ToArray());
        Gegenprobe("keine Ueberlappung",p=>p.Objekte[1].Grundriss=p.Objekte[0].Grundriss.ToArray());
        Gegenprobe("erreichbar",p=>p.Wege.RemoveAll(w=>w.Zufahrt));
        Gegenprobe("erreichbar",p=>p.Objekte[0].Zugang=new float2(100,100));
        Gegenprobe("Flaechen",p=>p.Flaechen[0]=Form(0,0,0,0,0,0));
        Gegenprobe("Kapazitaet",p=>p.Objekte[0].Kapazitaet++);
        Gegenprobe("Objektvertrag",p=>p.Objekte[0].Prefab="ParkingLotDecal01");
        Gegenprobe("Drehung und Masse",p=>p.Objekte[0].Drehung+=Math.PI);
        Gegenprobe("Licht und Baeume",p=>p.Laternen.Clear());
        Gegenprobe("Licht und Baeume",p=>p.Laternen.Clear(),Fahrradlayout.D);
        var achsenprobe=FahrradGeometrie.Baue(ring,LayoutSettings.Fahrrad(Fahrradlayout.D));
        Pruefe(LaufachseFrei(achsenprobe,ring),"Laufachsen-Mutation Ausgang gruen");
        if(achsenprobe.Objekte.Count>0)
        {
            var o=achsenprobe.Objekte[0];
            var verschiebung=new float2(15,2)-o.Position;
            o.Grundriss=o.Grundriss.Select(p=>p+verschiebung).ToArray();
            Pruefe(!LaufachseFrei(achsenprobe,ring),"Buegel in der Laufachse erkannt");
        }
        foreach(var layout in Layouts)
        {
            var p=FahrradGeometrie.Baue(ring,LayoutSettings.Fahrrad(layout));
            var ziel=layout==Fahrradlayout.A?336:layout==Fahrradlayout.C?192:168;
            p.Objekte.RemoveRange(0,p.Objekte.Count/2);
            Pruefe(p.Kapazitaet<ziel*.85,"Halber Bau verfehlt Zielkapazitaet "+layout);
        }
    }
}
