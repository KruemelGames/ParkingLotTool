using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;
using Unity.Mathematics;

internal static partial class Program
{
    private static int _fehler, _pruefungen;
    private static void Pruefe(bool ok, string name)
    { _pruefungen++; if (!ok) { _fehler++; Console.WriteLine("FEHLER: " + name); } }

    // Eine unbehandelte Ausnahme (etwa aus einer Mutation) waere ein Absturz mit
    // Windows-Fehlerfenster. Hier wird sie ein gewoehnlicher Testfehler mit Exit 1.
    private static int Main()
    {
        try { return Ablauf(); }
        catch (Exception x)
        {
            Console.WriteLine("FEHLER: unbehandelte Ausnahme: " + x);
            return 1;
        }
    }

    private static int Ablauf()
    {
        // 360 Richtungen, beidseitig, 6 Breiten bis 64 m. Gemessen wird
        // CS2s Eingang (+Z * Tiefe * 4), nicht der errechnete Mittelpunkt.
        foreach (var breite in new[] { 4f, 8f, 16f, 24f, 32f, 64f })
        for (var winkel = 0; winkel < 360; winkel++)
        foreach (var seite in new[] { -1f, 1f })
        foreach (var tiefe in new[] { 1, 3 })
        {
            var normal = new float2(math.cos(math.radians(winkel)), math.sin(math.radians(winkel))) * seite;
            var achse = new float2(4200f, -4100f);
            var rand = achse + normal * (breite / 2f);
            var ok = BegleiterAnbindung.Lage(rand, achse, achse + normal * 70f, tiefe, out var mitte, out var vorne);
            var rotation = quaternion.LookRotationSafe(new float3(vorne.x, 0, vorne.y), math.up());
            var eingang = new float3(mitte.x, 0, mitte.y) + math.mul(rotation, new float3(0, 0, tiefe * 4f));
            Pruefe(ok && math.distance(eingang.xz, rand + normal * 0.5f) < 0.003f,
                "Eingang am fertigen Rand: " + breite + "/" + winkel + "/" + seite + "/" + tiefe);
        }
        Pruefe(!BegleiterAnbindung.Lage(new(1, 0), default, new(-10, 0), 1, out _, out _), "gegenueberliegende Strassenseite abgelehnt");
        Pruefe(!BegleiterAnbindung.Lage(default, default, new(10, 0), 1, out _, out _), "Nullbreite abgelehnt");
        Pruefe(!BegleiterAnbindung.Lage(new(float.NaN, 0), default, new(10, 0), 1, out _, out _), "NaN abgelehnt");

        Pruefe(SyncWache.Offen(0, 0, 0, 1, 0) == 1, "nur Flaechensync sperrt Panel und Reparatur");
        Pruefe(SyncWache.Offen(0, 0, 0, 0, 1) == 1, "nur Strassenanschluss sperrt Panel");
        Pruefe(SyncWache.Offen(1, 0, 0, 0, 0) == 1, "eingereihter Auftrag zwischen zwei Systemphasen bleibt aktiv");
        Pruefe(SyncWache.Offen(3, 1, 1, 1, 0) == 3, "wartende und laufende Auftraege nicht doppelt zaehlen");
        Pruefe(!SyncWache.AntwortErwartet(false, true) && !SyncWache.AntwortErwartet(true, false), "Ladewechsel und Antworten ohne Auftrag ignoriert");
        Pruefe(SyncWache.AntwortErwartet(true, true), "aktiver Auftrag kann abgeschlossen werden");

        var w = new World(); var em = w.EntityManager;
        var lot = new Entity(1); var carrier = new Entity(2); var edge = new Entity(3); var pf = new Entity(4); var ziel = new Entity(5);
        em.Set(lot, new ParkingLotCarrierReference { Carrier = carrier }); em.Set(carrier, 0);
        em.Set(lot, new List<SubNet> { new() { m_SubNet = edge } });
        em.Set(edge, new Edge()); em.Set(edge, new Owner { m_Owner = lot }); em.Set(edge, new PrefabRef { m_Prefab = pf });
        em.Set(pf, 0);
        const string name = "Invisible Car Path - 1xTwoway";
        w.GetOrCreateSystemManaged<PrefabSystem>().Prefabs[pf] = new PrefabBase { name = name };
        var t = w.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>();
        var tool = w.GetOrCreateSystemManaged<ParkingLotFahrwegTauschWerkzeug>();
        var sync = w.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        Pruefe(t.BrauchtTausch(lot), "alter Fahrweg bleibt noetig obwohl Zielklon noch fehlt");
        Pruefe(t.Plan(lot) == null, "keine halbe oder leere Migration wenn Klon fehlt");
        UnityEngine.Time.frameCount = 0; t.Einreihen(lot); t.Tick();
        Pruefe(t.Offen == 1 && sync.Antworten.Count == 0 && tool.Starts == 0, "nicht bereiter Klon wartet ohne falschen Erfolg");
        w.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>().Ziele[name] = ziel; em.Set(ziel, 0);
        w.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>().Laeuft = true; t.Tick();
        Pruefe(tool.Starts == 0, "keine parallele Uebernahme mit laufendem Flaechentausch");
        w.GetOrCreateSystemManaged<ParkingLotFlaechenSyncSystem>().Laeuft = false; t.Tick();
        Pruefe(tool.Starts == 1 && t.Plan(lot).Count == 1, "spaeter bereiter Klon startet denselben Auftrag");
        t.Preload();
        Pruefe(t.Offen == 0 && !tool.Beschaeftigt, "Laden verwirft alte Warteschlange und Werkzeug");
        w.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>().Ziele.Clear();
        UnityEngine.Time.frameCount = 10; t.Einreihen(lot);
        UnityEngine.Time.frameCount = 610; t.Tick();
        Pruefe(t.Offen == 0 && sync.Antworten.Count == 1 && !sync.Antworten[0], "fehlender Klon endet begrenzt mit Fehlschlag");
        Pruefe(ParkingLotSyncGueltigkeit.Bestand(em, lot, carrier), "gesunder Bestand gueltig");
        em.Set(carrier, new Deleted());
        Pruefe(!ParkingLotSyncGueltigkeit.Bestand(em, lot, carrier), "zum Abriss vorgemerkter Besitzer sperrt Uebernahme");
        em.Remove(carrier);
        Pruefe(!ParkingLotSyncGueltigkeit.Bestand(em, lot, carrier), "verschwundener Besitzer sperrt Uebernahme");
        em.Set(carrier, 0); em.Set(lot, new ParkingLotCarrierReference { Carrier = new Entity(8) });
        Pruefe(!ParkingLotSyncGueltigkeit.Bestand(em, lot, carrier), "ersetzter Besitzer sperrt alten Auftrag");
        em.Set(edge, new Deleted());
        Pruefe(!t.BrauchtTausch(lot), "geloeschter Fahrweg wird nicht erneut migriert");

        // F3TK/DBJT: neuer Parkplatz samt Traeger und Bauzettel, aber OHNE
        // ParkingLotDatenstand. Set auf einen fehlenden Typ wirft im Container;
        // im Release-ECS kann derselbe Fehler den nativen Speicher beschaedigen.
        var neu = new Entity(20); var neuerTraeger = new Entity(21);
        em.Set(neu, new ParkingLotCarrierReference { Carrier = neuerTraeger });
        em.Set(neu, new ParkingLotBuildReceipt()); em.Set(neuerTraeger, 0);
        Pruefe(!em.HasComponent<ParkingLotDatenstand>(neu), "neuer Ersatz beginnt ohne Datenstand");
        Pruefe(ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger), "Hintergrundabschluss legt fehlenden Datenstand an");
        Pruefe(em.GetComponentData<ParkingLotDatenstand>(neu).Stand == 7
            && em.HasComponent<ParkingLotNebenarbeitOffen>(neu)
            && em.Adds == 2 && em.Sets == 0, "Stand 7 und offene Nebenarbeit werden vor weiteren Migrationen gespeichert");
        Pruefe(ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger)
            && em.Adds == 2 && em.Sets == 1, "Fehlerpfad kann denselben Abschluss erneut merken ohne doppelten Tag");

        var vorher = em.Adds + em.Sets;
        Pruefe(!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, new Entity(99)), "vertauschter Traeger verhindert Abschluss");
        em.Set(neuerTraeger, new Deleted());
        Pruefe(!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger), "Abriss des Traegers verhindert Abschluss");
        em.Remove(neuerTraeger);
        Pruefe(!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger), "toter Traeger verhindert Abschluss");
        em.Set(neuerTraeger, 0); em.Set(neu, new Game.Tools.Temp());
        Pruefe(!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger), "Temp-Lot bekommt keinen dauerhaften Datenstand");
        em.Data[neu].Remove(typeof(Game.Tools.Temp)); em.Data[neu].Remove(typeof(ParkingLotBuildReceipt));
        Pruefe(!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger), "Lot ohne fertigen Bauzettel verhindert Abschluss");
        em.Set(neu, new ParkingLotBuildReceipt()); em.Set(neu, new Deleted());
        Pruefe(!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger), "Abriss des Lots verhindert Abschluss");
        em.Remove(neu);
        Pruefe(!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, neu, neuerTraeger)
            && !ParkingLotHintergrundAbschluss.MerkeNebenarbeit(em, Entity.Null, Entity.Null), "totes und leeres Lot verhindern Abschluss");
        Pruefe(em.Adds + em.Sets == vorher, "ungueltige Abschluesse schreiben keinerlei Komponenten");
        Hardening();
        Logauszuege();
        Console.WriteLine($"SYNC-SICHERHEIT: {_pruefungen} Pruefungen, {_fehler} Fehler.");
        return _fehler == 0 ? 0 : 1;
    }
}
