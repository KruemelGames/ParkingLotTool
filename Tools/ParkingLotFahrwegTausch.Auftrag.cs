using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotFahrwegTauschWerkzeug
    {
        private Auftragskennung _auftrag;

        // Nach Weltwechsel niemals alte Entity-IDs bereinigen: sie koennen
        // inzwischen einem fremden Objekt der neuen Welt gehoeren.
        private bool VerwirfVeraltetenAuftrag()
        {
            if (_auftrag.IstLeer || World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().DarfAnwenden(_auftrag))
                return false;
            Zuruecksetzen();
            return true;
        }
    }
}
