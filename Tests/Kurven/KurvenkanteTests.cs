using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using K = ParkingLotTool.Geometry.Strassenkurve;

internal static class KurvenkanteTests
{
    private static float3 P(double x, double z) => new float3((float)x, 0, (float)z);
    private static K.Bezier Bogen(double r, double von, double bis, float3 o)
    {
        double a = von * Math.PI / 180, b = bis * Math.PI / 180;
        double h = 4.0 / 3 * Math.Tan((b - a) / 4);
        var pa = P(r * Math.Cos(a), r * Math.Sin(a));
        var pd = P(r * Math.Cos(b), r * Math.Sin(b));
        return new K.Bezier(pa + o, pa + P(-r * Math.Sin(a) * h, r * Math.Cos(a) * h) + o,
            pd - P(-r * Math.Sin(b) * h, r * Math.Cos(b) * h) + o, pd + o);
    }

    internal static void Pruefe(Action<bool, string> test)
    {
        var uhr = Stopwatch.StartNew();
        foreach (int r in new[] { 15, 30, 60, 150 })
        foreach (int winkel in new[] { 90, 180 })
        foreach (var o in new[] { default(float3), P(-1500, 2000) })
        {
            var c = winkel == 90 ? new[] { Bogen(r, 0, 90, o) }
                : new[] { Bogen(r, 0, 90, o), Bogen(r, 90, 180, o) };
            var e = K.Einfrieren(123, c);
            test(e.Folgt && e.Gruppe != null, $"Einfrieren R{r}/{winkel}/{o}: {e.Grund}");
            var g = e.Gruppe;
            test(g.Id == 123 && g.Stuecke == e.Stuecke, "Stabile Gruppen-ID und gemeinsame Abtastung");
            PruefeGruppe(g, test);
            PruefeGruppe(g.Umkehren(), test);
            double referenz = g.Abschnitte.Sum(a => DichteLaenge(a));
            test(Math.Abs(g.Laenge - referenz) < .00003, "Bogenlaenge gegen dichte unabhaengige Laengenmessung");
            var ring = g.Punkte.Select(p => new float2((float)p.x, (float)p.z)).ToArray();
            test(Cs2Triangulierung.Dreiecke(ring) == ring.Length - 2, "Gebildeter Kreisbogenring wird von CS2 angenommen");
            var kopie = g.Umkehren().Umkehren();
            test(math.distance(kopie.Anfang, g.Anfang) < 1e-9 && kopie.Id == g.Id, "Gegenrichtung zweimal samt ID");
            var um = g.Umkehren();
            for (int i = 0; i < g.Punkte.Count; i++)
            {
                test(math.distance(g.Punkte[i], um.Punkte[g.Stuecke - i]) < 1e-9, "Gegenrichtung Endpunkte und alle Abtastpunkte");
                test(Math.Abs(um.Bogenlaengen[g.Stuecke - i] - (g.Laenge - g.Bogenlaengen[i])) < 1e-9,
                    "Gegenrichtung Bogenlaengen");
            }
            c[0] = default;
            e.Punkte[0] = default;
            test(math.distance(g.Anfang, g.Abschnitte[0].A) < .001, "Eingabe- und Diagnoseaenderung veraendern eingefrorene Gruppe nicht");
        }
        var s = new K.Bezier(P(0, 0), P(20, 30), P(40, -30), P(60, 0));
        test(Kurvengruppe.Abschnitt.Aus(s).Huelle(s.A, s.D) == 30, "Kontrollhuelle des symmetrischen S statt Mittelpunkt");
        var sym = K.Einfrieren(7, new[] { s });
        test(sym.Folgt, "Symmetrisches S wird eingefroren");
        PruefeGruppe(sym.Gruppe, test);
        PruefeGruppe(sym.Gruppe.Umkehren(), test);

        // 64 volle Hoechstsehnen: genau am Deckel (haengt an der Konstante, nicht an 8 m).
        double deckel = 64 * K.Hoechstsehne;
        var gerade = new K.Bezier(P(0, 0), P(deckel / 3, 0), P(deckel * 2 / 3, 0), P(deckel, 0));
        var genau = K.Einfrieren(1, new[] { gerade });
        test(genau.Folgt && genau.Stuecke == 64, "Budgetgrenze exakt 64");
        var zuViel = K.Einfrieren(1, new[] { gerade }, 63);
        test(zuViel.Grund == K.Grund.Budget && zuViel.Punkte.Length == 0 && zuViel.Gruppe == null,
            "Budget 63 liefert keinen Teilstand");
        var eng = K.Einfrieren(1, new[] { Bogen(15, 0, 90, default) }, innenversatz: 10.4, innenseite: 1);
        test(eng.Grund == K.Grund.ZuEng && eng.Punkte.Length == 0 && eng.Gruppe == null, "Zu eng liefert keine Vorschaugruppe");
        test(K.Einfrieren(1, new[] { default(K.Bezier) }).Grund == K.Grund.Nullsehne, "Einfrieren Nullsehne");
        test(K.Einfrieren(0, new[] { s }).Grund == K.Grund.Ungueltig, "Fehlende Gruppen-ID");
        test(K.Einfrieren(1, new[] { s }, innenversatz: double.NaN).Grund == K.Grund.Ungueltig, "NaN Versatz");
        test(K.Einfrieren(1, new[] { s }, 65).Grund == K.Grund.Ungueltig, "Kein Budget ueber dem harten Deckel");
        test(!sym.Gruppe.AufPolygon(double.NaN, 0, 100, out _), "NaN Bogenlaenge abweisen");
        test(!sym.Gruppe.VomPolygon(new Kurvengruppe.Polygonlage(0, double.PositiveInfinity), 0, 100, out _), "Inf Along abweisen");
        test(!sym.Gruppe.AufPolygon(-1, 0, 100, out _), "Negativer Bogen");
        test(!sym.Gruppe.AufPolygon(0, 100, 100, out _), "Ungueltige Startecke");
        test(!sym.Gruppe.VomPolygon(new Kurvengruppe.Polygonlage(99, 0), 0, 100, out _), "Fremdes Polygonsegment");
        K.Bezier Linie(float3 a, float3 b) => new K.Bezier(a, math.lerp(a, b, 1f / 3), math.lerp(a, b, 2f / 3), b);
        var v = P(20, 0);
        var w = v + P(2 * Math.Cos(8 * Math.PI / 180), 2 * Math.Sin(8 * Math.PI / 180));
        var z = w + P(20 * Math.Cos(16 * Math.PI / 180), 20 * Math.Sin(16 * Math.PI / 180));
        var knicke = K.Einfrieren(18, new[] { Linie(P(0, 0), v), Linie(v, w), Linie(w, z) });
        test(knicke.Folgt, $"Zwei kleine native Tangentenknicke bleiben abtastbar: {knicke.Grund}, R={knicke.Radius}, n={knicke.Stuecke}");
        PruefeGruppe(knicke.Gruppe, test);
        var hang = K.Einfrieren(3, new[] { new K.Bezier(new float3(0, 0, 0),
            new float3(13, .1f, 0), new float3(27, .2f, 0), new float3(40, .3f, 0)) }).Gruppe;
        test(hang.Laenge > 40 && hang.AufPolygon(hang.Laenge, 0, 20, out var hangort)
            && Math.Abs(hangort.Along - math.distance(hang.Punkte[hang.Stuecke - 1].xz, hang.Ende.xz)) < 1e-10,
            "Along misst XZ-Polygonmeter, Bogenlaenge den 3D-Rand");
        VersatzTests.Pruefe(test);
        uhr.Stop();
        Console.WriteLine($"Eingefrorene Kurven, dichte Abstaende, Bogenrundreisen und Versatz: {uhr.Elapsed.TotalMilliseconds:F1} ms");
        var gross = new[] { Bogen(150, 0, 90, default), Bogen(150, 90, 180, default) };
        var zeiten = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            var takt = Stopwatch.StartNew();
            var stand = K.Einfrieren(200, gross);
            takt.Stop();
            test(stand.Folgt && stand.Gruppe != null && stand.Stuecke == stand.Gruppe.Stuecke && stand.Stuecke >= 30 && stand.Stuecke <= 64, "Messung friert ganzen R150/180-Bogen ein");
            if (i >= 10) zeiten.Add(takt.Elapsed.TotalMilliseconds);
        }
        zeiten.Sort();
        Console.WriteLine($"R150/180 einfrieren, {K.Einfrieren(200, gross).Stuecke} Stuecke: Median {zeiten[15]:F3} ms, p95 {zeiten[28]:F3} ms (30 warm)");
    }

    internal static void PruefeGruppe(Kurvengruppe g, Action<bool, string> test)
    {
        test(g.Bogenlaengen[0] == 0 && g.Laenge > 0, "Bogenlaengen starten bei null");
        for (int i = 0; i < g.Stuecke; i++)
        {
            test(g.Bogenlaengen[i + 1] > g.Bogenlaengen[i], "Bogenlaengen streng aufsteigend");
            double sehne = math.distance(g.Punkte[i].xz, g.Punkte[i + 1].xz);
            test(sehne >= K.Mindestsehne - .001 && sehne <= K.Hoechstsehne + .001, "Eingefrorene Sehnen 1..8 m");
        }
        test(math.distance(g.Anfang, g.Abschnitte[0].A) < .001
            && math.distance(g.Ende, g.Abschnitte[g.Abschnitte.Count - 1].D) < .001, "Orientierte Abschnitte passen zu Endpunkten");
        double bogenStart = 0;
        var drehung = new double[g.Stuecke];
        double3 letzterTan = default;
        bool hatTan = false;
        foreach (var c in g.Abschnitte)
        {
            var tan = Tangente(c, 0);
            if (hatTan)
            {
                test(g.AufPolygon(Math.Min(g.Laenge, bogenStart), 0, g.Stuecke + 2, out var naht), "Native Naht zu Polygon");
                drehung[naht.Segment] += Math.Acos(math.clamp(math.dot(letzterTan, tan), -1, 1));
            }
            var vorher = c.Punkt(0);
            double s = 0;
            for (int j = 0; j <= 4096; j++)
            {
                var p = c.Punkt(j / 4096.0);
                double dist = double.PositiveInfinity;
                for (int i = 0; i < g.Stuecke; i++) dist = Math.Min(dist, Abstand(p, g.Punkte[i], g.Punkte[i + 1]));
                test(dist <= K.Pfeilhoehe + .000001, "Dichte unabhaengige Bezier-Abstandsmessung");
                var t = Tangente(c, j / 4096.0);
                s += math.distance(p, vorher);
                // Dichte Tangentenprobe, unabhaengig von der Radius-Huellenschranke.
                double bogen = Math.Min(g.Laenge, bogenStart + s);
                test(g.AufPolygon(bogen, 0, g.Stuecke + 2, out var ort), "Bogenprobe zu Polygon");
                double dot = math.clamp(math.dot(t, tan), -1, 1);
                test(!double.IsNaN(Math.Acos(dot)), "Dichte Tangenten endlich");
                drehung[ort.Segment] += Math.Acos(dot);
                tan = t; vorher = p;
            }
            letzterTan = tan;
            hatTan = true;
            bogenStart += DichteLaenge(c);
        }
        test(drehung.All(w => w <= K.Winkelgrenze + .001), "Dichte totale Tangentendrehung je Stueck maximal 15 Grad");
        int count = g.Stuecke + 5;
        foreach (int erste in new[] { 0, count - 2 })
        {
            for (int i = 0; i < g.Stuecke; i++)
            foreach (double anteil in new[] { 0d, .37, 1d })
            {
                double sehne = math.distance(g.Punkte[i].xz, g.Punkte[i + 1].xz);
                var eingabe = new Kurvengruppe.Polygonlage((erste + i) % count, sehne * anteil);
                double erwartet = g.Bogenlaengen[i] + anteil * (g.Bogenlaengen[i + 1] - g.Bogenlaengen[i]);
                test(g.VomPolygon(eingabe, erste, count, out double s) && Math.Abs(s - erwartet) < 1e-8,
                    "Polygonsegment und Along zu unabhaengiger Bogenposition");
                test(g.AufPolygon(s, erste, count, out var ausgabe), "Rueckweg zur Polygonlage");
                int lokal = (ausgabe.Segment - erste + count) % count;
                var ort = math.lerp(g.Punkte[lokal].xz, g.Punkte[lokal + 1].xz,
                    ausgabe.Along / math.distance(g.Punkte[lokal].xz, g.Punkte[lokal + 1].xz));
                test(math.distance(ort, math.lerp(g.Punkte[i].xz, g.Punkte[i + 1].xz, anteil)) < 1e-8,
                    "Polygonlagen-Rundreise bleibt am selben Weltpunkt");
            }
            for (int j = 0; j <= 1000; j++)
            {
                double s = g.Laenge * j / 1000;
                test(g.AufPolygon(s, erste, count, out var ort), "Bogenlaenge zu Polygonsegment plus Along");
                test(g.VomPolygon(ort, erste, count, out var zurueck) && Math.Abs(zurueck - s) < 1e-8,
                    "Bogenlaengen-Rundreise mit Startecke und Gegenrichtung");
            }
        }
    }

    private static double Abstand(double3 p, double3 a, double3 b)
    {
        var v = b.xz - a.xz;
        double t = math.clamp(math.dot(p.xz - a.xz, v) / math.lengthsq(v), 0, 1);
        return math.distance(p.xz, a.xz + t * v);
    }
    private static double3 Tangente(Kurvengruppe.Abschnitt c, double t)
        => math.normalize(3 * (1 - t) * (1 - t) * (c.B - c.A)
            + 6 * (1 - t) * t * (c.C - c.B) + 3 * t * t * (c.D - c.C));
    private static double DichteLaenge(Kurvengruppe.Abschnitt c)
    {
        double sum = 0; var alt = c.A;
        for (int i = 1; i <= 32768; i++) { var p = c.Punkt(i / 32768.0); sum += math.distance(alt, p); alt = p; }
        return sum;
    }
}
