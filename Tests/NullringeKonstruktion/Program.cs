using System;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;

internal static class Program
{
    private static int Main()
    {
        var fehler = 0;
        var faelle = 0;
        // Exakt binaere Masse: 2^-15 * 2^-15 = 2^-30 m2. Beide Teile
        // muessen existieren, auch fern vom Ursprung. Eine blosse Pruefung
        // der ausgegebenen Ringe wuerde das Wegwerfen beider Teile uebersehen.
        var breite = Math.Pow(2, -15);
        var erwartet = Math.Pow(2, -30);
        foreach (var ort in new[] { new Punkt(0, 0), new Punkt(-100, 1164),
                     new Punkt(10000, -10000), new Punkt(1000000, 1000000) })
        foreach (var senkrecht in new[] { false, true })
        {
            faelle++;
            var fabrik = new Knotenfabrik();
            var register = new Linienregister();
            var punkte = new[] { ort, ort + new Punkt(breite, 0),
                ort + new Punkt(breite, breite), ort + new Punkt(0, breite) };
            var polygon = new Polygon(punkte.Select((p, i) =>
                new Ecke(fabrik.Neu(p), register.Aussenkante(i))));
            var teiler = new Polygonteiler(fabrik);
            var teile = teiler.Teile(polygon, senkrecht
                ? register.RasterX(ort.X + breite / 2)
                : register.BandY(ort.Y + breite / 2));
            var summe = teile.Sum(Geometrie.Flaeche);
            var ok = teile.Count == 2 && Math.Abs(summe / erwartet - 1) < 1e-12
                && teile.All(t => Math.Abs(Geometrie.Flaeche(t) / (erwartet / 2) - 1) < 1e-12)
                && teiler.Warnungen.Count == 0;
            if (teile.Count == 2)
            {
                var zellen = teile.Select((p, i) => new Zelle
                {
                    Id = i, Polygon = p, Art = Zellart.Restgruen, Material = Material.Gruen,
                }).ToArray();
                var bau = Vereinigung.Vereinige(zellen);
                ok &= bau.Flaechen.Count == 1 && bau.Flaechen[0].Loecher.Count == 0
                    && Math.Abs(bau.Flaechen[0].Aussenring.Vorzeichenflaeche / erwartet - 1) < 1e-12
                    && bau.Topologie.UnerwarteteOffeneKanten == 0
                    && bau.Topologie.NichtMannigfaltigeKanten == 0;
                // Die Schnittlinie muss beide Teilpolygone begrenzen: zwei
                // ausgegebene Kopien der ganzen Zelle sind keine Loesung.
                var index = senkrecht ? 0 : 1;
                double Wert(Punkt p) => index == 0 ? p.X : p.Y;
                var mitte = Wert(ort) + breite / 2;
                ok &= teile.Count(t => t.Punkte.All(p => Wert(p) <= mitte)) == 1
                    && teile.Count(t => t.Punkte.All(p => Wert(p) >= mitte)) == 1;
            }
            if (!ok) fehler++;
            Console.WriteLine($"Schnitt {ort.X}/{ort.Y}, {(senkrecht ? "X" : "Y")}: "
                + $"{teile.Count}/2 Teile, {summe:G17}/{erwartet:G17} m2, "
                + $"{teiler.Warnungen.Count} Warnungen: {(ok ? "OK" : "FEHLER")}");
        }
        Console.WriteLine($"NullringeKonstruktion: {faelle} Faelle, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
