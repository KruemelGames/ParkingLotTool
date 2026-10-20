using System;
using System.Collections.Generic;
using Unity.Mathematics;
using K = ParkingLotTool.Geometry.Strassenkurve;
using Z = ParkingLotTool.Geometry.Kurvenzettel;

internal static partial class KurvenzettelTests
{
    private static float3 P(double x, double z) => new float3((float)x, 0, (float)z);

    private static float3[] Abschnitte(params K.Bezier[] c)
    {
        var a = new List<float3>();
        foreach (var b in c) { a.Add(b.A); a.Add(b.B); a.Add(b.C); a.Add(b.D); }
        return a.ToArray();
    }

    private static K.Bezier Bogen(double r, double von, double bis, double mx = 0, double mz = 0)
    {
        double a = von * Math.PI / 180, b = bis * Math.PI / 180, h = 4.0 / 3 * Math.Tan((b - a) / 4);
        var m = P(mx, mz);
        var pa = P(r * Math.Cos(a), r * Math.Sin(a)) + m; var pd = P(r * Math.Cos(b), r * Math.Sin(b)) + m;
        return new K.Bezier(pa, pa + P(-r * Math.Sin(a) * h, r * Math.Cos(a) * h),
            pd - P(-r * Math.Sin(b) * h, r * Math.Cos(b) * h), pd);
    }

    internal static void Run(Action<bool, string> test, K.Ergebnis bogenErgebnis)
    {
        // Einfacher Bogen: kein Wendepunkt. Gerade: keiner. S-Kurve: genau einer.
        test(Z.ZaehleWendepunkte(Abschnitte(Bogen(60, 0, 45), Bogen(60, 45, 90))) == 0, "Bogen ohne Wendepunkt");
        var g = new K.Bezier(P(0, 0), P(10, 0), P(20, 0), P(30, 0));
        test(Z.ZaehleWendepunkte(Abschnitte(g)) == 0, "Gerade ohne Wendepunkt");
        // Zweiter Bogen gespiegelt um den Anschluss: Kruemmung wechselt das Vorzeichen.
        var links = Bogen(60, 0, 30);
        var ende = links.D; var tangente = math.normalize(links.D - links.C);
        var rechts = new K.Bezier(ende, ende + tangente * 10f, ende + tangente * 20f + new float3(tangente.z, 0, -tangente.x) * 1.5f,
            ende + tangente * 30f + new float3(tangente.z, 0, -tangente.x) * 6f);
        test(Z.ZaehleWendepunkte(Abschnitte(links, rechts)) == 1, "S-Kurve: ein Wendepunkt");
        test(Z.ZaehleWendepunkte(null) == 0, "Leer ohne Wendepunkt");
        // Bogen in Weltkoordinaten mit fast geraden Zwischenstuecken: Rauschen ist kein Wendepunkt.
        var welt = new float3(1300, 0, 4600);
        var zug = new List<float3>();
        var bogenA = Bogen(140, 0, 30);
        var richtung = math.normalize(bogenA.D - bogenA.C); var quer = new float3(-richtung.z, 0, richtung.x);
        // 5 m fast gerade mit 2 mm Zittern quer: dreht in beide Richtungen, zusammen unter 1 Grad.
        var zittern = new K.Bezier(bogenA.D, bogenA.D + richtung * 1.667f + quer * .002f,
            bogenA.D + richtung * 3.333f - quer * .002f, bogenA.D + richtung * 5f);
        foreach (var teil in new[] { bogenA, zittern, Bogen(140, 30, 88) })
            foreach (var q in Abschnitte(teil)) zug.Add(q + welt);
        test(Z.ZaehleWendepunkte(zug) == 0, "Weltbogen mit Kurzstuecken ohne Wendepunkt");
        var sWelt = new List<float3>();
        foreach (var q in Abschnitte(links, rechts)) sWelt.Add(q + welt);
        test(Z.ZaehleWendepunkte(sWelt) == 1, "S-Kurve in Weltkoordinaten: ein Wendepunkt");

        // Aus einem echten Verfolgungsergebnis.
        var z = Z.Aus(bogenErgebnis, bogenErgebnis.Punkte[0].xz, bogenErgebnis.Punkte[bogenErgebnis.Punkte.Length - 1].xz);
        test(z != null && z.Zwischen == bogenErgebnis.Punkte.Length - 2, "Zettel: Zwischenpunkte");
        test(z.AbschnittAnzahl >= 1 && z.Abschnitte.Length == z.AbschnittAnzahl * 4, "Zettel: Formel vorhanden");
        test(z.Bogen.Length == z.Zwischen, "Zettel: Station je Zwischenpunkt");
        for (int i = 0; i < z.Bogen.Length; i++)
            test(z.Bogen[i] > 0 && z.Bogen[i] < z.Laenge && (i == 0 || z.Bogen[i] > z.Bogen[i - 1]), "Zettel: Stationen steigen");
        test(z.Wendepunkte == 0 && z.RadiusMin > 50 && z.RadiusMin < 70, "Zettel: Bogen R60 ohne Wendepunkt");
        test(z.Toleranz == (float)K.Pfeilhoehe && z.MinSehne == (float)K.Mindestsehne && z.MaxSehne == (float)K.Hoechstsehne,
            "Zettel: Grenzwerte mitgeschrieben");
        test(z.Kurzzeile().Contains("R min") && z.Kurzzeile().Contains("Wendepunkt"), "Zettel: Logzeile");
        test(Z.Aus(null, default, default) == null, "Zettel: ohne Ergebnis nichts");

        // Wiederfinden der Zwischenpunkte im Punktzug: vorwaerts, rueckwaerts, ueber das Ringende.
        var ring = new List<float2> { new float2(0, 0), new float2(1, 0), new float2(2, 0), new float2(3, 0), new float2(3, 5), new float2(0, 5) };
        var vor = new Z { Von = new float2(0, 0), Bis = new float2(3, 0), Zwischen = 2, Toleranz = .15f,
            Abschnitte = Abschnitte(new K.Bezier(P(0, 0), P(1, 0), P(2, 0), P(3, 0))) };
        test(vor.FindeZwischenpunkte(ring, out var a) && a.Length == 2 && a[0].x == 1 && a[1].x == 2, "Finde vorwaerts");
        // Von (3,0) nach (0,0) gibt es im Ring zwei Wege mit je zwei Punkten - ohne Formel kein Raten.
        var mehrdeutig = new Z { Von = new float2(3, 0), Bis = new float2(0, 0), Zwischen = 2, Toleranz = .15f };
        test(!mehrdeutig.FindeZwischenpunkte(ring, out _), "Zwei Wege ohne Formel: nichts");
        var rueck = new Z { Von = new float2(3, 0), Bis = new float2(0, 0), Zwischen = 2, Toleranz = .15f,
            Abschnitte = Abschnitte(new K.Bezier(P(3, 0), P(2, 0), P(1, 0), P(0, 0))) };
        test(rueck.FindeZwischenpunkte(ring, out var b) && b[0].x == 2 && b[1].x == 1, "Finde rueckwaerts per Formel");
        var gedreht = new List<float2> { new float2(2, 0), new float2(3, 0), new float2(3, 5), new float2(0, 5), new float2(0, 0), new float2(1, 0) };
        test(vor.FindeZwischenpunkte(gedreht, out var c) && c[0].x == 1 && c[1].x == 2, "Finde ueber das Ringende");
        var falsch = new Z { Von = new float2(0, 0), Bis = new float2(3, 0), Zwischen = 3 };
        test(!falsch.FindeZwischenpunkte(ring, out _), "Falsche Zahl: nichts gefunden");

        // Kurvenlaeufe fuer das Layout (Etappe 6): Index und Richtung im Polygon, Formel mitgedreht.
        var laeufeVor = Z.Laeufe(ring, new[] { vor });
        test(laeufeVor.Length == 1 && laeufeVor[0].Von == 0 && laeufeVor[0].Kanten == 3
            && laeufeVor[0].Kontrollpunkte[0].x == 0 && laeufeVor[0].Kontrollpunkte[3].x == 3, "Lauf vorwaerts");
        var laeufeRueck = Z.Laeufe(ring, new[] { rueck });
        test(laeufeRueck.Length == 1 && laeufeRueck[0].Von == 0 && laeufeRueck[0].Kanten == 3
            && laeufeRueck[0].Kontrollpunkte[0].x == 0 && laeufeRueck[0].Kontrollpunkte[3].x == 3,
            "Lauf rueckwaerts: Von in Polygonrichtung, Formel gedreht");
        var laeufeGedreht = Z.Laeufe(gedreht, new[] { vor });
        test(laeufeGedreht.Length == 1 && laeufeGedreht[0].Von == 4 && laeufeGedreht[0].Kanten == 3, "Lauf ueber das Ringende");
        // Versetzte/gekuerzte Kurve: Formel passt nicht mehr zu den Enden - kein Lauf.
        var versetzt = vor.MitEnden(new float2(0, .5f), new float2(3, .5f));
        var ringVersetzt = new List<float2> { new float2(0, .5f), new float2(1, .5f), new float2(2, .5f), new float2(3, .5f), new float2(3, 5), new float2(0, 5) };
        test(Z.Laeufe(ringVersetzt, new[] { versetzt }).Length == 0, "Versetzte Kurve: kein Lauf");
        test(Z.Laeufe(ring, null).Length == 0 && Z.Laeufe(ring, new Z[] { null }).Length == 0, "Ohne Zettel: kein Lauf");

        // Gekuerzte Kurve auf demselben Bordstein (Schliesskante gezogen, 20.10.): Formel wird
        // zugeschnitten statt den Lauf zu verlieren. Bogen R60 0..90 Grad, Enden bei 20/70 Grad.
        var formel = Abschnitte(Bogen(60, 0, 45), Bogen(60, 45, 90));
        float2 Auf(double grad) => new float2((float)(60 * Math.Cos(grad * Math.PI / 180)), (float)(60 * Math.Sin(grad * Math.PI / 180)));
        var gekuerzt = new Z { Von = Auf(20), Bis = Auf(70), Zwischen = 4, Toleranz = .15f, Abschnitte = formel };
        var uRing = new List<float2> { Auf(20), Auf(30), Auf(40), Auf(50), Auf(60), Auf(70), new float2(0, 0) };
        var lauf = Z.Laeufe(uRing, new[] { gekuerzt });
        test(lauf.Length == 1 && lauf[0].Von == 0 && lauf[0].Kanten == 5, "Gekuerzte Kurve behaelt ihren Lauf");
        var kp = lauf.Length == 1 ? lauf[0].Kontrollpunkte : new float3[0];
        test(kp.Length >= 4 && math.distance(kp[0].xz, Auf(20)) < 1e-3f && math.distance(kp[kp.Length - 1].xz, Auf(70)) < 1e-3f,
            "Zugeschnittene Formel endet genau an den Formpunkten");
        bool aufBogen = true;
        for (int s = 0; s + 3 < kp.Length; s += 4)
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f, u = 1 - t;
                var q = u * u * u * kp[s] + 3 * u * u * t * kp[s + 1] + 3 * u * t * t * kp[s + 2] + t * t * t * kp[s + 3];
                aufBogen &= Math.Abs(math.length(q.xz) - 60) < .05;
            }
        test(aufBogen, "Zugeschnittene Formel bleibt auf dem Bordstein");
        test(Z.Zuschneiden(formel, Auf(70), Auf(20), .2) == null, "Vertauschte Enden: kein Zuschnitt");
        test(Z.Zuschneiden(formel, new float2(10, 10), Auf(70), .2) == null, "Ende neben der Formel: kein Zuschnitt");
    }
}
