using System;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotBuildingEconomySystem
    {
        private void AnschlussBeendet(Entity lot, bool ok, string grund)
        {
            if (!_anschluesse.TryGetValue(lot, out var a)) return;
            _anschluesse.Remove(lot);
            try
            {
                Mod.log.Info($"PLT-Begleiteranschluss: Lot {lot.Index}, Erfolg={ok}: {grund}"
                    + (ok ? "." : $"; Stadtstrassen-Kandidaten {(Lebt(lot) ? Stadtstrassen(lot).Count : 0)}, Platzierungen {a.Versuche}."));
            }
            catch (Exception x)
            {
                try { Mod.log.Warn("PLT-Begleiteranschluss: Abschlussdiagnose fehlgeschlagen: " + x); }
                catch (Exception) { /* Rueckmeldung bleibt unabhaengig vom Logger. */ }
            }
            // Ausserhalb des Catchs: auch bei werfendem Empfaenger genau ein Versuch.
            if (a.Sync) World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(lot, lot, ok);
        }
    }
}
