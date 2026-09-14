using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Haengt die Zerlegung an der REIHENFOLGE der Punkte?
     *
     * `Decompose` nimmt die ERSTE einspringende Ecke in der Punktliste und
     * verlaengert deren EINGEHENDE Kante. Beides sind Eigenschaften der
     * Liste, nicht der Form. Wenn das stimmt, liefert dieselbe Form je nach
     * Startecke andere Teilflaechen - und der Nutzer bekommt Klickziele, die
     * davon abhaengen, wo er zu zeichnen begonnen hat.
     */
    private static void RunZerlegungsprobe()
    {
        void Probe(string name, double2[] form)
        {
            Console.WriteLine(name + "  (" + form.Length + " Punkte)");
            string ersteAusgabe = null;
            for (var start = 0; start < form.Length; start++)
            {
                var gedreht = Enumerable.Range(0, form.Length)
                    .Select(i => form[(start + i) % form.Length]).ToArray();
                var teile = ParkingGeometry.Teilflaechen(gedreht);
                var text = string.Join(" | ", teile.Select(teil =>
                    teil.Length + " Ecken, "
                    + Flaeche(teil).ToString("F0") + " m2"));
                ersteAusgabe = ersteAusgabe ?? text;
                Console.WriteLine($"   Start {start}: {teile.Length} Teile"
                    + (text == ersteAusgabe ? "" : "   <- ANDERS") + "  " + text);
            }
            Console.WriteLine();
        }

        PruefeHandschnitt();
        Console.WriteLine();

        // L-Form
        Probe("L-Form", new[]
        {
            new double2(0, 0), new double2(180, 0), new double2(180, 60),
            new double2(90, 60), new double2(90, 120), new double2(0, 120),
        });

        // T-Form, leicht schief - nachgebaut
        Probe("T-Form schief", new[]
        {
            new double2(0, 0), new double2(180, 4), new double2(178, 60),
            new double2(120, 62), new double2(122, 130), new double2(58, 128),
            new double2(60, 61), new double2(2, 59),
        });

        /*
         * ECHTE FORMEN DES NUTZERS, aus dem Bauprotokoll vom 2026-08-31.
         * Nachgebaute Formen beweisen wenig - diese hier hat er gezogen.
         */
        Probe("Nutzer L (6 Punkte)", new[]
        {
            new double2(-1031.6, 496.5), new double2(-1029.9, 576.5),
            new double2(-1171.3, 579.5), new double2(-1175.8, 368.8),
            new double2(-1107.1, 367.3), new double2(-1104.3, 498.1),
        });
        Probe("Nutzer L (7 Punkte)", new[]
        {
            new double2(-1031.6, 496.5), new double2(-1029.9, 576.5),
            new double2(-1102.6, 578.1), new double2(-1171.3, 579.5),
            new double2(-1175.8, 368.8), new double2(-1107.1, 367.3),
            new double2(-1104.3, 498.1),
        });
        Probe("Nutzer T (7 Punkte)", new[]
        {
            new double2(-1031.8, 488.5), new double2(-1114.5, 490.3),
            new double2(-1117.6, 345.0), new double2(-1194.2, 346.6),
            new double2(-1191.1, 491.9), new double2(-1188.4, 619.9),
            new double2(-1029.0, 616.5),
        });
    }

    /**
     * Der Handschnitt: was der Nutzer zieht, gilt - und sonst nichts.
     *
     * Geprueft an seiner echten T-Form. Die Automatik liefert dort je nach
     * Startecke 2 oder 6 Teile; ein einziger gezogener Schnitt liefert
     * IMMER zwei, unabhaengig davon, wo gezeichnet wurde.
     */
    private static bool PruefeHandschnitt()
    {
        var form = new[]
        {
            new double2(-1031.8, 488.5), new double2(-1114.5, 490.3),
            new double2(-1117.6, 345.0), new double2(-1194.2, 346.6),
            new double2(-1191.1, 491.9), new double2(-1188.4, 619.9),
            new double2(-1029.0, 616.5),
        };
        var gut = true;
        string ersteAusgabe = null;
        for (var start = 0; start < form.Length; start++)
        {
            var gedreht = Enumerable.Range(0, form.Length)
                .Select(i => form[(start + i) % form.Length]).ToArray();
            // Derselbe Schnitt, in Weltkoordinaten - er dreht sich nicht mit.
            var schnitte = new[]
            {
                new Teilflaechenschnitt
                {
                    A = new float2((float)form[0].x, (float)form[0].y),
                    B = new float2((float)form[4].x, (float)form[4].y),
                },
            };
            var teile = ParkingGeometry.TeilflaechenAusSchnitten(gedreht, schnitte);
            var text = teile.Length + " Teile: " + string.Join(" | ",
                teile.Select(t => t.Length + " Ecken, "
                    + Flaeche(t).ToString("F0") + " m2"));
            ersteAusgabe = ersteAusgabe ?? text;
            if (text != ersteAusgabe) gut = false;
        }
        Console.WriteLine("Handschnitt an der Nutzer-T-Form: " + ersteAusgabe
            + (gut ? "  (gleich ab JEDER Startecke)" : "  <- HAENGT AN DER STARTECKE"));
        if (!gut)
            Console.Error.WriteLine("TEILFLAECHENFEHLER: der Handschnitt "
                + "haengt an der Punktreihenfolge.");
        return gut;
    }

    /** Hat dieser Ring nur Linksknicke (oder nur Rechtsknicke)? */
    internal static bool IstKonvex(double2[] ring)
    {
        var positiv = false;
        var negativ = false;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            var c = ring[(i + 2) % ring.Length];
            var kreuz = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            if (kreuz > 1e-9) positiv = true;
            if (kreuz < -1e-9) negativ = true;
        }
        return !(positiv && negativ);
    }

    private static double Flaeche(double2[] ring)
    {
        var summe = 0.0;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            summe += a.x * b.y - b.x * a.y;
        }
        return Math.Abs(summe) / 2;
    }
}
