using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * DIE GEMELDETE FORM ALS BILD.
     *
     * Die Kennzahlen sagen bei `PLT-224A4F51` nichts: keine Ueberlappung,
     * nichts auf der Fahrbahn, nichts ausserhalb, 0,0 % ungedeckt. Der Nutzer
     * sieht trotzdem, dass "einiges falsch laeuft beim Verlegen der
     * Parkbuchten und der Strassen und der Flaechen". Wenn die Zahlen sauber
     * sind und das Bild nicht, misst der Pruefstand das Falsche - dann muss
     * man hinsehen.
     *
     * Ausgegeben wird je Fall ein SVG: Umriss, Gras, Asphalt, Buchten,
     * Fahrwege. Dieselben Ebenen und Farben wie `--fehlerbilder`.
     */
    private static void RunNutzerbild(string ziel)
    {
        // PLT-66E7436D, gebaut 2026-09-01 11:03. Winkel aus dem Live-Log.
        var site = new[]
        {
            new float2(-1031.5846f, 496.5344f),
            new float2(-1116.6327f, 513.4900f),
            new float2(-1181.6761f, 559.7708f),
            new float2(-1170.2239f, 644.4089f),
            new float2(-1116.7362f, 624.0984f),
            new float2(-1029.1805f, 610.1551f),
        };

        LayoutSettings Grund()
        {
            var s = LayoutSettings.Cs2;
            s.Zellen = true;
            s.AngleMode = "edge";
            s.Auto = false;
            s.AutomaticEntrances = false;
            s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
            return s;
        }

        var faelle = new List<(string Name, LayoutSettings Settings,
            Teilflaechenschnitt Schnitt)>();

        faelle.Add(("ohne Ausrichtung", Grund(), null));
        var einer = Grund();
        einer.Ausrichtwinkel = 168.7;
        faelle.Add(("nur 168,7 Grad", einer, null));

        var doppel = site.Select(p => new double2(p.x, p.y)).ToArray();
        for (var i = 0; i < site.Length; i++)
            for (var j = i + 2; j < site.Length; j++)
            {
                if (i == 0 && j == site.Length - 1) continue;
                if (!ParkingGeometry.SchnittLiegtInnen(doppel, i, j)) continue;
                var schnitt = new Teilflaechenschnitt { A = site[i], B = site[j] };
                var teile = ParkingGeometry.TeilflaechenAusSchnitten(
                    doppel, new[] { schnitt });
                if (teile.Length != 2) continue;
                float2 Anker(double2[] teil)
                {
                    var summe = double2.zero;
                    foreach (var p in teil) summe += p;
                    var m = summe / teil.Length;
                    return new float2((float)m.x, (float)m.y);
                }
                var s = Grund();
                s.Ausrichtwinkel = 168.7;
                s.Teilflaechenschnitte = new[] { schnitt };
                s.TeilflaechenAusrichtungen = new[]
                {
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[0]), Winkel = 144.6 },
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[1]), Winkel = 168.7 },
                };
                faelle.Add(($"Schnitt {i}-{j} · 144,6°/168,7°", s, schnitt));
            }

        var text = new StringBuilder();
        foreach (var fall in faelle)
        {
            ParkingLayout layout;
            try { layout = ParkingGeometry.Build(site, fall.Settings); }
            catch (Exception fehler)
            {
                text.Append("<!-- " + fall.Name + " ABSTURZ: "
                    + fehler.Message + " -->\n");
                continue;
            }
            text.Append("<!--FALL " + fall.Name + "|" + layout.Stalls
                + " Buchten, " + layout.GrassSurface.Length + " Gras, "
                + layout.AsphaltSurface.Length + " Asphalt, "
                + UnservedBays(layout) + " unerreichbar-->\n");
            text.Append(Zeichne(site, layout, fall.Schnitt));
            text.Append("\n");
        }
        File.WriteAllText(ziel, text.ToString());
        Console.WriteLine("Geschrieben: " + ziel + "  (" + faelle.Count + " Faelle)");
    }

    private static string Zeichne(
        float2[] site, ParkingLayout layout, Teilflaechenschnitt schnitt)
    {
        var min = new float2(float.MaxValue, float.MaxValue);
        var max = new float2(float.MinValue, float.MinValue);
        foreach (var p in site) { min = math.min(min, p); max = math.max(max, p); }
        min -= 6f; max += 6f;
        var breite = max.x - min.x;
        var hoehe = max.y - min.y;

        string P(IEnumerable<float2> ring) => string.Join(" ", ring.Select(p =>
            $"{(p.x - min.x).ToString("F2", Kultur)},"
            + $"{(max.y - p.y).ToString("F2", Kultur)}"));

        var svg = new StringBuilder();
        svg.Append("<svg viewBox=\"0 0 " + breite.ToString("F2", Kultur) + " "
            + hoehe.ToString("F2", Kultur)
            + "\" preserveAspectRatio=\"xMidYMid meet\">");
        svg.Append($"<polygon points=\"{P(site)}\" fill=\"#0f1418\" "
            + "stroke=\"#46545f\" stroke-width=\"0.6\"/>");
        foreach (var ring in layout.AsphaltSurface ?? Array.Empty<float2[]>())
            if (ring != null && ring.Length >= 3)
                svg.Append($"<polygon points=\"{P(ring)}\" fill=\"#39424c\" "
                    + "stroke=\"#4a5661\" stroke-width=\"0.15\"/>");
        foreach (var ring in layout.GrassSurface ?? Array.Empty<float2[]>())
            if (ring != null && ring.Length >= 3)
                svg.Append($"<polygon points=\"{P(ring)}\" fill=\"#2f4a33\" "
                    + "stroke=\"#3c5c41\" stroke-width=\"0.15\"/>");
        foreach (var bucht in layout.Bay ?? Array.Empty<float2[]>())
            if (bucht != null && bucht.Length >= 3)
                svg.Append($"<polygon points=\"{P(bucht)}\" fill=\"none\" "
                    + "stroke=\"#7d8b98\" stroke-width=\"0.12\"/>");
        foreach (var linie in new[]
                 {
                     layout.PerimeterLine, layout.AisleLine,
                     layout.CrossRouteLine, layout.EntranceLine,
                 })
        foreach (var weg in linie ?? Array.Empty<float2[]>())
            if (weg != null && weg.Length >= 2)
                svg.Append($"<polyline points=\"{P(weg)}\" fill=\"none\" "
                    + "stroke=\"#9fb4c8\" stroke-width=\"0.6\" "
                    + "stroke-opacity=\"0.9\"/>");
        if (schnitt != null)
            svg.Append("<polyline points=\""
                + P(new[] { schnitt.A, schnitt.B })
                + "\" fill=\"none\" stroke=\"#ffb454\" stroke-width=\"0.8\" "
                + "stroke-dasharray=\"3 2\"/>");
        svg.Append("</svg>");
        return svg.ToString();
    }
}
