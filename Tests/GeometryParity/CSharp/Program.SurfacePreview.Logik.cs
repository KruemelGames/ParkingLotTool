using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static void PruefeMusterEntscheidung(Action<bool,string> test)
    {
        for (int bits=0; bits<512; bits++)
        {
            bool B(int n) => (bits & (1<<n))!=0;
            test(OberflaechenMuster.DarfZeigen(B(0),B(1),B(2),B(3),B(4),B(5),B(6),B(7),B(8)) == (bits==23),
                "Sichtbarkeitsmatrix " + bits);
        }
        var neu = new OberflaechenMuster.NeuesterStand();
        object layout = new object();
        long eins=neu.Merke(layout,"Asphalt");
        test(neu.Merke(layout,"Asphalt")==eins,"Keine Neuberechnung ohne Aenderung");
        long zwei=neu.Merke(layout,"Gras");
        long drei=neu.Merke(layout,"Beton");
        test(!neu.IstAktuell(eins) && !neu.IstAktuell(zwei) && neu.IstAktuell(drei),"Nur letzte schnelle Wahl gilt");
        long vier=neu.Merke(new object(),"Beton");
        test(!neu.IstAktuell(drei) && neu.IstAktuell(vier),"Neues fertiges Layout ersetzt alten Stand");
        neu.Stoppe();
        test(!neu.IstAktuell(vier),"ESC sperrt den gerade ausgegebenen Satz");
        test(neu.Merke(layout,"neue Wahl")>vier,"Neue Wahl erzeugt neuen Stand");
        long fuenf=neu.Stand;
        test(neu.IstAktuell(fuenf),"Neue Wahl darf nach ESC wieder zeigen");
        neu.Stoppe();neu.Merke(layout,"neue Wahl");
        test(!neu.IstAktuell(fuenf),"Gleicher Wunsch darf nach ESC nicht wieder auftauchen");
        neu.Verwirf();test(!neu.IstAktuell(fuenf),"Laden/Abschalten entwertet Ergebnisse");
    }

    private static double Flaeche(float2[] q)
        => Math.Abs((q[1].x-(double)q[0].x)*(q[2].y-(double)q[0].y)-(q[1].y-(double)q[0].y)*(q[2].x-(double)q[0].x))*.5;

    private static bool ImDreieck(float2 p, float2[] q)
    {
        double K(float2 a,float2 b) => (b.x-(double)a.x)*(p.y-(double)a.y)-(b.y-(double)a.y)*(p.x-(double)a.x);
        var a=K(q[0],q[1]);var b=K(q[1],q[2]);var c=K(q[2],q[0]);
        return (a>=0 && b>=0 && c>=0) || (a<=0 && b<=0 && c<=0);
    }

    private static void PruefeMustermaske(Action<bool,string> test)
    {
        var zufall = new System.Random(17102026);
        var faelle = new[] {
            new[] {new float2(-60,-60),new float2(60,-60),new float2(0,60)},
            new[] {new float2(0,0),new float2(1,0),new float2(0,1)},
            new[] {new float2(30,30),new float2(40,30),new float2(30,40)},
            new[] {new float2(-20,-20),new float2(20,-20),new float2(20,20)},
            new[] {new float2(-40,0),new float2(40,0),new float2(0,.0008f)},
        };
        foreach (var ort in new[] { new float2(0), new float2(8000,-12000) })
        foreach (var fall in faelle)
        foreach (bool umgekehrt in new[] { false,true })
        {
            var q=fall.Select(p=>p+ort).ToArray();if(umgekehrt)Array.Reverse(q);
            var rest=OberflaechenMuster.Ausserhalb(q[0],q[1],q[2],new OberflaechenMuster.Bereich(ort,20));
            test(rest.Length<=24,"Konstante Zahl Farbnetz-Restdreiecke");
            test(rest.All(r=>r.Length==3 && r.All(p=>math.all(math.isfinite(p)))),"Endliche Restdreiecke");
            test(rest.Sum(Flaeche)<=Flaeche(q)+.1,"Keine vervielfachte Flaeche");
            for (int i=0;i<3000;i++)
            {
                var p=ort+new float2((float)zufall.NextDouble()*150-75,(float)zufall.NextDouble()*150-75);
                // Randpunkte liegen auf zwei geschlossenen Dreiecken; nur echte Innenpunkte zaehlen.
                double x=Math.Abs(p.x-(double)ort.x),y=Math.Abs(p.y-(double)ort.y);
                if(Math.Abs(x-20)<.002 || Math.Abs(y-20)<.002)continue;
                bool soll=ImDreieck(p,q) && !(x<20 && y<20);
                int deckung=rest.Count(r=>ImDreieck(p,r));
                test((deckung>0)==soll,"Farbnetzmaske Deckung/Loch");
            }
        }
        foreach(var p in new[] {new float2(float.NaN,0),new float2(0,float.PositiveInfinity)})
        {
            bool abgewiesen=false;try{OberflaechenMuster.Ausschnitt(faelle[0],p);}catch(ArgumentException){abgewiesen=true;}
            test(abgewiesen,"Ungueltige Quadratmitte");
            abgewiesen=false;try{OberflaechenMuster.Ausserhalb(p,new float2(0),new float2(1),new OberflaechenMuster.Bereich(new float2(0),20));}catch(ArgumentException){abgewiesen=true;}
            test(abgewiesen,"Ungueltiges Farbnetzdreieck");
        }
    }
    private static void PruefeMusterschnitt(Action<bool,string> test)
    {
        float2[] Rechteck(float x,float y,float b,float h) => new[] {new float2(x,y),new float2(x+b,y),new float2(x+b,y+h),new float2(x,y+h)};
        var l = new[] {new float2(0,0),new float2(100,0),new float2(100,20),new float2(20,20),new float2(20,100),new float2(0,100)};
        var u = new[] {new float2(0,0),new float2(100,0),new float2(100,100),new float2(80,100),new float2(80,20),new float2(20,20),new float2(20,100),new float2(0,100)};
        foreach(var lot in new[] {Rechteck(0,0,100,80),l,u})
        foreach(var ort in new[] {new float2(0),new float2(8000,-12000)})
        foreach(var mitte in new[] {lot[0],new float2(50,40),new float2(20,20)})
        {
            var q=lot.Select(p=>p+ort).ToArray();
            var ausschnitt=OberflaechenMuster.Ausschnitt(q,mitte+ort);
            var teile=OberflaechenSchnitt.Schneide(q,ausschnitt);
            double anteil=teile.Sum(p=>Math.Abs(OberflaechenSchnitt.Flaeche(p)))/Math.Abs(OberflaechenSchnitt.Flaeche(q));
            test(Math.Abs(anteil-.25)<.0002,"25 Prozent auch bei Ecke/Mitte/konkav/Weltort: "+anteil);
            var plan=OberflaechenMuster.Berechne(q,mitte+ort,new[] {new[] {q}});
            test(plan.Stuecke.Length>0,"Gueltiger Lotring findet Vorschau");
            test(plan.Stuecke.All(p=>Cs2Triangulierung.Dreiecke(p.Ring)==p.Ring.Length-2),"Keine native Teilablehnung");
        }
        foreach (bool umgekehrt in new[] {false,true})
        foreach (bool geschlossen in new[] {false,true})
        {
            var ring=Rechteck(5,5,10,10);
            if(umgekehrt) Array.Reverse(ring);
            if(geschlossen) ring=ring.Concat(new[] {ring[0]}).ToArray();
            var innen=OberflaechenSchnitt.Schneide(ring,new OberflaechenMuster.Bereich(new float2(10),30));
            test(innen.Length==1 && Math.Abs(OberflaechenSchnitt.Flaeche(innen[0])-100)<.001,
                "Vollstaendig innen, Umlauf/Ringschluss vereinheitlicht");
        }
        var bereich=new OberflaechenMuster.Bereich(new float2(50,70),35);
        var inseln=OberflaechenSchnitt.Schneide(u,bereich);
        test(inseln.Length==2,"Konkaver Schnitt trennt zwei Inseln ohne Bruecke");
        var basis=Rechteck(0,0,100,100);
        var haar=Rechteck(0,0,30,.001f);
        var splitter=Rechteck(0,0,.01f,.01f);
        var planHaar=OberflaechenMuster.Berechne(basis,new float2(0),new[] {new[] {haar,splitter,basis}});
        test(planHaar.Verworfen>=2 && planHaar.Stuecke.Length==1,"Haarlinien/Splitter weglassen, gueltiges Stueck behalten");
        var nan=Rechteck(0,0,10,10);nan[1]=new float2(float.NaN,0);
        var planNaN=OberflaechenMuster.Berechne(basis,new float2(0),new[] {new[] {nan,basis}});
        test(planNaN.Verworfen==1 && planNaN.Stuecke.Length==1,"NaN-Stueck isoliert abweisen");
        bool nanAbgewiesen=false;
        try { OberflaechenMuster.Ausschnitt(nan,new float2(0)); } catch(ArgumentException) {nanAbgewiesen=true;}
        test(nanAbgewiesen,"NaN im Lotumriss abweisen");
        var flach=new[] {new float2(0,0),new float2(20,0),new float2(40,.02f)};
        test(Cs2Triangulierung.Dreiecke(flach)==0,"Prueffigur scheitert trotz langer Kanten an nativem Innenversatz");
        var planFlach=OberflaechenMuster.Berechne(basis,new float2(0),new[] {new[] {flach,basis}});
        test(planFlach.Verworfen==1 && planFlach.Stuecke.Length==1,"CS2-Abweisung immer vor Ausgabe");
        var viele=new List<float2[]>();
        for(int x=0;x<30;x++)for(int y=0;y<30;y++)viele.Add(Rechteck(x*3,y*3,2,2));
        var voll=OberflaechenMuster.Berechne(basis,new float2(0),new[] {viele.ToArray()});
        test(voll.Verkleinerungen>0 && voll.Stuecke.Length>0 && voll.Stuecke.Length<=128 && voll.Ecken<=2048,"Budget verkleinert Ausschnitt, keine teilweise Ausgabe");
        test(OberflaechenSchnitt.Schneide(basis,new OberflaechenMuster.Bereich(new float2(500),20)).Length==0,"Aussen ohne Beruehrung");
        test(OberflaechenSchnitt.Schneide(basis,new OberflaechenMuster.Bereich(new float2(120,50),20)).Length==0,"Nur Randkontakt ohne Flaeche");

        // Gedrehter Ausschnitt (Spieltest 18.10.: weltachsenparallel wirkte am schraegen Lot schief).
        float w=18.34f*math.PI/180f;
        var r=new float2(math.cos(w),math.sin(w));
        test(math.distance(OberflaechenMuster.Richtung(new[] {new[] {new float2(0),r*50}},basis,new float2(5)),r)<1e-5f,
            "Richtung folgt der naechsten Fahrgasse");
        test(math.distance(OberflaechenMuster.Richtung(new[] {new[] {r*50,new float2(0)}},basis,new float2(5)),r)<1e-5f
            && math.distance(OberflaechenMuster.Richtung(new[] {new[] {new float2(0),new float2(-r.y,r.x)*50}},basis,new float2(5)),r)<1e-5f,
            "Gegenrichtung und Quergasse ergeben dasselbe Quadrat");
        test(math.distance(OberflaechenMuster.Richtung(null,new[] {new float2(0),r*40,new float2(0,40)},new float2(0)),r)<1e-5f,
            "Ohne Fahrgasse gilt die erste Shape-Kante");
        var gedreht=new OberflaechenMuster.Bereich(new float2(1259,3954),20,r);
        var probe=new float2(1270.5f,3961.25f);
        test(math.distance(gedreht.Welt(gedreht.Lokal(probe)),probe)<.002f,"Rahmenwechsel hin und zurueck");
        var schief=new[] {new float2(1200,3900),new float2(1300,3910),new float2(1290,4010),new float2(1190,4000)};
        var lotPlan=OberflaechenMuster.Berechne(schief,schief[0],new[] {new[] {schief}},r);
        double lotFlaeche=Math.Abs(OberflaechenSchnitt.Flaeche(schief));
        double gedrehtAnteil=lotPlan.Stuecke.Sum(s=>Math.Abs(OberflaechenSchnitt.Flaeche(s.Ring)))/lotFlaeche;
        test(Math.Abs(gedrehtAnteil-.25)<.01 && lotPlan.Stuecke.All(p=>Cs2Triangulierung.Dreiecke(p.Ring)==p.Ring.Length-2),
            "Gedrehter Ausschnitt trifft 25 % und bleibt CS2-tauglich");
        test(lotPlan.Ausschnitt.Richtung.Equals(r) && !lotPlan.Ausschnitt.Equals(new OberflaechenMuster.Bereich(lotPlan.Ausschnitt.Mitte,lotPlan.Ausschnitt.Halbseite)),
            "Richtung gehoert zur Bereichsgleichheit (Farbnetzmaske baut neu)");
        var dreieck=new[] {new float2(1240,3940),new float2(1290,3945),new float2(1262,3990)};
        var aussen=OberflaechenMuster.Ausserhalb(dreieck[0],dreieck[1],dreieck[2],gedreht);
        double ganz=Math.Abs(OberflaechenSchnitt.Flaeche(dreieck));
        double drin=OberflaechenSchnitt.Schneide(dreieck.Select(gedreht.Lokal).ToArray(),gedreht.Achsen).Sum(t=>Math.Abs(OberflaechenSchnitt.Flaeche(t)));
        test(Math.Abs(aussen.Sum(t=>Math.Abs(OberflaechenSchnitt.Flaeche(t)))+drin-ganz)<.05,
            "Farbnetzmaske spart genau das gedrehte Quadrat aus");
    }
}
