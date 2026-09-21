using System;
using System.Linq;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunZoningkanten()
    {
        var fehler = 0; var faelle = 0; var anschluesse = 0;
        var drehvergleich = new Dictionary<(double, double), (int, int)>();
        void Pruefe(bool ok, string text) { if (!ok) { fehler++; Console.WriteLine("FEHLER: " + text); } }
        foreach (var winkel in new[] { 0.0, 30, 44.99, 45, 45.01, 60, 90 })
        {
            var d = new Punkt(Math.Cos(winkel * Math.PI / 180), Math.Sin(winkel * Math.PI / 180));
            Pruefe(Zoningkanten.Direkt(new Punkt(0, 0), d) == (winkel >= 45), "Klasse " + winkel);
        }
        // Unabhaengige Geometrie: Raute |x|+|y| <= 10, Streifen y=8..9.
        var schnitt = Zoningkanten.Streifenschnitt(new[] { new Punkt(-10, 0), new Punkt(0, -10),
            new Punkt(10, 0), new Punkt(0, 10) }, 8, 9);
        Pruefe(schnitt.HasValue && Math.Abs(schnitt.Value.A + 2) < 1e-8
            && Math.Abs(schnitt.Value.B - 2) < 1e-8, "Raute: 4 m Schnitt statt 20 m Huelle");

        foreach (var raster in new[] { 0.0, 17, 61 })
        foreach (var relativ in new[] { 0.0, 30, 45, 60, 90 })
        foreach (var versatz in new[] { 0.0, 11.3, 70 })
        {
            faelle++;
            var rad = raster * Math.PI / 180;
            float2 Welt(double x, double y) => new float2((float)(x * Math.Cos(rad) - y * Math.Sin(rad)),
                (float)(x * Math.Sin(rad) + y * Math.Cos(rad)));
            var poly = new[] { Welt(-130, -100), Welt(130, -100), Welt(130, 100), Welt(-130, 100) };
            var e = LayoutSettings.Cs2;
            e.Zellen = true; e.Randstrassen = false; e.Auto = false; e.AngleMode = "edge";
            e.AutomaticEntrances = false; e.Entrances = new[] { new Entrance { Edge = 0, Along = 80 } };
            e.Zoningflaechen = new[] { new ParkingGeometry.Zoningflaeche {
                Ecke = Welt(-20 + versatz, -25), Spalten = 5, Reihen = 4, Winkel = raster + relativ, Rand = 8 } };
            var layout = ParkingGeometry.Build(poly, e);
            var name = $"Raster {raster} / Zone {relativ} / Verschiebung {versatz}";
            var netz = layout.NetLine ?? Array.Empty<NetSegment>();
            var z = netz.Where(n => n.Kind == "zoning").ToArray();
            var g = netz.Where(n => n.Kind == "aisle").ToArray();
            var q = netz.Where(n => n.Kind == "cross").ToArray();
            bool Nah(float2 a, float2 b) => math.distance(a, b) < 0.002;
            int Knoten(NetSegment[] wege) => wege.Count(w => z.Any(s => Nah(w.A, s.A) || Nah(w.A, s.B)
                || Nah(w.B, s.A) || Nah(w.B, s.B)));
            var direkt = Knoten(g); var normal = Knoten(q);
            Pruefe(direkt > 0, name + ": 0 direkte Gassenknoten");
            if (relativ != 45) Pruefe(normal > 0, name + ": 0 normale Querknoten");
            if (relativ == 45) Pruefe(normal == 0, name + ": 45-Grad-Gleichstand muss direkt anbinden");
            var zrad = (raster + relativ) * Math.PI / 180;
            var u = new Punkt(Math.Cos(zrad), Math.Sin(zrad)); var v = new Punkt(-u.Y, u.X);
            var ecke = e.Zoningflaechen[0].Ecke;
            var origin = new Punkt(ecke.x, ecke.y) - u * 4 - v * 4;
            var ring = new[] { origin, origin + u * 48, origin + u * 48 + v * 40, origin + v * 40 };
            var reihen = new Punkt(Math.Cos(rad), Math.Sin(rad));
            var verbotene = 0;
            foreach (var str in netz.Where(n => n.Kind == "cross" || (n.Kind == "entrance" && n.Art == Zufahrtsart.Fussweg)))
            for (var k = 0; k < 4; k++)
            {
                var a = ring[k]; var d = ring[(k + 1) % 4] - a; var len = Geometrie.Laenge(d);
                var winkel = Math.Acos(Math.Min(1, Math.Abs(Geometrie.Skalar(d, reihen)) / len)) * 180 / Math.PI;
                if (winkel < 45 - 0.001) continue;
                for (var t = 0; t <= 20; t++)
                {
                    var pp = str.A + (str.B - str.A) * (t / 20f);
                    var nach = new Punkt(pp.x, pp.y) - a;
                    var laengs = Geometrie.Skalar(nach, d) / len;
                    var aussen = -Geometrie.Kreuz(d, nach) / len;
                    if (laengs > 0.01 && laengs < len - 0.01 && aussen > 0.01 && aussen < 4 + e.Sl - 0.01)
                    { verbotene++; break; }
                }
            }
            Pruefe(verbotene == 0, name + $": {verbotene} Wege im Direktstreifen");
            var key = (relativ, versatz);
            if (drehvergleich.TryGetValue(key, out var vorher))
                Pruefe(vorher == (direkt, normal), name + ": Anschlusszahlen aendern sich bei gemeinsamer Drehung");
            else drehvergleich.Add(key, (direkt, normal));
            anschluesse += direkt + normal;
            Console.WriteLine($"{name}: Gassenknoten {direkt}, Querknoten {normal}");
        }
        Console.WriteLine($"Zoningkanten: {faelle} Faelle, {anschluesse} Anschluesse, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
