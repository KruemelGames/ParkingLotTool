using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * `--gassenreichweite`: erreicht eine Gasse die richtige Strasse?
 *
 * Die Regel des Nutzers vom 2026-09-24: ab Polygonrand, in Fangrichtung,
 * Bordstein hoechstens 16 m entfernt. Geprueft wird am Ergebnis - welche
 * Strasse getroffen wird und wo -, nicht an der Absicht.
 *
 * Der entscheidende Fall ist die SCHRAEGE Strasse: senkrecht naeher als in
 * Fangrichtung. Die fruehere Suche nach dem naechsten Punkt traf dort eine
 * Stelle senkrecht zur Strasse, und die Gasse knickte vom Fang weg.
 */
internal static partial class Program
{
    private static int PruefeGassenreichweite()
    {
        var fehler = 0;
        var faelle = 0;
        var rand = new float2(0f, 0f);
        var nachAussen = new float2(0f, 1f); // Gasse zeigt nach +z

        Gassenreichweite.Strasse Gerade(float2 a, float2 b, float halb, int id)
            => new Gassenreichweite.Strasse
            {
                Mittellinie = new[] { a, b },
                HalbeBreite = halb,
                Kennung = id,
            };

        void Erwarte(string name, IReadOnlyList<Gassenreichweite.Strasse> strassen,
            bool soll, int sollId = -1, float2? sollMitte = null)
        {
            faelle++;
            var ist = Gassenreichweite.Finde(rand, nachAussen, strassen, out var t);
            if (ist != soll)
            {
                fehler++;
                Console.WriteLine($"FALSCH {name}: gefunden={ist}, erwartet={soll}");
                return;
            }
            if (!ist) return;
            if (sollId >= 0 && t.Kennung != sollId)
            {
                fehler++;
                Console.WriteLine($"FALSCH {name}: Strasse {t.Kennung}, erwartet {sollId}");
            }
            // Die Mitte MUSS auf dem Fangstrahl liegen - x bleibt 0.
            if (math.abs(t.Mitte.x) > 1e-3f)
            {
                fehler++;
                Console.WriteLine($"FALSCH {name}: Mitte {t.Mitte} liegt nicht in Fangrichtung");
            }
            if (sollMitte.HasValue && math.distance(t.Mitte, sollMitte.Value) > 1e-2f)
            {
                fehler++;
                Console.WriteLine($"FALSCH {name}: Mitte {t.Mitte}, erwartet {sollMitte.Value}");
            }
        }

        // Senkrecht, Mitte 12 m, halbe Breite 4 -> Bordstein bei 8 m.
        Erwarte("senkrecht nah",
            new[] { Gerade(new float2(-50, 12), new float2(50, 12), 4f, 0) },
            true, 0, new float2(0, 12));

        // Senkrecht, Bordstein genau bei 16 m (Mitte 20).
        Erwarte("senkrecht an der Grenze",
            new[] { Gerade(new float2(-50, 20), new float2(50, 20), 4f, 0) },
            true, 0);

        // Senkrecht, Bordstein bei 17 m - zu weit.
        Erwarte("senkrecht zu weit",
            new[] { Gerade(new float2(-50, 21), new float2(50, 21), 4f, 0) },
            false);

        // SCHRAEG 45 Grad: Linie z = x + 14. Senkrechter Abstand vom Rand
        // 14/sqrt2 = 9,9 m; in Fangrichtung aber Mitte bei z=14, halbe Breite
        // im Strahl 4*sqrt2 = 5,66 -> Bordstein bei 8,34 m. Muss treffen,
        // und zwar bei (0,14), NICHT beim naechsten Punkt (-7,7).
        Erwarte("schraeg 45 Grad",
            new[] { Gerade(new float2(-40, -26), new float2(40, 54), 4f, 0) },
            true, 0, new float2(0, 14));

        // SCHRAEG, in Fangrichtung knapp zu weit: z = 0,3*x + 22 -> Mitte
        // bei 22, sin 0,958, halbe Breite im Strahl 4,18 -> Bordstein bei
        // 17,8 m. Die alte Suche (naechster Punkt im Umkreis 40 m) haette sie
        // bei senkrecht 21 m gefunden und die Gasse dorthin geknickt.
        Erwarte("schraeg zu weit in Fangrichtung",
            new[] { Gerade(new float2(-100, -8), new float2(100, 52), 4f, 0) },
            false);

        // Hinter dem Rand: nie.
        Erwarte("hinter dem Parkplatz",
            new[] { Gerade(new float2(-50, -6), new float2(50, -6), 4f, 0) },
            false);

        // Parallel zur Gasse: keine Einmuendung.
        Erwarte("parallel",
            new[] { Gerade(new float2(3, -10), new float2(3, 40), 4f, 0) },
            false);

        // Zwei Strassen hintereinander: die erste im Strahl gewinnt.
        Erwarte("zwei Strassen",
            new[]
            {
                Gerade(new float2(-50, 18), new float2(50, 18), 4f, 0),
                Gerade(new float2(-50, 9), new float2(50, 9), 3f, 1),
            },
            true, 1, new float2(0, 9));

        // Strasse, die den Strahl verfehlt (endet vorher).
        Erwarte("Strasse endet neben dem Strahl",
            new[] { Gerade(new float2(5, 10), new float2(50, 10), 4f, 0) },
            false);

        // Gebogene Strasse als Polylinie, Treffer im zweiten Stueck.
        Erwarte("Polylinie",
            new[]
            {
                new Gassenreichweite.Strasse
                {
                    Mittellinie = new[]
                    {
                        new float2(-30, 20), new float2(-5, 12), new float2(20, 14),
                    },
                    HalbeBreite = 3f,
                    Kennung = 0,
                },
            },
            true, 0);

        Console.WriteLine($"Gassenreichweite: {faelle} Faelle, Reichweite "
            + $"{Gassenreichweite.Reichweite:F0} m ab Polygonrand, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
