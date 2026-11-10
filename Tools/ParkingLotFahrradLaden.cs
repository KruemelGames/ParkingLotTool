using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        internal void RestoreFahrradGehflaechenAfterLoad()
        {
            using var lotsQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using var areasQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.Area>(),
                ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var nachBesitzer = new Dictionary<Entity,List<Entity>>();
            using var areas = areasQuery.ToEntityArray(Allocator.Temp);
            foreach(var area in areas)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(area).m_Prefab;
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab,out var p) || p == null
                    || p.name != ParkingLotFahrradPrefabBau.Gehname) continue;
                var owner = EntityManager.GetComponentData<Owner>(area).m_Owner;
                if (!nachBesitzer.TryGetValue(owner,out var liste)) nachBesitzer[owner]=liste=new List<Entity>();
                liste.Add(area);
            }
            using var lots = lotsQuery.ToEntityArray(Allocator.Temp);
            int anzahl=0,gefuellt=0,fehlend=0,netze=0;
            foreach(var lot in lots)
            {
                if(!ParkingLotFahrradBetrieb.IstBestand(EntityManager,lot)) continue;
                anzahl++;
                var traeger=EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
                netze+=ParkingLotFahrradBetrieb.LadeNetze(EntityManager,lot,traeger);
                nachBesitzer.TryGetValue(lot,out var liste);
                var n=ParkingLotFahrradBetrieb.LadeGehflaechen(EntityManager,lot,traeger,
                    liste ?? new List<Entity>());
                gefuellt+=n;
                if(n==0) fehlend++;
            }
            Mod.log.Info($"PLT-Bauzettel Fahrrad-Laden: {anzahl} Lots, {netze} Netzverweise, {gefuellt} Geh-Flaechenverweise rekonstruiert, {fehlend} ohne Geh-Flaeche.");
        }
    }
}
