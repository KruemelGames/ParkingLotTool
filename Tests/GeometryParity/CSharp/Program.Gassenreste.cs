using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
internal static partial class Program
{
    private static int RunGassenreste()
    {
        var fehler = 0; var faelle = 0; var fussPruefungen = 0;
        var perimeterAbzug = new System.Collections.Generic.List<string>();
        foreach (var nutzerform in new[] { false, true })
        foreach (var breite in new[] { 5.5, 8.0 })
        foreach (var winkel in new[] { 0.0, 17.0, 61.0 })
        foreach (var perimeter in new[] { false, true })
        foreach (var edge in new[] { 0, 1, 2, 3 })
        {
            faelle++;
            Console.WriteLine($"FALL Nutzerform={nutzerform} Breite={breite} Winkel={winkel} Perimeter={perimeter} Kante={edge}");
            var e = LayoutSettings.Cs2;
            e.Zellen = true; e.Randstrassen = perimeter; e.Auto = false; e.AngleMode = "edge";
            e.Gassenbreite = breite;
            e.AutomaticEntrances = false;
            e.Entrances = new[] { new Entrance { Edge = edge, Along = 30, Art = Zufahrtsart.Gasse },
                new Entrance { Edge = edge, Along = 60, Art = Zufahrtsart.GasseAus } };
            var rad = winkel * Math.PI / 180;
            float2 Drehe(float2 p) => new float2((float)(p.x * Math.Cos(rad) - p.y * Math.Sin(rad)),
                (float)(p.x * Math.Sin(rad) + p.y * Math.Cos(rad)));
            var polygon = nutzerform ? new[] {
                new float2(-1250.982666f,842.118103f), new float2(-1150.461548f,843.283997f),
                new float2(-1149.069458f,954.494507f), new float2(-1252.279175f,954.003113f) }
                : new[] { new float2(0,0), new float2(150,0), new float2(150,120), new float2(0,120) };
            var l = ParkingGeometry.Build(polygon.Select(Drehe).ToArray(), e);
            if (perimeter)
                perimeterAbzug.AddRange(l.NetLine.Select(n => $"{nutzerform}/{breite}/{winkel}/{edge}/{n.Kind}/{n.Art}/{n.A.x:R}/{n.A.y:R}/{n.B.x:R}/{n.B.y:R}"));
            // Unabhaengige Flaechenprobe: komplette 2-m-Fusswegbreite gegen
            // die sichtbaren Gassenrechtecke, nicht nur Schnitt der Achsen.
            foreach (var gasse in l.NetLine.Where(n => n.Kind == "entrance" && Zufahrtsarten.IstGasse(n.Art)))
            foreach (var fuss in l.NetLine.Where(n => n.Kind == "entrance" && n.Art == Zufahrtsart.Fussweg))
            {
                fussPruefungen++;
                var gassenbreite = new Entrance { Art = gasse.Art }.Breite(e.Ai, e.Gassenbreite);
                float2[] Rechteck(NetSegment n, double b)
                {
                    var d = n.B - n.A; var normal = new float2(-d.y, d.x) * (float)(b / (2 * math.length(d)));
                    return new[] { n.A - normal, n.B - normal, n.B + normal, n.A + normal };
                }
                var a = Rechteck(gasse, gassenbreite); var b = Rechteck(fuss, 2);
                var getrennt = false;
                foreach (var poly in new[] { a, b })
                for (var k = 0; k < 4; k++)
                {
                    var d = poly[(k + 1) % 4] - poly[k];
                    var normal = new float2(-d.y, d.x) / math.length(d);
                    double Projektion(float2 p) => (double)p.x * normal.x + (double)p.y * normal.y;
                    if (a.Max(Projektion) <= b.Min(Projektion) + 0.002 || b.Max(Projektion) <= a.Min(Projektion) + 0.002)
                        getrennt = true;
                }
                if (!getrennt) { fehler++; Console.WriteLine("FEHLER Fusswegflaeche ueberlappt sichtbare Gasse"); }
            }
            if (!perimeter)
            {
                var fusslaenge = l.NetLine.Where(n => n.Kind == "entrance" && n.Art == Zufahrtsart.Fussweg)
                    .Sum(n => (double)math.distance(n.A, n.B));
                // Zwei Endwege ueber 4 Modulschritte plus 7 m Stirnbreite.
                // An den Seitenkanten ersetzen genau zwei Gassen ihren Anteil.
                var soll = 184.4 - (edge % 2 == 1 ? 2 * breite : 0);
                if (nutzerform ? fusslaenge < 1 : Math.Abs(fusslaenge - soll) > 0.01)
                { fehler++; Console.WriteLine($"FEHLER Endweglaenge {fusslaenge:F3} statt {soll:F3}"); }
            }
            foreach (var g in l.NetLine.GroupBy(n => (n.Kind,n.Art)))
                Console.WriteLine($"NETZ {g.Key}: {g.Count()} Laengen={string.Join("/",g.Select(n => math.distance(n.A,n.B).ToString("F3")))}");
            foreach (var art in new[] { Zufahrtsart.Gasse, Zufahrtsart.GasseAus })
            {
                var n = l.NetLine.Count(n => n.Kind == "entrance" && n.Art == art);
                if (n != 1) { fehler++; Console.WriteLine($"FEHLER {art}: {n} statt 1 Kurs"); }
            }
        }
        var abzug = string.Join("\n", perimeterAbzug);
        var snapshot = Environment.GetEnvironmentVariable("PLT_PERIMETER_SNAPSHOT");
        if (!string.IsNullOrEmpty(snapshot))
        {
            if (System.IO.File.Exists(snapshot))
            {
                if (System.IO.File.ReadAllText(snapshot) != abzug) { fehler++; Console.WriteLine("FEHLER Perimeter-Abzug veraendert"); }
                else Console.WriteLine($"Perimeter-Abzug: {perimeterAbzug.Count} Kurse exakt unveraendert");
            }
            else System.IO.File.WriteAllText(snapshot, abzug);
        }
        Console.WriteLine($"Gassenreste: {faelle} edge-Faelle, {fussPruefungen} Flaechenpaare, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
