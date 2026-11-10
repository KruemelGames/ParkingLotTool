using System;
using System.IO;
using System.Text.RegularExpressions;

internal static partial class Program
{
    /** Die dynamische Bedienprobe prueft die Regeln, hier stehen ihre Aufrufstellen. */
    private static void Bedienvertraege()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Mod.cs"))) root = root.Parent;
        var pfad = Environment.GetEnvironmentVariable("PLT_FAHRRAD_UIWURZEL") ?? root.FullName;
        bool Hat(string p, string muster) => Regex.IsMatch(File.ReadAllText(Path.Combine(pfad, p)),
            muster, RegexOptions.Singleline);
        Pruefe(Hat("Tools/ParkingLotUISystem.cs", @"OnCreate\(\).*?BindeFahrrad\(\);"), "Bauartbindungen werden beim Start angemeldet");
        Pruefe(Hat("Tools/ParkingLotWerkzeugzustand.cs", @"DarfArbeiten\(Weltarbeit arbeit\).*?FahrradBedienung.Arbeit\(.*?&& Werkzeugzustand.Erlaubt"), "Bauartfilter erreicht Weltklicks");
        Pruefe(Hat("Tools/ParkingLotWerkzeugzustand.cs", @"BauartGewechselt\(\).*?SetBusStopModeFromPanel\(false\).*?SetzeZoningModus\(false\).*?_zufahrtsart = Zufahrtsart.Zufahrt;.*?PublishEntranceState"), "Artwechsel raeumt Modi und Zugangsart ab");
        Pruefe(Hat("Tools/ParkingLotToolSystem.cs", @"_buildSettings.Randzoning =.*?FahrradBedienung.FuerBau\(_buildSettings\);.*?_buildRevision ="), "Bauartfilter steht nach allen Entwurfsdaten vor Geometrieauftrag");
        Pruefe(Hat("Tools/ParkingLotEntrances.cs", @"SetZufahrtsartFromPanel\(int art\).*?if \(!FahrradBedienung.Zugang\(.*?return;"), "direkter Trigger kann keine Autozufahrtsart fuer Fahrrad setzen");
        Pruefe(Hat("Tools/ParkingLotEntrances.cs", @"BeginEntrancePlacement\(bool showMissingPrompt\).*?if \(Fahrradmodus\) _zufahrtsart = Zufahrtsart.Zufahrt;"), "alle Startwege des Zugangsmodus uebernehmen keine Autoart");
        Pruefe(Hat("Tools/ParkingLotEntrances.cs", @"FehlenderZugang\(\).*?if \(Fahrradmodus\) return _entrances.Exists\(e => e != null\) \? null : ""zugang"";"), "Fahrradzugang braucht keine getrennte Ein-/Ausfahrt");
        Pruefe(Hat("Tools/ParkingLotEntrances.cs", @"BeginEntrancePlacement\(bool showMissingPrompt\).*?_uiSystem\?\.SetStatus\(Fahrradmodus \? T\(""ui.fahrradZugang""\)"), "Enter und automatischer Zugang zeigen Fahrradgrund statt Auto-Ein-/Ausfahrt");
        Pruefe(Hat("Tools/ParkingLotBusStops.cs", @"SetBusStopModeFromPanel\(bool on\).*?if \(on && \(Fahrradmodus \|\|"), "Bushaltmodus hat serverseitige Bauartsperre");
        Pruefe(Hat("Tools/ParkingLotBusStops.cs", @"PlanBusStopBuild\(.*?_pendingBusStops = Fahrradbau \? Array.Empty<BusStopPlacement>\(\) : _busStops.ToArray\(\);"), "gemerkte Auto-Halte gelangen nicht in Fahrradnacharbeit");
        Pruefe(Hat("Tools/ParkingLotZoning.cs", @"SetzeZoningModus\(bool an\).*?if \(an && Fahrradmodus\).*?return;"), "Zoning setzen ist auch per direktem Trigger gesperrt");
        Pruefe(Hat("Tools/ParkingLotZoningRoadEdit.cs", @"SetzeZoningSeitenArt\(bool an, bool nurRand\).*?if \(an && Fahrradmodus\).*?return;"), "Zoningseiten und Randzoning haben Bauartsperre");
        Pruefe(Hat("Tools/ParkingLotRandzoning.cs", @"ZufahrtTrifftBauland\(Entrance zufahrt\).*?if \(Fahrradmodus \|\| zufahrt == null\) return false;"), "gemerktes Auto-Zoning sperrt keine Fahrradzufahrt");
        Pruefe(Hat("Tools/ParkingLotZoningBand.cs", @"ZoningSperrquads\(\).*?if \(Fahrradmodus \|\| layout == null \|\| !_closed\)"), "keine Autostrassen-Sperrquads bei Fahrrad");
        Pruefe(Hat("Tools/ParkingLotBauzettelSchreiber.cs", @"if \(settings.Fahrzeugart == Fahrzeugart.Fahrrad\).*?q.Zoningflaechen = Array.Empty<ParkingGeometry.Zoningflaeche>\(\);.*?q.Randzoning = Array.Empty<ParkingGeometry.RandzoningLinie>\(\);.*?q.Seitenplan = Array.Empty<"), "Fahrradzettel enthaelt keine versteckten Auto-Zonen");
    }
}
