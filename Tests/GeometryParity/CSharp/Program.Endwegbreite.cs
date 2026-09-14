using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * HOERT EIN ENDFUSSWEG MITTEN IN EINER FAHRGASSE AUF?
 *
 * Befund des Nutzers am 2026-09-09: *"Dort wo sie sich teilen gehen sie nur
 * bis zur Haelfte der Strasse."*
 *
 * Ein Endfussweg laeuft quer ueber die Gassen einer Endgruppe. An ihrem
 * aeussersten Ende muss er ueber die letzte Gassenachse hinaus bis an deren
 * Aussenkante reichen - sonst endet er auf der Fahrbahnmitte. Zwischen zwei
 * Gassen derselben Gruppe stimmt der Anschluss ohnehin, dort stoesst der
 * naechste Streifen an.
 *
 * Form, Regler und alle fuenf Zugaenge stammen aus dem Bauzettel
 * 2026-09-09 12:19.
 */
internal static partial class Program
{
    private static int RunEndwegbreite()
    {
        var form = new[]
        {
            new float2(-1037.0201416015625f, 118.68980407714844f),
            new float2(-1121.4210205078125f, 121.58500671386719f),
            new float2(-1124.6280517578125f, 28.173002243041992f),
            new float2(-1191.7340087890625f, 30.476001739501953f),
            new float2(-1195.4329833984375f, -77.29215240478516f),
            new float2(-1043.880615234375f, -81.19245910644531f),
        };

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;
        e.Entrances = new[]
        {
            new Entrance { Edge = 0, Along = 11.898293495178223, Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 0, Along = 54.49819564819336, Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 5, Along = 176.49195861816406, Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 5, Along = 99.35395812988281, Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 5, Along = 63.35395431518555, Art = Zufahrtsart.Zufahrt },
        };

        var layout = ParkingGeometry.Build(form, e);
        var fehler = 0;

        // Achse der ersten Gasse; "quer" steht senkrecht darauf.
        var g0 = layout.AisleLine[0];
        var achse = math.normalize(g0[g0.Length - 1] - g0[0]);
        var norm = new float2(-achse.y, achse.x);
        double Quer(float2 p) => p.x * norm.x + p.y * norm.y;
        double Laengs(float2 p) => p.x * achse.x + p.y * achse.y;

        var gassen = layout.AisleLine
            .Select(g => (Quer: Quer(g[0]),
                          Von: Math.Min(Laengs(g[0]), Laengs(g[g.Length - 1])),
                          Bis: Math.Max(Laengs(g[0]), Laengs(g[g.Length - 1]))))
            .OrderBy(g => g.Quer).ToArray();
        var halbe = e.Ai / 2;
        Console.WriteLine($"  {gassen.Length} Gassen, Buchten {layout.Stalls}");
        foreach (var g in gassen)
            Console.WriteLine($"    quer {g.Quer,8:F1}   laengs {g.Von,8:F1} .. {g.Bis,8:F1}");

        Console.WriteLine("  Endfusswege (quer zur Gasse):");
        foreach (var n in layout.EntranceLine ?? Array.Empty<float2[]>())
        {
            var A = n[0]; var B = n[n.Length - 1];
            if (Math.Abs(Quer(B) - Quer(A)) <= Math.Abs(Laengs(B) - Laengs(A)))
                continue;                       // laeuft laengs - eine gesetzte Zufahrt
            var von = Math.Min(Quer(A), Quer(B));
            var bis = Math.Max(Quer(A), Quer(B));

            /*
             * NUR DIE AUTOMATISCHEN ENDFUSSWEGE, NICHT DIE GESETZTEN.
             *
             * Ein gesetzter Fussweg kommt von der Grundstueckskante herein
             * und endet AN einer Fahrgasse - das ist sein Anschluss, kein
             * Mangel. Der erste Anlauf dieser Pruefung hat genau die drei
             * gesetzten Wege des Nutzerfalls angemeckert.
             *
             * Ein Endfussweg laeuft dagegen quer ueber die Gassen einer
             * Gruppe; er hat also mindestens zwei Gassenachsen ECHT zwischen
             * seinen Enden. Das ist die geometrische Unterscheidung, ohne
             * dass die Ausgabe eine Herkunft mitliefern muesste.
             */
            var dazwischen = gassen.Count(g => g.Quer > von + 0.10
                && g.Quer < bis - 0.10);
            Console.WriteLine($"    quer {von,8:F1} .. {bis,8:F1}"
                + $"   Laenge {math.distance(A, B),6:F1}"
                + $"   Gassen dazwischen {dazwischen}"
                + (dazwischen < 2 ? "   (gesetzter Zugang, nicht geprueft)" : ""));
            if (dazwischen < 2) continue;

            /*
             * BEIDE ENDEN MUESSEN AUS DER FAHRBAHN HERAUS.
             *
             * Liegt ein Ende auf einer Gassenachse und folgt dort KEINE
             * weitere Gasse derselben Endgruppe, hoert der Weg auf der
             * Fahrbahnmitte auf. Toleranz 0,10 m fuer Rundung.
             */
            foreach (var (wert, name) in new[] { (von, "Anfang"), (bis, "Ende") })
            {
                var auf = gassen.FirstOrDefault(g => Math.Abs(g.Quer - wert) < 0.10);
                if (auf.Quer == 0 && auf.Von == 0 && auf.Bis == 0) continue;
                fehler++;
                Console.WriteLine($"FEHLER: Endfussweg {name} liegt bei "
                    + $"{wert:F1} auf der Achse einer Fahrgasse - es fehlen "
                    + $"{halbe:F1} m bis zu ihrer Aussenkante");
            }
        }

        Console.WriteLine($"Endwegbreite: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
