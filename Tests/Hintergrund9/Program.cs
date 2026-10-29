using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;
using Unity.Mathematics;

int pruefungen=0,fehler=0;
void Pruefe(bool gut,string name) { pruefungen++; if(!gut) { fehler++; Console.WriteLine("FEHLER: "+name); } }
Entity E(int i) => new(i);
foreach(float ort in new[]{0f,-1200f,8000f,-8000f})
foreach(float hoehe in new[]{0f,.00005f,.3975f,183f})
{
    var system=new ParkingLotDauerkursSystem(); system.Init(); var em=system.EntityManager;
    em.Neu(E(99)); em.AddComponentData(E(99),new PathwayData());
    em.AddComponentData(E(99),new NetData{m_RequiredLayers=Layer.MarkerPathway,m_ConnectLayers=Layer.MarkerPathway});
    em.Neu(E(98));em.AddComponentData(E(98),new NetData{m_RequiredLayers=Layer.Road,m_ConnectLayers=Layer.Road});
    var node=E(461252); em.Neu(node); var lage=new float3(ort,123.0815f,ort);
    em.AddComponentData(node,new Node {m_Position=lage}); em.AddBuffer<ConnectedEdge>(node);
    em.AddComponentData(node,new Owner {m_Owner=E(447078)}); em.AddComponentData(node,new PrefabRef{m_Prefab=E(98)});
    var definition=E(20); em.Neu(definition);
    em.AddComponentData(definition,new ParkingLotAuftragsdefinition());
    em.AddComponentData(definition,new CreationDefinition {m_Flags=CreationFlags.Permanent,m_Owner=E(381088),m_Prefab=E(99)});
    em.AddComponentData(definition,new NetCourse {m_StartPosition=new(){m_Entity=node,m_Position=lage},m_EndPosition=new(){m_Position=lage+new float3(30,hoehe,0)}});
    ParkingLotDauerkursSystem.Plane(em,definition);
    Pruefe(!em.HasComponent<NetCourse>(definition)&&em.HasComponent<ParkingLotGeplanterDauerkurs>(definition),"CourseSplit sieht 0 Kurse dieser eigenen Portion");
    system.Tick(); var ist=em.GetComponentData<NetCourse>(definition);
    Pruefe(ist.m_StartPosition.m_Entity==node&&math.all(ist.m_StartPosition.m_Position==lage),"GenerateNodes/Edges bekommt genau 461252 mit voller Lage, trotz anderem Owner");
    Pruefe(em.GetComponentData<Owner>(node).m_Owner==E(447078),"Erhaltener Knoten wird nicht umgehaengt");
    var c=(lage-new float3(0,hoehe,0),lage+new float3(10,2,0),lage+new float3(20,3,0),lage+new float3(30,4,0));
    var korrigiert=HintergrundDauerkurs.BindeEnden(c,lage,c.Item4);
    Pruefe(math.all(korrigiert.A==lage)&&math.all(korrigiert.D==c.Item4),"Rohabweichungen werden vor Ausgabe an echte Endknoten gebunden");
    Pruefe(math.distance(korrigiert.B,c.Item2+new float3(0,hoehe*2f/3f,0))<.00003f,"Kruemmung gegenueber Sehne erhalten");
}
foreach(int art in new[]{0,1,2,3,4,5})
{
    var s=new ParkingLotDauerkursSystem(); s.Init(); var em=s.EntityManager; var n=E(10); em.Neu(n);
    em.AddComponentData(n,new Node()); em.AddBuffer<ConnectedEdge>(n); em.AddComponentData(n,new PrefabRef());
    if(art==0) em.AddComponent<Deleted>(n);
    if(art==1) em.AddComponent<Temp>(n);
    if(art==2) em.RemoveComponent<Node>(n);
    if(art==3) em.RemoveComponent<PrefabRef>(n);
    if(art==5) em.RemoveComponent<DynamicBuffer<ConnectedEdge>>(n);
    var d=E(20); em.Neu(d); em.AddComponentData(d,new ParkingLotAuftragsdefinition());
    em.AddComponentData(d,new CreationDefinition{m_Flags=CreationFlags.Permanent});
    em.AddComponentData(d,new NetCourse{m_StartPosition=new(){m_Entity=n,m_Position=art==4?new float3(0,.002f,0):float3.zero}});
    bool abgelehnt=false; try {ParkingLotDauerkursSystem.Plane(em,d);} catch(InvalidOperationException){abgelehnt=true;}
    Pruefe(abgelehnt,"Toter/Temp/falscher/verschobener Anschluss wird vor Ausgabe abgelehnt");
    Pruefe(!em.HasComponent<NetCourse>(d),"Abgelehnte Definition hat 0 baubare Kurse");
}
foreach(bool hilfsnetz in new[]{false,true})
{
    var s=new ParkingLotDauerkursSystem();s.Init();var em=s.EntityManager;var prefab=E(99);em.Neu(prefab);
    if(hilfsnetz){em.AddComponentData(prefab,new PathwayData());em.AddBuffer<AuxiliaryNet>(prefab).Add(new());}
    var d=E(20);em.Neu(d);em.AddComponentData(d,new ParkingLotAuftragsdefinition());
    em.AddComponentData(d,new CreationDefinition{m_Flags=CreationFlags.Permanent,m_Prefab=prefab});
    em.AddComponentData(d,new NetCourse{m_EndPosition=new(){m_Position=new float3(30,0,0)}});
    ParkingLotDauerkursSystem.Plane(em,d);
    Pruefe(em.HasComponent<NetCourse>(d)&&!em.HasComponent<ParkingLotGeplanterDauerkurs>(d),"Road und Pathway mit AuxiliaryNet bleiben im regulaeren CourseSplit-Pfad");
}
{
    var s=new ParkingLotDauerkursSystem();s.Init();var em=s.EntityManager;var prefab=E(99);em.Neu(prefab);em.AddComponentData(prefab,new PathwayData());
    em.AddComponentData(prefab,new NetData{m_RequiredLayers=Layer.MarkerPathway,m_ConnectLayers=Layer.MarkerPathway});em.Neu(E(98));em.AddComponentData(E(98),new NetData{m_RequiredLayers=Layer.Road,m_ConnectLayers=Layer.Road});
    var n=E(10);em.Neu(n);em.AddComponentData(n,new Node());em.AddBuffer<ConnectedEdge>(n);em.AddComponentData(n,new PrefabRef{m_Prefab=E(98)});
    var d=E(20);em.Neu(d);em.AddComponentData(d,new ParkingLotAuftragsdefinition());em.AddComponentData(d,new CreationDefinition{m_Flags=CreationFlags.Permanent,m_Prefab=prefab});
    em.AddComponentData(d,new NetCourse{m_StartPosition=new(){m_Entity=n},m_EndPosition=new(){m_Position=new float3(30,0,0)}});
    ParkingLotDauerkursSystem.Plane(em,d);em.AddComponent<Deleted>(n);s.Tick();
    Pruefe(!em.HasComponent<NetCourse>(d),"Zwischen Planung und Generator geloeschtes Original verwirft ganze Portion ohne Doppelknoten");
}
{
    var s=new ParkingLotDauerkursSystem();s.Init();var em=s.EntityManager;var prefab=E(99);em.Neu(prefab);em.AddComponentData(prefab,new PathwayData());
    em.AddComponentData(prefab,new NetData{m_RequiredLayers=Layer.MarkerPathway,m_ConnectLayers=Layer.MarkerPathway});
    var n=E(10);em.Neu(n);em.AddComponentData(n,new Node());em.AddBuffer<ConnectedEdge>(n);em.AddComponentData(n,new PrefabRef{m_Prefab=prefab});
    var d=E(20);em.Neu(d);em.AddComponentData(d,new ParkingLotAuftragsdefinition());em.AddComponentData(d,new CreationDefinition{m_Flags=CreationFlags.Permanent,m_Prefab=prefab});
    em.AddComponentData(d,new NetCourse{m_StartPosition=new(){m_Entity=n},m_EndPosition=new(){m_Position=new float3(30,0,0)}});
    ParkingLotDauerkursSystem.Plane(em,d);
    Pruefe(em.HasComponent<NetCourse>(d)&&!em.HasComponent<ParkingLotGeplanterDauerkurs>(d),"Kompatible Wegknoten behalten CourseSplit samt Terrain-Innenhoehe");
}
foreach(int anzahl in new[]{1,9,218})
{
    var em=new EntityManager(); var alt=E(1); var neu=E(2); var carrier=E(3);
    foreach(var e in new[]{alt,neu,carrier})em.Neu(e);
    em.AddComponentData(carrier,new Owner{m_Owner=neu});
    var bleibt=E(4);em.Neu(bleibt);em.AddComponentData(bleibt,new Node());em.AddComponentData(bleibt,new Owner{m_Owner=alt});
    em.AddBuffer<ParkingLotErhaltenerKurs>(alt).Add(new(){Bestand=new(){Start=bleibt}});
    var nodes=new List<Entity>();var edges=new List<Entity>();
    for(int i=0;i<=anzahl;i++){var n=E(1000+i);nodes.Add(n);em.Neu(n);em.AddComponentData(n,new Node());em.AddComponentData(n,new Owner{m_Owner=neu});em.AddBuffer<ConnectedEdge>(n);}
    for(int i=0;i<anzahl;i++){var k=E(10000+i);edges.Add(k);em.Neu(k);em.AddComponentData(k,new Owner{m_Owner=neu});em.AddComponentData(k,new Edge{m_Start=nodes[i],m_End=nodes[i+1]});foreach(var n in new[]{nodes[i],nodes[i+1]})em.GetBuffer<ConnectedEdge>(n).Add(new(){m_Edge=k});}
    // Kein SubNet-Puffer und keine _eigeneDauerteile: gerade diese Knoten
    // fehlten in der bisher alleinigen Puffer-Auswahl.
    using var p=new HintergrundPortion(ParkingLotErsatzabriss.Schritte(em,alt,neu,carrier));
    int bilder=0,loeschbild=-1;
    while(!p.Fertig&&bilder++<1000)
    {
        double zeit=0;p.Weiter(()=>zeit);
        int geloescht=nodes.Count(n=>!ParkingLotNetzRueckweg.Lebt(em,n));
        Pruefe(geloescht==0||geloescht==nodes.Count,"Knoten fallen vollstaendig in EINEM Bild");
        if(geloescht>0){Pruefe(loeschbild==-1,"Kein weiterer Knotenabriss in spaeterem Bild");loeschbild=bilder;}
        foreach(var k in edges.Where(k=>ParkingLotNetzRueckweg.Lebt(em,k))){var edge=em.GetComponentData<Edge>(k);Pruefe(ParkingLotNetzRueckweg.Lebt(em,edge.m_Start)&&ParkingLotNetzRueckweg.Lebt(em,edge.m_End),"Jede lebende Kante hat zwei lebende Knoten am Bildende");}
        if(!ParkingLotNetzRueckweg.Lebt(em,neu))Pruefe(edges.Concat(nodes).All(e=>!ParkingLotNetzRueckweg.Lebt(em,e)),"Besitzer faellt erst nach allen Kindern");
        em.Bildende();
    }
    Pruefe(p.Fertig&&edges.Concat(nodes).All(e=>!em.Exists(e)),"ALLE Stage-B-Kanten und -Knoten entfernt, auch ohne Besitzerpuffer");
    Pruefe(em.Exists(alt)&&em.Exists(bleibt),"Altes Lot und erhaltener Originalknoten bleiben");
    Pruefe(ParkingLotErsatzabriss.Eigene(em,alt,neu,carrier).Count==0,"Vollstaendigkeitsabfrage vor Rueckweg ist wirklich leer");
}
foreach(double kosten in new[]{0d,.01d,.1d,1d,26d})
{
    double zeit=0;int bilder=0,einheiten=0;var tempo=new HintergrundTempo();
    IEnumerable<int> Arbeit(){for(int i=0;i<10000;i++){zeit+=kosten;einheiten++;yield return 1;}}
    using var p=new HintergrundPortion(Arbeit(),tempo);
    while(!p.Fertig&&bilder++<20000){var vorher=zeit;var budget=tempo.BudgetMs;var limit=tempo.Einheiten;p.Weiter(()=>zeit);Pruefe(p.Einheiten<=limit&&zeit-vorher<=budget+kosten+.00001,"Adaptiv: feste Obergrenze und Budget plus eine unteilbare Einheit");tempo.MeldeBild(zeit-vorher);}
    Pruefe(p.Fertig&&einheiten==10000&&p.Gesamt==10000,"Alle 10000 Einheiten trotz Adaptivregel ausgegeben");
    if(kosten<=.1)Pruefe(bilder<100,"10000 billige Einheiten in unter 100 Bildern statt 1251");
    Console.WriteLine($"Tempo Modell: 10000 Einheiten, {kosten} ms/Einheit, {bilder} Bilder; keine Ingame-Zeitbehauptung.");
}
var z=new HintergrundZeitmessung();z.Fuege(1,4);z.Fuege(1,6);z.Fuege(2,7);
for(int mask=0;mask<16;mask++)Pruefe(HintergrundKurspruefung.InnenhoeheGeneratoren((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0)==((mask&1)!=0&&(mask&2)==0&&((mask&4)==0||(mask&8)!=0)),"Direkte Ausgabe erlaubt Innen-Y nur bei wirklicher GenerateEdges-Bodenanpassung");
Pruefe(z.Bilder==2&&z.Summe==17&&z.Max==10,"Phasenzeit: Bilder eindeutig, Max ist ganze Bildsumme");
VanillaBefund.Messen(Pruefe);
var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
string Lies(string p)=>File.ReadAllText(Path.Combine(root!.FullName,p));
Pruefe(Lies("Mod.cs").Contains("UpdateBefore<ParkingLotDauerkursSystem, Game.Tools.GenerateNodesSystem>(SystemUpdatePhase.Modification1)"),"Ausgabe nach PostTool/CourseSplit, unmittelbar vor GenerateNodes");
Pruefe(Lies("Tools/ParkingLotLotOwner.cs").Contains("if (_bauarbeiter && _definitionsmodus == ParkingLotDefinitionsmodus.Permanent)")&&Lies("Tools/ParkingLotNetzRueckweg.cs").Contains("ParkingLotDauerkursSystem.Plane(em,d)"),"Bauer und Rueckweg geben explizit geplant aus; Temp-Normalbau bleibt bisheriger Pfad");
Pruefe(Lies("Tools/ParkingLotHintergrundSystem.cs").Contains("a.Ersatzabriss ??= new HintergrundPortion(ParkingLotErsatzabriss.Schritte(")&&Lies("Tools/ParkingLotBauarbeiter.cs").Contains("ParkingLotErsatzabriss.Schritte("),"Dieselbe vollstaendige Ruecknahme auch nach Speichern/Laden");
// Die Fehlerbehandlung zog am 16.10. (04c4fa4) nach ParkingLotHintergrund.Abschluss.cs: dieselbe Eigenschaft, neuer Ort.
Pruefe((Lies("Tools/ParkingLotHintergrundSystem.cs")+Lies("Tools/ParkingLotHintergrund.Abschluss.cs")).Contains("if (a.Phase == Phase.Ruecknahme || a.Phase == Phase.Ruhe"),"Ein unvollstaendiger Ersatzabriss wird angehalten statt endlos wiederholt");
Console.WriteLine($"HINTERGRUND9: {pruefungen} Pruefungen, {fehler} Fehler (Produktionsmethoden mit Test-API, keine nativen ECS-Jobs).");
return fehler==0?0:1;
