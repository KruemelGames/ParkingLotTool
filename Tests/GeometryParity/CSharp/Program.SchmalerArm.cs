using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * SCHMALER ARM - Issue #10 (2026-10-09).
 *
 * Ein U-foermiger Parkplatz, dessen linker Arm oben nur 10,5 m breit ist.
 * Die Randstrassenmittellinie (10,4 m Versatz) klappt dort zu einem Punkt
 * zusammen; die zusammengeklappte Kante hat Laenge null. Beim Teilen der
 * Wege galt sie als kollinear mit jeder anderen Strecke, und ihr Punkt
 * wurde in Randstrassenkanten am anderen Ende eingesetzt: Wege liefen als
 * Stern quer ueber den Innenhof, bis zu 36 m ausserhalb des Umrisses.
 *
 * Geprueft wird der Umriss aus dem Bericht, gedreht und gespiegelt - damit
 * nicht nur genau diese Lage abgedeckt ist.
 */
internal static partial class Program
{
    private const string SchmalerArmEinstellungen = "{\"Es\":1,\"Ai\":7,\"Cw\":3,\"Sl\":5.9,\"Sw\":3,\"Md\":2.5,"
        + "\"Gassenbreite\":5.5,\"Cr\":33,\"Qk\":true,\"Randstrassen\":true,\"Auto\":false,\"Angle\":0,"
        + "\"AngleMode\":\"quer\",\"Randzoning\":[],\"Zoningstrasse\":\"Alley\",\"BusStops\":[],"
        + "\"Entrances\":[{\"Edge\":8,\"Along\":16.136369705200195,\"Art\":\"Zufahrt\"}],"
        + "\"AutomaticEntrances\":false,\"KantenVersatz\":0}";

    private static readonly float2[] SchmalerArmUmriss =
    {
        new float2(5113.4248f, -2973.1836f), new float2(5125.5103f, -3085.0273f),
        new float2(5043.2188f, -3093.8992f), new float2(5031.1328f, -2982.0483f),
        new float2(5020.5635f, -2983.2178f), new float2(5011.1299f, -3097.3591f),
        new float2(5016.4644f, -3133.3848f), new float2(5156.2222f, -3106.7808f),
        new float2(5145.2075f, -2969.7598f),
    };

    private static int RunSchmalerArm()
    {
        var optionen = new JsonSerializerOptions { Converters = { new Float2Json(), new JsonStringEnumConverter() } };
        var mitte = SchmalerArmUmriss.Aggregate(float2.zero, (s, p) => s + p) / SchmalerArmUmriss.Length;
        var fehler = 0;
        var laeufe = 0;
        foreach (var spiegeln in new[] { false, true })
        foreach (var grad in new[] { 0f, 17f, 45f, 90f, 133f, 180f, 251f, 300f })
        {
            var settings = JsonSerializer.Deserialize<LayoutSettings>(SchmalerArmEinstellungen, optionen);
            settings.Zellen = true;
            var bogen = math.radians(grad);
            var (s, c) = (math.sin(bogen), math.cos(bogen));
            var umriss = SchmalerArmUmriss.Select(p =>
            {
                var d = p - mitte;
                if (spiegeln) d.x = -d.x;
                return mitte + new float2(c * d.x - s * d.y, s * d.x + c * d.y);
            }).ToArray();
            // Gespiegelt laeuft der Umriss andersherum; die Zufahrt bleibt an derselben Kante.
            if (spiegeln)
            {
                umriss = umriss.Reverse().ToArray();
                var n = umriss.Length;
                var kante = settings.Entrances[0].Edge;
                var neueKante = n - 2 - kante;
                if (neueKante < 0) neueKante += n;
                var laenge = math.distance(umriss[neueKante], umriss[(neueKante + 1) % n]);
                settings.Entrances[0].Edge = neueKante;
                settings.Entrances[0].Along = laenge - settings.Entrances[0].Along;
            }
            laeufe++;
            var name = $"{(spiegeln ? "gespiegelt" : "original")}, {grad:F0} Grad";
            ParkingLayout layout;
            try { layout = ParkingGeometry.Build(umriss, settings); }
            catch (Exception e)
            {
                fehler++;
                Console.WriteLine($"  FEHLER {name}: {e.GetType().Name}: {e.Message}");
                continue;
            }
            var probleme = new List<string>();
            var draussen = Wegeausserhalb(layout, umriss);
            if (draussen.Count > 0) probleme.Add($"{draussen.Count} Weg(e) mehr als 10 m ausserhalb: {draussen[0]}");
            var grad5 = layout.NetLine.SelectMany(l => new[] { l.A, l.B })
                .GroupBy(p => (math.round(p.x * 100), math.round(p.y * 100)))
                .Select(g => g.Count()).DefaultIfEmpty(0).Max();
            if (grad5 >= 5) probleme.Add($"{grad5} Wege an einem Knoten");
            var befund = RandstrassenErreichbarkeit.Pruefe(layout.NetLine, umriss, settings.Ai, settings.Cw);
            if (!befund.Vollstaendig) probleme.Add($"erreicht {befund.Erreicht} von {befund.Autowege} Autowegen");
            var verworfen = (layout.AsphaltSurface ?? Array.Empty<float2[]>())
                .Concat(layout.GrassSurface ?? Array.Empty<float2[]>())
                .Count(r => r != null && r.Length >= 3 && Cs2Triangulierung.Dreiecke(r) == 0);
            if (verworfen > 0) probleme.Add($"CS2 verwirft {verworfen} Flaechenring(e)");
            if (probleme.Count == 0)
            {
                Console.WriteLine($"  ok {name}: {layout.Stalls} Buchten, {layout.NetLine.Length} Netzlinien");
                continue;
            }
            fehler++;
            Console.WriteLine($"  FEHLER {name}: " + string.Join("; ", probleme));
        }
        Console.WriteLine($"Schmaler Arm: {laeufe} Laeufe, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
