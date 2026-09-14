using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    /**
     * Welche Infokarte wann erscheint.
     *
     * Die Regeln stehen ausformuliert in PLAN-Infokarten.md. Hier liegt nur
     * das Rechnende daran, und zwar bewusst in `Geometry/`: das Testprojekt
     * uebersetzt ausschliesslich diesen Ordner. Alles unter `Tools/` ist von
     * keinem Pflichtlauf gedeckt, und die Ziehungsregel ist genau die Sorte
     * stille Logik, die sonst jahrelang halb falsch laeuft.
     */
    public enum Infokategorie
    {
        Keine = 0,
        Gaeste,
        Lage,
        Andrang,
        Platz,

        /**
         * Maengel rotieren NICHT.
         *
         * Ein Parkplatz, den niemand erreicht, darf nicht alle sechzig
         * Sekunden fuer fuenfzehn aufblitzen. Trifft eine Mangelmeldung zu,
         * steht sie fest unter den wechselnden Infos.
         */
        Mangel,
    }

    public enum Infoart
    {
        Keine = 0,

        // A - Wer hier parkt
        ZielRang = 10,
        Zweck = 11,
        Wohnparkplatz = 12,
        Touristen = 13,
        Alter = 14,
        Bildung = 15,
        Zufriedenheit = 16,
        Stammgaeste = 17,

        // B - Lage und Fusswege
        Fussweg = 30,
        Laufweite = 31,
        Erreichbar = 32,

        // C - Andrang
        Tagesprofil = 50,
        Spitze = 51,
        Woche = 52,
        Dauerlast = 53,
        Leerstand = 54,
        Durchsatz = 55,
        Rekord = 56,
        Standzeit = 57,

        // D - Wenn etwas nicht stimmt
        KeinWeg = 70,
        Tot = 71,
        Zufahrt = 72,
        Ungleichgewicht = 73,
        Gebuehrenwirkung = 74,

        // E - Ueber den Platz selbst
        Rang = 90,
        Kosten = 91,
        Bilanz = 92,
    }

    /** „Die meisten" / „Viele" / „Einige" / „Wenige" statt Prozentzahlen. */
    public enum Mengenstufe
    {
        Wenige = 0,
        Einige = 1,
        Viele = 2,
        Meisten = 3,
    }

    /** Das eingesetzte Urteilswort hinter einer Fusswegangabe. */
    public enum Wegstufe
    {
        Kurz = 0,
        Ueblich = 1,
        Weit = 2,
    }

    /**
     * Wiederholbarer Zufall.
     *
     * Bewusst kein `System.Random`: die Ziehung muss in einer Pruefung mit
     * derselben Saat dasselbe Ergebnis liefern, sonst laesst sich die
     * Sperrregel nicht gegenpruefen. Xorshift32, mehr braucht es nicht.
     */
    public struct Infozufall
    {
        private uint _zustand;

        public Infozufall(uint saat)
        {
            // Null wuerde bei Xorshift auf ewig Null bleiben.
            _zustand = saat == 0u ? 2463534242u : saat;
        }

        /** Gleichverteilt in [0, grenze). Bei grenze <= 1 immer 0. */
        public int Naechste(int grenze)
        {
            if (grenze <= 1) return 0;
            _zustand ^= _zustand << 13;
            _zustand ^= _zustand >> 17;
            _zustand ^= _zustand << 5;
            return (int)(_zustand % (uint)grenze);
        }
    }

    public static class Infokarten
    {
        /**
         * Die Kategorien, aus denen eine Runde zieht - in dieser Reihenfolge
         * gesammelt, danach gemischt.
         *
         * `Mangel` fehlt hier absichtlich; siehe Kommentar am Enum.
         */
        public static readonly Infokategorie[] RotierendeKategorien =
        {
            Infokategorie.Gaeste,
            Infokategorie.Lage,
            Infokategorie.Andrang,
            Infokategorie.Platz,
        };

        public static Infokategorie KategorieVon(Infoart art)
        {
            var wert = (int)art;
            if (art == Infoart.Keine) return Infokategorie.Keine;
            if (wert < 30) return Infokategorie.Gaeste;
            if (wert < 50) return Infokategorie.Lage;
            if (wert < 70) return Infokategorie.Andrang;
            if (wert < 90) return Infokategorie.Mangel;
            return Infokategorie.Platz;
        }

        /**
         * Staffel aus PLAN-Infokarten.md, Regel 4.
         *
         * ueber 60 % - „Die meisten" | 25 bis 60 % - „Viele"
         * 10 bis 25 % - „Einige"     | darunter    - „Wenige"
         */
        public static Mengenstufe MengenstufeVon(double anteil)
        {
            if (anteil > 0.60) return Mengenstufe.Meisten;
            if (anteil >= 0.25) return Mengenstufe.Viele;
            if (anteil >= 0.10) return Mengenstufe.Einige;
            return Mengenstufe.Wenige;
        }

        /**
         * Urteil ueber einen Fussweg in Metern.
         *
         * Die Schwellen sind gesetzt, nicht gemessen: 75 m sind etwa eine
         * Strassenbreite plus Vorplatz, ab 150 m weicht in CS2 spuerbar
         * Verkehr auf den Strassenrand aus.
         */
        public static Wegstufe WegstufeVon(double meter)
        {
            if (meter <= 75.0) return Wegstufe.Kurz;
            if (meter <= 150.0) return Wegstufe.Ueblich;
            return Wegstufe.Weit;
        }

        /**
         * Zieht eine Runde: je eine Info aus jeder rotierenden Kategorie.
         *
         * `vorrunde` ist die zuletzt gezogene Runde. Deren Eintrag ist je
         * Kategorie gesperrt, damit nie zweimal hintereinander dasselbe
         * kommt - AUSSER die Kategorie hat nur diese eine Info. Dann waere
         * die Alternative, die Kategorie stumm zu schalten, und ein stummer
         * Platz ist schlechter als ein wiederholter Satz.
         *
         * Rueckgabe: Anzahl der beschriebenen Eintraege in `ziel`.
         * `ziel` muss mindestens `RotierendeKategorien.Length` fassen.
         */
        public static int ZieheRunde(IReadOnlyList<Infoart> verfuegbar,
                                     IReadOnlyList<Infoart> vorrunde,
                                     ref Infozufall zufall,
                                     Infoart[] ziel)
        {
            if (ziel == null) return 0;

            var anzahl = 0;
            var auswahl = new List<Infoart>();

            for (var k = 0; k < RotierendeKategorien.Length; k++)
            {
                if (anzahl >= ziel.Length) break;
                var kategorie = RotierendeKategorien[k];

                auswahl.Clear();
                if (verfuegbar != null)
                {
                    for (var i = 0; i < verfuegbar.Count; i++)
                    {
                        if (KategorieVon(verfuegbar[i]) == kategorie)
                            auswahl.Add(verfuegbar[i]);
                    }
                }
                if (auswahl.Count == 0) continue;

                // Sperre der Vorrunde - nur, wenn danach etwas uebrig bleibt.
                var gesperrt = EintragDerKategorie(vorrunde, kategorie);
                if (gesperrt != Infoart.Keine && auswahl.Count > 1)
                    auswahl.Remove(gesperrt);

                ziel[anzahl++] = auswahl[zufall.Naechste(auswahl.Count)];
            }

            Mische(ziel, anzahl, ref zufall);
            return anzahl;
        }

        /** Was in dieser Runde fuer die Kategorie gezogen wurde. */
        public static Infoart EintragDerKategorie(IReadOnlyList<Infoart> runde,
                                                  Infokategorie kategorie)
        {
            if (runde == null) return Infoart.Keine;
            for (var i = 0; i < runde.Count; i++)
            {
                if (KategorieVon(runde[i]) == kategorie) return runde[i];
            }
            return Infoart.Keine;
        }

        /**
         * Fisher-Yates ueber die ersten `anzahl` Eintraege.
         *
         * Ohne diesen Schritt kaeme immer erst „Wer hier parkt", dann „Lage",
         * dann „Andrang" - nach zwei Sitzungen weiss man nach fuenfzehn
         * Sekunden schon, was als naechstes kommt.
         */
        private static void Mische(Infoart[] feld, int anzahl,
                                   ref Infozufall zufall)
        {
            for (var i = anzahl - 1; i > 0; i--)
            {
                var j = zufall.Naechste(i + 1);
                (feld[i], feld[j]) = (feld[j], feld[i]);
            }
        }
    }
}
