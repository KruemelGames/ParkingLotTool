using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;

internal static partial class Program
{
    /**
     * Die Ziehung der Infokarten.
     *
     * Geprueft wird das, was im Spiel niemand sieht: dass jede Runde je eine
     * Info aus jeder verfuegbaren Kategorie bringt, dass Mangelmeldungen NIE
     * mitrotieren, und dass sich eine Info nicht zweimal hintereinander
     * wiederholt. Der letzte Punkt ist der eigentliche Grund fuer diesen
     * Lauf - eine Ziehung ohne Sperre sieht im Spiel voellig normal aus.
     */
    private static int RunInfokarten()
    {
        var fehler = new List<string>();

        PruefeInfoKategorien(fehler);
        PruefeInfoMengenstufen(fehler);
        PruefeInfoWegstufen(fehler);
        var runden = PruefeInfoZiehung(fehler);
        PruefeInfoVerfuegbarkeit(fehler);
        PruefeInfoMangel(fehler);
        PruefeInfoStaerkste(fehler);

        foreach (var zeile in fehler) Console.WriteLine("  " + zeile);
        Console.WriteLine("INFOKARTEN: " + runden + " Ziehungen, "
            + fehler.Count + " Fehler"
            + (fehler.Count == 0 ? " - alles sauber" : ""));
        return fehler.Count == 0 ? 0 : 1;
    }

    private static void PruefeInfoKategorien(List<string> fehler)
    {
        void Erwarte(Infoart art, Infokategorie soll)
        {
            var ist = Infokarten.KategorieVon(art);
            if (ist != soll)
                fehler.Add($"Kategorie von {art}: {ist} statt {soll}");
        }

        Erwarte(Infoart.Keine, Infokategorie.Keine);
        Erwarte(Infoart.ZielRang, Infokategorie.Gaeste);
        Erwarte(Infoart.Stammgaeste, Infokategorie.Gaeste);
        Erwarte(Infoart.Fussweg, Infokategorie.Lage);
        Erwarte(Infoart.Erreichbar, Infokategorie.Lage);
        Erwarte(Infoart.Tagesprofil, Infokategorie.Andrang);
        Erwarte(Infoart.Standzeit, Infokategorie.Andrang);
        Erwarte(Infoart.KeinWeg, Infokategorie.Mangel);
        Erwarte(Infoart.Gebuehrenwirkung, Infokategorie.Mangel);
        Erwarte(Infoart.Rang, Infokategorie.Platz);
        Erwarte(Infoart.Bilanz, Infokategorie.Platz);

        // Kein Wert des Enums darf durch das Raster fallen.
        foreach (Infoart art in Enum.GetValues(typeof(Infoart)))
        {
            if (art == Infoart.Keine) continue;
            if (Infokarten.KategorieVon(art) == Infokategorie.Keine)
                fehler.Add($"{art} hat keine Kategorie");
        }
    }

    private static void PruefeInfoMengenstufen(List<string> fehler)
    {
        void Erwarte(double anteil, Mengenstufe soll)
        {
            var ist = Infokarten.MengenstufeVon(anteil);
            if (ist != soll)
                fehler.Add($"Mengenstufe bei {anteil:F4}: {ist} statt {soll}");
        }

        // Die Staffel aus PLAN-Infokarten.md, Regel 4 - genau an den Kanten.
        Erwarte(1.00, Mengenstufe.Meisten);
        Erwarte(0.601, Mengenstufe.Meisten);
        Erwarte(0.60, Mengenstufe.Viele);
        Erwarte(0.25, Mengenstufe.Viele);
        Erwarte(0.2499, Mengenstufe.Einige);
        Erwarte(0.10, Mengenstufe.Einige);
        Erwarte(0.0999, Mengenstufe.Wenige);
        Erwarte(0.00, Mengenstufe.Wenige);
    }

    private static void PruefeInfoWegstufen(List<string> fehler)
    {
        void Erwarte(double meter, Wegstufe soll)
        {
            var ist = Infokarten.WegstufeVon(meter);
            if (ist != soll)
                fehler.Add($"Wegstufe bei {meter:F1} m: {ist} statt {soll}");
        }

        Erwarte(0.0, Wegstufe.Kurz);
        Erwarte(75.0, Wegstufe.Kurz);
        Erwarte(75.1, Wegstufe.Ueblich);
        Erwarte(150.0, Wegstufe.Ueblich);
        Erwarte(150.1, Wegstufe.Weit);
        Erwarte(400.0, Wegstufe.Weit);
    }

    private static int PruefeInfoZiehung(List<string> fehler)
    {
        // Volles Angebot: mehrere Infos je Kategorie, dazu Mangelmeldungen.
        var voll = new[]
        {
            Infoart.ZielRang, Infoart.Zweck, Infoart.Alter, Infoart.Bildung,
            Infoart.Fussweg, Infoart.Laufweite, Infoart.Erreichbar,
            Infoart.Tagesprofil, Infoart.Spitze, Infoart.Leerstand,
            Infoart.Rang, Infoart.Kosten, Infoart.Bilanz,
            Infoart.KeinWeg, Infoart.Zufahrt, Infoart.Ungleichgewicht,
        };

        var ziel = new Infoart[Infokarten.RotierendeKategorien.Length];
        var vorrunde = new List<Infoart>();
        var zufall = new Infozufall(20260907u);
        var reihenfolgen = new HashSet<string>();
        var gesehen = new HashSet<Infoart>();
        const int Durchlaeufe = 4000;

        for (var lauf = 0; lauf < Durchlaeufe; lauf++)
        {
            var anzahl = Infokarten.ZieheRunde(voll, vorrunde, ref zufall, ziel);
            var runde = ziel.Take(anzahl).ToList();

            if (anzahl != Infokarten.RotierendeKategorien.Length)
            {
                fehler.Add($"Runde {lauf}: {anzahl} Eintraege statt "
                    + Infokarten.RotierendeKategorien.Length);
                break;
            }

            foreach (var kategorie in Infokarten.RotierendeKategorien)
            {
                var treffer = runde.Count(
                    a => Infokarten.KategorieVon(a) == kategorie);
                if (treffer != 1)
                    fehler.Add($"Runde {lauf}: {kategorie} kommt {treffer}x vor");
            }

            foreach (var art in runde)
            {
                if (Infokarten.KategorieVon(art) == Infokategorie.Mangel)
                    fehler.Add($"Runde {lauf}: Mangelmeldung {art} rotiert mit");
                gesehen.Add(art);
            }

            /*
             * DIE SPERRE. Bei mehr als einer Info je Kategorie darf der
             * Eintrag der Vorrunde nicht noch einmal kommen. Faellt diese
             * Regel weg, geht genau diese Zeile rot - im Spiel saehe man
             * nur gelegentlich zweimal dieselbe Karte.
             */
            foreach (var kategorie in Infokarten.RotierendeKategorien)
            {
                var alt = Infokarten.EintragDerKategorie(vorrunde, kategorie);
                var neu = Infokarten.EintragDerKategorie(runde, kategorie);
                if (alt != Infoart.Keine && alt == neu)
                    fehler.Add($"Runde {lauf}: {kategorie} wiederholt {alt}");
            }

            reihenfolgen.Add(string.Join(",", runde.Select(a => (int)a)));
            vorrunde = runde;
        }

        // Jede angebotene Nicht-Mangel-Info muss auch wirklich drankommen.
        foreach (var art in voll)
        {
            if (Infokarten.KategorieVon(art) == Infokategorie.Mangel) continue;
            if (!gesehen.Contains(art))
                fehler.Add($"{art} wurde in {Durchlaeufe} Runden nie gezogen");
        }

        // Die Reihenfolge muss sich mischen, sonst ist sie vorhersehbar.
        if (reihenfolgen.Count < 8)
            fehler.Add($"nur {reihenfolgen.Count} verschiedene Reihenfolgen");

        /*
         * EINE EINZIGE INFO IN DER KATEGORIE MUSS SICH WIEDERHOLEN DUERFEN.
         * Sonst bliebe die Kategorie stumm - und ein stummer Platz ist
         * schlechter als ein wiederholter Satz.
         */
        var karg = new[] { Infoart.Zweck, Infoart.Fussweg };
        var kargVor = new List<Infoart>();
        var kargZufall = new Infozufall(4711u);
        for (var lauf = 0; lauf < 5; lauf++)
        {
            var anzahl = Infokarten.ZieheRunde(
                karg, kargVor, ref kargZufall, ziel);
            if (anzahl != 2)
            {
                fehler.Add($"karge Ziehung {lauf}: {anzahl} statt 2 Eintraege");
                break;
            }
            kargVor = ziel.Take(anzahl).ToList();
        }

        // Fehlt eine Kategorie ganz, wird sie uebersprungen - nicht gefuellt.
        var ohneLage = new[]
        {
            Infoart.Zweck, Infoart.Spitze, Infoart.Rang,
        };
        var ohneZufall = new Infozufall(99u);
        var ohneAnzahl = Infokarten.ZieheRunde(
            ohneLage, new List<Infoart>(), ref ohneZufall, ziel);
        if (ohneAnzahl != 3)
            fehler.Add($"ohne Lage-Info: {ohneAnzahl} statt 3 Eintraege");
        if (ziel.Take(ohneAnzahl).Any(
                a => Infokarten.KategorieVon(a) == Infokategorie.Lage))
            fehler.Add("ohne Lage-Info wurde trotzdem eine Lage-Info gezogen");

        // Gar kein Angebot: leere Runde, kein Absturz.
        var leerZufall = new Infozufall(1u);
        var leerAnzahl = Infokarten.ZieheRunde(
            new Infoart[0], new List<Infoart>(), ref leerZufall, ziel);
        if (leerAnzahl != 0)
            fehler.Add($"leeres Angebot: {leerAnzahl} statt 0 Eintraege");

        return Durchlaeufe;
    }

    /** Ein Parkplatz mit allem, was gemessen sein kann. */
    private static Infowerte VollerParkplatz()
    {
        return new Infowerte
        {
            Proben = 200,
            Kapazitaet = 263,
            BelegtPromille = 840,
            ZweckEinkaufen = 620, ZweckArbeiten = 180, ZweckNachHause = 60,
            ZweckFreizeit = 90, ZweckBesichtigen = 50,
            TouristPromille = 210,
            AlterKind = 20, AlterJugend = 90, AlterErwachsen = 700,
            AlterSenior = 190,
            BildungPromille = 610,
            Zufriedenheit = 72, ZufriedenheitStadt = 65,
            FusswegDezimeter = 1200,
            KeinWegPromille = 0,
            AutosGesamt = 41200, AutosLetzteWoche = 1840,
            StandzeitMinuten = 130,
            ZieleBekannt = 9,
            GebaeudeInLaufweite = 14,
            Zufahrten = 2,
            StundenMitWerten = 24,
            TageImRing = 7,
            RangInStadt = 3, LotsInStadt = 7,
            TeuersterJeBucht = true,
            GebautBekannt = true,
        };
    }

    private static void PruefeInfoVerfuegbarkeit(List<string> fehler)
    {
        var ziel = new Infoart[40];

        /*
         * FRISCH GEBAUT. Noch keine einzige Probe, keine Nachbarschaft
         * vermessen: die Karte darf NICHTS ueber Gaeste behaupten. Genau
         * hier entstuende sonst „Wenige gehen zu ..." aus null Beobachtungen.
         */
        var frisch = new Infowerte { Kapazitaet = 100, LotsInStadt = 1 };
        var n = Infoauswahl.SammleVerfuegbare(frisch, ziel);
        for (var i = 0; i < n; i++)
        {
            var k = Infokarten.KategorieVon(ziel[i]);
            if (k == Infokategorie.Gaeste)
                fehler.Add($"frischer Platz bietet Gaeste-Info {ziel[i]}");
            if (k == Infokategorie.Andrang)
                fehler.Add($"frischer Platz bietet Andrang-Info {ziel[i]}");
        }

        // Knapp unter der Schwelle zaehlt noch nicht als gemessen.
        var knapp = VollerParkplatz();
        knapp.Proben = Infoauswahl.ProbenFuerAussage - 1;
        var knappN = Infoauswahl.SammleVerfuegbare(knapp, ziel);
        for (var i = 0; i < knappN; i++)
        {
            if (Infokarten.KategorieVon(ziel[i]) == Infokategorie.Gaeste)
                fehler.Add($"unter der Probenschwelle kommt {ziel[i]}");
        }

        // Volles Angebot: jede Kategorie muss etwas hergeben.
        var voll = VollerParkplatz();
        var vollN = Infoauswahl.SammleVerfuegbare(voll, ziel);
        foreach (var kategorie in Infokarten.RotierendeKategorien)
        {
            var da = false;
            for (var i = 0; i < vollN; i++)
                if (Infokarten.KategorieVon(ziel[i]) == kategorie) da = true;
            if (!da)
                fehler.Add($"voller Platz bietet nichts fuer {kategorie}");
        }

        /*
         * MAENGEL GEHOEREN NIE IN DIE ROTATION.
         *
         * Sie stehen fest unter den Karten. Rutschte eine hier hinein,
         * blinkte eine Warnung alle sechzig Sekunden fuer fuenfzehn auf.
         */
        var kaputt = VollerParkplatz();
        kaputt.KeinWegPromille = 300;
        kaputt.Zufahrten = 1;
        kaputt.GebuehrenwirkungBekannt = true;
        kaputt.LeererNachbarBekannt = true;
        var kaputtN = Infoauswahl.SammleVerfuegbare(kaputt, ziel);
        for (var i = 0; i < kaputtN; i++)
        {
            if (Infokarten.KategorieVon(ziel[i]) == Infokategorie.Mangel)
                fehler.Add($"Mangel {ziel[i]} steckt in der Rotation");
        }

        // Das Angebot muss sich auch ziehen lassen.
        var zufall = new Infozufall(7u);
        var runde = new Infoart[Infokarten.RotierendeKategorien.Length];
        var liste = new List<Infoart>();
        for (var i = 0; i < vollN; i++) liste.Add(ziel[i]);
        if (Infokarten.ZieheRunde(liste, new List<Infoart>(), ref zufall, runde)
            != Infokarten.RotierendeKategorien.Length)
            fehler.Add("volles Angebot ergibt keine volle Runde");
    }

    private static void PruefeInfoMangel(List<string> fehler)
    {
        void Erwarte(string was, Infowerte w, Infoart soll)
        {
            var ist = Infoauswahl.WaehleMangel(w);
            if (ist != soll) fehler.Add($"Mangel bei {was}: {ist} statt {soll}");
        }

        Erwarte("gesundem Platz", VollerParkplatz(), Infoart.Keine);

        var tot = VollerParkplatz();
        tot.AutosLetzteWoche = 0;
        Erwarte("totem Platz", tot, Infoart.Tot);

        var stecken = VollerParkplatz();
        stecken.KeinWegPromille = 150;
        Erwarte("steckengebliebenen Gaesten", stecken, Infoart.KeinWeg);

        /*
         * DIE RANGFOLGE IST DER PUNKT. Ein toter Platz und ein
         * Ausfahrtsproblem gleichzeitig: gemeldet wird der tote Platz.
         */
        var beides = VollerParkplatz();
        beides.AutosLetzteWoche = 0;
        beides.KeinWegPromille = 500;
        beides.Zufahrten = 1;
        Erwarte("totem Platz mit Ausfahrtsproblem", beides, Infoart.Tot);

        var eng = VollerParkplatz();
        eng.Zufahrten = 1;
        Erwarte("einer Zufahrt", eng, Infoart.Zufahrt);

        // Eine einzelne Zufahrt an einem kleinen Platz ist kein Mangel.
        var klein = VollerParkplatz();
        klein.Zufahrten = 1;
        klein.Kapazitaet = 40;
        Erwarte("kleinem Platz mit einer Zufahrt", klein, Infoart.Keine);
    }

    private static void PruefeInfoStaerkste(List<string> fehler)
    {
        var w = VollerParkplatz();

        var zweck = Infoauswahl.StaerksterZweck(w, out var zweckAnteil);
        if (zweck != Zweckstufe.Einkaufen)
            fehler.Add($"staerkster Zweck: {zweck} statt Einkaufen");
        if (zweckAnteil != 620)
            fehler.Add($"Zweckanteil: {zweckAnteil} statt 620");
        if (Infokarten.MengenstufeVon(zweckAnteil / 1000.0) != Mengenstufe.Meisten)
            fehler.Add("620 Promille ergeben nicht die Stufe Meisten");

        var alter = Infoauswahl.StaerksteAltersgruppe(w, out var alterAnteil);
        if (alter != Altersstufe.Erwachsen)
            fehler.Add($"staerkste Altersgruppe: {alter} statt Erwachsen");
        if (alterAnteil != 700)
            fehler.Add($"Altersanteil: {alterAnteil} statt 700");

        var senioren = VollerParkplatz();
        senioren.AlterErwachsen = 300;
        senioren.AlterSenior = 600;
        if (Infoauswahl.StaerksteAltersgruppe(senioren, out _) != Altersstufe.Senior)
            fehler.Add("Seniorenplatz wird nicht erkannt");
    }
}
