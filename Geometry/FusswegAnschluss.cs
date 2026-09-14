namespace ParkingLotTool.Geometry
{
    public static class FusswegAnschluss
    {
        // GenerateEdgesSystem prueft die Suchmaske gegen die Zielebene vor
        // dem Abstand. Null sperrt auch die 8-m-Zugabe fuer besitzereigene
        // Wege. Suchweite 0 allein laesst noch beide halben Netzbreiten zu.
        // Die Maske fuer Autos bleibt bitgleich; nur unser Fusswegklon nutzt 0.
        public static uint Suchmaske(Zufahrtsart art, uint original)
            => art == Zufahrtsart.Fussweg ? 0u : original;
    }
}
