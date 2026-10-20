using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using K = ParkingLotTool.Geometry.Strassenkurve;

internal static class Program
{
    private static int _zahl;
    private static void Test(bool ok,string name) { _zahl++;if(!ok)throw new Exception(name); }
    private static int Main()
    {
        try { Run();Console.WriteLine($"Kurven: {_zahl} Pruefungen, 0 Fehler.");return 0; }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static float3 P(double x,double z) => new float3((float)x,0,(float)z);
    private static K.Bezier Gerade(float3 a,float3 b) => new K.Bezier(a,math.lerp(a,b,1f/3),math.lerp(a,b,2f/3),b);
    private static K.Bezier Bogen(double r,double von,double bis,float3 offset=default)
    {
        double a=von*Math.PI/180,b=bis*Math.PI/180,h=4.0/3*Math.Tan((b-a)/4);
        var pa=P(r*Math.Cos(a),r*Math.Sin(a));var pd=P(r*Math.Cos(b),r*Math.Sin(b));
        return new K.Bezier(pa+offset,pa+P(-r*Math.Sin(a)*h,r*Math.Cos(a)*h)+offset,
            pd-P(-r*Math.Sin(b)*h,r*Math.Cos(b)*h)+offset,pd+offset);
    }
    private static K.Teil[] Teile(params K.Bezier[] c)
    {var t=new K.Teil[c.Length];for(int i=0;i<c.Length;i++)t[i]=new K.Teil {Kurve=c[i],Quelle=i};return t;}
    private static K.Kante Road(long id,long a,long b,params K.Bezier[] c)
        => new K.Kante {Id=id,Start=a,Ende=b,Links=Teile(c),Rechts=Teile(c),Mittellinie=c.Length>0?c[0]:default,HatMittellinie=c.Length>0};
    private static K.Fang Fang(K.Kante k,int seite,int teil,float t)
        => new K.Fang {Kante=k.Id,Seite=seite,Teil=teil,T=t,Punkt=k.Seite(seite)[teil].Kurve.Punkt(t)};
    private static K.Ergebnis Einzeln(K.Kante k,int innen=0,double d=10.4)
        => K.Verfolge(Fang(k,1,0,0),Fang(k,1,k.Links.Length-1,1),
            new Dictionary<long,K.Kante>{{k.Id,k}},new Dictionary<long,K.Knoten>(),d,innen);
    private static K.Ergebnis Paar(K.Kante a,K.Kante b,K.Knoten n,int seite=-1)
        => K.Verfolge(Fang(a,1,0,.1f),Fang(b,seite,0,.1f),
            new Dictionary<long,K.Kante>{{a.Id,a},{b.Id,b}},new Dictionary<long,K.Knoten>{{n.Id,n}});
    private static double Abstand(float3 p,float3 a,float3 b)
    {
        var d=b.xz-a.xz;var den=math.lengthsq(d);
        var t=den>0?math.clamp(math.dot(p.xz-a.xz,d)/den,0,1):0;
        return math.distance(p.xz,a.xz+t*d);
    }
    // T-Kreuzung: Balken 1 und 2 entlang x, Stiel 3 nach -z. Gegenseite (z=+5) laeuft durch.
    private static void TKreuzung()
    {
        var balkenA=new K.Kante {Id=1,Start=10,Ende=20,Links=Teile(Gerade(P(0,5),P(35,5))),Rechts=Teile(Gerade(P(0,-5),P(35,-5))),
            Mittellinie=Gerade(P(0,0),P(40,0)),HatMittellinie=true};
        var balkenB=new K.Kante {Id=2,Start=20,Ende=30,Links=Teile(Gerade(P(45,5),P(80,5))),Rechts=Teile(Gerade(P(45,-5),P(80,-5))),
            Mittellinie=Gerade(P(40,0),P(80,0)),HatMittellinie=true};
        var stiel=new K.Kante {Id=3,Start=20,Ende=40,Links=Teile(Gerade(P(45,-5),P(45,-40))),Rechts=Teile(Gerade(P(35,-5),P(35,-40))),
            Mittellinie=Gerade(P(40,0),P(40,-40)),HatMittellinie=true};
        var t=new K.Knoten {Id=20,Kanten=new long[]{1,2,3},Kreuzung=true,Lage=P(40,0),
            Richtungen=new[]{new float2(-1,0),new float2(1,0),new float2(0,-1)},
            Uebergaenge=new[]{Gerade(P(35,5),P(40,5)),Gerade(P(40,5),P(45,5)),Bogen(5,90,0,P(35,-10)),Bogen(5,180,90,P(50,-10))}};
        var netz=new Dictionary<long,K.Kante>{{1,balkenA},{2,balkenB},{3,stiel}};
        var knoten=new Dictionary<long,K.Knoten>{{20,t}};
        var gegen=K.Verfolge(Fang(balkenA,1,0,.5f),Fang(balkenB,1,0,.5f),netz,knoten);
        Test(gegen.Folgt,$"T-Kreuzung Gegenseite folgt ({gegen.Grund})");
        var zurueck=K.Verfolge(Fang(balkenB,1,0,.5f),Fang(balkenA,1,0,.5f),netz,knoten);
        Test(zurueck.Folgt,$"T-Kreuzung Gegenseite rueckwaerts ({zurueck.Grund})");
        var einmuendung=K.Verfolge(Fang(balkenA,-1,0,.5f),Fang(balkenB,-1,0,.5f),netz,knoten);
        Test(einmuendung.Grund==K.Grund.Kreuzung,$"T-Kreuzung Einmuendungsseite bleibt gerade ({einmuendung.Grund})");
        Test(K.Verfolge(Fang(balkenA,-1,0,.5f),Fang(stiel,-1,0,.5f),netz,knoten).OhneFolgen,"T-Kreuzung in den Stiel: keine Kurve");
        Test(ParkingLotTool.Geometry.StrassenkurvenNetz.Verbunden(1,2,netz,knoten),"T-Kreuzung verbindet Balken");
        Test(K.Weiterfuehrung(t,3)==0,"Stiel hat keine Weiterfuehrung");
        var ohneRichtung=new K.Knoten {Id=20,Kanten=new long[]{1,2,3},Kreuzung=true,Uebergaenge=t.Uebergaenge};
        Test(K.Verfolge(Fang(balkenA,1,0,.5f),Fang(balkenB,1,0,.5f),netz,new Dictionary<long,K.Knoten>{{20,ohneRichtung}}).Grund
            ==K.Grund.Kreuzung,"Kreuzung ohne Richtungen bleibt gerade");
        var vier=new K.Knoten {Id=20,Kanten=new long[]{1,2,3,4},Kreuzung=true,Lage=P(40,0),
            Richtungen=new[]{new float2(-1,0),new float2(1,0),new float2(0,-1),new float2(0,1)},Uebergaenge=t.Uebergaenge};
        Test(K.Verfolge(Fang(balkenA,1,0,.5f),Fang(balkenB,1,0,.5f),netz,new Dictionary<long,K.Knoten>{{20,vier}}).Grund
            ==K.Grund.Kreuzung,"Vierwegkreuzung bleibt gerade");
    }
    private static void Run()
    {
        var grade=Road(1,10,11,Gerade(P(0,0),P(40,0)));
        var g=Einzeln(grade);Test(g.Folgt && g.Grund==K.Grund.Gerade,"Gerade");
        Test(g.Stuecke==(int)Math.Ceiling(40/K.Hoechstsehne) && double.IsPositiveInfinity(g.Radius),"Gerade Formel/Radius");
        foreach(var r in new[]{15,30,60,150})foreach(var grad in new[]{90,180})
        {
            var curves=grad==90?new[]{Bogen(r,0,90)}:new[]{Bogen(r,0,90),Bogen(r,90,180)};
            var road=Road(1,10,11,curves);var result=Einzeln(road);
            Test(result.Folgt,$"R{r}/{grad}: {result.Grund}");
            Console.WriteLine($"R{r}/{grad}: Radius {result.Radius:F3} m, Winkel {result.Winkel*180/Math.PI:F2}, {result.Stuecke} Stuecke");
            Test(Math.Abs(result.Radius-r)<r*.03,$"Radius R{r}: {result.Radius}");
            Test(Math.Abs(result.Winkel-grad*Math.PI/180)<.002,"Bogenwinkel");
            Test(result.Stuecke<=K.Hoechststuecke && result.Punkte.Length==result.Stuecke+1,"Ganzer Bogen");
            Test(math.distance(result.Punkte[0],curves[0].A)<.001 && math.distance(result.Punkte[result.Punkte.Length-1],curves[curves.Length-1].D)<.001,"Beide Enden");
            for(int i=1;i<result.Punkte.Length;i++)Test(math.distance(result.Punkte[i-1],result.Punkte[i])>=K.Mindestsehne-.001
                && math.distance(result.Punkte[i-1],result.Punkte[i])<=K.Hoechstsehne+.001,"Sehnenlaenge");
            foreach(var curve in curves)for(int z=0;z<=256;z++)
            {
                var probe=curve.Punkt(z/256.0);double dist=double.PositiveInfinity;
                for(int q=1;q<result.Punkte.Length;q++)dist=Math.Min(dist,Abstand(probe,result.Punkte[q-1],result.Punkte[q]));
                Test(dist<=K.Pfeilhoehe+.0005,"Unabhaengige Kurvenabdeckung");
            }
            var verschoben=new K.Bezier[curves.Length];var o=P(-1500,2000);
            for(int i=0;i<curves.Length;i++){var q=curves[i];verschoben[i]=new K.Bezier(q.A+o,q.B+o,q.C+o,q.D+o);}
            var shift=Einzeln(Road(1,10,11,verschoben));
            Test(shift.Folgt && Math.Abs(shift.Radius-result.Radius)<.04,"Weltverschiebung");
            Test(Math.Abs(shift.Stuecke-result.Stuecke)<=1,"Abtastung Weltverschiebung");
        }
        // Gerade plus enger Bogen (Spiel 19.10.): die Gerade bekommt lange Sehnen, nicht die des Bogens.
        var gemischt=Einzeln(Road(1,10,11,Gerade(P(0,0),P(48,0)),Bogen(15,-90,0,P(48,15))));
        double laengsteGerade=0;
        for(int i=1;i<gemischt.Punkte.Length;i++)
            if(gemischt.Punkte[i].x<=48.001f && Math.Abs(gemischt.Punkte[i].z)<.001f && Math.Abs(gemischt.Punkte[i-1].z)<.001f)
                laengsteGerade=Math.Max(laengsteGerade,math.distance(gemischt.Punkte[i-1],gemischt.Punkte[i]));
        Test(gemischt.Folgt && gemischt.Stuecke<=12 && laengsteGerade>=10,
            $"Adaptive Abtastung: {gemischt.Stuecke} Stuecke, laengste Sehne auf der Geraden {laengsteGerade:F1} m");
        var s=new K.Bezier(P(0,0),P(20,30),P(40,-30),P(60,0));
        Test(math.distance(s.Punkt(.5),P(30,0))<.001,"Symmetrisches S: Mittelpunkt auf Sehne");
        var sr=Einzeln(Road(1,10,11,s));
        Test(sr.Folgt && sr.Abweichung>=29.9 && sr.Grund!=K.Grund.Gerade,"S braucht Kontrollhuelle");
        var eng=Road(1,10,11,Bogen(15,0,90));
        Test(Einzeln(eng,1).Grund==K.Grund.ZuEng,"Innenversatz: zu eng");
        Test(Einzeln(eng,-1).Folgt,"Aussenversatz nicht faelschlich eng");
        // Gegen die Richtung der Strasse gefahren: der Kruemmungsmittelpunkt liegt jetzt rechts.
        var engNetz=new Dictionary<long,K.Kante>{{1,eng}};
        var rueckLinks=K.Verfolge(Fang(eng,1,0,1),Fang(eng,1,0,0),engNetz,new Dictionary<long,K.Knoten>(),10.4,1);
        var rueckRechts=K.Verfolge(Fang(eng,1,0,1),Fang(eng,1,0,0),engNetz,new Dictionary<long,K.Knoten>(),10.4,-1);
        Test(rueckLinks.Folgt,$"Rueckwaerts, Lot links (aussen): nicht eng ({rueckLinks.Grund})");
        Test(rueckRechts.Grund==K.Grund.ZuEng,$"Rueckwaerts, Lot rechts (innen): zu eng ({rueckRechts.Grund})");
        var zwei=Road(1,10,11,Bogen(15,0,45),Bogen(15,45,90));var zweiNetz=new Dictionary<long,K.Kante>{{1,zwei}};
        var rueckTeil=K.Verfolge(Fang(zwei,1,1,.9f),Fang(zwei,1,0,.1f),zweiNetz,new Dictionary<long,K.Knoten>(),10.4,1);
        Test(rueckTeil.Folgt,$"Rueckwaerts ueber zwei Teile, Lot aussen: nicht eng ({rueckTeil.Grund})");
        Test(Einzeln(eng,0).Folgt,"Offene Shape: Innenseite unbekannt");
        Test(Einzeln(Road(1,10,11,Bogen(30,0,90)),1).Folgt,"Innenversatz R30");
        KurvenzettelTests.Run(Test,Einzeln(Road(1,10,11,Bogen(60,0,45),Bogen(60,45,90))));
        var a=Road(1,10,20,Bogen(60,0,45));
        var b=Road(2,30,20,Bogen(60,90,45));
        var n=new K.Knoten {Id=20,Kanten=new long[]{1,2}};
        Test(Paar(a,b,n).Folgt,"Zwei Segmente, Edge-Orientierung gedreht");
        Test(Paar(a,b,n,1).Grund==K.Grund.Seitenwechsel,"Seitenwechsel");
        n.Kanten=new long[]{1,2,3};Test(Paar(a,b,n).Grund==K.Grund.Kreuzung,"Knoten Grad 3");
        TKreuzung();
        // Kante neben einer Kurve verschoben: das Ende gleitet auf den Bordstein (Schnitt Gerade/Bogen).
        var bogen=Road(1,10,11,Bogen(60,0,90));
        double x30=60*Math.Cos(Math.PI/6);
        var schnitte=K.Bordsteinschnitte(new[]{bogen},new float2((float)x30,-10),new float2(0,1),new float2((float)x30,25),64);
        Test(schnitte.Count>=1 && Math.Abs(schnitte[0].Punkt.x-x30)<.001 && Math.Abs(schnitte[0].Punkt.z-30)<.05,$"Bordsteinschnitt bei 30 Grad ({schnitte.Count})");
        var bogenNetz=new Dictionary<long,K.Kante>{{1,bogen}};
        Test(K.Verfolge(Fang(bogen,1,0,0),schnitte.Find(f=>f.Seite==1),bogenNetz,new Dictionary<long,K.Knoten>()).Folgt,"Bordsteinschnitt ist verfolgbarer Fang");
        Test(K.Bordsteinschnitte(new[]{bogen},new float2(200,0),new float2(0,1),new float2(200,0),64).Count==0,"Kein Schnitt ausser Reichweite");
        n.Kanten=new long[]{1,2};n.Kreisverkehr=true;Test(Paar(a,b,n).Grund==K.Grund.Kreisverkehr,"Kreisverkehrflag");
        n.Kreisverkehr=false;n.Kreuzung=true;Test(Paar(a,b,n).Grund==K.Grund.Kreuzung,"Kreuzungsflag");
        n.Kreuzung=false;n.Hoehe=true;Test(Paar(a,b,n).Grund==K.Grund.Hoehe,"Knotenhöhe");
        n.Hoehe=false;b.Hoehe=true;Test(Paar(a,b,n).Grund==K.Grund.Hoehe,"Rampenkante");b.Hoehe=false;
        var c=b.Links[0].Kurve;var jump=new float3(0,1,0);
        b.Rechts=Teile(new K.Bezier(c.A+jump,c.B+jump,c.C+jump,c.D+jump));
        Test(Paar(a,b,n).Grund==K.Grund.Hoehe,"Hoehensprung");b.Rechts=b.Links;
        var gap=P(2,0);b.Rechts=Teile(new K.Bezier(c.A+gap,c.B+gap,c.C+gap,c.D+gap));
        Test(Paar(a,b,n).Grund==K.Grund.Luecke,"Luecke");b.Rechts=b.Links;
        var end=a.Links[0].Kurve.D;
        b.Rechts=Teile(Gerade(end+P(30,0),end));
        Test(Paar(a,b,n).Grund==K.Grund.Tangentensprung,"Tangentensprung");b.Rechts=b.Links;
        // Scharfer Grad-2-Knick = andere Strasse, auch wenn die native Abrundung glatt waere.
        var vorEcke=Road(1,10,20,Gerade(P(0,0),P(40,0)));
        var nachEcke=Road(2,20,30,Gerade(P(40,0),P(40,40)));
        var weiterGerade=Road(2,20,30,Gerade(P(40,0),P(80,0)));
        var knick=new K.Knoten {Id=20,Kanten=new long[]{1,2}};
        Test(Paar(vorEcke,nachEcke,knick,1).Grund==K.Grund.EckeAndereStrasse,"Knick = Ecke");
        Test(Paar(vorEcke,nachEcke,knick,1).OhneFolgen,"Ecke ohne Fehler");
        Test(Paar(vorEcke,weiterGerade,knick,1).Grund!=K.Grund.EckeAndereStrasse,"Glatter Knoten keine Ecke");
        // Linkskurve um einen Knick: rechts (aussen) rundet CS2 ab, links (innen) laeuft es spitz zu.
        var innenA=Gerade(P(0,5),P(35,5));var aussenA=Gerade(P(0,-5),P(40,-5));
        var innenB=Gerade(P(35,5),P(35,40));var aussenB=Gerade(P(45,0),P(45,40));
        var knickA=new K.Kante {Id=1,Start=10,Ende=20,Links=Teile(innenA),Rechts=Teile(aussenA),
            Mittellinie=Gerade(P(0,0),P(40,0)),HatMittellinie=true};
        var knickB=new K.Kante {Id=2,Start=20,Ende=30,Links=Teile(innenB),Rechts=Teile(aussenB),
            Mittellinie=Gerade(P(40,0),P(40,40)),HatMittellinie=true};
        var knickKnoten=new K.Knoten {Id=20,Kanten=new long[]{1,2},
            Uebergaenge=new[]{Bogen(5,-90,-45,P(40,0)),Bogen(5,-45,0,P(40,0))}};
        var knickNetz=new Dictionary<long,K.Kante>{{1,knickA},{2,knickB}};
        var knickKnotenNetz=new Dictionary<long,K.Knoten>{{20,knickKnoten}};
        var aussen=K.Verfolge(Fang(knickA,-1,0,.5f),Fang(knickB,-1,0,.5f),knickNetz,knickKnotenNetz,10.4,0);
        Test(aussen.Folgt && aussen.Grund==K.Grund.Keiner,$"Aeussere Ecke folgt der Abrundung ({aussen.Grund})");
        var innen=K.Verfolge(Fang(knickA,1,0,.5f),Fang(knickB,1,0,.5f),knickNetz,knickKnotenNetz,10.4,0);
        Test(innen.Grund==K.Grund.EckeAndereStrasse,$"Innere Ecke bleibt Ecke ({innen.Grund})");
        var aussenZurueck=K.Verfolge(Fang(knickB,-1,0,.5f),Fang(knickA,-1,0,.5f),knickNetz,knickKnotenNetz,10.4,0);
        Test(aussenZurueck.Folgt,$"Aeussere Ecke auch rueckwaerts ({aussenZurueck.Grund})");
        // Eckenfang: Tangentenschnitt (40,0) vor der Abrundung, Rand beginnt erst bei 5 m.
        var randA=Gerade(P(0,0),P(35,0));var randB=Gerade(P(40,5),P(40,40));
        var versatz=P(0,-3);
        var eckeA=new K.Kante {Id=1,Start=10,Ende=20,Links=Teile(randA),
            Rechts=Teile(new K.Bezier(randA.A+versatz,randA.B+versatz,randA.C+versatz,randA.D+versatz)),
            Mittellinie=randA,HatMittellinie=true};
        var eckeB=new K.Kante {Id=2,Start=20,Ende=30,Links=Teile(randB),
            Rechts=Teile(new K.Bezier(randB.A+versatz,randB.B+versatz,randB.C+versatz,randB.D+versatz)),
            Mittellinie=randB,HatMittellinie=true};
        var eckenNetz=new Dictionary<long,K.Kante>{{1,eckeA},{2,eckeB}};
        var eckpunkt=P(40,0);
        var ankerA=ParkingLotTool.Geometry.Strassenecke.Anker(eckeA,randA,false,eckpunkt,new float2(1,0));
        var ankerB=ParkingLotTool.Geometry.Strassenecke.Anker(eckeB,randB,false,eckpunkt,new float2(0,1));
        Test(ParkingLotTool.Geometry.Strassenecke.Bestaetige(eckpunkt,ankerA,ankerB,eckenNetz,out var ecke),"Ecke bestaetigt");
        var vonEcke=K.Verfolge(ecke.AlsFang(),Fang(eckeB,1,0,.5f),eckenNetz,
            new Dictionary<long,K.Knoten>{{20,new K.Knoten {Id=20,Kanten=new long[]{1,2}}}},10.4,0);
        Test(vonEcke.Folgt,$"Ecke -> Strasse folgt: {vonEcke.Grund}");
        Test(math.distance(vonEcke.Punkte[0],eckpunkt)<.001,"Ecke ist erster Punkt");
        // Gebogener Bordstein: die Fanglinie verfehlt das Randende um Zentimeter - noch eine Ecke.
        var knapp=P(40,.08);
        Test(ParkingLotTool.Geometry.Strassenecke.Bestaetige(knapp,
            ParkingLotTool.Geometry.Strassenecke.Anker(eckeA,randA,false,knapp,new float2(1,0)),
            ParkingLotTool.Geometry.Strassenecke.Anker(eckeB,randB,false,knapp,new float2(0,1)),eckenNetz,out var knappeEcke),
            "Ecke 8 cm neben dem Randende");
        var vonKnapp=K.Verfolge(knappeEcke.AlsFang(),Fang(eckeB,1,0,.5f),eckenNetz,
            new Dictionary<long,K.Knoten>{{20,new K.Knoten {Id=20,Kanten=new long[]{1,2}}}},10.4,0);
        Test(vonKnapp.Folgt,$"Knappe Ecke -> Strasse folgt: {vonKnapp.Grund}");
        var zuWeit=P(40,.3);
        Test(!ParkingLotTool.Geometry.Strassenecke.Bestaetige(zuWeit,
            ParkingLotTool.Geometry.Strassenecke.Anker(eckeA,randA,false,zuWeit,new float2(1,0)),
            ParkingLotTool.Geometry.Strassenecke.Anker(eckeB,randB,false,zuWeit,new float2(0,1)),eckenNetz,out _),
            "30 cm neben dem Randende keine Ecke");
        // Glatter Durchlaufknoten in der Kurve ist keine Ecke, auch wenn sich die Randtangenten schneiden.
        var glattA=Road(1,10,20,Bogen(60,0,45));var glattB=Road(2,20,30,Bogen(60,45,90));
        glattA.Rechts=Teile(Bogen(57,0,45));glattB.Rechts=Teile(Bogen(57,45,90));
        var glattNetz=new Dictionary<long,K.Kante>{{1,glattA},{2,glattB}};
        var naht=glattA.Links[0].Kurve.D;
        var glattAnkerA=ParkingLotTool.Geometry.Strassenecke.Anker(glattA,glattA.Links[0].Kurve,false,naht,
            math.normalize(glattA.Links[0].Kurve.D.xz-glattA.Links[0].Kurve.C.xz));
        var glattAnkerB=ParkingLotTool.Geometry.Strassenecke.Anker(glattB,glattB.Links[0].Kurve,false,naht,
            math.normalize(glattB.Links[0].Kurve.B.xz-glattB.Links[0].Kurve.A.xz));
        Test(glattAnkerA.Eins.Vorhanden && glattAnkerB.Eins.Vorhanden,"Glatte Naht: Anker vorhanden");
        Test(!ParkingLotTool.Geometry.Strassenecke.Bestaetige(naht,glattAnkerA,glattAnkerB,glattNetz,out _),"Glatte Naht keine Ecke");
        // Deutlich neben dem Bordstein (Ecke vor enger Innenrundung) zaehlt der Schnitt doch als Ecke.
        var tangenteNaht=math.normalize(glattA.Links[0].Kurve.D-glattA.Links[0].Kurve.C);
        var weit=naht+tangenteNaht*2f;
        var weitA=ParkingLotTool.Geometry.Strassenecke.Anker(glattA,glattA.Links[0].Kurve,false,weit,tangenteNaht.xz);
        var weitB=ParkingLotTool.Geometry.Strassenecke.Anker(glattB,glattB.Links[0].Kurve,false,weit,tangenteNaht.xz);
        Test(ParkingLotTool.Geometry.Strassenecke.Bestaetige(weit,weitA,weitB,glattNetz,out _),"Glatter Knoten, Schnitt 2 m daneben: Ecke");
        // Begradigtes Knotenstueck hinter dem Randende: gerade zum Rand, dann der Bogen.
        var randAnfang=glattA.Links[0].Kurve.A;
        var verlaengert=ParkingLotTool.Geometry.Strassenecke.Verlaengerung(randAnfang+P(0,-3),
            new K.Fang {Kante=1,Seite=1,Teil=0,T=0,Punkt=randAnfang});
        var vonEnde=K.Verfolge(verlaengert.AlsFang(),Fang(glattA,1,0,.8f),glattNetz,new Dictionary<long,K.Knoten>(),10.4,0);
        Test(vonEnde.Folgt && vonEnde.Grund==K.Grund.Keiner,$"Verlaengerung folgt: {vonEnde.Grund}");
        Test(math.distance(vonEnde.Punkte[0],randAnfang+P(0,-3))<.001,"Verlaengerung ist erster Punkt");
        var schief=ParkingLotTool.Geometry.Strassenecke.Verlaengerung(randAnfang+P(2,-3),
            new K.Fang {Kante=1,Seite=1,Teil=0,T=0,Punkt=randAnfang});
        Test(!K.Verfolge(schief.AlsFang(),Fang(glattA,1,0,.8f),glattNetz,new Dictionary<long,K.Knoten>(),10.4,0).Folgt,"Schiefe Verlaengerung folgt nicht");
        var falsch=new K.Bezier(P(0,0),P(double.NaN,0),P(20,0),P(40,0));
        Test(Einzeln(Road(1,10,11,falsch)).Grund==K.Grund.Ungueltig,"NaN");
        falsch.B=P(double.PositiveInfinity,0);Test(Einzeln(Road(1,10,11,falsch)).Grund==K.Grund.Ungueltig,"Inf");
        Test(Einzeln(Road(1,10,11,Gerade(P(1,1),P(1,1)))).Grund==K.Grund.Nullsehne,"Nullsehne");
        var gross=Einzeln(Road(1,10,11,Bogen(1000,0,90),Bogen(1000,90,180)));
        Test(gross.Grund==K.Grund.Budget && gross.Punkte.Length==0,"Budget: nie Teilbogen");
        var normal=Bogen(60,0,45);normal.Teile(.5f,out var links,out var rechts);
        var sortiert=K.Ordne(rechts.Umgekehrt(),links.Umgekehrt(),normal.A);
        Test(math.distance(sortiert[0].Kurve.A,normal.A)<.001 && math.distance(sortiert[1].Kurve.D,normal.D)<.001,"Native Haelften ordnen");
        Test(sortiert[0].Quelle==1 && sortiert[0].Umgekehrt,"Native Herkunft behalten");
        var einzel=K.Ordne(default,normal,normal.A);
        Test(einzel.Length==1 && einzel[0].Quelle==1,"Leere native Halbkurve auslassen");
        Test(K.Ordne(default,default,P(0,0)).Length==0,"Keine erfundene native Halbkurve");
        // Native Teilgrenze knapp neben dem Klick darf keinen Kurzrest erzwingen.
        var rest=Road(7,1,2,Bogen(60,0,45),Bogen(60,45,90));
        var restErgebnis=K.Verfolge(Fang(rest,1,0,.999f),Fang(rest,1,1,.9f),
            new Dictionary<long,K.Kante>{{7,rest}},new Dictionary<long,K.Knoten>());
        Test(restErgebnis.Folgt,"Keine Kurzreste an nativer Halbkurve");
        var off=P(-1500,2000);var ra=Bogen(60,0,45,off);var rb=Bogen(60,45,90,off);
        var restWelt=Road(7,1,2,ra,rb);
        foreach(var nahe in new[]{.9f,.99f,.999f,.9999f,.99999f}) {
        var restWeltErgebnis=K.Verfolge(Fang(restWelt,1,0,nahe),Fang(restWelt,1,1,.9f),
            new Dictionary<long,K.Kante>{{7,restWelt}},new Dictionary<long,K.Knoten>(),10.4,1);
        Test(restWeltErgebnis.Folgt,$"Native Kurzgrenze in Welt: {restWeltErgebnis.Grund}, R={restWeltErgebnis.Radius}, t={nahe}"); }

        var v=Road(8,1,2,Gerade(P(0,0),P(10,0)));var roadW=Road(9,3,2,Gerade(P(30,0),P(20,0)));
        var verbindung=Gerade(P(10,0),P(20,0));verbindung.Teile(.5f,out var vl,out var vr);
        var node=new K.Knoten {Id=2,Kanten=new long[]{8,9},Uebergaenge=new[]{vl,vr}};
        Test(Paar(v,roadW,node).Folgt,"Zwei echte native Knotenhalbkurven");
        node.Uebergaenge=new[]{verbindung,vl,vr};
        Test(Paar(v,roadW,node).Grund==K.Grund.Mehrdeutig,"Mehrdeutiger nativer Uebergang");
        Test(K.BestaetigeFang(grade,1,0,.5f,P(20,0),out var f0),"Echter Fang bestaetigt");
        Test(!K.BestaetigeFang(grade,1,0,.5f,P(20,.1),out _),"Tangentenschnitt neben echter Kurve abweisen");
        Test(!K.BestaetigeFang(grade,1,0,.5f,new float3(20,1,0),out _),"Fang muss in 3D passen");
        var umgeordnet=Road(1,10,11);umgeordnet.Links=sortiert;
        Test(K.BestaetigeFang(umgeordnet,1,1,1,normal.A,out var fo) && fo.T==0,"Orientiertes t aus nativer Quelle");
        NativeKnoten.Pruefe(Test);
        // Das symmetrische S liegt in der Mitte auf seiner Sehne. Die
        // produktive Sehnenpruefung muss beide Ausbuchtungen trotzdem sehen.
        var pruefer = typeof(K).GetMethod("MesseSehne", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var sinus = new K.Bezier(P(0, 0), P(5, 1), P(10, -1), P(15, 0));
        var argumente = new object[] { sinus, sinus.A, sinus.D, 0.0, 0.0 };
        pruefer.Invoke(null, argumente);
        Test((double)argumente[3] > .28, "Sehnenmessung sieht beide S-Ausbauchungen statt nur Mittelpunkt");
        KurvenkanteTests.Pruefe(Test);
        for(int i=0;i<1000;i++)K.BestaetigeFang(grade,1,0,.5f,P(20,0),out _);
        var bytes=GC.GetAllocatedBytesForCurrentThread();var uhr=Stopwatch.StartNew();
        // Stopwatch vor dem Allokationszaehler anlegen.
        bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)K.BestaetigeFang(grade,1,0,.5f,P(20,0),out _);
        long allokiert=GC.GetAllocatedBytesForCurrentThread()-bytes;uhr.Stop();
        Test(allokiert==0,"Fangbestaetigung ohne Allokation");
        Console.WriteLine($"Herkunftsbestaetigung: {uhr.Elapsed.TotalMilliseconds/100000:F6} ms/Aufruf, {allokiert} Bytes/100000 Aufrufe (ohne nativen Fang)");
        // Warmmessung ohne Spiel: produktive Verfolgung samt Sampling/Radius.
        var times=new List<double>();for(int i=0;i<101;i++)
        {var w=Stopwatch.StartNew();Paar(a,b,n);w.Stop();if(i>0)times.Add(w.Elapsed.TotalMilliseconds);}
        times.Sort();Console.WriteLine($"Verfolgung zwei Kanten: Median {times[50]:F3} ms, p95 {times[94]:F3} ms (100 Laeufe)");
    }
}
