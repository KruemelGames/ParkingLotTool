using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Warum zeigt der Zellenweg bei manchen Formen NICHTS an?
     *
     * Nutzerbefund vom 2026-08-21: "wenn ich an den Linien in der L Form ziehe
     * dann zeigt er nix an ... ich denke mal er bricht durch einige Regeln
     * einfach wieder ab."
     *
     * Statt das zu vermuten, laeuft hier jede je gezogene Form des Nutzers
     * durch den Zellenweg, und die Abbrueche werden nach ihrer Meldung
     * gruppiert. Der Zellenweg braucht rund 15 ms je Form - der ganze Lauf
     * kostet also Sekunden, nicht Minuten.
     */
    private static int RunZellenlauf()
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Zellen = true;
        // Wie im Spiel und wie im Qualitaetslauf: mit Zufahrt. Der Zellenweg
        // kennt keine automatischen Zufahrten, und automatische soll es laut
        // Nutzer auch nie geben - ohne diese Zeile pruefte der Lauf einen
        // Fall, den es gar nicht gibt.
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var formen = new List<(string Name, float2[] Site)>();
        foreach (var fall in Cases) formen.Add((fall.Name, fall.Site));
        formen.Add(("L-Prototyp", new[]
        {
            new float2(-45.545136f, -28.210793f),
            new float2(132.066062f, -28.210793f),
            new float2(132.066062f, 37.522440f),
            new float2(60f, 37.522440f),
            new float2(60f, 85.786789f),
            new float2(-45.545136f, 85.786789f),
        }));
        formen.Add(("L-Prototyp gross", new[]
        {
            new float2(-65.7f, -31f), new float2(120f, -31f),
            new float2(120f, 45f), new float2(60f, 45f),
            new float2(60f, 90f), new float2(-65.7f, 90f),
        }));
        formen.AddRange(FormenAusBauprotokoll());

        Console.WriteLine($"ZELLENLAUF ueber {formen.Count} Formen, Modus edge, "
            + "mit Zufahrt Kante 0 bei 20 m");
        Console.WriteLine();

        var gutZeiten = new List<double>();
        var abbrueche = new List<(string Form, string Meldung, string Ort)>();
        // Einspringende Ecken zaehlen: die Konvexzerlegung ist der Verdacht,
        // und ihre Arbeit haengt genau daran.
        var gutEcken = new List<int>();
        var schlechtEcken = new Dictionary<string, List<int>>();
        // Vereinzelte Buchten - das Mass fuer die Regel des Nutzers vom
        // 2026-08-21: eine Reihe soll mindestens 5 Buchten am Stueck haben.
        var kleineGruppen = 0;
        var gruppenGesamt = 0;
        var buchtenInKleinen = 0;
        var formenMitKleinen = 0;
        var kleineAlleAnQuerstrasse = 0;
        var buchtenAlleAnQuerstrasse = 0;
        var kleineAlleAusArealform = 0;
        var buchtenAlleAusArealform = 0;
        var kleineAnQuerstrasse = 0;
        var buchtenAnQuerstrasse = 0;
        var kleineAusArealform = 0;
        var buchtenAusArealform = 0;
        var kleineInnen = 0;
        var gruppenInnen = 0;
        var formenMitKleinenInnen = 0;
        var kleineAmRand = 0;
        var buchtenAmRand = 0;
        var querstrassenenden = 0;
        var exakteQuerstrassenenden = 0;
        var formenMitOffenenQuerstrassen = 0;
        foreach (var form in formen)
        {
            var uhr = Stopwatch.StartNew();
            try
            {
                var layout = ParkingGeometry.Build(form.Site, einstellungen);
                uhr.Stop();
                gutZeiten.Add(uhr.Elapsed.TotalMilliseconds);
                gutEcken.Add(EinspringendeEcken(form.Site));
                var anschluesse = PruefeAnschluesse(layout);
                querstrassenenden += anschluesse.Querstrassenenden.Gesamt;
                exakteQuerstrassenenden += anschluesse.Querstrassenenden.Exakt;
                if (!anschluesse.Querstrassenenden.Vollstaendig)
                    formenMitOffenenQuerstrassen++;
                var gruppen = Buchtengruppen(layout);
                var klein = gruppen.Count(g => g.Anzahl < 5);
                var kleineGruppenDieserForm = gruppen
                    .Where(g => g.Anzahl < 5)
                    .ToArray();
                gruppenGesamt += gruppen.Count;
                kleineGruppen += klein;
                buchtenInKleinen += kleineGruppenDieserForm.Sum(g => g.Anzahl);
                foreach (var gruppe in kleineGruppenDieserForm)
                {
                    if (EndetAnQuerstrasse(gruppe, layout))
                    {
                        kleineAlleAnQuerstrasse++;
                        buchtenAlleAnQuerstrasse += gruppe.Anzahl;
                    }
                    else
                    {
                        kleineAlleAusArealform++;
                        buchtenAlleAusArealform += gruppe.Anzahl;
                    }
                }
                var innereGruppen = Buchtengruppen(layout, BayKind.Inner);
                var kleineInnere = innereGruppen
                    .Where(gruppe => gruppe.Anzahl < 5)
                    .ToArray();
                gruppenInnen += innereGruppen.Count;
                kleineInnen += kleineInnere.Length;
                if (kleineInnere.Length != 0) formenMitKleinenInnen++;
                foreach (var gruppe in kleineInnere)
                {
                    if (EndetAnQuerstrasse(gruppe, layout))
                    {
                        kleineAnQuerstrasse++;
                        buchtenAnQuerstrasse += gruppe.Anzahl;
                    }
                    else
                    {
                        kleineAusArealform++;
                        buchtenAusArealform += gruppe.Anzahl;
                    }
                }
                var kleineRandgruppen = Buchtengruppen(
                        layout, BayKind.Perimeter)
                    .Where(gruppe => gruppe.Anzahl < 5)
                    .ToArray();
                kleineAmRand += kleineRandgruppen.Length;
                buchtenAmRand += kleineRandgruppen.Sum(
                    gruppe => gruppe.Anzahl);
                if (klein > 0) formenMitKleinen++;
                if (layout.Stalls == 0)
                    abbrueche.Add((form.Name, "0 Buchten gebaut (keine Ausnahme)", "-"));
            }
            catch (Exception ausnahme)
            {
                uhr.Stop();
                while (ausnahme.InnerException != null)
                    ausnahme = ausnahme.InnerException;
                abbrueche.Add((form.Name, ausnahme.Message,
                    ErsteEigeneZeile(ausnahme)));
                if (!schlechtEcken.TryGetValue(ausnahme.Message, out var liste))
                    schlechtEcken[ausnahme.Message] = liste = new List<int>();
                liste.Add(EinspringendeEcken(form.Site));
            }
        }

        gutZeiten.Sort();
        Console.WriteLine($"  {gutZeiten.Count} von {formen.Count} Formen gebaut, "
            + $"{abbrueche.Count} abgebrochen "
            + $"({100d * abbrueche.Count / formen.Count:F1} %)");
        if (gutZeiten.Count != 0)
            Console.WriteLine($"  Bauzeit Median {gutZeiten[gutZeiten.Count / 2]:F1} ms, "
                + $"schlimmste {gutZeiten[gutZeiten.Count - 1]:F1} ms");
        Console.WriteLine($"  Buchtengruppen in Parkmodulen: {gruppenInnen} gesamt, "
            + $"davon {kleineInnen} unter 5 Buchten "
            + $"({buchtenAnQuerstrasse + buchtenAusArealform} Buchten), auf "
            + $"{formenMitKleinenInnen} von {gutZeiten.Count} gebauten Formen");
        Console.WriteLine($"    an einer Querstrasse: {kleineAnQuerstrasse} Gruppen "
            + $"({buchtenAnQuerstrasse} Buchten)");
        Console.WriteLine($"    aus der Arealform:    {kleineAusArealform} Gruppen "
            + $"({buchtenAusArealform} Buchten)");
        Console.WriteLine($"  Alle Buchtarten (alte Messbreite): {gruppenGesamt} gesamt, "
            + $"{kleineGruppen} unter 5 ({buchtenInKleinen} Buchten), auf "
            + $"{formenMitKleinen} Formen");
        Console.WriteLine($"    an einer Querstrasse: {kleineAlleAnQuerstrasse} Gruppen "
            + $"({buchtenAlleAnQuerstrasse} Buchten)");
        Console.WriteLine($"    aus der Arealform:    {kleineAlleAusArealform} Gruppen "
            + $"({buchtenAlleAusArealform} Buchten)");
        Console.WriteLine($"    davon ausserhalb der Modulregel am Rand: "
            + $"{kleineAmRand} Gruppen ({buchtenAmRand} Buchten)");
        Console.WriteLine($"  Querstrassenenden: {querstrassenenden} gesamt, "
            + $"{exakteQuerstrassenenden} exakt an Fahrgasse/Randstrasse; "
            + $"{formenMitOffenenQuerstrassen} Formen mit offenem Ende");
        Console.WriteLine();

        // Nach Meldung gruppiert: eine Ursache mit 40 Treffern ist etwas
        // anderes als 40 Einzelfaelle, und genau diese Unterscheidung
        // entscheidet, woran als naechstes gearbeitet wird.
        Console.WriteLine("  ABBRUECHE, haeufigste zuerst:");
        foreach (var gruppe in abbrueche
                     .GroupBy(a => a.Meldung)
                     .OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {gruppe.Count(),4}x  {gruppe.Key}");
            Console.WriteLine($"        bei {gruppe.First().Ort}");
            if (schlechtEcken.TryGetValue(gruppe.Key, out var ecken))
                Console.WriteLine($"        einspringende Ecken: "
                    + string.Join(", ", ecken.GroupBy(e => e)
                        .OrderBy(g => g.Key)
                        .Select(g => $"{g.Count()}x {g.Key}")));
            Console.WriteLine("        z. B. " + string.Join(", ",
                gruppe.Take(4).Select(a => a.Form)));
        }
        if (abbrueche.Count == 0) Console.WriteLine("        keine");
        Console.WriteLine();
        Console.WriteLine("  Gebaute Formen nach einspringenden Ecken: "
            + string.Join(", ", gutEcken.GroupBy(e => e).OrderBy(g => g.Key)
                .Select(g => $"{g.Count()}x {g.Key}")));
        Console.WriteLine();

        // Die Formen namentlich, damit sich jede einzeln nachstellen laesst.
        foreach (var abbruch in abbrueche.Take(20))
        {
            var form = formen.First(f => f.Name == abbruch.Form);
            Console.WriteLine($"  {abbruch.Form}: " + string.Join(";",
                form.Site.Select(p => $"{p.x.ToString("0.###", Kultur)},"
                    + $"{p.y.ToString("0.###", Kultur)}")));
        }
        return 0;
    }

    /** Ecken, an denen das Polygon nach innen knickt (CCW vorausgesetzt). */
    private static int EinspringendeEcken(float2[] ring)
    {
        var flaeche = 0.0;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            flaeche += (double)a.x * b.y - (double)b.x * a.y;
        }
        var vorzeichen = flaeche >= 0 ? 1 : -1;
        var zahl = 0;
        for (var i = 0; i < ring.Length; i++)
        {
            var vor = ring[(i + ring.Length - 1) % ring.Length];
            var hier = ring[i];
            var nach = ring[(i + 1) % ring.Length];
            var kreuz = ((double)hier.x - vor.x) * ((double)nach.y - hier.y)
                      - ((double)hier.y - vor.y) * ((double)nach.x - hier.x);
            if (kreuz * vorzeichen < 0) zahl++;
        }
        return zahl;
    }

    private static readonly System.Globalization.CultureInfo Kultur
        = System.Globalization.CultureInfo.InvariantCulture;

    /** Die erste Zeile der Ausnahme, die im eigenen Code liegt. */
    private static string ErsteEigeneZeile(Exception ausnahme)
    {
        var spur = ausnahme.StackTrace;
        if (string.IsNullOrEmpty(spur)) return "-";
        foreach (var zeile in spur.Split('\n'))
            if (zeile.Contains("ParkingLotTool."))
                return zeile.Trim();
        return spur.Split('\n')[0].Trim();
    }
}
