using System;
using System.Collections.Generic;
using Game.Common;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;

internal static partial class Program
{
    private static void RestlicheWorkerPruefen()
    {
        BestandsUndFlaechenAuftraege();
        HintergrundAuftraege();
    }

    private static void BestandsUndFlaechenAuftraege()
    {
        foreach (var schritt in new[] { 10, 11, 12, 13 })
        foreach (var stand in new[] { -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })
        {
            var w = new World(); var lot = new Entity(4000); Gesund(w, lot);
            if (stand >= 0) w.EntityManager.Set(lot, new ParkingLotDatenstand { Stand = stand });
            var s = w.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
            var b = w.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>();
            var f = w.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>();
            var a = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
            var worker = schritt == 13 ? Auftragsarbeiter.Flaechen : Auftragsarbeiter.Bestand;
            var id = s.TestMigration(lot, schritt, worker, token => schritt == 13
                ? f.Einreihen(lot, token) : b.Einreihen(lot, schritt == 10 ? Tauschart.Pflanzen : Tauschart.Laternen, token));
            Pruefe(s.TestWartet(lot) && s.TestStand(lot) == Math.Max(0, stand), "Annahme schreibt keinen Stand " + schritt + "/" + stand);
            Pruefe(schritt == 13 ? f.TestStart(lot, id) : b.TestStart(lot, id), "Worker startet bestaetigte Kennung");
            if (schritt == 13) { f.TestEnde(true); f.TestEnde(true); }
            else { b.TestAntwort(lot, id, true); b.TestAntwort(lot, id, true); }
            Pruefe(!s.TestWartet(lot) && s.TestStand(lot) == schritt && a.Register.Zaehle().OffeneTeile == 0,
                "genau bestaetigter Migrationsschritt " + schritt + "/" + stand);
            s.TestNutzerFertig(lot);
            Pruefe(a.Register.Zaehle().Erledigt == 1, "ein Nutzerabschluss nach Bestands-/Flaechensync");
        }

        foreach (var worker in new[] { Auftragsarbeiter.Bestand, Auftragsarbeiter.Flaechen })
        foreach (var grund in new[] { Auftragsgrund.Repair, Auftragsgrund.Normalbau, Auftragsgrund.Wiederaufnahme })
        {
            var w = new World(); var lot = new Entity(5000); Gesund(w, lot);
            var a = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
            var b = w.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>();
            var f = w.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>();
            var id = a.Merke(lot, worker, grund);
            a.Uebergib(id, token => worker == Auftragsarbeiter.Bestand ? b.Einreihen(lot, Tauschart.Pflanzen, token) : f.Einreihen(lot, token));
            if (worker == Auftragsarbeiter.Bestand) { b.TestStart(lot, id); b.TestAntwort(lot, id, true); }
            else { f.TestStart(lot, id); f.TestEnde(true); }
            Pruefe(!w.EntityManager.HasComponent<ParkingLotDatenstand>(lot) && a.Register.Zaehle().Erledigt == 1,
                worker + "/" + grund + " schreibt keinen Migrationsstand");
        }

        var welt = new World(); var l = new Entity(6000); Gesund(welt, l);
        var r = welt.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var bst = welt.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>();
        var sync = welt.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        var alt = sync.TestMigration(l, 10, Auftragsarbeiter.Bestand, id => bst.Einreihen(l, Tauschart.Pflanzen, id));
        bst.TestStart(l, alt); var altesErgebnis = bst.TestRechnung(l, alt);
        Pruefe(altesErgebnis(), "aktuelles Task-Ergebnis lesbar");
        r.Preload(); bst.TestLaden(); sync.TestNeueLadung(); r.PufferWiederhergestellt();
        var neu = sync.TestMigration(l, 12, Auftragsarbeiter.Bestand, id => bst.Einreihen(l, Tauschart.Laternen, id));
        bst.TestStart(l, neu);
        Pruefe(!altesErgebnis(), "altes Task-Ergebnis vor Anwendung verworfen");
        bst.TestAntwort(l, alt, true);
        Pruefe(sync.TestWartet(l) && bst.Freigaben == 0, "alte Bestandsantwort gibt neuen Worker nicht frei");
        bst.TestAntwort(l, neu, true);
        Pruefe(sync.TestStand(l) == 12 && bst.Freigaben == 1, "neuer Bestandsauftrag bleibt abschliessbar");

        var fremd = r.Merke(l, Auftragsarbeiter.Bestand, Auftragsgrund.Migration, 12);
        r.Annehmen(fremd, l, Auftragsarbeiter.Bestand); r.Starte(fremd);
        var eigen = sync.TestMigration(l, 12, Auftragsarbeiter.Bestand, id => bst.Einreihen(l, Tauschart.Laternen, id));
        bst.TestStart(l, eigen);
        r.Register.Lies(fremd, out var falscheAntwort);
        Pruefe(!sync.TestAntwortDirekt(falscheAntwort, l, true) && sync.TestWartet(l),
            "andere gueltige Kennung derselben Generation und desselben Lots darf Sync nicht abschliessen");
        bst.TestAntwort(l, fremd, true);
        Pruefe(sync.TestWartet(l) && bst.Freigaben == 1, "fremde aktuelle Kennung gibt Bestandsworker nicht frei");
        bst.TestAntwort(l, eigen, true); r.Beende(fremd, l, false);

        var def = new Entity(6100); welt.EntityManager.Set(def, 0);
        var tool = welt.GetOrCreateSystemManaged<ParkingLotBestandsTauschWerkzeug>();
        int meldungen = 0;
        int fehlerVorher = r.Register.Zaehle().Fehler;
        var idTool = r.Merke(l, Auftragsarbeiter.Bestand, Auftragsgrund.Repair);
        r.Annehmen(idTool, l, Auftragsarbeiter.Bestand); r.Starte(idTool);
        tool.Fertig = (_, token, ok, _) => { meldungen++; r.Beende(token, l, ok); };
        tool.TestStart(l, idTool, def);
        welt.EntityManager.BeimZerstoeren = _ => throw new InvalidOperationException("Definition");
        ParkingLotSchrittmarke.Fehler = true;
        tool.TestEnde(true); tool.TestEnde(true);
        ParkingLotSchrittmarke.Fehler = false;
        Pruefe(!tool.Beschaeftigt && meldungen == 1 && r.Register.Zaehle().Fehler == fehlerVorher + 1, "Bestandswerkzeug frei, ein Fehlschlag trotz Bereinigungs-/Diagnosefehler");
        welt.EntityManager.BeimZerstoeren = null;
        var stale = r.Merke(l, Auftragsarbeiter.Bestand, Auftragsgrund.Repair);
        r.Annehmen(stale, l, Auftragsarbeiter.Bestand); r.Starte(stale);
        tool.TestStart(l, stale, def);
        r.Preload(); r.PufferWiederhergestellt();
        var zerstoert = welt.EntityManager.Zerstoerversuche.Count;
        tool.TestEnde(true);
        Pruefe(!tool.Beschaeftigt && welt.EntityManager.Zerstoerversuche.Count == zerstoert,
            "Bestandsabschluss aus alter Generation bereinigt keine Entity der neuen Welt");

        var w2 = new World(); var lot2 = new Entity(6200); Gesund(w2, lot2);
        var r2 = w2.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var b2 = w2.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>();
        var f2 = w2.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>();
        var s2 = w2.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        var fehlTask = s2.TestMigration(lot2, 10, Auftragsarbeiter.Bestand, id => b2.Einreihen(lot2, Tauschart.Pflanzen, id));
        b2.TestStart(lot2, fehlTask);
        Pruefe(!b2.TestRechnung(lot2, fehlTask, true)() && !s2.TestWartet(lot2) && s2.TestStand(lot2) == 0,
            "Task-Ausnahme beendet den zugeordneten Auftrag ohne Datenstand");
        var fl = s2.TestMigration(lot2, 13, Auftragsarbeiter.Flaechen, id => f2.Einreihen(lot2, id));
        f2.TestStart(lot2, fl);
        Mod.log.InfoFehler = Mod.log.WarnFehler = true;
        f2.TestFehler(); f2.TestFehler();
        Mod.log.InfoFehler = Mod.log.WarnFehler = false;
        Pruefe(!f2.Laeuft && !s2.TestWartet(lot2) && s2.TestStand(lot2) == 0,
            "Flaechen-Pruefungs-/Diagnoseausnahme gibt frei und meldet einmal");
        var staleFlaeche = s2.TestMigration(lot2, 13, Auftragsarbeiter.Flaechen, id => f2.Einreihen(lot2, id));
        f2.TestStart(lot2, staleFlaeche);
        r2.Preload(); r2.PufferWiederhergestellt();
        Pruefe(!f2.TestDarfArbeiten(), "alte Flaechen-Nachpruefung vor ECS-Zugriff abgewiesen");
        f2.TestLaden(); s2.TestNeueLadung();
        var bf = s2.TestMigration(lot2, 11, Auftragsarbeiter.Bestand, id => b2.Einreihen(lot2, Tauschart.Laternen, id));
        b2.TestStart(lot2, bf); b2.Freigabefehler = true;
        try { b2.TestAntwort(lot2, bf, true); }
        catch (InvalidOperationException) { }
        Pruefe(!s2.TestWartet(lot2) && s2.TestStand(lot2) == 0, "Bestandsfreigabe-Ausnahme meldet Fehlschlag statt Erfolg");
    }

    private static void HintergrundAuftraege()
    {
        foreach (var grund in new[] { Auftragsgrund.Repair, Auftragsgrund.Normalbau, Auftragsgrund.Wiederaufnahme })
        {
            var w = new World(); var l = new Entity(7000); Gesund(w, l);
            var h = w.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
            var r = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
            h.Einreihen(l, grund: grund);
            var id = h.TestKennung;
            Pruefe(r.Register.Lies(id, out var daten) && daten.Grund == grund, "Hintergrund nimmt richtigen Grund an");
            Pruefe(h.TestStart(), "Hintergrund startet");
            var ende = h.TestAntwort(true, l); ende(); ende();
            Pruefe(r.Register.Zaehle().Erledigt == 1 && r.Register.Zaehle().OffeneTeile == 0 && h.Offen == 0
                && !w.EntityManager.HasComponent<ParkingLotDatenstand>(l), "Hintergrund " + grund + " kein Migrationsstand, ein Abschluss");
        }

        var pruefwelt = new World(); var prueflot = new Entity(7050); Gesund(pruefwelt, prueflot);
        var pruefBg = pruefwelt.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
        var pruefReg = pruefwelt.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        pruefBg.BauplanFehlt = true; pruefBg.Einreihen(prueflot);
        Pruefe(pruefBg.Offen == 0 && !pruefBg.TestGesperrt(prueflot) && pruefReg.Register.Zaehle().Fehler == 1,
            "fehlender Bauplan lehnt Uebergabe ohne Sperre ab");
        pruefBg.BauplanFehlt = false;
        bool? migrationOk = null;
        var rueckMigration = pruefReg.Merke(prueflot, Auftragsarbeiter.Hintergrund, Auftragsgrund.Migration, 8,
            antwort: (_, _, ok) => { migrationOk = ok; return ok; });
        pruefReg.Uebergib(rueckMigration, id => pruefBg.Einreihen(prueflot, id, true));
        pruefBg.TestStart(); pruefBg.TestRueckweg();
        Pruefe(migrationOk == false, "reiner Rueckweg bestaetigt niemals Migration");
        pruefwelt.EntityManager.Set(prueflot, new ParkingLotOffenerErsatz { Lot = new Entity(7060), Traeger = new Entity(7061) });
        pruefwelt.EntityManager.Set(prueflot, new List<ParkingLotRueckwegkurs> { new() });
        pruefBg.Einreihen(prueflot, true); pruefBg.TestStart();
        ParkingLotNetzRueckweg.Fehlend = 1;
        Pruefe(!pruefBg.TestFehlendePrefabs() && pruefwelt.EntityManager.HasComponent<ParkingLotOffenerErsatz>(prueflot)
            && pruefwelt.EntityManager.HasBuffer<ParkingLotRueckwegkurs>(prueflot),
            "fehlende Prefabs bei offenem Ersatz verwerfen weder Rueckweg noch Ersatzmarker");
        ParkingLotNetzRueckweg.Fehlend = 0; pruefBg.TestRueckweg();

        pruefBg.Einreihen(prueflot); var gateFehler = pruefBg.TestKennung; pruefBg.TestStart();
        pruefBg.TestGate(() => throw new InvalidOperationException("Gate"));
        pruefwelt.GetOrCreateSystemManaged<ParkingLotExklusivesBildSystem>().Arbeit[0](0);
        Pruefe(pruefBg.TestInRuecknahme && pruefReg.DarfAnwenden(gateFehler),
            "Gate-Ausnahme wechselt in bisherigen Ruecknahmepfad, kein vorzeitiger Abschluss");
        pruefBg.TestRueckweg();
        pruefBg.Einreihen(prueflot); var freigabeFehler = pruefBg.TestKennung; pruefBg.TestStart();
        pruefBg.TestDefekteFreigabe();
        try { pruefBg.TestAntwort(true, prueflot)(); }
        catch (InvalidOperationException) { }
        Pruefe(!pruefReg.Gilt(freigabeFehler) && pruefBg.Offen == 0 && !pruefBg.TestGesperrt(prueflot),
            "native Freigabeausnahme beendet Register genau einmal und loest seine Arbeitssperre");

        var welt = new World(); var lot = new Entity(7100); var ersatz = new Entity(7200);
        Gesund(welt, lot); Gesund(welt, ersatz);
        var a = welt.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var bg = welt.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
        var s = welt.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        var migration = s.TestMigration(lot, 8, Auftragsarbeiter.Hintergrund, id => bg.Einreihen(lot, id));
        bg.TestStart(); bg.TestAntwort(true, ersatz)();
        Pruefe(s.TestStand(ersatz) == 8 && !s.TestWartet(lot), "bestaetigter Hintergrund-Ersatz schreibt nur seinen Schritt");

        var alt = a.Merke(lot, Auftragsarbeiter.Hintergrund, Auftragsgrund.Repair);
        a.Uebergib(alt, id => bg.Einreihen(lot, id)); bg.TestStart();
        int wirkungen = 0;
        bg.TestGate(() => ++wirkungen);
        var alterAbschluss = bg.TestAntwort(true);
        var gate = welt.GetOrCreateSystemManaged<ParkingLotExklusivesBildSystem>();
        var rueckruf = gate.Arbeit[0];
        a.Preload(); bg.TestLaden(); s.TestNeueLadung(); a.PufferWiederhergestellt();
        bg.Einreihen(lot); var aktuell = bg.TestKennung; bg.TestStart();
        Pruefe(rueckruf(0) == 0 && wirkungen == 0, "alter Gate-Rueckruf wirkt nicht in neuer Generation");
        alterAbschluss();
        Pruefe(bg.TestKennung == aktuell && a.DarfAnwenden(aktuell), "alter Abschluss beendet keinen neuen Hintergrundauftrag");
        bg.TestAntwort(true)();
        // Auch innerhalb derselben Generation: ein spaeter Gate-Rueckruf aus
        // einem beendeten physischen Versuch darf den Folgeveruch nicht treffen.
        lot = new Entity(7300); Gesund(welt, lot);
        bg.Einreihen(lot); var wieder = bg.TestKennung; bg.TestStart();
        bg.TestGate(() => ++wirkungen); var alterVersuch = gate.Arbeit[1];
        for (int versuch = 1; versuch <= 3; versuch++)
        {
            bg.TestFehler();
            Pruefe(bg.TestInRuecknahme && a.DarfAnwenden(wieder), "Fehler vor Wirkung wechselt in Ruecknahme ohne Registerabschluss");
            welt.EntityManager.Set(lot, new List<ParkingLotRueckwegkurs> { new() });
            bg.TestRueckweg();
            if (versuch < 3)
            {
                Pruefe(bg.TestKennung == wieder && a.DarfAnwenden(wieder)
                    && bg.TestFruehestens == UnityEngine.Time.frameCount + HintergrundTakt.Versuchspause,
                    "Rueckweg und Wiederholung behalten Kennung und bisherige Pause");
                UnityEngine.Time.frameCount += HintergrundTakt.Versuchspause;
                bg.TestStart();
                Pruefe(bg.TestVersuch == versuch + 1, "bisherige Versuchszahl bleibt erhalten");
                Pruefe(alterVersuch(0) == 0 && wirkungen == 0, "Gate des alten Versuchs trotz gleicher Kennung abgewiesen");
            }
        }
        Pruefe(!bg.TestGesperrt(lot) && !a.Gilt(wieder) && a.Register.Zaehle().Fehler == 1,
            "nach drei Versuchen genau ein Fehlschlag, keine neue Registersperre");

        bg.Einreihen(lot, true, grund: Auftragsgrund.Wiederaufnahme);
        var nurRueckweg = bg.TestKennung; bg.TestStart(); bg.TestRueckweg();
        Pruefe(!a.Gilt(nurRueckweg) && a.Register.Zaehle().OffeneTeile == 0, "reiner Wiederaufnahme-Rueckweg schliesst ab");
        // Fuer weitere Faelle eine neue Sitzung mit zurueckgesetzter Dreiversuchsregel.
        a.Preload(); bg.TestLaden(); s.TestNeueLadung(); a.PufferWiederhergestellt();
        bg.Einreihen(lot); var vorUmstellung = bg.TestKennung;
        bg.Einreihen(lot, true);
        Pruefe(bg.TestKennung == vorUmstellung && bg.Offen == 1, "Edit-Abbruch stellt wartenden Auftrag ohne doppelte Kennung um");
        bg.TestStart(); bg.TestRueckweg();
        bg.Einreihen(lot, true, grund: Auftragsgrund.Wiederaufnahme);
        var verloren = bg.TestKennung; bg.TestStart();
        welt.EntityManager.Set(lot, new List<ParkingLotRueckwegkurs> { new() });
        ParkingLotNetzRueckweg.Fehlend = 1;
        Pruefe(bg.TestFehlendePrefabs() && bg.TestKennung == verloren && a.DarfAnwenden(verloren)
            && welt.EntityManager.HasComponent<ParkingLotNeubauFaellig>(lot), "fehlende Prefabs: Marker und Neubau behalten Nutzerauftrag");
        ParkingLotNetzRueckweg.Fehlend = 0;
        bg.TestStart(); bg.TestAntwort(true, lot)();

        // Fehler NACH der Uebernahme laesst den dauerhaften Neubau stehen.
        bg.Einreihen(lot); var nachWirkung = bg.TestKennung; bg.TestStart();
        var traeger = welt.EntityManager.GetComponentData<ParkingLotCarrierReference>(ersatz).Carrier;
        bg.TestFehler(true, ersatz, traeger);
        Pruefe(!a.Gilt(nachWirkung) && welt.EntityManager.Exists(ersatz)
            && welt.EntityManager.HasComponent<ParkingLotNebenarbeitOffen>(ersatz)
            && s.TestStand(ersatz) == 7, "Fehler nach Wirkung behaelt Neubau und Nebenarbeit bei Stand 7");

        bg.Einreihen(lot, true); var gestoppt = bg.TestKennung; bg.TestStart();
        welt.EntityManager.Set(lot, new List<ParkingLotRueckwegkurs> { new() });
        Mod.log.InfoFehler = true;
        try { bg.TestRueckwegFehler(); }
        finally { Mod.log.InfoFehler = false; }
        Pruefe(!a.Gilt(gestoppt) && bg.TestGesperrt(lot)
            && welt.EntityManager.HasBuffer<ParkingLotRueckwegkurs>(lot),
            "Rueckwegfehler behaelt bisherige Sperre und Sicherung, Registerauftrag endet");
    }
}
