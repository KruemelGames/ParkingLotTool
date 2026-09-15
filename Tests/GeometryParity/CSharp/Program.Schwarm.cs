using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * FORMEN-SCHWARM AUF DER C#-SEITE.
 *
 * Der Nutzer kann seine Formen nicht nachbauen und formulierte am 2026-08-17
 * das eigentliche Ziel: nicht im Kreis drehen, sondern bei so gut wie JEDER
 * Form ein ordentliches Ergebnis. Dafuer braucht es ein Mass ueber die ganze
 * Bandbreite statt Einzelfaelle.
 *
 * WARUM HIER UND NICHT IM PROTOTYP: der Prototyp hat in `finalizeMaterialSurfaces`
 * eine Weiche (`calibrated`), die bei `angleMode: "edge"` auf einen Rueckfallweg
 * springt. Der Mod hat diese Weiche NICHT und faehrt immer die strikte
 * Pipeline. Gemessen am selben Schwarm:
 *
 *     Prototyp edge    90 % entartete Flaechen, schlimmster Fall 1510 Ringe
 *     Prototyp auto    57 %, schlimmster Fall 30 Ringe
 *
 * Der Schwarm im Prototyp misst also Code, den im Spiel niemand ausfuehrt.
 * Diese Fassung baut mit `ParkingGeometry.Build` - genau dem, was ausgeliefert
 * wird - und im UI-Modus `edge`, den der Mod wirklich setzt.
 *
 * Aufruf: dotnet run -c Release -- --schwarm [Anzahl]
 */
internal static partial class Program
{
    private static long _schwarmSeed;

    private static double SchwarmZufall()
    {
        _schwarmSeed = (_schwarmSeed * 1103515245 + 12345) & 0x7fffffff;
        return _schwarmSeed / (double)0x7fffffff;
    }

    /** Dieselbe Erzeugung wie `formen-schwarm.cjs`, damit die Faelle vergleichbar sind. */
    private static float2[] SchwarmForm(int index)
    {
        var ecken = 4 + (int)(SchwarmZufall() * 5);
        var radius = 35 + SchwarmZufall() * 95;
        var unruhe = SchwarmZufall() * 0.55;
        var drehung = SchwarmZufall() * Math.PI * 2;
        var punkte = new System.Collections.Generic.List<double2>();
        for (var i = 0; i < ecken; i++)
        {
            var w = drehung + (i / (double)ecken) * Math.PI * 2;
            var r = radius * (1 - unruhe / 2 + SchwarmZufall() * unruhe);
            punkte.Add(new double2(Math.Cos(w) * r * (0.7 + SchwarmZufall() * 0.6),
                                   Math.Sin(w) * r * (0.7 + SchwarmZufall() * 0.6)));
        }
        // Jede zweite Form im Uhrzeigersinn - beide Umlaufrichtungen muessen
        // taugen, seit 9cc1582 wissen wir warum.
        if (index % 2 == 0) punkte.Reverse();
        return punkte.Select(p => new float2((float)(p.x - 1250), (float)(p.y + 190))).ToArray();
    }

    private static double KuerzesteKanteVon(float2[] ring)
    {
        var min = double.MaxValue;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            min = Math.Min(min, Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y)));
        }
        return min;
    }

    private static double FlaecheVon(float2[] ring)
    {
        double summe = 0;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            summe += a.x * b.y - b.x * a.y;
        }
        return Math.Abs(summe) / 2;
    }

    /**
     * EINE BESTIMMTE FORM UNTERSUCHEN, statt Zufallsformen.
     *
     * Der Nutzer meldet Formen aus dem Spiel; sein Bauprotokoll
     * (`ParkingLotTool-builds.jsonl`) enthaelt jedes Polygon mit voller
     * Genauigkeit. Damit laesst sich sein Fall hier exakt nachrechnen - und
     * genau das brauchte es am 2026-08-17, als er meldete, dass das Gruen aus
     * vielen kleinen Stuecken besteht statt aus einer grossen Flaeche.
     *
     * Aufruf: dotnet run -c Release -- --polygon "x,y;x,y;x,y;..."
     */
    internal static int ZufahrtKante = 0;
    internal static double ZufahrtLaenge = 20;

    /**
     * Zaehlt die Spalten zwischen zwei verschiedenen Flaechen desselben
     * Materials.
     *
     * Gemeint ist genau das, was im Spiel als Narbe zu sehen ist: beide
     * Flaechen sind gesund, aber zwischen ihnen liegt ein Streifen nackter
     * Boden. Gezaehlt wird jedes Paar, das sich naeher als CS2s Mindestkante
     * (0,375 m) kommt, ohne sich zu beruehren - unter dieser Breite kann dort
     * gar keine dritte Flaeche mehr stehen, der Streifen bleibt also leer.
     */
    private static void NenneSpalten(string name, float2[][] ringe)
    {
        if (ringe == null || ringe.Length < 2)
        {
            Console.WriteLine($"  {name}: unter zwei Flaechen, keine Spalte moeglich");
            return;
        }
        var spalten = new System.Collections.Generic.List<double>();
        for (var i = 0; i < ringe.Length; i++)
            for (var j = i + 1; j < ringe.Length; j++)
            {
                var d = RingAbstand(ringe[i], ringe[j]);
                // Null heisst beruehrend - das ist keine Spalte, sondern eine
                // gemeinsame Kante.
                if (d > 1e-6 && d < 0.375) spalten.Add(d);
            }
        if (spalten.Count == 0)
        {
            Console.WriteLine($"  {name}: keine Spalte unter 0,375 m");
            return;
        }
        spalten.Sort();
        Console.WriteLine($"  {name}: {spalten.Count} Spalte(n) unter 0,375 m, "
            + $"engste {spalten[0]:F3} m, weiteste {spalten[spalten.Count - 1]:F3} m");
    }

    /**
     * WO der nackte Boden liegt, nicht nur wie viel.
     *
     * Ohne Ortsangabe ist "1,5 m2 ungedeckt" nicht nachzugehen: der Nutzer
     * markiert eine Stelle, und die Zahl allein sagt nicht, ob es dieselbe
     * ist. Deshalb wird das feine Raster zu zusammenhaengenden Flecken
     * verklebt und jeder Fleck mit Mitte und Ausdehnung genannt.
     *
     * Die Ausdehnung ist die Aussage: ein Fleck von 15 x 0,3 m ist ein
     * Streifen an einer Kante, ein Fleck von 2 x 2 m ist ein vergessenes Eck.
     */
    private static void NenneFlecken(float2[] site, ParkingLayout layout,
                                     LayoutSettings einstellungen, double schritt)
    {
        var alle = layout.Bay.Concat(layout.GrassSurface)
            .Concat(layout.AsphaltSurface).ToList();
        var minX = site.Min(p => p.x);
        var maxX = site.Max(p => p.x);
        var minY = site.Min(p => p.y);
        var maxY = site.Max(p => p.y);
        var spalten = (int)Math.Ceiling((maxX - minX) / schritt) + 1;
        var zeilen = (int)Math.Ceiling((maxY - minY) / schritt) + 1;
        var leer = new bool[spalten, zeilen];
        for (var sx = 0; sx < spalten; sx++)
            for (var sy = 0; sy < zeilen; sy++)
            {
                var punkt = new float2(
                    (float)(minX + (sx + 0.5) * schritt),
                    (float)(minY + (sy + 0.5) * schritt));
                if (!PointIn(punkt, site)) continue;
                if (alle.Any(q => PointIn(punkt, q))
                    || alle.Any(q => DistanceToBoundary(punkt, q) <= 1e-6)) continue;
                leer[sx, sy] = true;
            }

        var gesehen = new bool[spalten, zeilen];
        var flecken = new System.Collections.Generic.List<(double A, double X,
            double Y, double B, double H)>();
        for (var sx = 0; sx < spalten; sx++)
            for (var sy = 0; sy < zeilen; sy++)
            {
                if (!leer[sx, sy] || gesehen[sx, sy]) continue;
                var stapel = new System.Collections.Generic.Stack<(int, int)>();
                stapel.Push((sx, sy));
                gesehen[sx, sy] = true;
                int x0 = sx, x1 = sx, y0 = sy, y1 = sy, zahl = 0;
                while (stapel.Count > 0)
                {
                    var (cx, cy) = stapel.Pop();
                    zahl++;
                    x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx);
                    y0 = Math.Min(y0, cy); y1 = Math.Max(y1, cy);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= spalten || ny >= zeilen) continue;
                        if (!leer[nx, ny] || gesehen[nx, ny]) continue;
                        gesehen[nx, ny] = true;
                        stapel.Push((nx, ny));
                    }
                }
                flecken.Add((zahl * schritt * schritt,
                    minX + (x0 + x1 + 1) * 0.5 * schritt,
                    minY + (y0 + y1 + 1) * 0.5 * schritt,
                    (x1 - x0 + 1) * schritt, (y1 - y0 + 1) * schritt));
            }

        if (flecken.Count == 0)
        {
            Console.WriteLine("  keine zusammenhaengenden Flecken");
            return;
        }
        Console.WriteLine($"  {flecken.Count} Fleck(en), die groessten:");
        foreach (var f in flecken.OrderByDescending(f => f.A).Take(6))
            Console.WriteLine($"    {f.A,6:F2} m2 bei {f.X,9:F2} / {f.Y,8:F2}, "
                + $"Ausdehnung {f.B:F2} x {f.H:F2} m");
    }

    /**
     * WIE NAH LAEUFT EINE VERBINDUNGSSTRASSE NEBEN EINER PARALLELEN FAHRBAHN?
     *
     * Gemessen wird der FREIE Streifen zwischen beiden Fahrbahnraendern, nicht
     * der Achsabstand: was zaehlt, ist der Platz, der dazwischen uebrig
     * bleibt. Ist er schmaler als eine Bucht tief ist, passt dort keine Reihe
     * mehr - die Verbindungsstrasse kostet dann Flaeche und einen
     * Kontaktpunkt, ohne etwas zu tragen.
     *
     * Belegt am Prototyp-Debug des Nutzers vom 2026-08-20 (L-Form, 16.769 m2):
     * Verbindungsstrasse bei x=41,61 (Breite 3) neben Randstrasse bei x=49,60
     * (Breite 7) - freier Streifen 2,99 m bei 5,9 m Buchttiefe. In dem
     * 512-m2-Streifen standen 4 Buchten auf 90 m Laenge.
     *
     * Nur PARALLELE Paare zaehlen (bis 1 Grad) und nur dort, wo sie
     * nebeneinander herlaufen: eine Verbindungsstrasse KREUZT Fahrgassen
     * rechtwinklig, das ist keine Enge, sondern ihr Zweck.
     */
    private sealed class QuerNaehe
    {
        internal double Frei = double.PositiveInfinity;
        internal string Wo = "";
        internal int Zahl;
    }

    private static QuerNaehe EngsteQuerNaehe(ParkingLayout layout,
                                             LayoutSettings s)
    {
        var ergebnis = new QuerNaehe();
        if (layout?.NetLine == null) return ergebnis;
        var quer = layout.NetLine.Where(n => n.Kind == "cross").ToArray();
        var laengs = layout.NetLine
            .Where(n => n.Kind == "perimeter" || n.Kind == "aisle").ToArray();
        // Halbe Breiten: Randstrasse und Fahrgasse fahren beide auf Ai,
        // die Verbindungsstrasse auf Cw (NetLine.cs, AddRoads).
        var halb = s.Ai / 2 + s.Cw / 2;
        var parallelSin = Math.Sin(Math.PI / 180);

        foreach (var q in quer)
        {
            var qv = new double2(q.B.x - q.A.x, q.B.y - q.A.y);
            var qLen = Math.Sqrt(qv.x * qv.x + qv.y * qv.y);
            if (qLen < 1) continue;
            var qDir = qv / qLen;

            foreach (var l in laengs)
            {
                var lv = new double2(l.B.x - l.A.x, l.B.y - l.A.y);
                var lLen = Math.Sqrt(lv.x * lv.x + lv.y * lv.y);
                if (lLen < 1) continue;
                var lDir = lv / lLen;
                if (Math.Abs(qDir.x * lDir.y - qDir.y * lDir.x) > parallelSin)
                    continue;

                // Laufen sie ueberhaupt nebeneinander her? Ohne diese Pruefung
                // zaehlten auch zwei Strassen, die nur zufaellig dieselbe
                // Richtung haben und hundert Meter auseinanderliegen.
                double Auf(float2 p) => (p.x - q.A.x) * qDir.x + (p.y - q.A.y) * qDir.y;
                var l0 = Math.Min(Auf(l.A), Auf(l.B));
                var l1 = Math.Max(Auf(l.A), Auf(l.B));
                var ueberlappung = Math.Min(qLen, l1) - Math.Max(0, l0);
                // Dieselbe Schwelle wie TooCloseToParallelRoad. Eine kurze
                // echte Ueberlappung rutscht damit durch - bekannter blinder
                // Fleck, siehe die Begruendung dort.
                if (ueberlappung < 2 * s.Sl) continue;
                var quer0 = Math.Abs((l.A.x - q.A.x) * -qDir.y
                                     + (l.A.y - q.A.y) * qDir.x);
                var frei = quer0 - halb;
                if (frei >= ergebnis.Frei) continue;
                ergebnis.Frei = frei;
                ergebnis.Wo = $"Quer ({q.A.x:F1}/{q.A.y:F1} -> {q.B.x:F1}/{q.B.y:F1}) "
                    + $"neben {l.Kind} ({l.A.x:F1}/{l.A.y:F1} -> {l.B.x:F1}/{l.B.y:F1})";
            }
        }

        if (double.IsPositiveInfinity(ergebnis.Frei)) return ergebnis;
        // Wie viele Paare unterschreiten die Buchttiefe?
        foreach (var q in quer)
        {
            var qv = new double2(q.B.x - q.A.x, q.B.y - q.A.y);
            var qLen = Math.Sqrt(qv.x * qv.x + qv.y * qv.y);
            if (qLen < 1) continue;
            var qDir = qv / qLen;
            foreach (var l in laengs)
            {
                var lv = new double2(l.B.x - l.A.x, l.B.y - l.A.y);
                var lLen = Math.Sqrt(lv.x * lv.x + lv.y * lv.y);
                if (lLen < 1) continue;
                var lDir = lv / lLen;
                if (Math.Abs(qDir.x * lDir.y - qDir.y * lDir.x) > parallelSin) continue;
                double Auf(float2 p) => (p.x - q.A.x) * qDir.x + (p.y - q.A.y) * qDir.y;
                var l0 = Math.Min(Auf(l.A), Auf(l.B));
                var l1 = Math.Max(Auf(l.A), Auf(l.B));
                if (Math.Min(qLen, l1) - Math.Max(0, l0) < 2 * s.Sl) continue;
                var frei = Math.Abs((l.A.x - q.A.x) * -qDir.y
                                    + (l.A.y - q.A.y) * qDir.x) - halb;
                if (frei < s.Sl) ergebnis.Zahl++;
            }
        }
        return ergebnis;
    }

    /**
     * Der laengste NACKTE STREIFEN, oder null wenn es keinen gibt.
     *
     * Ein Streifen ist ein zusammenhaengender Fleck ohne Belag und ohne Gras,
     * der laenger als 5 m und schmaler als 1 m ist - im Spiel eine Narbe.
     * Kompakte Loecher zaehlen nicht; die faengt schon das grobe Raster.
     *
     * Dieselbe Flutfuellung wie in `NenneFlecken`, nur mit Urteil statt
     * Ausgabe.
     */
    private static string LangsterSchmalerFleck(float2[] site,
        ParkingLayout layout, LayoutSettings einstellungen)
    {
        const double schritt = 0.1;
        var alle = layout.Bay.Concat(layout.GrassSurface)
            .Concat(layout.AsphaltSurface).ToList();
        var minX = site.Min(p => p.x);
        var maxX = site.Max(p => p.x);
        var minY = site.Min(p => p.y);
        var maxY = site.Max(p => p.y);
        var sp = (int)Math.Ceiling((maxX - minX) / schritt) + 1;
        var ze = (int)Math.Ceiling((maxY - minY) / schritt) + 1;
        var leer = new bool[sp, ze];
        for (var sx = 0; sx < sp; sx++)
            for (var sy = 0; sy < ze; sy++)
            {
                var pt = new float2((float)(minX + (sx + 0.5) * schritt),
                                    (float)(minY + (sy + 0.5) * schritt));
                if (!PointIn(pt, site)) continue;
                if (alle.Any(q => PointIn(pt, q))
                    || alle.Any(q => DistanceToBoundary(pt, q) <= 1e-6)) continue;
                leer[sx, sy] = true;
            }

        var gesehen = new bool[sp, ze];
        string schlimmster = null;
        var schlimmsteLaenge = 5.0;
        for (var sx = 0; sx < sp; sx++)
            for (var sy = 0; sy < ze; sy++)
            {
                if (!leer[sx, sy] || gesehen[sx, sy]) continue;
                var stapel = new System.Collections.Generic.Stack<(int, int)>();
                stapel.Push((sx, sy));
                gesehen[sx, sy] = true;
                int x0 = sx, x1 = sx, y0 = sy, y1 = sy;
                while (stapel.Count > 0)
                {
                    var (cx, cy) = stapel.Pop();
                    x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx);
                    y0 = Math.Min(y0, cy); y1 = Math.Max(y1, cy);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= sp || ny >= ze) continue;
                        if (!leer[nx, ny] || gesehen[nx, ny]) continue;
                        gesehen[nx, ny] = true;
                        stapel.Push((nx, ny));
                    }
                }
                var breite = (x1 - x0 + 1) * schritt;
                var hoehe = (y1 - y0 + 1) * schritt;
                var laenge = Math.Max(breite, hoehe);
                var dicke = Math.Min(breite, hoehe);
                if (dicke >= 1.0 || laenge <= schlimmsteLaenge) continue;
                schlimmsteLaenge = laenge;
                schlimmster = $"{breite:F2} x {hoehe:F2} m bei "
                    + $"{minX + (x0 + x1 + 1) * 0.5 * schritt:F1} / "
                    + $"{minY + (y0 + y1 + 1) * 0.5 * schritt:F1}";
            }
        return schlimmster;
    }

    /** Kleinster Abstand zweier Ringraender, in beide Richtungen gemessen. */
    private static double RingAbstand(float2[] a, float2[] b)
    {
        var minimum = double.MaxValue;
        foreach (var p in a) minimum = Math.Min(minimum, DistanceToBoundary(p, b));
        foreach (var p in b) minimum = Math.Min(minimum, DistanceToBoundary(p, a));
        return minimum;
    }

    internal static void RunPolygon(string beschreibung)
    {
        var punkte = PolygonPunkte(beschreibung);
        // Ohne das lief der Polygon-Modus stumm - die Zwischenmeldungen der
        // Bauphasen fehlten genau dort, wo ein gemeldeter Fall untersucht wird.
        ParkingGeometry.PhaseLog = true;
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        /**
         * MIT ZUFAHRT RECHNEN, WIE IM SPIEL.
         *
         * Am 2026-08-18 wichen Spiel und Nachrechnung erstmals auseinander: 22
         * gegen 3 Asphaltflaechen bei gleicher Gesamtflaeche. Ursache war nicht
         * die Geometrie, sondern die EINGABE - der Abzug des Spiels zeigte
         * "Entrances: [{Edge: 7, Along: 46,5}]", die Nachrechnung baute ohne.
         * Jeder echte Parkplatz hat mindestens eine Zufahrt; ohne sie misst der
         * Test einen Fall, den es im Spiel nicht gibt.
         */
        // Die Zufahrt laesst sich mitgeben: --polygon "..." kante,laenge
        // Ohne Angabe eine Vorgabe. Der Bericht des Nutzers und der Abzug des
        // Spiels nennen beide die echte Lage; ohne sie rechnet der Test einen
        // anderen Fall als das Spiel - am 2026-08-18 kamen so 17 statt 26
        // Grasflaechen heraus.
        einstellungen.Entrances = new[]
            { new Entrance { Edge = ZufahrtKante, Along = ZufahrtLaenge } };

        var uhr = Stopwatch.StartNew();
        var layout = ParkingGeometry.Build(punkte, einstellungen);
        uhr.Stop();

        Console.WriteLine($"Form mit {punkte.Length} Ecken, "
            + $"{FlaecheVon(punkte):F0} m2, {uhr.ElapsedMilliseconds} ms");
        Console.WriteLine($"  {layout.Stalls} Buchten, {layout.Aisles} Fahrgassen");
        Console.WriteLine();

        /**
         * WAS DER NUTZER ALS SAND SIEHT.
         *
         * Der Bericht aus dem Spiel meldet es als "Luecke 0,349 m zwischen
         * 'Grass Surface 01' und 'Grass Surface 01'": zwei gesunde
         * Grasflaechen, und dazwischen nackter Boden. Keine der beiden ist zu
         * duenn, keine ist unsichtbar - deshalb schweigen alle bisherigen
         * Zaehler.
         *
         * `CoverGap` schweigt sogar zweimal: es tastet auf einem Raster von
         * 0,5 m ab. Ein Spalt von 0,349 m liegt zwischen zwei Tastpunkten und
         * wird schlicht nicht getroffen. Deshalb hier BEIDE Rasterweiten -
         * die Differenz ist genau das Mass fuer das, was der grobe Test
         * uebersieht.
         */
        var grob = CoverGap(punkte, layout, einstellungen);
        var fein = CoverGap(punkte, layout, einstellungen, 0.1);
        Console.WriteLine("Unbedeckte Flaeche (nackter Boden):");
        Console.WriteLine($"  Raster 0,50 m: {grob.Gap,8:F1} m2  ({grob.Percent:F2} %)");
        Console.WriteLine($"  Raster 0,10 m: {fein.Gap,8:F1} m2  ({fein.Percent:F2} %)"
            + $"   <== was der grobe Test uebersieht: {fein.Gap - grob.Gap:F1} m2");

        /**
         * Der kleinste Abstand zwischen zwei VERSCHIEDENEN Flaechen gleichen
         * Materials - dieselbe Zahl, die der Bericht im Spiel nennt.
         */
        /**
         * WAS AN DER EINSPRINGENDEN ECKE WIRKLICH IM WEGENETZ LANDET.
         *
         * Der Zaehler in BuildNotchAisles sagt nur, ob die Eckgasse ANGELEGT
         * und ob sie in der Abwaegung BEHALTEN wurde. Beides kann zutreffen,
         * ohne dass am Ende eine Strasse steht: zwischen Layout und
         * Netzfassung liegen noch Kappungen, Zuschnitte und Filter. Der
         * Nutzer sagt am 2026-08-20, er sehe dort keine Fahrgasse - genau
         * diese Luecke schliesst die Ausgabe hier.
         */
        Console.WriteLine("Wegenetz an den einspringenden Ecken:");
        var eckenGefunden = 0;
        for (var i = 0; i < punkte.Length; i++)
        {
            var a = punkte[(i - 1 + punkte.Length) % punkte.Length];
            var b = punkte[i];
            var c = punkte[(i + 1) % punkte.Length];
            var kreuz = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            // Umlaufrichtung aus der Gesamtflaeche, sonst ist das Vorzeichen
            // von der Zeichenrichtung abhaengig.
            if (FlaecheVon(punkte) > 0 ? kreuz >= 0 : kreuz <= 0) continue;
            eckenGefunden++;
            Console.WriteLine($"  Ecke {i} bei {b.x:F2} / {b.y:F2}:");
            var nah = layout.NetLine
                .Select(n => (Teil: n, D: Math.Min(Abstand(n.A, b), Abstand(n.B, b))))
                .Where(x => x.D < 25)
                .OrderBy(x => x.D).ToArray();
            if (nah.Length == 0)
            {
                Console.WriteLine("    NICHTS im Umkreis von 25 m");
                continue;
            }
            foreach (var x in nah)
                Console.WriteLine($"    {x.Teil.Kind,-10} {Abstand(x.Teil.A, x.Teil.B),6:F2} m lang, "
                    + $"{x.D,5:F2} m von der Ecke   "
                    + $"({x.Teil.A.x:F1}/{x.Teil.A.y:F1} -> {x.Teil.B.x:F1}/{x.Teil.B.y:F1})");
        }
        if (eckenGefunden == 0) Console.WriteLine("  keine einspringende Ecke");
        Console.WriteLine();

        NenneSpalten("Gras", layout.GrassSurface);
        NenneSpalten("Belag", layout.AsphaltSurface);
        NenneFlecken(punkte, layout, einstellungen, 0.1);

        var naehe = EngsteQuerNaehe(layout, einstellungen);
        Console.WriteLine("Verbindungsstrassen neben parallelen Fahrbahnen:");
        if (double.IsPositiveInfinity(naehe.Frei))
            Console.WriteLine("  kein paralleles Paar");
        else
            Console.WriteLine($"  engster freier Streifen {naehe.Frei:F2} m "
                + $"(Buchttiefe {einstellungen.Sl:F2} m), "
                + $"{naehe.Zahl} Paar(e) darunter");
        if (!double.IsPositiveInfinity(naehe.Frei))
            Console.WriteLine($"    {naehe.Wo}");
        Console.WriteLine();
        /**
         * LIEGT DIE ZERSPLITTERUNG AN DEN KREUZUNGEN?
         *
         * Frage des Nutzers am 2026-08-17. Pruefbar, weil wir die Kreuzungen
         * kennen: es sind die Endpunkte der Netzfassung, an denen sich mehrere
         * Fahrwege treffen. Wenn die Vermutung stimmt, muessen die KLEINEN
         * Grasstuecke dort gehaeuft sitzen und die grossen weit weg.
         */
        var knoten = new System.Collections.Generic.List<float2>();
        foreach (var n in layout.NetLine)
            foreach (var punkt in new[] { n.A, n.B })
            {
                var treffer = 0;
                foreach (var m in layout.NetLine)
                {
                    if (Abstand(m.A, punkt) < 0.05) treffer++;
                    if (Abstand(m.B, punkt) < 0.05) treffer++;
                }
                // Erst ab drei zusammenlaufenden Enden ist es eine Kreuzung;
                // zwei sind nur eine Fortsetzung derselben Strasse.
                if (treffer >= 3 && !knoten.Any(k => Abstand(k, punkt) < 0.05))
                    knoten.Add(punkt);
            }
        Console.WriteLine($"Kreuzungen im Wegenetz: {knoten.Count}");
        foreach (var k in knoten)
            Console.WriteLine($"    Kreuzung {k.x:F2} / {k.y:F2}");
        if (knoten.Count > 0)
        {
            var mitAbstand = layout.GrassSurface
                .Select(r => (Flaeche: FlaecheVon(r),
                              Abstand: knoten.Min(k => Abstand(k, Mitte(r)))))
                .OrderBy(x => x.Flaeche).ToArray();
            var klein = mitAbstand.Where(x => x.Flaeche < 10).ToArray();
            var gross = mitAbstand.Where(x => x.Flaeche >= 50).ToArray();
            Console.WriteLine($"  Grasstuecke unter 10 m2 ({klein.Length}): "
                + $"Abstand zur naechsten Kreuzung im Median "
                + $"{(klein.Length > 0 ? klein.OrderBy(x => x.Abstand).ElementAt(klein.Length / 2).Abstand : 0):F1} m");
            Console.WriteLine($"  Grasstuecke ab 50 m2 ({gross.Length}): "
                + $"Median {(gross.Length > 0 ? gross.OrderBy(x => x.Abstand).ElementAt(gross.Length / 2).Abstand : 0):F1} m");
            Console.WriteLine($"  unter 10 m2 UND naeher als 12 m an einer Kreuzung: "
                + $"{klein.Count(x => x.Abstand < 12)} von {klein.Length}");
        }
        /**
         * WIE VIELE DER RINGE BERUEHREN EINANDER?
         *
         * Der Nutzer stellte am 2026-08-17 fest, dass sogar Mittelstreifen und
         * ihre Kappen getrennt bleiben - und die sitzen unmittelbar
         * aneinander. Wenn das stimmt, muessten sich die 105 Grasringe zu ganz
         * wenigen zusammenhaengenden GRUPPEN zusammenfassen lassen.
         *
         * Gezaehlt wird eine Beruehrung, wenn zwei Ringe irgendwo ein Stueck
         * gemeinsame Kante haben - auch wenn die Punkte nicht zusammenfallen.
         * Genau dieser Fall (Punkt MITTEN auf der Nachbarkante) ist der
         * Verdacht, warum die vorhandene Verschmelzung sie nicht erkennt.
         */
        Console.WriteLine();
        BeruehrungsBericht("Gras", layout.GrassSurface);
        BeruehrungsBericht("Belag", layout.AsphaltSurface);
        Console.WriteLine();
        foreach (var (name, ringe) in new[]
                 { ("Gras", layout.GrassSurface), ("Belag", layout.AsphaltSurface) })
        {
            if (ringe.Length == 0) { Console.WriteLine($"{name}: keine"); continue; }
            var flaechen = ringe.Select(FlaecheVon).OrderBy(x => x).ToArray();
            Console.WriteLine($"{name}: {ringe.Length} Ringe, zusammen "
                + $"{flaechen.Sum():F0} m2");
            Console.WriteLine($"  Flaeche  min {flaechen.First():F2} | "
                + $"Median {flaechen[flaechen.Length / 2]:F2} | "
                + $"max {flaechen.Last():F0} m2");
            Console.WriteLine($"  Punkte   min {ringe.Min(r => r.Length)} | "
                + $"max {ringe.Max(r => r.Length)}");
            // WIE VIELE SIND WINZIG? Das ist die Frage des Nutzers in Zahlen:
            // besteht das Gruen aus wenigen grossen Flaechen oder aus Schnipseln?
            Console.WriteLine($"  davon unter  10 m2: {flaechen.Count(x => x < 10)}"
                + $" | unter 50 m2: {flaechen.Count(x => x < 50)}"
                + $" | ueber 200 m2: {flaechen.Count(x => x >= 200)}");
            StreifenProfil(ringe);
            // DICKE ALLER Ringe, nicht nur der winzigen. Der Nutzer zeigte am
            // 2026-08-18 einen Gruenstreifen, der in duenne Baender zerfallen
            // ist - die Baender sind gross genug, um durch jede Flaechenpruefung
            // zu rutschen, aber zu duenn, um als eine Flaeche zu wirken.
            var dicken = ringe.Where(r => r.Length >= 3)
                .Select(SchmalsteBreiteVon).OrderBy(x => x).ToArray();
            if (dicken.Length > 0)
                Console.WriteLine($"  Dicke aller Ringe: min {dicken.First():F2} | "
                    + $"Median {dicken[dicken.Length / 2]:F2} | max {dicken.Last():F2} m, "
                    + $"{dicken.Count(d => d < 1.0)} duenner als 1 m, "
                    + $"{dicken.Count(d => d < 0.375)} unter CS2s Mindestkante");
        }
    }

    /**
     * SIND DIE KLEINEN TEILE STREIFEN - UND ALLE GLEICH GEDREHT?
     *
     * Der Nutzer beschrieb am 2026-08-17, was er im Spiel sieht: "die Streifen
     * scheinen alle den gleichen Winkel zu haben, sind aber unterschiedlich
     * dick." Das ist eine pruefbare Aussage und die genaueste Spur zur Quelle,
     * denn eine Streifenzerlegung erzeugt genau das - parallele Baender in der
     * Schnittrichtung, deren Breite sich aus der Form ergibt.
     *
     * Gemessen wird je Ring die schmalste Ausdehnung (rotierende Schieblehre
     * ueber alle Kantenrichtungen) und die Richtung, in der er lang ist. Die
     * Richtung wird auf 0..180 Grad gefaltet, weil ein Streifen keine
     * Vorzugsrichtung hat.
     */
    /** Schmalste Ausdehnung eines Rings ueber alle Kantenrichtungen. */
    private static double SchmalsteBreiteVon(float2[] ring)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            double dx = b.x - a.x, dy = b.y - a.y;
            var l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-9) continue;
            dx /= l; dy /= l;
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var q in ring)
            {
                var quer = -q.x * dy + q.y * dx;
                lo = Math.Min(lo, quer);
                hi = Math.Max(hi, quer);
            }
            best = Math.Min(best, hi - lo);
        }
        return double.IsInfinity(best) ? 0 : best;
    }

    private static void StreifenProfil(float2[][] ringe)
    {
        var schmal = new System.Collections.Generic.List<(double Dicke, double Winkel, double Flaeche)>();
        foreach (var ring in ringe)
        {
            if (ring.Length < 3) continue;
            var flaeche = FlaecheVon(ring);
            if (flaeche >= 10) continue;

            var besteDicke = double.PositiveInfinity;
            var besterWinkel = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                double dx = b.x - a.x;
                double dy = b.y - a.y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                dx /= len; dy /= len;
                // Ausdehnung quer zu dieser Kante.
                var min = double.PositiveInfinity;
                var max = double.NegativeInfinity;
                foreach (var q in ring)
                {
                    var quer = -q.x * dy + q.y * dx;
                    min = Math.Min(min, quer);
                    max = Math.Max(max, quer);
                }
                var dicke = max - min;
                if (dicke >= besteDicke) continue;
                besteDicke = dicke;
                besterWinkel = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            }
            if (double.IsInfinity(besteDicke)) continue;
            var gefaltet = ((besterWinkel % 180) + 180) % 180;
            schmal.Add((besteDicke, gefaltet, flaeche));
        }
        if (schmal.Count == 0) return;

        var dicken = schmal.Select(s => s.Dicke).OrderBy(x => x).ToArray();
        Console.WriteLine($"  Streifenprofil der {schmal.Count} Teile unter 10 m2: "
            + $"Dicke min {dicken.First():F3} | Median "
            + $"{dicken[dicken.Length / 2]:F3} | max {dicken.Last():F2} m, "
            + $"{dicken.Count(d => d < 0.375):D} unter CS2s Mindestkante");
        // Winkel in 10-Grad-Faechern: liegt alles in einem, sind es Baender
        // aus EINER Schnittrichtung und keine zufaellig verteilten Reste.
        var faecher = schmal.GroupBy(s => (int)(s.Winkel / 10))
                            .OrderByDescending(g => g.Count())
                            .Take(4);
        Console.WriteLine("  Laengsrichtung: "
            + string.Join(" | ", faecher.Select(g =>
                $"{g.Key * 10}-{g.Key * 10 + 10} Grad: {g.Count()}")));
    }

    /** Siehe die Begruendung an der Aufrufstelle. */
    private static void BeruehrungsBericht(string name, float2[][] ringe)
    {
        if (ringe.Length == 0) { Console.WriteLine($"{name}: keine Ringe"); return; }
        // Beruehren sich zwei Ringe? Ein Punkt des einen liegt auf einer Kante
        // des anderen, und das gleich mehrfach - dann laufen sie ein Stueck
        // gemeinsam.
        bool Beruehrt(float2[] a, float2[] b)
        {
            var treffer = 0;
            foreach (var p in a)
                foreach (var (q, r) in Kanten(b))
                    if (AbstandZuStrecke(p, q, r) < 0.05) { treffer++; break; }
            foreach (var p in b)
                foreach (var (q, r) in Kanten(a))
                    if (AbstandZuStrecke(p, q, r) < 0.05) { treffer++; break; }
            return treffer >= 2;
        }
        var eltern = Enumerable.Range(0, ringe.Length).ToArray();
        int Wurzel(int i) { while (eltern[i] != i) { eltern[i] = eltern[eltern[i]]; i = eltern[i]; } return i; }
        var paare = 0;
        for (var i = 0; i < ringe.Length; i++)
            for (var j = i + 1; j < ringe.Length; j++)
                if (Beruehrt(ringe[i], ringe[j]))
                {
                    paare++;
                    var a = Wurzel(i);
                    var b = Wurzel(j);
                    if (a != b) eltern[b] = a;
                }
        var gruppen = Enumerable.Range(0, ringe.Length).Select(Wurzel).Distinct().Count();
        Console.WriteLine($"{name}: {ringe.Length} Ringe, {paare} beruehrende Paare, "
            + $"{gruppen} zusammenhaengende Gruppen");
        if (gruppen < ringe.Length)
            Console.WriteLine($"    -> waeren alle Beruehrenden EINE Flaeche, blieben "
                + $"{gruppen} statt {ringe.Length}");
    }

    private static System.Collections.Generic.IEnumerable<(float2, float2)> Kanten(float2[] ring)
    {
        for (var i = 0; i < ring.Length; i++) yield return (ring[i], ring[(i + 1) % ring.Length]);
    }

    private static double AbstandZuStrecke(float2 p, float2 a, float2 b)
    {
        double vx = b.x - a.x, vy = b.y - a.y, l2 = vx * vx + vy * vy;
        if (l2 < 1e-12) return Abstand(p, a);
        var t = Math.Max(0, Math.Min(1, ((p.x - a.x) * vx + (p.y - a.y) * vy) / l2));
        return Math.Sqrt(Math.Pow(p.x - (a.x + t * vx), 2) + Math.Pow(p.y - (a.y + t * vy), 2));
    }

    private static double Abstand(float2 a, float2 b)
        => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    private static float2 Mitte(float2[] ring)
    {
        float x = 0, y = 0;
        foreach (var p in ring) { x += p.x; y += p.y; }
        return new float2(x / ring.Length, y / ring.Length);
    }

    internal static void RunSchwarm(int anzahl)
    {
        _schwarmSeed = 12345;
        ParkingGeometry.PhaseLog = true;
        // Genau die Einstellungen, die das UI setzt.
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;

        var buchten = new System.Collections.Generic.List<int>();
        var zeiten = new System.Collections.Generic.List<long>();
        var dichten = new System.Collections.Generic.List<double>();
        int entarteteFaelle = 0, langsam = 0, leer = 0, fehler = 0;
        var schlimmste = new System.Collections.Generic.List<(int Index, int Ringe, double Flaeche, int Buchten)>();
        var langsamste = new System.Collections.Generic.List<(int Index, long Ms, double Flaeche, int Buchten)>();
        var verlust = new System.Collections.Generic.List<(int Index, double Weg, double Anteil, double Groesster)>();
        var restProfile = new System.Collections.Generic.List<(int Punkte, double Flaeche, double Kante, bool Gras)>();

        Console.WriteLine($"FORMEN-SCHWARM (C#, Modus edge wie im Spiel): {anzahl} Areale");
        Console.WriteLine();
        for (var i = 0; i < anzahl; i++)
        {
            var site = SchwarmForm(i);
            var flaeche = FlaecheVon(site);
            // Fortschritt VOR dem Bau ausgeben und sofort schreiben. Ohne das
            // sieht man bei einer Form, die nicht terminiert, nur Stille - und
            // genau so eine hat der erste Lauf am 2026-08-17 erwischt.
            // Das Polygon MITSCHREIBEN. Ohne das laesst sich eine haengende
            // Form nicht nachbauen: die Zufallsfolge des Prototyps weicht ab,
            // weil JS bei `seed * 1103515245` Genauigkeit verliert und C# in
            // `long` exakt rechnet. Form 21 des C#-Laufs ist NICHT Form 21 des
            // JS-Laufs.
            var punkte = string.Join(",", site.Select(p2 =>
                $"[{p2.x.ToString("F4", CultureInfo.InvariantCulture)},"
                + $"{p2.y.ToString("F4", CultureInfo.InvariantCulture)}]"));
            Console.Write($"  Form {i,3} ({flaeche,7:F0} m2, {site.Length} Ecken) "
                + $"[{punkte}] ... ");
            Console.Out.Flush();
            ParkingLayout layout;
            var uhr = Stopwatch.StartNew();
            try { layout = ParkingGeometry.Build(site, einstellungen); }
            catch (Exception e)
            {
                fehler++;
                Console.WriteLine($"  Form {i}: FEHLER {e.Message}");
                continue;
            }
            uhr.Stop();
            Console.WriteLine($"{uhr.ElapsedMilliseconds,6} ms, {layout.Stalls,4} Buchten");
            // NICHT nur zaehlen, sondern WIEGEN. Der Nutzer stellte am
            // 2026-08-17 richtig: es fehlen keine Kruemel, sondern GROSSE
            // Flaechen. CS2 verwirft den ganzen Ring, sobald EINE Kante unter
            // 0,375 m liegt - bei ihm ein 4.545 m2 grosser Asphaltring mit
            // einer 6-mm-Kante. Eine reine Anzahl verdeckt genau das.
            var gefaehrdet = layout.GrassSurface.Concat(layout.AsphaltSurface)
                .Where(r => KuerzesteKanteVon(r) < 0.375).ToArray();
            var ringe = gefaehrdet.Length;
            var flaecheWeg = gefaehrdet.Sum(FlaecheVon);
            var flaecheGesamt = layout.GrassSurface.Concat(layout.AsphaltSurface)
                .Sum(FlaecheVon);
            var groessterWeg = gefaehrdet.Length == 0 ? 0 : gefaehrdet.Max(FlaecheVon);
            // WIE SEHEN DIE RESTLICHEN AUS? Nach dem Entfernen der
            // Haarrisskanten bleiben 53 % der Formen mit Restringen unter der
            // Mindestkante - aber winzig. Fuer den Fix muss man wissen, ob es
            // Dreiecke sind (nicht weiter reduzierbar), wie kurz ihre kuerzeste
            // Kante ist und ob Gras oder Belag.
            foreach (var r in gefaehrdet)
            {
                var istGras = layout.GrassSurface.Contains(r);
                restProfile.Add((r.Length, FlaecheVon(r), KuerzesteKanteVon(r), istGras));
            }
            buchten.Add(layout.Stalls);
            zeiten.Add(uhr.ElapsedMilliseconds);
            dichten.Add(layout.Stalls / (flaeche / 1000));
            if (ringe > 0)
            {
                entarteteFaelle++;
                schlimmste.Add((i, ringe, flaeche, layout.Stalls));
                verlust.Add((i, flaecheWeg, 100.0 * flaecheWeg / Math.Max(flaecheGesamt, 1), groessterWeg));
            }
            if (uhr.ElapsedMilliseconds > 2000) { langsam++; langsamste.Add((i, uhr.ElapsedMilliseconds, flaeche, layout.Stalls)); }
            if (layout.Stalls == 0) leer++;
        }

        double Median(System.Collections.Generic.IEnumerable<double> xs)
        {
            var a = xs.OrderBy(x => x).ToArray();
            return a.Length == 0 ? 0 : a[a.Length / 2];
        }

        var gut = anzahl - fehler;
        Console.WriteLine($"  Abstuerze/Fehler          {fehler}");
        Console.WriteLine($"  Median Buchten            {Median(buchten.Select(x => (double)x)):F0}");
        Console.WriteLine($"  Median Dichte             {Median(dichten):F1} Buchten je 1000 m2");
        Console.WriteLine($"  Median Bauzeit            {Median(zeiten.Select(x => (double)x)):F0} ms");
        Console.WriteLine();
        Console.WriteLine("MAENGEL, nach Haeufigkeit:");
        void Zeile(int n, string t) => Console.WriteLine(
            $"  {n,3} von {gut}  ({100.0 * n / Math.Max(gut, 1),3:F0} %)  {t}");
        Zeile(entarteteFaelle, "Flaechen unter CS2s Mindestkante 0,375 m -> Luecken im Belag");
        Zeile(langsam, "Bauzeit ueber 2 s");
        Zeile(leer, "gar keine Bucht");
        Console.WriteLine();
        if (verlust.Count > 0)
        {
            Console.WriteLine($"  Flaeche, die CS2 verwirft: im Mittel "
                + $"{Median(verlust.Select(x => x.Anteil)):F1} % der Materialflaeche, "
                + $"schlimmstenfalls {verlust.Max(x => x.Anteil):F1} %");
            Console.WriteLine($"  Groesster einzelner verworfener Ring: "
                + $"{verlust.Max(x => x.Groesster):F0} m2");
            Console.WriteLine();
        }
        if (restProfile.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"RESTRINGE unter 0,375 m: {restProfile.Count} Stueck");
            Console.WriteLine($"  Gras {restProfile.Count(x => x.Gras)} | "
                + $"Belag {restProfile.Count(x => !x.Gras)}");
            Console.WriteLine($"  Punktzahl: min {restProfile.Min(x => x.Punkte)}, "
                + $"Median {restProfile.OrderBy(x => x.Punkte).ElementAt(restProfile.Count / 2).Punkte}, "
                + $"max {restProfile.Max(x => x.Punkte)}");
            Console.WriteLine($"  davon Dreiecke (3 Punkte): "
                + $"{restProfile.Count(x => x.Punkte == 3)}");
            Console.WriteLine($"  Flaeche: min {restProfile.Min(x => x.Flaeche):F3} m2, "
                + $"Median {restProfile.OrderBy(x => x.Flaeche).ElementAt(restProfile.Count / 2).Flaeche:F3} m2, "
                + $"max {restProfile.Max(x => x.Flaeche):F1} m2");
            Console.WriteLine($"  kuerzeste Kante: min {restProfile.Min(x => x.Kante):F4} m, "
                + $"Median {restProfile.OrderBy(x => x.Kante).ElementAt(restProfile.Count / 2).Kante:F4} m");
        }
        ParkingGeometry.MeldeSplitterBilanz();
        ParkingGeometry.MeldeMaterialZeiten();
        Console.WriteLine("SCHLIMMSTE FAELLE:");
        foreach (var e in schlimmste.OrderByDescending(x => x.Ringe).Take(4))
            Console.WriteLine($"     entartet: Form {e.Index,3} ({e.Flaeche,7:F0} m2, "
                + $"{e.Buchten,4} Buchten): {e.Ringe} Ringe");
        foreach (var e in langsamste.OrderByDescending(x => x.Ms).Take(4))
            Console.WriteLine($"     langsam:  Form {e.Index,3} ({e.Flaeche,7:F0} m2, "
                + $"{e.Buchten,4} Buchten): {e.Ms} ms");
    }
}
