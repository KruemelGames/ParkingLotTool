using System;
using Colossal.Serialization.Entities;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /** Neue Stufe-A-Sicherung: Version und PrefabID, keine gespeicherte Prefab-Entity.
     *  ParkingLotStufeAPrefab bleibt ausschliesslich fuer alte Spielstaende lesbar. */
    public struct ParkingLotStufeAPrefabID : IComponentData, ISerializable
    {
        public FixedString512Bytes PrefabName;
        public Entity Prefab; // Nur Laufzeitcache, wird nicht serialisiert.

        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter
        {
            w.Write(1);
            w.Write(PrefabName.ToString());
        }

        public void Deserialize<TReader>(TReader r) where TReader : IReader
        {
            r.Read(out int version);
            if (version != 1) throw new InvalidOperationException("Stufe-A-Prefabversion");
            r.Read(out string name);
            PrefabName = ParkingLotPrefabname.Fest(name);
            Prefab = Entity.Null;
        }
    }

    internal static class ParkingLotStufeA
    {
        internal static Entity Prefab(EntityManager em, Entity lot)
        {
            if (em.HasComponent<ParkingLotStufeAPrefabID>(lot))
            {
                var neu = em.GetComponentData<ParkingLotStufeAPrefabID>(lot);
                // Eine vorhandene ID hat Vorrang auch vor einem veralteten Cache.
                if (!neu.PrefabName.IsEmpty) return ParkingLotPrefabname.Aufloesen(em, neu.PrefabName);
            }
            if (!em.HasComponent<ParkingLotStufeAPrefab>(lot)) return Entity.Null;
            var alt = em.GetComponentData<ParkingLotStufeAPrefab>(lot).Prefab;
            return alt != Entity.Null && em.Exists(alt) && em.HasComponent<PrefabData>(alt)
                ? alt : Entity.Null;
        }

        internal static void VergissPrefab(EntityManager em, Entity lot)
        {
            if (em.HasComponent<ParkingLotStufeAPrefabID>(lot)) em.RemoveComponent<ParkingLotStufeAPrefabID>(lot);
            if (em.HasComponent<ParkingLotStufeAPrefab>(lot)) em.RemoveComponent<ParkingLotStufeAPrefab>(lot);
        }
    }
}
