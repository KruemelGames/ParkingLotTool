using System;

namespace ParkingLotTool.Geometry
{
    /**
     * DIE REIHENWINKEL-MODI AN EINER EINZIGEN STELLE.
     *
     * Befund des Nutzers am 2026-09-09: *"Ich habe das Gefuehl dass einiges
     * was ich aendere nicht wirklich uebernommen wird ... zb gestern wurde
     * 'Across' nicht uebernommen."*
     *
     * Er hatte recht, und es war kein Gefuehl. Die Liste der gueltigen Modi
     * stand an zwei Stellen und die beiden waren auseinandergelaufen:
     *
     *     UI liess zu:      edge, auto, quer, fixed
     *     Bauzettel kannte: edge, auto,       fixed
     *
     * `EncodeAngleMode` bildete alles Unbekannte auf 0 ab, und 0 heisst
     * `edge`. "Across" (`quer`) wurde also gebaut, aber beim Oeffnen im Edit
     * kam `edge` zurueck - lautlos.
     *
     * Deshalb liegt die Liste jetzt hier, in `Geometry/`: das Testprojekt
     * uebersetzt nur diesen Ordner, und damit ist der Abgleich pruefbar. Wer
     * einen Modus hinzufuegt, fuegt ihn genau einmal hinzu.
     */
    public static class Winkelmodus
    {
        /** Reihe folgt der Arealkante - der Standard. */
        public const string Kante = "edge";
        /** Fester Winkel aus dem Regler. */
        public const string Fest = "fixed";
        /** Das Werkzeug sucht den besten Winkel selbst. */
        public const string Automatisch = "auto";
        /** Quer zur Kante - im Panel "Across". */
        public const string Quer = "quer";

        /**
         * ALLE gueltigen Modi. Die Reihenfolge ist die des Panels; die
         * Zahlenwerte im Bauzettel haengen NICHT daran, sondern an
         * `Kodiere` - sonst wuerde ein Umsortieren alte Spielstaende
         * umdeuten.
         */
        public static readonly string[] Alle = { Kante, Quer, Fest, Automatisch };

        public static bool IstGueltig(string modus)
            => Array.IndexOf(Alle, modus) >= 0;

        /**
         * Bauzettel-Zahl aus dem Modus. Die Zuordnung ist FEST: 0, 1 und 2
         * stehen seit dem ersten Bauzettel fuer Kante, Fest und Automatisch
         * und duerfen sich nie aendern - in gespeicherten Staedten stehen
         * diese Zahlen. `Quer` bekommt deshalb die naechste freie, die 3.
         *
         * Unbekanntes faellt bewusst auf `Kante` zurueck; ein Bauzettel aus
         * der Zukunft soll einen alten Mod nicht umbringen.
         */
        public static int Kodiere(string modus)
            => modus == Fest ? 1
             : modus == Automatisch ? 2
             : modus == Quer ? 3
             : 0;

        public static string Dekodiere(int wert)
            => wert == 1 ? Fest
             : wert == 2 ? Automatisch
             : wert == 3 ? Quer
             : Kante;

        /** Die groesste vergebene Zahl - der Bauzettel prueft damit. */
        public static int GroessteZahl => 3;
    }
}
