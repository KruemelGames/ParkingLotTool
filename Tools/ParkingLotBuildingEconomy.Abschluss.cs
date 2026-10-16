using System;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotBuildingEconomySystem
    {
        private void AnschlussBeendet(Entity lot, Auftragskennung id, bool ok, string grund)
        {
            if (!_anschluesse.TryGetValue(lot, out var a) || a.Auftrag != id || !Auftraege.DarfAnwenden(id)) return;
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
            Auftraege.Beende(id, lot, ok);
        }
    }
}
