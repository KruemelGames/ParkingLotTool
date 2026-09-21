using System;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;
class Messung {
static double Inside(float2 a,float2 b,double r) {
double lo=0,hi=1;var d=b-a;
foreach(var q in new[]{((double)a.x,(double)d.x,116-r,164+r),((double)a.y,(double)d.y,104-r,136+r)}) {
if(Math.Abs(q.Item2)<1e-10){if(q.Item1<=q.Item3||q.Item1>=q.Item4)return 0;continue;}
var t=(q.Item3-q.Item1)/q.Item2;var u=(q.Item4-q.Item1)/q.Item2;lo=Math.Max(lo,Math.Min(t,u));hi=Math.Min(hi,Math.Max(t,u));}
return Math.Max(0,hi-lo)*math.length(d);
}
static double Area(float2[] p) { double a=0; for(int i=0;i<p.Length;i++){var q=p[(i+1)%p.Length]; a+=(double)p[i].x*q.y-(double)p[i].y*q.x;}return Math.Abs(a)/2; }
static void Main(){
var site=new[]{new float2(0,0),new float2(280,0),new float2(280,240),new float2(0,240)};
foreach(var ring in new[]{true,false}) foreach(var depth in new[]{0,1,2,6}) {
var s=LayoutSettings.Cs2;s.Zellen=true;s.AngleMode="edge";s.Auto=false;s.AutomaticEntrances=false;s.Randstrassen=ring;s.Entrances=new[]{new Entrance{Edge=0,Along=30}};
s.Zoningflaechen=new[]{new ParkingGeometry.Zoningflaeche{Ecke=new float2(116,104),Spalten=6,Reihen=4,Winkel=0,Rand=8+8*depth}};
var l=ParkingGeometry.Build(site,s);
Console.WriteLine($"ring={ring} depth={depth} Rand={s.Zoningflaechen[0].Rand} Zoning_m2={l.ZoningSurface.Sum(Area):F3} Zoningstrasse_m2={l.ZoningRoadSurface.Sum(Area):F3} stalls={l.Stalls} aisle={l.AisleLine.Length} cross={l.CrossLine.Length}");
foreach(var kind in new[]{"aisle","cross","zoning"})Console.WriteLine($" net {kind}: count={l.NetLine.Count(x=>x.Kind==kind)} outer_m={l.NetLine.Where(x=>x.Kind==kind).Sum(x=>Inside(x.A,x.B,8+8*depth)-Inside(x.A,x.B,8)):F3}");
var e=new Zelleneinstellungen{Randstrassen=ring,Reihenwinkel=null,Zoningflaechen=new[]{new Zoningvorgabe{Ecke=new Punkt(116,104),Spalten=6,Reihen=4,Winkel=0,Rand=8+8*depth}}};
var bau=Layoutbauer.Baue(new Formdefinition("Messung edge",site.Select(p=>new Punkt(p.x,p.y)).ToArray()),e);
foreach(var art in new[]{Zellart.Zoning,Zellart.Zoningstrasse}) Console.WriteLine($" cells {art}: count={bau.Zellen.Count(z=>z.Art==art)} area={bau.Zellen.Where(z=>z.Art==art).Sum(z=>Math.Abs(Geometrie.Vorzeichenflaeche(z.Polygon.Punkte.ToArray()))):F3}");
}
var path=System.IO.Path.Combine(Environment.GetEnvironmentVariable("CSII_MANAGEDPATH"),"Game.dll");
using var fs=System.IO.File.OpenRead(path);using var pe=new System.Reflection.PortableExecutable.PEReader(fs);var md=System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
foreach(var th in md.TypeDefinitions){var t=md.GetTypeDefinition(th);if(md.GetString(t.Name)!="ZoneUtils")continue;foreach(var fh in t.GetFields()){var f=md.GetFieldDefinition(fh);var n=md.GetString(f.Name);if(n!="MAX_ZONE_DEPTH"&&n!="CELL_SIZE")continue;var c=md.GetConstant(f.GetDefaultValue());Console.WriteLine($"Game.dll ZoneUtils.{n} type={c.TypeCode} bytes={Convert.ToHexString(md.GetBlobBytes(c.Value))}");}}
}}
