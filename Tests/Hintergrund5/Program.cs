using System;
using System.Collections.Generic;
using System.IO;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

int n = 0, fehler = 0;
void Pruefe(bool gut, string text)
{ n++; if (!gut) { fehler++; Console.WriteLine("FEHLER: " + text); } }

var geplant = new float3(-1000,123,-300);
var knoten = geplant + new float3(0,.26f,0);
Pruefe(HintergrundAbgleich.Anschlusslage(geplant,knoten,true).Equals(knoten),"Knotenfang: 0 m XZ, 26 cm Y muessen uebernommen werden");
Pruefe(HintergrundAbgleich.Anschlusslage(geplant,knoten,false).Equals(geplant),"Ohne echten Knoten bleibt der berechnete Strassenpunkt");
Pruefe(HintergrundAbgleich.ErhaltenerKnoten(geplant.xz,new[] {(7,knoten)}) == 7,"Erhaltener Zoningknoten muss trotz anderer Hoehe wiederverwendet werden");
Pruefe(HintergrundAbgleich.ErhaltenerKnoten(geplant.xz,new[] {(7,knoten+new float3(.049f,0,0))}) == 7,"Erhaltener Knoten innerhalb 4,9 cm wird gefunden");
Pruefe(HintergrundAbgleich.ErhaltenerKnoten(geplant.xz,new[] {(7,knoten+new float3(.051f,0,0))}) == -1,"Kein Knotenfang bei 5,1 cm");
Pruefe(HintergrundAbgleich.ErhaltenerKnoten(geplant.xz,new[] {(7,knoten),(8,knoten)}) == -2,"Zwei erhaltene IDs am selben Ort sind mehrdeutig");
Pruefe(HintergrundAbgleich.ErhaltenerKnoten(geplant.xz,Array.Empty<(int,float3)>()) == -1,"Kein fremder ungesicherter Knoten wird erfunden");
foreach (float y in new[] { .14f, -.23f, .26f })
{
    var boden = geplant+new float3(0,y,0);
    Pruefe(HintergrundAbgleich.Objektlage(boden,geplant,boden,true),"Vanilla darf den Boden nach Netzbau nachfuehren");
    Pruefe(!HintergrundAbgleich.Objektlage(boden,geplant,boden,false),"Feste Hoehe darf keine Bodenalternative nutzen");
    Pruefe(!HintergrundAbgleich.Objektlage(boden+new float3(.051f,0,0),geplant,boden,true),"5,1 cm XZ-Versatz bleibt Fehler");
    Pruefe(!HintergrundAbgleich.Objektlage(boden+new float3(0,.051f,0),geplant,boden,true),"5,1 cm gegen Vanilla-Bodensoll bleibt Fehler");
    Pruefe(!HintergrundAbgleich.Objektlage(boden-new float3(0,183,0),geplant,boden,true),"183 m Absacken bleibt Fehler");
}
Pruefe(HintergrundAbgleich.Objektlage(geplant,geplant,knoten,true),"Vor Vanilla-Nachfuehrung muss die Definition gefunden werden");
Pruefe(!HintergrundAbgleich.Objektlage(new float3(float.NaN),geplant,knoten,true),"NaN ist kein Treffer");

// Analytisch vorgegebene Kontrollpunkte des Halbschnitts, nicht aus dem
// Produktions-Schnitt gewonnen. De Casteljau bei t=1/2 ist nachrechenbar.
var c = (new float3(0,0,0),new float3(30,12,0),new float3(60,-6,30),new float3(90,3,30));
var links = (c.Item1,new float3(15,6,0),new float3(30,4.5f,7.5f),new float3(45,2.625f,15));
var rechts = (links.Item4,new float3(60,.75f,22.5f),new float3(75,-1.5f,30),c.Item4);
Pruefe(HintergrundAbgleich.Kurvenabstand(HintergrundAbgleich.Schnitt(c,0,.5f),links) < 1e-5f,"Gekruemmter linker Abschnitt MUSS gefunden werden");
Pruefe(HintergrundAbgleich.Kurvenabstand(HintergrundAbgleich.Schnitt(c,.5f,1),rechts) < 1e-5f,"Gekruemmter rechter Abschnitt MUSS gefunden werden");
var rueck = (rechts.Item4,rechts.Item3,rechts.Item2,rechts.Item1);
Pruefe(HintergrundAbgleich.Kurvenabstand(HintergrundAbgleich.Schnitt(c,1,.5f),rueck) < 1e-5f,"Umgekehrter Abschnitt MUSS gefunden werden");
Pruefe(HintergrundAbgleich.Kurvenabstand(HintergrundAbgleich.Schnitt(c,0,1),c) < 1e-5f,"Ungeteilter Kurs MUSS gefunden werden");
Pruefe(HintergrundAbgleich.Kurvenabstand((c.Item1,c.Item2+new float3(0,.051f,0),c.Item3,c.Item4),c) > .05f,"Mittlere Hoehenabweichung bleibt messbar");
Pruefe(float.IsPositiveInfinity(HintergrundAbgleich.Kurvenabstand((c.Item1,new float3(float.NaN),c.Item3,c.Item4),c)),"NaN-Kontrollpunkt darf nicht in math.max verschwinden");

List<(float2 Bereich,int Start,int Ende)> Plan(params (float2,int,int)[] p) => new(p);
bool Kette(List<(float2 Bereich,int Start,int Ende)> p) => HintergrundAbgleich.Kette(p,out _,out _);
Pruefe(!Kette(Plan()),"Nichts-Tun ist kein Rueckweg");
Pruefe(Kette(Plan((new float2(0,1),10,20))),"Ein gueltiger Kurs MUSS gefunden werden");
Pruefe(Kette(Plan((new float2(.5f,1),15,20),(new float2(.5f,0),15,10))),"Zwei Abschnitte mit Umkehr und beliebiger Reihenfolge");
Pruefe(!Kette(Plan((new float2(0,.5f),10,15))),"Halber Kurs gibt nicht frei");
Pruefe(!Kette(Plan((new float2(0,.5f),10,15),(new float2(.5f,1),16,20))),"Geometrische Deckung ohne gemeinsamen Zwischenknoten verworfen");
Pruefe(!Kette(Plan((new float2(0,.5f),10,15),(new float2(.51f,1),15,20))),"Parameterluecke verworfen");
Pruefe(!Kette(Plan((new float2(0,.6f),10,15),(new float2(.5f,1),15,20))),"Ueberlappende Kanten sind kein Erfolg");
Pruefe(!Kette(Plan((new float2(float.NaN,1),10,20))),"NaN-Parameter verworfen");
Pruefe(!Kette(Plan((new float2(0,0),10,20))),"Nullkurs verworfen");
Pruefe(HintergrundAbgleich.Teilparameter(new float2(.25f,.75f),.5f,out float lokal) && lokal == .5f,"Seitenanschluss auf geteiltem Kurs auf lokalen Parameter umrechnen");
Pruefe(HintergrundAbgleich.Teilparameter(new float2(1,.5f),.75f,out lokal) && lokal == .5f,"Seitenanschluss auf umgekehrtem Abschnitt");
Pruefe(HintergrundAbgleich.Teilparameter(new float2(1,.5f),.5f,out lokal) && lokal == 1,"Umgekehrtes Teilende behaelt Lage 1");
Pruefe(!HintergrundAbgleich.Teilparameter(new float2(.5f,1),.49f,out _),"Kein Anschluss auf fremdem Abschnitt");
Pruefe(!HintergrundAbgleich.Teilparameter(new float2(.5f,.5f),.5f,out _),"Keine Division auf Nullabschnitt");
for (int teile = 1; teile <= 22; teile++)
{
    var p = Plan();
    for (int i = 0; i < teile; i++) p.Add((new float2((float)i/teile,(float)(i+1)/teile),i,i+1));
    Pruefe(HintergrundAbgleich.Kette(p,out int start,out int ende) && start == 0 && ende == teile,"Alle gueltigen Teilungen 1..22 werden gefunden");
    p.RemoveAt(teile/2);
    Pruefe(!Kette(p),"Jede entfernte Teilkante 1..22 wird erkannt");
}
for (int i = 0; i < 3; i++) Pruefe(HintergrundRueckwegfrist.Weiter(i),"Drei feste Messfenster sind zulaessig");
for (int i = 3; i < 12; i++) Pruefe(!HintergrundRueckwegfrist.Weiter(i),"Kein viertes/endloses Messfenster");
foreach (bool eigen in new[] { false,true })
foreach (bool permanent in new[] { false,true })
    Pruefe(HintergrundDefinitionsleben.Abschliessen(eigen,permanent) == (eigen && permanent),"Lebensdauerabschluss nur fuer angemeldete eigene Permanent-Kurse");
Pruefe(HintergrundDefinitionsleben.Abschliessen(false,true,true),"Hilfskurs mit belegter eigener Herkunft wird trotz fehlendem Owner abgeschlossen");
Pruefe(!HintergrundDefinitionsleben.Abschliessen(false,false,true),"Temp-Hilfskurs wird nicht geloescht");

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root = root.Parent;
if (root == null) throw new InvalidOperationException("Repo fehlt");
string Lies(string p) => File.ReadAllText(Path.Combine(root.FullName,p));
var bauer = Lies("Tools/ParkingLotBauarbeiter.cs");
var rueckweg = Lies("Tools/ParkingLotNetzRueckweg.cs");
var zustand = Lies("Tools/ParkingLotHintergrundSystem.cs");
var abschluss = Lies("Tools/ParkingLotDefinitionsausgabe.cs");
Pruefe(bauer.Contains("HintergrundAbgleich.Objektlage(") && bauer.Contains("ParkingLotKursabgleich.Kette("),"Produktionsabnahme ruft beide geprueften Regeln auf");
Pruefe(bauer.Contains("_netSearchSystem = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>()"),"Bauarbeiter initialisiert Netzsuche fuer Vorflaechen/Versorgung");
Pruefe(rueckweg.Contains("ParkingLotToolSystem.NurDiesesBild(em, d)"),"Rueckwegdefinition wird im Erzeugungsbild geloescht");
Pruefe(abschluss.Contains("NurDiesesBild(EntityManager, definition)") && abschluss.Contains("CreationFlags.Permanent) != 0"),"Gemeinsamer Abschluss deckt Permanent auch im Werkzeugpfad");
Pruefe(Lies("Tools/ParkingLotBusStops.cs").Contains("SchliesseDefinition(definition, Entity.Null)"),"Permanent-Bushalt benutzt Abschluss");
Pruefe(Lies("Tools/ParkingLotAutoVersorgungBau.cs").Contains("SchliesseDefinition(d,"),"Permanent-Leitung benutzt Abschluss");
Pruefe(Lies("Mod.cs").Contains("UpdateAfter<ParkingLotDefinitionsendeSystem, Game.Tools.ToolReadyBarrier>(SystemUpdatePhase.PostTool)"),"Abschluss abgeleiteter Definitionen nach CourseSplit-Wiedergabe und vor Generatoren");
Pruefe(abschluss.Contains("ParkingLotDefinitionsendeSystem>().Merke(owner,") && rueckweg.Contains("ParkingLotDefinitionsendeSystem>().Merke(alt.Besitzer,"),"Beide Permanent-Netzerzeuger melden den berechtigten Besitzer");
Pruefe(zustand.Contains("HintergrundRueckwegfrist.Weiter(") && zustand.Contains("_angehalten.Contains(lot)"),"Feste Frist und Lotsperre im Produktionszustand");
Pruefe(zustand.Contains("a.Bauer.HintergrundFehlstellen()") && zustand.Contains("diagnose:true"),"Beide Fehlstellendiagnosen vor endgueltiger Meldung");
// Seit ddb6168 (06.10.) stehen Spielertexte in Lang/*.json: dieselbe Eigenschaft, neuer Ort.
Pruefe(Lies("Tools/ParkingLotExklusivesBildSystem.cs").Contains("pltZeichnet ? ParkingLotTexte.T(\"exklusivesBildSystem.synchronizationIsWaitingFinishTheDraft\")")
    && Lies("Lang/en-US.json").Contains("close the Parking Lot panel/tool"),"Gate nennt das zeichnende PLT-Werkzeug");
Pruefe(Lies("UI/src/mods/sync-fortschritt.tsx").Contains("{felder[2]}"),"Fortschrittsmeldung zeigt den Warte-/Abbruchgrund");
Console.WriteLine($"HINTERGRUND5: {n} Pruefungen, {fehler} Fehler (Rechenregeln und Quellenvertrag; kein Spieltest).");
return fehler == 0 ? 0 : 1;
