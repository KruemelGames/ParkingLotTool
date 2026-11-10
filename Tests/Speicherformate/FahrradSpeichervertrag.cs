using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;
using Unity.Mathematics;

internal static partial class Program
{
    private static void FahrradSpeichervertrag()
    {
        foreach(var art in new[]{Fahrzeugart.Auto,Fahrzeugart.Fahrrad})
        foreach(var layout in new[]{Fahrradlayout.A,Fahrradlayout.C,Fahrradlayout.D})
        foreach(var stil in new[]{Fahrradstil.Schlicht,Fahrradstil.Rahmen,Fahrradstil.Platz})
        {
            var s=art==Fahrzeugart.Fahrrad?LayoutSettings.Fahrrad(layout,stil):LayoutSettings.Cs2;
            s.Fahrradlayout=layout;s.Fahrradstil=stil;
            var r=new ParkingLotBuildReceipt
            {
                Version=ParkingLotBuildReceipt.CurrentVersion,Fahrzeugart=art,Fahrradlayout=layout,Fahrradstil=stil,
                Es=s.Es,Ai=s.Ai,Cw=s.Cw,Sl=s.Sl,Sw=s.Sw,Md=s.Md,Cr=s.Cr,
                Gassenbreite=s.Gassenbreite,Angle=s.Angle,KantenVersatz=s.KantenVersatz,
                Qk=s.Qk,Randstrassen=s.Randstrassen,Auto=s.Auto,AutomaticEntrances=s.AutomaticEntrances,
                Zellen=s.Zellen,EineFlaeche=s.EineFlaeche,NoNotch=s.NoNotch,Single=s.Single,NoHalf=s.NoHalf,
                AngleMode=ParkingLotBuildReceipt.EncodeAngleMode(s.AngleMode),
            };
            var w=new Schreiber();r.Serialize(w);
            var felder=w.Felder.Select(f=>new Feld {Typ=f.Typ.Name,Wert=JsonSerializer.SerializeToElement(f.Wert)}).ToList();
            var leser=new Leser(felder);var neu=Lies<ParkingLotBuildReceipt>(leser);
            Pruefe(neu.Version==ParkingLotBuildReceipt.CurrentVersion && neu.Fahrzeugart==art && neu.Fahrradlayout==layout && neu.Fahrradstil==stil,"Roundtrip "+art+"/"+layout+"/"+stil);
            Pruefe(leser.Position==felder.Count,"aktuelle Fassung exakt bis Datensatzende gelesen");
            var geladen=neu.ToLayoutSettings(Array.Empty<Entrance>());
            Pruefe(geladen.Fahrzeugart==art && geladen.Fahrradlayout==layout && geladen.Fahrradstil==stil,"Edit-Settings behalten Art/Layout/Stil");
            var w2=new World();var e2=w2.EntityManager;var lot2=new Entity(940,1);var p2=new Entity(941,1);
            e2.Set(lot2,new Game.Prefabs.PrefabRef {m_Prefab=p2});
            if(art==Fahrzeugart.Fahrrad) e2.Set(p2,new ParkingLotFahrradLayout {Layout=layout});
            Pruefe(ParkingLotFahrradBetrieb.PasstZettel(e2,lot2,neu),"Edit akzeptiert Typ/Layout des wirklichen Lots");
            var falsch=neu;falsch.Fahrzeugart=art==Fahrzeugart.Auto?Fahrzeugart.Fahrrad:Fahrzeugart.Auto;
            Pruefe(!ParkingLotFahrradBetrieb.PasstZettel(e2,lot2,falsch),"gueltiger Zettel mit falscher Fahrzeugart kann Bestand nicht umwandeln");
            if(art==Fahrzeugart.Fahrrad)
            {
                falsch=neu;falsch.Fahrradlayout=layout==Fahrradlayout.A?Fahrradlayout.C:Fahrradlayout.A;
                Pruefe(!ParkingLotFahrradBetrieb.PasstZettel(e2,lot2,falsch),"Zettel mit falschem Fahrradlayout kann Bestand nicht umdeuten");
            }
            var breite=art==Fahrzeugart.Auto?120:30;var tiefe=art==Fahrzeugart.Auto?90:20;
            var ring=new[]{new float2(0,0),new float2(breite,0),new float2(breite,tiefe),new float2(0,tiefe)};
            var vorher=ParkingGeometry.Build(ring,s);var nachher=ParkingGeometry.Build(ring,geladen);
            Pruefe(vorher.Stalls==nachher.Stalls && (vorher.Fahrrad==null)==(nachher.Fahrrad==null)
                && vorher.GrassSurface.Length==nachher.GrassSurface.Length && vorher.AsphaltSurface.Length==nachher.AsphaltSurface.Length,"Save/Load behaelt gebauten Layouttyp, Stil und Kapazitaet");
            if(vorher.Fahrrad!=null)
                Pruefe(vorher.Fahrrad.Objekte.Zip(nachher.Fahrrad.Objekte,(a,b)=>a.Position.Equals(b.Position) && a.Drehung==b.Drehung && a.Prefab==b.Prefab).All(x=>x)
                    && vorher.Fahrrad.Objekte.Count==nachher.Fahrrad.Objekte.Count,"Fahrradobjekte bleiben nach Roundtrip identisch");
            foreach(var neuwahl in new[]{Fahrzeugart.Auto,Fahrzeugart.Fahrrad})
            {
                var wirksam=LayoutSettings.Cs2;wirksam.Fahrzeugart=neuwahl;
                Bauartvertrag.Edit(wirksam,art,layout,stil);
                Pruefe(wirksam.Fahrzeugart==art && wirksam.Fahrradlayout==layout && wirksam.Fahrradstil==stil,"Neubauwahl kann Edit-Art nicht aendern");
            }
        }
        Pruefe(!Bauartvertrag.Gueltig((Fahrzeugart)9,Fahrradlayout.A),"unbekannte Fahrzeugart ungueltig");
        Pruefe(!Bauartvertrag.Gueltig(Fahrzeugart.Fahrrad,(Fahrradlayout)1),"entfallenes B ungueltig");
        Pruefe(Bauartvertrag.Stil(9)==Fahrradstil.Schlicht && Bauartvertrag.Stil(2)==Fahrradstil.Platz,"unbekannter gelesener Stil wird Schlicht");
        var freie=LayoutSettings.Fahrrad(Fahrradlayout.D);
        Bauartvertrag.Edit(freie,null,Fahrradlayout.A);
        Pruefe(freie.Fahrzeugart==Fahrzeugart.Fahrrad && freie.Fahrradlayout==Fahrradlayout.D,"Neubau ohne Edit bleibt frei");

        var welt=new World();var em=welt.EntityManager;
        var lot=new Entity(910,1);var traeger=new Entity(911,1);var gehen=new Entity(912,1);
        em.Set(lot,new ParkingLotBuildReceipt {Version=7,Fahrzeugart=Fahrzeugart.Fahrrad,Fahrradlayout=Fahrradlayout.D});
        em.Set(traeger,new Game.Common.Owner {m_Owner=lot});
        em.Set(gehen,new Game.Common.Owner {m_Owner=lot});
        Pruefe(ParkingLotFahrradBetrieb.IstBestand(em,lot),"Ladefenster ohne Prefabmetadaten ist gegen Auto-Reparatur gesichert");
        Pruefe(!em.HasBuffer<Game.Areas.SubArea>(traeger),"IEmptySerializable-Situation: 0 geladene Geh-Verweise");
        var kinder=new[]{gehen,gehen,lot,traeger,Entity.Null};
        var n=ParkingLotFahrradBetrieb.LadeGehflaechen(em,lot,traeger,kinder);
        Pruefe(n==1 && em.GetBuffer<Game.Areas.SubArea>(traeger).Length==1,"1 echte Geh-Flaeche erreicht Fahrradtraeger nach Laden");
        Pruefe(em.GetBuffer<Game.Areas.SubArea>(lot).Length==1 && em.GetBuffer<Game.Areas.SubArea>(traeger).Length==1
            && em.GetBuffer<Game.Areas.SubArea>(traeger)[0].m_Area==gehen,"Abrisswurzel und Navigationswurzel zeigen auf dasselbe Kind");
        Pruefe(ParkingLotFahrradBetrieb.LadeGehflaechen(em,lot,traeger,kinder)==1 && em.GetBuffer<Game.Areas.SubArea>(lot).Length==1,"Laden idempotent, kein Zyklus und keine Duplikate");
        var netze=em.AddBuffer<Game.Net.SubNet>(lot);
        netze.Add(new Game.Net.SubNet { m_SubNet=new Entity(930,1) });
        netze.Add(new Game.Net.SubNet { m_SubNet=new Entity(931,1) });
        Pruefe(ParkingLotFahrradBetrieb.LadeNetze(em,lot,traeger)==2 && em.GetBuffer<Game.Net.SubNet>(traeger)[1].m_SubNet==new Entity(931,1),"0 geladene Fahrrad-Netzverweise werden aus der nativen Besitzerliste rekonstruiert");
        Pruefe(ParkingLotFahrradBetrieb.LadeNetze(em,lot,traeger)==2,"Netzverweise bleiben idempotent");
        var auto=new Entity(913,1);em.Set(auto,new ParkingLotBuildReceipt {Version=6});
        Pruefe(!ParkingLotFahrradBetrieb.IstBestand(em,auto) && ParkingLotFahrradBetrieb.LadeGehflaechen(em,auto,traeger,kinder)==0,"Auto-Bestand wird von Fahrrad-Ladehilfe nicht angefasst");
        em.Set(lot,new Game.Prefabs.PrefabRef {m_Prefab=new Entity(920,1)});
        em.Set(traeger,new Game.Prefabs.PrefabRef {m_Prefab=new Entity(920,1)});
        em.AddBuffer<Game.Objects.SubObject>(traeger);
        Pruefe(ParkingLotFahrradBetrieb.AbrissTraeger(em,lot,new[]{traeger},out var zahl)==traeger && zahl==1,"Abriss findet 1 Fahrradtraeger ohne Mod-Relation ueber Owner/Prefab");
        var zweiter=new Entity(921,1);em.Set(zweiter,new Game.Common.Owner {m_Owner=lot});
        em.Set(zweiter,new Game.Prefabs.PrefabRef {m_Prefab=new Entity(920,1)});em.AddBuffer<Game.Objects.SubObject>(zweiter);
        Pruefe(ParkingLotFahrradBetrieb.AbrissTraeger(em,lot,new[]{traeger,zweiter},out zahl)==Entity.Null && zahl==2,"mehrdeutige Traeger werden nie geraten");
        em.Set(zweiter,new Game.Prefabs.PrefabRef {m_Prefab=new Entity(922,1)});
        Pruefe(ParkingLotFahrradBetrieb.AbrissTraeger(em,lot,new[]{zweiter},out zahl)==Entity.Null && zahl==0,"fremdes Prefab bleibt beim Abriss unangetastet");
        em.Set(traeger,new Game.Common.Owner {m_Owner=auto});
        Pruefe(ParkingLotFahrradBetrieb.AbrissTraeger(em,lot,new[]{traeger},out zahl)==Entity.Null && zahl==0,"fremder Besitzer bleibt unangetastet");

        foreach(var layout in new[]{Fahrradlayout.A,Fahrradlayout.C,Fahrradlayout.D})
        {
            Pruefe(Bauartvertrag.ProtokollPasst("Fahrrad",layout.ToString(),Fahrzeugart.Fahrrad,layout),"passender Fahrrad-Protokolleintrag wird gefunden");
            Pruefe(!Bauartvertrag.ProtokollPasst(null,null,Fahrzeugart.Fahrrad,layout),"altes Auto-Protokoll darf Fahrrad nicht umwandeln");
            Pruefe(!Bauartvertrag.ProtokollPasst("Fahrrad",layout.ToString(),Fahrzeugart.Auto,null),"Fahrrad-Protokoll darf Auto nicht umwandeln");
        }
        Pruefe(Bauartvertrag.ProtokollPasst(null,null,Fahrzeugart.Auto,null),"historisches Auto-Protokoll bleibt lesbar");
        Pruefe(!Bauartvertrag.ProtokollPasst("Fahrrad","C",Fahrzeugart.Fahrrad,Fahrradlayout.D),"Protokoll gleicher Form mit anderem Fahrradlayout ausgeschlossen");
    }
}
