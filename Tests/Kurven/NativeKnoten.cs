using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Unity.Mathematics;
using ParkingLotTool.Geometry;
using K=ParkingLotTool.Geometry.Strassenkurve;

internal static class NativeKnoten
{
    private static K.Bezier Kurve(JsonElement band,string key)
    {
        var p=band.GetProperty(key);float3 Punkt(int i)=>new float3(p[i][0].GetSingle(),p[i][1].GetSingle(),p[i][2].GetSingle());
        return new K.Bezier(Punkt(0),Punkt(1),Punkt(2),Punkt(3));
    }
    private static K.Teil[] Haelften(K.Bezier c)
    {c.Teile(.5f,out var a,out var b);return K.Ordne(a,b,c.A);}
    private static K.Fang Fang(K.Kante k,int seite,float t)
    {
        int teil=t<.5f?0:1;float u=t<.5f?t*2:(t-.5f)*2;
        return new K.Fang {Kante=k.Id,Seite=seite,Teil=teil,T=u,Punkt=k.Seite(seite)[teil].Kurve.Punkt(u)};
    }
    internal static void Pruefe(Action<bool,string> test)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"NativeKnoten.json")));
        var daten=json.RootElement;var aussen=daten.GetProperty("R148");var innen=daten.GetProperty("R136");
        var na=daten.GetProperty("KnotenA");var nb=daten.GetProperty("KnotenB");
        // Native Segment-Invertierung vertauscht links/rechts UND kehrt die Kurven um.
        // Knoten-A: (Aussen,Mitte)/(Mitte,Innen); Knoten-B: (Innen,Mitte)/(Mitte,Aussen).
        var angebotA=new[]{Kurve(na,"LeftLeft"),Kurve(na,"LeftRight"),Kurve(na,"RightLeft"),Kurve(na,"RightRight")};
        var angebotB=new[]{Kurve(nb,"LeftLeft"),Kurve(nb,"LeftRight"),Kurve(nb,"RightLeft"),Kurve(nb,"RightRight")};
        var quelle=new Dictionary<long,K.Kante>();var kanten=new Dictionary<long,K.Kante>();
        var knoten=new Dictionary<long,K.Knoten>();var suche=new List<long>();
        var zeiten=new List<double>();
        foreach(bool gedreht in new[]{false,true})foreach(bool zurueck in new[]{false,true})
        foreach(float nah in new[]{.1f,.9999f})
        {
            var ca=Kurve(aussen,"EdgeA");var ia=Kurve(innen,"EdgeA");
            var cb=Kurve(aussen,"EdgeB");var ib=Kurve(innen,"EdgeB");
            var a=new K.Kante {Id=1,Start=10,Ende=20,Links=Haelften(ca),Rechts=Haelften(ia),
                Mittellinie=Kurve(daten,"MittellinieA"),HatMittellinie=true};
            var b=new K.Kante {Id=2,Start=gedreht?30:20,Ende=gedreht?20:30,
                Links=Haelften(gedreht?ib.Umgekehrt():cb),Rechts=Haelften(gedreht?cb.Umgekehrt():ib),
                Mittellinie=gedreht?Kurve(daten,"MittellinieB").Umgekehrt():Kurve(daten,"MittellinieB"),HatMittellinie=true};
            quelle.Clear();quelle.Add(1,a);quelle.Add(2,b);
            var node=new K.Knoten {Id=20,Kanten=new long[]{1,2}};
            var f=Fang(a,1,nah);float bt=nah>.9f?.0001f:.9f;
            var g=Fang(b,gedreht?-1:1,gedreht?1-bt:bt);
            if(zurueck){var temp=f;f=g;g=temp;}
            void Ergaenze(K.Kante k)
            {
                knoten[20]=node;
                StrassenkurvenNetz.ErgaenzeAngebot(node,k.Id==1?angebotA:angebotB);
            }
            kanten.Clear();kanten[1]=a;knoten.Clear();knoten[20]=node;
            test(!StrassenkurvenNetz.Verbunden(1,2,kanten,knoten),"Zielgeometrie muss vor Verbindungsfreigabe gelesen sein");
            var uhr=Stopwatch.StartNew();
            StrassenkurvenNetz.Lade(f,g,kanten,knoten,suche,id=>quelle.TryGetValue(id,out var k)?k:null,Ergaenze);
            test(kanten.Count==2,"Beide Edges vor Verfolgung gelesen");
            test(node.Uebergaenge.Length==6,"Zweite native Knotenhaelfte muss im Angebot sein");
            var e=K.Verfolge(f,g,kanten,knoten);uhr.Stop();zeiten.Add(uhr.Elapsed.TotalMilliseconds);
            test(e.Gruppe != null, "Native Knotenkette wird gemeinsam eingefroren");
            KurvenkanteTests.PruefeGruppe(e.Gruppe, test);
            test(e.Folgt,$"Native Grad-2-Fixtur: gedreht={gedreht}, zurueck={zurueck}, nah={nah}: {e.Grund}");
            test(math.distance(e.Punkte[0],f.Punkt)<.001f && math.distance(e.Punkte[e.Punkte.Length-1],g.Punkt)<.001f,"Native Fixtur verfolgt ganzen Bogen");
            test(e.Radius>130 && e.Radius<160,"Native Fixtur behaelt Strassenradius");
            // Gegenprobe: genau die Knotenhaelfte der Ziel-Edge fehlt.
            node.Uebergaenge=Array.Empty<K.Bezier>();
            StrassenkurvenNetz.ErgaenzeAngebot(node,zurueck?angebotB:angebotA);
            var luecke=K.Verfolge(f,g,kanten,knoten);
            test(luecke.Grund==K.Grund.Luecke && luecke.Luecke.Vorhanden,"Fehlende native Haelfte liefert messbare Luecke");
            test(luecke.Luecke.Angebote==3 && luecke.Luecke.KantenendeZumAngebot<.001
                && luecke.Luecke.AngebotZumZiel>10,"Lueckenmessung nennt Anzahl und beide Endabstaende");
        }
        var kurz=new K.Kante {Id=1,Start=10,Ende=20,Links=Haelften(Kurve(aussen,"EdgeA"))};
        var kurzErgebnis=K.Verfolge(Fang(kurz,1,.809f),Fang(kurz,1,.8091f),
            new Dictionary<long,K.Kante>{{1,kurz}},new Dictionary<long,K.Knoten>(),10.4,1);
        test(kurzErgebnis.Radius>130 && kurzErgebnis.Grund==K.Grund.ZuKurz,"R148-Kurzabschnitt ist ZuKurz, nie ZuEng");
        Log(test);
        zeiten.Sort();Console.WriteLine($"Native Grad-2-Fixtur inkl. Netzaufnahme: {zeiten.Count} Faelle, warm Median {zeiten[zeiten.Count/2]:F3} ms (ohne ECS)");
    }
    private static void Log(Action<bool,string> test)
    {
        var cache=new StrassenkurvenLog();
        var fest=new K.Ergebnis {Von=new K.Fang {Kante=1,Seite=1},Bis=new K.Fang {Kante=1,Seite=1},Stuecke=8};
        var maus=new K.Ergebnis {Von=fest.Bis,Bis=new K.Fang {Kante=2,Seite=1},Grund=K.Grund.Luecke};
        test(cache.Neu(0,fest) && cache.Neu(-1,maus),"Je Quelle erste Logzeile");
        int logs=0;
        for(int i=0;i<1000;i++)
        {
            maus.Bis.T=i/1000f;maus.Radius=i;fest.Abweichung=i;
            if(cache.Neu(0,fest))logs++;if(cache.Neu(-1,maus))logs++;
        }
        test(logs==0,"Wechselnde feste/Mausquelle sowie t/ms erzeugen keine Logflut");
        test(cache.Neu(1,fest),"Jede feste Kante hat eigenen Logzustand");
        maus.Stuecke++;test(cache.Neu(-1,maus),"Neue Stueckzahl wird geloggt");
        maus.Bis.Seite=-1;test(cache.Neu(-1,maus),"Neue Seite wird geloggt");
        maus.Bis.Kante=3;test(cache.Neu(-1,maus),"Neue Herkunftskante wird geloggt");
        maus.Grund=K.Grund.ZuKurz;test(cache.Neu(-1,maus),"Neuer Grund wird geloggt");
        // Den identischen Pfad vor der Messung ausfuehren: generische
        // Vergleichshelfer und Tiering duerfen nicht Teil der Messung sein.
        for (int i = 0; i < 200000; i++) { cache.Neu(0, fest); cache.Neu(-1, maus); }
        var uhr = new Stopwatch();
        uhr.Start();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++){if(cache.Neu(0,fest))logs++;if(cache.Neu(-1,maus))logs++;}
        uhr.Stop();long allokiert=GC.GetAllocatedBytesForCurrentThread()-bytes;
        test(logs==0 && allokiert==0,"Logfilter in Ruhe allokationsfrei und stumm");
        Console.WriteLine($"Logfilter: {uhr.Elapsed.TotalMilliseconds:F3} ms / 200000 Aufrufe, {allokiert} Bytes, {logs} Wiederholungszeilen");
        cache.Leere();test(cache.Neu(0,fest),"Neue Shape leert Logzustand");
    }
}
