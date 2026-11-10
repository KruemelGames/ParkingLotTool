using System;

namespace ParkingLotTool.Geometry
{
    /** Bauartregeln vor der Konstruktion; Autowerte werden nicht umgeschrieben. */
    public static class FahrradBedienung
    {
        public static bool Artwechsel(bool edit, bool beschaeftigt) => !edit && !beschaeftigt;
        public static bool Layoutwahl(Fahrzeugart art, int layout, bool beschaeftigt)
            => art == Fahrzeugart.Fahrrad && !beschaeftigt
                && Enum.IsDefined(typeof(Fahrradlayout), layout);

        public static bool Stilwahl(Fahrzeugart art, int stil, bool beschaeftigt)
            => art == Fahrzeugart.Fahrrad && !beschaeftigt
                && Enum.IsDefined(typeof(Fahrradstil), stil);

        public static bool Arbeit(Fahrzeugart art, Weltarbeit arbeit)
            => art != Fahrzeugart.Fahrrad || (arbeit != Weltarbeit.Zoningflaeche
                && arbeit != Weltarbeit.Zoningseite && arbeit != Weltarbeit.Bushaltestelle);

        public static bool Zugang(Fahrzeugart art, int zugang)
            => art != Fahrzeugart.Fahrrad || zugang == (int)Zufahrtsart.Zufahrt;

        /** Gemerkte Auto-Entwurfsdaten duerfen keinen Fahrrad-Bauauftrag erreichen. */
        public static void FuerBau(LayoutSettings settings)
        {
            if (settings.Fahrzeugart != Fahrzeugart.Fahrrad) return;
            settings.BusStops = Array.Empty<BusStopPlacement>();
            settings.Zoningflaechen = Array.Empty<ParkingGeometry.Zoningflaeche>();
            settings.Randzoning = Array.Empty<ParkingGeometry.RandzoningLinie>();
            foreach (var e in settings.Entrances ?? Array.Empty<Entrance>())
                if (e != null) e.Art = Zufahrtsart.Zufahrt;
        }
    }
}
