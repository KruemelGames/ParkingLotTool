namespace ParkingLotTool.Tools
{
    /**
     * SPIELSTAND AUS EINER NEUEREN PLT-FASSUNG (2026-11-03).
     *
     * Ein aelterer Leser kann die Felder einer neueren Fassung nicht
     * ueberspringen: CS2 liest jede Komponente am Stueck, ohne Laengenangabe
     * je Eintrag. Ab dem ersten Bauzettel der neueren Fassung liest er deshalb
     * versetzt, CS2 meldet "Data size mismatch", und alle folgenden Bauzettel
     * enthalten Unsinn. Gemessen am Spielstand des Nutzers (Fahrrad-Build
     * schrieb Fassung 7, main las Fassung 6): 7 intakte Parkplaetze kamen mit
     * `Zellen = false` heraus, und PLT bot sie als Altbestand zum LOESCHEN an.
     *
     * Dasselbe passiert jedem Spieler, der nach einem Update auf eine aeltere
     * PLT-Fassung zurueckgeht. Deshalb: sieht ein Leser eine Fassung, die er
     * nicht kennt, gilt fuer diese Sitzung JEDER Bauzettel als unlesbar. Sync,
     * Altbestand, Hintergrundreparatur und Bearbeiten fassen dann keinen
     * Parkplatz an, und der Spieler erfaehrt warum. Die Parkplaetze selbst
     * (Wege, Flaechen, Aufkleber) laufen weiter wie gespeichert.
     *
     * Gesetzt wird das aus dem Deserialisieren heraus, also nicht vom
     * Hauptthread - deshalb nur ein Merker, kein Log und keine UI hier.
     */
    internal static class ParkingLotFassungsschutz
    {
        private static volatile bool _fremd;
        private static string _befund;

        /** Wurde in diesem Spielstand eine unbekannte Fassung gelesen? */
        internal static bool Fremd => _fremd;

        /** Welche Komponente in welcher Fassung, fuer das Log. */
        internal static string Befund => _befund ?? string.Empty;

        /**
         * Jede gespeicherte PLT-Komponente mit Fassungsnummer ruft das direkt
         * nach dem Lesen der Nummer auf (Stand 03.11.: 27 Lesestellen).
         *
         * OHNE FASSUNGSNUMMER sind bis heute: Lot-/Traegerverweis, Wirtschafts-
         * daten, Versorgungsleitung, Zoning-Probe, Stufe-A-Prefab/-Knoten,
         * offener Ersatz und Vegetationszettel. Aendert sich deren Aufbau,
         * merkt ein aelterer Leser das nicht. Wer eine davon aendert, gibt ihr
         * DABEI eine Fassungsnummer und ruft hier `Pruefe` auf.
         */
        internal static void Pruefe(string komponente, int gelesen, int bekannt)
        {
            if (gelesen > bekannt || gelesen < 1) Melde(komponente, gelesen, bekannt);
        }

        internal static void Melde(string komponente, int fassung, int bekannt)
        {
            if (_fremd) return;
            _befund = $"{komponente} in Fassung {fassung}, dieser Build kennt bis {bekannt}";
            _fremd = true;
        }

        /** Vor jedem Laden: der Befund gilt nur fuer einen Spielstand. */
        internal static void Zuruecksetzen()
        {
            _fremd = false;
            _befund = null;
        }
    }
}
