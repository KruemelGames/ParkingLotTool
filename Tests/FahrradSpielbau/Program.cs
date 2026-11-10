using System;
using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Mathematics;

internal static partial class Program
{
    private static int n,fehler;
    private static void Pruefe(bool ok,string name)
    {n++;if(!ok){fehler++;Console.WriteLine("FEHLER: "+name);}}
    private static int Main()
    {
        try { Fabrik(); Bau(); Vertraege(); Betrieb(); Betriebsvertraege(); Bestandsvertraege(); Bedienvertraege(); }
        catch(Exception e){fehler++;Console.WriteLine("FEHLER: "+e);}
        Console.WriteLine($"FAHRRAD-SPIELBAU: {n} Pruefungen, {fehler} Fehler");
        return fehler==0?0:1;
    }

    private static void Fabrik()
    {
        var kosten=new PathfindPrefab{name="Bicycle Pathfind"};
        kosten.AddComponent<CarPathfind>().m_DrivingCost=new(0,1,2,3);
        var lane=new NetLanePrefab{name="Bicycle Lane",m_PathfindPrefab=kosten};
        lane.AddComponent<CarLane>().m_Width=1.5f;
        lane.GetComponent<CarLane>().m_RoadType=Game.Net.RoadTypes.Bicycle;
        var piece=new NetPiecePrefab{name="Bike Piece",m_Width=3};
        piece.AddComponent<NetPieceLanes>().m_Lanes=new[]{new NetLaneInfo{m_Lane=lane,m_Position=new float3(.75f,0,0)}};
        piece.AddComponent<SpawnableArea>();
        var section=new NetSectionPrefab{name="Bike Section",m_Pieces=new[]{new NetPieceInfo{m_Piece=piece}}};
        var original=new PathwayPrefab{name="TwoWayBikePath",m_SpeedLimit=60,m_Sections=new[]{new NetSectionInfo{m_Section=section}}};
        original.AddComponent<SpawnableArea>();original.AddComponent<UIObject>();original.AddComponent<ObsoleteIdentifiers>();
        var registriert=new List<PrefabBase>();
        var klon=ParkingLotFahrradPrefabBau.Weg(original,ParkingLotFahrradPrefabBau.Schmal,2.5f,(q,k)=>registriert.Add(k));
        Pruefe(klon.name=="PLT Fahrradweg 2.5m intern","stabiler Speichername");
        Pruefe(klon.m_SpeedLimit==60 && original.m_SpeedLimit==60,"keine Auto-Tempo-Regel im Fahrradklon");
        var kp=klon.m_Sections[0].m_Section.m_Pieces[0].m_Piece;
        var kl=kp.GetComponent<NetPieceLanes>().m_Lanes[0];
        Pruefe(kp!=piece && kl.m_Lane!=lane && kl.m_Lane.m_PathfindPrefab!=kosten,"Kette vollstaendig isoliert");
        Pruefe(Math.Abs(kp.m_Width-2.5f)<.00001,"C-Querschnitt 2,5 m");
        Pruefe(Math.Abs(kl.m_Position.x-.625f)<.00001,"Spurlage skaliert");
        Pruefe(Math.Abs(kl.m_Lane.GetComponent<CarLane>().m_Width-1.25f)<.00001,"Spurbreite skaliert");
        Pruefe(kl.m_Lane.GetComponent<CarLane>().m_RoadType==Game.Net.RoadTypes.Bicycle,"Fahrradverkehr bleibt erhalten");
        Pruefe(kl.m_Lane.m_PathfindPrefab.GetComponent<CarPathfind>().m_DrivingCost.m_Behaviour==1,"Vanilla-Fahrradkosten erhalten");
        Pruefe(piece.m_Width==3 && piece.GetComponent<NetPieceLanes>().m_Lanes[0].m_Position.x==.75f
            && lane.GetComponent<CarLane>().m_Width==1.5f,"Vanilla bleibt unveraendert");
        Pruefe(registriert.Count>3 && registriert.Last()==klon,"Abhaengigkeiten vor Wurzel angemeldet");
        Pruefe(registriert.All(p=>p.GetComponent<SpawnableArea>()==null && p.GetComponent<UIObject>()==null
            && p.GetComponent<ObsoleteIdentifiers>()==null),"keine Spawn-Pools/UI/Vanilla-IDs");
        var lot=ParkingLotFahrradPrefabBau.Lot();
        Pruefe(lot.name=="PLT Fahrradparkplatzflaeche","Lot-Speichername");
        Pruefe(lot.GetComponent<ParkingFacility>().m_RoadTypes==Game.Net.RoadTypes.Bicycle,"Lot ist Fahrrad-Parkeinrichtung");
        Pruefe(lot.GetComponent<ParkingFacility>().m_GarageMarkerCapacity==0,"kein erfundener Garagenplatz");
        var gehen=ParkingLotFahrradPrefabBau.Gehflaeche();
        var nav=gehen.GetComponent<NavigationArea>();
        // 02.11.2026: nur Pedestrian wie die Vanilla-Walking-Area. Secondary Road gab eine
        // Offroad-Flaechenspur (PathUtils:1385-1390), die Raeder nicht befahren duerfen.
        Pruefe(nav.m_ConnectionType==RouteConnectionType.Pedestrian && nav.m_SecondaryType==RouteConnectionType.None
            && nav.m_RoadTypes==Game.Net.RoadTypes.Bicycle,"nativer Geh-/Fahrrad-Navigationsvertrag");
        Pruefe(lot.GetComponent<NavigationArea>()==null,"Gehflaeche ist Kind statt Rueckverweis auf Lot");
        Pruefe(gehen.m_Color.a==0 && gehen.m_EdgeColor.a==0,"Gehflaeche unsichtbar");
    }

    private static void Bau()
    {
        var ring=new[]{new float2(0,0),new float2(30,0),new float2(30,20),new float2(0,20)};
        foreach(var layout in new[]{Fahrradlayout.A,Fahrradlayout.C,Fahrradlayout.D})
        {
            // Gesetzte Zufahrt an der oberen Kante: im Spiel baut der Fahrradmodus nur
            // mit gesetzter Zufahrt (FehlenderZugang); die Automatik waehlt bei zwei
            // gleich langen Kanten beliebig.
            var oben=LayoutSettings.Fahrrad(layout);
            oben.AutomaticEntrances=false;
            oben.Entrances=new[]{new Entrance{Edge=2,Along=15}};
            var plan=FahrradGeometrie.Baue(ring,oben);
            var bau=new ParkingLotToolSystem();bau.Baue(plan);
            Pruefe(bau.Objekte.Count(o=>o.Art=="Fahrradobjekt")==plan.Objekte.Count,$"{layout}: alle physischen Objekte ausgegeben");
            // Aufkleber nur unter Unterstaenden; Restbausteine (Staender, Buegel) ohne.
            var daecher=plan.Objekte.Count(o=>o.Art==Fahrradobjektart.Unterstand);
            Pruefe(bau.Objekte.Count(o=>o.Art=="Fahrradaufkleber")==daecher && (layout==Fahrradlayout.C)==(daecher>0),$"{layout}: Dachaufkleber richtig");
            for(var i=0;i<plan.Objekte.Count;i++)
            {
                var o=plan.Objekte[i];var ausgegeben=bau.Objekte.Single(a=>a.Index==i && a.Art=="Fahrradobjekt");
                Pruefe(math.distance(ausgegeben.Center,(double2)o.Position)<.00001,$"{layout}: Lage {i}");
                Pruefe(math.distance(ausgegeben.Facing,new double2(-Math.Sin(o.Drehung),Math.Cos(o.Drehung)))<.00001,$"{layout}: Assetrichtung {i}");
            }
            Pruefe(bau.Kurse.Count==FahrradBaukurse.Plan(plan).Length,$"{layout}: alle echten Netze ausgegeben");
            Pruefe(bau.Kurse.All(k=>k.Prefab.Index>=901 && k.Prefab.Index<=903),$"{layout}: ausschliesslich Fahrradklone");
            // Nutzer 02.11.2026: alle Wege innen sind EIN Wegtyp, auch die D-Laufachse.
            Pruefe(bau.Kurse.Where(k=>k.Art=="fahrrad-weg").All(k=>k.Prefab.Index==902),$"{layout}: innen ein Wegtyp (3-m-Fahrradweg)");
            var zufahrt=bau.Kurse.Single(k=>k.Art=="fahrrad-zugang");
            Pruefe(zufahrt.Prefab.Index==901,$"{layout}: Zugang verwendet LocalConnect-Klon");
            Pruefe(zufahrt.Starthoehe==.2f,$"{layout}: Hoehe kommt von der Stadtstrasse");
            Pruefe(zufahrt.A.y>20 && zufahrt.A.y<28,$"{layout}: aeusserer Endpunkt am Stadtgehweg");
            Pruefe(bau.Vorflaechen.Length==1 && Cs2Triangulierung.Dreiecke(bau.Vorflaechen[0])==2,$"{layout}: 3-m-Vorflaeche wird von CS2 angenommen");
            Pruefe(bau.TraegerRichtig,$"{layout}: genau eine Fahrrad-Zaehlwurzel, kein Auto");
            Pruefe(bau.NavigationRichtig,$"{layout}: Traeger findet die eigene Geh-Flaeche");
            var l=FahrradDekoration.Laternen(new ParkingLayout{Fahrrad=plan,Grundstueck=ring},30);
            Pruefe(l.Laternen.Count>0,$"{layout}: Laternenplan fuer vorhandenen Bauweg");
            var pflanzen=FahrradDekoration.Vegetation(plan,new VegetationOptions{Enabled=true,Density=100},
                new[]{new VegetationSpecies{Tree=true,Hoehe=10,Spacing=5}});
            Pruefe(layout!=Fahrradlayout.D || pflanzen.Plants.Count==plan.Baeume.Count,"D: Baumvorschlaege erreichen Bauweg");
            var schraeg=LayoutSettings.Fahrrad(layout);
            schraeg.AutomaticEntrances=false;
            schraeg.Entrances=new[]{new Entrance{Edge=2,Along=15,AxisDirection=math.normalize(new float2(1,1))}};
            var entwurf=FahrradGeometrie.Baue(ring,schraeg);
            var winkelbau=new ParkingLotToolSystem();winkelbau.Baue(entwurf,schraeg,ring);
            Pruefe(winkelbau.Kurse.Count==FahrradBaukurse.Plan(entwurf).Length+1,$"{layout}: schraeger Fang wird am Polygonrand geteilt");
            Pruefe(winkelbau.Kurse.Select(k=>(k.Art,k.Index)).Distinct().Count()==winkelbau.Kurse.Count,$"{layout}: eindeutige Bau-/Rueckwegkennung");
            Pruefe(winkelbau.Kurse.Any(k=>k.A.x>16 && k.A.y>21 && math.distance(k.B,new float2(15,20))<.005),$"{layout}: gefangene Richtung bleibt erhalten");
        }
    }
}
