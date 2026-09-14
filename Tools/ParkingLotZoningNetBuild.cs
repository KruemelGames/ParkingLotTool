using System;
using System.Collections.Generic;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Nur Messung, keine Reparatur und keine Bausperre.
         * GenerateNodes.NodeKey vergleicht bei Entity.Null die Position exakt.
         * Deshalb zaehlen hier die tatsaechlich ausgegebenen float3-Kursenden.
         * Die Abnahme bleibt PLT-Zoningknoten/PLT-Ueberlappung NACH Apply.
         */
        private void MeldeZoningKnotenbau()
        {
            var prefabs = new HashSet<Entity>();
            var enden = new Dictionary<float3, int>();
            var kurse = 0;
            foreach (var record in _netRecords)
            {
                if (!string.Equals(record.Kind, "zoning",
                        StringComparison.Ordinal)) continue;
                kurse++;
                prefabs.Add(record.Prefab);
                foreach (var punkt in new[] { record.From, record.To })
                {
                    enden.TryGetValue(punkt, out var grad);
                    enden[punkt] = grad + 1;
                }
            }
            if (kurse == 0) return;
            var plankreuzungen = 0;
            foreach (var grad in enden.Values)
                if (grad >= 3) plankreuzungen++;

            var paare = new List<(Entity A, Entity B)>();
            var knotengrade = new Dictionary<Entity, int>();
            var tempreferenzen = 0;
            using (var entities = _tempNetQuery.ToEntityArray(Allocator.TempJob))
                foreach (var entity in entities)
                {
                    if (!EntityManager.HasComponent<Edge>(entity)
                        || !prefabs.Contains(EntityManager
                            .GetComponentData<PrefabRef>(entity).m_Prefab)) continue;
                    var temp = EntityManager.GetComponentData<Temp>(entity);
                    if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0)
                        continue;
                    if (temp.m_Original != Entity.Null
                        && EntityManager.HasComponent<Temp>(temp.m_Original))
                        tempreferenzen++;
                    var edge = EntityManager.GetComponentData<Edge>(entity);
                    paare.Add((edge.m_Start, edge.m_End));
                    foreach (var node in new[] { edge.m_Start, edge.m_End })
                    {
                        knotengrade.TryGetValue(node, out var grad);
                        knotengrade[node] = grad + 1;
                    }
                }
            var istkreuzungen = 0;
            foreach (var grad in knotengrade.Values)
                if (grad >= 3) istkreuzungen++;
            Mod.log.Info($"PLT-Zoningknotenbau: {kurse} Kurse gemeinsam, "
                + $"{enden.Count} exakte 3D-Planpunkte, "
                + $"{plankreuzungen} Planknoten mit mindestens 3 Armen; "
                + $"Temp: {paare.Count} Kanten, {knotengrade.Count} Knoten, "
                + $"{EntityNetze(paare)} Netz(e), {istkreuzungen} Kreuzung(en), "
                + $"{tempreferenzen} Temp-auf-Temp-Kantenreferenzen. "
                + "Vor Apply; Abnahme folgt am fertigen Netz.");
            if (plankreuzungen != istkreuzungen || tempreferenzen != 0)
                Mod.log.Warn("PLT-Zoningknotenbau: Vorschautopologie weicht ab; "
                    + "es wird gebaut. Beide Abnahmezeilen danach pruefen.");
        }

        private static int EntityNetze(List<(Entity A, Entity B)> paare)
        {
            var eltern = new Dictionary<Entity, Entity>();
            Entity Wurzel(Entity e)
            {
                if (!eltern.TryGetValue(e, out var p)) eltern[e] = p = e;
                while (p != eltern[p]) p = eltern[p] = eltern[eltern[p]];
                return p;
            }
            foreach (var paar in paare)
            {
                var a = Wurzel(paar.A);
                var b = Wurzel(paar.B);
                if (a != b) eltern[a] = b;
            }
            var netze = new HashSet<Entity>();
            foreach (var e in new List<Entity>(eltern.Keys))
                netze.Add(Wurzel(e));
            return netze.Count;
        }

    }
}
