using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using SubNet = Game.Net.SubNet;

namespace ParkingLotTool.Tools
{
    public struct ParkingLotZoninganker : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(1);
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); ParkingLotFassungsschutz.Pruefe(nameof(ParkingLotZoninganker), version, 1); }
    }
    public struct ParkingLotAbrisswurzel : IComponentData { }

    internal static class ParkingLotBesitz
    {
        internal static bool GehoertZu(EntityManager em, Entity owner, Entity lot)
            => ParkingLotTool.Geometry.LotBesitzregel.GehoertZu(owner,lot,
                a => em.HasComponent<ParkingLotZoninganker>(a) && em.HasComponent<Owner>(a),
                a => em.GetComponentData<Owner>(a).m_Owner);
    }

    public sealed partial class ParkingLotToolSystem
    {
        /** Netzerhalt braucht dieselben Strassen-Entities UND deren Owner-ID.
         *  Deshalb bleibt die alte Besitzer-ID als nackter Anker erhalten;
         *  entfernt werden ihre Area und ihr Bauzettel. Nur der Anker wird
         *  umgehaengt, nie eine fertige Kante oder ein fertiger Knoten. */
        private Entity HintergrundBesitzanker(Entity alt, Entity neu, Entity alterTraeger)
        {
            var anker = new HashSet<Entity>();
            foreach (var e in _erhalteneNetzteile)
                if (EntityManager.HasComponent<Owner>(e)) anker.Add(EntityManager.GetComponentData<Owner>(e).m_Owner);
            foreach (var a in anker)
            {
                if (a == alt) continue;
                if (!EntityManager.HasComponent<ParkingLotZoninganker>(a)) throw new InvalidOperationException("Unbekannter Netzbesitzer.");
                EntityManager.SetComponentData(a,new Owner(neu));
                EntityManager.SetComponentData(a,new ParkingLotPartRelation { Lot = neu, Carrier = _lotCarrier });
            }
            if (!anker.Contains(alt)) return alt;

            // Der bisherige Aufraeumer bekommt dieselben nicht erhaltenen
            // Kinder in einer nackten Abrisswurzel, inklusive der bisherigen
            // 32er-Portionen. Diese Wurzel wird sofort regulaer geloescht.
            var abriss = EntityManager.CreateEntity();
            EntityManager.AddComponent<ParkingLotAbrisswurzel>(abriss);
            EntityManager.AddComponentData(abriss,EntityManager.GetComponentData<PrefabRef>(alt));
            EntityManager.AddComponentData(abriss,new ParkingLotCarrierReference { Carrier = alterTraeger });
            EnsureOwnerBuffers(abriss);
            EnsureOwnerBuffers(alt);
            var flaechen = EntityManager.GetBuffer<Game.Areas.SubArea>(alt,true).ToNativeArray(Allocator.Temp);
            foreach (var a in flaechen) EntityManager.GetBuffer<Game.Areas.SubArea>(abriss).Add(a);
            flaechen.Dispose(); EntityManager.GetBuffer<Game.Areas.SubArea>(alt).Clear();
            EntityManager.GetBuffer<Game.Objects.SubObject>(alt).Clear();
            var nets = EntityManager.GetBuffer<SubNet>(alt);
            for (int i = nets.Length-1; i >= 0; i--)
                if (!_erhalteneNetzteile.Contains(nets[i].m_SubNet)) nets.RemoveAt(i);
            using (var teile = _editRelatedParts.ToEntityArray(Allocator.Temp))
                foreach (var e in teile)
                {
                    var r = EntityManager.GetComponentData<ParkingLotPartRelation>(e);
                    if (r.Lot != alt) continue;
                    r.Lot = abriss; EntityManager.SetComponentData(e,r);
                }
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotVersorgungsleitung>()))
            using (var teile = q.ToEntityArray(Allocator.Temp))
                foreach (var e in teile)
                {
                    var r = EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e);
                    if (r.Lot != alt) continue;
                    r.Lot = abriss; EntityManager.SetComponentData(e,r);
                }
            if (EntityManager.HasComponent<Owner>(alterTraeger)) EntityManager.SetComponentData(alterTraeger,new Owner(abriss));

            // Area-Suche synchron entfernen, bevor Area/Nodes verschwinden.
            // Oeffentliche SearchSystem-API, nur der eigene Area-Eintrag;
            // keine globale Baumleerung und keinerlei Netzgeometrie.
            var suche = World.GetOrCreateSystemManaged<Game.Areas.SearchSystem>();
            var baum = suche.GetSearchTree(false,out var jobs,out var dreiecke);
            jobs.Complete();
            if (dreiecke.TryGetValue(alt,out var zahl))
                for (int i = 0; i < zahl; i++) baum.Remove(new Game.Areas.AreaSearchItem(alt,i));
            dreiecke.Remove(alt);
            // Der Anker darf insbesondere KEIN zweites ParkingFacility oder
            // ServiceUsage behalten. Vier technische Komponenten genuegen;
            // die echten Zoning-Kinder behalten ihren unveraenderten Owner.
            var behalten = new[] { ComponentType.ReadWrite<PrefabRef>(),
                ComponentType.ReadWrite<SubNet>(),ComponentType.ReadWrite<Game.Areas.SubArea>(),
                ComponentType.ReadWrite<Game.Objects.SubObject>() };
            using (var typen = EntityManager.GetComponentTypes(alt,Allocator.Temp))
                foreach (var t in typen)
                    if (!behalten.Any(k => k.TypeIndex == t.TypeIndex)) EntityManager.RemoveComponent(alt,t);
            EntityManager.AddComponent<ParkingLotZoninganker>(alt);
            EntityManager.AddComponentData(alt,new Owner(neu));
            EntityManager.AddComponentData(alt,new ParkingLotPartRelation { Lot = neu, Carrier = _lotCarrier });
            ParkingLotNetzRueckweg.Melde($"Netzerhalt: {anker.Count} nackte Besitzeranker, {_erhalteneNetzteile.Count} Kanten/Knoten behalten Entity und Owner; alte Lot-Area entfernt, 0 Netze umgeschrieben.");
            return abriss;
        }
    }
}
