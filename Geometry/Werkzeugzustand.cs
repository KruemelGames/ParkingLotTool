using System;

namespace ParkingLotTool.Geometry
{
    /** Die Reiter des Panels. */
    public enum Werkzeugreiter
    {
        /** „Draft" - hier und nur hier wird der Umriss bearbeitet. */
        Entwurf,
        Zoning,
        /** „Parking lots" - reine Liste gebauter Parkplaetze. */
        Liste,
        Debug,
        Melden,
    }

    /** Was der naechste Weltklick bedient. */
    public enum Werkzeugmodus
    {
        /** Umriss zeichnen und aendern. */
        Grund,
        /** Zugaenge setzen, ziehen, entfernen. */
        Zugang,
        /** Bezugslinie, Teilflaeche oder Trennschnitt waehlen. */
        Ausrichten,
        Zoningflaeche,
        Zoningseite,
        Meldemarke,
    }

    /**
     * WAS EIN KLICK IN DER WELT TUN KANN - IN KATEGORIEN, NICHT IN FUNKTIONEN.
     *
     * Ansage des Nutzers am 2026-09-09: *"Das ganze was wir gerade machen
     * soll nur kategorisch sein, nicht auf jede einzelne Funktion."*
     *
     * Der erste Entwurf zerlegte den Umriss in fuenf Eintraege - Punkt
     * setzen, ziehen, einfuegen, Kante ziehen, ausstuelpen. Das ist genau das
     * Raster, in dem eine Funktion vergessen wird: wer eine sechste
     * Umrisshandlung ergaenzt und den Eintrag nicht mitpflegt, hat wieder
     * eine offene Stelle. Umrissarbeit ist EINE Sache.
     *
     * Dasselbe gilt fuer die Zugaenge: Zufahrt, Fussweg, Einfahrt und
     * Ausfahrt sind eine Kategorie. Die gewaehlte Art entscheidet nur, was
     * ein NEUER Klick erzeugt - *"wenn ich Einfahrt angeklickt habe, dann
     * will ich weiterhin Ausfahrt loeschen oder verschieben"*.
     */
    public enum Weltarbeit
    {
        /** Punkte setzen, ziehen, einfuegen; Kanten ziehen und ausstuelpen. */
        Umriss,
        /** Zugaenge setzen, ziehen, entfernen - alle vier Arten. */
        Zugang,
        Zoningflaeche,
        Zoningseite,
        Ausrichtwahl,
        Meldemarke,
        SchrittZurueck,
        Rueckgaengig,
        Bauen,
        /** Bauzettel schreiben - aendert nichts und ist deshalb ueberall frei. */
        Abzug,
    }

    /**
     * EIN KLICK HAT GENAU EINEN BESITZER, UND DER STEHT FEST.
     *
     * Ansage des Nutzers am 2026-09-09:
     *
     *   *"Also z.B. bin ich im Reiter 'Parkplaetze', kann ich weiter bauen,
     *   was sinnlos ist. Waehrend ich gerade Zugaenge baue, dann kann ich die
     *   Polygonpunkte verschieben. … Das sind all so Dinge, die das Arbeiten
     *   mit dem Mod nervig machen."*
     *
     * Bis dahin verteilte `ParkingLotToolSystem.OnUpdate` die Klicks ueber
     * eine feste Kette - Ausrichten, Zoningseiten, Zoning, Zugaenge, Marken,
     * Zuege, Polygon - und fragte den REITER an keiner Stelle. Wer in der
     * Kette zuerst stand, bekam den Klick; alles darunter blieb offen. Nur
     * `SetTab` schaltete beim Zoning-Reiter etwas, mit der Begruendung, die
     * dort schon steht: *"Ein Modus, der einen Reiter ueberlebt, ist eine
     * Falle."*
     *
     * Diese Tabelle ist die Antwort darauf. Sie liegt in `Geometry/`, weil
     * das Testprojekt nur diesen Ordner uebersetzt - `Tools/` ist von keinem
     * Lauf gedeckt. Die Klickkette fragt hier nach, statt sich auf ihre
     * eigene Reihenfolge zu verlassen.
     *
     * ENTSCHEIDUNGEN DES NUTZERS am 2026-09-09, woertlich erfragt:
     * Umrisspunkte ziehen im Zoning-Reiter? *"Nein weil wir nicht in Draft
     * sind."* Im Debug-Reiter ziehen? *"Ne."* Im Debug-Reiter bauen?
     * *"Ne."* Daraus wurde die Regel, die die drei Einzelfaelle ersetzt:
     * **Umrissarbeit gehoert in den Entwurfsreiter, sonst nirgends.**
     */
    public static class Werkzeugzustand
    {
        /** Verwaltung zeigt nur gebaute Lots, keinen noch offenen Bauentwurf. */
        public static bool ZeigtWerkzeugvorschau(Werkzeugreiter reiter)
            => reiter != Werkzeugreiter.Liste;

        /** Die Zeichenhilfe darf keine Handlung anbieten, die gesperrt ist. */
        public static bool ZeigtUmrisshilfe(Werkzeugreiter reiter, Werkzeugmodus modus)
            => Erlaubt(reiter, modus, Weltarbeit.Umriss);

        /**
         * Darf diese Arbeit im REITER ueberhaupt vorkommen?
         *
         * Der Reiter entscheidet grob, der Modus fein. Beides muss zustimmen.
         */
        public static bool ReiterErlaubt(Werkzeugreiter reiter, Weltarbeit arbeit)
        {
            // Ein Abzug aendert nichts und ist ueberall frei.
            if (arbeit == Weltarbeit.Abzug) return true;

            switch (reiter)
            {
                case Werkzeugreiter.Entwurf:
                    // Alles ausser dem, was anderen Reitern gehoert.
                    return arbeit != Weltarbeit.Zoningflaeche
                        && arbeit != Weltarbeit.Zoningseite
                        && arbeit != Weltarbeit.Meldemarke;

                case Werkzeugreiter.Zoning:
                    /*
                     * Kein Umriss - "weil wir nicht in Draft sind".
                     *
                     * UND KEIN SCHRITT ZURUECK. Hier stand er erlaubt, mit
                     * dem Kommentar "ein Rechtsklick nimmt dort den letzten
                     * Zoningschritt zurueck" - behauptet, nicht nachgesehen.
                     * `StepBack` nimmt keinen Zoningschritt zurueck: am
                     * geschlossenen, seit dem Schliessen unberuehrten Umriss
                     * OEFFNET es ihn wieder. Befund des Nutzers am
                     * 2026-09-09: *"Rechtsklick beim Zoning fuehrt immer noch
                     * dazu, dass das Polygon aufgeloest wird."*
                     *
                     * Die Zoninghandler nehmen ihre eigenen Schritte selbst
                     * zurueck und verbrauchen den Klick dabei. Faellt er
                     * durch, gehoert er niemandem - und darf schon gar nicht
                     * den Umriss zerlegen.
                     *
                     * Rueckgaengig (Strg+Z) und Bauen bleiben: der
                     * Zoning-Reiter ist Teil desselben Entwurfs.
                     */
                    return arbeit == Weltarbeit.Zoningflaeche
                        || arbeit == Weltarbeit.Zoningseite
                        || arbeit == Weltarbeit.Rueckgaengig
                        || arbeit == Weltarbeit.Bauen;

                case Werkzeugreiter.Liste:
                    // Reine Anzeige. Kein Klick darf hier etwas aendern.
                    return false;

                case Werkzeugreiter.Debug:
                    // Messen, nicht bauen. Rueckgaengig bleibt, damit man
                    // sich aus einem Messversuch wieder herausbewegen kann.
                    return arbeit == Weltarbeit.Rueckgaengig;

                case Werkzeugreiter.Melden:
                    // Nur Marken. Bauen und Rueckgaengig gehoeren nicht in
                    // einen Fehlerbericht.
                    return arbeit == Weltarbeit.Meldemarke;
            }
            return false;
        }

        /**
         * Darf diese Arbeit im MODUS vorkommen?
         *
         * Ein Modus bedient, was er bedient - und sperrt den Rest. Kein
         * "wer naeher liegt": genau das liess im Zugangsmodus die
         * Polygonpunkte greifbar, waehrend die Kanten schon gesperrt waren.
         */
        public static bool ModusErlaubt(Werkzeugmodus modus, Weltarbeit arbeit)
        {
            if (arbeit == Weltarbeit.Abzug) return true;
            // Diese drei haengen am Werkzeug, nicht am Modus.
            if (arbeit == Weltarbeit.SchrittZurueck
                || arbeit == Weltarbeit.Rueckgaengig
                || arbeit == Weltarbeit.Bauen) return true;

            switch (modus)
            {
                case Werkzeugmodus.Grund:
                    return arbeit == Weltarbeit.Umriss;

                case Werkzeugmodus.Zugang:
                    return arbeit == Weltarbeit.Zugang;

                case Werkzeugmodus.Ausrichten:
                    return arbeit == Weltarbeit.Ausrichtwahl;

                case Werkzeugmodus.Zoningflaeche:
                    return arbeit == Weltarbeit.Zoningflaeche;

                case Werkzeugmodus.Zoningseite:
                    return arbeit == Weltarbeit.Zoningseite;

                case Werkzeugmodus.Meldemarke:
                    return arbeit == Weltarbeit.Meldemarke;
            }
            return false;
        }

        /** Beides muss zustimmen. */
        public static bool Erlaubt(Werkzeugreiter reiter, Werkzeugmodus modus,
            Weltarbeit arbeit)
            => ReiterErlaubt(reiter, arbeit) && ModusErlaubt(modus, arbeit);

        /**
         * Passt der Modus ueberhaupt zum Reiter?
         *
         * Ein Modus, dessen eigene Arbeit der Reiter verbietet, ist eine
         * Falle - der naechste Klick ginge ins Leere.
         */
        public static bool ModusPasstZuReiter(Werkzeugreiter reiter,
            Werkzeugmodus modus)
            // Der Grundzustand passt ueberall: er ist das Fehlen eines
            // Modus, nicht selbst einer. In der Liste und im Debug-Reiter
            // heisst er schlicht "der Klick gehoert niemandem".
            => modus == Werkzeugmodus.Grund
                || ReiterErlaubt(reiter, EigeneArbeit(modus));

        /** Die Arbeit, für die ein Modus da ist. */
        public static Weltarbeit EigeneArbeit(Werkzeugmodus modus)
        {
            switch (modus)
            {
                case Werkzeugmodus.Zugang: return Weltarbeit.Zugang;
                case Werkzeugmodus.Ausrichten: return Weltarbeit.Ausrichtwahl;
                case Werkzeugmodus.Zoningflaeche: return Weltarbeit.Zoningflaeche;
                case Werkzeugmodus.Zoningseite: return Weltarbeit.Zoningseite;
                case Werkzeugmodus.Meldemarke: return Weltarbeit.Meldemarke;
                default: return Weltarbeit.Umriss;
            }
        }

        /**
         * WELCHER MODUS GILT NACH EINEM REITERWECHSEL.
         *
         * Kein Modus ueberlebt einen Reiterwechsel. Der Zoning-Reiter bringt
         * seinen eigenen mit - das tat er schon vorher, auf Wunsch des
         * Nutzers: *"'Place Patches' sollte automatisch an sein, wenn ich in
         * den Reiter Zoning gehe."* Der Melde-Reiter ebenso. Alle anderen
         * fallen auf den Grundzustand zurueck.
         */
        public static Werkzeugmodus ModusBeimReiter(Werkzeugreiter reiter)
        {
            switch (reiter)
            {
                case Werkzeugreiter.Zoning: return Werkzeugmodus.Zoningflaeche;
                case Werkzeugreiter.Melden: return Werkzeugmodus.Meldemarke;
                default: return Werkzeugmodus.Grund;
            }
        }

        /**
         * WAS ESCAPE TUT.
         *
         * Escape stuft ab, statt sofort ganz auszusteigen: erst den Modus
         * verlassen, dann das Werkzeug. Bis zum 2026-09-09 beendete Escape
         * aus jedem Modus heraus gleich das ganze Werkzeug.
         *
         * Gibt `true` zurueck, wenn nur der Modus faellt; `false` heisst
         * "das Werkzeug schliessen".
         */
        public static bool EscapeStuftAb(Werkzeugreiter reiter,
            Werkzeugmodus modus, out Werkzeugmodus danach)
        {
            danach = ModusBeimReiter(reiter);
            return modus != danach;
        }
    }
}
