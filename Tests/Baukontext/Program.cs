using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;
using Unity.Mathematics;

var em = new EntityManager();
var lot = new Entity { Index = 10, Version = 1 };
var anderesLot = new Entity { Index = 20, Version = 1 };
var zettel = new ParkingLotBuildReceipt
{
    Version = ParkingLotBuildReceipt.CurrentVersion,
    Es = 8, Ai = 6, Cw = 6, Sl = 5, Sw = 3, Md = 2, Cr = 18,
    Gassenbreite = 6, Angle = 0.7, AngleMode = Winkelmodus.Kodiere("quer"),
    ZoningAussentiefeVorwahl = 2, Ausrichtwinkel = double.NaN,
    ZoningAusrichtwinkel = double.NaN, BusStopCount = 1,
};
em.Set(lot, zettel);
em.Set(lot, new ParkingLotCarrierReference());
var punkte = new List<ParkingLotBuildPoint>
{
    new() { Version = 1, Position = new float3(0, 25, 0) },
    new() { Version = 1, Position = new float3(100, 26, 0) },
    new() { Version = 1, Position = new float3(100, 27, 100) },
};
em.SetBuffer(lot, punkte);
em.SetBuffer(lot, new List<ParkingLotBuildEntrance> { new()
{
    Version = 2, Edge = 1, Along = 0.4, Corner = 2,
    Art = Zufahrtsart.Zufahrt, HasAxis = true,
    AxisDirection = new float2(1, 0), AxisLength = 12,
} });
var texte = new List<ParkingLotBuildText>();
void Text(int kind, string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    for (var i = 0; i < bytes.Length; i++) texte.Add(new()
        { Version = 1, Kind = kind, Index = i, Value = bytes[i] });
}
Text(1, "Pavement Surface 01");
Text(2, "Gras mit Umlaut ä");
Text(3, "Sand Surface 01");
Text(4, "{\"Options\":\"vegetation\",\"Signature\":\"alt\"}");
Text(5, "Zoning Road");
em.SetBuffer(lot, texte);
em.SetBuffer(lot, new List<ParkingLotBuildAlignment> { new()
    { Version = 1, Anchor = new float2(10, 20), LineA = new float2(0, 0),
        LineB = new float2(100, 0), Angle = 0.5 } });
em.SetBuffer(lot, new List<ParkingLotBuildCut> { new()
    { Version = 1, A = new float2(40, 0), B = new float2(40, 100) } });
em.SetBuffer(lot, new List<ParkingLotBuildZoning> { new()
    { Version = 4, Ecke = new float2(60, 20), Spalten = 3, Reihen = 4,
        Winkel = 0.4, Rand = 8, Aussen0 = 8, Aussen1 = 16, Aussen2 = 24, Aussen3 = 32 } });
em.SetBuffer(lot, new List<ParkingLotBuildZoningSeite> { new()
    { Version = 1, A = new float2(1, 2), B = new float2(3, 4), Links = true, Aus = true } });
em.SetBuffer(lot, new List<ParkingLotBuildRandzoning> { new()
    { Version = 1, A = new float2(0, 0), B = new float2(100, 0) } });
em.SetBuffer(lot, new List<ParkingLotBuildBusStop> { new()
    { Version = 1, A = new float2(5, 6), B = new float2(20, 6), Along = 0.25f, Left = true } });
// Fremder unbrauchbarer Zettel darf die gezielte Lesung nicht beeinflussen.
em.Set(anderesLot, new ParkingLotBuildReceipt { Version = -1 });

var fehler = 0;
var pruefungen = 0;
void Pruefe(bool stimmt, string text)
{
    pruefungen++;
    if (!stimmt) { Console.WriteLine("FEHLER: " + text); fehler++; }
}
var schreibstand = em.Schreibzugriffe;
var live = new List<string>();
ParkingGeometry.LiveSchreiber = live.Add;
var gelesen = ParkingLotBaukontextLeser.TryRead(em, lot, out var k, out var grund);
Pruefe(gelesen && k != null, "Gueltiger Zettel muss einen Kontext ergeben: " + grund);
if (k == null) return 1;
Pruefe(k.AltesLot == lot && k.Zettel.AngleMode == zettel.AngleMode, "Lot und Reihenwinkel");
Pruefe(k.Punkte.Length == 3 && k.Punkte[2].y == 27, "Punkte samt Hoehen");
Pruefe(k.Zugaenge.Length == 1 && k.Zugaenge[0].AxisLength == 12
    && k.Zugaenge[0].Corner == "end", "Zugang samt Achse und Eckfang");
Pruefe(k.Ausrichtungen.Length == 1 && k.Ausrichtungen[0].Winkel == 0.5, "Teilflaechenausrichtung");
Pruefe(k.Schnitte.Length == 1 && k.Schnitte[0].B.y == 100, "Trennschnitt");
Pruefe(k.Zonen.Length == 1 && k.Zonen[0].Aussentiefen.SequenceEqual(new double[] { 8, 16, 24, 32 }), "Alle 4 Zoningseiten");
Pruefe(k.Seitenplan.Count == 1 && k.Seitenplan[0].Links && k.Seitenplan[0].Aus, "Handschaltung");
Pruefe(k.Randzoning.Count == 1 && k.Randzoning[0].B.x == 100, "Randzoning");
Pruefe(k.Bushalte.Count == 1 && k.Bushalte[0].Along == 0.25f, "Bushalt");
Pruefe(k.FlaecheStrasse == "Pavement Surface 01" && k.FlaecheDekoration == "Gras mit Umlaut ä"
    && k.FlaecheZoning == "Sand Surface 01", "3 Flaechennamen mit UTF-8");
Pruefe(k.Vegetationszettel.Contains("Signature") && k.Zoningstrasse == "Zoning Road", "Vegetationszettel und Strassenwahl");
Pruefe(em.Schreibzugriffe == schreibstand, "Leser darf 0 Schreibzugriffe ausloesen");
Pruefe(live.Count == 1 && live[0].StartsWith("PLT-Hintergrund: Bauzettelkopie Lot 10;")
    && live[0].Contains("Punkte 3, Zugaenge 1, Zoning 1, Seiten 1, Bushalte 1"),
    "Live-Messung muss die wirklich kopierten Listen zaehlen");
punkte[0] = new() { Version = 1, Position = new float3(500, 500, 500) };
Pruefe(k.Punkte[0].x == 0 && k.Punkte[0].y == 25, "Kontext muss vom Quellpuffer getrennt sein");
var bad = zettel;
bad.Version = -1;
em.Set(lot, bad);
Pruefe(!ParkingLotBaukontextLeser.TryRead(em, lot, out var fehlt, out _)
    && fehlt == null, "Unbekannte Zettelversion ablehnen");
em.Set(lot, zettel);
punkte[0] = new() { Version = 1, Position = new float3(float.NaN, 25, 0) };
Pruefe(!ParkingLotBaukontextLeser.TryRead(em, lot, out fehlt, out _)
    && fehlt == null, "Nicht endliche Punkte ablehnen");
punkte[0] = new() { Version = 1, Position = new float3(0, 25, 0) };
var original = texte[0];
texte.Add(original);
Pruefe(!ParkingLotBaukontextLeser.TryRead(em, lot, out fehlt, out _)
    && fehlt == null, "Doppelte Textbytes ablehnen");
texte.RemoveAt(texte.Count - 1);
texte.RemoveAll(t => t.Kind >= 3);
Pruefe(ParkingLotBaukontextLeser.TryRead(em, lot, out k, out _)
    && k.FlaecheZoning == "" && k.Vegetationszettel == "" && k.Zoningstrasse == "",
    "Alte Zettel ohne freiwillige Texte behalten bisherigen Rueckfall");
Pruefe(!ParkingLotBaukontextLeser.TryRead(em, Entity.Null, out fehlt, out _) && fehlt == null,
    "Kein Kontext fuer fehlendes Lot");
foreach(var layout in new[]{Fahrradlayout.A,Fahrradlayout.C,Fahrradlayout.D})
{
    var fahrrad=zettel;fahrrad.Fahrzeugart=Fahrzeugart.Fahrrad;fahrrad.Fahrradlayout=layout;
    em.Set(lot,fahrrad);
    Pruefe(ParkingLotBaukontextLeser.TryRead(em,lot,out var bike,out _,melden:false)
        && bike.Zettel.ToLayoutSettings(bike.Zugaenge).Fahrzeugart==Fahrzeugart.Fahrrad
        && bike.Zettel.ToLayoutSettings(bike.Zugaenge).Fahrradlayout==layout,
        "echter Baukontextleser behaelt Fahrradart/Layout "+layout);
}
bad=zettel;bad.Fahrzeugart=(Fahrzeugart)99;em.Set(lot,bad);
Pruefe(!ParkingLotBaukontextLeser.TryRead(em,lot,out fehlt,out _,melden:false),"unbekannte Fahrzeugart wird vor Edit/Bau abgelehnt");
bad=zettel;bad.Fahrzeugart=Fahrzeugart.Fahrrad;bad.Fahrradlayout=(Fahrradlayout)1;em.Set(lot,bad);
Pruefe(!ParkingLotBaukontextLeser.TryRead(em,lot,out fehlt,out _,melden:false),"entfallenes B wird vor Edit/Bau abgelehnt");
Console.WriteLine($"Baukontext: {pruefungen} Pruefungen, {fehler} Fehler. "
    + "Getestet ist der echte Leser mit Datencontainern, keine ECS-Materialisierung.");
return fehler == 0 ? 0 : 1;
