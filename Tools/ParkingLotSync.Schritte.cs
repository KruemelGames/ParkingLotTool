using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Die Ausfuehrung der Schritte aus `Migrationskatalog`.
     *
     * Jeder Schritt hat zwei Haelften: `Braucht` sagt, ob an DIESEM
     * Parkplatz etwas zu tun ist (und dient danach als Nachpruefung - nach
     * dem Ausfuehren muss es `false` sein), `Ausfuehren` tut es. Beide sehen
     * nur die Teile dieses einen Parkplatzes.
     *
     * Die drei ersten Schritte standen bis 2026-09-25 in
     * `RestoreCarrierSubNetsAfterLoad` und liefen bei JEDEM Laden fuer alle
     * Parkplaetze auf einmal. Dort sind sie entfernt.
     */
    public sealed partial class ParkingLotSyncSystem
    {
        private sealed class Ausfuehrung
        {
            internal System.Func<Entity, Entity, List<Entity>, bool> Braucht;
            internal System.Action<Entity, Entity, List<Entity>> Ausfuehren;
        }

        private Dictionary<string, Ausfuehrung> _ausfuehrungen;

        private void LegeSchritteAn()
        {
            _ausfuehrungen = new Dictionary<string, Ausfuehrung>
            {
                /*
                 * Pflanze ohne Besitzer, oder Traeger ohne Besitzer: CS2
                 * bewertet sie dann als fremd und verdraengt sie. `Updated`,
                 * damit die Verdraengung neu bewertet wird.
                 */
                ["PflanzenAmTraeger"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) =>
                    {
                        foreach (var t in teile)
                            if (IstPflanze(t) && (!EntityManager.HasComponent<Owner>(t)
                                    || !EntityManager.HasComponent<Owner>(traeger)))
                                return true;
                        return false;
                    },
                    Ausfuehren = (lot, traeger, teile) =>
                    {
                        foreach (var t in teile)
                        {
                            if (!IstPflanze(t)) continue;
                            if (EntityManager.HasComponent<Owner>(t)
                                && EntityManager.HasComponent<Owner>(traeger)) continue;
                            _werkzeug.SetVegetationOwner(t, traeger, lot);
                            if (!EntityManager.HasComponent<Updated>(t))
                                EntityManager.AddComponent<Updated>(t);
                        }
                    },
                },
                /*
                 * Aufkleber, Pfeile, Saeulen ohne Besitzer. GEBAEUDE SIND
                 * AUSGENOMMEN: der Wirtschaftsbegleiter bekommt beim Bau
                 * absichtlich keinen Besitzer (nur Attached). Der alte
                 * Ladecode nahm jedes Objekt - auch ihn.
                 */
                ["ObjekteAmTraeger"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) =>
                    {
                        foreach (var t in teile)
                            if (IstLoseObjekt(t)) return true;
                        return false;
                    },
                    Ausfuehren = (lot, traeger, teile) =>
                    {
                        foreach (var t in teile)
                            if (IstLoseObjekt(t))
                                _werkzeug.SetVegetationOwner(t, traeger, lot);
                    },
                },
                /* Mit Besitzer ist ein Halt im Linienwerkzeug nicht anwaehlbar. */
                ["HaltestellenOhneBesitzer"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) =>
                    {
                        foreach (var t in teile)
                            if (EntityManager.HasComponent<Game.Routes.TransportStop>(t)
                                && EntityManager.HasComponent<Owner>(t))
                                return true;
                        return false;
                    },
                    Ausfuehren = (lot, traeger, teile) =>
                    {
                        foreach (var t in teile)
                            if (EntityManager.HasComponent<Game.Routes.TransportStop>(t)
                                && EntityManager.HasComponent<Owner>(t))
                                EntityManager.RemoveComponent<Owner>(t);
                    },
                },
                /*
                 * Netze nachtraeglich umzuschreiben waere das Direktschreiben,
                 * das CS2 zum Absturz bringt. Also Neubau ueber den
                 * Bearbeiten-Weg (`ParkingLotToolSystem.PlaneNachbau`), der die
                 * einebnenden Wege am Gassenende von selbst setzt.
                 */
                ["GassenknotenEben"] = new Ausfuehrung
                {
                    // AUSGESETZT 2026-09-26: der Neubau setzte einebnende Wege,
                    // und die entstellen das Gelaende. Bis der Knotenschutz ohne
                    // FlattenTerrain steht, braucht kein Parkplatz diesen Schritt;
                    // der neue Schutz kommt als eigener Schritt.
                    Braucht = (lot, traeger, teile) => false,
                    Ausfuehren = (lot, traeger, teile) => _werkzeug.PlaneNachbau(lot),
                },
            };

            // Katalog und Ausfuehrung muessen sich decken - sonst bliebe ein
            // Parkplatz fuer immer vor einem Schritt stehen.
            foreach (var schritt in Migrationskatalog.Schritte)
                if (!_ausfuehrungen.ContainsKey(schritt.Name))
                    Mod.log.Error("PLT-Sync: Schritt " + schritt.Nummer + " '"
                        + schritt.Name + "' hat keine Ausfuehrung.");
        }

        /**
         * Haengt an einem Ende einer unserer Zufahrtsgassen einer unserer
         * Wege, der NICHT einebnet? Genau dann kann CS2 den gemeinsamen
         * Knoten aufs Gelaende legen, das die Gasse wegschneidet.
         */
        private bool HatSinkendesGassenende(List<Entity> teile)
        {
            var eigene = new HashSet<Entity>(teile);
            foreach (var t in teile)
            {
                if (!EntityManager.HasComponent<Game.Net.Edge>(t)
                    || !EntityManager.HasComponent<PrefabRef>(t)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(t).m_Prefab;
                if (!IstZufahrtsgasse(prefab)) continue;
                var kante = EntityManager.GetComponentData<Game.Net.Edge>(t);
                foreach (var knoten in new[] { kante.m_Start, kante.m_End })
                {
                    if (!EntityManager.HasBuffer<Game.Net.ConnectedEdge>(knoten)) continue;
                    foreach (var v in EntityManager.GetBuffer<Game.Net.ConnectedEdge>(knoten, true))
                    {
                        var andere = v.m_Edge;
                        if (andere == t || !eigene.Contains(andere)
                            || !EntityManager.HasComponent<PrefabRef>(andere)) continue;
                        var ap = EntityManager.GetComponentData<PrefabRef>(andere).m_Prefab;
                        if (IstZufahrtsgasse(ap)) continue;
                        if (!EntityManager.HasComponent<NetGeometryData>(ap)) continue;
                        var flags = EntityManager.GetComponentData<NetGeometryData>(ap).m_Flags;
                        if ((flags & Game.Net.GeometryFlags.FlattenTerrain) == 0) return true;
                    }
                }
            }
            return false;
        }

        private bool IstZufahrtsgasse(Entity prefab)
        {
            _prefabSystemFuerSync ??= World.GetOrCreateSystemManaged<PrefabSystem>();
            return _prefabSystemFuerSync.TryGetPrefab<PrefabBase>(prefab, out var p)
                   && p != null
                   && p.name.StartsWith("PLT Zufahrtsgasse", System.StringComparison.Ordinal);
        }

        private PrefabSystem _prefabSystemFuerSync;

        private bool IstPflanze(Entity t)
            => EntityManager.HasComponent<PrefabRef>(t)
               && EntityManager.HasComponent<PlantData>(
                   EntityManager.GetComponentData<PrefabRef>(t).m_Prefab);

        private bool IstLoseObjekt(Entity t)
            => EntityManager.HasComponent<Game.Objects.Object>(t)
               && !EntityManager.HasComponent<Owner>(t)
               && !EntityManager.HasComponent<Game.Routes.TransportStop>(t)
               && !EntityManager.HasComponent<Game.Buildings.Building>(t)
               && !IstPflanze(t);
    }
}
