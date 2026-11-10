using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /** --fall <datei> <x,z;x,z;...> <kante> <entlang> <es>: Nutzerfall aus einem Bericht in allen Stilen als Bild-JSON. */
    private static int Fall(string datei, string polygon, int kante, float entlang, double es, string modus = null, double grad = 0)
    {
        var ring = polygon.Split(';').Select(p => p.Split(',')).Select(p => new float2(
            float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        float[][] P(float2[] r) => r.Select(p => new[] { p.x, p.y }).ToArray();
        var faelle = (from stil in new[] { Fahrradstil.Rahmen, Fahrradstil.Platz }
            from layout in Layouts
            let s = Winkel(Zugang(Mit(LayoutSettings.Fahrrad(layout, stil), es), kante, entlang), modus, grad)
            let plan = ParkingGeometry.Build(ring, s).Fahrrad
            select new
            {
                Stil = stil.ToString(), Layout = layout.ToString(), Form = "Bericht", plan.Kapazitaet, Hinweise = plan.Hinweise,
                Umriss = P(ring), Gras = plan.Gras.Select(P), Pflaster = plan.Flaechen.Select(P),
                Objekte = plan.Objekte.Select(o => new { Art = o.Art.ToString(), Grundriss = P(o.Grundriss) }),
                Wege = plan.Wege.Select(w => new { A = new[] { w.A.x, w.A.y }, B = new[] { w.B.x, w.B.y }, w.Zufahrt }),
                Baeume = plan.Baeume.Select(p => new[] { p.x, p.y }),
                Buesche = plan.Buesche.Select(p => new[] { p.x, p.y }),
                Laternen = plan.Laternen.Select(p => new[] { p.x, p.y }),
            }).ToList();
        File.WriteAllText(datei, JsonSerializer.Serialize(faelle));
        Console.WriteLine($"FALL: {faelle.Count} Plaene nach {datei}");
        return 0;
    }

    private static LayoutSettings Winkel(LayoutSettings s, string modus, double grad) { s.AngleMode = modus; s.Angle = grad; return s; }

    private static LayoutSettings Zugang(LayoutSettings s, int kante, float entlang)
    {
        s.AutomaticEntrances = false;
        s.Entrances = new[] { new Entrance { Edge = kante, Along = entlang } };
        return s;
    }
}
