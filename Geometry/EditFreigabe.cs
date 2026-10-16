namespace ParkingLotTool.Geometry
{
    public enum EditSperre { Keine, Wiederherstellung, Arbeit, Bearbeitung, Waise, Halbwaise, Bauplan, Traeger }

    /** Nur die Anzeigeentscheidung; keine Auftraege starten oder bestehende Sperren loesen. */
    public static class EditFreigabe
    {
        public static EditSperre Pruefe(bool wiederherstellung, bool arbeit, bool bearbeitung,
            bool vollstaendig, bool traeger, int waise, bool halbwaise)
        {
            if (wiederherstellung) return EditSperre.Wiederherstellung;
            if (arbeit) return EditSperre.Arbeit;
            if (bearbeitung) return EditSperre.Bearbeitung;
            if (waise == 1 || waise == 2) return EditSperre.Waise;
            if (halbwaise) return EditSperre.Halbwaise;
            if (!vollstaendig) return EditSperre.Bauplan;
            if (!traeger) return EditSperre.Traeger;
            return EditSperre.Keine;
        }

        public static bool DarfReparieren(EditSperre sperre, int waise)
            => (waise == 1 || waise == 3) && sperre != EditSperre.Wiederherstellung
                && sperre != EditSperre.Arbeit && sperre != EditSperre.Bearbeitung;
    }
}
