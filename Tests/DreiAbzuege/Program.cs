using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
class Program {
static void Main() {
foreach(var id in new[]{"154354-667","154359-722","154415-436"}) {
Console.WriteLine("ABZUG "+id);
var path=Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"),"AppData/LocalLow/Colossal Order/Cities Skylines II/Logs/ParkingLotTool-debug-20260909-"+id+".json");
using var doc=JsonDocument.Parse(File.ReadAllText(path)); var input=doc.RootElement.GetProperty("Input");
var poly=input.GetProperty("PolygonXZ").EnumerateArray().Select(p=>new float2(p.GetProperty("X").GetSingle(),p.GetProperty("Z").GetSingle())).ToArray();
var settings=JsonSerializer.Deserialize<LayoutSettings>(input.GetProperty("LayoutSettings").GetRawText(),new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true});
var l=ParkingGeometry.Build(poly,settings);
Console.WriteLine($"ERGEBNIS {l.Aisles} Gassen {l.Stalls} Buchten");
var auto=l.NetLine.Where(w=>w.Art!=Zufahrtsart.Fussweg).ToArray();
var seen=new System.Collections.Generic.HashSet<int>();var components=0;
for(int i=0;i<auto.Length;i++){if(!seen.Add(i))continue;components++;var queue=new System.Collections.Generic.Queue<int>();queue.Enqueue(i);while(queue.Count>0){var k=queue.Dequeue();for(int j=0;j<auto.Length;j++)if(!seen.Contains(j)&&new[]{auto[k].A,auto[k].B}.Any(p=>math.distance(p,auto[j].A)<=0.001||math.distance(p,auto[j].B)<=0.001)){seen.Add(j);queue.Enqueue(j);}}}
Console.WriteLine($"AUTONETZ {auto.Length} Segmente {components} Komponenten; gesetzte Zufahrten {settings.Entrances.Length}");
if(components!=1 || l.Aisles!=(id=="154354-667"?4:5))Environment.ExitCode=1;

var d=math.normalize(l.AisleLine[0].Last()-l.AisleLine[0][0]); var n=new float2(-d.y,d.x);
double X(float2 p)=> (double)p.x*d.x+(double)p.y*d.y;
double Y(float2 p)=> (double)p.x*n.x+(double)p.y*n.y;
Console.WriteLine("ENDEN "+string.Join(" ",l.AisleLine.OrderBy(g=>Y(g[0])).Select(g=>X(g.Last()).ToString("F6"))));
foreach(var w in l.EntranceLine) Console.WriteLine($"WEG quer {Y(w[0]):F6}..{Y(w.Last()):F6} laengs {X(w[0]):F6}..{X(w.Last()):F6}");
}
}}

