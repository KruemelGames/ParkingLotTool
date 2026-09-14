using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;

var tests = 0;
void Check(bool ok, string name)
{
    tests++;
    if (!ok) throw new Exception("FEHLER: " + name);
}
var edges = new List<(int Kante, int Start, int Ende)> {
    (101, 1, 2), (102, 2, 3), (103, 3, 1), (104, 3, 4),
    (201, 7, 8), (202, 8, 9), (301, 12, 13)
};
Check(Versorgungsuebernahme.Finde(edges, new[] {1}).SetEquals(new[] {101,102,103,104}),
    "ganze Kette inklusive Ring und Ast, keine getrennten Leitungen");
Check(Versorgungsuebernahme.Finde(edges, new[] {1,7}).SetEquals(new[] {101,102,103,104,201,202}),
    "Strom und Wasser mit getrennten Startpunkten");
Check(Versorgungsuebernahme.Finde(edges, Array.Empty<int>()).Count == 0,
    "fehlgeschlagener Wiederanschluss uebernimmt nichts");
Check(Versorgungsuebernahme.Finde(edges, new[] {99}).Count == 0, "fremder Start");
Check(Versorgungsuebernahme.Finde(edges, new[] {1,1,1}).Count == 4, "keine Doppeluebernahme");
var unterbrochen = new List<(int Kante, int Start, int Ende)> {(10,1,2),(12,3,4)};
Check(Versorgungsuebernahme.Finde(unterbrochen,new[]{1}).SetEquals(new[]{10}),
    "ausgefilterte fremde/Temp/Deleted-Kante ist keine Bruecke");
// Entity-Versionen sind Teil der Identitaet: gleiche Indexnummer ist kein Treffer.
var versioniert = new List<(string Kante,string Start,string Ende)> {("K:1","N:1","B:1")};
Check(Versorgungsuebernahme.Finde(versioniert,new[]{"N:2"}).Count==0,"andere Entity-Version");
// Unabhaengiges Orakel: Zusammenhang per Vereinigungsmenge statt Suchlauf.
var random = new Random(213722);
for (var run=0;run<500;run++)
{
    var parent=Enumerable.Range(0,30).ToArray();
    int Root(int x) { while(parent[x]!=x) x=parent[x]; return x; }
    var graph=new List<(int Kante,int Start,int Ende)>();
    for(var i=0;i<25;i++) {
        var a=random.Next(30);var b=random.Next(30);
        graph.Add((100+i,a,b));parent[Root(a)]=Root(b);
    }
    var seeds=new[]{random.Next(30),random.Next(30)};
    var roots=seeds.Select(Root).ToHashSet();
    var expected=graph.Where(e=>roots.Contains(Root(e.Start))).Select(e=>e.Kante).ToHashSet();
    Check(Versorgungsuebernahme.Finde(graph,seeds).SetEquals(expected),"Zusammenhang "+run);
    graph.Reverse();
    Check(Versorgungsuebernahme.Finde(graph,seeds.Reverse()).SetEquals(expected),"Reihenfolge "+run);
}
Console.WriteLine($"Versorgungsuebernahme: {tests} Pruefungen, 0 Fehler.");
