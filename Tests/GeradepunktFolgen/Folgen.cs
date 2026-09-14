using System;
using System.Linq;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class GeradepunktFolgen
{
    static float2 Position(float2[] f,Entrance e) => f[e.Edge]+math.normalize(f[(e.Edge+1)%f.Length]-f[e.Edge])*(float)e.Along;
    static void ZugaengeUndGitter() {
        foreach(var art in new[]{Zufahrtsart.Zufahrt,Zufahrtsart.Fussweg})
        foreach(var eingang in new[]{new Entrance{Edge=5,Along=80,Art=art},new Entrance{Edge=4,Along=100,Art=art},new Entrance{Edge=4,Along=67.5,Art=art}}) {
            var pos=Position(Ohne,eingang); var neu=Werkzeugprobe.Zugang(Mit,eingang,pos,Einstellungen(false));
            Console.WriteLine($"Zugang {art} {eingang.Edge}/{eingang.Along:F6}: neu={(neu==null ? "entfernt" : $"{neu.Edge}/{neu.Along:F6}, Verschiebung={math.distance(pos,Position(Mit,neu)):F6} m")}");
            if(neu==null) continue;
            foreach(var f in new[]{Ohne,Mit}) {
                var ent=f==Ohne ? eingang : neu;var s=Einstellungen(false);s.Entrances=new[]{ent};
                var eingabe=Eingabe(f,s);
                var rahmen=Geometrie.Reihenrahmen(Punkte(Ohne),Winkel); var p=eingabe.Punkte.Select(rahmen.NachLokal).ToArray();
                var innen=Layoutplanung.Innenrand(p,1); var e=ZSettings(false);
                var vorgaben=eingabe.Zufahrten.Select(z=>z.NachLokal(rahmen)).ToArray();
                var band=Layoutplanung.Baender(innen.Min(v=>v.Y),innen.Max(v=>v.Y),5.9,7,2.5);
                var plan=Ringlosplan.Plane(band,innen,p,vorgaben,e,new Linienregister(),out var quer);
                var erster=Zufahrtsbauer.Plane(p,vorgaben,0,new Linienregister()).FirstOrDefault(z=>z.Vorgabe.Art!=Zufahrtsart.Fussweg);
                var anker=erster==null?(plan.Links+plan.Rechts)/2:erster.Start.X;
                if(erster!=null && Math.Abs(erster.Innennormale.Y)>1e-6) {
                    var t=plan.Gassen.Select(g=>(g.A.Y-erster.Start.Y)/erster.Innennormale.Y).Where(t=>t>0).ToArray();
                    if(t.Length>0) anker+=erster.Innennormale.X*t.Min();
                }
                var n=Math.Max(1,(int)Math.Round(e.Querstrassenabstand/e.Buchtbreite-(e.Querstrassenkappen?2:0),MidpointRounding.AwayFromZero));
                var schritt=(n+(e.Querstrassenkappen?2:0))*e.Buchtbreite+e.Querstrassenbreite;
                Console.WriteLine($" Gitter {f.Length}: StartX={erster?.Start.X:F9}, NormaleY={erster?.Innennormale.Y:F12}, Anker={anker:F9}, Schritt={schritt:F6}, Quer={quer.Count}, Lagen="+string.Join(";",quer.Select(q=>q.Mitte.ToString("F6"))));
                if(art==Zufahrtsart.Zufahrt) {
                    var layout=ParkingGeometry.Build(f,s);
                    var lagen=layout.NetLine.Where(n=>n.Kind=="cross").Select(n=>rahmen.NachLokal(new Punkt((n.A.x+n.B.x)*.5,(n.A.y+n.B.y)*.5)).X).ToArray();
                    var phasen=lagen.Select(x=>(x%schritt+schritt)%schritt).ToArray();
                    Console.WriteLine($"  Gebaute Querachsen: {lagen.Length}, Phase min={phasen.Min():F9}, max={phasen.Max():F9}, Mittel={phasen.Average():F9}");
                }
            }
        }
        foreach(var f in new[]{Ohne,Mit}) {
            var p=Punkte(f); var rand=Randreihenplanung.Plane(p,Layoutplanung.Innenrand(p,1),Layoutplanung.Innenrand(p,6.9),1,6.9,3,0,new Linienregister());
            var q=Punkte(Ohne); var basis=Randreihenplanung.Plane(q,Layoutplanung.Innenrand(q,1),Layoutplanung.Innenrand(q,6.9),1,6.9,3,0,new Linienregister());
            Punkt Mitte(Randbuchtplan b) => b.Ecken.Aggregate(new Punkt(0,0),(s,p)=>s+p)*.25;
            var delta=rand.Buchten.Select(b=>basis.Buchten.Min(a=>Geometrie.Laenge(Mitte(a)-Mitte(b)))).ToArray();
            Console.WriteLine($"Randraster {f.Length}: {delta.Count(d=>d>.01)} von {delta.Length} Mittelpunkten >1cm versetzt, max={delta.Max():F6} m");
        }
    }
    static void Diagonale() {
        var a=new[]{new Punkt(0,0),new Punkt(200,0),new Punkt(160,140),new Punkt(0,140)};
        var b=new[]{a[0],a[1],new Punkt(180,70),a[2],a[3]};
        Ringlosplan Bau(Punkt[] f) {
            var innen=Layoutplanung.Innenrand(f,1);
            return Ringlosplan.Plane(Layoutplanung.Baender(innen.Min(v=>v.Y),innen.Max(v=>v.Y),5.9,7,2.5),innen,f,Array.Empty<Zufahrtsvorgabe>(),ZSettings(false),new Linienregister(),out _);
        }
        var ohne=Bau(a); var mit=Bau(b);
        foreach(var plan in new[]{ohne,mit}) {
            var verloren=0.0;
            foreach(var w in ohne.Fusswege) {
                var l=Geometrie.Laenge(w.B-w.A);
                const int n=10000;
                for(var i=0;i<n;i++) {
                    var p=w.A+(w.B-w.A)*((i+.5)/n);
                    if(ohne.Fusswege.Any(v=>Geometrie.EnthaeltOderRand(v.Ecken,p)) && !plan.Fusswege.Any(v=>Geometrie.EnthaeltOderRand(v.Ecken,p))) verloren+=l/n;
                }
            }
            Console.WriteLine($"Diagonale {(plan==ohne?4:5)} Punkte: Gassen={plan.Gassen.Count}, Fusswege={plan.Fusswege.Count}, Laenge={plan.Fusswege.Sum(w=>Geometrie.Laenge(w.B-w.A)):F6}, ungedeckte Referenzmittellinie={verloren:F6} m, Herkunft="+string.Join(";",plan.Gassen.Select(g=>$"{g.KanteA}/{g.KanteB}")));
        }
    }
    static void TeilflaechenUndZoning() {
        var anker=new[]{new float2(-1200,-10),new float2(-1100,50)};
        foreach(var ring in new[]{true,false})
        foreach(var f in new[]{Ohne,Mit}) {
            var e=Einstellungen(ring);
            e.TeilflaechenAusrichtungen=new[]{new TeilflaechenAusrichtung{Anker=anker[0],Winkel=Winkel},new TeilflaechenAusrichtung{Anker=anker[1],Winkel=Winkel+15}};
            try {
                var l=ParkingGeometry.Build(f,e);
                Console.WriteLine($"Teilraster ring={ring} {f.Length}: {l.Stalls} Buchten, {l.AisleLine.Length} Gassen, {l.Teilflaechen.Length} Teile, Verbindungen={l.TeilflaechenVerbindungen}; "+string.Join(";",l.Teilflaechen.Select(t=>$"{t.Index}:{t.Winkel:F6}/{t.Innenbuchten}/{t.EigeneZuweisung}")));
            } catch(Exception ex) {Console.WriteLine($"Teilraster ring={ring} {f.Length}: AUSNAHME {ex.Message}");}
        }
        var zoningReferenzen=new Dictionary<(double Winkel,bool Fest),NetSegment[]>();
        foreach(var winkel in new[]{0.0,40.0})
        foreach(var fest in new[]{false,true})
        foreach(var f in new[]{Ohne,Mit}) {
            var rad=winkel*Math.PI/180; var u=new Punkt(Math.Cos(rad),Math.Sin(rad));var v=new Punkt(-u.Y,u.X);
            var spalten=winkel==0?6:10; var reihen=winkel==0?4:6;
            var ecke=winkel==0 ? new Punkt(-1140,-20) : new Punkt(-1110,25)-u*40-v*24;
            var z=new ParkingGeometry.Zoningflaeche{Ecke=new float2((float)ecke.X,(float)ecke.Y),Spalten=spalten,Reihen=reihen,Winkel=winkel};
            var e=Einstellungen(true,fest); e.Zoningflaechen=new[]{z};
            var l=ParkingGeometry.Build(f,e);
            var netz=l.NetLine.Where(n=>n.Kind=="zoning").ToArray();
            if(f==Ohne) zoningReferenzen[(winkel,fest)]=netz;
            else {
                var vorher=zoningReferenzen[(winkel,fest)].SelectMany(n=>new[]{n.A,n.B}).ToArray();
                var nachher=netz.SelectMany(n=>new[]{n.A,n.B}).ToArray();
                var delta=Math.Max(vorher.Max(p=>nachher.Min(q=>math.distance(p,q))),nachher.Max(p=>vorher.Min(q=>math.distance(p,q))));
                Console.WriteLine($" Zonenring Punktvergleich 6->7, Winkel={winkel}, fest={fest}: max={delta:F9} m");
            }
            Console.WriteLine($"Zoning Winkel={winkel} fest={fest} {f.Length}: {spalten*reihen} Sollkacheln, {netz.Length} Netzsegmente, {netz.Sum(n=>math.distance(n.A,n.B)):F6} m, {l.Stalls} Buchten");
            // Lokale Geometrieprobe, KEINE Simulation von CS2s ZoneBlock-Gueltigkeit.
            ecke=new Punkt(z.Ecke.x,z.Ecke.y);
            var ecken=new[]{ecke-u*4-v*4,ecke+u*(spalten*8+4)-v*4,ecke+u*(spalten*8+4)+v*(reihen*8+4),ecke-u*4+v*(reihen*8+4)};
            var kanten=Enumerable.Range(0,4).Select(i=>(A:ecken[i],B:ecken[(i+1)%4])).ToArray();
            var maxLot=0.0; var maxWinkel=0.0; var aufRing=0;
            foreach(var n in netz) {
                var na=new Punkt(n.A.x,n.A.y);var nb=new Punkt(n.B.x,n.B.y);var mitte=(na+nb)*.5;
                var passend=kanten.Where(k=>Geometrie.AbstandPunktStrecke(mitte,k.A,k.B)<.01).ToArray();
                if(passend.Length==0) continue;
                var k=passend.OrderBy(k=>Geometrie.AbstandPunktStrecke(mitte,k.A,k.B)).First();
                aufRing++;
                maxLot=Math.Max(maxLot,Math.Max(Geometrie.AbstandPunktStrecke(na,k.A,k.B),Geometrie.AbstandPunktStrecke(nb,k.A,k.B)));
                var d=nb-na; var r=k.B-k.A;
                var diff=Math.Atan2(Geometrie.Kreuz(d,r),Geometrie.Skalar(d,r))*180/Math.PI;
                maxWinkel=Math.Max(maxWinkel,Math.Min(Math.Abs(diff),180-Math.Abs(diff)));
            }
            Console.WriteLine($" Zonenring: {aufRing} Segmente, max.Endpunktabstand={maxLot:F9} m, max.Winkelfehler={maxWinkel:F9} Grad");
            var besetzt=0;
            var asphalt=l.AsphaltSurface.Select(Punkte).ToArray();
            for(var x=0;x<spalten;x++) for(var y=0;y<reihen;y++) {
                var mitte=ecke+u*(8*x+4)+v*(8*y+4);
                if(asphalt.Any(p=>Geometrie.Enthaelt(p,mitte))) besetzt++;
            }
            Console.WriteLine($" Kachelmittelpunkte auf Parkplatzasphalt: {besetzt}/{spalten*reihen}; CS2-Gueltigkeitsbits nicht gemessen");
        }
    }
}
