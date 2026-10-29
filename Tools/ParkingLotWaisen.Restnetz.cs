using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * STEHEN STRASSEN OHNE LEBENDEN BESITZER IM SPIELSTAND? (29.10.2026)
     *
     * Bis 9f1015b liess der Abriss eines im Hintergrund neu gebauten
     * Parkplatzes (Sync, Reparatur) die Strassen seines Netzerhalt-Ankers
     * stehen: Gassen, Zoningstrassen, Wege - mit einem Besitzer, der danach
     * zerstoert wurde. Wer das gespeichert hat, traegt sie weiter mit sich.
     *
     * Bevor irgendwer sie wegraeumt, zaehlt diese Aufnahme nach jedem Laden
     * Kanten und Knoten, deren Owner leer ist, nicht mehr existiert oder
     * geloescht ist - getrennt nach PLT-Prefabs und fremden, mit Lage.
     * Unabhaengig davon, ob noch ein Parkplatz steht: gerade wer alle
     * abgerissen hat, kann Reste haben. VERAENDERT NICHTS.
     */
    public sealed partial class ParkingLotWaisenSystem
    {
        private void ZaehleRestnetz()
        {
            using var query = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Owner>() },
                Any = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Node>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            using var netz = query.ToEntityArray(Allocator.Temp);
            using var besitzer = query.ToComponentDataArray<Owner>(Allocator.Temp);

            int kanten = 0, knoten = 0, plt = 0, leer = 0;
            var namen = new Dictionary<string, int>();
            var orte = new List<string>();
            for (var i = 0; i < netz.Length; i++)
            {
                var o = besitzer[i].m_Owner;
                var tot = o == Entity.Null || !EntityManager.Exists(o) || EntityManager.HasComponent<Deleted>(o);
                if (!tot) continue;
                if (o == Entity.Null) leer++;
                var e = netz[i];
                var kante = EntityManager.HasComponent<Game.Net.Edge>(e);
                if (kante) kanten++; else knoten++;

                var name = "?";
                if (EntityManager.HasComponent<PrefabRef>(e)
                    && _prefabSystem.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab, out var p)
                    && p != null) name = p.name;
                if (name.StartsWith("PLT ", System.StringComparison.Ordinal)) plt++;
                namen[name] = namen.TryGetValue(name, out var n) ? n + 1 : 1;

                if (orte.Count >= 8) continue;
                float3 lage;
                if (kante && EntityManager.HasComponent<Game.Net.Curve>(e))
                {
                    var c = EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
                    lage = (c.a + c.d) * 0.5f;
                }
                else if (!kante) lage = EntityManager.GetComponentData<Game.Net.Node>(e).m_Position;
                else continue;
                orte.Add($"({lage.x:0.0}/{lage.z:0.0})");
            }

            var summe = kanten + knoten;
            if (summe == 0)
            {
                Mod.log.Info("PLT-Restnetz: 0 Kanten und Knoten ohne lebenden Besitzer.");
                return;
            }
            Mod.log.Warn($"PLT-Restnetz: {kanten} Kante(n) und {knoten} Knoten ohne lebenden Besitzer "
                + $"(davon {plt} mit PLT-Prefab, {leer} mit leerem Owner). Prefabs "
                + string.Join(", ", namen.OrderByDescending(x => x.Value).Select(x => x.Value + "x " + x.Key))
                + "; Lage " + string.Join(" ", orte) + (summe > orte.Count ? " ..." : "") + ". Nichts veraendert.");
        }
    }
}
