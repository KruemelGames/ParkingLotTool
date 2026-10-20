using System;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;

internal static class SchnittTests
{
    internal static void Laufe(Action<bool, string> test, bool allokationen)
    {
        var knoten = new Knotenfabrik();
        var linien = new Linienregister();
        var ring = new[] { new Punkt(0, 0), new Punkt(80, 0), new Punkt(80, 64), new Punkt(0, 64) };
        var polygon = Polygonfabrik.Areal(ring, knoten, linien);
        var teiler = new Polygonteiler(knoten);
        var aussen = linien.RasterX(100);
        var rand = linien.RasterX(80);
        var innen = linien.RasterX(40);
        var fernA = new Punkt(100, 100);
        var fernB = new Punkt(100, 110);
        test(teiler.Teile(polygon, aussen).Count == 1
            && ReferenceEquals(teiler.Teile(polygon, aussen)[0], polygon), "Unberuehrter Schnitt behaelt das Polygon");
        test(teiler.Teile(polygon, rand).Count == 1, "Beruehrung erzeugt kein Fragment");
        test(teiler.TeileSegment(polygon, aussen, fernA, fernB).Count == 1, "Begrenzter Schnitt verfehlt Zelle");
        test(teiler.TeileSegment(polygon, aussen, new Punkt(100, 0), new Punkt(100, 64)).Count == 1,
            "Begrenzter Schnitt ohne Kantentreffer behaelt Zelle");
        var teile = teiler.Teile(polygon, innen);
        test(teile.Count == 2 && teile.All(p => p.Anzahl == 4)
            && teile.SelectMany(p => p.Punkte).All(p => p.X >= 0 && p.X <= 80), "Echter Schnitt bleibt geteilt");
        var ergebnis = teiler.Teile(polygon, aussen);
        polygon.Ecken.Insert(1, new Ecke(knoten.Neu(new Punkt(40, 0)), polygon.Linie(0)));
        test(ergebnis[0].Anzahl == 5, "Nachbarknoten bleibt im bereits gelieferten Polygon sichtbar");
        double x = 0, y = 0;
        foreach (var punkt in polygon.Punkte) { x += punkt.X; y += punkt.Y; }
        var mittel = Geometrie.Mittelwert(polygon);
        test(BitConverter.DoubleToInt64Bits(mittel.X) == BitConverter.DoubleToInt64Bits(x / polygon.Anzahl)
            && BitConverter.DoubleToInt64Bits(mittel.Y) == BitConverter.DoubleToInt64Bits(y / polygon.Anzahl),
            "Mittelwert erhaelt Summationsreihenfolge einschliesslich Nachbarknoten");
        if (!allokationen) return;
        // Der warme Nichttrefferpfad darf nicht fuer jede Schnittanfrage
        // ein Array oder einen LINQ-Iterator anlegen. Rueckgabewerte benutzen.
        long summe = 0;
        for (int i = 0; i < 10000; i++) summe += teiler.Teile(polygon, aussen).Count
            + teiler.TeileSegment(polygon, aussen, fernA, fernB).Count;
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) summe += teiler.Teile(polygon, aussen).Count
            + teiler.TeileSegment(polygon, aussen, fernA, fernB).Count;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        test(summe == 220000, "Nichttreffer bleiben vollstaendig");
        test(bytes == 0, "Warme Schnitt-Nichttreffer ohne Allokationen");
        Console.WriteLine("200000 warme Schnitt-Nichttreffer: " + bytes + " Bytes.");
    }
}
