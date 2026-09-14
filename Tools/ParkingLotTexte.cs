namespace ParkingLotTool.Tools
{
    /**
     * Zweisprachige Meldungen fuer alles, was aus dem C#-Teil ins Panel geht.
     *
     * Das Panel selbst holt seine Texte aus `UI/src/mods/texte.ts`. Die
     * Statuszeile und die Hinweise entstehen aber hier, oft mit eingesetzten
     * Zahlen - sie koennen also nicht drueben liegen. Damit beide Seiten
     * derselben Wahl folgen, fragt dieser Helfer dieselbe Einstellung ab.
     *
     * Aufbau bewusst simpel: `Sag(deutsch, englisch)`. Wer eine Meldung
     * ergaenzt, sieht beide Fassungen nebeneinander und kann die zweite nicht
     * vergessen - anders als bei einem Woerterbuch, in dem ein fehlender
     * Schluessel erst im Spiel auffaellt.
     */
    internal static class ParkingLotTexte
    {
        internal static bool Deutsch => Mod.Optionen?.SprachKuerzel() == "de";

        internal static string T(string deutsch, string englisch)
            => Deutsch ? deutsch : englisch;
    }
}
