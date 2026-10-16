using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;

internal static partial class Program
{
    private static void StarteFahrweg(World w, Entity lot)
    {
        var a = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var id = a.Merke(lot, Auftragsarbeiter.Fahrweg, Auftragsgrund.Migration, 9,
            antwort: w.GetOrCreateSystemManaged<ParkingLotSyncSystem>().TestAntwort);
        a.Uebergib(id, token => w.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().Einreihen(lot, token));
    }

    private static void AuftraegePruefen()
    {
        var r = new Auftragsregister<int>();
        Pruefe(!r.Bereit, "Register wartet auf native Puffer");
        bool zuFrueh = false;
        try { r.Merke(1, Auftragsarbeiter.Hintergrund, Auftragsgrund.Wiederaufnahme, 0, 1); }
        catch (InvalidOperationException) { zuFrueh = true; }
        Pruefe(zuFrueh && r.Offen == 0, "gespeicherte Absicht wird vor Pufferfreigabe nicht aktiviert");
        r.PufferWiederhergestellt();
        var nutzer = r.NeuerNutzer();
        var id = r.Merke(1, Auftragsarbeiter.Fahrweg, Auftragsgrund.Migration, 9, 2, nutzer);
        Pruefe(!r.Starte(id, 3), "Start braucht bestaetigte Annahme");
        Pruefe(!r.Annehmen(id, 2, Auftragsarbeiter.Fahrweg, 3)
            && !r.Annehmen(id, 1, Auftragsarbeiter.Strasse, 3), "Annahme prueft Lot und Worker");
        Pruefe(r.Annehmen(id, 1, Auftragsarbeiter.Fahrweg, 4) && !r.DarfAnwenden(id), "Annahme allein erlaubt keine Weltwirkung");
        Pruefe(r.Starte(id, 5) && r.DarfAnwenden(id), "bestaetigter Start erlaubt Wirkung");
        Pruefe(r.Fortschritt(id, "Apply", 6) && !r.Fortschritt(id, "Apply", 99), "gleiches Lebenszeichen ist kein neuer Fortschritt");
        r.Lies(id, out var info);
        Pruefe(info.Vorgemerkt == 2 && info.Gestartet == 5 && info.LetzteAenderung == 6, "Zeit kommt als Parameter, kein Update-Herzschlag");
        int wirkungen = 0;
        r.Beende(id, 7, a =>
        {
            wirkungen++;
            Pruefe(!r.Beende(id, 8, b => { wirkungen++; return true; }), "reentranter Doppelabschluss abgewiesen");
            return true;
        });
        Pruefe(wirkungen == 1 && !r.Beende(id, 9, a => { wirkungen++; return true; }) && r.Offen == 0, "genau eine Wirkung und vollstaendige Freigabe");
        var zweiter = r.Merke(1, Auftragsarbeiter.Strasse, Auftragsgrund.Migration, 14, 10, nutzer);
        r.Annehmen(zweiter, 1, Auftragsarbeiter.Strasse, 11); r.Starte(zweiter, 12); r.Beende(zweiter, 13, a => true);
        Pruefe(r.Zaehle().Gesamt == 1 && r.Zaehle().Erledigt == 0 && r.NutzerBeendet(nutzer, true)
            && r.Zaehle().Erledigt == 1, "zwei Migrationen zaehlen als ein Nutzerauftrag");
        var alt = r.Merke(1, Auftragsarbeiter.Bestand, Auftragsgrund.Repair, 0, 14);
        r.Annehmen(alt, 1, Auftragsarbeiter.Bestand, 15); r.Starte(alt, 16);
        var generation = r.Generation; r.NeuesLaden(17);
        Pruefe(r.Generation == generation + 1 && !r.Bereit && !r.DarfAnwenden(alt), "Laden entwertet alte Ausfuehrer vor Wiederaufnahme");
        r.PufferWiederhergestellt();
        var neu = r.Merke(1, Auftragsarbeiter.Bestand, Auftragsgrund.Wiederaufnahme, 0, 18);
        var falscheGeneration = new Auftragskennung(neu.Id, generation);
        Pruefe(!r.Lies(falscheGeneration, out _) && !r.Beende(alt, 19, a => { wirkungen++; return true; }),
            "ID und Generation werden unabhaengig geprueft");
        Pruefe(r.Zaehle().Gesamt == 1 && r.Zaehle().Erledigt == 0, "neue Generation hat neue Fortschrittsgruppen");
        var ausnahme = r.Merke(3, Auftragsarbeiter.Strasse, Auftragsgrund.Normalbau, 0, 20);
        r.Annehmen(ausnahme, 3, Auftragsarbeiter.Strasse, 21); r.Starte(ausnahme, 22);
        try { r.Beende(ausnahme, 23, a => throw new InvalidOperationException("Empfaenger")); }
        catch (InvalidOperationException) { }
        Pruefe(!r.Lies(ausnahme, out _) && r.Zaehle().Fehler == 1, "Ausnahme beim Abschluss gibt Registereintrag trotzdem frei");

        for (int i = 0; i < 1000; i++)
        {
            var kurz = r.Merke(i, Auftragsarbeiter.Strasse, Auftragsgrund.Normalbau, 0, 24);
            r.Annehmen(kurz, i, Auftragsarbeiter.Strasse, 25); r.Starte(kurz, 26);
            r.Beende(kurz, 27, a => true);
        }
        Pruefe(r.Zaehle().Gesamt == 1002 && r.Zaehle().Erledigt == 1001 && r.OffeneNutzer == 1,
            "1000 fertige Nutzerauftraege behalten keine Lot-Referenzen; offene Wiederaufnahme bleibt erhalten");

        AdapterUebergaben();
        MigrationUndNormalbau();
        AlteFahrwegAntwort();
    }

    private static void AdapterUebergaben()
    {
        var w = new World(); var a = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var lot = new Entity(2000); int antworten = 0;
        Auftragskennung Merke() => a.Merke(lot, Auftragsarbeiter.Strasse, Auftragsgrund.Repair,
            antwort: (x, neu, ok) => { antworten++; return ok; });
        var id = Merke();
        Pruefe(!a.Uebergib(id, token => false) && antworten == 1 && a.Register.Offen == 0, "synchrone Ablehnung schliesst registrierten Auftrag einmal ab");
        id = Merke();
        Pruefe(!a.Uebergib(id, token => true) && antworten == 2 && a.Register.Offen == 0, "verlorene Uebergabe ohne Annahme wird sofort als Fehler abgeschlossen");
        id = Merke();
        Pruefe(!a.Uebergib(id, token => throw new InvalidOperationException("vor Annahme"))
            && antworten == 3 && a.Register.Offen == 0, "Uebergabeausnahme vor Annahme verliert keinen Auftrag");
        id = Merke();
        a.Uebergib(id, token =>
        {
            a.Annehmen(token, lot, Auftragsarbeiter.Strasse); a.Starte(token);
            a.Beende(token, lot, true); return true;
        });
        Pruefe(antworten == 4 && a.Register.Offen == 0, "synchrone Antwort waehrend Uebergabe bleibt zugeordnet");
        id = Merke();
        Pruefe(a.Uebergib(id, token =>
        {
            a.Annehmen(token, lot, Auftragsarbeiter.Strasse);
            throw new InvalidOperationException("nach Annahme, Worker besitzt Warteschlange");
        }) && a.Gilt(id), "Ausnahme nach Annahme entzieht laufendem Worker nicht die Kennung");
        a.Starte(id); a.Beende(id, lot, true);
        Pruefe(antworten == 5 && a.Register.Offen == 0, "angenommener Worker kann nach Uebergabeausnahme normal abschliessen");
        id = Merke();
        Pruefe(a.Uebergib(id, token =>
        {
            a.Annehmen(token, lot, Auftragsarbeiter.Strasse);
            return false; // Widerspruch nach verbindlicher Annahme, noch kein Abbruch.
        }) && a.Gilt(id), "bestaetigte Annahme behaelt Vorrang vor widerspruechlichem Rueckgabewert");
        a.Starte(id); a.Beende(id, lot, false);
        Pruefe(antworten == 6 && a.Register.Offen == 0, "Worker bestaetigt seinen tatsaechlichen Abschluss");
    }

    private static void Gesund(World w, Entity lot)
    {
        var carrier = new Entity(lot.Index + 10000);
        w.EntityManager.Set(lot, new ParkingLotCarrierReference { Carrier = carrier });
        w.EntityManager.Set(lot, new ParkingLotBuildReceipt()); w.EntityManager.Set(carrier, 0);
    }

    private static void MigrationUndNormalbau()
    {
        var w = new World(); var a = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var s = w.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        var e = w.GetOrCreateSystemManaged<ParkingLotBuildingEconomySystem>();
        var lot = new Entity(2100); Gesund(w, lot);
        // Fehlende Komponente und alle historischen Datenstaende bleiben exakt lesbar.
        Pruefe(s.TestStand(lot) == 0, "fehlender Datenstand wird unveraendert als 0 behandelt");
        for (int stand = 0; stand <= 15; stand++)
        {
            w.EntityManager.Set(lot, new ParkingLotDatenstand { Stand = stand });
            Pruefe(s.TestStand(lot) == stand, "historischer Datenstand " + stand + " unveraendert");
        }
        w.EntityManager.Set(lot, new ParkingLotDatenstand { Stand = 7 });
        var normal = e.TestNormal(lot); e.TestAntwort(lot, normal, true);
        Pruefe(s.TestStand(lot) == 7 && e.AnschluesseOffen == 0 && a.Register.Offen == 0, "Normalbau-Strassenabschluss schreibt keinen Migrationsschritt");
        foreach (int schritt in new[] { 14, 15 })
        {
            var id = s.TestMigration(lot, schritt, Auftragsarbeiter.Strasse, token => e.StrassensyncEinreihen(lot, token));
            Pruefe(s.TestWartet(lot) && e.TestAnlauf(lot), "Migration vor Uebergabe zugeordnet und Worker gestartet");
            e.TestAntwort(lot, id, true);
            Pruefe(!s.TestWartet(lot) && s.TestStand(lot) == schritt && e.AnschluesseOffen == 0,
                "Strassenabschluss schreibt genau seinen Schritt " + schritt);
            var vor = w.EntityManager.Sets + w.EntityManager.Adds;
            e.TestAntwort(lot, id, true);
            Pruefe(vor == w.EntityManager.Sets + w.EntityManager.Adds, "doppelte Strassenantwort schreibt nichts");
            s.TestNutzerFertig(lot);
        }
        var ersatz = new Entity(2200); Gesund(w, ersatz);
        var tokenErsatz = s.TestMigration(lot, 14, Auftragsarbeiter.Fahrweg, id =>
        { a.Annehmen(id, lot, Auftragsarbeiter.Fahrweg); a.Starte(id); return true; });
        a.Beende(tokenErsatz, ersatz, true);
        Pruefe(s.TestStand(ersatz) == 14 && s.TestStand(lot) == 15 && !s.TestWartet(lot), "Ersatz-Lot bekommt den bestaetigten Schritt, altes bleibt unveraendert");
        s.TestNutzerFertig(ersatz);
        foreach (var grund in new[] { Auftragsgrund.Repair, Auftragsgrund.Normalbau, Auftragsgrund.Wiederaufnahme })
        {
            var id = a.Merke(ersatz, Auftragsarbeiter.Strasse, grund);
            a.Annehmen(id, ersatz, Auftragsarbeiter.Strasse); a.Starte(id); a.Beende(id, ersatz, true);
            Pruefe(s.TestStand(ersatz) == 14, grund + " ohne Migration veraendert keinen Schritt");
        }
        var alt = s.TestMigration(lot, 15, Auftragsarbeiter.Strasse, id => e.StrassensyncEinreihen(lot, id));
        e.TestAnlauf(lot);
        a.Preload(); s.TestNeueLadung();
        Pruefe(!a.Register.Bereit, "zentraler Preload sperrt Annahme bis Pufferwiederherstellung");
        a.PufferWiederhergestellt();
        var aktuell = s.TestMigration(lot, 14, Auftragsarbeiter.Strasse, id => e.StrassensyncEinreihen(lot, id));
        e.TestAnlauf(lot);
        e.TestAntwort(lot, alt, true);
        Pruefe(!a.Beende(alt, lot, true), "abgeschlossene alte Kennung kann nicht erneut antworten");
        Pruefe(s.TestWartet(lot) && s.TestStand(lot) == 15, "alte Antworten koennen neuen registrierten Schritt nicht verbuchen");
        e.TestAntwort(lot, aktuell, true);
        Pruefe(s.TestStand(lot) == 14 && !s.TestWartet(lot), "neue Generation bleibt nach alter Antwort abschliessbar");
        s.TestNutzerFertig(lot);

        var fehlerLot = new Entity(2300); Gesund(w, fehlerLot);
        var fehler = s.TestMigration(fehlerLot, 14, Auftragsarbeiter.Strasse, id => e.StrassensyncEinreihen(fehlerLot, id));
        e.TestAnlauf(fehlerLot);
        w.EntityManager.VorSchreiben = () => throw new InvalidOperationException("vor Datenstandwirkung");
        e.TestAntwort(fehlerLot, fehler, true);
        w.EntityManager.VorSchreiben = null;
        s.TestPflege();
        Pruefe(s.TestStand(fehlerLot) == 0 && s.TestGescheitert(fehlerLot) && !s.TestWartet(fehlerLot)
            && e.AnschluesseOffen == 0, "Schreibausnahme beendet einmal mit Fehler und laesst Stand unveraendert");
        var diagLot = new Entity(2400); Gesund(w, diagLot);
        Mod.Optionen.AutomatischSynchronisieren = true;
        s.BeiMeldung = () => throw new InvalidOperationException("Diagnose nach Wirkung");
        var diag = s.TestMigration(diagLot, 15, Auftragsarbeiter.Strasse, id => e.StrassensyncEinreihen(diagLot, id));
        e.TestAnlauf(diagLot); e.TestAntwort(diagLot, diag, true);
        s.BeiMeldung = null; Mod.Optionen.AutomatischSynchronisieren = false;
        Pruefe(s.TestStand(diagLot) == 15 && !s.TestWartet(diagLot) && !s.TestGescheitert(diagLot)
            && e.AnschluesseOffen == 0, "Ausnahme nach bestaetigter Wirkung erzeugt keinen zweiten Fehlerabschluss");
        s.TestNutzerFertig(diagLot);

        var synchron = new Entity(2450); Gesund(w, synchron);
        s.TestMigration(synchron, 14, Auftragsarbeiter.Strasse, id =>
        {
            a.Annehmen(id, synchron, Auftragsarbeiter.Strasse); a.Starte(id);
            a.Beende(id, synchron, true);
            return true;
        });
        Pruefe(!s.TestWartet(synchron) && s.TestStand(synchron) == 14,
            "echte Sync-Zuordnung besteht schon bei synchroner Antwort aus der Uebergabe");
        s.TestNutzerFertig(synchron);
        foreach (bool behauptet in new[] { false, true })
        {
            var verloren = new Entity(behauptet ? 2451 : 2452); Gesund(w, verloren);
            s.TestMigration(verloren, 15, Auftragsarbeiter.Strasse, id => behauptet);
            s.TestPflege();
            Pruefe(!s.TestWartet(verloren) && s.TestGescheitert(verloren) && s.TestStand(verloren) == 0,
                "Ablehnung oder nicht bestaetigte Uebergabe laesst keinen wartenden Sync zurueck");
        }
        var abgeloest = new Entity(2460); Gesund(w, abgeloest);
        var alterNormalbau = e.TestNormal(abgeloest);
        var migration = s.TestMigration(abgeloest, 15, Auftragsarbeiter.Strasse, id => e.StrassensyncEinreihen(abgeloest, id));
        e.TestAnlauf(abgeloest);
        e.TestAntwort(abgeloest, alterNormalbau, true);
        Pruefe(s.TestWartet(abgeloest) && s.TestStand(abgeloest) == 0,
            "abgeloester Normalbau kann laufende Migration derselben Generation nicht bestaetigen");
        var doppelt = a.Merke(abgeloest, Auftragsarbeiter.Strasse, Auftragsgrund.Repair);
        Pruefe(!a.Uebergib(doppelt, id => e.StrassensyncEinreihen(abgeloest, id)) && a.DarfAnwenden(migration),
            "beschaeftigter Strassenworker lehnt neuen Auftrag ab und behaelt seinen bisherigen");
        e.TestAntwort(abgeloest, migration, true); s.TestNutzerFertig(abgeloest);
        Pruefe(s.TestStand(abgeloest) == 15, "urspruengliche Migration bleibt nach Doppeluebergabe abschliessbar");
    }

    private static void AlteFahrwegAntwort()
    {
        var w = new World(); var a = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var t = w.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>();
        var tool = w.GetOrCreateSystemManaged<ParkingLotFahrwegTauschWerkzeug>();
        var s = w.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        var lot = new Entity(2500); Gesund(w, lot);
        var kante = new Entity(2501); var pf = new Entity(2502); var ziel = new Entity(2503); var definition = new Entity(2504);
        w.EntityManager.Set(lot, new List<Game.Net.SubNet> { new() { m_SubNet = kante } });
        w.EntityManager.Set(kante, new Game.Net.Edge()); w.EntityManager.Set(kante, new PrefabRef { m_Prefab = pf });
        w.EntityManager.Set(kante, new Owner { m_Owner = lot }); w.EntityManager.Set(pf, 0); w.EntityManager.Set(ziel, 0);
        const string name = "Invisible Car Path - 1xTwoway";
        w.GetOrCreateSystemManaged<PrefabSystem>().Prefabs[pf] = new PrefabBase { name = name };
        w.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>().Ziele[name] = ziel;
        var id = s.TestMigration(lot, 9, Auftragsarbeiter.Fahrweg, token => t.Einreihen(lot, token));
        t.Tick();
        var spaeteAntwort = tool.Fertig;
        w.EntityManager.Set(definition, 0); tool.MerkeDefinition(definition);
        a.Preload();
        Pruefe(tool.TestVeraltetenVerwerfen() && !tool.Beschaeftigt && w.EntityManager.Exists(definition),
            "alte Fahrweg-ID wird vor Bereinigung verworfen, fremde IDs der neuen Welt bleiben unberuehrt");
        t.Preload(); s.TestNeueLadung(); a.PufferWiederhergestellt();
        var neu = s.TestMigration(lot, 9, Auftragsarbeiter.Fahrweg, token => t.Einreihen(lot, token));
        t.Tick();
        spaeteAntwort(lot, true, "alte Antwort");
        Pruefe(s.TestWartet(lot) && tool.Beschaeftigt && a.DarfAnwenden(neu), "alte Fahrwegantwort beendet oder entsperrt neuen Tausch nicht");
        w.EntityManager.Set(kante, new PrefabRef { m_Prefab = ziel });
        tool.PruefeJetzt();
        Pruefe(!tool.Beschaeftigt && !s.TestWartet(lot) && s.TestStand(lot) == 9 && t.Offen == 0,
            "echter Fahrwegabschluss gibt Werkzeug frei und bestaetigt nur Schritt 9");
    }
}
