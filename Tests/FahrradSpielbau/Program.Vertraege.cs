using System;
using System.IO;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static void Vertraege()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"Mod.cs")))root=root.Parent;
        var mutationswurzel=Environment.GetEnvironmentVariable("PLT_FAHRRAD_VERTRAGSWURZEL");
        if(!string.IsNullOrEmpty(mutationswurzel))root=new DirectoryInfo(mutationswurzel);
        if(root==null)throw new Exception("Projektwurzel fehlt");
        string Lies(string p)=>File.ReadAllText(Path.Combine(root.FullName,p));
        var mod=Lies("Mod.cs");
        bool Treffer(string s,string muster)=>Regex.IsMatch(s,muster,RegexOptions.Singleline);
        Pruefe(Treffer(mod,@"UpdateBefore<ParkingLotFahrradPrefabSystem,\s*Game\.Prefabs\.PrefabInitializeSystem>\(SystemUpdatePhase\.PrefabUpdate\)"),"Anmeldung vor PrefabInitialize");
        Pruefe(Treffer(mod,@"UpdateAfter<ParkingLotFahrradPrefabAbschlussSystem,\s*Game\.Prefabs\.NetInitializeSystem>\(SystemUpdatePhase\.PrefabUpdate\)"),"Abschluss nach NetInitialize");
        var netz=Lies("Tools/ParkingLotNetDefinitions.cs");
        Pruefe(Treffer(netz,@"if \(layout.Fahrrad != null\).*?FahrradNetze.*?yield break;"),"echter Netzbau benutzt Fahrradpfad");
        var objekte=Lies("Tools/ParkingLotBayObjects.cs");
        Pruefe(Treffer(objekte,@"if \(layout\?\.Fahrrad != null\).*?FahrradObjekte.*?yield break;"),"echter Objektbau benutzt Fahrradpfad");
        var flaechen=Lies("Tools/ParkingLotAreaPreviewDefinitions.cs");
        Pruefe(Treffer(flaechen,@"if \(layout.Fahrrad != null\).*?TryGehen.*?CreateAreaPreviewDefinition\(""FahrradGehflaeche"""),"Geh-Flaeche wird wirklich definiert");
        Pruefe(Treffer(flaechen,@"if \(layout.Fahrrad != null\)\s*MeasureAreaPreviewGroup\(new\[\] \{ layout.Grundstueck \}.*?if \(sampled == 0\)"),"Bau und Navigation bleiben bei ausgeschaltetem Belag moeglich");
        var owner=Lies("Tools/ParkingLotLotOwner.cs");
        Pruefe(owner.Contains("if (Fahrradbau) FahrradTraeger();"),"materialisierter Traeger wird typisiert");
        var overlay=Lies("Tools/ParkingLotOverlay.cs");
        Pruefe(Treffer(overlay,@"if \(layout.Fahrrad != null\).*?o.Grundriss.*?layout.Fahrrad.Wege.*?ParkingLotPreviewStyle.FahrradWeg"),"Vorschau zeichnet denselben Objekt-/Wegeplan");
        Pruefe(Lies("Tools/ParkingLotOverlay.Draw.cs").Contains("if (!netzFuellt || _fahrradLayout) DrawBands(buffer, _roads, true);"),"Fahrradwege bleiben ueber dem Flaechennetz sichtbar");
        var uninstall=Lies("Tools/ParkingLotStadtreinigung.cs");
        Pruefe(Treffer(uninstall,@"IstUnserLot\(Entity lot\).*?ParkingLotFahrradPrefabBau.Lotname"),"Deinstallation erkennt Fahrrad-Lot");
        Pruefe(Treffer(uninstall,@"fahrradtraeger.*?ReadOnly<Game.Buildings.BicycleParkingFacility>.*?var summe = [^;]+fahrradtraeger;"),"Schlusszaehlung findet auch verwaiste Fahrradtraeger");
        Pruefe(Treffer(uninstall,@"bool Blockiert\(\).*?sync.ArbeitLaeuft.*?werkzeug.ArbeitetGerade"),"Deinstallationssperren bleiben wirksam");
        Pruefe(Treffer(uninstall,@"void ReisseAb\(.*?FuelleBesitzlisten\(lot\).*?_abriss.Einreihen\(auftrag\)"),"Abriss bleibt Besitzerpuffer/Bulldozer");
    }
}
