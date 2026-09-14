using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;

internal static partial class Program
{
    /**
     * WARUM FEHLT DIE QUERSTRASSE?
     *
     * Nutzerbefund am 2026-08-25, 21:50: *"21 Buchten, gestellt auf N=9, aber
     * es erscheint keine Verbindungsstrasse - bis ich N auf 5 stelle."*
     *
     * Der Fall ist aus seinem Bericht abgeschrieben, nicht nachempfunden:
     * Polygon und Einstellungen stammen aus `ParkingLotTool-report-20260825-
     * 215007.txt`, die zwei Markierungen lagen bei -1263,47/156,72 und
     * -1259,77/97,01 - rund 59,8 m auseinander, dazwischen 21 Buchten.
     *
     * Der Zellenweg fuehrt ueber jede geplante Querstrasse Buch
     * (`Bauergebnis.Querstrassenpruefungen`): je Modul und Querstrasse steht
     * dort, wieviele Buchten links und rechts von ihr liegen und ob sie
     * bleibt. Genau das wird hier ausgegeben - statt zu raten, welche Regel
     * zuschlaegt.
     *
     * Gefahren wird mit mehreren N, damit die Schwelle sichtbar wird: der
     * Nutzer sagt, bei N=5 erscheint sie. Wo genau kippt es?
     */
    private static readonly Punkt[] QuerPolygon =
    {
        new Punkt(-1189.989, 179.581955),
        new Punkt(-1283.77869, 174.132965),
        new Punkt(-1278.15283, 77.27152),
        new Punkt(-1184.35864, 82.71934),
    };

    private static int RunQuerstrassen()
    {
        Console.WriteLine("QUERSTRASSEN - warum fehlt die Verbindung?");
        Console.WriteLine("  Fall aus dem Nutzerbericht vom 2026-08-25 21:50.");
        Console.WriteLine("  Buchtbreite 3,0 m; N Buchten => Cr = (N + 2) * 3,0 m.");
        Console.WriteLine();

        foreach (var n in new[] { 3, 4, 5, 6, 7, 8, 9, 12 })
        {
            var cr = (n + 2) * 3.0;
            var e = new Zelleneinstellungen
            {
                Randabstand = 1.0,
                Fahrgassenbreite = 7.0,
                Querstrassenbreite = 3.0,
                Buchttiefe = 5.9,
                Buchtbreite = 3.0,
                Gruenstreifenbreite = 2.5,
                Querstrassenabstand = cr,
                Querstrassenkappen = true,
                Reihenwinkel = null,
            };

            Bauergebnis bau;
            try
            {
                bau = Layoutbauer.Baue(
                    new Formdefinition("Quer", QuerPolygon.ToList()), e,
                    new List<Zufahrtsvorgabe> { new Zufahrtsvorgabe(0, 40) });
            }
            catch (Exception ausnahme)
            {
                Console.WriteLine($"  N={n,2} (Cr {cr,5:F1} m)  ABBRUCH: "
                    + ausnahme.Message);
                continue;
            }

            var pruefungen = bau.Querstrassenpruefungen ?? new List<Querstrassenpruefung>();
            var geblieben = pruefungen.Count(p => p.Bleibt);
            var buchten = bau.Zellen.Where(z => z.BuchtId.HasValue)
                .Select(z => z.BuchtId.Value).Distinct().Count();
            var querzellen = bau.Zellen.Count(z => z.Art == Zellart.Querstrasse);

            Console.WriteLine($"  N={n,2} (Cr {cr,5:F1} m)  Buchten {buchten,4}  "
                + $"Querstrassenzellen {querzellen,3}  "
                + $"geprueft {pruefungen.Count,3}, davon geblieben {geblieben,3}");

            // Die verworfenen im Einzelnen - dort steht die Ursache.
            foreach (var p in pruefungen.Where(x => !x.Bleibt).Take(6))
                Console.WriteLine($"        VERWORFEN  Modul {p.ModulId,3} "
                    + $"Quer {p.QuerstrassenId,3}  "
                    + $"erste {p.ErsteLinks,3}/{p.ErsteRechts,-3} "
                    + $"zweite {p.ZweiteLinks,3}/{p.ZweiteRechts,-3}");
            foreach (var p in pruefungen.Where(x => x.Bleibt).Take(3))
                Console.WriteLine($"        bleibt     Modul {p.ModulId,3} "
                    + $"Quer {p.QuerstrassenId,3}  "
                    + $"erste {p.ErsteLinks,3}/{p.ErsteRechts,-3} "
                    + $"zweite {p.ZweiteLinks,3}/{p.ZweiteRechts,-3}");
        }

        Console.WriteLine();
        Console.WriteLine("  Lesehilfe: 'erste'/'zweite' sind die Buchtenzahlen links");
        Console.WriteLine("  und rechts der Querstrasse in den beiden Reihen daneben.");
        return 0;
    }
}
