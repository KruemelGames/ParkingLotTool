using Game.Common;
using Game.Net;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /** Unveraenderte automatische Leitungen werden wie unveraendertes
         *  Zoning erhalten. Nur unsere Zuordnungsdaten wechseln; kein Prefab,
         *  keine Kurve, kein Upgrade und kein Vanilla-Owner an fertigen Netzen.
         *  Die erfassten Anschluesse uebergibt der gemeinsame Rueckanschluss. */
        private void UebertrageAutoVersorgungsbestand(Entity alt, Entity neu, Entity traeger)
        {
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotVersorgungsleitung>(),
                ComponentType.ReadOnly<Edge>(), ComponentType.Exclude<Deleted>());
            using var teile = query.ToEntityArray(Allocator.Temp);
            int n = 0;
            foreach (var e in teile)
            {
                var relation = EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e);
                if (relation.Lot != alt) continue;
                // Neue Permanent-Leitungen haengen am alten Traeger und
                // fallen mit ihm. Keine nachtraegliche Owner-Umschreibung:
                // die Nebenarbeit baut diese Anschluesse erneut.
                if (EntityManager.HasComponent<Owner>(e)) continue;
                relation.Lot = neu; relation.Carrier = traeger;
                EntityManager.SetComponentData(e,relation); n++;
            }
            UebertrageVersorgungsnachweise(alt, neu, traeger);
            Nebenarbeitsdiagnose(() => ParkingLotNetzRueckweg.Melde($"Versorgungsbestand uebernommen: {n} dauerhafte Leitungskanten; 0 Kurven-/Prefab-/Owner-Aenderungen."));
        }

        private void UebertrageBushaltbestand(Entity alt, Entity neu, Entity traeger)
        {
            if (!_zoningErhalten) return;
            using var teile = _editRelatedParts.ToEntityArray(Allocator.Temp);
            int n = 0;
            foreach (var e in teile)
            {
                if (!EntityManager.HasComponent<TransportStop>(e)) continue;
                var relation = EntityManager.GetComponentData<ParkingLotPartRelation>(e);
                if (relation.Lot != alt) continue;
                if (!EntityManager.HasComponent<Game.Objects.Attached>(e)
                    || !_erhalteneNetzteile.Contains(EntityManager.GetComponentData<Game.Objects.Attached>(e).m_Parent)) continue;
                relation.Lot = neu; relation.Carrier = traeger;
                EntityManager.SetComponentData(e,relation);
                if (EntityManager.HasComponent<Hidden>(e)) EntityManager.RemoveComponent<Hidden>(e);
                if (!EntityManager.HasComponent<BatchesUpdated>(e)) EntityManager.AddComponent<BatchesUpdated>(e);
                _hiddenByEdit.Remove(e); n++;
            }
            ParkingLotNetzRueckweg.Melde($"Bushaltbestand uebernommen: {n} Haltestellen an erhaltenen Zoningkanten.");
        }
    }
}
