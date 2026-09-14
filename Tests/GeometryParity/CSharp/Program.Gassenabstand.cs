using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunGassenabstand()
    {
        // Exakte Input.PolygonXZ aus ParkingLotTool-debug-20260909-121731-073.json.
        var polygon = new[] {
            new float2(-1037.020263671875f,118.68978118896484f),
            new float2(-1140.1533203125f,122.22779083251953f),
            new float2(-1143.2060546875f,33.29500198364258f),
            new float2(-1194.3460693359375f,35.051002502441406f),
            new float2(-1198.6397705078125f,-90.04550170898438f),
            new float2(-1044.3660888671875f,-95.34059143066406f) };
        var s = LayoutSettings.Cs2;
        s.Es=1; s.Ai=7; s.Cw=3; s.Sl=5.9; s.Sw=3; s.Md=2.5;
        s.Cr=33; s.Qk=true; s.Auto=false; s.Angle=0; s.AngleMode="edge";
        s.Zellen=true; s.Randstrassen=true; s.AutomaticEntrances=false;
        s.Entrances=Array.Empty<Entrance>();
        var l = ParkingGeometry.Build(polygon,s);
        Console.WriteLine($"Buchten {l.Stalls}, Gassen {l.AisleLine.Length}, Winkel {l.Angle}");
        // Gegen die exportierten Achsen des archivierten Abzugs gemessen:
        // 26,2617 / 4,9617 statt der im Auftrag genannten 27,37 / 9,15.
        var vorher = new[] {13.5324,34.8324,26.2617,12.9,34.8323,13.5323};
        int fehler = l.AisleLine.Length == 6 ? 0 : 1;
        for (int i=0; i<l.AisleLine.Length; i++)
        {
            var g=l.AisleLine[i]; var a=g[0]; var b=g[g.Length-1];
            double len=Math.Sqrt(Math.Pow(b.x-a.x,2)+Math.Pow(b.y-a.y,2));
            double abstand=ParallelerGassenabstand(g,l.PerimeterLine);
            Console.WriteLine($"Gasse {i} {len:F2} m {abstand:F4} m ({a.x:F3},{a.y:F3}) -> ({b.x:F3},{b.y:F3})");
            if(!double.IsFinite(abstand) || i>=6 || abstand+0.001<vorher[i]) fehler++;
            if (!l.PerimeterLine.Any(r => DistanceToBoundary(a,r)<=0.001)
                || !l.PerimeterLine.Any(r => DistanceToBoundary(b,r)<=0.001)) fehler++;
        }
        int unerreichbar=UnservedBays(l), ueberlappung=OverlapCount(l.Bay), aufStrasse=BayOnRoad(l,s);
        int abgelehnt=ZaehleAbgelehnt(l.AsphaltSurface)+ZaehleAbgelehnt(l.GrassSurface);
        Console.WriteLine($"Unerreichbare Buchten {unerreichbar}, Buchtueberlappungen {ueberlappung}, Buchten auf Fahrbahn {aufStrasse}, abgelehnte Ringe {abgelehnt}");
        if(l.Stalls==0 || unerreichbar!=0 || ueberlappung!=0 || aufStrasse!=0 || abgelehnt!=0) fehler++;
        // Das Messgeraet selbst: Endanschluss und 19 m Parallelstueck zaehlen
        // nicht. Bei 20 m zaehlt auch die knappere Seite einer schraegen Achse.
        var achse = new[] {new float2(0,0),new float2(100,0)};
        var ende = new[] {new float2(100,0),new float2(100,30)};
        var kurz = new[] {new float2(0,1),new float2(19,1)};
        var lang = new[] {new float2(0,13),new float2(20,12.9f)};
        if (!double.IsPositiveInfinity(ParallelerGassenabstand(achse,new[] {ende,kurz}))
            || Math.Abs(ParallelerGassenabstand(achse,new[] {lang})-12.9)>1e-5) fehler++;
        // Die alte Paritaets-L enthaelt denselben Mangel. Geometrische
        // Eigenschaften pruefen; ihre historischen Sollzahlen nicht aendern.
        foreach (bool kappen in new[] { true, false })
        {
            var ls = LayoutSettings.Cs2;
            ls.Qk = kappen;
            var ll = ParkingGeometry.Build(Cases.First(c => c.Name == "L-Form").Site, ls);
            var abstaende = ll.AisleLine.Select(g => ParallelerGassenabstand(g,ll.PerimeterLine)).ToArray();
            var unerreichbare = UnservedBays(ll);
            Console.WriteLine($"Paritaets-L, Kappen {kappen}: {ll.Stalls} Buchten, {ll.AisleLine.Length} Gassen, Abstaende "
                + string.Join(" / ",abstaende.Select(d => d.ToString("F4"))) + $" m, unerreichbar {unerreichbare}");
            if(ll.AisleLine.Length==0 || ll.Stalls==0 || unerreichbare!=0
                || abstaende.Any(d => d + 0.001 < ls.Ai + ls.Sl)) fehler++;
        }
        Console.WriteLine($"Gassenabstand: {fehler} Fehler");
        return fehler==0 ? 0 : 1;
    }

    private static double ParallelerGassenabstand(float2[] gasse, float2[][] rand)
    {
        var a = gasse[0]; var b = gasse[gasse.Length - 1];
        double dx = b.x - a.x, dy = b.y - a.y;
        double laenge = Math.Sqrt(dx * dx + dy * dy);
        if (laenge < 1e-6) return double.PositiveInfinity;
        dx /= laenge; dy /= laenge;
        double abstand = double.PositiveInfinity;
        foreach (var r in rand)
        for (int j = 1; j < r.Length; j++)
        {
            double rx = r[j].x - r[j - 1].x, ry = r[j].y - r[j - 1].y;
            double rl = Math.Sqrt(rx * rx + ry * ry);
            if (rl < 1e-6 || Math.Abs(dx * ry - dy * rx) / rl > 0.01) continue;
            double u = (r[j - 1].x - a.x) * dx + (r[j - 1].y - a.y) * dy;
            double v = (r[j].x - a.x) * dx + (r[j].y - a.y) * dy;
            double von = Math.Max(0, Math.Min(u, v)), bis = Math.Min(laenge, Math.Max(u, v));
            if (bis - von < 20 - 1e-4) continue;
            double AbstandBei(double t)
            {
                double q = (t - u) / (v - u);
                return dx * (r[j - 1].y + q * ry - a.y) - dy * (r[j - 1].x + q * rx - a.x);
            }
            double av = AbstandBei(von), ab = AbstandBei(bis);
            double dist = av * ab <= 0 ? 0 : Math.Min(Math.Abs(av), Math.Abs(ab));
            abstand = Math.Min(abstand, dist);
        }
        return abstand;
    }
}
