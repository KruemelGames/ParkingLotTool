using System;
using System.Diagnostics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotSyncSystem
    {
        private void Abarbeiten()
        {
            if (_warteschlange.Count == 0) return;
            // Waehrend eines Umbaus wird das alte Lot gleich ersetzt.
            if (_werkzeug.IsEditing) return;
            var uhr = Stopwatch.StartNew();
            do
            {
                var lot = _warteschlange[0];
                _warteschlange.RemoveAt(0);
                try
                {
                    // Auch ein Indexfehler gehoert zu genau diesem vorgemerkten Lot.
                    _index ??= TeileJeLot();
                    if (Synchronisiere(lot, _index)) _syncErfolge++;
                }
                catch (Exception ex)
                {
                    _index = null;
                    _wartetAuf.Remove(lot);
                    _fortsetzen.Remove(lot);
                    Scheitern(lot, StandVon(lot), "Ausfuehrung", ex.ToString());
                }
                finally { _erledigt++; }
            }
            while (_warteschlange.Count > 0 && uhr.Elapsed.TotalMilliseconds < BudgetMs);
            if (_warteschlange.Count == 0)
            {
                _index = null;
                if (Mod.Optionen?.AutomatischSynchronisieren ?? false)
                {
                    _meldungSync += _syncErfolge;
                    if (_syncErfolge > 0) VeroeffentlicheMeldung();
                }
            }
        }
    }
}
