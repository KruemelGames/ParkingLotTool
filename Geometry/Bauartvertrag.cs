using System;

namespace ParkingLotTool.Geometry
{
    /** Der gespeicherte Typ hat beim Edit Vorrang vor der Neubauwahl. */
    public static class Bauartvertrag
    {
        public static bool Gueltig(Fahrzeugart art, Fahrradlayout layout)
            => Enum.IsDefined(typeof(Fahrzeugart), art)
                && Enum.IsDefined(typeof(Fahrradlayout), layout);

        /** Unbekannter Stil (beschaedigter Zettel) faellt auf Schlicht zurueck, nie auf einen Fehler. */
        public static Fahrradstil Stil(int stil)
            => Enum.IsDefined(typeof(Fahrradstil), stil) ? (Fahrradstil)stil : Fahrradstil.Schlicht;

        public static void Edit(LayoutSettings settings, Fahrzeugart? gespeichert, Fahrradlayout layout,
            Fahrradstil stil = Fahrradstil.Schlicht)
        {
            if (!gespeichert.HasValue) return;
            settings.Fahrzeugart = gespeichert.Value;
            settings.Fahrradlayout = layout;
            settings.Fahrradstil = stil;
        }

        /** Gleicher Umriss ist keine gleiche Bauart: neuester PASSENDER Zettel. */
        public static bool ProtokollPasst(string art, string layout, Fahrzeugart? gesucht, Fahrradlayout? sollLayout)
        {
            if(!gesucht.HasValue) return true;
            if(!Enum.TryParse(art ?? "Auto",out Fahrzeugart gelesen)
                || !Enum.TryParse(layout ?? "A",out Fahrradlayout lage) || !Gueltig(gelesen,lage)) return false;
            return gelesen==gesucht.Value && (!sollLayout.HasValue || lage==sollLayout.Value);
        }
    }
}
