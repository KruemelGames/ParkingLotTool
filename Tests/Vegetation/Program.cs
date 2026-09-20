using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
class Program {
 static int checks;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static float2[] Rect(float w,float h)=>new[]{new float2(0,0),new float2(w,0),new float2(w,h),new float2(0,h)};
 static void Main(){
 for(int age=0;age<6;age++) for(uint seed=0;seed<20;seed++) Check(ParkingVegetation.SelectAge(1<<age,seed)==age,"Jede der sechs Altersstufen erreichbar");
 Check(Enumerable.Range(0,60).Select(seed=>ParkingVegetation.SelectAge(63,(uint)seed)).Distinct().Count()==6,"Alle Altersstufen gemischt");
 var species=new[]{new VegetationSpecies{Id="tree",Tree=true,Spacing=8},new VegetationSpecies{Id="shrub",Spacing=2.5f}};
 var rings=new[]{Rect(90,40)};var o=new VegetationOptions{Enabled=true,Density=100};
 var all=ParkingVegetation.Plan(rings,o,species);Check(all.Plants.Count>30,"Gueltige Flaeche muss Pflanzen erhalten");
 Check(all.Plants.Any(p=>p.Species==0)&&all.Plants.Any(p=>p.Species==1),"Baeume und Buesche muessen beide vorkommen");
 foreach(int density in new[]{0,25,50,100}) foreach(bool line in new[]{false,true}) {
 o.Density=density;o.Line=line;
 foreach(var ring in new[]{Rect(80,3),Rect(80,40),new[]{new float2(0,0),new float2(50,0),new float2(50,10),new float2(10,10),new float2(10,50),new float2(0,50)}}) {
 var a=ParkingVegetation.Plan(new[]{ring},o,species);var b=ParkingVegetation.Plan(new[]{ring},o,species);
 Check(a.Plants.Count==b.Plants.Count,"Deterministisch");
 for(int i=0;i<a.Plants.Count;i++) {
 var x=a.Plants[i];Check(math.distance(x.Position,b.Plants[i].Position)<.001,"Position stabil");
 Check(ParkingVegetation.Inside(x.Position,ring),"Nur Dekoflaeche");
 Check(ParkingVegetation.EdgeDistance(x.Position,ring)>=ParkingVegetation.Randabstand(species[x.Species].Tree)-.001,"Randabstand");
 for(int j=0;j<i;j++){var y=a.Plants[j];float gap=(ParkingVegetation.Spacing(species[x.Species],line)+ParkingVegetation.Spacing(species[y.Species],line))*.5f;Check(math.distance(x.Position,y.Position)>=gap-.001,"Pflanzabstand");}
 }
 if(density==0)Check(a.Plants.Count==0,"Null Prozent");else Check(a.Plants.Count>0,"Kein stilles Nichts-Tun");
 }
 }
 o.Line=true;o.Density=100;var median=ParkingVegetation.Plan(new[]{Rect(80,3)},o,new[]{species[0]});
 Check(median.Plants.Count>=5,"Median bepflanzt");Check(median.Plants.All(p=>math.abs(p.Position.y-1.5f)<.001),"Mittige Reihe");Check(median.Plants.Any(p=>math.abs(p.Position.x-40)<.001),"Reihe beginnt in Mitte");
 o.Line=false;var translated=ParkingVegetation.Plan(new[]{rings[0].Select(p=>p+new float2(8000,-8000)).ToArray()},o,species);
 Check(translated.Plants.Count==all.Plants.Count,"Translation Anzahl");for(int i=0;i<all.Plants.Count;i++) Check(math.distance(translated.Plants[i].Position-new float2(8000,-8000),all.Plants[i].Position)<.01,"Translation Position");
 o.Density=50;var half=ParkingVegetation.Plan(rings,o,species);Check(half.Plants.Count>=all.Plants.Count*.3 && half.Plants.Count<=all.Plants.Count*.7,"50 Prozent reduziert tatsaechlich etwa auf die Haelfte");foreach(var plant in half.Plants)Check(all.Plants.Any(p=>p.Position.Equals(plant.Position)&&p.Species==plant.Species),"Dichte erhaelt Positionen");
 // --- Dichte je Quadratmeter, unabhaengig von der Groesse ---------------
 o.Enabled=true;o.Line=false;
 foreach(int dichte in new[]{50,100}) {
  o.Density=dichte;
  var klein=ParkingVegetation.Plan(new[]{Rect(160,80)},o,species);
  var gross=ParkingVegetation.Plan(new[]{Rect(640,320)},o,species);
  double je_klein=klein.Plants.Count/(160.0*80.0), je_gross=gross.Plants.Count/(640.0*320.0);
  Check(je_klein>0&&je_gross>0,"Beide Flaechen bepflanzt");
  double verhaeltnis=je_gross/je_klein;
  Check(verhaeltnis>.8&&verhaeltnis<1.25,
   $"Dichte je m2 haengt an der Groesse: klein {je_klein:F4}/m2, gross {je_gross:F4}/m2, Verhaeltnis {verhaeltnis:F2}");
  Check(!klein.Limited&&!gross.Limited,"Keine Schranke bei gewoehnlichen Groessen");
 }
 o.Density=100;
 // Gleiche Beete im selben Parkplatz duerfen keine Stempelkopien sein.
 o.Line=false;o.Seed=12345;o.Density=100;
 var bed=Rect(80,20);var shift=new float2(0,60);
 var beds=ParkingVegetation.Plan(new[]{bed,bed.Select(p=>p+shift).ToArray()},o,species);
 var first=beds.Plants.Where(p=>p.Position.y<20).ToArray();
 var second=beds.Plants.Where(p=>p.Position.y>59).ToArray();
 Check(first.Length>0&&second.Length>0,"Beide gleichen Beete bepflanzt");
 int copies=second.Count(p=>first.Any(q=>math.distance(p.Position-shift,q.Position)<.01f));
 Check(copies<second.Length/10,"Keine wiederholten Pflanzenpositionen in gleichen Beeten");
 var seeded=ParkingVegetation.Plan(rings,o,species);
 o.Seed=54321;var otherSeed=ParkingVegetation.Plan(rings,o,species);
 Check(otherSeed.Plants.Count>0&&!seeded.Plants[0].Position.Equals(otherSeed.Plants[0].Position),"Seed aendert freien Entwurf");
 o.Line=true;var lineA=ParkingVegetation.Plan(rings,o,species);o.Seed=987;
 var lineB=ParkingVegetation.Plan(rings,o,species);
 Check(lineA.Plants.Count==lineB.Plants.Count&&lineA.Plants.Zip(lineB.Plants,(a,b)=>a.Position.Equals(b.Position)&&a.Species==b.Species&&a.Seed==b.Seed).All(x=>x),"Line bleibt unabhaengig vom neuen Seed");
 o.Enabled=false;Check(ParkingVegetation.Plan(rings,o,species).Plants.Count==0,"Standard aus");
 Console.WriteLine($"Vegetation: {checks} Pruefungen, 0 Fehler; 50%={half.Plants.Count}, 100%={all.Plants.Count}; Median={median.Plants.Count}");
 }
}
