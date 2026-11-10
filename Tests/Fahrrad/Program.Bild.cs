using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static LayoutSettings Mit(LayoutSettings s, double randabstand) { s.Es = randabstand; return s; }

    /** --bild <datei>: Plaene je Stil/Layout/Form als JSON zum Zeichnen (Sichtpruefung, keine Pruefung). */
    private static int Bild(string datei)
    {
        float[][] P(float2[] r) => r.Select(p => new[] { p.x, p.y }).ToArray();
        var faelle = (from stil in new[] { Fahrradstil.Schlicht, Fahrradstil.Rahmen, Fahrradstil.Platz }
            from layout in Layouts
            from form in Stilformen.Where(f => f.Name is "Rechteck" or "Schmal" or "Schief" or "Kurvenpolygon" or "Gross" or "Trapez gross" or "L gross")
            let s = Mit(LayoutSettings.Fahrrad(layout, stil), FahrradGeometrie.Rahmenbreite)
            let plan = ParkingGeometry.Build(form.Ring, s).Fahrrad
            select new
            {
                Stil = stil.ToString(), Layout = layout.ToString(), Form = form.Name, plan.Kapazitaet,
                Hinweise = plan.Hinweise,
                Umriss = P(form.Ring), Gras = plan.Gras.Select(P), Pflaster = plan.Flaechen.Select(P),
                Objekte = plan.Objekte.Select(o => new { Art = o.Art.ToString(), Grundriss = P(o.Grundriss) }),
                Wege = plan.Wege.Select(w => new { A = new[] { w.A.x, w.A.y }, B = new[] { w.B.x, w.B.y }, w.Zufahrt }),
                Baeume = plan.Baeume.Select(p => new[] { p.x, p.y }),
                Buesche = plan.Buesche.Select(p => new[] { p.x, p.y }),
                Laternen = plan.Laternen.Select(p => new[] { p.x, p.y }),
            }).ToList();
        File.WriteAllText(datei, JsonSerializer.Serialize(faelle));
        Console.WriteLine($"BILD: {faelle.Count} Plaene nach {datei}");
        return 0;
    }
}
