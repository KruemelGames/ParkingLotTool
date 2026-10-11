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
            // Oberhalb der Spitze P lagen 25 Buchten an Fahrbahn ohne Weg, bis
            // 36 m vom naechsten Fahrweg (Nutzerentscheidung 2026-10-09: dort Gruen).
            var weitesteBucht = layout.Bay.Select(b =>
            {
                var m = b.Aggregate(float2.zero, (s2, q) => s2 + q) / b.Length;
                return layout.NetLine.Where(l => l.Kind != "zoning").Select(l =>
                {
                    var ab = l.B - l.A;
                    var t = math.clamp(math.dot(m - l.A, ab) / math.max(math.lengthsq(ab), 1e-9f), 0f, 1f);
                    return math.distance(m, l.A + ab * t);
                }).DefaultIfEmpty(float.MaxValue).Min();
            }).DefaultIfEmpty(0f).Max();
            if (weitesteBucht > 12f) probleme.Add($"Bucht {weitesteBucht:F1} m vom naechsten Fahrweg");
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

    /** Zusammenhaengende Teile des Autowegenetzes (Wege teilen sich an Einmuendungen exakte Endpunkte). */
    private static int Netzteile(ParkingLayout layout)
    {
        var eltern = new Dictionary<(long, long), (long, long)>();
        (long, long) K(float2 p) => ((long)math.round(p.x * 100), (long)math.round(p.y * 100));
        (long, long) Find((long, long) x) { while (!eltern[x].Equals(x)) x = eltern[x] = eltern[eltern[x]]; return x; }
        foreach (var l in layout.NetLine.Where(l => l.Kind == "aisle" || l.Kind == "cross" || l.Kind == "perimeter"))
        {
            var a = K(l.A); var b = K(l.B);
            if (!eltern.ContainsKey(a)) eltern[a] = a;
            if (!eltern.ContainsKey(b)) eltern[b] = b;
            var ra = Find(a); var rb = Find(b);
            if (!ra.Equals(rb)) eltern[ra] = rb;
        }
        return eltern.Keys.Select(Find).Distinct().Count();
    }
}

/**
 * FAST GERADE ECKE - Preview-Bericht 6T8J (2026-10-10).
 *
 * Ein eingerasteter Punkt auf einer geraden Kante lag 3,6 mm daneben. Die
 * Zerlegung hielt ihn fuer eine Reflexecke und brach mit "A polygon needs at
 * least three corners" ab - keine Vorschau. Geprueft: baut, gedreht.
 */
internal static partial class Program
{
    private static readonly float2[] FastGeradeUmriss =
    {
        new float2(-1420.32532f, -114.501083f), new float2(-1460.81238f, -8.789024f),
        new float2(-1397.25732f, 15.5486946f), new float2(-1340.234f, 37.389f),
        new float2(-1324.34106f, -4.10700035f), new float2(-1381.3645f, -25.946743f),
        new float2(-1356.7489f, -90.21639f),
    };

    private static int RunFastGerade()
    {
        var mitte = FastGeradeUmriss.Aggregate(float2.zero, (s, p) => s + p) / FastGeradeUmriss.Length;
        var fehler = 0;
        foreach (var randstrassen in new[] { true, false })
        foreach (var grad in new[] { 0f, 30f, 90f, 200f })
        {
            var settings = LayoutSettings.Cs2;
            settings.Randstrassen = randstrassen;
            var bogen = math.radians(grad);
            var (s, c) = (math.sin(bogen), math.cos(bogen));
            var umriss = FastGeradeUmriss.Select(p =>
            {
                var d = p - mitte;
                return mitte + new float2(c * d.x - s * d.y, s * d.x + c * d.y);
            }).ToArray();
            var name = $"Randstrassen {(randstrassen ? "an" : "aus")}, {grad:F0} Grad";
            try
            {
                var layout = ParkingGeometry.Build(umriss, settings);
                Console.WriteLine($"  ok {name}: {layout.Stalls} Buchten");
            }
            catch (Exception e)
            {
                fehler++;
                Console.WriteLine($"  FEHLER {name}: {e.Message}");
            }
        }
        Console.WriteLine($"Fast gerade Ecke: 8 Laeufe, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}

/**
 * QUERSTRASSEN AUS - Schalter "Cross roads" (2026-10-11).
 *
 * Aus heisst: Cr negativ im Bauzettel. Mit Randstrasse legt der Kern dann
 * keine einzige Querstrasse. Ohne Randstrasse bleibt je Gassenpaar die
 * NOTWENDIGE Verbindung: es duerfen nicht mehr Querwege werden, und das
 * Wegenetz muss genauso zusammenhaengen wie mit Querstrassen.
 */
internal static partial class Program
{
    private static int RunQuerstrassenAus()
    {
        var formen = new (string Name, float2[] Umriss)[]
        {
            ("Rechteck", new[] { new float2(0, 0), new float2(160, 0), new float2(160, 90), new float2(0, 90) }),
            ("L-Form", new[] { new float2(0, 0), new float2(150, 0), new float2(150, 60), new float2(60, 60), new float2(60, 140), new float2(0, 140) }),
            ("Schief", new[] { new float2(0, 0), new float2(140, 20), new float2(120, 110), new float2(-10, 90) }),
            ("Issue10", SchmalerArmUmriss),
            ("FastGerade", FastGeradeUmriss),
        };
        var fehler = 0;
        foreach (var (name, umriss) in formen)
        foreach (var randstrassen in new[] { true, false })
        {
            var an = LayoutSettings.Cs2;
            an.Randstrassen = randstrassen;
            var aus = LayoutSettings.Cs2;
            aus.Randstrassen = randstrassen;
            aus.Cr = -an.Cr;
            var fall = $"{name}, Randstrassen {(an.Randstrassen ? "an" : "aus")}";
            try
            {
                var mit = ParkingGeometry.Build(umriss, an);
                var ohne = ParkingGeometry.Build(umriss, aus);
                var querMit = mit.NetLine.Count(l => l.Kind == "cross");
                var querOhne = ohne.NetLine.Count(l => l.Kind == "cross");
                var verworfen = (ohne.AsphaltSurface ?? Array.Empty<float2[]>()).Concat(ohne.GrassSurface ?? Array.Empty<float2[]>())
                    .Count(r => r != null && r.Length >= 3 && Cs2Triangulierung.Dreiecke(r) == 0);
                var teileMit = Netzteile(mit);
                var teileOhne = Netzteile(ohne);
                var ok = verworfen == 0 && (randstrassen
                    ? querOhne == 0
                    : querOhne <= querMit && teileOhne <= teileMit);
                if (!ok) fehler++;
                Console.WriteLine($"  {(ok ? "ok" : "FEHLER")} {fall}: Querstrassen {querMit} -> {querOhne}, Buchten {mit.Stalls} -> {ohne.Stalls}, Netzteile {teileMit} -> {teileOhne}"
                    + (verworfen > 0 ? $", CS2 verwirft {verworfen}" : ""));
            }
            catch (Exception e)
            {
                fehler++;
                Console.WriteLine($"  FEHLER {fall}: {e.GetType().Name}: {e.Message}");
            }
        }
        Console.WriteLine($"Querstrassen aus: {formen.Length * 2} Laeufe, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
