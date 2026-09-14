using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * BLEIBT BEI DER L-FORM EIN ARM LEER?
 *
 * Befund des Nutzers am 2026-09-09: *"Besonders aufgefallen bei L-Form
 * (neuster Bauzettel), dass einfach eine Teilform vom Parkplatz frei bleibt
 * ohne Strassen."*
 *
 * Form und Einstellungen stammen aus dem Bauzettel 2026-09-09 00:14.
 * Gemessen wird die Belagsflaeche je Arm - Buchtenzahl allein wuerde einen
 * leeren Arm nicht auffallen lassen, solange der andere gut gefuellt ist.
 */
internal static partial class Program
{
    private static readonly float2[] LformNutzer =
    {
        new float2(-1037.020263671875f, 118.6897964477539f),
        new float2(-1094.782470703125f, 120.67132568359375f),
        new float2(-1097.97900390625f, 27.53803253173828f),
        new float2(-1139.883544921875f, 28.976308822631836f),
        new float2(-1142.1434326171875f, -36.866477966308594f),
        new float2(-1042.473388671875f, -40.287418365478516f),
    };

    /** Der obere Arm der L: alles ueber Z = 30. */
    private const double LformArmgrenze = 30.0;

    private static int RunLformtoggle()
    {
        var fehler = 0;
        if (Environment.GetEnvironmentVariable("PLT_LIVE") == "1")
            ParkingGeometry.LiveSchreiber = z => Console.WriteLine("    LIVE " + z);
        foreach (var randstrassen in new[] { true, false })
        {
            var e = LayoutSettings.Cs2;
            e.Randstrassen = randstrassen;
            e.AngleMode = "edge";
            e.Auto = false;
            e.Zellen = true;
            e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
            e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
            e.Entrances = new[]
            {
                new Entrance { Edge = 5, Along = 40.38279342651367,
                    Art = Zufahrtsart.Zufahrt },
            };

            var layout = ParkingGeometry.Build(LformNutzer, e);

            double Flaeche(float2[] ring)
            {
                var a = 0.0;
                for (var i = 0; i < ring.Length; i++)
                {
                    var b = ring[(i + 1) % ring.Length];
                    a += ring[i].x * b.y - b.x * ring[i].y;
                }
                return Math.Abs(a) / 2;
            }
            bool ImArm(float2[] ring) => ring.Average(p => p.y) > LformArmgrenze;

            var belag = layout.AsphaltSurface ?? Array.Empty<float2[]>();
            var oben = belag.Where(ImArm).Sum(Flaeche);
            var unten = belag.Where(r => !ImArm(r)).Sum(Flaeche);
            var buchtenOben = (layout.Bay ?? Array.Empty<float2[]>()).Count(ImArm);
            var gassenOben = (layout.AisleLine ?? Array.Empty<float2[]>())
                .Count(l => l.Average(p => p.y) > LformArmgrenze);

            Console.WriteLine($"  Randstrassen {(randstrassen ? "an " : "aus")}"
                + $"  Buchten {layout.Stalls,4} (oberer Arm {buchtenOben,4})"
                + $"  Gassen {layout.Aisles,2} (oben {gassenOben,2})"
                + $"  Teile {layout.Parts}"
                + $"  Belag oben {oben,7:F0} m2 / unten {unten,7:F0} m2");

            /*
             * KEIN WEG DARF QUER UEBER DEN PLATZ LAUFEN.
             *
             * Befund des Nutzers am 2026-09-09: *"Dieser fuehrt quer entlang,
             * also er verbindet die Fahrtgassen obwohl die weit auseinander
             * liegen bzw in einem anderen Teilgebiet sind."* Gemessen an
             * seiner L-Form ein Fussweg von (-1076,3/118,0) nach
             * (-1100,8/25,6): 95,6 m lang, 14,8 Grad schraeg.
             *
             * Jeder Weg laeuft entweder laengs der Fahrgassen oder quer dazu.
             * Schiefer als das Grundstueck selbst darf er nicht sein - der
             * Reihenwinkel ist hier 88 Grad, also 2 Grad Schraeglauf, und ein
             * Grad Zugabe fuer Rundungen. Die Schranke ist damit aus dem
             * Layout abgeleitet, nicht gegriffen: die echten Wege liegen bei
             * 1,9 Grad, der falsche bei 14,8.
             */
            var achse = layout.AisleLine != null && layout.AisleLine.Length > 0
                ? math.normalize(layout.AisleLine[0][layout.AisleLine[0].Length - 1]
                    - layout.AisleLine[0][0])
                : new float2(1, 0);
            var schraeglauf = Math.Abs(90.0 - Math.Abs(layout.Angle)) + 1.0;
            foreach (var n in layout.EntranceLine ?? Array.Empty<float2[]>())
            {
                var d = n[n.Length - 1] - n[0];
                if (math.length(d) < 1e-3) continue;
                d = math.normalize(d);
                var grad = Math.Atan2(Math.Abs(d.x * achse.y - d.y * achse.x),
                    Math.Abs(d.x * achse.x + d.y * achse.y)) * 180 / Math.PI;
                var schiefe = Math.Min(grad, 90 - grad);
                Console.WriteLine($"      Zufahrtslinie ({n[0].x:F1}/{n[0].y:F1})"
                    + $" .. ({n[n.Length - 1].x:F1}/{n[n.Length - 1].y:F1})"
                    + $"  Laenge {math.distance(n[0], n[n.Length - 1]),5:F1}"
                    + $"  schief {schiefe,4:F1} Grad");
                if (schiefe > schraeglauf)
                {
                    fehler++;
                    Console.WriteLine($"FEHLER: Randstrassen "
                        + $"{(randstrassen ? "an" : "aus")}: Weg von "
                        + $"({n[0].x:F1}/{n[0].y:F1}) nach "
                        + $"({n[n.Length - 1].x:F1}/{n[n.Length - 1].y:F1}) laeuft "
                        + $"{schiefe:F1} Grad schraeg - erlaubt sind "
                        + $"{schraeglauf:F1} Grad");
                }
            }

            /*
             * WIE GROSS IST DIE LUECKE?
             *
             * Seit dem 2026-09-09 entsteht ein Endfussweg nur zwischen
             * Gassen, die am SELBEN Ende aufhoeren - sonst lief er schraeg
             * quer ueber den Platz. Wo die Enden verschieden sind, entsteht
             * seitdem gar keiner. Hier wird gezaehlt, wie viele
             * Gassennachbarn ohne Fussweg dastehen; das ist der Massstab
             * fuer jeden Vorschlag, der die Luecke schliessen soll.
             */
            var achsen = (layout.AisleLine ?? Array.Empty<float2[]>())
                .Select(a => (A: a[0], B: a[a.Length - 1]))
                .OrderBy(a => a.A.x * achse.y - a.A.y * achse.x)
                .ToArray();
            var ohneWeg = 0;
            for (var i = 0; i + 1 < achsen.Length; i++)
            {
                foreach (var ende in new[] { true, false })
                {
                    var p1 = ende ? achsen[i].A : achsen[i].B;
                    var p2 = ende ? achsen[i + 1].A : achsen[i + 1].B;
                    // Liegt zwischen den beiden Enden ein Weg?
                    var da = math.distance(p1, p2);
                    var hat = (layout.EntranceLine ?? Array.Empty<float2[]>())
                        .Any(n => math.distance(n[0], p1) < da
                            && math.distance(n[n.Length - 1], p2) < da
                            || math.distance(n[0], p2) < da
                            && math.distance(n[n.Length - 1], p1) < da);
                    if (!hat) ohneWeg++;
                }
            }
            Console.WriteLine($"    Gassennachbarn ohne Endfussweg: {ohneWeg}"
                + $" von {Math.Max(0, (achsen.Length - 1) * 2)}");

            /*
             * KEIN ARM DARF LEER BLEIBEN.
             *
             * Der obere Arm ist rund 5.400 m2 gross - Platz fuer weit ueber
             * hundert Buchten. Bleibt er ohne Belag, ist fast die halbe
             * Flaeche ungenutzt. Die Schranke ist bewusst niedrig: es geht
             * nicht um eine gute Ausnutzung, sondern darum, dass dort
             * ueberhaupt gebaut wird.
             */
            if (oben < 500)
            {
                fehler++;
                Console.WriteLine($"FEHLER: Randstrassen "
                    + $"{(randstrassen ? "an" : "aus")}: der obere Arm der "
                    + $"L-Form bekommt nur {oben:F0} m2 Belag - er bleibt leer");
            }
        }

        // Der obere Arm ALLEIN - traegt er fuer sich einen ringlosen Plan?
        var arm = new[]
        {
            new float2(-1037.020263671875f, 118.6897964477539f),
            new float2(-1094.782470703125f, 120.67132568359375f),
            new float2(-1097.97900390625f, 27.53803253173828f),
            new float2(-1040.16f, 26.72f),
        };
        Console.WriteLine();
        Console.WriteLine("  Der obere Arm allein (rund 58 x 93 m):");
        foreach (var randstrassen in new[] { true, false })
        {
            var e = LayoutSettings.Cs2;
            e.Randstrassen = randstrassen;
            e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
            e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
            e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
            e.Entrances = Array.Empty<Entrance>();
            var l = ParkingGeometry.Build(arm, e);
            Console.WriteLine($"    Randstrassen {(randstrassen ? "an " : "aus")}"
                + $"  Buchten {l.Stalls,4}  Gassen {l.Aisles,2}"
                + $"  Teile {l.Parts}  Belag {(l.AsphaltSurface ?? Array.Empty<float2[]>()).Length,3} Ringe");
            foreach (var w in l.Warnings ?? Array.Empty<string>())
                Console.WriteLine("        ! " + w);
        }

        /*
         * DIE STUFENFORM DES NUTZERS (Bauzettel 2026-09-09 00:48).
         *
         * Sieben Ecken, zwei Stufen - und damit nicht konvex. Befund:
         * *"Splitting ist wieder aufgetaucht an Randgruen und
         * Fahrtgassenfussweg."* Gemessen wird, in wie viele Ringe die beiden
         * schmalen Baender zerfallen. Ein gerades Band gehoert in EIN Stueck;
         * 21,3 m Stuecke sind der Gassenabstand, also ein Schnitt je Reihe.
         */
        Console.WriteLine();
        Console.WriteLine("  Stufenform des Nutzers (Bauzettel 00:48):");
        {
            var e = LayoutSettings.Cs2;
            e.Randstrassen = false;
            e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
            e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
            e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
            e.Entrances = new[]
            {
                new Entrance { Edge = 6, Along = 77.4548568725586,
                    Art = Zufahrtsart.Zufahrt },
                new Entrance { Edge = 0, Along = 101.41918182373047,
                    Art = Zufahrtsart.Einfahrt },
            };
            var l = ParkingGeometry.Build(new[]
            {
                new float2(-1037.020263671875f, 118.68978881835938f),
                new float2(-1152.4766845703125f, 122.65056610107422f),
                new float2(-1155.5938720703125f, 31.82688331604004f),
                new float2(-1221.5670166015625f, 34.09100341796875f),
                new float2(-1223.7630615234375f, -29.902002334594727f),
                new float2(-1157.790283203125f, -32.16577911376953f),
                new float2(-1042.3310546875f, -36.12865447998047f),
            }, e);

            double Kurz(float2[] r) => Enumerable.Range(0, r.Length)
                .Select(i => (double)math.distance(r[i], r[(i + 1) % r.Length]))
                .Min();
            double Lang(float2[] r) => Enumerable.Range(0, r.Length)
                .Select(i => (double)math.distance(r[i], r[(i + 1) % r.Length]))
                .Max();
            int Zaehle(float2[][] v, double breite, double laenge) =>
                (v ?? Array.Empty<float2[]>()).Count(r =>
                    Math.Abs(Kurz(r) - breite) < 0.15
                    && Math.Abs(Lang(r) - laenge) < 0.6);

            var gassenabstand = e.Ai + 2 * e.Sl + e.Md;
            var randStuecke = Zaehle(l.GrassSurface, e.Es, gassenabstand);
            var fussStuecke = Zaehle(l.AsphaltSurface, 2.0, gassenabstand);
            var halbeFuss = (l.AsphaltSurface ?? Array.Empty<float2[]>())
                .Count(r => Math.Abs(Kurz(r) - 1.0) < 0.15);
            Console.WriteLine($"    Buchten {l.Stalls}, Gassen {l.Aisles}"
                + $", Belagringe {(l.AsphaltSurface ?? Array.Empty<float2[]>()).Length}"
                + $", Grasringe {(l.GrassSurface ?? Array.Empty<float2[]>()).Length}");
            Console.WriteLine($"    Randgruen in {gassenabstand:F1}-m-Stuecken: {randStuecke}"
                + $" | Fussweg in {gassenabstand:F1}-m-Stuecken: {fussStuecke}"
                + $" | halbbreite Fusswegstuecke: {halbeFuss}");

            /*
             * EIN GERADES BAND GEHOERT NICHT IN REIHENSTUECKE.
             *
             * Die Schranke ist die Reihenzahl selbst: bis zu zwei Stuecke je
             * Band sind Konstruktion (ein Ende oben, eins unten), alles
             * darueber sind Schnitte an den Gassenachsen. Gemessen lagen es
             * 10 beim Randgruen und 14 beim Fussweg.
             */
            if (randStuecke > 2 || fussStuecke > 2)
            {
                fehler++;
                Console.WriteLine($"FEHLER: Stufenform: Randgruen in {randStuecke},"
                    + $" Fussweg in {fussStuecke} Stuecken vom Gassenabstand"
                    + " - die Baender werden an jeder Reihe durchgeschnitten");
            }
            /*
             * NUR BEOBACHTET, NICHT BEANSTANDET.
             *
             * Im zerschnittenen Zustand lagen hier zwei Bruchstuecke von
             * 1,0 m Breite. Nachgemessen war das kein halbes Band, sondern
             * die Folge der Zerlegung. Was uebrig bleibt, ist EIN 90 m langer
             * Ring von 2,0 m Breite, der auf den letzten Metern auf 1,0 m
             * auslaeuft, weil die Grundstueckskante dort hereinschneidet -
             * eine Verjuengung, kein Fehler. Eine Schranke auf die kuerzeste
             * Kante haette genau diesen richtigen Ring rot gemacht.
             */
            Console.WriteLine($"    (Ringe mit einer 1,0-m-Kante: {halbeFuss}"
                + " - Verjuengung an der Kante, keine Beanstandung)");
        }

        // Zum Vergleich das RECHTECK des Nutzers (Bauzettel 23:51) - dort
        // sollen die Enden zusammenfallen, das war der Zweck der Funktion.
        Console.WriteLine();
        Console.WriteLine("  Zum Vergleich das Rechteck (Bauzettel 23:51):");
        {
            var e = LayoutSettings.Cs2;
            e.Randstrassen = false;
            e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
            e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
            e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
            e.Entrances = new[]
            {
                new Entrance { Edge = 0, Along = 80.7355728149414,
                    Art = Zufahrtsart.Zufahrt },
            };
            var l = ParkingGeometry.Build(new[]
            {
                new float2(-1037.0201416015625f, 118.68978118896484f),
                new float2(-1177.112060546875f, 123.49600219726562f),
                new float2(-1183.0321044921875f, -48.99800109863281f),
                new float2(-1042.93701171875f, -53.80400085449219f),
            }, e);
            Console.WriteLine($"    Buchten {l.Stalls}, Gassen {l.Aisles}");
        }

        Console.WriteLine($"Lformtoggle: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
