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
            test(OberflaechenMuster.DarfZeigen(B(0),B(1),B(2),B(3),B(4),B(5),B(6),B(7),B(8)) == (bits==31),
                "Sichtbarkeitsmatrix " + bits);
        }
        foreach (bool zeigen in new[] { false,true })
        foreach (int anzahl in new[] { 0,1,2 })
        foreach (bool offen in new[] { false,true })
        foreach (bool gleich in new[] { false,true })
        {
            var soll = !zeigen ? (anzahl>0 || offen ? OberflaechenMuster.Aktion.Entfernen : OberflaechenMuster.Aktion.Keine)
                : anzahl>1 ? OberflaechenMuster.Aktion.Entfernen
                : gleich && (anzahl==1 || offen) ? OberflaechenMuster.Aktion.Behalten : OberflaechenMuster.Aktion.Anlegen;
            test(OberflaechenMuster.Entscheide(zeigen,anzahl,offen,gleich)==soll,"Marker-/Eingabenentscheidung");
        }
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
            var rest=OberflaechenMuster.Ausserhalb(q[0],q[1],q[2],ort);
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
            bool abgewiesen=false;try{OberflaechenMuster.Quadrat(p);}catch(ArgumentException){abgewiesen=true;}
            test(abgewiesen,"Ungueltige Quadratmitte");
            abgewiesen=false;try{OberflaechenMuster.Ausserhalb(p,new float2(0),new float2(1),new float2(0));}catch(ArgumentException){abgewiesen=true;}
            test(abgewiesen,"Ungueltiges Farbnetzdreieck");
        }
    }
}
