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
            internal bool Hintergrund;
            /** Wartet wie Hintergrund auf eine Rueckmeldung, braucht aber keinen Neubau-Bauplan. */
            internal bool Tausch;
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
                        if (Mod.Aus("bushaltestellen")) return false;
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
                ["Fahrwege25ImHintergrund"] = new Ausfuehrung
                {
                    // Stillgelegt 2026-10-04, ersetzt durch Schritt 9.
                    Braucht = (lot, traeger, teile) => false,
                    Ausfuehren = (lot, traeger, teile) => { },
                },
                ["Fahrwege25Tauschen"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) => World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().BrauchtTausch(lot),
                    Ausfuehren = (lot, traeger, teile) => World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().Einreihen(lot),
                    Tausch = true,
                },
                /*
                 * Buesche mit Altersstufen haben TreeData und wurden bis
                 * 2026-10-04 wie Baeume gepflanzt (3 m zur Laterne, Baumgruppen,
                 * 30 % am Streifenrand). Neu gesetzt wird nur, wo das die Pflanzung
                 * aendert; danach traegt der Vegetationszettel Version 2.
                 */
                ["PflanzenNachWuchs"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) => _werkzeug.BrauchtPflanzenNachWuchs(lot),
                    Ausfuehren = (lot, traeger, teile) => World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().Einreihen(lot, Tauschart.Pflanzen),
                    Tausch = true,
                },
                /*
                 * Laternen fuer Parkplaetze von vor dem 2026-10-04. Wer schon
                 * welche hat (Testbauten) oder einen Laternenzettel, braucht
                 * nichts. Ist der Standard des Spielers "aus", auch nicht.
                 */
                ["LaternenNachruesten"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) => _werkzeug.BrauchtLaternenNachruesten(lot, teile),
                    Ausfuehren = (lot, traeger, teile) => World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().Einreihen(lot, Tauschart.Laternen),
                    Tausch = true,
                },
                /*
                 * Zettel sagt "Laternen an", am Parkplatz steht keine (1.0.6).
                 * Derselbe Tausch wie Schritt 11; er nimmt die Wahl aus dem Zettel.
                 */
                ["LaternenNachZettel"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) => _werkzeug.BrauchtLaternenNachZettel(lot, teile),
                    Ausfuehren = (lot, traeger, teile) => World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().Einreihen(lot, Tauschart.Laternen),
                    Tausch = true,
                },
                /*
                 * Verworfene Flaechen (Schritt 13): nur die Flaeche selbst wird
                 * ersetzt, siehe ParkingLotFlaechenSyncSystem. Erfolg meldet
                 * das System erst nach CS2s eigener Antwort (Dreiecke da).
                 */
                ["FlaechenFuerCs2"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) => World.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>().Braucht(lot),
                    Ausfuehren = (lot, traeger, teile) => World.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>().Einreihen(lot),
                    Tausch = true,
                },
                ["Fahrwege25MitKosten"] = new Ausfuehrung
                {
                    // Stillgelegt 2026-10-02: der Neubau lief ueber das sichtbare
                    // Werkzeug; ESC/Panel-Schliessen brach ihn nach dem Abriss
                    // der alten Wege ab. Alte Parkplaetze bekommen die neuen
                    // Fahrwege beim eigenen Bearbeiten; der Hintergrund-Neubau
                    // kommt als neuer Schritt.
                    Braucht = (lot, traeger, teile) => false,
                    Ausfuehren = (lot, traeger, teile) => { },
                },
                /*
                 * Der Besitz ALLEIN reicht nicht: im Puffer der Flaeche stehen
                 * sie bis zum naechsten Laden weiter, und jedes `Updated` der
                 * Flaeche wuerfelt sie dort neu. Also auch den Eintrag
                 * herausnehmen und beim Traeger eintragen.
                 */
                ["BaeumeAlternNicht"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) =>
                    {
                        if (!_werkzeug.VegetationVon(lot).NoAging) return false;
                        foreach (var t in teile)
                            if (IstPflanze(t) && _werkzeug.BaumAltert(t)) return true;
                        return false;
                    },
                    Ausfuehren = (lot, traeger, teile) =>
                    {
                        var maske = _werkzeug.VegetationVon(lot).Ages;
                        foreach (var t in teile)
                            if (IstPflanze(t) && _werkzeug.BaumAltert(t))
                                _werkzeug.FriereBaumEin(t, maske);
                    },
                },
                ["ObjekteNichtAnFlaeche"] = new Ausfuehrung
                {
                    Braucht = (lot, traeger, teile) =>
                        AnFlaeche(lot, teile).Count > 0,
                    Ausfuehren = (lot, traeger, teile) =>
                    {
                        foreach (var t in AnFlaeche(lot, teile))
                        {
                            _werkzeug.SetVegetationOwner(t, traeger, lot);
                            // Ohne Zuordnung kennt der Abriss sie nicht.
                            if (!EntityManager.HasComponent<ParkingLotPartRelation>(t))
                                EntityManager.AddComponentData(t, new ParkingLotPartRelation
                                {
                                    Lot = lot,
                                    Carrier = traeger,
                                });
                            if (EntityManager.HasBuffer<Game.Objects.SubObject>(lot))
                            {
                                var puffer = EntityManager.GetBuffer<Game.Objects.SubObject>(lot);
                                for (var i = puffer.Length - 1; i >= 0; i--)
                                    if (puffer[i].m_SubObject == t) puffer.RemoveAt(i);
                            }
                            if (EntityManager.HasBuffer<Game.Objects.SubObject>(traeger))
                            {
                                var puffer = EntityManager.GetBuffer<Game.Objects.SubObject>(traeger);
                                var da = false;
                                for (var i = 0; i < puffer.Length; i++)
                                    if (puffer[i].m_SubObject == t) { da = true; break; }
                                if (!da) puffer.Add(new Game.Objects.SubObject(t));
                            }
                            if (!EntityManager.HasComponent<Updated>(t))
                                EntityManager.AddComponent<Updated>(t);
                        }
                    },
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

        // Aufkleber, Pfeile, Saeulen - was an den Traeger gehoert. Halte-
        // stellen und der Wirtschaftsbegleiter bleiben absichtlich ohne.
        private bool IstTraegerObjekt(Entity t)
            => EntityManager.HasComponent<Game.Objects.Object>(t)
               && !EntityManager.HasComponent<Game.Routes.TransportStop>(t)
               && !EntityManager.HasComponent<Game.Buildings.Building>(t)
               && !IstPflanze(t);

        private bool IstLoseObjekt(Entity t)
            => IstTraegerObjekt(t) && !EntityManager.HasComponent<Owner>(t);

        private bool HaengtAnFlaeche(Entity t, Entity lot)
            => IstTraegerObjekt(t) && EntityManager.HasComponent<Owner>(t)
               && EntityManager.GetComponentData<Owner>(t).m_Owner == lot;

        /*
         * Die Teile-Liste kommt aus `ParkingLotPartRelation`. Eine Saeule, die
         * an der Heftung vorbeilief, hat diese Zuordnung nie bekommen - sie
         * steht nur im SubObject-Puffer der Flaeche. Also beide Quellen.
         */
        private List<Entity> AnFlaeche(Entity lot, List<Entity> teile)
        {
            var treffer = new List<Entity>();
            foreach (var t in teile)
                if (HaengtAnFlaeche(t, lot)) treffer.Add(t);
            if (EntityManager.HasBuffer<Game.Objects.SubObject>(lot))
            {
                var puffer = EntityManager.GetBuffer<Game.Objects.SubObject>(lot);
                for (var i = 0; i < puffer.Length; i++)
                {
                    var t = puffer[i].m_SubObject;
                    if (EntityManager.Exists(t) && HaengtAnFlaeche(t, lot)
                        && !treffer.Contains(t)) treffer.Add(t);
                }
            }
            return treffer;
        }
    }
}
