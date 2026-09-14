using System;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;

internal static partial class GeradepunktFolgen
{
    // Nur Entwurf: maximal 5 cm Abstand ALLER Zwischenpunkte zur gemeinsamen
    // Sehne, monotone Projektion. Kein Entfernen oder Verschieben von Punkten.
    static (double Winkel,double Laenge) Lauf(Punkt[] p,bool mutation=false) {
        var best=-1.0; var winkel=0.0;
        for(var i=0;i<p.Length;i++)
        for(var schritte=1;schritte<(mutation?2:p.Length);schritte++) {
            var d=p[(i+schritte)%p.Length]-p[i]; var l=Geometrie.Laenge(d);
            if(l<1e-9) continue;
            var ok=true; var vorher=0.0;
            for(var s=1;s<schritte;s++) {
                var v=p[(i+s)%p.Length]-p[i]; var t=Geometrie.Skalar(v,d)/l;
                if(t<vorher-1e-9 || t>l+1e-9 || Math.Abs(Geometrie.Kreuz(d,v))/l>.05+1e-9) {ok=false;break;}
                vorher=t;
            }
            if(!ok) continue;
            var w=(Math.Atan2(d.Y,d.X)*180/Math.PI+180)%180;
            if(l>best+1e-9 || Math.Abs(l-best)<=1e-9 && w<winkel) {best=l;winkel=w;}
        }
        return (winkel,best);
    }
    static void Laufentwurf(bool mutation) {
        var fehler=0;
        void Fordere(bool ok,string text) {if(!ok){fehler++;Console.WriteLine("ENTWURFSFEHLER "+text);}}
        var basis=Lauf(Punkte(Ohne),mutation);
        var punkte=Punkte(Mit);
        var d=punkte[6]-punkte[4]; var l=Geometrie.Laenge(d);
        var t=Geometrie.Skalar(punkte[5]-punkte[4],d)/(l*l);
        var normal=new Punkt(-d.Y/l,d.X/l);
        var pruefungen=0;
        foreach(var offset in new[]{0.0,.000467931,.05,-.05,.051}) {
            var f=(Punkt[])punkte.Clone();f[5]=f[4]+d*t+normal*offset;
            var ergebnis=Lauf(f,mutation);
            Console.WriteLine($"Laufentwurf Lot={offset:F9}: Winkel={ergebnis.Winkel:F9}, Laenge={ergebnis.Laenge:F9}");
            foreach(var umkehr in new[]{false,true})
            for(var rotation=0;rotation<f.Length;rotation++) {
                var q=(umkehr?f.Reverse():f).ToArray();q=q.Skip(rotation).Concat(q.Take(rotation)).ToArray();
                var r=Lauf(q,mutation);pruefungen++;
                Fordere(Math.Abs(r.Winkel-ergebnis.Winkel)<1e-8,"Umlauf/Startpunkt");
            }
            if(Math.Abs(offset)<=.05) Fordere(Math.Abs(ergebnis.Winkel-basis.Winkel)<1e-8,"gestreckter Lauf muss Bezugsrichtung halten");
            else Fordere(Math.Abs(ergebnis.Winkel-basis.Winkel)>1,"5.1 cm duerfen nicht als derselbe Lauf gelten");
        }
        var mit=Lauf(Punkte(Mit),mutation);
        var e=Einstellungen(true);e.Ausrichtwinkel=mit.Winkel;
        var gebaut=ParkingGeometry.Build(Mit,e);
        Console.WriteLine($"Entwurfsbau: {gebaut.Stalls} Buchten, {gebaut.AisleLine.Length} Gassen, Winkelvorgabe={mit.Winkel:F9}");
        Fordere(gebaut.Stalls==703 && gebaut.AisleLine.Length==7,"Nutzerform: 703 Buchten und 7 Gassen gefordert");
        Console.WriteLine($"Laufentwurf: {pruefungen} Umlaufproben, {fehler} Fehler; Mutation={mutation}");
        Environment.ExitCode=fehler==0?0:1;
    }
}
