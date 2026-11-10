using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotWaisenSystem
    {
        /** Nur Deinstallation: auch ohne Protokoll darf die exakte native
         *  Besitzerkette ueber den bereits vorhandenen Verbinden-Pfad weg. */
        internal bool VerbindeFahrradFuerAbriss(Entity lot)
        {
            using var kandidaten=_mitPrefab.ToEntityArray(Allocator.Temp);
            var traeger=ParkingLotFahrradBetrieb.AbrissTraeger(EntityManager,lot,kandidaten,out var n);
            if(traeger==Entity.Null)
            {
                Mod.log.Warn($"PLT-Bauzettel Fahrrad-Abriss Lot {lot.Index}: {n} passende Traeger, eindeutige Zuordnung erforderlich.");
                return false;
            }
            _traegerVon[lot]=traeger;
            if(!HerrenloseTraeger.Contains(traeger)) HerrenloseTraeger.Add(traeger);
            SucheBegleiter();
            return Verbinden(lot);
        }
    }
}
