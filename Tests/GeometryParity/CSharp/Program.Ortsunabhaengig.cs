using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * KOMMT DASSELBE HERAUS, EGAL WO DER PARKPLATZ LIEGT?
 *
 * Ansage des Nutzers am 2026-09-10: *"Wenn der Fehler, der gefixt wurde, noch
 * woanders auftaucht in irgendeiner Art - egal ob er zu Crash oder
 * Nichtsetzen von Flaechen fuehrt - und nochmal vorkommt, bitte fixen."*
 *
 * DER FEHLER, UM DEN ES GEHT. Am 2026-09-08 bekam der Zellenschnitt eine
 * Fehlerschranke aus der Maschinengenauigkeit. Die FLAECHE wurde dabei auf
 * einen ortsnahen Ursprung umgestellt, ihre SCHRANKE nicht - die rechnete
 * weiter in Weltkoordinaten. Bei (-1170/+120) blies das die Schranke auf
 * 1,23e-9 m2 auf, waehrend die ehrliche Flaeche 1,00e-9 m2 mass. Zwei
 * gueltige Zellen flogen raus, hinterliessen drei Loecher, daraus wurden
 * 22,6-Mikrometer-Streifen und daraus Ringe ohne Flaeche - CS2 stuerzte ab.
 *
 * Das Tueckische daran war die ORTSABHAENGIGKEIT: dieselbe Form haette am
 * Nullpunkt nie gecrasht. Genau das prueft dieser Lauf, und zwar fuer jede
 * Schranke auf einmal - auch fuer die, die noch niemand eingebaut hat.
 *
 * VERGLICHEN WIRD NICHT AUF DEN MILLIMETER. Fliesskomma rechnet weit draussen
 * nun einmal groeber; ein paar Zentimeter Unterschied in einer Ringecke sind
 * kein Fehler. Verglichen wird, was STRUKTURELL gleich sein muss: wie viele
 * Buchten, wie viele Gassen, wie viele Ringe, wie viel Flaeche - und dass
 * nirgends ein Ring ohne Flaeche entsteht.
 */
internal static partial class Program
{
    private static int RunOrtsunabhaengig()
    {
        var fehler = 0;

        /*
         * DIE ORTE. Der Nullpunkt als Bezug, dazu die Stelle des Nutzers und
         * zwei Punkte weiter draussen - eine CS2-Karte ist rund 14 km breit,
         * ihr Rand ist also ein ehrlicher Extremfall und kein konstruierter.
         */
        var orte = new (string Name, double2 Versatz)[]
        {
            ("Nullpunkt", new double2(0, 0)),
            ("Nutzerort", new double2(-1170, 120)),
            ("8 km", new double2(8000, -8000)),
            ("Kartenrand", new double2(14000, 14000)),
        };

        /*
         * DIE FORMEN. Die erste ist der Absturzfall vom 2026-09-10, auf den
         * Nullpunkt zurueckgeschoben - sie hat die Entartung ausgeloest und
         * ist damit die empfindlichste, die wir kennen. Dazu zwei einfache
         * Formen, damit ein Fund nicht nur an einer Sonderform haengt.
         */
        var absturz = new[]
        {
            new float2(-1037.020f, 118.6898f),
            new float2(-1167.300f, 123.160f),
            new float2(-1173.576f, -59.734f),
            new float2(-1043.294f, -64.20625f),
        };
        var mitte = new double2(
            absturz.Average(p => (double)p.x), absturz.Average(p => (double)p.y));

        var formen = new (string Name, float2[] Punkte, int RzKante)[]
        {
            ("Absturzform", absturz.Select(p => new float2(
                (float)(p.x - mitte.x), (float)(p.y - mitte.y))).ToArray(), 1),
            ("Rechteck", new[]
            {
                new float2(0, 0), new float2(120, 0),
                new float2(120, 80), new float2(0, 80),
            }, -1),
            ("L-Form", new[]
            {
                new float2(0, 0), new float2(120, 0), new float2(120, 50),
                new float2(50, 50), new float2(50, 120), new float2(0, 120),
            }, 0),
        };

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;

        /*
         * WAS GEZAEHLT WIRD. Alles davon muss ortsunabhaengig sein: eine
         * Bucht verschwindet nicht, weil der Parkplatz weiter oestlich liegt.
         * Die Flaeche vergleiche ich mit Spielraum, die ZAHLEN nicht.
         */
        (int Buchten, int Gassen, int Gras, int Belag, double Flaeche,
            double KleinsterRing, double KuerzesteKante) Baue(
                float2[] form, int rzKante, double2 versatz)
        {
            var punkte = form
                .Select(p => new float2((float)(p.x + versatz.x),
                    (float)(p.y + versatz.y))).ToArray();
            var s = e;
            s.Entrances = new[] { new Entrance { Edge = 0, Along = 40 } };
            s.Randzoning = rzKante < 0
                ? Array.Empty<ParkingGeometry.RandzoningLinie>()
                : new[]
                {
                    new ParkingGeometry.RandzoningLinie
                    {
                        A = punkte[rzKante],
                        B = punkte[(rzKante + 1) % punkte.Length],
                    },
                };

            var l = ParkingGeometry.Build(punkte, s);
            var ringe = (l.GrassSurface ?? Array.Empty<float2[]>())
                .Concat(l.AsphaltSurface ?? Array.Empty<float2[]>()).ToArray();
            var kleinster = double.PositiveInfinity;
            var kuerzeste = double.PositiveInfinity;
            var gesamt = 0.0;
            foreach (var r in ringe)
            {
                if (r == null || r.Length < 3) { kleinster = 0; kuerzeste = 0; continue; }
                var f = Math.Abs(Enumerable.Range(0, r.Length).Sum(i =>
                    (double)r[i].x * r[(i + 1) % r.Length].y
                    - (double)r[(i + 1) % r.Length].x * r[i].y) / 2);
                gesamt += f;
                kleinster = Math.Min(kleinster, f);
                for (var i = 0; i < r.Length; i++)
                    kuerzeste = Math.Min(kuerzeste,
                        math.distance(r[i], r[(i + 1) % r.Length]));
            }
            return (l.Stalls, l.AisleLine?.Length ?? 0,
                l.GrassSurface?.Length ?? 0, l.AsphaltSurface?.Length ?? 0,
                gesamt, kleinster, kuerzeste);
        }

        foreach (var form in formen)
        {
            Console.WriteLine($"  {form.Name}:");
            var bezug = Baue(form.Punkte, form.RzKante, orte[0].Versatz);
            foreach (var ort in orte)
            {
                var jetzt = ort.Name == orte[0].Name
                    ? bezug : Baue(form.Punkte, form.RzKante, ort.Versatz);
                var flaechenDiff = Math.Abs(jetzt.Flaeche - bezug.Flaeche);
                Console.WriteLine($"    {ort.Name,-11} "
                    + $"{jetzt.Buchten,4} Buchten  {jetzt.Gassen,2} Gassen  "
                    + $"Gras {jetzt.Gras,3}  Belag {jetzt.Belag,3}  "
                    + $"Fläche {jetzt.Flaeche,9:F1} m²  "
                    + $"kleinster Ring {jetzt.KleinsterRing,8:F3} m²  "
                    + $"kürzeste Kante {jetzt.KuerzesteKante,6:F3} m");

                /*
                 * EIN RING OHNE FLAECHE IST IMMER FALSCH - unabhaengig vom
                 * Vergleich mit dem Nullpunkt. Er sprengt CS2.
                 */
                if (jetzt.KleinsterRing < 0.01 || jetzt.KuerzesteKante < 0.001)
                {
                    fehler++;
                    Console.WriteLine($"FEHLER: {form.Name} am {ort.Name}: "
                        + "ein Ring ohne Fläche oder mit Nullkante");
                }

                if (ort.Name == orte[0].Name) continue;

                if (jetzt.Buchten != bezug.Buchten || jetzt.Gassen != bezug.Gassen)
                {
                    fehler++;
                    Console.WriteLine($"FEHLER: {form.Name} am {ort.Name}: "
                        + $"{jetzt.Buchten} Buchten / {jetzt.Gassen} Gassen "
                        + $"statt {bezug.Buchten} / {bezug.Gassen} am Nullpunkt "
                        + "- das Ergebnis hängt am Ort");
                }
                if (jetzt.Gras != bezug.Gras || jetzt.Belag != bezug.Belag)
                {
                    fehler++;
                    Console.WriteLine($"FEHLER: {form.Name} am {ort.Name}: "
                        + $"{jetzt.Gras} Gras- und {jetzt.Belag} Belagringe "
                        + $"statt {bezug.Gras} / {bezug.Belag} - "
                        + "eine Fläche entsteht nur an einem der beiden Orte");
                }
                // Ein Promille der Gesamtflaeche als Spielraum fuer float.
                if (flaechenDiff > Math.Max(1.0, bezug.Flaeche * 0.001))
                {
                    fehler++;
                    Console.WriteLine($"FEHLER: {form.Name} am {ort.Name}: "
                        + $"{flaechenDiff:F1} m² Flächenunterschied zum "
                        + "Nullpunkt");
                }
            }
        }

        Console.WriteLine($"Ortsunabhaengig: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
