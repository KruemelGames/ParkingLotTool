using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * WIE SIEHT DIE ZUFAHRT IN DER VORSCHAU AUS?
 *
 * Befund des Nutzers am 2026-09-08: *"In der Preview von Randstrasse aus sind
 * die Grafiken der Einfahrten komplett anders farblich und formmaessig."*
 *
 * Die Vorschau malt `EntranceQuad`. Dieser Lauf zaehlt, wie viele davon
 * entartet sind - ein Viereck mit einer 0,0-m-Kante ist kein Streifen mehr,
 * sondern ein Strich, und faerbt trotzdem.
 *
 * Fall und Einstellungen stammen aus dem Bauzettel vom 2026-09-08 23:51,
 * nicht geschaetzt.
 */
internal static partial class Program
{
    private static int RunZufahrtsquads()
    {
        var flaeche = new[]
        {
            new float2(-1037.0201416015625f, 118.68978118896484f),
            new float2(-1177.112060546875f, 123.49600219726562f),
            new float2(-1183.0321044921875f, -48.99800109863281f),
            new float2(-1042.93701171875f, -53.80400085449219f),
        };

        var fehler = 0;
        foreach (var randstrassen in new[] { true, false })
        {
            var e = LayoutSettings.Cs2;
            e.Randstrassen = randstrassen;
            e.AngleMode = "edge";
            e.Auto = false;
            e.Zellen = true;
            e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
            e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
            e.Entrances = new[]
            {
                new Entrance { Edge = 0, Along = 80.7355728149414,
                    Art = Zufahrtsart.Zufahrt },
                new Entrance { Edge = 3, Along = 112.4997787475586,
                    Art = Zufahrtsart.Fussweg },
            };

            var layout = ParkingGeometry.Build(flaeche, e);
            var quads = layout.EntranceQuad ?? Array.Empty<float2[]>();
            double Kuerzeste(float2[] q) => Enumerable.Range(0, q.Length)
                .Select(i => (double)math.distance(q[i], q[(i + 1) % q.Length]))
                .Min();
            double Flaeche(float2[] q)
            {
                var a = 0.0;
                for (var i = 0; i < q.Length; i++)
                {
                    var b = q[(i + 1) % q.Length];
                    a += q[i].x * b.y - b.x * q[i].y;
                }
                return Math.Abs(a) / 2;
            }
            foreach (var feld in new (string, float2[][])[]
            {
                ("EntranceQuad", layout.EntranceQuad),
                ("AisleQuad", layout.AisleQuad),
                ("CrossQuad", layout.CrossQuad),
                ("Green", layout.Green),
                ("Bay", layout.Bay),
            })
            {
                var v = feld.Item2 ?? Array.Empty<float2[]>();
                Console.WriteLine($"      {feld.Item1,-14} {v.Length,4}"
                    + $" | Kante <1cm {v.Count(q => Kuerzeste(q) < 0.01),4}"
                    + $" | <5cm {v.Count(q => Kuerzeste(q) < 0.05),4}"
                    + $" | <20cm {v.Count(q => Kuerzeste(q) < 0.20),4}"
                    + $" | Flaeche <0,01 m2 {v.Count(q => Flaeche(q) < 0.01),4}");
            }
            var entartet = quads.Count(q => Kuerzeste(q) < 0.05);
            Console.WriteLine($"  Randstrassen {(randstrassen ? "an " : "aus")}"
                + $"  Buchten {layout.Stalls,4}"
                + $"  EntranceQuad {quads.Length,4}"
                + $"  davon entartet (<5 cm) {entartet,4}");
            foreach (var g in quads
                .Select(q => (Kurz: Kuerzeste(q), Lang: Enumerable.Range(0, q.Length)
                    .Select(i => (double)math.distance(q[i], q[(i + 1) % q.Length]))
                    .Max()))
                .GroupBy(m => (Math.Round(m.Kurz, 1), Math.Round(m.Lang, 1)))
                .OrderByDescending(g => g.Count()))
                Console.WriteLine($"        {g.Key.Item1,5:F1} x {g.Key.Item2,6:F1} m"
                    + $"   x{g.Count()}");
            foreach (var q in quads.Where(q => Kuerzeste(q) > 0.5 && Kuerzeste(q) < 1.5))
                Console.WriteLine("        halbbreit: " + string.Join(" ",
                    q.Select(pt => $"({pt.x:F1}/{pt.y:F1})")));

            /*
             * KEIN ENTARTETES VIERECK IN DIE VORSCHAU.
             *
             * Es traegt keine Flaeche, wird aber gefaerbt - und genau das
             * sieht der Nutzer als "komplett andere Grafik".
             */
            if (entartet > 0)
            {
                fehler++;
                Console.WriteLine($"FEHLER: Randstrassen "
                    + $"{(randstrassen ? "an" : "aus")}: {entartet} entartete "
                    + "Zufahrtsvierecke gehen in die Vorschau");
            }
        }

        Console.WriteLine($"Zufahrtsquads: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
