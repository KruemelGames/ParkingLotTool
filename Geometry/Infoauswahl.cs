namespace ParkingLotTool.Geometry
{
    /**
     * Alles, was eine Infokarte ueber einen Parkplatz braucht - als reine
     * Zahlen.
     *
     * BEWUSST OHNE CS2-TYPEN. Das Testprojekt uebersetzt nur `Geometry/` und
     * kennt weder `ParkingLotStatistik` noch `Entity`. Die Bruecke fuellt
     * `Tools/ParkingLotListeUISystem.cs`. Damit bleibt die Frage „welche
     * Karte darf ueberhaupt erscheinen" pruefbar - und das ist die Frage, an
     * der sonst still eine leere Karte entsteht.
     */
    public struct Infowerte
    {
        /** Wie oft der Parkplatz schon abgetastet wurde. */
        public int Proben;

        public int Kapazitaet;
        public int BelegtPromille;

        public int ZweckEinkaufen;
        public int ZweckArbeiten;
        public int ZweckNachHause;
        public int ZweckFreizeit;
        public int ZweckBesichtigen;
        public int TouristPromille;

        public int AlterKind;
        public int AlterJugend;
        public int AlterErwachsen;
        public int AlterSenior;

        public int BildungPromille;
        public int Zufriedenheit;
        public int ZufriedenheitStadt;

        public int FusswegDezimeter;
        public int KeinWegPromille;

        public int AutosGesamt;
        public int AutosLetzteWoche;
        public int StandzeitMinuten;

        /** Wie viele Zielgebaeude die Rangliste kennt. */
        public int ZieleBekannt;

        /** Gebaeude in Laufweite, aus der Umgebung gezaehlt. */
        public int GebaeudeInLaufweite;

        public int Zufahrten;

        /** Wie viele der vierundzwanzig Stunden schon Messwerte haben. */
        public int StundenMitWerten;

        /** Wie viele Tage der Ring traegt. */
        public int TageImRing;

        /** Platz in der Groessenrangliste der Stadt; 1 ist der groesste. */
        public int RangInStadt;
        public int LotsInStadt;

        public bool TeuersterJeBucht;
        public bool GuenstigsterJeBucht;

        /** Baudatum bekannt? Bei Altbestand steht es nicht im Spielstand. */
        public bool GebautBekannt;

        /** Gebuehr wurde geaendert und es liegen Werte von davor vor. */
        public bool GebuehrenwirkungBekannt;

        /** Es gibt einen anderen eigenen Parkplatz, der deutlich leerer ist. */
        public bool LeererNachbarBekannt;
    }

    public static class Infoauswahl
    {
        /**
         * Ab so vielen Proben traut sich eine Karte eine Aussage zu.
         *
         * Darunter waere jede Mengenstufe Zufall: bei zwei beobachteten Autos
         * sind „die meisten" eben ein einziger Fahrer.
         */
        public const int ProbenFuerAussage = 5;

        /** Ab so vielen Stunden mit Werten lohnt sich ein Tagesprofil. */
        public const int StundenFuerProfil = 12;

        /** Ab so vielen Tagen im Ring lohnt sich ein Wochenwert. */
        public const int TageFuerWoche = 3;

        /**
         * Sammelt, welche Infos dieser Parkplatz gerade tragen kann.
         *
         * Eine Info fehlt hier, wenn ihre Grundlage fehlt - nicht, wenn ihr
         * Wert klein ist. „Wenige gehen zu X" ist eine gueltige Aussage,
         * „gar nichts gemessen" ist keine.
         *
         * Rueckgabe: Anzahl der beschriebenen Eintraege in `ziel`.
         */
        public static int SammleVerfuegbare(in Infowerte w, Infoart[] ziel)
        {
            if (ziel == null) return 0;
            var n = 0;

            void Nimm(Infoart art)
            {
                if (n < ziel.Length) ziel[n++] = art;
            }

            var gemessen = w.Proben >= ProbenFuerAussage;

            // A - Wer hier parkt
            if (gemessen && w.ZieleBekannt > 0) Nimm(Infoart.ZielRang);
            if (gemessen && HatZweck(w)) Nimm(Infoart.Zweck);
            if (gemessen && w.ZweckNachHause > 0) Nimm(Infoart.Wohnparkplatz);
            if (gemessen && w.TouristPromille > 0) Nimm(Infoart.Touristen);
            if (gemessen && HatAltersgruppe(w)) Nimm(Infoart.Alter);
            if (gemessen && w.BildungPromille > 0) Nimm(Infoart.Bildung);
            if (gemessen && w.Zufriedenheit > 0 && w.ZufriedenheitStadt > 0)
                Nimm(Infoart.Zufriedenheit);

            // B - Lage und Fusswege
            if (gemessen && w.FusswegDezimeter > 0 && w.ZieleBekannt > 0)
                Nimm(Infoart.Fussweg);
            if (w.GebaeudeInLaufweite > 0) Nimm(Infoart.Laufweite);
            if (w.GebaeudeInLaufweite > 0) Nimm(Infoart.Erreichbar);

            // C - Andrang
            if (w.StundenMitWerten >= StundenFuerProfil)
            {
                Nimm(Infoart.Tagesprofil);
                Nimm(Infoart.Spitze);
            }
            if (w.TageImRing >= TageFuerWoche)
            {
                Nimm(Infoart.Durchsatz);
                if (w.BelegtPromille > 0) Nimm(Infoart.Dauerlast);
            }
            if (gemessen && w.Kapazitaet > 0) Nimm(Infoart.Leerstand);
            if (w.StandzeitMinuten > 0) Nimm(Infoart.Standzeit);

            // E - Ueber den Platz selbst
            if (w.RangInStadt > 0 && w.LotsInStadt > 1) Nimm(Infoart.Rang);
            if (w.TeuersterJeBucht || w.GuenstigsterJeBucht) Nimm(Infoart.Kosten);
            if (w.GebautBekannt && w.AutosGesamt > 0) Nimm(Infoart.Bilanz);

            return n;
        }

        /**
         * Die eine feststehende Mangelmeldung - oder keine.
         *
         * Nur EINE, und die dringlichste zuerst: ein Platz, den niemand
         * benutzt, hat ein groesseres Problem als einer mit einer knappen
         * Ausfahrt. Zwei Warnungen nebeneinander liest ohnehin niemand.
         */
        public static Infoart WaehleMangel(in Infowerte w)
        {
            var gemessen = w.Proben >= ProbenFuerAussage;

            if (w.TageImRing >= TageFuerWoche && w.AutosLetzteWoche == 0)
                return Infoart.Tot;
            if (gemessen && w.KeinWegPromille >= 100) return Infoart.KeinWeg;
            if (w.GebuehrenwirkungBekannt) return Infoart.Gebuehrenwirkung;
            if (gemessen && w.BelegtPromille >= 900 && w.LeererNachbarBekannt)
                return Infoart.Ungleichgewicht;
            if (w.Zufahrten == 1 && w.Kapazitaet >= 150) return Infoart.Zufahrt;
            return Infoart.Keine;
        }

        /**
         * Welcher Zweck ueberwiegt, und mit welchem Anteil.
         *
         * Das Wort ist das EINZIGE, was sich am Satz aendert - nicht der
         * Satzbau. Siehe PLAN-Infokarten.md, Regel 3.
         */
        public static Zweckstufe StaerksterZweck(in Infowerte w,
                                                 out int promille)
        {
            promille = w.ZweckEinkaufen;
            var stufe = Zweckstufe.Einkaufen;
            if (w.ZweckArbeiten > promille)
            { promille = w.ZweckArbeiten; stufe = Zweckstufe.Arbeiten; }
            if (w.ZweckFreizeit > promille)
            { promille = w.ZweckFreizeit; stufe = Zweckstufe.Entspannen; }
            if (w.ZweckBesichtigen > promille)
            { promille = w.ZweckBesichtigen; stufe = Zweckstufe.Besichtigen; }
            return stufe;
        }

        /** Welche Altersgruppe ueberwiegt, und mit welchem Anteil. */
        public static Altersstufe StaerksteAltersgruppe(in Infowerte w,
                                                        out int promille)
        {
            promille = w.AlterErwachsen;
            var stufe = Altersstufe.Erwachsen;
            if (w.AlterSenior > promille)
            { promille = w.AlterSenior; stufe = Altersstufe.Senior; }
            if (w.AlterJugend > promille)
            { promille = w.AlterJugend; stufe = Altersstufe.Jugend; }
            if (w.AlterKind > promille)
            { promille = w.AlterKind; stufe = Altersstufe.Kind; }
            return stufe;
        }

        private static bool HatZweck(in Infowerte w)
            => w.ZweckEinkaufen > 0 || w.ZweckArbeiten > 0
               || w.ZweckFreizeit > 0 || w.ZweckBesichtigen > 0;

        private static bool HatAltersgruppe(in Infowerte w)
            => w.AlterKind > 0 || w.AlterJugend > 0
               || w.AlterErwachsen > 0 || w.AlterSenior > 0;
    }

    /** Eingesetztes Wort fuer „{Mengenwort} sind zum … hier". */
    public enum Zweckstufe
    {
        Einkaufen = 0,
        Arbeiten = 1,
        Entspannen = 2,
        Besichtigen = 3,
    }

    /** Eingesetztes Wort fuer „{Mengenwort} Gaeste sind …". */
    public enum Altersstufe
    {
        Kind = 0,
        Jugend = 1,
        Erwachsen = 2,
        Senior = 3,
    }
}
