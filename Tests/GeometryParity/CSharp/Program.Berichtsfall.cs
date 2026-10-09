using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * `--bericht <prebuild.json>`: rechnet die Vorschau eines Vorschau-Berichts
 * exakt nach (Umriss + alle Einstellungen aus `Input`) und prueft, ob jede
 * Fahrgasse von einer Zufahrt aus erreichbar ist.
 *
 * Seit 2026-09-25 schreibt "Vorschau melden" den Vorab-Abzug des aktuellen
 * Stands; damit wird jeder gemeldete Fall ein wiederholbarer Testfall.
 */
internal static partial class Program
{
    private static int RechneBerichtNach(string pfad)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(pfad));
        var input = doc.RootElement.GetProperty("Input");
        var optionen = new JsonSerializerOptions
        {
            Converters = { new Float2Json(), new JsonStringEnumConverter() },
        };
        var alle = input.GetProperty("LayoutSettings").GetProperty("AlleWerte");
        var settings = alle.Deserialize<LayoutSettings>(optionen);
        bool B(string n, bool v) => alle.TryGetProperty(n, out var e) ? e.GetBoolean() : v;
        settings.Zellen = B("Zellen", true);
        settings.EineFlaeche = B("EineFlaeche", false);
        settings.NoNotch = B("NoNotch", false);
        settings.Single = B("Single", false);
        settings.NoHalf = B("NoHalf", false);
        if (alle.TryGetProperty("KantenVersatz", out var kv)) settings.KantenVersatz = kv.GetDouble();
        var site = input.GetProperty("PolygonXZ").EnumerateArray()
            .Select(p => new float2(p.GetProperty("X").GetSingle(),
                p.GetProperty("Z").GetSingle())).ToArray();

        var layout = ParkingGeometry.Build(site, settings);
        var svgZiel = Environment.GetEnvironmentVariable("PLT_BERICHT_SVG");
        if (svgZiel != null) TeilenSvg(layout, site, svgZiel);
        Console.WriteLine(Path.GetFileName(pfad) + ": " + layout.Stalls + " Buchten, "
            + layout.NetLine.Length + " Netzlinien, Randstrassen "
            + (settings.Randstrassen ? "an" : "aus"));
        foreach (var n in layout.NetLine)
            Console.WriteLine($"  {n.Kind,-9} {n.Art,-9} ({n.A.x:F2}/{n.A.y:F2}) -> ({n.B.x:F2}/{n.B.y:F2})");
        var befund = RandstrassenErreichbarkeit.Pruefe(layout.NetLine, site,
            settings.Ai, settings.Cw);
        Console.WriteLine($"Erreichbarkeit: {befund.Erreicht} von {befund.Autowege} Autowegen, "
            + $"{befund.Quellen} Quelle(n), vollstaendig {befund.Vollstaendig}");
        foreach (var w in layout.Warnings) Console.WriteLine("  Warnung: " + w);

        // Was CS2 verwerfen wird - dieselbe Probe wie `LogSurfaceHealth` im Mod.
        void Verworfen(string art, float2[][] ringe)
        {
            var weg = (ringe ?? Array.Empty<float2[]>())
                .Where(r => r != null && r.Length >= 3 && Cs2Triangulierung.Dreiecke(r) == 0)
                .ToArray();
            Console.WriteLine($"CS2 verwirft {weg.Length} von {ringe?.Length ?? 0} {art}-Ringen");
            foreach (var r in weg)
                Console.WriteLine($"  {art}: {r.Length} Ecken | "
                    + string.Join(" ", r.Select(p => $"{p.x:R}/{p.y:R}")));
        }
        Verworfen("Gras", layout.GrassSurface);
        Verworfen("Belag", layout.AsphaltSurface);

        // Netzlinien, die weit ausserhalb des Umrisses verlaufen (Issue #10:
        // Randstrasse im schmalen Arm zu einem Punkt zusammengeklappt, von dort
        // gerade Kanten quer ueber den Innenhof).
        var draussen = Wegeausserhalb(layout, site);
        Console.WriteLine($"Netzlinien mehr als 10 m ausserhalb des Umrisses: {draussen.Count}");
        foreach (var d in draussen.Take(10)) Console.WriteLine("  " + d);

        var (aufkleber, falsch, gemischt) = Aufkleberbefund(layout, settings);
        Console.WriteLine($"Aufkleber: {aufkleber}, zur Seite ohne Fahrbahn {falsch}, "
            + $"gegen den Reihennachbarn gedreht {gemischt}");

        // Fusswege: keine Reste kuerzer als eine Fusswegbreite (2 m), und
        // jedes Fusswegende beruehrt einen anderen Weg oder den Umriss -
        // ein freies Ende mitten im Parkplatz ist ein abgetrennter Weg.
        var fuss = layout.NetLine.Where(n => n.Art == Zufahrtsart.Fussweg).ToArray();
        var reste = fuss.Count(n => math.distance(n.A, n.B) < 2f);
        bool Beruehrt(float2 p) => layout.NetLine.Count(n =>
            math.distance(n.A, p) < 0.01f || math.distance(n.B, p) < 0.01f) > 1
            || Enumerable.Range(0, site.Length).Any(k => AbstandZuStrecke(p,
                site[k], site[(k + 1) % site.Length]) < 0.05f);
        var frei = fuss.Sum(n => (Beruehrt(n.A) ? 0 : 1) + (Beruehrt(n.B) ? 0 : 1));
        foreach (var n in fuss)
            foreach (var p in new[] { n.A, n.B })
                if (!Beruehrt(p))
                {
                    var naechster = layout.NetLine.Where(m => !m.Equals(n))
                        .Min(m => AbstandZuStrecke(p, m.A, m.B));
                    Console.WriteLine($"  freies Fussweg-Ende ({p.x:F2}/{p.y:F2}), naechster Weg {naechster:F2} m");
                }
        Console.WriteLine($"Fusswege: {fuss.Length}, Reste unter 2 m: {reste}, freie Enden: {frei}");
        // Freie Enden werden gezeigt, aber (noch) nicht gewertet: die
        // Endwege ohne Randstrasse enden seit jeher 2,5-4 m neben dem Netz
        // (Befund 2026-09-25) - das gehoert in die Neukonzeption.
        // Weggelassene Flaeche: mehr als 1 m2 ist ein sichtbares Loch.
        // Bericht CCBP (2026-09-25): 582,25 m2 Gras fehlten, weil ein
        // Fusswegrest samt Belag entfernt wurde.
        var weggelassen = layout.Warnings.Sum(w =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(w,
                @"surface ring\(s\) with ([0-9]+[,.][0-9]+) m2 left out");
            return m.Success ? double.Parse(m.Groups[1].Value.Replace(',', '.'),
                System.Globalization.CultureInfo.InvariantCulture) : 0.0;
        });
        Console.WriteLine($"Weggelassene Flaeche: {weggelassen:F2} m2");
        var umgeklappt = UmgeklappteDreiecke(layout.AsphaltSurface, "Asphalt")
            + UmgeklappteDreiecke(layout.GrassSurface, "Gras");
        Console.WriteLine($"Umgeklappte CS2-Dreiecke: {umgeklappt}");
        return befund.Vollstaendig && reste == 0 && weggelassen <= 1.0
            && umgeklappt == 0 ? 0 : 1;
    }

    /*
     * WAS CS2 WIRKLICH ZEICHNET, NICHT NUR OB ES ANNIMMT.
     *
     * CS2 schiebt jeden Knoten 0,1 m nach innen, schneidet dort die Ohren und
     * zeichnet die Dreiecke dann auf den ECHTEN Knoten. Ein Zipfel unter
     * wenigen Grad springt dabei meterweit; das Dreieck liegt versetzt noch
     * richtig herum, auf den echten Knoten aber umgeklappt und ausserhalb
     * der Flaeche. Bauzettel AJ54 (2026-09-27): 5,2-Grad-Zipfel an der
     * Innenecke der Randstrasse, 14,6 m2 Gras auf der Fahrgasse. Die Flaeche
     * galt dabei als angenommen - `--flaechenannahme` sieht das nicht.
     */
    private static int UmgeklappteDreiecke(float2[][] ringe, string art)
    {
        var anzahl = 0;
        for (var r = 0; r < ringe.Length; r++)
        {
            var ring = ringe[r];
            var flaeche = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var p = ring[i]; var q = ring[(i + 1) % ring.Length];
                flaeche += (double)p.x * q.y - (double)q.x * p.y;
            }
            var ccw = flaeche > 0;
            var netz = Cs2Triangulierung.Netz(ring, ccw);
            if (netz == null) continue;
            var versetzt = Cs2Triangulierung.VersetzterRing(ring, ccw);
            double Kreuz(float2[] k, int a, int b, int c)
                => ((double)k[b].x - k[a].x) * ((double)k[c].y - k[a].y)
                 - ((double)k[b].y - k[a].y) * ((double)k[c].x - k[a].x);
            for (var t = 0; t < netz.Length; t += 3)
            {
                var echt = Kreuz(ring, netz[t], netz[t + 1], netz[t + 2]);
                var cs2 = Kreuz(versetzt, netz[t], netz[t + 1], netz[t + 2]);
                // Gestreckte Dreiecke (0,00 m2) klappen nur rechnerisch um -
                // gezeichnet wird dort nichts. Gezaehlt wird ab 0,01 m2.
                if (echt * cs2 >= 0 || Math.Abs(echt) / 2 < 0.01) continue;
                anzahl++;
                Console.WriteLine($"  {art}ring {r}: Dreieck {netz[t]}/{netz[t + 1]}/{netz[t + 2]} "
                    + $"umgeklappt, {Math.Abs(echt) / 2:F2} m2 ausserhalb");
            }
        }
        return anzahl;
    }

    /**
     * AUFKLEBER-BEFUND: zeigt jeder Aufkleber zu einer Seite mit direkt
     * angrenzender Fahrbahn, und zeigen Nachbarn derselben Reihe gleich?
     * Anlass: Baubericht 2026-10-08 - eine Reihe zwischen zwei Gassen, beide
     * 6,45 m entfernt, und die Rundung entschied je Bucht neu.
     */
    internal static (int Aufkleber, int Falsch, int Gemischt) Aufkleberbefund(
        ParkingLayout layout, LayoutSettings settings)
    {
        var plan = ParkingBayDecals.Plan(layout, settings);
        var strassen = layout.AisleQuad.Concat(layout.PerimeterQuad).Concat(layout.CrossQuad)
            .Concat(layout.EntranceQuad).Select(q => q.ToArray()).ToArray();
        var falsch = 0;
        var gemischt = 0;
        foreach (var p in plan.Placements)
        {
            var tiefe = p.Facing;
            bool Strasse(double s) => strassen.Any(q => PointInRing(new float2(
                (float)(p.Center.x + tiefe.x * s * (settings.Sl / 2 + 0.3)),
                (float)(p.Center.y + tiefe.y * s * (settings.Sl / 2 + 0.3))), q));
            if (!Strasse(1) && Strasse(-1))
            {
                falsch++;
                if (Environment.GetEnvironmentVariable("PLT_AUFKLEBER") == "1")
                    Console.WriteLine($"    AUFKLEBER Bucht {p.Bay} ({p.Center.x:F1}/{p.Center.y:F1}) zeigt ({p.Facing.x:F2}/{p.Facing.y:F2}) - dort keine Fahrbahn, dahinter schon");
            }
            var nachbar = plan.Placements.Where(o => o.Bay != p.Bay
                    && Math.Abs(Math.Abs(math.dot(o.Facing, p.Facing)) - 1) < 1e-3
                    && Math.Abs(math.dot(o.Center - p.Center, p.Facing)) < 0.1
                    && math.distance(o.Center, p.Center) < settings.Sw * 1.6)
                .FirstOrDefault();
            if (nachbar != null && math.dot(nachbar.Facing, p.Facing) < 0
                && nachbar.Kind == p.Kind)
            {
                gemischt++;
                if (Environment.GetEnvironmentVariable("PLT_AUFKLEBER") == "1")
                    Console.WriteLine($"    GEMISCHT Bucht {p.Bay} ({p.Center.x:F2}/{p.Center.y:F2}) zeigt ({p.Facing.x:F2}/{p.Facing.y:F2}), "
                        + $"Nachbar {nachbar.Bay} ({nachbar.Center.x:F2}/{nachbar.Center.y:F2}) zeigt ({nachbar.Facing.x:F2}/{nachbar.Facing.y:F2})");
            }
        }
        return (plan.Placements.Length, falsch, gemischt);
    }
    /** Netzlinien, die an Anfang, Ende oder dazwischen mehr als 10 m ausserhalb des Umrisses liegen. */
    internal static List<string> Wegeausserhalb(ParkingLayout layout, float2[] site)
    {
        float Draussen(float2 p)
        {
            var innen = false;
            var naechster = float.MaxValue;
            for (int i = 0, j = site.Length - 1; i < site.Length; j = i++)
            {
                var a = site[i]; var b = site[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    innen = !innen;
                var ab = b - a;
                var t = math.clamp(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-9f), 0f, 1f);
                naechster = math.min(naechster, math.distance(p, a + ab * t));
            }
            return innen ? 0 : naechster;
        }
        var aus = new List<string>();
        foreach (var n in layout.NetLine)
        {
            var groesster = 0f;
            foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                groesster = math.max(groesster, Draussen(math.lerp(n.A, n.B, t)));
            if (groesster > 10f)
                aus.Add($"{n.Kind} ({n.A.x:F1}/{n.A.y:F1}) -> ({n.B.x:F1}/{n.B.y:F1}), bis {groesster:F1} m draussen");
        }
        return aus;
    }
}
