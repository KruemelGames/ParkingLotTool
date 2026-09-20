using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubNet = Game.Net.SubNet;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private float2[][] _alteZoningkurse;
        private string _altesZoningprefab;
        private readonly HashSet<Entity> _erhalteneZoningteile = new HashSet<Entity>();
        private bool _zoningErhalten;

        private static float2[][] Zoningkurse(ParkingLayout layout)
            => layout.NetLine.Where(p => p.Kind == "zoning")
                .Select(p => new[] { p.A, p.B }).ToArray();

        private void MerkeZoningbestand(ParkingLayout layout)
        {
            _alteZoningkurse = Zoningkurse(layout);
            _altesZoningprefab = _uiSystem.CurrentSettings().Zoningstrasse;
        }

        private void PlaneZoningerhalt()
        {
            _erhalteneZoningteile.Clear();
            _zoningErhalten = false;
            if (!IsEditing || _areaPreviewLayout == null || !ZoningErhalt.Gleich(
                _alteZoningkurse, Zoningkurse(_areaPreviewLayout), _altesZoningprefab,
                _uiSystem.CurrentSettings().Zoningstrasse)) return;

            using var teile = _editOwnerParts.ToEntityArray(Allocator.Temp);
            foreach (var teil in teile)
            {
                if (EntityManager.GetComponentData<Owner>(teil).m_Owner != _editLot
                    || !EntityManager.HasComponent<Edge>(teil)
                    || EntityManager.HasComponent<Deleted>(teil)
                    || EntityManager.HasComponent<Temp>(teil)
                    || !EntityManager.HasComponent<PrefabRef>(teil)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(teil).m_Prefab;
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var asset)
                    || asset.name != "PLT Zoningstrasse (" + _altesZoningprefab + ")") continue;
                _erhalteneZoningteile.Add(teil);
                var edge = EntityManager.GetComponentData<Edge>(teil);
                _erhalteneZoningteile.Add(edge.m_Start);
                _erhalteneZoningteile.Add(edge.m_End);
            }
            _zoningErhalten = _erhalteneZoningteile.Count > 0;
            Mod.log.Info("PLT-Vorbauzettel Zoningerhalt: unveraenderter Strassenplan; "
                + _erhalteneZoningteile.Count + " bestehende Kanten/Knoten erhalten. "
                + "Zonenbloecke und Gebaeude werden nicht neu erzeugt.");
        }

        private void UebertrageZoningbestand(Entity old, Entity next, Entity carrier)
        {
            if (!_zoningErhalten) return;
            foreach (var teil in _erhalteneZoningteile)
            {
                if (!EntityManager.Exists(teil) || EntityManager.HasComponent<Deleted>(teil)) continue;
                if (EntityManager.HasComponent<Owner>(teil)
                    && EntityManager.GetComponentData<Owner>(teil).m_Owner == old)
                    EntityManager.SetComponentData(teil, new Owner(next));
                if (EntityManager.HasComponent<ParkingLotPartRelation>(teil))
                {
                    var relation = EntityManager.GetComponentData<ParkingLotPartRelation>(teil);
                    if (relation.Lot == old)
                    {
                        relation.Lot = next; relation.Carrier = carrier;
                        EntityManager.SetComponentData(teil, relation);
                    }
                }
                if (!EntityManager.HasComponent<Edge>(teil)) continue;
                foreach (var owner in new[] { next, carrier })
                {
                    if (!EntityManager.HasBuffer<SubNet>(owner)) EntityManager.AddBuffer<SubNet>(owner);
                    var nets = EntityManager.GetBuffer<SubNet>(owner);
                    bool found = false;
                    for (int i = 0; i < nets.Length; i++) if (nets[i].m_SubNet == teil) found = true;
                    if (!found) nets.Add(new SubNet(teil));
                }
            }
            EntferneAlteZoningverweise(old);
            if (EntityManager.HasComponent<ParkingLotCarrierReference>(old))
                EntferneAlteZoningverweise(EntityManager.GetComponentData<ParkingLotCarrierReference>(old).Carrier);
        }

        private void EntferneAlteZoningverweise(Entity owner)
        {
            if (!EntityManager.Exists(owner) || !EntityManager.HasBuffer<SubNet>(owner)) return;
            var nets = EntityManager.GetBuffer<SubNet>(owner);
            for (int i = nets.Length - 1; i >= 0; i--)
                if (_erhalteneZoningteile.Contains(nets[i].m_SubNet)) nets.RemoveAt(i);
        }
    }
}
