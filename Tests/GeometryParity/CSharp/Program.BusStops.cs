using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int PruefeBushaltestellen()
    {
        var errors = 0;
        var checkedCases = 0;
        void Check(bool okay, string name)
        {
            checkedCases++;
            if (okay) return;
            errors++;
            Console.WriteLine("FEHLER: " + name);
        }
        var layout = new ParkingLayout
        {
            NetLine = new[]
            {
                new NetSegment("aisle", new float2(0, 2), new float2(40, 2)),
                new NetSegment("perimeter", new float2(0, 4), new float2(40, 4)),
                new NetSegment("zoning", new float2(0, 0), new float2(40, 0)),
            },
        };
        Check(BusStopSnap.TryFind(layout, new float2(20, 5), 8, 7, 3,
            out var left) && left.Left && math.abs(left.Along - .5f) < 1e-5f,
            "links auf Zoning statt naeherer Fahrgasse/Randstrasse");
        Check(BusStopSnap.TryFind(layout, new float2(20, -5), 8, 7, 3,
            out var right) && !right.Left && right.Position.y == 0,
            "rechte Seite auf derselben Zoning-Linie");
        Check(!BusStopSnap.TryFind(layout, new float2(20, 8.01f), 8, 7, 3,
            out _), "maximaler Abstand");
        // SPEZIFIKATION GEAENDERT (Nutzer, 2026-09-24): gesperrte Stellen
        // werden UEBERSPRUNGEN, nicht abgelehnt. Vorher hiess es hier
        // "6-m-Endreserve: kein Halt"; jetzt springt der Punkt auf 6 m.
        Check(BusStopSnap.TryFind(layout, new float2(5.99f, 2), 8, 7, 3,
            out var amEnde) && amEnde.Along * 40f >= 6f - 1e-3f
            && amEnde.Along * 40f < 6.1f,
            "6-m-Endreserve: Punkt springt an die erlaubte Grenze");
        Check(!BusStopSnap.TryFind(layout, new float2(20, 0), 8, 7, 3,
            out _), "Seite auf Mittellinie nicht raten");
        var ohneZoning = new ParkingLayout
        {
            NetLine = new[]
            {
                new NetSegment("aisle", new float2(0, 0), new float2(40, 0)),
            },
        };
        Check(!BusStopSnap.TryFind(ohneZoning, new float2(20, 4), 8, 7, 3,
            out _), "ohne Zoning-Strasse kein Halt");
        var settings = LayoutSettings.Cs2;
        settings.BusStops = new[] { left, right };
        var clone = settings.Clone();
        Check(clone.BusStops.Length == 2 && clone.BusStops[0].Left
            && !clone.BusStops[1].Left, "beide Seiten im Layoutklon");
        Check(BusStopSnap.TryProjectToEdge(left, new float2(12, 0),
            new float2(28, 0), out var splitT, out var splitReverse,
            out _) && math.abs(splitT - .5f) < 1e-5f && !splitReverse,
            "geteilte gebaute Kante");
        Check(BusStopSnap.TryProjectToEdge(left, new float2(60, 0),
            new float2(-20, 0), out var joinedT, out var joinedReverse,
            out _) && math.abs(joinedT - .5f) < 1e-5f && joinedReverse,
            "vereinte umgekehrte Kante mit Seitenwechsel");
        Check(!BusStopSnap.TryProjectToEdge(left, new float2(0, 1.01f),
            new float2(40, 1.01f), out _, out _, out _),
            "nur dieselbe gebaute Linie");
        Check(math.distancesq(BusStopSnap.SignPosition(left),
            new float2(20, 4)) < 1e-5f
            && math.distancesq(BusStopSnap.SignPosition(right),
                new float2(20, -4)) < 1e-5f,
            "Schild auf Cursor-Seite");
        Check(math.distancesq(BusStopSnap.TravelDirection(right, false),
            new float2(1, 0)) < 1e-5f
            && math.distancesq(BusStopSnap.TravelDirection(left, true),
                new float2(1, 0)) < 1e-5f,
            "Fahrtrichtung bei Rechts- und Linksverkehr");
        Check(!BusStopSnap.IsDuplicate(left, right)
            && BusStopSnap.IsDuplicate(left, left),
            "mehrere Stopps und identische Platzierung");
        // KREUZUNGEN UEBERSPRINGEN: Querstrasse quer bei x=30, Fahrgasse
        // endet bei x=50 an der Zoning-Strasse (T-Stoss). Dazwischen 38..42 frei.
        var mitKreuzung = new ParkingLayout
        {
            NetLine = new[]
            {
                new NetSegment("zoning", new float2(0, 0), new float2(60, 0)),
                new NetSegment("cross", new float2(30, -20), new float2(30, 20)),
                new NetSegment("aisle", new float2(50, 20), new float2(50, 0.5f)),
            },
        };
        bool AufKreuzung(BusStopPlacement h)
        {
            var x = h.Position.x;
            var sperre = BusStopSnap.KreuzungHalbeBreite + BusStopSnap.KreuzungFreiraum;
            return math.abs(x - 30f) < sperre - 1e-2f || math.abs(x - 50f) < sperre - 1e-2f;
        }
        Check(BusStopSnap.TryFind(mitKreuzung, new float2(29, 5), 8, 7, 3, out var k1)
            && !AufKreuzung(k1) && math.abs(k1.Position.x - 22f) < 0.05f,
            "Kreuzung: Punkt springt vor die Querstrasse");
        Check(BusStopSnap.TryFind(mitKreuzung, new float2(33, -5), 8, 7, 3, out var k2)
            && !AufKreuzung(k2) && math.abs(k2.Position.x - 38f) < 0.05f,
            "Kreuzung: Punkt springt hinter die Querstrasse");
        Check(BusStopSnap.TryFind(mitKreuzung, new float2(51, 5), 8, 7, 3, out var k3)
            && !AufKreuzung(k3),
            "T-Stoss einer Fahrgasse wird ebenfalls uebersprungen");
        Check(BusStopSnap.TryFind(mitKreuzung, new float2(12, 5), 8, 7, 3, out var k4)
            && math.abs(k4.Position.x - 12f) < 1e-3f,
            "freie Stelle bleibt, wo sie ist");
        var probe = 0;
        var aufKreuzungGefunden = 0;
        for (var x = 0f; x <= 60f; x += 0.25f)
            foreach (var y in new[] { 5f, -5f })
                if (BusStopSnap.TryFind(mitKreuzung, new float2(x, y), 8, 7, 3, out var h))
                {
                    probe++;
                    if (AufKreuzung(h)) aufKreuzungGefunden++;
                }
        Check(probe > 0 && aufKreuzungGefunden == 0,
            $"kein Halt auf einer Kreuzung ({aufKreuzungGefunden} von {probe})");

        // GEGENUEBER EINRASTEN, Shift schaltet es ab.
        var vorhanden = new[]
        {
            new BusStopPlacement { A = new float2(0, 0), B = new float2(40, 0),
                Along = 0.25f, Left = false },
        };
        Check(BusStopSnap.TryFind(layout, new float2(14, 5), 8, 7, 3, out var g1,
                vorhanden, false)
            && g1.Left && math.abs(g1.Position.x - 10f) < 1e-3f,
            "gegenueber auf gleicher Hoehe eingerastet");
        Check(BusStopSnap.TryFind(layout, new float2(14, 5), 8, 7, 3, out var g2,
                vorhanden, true)
            && math.abs(g2.Position.x - 14f) < 1e-3f,
            "Shift: kein Einrasten");
        Check(BusStopSnap.TryFind(layout, new float2(14, -5), 8, 7, 3, out var g3,
                vorhanden, false)
            && math.abs(g3.Position.x - 14f) < 1e-3f,
            "gleiche Seite rastet nicht ein");
        Check(BusStopSnap.TryFind(layout, new float2(19, 5), 8, 7, 3, out var g4,
                vorhanden, false)
            && math.abs(g4.Position.x - 19f) < 1e-3f,
            "zu weit weg rastet nicht ein");
        var umgedreht = new[]
        {
            new BusStopPlacement { A = new float2(40, 0), B = new float2(0, 0),
                Along = 0.75f, Left = true },
        };
        Check(BusStopSnap.TryFind(layout, new float2(14, 5), 8, 7, 3, out var g5,
                umgedreht, false)
            && math.abs(g5.Position.x - 10f) < 1e-3f,
            "gegenueber auch bei andersherum gespeicherter Linie");

        // DIE ECHTE MUENDUNG (2026-09-25): im Spiel beruehrt keine Fahrgasse
        // die Zoning-Strasse. Ihr Ende liegt (8 + 7) / 2 = 7,5 m vor der
        // Achse, ein Querweg (8 + 3) / 2 = 5,5 m. Der alte Fall oben (0,5 m)
        // sah so nie aus - deshalb war der Test gruen und das Spiel falsch.
        var muendung = new ParkingLayout
        {
            NetLine = new[]
            {
                new NetSegment("zoning", new float2(0, 0), new float2(100, 0)),
                new NetSegment("aisle", new float2(30, 40), new float2(30, 7.5f)),
                new NetSegment("cross", new float2(70, -30), new float2(70, -5.5f)),
                // Parallel im Muendungsabstand: KEINE Muendung.
                new NetSegment("aisle", new float2(80, 7.5f), new float2(95, 7.5f)),
            },
        };
        Check(BusStopSnap.TryFind(muendung, new float2(30, 5), 8, 7, 3, out var m1)
            && math.abs(m1.Position.x - 30f) >= 3.5f + BusStopSnap.KreuzungFreiraum - 0.05f,
            "Fahrgassen-Muendung (7,5 m vor der Achse) wird uebersprungen");
        Check(BusStopSnap.TryFind(muendung, new float2(70, -5), 8, 7, 3, out var m2)
            && math.abs(m2.Position.x - 70f) >= 1.5f + BusStopSnap.KreuzungFreiraum - 0.05f,
            "Querweg-Muendung (5,5 m vor der Achse) wird uebersprungen");
        Check(BusStopSnap.TryFind(muendung, new float2(88, 5), 8, 7, 3, out var m3)
            && math.abs(m3.Position.x - 88f) < 1e-3f,
            "paralleler Weg im Muendungsabstand sperrt nichts");
        var probeM = 0; var inMuendung = 0;
        for (var x = 0f; x <= 100f; x += 0.25f)
            foreach (var y in new[] { 5f, -5f })
                if (BusStopSnap.TryFind(muendung, new float2(x, y), 8, 7, 3, out var hm))
                {
                    probeM++;
                    if (math.abs(hm.Position.x - 30f) < 3.5f + BusStopSnap.KreuzungFreiraum - 0.05f
                        || math.abs(hm.Position.x - 70f) < 1.5f + BusStopSnap.KreuzungFreiraum - 0.05f)
                        inMuendung++;
                }
        Check(probeM > 0 && inMuendung == 0,
            $"kein Halt in einer Muendung ({inMuendung} von {probeM})");

        Console.WriteLine($"BUSHALTESTELLEN: {checkedCases} Pruefungen, "
            + $"{errors} Fehler.");
        return errors == 0 ? 0 : 1;
    }
}
