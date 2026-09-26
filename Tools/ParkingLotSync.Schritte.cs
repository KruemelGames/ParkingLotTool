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
                // Entfaellt, siehe Migrationskatalog. Kein Parkplatz braucht ihn.
                ["GassenknotenEben"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) => false,
                    Ausfuehren = (lot, traeger, teile) => { },
                },
            };

            // Katalog und Ausfuehrung muessen sich decken - sonst bliebe ein
            // Parkplatz fuer immer vor einem Schritt stehen.
            foreach (var schritt in Migrationskatalog.Schritte)
                if (!_ausfuehrungen.ContainsKey(schritt.Name))
                    Mod.log.Error("PLT-Sync: Schritt " + schritt.Nummer + " '"
                        + schritt.Name + "' hat keine Ausfuehrung.");
        }

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
