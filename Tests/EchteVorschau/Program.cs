using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Mathematics;

internal static partial class Program
{
    private static int _n,_fehler;
    private static void Pruefe(bool ja,string text) { _n++; if(!ja) {_fehler++;Console.WriteLine("FEHLER EchteVorschau: "+text);} }
    private static int Main()
    {
        try {return Ablauf();} catch(Exception e) {Console.WriteLine("FEHLER EchteVorschau: "+e);return 1;}
    }
    private static int Ablauf()
    {
        GriffPruefungen();
        var komponenten=new HashSet<Unity.Entities.ComponentType>();
        new ParkingLotMusterobjektPrefab().GetArchetypeComponents(komponenten);
        Pruefe(komponenten.Contains(Unity.Entities.ComponentType.ReadWrite<ParkingLotMusterflaeche>()),"Marker vor der nativen Ausgabe");
        Pruefe(!komponenten.Contains(Unity.Entities.ComponentType.ReadWrite<Game.Rendering.Emissive>()),"Keine Beleuchtungsdaten");
        Pruefe(!komponenten.Contains(Unity.Entities.ComponentType.ReadWrite<Game.Rendering.LightState>()),"Kein Leuchtzustand");
        Pruefe(komponenten.Contains(Unity.Entities.ComponentType.ReadWrite<Game.Rendering.MeshBatch>()),"Darstellung bleibt erhalten");
        foreach(var jetzt in Enum.GetValues<EchteVorschauArt>())
        foreach(var ziel in Enum.GetValues<EchteVorschauArt>())
        foreach(bool an in new[] {false,true})
        {
            var neu=EchteVorschau.Schalten(jetzt,ziel,an);
            var soll=ziel==EchteVorschauArt.Aus ? jetzt : an ? ziel : jetzt==ziel ? EchteVorschauArt.Aus : jetzt;
            Pruefe(neu==soll,"Alle Schalteruebergaenge");
            Pruefe(new[] {EchteVorschauArt.Flaechen,EchteVorschauArt.Laternen,EchteVorschauArt.Vegetation}.Count(a=>a==neu)<=1,"Nie zwei aktiv");
        }
        foreach(int neu in new[] {-1,0,1,2,3,4,100})
        foreach(bool alt in new[] {false,true})
        {
            int soll=neu>=0 && neu<=3 ? neu : 0;
            foreach(string json in new[] {$"{{\"SurfacePreview\":{alt.ToString().ToLowerInvariant()},\"EchteVorschau\":{neu}}}",
                $"{{\"EchteVorschau\":{neu},\"SurfacePreview\":{alt.ToString().ToLowerInvariant()}}}"})
            {
                Pruefe(ParkingLotUISystem.LiesVorschau(json)==soll,"Neuer Wert vor altem Bool unabhaengig von Reihenfolge");
                var aus=JObject.Parse(ParkingLotUISystem.SchreibeVorschau(json));
                Pruefe(aus.Property("SurfacePreview")==null && aus.Count==1 && (int)aus["EchteVorschau"]==soll,"Ein gespeicherter Wert, keine drei Schalter");
            }
        }
        Pruefe(ParkingLotUISystem.LiesVorschau("{}")==0,"Alter Standard AUS");
        Pruefe(ParkingLotUISystem.LiesVorschau("{\"SurfacePreview\":true}")==1,"Altes AN migriert zu Flaechen");
        Pruefe(ParkingLotUISystem.LiesVorschau("{\"SurfacePreview\":false}")==0,"Altes AUS bleibt AUS");
        Pruefe((int)JObject.Parse(ParkingLotUISystem.SchreibeVorschau("{\"SurfacePreview\":true}"))["EchteVorschau"]==1,"Migration ueberlebt Schreiben/Lesen");
        for(int zustand=0;zustand<38;zustand++)for(int alter=-1;alter<6;alter++)
            Pruefe(EchteVorschau.Altersmesh(zustand,alter)==(zustand<1 || zustand>6 || alter<0 || zustand==alter+1),"Nur gewaehltes Altersmesh, Busch nicht als Baum erraten");
        var lot=new[] {new float2(0,0),new float2(100,0),new float2(100,100),new float2(0,100)};
        var b=new OberflaechenMuster.Bereich(new float2(8000,-12000),20,math.normalize(new float2(3,4)));
        foreach(var p in new[] {new float2(0),new float2(20,0),new float2(-20,20),new float2(19.99f,-19.99f)})
            Pruefe(EchteVorschau.Enthaelt(b,b.Welt(b.Mitte+p)),"Gedrehter Rand/eigene Mitte enthalten");
        Pruefe(!EchteVorschau.Enthaelt(b,b.Welt(b.Mitte+new float2(20.02f,0))),"Gedreht ausserhalb");
        Pruefe(!EchteVorschau.Enthaelt(b,new float2(float.NaN,0)),"NaN nicht anlegen");
        Pruefe(!EchteVorschau.Enthaelt(new OberflaechenMuster.Bereich(new float2(0),0),new float2(0)),"Entarteter Ausschnitt nicht anlegen");
        var punkte=new List<float2>();for(int x=0;x<100;x++)for(int y=0;y<100;y++)punkte.Add(new float2(x,y));
        var auswahl=EchteVorschau.Waehle(lot,lot[0],new float2(1,1),punkte);
        Pruefe(auswahl.Verkleinerungen>0 && auswahl.Indizes.Length>0 && auswahl.Indizes.Length<=256,"Budget verkleinert statt halb auszugeben");
        Pruefe(auswahl.Indizes.SequenceEqual(Enumerable.Range(0,punkte.Count).Where(i=>EchteVorschau.Enthaelt(auswahl.Ausschnitt,punkte[i]))),"Alle Objekte innerhalb des verkleinerten Ausschnitts");
        var leer=EchteVorschau.Waehle(lot,lot[0],new float2(1,0),Enumerable.Repeat(lot[0],300).ToArray());
        Pruefe(leer.Indizes.Length==0 && leer.Verkleinerungen==24,"Unaufloesbare Deckung nie als halben Satz ausgeben");
        Messung();
        Console.WriteLine($"Echte Vorschau: {_n} Pruefungen, {_fehler} Fehler.");return _fehler==0 ? 0 : 1;
    }
    private sealed class PunktLeser : System.Text.Json.Serialization.JsonConverter<float2>
    {
        public override float2 Read(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)
        {
            if(r.TokenType!=JsonTokenType.StartArray) throw new JsonException();
            r.Read();float x=r.GetSingle();r.Read();float y=r.GetSingle();r.Read();return new float2(x,y);
        }
        public override void Write(Utf8JsonWriter w,float2 p,JsonSerializerOptions o)
        {w.WriteStartArray();w.WriteNumberValue(p.x);w.WriteNumberValue(p.y);w.WriteEndArray();}
    }
    private static void Messung()
    {
        string root=Directory.GetCurrentDirectory();
        using var f=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Tests/GeometryParity/Laternen/F-l-form.json")));
        var daten=f.RootElement.GetProperty("Layout");
        float2[] Ring(JsonElement e)=>e.EnumerateArray().Select(p=>new float2(p[0].GetSingle(),p[1].GetSingle())).ToArray();
        var kleinLot=Ring(daten.GetProperty("Grundstueck"));
        // Fuer die Laternen die vorhandenen Layoutfelder mit ihren produktiven Typen einlesen.
        var klein=new ParkingLayout();
        foreach(var prop in typeof(ParkingLayout).GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
            if(prop.SetMethod!=null && daten.TryGetProperty(prop.Name,out var value))
                try { prop.SetValue(klein,System.Text.Json.JsonSerializer.Deserialize(value.GetRawText(),prop.PropertyType,new JsonSerializerOptions {IncludeFields=true,Converters={new PunktLeser()}})); } catch(JsonException) { }
        klein.Stalls=klein.Bay.Length;
        Pruefe(klein.Stalls==54,"Kleine Originalfixtur mit 54 Buchten");
        Pruefe(ParkingLanterns.Plan(klein,30).Laternen.Count==f.RootElement.GetProperty("Erwartet").GetProperty("30").GetArrayLength(),"Kleine Laternenfixtur unveraendert eingelesen");
        var grossLot=new[] {new float2(0,0),new float2(480,0),new float2(480,360),new float2(0,360)};
        var s=LayoutSettings.Cs2;s.Zellen=true;s.Auto=false;s.AngleMode="edge";s.AutomaticEntrances=false;
        var gross=ParkingGeometry.Build(grossLot,s);
        Pruefe(gross.Stalls>2000,"Grosse echte Layoutfixtur");
        var messungen=new List<object>();
        foreach(var fall in new[] {("F-l-form",kleinLot,klein),("Rechteck x4",grossLot,gross)})
        {
            var lampe=ParkingLanterns.Plan(fall.Item3,30);
            Pruefe(lampe.Laternen.Count>0,"Testfixtur muss gueltige Laternen finden");
            var optionen=new VegetationOptions {Enabled=true,Density=70,Ages=6,Seed=4711,Species=new[] {"Baum","Busch"}};
            var arten=new[] {new VegetationSpecies {Id="Baum",Tree=true,Spacing=5,Hoehe=12},new VegetationSpecies {Id="Busch",Tree=false,Spacing=1.5f,Hoehe=2}};
            var koerper=lampe.Laternen.Select(p=>new LaternenKoerper {Position=p.Position,Vorwaerts=new float2(1,0),Bein=new float3(.3f,5,.3f),Max=new float3(2,8,2)}).ToList();
            var pflanzen=ParkingVegetation.Plan(fall.Item3.GrassForVegetation,optionen,arten,koerper);
            Pruefe(pflanzen.Plants.Count>0,"Testfixtur muss gueltige Pflanzen finden");
            foreach(var art in new[] {("Laternen",lampe.Laternen.Select(p=>p.Position).ToArray()),("Vegetation",pflanzen.Plants.Select(p=>p.Position).ToArray())})
            {
                var zeiten=new double[31];EchteVorschau.Auswahl a=null;
                var richtung=OberflaechenMuster.Richtung(fall.Item3.AisleLine,fall.Item2,fall.Item2[0]);
                for(int n=-1;n<31;n++) {var uhr=Stopwatch.StartNew();a=EchteVorschau.Waehle(fall.Item2,fall.Item2[0],richtung,art.Item2);if(n>=0)zeiten[n]=uhr.Elapsed.TotalMilliseconds;}
                Array.Sort(zeiten);
                Console.WriteLine($"Messung {fall.Item1}/{art.Item1}: {fall.Item3.Stalls} Buchten, {art.Item2.Length} geplant, {a.Indizes.Length} im Ausschnitt, verkleinert {a.Verkleinerungen}; Median {zeiten[15]:F3}/p95 {zeiten[29]:F3} ms.");
                messungen.Add(new {fall=fall.Item1,art=art.Item1,buchten=fall.Item3.Stalls,gesamt=art.Item2.Length,ausschnitt=a.Indizes.Length,verkleinert=a.Verkleinerungen,medianMs=zeiten[15],p95Ms=zeiten[29]});
            }
        }
        var ordner=Path.Combine(root,".codex-build/echte-vorschau-5-20261018");Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner,"objekt-messung.json"),System.Text.Json.JsonSerializer.Serialize(messungen,new JsonSerializerOptions {WriteIndented=true}));
    }
}
