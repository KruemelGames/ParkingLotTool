using Colossal.UI.Binding;
using Game.Common;
using Game.Prefabs;
using ParkingLotTool.Tools;
using Unity.Entities;

internal static partial class Program
{
    private static void InfoSektion()
    {
        var welt = new World(); var em = welt.EntityManager;
        var lot = new Entity(900, 2); var traeger = new Entity(901, 3); var prefab = new Entity(902, 4);
        var id = new PrefabID("LotPrefab", ParkingLotToolSystem.LotOwnerPrefabName);
        welt.Prefabs.Werte[id] = (prefab, new PrefabBase { ID = id, name = id.Name });
        em.Set(lot, new Game.Areas.Area()); em.Set(lot, new PrefabRef { m_Prefab = prefab });
        var sektion = new ParkingLotEditSection { World = welt }; sektion.Erzeuge();
        JsonWriter Lies()
        {
            Pruefe(sektion.Aktualisiere(lot), "PLT-Flaeche bleibt in der Infosektion sichtbar");
            var w = new JsonWriter(); sektion.OnWriteProperties(w);
            Pruefe((string)w.Werte["schluessel"] == "900:2", "Auswahl bleibt beim Werkzeugwechsel adressierbar");
            return w;
        }
        void Sperre(string grund)
        {
            var w = Lies();
            Pruefe(!(bool)w.Werte["bearbeitbar"] && (string)w.Werte["grund"] == grund, "C# liefert Sperre: " + grund);
        }
        Sperre("editSection.plan");
        em.Set(lot, new ParkingLotCarrierReference { Carrier = traeger });
        em.Set(traeger, new Owner { m_Owner = lot });
        var waisen = welt.GetOrCreateSystemManaged<ParkingLotWaisenSystem>();
        waisen.Halbwaisen.Add(lot); waisen.Status = 3;
        Sperre("editSection.partial");
        Pruefe((bool)Lies().Werte["reparierbar"], "vorhandener Protokollplan kann wiederhergestellt werden");
        sektion.Ausloeser["GewaehltenReparieren"]();
        Pruefe(waisen.Reparaturen == 1, "freigegebene Wiederherstellung erreicht ihren Ausloeser");
        em.Set(lot, new ParkingLotBuildReceipt()); em.SetBuffer<ParkingLotBuildPoint>(lot);
        em.SetBuffer<ParkingLotBuildEntrance>(lot); em.SetBuffer<ParkingLotBuildText>(lot);
        waisen.Status = 0;
        Pruefe((bool)Lies().Werte["bearbeitbar"] && (string)Lies().Werte["grund"] == "",
            "abgeschlossene Wiederherstellung entsperrt trotz alter Halbwaisenliste");
        var hintergrund = welt.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
        hintergrund.Grund = "Rueckweg angehalten: Anschluss fehlt";
        Sperre(hintergrund.Grund);
        waisen.Status = 3;
        sektion.Ausloeser["GewaehltenReparieren"]();
        Pruefe(waisen.Reparaturen == 1, "Reparaturausloeser prueft die neue Sperre vor dem Eingriff erneut");
        hintergrund.Grund = ""; waisen.Status = 0;
        em.Set(lot, new ParkingLotNeubauFaellig());
        Sperre("editSection.recovery");
        em.RemoveComponent<ParkingLotNeubauFaellig>(lot);
        var sync = welt.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
        sync.ArbeitLaeuft = true; Sperre("editSection.work"); sync.ArbeitLaeuft = false;
        var werkzeug = welt.GetOrCreateSystemManaged<ParkingLotToolSystem>();
        werkzeug.IsEditing = true; Sperre("editSection.editing"); werkzeug.IsEditing = false;
        em.RemoveComponent<Owner>(traeger);
        Sperre("editSection.partial");
        em.Set(traeger, new Owner { m_Owner = lot }); waisen.Halbwaisen.Clear();
        em.Set(traeger, new ParkingLotAbrissabsicht());
        Sperre("editSection.carrier");
        em.RemoveComponent<ParkingLotAbrissabsicht>(traeger);
        waisen.Status = 1; Sperre("editSection.orphan");
        waisen.Status = 2; Pruefe(!(bool)Lies().Werte["reparierbar"], "mehrdeutige Waise bleibt unreparierbar");
        em.Set(lot, new Deleted());
        Pruefe(!sektion.Aktualisiere(lot), "geloeschte Flaeche wird nicht angeboten");
        var fremd = new Entity(999, 9); em.Set(fremd, new Game.Areas.Area());
        Pruefe(!sektion.Aktualisiere(fremd), "Vanilla-Flaeche bekommt keine PLT-Sektion");
    }
}
