using System;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

/**
 * DARF EIN PUNKT AUF DER GERADEN DAS LAYOUT UMWERFEN?
 *
 * Befund des Nutzers am 2026-09-09: *"Ein Polygonpunkt liegt direkt auf einer
 * direktionalen Linie. Dieser Punkt liegt eigentlich genau so da, dass er
 * nichts aendern sollte - theoretisch. Aber praktisch aendert er ganz viel."*
 *
 * Zwei Vorschau-Berichte, drei Sekunden auseinander, derselbe Umriss - einmal
 * mit sieben, einmal mit sechs Punkten. Der siebte liegt einen halben
 * MILLIMETER neben der Verbindungsgeraden seiner Nachbarn:
 *
 *     20:32:29   7 Punkte   699 Buchten   8 Fahrgassen   Winkel  88,04
 *     20:32:32   6 Punkte   703 Buchten   7 Fahrgassen   Winkel 178,03
 *
 * Ein Punkt auf der Geraden aendert die FLAECHE nicht. Er darf deshalb auch
 * das Ergebnis nicht umwerfen.
 */
internal static partial class Program
{
    private static int RunGeradepunkt()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        // Beide Umrisse aus den Berichten 20:32:29 und 20:32:32, unveraendert.
        var mitPunkt = new[]
        {
            new float2(-1037.02026f, 118.689781f),
            new float2(-1170.38806f, 123.265007f),
            new float2(-1173.37708f, 36.167f),
            new float2(-1239.602f, 38.439003f),
            new float2(-1242.69409f, -51.6540031f),
            new float2(-1175.22607f, -53.969f),
            new float2(-1043.099f, -58.504f),
        };
        var ohnePunkt = new[]
        {
            new float2(-1037.02026f, 118.689781f),
            new float2(-1170.38806f, 123.265007f),
            new float2(-1173.37708f, 36.167f),
            new float2(-1239.602f, 38.439003f),
            new float2(-1242.69409f, -51.6540031f),
            new float2(-1043.099f, -58.504f),
        };

        /*
         * ERST NACHWEISEN, DASS DER PUNKT WIRKLICH AUF DER GERADEN LIEGT.
         *
         * Sonst misst der Lauf zwei verschiedene Flaechen und meldet einen
         * Unterschied, den es geben MUSS.
         */
        var a = ohnePunkt[4];
        var b = ohnePunkt[5];
        var p = mitPunkt[5];
        var d = b - a;
        var lot = Math.Abs(d.x * (p.y - a.y) - d.y * (p.x - a.x))
            / math.length(d);
        Console.WriteLine($"  Der siebte Punkt liegt {lot * 1000:F2} mm neben "
            + $"der Geraden (Kante {math.length(d):F1} m lang)");
        Pruefe(lot < 0.01,
            $"der Punkt liegt {lot:F3} m neben der Geraden - das ist keine "
            + "gestreckte Ecke mehr, der Vergleich trägt nicht");

        var e = LayoutSettings.Cs2;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.Randstrassen = true;
        e.AutomaticEntrances = false;
        e.Entrances = Array.Empty<Entrance>();

        (int Buchten, int Gassen, double Winkel, double Flaeche) Baue(
            string name, float2[] form)
        {
            var layout = ParkingGeometry.Build(form, e);
            var g0 = layout.AisleLine != null && layout.AisleLine.Length > 0
                ? layout.AisleLine[0] : null;
            var winkel = g0 == null ? double.NaN
                : Math.Atan2(g0[g0.Length - 1].y - g0[0].y,
                    g0[g0.Length - 1].x - g0[0].x) * 180 / Math.PI;
            if (winkel < 0) winkel += 180;
            var flaeche = Math.Abs(Enumerable.Range(0, form.Length).Sum(i =>
                (double)form[i].x * form[(i + 1) % form.Length].y
                - (double)form[(i + 1) % form.Length].x * form[i].y) / 2);
            Console.WriteLine($"  {name,-14} {form.Length} Punkte   "
                + $"{layout.Stalls,4} Buchten   "
                + $"{(layout.AisleLine?.Length ?? 0),2} Fahrgassen   "
                + $"Winkel {winkel,6:F2}   Fläche {flaeche,9:F1} m²");
            return (layout.Stalls, layout.AisleLine?.Length ?? 0, winkel,
                flaeche);
        }

        var mit = Baue("mit Punkt", mitPunkt);
        var ohne = Baue("ohne Punkt", ohnePunkt);

        /*
         * DIE FLAECHE IST DERSELBE MASSSTAB WIE OBEN - nur anders gerechnet.
         * Stimmt sie nicht ueberein, ist der Punkt doch keine gestreckte Ecke.
         */
        Pruefe(Math.Abs(mit.Flaeche - ohne.Flaeche) < 1.0,
            $"die Flächen unterscheiden sich um "
            + $"{Math.Abs(mit.Flaeche - ohne.Flaeche):F1} m²");

        /*
         * WAS GLEICH BLEIBEN MUSS.
         *
         * Der Winkel zuerst: er entscheidet alles Weitere. Danach die Zahl der
         * Fahrgassen und die Buchtenzahl - letztere mit etwas Spiel, weil die
         * zusaetzliche Ecke die Randreihe an genau dieser Stelle um eine
         * Bucht verschieben darf.
         */
        var winkelUnterschied = Math.Abs(mit.Winkel - ohne.Winkel);
        winkelUnterschied = Math.Min(winkelUnterschied, 180 - winkelUnterschied);
        Pruefe(winkelUnterschied < 0.5,
            $"der Reihenwinkel springt um {winkelUnterschied:F2} Grad, "
            + $"{mit.Winkel:F2} statt {ohne.Winkel:F2} - ein Punkt auf der "
            + "Geraden dreht den ganzen Parkplatz");
        Pruefe(mit.Gassen == ohne.Gassen,
            $"{mit.Gassen} Fahrgassen statt {ohne.Gassen}");
        Pruefe(Math.Abs(mit.Buchten - ohne.Buchten) <= 2,
            $"{mit.Buchten} Buchten statt {ohne.Buchten} - "
            + $"{Math.Abs(mit.Buchten - ohne.Buchten)} Unterschied");

        /*
         * DIE TOLERANZGRENZE - beide Seiten.
         *
         * `GeradeToleranz` sagt, wie weit ein Punkt neben der Sehne liegen
         * darf. Eine Grenze, die nur von einer Seite geprueft wird, ist keine:
         * bei 5,0 cm muss der Lauf noch zusammenfassen, bei 5,1 cm nicht mehr.
         * Sonst waere jede beliebig grosse Toleranz "richtig".
         */
        var pa = new Punkt(ohnePunkt[4].x, ohnePunkt[4].y);
        var pb = new Punkt(ohnePunkt[5].x, ohnePunkt[5].y);
        var richtung = pb - pa;
        var laenge = Geometrie.Laenge(richtung);
        var anteil = Geometrie.Skalar(
            new Punkt(mitPunkt[5].x, mitPunkt[5].y) - pa, richtung)
            / (laenge * laenge);
        var lotrecht = new Punkt(-richtung.Y / laenge, richtung.X / laenge);

        Punkt[] MitVersatz(double versatz)
        {
            var punkte = mitPunkt
                .Select(q => new Punkt(q.x, q.y)).ToArray();
            punkte[5] = pa + richtung * anteil + lotrecht * versatz;
            return punkte;
        }

        var bezug = Geometrie.LaengsteGerade(
            ohnePunkt.Select(q => new Punkt(q.x, q.y)).ToArray());
        Console.WriteLine($"  Bezugsrichtung ohne Punkt: "
            + $"{bezug.Grad:F9} Grad über {bezug.Laenge:F1} m");

        var grenze = Geometrie.GeradeToleranz;
        foreach (var versatz in new[]
            { 0.0, 0.000467931, grenze, -grenze, grenze + 0.001 })
        {
            var lauf = Geometrie.LaengsteGerade(MitVersatz(versatz));
            var gleich = Math.Abs(lauf.Grad - bezug.Grad) < 1e-8;
            Console.WriteLine($"    Lot {versatz * 1000,8:F3} mm -> "
                + $"{lauf.Grad,12:F9} Grad über {lauf.Laenge,7:F1} m"
                + (gleich ? "   zusammengefasst" : "   eigene Ecke"));
            if (Math.Abs(versatz) <= grenze)
                Pruefe(gleich,
                    $"bei {versatz * 1000:F1} mm Lot fasst der Lauf nicht mehr "
                    + "zusammen - innerhalb der Toleranz muss er es");
            else
                Pruefe(!gleich,
                    $"bei {versatz * 1000:F1} mm Lot fasst der Lauf noch immer "
                    + "zusammen - die Toleranz greift nicht");
        }

        /*
         * UND DAS ERGEBNIS HAENGT NICHT AM STARTPUNKT.
         *
         * Der Lauf darf ueber den Listenanfang hinweggehen, sonst entschiede
         * die Stelle, an der der Nutzer zu zeichnen begonnen hat. Geprueft
         * wird jede Drehung der Punktliste, in beiden Umlaufrichtungen.
         */
        var proben = 0;
        var abweichend = 0;
        foreach (var umgekehrt in new[] { false, true })
        for (var drehung = 0; drehung < mitPunkt.Length; drehung++)
        {
            var punkte = MitVersatz(0.000467931).ToList();
            if (umgekehrt) punkte.Reverse();
            var gedreht = punkte.Skip(drehung).Concat(punkte.Take(drehung))
                .ToArray();
            proben++;
            if (Math.Abs(Geometrie.LaengsteGerade(gedreht).Grad - bezug.Grad)
                >= 1e-8) abweichend++;
        }
        Console.WriteLine($"  {proben} Umlaufproben, {abweichend} abweichend");
        Pruefe(abweichend == 0,
            $"{abweichend} von {proben} Umlaufproben liefern eine andere "
            + "Richtung - das Ergebnis hängt am Startpunkt");

        Console.WriteLine($"Geradepunkt: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
