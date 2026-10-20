using System;
using System.Collections.Generic;
using System.Text.Json;
using Colossal.Serialization.Entities;
using ParkingLotTool.Tools;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Strassenkurven im Bauzettel (Etappe 4): jedes Element beginnt mit
     * seiner Version, Schreiben und Lesen ergeben dieselben Felder, und
     * zwei Datensaetze hintereinander enden exakt an der Endmarke.
     */
    private static void Kurvenformate()
    {
        var kurve = new ParkingLotBuildCurve
        {
            Von = new float2(1, 2), Bis = new float2(3, 4), Zwischen = 7, ErsterAbschnitt = 0, Abschnitte = 2,
            ErsteStation = 0, Laenge = 42.5f, RadiusMin = 139.3f, Winkel = 0.42f, Pfeilhoehe = 4.07f,
            Wendepunkte = 1, Toleranz = 0.15f, MinSehne = 1f, MaxSehne = 8f,
        };
        var abschnitt = new ParkingLotBuildCurveSection
        {
            A = new float3(0, 1, 0), B = new float3(10, 1, 2), C = new float3(20, 1, 6), D = new float3(30, 1, 12),
        };
        var station = new ParkingLotBuildCurveStation { Bogen = 5.25f };

        Rundreise(kurve, 15, "Kurve");
        Rundreise(abschnitt, 5, "Kurvenabschnitt");
        Rundreise(station, 2, "Kurvenstation");

        var k = Lies<ParkingLotBuildCurve>(StromAus(kurve));
        Pruefe(k.Version == ParkingLotBuildCurve.CurrentVersion && math.all(k.Von == kurve.Von) && math.all(k.Bis == kurve.Bis)
            && k.Zwischen == 7 && k.Abschnitte == 2 && k.Wendepunkte == 1 && k.RadiusMin == kurve.RadiusMin
            && k.Toleranz == kurve.Toleranz && k.MaxSehne == kurve.MaxSehne, "Kurve: Felder unveraendert");
        var a = Lies<ParkingLotBuildCurveSection>(StromAus(abschnitt));
        Pruefe(math.all(a.A == abschnitt.A) && math.all(a.D == abschnitt.D), "Kurvenabschnitt: Kontrollpunkte unveraendert");

        Kurvenrandformate();
    }

    private static void Kurvenrandformate()
    {
        var lauf = new ParkingLotTool.Geometry.KurvenRandlauf
        {
            Von = 3, Kanten = 9,
            Kontrollpunkte = new[] { new float3(1300, 0, 4600), new float3(1305, 0, 4600),
                new float3(1310, 0, 4605), new float3(1310, 0, 4610) },
        };
        var w = new Schreiber();
        void Schreibe() => ParkingLotTool.Geometry.KurvenRandlaufFormat.Schreiben(new[] { lauf },
            n => w.Write(n), p => w.Write(p));
        Schreibe(); Schreibe(); w.Write("ENDE_KURVENRAND");
        var werte = w.Felder.ToArray();
        Pruefe((int)werte[0].Wert == 1 && (int)werte[1].Wert == 1
            && (int)werte[2].Wert == 3 && (int)werte[3].Wert == 9 && (int)werte[4].Wert == 4,
            "Kurvenrand v1: eingefrorene Feldfolge Version/Anzahl/Von/Kanten/Kontrollpunkte");
        var r = new Leser(Felder(w));
        int Zahl() { r.Read(out int n); return n; }
        float3 Punkt() { r.Read(out float3 p); return p; }
        for (int i = 0; i < 2; i++)
        {
            var gelesen = ParkingLotTool.Geometry.KurvenRandlaufFormat.Lesen(Zahl, Punkt);
            Pruefe(gelesen.Length == 1 && gelesen[0].InhaltGleich(lauf), "Kurvenrandformel: Rundreise");
            Pruefe(r.Position == (i + 1) * 9, "Kurvenrand: exakte Datensatzgrenze");
        }
        r.Read(out string ende);
        Pruefe(ende == "ENDE_KURVENRAND", "Kurvenrand: Endmarke");
        // Historische Formate haben keinen optionalen Kurvenrand-Puffer.
        Pruefe(new ParkingLotTool.Geometry.LayoutSettings().Kurvenlaeufe.Length == 0,
            "Alte Bauzettel aktivieren keine radialen Reihen");
        var kopie = lauf.Clone(); kopie.Kontrollpunkte[0] += new float3(1, 0, 0);
        Pruefe(!kopie.InhaltGleich(lauf), "Kurvenlauf Clone und Gleichheit beruecksichtigen Formel");
        bool abgelehnt = false;
        int versionAufruf = 0;
        try { ParkingLotTool.Geometry.KurvenRandlaufFormat.Lesen(() => versionAufruf++ == 0 ? -1 : 0, Punkt); }
        catch (InvalidOperationException) { abgelehnt = true; }
        Pruefe(abgelehnt, "Unbekannte Kurvenrand-Version abweisen");
    }

    private static void Rundreise<T>(T wert, int felder, string name) where T : struct, ISerializable
    {
        var w = new Schreiber(); wert.Serialize(w);
        Pruefe(w.Felder.Count == felder, name + " Feldzahl");
        Pruefe(w.Felder.Count > 0 && w.Felder[0].Typ == typeof(int), name + " Version zuerst");
        // Zwei Datensaetze plus Endmarke: der Leser muss genau an der Grenze stehen.
        var strom = new List<Feld>();
        strom.AddRange(Felder(w)); strom.AddRange(Felder(w));
        strom.Add(new Feld { Name = "$ende", Typ = "String", Wert = JsonSerializer.SerializeToElement("ENDE_PLT") });
        var r = new Leser(strom);
        Lies<T>(r); Pruefe(r.Position == felder, name + " erste Datensatzgrenze");
        Lies<T>(r); r.Read(out string ende);
        Pruefe(ende == "ENDE_PLT" && r.Position == strom.Count, name + " Endmarke nach zwei Datensaetzen");
    }

    private static Leser StromAus<T>(T wert) where T : struct, ISerializable
    {
        var w = new Schreiber(); wert.Serialize(w);
        return new Leser(Felder(w));
    }

    private static List<Feld> Felder(Schreiber w)
    {
        var liste = new List<Feld>();
        foreach (var (typ, wert) in w.Felder)
        {
            object json = wert switch
            {
                float2 v => new[] { v.x, v.y },
                float3 v => new[] { v.x, v.y, v.z },
                _ => wert,
            };
            liste.Add(new Feld { Name = typ.Name, Typ = typ.Name, Wert = JsonSerializer.SerializeToElement(json) });
        }
        return liste;
    }
}
