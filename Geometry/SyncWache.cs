namespace ParkingLotTool.Geometry
{
    /** Gemeinsame Grenze fuer Panel, Reparaturknopf und Fortschrittsanzeige. */
    public static class SyncWache
    {
        public static int Offen(int warten, int fahrwege, int bestand, int flaechen, int begleiter)
            => System.Math.Max(warten, fahrwege + bestand + flaechen + begleiter);

        // Eine spaete Antwort aus einer alten Welt oder von einer Reparatur
        // ohne Sync-Auftrag darf keinen gespeicherten Datenstand hochsetzen.
        public static bool AntwortErwartet(bool geladen, bool wartet) => geladen && wartet;
    }
}
