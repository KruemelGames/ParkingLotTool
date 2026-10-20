using System;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;

internal static class VorbereitungTests
{
    internal static void Laufe(Action<bool, string> test)
    {
        var speicher = new VorschauVorbereitung();
        var ring = new[] { new Punkt(0, 0), new Punkt(80, 0), new Punkt(80, 64), new Punkt(0, 64) };
        for (int schritt = 0; schritt < 4; schritt++)
        {
            var p = schritt == 2 ? ring.Select(v => new Punkt(v.X + 1, v.Y - 2)).ToArray() : ring.ToArray();
            var k = new Knotenfabrik(); var l = new Linienregister();
            var a = Polygonfabrik.Areal(p, k, l);
            var teile = new Konvexzerlegung(k, l).Zerlege(a);
            var kk = new Knotenfabrik(); var ll = new Linienregister();
            speicher.Zerlege(p, kk, ll, out var aa, out var tt);
            test(Vergleich.Abdruck(a) == Vergleich.Abdruck(aa) && Vergleich.Abdruck(teile) == Vergleich.Abdruck(tt),
                "Cache prueft Koordinaten statt nur Punktzahl");
            test(k.Neu(new Punkt(1, 2)).Id == kk.Neu(new Punkt(1, 2)).Id
                && l.RasterX(7).Id == ll.RasterX(7).Id, "Cache erhaelt Knoten- und Liniennummern");
            foreach (double d in new[] { 1.0, 2.0, 1.0 })
                test(Vergleich.Abdruck(Layoutplanung.Innenrand(p, d)) == Vergleich.Abdruck(speicher.Innenrand(p, d)),
                    "Cache unterscheidet Versatzabstaende");
            // Nachbarkanten duerfen nur die aktuelle Polygonliste aendern.
            tt[0].Ecken.RemoveAt(0);
            var innen = (Punkt[])speicher.Innenrand(p, 1);
            innen[0] = new Punkt(1000, 1000);
            test(Vergleich.Abdruck(Layoutplanung.Innenrand(p, 1)) == Vergleich.Abdruck(speicher.Innenrand(p, 1)),
                "Cache gibt keine veraenderbaren Innenrandlisten heraus");
        }
        test(speicher.WiederverwendeteZerlegungen == 1, "Vorbereitung nutzt identische neue Arrayinstanz");
    }
}
