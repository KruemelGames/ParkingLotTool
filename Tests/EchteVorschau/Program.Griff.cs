using System;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static void GriffPruefungen()
    {
        var lot=new[] {new float2(0,0),new float2(100,0),new float2(100,100),new float2(0,100)};
        var l=new[] {new float2(0,0),new float2(100,0),new float2(100,30),new float2(30,30),new float2(30,100),new float2(0,100)};
        foreach(var punkt in new[] {new float2(50,50),new float2(-10,50),new float2(110,120),new float2(50,0)})
        {
            var k=Vorschaugriff.Klemme(lot,punkt);
            Pruefe(Vorschaugriff.Enthaelt(lot,k),"Klemmen bleibt im Lot einschliesslich Rand");
            if(punkt.x<0) Pruefe(math.distance(k,new float2(0,50))<.001f,"Naechster statt beliebiger Innenpunkt");
        }
        Pruefe(!Vorschaugriff.Enthaelt(l,new float2(60,60)),"Konkaver Ruecksprung ist ausserhalb");
        Pruefe(math.distance(Vorschaugriff.Klemme(l,new float2(60,50)),new float2(60,30))<.001f,"Naechste konkave Randkante");
        Pruefe(Vorschaugriff.Enthaelt(lot,Vorschaugriff.Klemme(lot,new float2(float.NaN,0))),"NaN-Zug faellt sicher zurueck");
        for(int mask=0;mask<16;mask++)
        {
            bool a=(mask&1)!=0,b=(mask&2)!=0,c=(mask&4)!=0,d=(mask&8)!=0;
            Pruefe(Vorschaugriff.Beginnt(a,b,c,d)==(a&&b&&!c&&!d),"Griffprioritaet mit Bau/Abbruch");
            Pruefe(Vorschaugriff.Trifft(a,b,c,new float2(3,4),default,5)==(a&&b&&!c),"Hover mit Randtreffer und anderem Zug");
        }
        Pruefe(!Vorschaugriff.Trifft(true,true,false,new float2(5.01f,0),default,5),"Ausserhalb Radius keine Klickuebernahme");
        Pruefe(!Vorschaugriff.Trifft(true,true,false,new float2(float.NaN,0),default,5),"NaN-Hover aus");
        var g=new Vorschaugriff();var b0=g.Startbereich(lot,new float2(3,4));
        Pruefe(b0.Mitte.Equals(lot[0]),"Neue Shape startet an Punkt 0");
        g.MerkeAusgabe(new OberflaechenMuster.Bereich(b0.Mitte,10,b0.Richtung));
        Pruefe(g.Beginne(new float2(1,1)),"Griff beginnt");g.Bewege(new float2(81,71));
        Pruefe(g.Stand==0 && g.Bereich.Value.Mitte.Equals(lot[0]),"Ziehen aendert Ausgabe/Generation noch nicht");
        g.Ende(lot,false);long stand=g.Stand;
        Pruefe(g.Bereich.Value.Mitte.Equals(new float2(80,70)),"Letzte Mausposition mit Griffversatz");
        var verschoben=g.Startbereich(lot,new float2(1,0));
        Pruefe(verschoben.Halbseite==10 && verschoben.Richtung.Equals(b0.Richtung),"Gleiche Groesse und Drehung nach Verschieben");
        g.Beginne(verschoben.Mitte);g.Bewege(new float2(20,20));g.Ende(lot,true);
        Pruefe(g.Bereich.Value.Equals(verschoben) && g.Stand>stand,"ESC/Rechtsklick stellen alte Lage wieder her");
        g.PruefeForm(lot);Pruefe(g.Bereich.HasValue,"Gueltige Mitte ueberlebt Shape-Aenderung");
        g.PruefeForm(l);Pruefe(!g.Bereich.HasValue,"Ungueltige Mitte faellt auf Punkt 0 zurueck");
        g.Startbereich(lot,new float2(1,0));g.Vergiss();Pruefe(!g.Bereich.HasValue && !g.Zieht,"Reset ohne gespeicherten Griff");
        foreach(var ring in new[] {l,l.Reverse().ToArray(),l.Select(p=>p+new float2(8000,-12000)).ToArray()})
        {
            var delta=ring==l || ring[0].x<1000 ? new float2(0) : new float2(8000,-12000);
            var q=Vorschaugriff.Klemme(ring,new float2(60,50)+delta);
            Pruefe(Vorschaugriff.Enthaelt(ring,q),"Konkav und ortsunabhaengig klemmen");
            Pruefe(math.distance(q,new float2(60,30)+delta)<.005f,"Naechste Kante auch fern vom Nullpunkt");
        }
        var neu=new OberflaechenMuster.NeuesterStand();var layout=new object();
        long alt=neu.Merke(layout,"1");long aktuell=neu.Merke(layout,"2");
        Pruefe(!neu.IstAktuell(alt) && neu.IstAktuell(aktuell),"Positionsstand verwirft alte Ergebnisse");
        // Bestehende Auswahlpfade mit frei gesetztem, gedrehtem Bereich.
        var b1=new OberflaechenMuster.Bereich(new float2(60,60),10,new float2(3,4));
        var plan=OberflaechenMuster.Berechne(b1,new[] {new[] {lot}});
        Pruefe(plan.Stuecke.Length>0 && plan.Ausschnitt.Equals(b1),"Flaechenschnitt behaelt verschobene Groesse");
        var auswahl=EchteVorschau.Waehle(b1,new[] {new float2(60,60),new float2(0,0)});
        Pruefe(auswahl.Indizes.SequenceEqual(new[] {0}) && auswahl.Ausschnitt.Equals(b1),"Objektwahl nutzt verschobenen Bereich");
        // Warm, nur die je Bild verwendeten reinen Operationen; keine Logger/Closure im Messfenster.
        g.MerkeAusgabe(b1);g.Beginne(b1.Mitte);for(int i=0;i<100;i++) g.Bewege(new float2(i%80,i%70));
        long bytes=GC.GetAllocatedBytesForCurrentThread();var uhr=Stopwatch.StartNew();
        long basis=GC.GetAllocatedBytesForCurrentThread();int treffer=0;
        for(int i=0;i<100000;i++) {g.Bewege(new float2(i%80,i%70));if(Vorschaugriff.Trifft(true,true,false,g.Ziel,b1.Mitte,8))treffer++;}
        double ms=uhr.Elapsed.TotalMilliseconds;long kosten=GC.GetAllocatedBytesForCurrentThread()-basis;
        Pruefe(kosten==0,"Keine Allokation durch Zug/Hover je Bild");
        Console.WriteLine($"Griffmessung: 100000 Zug/Hover-Paare, {ms:F3} ms, {kosten} Bytes, {treffer} Treffer (ohne Rendering).");
    }
}
