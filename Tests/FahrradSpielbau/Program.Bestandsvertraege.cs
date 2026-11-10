using System;
using System.IO;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static void Bestandsvertraege()
    {
        var wurzel=new DirectoryInfo(AppContext.BaseDirectory);
        while(wurzel!=null && !File.Exists(Path.Combine(wurzel.FullName,"Mod.cs")))wurzel=wurzel.Parent;
        var pfad=Environment.GetEnvironmentVariable("PLT_FAHRRAD_BESTANDSWURZEL") ?? wurzel.FullName;
        string Lies(string p)=>File.ReadAllText(Path.Combine(pfad,p));
        bool Hat(string p,string muster)=>Regex.IsMatch(Lies(p),muster,RegexOptions.Singleline);
        Pruefe(Hat("Tools/ParkingLotBauzettelSchreiber.cs",@"Fahrzeugart = settings.Fahrzeugart,\s*Fahrradlayout = settings.Fahrradlayout"),"produktiver Zettelschreiber erfasst beide Bauartfelder");
        Pruefe(Hat("Tools/ParkingLotUISystem.cs",@"LoadBuildReceipt\(.*?_editFahrzeugart = receipt.Fahrzeugart;\s*_editFahrradlayout = receipt.Fahrradlayout;"),"Edit-Art wird vor optionaler Vorlage festgehalten");
        Pruefe(Hat("Tools/ParkingLotUISystem.cs",@"Bauartvertrag.Edit\(settings, _editFahrzeugart, _editFahrradlayout, _editFahrradstil\);\s*if \(settings.Fahrzeugart == Fahrzeugart.Fahrrad\)"),"Neubauwahl wird auch ohne Vorlage vom Edit-Typ ueberstimmt");
        Pruefe(Hat("Tools/ParkingLotUISystem.cs",@"ClearBuildReceiptTemplate\(\).*?_editFahrzeugart = null;"),"nach Edit ist Neubauwahl wieder frei");
        Pruefe(Hat("Tools/ParkingLotBaukontextLeser.cs",@"ValidReceiptSettings\(.*?Bauartvertrag.Gueltig\(r.Fahrzeugart, r.Fahrradlayout\)"),"echter Leser lehnt ungueltige Bauart ab");
        Pruefe(Hat("Tools/ParkingLotEditMode.cs",@"if\(gelesen && !ParkingLotFahrradBetrieb.PasstZettel\(EntityManager,lot,receipt\)\).*?gelesen=false;"),"Edit-Einstieg vergleicht Zettel mit wirklichem Prefabtyp");
        Pruefe(Hat("Tools/ParkingLotSync.cs",@"var lot = lots\[i\];\s*var hintergrund = .*?if \(ParkingLotFahrradBetrieb.IstBestand\(EntityManager, lot\)\) continue;"),"Auto-Sync nimmt Fahrrad nicht auf und setzt keinen Stand hoch");
        Pruefe(Hat("Tools/ParkingLotSync.cs",@"private void Einreihen\(Entity lot\)\s*\{\s*if \(ParkingLotFahrradBetrieb.IstBestand\(EntityManager, lot\)\) return;"),"direkter Sync-Trigger kann Fahrrad nicht einreihen");
        Pruefe(Hat("Tools/ParkingLotSync.cs",@"private bool Synchronisiere\(.*?\{\s*if \(ParkingLotFahrradBetrieb.IstBestand\(EntityManager, lot\)\) return false;"),"auch wartende Auto-Sync-Auftraege fassen Fahrrad nicht an");
        Pruefe(Hat("Tools/ParkingLotHintergrundSystem.cs",@"KannNeubauen\(Entity lot, out string grund\).*?ParkingLotFahrradBetrieb.IstBestand.*?fahrrad.automatikAus.*?return false;"),"kein voller Auto-Hintergrundneubau fuer Fahrrad");
        Pruefe(Lies("Tools/ParkingLotHintergrund.Auftrag.cs").Contains("if (!rueckweg && !KannNeubauen(lot, out var grund))"),"gespeicherter generischer Netz-Rueckweg bleibt fuer Edit-Abbruch verfuegbar");
        Pruefe(Hat("Tools/ParkingLotWegpruefung.cs",@"internal void Reparieren\(Entity lot\)\s*\{\s*if \(ParkingLotFahrradBetrieb.IstBestand"),"keine Auto-Wegreparatur/Gassenvorbau vor Fahrrad-Sperre");
        Pruefe(Hat("Tools/ParkingLotFehlendeAssets.cs",@"internal void Reparieren\(Entity lot\)\s*\{\s*if \(ParkingLotFahrradBetrieb.IstBestand"),"keine Auto-Assetneubauplanung fuer Fahrrad");
        Pruefe(Lies("Tools/ParkingLotWaisen.cs").Contains("ParkingLotFahrradPrefabBau.IstLotname(name)"),"Waisenaufnahme erkennt alle Fahrradnamen");
        Pruefe(Hat("Tools/ParkingLotWaisen.Reparatur.cs",@"TraegerPasst\(EntityManager,lot,traeger\).*?RestoreFahrradGehflaechenAfterLoad\(\)"),"relationale Waisenreparatur prueft Bauart und stellt Navigation wieder her");
        Pruefe(Hat("Tools/ParkingLotBauzettelWiederherstellung.cs",@"foreach \(var zeile in File.ReadAllLines\(pfad\)\).*?if\(!Bauartvertrag.ProtokollPasst.*?continue;.*?treffer = zeile;"),"Journal sucht neuesten passenden Eintrag statt falschen Typ zu uebernehmen");
        Pruefe(Hat("Tools/ParkingLotBauzettelWiederherstellung.cs",@"ProtokollzeileZu\(kennung,fahrrad\?Fahrzeugart.Fahrrad:Fahrzeugart.Auto,\s*fahrrad\?\(Fahrradlayout\?\)layout:null\)"),"Bauplanwiederherstellung bindet sich an vorhandenen Prefabtyp");
        Pruefe(Hat("Tools/ParkingLotBayObjects.cs",@"RestoreCarrierSubNetsAfterLoad\(\);\s*RestoreFahrradGehflaechenAfterLoad\(\);.*?NimmAbrissNachLadenWiederAuf"),"Geh-Verweise stehen vor Wiederaufnahme des Abrisses");
        Pruefe(Hat("Tools/ParkingLotFahrradLaden.cs",@"p.name != ParkingLotFahrradPrefabBau.Gehname.*?GetComponentData<Owner>\(area\).m_Owner.*?LadeGehflaechen\(EntityManager,lot,traeger"),"Ladehilfe verwendet nur echte eigene Geh-Flaechen mit nativem Owner");
        Pruefe(Lies("Tools/ParkingLotFahrradLaden.cs").Contains("ParkingLotFahrradBetrieb.LadeNetze(EntityManager,lot,traeger)"),"relationale Wiederherstellung ergaenzt auch Fahrrad-Netzverweise");
        Pruefe(Hat("Tools/ParkingLotVegetationBuild.cs",@"if \(Fahrradbau\) s.Append\('\|'\).Append\(FahrradDekoration.Speicherkennung\(_areaPreviewLayout.Fahrrad\)\)"),"D-Baumlagen beeinflussen Pflanzenuebernahme; Auto-Signatur bleibt unveraendert");
        var abriss=Lies("Tools/ParkingLotStadtreinigung.cs");
        var von=abriss.IndexOf("internal void Bestand(",StringComparison.Ordinal);
        var bis=abriss.IndexOf("internal int EntferneAlle(",StringComparison.Ordinal);
        Pruefe(!abriss.Substring(von,bis-von).Contains("VerbindeFahrradFuerAbriss"),"Bestandszaehlung bleibt ohne Schreibzugriff");
        Pruefe(Hat("Tools/ParkingLotStadtreinigung.cs",@"internal int EntferneAlle\(\).*?VerbindeFahrradFuerAbriss\(flaechen\[i\]\).*?ReisseAb\(auftrag\)"),"Deinstallation gewinnt verlorene Fahrradrelation vor Bulldozer zurueck");
        Pruefe(Hat("Tools/ParkingLotWaisen.Fahrrad.cs",@"AbrissTraeger\(.*?if\(traeger==Entity.Null\).*?return false;.*?return Verbinden\(lot\)"),"nur eindeutig zugeordneter Traeger geht in den vorhandenen Beziehungsweg");
        Pruefe(Hat("Tools/ParkingLotStadtreinigung.cs",@"out var p\) && ParkingLotFahrradPrefabBau.IstLotname\(p.name\)\) fahrradtraeger\+\+.*?var summe = [^;]*fahrradtraeger;"),"Schlusspruefung zaehlt auch verwaiste Fahrradtraeger");
        Pruefe(Hat("Tools/ParkingLotStadtreinigung.cs",@"bool Blockiert\(\).*?sync.ArbeitLaeuft.*?werkzeug.ArbeitetGerade"),"laufender Sync oder Edit sperrt weiterhin Deinstallation");
        Pruefe(Hat("Tools/ParkingLotStadtreinigung.cs",@"internal int EntferneAlle\(\)\s*\{[^}]*if\(Blockiert\(\)\) \{ Warte\(\); return 0; \}"),"Sperre wird bei Annahme erneut vor jeder Besitzerlisten-Aenderung geprueft");
    }
}
