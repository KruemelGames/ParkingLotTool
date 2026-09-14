using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * TRIFFT EIN DRUCK EINE SCHON GESETZTE ZONING-FLAECHE?
     *
     * Nutzerverdacht: *"Evtl. ist das Verschieben nur direkt nach dem
     * Erstellen moeglich?"* Genau das haengt an dieser einen Frage - wer
     * eine Flaeche bewegen will, muss sie erst treffen. Geprueft wird
     * dieselbe Kette wie im Werkzeug: aus einem Zug eine Flaeche bauen,
     * dann mit `ZoningEnthaelt` hineinzeigen.
     *
     * Gezogen wird in alle vier Richtungen, weil `ZoningAusZug` die Ecke
     * dabei verschiebt - ein Vorzeichenfehler dort faellt nur in zwei der
     * vier Quadranten auf.
     */
    private static int RunZoningtreffer()
    {
        var fehler = 0;
        var geprueft = 0;
        foreach (var winkel in new double[] { 0, 37, 90, 143, 217, 315 })
        foreach (var dx in new[] { 40f, -40f })
        foreach (var dz in new[] { 25f, -25f })
        {
            var start = new float2(-1100f, 520f);
            var flaeche = ParkingGeometry.ZoningAusZug(
                start, start + new float2(dx, dz), winkel);
            var mitte = ParkingGeometry.ZoningMitte(flaeche);
            geprueft++;

            if (!ParkingGeometry.ZoningEnthaelt(flaeche, mitte))
            {
                fehler++;
                Console.WriteLine($"  MITTE NICHT GETROFFEN  Winkel {winkel,3} "
                    + $"Zug {dx,5}/{dz,5}  {flaeche.Spalten}x{flaeche.Reihen}");
            }

            // Jede Parzellenmitte muss treffen - so klickt der Nutzer.
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(flaeche.Winkel);
            for (var i = 0; i < flaeche.Spalten; i++)
            for (var k = 0; k < flaeche.Reihen; k++)
            {
                var punkt = flaeche.Ecke
                    + laengs * (float)((i + 0.5) * ParkingGeometry.Zoningparzelle)
                    + quer * (float)((k + 0.5) * ParkingGeometry.Zoningparzelle);
                if (ParkingGeometry.ZoningEnthaelt(flaeche, punkt)) continue;
                fehler++;
                Console.WriteLine($"  PARZELLE {i}/{k} NICHT GETROFFEN  "
                    + $"Winkel {winkel,3} Zug {dx,5}/{dz,5}");
            }

            // Ein Punkt klar ausserhalb darf NICHT treffen.
            var weit = mitte + laengs * 500f;
            if (ParkingGeometry.ZoningEnthaelt(flaeche, weit))
            {
                fehler++;
                Console.WriteLine($"  AUSSEN TRIFFT TROTZDEM  Winkel {winkel,3}");
            }

            // Und nach einer Drehung an Ort und Stelle muss die Mitte
            // dieselbe bleiben - sonst waere die Flaeche nach jedem
            // Winkelwechsel woanders als gedacht.
            var gedreht = ParkingGeometry.ZoningGedreht(flaeche, winkel + 31);
            var mitte2 = ParkingGeometry.ZoningMitte(gedreht);
            if (math.distance(mitte, mitte2) > 0.01f)
            {
                fehler++;
                Console.WriteLine($"  MITTE WANDERT BEIM DREHEN  Winkel {winkel,3}"
                    + $"  um {math.distance(mitte, mitte2):F2} m");
            }
            if (!ParkingGeometry.ZoningEnthaelt(gedreht, mitte2))
            {
                fehler++;
                Console.WriteLine($"  GEDREHT NICHT GETROFFEN  Winkel {winkel,3}");
            }
        }

        Console.WriteLine($"Zoningtreffer: {geprueft} Faelle, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}
