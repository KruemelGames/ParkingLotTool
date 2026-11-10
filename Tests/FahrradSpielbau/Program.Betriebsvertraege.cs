using System;
using System.IO;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static void Betriebsvertraege()
    {
        var wurzel=new DirectoryInfo(AppContext.BaseDirectory);
        while(wurzel!=null && !File.Exists(Path.Combine(wurzel.FullName,"Mod.cs")))wurzel=wurzel.Parent;
        var pfad=Environment.GetEnvironmentVariable("PLT_FAHRRAD_BETRIEBSWURZEL") ?? wurzel.FullName;
        string Lies(string p)=>File.ReadAllText(Path.Combine(pfad,p));
        bool Hat(string p,string muster)=>Regex.IsMatch(Lies(p),muster,RegexOptions.Singleline);
        Pruefe(Hat("Tools/ParkingLotFahrradPrefabSystem.cs",@"foreach \(var layout in new\[\] \{ Fahrradlayout.A, Fahrradlayout.C, Fahrradlayout.D \}\).*?Lot\(layout\).*?AddComponentData\(entity, new ParkingLotFahrradLayout \{ Layout = layout \}"),"drei eigene Layout-Prefabs mit Laufzeitmetadaten angemeldet");
        Pruefe(Hat("Tools/ParkingLotLotArea.cs",@"Fahrradbau.*?TryLot\(_areaPreviewLayout.Fahrrad.Layout, out prefab\)"),"Baudefinition nimmt richtiges Layout-Prefab");
        var wirtschaft=Lies("Tools/ParkingLotEconomySystem.cs");
        Pruefe(Regex.Matches(wirtschaft,Regex.Escape("Upkeep = ParkingLotFahrradBetrieb.Unterhalt(EntityManager, lot, capacity)")).Count==2,"Neubau und Ersatz benutzen Fahrrad-Unterhalt");
        Pruefe(Regex.Matches(wirtschaft,Regex.Escape("ParkingLotFahrradBetrieb.Ist(EntityManager, lot) ? 0 : UnityEngine.Mathf.Clamp")).Count==3,"Gebuehr ist bei Fahrrad immer 0, auch im Ersatz-/Altformatpfad");
        Pruefe(wirtschaft.Contains("if (Mod.WirtschaftAn) RestoreRuntimeComponents(lot, b, economy);"),"keine Abrechnung durch Kapazitaetsmessung bei AUS");
        Pruefe(Hat("Tools/ParkingLotEconomy.Fahrrad.cs",@"ParkingLotFahrradBetrieb.Ist\(EntityManager, lot\).*?!EntityManager.HasComponent<ParkingLotEconomyData>\(lot\).*?UpdateLot\(lot, leer\)"),"Fahrradplaetze werden auch ohne Wirtschaft wirklich gemessen");
        Pruefe(wirtschaft.Contains("MesseFahrradOhneWirtschaft();"),"AUS-Pfad ruft Messung auf");
        Pruefe(wirtschaft.Contains("ParkingLotFahrradBetrieb.Ist(EntityManager, lot) && !Mod.WirtschaftAn ? 0"),"Liste zeigt 0 Kosten bei Wirtschaft AUS");
        Pruefe(wirtschaft.Contains("ParkingLotFahrradPrefabBau.IstLotname(lotPrefab.name)"),"Wirtschaft akzeptiert eigene Fahrrad-Lots");
        Pruefe(Hat("Tools/ParkingLotBuildingEconomySystem.cs",@"IsOwnLotPrefab\(Entity prefab\).*?ParkingLotFahrradPrefabBau.IstLotname\(value.name\)"),"vorhandener Begleiter wird fuer Fahrrad verwendet");
        Pruefe(Lies("Tools/ParkingLotOhnePersonal.cs").Contains("ParkingLotFahrradBetrieb.Ist(em, lot)"),"Fahrrad hat zwingend kein Personal");
        Pruefe(Hat("Tools/ParkingLotEmployeeSystem.cs",@"ParkingLotPersonal.Ohne\(EntityManager, relation.Lot\)\s*\? 0 : CalculateEmployees"),"Personalregel erreicht echte Stellenzahl");
        Pruefe(Hat("Tools/ParkingLotEmployeeSection.cs",@"var sichtbar = .*?!ParkingLotFahrradBetrieb.Ist\(EntityManager, selectedEntity\)"),"keine Personalsektion fuer Fahrrad");
        Pruefe(Hat("Tools/ParkingLotFeeSection.cs",@"var sichtbar = Mod.WirtschaftAn\s*&& !ParkingLotFahrradBetrieb.Ist\(EntityManager, lot\)"),"keine Gebuehrensektion fuer Fahrrad");
        Pruefe(Hat("Tools/ParkingLotFeeUISystem.cs",@"bool IsParkingLot\(Entity entity\).*?!ParkingLotFahrradBetrieb.Ist\(EntityManager, entity\)"),"direkter Gebuehrentrigger nimmt kein Fahrrad an");
        Pruefe(Hat("Tools/ParkingLotComfortSystem.cs",@"var fahrrad = ParkingLotFahrradBetrieb.TryLayout.*?var zielGebuehrInt = !wirtschaftAn \|\| fahrrad.*?FahrradBetrieb.Komfort\(fahrradlayout\).*?var ziel = !wirtschaftAn \? \(ushort\)0.*?RoundToInt\(komfort \* 65535f\)"),"echte Parkspuren erhalten Layoutkomfort, keine Gebuehr, Hauptschalter");
        Pruefe(Hat("Tools/ParkingLotComfortSystem.cs",@"SetParkingFeeImmediately\(Entity lot, int fee\).*?if \(ParkingLotFahrradBetrieb.Ist\(EntityManager, lot\)\) fee = 0;"),"Sofortpfad kann keine Fahrradgebuehr setzen");
        Pruefe(Hat("Tools/ParkingLotInfoansicht.cs",@"AktiverParkplatzIndex\(BuildingType.BicycleParkingFacility\).*?var aktiv = fahrrad \? fahrradIndex : index;.*?var symbol = fahrrad \? fahrradMarker : marker;"),"Fahrradfarbe/-symbol werden nach Art getrennt");
        Pruefe(Hat("Tools/ParkingLotInfoansicht.cs",@"HasComponent<Game.Buildings.BicycleParkingFacility>\(traeger\).*?continue;\s*EntityManager.AddComponent<Game.Buildings.CarParkingFacility>"),"Fahrrad erhaelt keinen Autozaehler");
        Pruefe(Hat("Tools/ParkingLotListeUISystem.cs",@"Feld 24:.*?ParkingLotFahrradBetrieb.Ist\(EntityManager, lot\) \? 1 : 0"),"Liste liefert Fahrzeugart");
        Pruefe(Hat("Tools/ParkingLotListeUISystem.cs",@"ParkplatzGebuehr\(string nutzlast\).*?if \(ParkingLotFahrradBetrieb.Ist\(EntityManager, lot\)\) return;"),"Listentrigger sperrt Fahrradgebuehr");
        Pruefe(Hat("Tools/ParkingLotEditSection.cs",@"PropertyName\(""fahrradPlaetze""\).*?ParkingLotEconomyData>\(selectedEntity\).Capacity : 0\) : -1"),"Infofenster zeigt wirkliche Fahrradplaetze");
        Pruefe(Hat("UI/src/mods/liste.tsx",@"fahrrad: zahl\(f\[WEGE_FELD \+ 1\]\) === 1"),"neues Listenfeld, alte Zeilen bleiben Auto");
        Pruefe(Hat("UI/src/mods/liste.tsx",@"platz.fahrrad &&.*?t.fahrradPlaetze\(platz.kapazitaet\)"),"Liste zeigt Fahrradart und Plaetze");
        Pruefe(Hat("UI/src/mods/liste.tsx",@"platz.fahrrad \? <span>\{t.fahrradKostenlos\}.*?: <Listengebuehr"),"Liste hat keinen Fahrrad-Gebuehrenregler");
        Pruefe(Hat("UI/src/mods/edit-section.tsx",@"props.fahrradPlaetze.*?t.fahrradPlaetze\(props.fahrradPlaetze"),"Infofenster verwendet uebersetzte Art-/Kapazitaetsvorlage");
        Pruefe(Hat("Tools/ParkingLotStadtreinigung.cs",@"IstUnserLot\(Entity lot\).*?Lotname.*?LotC.*?LotD"),"Deinstallation kennt alle drei Lotvarianten");
        Pruefe(Hat("Tools/ParkingLotStadtreinigung.cs",@"out var p\) && ParkingLotFahrradPrefabBau.IstLotname\(p.name\)\) fahrradtraeger\+\+"),"Schlusszaehlung kennt verwaiste C-/D-Traeger");
    }
}
