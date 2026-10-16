using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotHintergrundAbschluss
    {
        /**
         * F3TK und DBJT (10.10.2026): gleicher nativer Absturz bei AddComponent
         * direkt nach SetComponentData<ParkingLotDatenstand>. Der Neubau hat
         * diesen Typ noch NICHT: sein Bauzettel schreibt ihn nicht, und die
         * Sync-Aufnahme laesst den laufenden Ersatz ausdruecklich aus.
         * Set auf den fehlenden Typ kann ohne ECS-Sicherheitspruefungen den
         * Speicher beschaedigen. Deshalb erst anlegen, spaeter aktualisieren;
         * dieselbe Regel gilt auch im Fehlerpfad nach der Uebernahme.
         */
        internal static bool MerkeNebenarbeit(EntityManager em, Entity lot, Entity traeger)
        {
            if (!ParkingLotSyncGueltigkeit.Bestand(em, lot, traeger)
                || !em.HasComponent<ParkingLotBuildReceipt>(lot)) return false;

            // Stand 7 bleibt bis zur bestaetigten Nacharbeit erhalten. Die
            // restlichen Bestandsmigrationen werden damit nicht uebersprungen.
            var stand = new ParkingLotDatenstand { Stand = 7 };
            if (em.HasComponent<ParkingLotDatenstand>(lot)) em.SetComponentData(lot, stand);
            else em.AddComponentData(lot, stand);
            ParkingLotNebenarbeitDaten.Merke(em, lot, Mod.Optionen == null || Mod.Optionen.AutomatischVersorgung);
            return true;
        }
    }
}
