using System;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;
using Unity.Mathematics;

internal static partial class Program
{
    private static void NebenarbeitPruefen()
    {
        var w = new World(); var em = w.EntityManager;
        var lot = new Entity(9000); Gesund(w, lot);
        var carrier = em.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
        var prefab = new Entity(9010); em.Set(prefab, 0);
        var s = w.GetOrCreateSystemManaged<ParkingLotErzeugungsSystem>();
        var quelle = new ParkingLotErzeugungsauftrag { Art = ParkingLotErzeugungsart.Strom, Seed = 17,
            A = new float3(0, -10, 0), D = new float3(10, -10, 0) };
        for (int fall = 0; fall < 8; fall++)
        {
            var e = new Entity(9020 + fall);
            em.Set(e, new PrefabRef { m_Prefab = fall == 1 ? new Entity(9999) : prefab });
            em.Set(e, new PseudoRandomSeed { m_Seed = (ushort)(fall == 2 ? 18 : 17) });
            em.Set(e, new Edge());
            em.Set(e, new Curve { m_Bezier = new TestBezier { a = quelle.A, d = quelle.D } });
            em.Set(e, new Temp { m_Original = fall == 3 ? new Entity(9998) : Entity.Null,
                m_Flags = fall == 4 ? TempFlags.Delete : fall == 5 ? TempFlags.Cancel : fall == 6 ? 0 : TempFlags.Create });
            if (fall == 7) em.Set(e, new ParkingLotVersorgungsleitung { Lot = new Entity(99) });
            bool gebunden = s.TestLeitung(lot, carrier, prefab, quelle, e);
            Pruefe(gebunden == (fall == 0), "nur eigene neue Quelle wird Leitung: Fall " + fall);
            if (fall == 0)
            {
                em.RemoveComponent<Temp>(e);
                Pruefe(em.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot == lot
                    && !em.HasComponent<Owner>(e), "Zuordnung bleibt beim Temp-Entfernen auf derselben Entity; Owner unveraendert");
            }
            else Pruefe(!em.HasComponent<ParkingLotVersorgungsleitung>(e)
                || em.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot != lot, "fremde/unveraenderte Kante nicht vereinnahmt");
        }
        var aux = new Entity(9050);
        em.Set(aux, new PrefabRef { m_Prefab = prefab }); em.Set(aux, new PseudoRandomSeed { m_Seed = 17 });
        em.Set(aux, new Edge());
        em.Set(aux, new Temp { m_Flags = TempFlags.Create });
        em.Set(aux, new Curve { m_Bezier = new TestBezier { a = quelle.A, d = new float3(0, -1, 0) } });
        Pruefe(s.TestLeitung(lot, carrier, prefab, quelle, aux), "eigenes senkrechtes Anschlussstueck traegt Zuordnung");
        Pruefe(!Erzeugungsherkunft.Leitungsstueck(new float3(3, -10, 3), new float3(3, -1, 3), quelle.A, quelle.D),
            "fremdes senkrechtes Nachbarstueck ausserhalb Quellende abgewiesen");

        var edge = new Entity(9060);
        var bus = new ParkingLotErzeugungsauftrag { Art = ParkingLotErzeugungsart.Bushalt, Seed = 23, Bezug = edge, A = new float3(3, 2, 1) };
        Entity Schild(int index, int seed)
        {
            var e = new Entity(index);
            em.Set(e, new PrefabRef { m_Prefab = prefab }); em.Set(e, new PseudoRandomSeed { m_Seed = (ushort)seed });
            em.Set(e, new Game.Routes.TransportStop()); em.Set(e, new Attached { m_Parent = edge });
            em.Set(e, new Transform { m_Position = bus.A }); return e;
        }
        var fremd = Schild(9070, 24); var eigen = Schild(9071, 23);
        Pruefe(!s.TestBushalt(lot, carrier, prefab, ref bus, fremd) && !em.HasComponent<ParkingLotPartRelation>(fremd),
            "fremdes Schild trotz gleicher Lage, Prefab und Kante nicht vereinnahmt");
        Pruefe(s.TestBushalt(lot, carrier, prefab, ref bus, eigen) && bus.Ergebnis == eigen && bus.Erledigt
            && em.GetComponentData<ParkingLotPartRelation>(eigen).Lot == lot && !em.HasComponent<Owner>(eigen),
            "eindeutiger Erzeugungsauftrag bindet Schild bei leerem Vanilla-Owner");
        var doppelt = Schild(9072, 23);
        Pruefe(!s.TestBushalt(lot, carrier, prefab, ref bus, eigen, doppelt)
            && !em.HasComponent<ParkingLotPartRelation>(doppelt), "mehrdeutige Herkunft baut keine Zuordnung auf Verdacht");

        Pruefe(!Erzeugungsherkunft.BushalteVollstaendig(new[] { new float2(0,0), new float2(10,0) },
                new[] { new float2(0,0), new float2(0,0) }),
            "zwei Schilder an derselben Stelle quittieren keinen anderen fehlenden Halt");
        Pruefe(Erzeugungsherkunft.BushalteVollstaendig(new[] { new float2(0,0), new float2(10,0) },
                new[] { new float2(0,0), new float2(10,0) }),
            "jede geplante Stelle mit eindeutigem eigenem Bestand darf quittiert werden");

        Pruefe(Erzeugungsherkunft.BushalteVollstaendig(new[] { new float2(0,0), new float2(.4f,0) },
                new[] { new float2(.4f,0), new float2(-.1f,0) }),
            "nahe Sollstellen werden vollstaendig zugeordnet, statt am ersten Treffer zu scheitern");

        Pruefe(!Erzeugungsherkunft.BushalteVollstaendig(new[] { new float2(0,0) }, new[] { new float2(float.NaN,0) }),
            "nichtendliche Schildposition ist kein bestaetigter Bestand");

        ParkingLotNebenarbeitDaten.Merke(em, lot, true);
        Mod.log.InfoFehler = true;
        try { ParkingLotNebenarbeitDaten.Diagnose(() => Mod.log.Info("Quittung")); }
        finally { Mod.log.InfoFehler = false; }
        ParkingLotNebenarbeitDaten.Quittiere(em, lot, NebenarbeitPflicht.Bushalte);
        ParkingLotNebenarbeitDaten.Merke(em, lot, true);
        var m = em.GetComponentData<ParkingLotNebenarbeitOffen>(lot);
        Pruefe(m.Offen == (NebenarbeitPflicht.Versorgung | NebenarbeitPflicht.Zoning),
            "erledigte Teilpflicht wird weder doppelt quittiert noch beim erneuten Merken zurueckgesetzt");
        Pruefe(ParkingLotNebenarbeitDaten.Generation(true, 2, 2)
            && !ParkingLotNebenarbeitDaten.Generation(true, 2, 1) && !ParkingLotNebenarbeitDaten.Generation(false, 2, 2),
            "Quittung darf weder alte Generation noch unbereites Register treffen");
        var tool = w.GetOrCreateSystemManaged<ParkingLotToolSystem>();
        var eigeneKante = new Entity(9020);
        tool.TestVorApply(lot, carrier, eigeneKante);
        bool abgewiesen = false;
        try { tool.TestVorApply(lot, carrier, new Entity(9021)); }
        catch (InvalidOperationException) { abgewiesen = true; }
        Pruefe(abgewiesen, "ohne belegte Zuordnung darf der Apply nicht beginnen");
        tool.TestSichereVerbindungen(lot, eigeneKante, aux);
        tool.TestSichereVerbindungen(lot, eigeneKante, aux);
        Pruefe(em.GetBuffer<ParkingLotVersorgungsnachweis>(lot).Length == 2,
            "erneuter gepruefter Apply derselben Trasse ersetzt Nachweis ohne doppelte Kanten");
        em.RemoveComponent<Temp>(aux);
        Pruefe(tool.TestLadeVerbindungen(lot) == 1, "vollstaendig materialisierter Nachweis setzt Verbindung ohne Neubau fort");
        var neuerLot = new Entity(9090); Gesund(w, neuerLot);
        var neuerCarrier = em.GetComponentData<ParkingLotCarrierReference>(neuerLot).Carrier;
        em.Set(eigeneKante, new ParkingLotVersorgungsleitung { Lot = neuerLot, Carrier = neuerCarrier });
        em.Set(aux, new ParkingLotVersorgungsleitung { Lot = neuerLot, Carrier = neuerCarrier });
        tool.TestUebertrageVerbindungen(lot, neuerLot, neuerCarrier);
        tool.TestUebertrageVerbindungen(lot, neuerLot, neuerCarrier);
        Pruefe(em.GetBuffer<ParkingLotVersorgungsnachweis>(neuerLot).Length == 2, "erneute Edit-Uebernahme verdoppelt keine Nachweiskante");
        Pruefe(tool.TestLadeVerbindungen(neuerLot) == 1, "Edit uebernimmt den vollstaendigen Nachweis erhaltener Leitungen");
        em.Set(eigeneKante, new ParkingLotVersorgungsleitung { Lot = lot, Carrier = carrier });
        em.Set(aux, new ParkingLotVersorgungsleitung { Lot = lot, Carrier = carrier });
        em.Remove(aux);
        Pruefe(tool.TestLadeVerbindungen(lot) == 0, "ein fehlender Nachweisteil bestaetigt keine vollstaendige Verbindung");
        var r = w.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();
        var bg = w.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
        em.Set(lot, new ParkingLotDatenstand { Stand = 15 });
        bg.EinreihenNebenarbeit(lot); var id = bg.TestKennung;
        Pruefe(r.Register.Lies(id, out var daten) && daten.Grund == Auftragsgrund.Wiederaufnahme
            && daten.Arbeiter == Auftragsarbeiter.Hintergrund, "Nebenarbeit hat eigenen Wiederaufnahmeauftrag");
        Pruefe(!bg.IstErsatz(lot), "Nebenarbeit am bestehenden Lot ist kein abzureissender Ersatz");
        bg.TestStart();
        Pruefe(!bg.IstErsatz(lot), "auch aktive Nebenarbeit markiert das vorhandene Lot nicht als Ersatz");
        Pruefe(bg.TestVersuch == 0, "Nebenarbeit verbraucht keinen Neubauversuch");
        Mod.log.InfoFehler = true;
        try { bg.TestFehler(); } finally { Mod.log.InfoFehler = false; }
        Pruefe(!r.Gilt(id) && bg.Offen == 0 && em.GetComponentData<ParkingLotDatenstand>(lot).Stand == 15
            && em.GetComponentData<ParkingLotNebenarbeitOffen>(lot).Offen == m.Offen
            && !em.HasBuffer<ParkingLotRueckwegkurs>(lot), "Nebenarbeitsfehler behaelt Marker, Stand und Parkplatz; kein Rueckweg");
        bg.EinreihenNebenarbeit(lot); var abbrechen = bg.TestKennung;
        bg.Einreihen(lot, true); bg.TestStart(); bg.TestRueckwegFehler();
        Pruefe(!r.Gilt(abbrechen) && bg.TestGesperrt(lot),
            "Edit-Abbruch stellt wartende Nebenarbeit auf echten Rueckweg samt Fehlersperre um");
        tool.TestAvLauf(carrier);
        Pruefe(tool.TestAvGueltig(), "laufende Versorgung gehoert der aktuellen Generation");
        int vorherGeloescht = em.Zerstoerversuche.Count;
        r.Preload(); bg.TestLaden();
        Pruefe(!tool.TestAvGueltig() && tool.TestAvLeer && em.Zerstoerversuche.Count == vorherGeloescht,
            "Laden vergisst alte Versorgung ohne Loeschung alter Entity-IDs in neuer Welt");
        bg.EinreihenNebenarbeit(lot);
        Pruefe(bg.Offen == 0, "Wiederaufnahme wartet auf Registerbereitschaft nach Pufferwiederherstellung");
        r.PufferWiederhergestellt(); bg.EinreihenNebenarbeit(lot); var neu = bg.TestKennung;
        bg.TestStart(); bg.TestAntwort(true, lot)();
        Pruefe(neu.Generation != id.Generation && !r.Gilt(neu)
            && em.GetComponentData<ParkingLotDatenstand>(lot).Stand == 15,
            "neue Generation setzt vorhandenen Parkplatz ohne Migrationsschritt fort");
    }
}
