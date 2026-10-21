using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Program
{
    private static int PruefeAussenband()
    {
        var fehler = PruefeAussenbandGrundregeln();
        var faelle = 0;
        foreach (var pfad in Directory.GetFiles("Tests/GeometryParity/Aussenband", "*.json").OrderBy(p => p))
        foreach (var rand in new[] { true, false })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pfad));
            var root = doc.RootElement;
            var optionen = new JsonSerializerOptions
            {
                Converters = { new Float2Json(), new JsonStringEnumConverter() },
            };
            var s = root.GetProperty("Einstellungen").Deserialize<LayoutSettings>(optionen);
            var werte = root.GetProperty("Einstellungen");
            s.Zellen = werte.GetProperty("Zellen").GetBoolean();
            s.EineFlaeche = werte.GetProperty("EineFlaeche").GetBoolean();
            s.NoNotch = werte.GetProperty("NoNotch").GetBoolean();
            s.Single = werte.GetProperty("Single").GetBoolean();
            s.NoHalf = werte.GetProperty("NoHalf").GetBoolean();
            s.Randstrassen = rand;
            var site = root.GetProperty("PolygonXZ").EnumerateArray()
                .Select(p => new float2(p.GetProperty("X").GetSingle(), p.GetProperty("Z").GetSingle())).ToArray();
            var layout = ParkingGeometry.Build(site, s);
            var name = Path.GetFileNameWithoutExtension(pfad) + (rand ? "/AN" : "/AUS");
            var bandtreffer = 0;
            foreach (var f in s.Zoningflaechen)
            {
                var (u, v) = ParkingGeometry.ZoningRichtungen(f.Winkel);
                // Unabhaengiger Zellmassstab: 8 m, Achse 4 m vor dem Feld,
                // und an beiden Ringenden die halbe Strassenbreite dazu.
                var achse = f.Ecke - 4 * u - 4 * v;
                var b = f.Spalten * 8 + 8;
                var t = f.Reihen * 8 + 8;
                for (var seite = 0; seite < 4; seite++)
                {
                    var tiefe = f.Aussentiefen?[seite] ?? 0;
                    if (tiefe <= 0) continue;
                    var lo = seite == 0 ? new double2(-4, -4 - tiefe)
                        : seite == 1 ? new double2(b + 4, -4)
                        : seite == 2 ? new double2(-4, t + 4) : new double2(-4 - tiefe, -4);
                    var hi = seite == 0 ? new double2(b + 4, -4)
                        : seite == 1 ? new double2(b + 4 + tiefe, t + 4)
                        : seite == 2 ? new double2(b + 4, t + 4 + tiefe) : new double2(-4, t + 4);
                    lo += 0.02; hi -= 0.02;
                    double2 Lokal(float2 p) => new double2(
                        ((double)p.x - achse.x) * u.x + ((double)p.y - achse.y) * u.y,
                        ((double)p.x - achse.x) * v.x + ((double)p.y - achse.y) * v.y);
                    foreach (var n in layout.NetLine.Where(n => n.Kind == "aisle" || n.Kind == "cross"))
                        if (AussenbandSchnitt(Lokal(n.A), Lokal(n.B), lo, hi)) bandtreffer++;
                }
            }
            var erreicht = AussenbandErreicht(layout.NetLine, site, s);
            var zoningFrei = Enumerable.Range(0, layout.NetLine.Length)
                .Count(i => layout.NetLine[i].Kind == "zoning" && !erreicht.Contains(i));
            var wegeFrei = Enumerable.Range(0, layout.NetLine.Length)
                .Count(i => layout.NetLine[i].Art != Zufahrtsart.Fussweg && !erreicht.Contains(i));
            // Ganze Buchtkante muss an einer von der Zufahrt erreichbaren
            // Fahrbahn liegen. Die Gegenprobe entfernt alle Netzlinien.
            var buchtenFrei = AussenbandFreieBuchten(layout, s, erreicht);
            var gegenprobe = AussenbandFreieBuchten(layout, s, new HashSet<int>());
            if (bandtreffer != 0 || zoningFrei != 0 || buchtenFrei != 0
                || layout.Stalls == 0 || gegenprobe != layout.Stalls) fehler++;
            Console.WriteLine($"{name}: {layout.Stalls} Buchten, {layout.NetLine.Length} Netze, "
                + $"Bandtreffer {bandtreffer}, Zoning unerreicht {zoningFrei}, Wege unerreicht {wegeFrei}, "
                + $"Buchten abgehaengt {buchtenFrei}");
            var verworfen = layout.AsphaltSurface.Concat(layout.GrassSurface).Concat(layout.ZoningSurface)
                .Concat(layout.ZoningRoadSurface).Count(r => r.Length >= 3 && Cs2Triangulierung.Dreiecke(r) == 0);
            Console.WriteLine($"  Material: CS2 verwirft {verworfen}, Warnungen {string.Join(" | ", layout.Warnings)}");
            if (Environment.GetEnvironmentVariable("PLT_AUSSEN_DUMP") is string ziel)
            {
                Directory.CreateDirectory(ziel);
                File.WriteAllText(Path.Combine(ziel, name.Replace('/', '-') + ".json"),
                    JsonSerializer.Serialize(new {
                        Bay = layout.Bay.Select(b => b.Select(p => new[] { p.x, p.y })),
                        Net = layout.NetLine.Select(n => new { n.Kind, A = new[] { n.A.x, n.A.y }, B = new[] { n.B.x, n.B.y }, Art = n.Art.ToString() })
                    }));
            }
            faelle++;
        }
        fehler += PruefeAussenbandBitgleich();
        fehler += PruefeBandwirkung();
        Console.WriteLine($"AUSSENBAND: {faelle} Bandfaelle, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    /*
     * GELB WIRD NICHT FREIGERAEUMT (21.10.): mit wirksamer Tiefe und
     * gekuerzten Enden muss das Bauland kleiner werden, nichts darf
     * abreissen, kein Ring an CS2 fallen, keine Bucht verloren gehen.
     * Am 5x3-Abzug: Seite 1 gewaehlt 40 m, wirksam 16 m, je Ende 8 m weg.
     */
    private static int PruefeBandwirkung()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText("Tests/GeometryParity/Aussenband/Band5x3.json"));
        var root = doc.RootElement;
        var optionen = new JsonSerializerOptions { Converters = { new Float2Json(), new JsonStringEnumConverter() } };
        LayoutSettings Lade()
        {
            var s = root.GetProperty("Einstellungen").Deserialize<LayoutSettings>(optionen);
            var werte = root.GetProperty("Einstellungen");
            s.Zellen = werte.GetProperty("Zellen").GetBoolean();
            s.EineFlaeche = werte.GetProperty("EineFlaeche").GetBoolean();
            s.NoNotch = werte.GetProperty("NoNotch").GetBoolean();
            s.Single = werte.GetProperty("Single").GetBoolean();
            s.NoHalf = werte.GetProperty("NoHalf").GetBoolean();
            s.Randstrassen = true;
            return s;
        }
        var site = root.GetProperty("PolygonXZ").EnumerateArray()
            .Select(p => new float2(p.GetProperty("X").GetSingle(), p.GetProperty("Z").GetSingle())).ToArray();
        double Flaeche(float2[][] ringe) => (ringe ?? Array.Empty<float2[]>()).Sum(r =>
        {
            double summe = 0;
            for (var i = 0; i < r.Length; i++)
            {
                var a = r[i]; var b = r[(i + 1) % r.Length];
                summe += (double)a.x * b.y - (double)b.x * a.y;
            }
            return Math.Abs(summe) / 2;
        });
        var voll = Lade();
        var gekuerzt = Lade();
        foreach (var f in gekuerzt.Zoningflaechen)
        {
            f.AussenWirksam = (double[])f.Aussentiefen.Clone();
            f.AussenWirksam[1] = Math.Min(f.AussenWirksam[1], 16);
            f.AussenKuerzung = new double[] { 0, 0, 8, 8, 0, 0, 0, 0 };
        }
        var lv = ParkingGeometry.Build(site, voll);
        var lg = ParkingGeometry.Build(site, gekuerzt);
        var erreicht = AussenbandErreicht(lg.NetLine, site, gekuerzt);
        var zoningFrei = Enumerable.Range(0, lg.NetLine.Length)
            .Count(i => lg.NetLine[i].Kind == "zoning" && !erreicht.Contains(i));
        var buchtenFrei = AussenbandFreieBuchten(lg, gekuerzt, erreicht);
        var verworfen = lg.AsphaltSurface.Concat(lg.GrassSurface).Concat(lg.ZoningSurface)
            .Concat(lg.ZoningRoadSurface).Count(r => r.Length >= 3 && Cs2Triangulierung.Dreiecke(r) == 0);
        var bv = Flaeche(lv.ZoningSurface);
        var bg = Flaeche(lg.ZoningSurface);
        var fehler = bg < bv - 1 && zoningFrei == 0 && buchtenFrei == 0 && verworfen == 0
            && lg.Stalls >= lv.Stalls ? 0 : 1;
        Console.WriteLine($"BANDWIRKUNG Band5x3/AN: Bauland {bv:F0} -> {bg:F0} m2, Buchten {lv.Stalls} -> {lg.Stalls}, "
            + $"Zoning unerreicht {zoningFrei}, Buchten abgehaengt {buchtenFrei}, CS2 verwirft {verworfen}"
            + (fehler == 0 ? "" : "  FEHLER"));
        return fehler;
    }

    private static int PruefeAussenbandGrundregeln()
    {
        var z = new Zoningvorgabe
        {
            Ecke = new Punkt(0, 0), Spalten = 2, Reihen = 2,
            Rand = 8, Aussen = new[] { 0.0, 16, 24, 0 },
        };
        var innen = new[] { new Punkt(-100, -100),
            new Punkt(100, -100),
            new Punkt(100, 100),
            new Punkt(-100, 100) };
        var fehler = 0;
        foreach (var name in new[] { "Frei", "ZufahrtFrei" })
        {
            var methode = typeof(Ringlosplan).GetMethod(name,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            foreach (var band in new[] { true, false })
            {
                var weg = new Ringlosplan.Weg
                {
                    A = new Punkt(band ? 28 : -30, 8),
                    B = new Punkt(band ? 38 : -8, 8), Breite = 3,
                };
                var frei = (bool)methode.Invoke(null, new object[] { weg, innen, new[] { z } });
                if (frei == band) fehler++;
            }
        }
        Console.WriteLine($"Ringlose Wegzulassung: 4 Gegenproben, {fehler} Fehler");
        return fehler;
    }

    private static int PruefeAussenbandBitgleich()
    {
        // Vollstaendige Layoutabdruecke des unveraenderten Kerns c603f29,
        // nicht aus dem neuen Pfad uebernommene Sollwerte.
        var pfad = "Tests/GeometryParity/Aussenband/Bitgleich.ref";
        var erwartet = File.Exists(pfad)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(pfad))
            : new Dictionary<string, string>();
        var faelle = new List<(string Name, float2[] Site, LayoutSettings Settings)>();
        foreach (var f in Directory.GetFiles("Tests/GeometryParity/Aussenband", "*.json").OrderBy(p => p))
        foreach (var rand in new[] { true, false })
        {
            using var d = JsonDocument.Parse(File.ReadAllText(f));
            var s = d.RootElement.GetProperty("Einstellungen").Deserialize<LayoutSettings>(new JsonSerializerOptions
            {
                Converters = { new Float2Json(), new JsonStringEnumConverter() },
            });
            s.Zellen = true;
            s.Randstrassen = rand;
            foreach (var z in s.Zoningflaechen) z.Aussentiefen = new double[4];
            var site = d.RootElement.GetProperty("PolygonXZ").EnumerateArray()
                .Select(p => new float2(p.GetProperty("X").GetSingle(), p.GetProperty("Z").GetSingle())).ToArray();
            faelle.Add((Path.GetFileNameWithoutExtension(f) + (rand ? "/AN" : "/AUS"), site, s));
        }
        foreach (var c in Cases)
        foreach (var rand in new[] { true, false })
        {
            var s = LayoutSettings.Cs2;
            s.Randstrassen = rand;
            faelle.Add((c.Name + (rand ? "/AN" : "/AUS"), c.Site, s));
        }
        var fehler = 0;
        foreach (var c in faelle)
        {
            var ist = Vergleich.Abdruck(ParkingGeometry.Build(c.Site, c.Settings));
            Console.WriteLine($"BITGLEICH {c.Name}: {ist}");
            if (!erwartet.TryGetValue(c.Name, out var soll) || soll != ist) fehler++;
        }
        Console.WriteLine($"Ohne Aussenband: {faelle.Count} vollstaendige Bitvergleiche, {fehler} Fehler");
        return fehler;
    }

    private static bool AussenbandSchnitt(double2 a, double2 b, double2 lo, double2 hi)
    {
        var von = 0.0;
        var bis = 1.0;
        for (var k = 0; k < 2; k++)
        {
            var d = b[k] - a[k];
            if (Math.Abs(d) < 1e-12)
            {
                if (a[k] < lo[k] || a[k] > hi[k]) return false;
                continue;
            }
            var p = (lo[k] - a[k]) / d;
            var q = (hi[k] - a[k]) / d;
            von = Math.Max(von, Math.Min(p, q));
            bis = Math.Min(bis, Math.Max(p, q));
        }
        return bis > von;
    }

    private static HashSet<int> AussenbandErreicht(NetSegment[] netz, float2[] site, LayoutSettings s)
    {
        var nachbarn = netz.Select(_ => new List<int>()).ToArray();
        bool Nah(float2 a, float2 b) => math.distance(a, b) < 0.001;
        for (var i = 0; i < netz.Length; i++)
        for (var j = i + 1; j < netz.Length; j++)
        {
            if (netz[i].Art == Zufahrtsart.Fussweg || netz[j].Art == Zufahrtsart.Fussweg) continue;
            var iz = netz[i].Kind == "zoning";
            var jz = netz[j].Kind == "zoning";
            var verbunden = Nah(netz[i].A, netz[j].A) || Nah(netz[i].A, netz[j].B)
                || Nah(netz[i].B, netz[j].A) || Nah(netz[i].B, netz[j].B) || (iz != jz
                ? ZoningMuendung.Muendet(netz, netz[iz ? j : i], netz[iz ? i : j], s.Ai, s.Cw, out _)
                : Nah(netz[i].A, netz[j].A) || Nah(netz[i].A, netz[j].B)
                    || Nah(netz[i].B, netz[j].A) || Nah(netz[i].B, netz[j].B));
            if (!verbunden) continue;
            nachbarn[i].Add(j); nachbarn[j].Add(i);
        }
        var offen = new List<int>();
        var erreicht = new HashSet<int>();
        for (var i = 0; i < netz.Length; i++)
            if (netz[i].Kind == "entrance" && netz[i].Art != Zufahrtsart.Fussweg
                && site.Where((p, k) => AbstandZuStrecke(netz[i].A, p, site[(k + 1) % site.Length]) < 0.002
                    || AbstandZuStrecke(netz[i].B, p, site[(k + 1) % site.Length]) < 0.002).Any())
            { erreicht.Add(i); offen.Add(i); }
        for (var i = 0; i < offen.Count; i++)
            foreach (var j in nachbarn[offen[i]])
                if (erreicht.Add(j)) offen.Add(j);
        return erreicht;
    }

    private static int AussenbandFreieBuchten(ParkingLayout l, LayoutSettings s, HashSet<int> erreicht)
    {
        bool Fahrbahn(float2 p) => erreicht.Any(i => l.NetLine[i].Art != Zufahrtsart.Fussweg
            && AbstandZuStrecke(p, l.NetLine[i].A, l.NetLine[i].B)
                <= (l.NetLine[i].Kind == "zoning" ? 4 : ZoningMuendung.EigeneBreite(l.NetLine[i], s.Ai, s.Cw) / 2) + 0.002);
        bool Kante(float2 a, float2 b) => Enumerable.Range(0, 101).Select(k => k / 100f)
            .All(t => Fahrbahn(math.lerp(a, b, t)));
        return l.Bay.Count(b => !Kante(b[0], b[1]) && !Kante(b[2], b[3]));
    }
}
