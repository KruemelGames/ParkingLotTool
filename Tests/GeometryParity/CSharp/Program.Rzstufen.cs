using System;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Program
{
    private static readonly float2[] RzStufenForm = {
        new float2(-1037.0201416015625f, 118.68980407714844f),
        new float2(-1128.1390380859375f, 121.81600952148438f),
        new float2(-1178.18505859375f, 76.344001770019531f),
        new float2(-1182.5321044921875f, -50.322002410888672f),
        new float2(-1042.9830322265625f, -55.112003326416016f) };

    private static int RunRzstufen()
    {
        var e = DiagonalRegler();
        e.Entrances = new[] { new Entrance { Edge = 0, Along = 37.865444183349609 } };
        e.Randzoning = new[] { new ParkingGeometry.RandzoningLinie { A = RzStufenForm[2], B = RzStufenForm[3] } };
        var l = ParkingGeometry.Build(RzStufenForm, e);
        var fehler = 0;
        void Pruefe(bool ok, string text) { if (!ok) { fehler++; Console.WriteLine("FEHLER: " + text); } }
        var a = new Punkt(RzStufenForm[2].x, RzStufenForm[2].y);
        var b = new Punkt(RzStufenForm[3].x, RzStufenForm[3].y);
        var u = (b - a) * (1 / Geometrie.Laenge(b - a));
        var n = new Punkt(-u.Y, u.X);
        Punkt Lokal(float2 p) => new Punkt(Geometrie.Skalar(new Punkt(p.x, p.y) - a, n),
            Geometrie.Skalar(new Punkt(p.x, p.y) - a, u));
        Console.WriteLine($"Buchten {l.Stalls}, Gassen {l.AisleLine.Length}, Gras {l.GrassSurface.Length}, Asphalt {l.AsphaltSurface.Length}, RZ {l.ZoningRoadSurface.Length}");
        foreach (var g in l.AisleLine) {
            var x = Lokal(g[0]); var y = Lokal(g[1]);
            Console.WriteLine($"Gasse Lot {x.X:F4}, Laengs {x.Y:F4}..{y.Y:F4}, Laenge {math.distance(g[0], g[1]):F4}");
            Pruefe(math.distance(g[0], g[1]) >= e.Sw - 0.001, "Gasse kuerzer als eine Buchtbreite");
        }
        foreach (var w in l.Warnings) Console.WriteLine("Warnung: " + w);
        Pruefe(!l.Warnings.Any(w => w.Contains("Endfußweg") || w.Contains("Endfussweg")), "Endweg fehlt");
        Pruefe(!l.Warnings.Any(w => w.Contains("left out")), "Materialflaeche wird weggelassen");
        var strasse = l.NetLine.Where(s => s.Kind == "zoning").ToArray();
        Pruefe(strasse.Length > 0, "RZ-Strasse fehlt");
        var von = strasse.SelectMany(s => new[] { Lokal(s.A).Y, Lokal(s.B).Y }).Min();
        var bis = strasse.SelectMany(s => new[] { Lokal(s.A).Y, Lokal(s.B).Y }).Max();
        var rz = l.ZoningRoadSurface.Select(r => r.Select(Lokal).ToArray()).ToArray();

        /*
         * DER KORRIDOR LIEGT DORT, WO DIE STRASSE LIEGT.
         *
         * Hier standen 6,4 und 14,4 fest im Text - die Lage der frueher
         * ERZWUNGENEN RZ-Strasse, 10,4 m vom Rand, halbe Breite beiderseits.
         * Seit dem 2026-09-10 ist die naechstliegende FAHRGASSE die
         * RZ-Strasse; in diesem Fall liegt sie bei Lot 16,56. Der Lauf
         * tastete also einen leeren Streifen ab und meldete 1200 von 1600
         * Deckungsluecken - ein Messgeraet, das die aufgegebene Stelle prueft.
         *
         * AGENTS.md dazu: *"Ein veraltetes Messgeraet ist schlimmer als
         * keins. Wer die Geometrie aendert, zieht die Pruefung mit."*
         *
         * Die Forderung bleibt unveraendert scharf: je Strassenkurs ein
         * RECHTECK, halbe Strassenbreite beiderseits seiner Achse, ueber
         * seine ganze Laenge gedeckt, und keine Ecke der Flaeche abseits
         * einer der Sollgeraden.
         */
        var korridore = strasse.Select(kurs =>
        {
            var la = Lokal(kurs.A); var lb = Lokal(kurs.B);
            var lot = (la.X + lb.X) / 2;
            return (Lot0: lot - ParkingGeometry.ZoningStrassenbreite / 2,
                    Lot1: lot + ParkingGeometry.ZoningStrassenbreite / 2,
                    Von: Math.Min(la.Y, lb.Y), Bis: Math.Max(la.Y, lb.Y));
        }).ToArray();

        var luecken = 0;
        var proben = 0;
        foreach (var k in korridore)
        for (var i = 0; i < 100 / korridore.Length; i++)
        for (var j = 0; j < 16; j++) {
            var p = new Punkt(k.Lot0 + (j + 0.5) * (k.Lot1 - k.Lot0) / 16,
                k.Von + (k.Bis - k.Von) * (i + 0.5) / (100 / korridore.Length));
            proben++;
            if (!rz.Any(r => Geometrie.EnthaeltOderRand(r, p))) luecken++;
        }

        // Eine Ecke darf nur auf einer Sollgeraden IRGENDEINES Korridors
        // liegen - laengs an seinen Enden, quer an seinen beiden Raendern.
        var abweichungen = rz.SelectMany(r => r).Count(p =>
            !korridore.Any(k => Math.Abs(p.X - k.Lot0) < 0.001
                || Math.Abs(p.X - k.Lot1) < 0.001
                || Math.Abs(p.Y - k.Von) < 0.001
                || Math.Abs(p.Y - k.Bis) < 0.001));
        Console.WriteLine($"RZ Laengs {von:F4}..{bis:F4}, {korridore.Length} Korridor(e), "
            + $"Deckungsluecken {luecken}/{proben}, Ecken ausserhalb Sollgeraden {abweichungen}");
        Pruefe(luecken == 0 && abweichungen == 0, "RZ-Korridor hat keine gemeinsame rechteckige Grenze");
        foreach (var pair in new[] { ("Gras", l.GrassSurface, new[] { 5, 6, 11 }), ("Asphalt", l.AsphaltSurface, new[] { 15, 16, 17, 18, 37 }) })
        foreach (var i in pair.Item3.Where(i => i < pair.Item2.Length)) {
            var r = pair.Item2[i].Select(Lokal).ToArray();
            Console.WriteLine($"Nachbar {pair.Item1}[{i}]: {r.Length} Ecken, {Math.Abs(Geometrie.Vorzeichenflaeche(r)):F4} m2, Lot {r.Min(p => p.X):F4}..{r.Max(p => p.X):F4}, Laengs {r.Min(p => p.Y):F4}..{r.Max(p => p.Y):F4}");
        }
        var form = new Formdefinition("RZ", RzStufenForm.Select(p => new Punkt(p.x, p.y)).ToArray());
        var kern = Layoutbauer.Baue(form, new Zelleneinstellungen { Randstrassen = false, Querstrassenabstand = 33,
            Randzoning = new[] { (a, b) } });
        double[] Messpunkt(Punkt p) { var w = kern.Rahmen.NachWelt(p) - a; return new[] { Geometrie.Skalar(w, n), Geometrie.Skalar(w, u) }; }
        if (Environment.GetEnvironmentVariable("PLT_RZ_DUMP") == "1")
            System.IO.File.WriteAllText("artifacts/rzstufen/plan.json", System.Text.Json.JsonSerializer.Serialize(new {
                Gassen = kern.Ringlos.Gassen.Select(g => g.Ecken.Select(Messpunkt)),
                Wege = kern.Ringlos.Fusswege.Select(g => g.Ecken.Select(Messpunkt)),
                Flaechen = kern.Flaechen.Select(f => new { Material = f.Material.ToString(), Ring = f.Aussenring.Knoten.Select(v => Messpunkt(v.Punkt)) }) }));
        foreach (var w in kern.Ringlos.Fusswege) {
            var x = Lokal(new float2((float)kern.Rahmen.NachWelt(w.A).X, (float)kern.Rahmen.NachWelt(w.A).Y));
            var y = Lokal(new float2((float)kern.Rahmen.NachWelt(w.B).X, (float)kern.Rahmen.NachWelt(w.B).Y));
            Console.WriteLine($"Endweg Lot {x.X:F4}..{y.X:F4}, Laengs {x.Y:F4}..{y.Y:F4}");
        }
        Console.WriteLine($"Rzstufen: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
