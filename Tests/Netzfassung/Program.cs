using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static class Program
{
    private static int _tests;
    private static readonly Type Strasse = typeof(ParkingGeometry).GetNestedType("ZellenStrasse", BindingFlags.NonPublic);
    private static readonly MethodInfo Binde = typeof(ParkingGeometry).GetMethod("ZellenBindeNetzknoten", BindingFlags.NonPublic | BindingFlags.Static);
    private const BindingFlags Felder = BindingFlags.Instance | BindingFlags.NonPublic;

    private static int Main()
    {
        try { Laufe(); Console.WriteLine($"Netzfassung: {_tests} Pruefungen, 0 Fehler."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Pruefe(bool ok, string name)
    {
        _tests++;
        if (!ok) throw new Exception(name);
    }

    private static Punkt P(double x, double y) => new Punkt(x, y);
    private static object L(Punkt a, Punkt b, string kind = "perimeter")
    {
        var s = Activator.CreateInstance(Strasse, true);
        Strasse.GetProperty("A", Felder).SetValue(s, a);
        Strasse.GetProperty("B", Felder).SetValue(s, b);
        Strasse.GetProperty("Kind", Felder).SetValue(s, kind);
        Strasse.GetProperty("Art", Felder).SetValue(s, Zufahrtsart.Fussweg);
        return s;
    }
    private static IList Liste(params object[] linien)
    {
        var l = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Strasse));
        foreach (var s in linien) l.Add(s);
        return l;
    }
    private static Punkt Ende(object s, string name) => (Punkt)Strasse.GetProperty(name, Felder).GetValue(s);
    private static IList Fasse(IList l, params Punkt[] fest) => (IList)Binde.Invoke(null,
        new object[] { l, new Rahmen(P(1, 0), P(0, 1)), new HashSet<Punkt>(fest) });
    private static void Mindestmass(IList l)
    {
        foreach (var s in l)
        {
            var a = Ende(s, "A"); var b = Ende(s, "B");
            double x = (double)(float)a.X - (float)b.X, y = (double)(float)a.Y - (float)b.Y;
            Pruefe(Math.Sqrt(x * x + y * y) >= 1, "Jeder Welt-float-Kurs ist mindestens 1 m lang");
        }
    }

    private static void Laufe()
    {
        Pruefe(ParkingGeometry.NetzMindestlaenge == 1f, "Gemeinsamer Vertrag ist ein Meter");
        var lang = Liste(L(P(0, 0), P(1, 0)), L(P(0.4, 0.2), P(0.4, 8), "cross"));
        Pruefe(ReferenceEquals(lang, Fasse(lang)), "Ohne kurze Kante bleibt das Netz unangetastet");
        var ecke = P(0.4, 0);
        var ring = Liste(L(P(0, 0), ecke), L(ecke, P(10, 0)), L(P(10, 0), P(10, 10)),
            L(P(10, 10), P(0, 10)), L(P(0, 10), P(0, 0)), L(P(0, 0), P(5, 5), "cross"),
            L(P(0.3, 0.1), P(0.3, 7), "aisle"));
        var neu = Fasse(ring, ecke);
        Mindestmass(neu);
        Pruefe(neu.Count == 6, "Kurzes Ringstueck wird durch einen gemeinsamen Knoten ersetzt");
        Pruefe(neu.Cast<object>().Count(s => Ende(s, "A").Equals(ecke) || Ende(s, "B").Equals(ecke)) == 3,
            "Alle Anschlussenden teilen exakt die feste Kurvenecke");
        Pruefe(neu.Cast<object>().All(s => !Ende(s, "A").Equals(P(0, 0)) && !Ende(s, "B").Equals(P(0, 0))),
            "Der ersetzte Knoten bleibt nirgends als loses Ende stehen");
        Pruefe(neu.Cast<object>().Any(s => Ende(s, "A").Equals(P(0.3, 0.1)) && Ende(s, "B").Equals(P(0.3, 7))),
            "Nahe unverbundene Wege werden nicht vereinnahmt");
        var quer = neu.Cast<object>().Single(s => (string)Strasse.GetProperty("Kind", Felder).GetValue(s) == "cross");
        Pruefe((Zufahrtsart)Strasse.GetProperty("Art", Felder).GetValue(quer) == Zufahrtsart.Fussweg,
            "Wegart bleibt beim Binden erhalten");

        var kette = Fasse(Liste(L(P(0, 0), P(0.8, 0)), L(P(0.8, 0), P(0, 0.9)), L(P(0, 0.9), P(0, 8))));
        Mindestmass(kette);
        Pruefe(kette.Count == 1 && Ende(kette[0], "A").Equals(P(0, 0)),
            "Eine durch Bindung neu entstehende Kurzstrecke wird am selben Knoten gefasst");

        var transitiv = Fasse(Liste(L(P(0, 0), P(0.8, 0)), L(P(0.8, 0), P(0, 0.9)),
            L(P(0, 0.9), P(0, 8), "cross")));
        Mindestmass(transitiv);
        Pruefe(transitiv.Count == 1 && Ende(transitiv[0], "A").Equals(P(0, 0.9)),
            "Eine spaetere Bindung nimmt alle vorher gebundenen Anschlussenden mit");

        var a = P(10000, 10000); var b = P(10000.99950, 10000.033);
        Pruefe(Geometrie.Laenge(b - a) >= 1, "Float-Fixtur ist lokal mindestens einen Meter lang");
        var wa = new float2((float)a.X, (float)a.Y);
        var wb = new float2((float)b.X, (float)b.Y);
        Pruefe(math.distance(wa, wb) < 1, "Float-Fixtur unterschreitet als Welt-float den Meter");
        var rund = Fasse(Liste(L(a, b, "entrance"), L(a, P(10000, 10010)), L(a, P(9990, 10000))));
        Mindestmass(rund);
        Pruefe(rund.Count == 3, "Float-Rundung darf einen baubaren Zugang nicht verschlucken");
        var zugang = rund.Cast<object>().Single(s => (string)Strasse.GetProperty("Kind", Felder).GetValue(s) == "entrance");
        Pruefe(Ende(zugang, "A").Equals(a) && Geometrie.Laenge(Ende(zugang, "B") - b) < 0.002,
            "Welt-Rundung erhaelt den Anschluss und bewegt nur den freien Endpunkt");

        var polygon = new[] { new float2(0, 0), new float2(120, 0), new float2(120, 90), new float2(0, 90) };
        var satz = LayoutSettings.Cs2; satz.Auto = false; satz.AngleMode = "fixed";
        var layout = ParkingGeometry.Build(polygon, satz);
        Pruefe(layout.NetLine.Length > 10, "Produktiver Bau liefert ein vollstaendiges Netz");
        foreach (var n in layout.NetLine)
            Pruefe(math.distance(n.A, n.B) >= ParkingGeometry.NetzMindestlaenge, "Produktiver Bau haelt den Netzvertrag ein");
        var rundrand = new[] { new float2(0, 0), new float2(127, 0), new float2(127, 112),
            new float2(110, 106), new float2(95, 100), new float2(80, 92), new float2(68, 83),
            new float2(56, 74), new float2(43, 67), new float2(23, 64), new float2(0, 56) };
        satz = LayoutSettings.Cs2;
        var bestand = ParkingGeometry.Build(rundrand, satz);
        Pruefe(bestand.NetLine.Length > 10, "Historische Randfixtur bleibt befahrbar");
        foreach (var n in bestand.NetLine)
            Pruefe(math.distance(n.A, n.B) >= ParkingGeometry.NetzMindestlaenge,
                "Produktive Netzfassung bindet die historischen Kurzstuecke");
    }
}
