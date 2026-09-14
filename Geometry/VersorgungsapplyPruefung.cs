namespace ParkingLotTool.Geometry
{
    internal enum VersorgungsapplyZustand { Offen, Dauerhaft, Verloren }

    internal static class VersorgungsapplyPruefung
    {
        // Zwei reale Bauten meldeten 0/2 dauerhaft trotz leerer Temp-Abfrage.
        // Deshalb zaehlt ausschliesslich die vollstaendige Entity-Bilanz.
        internal static VersorgungsapplyZustand Zustand(int erwartet, int dauerhaft,
            int temporaer, int geloescht, int verschwunden)
        {
            if (geloescht > 0 || verschwunden > 0) return VersorgungsapplyZustand.Verloren;
            if (erwartet > 0 && dauerhaft == erwartet && temporaer == 0)
                return VersorgungsapplyZustand.Dauerhaft;
            return VersorgungsapplyZustand.Offen;
        }
    }
}
