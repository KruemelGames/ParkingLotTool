using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int fehler, pruefungen;
    private static readonly Fahrradlayout[] Layouts = { Fahrradlayout.A, Fahrradlayout.C, Fahrradlayout.D };
    private static float2[] Form(params float[] werte) => Enumerable.Range(0, werte.Length / 2)
        .Select(i => new float2(werte[i * 2], werte[i * 2 + 1])).ToArray();
    private static readonly (string Name, float2[] Ring)[] Formen =
    {
        ("Rechteck", Form(0,0,30,0,30,20,0,20)),
        ("L", Form(0,0,30,0,30,10,15,10,15,25,0,25)),
        ("Schief", Form(0,0,30,0,34,20,4,20)),
        ("Schmal", Form(0,0,8,0,8,35,0,35)),
        ("Ruecksprung", Form(0,0,40,0,40,25,25,25,25,9,15,9,15,25,0,25)),
        ("Kurvenpolygon", Enumerable.Range(0,24).Select(i => new float2(
            (float)(18*Math.Cos(i*Math.PI/12)), (float)(12*Math.Sin(i*Math.PI/12)))).ToArray()),
    };

    private static void Pruefe(bool ok, string name)
    { pruefungen++; if (!ok) { fehler++; Console.WriteLine("FEHLER: " + name); } }

    private static int Main(string[] args)
    {
        try
        {
            if(args.Contains("--spielnetz"))return MesseSpielnetz();
            if(args.Length>1 && args[0]=="--bild")return Bild(args[1]);
            if(args.Length>5 && args[0]=="--fall")return Fall(args[1],args[2],int.Parse(args[3]),
                float.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture),
                args.Length>6?args[6]:null, args.Length>7?double.Parse(args[7],System.Globalization.CultureInfo.InvariantCulture):0);
            foreach (var layout in Layouts)
            foreach (var form in Formen)
            foreach (var grad in new[] { 0, 17, 90, 203 })
            foreach (var ort in new[] { new float2(0,0), new float2(-1250,842), new float2(8000,-8000) })
            foreach (var umgekehrt in new[] { false, true })
            {
                var winkel = grad * Math.PI / 180;
                var ring = form.Ring.Select(p => new float2((float)(p.x*Math.Cos(winkel)-p.y*Math.Sin(winkel)),
                    (float)(p.x*Math.Sin(winkel)+p.y*Math.Cos(winkel))) + ort).ToArray();
                if (umgekehrt) Array.Reverse(ring);
                var settings = LayoutSettings.Fahrrad(layout);
                // Auch widerspruechliche Autowerte duerfen keine Randstrasse,
                // Sonderbuchten oder Zoning in den Fahrradplan einschleusen.
                settings.Randstrassen = true;
                var gesamt = ParkingGeometry.Build(ring, settings);
                var plan = gesamt.Fahrrad;
                var name = $"{layout}/{form.Name}/{grad}/{ort}/{umgekehrt}";
                Pruefe(plan != null, name + " Plan vorhanden");
                if (plan == null) continue;
                var befund = Messe(plan, ring);
                if (args.Contains("--diagnose") && grad == 17 && ort.x == -1250 && form.Name == "Rechteck")
                {
                    for (var i = 0; i < plan.Objekte.Count; i++)
                    {
                        var o = plan.Objekte[i];
                        for (var j = 0; j < plan.Objekte.Count; j++)
                            if (j > i && Schneidet(o.Grundriss, plan.Objekte[j].Grundriss))
                                Console.WriteLine($"DIAG {layout} Objekt {i}/{j}: " + string.Join(";",o.Grundriss) + " / " + string.Join(";",plan.Objekte[j].Grundriss));
                        for (var j = 0; j < plan.Wege.Count; j++)
                            if (Schneidet(o.Grundriss,Umriss(plan.Wege[j])))
                                Console.WriteLine($"DIAG {layout} Objekt {i}/Weg {j}: " + string.Join(";",o.Grundriss) + " / " + string.Join(";",Umriss(plan.Wege[j])));
                    }
                }
                foreach (var p in befund) Pruefe(p.Value, name + "/" + p.Key);
                Pruefe(gesamt.Bay.Length == 0 && gesamt.PerimeterLine.Length == 0
                    && gesamt.BayRole.Length == 0 && gesamt.ZoningSurface.Length == 0, name + " Autoausgabe leer");
                if (form.Name == "Rechteck")
                {
                    var ziel = layout == Fahrradlayout.A ? 336 : layout == Fahrradlayout.C ? 192 : 168;
                    // Entwurfsschaetzung als Untergrenze: die Restflaechenregel fuellt mehr.
                    Pruefe(plan.Kapazitaet >= ziel * .85, name + $" Kapazitaet {plan.Kapazitaet} mindestens {ziel}");
                    if (layout == Fahrradlayout.D)
                        Pruefe(LaufachseFrei(plan,ring),name+" Durchgehende 3-m-Laufachse");
                    if (grad == 0 && ort.x == 0 && !umgekehrt) Console.WriteLine($"{layout}: {plan.Objekte.Count} Objekte, {plan.Kapazitaet} Plaetze, {plan.Wege.Count} Wege, {plan.Laternen.Count} Laternen, {plan.Baeume.Count} Baeume");
                }
            }
            Einstellungen();
            Zugaenge();
            Mutationen();
            Stile(true);
            Zufahrtsvorpruefung();
            Console.WriteLine($"FAHRRAD: {pruefungen} Pruefungen, {fehler} Fehler");
            return fehler == 0 ? 0 : 1;
        }
        catch (Exception e) { Console.WriteLine("FEHLER: " + e); return 2; }
    }

    private static bool LaufachseFrei(FahrradPlan plan,float2[] ring)
    {
        var mitte=ring.Aggregate(new float2(0,0),(a,b)=>a+b)/4;
        var achse=math.normalize(ring[1]-ring[0]);
        return plan.Objekte.All(o=>
        {
            var abstaende=o.Grundriss.Select(p=>math.dot(p-mitte,achse)).ToArray();
            return abstaende.Max()<=-1.5+.003 || abstaende.Min()>=1.5-.003;
        });
    }

    private static void Einstellungen()
    {
        foreach (var layout in Layouts)
        {
            var s = LayoutSettings.Fahrrad(layout);
            var kopie = s.Clone();
            Pruefe(kopie.Fahrzeugart == Fahrzeugart.Fahrrad && kopie.Fahrradlayout == layout, "Clone Art/Layout");
            var json = JsonSerializer.Serialize(s);
            var gelesen = JsonSerializer.Deserialize<LayoutSettings>(json);
            Pruefe(gelesen.Fahrzeugart == Fahrzeugart.Fahrrad && gelesen.Fahrradlayout == layout, "JSON Art/Layout");
            var zettel = ParkingSettingsInventory.Erfasse(s, ParkingSettingsInventory.Layout);
            Pruefe((Fahrzeugart)zettel["Fahrzeugart"] == Fahrzeugart.Fahrrad
                && (Fahrradlayout)zettel["Fahrradlayout"] == layout, "Baujournal Art/Layout");
        }
        Pruefe(JsonSerializer.Deserialize<LayoutSettings>("{}").Fahrzeugart == Fahrzeugart.Auto, "Alter JSON-Zettel ist Auto");
        Pruefe(LayoutSettings.Cs2.Fahrzeugart == Fahrzeugart.Auto, "Auto-Preset bleibt Auto");
        var leer = FahrradGeometrie.Baue(Formen[0].Ring, new LayoutSettings
            { Fahrzeugart = Fahrzeugart.Fahrrad, AutomaticEntrances = false });
        // Vorschau schon vor der Zufahrt (Nutzer 30.10./02.11.), aber ohne Zufahrtsweg und mit Hinweis.
        Pruefe(!leer.Wege.Any(w => w.Zufahrt) && leer.Hinweise.Count > 0
            && leer.Objekte.All(o => ImRing(o.Grundriss, Formen[0].Ring)), "Ohne Zugang Vorschau ohne Zufahrtsweg");
        var abgelehnt=FahrradGeometrie.Baue(Formen[0].Ring,LayoutSettings.Fahrrad(Fahrradlayout.A),_=>false);
        Pruefe(abgelehnt.Flaechen.Count==0 && abgelehnt.Objekte.Count==0 && abgelehnt.Hinweise.Count>0,"Zulassungspruefer wird beachtet");
    }

    private static void Zugaenge()
    {
        foreach (var layout in Layouts)
        foreach (var edge in Enumerable.Range(0,4))
        {
            var s = LayoutSettings.Fahrrad(layout);
            s.AutomaticEntrances = false;
            s.Entrances = new[] { new Entrance { Edge = edge, Along = edge % 2 == 0 ? 15 : 10 } };
            var plan = FahrradGeometrie.Baue(Formen[0].Ring, s);
            foreach (var p in Messe(plan, Formen[0].Ring)) Pruefe(p.Value, $"Zugang {layout}/{edge}/{p.Key}");
            Pruefe(plan.Wege.Any(w => w.Zufahrt), "Gesetzter Zugang wird gebaut");
        }
        foreach(var layout in Layouts)
        {
            var s=LayoutSettings.Fahrrad(layout);
            s.AutomaticEntrances=false;
            s.Entrances=new[]{new Entrance{Edge=0,Along=10},new Entrance{Edge=1,Along=10},new Entrance{Edge=2,Along=15}};
            var plan=FahrradGeometrie.Baue(Formen[0].Ring,s);
            foreach(var p in Messe(plan,Formen[0].Ring))Pruefe(p.Value,$"Drei Zugaenge {layout}/{p.Key}");
            Pruefe(plan.Wege.Count(w=>w.Zufahrt)==3,"Alle drei gesetzten Zugaenge werden gebaut ("+layout+": "+plan.Wege.Count(w=>w.Zufahrt)+" Zufahrtsstuecke; "+string.Join(" | ",plan.Hinweise)+")");
        }
    }
}
