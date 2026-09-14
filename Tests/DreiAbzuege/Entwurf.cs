// Isolierter Entwurf fuer die drei Originalabzuege; kein Produktionscode.
using System;
using System.Linq;
using System.Collections.Generic;
namespace ParkingLotTool.Geometry.Zellen {
internal sealed partial class Ringlosplan {
private void PlaneDreiEntwurf(IReadOnlyList<Punkt> innen,IReadOnlyList<Zoningvorgabe> zoning) {
var gs=Gassen.OrderBy(g=>g.A.Y).ToArray();
var n=1; while(n<gs.Length && Math.Abs(gs[n].B.X-gs[0].B.X)<1e-6)n++;
if(n==gs.Length || gs.Length-n<2) throw new Exception("Entwurf ausserhalb des belegten Falls");
var a=gs[n]; var b=gs.Last(); var m=(b.B.X-a.B.X)/(b.A.Y-a.A.Y);
var c=gs.Skip(n).SelectMany(g=>new[]{g.B.X-m*(g.A.Y-g.Breite/2),g.B.X-m*(g.A.Y+g.Breite/2)}).Min();
var flat=gs[0].B.X; var y0=(flat-c)/m; var y=Math.Max(y0,gs[n-1].A.Y+gs[n-1].Breite/2);
if(Environment.GetEnvironmentVariable("PLT_DREI_MUTATION")=="eck")y=y0;
flat=c+m*y;
var norm=new Punkt(1,-m)*(1/Math.Sqrt(1+m*m));
// Achsenschnitt der um 1 m nach aussen versetzten Materialgrenzen.
var xS=flat+1; var yS=(xS-c-norm.X)/m+norm.Y;
var s=new Punkt(xS,yS);
var w=new Weg {A=new Punkt(xS,gs[0].A.Y-gs[0].Breite/2),B=s,Breite=2,Fuss=true,Art=Zufahrtsart.Fussweg};
var yB=b.A.Y+b.Breite/2;
var v=new Weg {A=s,B=new Punkt(c+m*yB,yB)+norm,Breite=2,Fuss=true,Art=Zufahrtsart.Fussweg};
SchneideEnde(v,true,new Punkt(0,yB),new Punkt(1,0));
var u=w.A-s; u=u*(1/Geometrie.Laenge(u)); var r=v.B-s; r=r*(1/Geometrie.Laenge(r));
SchneideEnde(w,true,s,u+r); SchneideEnde(v,false,s,u+r);
foreach(var g in gs.Take(n)){g.B=new Punkt(flat,g.A.Y);g.EndwegB=true;}
foreach(var g in gs.Skip(n)){var x=c+m*g.A.Y;g.B=new Punkt(x,g.A.Y);g.SchraegBRechts=-m*g.Breite/2;g.SchraegBLinks=m*g.Breite/2;g.EndwegB=true;}
Fusswege.Add(w); if(Environment.GetEnvironmentVariable("PLT_DREI_MUTATION")!="stummel") Fusswege.Add(v);
Console.WriteLine($"ENTWURF flach={n} schraeg={gs.Length-n} inneresEck={flat:F9},{y:F9} Achsenschnitt={s.X:F9},{s.Y:F9} Kuerzung={m*(y0-y):F9}");
var errors=0;
foreach(var f in Fusswege){if(!Frei(f,innen,zoning)){Console.WriteLine("ENTWURFFEHLER Kontur");errors++;}foreach(var g in gs)if(Ueberlappt(f.Ecken,g.Ecken)){Console.WriteLine("ENTWURFFEHLER Gassenueberlappung");errors++;}}
if(Ueberlappt(w.Ecken,v.Ecken)){Console.WriteLine("ENTWURFFEHLER Streifenueberlappung");errors++;}
// Unabhaengige Stirnproben an allen positiven Enden, 1 mm ausserhalb.
foreach(var g in gs) for(int k=0;k<=100;k++) {var q=k/100.0;var e=g.Ecken;var probe=e[1]*(1-q)+e[2]*q+new Punkt(0.001,0);if(!Fusswege.Any(f=>Geometrie.EnthaeltOderRand(f.Ecken,probe))){errors++;}}
var eW=w.Ecken;var eV=v.Ecken;
var naht=Math.Max(Geometrie.Laenge(eW[1]-eV[0]),Geometrie.Laenge(eW[2]-eV[3]));
if(naht>1e-6)errors++;
var ueber=Fusswege.SelectMany(f=>f.Ecken).Max(p=>p.Y)-yB;
if(ueber>1e-6)errors++;
Console.WriteLine($"ENTWURFPRUEFUNG {gs.Length*101} Stirnproben Naht={naht:F9} Ueberstand={ueber:F9} Fehler={errors}");
if(errors>0) Environment.ExitCode=1;
}
}}
