using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /*
     * Grundriss und Regler aus dem Abzug 20260923-002622-719.
     * 5-cm-Proben: vorher 3,00 m Loch und 3,60 m Gruen-Asymmetrie;
     * ohne Eckfang 0,00 m Loch, aber dieselbe Asymmetrie.
     */
    private static int RunRandgruen()
    {
        var form = new[]
        {
            // EXAKT aus dem Abzug. Auf 0,1 m gerundet passt eine Bucht
            // mehr in die Reihe und der Fehler verschwindet - genau daran
            // ist meine erste Nachstellung am 2026-09-23 vorbeigelaufen.
            new float2(-541.6783447265625f, -496.85504150390625f),
            new float2(-570.720703125f, -573.3721923828125f),
            new float2(-629.9588012695312f, -550.88818359375f),
            new float2(-600.9171142578125f, -474.3728942871094f),
        };

        LayoutSettings Basis()
        {
            var s = LayoutSettings.Cs2;
            s.Zellen = true; s.Auto = false; s.AutomaticEntrances = false;
            s.AngleMode = "edge"; s.Randstrassen = true;
            s.Es = 1; s.Md = 2.5;
            return s;
        }

        var nurZufahrt = Basis();
        nurZufahrt.Entrances = new[]
        {
            new Entrance { Edge = 3, Along = 31.680980682373047 },
        };

        var mitFusswegen = Basis();
        mitFusswegen.Entrances = new[]
        {
            new Entrance { Edge = 3, Along = 31.680980682373047 },
            new Entrance { Edge = 3, Along = 10.399999618530273,
                Corner = "start", Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 3, Along = 52.96149444580078,
                Corner = "end", Art = Zufahrtsart.Fussweg },
        };

        var freiGesetzt = Basis();
        freiGesetzt.Entrances = new[]
        {
            new Entrance { Edge = 3, Along = 31.680980682373047 },
            new Entrance { Edge = 3, Along = 10.399999618530273,
                Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 3, Along = 52.96149444580078,
                Art = Zufahrtsart.Fussweg },
        };

        var layouts = new[]
        {
            (Name: "ohne Fusswege", Layout: ParkingGeometry.Build(form, nurZufahrt)),
            (Name: "mit Fusswegen (gesnappt)", Layout: ParkingGeometry.Build(form, mitFusswegen)),
            (Name: "mit Fusswegen (frei)", Layout: ParkingGeometry.Build(form, freiGesetzt)),
        };
        foreach (var (name, lay) in layouts)
        {
            Console.WriteLine($"\n### {name}: {lay.Stalls} Buchten, "
                + $"{lay.Green.Length} Gruenringe");
            for (var kante = 0; kante < form.Length; kante++)
            {
                var a = form[kante];
                var b = form[(kante + 1) % form.Length];
                var laenge = math.distance(a, b);
                var u = (b - a) / laenge;
                double gedeckt = 0;
                var stuecke = 0;
                foreach (var ring in lay.Green)
                {
                    var qs = ring.Select(p => -(p.x - a.x) * u.y + (p.y - a.y) * u.x).ToArray();
                    if (qs.Max() > 0.2f || qs.Min() < -1.2f) continue;
                    var als = ring.Select(p => (p.x - a.x) * u.x + (p.y - a.y) * u.y).ToArray();
                    gedeckt += als.Max() - als.Min();
                    stuecke++;
                }
                Console.WriteLine($"  Kante {kante}: Laenge {laenge,6:0.00} m | "
                    + $"Randgruen {stuecke,2} Stueck, {gedeckt,6:0.00} m "
                    + $"= {(laenge > 0 ? gedeckt / laenge * 100 : 0),5:0.0} %");
                if (kante != 3) continue;
                // Der Streifen der ersten Buchtenreihe: wo ist Asphalt ohne
                // Bucht und ohne Gras - also das Loch, das der Nutzer sieht?
                var loch = 0.0;
                for (var t = 0.0; t < laenge; t += 0.25)
                {
                    var punkt = a + u * (float)t
                        + new float2(-u.y, u.x) * -3.9f;
                    var imGruen = lay.Green.Any(r => Drin(punkt, r));
                    var inBucht = lay.Bay.Any(bb => Drin(punkt, bb));
                    var aufAsphalt = lay.AsphaltSurface.Any(r => Drin(punkt, r));
                    if (aufAsphalt && !imGruen && !inBucht) loch += 0.25;
                }
                Console.WriteLine($"      Tiefe 3,9 m: {loch,5:0.00} m Asphalt "
                    + "ohne Bucht und ohne Gras");
            }
        }
        var start = form[3];
        var laenge3 = math.distance(start, form[0]);
        var u3 = (form[0] - start) / laenge3;
        const float schritt = 0.05f;
        var anzahl = (int)Math.Floor(laenge3 / schritt);
        var fehler = 0;
        foreach (var (name, lay) in layouts)
        {
            var loch = 0;
            var gruen = new bool[anzahl];
            var bucht = new bool[anzahl];
            var weg = new bool[anzahl];
            for (var i = 0; i < anzahl; i++)
            {
                var t = (i + 0.5f) * schritt;
                var punkt = start + u3 * t + new float2(-u3.y, u3.x) * -3.9f;
                gruen[i] = lay.Green.Any(r => Drin(punkt, r));
                bucht[i] = lay.Bay.Any(r => Drin(punkt, r));
                weg[i] = lay.EntranceQuad.Any(r => Drin(punkt, r));
                var asphalt = lay.AsphaltSurface.Any(r => Drin(punkt, r));
                if (asphalt && !gruen[i] && !bucht[i] && !weg[i]) loch++;
            }
            var spiegelfehlerGruen = 0;
            var spiegelfehlerBucht = 0;
            for (var i = 0; i < anzahl / 2; i++)
            {
                var t = (i + 0.5f) * schritt;
                var spiegelpunkt = start + u3 * (laenge3 - t)
                    + new float2(-u3.y, u3.x) * -3.9f;
                if (gruen[i] != lay.Green.Any(r => Drin(spiegelpunkt, r)))
                    spiegelfehlerGruen++;
                if (bucht[i] != lay.Bay.Any(r => Drin(spiegelpunkt, r)))
                    spiegelfehlerBucht++;
            }
            Console.WriteLine($"{name}: echtes Loch {loch * schritt:F2} m, "
                + $"Spiegelabweichung Gruen {spiegelfehlerGruen * schritt:F2} m, "
                + $"Bucht {spiegelfehlerBucht * schritt:F2} m, "
                + $"Gruen {gruen.Count(w => w) * schritt:F2} m, "
                + $"Bucht {bucht.Count(w => w) * schritt:F2} m, "
                + $"Weg {weg.Count(w => w) * schritt:F2} m");
            if (name == "ohne Fusswege")
            {
                if (Math.Abs(weg.Count(w => w) * schritt - 7.0f) > 0.10f)
                    fehler++;
                continue;
            }
            if (loch * schritt > 0.10f) fehler++;
            if (spiegelfehlerGruen * schritt > 0.10f) fehler++;
            if (spiegelfehlerBucht * schritt > 0.10f) fehler++;
            if (gruen.Count(w => w) * schritt < 2.0f) fehler++;
            if (bucht.Count(w => w) * schritt < 20.0f) fehler++;
            if (Math.Abs(weg.Count(w => w) * schritt - 11.0f) > 0.10f)
                fehler++;
        }
        if (layouts[1].Layout.Stalls != layouts[2].Layout.Stalls) fehler++;
        Console.WriteLine($"Randgruen: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    private static bool Drin(float2 p, float2[] ring)
    {
        var drin = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            if (ring[i].y > p.y != ring[j].y > p.y
                && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y)
                    / (ring[j].y - ring[i].y) + ring[i].x)
                drin = !drin;
        return drin;
    }
}
