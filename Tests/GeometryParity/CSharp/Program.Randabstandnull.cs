using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * RANDABSTAND 0 - Issue #19 (2026-10-29).
 *
 * Randabstand 0, Randstrasse an, noch keine Zufahrt: die Vorschau brach mit
 * "Eine Teilungsgerade braucht eine Richtung." ab. Ohne Zufahrt teilt der
 * Kern das Gruenband vor der Randstrasse an zwei Stoessen; bei Abstand 0
 * fallen Aussen- und Innenrand zusammen, jeder Stoss hatte Laenge null.
 * Jede Neuberechnung schrieb eine ERROR-Zeile ins Log, die Skyve anzeigt.
 *
 * Geprueft: baut, gedreht, ohne und mit Zufahrt; CS2 verwirft keinen Ring.
 */
internal static partial class Program
{
    private static int RunRandabstandNull()
    {
        var formen = new (string Name, float2[] Umriss)[]
        {
            ("Rechteck", new[] { new float2(0, 0), new float2(80, 0), new float2(80, 60), new float2(0, 60) }),
            ("L-Form", new[] { new float2(0, 0), new float2(150, 0), new float2(150, 60), new float2(60, 60), new float2(60, 140), new float2(0, 140) }),
            ("Schief", new[] { new float2(0, 0), new float2(140, 20), new float2(120, 110), new float2(-10, 90) }),
            ("FastGerade", FastGeradeUmriss),
        };
        var fehler = 0;
        var laeufe = 0;
        foreach (var (form, roh) in formen)
        foreach (var grad in new[] { 0f, 30f, 90f, 200f })
        foreach (var mitZufahrt in new[] { false, true })
        {
            var mitte = roh.Aggregate(float2.zero, (s, p) => s + p) / roh.Length;
            var bogen = math.radians(grad);
            var (sin, cos) = (math.sin(bogen), math.cos(bogen));
            var umriss = roh.Select(p =>
            {
                var d = p - mitte;
                return mitte + new float2(cos * d.x - sin * d.y, sin * d.x + cos * d.y);
            }).ToArray();
            var settings = LayoutSettings.Cs2;
            settings.Es = 0;
            settings.Randstrassen = true;
            settings.AutomaticEntrances = false;
            settings.Entrances = mitZufahrt
                ? new[] { new Entrance { Edge = 0, Along = math.distance(umriss[0], umriss[1]) / 2 } }
                : Array.Empty<Entrance>();
            laeufe++;
            var name = $"{form}, {grad:F0} Grad, {(mitZufahrt ? "mit" : "ohne")} Zufahrt";
            ParkingLayout layout;
            try { layout = ParkingGeometry.Build(umriss, settings); }
            catch (Exception e)
            {
                fehler++;
                Console.WriteLine($"  FEHLER {name}: {e.GetType().Name}: {e.Message}");
                continue;
            }
            var probleme = new List<string>();
            if (layout.Stalls == 0) probleme.Add("keine Bucht");
            var verworfen = (layout.AsphaltSurface ?? Array.Empty<float2[]>())
                .Concat(layout.GrassSurface ?? Array.Empty<float2[]>())
                .Count(r => r != null && r.Length >= 3 && Cs2Triangulierung.Dreiecke(r) == 0);
            if (verworfen > 0) probleme.Add($"CS2 verwirft {verworfen} Flaechenring(e)");
            if (probleme.Count == 0)
            {
                Console.WriteLine($"  ok {name}: {layout.Stalls} Buchten");
                continue;
            }
            fehler++;
            Console.WriteLine($"  FEHLER {name}: " + string.Join("; ", probleme));
        }
        Console.WriteLine($"Randabstand 0: {laeufe} Laeufe, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
