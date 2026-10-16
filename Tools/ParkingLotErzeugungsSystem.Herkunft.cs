using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotErzeugungsSystem
    {
        private bool BindeBushalt(Entity lot, Entity carrier, Entity prefab,
            ref ParkingLotErzeugungsauftrag q, List<Entity> treffer)
        {
            if (treffer.Count != 1 || !Passt(treffer[0], q, prefab, lot, carrier)) return false;
            var e = treffer[0];
            if (!EntityManager.HasComponent<ParkingLotPartRelation>(e))
                EntityManager.AddComponentData(e, new ParkingLotPartRelation { Lot = lot, Carrier = carrier });
            q.Ergebnis = e; q.Erledigt = true;
            return true;
        }

        private bool BindeLeitung(Entity lot, Entity carrier, Entity prefab, ParkingLotErzeugungsauftrag q, Entity e)
        {
            if (!Passt(e, q, prefab, lot, carrier)) return false;
            if (!EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e))
                EntityManager.AddComponentData(e, new ParkingLotVersorgungsleitung
                    { Lot = lot, Carrier = carrier, Strom = q.Art == ParkingLotErzeugungsart.Strom });
            return true;
        }

        private bool Passt(Entity e, ParkingLotErzeugungsauftrag q, Entity prefab, Entity lot, Entity carrier)
        {
            bool temp = EntityManager.HasComponent<Temp>(e);
            var t = temp ? EntityManager.GetComponentData<Temp>(e) : default;
            if (!Erzeugungsherkunft.Quelle(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab == prefab,
                EntityManager.GetComponentData<PseudoRandomSeed>(e).m_Seed, q.Seed,
                temp && t.m_Original != Entity.Null, temp && (t.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0)) return false;
            if (q.Art == ParkingLotErzeugungsart.Bushalt)
                return !temp && EntityManager.HasComponent<Game.Routes.TransportStop>(e)
                    && (!EntityManager.HasComponent<Owner>(e) || EntityManager.GetComponentData<Owner>(e).m_Owner == Entity.Null)
                    && EntityManager.HasComponent<Attached>(e) && EntityManager.GetComponentData<Attached>(e).m_Parent == q.Bezug
                    && EntityManager.HasComponent<Game.Objects.Transform>(e)
                    && math.distancesq(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position, q.A) < .000001f
                    && (!EntityManager.HasComponent<ParkingLotPartRelation>(e)
                        || EntityManager.GetComponentData<ParkingLotPartRelation>(e).Lot == lot);
            if (!EntityManager.HasComponent<Edge>(e) || !EntityManager.HasComponent<Curve>(e)) return false;
            if (temp && (t.m_Flags & TempFlags.Create) == 0) return false;
            // Permanent-Ergebnisse haben einen expliziten PLT-Besitzer;
            // geladene Ergebnisse ohne Besitzer brauchen bereits unsere Marke.
            if (!temp && !(EntityManager.HasComponent<Owner>(e) && EntityManager.GetComponentData<Owner>(e).m_Owner == carrier)
                && !(EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e)
                    && EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot == lot)) return false;
            var c = EntityManager.GetComponentData<Curve>(e).m_Bezier;
            return Erzeugungsherkunft.Leitungsstueck(c.a, c.d, q.A, q.D)
                && (!EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e)
                    || EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot == lot);
        }

    }
}
