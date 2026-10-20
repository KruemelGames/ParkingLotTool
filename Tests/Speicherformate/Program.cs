using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Colossal.Serialization.Entities;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

internal static partial class Program
{
    private static int _pruefungen, _fehler;
    private static void Pruefe(bool ok, string text)
    { _pruefungen++; if (!ok) { _fehler++; Console.WriteLine("FEHLER: " + text); } }

    // Auch Mutationen duerfen kein Windows-Absturzfenster ausloesen.
    private static int Main()
    {
        try { return Ablauf(); }
        catch (Exception x) { Console.WriteLine("FEHLER: unbehandelte Ausnahme: " + x); return 1; }
    }

    private static ISerializable Lies(string art, Leser r) => art switch
    {
        "Receipt" => Lies<ParkingLotBuildReceipt>(r),
        "Rueckweg" => Lies<ParkingLotRueckwegkurs>(r),
        "Erhaltener" => Lies<ParkingLotErhaltenerKurs>(r),
        "Anschluss" => Lies<ParkingLotRueckweganschluss>(r),
        "StufeAAlt" => Lies<ParkingLotStufeAPrefab>(r),
        "StufeANeu" => Lies<ParkingLotStufeAPrefabID>(r),
        "NeubauFaellig" => Lies<ParkingLotNeubauFaellig>(r),
        "Abrissabsicht" => Lies<ParkingLotAbrissabsicht>(r),
        "Musterflaeche" => Lies<ParkingLotMusterflaeche>(r),
        "NebenarbeitOffen" => Lies<ParkingLotNebenarbeitOffen>(r),
        _ => throw new InvalidOperationException("Fixturart: " + art),
    };
    private static T Lies<T>(Leser r) where T : struct, ISerializable
    { T wert = default; wert.Deserialize(r); return wert; }

    private static object Feldwert(object obj, string name)
    {
        foreach (var teil in name.Split('.'))
            obj = obj.GetType().GetField(teil)?.GetValue(obj)
                ?? throw new InvalidOperationException("Feld fehlt: " + name);
        return obj;
    }

    private static bool Gleich(object a, object b)
    {
        if (a is FixedString512Bytes fs) return fs.ToString() == (string)b;
        if (a.GetType().IsEnum) return Convert.ToUInt32(a) == (uint)b;
        return a.Equals(b);
    }

    private static int Ablauf()
    {
        var fixtures = JsonSerializer.Deserialize<Fixtur[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixturen/historisch.json")));
        foreach (var fixture in fixtures)
        {
            var strom = fixture.Datensaetze.SelectMany(x => x).ToList();
            strom.Add(new Feld { Name = "$ende", Typ = "String", Wert = JsonSerializer.SerializeToElement("ENDE_PLT") });
            var reader = new Leser(strom);
            foreach (var felder in fixture.Datensaetze)
            {
                var vorher = reader.Position;
                var wert = Lies(fixture.Art, reader);
                Pruefe(reader.Position - vorher == felder.Length, fixture.Art + "/" + fixture.Version + " liest genau einen Datensatz");
                foreach (var f in felder)
                    if (!f.Name.StartsWith("$"))
                        Pruefe(Gleich(Feldwert(wert, f.Name), f.Lesen()), fixture.Art + "/" + fixture.Version + ": " + f.Name);
                Defaults(wert, fixture.Version);
                if ((fixture.Art == "Receipt" && fixture.Version == 6)
                    || (fixture.Art == "Rueckweg" || fixture.Art == "Erhaltener") && fixture.Version == 2
                    || fixture.Art == "Anschluss" && fixture.Version == -2
                    || fixture.Art.StartsWith("StufeA") || fixture.Art == "NebenarbeitOffen" && fixture.Version == 2
                    || fixture.Art == "NeubauFaellig" || fixture.Art == "Abrissabsicht" || fixture.Art == "Musterflaeche")
                {
                    var writer = new Schreiber();
                    wert.Serialize(writer);
                    Pruefe(writer.Felder.Count == felder.Length, fixture.Art + " schreibt passende Datensatzlaenge");
                    for (int i = 0; i < Math.Min(writer.Felder.Count, felder.Length); i++)
                        Pruefe(writer.Felder[i].Typ.Name == felder[i].Typ
                            && Gleich(writer.Felder[i].Wert, felder[i].Lesen()), fixture.Art + " Schreiberfeld " + i);
                }
            }
            reader.Read(out string ende);
            Pruefe(ende == "ENDE_PLT" && reader.Position == strom.Count, fixture.Art + "/" + fixture.Version + " Endmarke nach zwei Datensaetzen");
        }
        NeueNebenformate();
        Kurvenformate();
        StufeA();
        EditAnzeige();
        InfoSektion();
        Console.WriteLine($"SPEICHERFORMATE: {_pruefungen} Pruefungen, {_fehler} Fehler.");
        return _fehler == 0 ? 0 : 1;
    }

    private static void Defaults(ISerializable wert, int version)
    {
        if (wert is ParkingLotBuildReceipt r)
        {
            if (version < 2) Pruefe(double.IsNaN(r.Ausrichtwinkel), "Receipt v1 ohne Bezugsrichtung");
            if (version < 3) Pruefe(r.Randstrassen, "alter Receipt mit Randstrassen");
            if (version < 4) Pruefe(r.Gassenbreite == LayoutSettings.Cs2.Gassenbreite, "alte Gassenbreite");
            if (version < 5) Pruefe(r.ZoningWinkelmodus == 0 && r.ZoningReglerwinkel == 0
                && r.ZoningAussentiefeVorwahl == 2 && double.IsNaN(r.ZoningAusrichtwinkel), "alte Zoning-Vorgaben");
            if (version < 6) Pruefe(r.BusStopCount == 0, "alter Receipt ohne Bushalte");
        }
        var kurs = wert is ParkingLotRueckwegkurs k ? k : wert is ParkingLotErhaltenerKurs e ? e.Bestand : default;
        if (wert is ParkingLotRueckwegkurs || wert is ParkingLotErhaltenerKurs)
        {
            Pruefe(kurs.Kurs.m_StartPosition.m_Entity == kurs.Start && kurs.Kurs.m_EndPosition.m_Entity == kurs.Ende,
                "Rueckweg bindet gespeicherte Endknoten");
            Pruefe(kurs.Kurs.m_FixedIndex == -1 && kurs.Kurs.m_StartPosition.m_ParentMesh == -1
                && kurs.Kurs.m_StartPosition.m_CourseDelta == 0 && kurs.Kurs.m_EndPosition.m_CourseDelta == 1, "Rueckweg-Positionsvorgaben");
            if (version == 1) Pruefe(kurs.PrefabName.IsEmpty, "alter Rueckweg ohne PrefabID");
        }
        if (wert is ParkingLotRueckweganschluss a && version == 0)
            Pruefe(a.PrefabName.IsEmpty, "alter Anschluss ohne Namensfeld");
        if (wert is ParkingLotStufeAPrefabID s)
            Pruefe(s.Prefab == Entity.Null, "Stufe-A-Cache ist nach Laden leer");
    }

    private static void StufeA()
    {
        var world = new World(); var em = world.EntityManager;
        var lot = new Entity(800, 4); var alt = new Entity(801, 5); var neu = new Entity(802, 6);
        var id = new Game.Prefabs.PrefabID("LotPrefab", "PLT Fläche/测试");
        var prefab = new Game.Prefabs.PrefabBase { ID = id };
        world.Prefabs.Werte[id] = (neu, prefab);
        em.Set(alt, new Game.Prefabs.PrefabData()); em.Set(neu, new Game.Prefabs.PrefabData());
        Pruefe(ParkingLotStufeA.Prefab(em, lot) == Entity.Null, "alte Saves ohne Stufe-A-Marker brauchen keinen Auftrag");
        em.Set(lot, new ParkingLotStufeAPrefab { Prefab = alt });
        Pruefe(ParkingLotStufeA.Prefab(em, lot) == alt, "altes Stufe-A-Prefab bleibt nutzbar");
        var name = ParkingLotPrefabname.Von(em, neu);
        Pruefe(name.ToString().Contains("%2F") && ParkingLotPrefabname.Aufloesen(em, name) == neu, "kodierte Prefab-ID wird aufgeloest");
        em.Set(lot, new ParkingLotStufeAPrefabID { PrefabName = name, Prefab = alt });
        Pruefe(ParkingLotStufeA.Prefab(em, lot) == neu, "ID gewinnt gegen falschen Cache und alten Marker");
        world.Prefabs.Werte.Clear();
        Pruefe(ParkingLotStufeA.Prefab(em, lot) == Entity.Null, "fehlendes benanntes Prefab erlaubt keine geratenen Ersatztreffer");
        world.Prefabs.Werte[id] = (neu, prefab);
        var schreiber = new Schreiber();
        var original = new ParkingLotStufeAPrefabID { PrefabName = name, Prefab = alt };
        original.Serialize(schreiber);
        Pruefe(schreiber.Felder.Count == 2 && schreiber.Felder[0].Wert.Equals(1)
            && schreiber.Felder.All(f => f.Typ != typeof(Entity)), "neuer Stufe-A-Schreiber speichert nur Version und ID");
        var felder = new List<Feld>
        {
            new() { Typ = "Int32", Wert = JsonSerializer.SerializeToElement(1) },
            new() { Typ = "String", Wert = JsonSerializer.SerializeToElement(name.ToString()) },
        };
        original.Deserialize(new Leser(felder));
        Pruefe(original.Prefab == Entity.Null, "Lesen setzt auch einen vorher belegten Cache zurueck");
        ParkingLotStufeA.VergissPrefab(em, lot);
        Pruefe(!em.HasComponent<ParkingLotStufeAPrefab>(lot) && !em.HasComponent<ParkingLotStufeAPrefabID>(lot),
            "Abschluss entfernt beide Stufe-A-Formate");
        var langerName = new string('a', 510);
        Pruefe(ParkingLotPrefabname.Fest(langerName).IsEmpty, "zu lange ID wird nicht abgeschnitten");
    }

    private static void EditAnzeige()
    {
        EditSperre Pruefung(bool r = false, bool a = false, bool e = false, bool v = true,
            bool t = true, int w = 0, bool h = false) => EditFreigabe.Pruefe(r, a, e, v, t, w, h);
        Pruefe(Pruefung() == EditSperre.Keine, "gesunder Parkplatz ist bearbeitbar");
        Pruefe(Pruefung(r: true, a: true, v: false) == EditSperre.Wiederherstellung, "Wiederherstellung hat Vorrang vor fehlendem Bauplan");
        Pruefe(Pruefung(a: true) == EditSperre.Arbeit && Pruefung(e: true) == EditSperre.Bearbeitung, "laufende Arbeiten haben lesbaren Sperrgrund");
        Pruefe(Pruefung(v: false) == EditSperre.Bauplan && Pruefung(t: false) == EditSperre.Traeger, "fehlender Bauplan oder Traeger bleibt sichtbar begruendbar");
        Pruefe(Pruefung(w: 1, v: false) == EditSperre.Waise && Pruefung(h: true, v: false) == EditSperre.Halbwaise, "Waise und Halbwaise werden unterschieden");
        Pruefe(EditFreigabe.DarfReparieren(EditSperre.Waise, 1), "eindeutige Waise reparierbar");
        Pruefe(EditFreigabe.DarfReparieren(EditSperre.Bauplan, 3), "Bauplan aus Protokoll wiederherstellbar");
        Pruefe(!EditFreigabe.DarfReparieren(EditSperre.Waise, 2), "mehrdeutige Waise nicht reparierbar");
        Pruefe(!EditFreigabe.DarfReparieren(EditSperre.Keine, 0), "gesunder Parkplatz braucht keine Waisenreparatur");
        foreach (var sperre in new[] { EditSperre.Wiederherstellung, EditSperre.Arbeit, EditSperre.Bearbeitung })
        {
            Pruefe(!EditFreigabe.DarfReparieren(sperre, 1), "Waise waehrend Arbeit gesperrt: " + sperre);
            Pruefe(!EditFreigabe.DarfReparieren(sperre, 3), "Planwiederherstellung waehrend Arbeit gesperrt: " + sperre);
        }
    }
}
