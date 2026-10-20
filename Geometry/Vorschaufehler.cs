using System;

namespace ParkingLotTool.Geometry
{
    /**
     * WAS DER SPIELER LIEST, WENN DIE VORSCHAU SCHEITERT.
     *
     * Bis 2026-09-29 stand im Status der rohe Ausnahmetext des Rechenkerns:
     * "Preview aborted: No parking module fits inside the inner contour.
     * Ctrl+Enter writes a geometry dump." Ein Spieler hielt das fuer einen
     * haengenden Zustand - er wusste nicht, was die "inner contour" ist, und
     * dass seine Form mit Randstrassen schlicht zu klein war. Andere
     * Kerntexte sind deutsch ("Eine Teilungsgerade braucht eine Richtung.")
     * und erschienen so auch im englischen Spiel.
     *
     * Deshalb: jede Ausnahme wird einer Ursache zugeordnet, die der Spieler
     * selbst beheben kann, und der Status sagt, WAS er tun soll. Der rohe
     * Text bleibt im Log (`PLT-Vorschau konnte nicht berechnet werden.`).
     */
    public static class Vorschaufehler
    {
        public enum Ursache
        {
            ZuKlein,
            UmrissUngueltig,
            Zufahrt,
            Zeitgrenze,
            Unbekannt,
        }

        public static Ursache Einordnen(Exception exception)
        {
            while (exception?.InnerException != null)
                exception = exception.InnerException;
            return Einordnen(exception?.Message);
        }

        public static Ursache Einordnen(string meldung)
        {
            if (string.IsNullOrWhiteSpace(meldung)) return Ursache.Unbekannt;
            bool Hat(string teil) => meldung.IndexOf(teil,
                StringComparison.OrdinalIgnoreCase) >= 0;

            // Innenrand verschwindet oder kein Modul passt: die Form ist fuer
            // Randabstand, Randstrasse und eine Buchtreihe zu klein/zu spitz.
            if (Hat("No parking module fits") || Hat("No sub-area could be built")
                || Hat("does not intersect the inner contour")
                || Hat("The inset boundary leaves the site")
                || (Hat("The boundary inset by") && (Hat("has no edge left")
                    || Hat("is not counter-clockwise")))
                // Kurvenrandreihe (Etappe 6): Bogen zu eng fuer eine ganze Bucht
                // samt Fahrbahn - wie "zu klein" (Nutzer 18.10.: zu eng -> Fehler mit Grund).
                || Hat("fuer eine ganze Randbucht zu eng") || Hat("passt keine ganze Randbucht")
                || Hat("keine durchgehende Fahrbahn frei"))
                return Ursache.ZuKlein;
            // Der gezeichnete Umriss selbst taugt nicht.
            if (Hat("fold back on each other") || Hat("at least three corners")
                || Hat("positive area") || Hat("no edge with any length")
                || Hat("contains a zero-length edge")
                || Hat("normalised site is not counter-clockwise"))
                return Ursache.UmrissUngueltig;
            if (meldung.StartsWith("Entrance", StringComparison.Ordinal)
                || Hat("Zufahrten erwarten") || Hat("Zufahrtsteilung"))
                return Ursache.Zufahrt;
            if (Hat("Materialreparatur")) return Ursache.Zeitgrenze;
            return Ursache.Unbekannt;
        }

        /** Schluessel der Statuszeile in `Lang/*.json`; der Aufrufer holt den Text. */
        public static string Text(Ursache ursache, bool randstrassen)
        {
            switch (ursache)
            {
                case Ursache.ZuKlein:
                    return randstrassen
                        ? "vorschaufehler.zuKleinRandstrassen"
                        : "vorschaufehler.zuKlein";
                case Ursache.UmrissUngueltig: return "vorschaufehler.umrissUngueltig";
                case Ursache.Zufahrt: return "vorschaufehler.zufahrt";
                case Ursache.Zeitgrenze: return "vorschaufehler.zeitgrenze";
                default: return "vorschaufehler.unbekannt";
            }
        }
    }
}
