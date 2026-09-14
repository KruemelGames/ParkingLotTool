using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;

internal static partial class Program
{
    /**
     * WO BLEIBT GRAS, UND WO NICHT.
     *
     * Zwei Fragen des Nutzers am 2026-08-25, beide nur zu beantworten, indem
     * man nachzaehlt statt den Quelltext zu lesen:
     *
     *   1. "Was ist aus dem Gras neben der Einfahrt geworden? Im Prototyp war
     *      da eine Parkbucht breit Gras."
     *   2. "Gilt die Kappen-Regel auch fuer die Parkbuchten aussen an der
     *      Randstrasse? Duerfte dort eigentlich keine geben."
     *
     * Beide betreffen dieselbe Stelle: das Band zwischen Randstrasse und
     * Arealkante, in dem die aeussere Buchtreihe liegt. Deshalb misst diese
     * Hilfe genau dort - Zellen nach Rolle, einmal mit und einmal ohne
     * Kappenschalter, und getrennt danach, wie nah sie an der Einfahrt liegen.
     *
     * Sie veraendert keinen Produktionscode.
     */
    private static readonly Punkt[] KappenRechteck =
    {
        new Punkt(0, 0), new Punkt(175, 0),
        new Punkt(175, 105), new Punkt(0, 105),
    };

    private static int RunKappen()
    {
        Console.WriteLine("KAPPEN UND EINFAHRT - was ist Gras, was ist Belag?");
        Console.WriteLine("  Form: Rechteck 175 x 105 m, eine Zufahrt auf Kante 0 bei 60 m.");
        Console.WriteLine();

        foreach (var kappen in new[] { true, false })
        {
            var bau = BaueMitZufahrt(kappen);
            Console.WriteLine($"--- Querstrassenkappen {(kappen ? "AN" : "AUS")} ---");
            ZaehleNachRolle(bau);
            Console.WriteLine();
            ZaehleRandband(bau);
            Console.WriteLine();
            ZaehleNebenZufahrt(bau);
            Console.WriteLine();
        }
        return 0;
    }

    private static Bauergebnis BaueMitZufahrt(bool kappen)
    {
        var form = new Formdefinition("Kappenmessung", KappenRechteck.ToList());
        var e = new Zelleneinstellungen
        {
            Randabstand = 1.0,
            Fahrgassenbreite = 7.0,
            Querstrassenbreite = 3.0,
            Buchttiefe = 5.9,
            Buchtbreite = 3.0,
            Gruenstreifenbreite = 2.5,
            Querstrassenabstand = 34.0,
            Querstrassenkappen = kappen,
            Reihenwinkel = null,
        };
        // Kante 0 laeuft von (0,0) nach (175,0); 60 m entlang liegt mittig.
        var zufahrten = new List<Zufahrtsvorgabe> { new Zufahrtsvorgabe(0, 60) };
        return Layoutbauer.Baue(form, e, zufahrten);
    }

    private static double Flaeche(Polygon p)
        => Math.Abs(Geometrie.Vorzeichenflaeche(p.Punkte));

    private static void ZaehleNachRolle(Bauergebnis bau)
    {
        Console.WriteLine("  Alle Zellen nach Rolle:");
        foreach (var gruppe in bau.Zellen
            .GroupBy(z => z.Art)
            .OrderByDescending(g => g.Sum(z => Flaeche(z.Polygon))))
        {
            var flaeche = gruppe.Sum(z => Flaeche(z.Polygon));
            var material = string.Join("/", gruppe.Select(z => z.Material)
                .Distinct().OrderBy(m => m.ToString()));
            Console.WriteLine($"    {gruppe.Key,-14} {gruppe.Count(),4} Zellen "
                + $"{flaeche,9:F1} m2   {material}");
        }
    }

    /**
     * Das Randband: alles, was INNERHALB des Innenrandes und AUSSERHALB der
     * Randstrasse liegt. Genau dort steht die aeussere Buchtreihe, um die es
     * in Frage 2 geht.
     */
    private static void ZaehleRandband(Bauergebnis bau)
    {
        var band = bau.Zellen.Where(z =>
        {
            var mitte = Geometrie.Mittelwert(z.Polygon);
            return Geometrie.Enthaelt(bau.Innenrand, mitte)
                   && !Geometrie.Enthaelt(bau.Randstrassenrand, mitte);
        }).ToList();

        Console.WriteLine("  Nur das Band der aeusseren Buchtreihe "
            + "(innerhalb Innenrand, ausserhalb Randstrasse):");
        foreach (var gruppe in band
            .GroupBy(z => z.Art)
            .OrderByDescending(g => g.Sum(z => Flaeche(z.Polygon))))
        {
            var flaeche = gruppe.Sum(z => Flaeche(z.Polygon));
            Console.WriteLine($"    {gruppe.Key,-14} {gruppe.Count(),4} Zellen "
                + $"{flaeche,9:F1} m2   {gruppe.First().Material}");
        }

        /*
         * WO liegt jede einzelne Kappe? Ohne diese Zeilen ist "12 Kappen"
         * eine Zahl ohne Aussage: eine Kappe in der Ecke des Rechtecks ist
         * etwas voellig anderes als eine mitten an einer geraden Kante.
         * Gemessen wird der Abstand zur naechsten Arealecke.
         */
        var kappen = band.Where(z => z.Art == Zellart.Kappe)
            .Select(z => new
            {
                Mitte = Geometrie.Mittelwert(z.Polygon),
                Flaeche = Flaeche(z.Polygon),
            })
            .OrderBy(x => x.Mitte.X).ThenBy(x => x.Mitte.Y)
            .ToList();
        if (kappen.Count == 0) return;
        Console.WriteLine("    Lage jeder Kappe (Abstand zur naechsten Arealecke):");
        foreach (var k in kappen)
        {
            var naechste = double.MaxValue;
            for (var i = 0; i < bau.ArealLokal.Anzahl; i++)
                naechste = Math.Min(naechste,
                    Geometrie.Laenge(bau.ArealLokal.Knoten(i).Punkt - k.Mitte));
            Console.WriteLine($"      x {k.Mitte.X,7:F1}  y {k.Mitte.Y,7:F1}  "
                + $"{k.Flaeche,7:F1} m2   Ecke {naechste,6:F1} m entfernt"
                + (naechste < 12 ? "   <-- ECKE" : "   <-- an gerader Kante"));
        }
    }

    /**
     * Was liegt neben der Einfahrt? Gemessen wird der Abstand der Zellmitte
     * zur Einfahrtsachse, quer zur Fahrtrichtung. Die Einfahrt selbst ist
     * 7 m breit, also 3,5 m je Seite; alles bis 10 m daneben ist "direkt
     * daneben" - eine Bucht ist 3 m breit.
     */
    private static void ZaehleNebenZufahrt(Bauergebnis bau)
    {
        // Zufahrt auf Kante 0 bei 60 m, in LOKALEN Koordinaten.
        var lokal = new Zufahrtsvorgabe(0, 60).NachLokal(bau.Rahmen);
        var a = bau.ArealLokal.Knoten(lokal.Kante).Punkt;
        var b = bau.ArealLokal.Knoten(lokal.Kante + 1).Punkt;
        var laenge = Geometrie.Laenge(b - a);
        var tangente = (b - a) * (1 / laenge);
        var start = a + tangente * lokal.Along;

        Console.WriteLine("  Neben der Einfahrt (Abstand der Zellmitte zur "
            + "Einfahrtsachse, quer):");
        var treffer = bau.Zellen
            .Select(z => new
            {
                Zelle = z,
                Quer = Geometrie.Skalar(
                    Geometrie.Mittelwert(z.Polygon) - start, tangente),
                Tief = Geometrie.Skalar(
                    Geometrie.Mittelwert(z.Polygon) - start,
                    new Punkt(-tangente.Y, tangente.X)),
            })
            .Where(x => Math.Abs(x.Quer) <= 10.0 && x.Tief >= 0 && x.Tief <= 8.0)
            .OrderBy(x => x.Quer)
            .ToList();

        if (treffer.Count == 0)
        {
            Console.WriteLine("    KEINE Zelle in diesem Streifen.");
            return;
        }
        foreach (var x in treffer)
            Console.WriteLine($"    quer {x.Quer,6:F2} m  tief {x.Tief,5:F2} m  "
                + $"{x.Zelle.Art,-14} {x.Zelle.Material,-8} "
                + $"{Flaeche(x.Zelle.Polygon),7:F2} m2"
                + (x.Zelle.BuchtId.HasValue ? $"  Bucht {x.Zelle.BuchtId}" : ""));
    }
}
