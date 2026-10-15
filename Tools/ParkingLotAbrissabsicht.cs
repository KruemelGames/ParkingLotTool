using Colossal.Serialization.Entities;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * ABRISSABSICHT AM WEITERLEBENDEN TRAEGER (2026-10-15).
     *
     * Der Aufraeumer loescht die Teile eines abgerissenen Parkplatzes in
     * Portionen ueber viele Bilder. Sein Auftrag lag nur im Speicher: ein
     * Speichern mittendrin schrieb Traeger und Restteile in den Spielstand,
     * aber keinen Auftrag - nach dem Laden blieben sie dauerhaft stehen.
     * Diese Markierung ueberlebt das Speichern; beim Laden nimmt
     * `ParkingLotCleanupSystem` den Abriss daraus wieder auf. Sie verschwindet
     * erst mit dem Traeger, also nach bestaetigtem Abschluss. Sie traegt
     * absichtlich keine Entity-Verweise auf die geloeschte Flaeche.
     */
    public struct ParkingLotAbrissabsicht : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter
            => w.Write(1);

        public void Deserialize<TReader>(TReader r) where TReader : IReader
        {
            r.Read(out int version);
        }
    }

    internal static class ParkingLotAbriss
    {
        /**
         * Vor dem `Deleted` der Lot-Flaeche aufrufen. Loest zugleich den
         * Owner des Traegers auf das Lot (Absturz 12.10.: die Besitzerkette
         * darf nie auf die zerstoerte Flaeche zeigen).
         */
        internal static void Sichern(EntityManager em, Entity lot,
            EntityCommandBuffer? befehle = null)
        {
            if (lot == Entity.Null || !em.Exists(lot)
                || em.HasComponent<Temp>(lot)
                || !em.HasComponent<ParkingLotCarrierReference>(lot)
                || (!em.HasComponent<Game.Areas.Area>(lot)
                    && !em.HasComponent<ParkingLotAbrisswurzel>(lot))) return;

            var carrier = em.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (carrier == Entity.Null || carrier == lot || !em.Exists(carrier)
                || em.HasComponent<Temp>(carrier))
            {
                Mod.log.Warn($"PLT-Aufraeumer: Lot {lot.Index}: keine "
                    + "Abrissabsicht gesichert; dauerhafter Traeger fehlt.");
                return;
            }

            if (em.HasComponent<Deleted>(carrier)) return;

            var neu = !em.HasComponent<ParkingLotAbrissabsicht>(carrier);
            var loesen = em.HasComponent<Owner>(carrier)
                && em.GetComponentData<Owner>(carrier).m_Owner == lot;
            // Ausloeser sichern sofort, der Aufraeumer an seiner Barrier.
            if (befehle.HasValue)
            {
                var buffer = befehle.Value;
                if (neu) buffer.AddComponent<ParkingLotAbrissabsicht>(carrier);
                if (loesen) buffer.RemoveComponent<Owner>(carrier);
            }
            else
            {
                if (neu) em.AddComponent<ParkingLotAbrissabsicht>(carrier);
                if (loesen) em.RemoveComponent<Owner>(carrier);
            }
            if (neu)
                Mod.log.Info($"PLT-Aufraeumer: Abrissabsicht fuer Lot "
                    + $"{lot.Index} an Traeger {carrier.Index} angelegt.");
        }
    }
}
