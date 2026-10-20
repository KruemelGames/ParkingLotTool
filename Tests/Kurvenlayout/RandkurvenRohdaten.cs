using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry.Zellen;

internal static class RandkurvenRohdaten
{
    private static int _folge;
    internal static void Schreibe(Bauergebnis bau)
    {
        double[][] Punkte(System.Collections.Generic.IEnumerable<Punkt> punkte)
            => punkte.Select(bau.Rahmen.NachWelt).Select(p => new[] { p.X, p.Y }).ToArray();
        var daten = new
        {
            Buchten = bau.Kurvenrand.Buchten.Select(b => new { b.Id, Ecken = Punkte(b.Ecken) }),
            Graskeile = bau.Kurvenrand.Graskeile.Select(Punkte),
            Anschluesse = bau.Kurvenrand.Anschluesse.Select(Punkte),
            Randbuchten = bau.Randbuchten.Values.Select(b => new { b.Id, b.Randkante, Ecken = Punkte(b.Ecken) }),
            Zellen = bau.Zellen.Select(z => new { z.Id, Art = z.Art.ToString(), Material = z.Material.ToString(),
                z.BuchtId, Polygon = Punkte(z.Polygon.Punkte) }),
            Vorher = bau.FlaechenVorTrennung.Select(f => new { f.Id, Material = f.Material.ToString(),
                Aussen = Punkte(f.Aussenring.Knoten.Select(k => k.Punkt)),
                Loecher = f.Loecher.Select(r => Punkte(r.Knoten.Select(k => k.Punkt))) }),
            Nachher = bau.Flaechen.Select(f => new { f.Id, Material = f.Material.ToString(),
                Aussen = Punkte(f.Aussenring.Knoten.Select(k => k.Punkt)),
                Loecher = f.Loecher.Select(r => Punkte(r.Knoten.Select(k => k.Punkt))) }),
        };
        var ordner = System.Environment.GetEnvironmentVariable("PLT_RANDROH") ?? ".codex-build/kurven-11";
        File.WriteAllText($"{ordner}/roh-{_folge++}.json", JsonSerializer.Serialize(daten));
    }
}
