using System;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotBestandsTauschWerkzeug
    {
        private bool VerwirfVeraltetenAuftrag()
        {
            if (_a == null || World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().DarfAnwenden(_a.Kennung))
                return false;
            // Keine Entity der alten Welt in der neuen Welt bereinigen.
            Zuruecksetzen();
            return true;
        }

        private void Beende(bool ok, string text)
        {
            if (_stufe == Stufe.Frei || VerwirfVeraltetenAuftrag()) return;
            var a = _a;
            _stufe = Stufe.Frei;
            _a = null;
            foreach (var d in _definitionen)
            {
                try { if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d); }
                catch (Exception x) { ok = false; text += "; Bereinigung fehlgeschlagen: " + x.Message; }
            }
            _definitionen.Clear();
            try { ParkingLotSchrittmarke.Aenderung(a.Name + ": Lot " + a.Lot.Index + (ok ? " fertig" : " abgebrochen")); }
            catch (Exception) { }
            Fertig?.Invoke(a.Lot, a.Kennung, ok, text);
        }
    }
}
