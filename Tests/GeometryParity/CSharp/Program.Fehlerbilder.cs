using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * DIE UEBRIGGEBLIEBENEN FEHLER ZUM ANSCHAUEN.
     *
     * Nach der besseren Pfadwahl bleiben ueber alle Formen des Bauprotokolls
     * 5 Stachel und 23 Keile. Zaehlen allein hilft nicht weiter - der Nutzer
     * will sehen, WO sie liegen und wie die Strassen dort verlaufen.
     *
     * Der Lauf schreibt deshalb eine HTML-Datei mit einem Bild je betroffener
     * Form: Gras, Belag, Wege und Buchten, und die verworfenen Ringe rot
     * hervorgehoben, mit einem Kreuz an der engsten Stelle.
     *
     * Absichtlich SVG in einer HTML-Datei und kein Bildformat: es laesst sich
     * ohne Werkzeug im Browser oeffnen, beliebig weit hineinzoomen, und die
     * Koordinaten bleiben Zahlen statt Pixel.
     */
    private static int RunFehlerbilder(string ziel)
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Zellen = true;
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var formen = new List<(string Name, float2[] Site)>();
        foreach (var fall in Cases) formen.Add((fall.Name, fall.Site));
        formen.AddRange(FormenAusBauprotokoll());

        var seiten = new StringBuilder();
        var betroffen = 0;
        var stachelGesamt = 0;
        var keilGesamt = 0;

        Console.WriteLine("FEHLERBILDER - Formen, an denen CS2 noch Flaechen verwirft");
        Console.WriteLine();

        foreach (var form in formen)
        {
            ParkingLayout layout;
            try { layout = ParkingGeometry.Build(form.Site, einstellungen); }
            catch { continue; }

            var schlecht = new List<(float2[] Ring, Ringmass Mass, string Art,
                                     string Klasse)>();
            foreach (var paar in new[]
                     {
                         (Ringe: layout.AsphaltSurface, Art: "Belag"),
                         (Ringe: layout.GrassSurface, Art: "Gras"),
                     })
            {
                if (paar.Ringe == null) continue;
                foreach (var ring in paar.Ringe)
                {
                    if (ring == null || ring.Length < 3) continue;
                    if (Cs2Triangulierung.Dreiecke(ring) != 0) continue;
                    var mass = Vermessen(ring);
                    if (mass == null) continue;
                    var ohne = OhneHaarkappen(ring);
                    var m2 = Vermessen(ohne) ?? mass;
                    var klasse = Cs2Triangulierung.Dreiecke(ohne) != 0 ? "Haarkappe"
                        : m2.MinHals < 0.01 ? "Stachel" : "Keil";
                    if (klasse == "Stachel") stachelGesamt++;
                    else if (klasse == "Keil") keilGesamt++;
                    schlecht.Add((ring, mass, paar.Art, klasse));
                }
            }
            if (schlecht.Count == 0) continue;

            betroffen++;
            var flaeche = Math.Abs(FlaechenmassRing(form.Site));
            Console.WriteLine($"  {form.Name,-16} {flaeche,9:F0} m2 | "
                + $"{layout.Stalls,5} Buchten | {schlecht.Count} verworfen: "
                + string.Join(", ", schlecht
                    .GroupBy(f => f.Klasse)
                    .Select(g => $"{g.Count()}x {g.Key}")));
            foreach (var fall in schlecht)
                Console.WriteLine($"      {fall.Klasse,-10} {fall.Art,-6} "
                    + $"{fall.Mass.Breite,7:F2} x {fall.Mass.Laenge,7:F2} m | "
                    + $"{fall.Mass.Flaeche,9:F1} m2 | Hals {fall.Mass.MinHals,8:F5} | "
                    + $"bei {fall.Mass.DuennsteStelle.x:F1} / "
                    + $"{fall.Mass.DuennsteStelle.y:F1}");

            seiten.Append(ZeichneForm(form.Name, form.Site, layout, schlecht,
                flaeche));
        }

        var html = "<!doctype html><meta charset=\"utf-8\">"
            + "<title>PLT - verworfene Flaechen</title><style>"
            + "body{background:#14181d;color:#dfe6ee;font:14px/1.5 sans-serif;"
            + "margin:0;padding:24px}"
            + "h1{font-size:20px;font-weight:600;margin:0 0 4px}"
            + "h2{font-size:16px;font-weight:600;margin:32px 0 2px}"
            + ".hinweis{color:#93a4b6;margin:0 0 20px}"
            + ".karte{background:#1c2229;border:1px solid #2b343d;border-radius:8px;"
            + "padding:10px;margin:8px 0 24px}"
            + "svg{width:100%;height:auto;display:block}"
            + "table{border-collapse:collapse;font-size:13px;margin:6px 0 0}"
            + "td{padding:2px 12px 2px 0;color:#b9c6d4}"
            + ".stachel{color:#ff6b6b}.keil{color:#ffb454}.kappe{color:#c58bff}"
            + "</style>"
            + "<h1>Was CS2 noch verwirft</h1>"
            + $"<p class=\"hinweis\">{betroffen} Formen betroffen, "
            + $"{stachelGesamt} Stachel und {keilGesamt} Keile. "
            + "Grau = Belag, gruen = Gras, hell = Wege. "
            + "Rot umrandet und mit Kreuz: die Flaeche, die CS2 nicht baut. "
            + "Das Kreuz sitzt an der engsten Stelle.</p>"
            + seiten;
        // Relativ zum Laufverzeichnis landet die Datei sonst irgendwo unter
        // bin/Release - und schlaegt fehl, wenn der Ordner nicht existiert.
        var voll = System.IO.Path.GetFullPath(ziel);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(voll));
        System.IO.File.WriteAllText(voll, html, new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine($"  {betroffen} Formen betroffen, {stachelGesamt} Stachel, "
            + $"{keilGesamt} Keile");
        Console.WriteLine($"  Bilder geschrieben nach {voll}");
        return 0;
    }

    private static string ZeichneForm(
        string name, float2[] site, ParkingLayout layout,
        List<(float2[] Ring, Ringmass Mass, string Art, string Klasse)> schlecht,
        double flaeche)
    {
        var min = new float2(float.MaxValue, float.MaxValue);
        var max = new float2(float.MinValue, float.MinValue);
        foreach (var p in site) { min = math.min(min, p); max = math.max(max, p); }
        var rand = 8f;
        min -= rand; max += rand;
        var breite = max.x - min.x;
        var hoehe = max.y - min.y;

        // Das Areal wird gespiegelt gezeichnet: in CS2 waechst z nach "oben",
        // in SVG y nach unten. Ohne das steht jedes Bild auf dem Kopf.
        string Punkte(float2[] ring) => string.Join(" ", ring.Select(p =>
            $"{(p.x - min.x).ToString("F2", Kultur)},"
            + $"{(max.y - p.y).ToString("F2", Kultur)}"));

        var svg = new StringBuilder();
        svg.Append($"<h2>{name} &middot; {flaeche:F0} m<sup>2</sup> &middot; "
            + $"{layout.Stalls} Buchten &middot; {schlecht.Count} verworfen</h2>");
        svg.Append("<div class=\"karte\"><svg viewBox=\"0 0 "
            + breite.ToString("F2", Kultur) + " " + hoehe.ToString("F2", Kultur)
            + "\" preserveAspectRatio=\"xMidYMid meet\">");

        svg.Append($"<polygon points=\"{Punkte(site)}\" fill=\"#10151a\" "
            + "stroke=\"#3b4854\" stroke-width=\"0.6\"/>");
        foreach (var ring in layout.GrassSurface ?? Array.Empty<float2[]>())
            if (ring != null && ring.Length >= 3)
                svg.Append($"<polygon points=\"{Punkte(ring)}\" fill=\"#2f4a33\" "
                    + "stroke=\"#3c5c41\" stroke-width=\"0.15\"/>");
        foreach (var ring in layout.AsphaltSurface ?? Array.Empty<float2[]>())
            if (ring != null && ring.Length >= 3)
                svg.Append($"<polygon points=\"{Punkte(ring)}\" fill=\"#39424c\" "
                    + "stroke=\"#4a5661\" stroke-width=\"0.15\"/>");
        foreach (var bucht in layout.Bay ?? Array.Empty<float2[]>())
            if (bucht != null && bucht.Length >= 3)
                svg.Append($"<polygon points=\"{Punkte(bucht)}\" fill=\"none\" "
                    + "stroke=\"#5d6b78\" stroke-width=\"0.1\"/>");

        // Die Wege zuletzt, damit sie ueber den Flaechen liegen - der Nutzer
        // will die erzeugte Strasse sehen, nicht suchen.
        foreach (var linie in new[]
                 {
                     layout.PerimeterLine, layout.AisleLine,
                     layout.CrossRouteLine, layout.EntranceLine,
                 })
        foreach (var weg in linie ?? Array.Empty<float2[]>())
            if (weg != null && weg.Length >= 2)
                svg.Append($"<polyline points=\"{Punkte(weg)}\" fill=\"none\" "
                    + "stroke=\"#9fb4c8\" stroke-width=\"0.5\" "
                    + "stroke-opacity=\"0.85\"/>");

        foreach (var fall in schlecht)
        {
            var farbe = fall.Klasse == "Stachel" ? "#ff6b6b"
                : fall.Klasse == "Keil" ? "#ffb454" : "#c58bff";
            svg.Append($"<polygon points=\"{Punkte(fall.Ring)}\" fill=\"{farbe}\" "
                + $"fill-opacity=\"0.35\" stroke=\"{farbe}\" stroke-width=\"0.7\"/>");
            var s = fall.Mass.DuennsteStelle;
            var x = (s.x - min.x).ToString("F2", Kultur);
            var y = (max.y - s.y).ToString("F2", Kultur);
            svg.Append($"<circle cx=\"{x}\" cy=\"{y}\" r=\"2.2\" fill=\"none\" "
                + $"stroke=\"{farbe}\" stroke-width=\"0.5\"/>");
            svg.Append($"<path d=\"M{x},{y} m-3,0 h6 m-3,-3 v6\" "
                + $"stroke=\"{farbe}\" stroke-width=\"0.4\"/>");
        }
        svg.Append("</svg></div>");

        svg.Append("<table>");
        foreach (var fall in schlecht)
            svg.Append($"<tr><td class=\"{fall.Klasse.ToLowerInvariant()}\">"
                + $"{fall.Klasse}</td><td>{fall.Art}</td>"
                + $"<td>{fall.Mass.Breite:F2} x {fall.Mass.Laenge:F2} m</td>"
                + $"<td>{fall.Mass.Flaeche:F1} m<sup>2</sup></td>"
                + $"<td>Hals {fall.Mass.MinHals:F5} m</td>"
                + $"<td>Winkel {fall.Mass.MinWinkel:F1}&deg;</td>"
                + $"<td>{fall.Mass.DuennsteStelle.x:F1} / "
                + $"{fall.Mass.DuennsteStelle.y:F1}</td></tr>");
        svg.Append("</table>");
        return svg.ToString();
    }
}
