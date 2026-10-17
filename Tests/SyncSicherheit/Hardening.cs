using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using ParkingLotTool.Tools;
using Unity.Entities;

internal static partial class Program
{
    private static void Hardening()
    {
        var w = new World();
        var em = w.EntityManager;
        var lot = new Entity(1000);
        var kante = new Entity(1001);
        var ziel = new Entity(1002);
        var d1 = new Entity(1003);
        var d2 = new Entity(1004);
        em.Set(lot, 0);
        em.Set(kante, new Owner { m_Owner = lot });
        var werkzeug = w.GetOrCreateSystemManaged<ParkingLotFahrwegTauschWerkzeug>();
        var plan = new List<(Entity, Entity, Entity)> { (kante, ziel, lot) };
        int antworten = 0;
        bool erfolg = true;
        werkzeug.Fertig = (e, ok, text) =>
        {
            antworten++;
            erfolg = ok;
            Pruefe(!werkzeug.Beschaeftigt && e == lot, "Fahrweg vor Rueckmeldung freigegeben und Lot erhalten");
        };
        werkzeug.Starte(lot, plan);
        werkzeug.PruefeJetzt();
        Pruefe(antworten == 1 && !erfolg, "fehlendes PrefabRef beendet echte Fahrwegpruefung einmal mit Fehler");

        werkzeug.Starte(lot, plan);
        em.Set(d1, 0); em.Set(d2, 0);
        werkzeug.MerkeDefinition(d1); werkzeug.MerkeDefinition(d2);
        em.BeimZerstoeren = e => { if (e == d1) throw new InvalidOperationException("defekte Definition"); };
        werkzeug.BeendeJetzt(true);
        Pruefe(antworten == 2 && !erfolg && !werkzeug.Beschaeftigt, "Bereinigungsfehler gibt Worker frei und meldet Fehlschlag");
        Pruefe(em.Zerstoerversuche.Contains(d2) && !em.Exists(d2), "Bereinigung setzt nach erster defekter Definition fort");
        em.BeimZerstoeren = null;
        werkzeug.BeendeJetzt(false);
        Pruefe(antworten == 2, "beendeter Worker meldet nicht doppelt");

        em.Set(kante, new PrefabRef { m_Prefab = ziel });
        werkzeug.Starte(lot, plan);
        int versuche = 0;
        werkzeug.Fertig = (e, ok, text) => { versuche++; throw new InvalidOperationException("Empfaenger"); };
        var empfaengerFehler = false;
        try { werkzeug.PruefeJetzt(); } catch (InvalidOperationException) { empfaengerFehler = true; }
        werkzeug.BeendeJetzt(false);
        Pruefe(empfaengerFehler && versuche == 1 && !werkzeug.Beschaeftigt, "werfender Empfaenger wird nach erfolgreicher Pruefung nicht erneut aufgerufen");

        werkzeug.Starte(lot, plan);
        werkzeug.Fertig = (e, ok, text) => antworten++;
        Mod.log.InfoFehler = true; Mod.log.WarnFehler = true;
        werkzeug.PruefeJetzt();
        Mod.log.InfoFehler = false; Mod.log.WarnFehler = false;
        Pruefe(antworten == 3 && !werkzeug.Beschaeftigt, "defekter Logger verhindert Fahrwegabschluss nicht");
        werkzeug.Starte(lot, plan);
        ParkingLotSchrittmarke.Fehler = true;
        werkzeug.PruefeJetzt();
        ParkingLotSchrittmarke.Fehler = false;
        Pruefe(antworten == 4 && !werkzeug.Beschaeftigt, "defekte Schrittspur verhindert Fahrwegabschluss nicht");

        var sync = w.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        var lot2 = new Entity(1010);
        int indexaufrufe = 0;
        sync.IndexBauen = () =>
        {
            if (++indexaufrufe == 1) throw new InvalidOperationException("Teileindex");
            return new Dictionary<Entity, List<Entity>>();
        };
        sync.StarteDispatcher(lot, lot2);
        for (var i = 0; i < 10 && sync.DispatcherOffen > 0; i++) sync.DispatcherBild();
        Pruefe(sync.Fehlerlots.Count == 1 && sync.Fehlerlots[0] == lot,
            "Indexfehler ist dem entnommenen vorgemerkten Lot zugeordnet");
        Pruefe(sync.Synclots.Count == 1 && sync.Synclots[0] == lot2
            && sync.DispatcherErledigt == 2 && sync.DispatcherOffen == 0,
            "nach Indexfehler laeuft naechster Auftrag weiter und beide sind gezaehlt");
        sync.BeimScheitern = () => throw new InvalidOperationException("Fehlermeldung");
        sync.IndexBauen = () => throw new InvalidOperationException("Index erneut");
        sync.StarteDispatcher(lot);
        try { sync.DispatcherBild(); } catch (InvalidOperationException) { }
        Pruefe(sync.DispatcherErledigt == 3 && sync.Fehlerlots.Count == 2,
            "Fortschritt und Fehlerzuordnung bleiben auch bei werfender Fehlerdiagnose erhalten");
        sync.BeimScheitern = null;

        var economy = w.GetOrCreateSystemManaged<ParkingLotBuildingEconomySystem>();
        economy.Kandidaten = () => throw new InvalidOperationException("Kandidatendiagnose");
        economy.StarteAnschluss(lot, true);
        economy.BeendeAnschluss(lot, false);
        Pruefe(economy.AnschluesseOffen == 0 && sync.Antworten.Count == 1 && !sync.Antworten[0],
            "werfende Kandidatensuche verhindert echte Strassenabschlussmeldung nicht");
        economy.BeendeAnschluss(lot, false);
        Pruefe(sync.Antworten.Count == 1, "Strassenabschluss meldet nur einmal");
        economy.StarteAnschluss(lot, false);
        economy.BeendeAnschluss(lot, false);
        Pruefe(economy.AnschluesseOffen == 0 && sync.Antworten.Count == 1,
            "Normalbauanschluss bleibt auch im Diagnosefehler ein Auftrag ohne Sync-Antwort");
        economy.StarteAnschluss(lot, true);
        Mod.log.InfoFehler = true; Mod.log.WarnFehler = true;
        economy.BeendeAnschluss(lot, true);
        Mod.log.InfoFehler = false; Mod.log.WarnFehler = false;
        Pruefe(sync.Antworten.Count == 2 && sync.Antworten[1], "Loggerfehler verhindert Strassenerfolg nicht");
        economy.StarteAnschluss(lot, true);
        sync.AntwortFehler = true;
        try { economy.BeendeAnschluss(lot, true); } catch (InvalidOperationException) { }
        economy.BeendeAnschluss(lot, true);
        sync.AntwortFehler = false;
        Pruefe(sync.Antworten.Count == 3 && economy.AnschluesseOffen == 0,
            "werfender Strassenempfaenger bekommt keinen zweiten Abschluss");
    }
}
