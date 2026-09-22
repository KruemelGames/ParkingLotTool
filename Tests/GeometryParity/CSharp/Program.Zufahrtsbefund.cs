using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunZufahrtsbefund()
    {
        var form = new[]
        {
            new float2(-625.7109985351562f, -550.5310668945312f),
            new float2(-649.7633056640625f, -613.9010620117188f),
            new float2(-569.7100219726562f, -644.2855834960938f),
            new float2(-558.2091064453125f, -613.9844360351562f),
            new float2(-586.99951171875f, -565.22412109375f),
        };
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }
        LayoutSettings Einstellungen(bool ring)
        {
            var s = LayoutSettings.Cs2;
            s.Es = 1; s.Ai = 7; s.Cw = 3; s.Sl = 5.9;
            s.Sw = 3; s.Md = 2.5; s.Cr = 33; s.Qk = true;
            s.Zellen = true; s.Auto = false; s.AngleMode = "edge";
            s.Randstrassen = ring; s.AutomaticEntrances = false;
            return s;
        }
        float2 Start(Entrance z)
        {
            var a = form[z.Edge];
            var b = form[(z.Edge + 1) % form.Length];
            return a + math.normalize(b - a) * (float)z.Along;
        }
        float2[] Linie(ParkingLayout layout, Entrance z)
        {
            var start = Start(z);
            return layout.EntranceLine.FirstOrDefault(line =>
                line != null && line.Length >= 2
                && math.distance(line[0], start) < 0.03f);
        }
        bool ImPolygon(float2[] polygon, float2 point)
        {
            var drin = false;
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    drin = !drin;
            }
            return drin;
        }

        var z0 = new Entrance { Edge = 3, Along = 36.46894073486328 };
        var z1 = new Entrance { Edge = 2, Along = 12.590177536010742 };
        var ringlos = Einstellungen(false);
        ringlos.Entrances = new[] { z0, z1 };
        var grundlage = ParkingGeometry.Build(form, ringlos);
        var basisLinie = Linie(grundlage, z0);
        if (basisLinie != null)
            Console.WriteLine("Ohne Fang: Overlayluecken " + Enumerable.Range(1, 99)
                .Count(i => !grundlage.EntranceQuad.Any(q => q.Length >= 3
                    && ImPolygon(q, math.lerp(basisLinie[0], basisLinie[1], i / 100f)))));
        Pruefe(grundlage.AisleLine.Length == 3,
            $"Grundlage hat {grundlage.AisleLine.Length} statt 3 Gassen");
        if (grundlage.AisleLine.Length < 3) return 1;

        // Im Abzug traf der Fang Kante 3 an Gasse 2 und Kante 2 an Gasse 0.
        // Mit diesen Achsen verschwanden vorher 2 von 2 Zufahrten.
        foreach (var (zugang, gasse) in new[]
        {
            (z0, grundlage.AisleLine[2]), (z1, grundlage.AisleLine[0]),
        })
        {
            var tangent = math.normalize(form[(zugang.Edge + 1) % form.Length]
                - form[zugang.Edge]);
            var inward = new float2(-tangent.y, tangent.x);
            var direction = math.normalize(gasse[0] - gasse[1]);
            zugang.AxisDirection = direction;
            zugang.AxisLength = 6.9 / math.dot(direction, inward);
        }
        var gebaut = ParkingGeometry.Build(form, ringlos);
        Console.WriteLine($"Ring aus, Fangachsen: Linien {gebaut.EntranceLine.Length}, "
            + $"Warnungen {gebaut.Warnings.Length}, Quads {gebaut.EntranceQuad.Length}");
        foreach (var (zugang, ziel) in new[]
        {
            (z0, new float2(-588.2999f, -578.1976f)),
            (z1, new float2(-568.0471f, -631.4502f)),
        })
        {
            var line = Linie(gebaut, zugang);
            Pruefe(line != null, $"Kante {zugang.Edge}: keine gebaute Linie");
            if (line == null) continue;
            var richtung = math.normalize(line[1] - line[0]);
            var richtungsfehler = math.distance(richtung, zugang.AxisDirection.Value);
            var endfehler = math.distance(line[1], ziel);
            var mitte = math.lerp(line[0], line[1], 0.25f);
            var bedeckt = gebaut.AsphaltSurface.Any(q => q.Length >= 3
                && ImPolygon(q, mitte));
            // Der bestehende Streifenfueller laesst selbst ohne Fang 20/99
            // Overlaypunkte frei; diese separate Altlast bleibt sichtbar.
            var overlayLuecken = Enumerable.Range(1, 99)
                .Count(i => !gebaut.EntranceQuad.Any(q => q.Length >= 3
                    && ImPolygon(q, math.lerp(line[0], line[1], i / 100f))));
            Console.WriteLine($"Kante {zugang.Edge}: Laenge {math.distance(line[0], line[1]):F3} m, "
                + $"Richtung {richtungsfehler:F4}, Endpunkt {endfehler:F4} m, "
                + $"Asphalt {bedeckt}, Overlayluecken {overlayLuecken}/99");
            Pruefe(richtungsfehler < 0.01f && endfehler < 0.05f && bedeckt,
                $"Kante {zugang.Edge}: Richtung, Anschluss oder Belag fehlt");
        }
        Pruefe(!gebaut.Warnings.Any(w => w.Contains("trifft ein Hindernis")),
            "gefangene Zufahrt wurde abgewiesen");

        var ring = Einstellungen(true);
        ring.Entrances = new[]
        {
            new Entrance { Edge = 3, Along = z0.Along,
                AxisDirection = z0.AxisDirection, AxisLength = z0.AxisLength },
            new Entrance { Edge = 2, Along = z1.Along,
                AxisDirection = z1.AxisDirection, AxisLength = z1.AxisLength },
        };
        var ringbau = ParkingGeometry.Build(form, ring);
        var ringTreffer = ring.Entrances.Count(z => Linie(ringbau, z) != null);
        Console.WriteLine($"Ring an: {ringTreffer}/2 gesetzte Zufahrten");
        Pruefe(ringTreffer == 2, "Ringweg verliert eine gesetzte Zufahrt");

        var unten = Einstellungen(false);
        unten.Entrances = new[] { new Entrance { Edge = 1, Along = 40 } };
        var untenbau = ParkingGeometry.Build(form, unten);
        var untenlinie = Linie(untenbau, unten.Entrances[0]);
        var laenge = untenlinie == null ? double.NaN
            : math.distance(untenlinie[0], untenlinie[1]);
        Console.WriteLine($"Untere lange Kante, Along 40 m: Linie {laenge:F3} m, "
            + $"erste Gasse 12,590 m");
        Pruefe(untenlinie != null && Math.Abs(laenge - 12.590f) < 0.05,
            "untere Zufahrt endet nicht an der ersten Gasse");

        Console.WriteLine($"Zufahrtsbefund: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
