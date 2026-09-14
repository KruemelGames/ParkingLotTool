using System.Globalization;

namespace Zellenversuch;

internal static class Program
{
    private const int Zeitwiederholungen = 200;
    private const int Aufwaermrunden = 100;

    private sealed record Versuchsfall(
        string Nummer,
        string Name,
        Formdefinition Form,
        IReadOnlyList<Zufahrtsvorgabe> Zufahrten,
        int MindestensGetroffeneRandseiten = 1);

    private sealed record Messergebnis(
        Versuchsfall Fall,
        Bauergebnis Bau,
        Rastermessung Raster,
        Bilanzmessung Bilanz,
        Strukturmessung Struktur,
        IReadOnlyList<Flaechenmessung> Flaechen,
        double Median,
        bool Traegt);

    private static readonly Formdefinition[] Formen =
    {
        new("Rechteck", new Punkt[]
        {
            new(0, 0), new(120, 0), new(120, 90), new(0, 90),
        }),
        new("L-Form", new Punkt[]
        {
            new(0, 0), new(120, 0), new(120, 45),
            new(60, 45), new(60, 90), new(0, 90),
        }),
        new("L schraeg", new Punkt[]
        {
            new(-65.7, -31), new(120, -31), new(120, 45),
            new(76.2, 52.4), new(60, 90), new(-65.7, 90),
        }),
        new("L gross", new Punkt[]
        {
            new(-65.7, -31), new(120, -31), new(120, 45),
            new(60, 45), new(60, 90), new(-65.7, 90),
        }),
        new("U-Form", new Punkt[]
        {
            new(0, 0), new(160, 0), new(160, 120), new(100, 120),
            new(100, 60), new(60, 60), new(60, 120), new(0, 120),
        }),
    };

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        var aktiveFormen = args.Length == 0
            ? Formen
            : Formen.Where(form => form.Name.Equals(
                string.Join(' ', args), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (aktiveFormen.Length == 0)
            throw new InvalidOperationException("Die angeforderte Versuchsform ist unbekannt.");

        var faelle = aktiveFormen.SelectMany(Versuchsfaelle).ToArray();
        var zeitfaelle = aktiveFormen
            .Select(form => (form, (IReadOnlyList<Zufahrtsvorgabe>)Array.Empty<Zufahrtsvorgabe>()))
            .Concat(faelle.Select(fall => (fall.Form, fall.Zufahrten)))
            .ToArray();

        Console.WriteLine("Zellenversuch, Teil 3: Zufahrten als Zellteilung und Umwidmung");
        Console.WriteLine($"Masse: Zufahrt {Layoutbauer.Fahrgassenbreite:F1} m breit, "
            + $"{Layoutbauer.Zufahrtstiefe:F1} m bis zur Randstrassenkante | "
            + $"Bucht {Layoutbauer.Buchtbreite:F1} x {Layoutbauer.Buchttiefe:F1} m");
        Console.WriteLine("Eingabe: Polygonkantenindex plus stufenloses along in Metern.");
        Console.WriteLine($"Zeitmessung: Median {Zeitwiederholungen} Laeufe nach "
            + $"{Aufwaermrunden} gemeinsamen Aufwaermrunden; Tiered Compilation aus.");
        Console.WriteLine();

        Messung.WaermeZeitmessung(zeitfaelle, Aufwaermrunden);

        var ergebnisse = new List<Messergebnis>();
        var allesTraegt = true;
        foreach (var form in aktiveFormen)
        {
            var basis = Layoutbauer.Baue(form);
            var basisRaster = Messung.Raster(basis);
            var basisBilanz = Messung.Bilanz(basis);
            var basisStruktur = Messung.Struktur(basis);
            var basisFlaechen = Messung.Flaechen(basis);
            var basisMedian = Messung.MedianBauzeitMillisekunden(
                form, Array.Empty<Zufahrtsvorgabe>(), Zeitwiederholungen);
            var basisTraegt = BeurteileBasis(
                basis, basisRaster, basisBilanz, basisStruktur, basisFlaechen, basisMedian);
            allesTraegt &= basisTraegt;

            Console.WriteLine($"=== {form.Name} ===");
            Console.WriteLine($"Basis ohne Zufahrt: Zellen {basis.Zellen.Count} | "
                + $"Buchten {basis.Buchtenzahl} | Loecher "
                + $"{basis.Lochtrennung.LoecherVorher}->"
                + $"{basis.Lochtrennung.LoecherNachher} | ungedeckt "
                + $"{basisRaster.UngedeckteFlaeche:F2} m2 / "
                + $"{basisRaster.UngedecktProzent:F2} % | Median {basisMedian:F3} ms | "
                + $"{(basisTraegt ? "TRAEGT" : "TRAEGT NICHT")}");
            SchreibeBasisSplitter(basis);

            foreach (var fall in faelle.Where(fall => fall.Form == form))
            {
                var bau = Layoutbauer.Baue(form, fall.Zufahrten);
                var raster = Messung.Raster(bau);
                var bilanz = Messung.Bilanz(bau);
                var struktur = Messung.Struktur(bau);
                var flaechen = Messung.Flaechen(bau);
                var median = Messung.MedianBauzeitMillisekunden(
                    form, fall.Zufahrten, Zeitwiederholungen);
                var traegt = Beurteile(
                    fall, bau, raster, bilanz, struktur, flaechen, median);
                allesTraegt &= traegt;
                ergebnisse.Add(new Messergebnis(
                    fall, bau, raster, bilanz, struktur, flaechen, median, traegt));
                SchreibeFall(fall, basis, bau, raster, bilanz, struktur, flaechen, median, traegt);
            }
            Console.WriteLine();
        }

        SchreibeGesamt(ergebnisse);
        Console.WriteLine();
        Console.WriteLine("Numerische Toleranzen: 0");
        Console.WriteLine("  Keine Epsilon-, Fang-, Rundungs- oder Mindestflaechen-Toleranz.");
        Console.WriteLine("  0,01 m2 ist nur die gemeldete Splittergrenze; nichts wird verworfen.");
        Console.WriteLine("  0,1 m ist nur die Messrasterweite; 0,375 m ist die CS2-Abnahmeschwelle.");
        Console.WriteLine();
        Console.WriteLine("Gebaut: Fall 1 Mitte; Fall 2 Ecke; Fall 3 schraege Kante; "
            + "Fall 4 vorhandene Zellgrenze; Fall 5a Beruehrung und 5b Ueberlappung; "
            + "Fall 6 zehn Zufahrten.");
        Console.WriteLine("Bewusst nicht gebaut: Aufkleber. Keine Mod-Datei wird benutzt.");
        Console.WriteLine();
        Console.WriteLine(allesTraegt
            ? "GESAMTURTEIL: Die Zufahrt traegt als Zellteilung plus Umwidmung."
            : "GESAMTURTEIL: Die Zufahrt traegt in mindestens einem Pflichtfall nicht.");
        return allesTraegt ? 0 : 1;
    }

    private static IReadOnlyList<Versuchsfall> Versuchsfaelle(Formdefinition form)
    {
        var laenge = Kantenlaenge(form, 0);
        var ausgabe = new List<Versuchsfall>
        {
            new("1", "Kantenmitte", form,
                new[] { new Zufahrtsvorgabe(0, laenge / 2) }),
            // along = 1,5 m ist absichtlich kleiner als die halbe Breite
            // 3,5 m. Das Zufahrtsrechteck trifft dadurch zwei Aussenkanten.
            new("2", "1,5 m von Ecke", form,
                new[] { new Zufahrtsvorgabe(0, 1.5) }, 2),
            new("4", "auf Zellgrenze", form,
                new[] { new Zufahrtsvorgabe(0, 8.75) }),
            new("5a", "zwei beruehren", form, new[]
            {
                new Zufahrtsvorgabe(0, laenge / 2 - 3.5),
                new Zufahrtsvorgabe(0, laenge / 2 + 3.5),
            }),
            new("5b", "zwei ueberlappen 2,0 m", form, new[]
            {
                new Zufahrtsvorgabe(0, laenge / 2 - 2.5),
                new Zufahrtsvorgabe(0, laenge / 2 + 2.5),
            }),
        };

        if (form.Name == "L schraeg")
            ausgabe.Insert(2, new Versuchsfall("3", "schraege Kante 3", form,
                new[] { new Zufahrtsvorgabe(3, Kantenlaenge(form, 3) / 2) }));
        if (form.Name == "L gross")
            ausgabe.Add(new Versuchsfall("6", "zehn auf sechs Kanten", form, new[]
            {
                new Zufahrtsvorgabe(0, 30), new Zufahrtsvorgabe(0, 90),
                new Zufahrtsvorgabe(1, 20), new Zufahrtsvorgabe(1, 55),
                new Zufahrtsvorgabe(2, 20), new Zufahrtsvorgabe(3, 20),
                new Zufahrtsvorgabe(4, 30), new Zufahrtsvorgabe(4, 90),
                new Zufahrtsvorgabe(5, 30), new Zufahrtsvorgabe(5, 90),
            }));
        return ausgabe;
    }

    private static double Kantenlaenge(Formdefinition form, int index) =>
        Geometrie.Laenge(form.Punkte[(index + 1) % form.Punkte.Count] - form.Punkte[index]);

    private static void SchreibeBasisSplitter(Bauergebnis basis)
    {
        var splitter = basis.Zellen
            .Select(zelle => (Zelle: zelle, Flaeche: Geometrie.Flaeche(zelle.Polygon)))
            .Where(wert => wert.Flaeche > 0 && wert.Flaeche < Zufahrtsbauer.Splittergrenze)
            .ToArray();
        if (splitter.Length == 0) return;
        Console.WriteLine($"  Bereits vor Zufahrt: {splitter.Length} Splitterzellen unter 0,01 m2.");
        foreach (var wert in splitter)
        {
            var punkte = string.Join(" ", wert.Zelle.Polygon.Ecken
                .Select(ecke => basis.Rahmen.NachWelt(ecke.Knoten.Punkt))
                .Select(punkt => $"({punkt.X:F3};{punkt.Y:F3})"));
            var linien = string.Join('/', wert.Zelle.Polygon.Ecken
                .Select(ecke => ecke.LinieBisNaechste.Art).Distinct());
            Console.WriteLine($"    Z{wert.Zelle.Id} {wert.Flaeche:F9} m2 "
                + $"{wert.Zelle.Art}, Linien {linien}: {punkte}");
        }
    }

    private static void SchreibeFall(
        Versuchsfall fall,
        Bauergebnis basis,
        Bauergebnis bau,
        Rastermessung raster,
        Bilanzmessung bilanz,
        Strukturmessung struktur,
        IReadOnlyList<Flaechenmessung> flaechen,
        double median,
        bool traegt)
    {
        var z = bau.Zufahrtsbericht;
        var lagen = string.Join(", ", fall.Zufahrten.Select(e =>
            $"k{e.Kante}@{e.Along:F3}"));
        Console.WriteLine($"[{fall.Nummer} {fall.Name}] {lagen}");
        Console.WriteLine($"  Zufahrten {z.Vorgaben} | Linien {z.Teilungslinien}, wirksam "
            + $"{z.WirksameTeilungslinien} | Randseiten getroffen {z.GetroffeneRandseiten} | "
            + $"Flaeche {z.Zufahrtsflaeche:F3} m2 | mehrfach {z.MehrfachUeberdeckteFlaeche:F3} m2 | "
            + $"Gruen->Asphalt {z.UmgewidmetesGruen:F3} m2");
        Console.WriteLine($"  Neue Punkte {z.NeueGeometriepunkte} | davon an Innenkanten gemeinsam "
            + $"{z.GemeinsamGenutzteNeuePunkte} | Abweichungen {z.GemeinsamePunktabweichungen} | "
            + $"zu wenig Nutzer {z.NeuePunkteMitZuWenigNutzern}");
        foreach (var fehler in z.Punktnutzungsfehler)
            Console.WriteLine($"    Punkt {fehler.Knoten.Id} "
                + $"({fehler.Knoten.Punkt.X:F6};{fehler.Knoten.Punkt.Y:F6}): "
                + $"Nutzer {fehler.Nutzer}/{fehler.Erwartet}, Quelle {fehler.Quelllinienart}");
        Console.WriteLine($"  Zellen {bau.ZellenVorZufahrt.Count}->{bau.Zellen.Count} | Buchten "
            + $"{basis.Buchtenzahl}->{bau.Buchtenzahl} (-{z.EntfalleneBuchten}) | "
            + $"Nullflaeche {z.NullflaechenzellenVorher}->{z.Nullflaechenzellen} | "
            + $"Splitter <0,01 m2 {z.SplitterzellenVorher}->{z.Splitterzellen} | "
            + $"kleinste Zelle {z.KleinsteZellflaecheVorher:F6}->"
            + $"{z.KleinsteZellflaeche:F6} m2");
        Console.WriteLine($"  Flaechenbilanz Zufahrt nach-vor {bilanz.Zufahrtsdifferenz:E3} m2 | "
            + $"bitgenau {(bilanz.ZufahrtsbilanzBitgenau ? "ja" : "nein")} | "
            + $"kombinatorisch exakt {(bilanz.ZufahrtsbilanzKombinatorischExakt ? "ja" : "nein")} | "
            + $"Quellzellfehler {bilanz.Quellzellenfehler}");
        Console.WriteLine($"  Loecher roh {basis.Lochtrennung.LoecherVorher}->"
            + $"{bau.Lochtrennung.LoecherVorher}, fertig "
            + $"{basis.Lochtrennung.LoecherNachher}->{bau.Lochtrennung.LoecherNachher} | "
            + $"Randbandkomponenten {struktur.Randbandkomponenten}, Inseln {struktur.Randbandinseln}");
        Console.WriteLine($"  Fertig: engste Stelle {flaechen.Min(x => x.Engstelle):F3} m | "
            + $"kuerzeste Kante {flaechen.Min(x => x.KuerzesteKante):F3} m | "
            + $"Loecher {flaechen.Sum(x => x.Flaeche.Loecher.Count)} | "
            + $"Selbstkreuzungen {flaechen.Sum(x => x.Selbstkreuzungen)}");
        foreach (var wert in flaechen.Where(wert =>
                     wert.Engstelle < Messung.Cs2Mindestkante
                     || wert.KuerzesteKante < Messung.Cs2Mindestkante))
        {
            var kurzA = bau.Rahmen.NachWelt(wert.KuerzesteKanteSegment.Von.Punkt);
            var kurzB = bau.Rahmen.NachWelt(wert.KuerzesteKanteSegment.Nach.Punkt);
            var eng = bau.Rahmen.NachWelt(wert.EngstellenKnoten.Punkt);
            Console.WriteLine($"    Unter 0,375 m in {wert.Flaeche.Material}: "
                + $"Kante {wert.KuerzesteKante:F3} m "
                + $"({kurzA.X:F3};{kurzA.Y:F3})->({kurzB.X:F3};{kurzB.Y:F3}), "
                + $"Engstelle {wert.Engstelle:F3} m bei ({eng.X:F3};{eng.Y:F3})");
        }
        Console.WriteLine($"  Raster: ungedeckt {raster.UngedeckteFlaeche:F2} m2 / "
            + $"{raster.UngedecktProzent:F2} % | ueberlappt {raster.UeberlappteFlaeche:F2} m2 | "
            + $"Topologie offen {bau.Topologie.OffeneTeilungsnaehte + bau.Topologie.UnerwarteteOffeneKanten}, "
            + $"nicht-mannigfaltig {bau.Topologie.NichtMannigfaltigeKanten}, "
            + $"Linien-ID-Fehler {bau.Topologie.AbweichendeLinienIds}");
        Console.WriteLine($"  Rechenzeit Median {median:F3} ms | Raster {raster.Dauer.TotalMilliseconds:F3} ms | "
            + $"{(traegt ? "TRAEGT" : "TRAEGT NICHT")}");
    }

    private static void SchreibeGesamt(IReadOnlyList<Messergebnis> ergebnisse)
    {
        if (ergebnisse.Count == 0) return;
        Console.WriteLine("=== Zusammenfassung der Zufahrtsfaelle ===");
        foreach (var gruppe in ergebnisse.GroupBy(x => x.Fall.Form.Name))
            Console.WriteLine($"{gruppe.Key}: {gruppe.Count(x => x.Traegt)}/{gruppe.Count()} tragen | "
                + $"neue Punkte {gruppe.Min(x => x.Bau.Zufahrtsbericht.NeueGeometriepunkte)}.."
                + $"{gruppe.Max(x => x.Bau.Zufahrtsbericht.NeueGeometriepunkte)} | "
                + $"Punktabweichungen max {gruppe.Max(x => x.Bau.Zufahrtsbericht.GemeinsamePunktabweichungen)} | "
                + $"neue Splitter max {gruppe.Max(x =>
                    x.Bau.Zufahrtsbericht.Splitterzellen
                    - x.Bau.Zufahrtsbericht.SplitterzellenVorher)} | "
                + $"engste fertige Stelle {gruppe.Min(x => x.Flaechen.Min(f => f.Engstelle)):F3} m | "
                + $"Median max {gruppe.Max(x => x.Median):F3} ms");
    }

    private static bool BeurteileBasis(
        Bauergebnis bau,
        Rastermessung raster,
        Bilanzmessung bilanz,
        Strukturmessung struktur,
        IReadOnlyList<Flaechenmessung> flaechen,
        double median)
    {
        var erwarteteBuchten = bau.Form.Name switch
        {
            "Rechteck" => 208,
            "L-Form" => 148,
            "L schraeg" => 385,
            "L gross" => 368,
            "U-Form" => 310,
            _ => -1,
        };
        return Grundpruefungen(bau, raster, bilanz, struktur, flaechen, median)
            && bau.Buchtenzahl == erwarteteBuchten
            && bau.Zufahrtsbericht.Vorgaben == 0
            && struktur.RandseitenMitRandband == struktur.Randseiten;
    }

    // L schraeg besitzt im Vollauf bereits vorher 5 exakte Splitter
    // (Minimum 0,000009697 m2). Die Zufahrt darf keinen hinzufuegen;
    // wegfiltern wuerde dagegen die unveraenderte Flaechenbilanz brechen.
    private static bool Beurteile(
        Versuchsfall fall,
        Bauergebnis bau,
        Rastermessung raster,
        Bilanzmessung bilanz,
        Strukturmessung struktur,
        IReadOnlyList<Flaechenmessung> flaechen,
        double median) =>
        Grundpruefungen(bau, raster, bilanz, struktur, flaechen, median)
        && bau.Zufahrtsbericht.Vorgaben == fall.Zufahrten.Count
        && bau.Zufahrtsbericht.Teilungslinien == 2 * fall.Zufahrten.Count
        && bau.Zufahrtsbericht.GetroffeneRandseiten >= fall.MindestensGetroffeneRandseiten
        && bau.Zufahrtsbericht.GemeinsamePunktabweichungen == 0
        && bau.Zufahrtsbericht.NeuePunkteMitZuWenigNutzern == 0
        && bau.Zufahrtsbericht.Nullflaechenzellen
            == bau.Zufahrtsbericht.NullflaechenzellenVorher
        && bau.Zufahrtsbericht.Splitterzellen
            <= bau.Zufahrtsbericht.SplitterzellenVorher
        && bilanz.ZufahrtsbilanzKombinatorischExakt
        && struktur.Randbandinseln == 0
        && bau.Buchtenzahl > 0
        && FallWurdeTatsaechlichGetroffen(fall, bau.Zufahrtsbericht);

    private static bool FallWurdeTatsaechlichGetroffen(
        Versuchsfall fall,
        Zufahrtsbericht bericht) =>
        fall.Nummer switch
        {
            "1" => bericht.NeueGeometriepunkte > 0,
            "2" => bericht.GetroffeneRandseiten >= 2,
            // Eine der beiden Seiten liegt genau auf einer vorhandenen Zelllinie
            // und darf deshalb weder neue Knoten noch eine Nullzelle erzeugen.
            "4" => bericht.WirksameTeilungslinien < bericht.Teilungslinien,
            "5a" => bericht.MehrfachUeberdeckteFlaeche == 0
                && bericht.WirksameTeilungslinien < bericht.Teilungslinien,
            "5b" => bericht.MehrfachUeberdeckteFlaeche > 0,
            "6" => bericht.Vorgaben == 10,
            _ => bericht.NeueGeometriepunkte > 0,
        };

    private static bool Grundpruefungen(
        Bauergebnis bau,
        Rastermessung raster,
        Bilanzmessung bilanz,
        Strukturmessung struktur,
        IReadOnlyList<Flaechenmessung> flaechen,
        double median) =>
        raster.UngedecktePunkte == 0
        && raster.UeberlapptePunkte == 0
        && bau.Topologie.OffeneTeilungsnaehte == 0
        && bau.Topologie.UnerwarteteOffeneKanten == 0
        && bau.Topologie.NichtMannigfaltigeKanten == 0
        && bau.Topologie.GleichgerichteteDoppelkanten == 0
        && bau.Topologie.AbweichendeLinienIds == 0
        && bau.Lochtrennung.LoecherNachher == 0
        && bau.Lochtrennung.NeueGeometriepunkte == 0
        && bau.Lochtrennung.NurVorhandeneZellkanten
        && bilanz.TrennbilanzKombinatorischExakt
        && struktur.NichtRandzellenAnAussenkante == 0
        && struktur.QuerstrassenMitBeidenKantenIds == struktur.Querstrassen
        && struktur.QuerstrassenZellfehler == 0
        && flaechen.All(wert => wert.Flaeche.Loecher.Count == 0)
        && flaechen.All(wert => wert.Selbstkreuzungen == 0)
        && flaechen.All(wert => wert.KuerzesteKante >= Messung.Cs2Mindestkante)
        && flaechen.All(wert => wert.Engstelle >= Messung.Cs2Mindestkante)
        && median < 50;
}
