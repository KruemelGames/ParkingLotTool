using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * DIE GEMELDETE FORM VOM 2026-09-01, 04:49:58 (`PLT-224A4F51`).
     *
     * Der Nutzer: *"da laeuft einiges falsch beim Verlegen der Parkbuchten
     * und der Strassen und der Flaechen."* Der Umriss steht im Bauprotokoll,
     * die beiden Reihenwinkel (169,0 und 133,0 Grad) im Live-Log.
     *
     * WAS FEHLT, und das ist der eigentliche Befund dieses Laufs: WO der
     * Trennschnitt lag, weiss niemand. Das Bauprotokoll schreibt ihn nicht
     * mit. Deshalb probiert dieser Lauf JEDEN moeglichen Schnitt durch - bei
     * sechs Ecken sind das neun - und misst jeden. Einer davon ist der des
     * Nutzers; wenn alle neun dieselbe Art von Fehler zeigen, ist es egal,
     * welcher.
     */
    /**
     * DIE GEBAUTE FORM VOM 2026-09-01, 10:36 (`PLT-6C6C0FE2`).
     *
     * Der Nutzer hat sie im Spiel angesehen: *"das war Teilflaeche 1, wo
     * enorm viel schief laeuft. Teilflaeche 2 sah in Ordnung aus. Decals,
     * die nicht gesetzt werden oder gar nicht erst berechnet werden. Fehler
     * beim richtigen Berechnen der Platzierung der Surfaces."*
     *
     * Eine Haelfte kaputt, die andere heil - das ist der schaerfste Hinweis,
     * den es bisher gab. Dieser Lauf zaehlt deshalb ALLES je Teilflaeche:
     * Buchten, Aufkleber, Gras- und Belagflaechen. Wo die Zahlen
     * auseinanderlaufen, sitzt der Fehler.
     */
    private static void RunGebauteForm()
    {
        /*
         * PLT-66E7436D, gebaut am 2026-09-01 um 11:03 - Umriss aus dem
         * Bauprotokoll, Winkel aus dem Live-Log: Teil 1 = 168,7 Grad,
         * Teil 0 = 144,6 Grad, EIN Handschnitt, 355 Buchten im Spiel.
         * Welcher der sieben moeglichen Schnitte es war, sagt die
         * Buchtenzahl: nur einer trifft die 355.
         */
        var site = new[]
        {
            new float2(-1031.5846f, 496.5344f),
            new float2(-1116.6327f, 513.4900f),
            new float2(-1181.6761f, 559.7708f),
            new float2(-1170.2239f, 644.4089f),
            new float2(-1116.7362f, 624.0984f),
            new float2(-1029.1805f, 610.1551f),
        };
        var doppel = site.Select(p => new double2(p.x, p.y)).ToArray();

        var settings = LayoutSettings.Cs2;
        settings.Zellen = true;
        settings.AngleMode = "edge";
        settings.Auto = false;
        settings.AutomaticEntrances = false;
        settings.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        // Beide Winkel aus dem Bauprotokoll: der gemeldete Reihenwinkel war
        // 178,79 Grad; der zweite ist unbekannt, deshalb wird jeder moegliche
        // Schnitt mit einem deutlich anderen Winkel durchprobiert.
        settings.Ausrichtwinkel = 168.7;

        Console.WriteLine("Gebaute Form PLT-6C6C0FE2, 6 Ecken, "
            + "355 Buchten im Spiel, Winkel 144,6 und 168,7 Grad");
        Console.WriteLine();

        for (var i = 0; i < site.Length; i++)
            for (var j = i + 2; j < site.Length; j++)
            {
                if (i == 0 && j == site.Length - 1) continue;
                if (!ParkingGeometry.SchnittLiegtInnen(doppel, i, j)) continue;
                var schnitt = new Teilflaechenschnitt { A = site[i], B = site[j] };
                var teile = ParkingGeometry.TeilflaechenAusSchnitten(
                    doppel, new[] { schnitt });
                if (teile.Length != 2) continue;

                float2 Anker(double2[] teil)
                {
                    var summe = double2.zero;
                    foreach (var p in teil) summe += p;
                    var m = summe / teil.Length;
                    return new float2((float)m.x, (float)m.y);
                }

                var s2 = settings;
                s2.Teilflaechenschnitte = new[] { schnitt };
                s2.TeilflaechenAusrichtungen = new[]
                {
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[0]), Winkel = 144.6 },
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[1]), Winkel = 168.7 },
                };
                ParkingLayout layout;
                try { layout = ParkingGeometry.Build(site, s2); }
                catch (Exception fehler)
                {
                    Console.WriteLine($"Schnitt {i}-{j}: ABSTURZ {fehler.Message}");
                    continue;
                }

                var decals = ParkingBayDecals.Plan(layout, s2);
                Console.WriteLine($"Schnitt {i}-{j}: {layout.Stalls} Buchten, "
                    + $"{decals.Placements.Length} Decals für {decals.Stalls} "
                    + $"Buchten, nicht erkannt {decals.Unrecognized}, "
                    + $"Gras {layout.GrassSurface.Length}, "
                    + $"Asphalt {layout.AsphaltSurface.Length}");

                for (var t = 0; t < teile.Length; t++)
                {
                    var ring = teile[t]
                        .Select(pt => new float2((float)pt.x, (float)pt.y))
                        .ToArray();
                    var buchten = 0;
                    for (var b = 0; b < layout.Bay.Length; b++)
                    {
                        var m = float2.zero;
                        foreach (var pt in layout.Bay[b]) m += pt;
                        if (PointInRing(m / layout.Bay[b].Length, ring)) buchten++;
                    }
                    var aufkleber = decals.Placements.Count(d => PointInRing(
                        new float2((float)d.Center.x, (float)d.Center.y), ring));
                    var gras = layout.GrassSurface.Count(r =>
                        PointInRing(Mittelwert(r), ring));
                    var asphalt = layout.AsphaltSurface.Count(r =>
                        PointInRing(Mittelwert(r), ring));
                    Console.WriteLine($"   Teil {t}: {buchten,3} Buchten, "
                        + $"{aufkleber,3} Decals, {gras,2} Gras, "
                        + $"{asphalt,2} Asphalt, "
                        + $"{Flaeche(teile[t]),6:F0} m2");
                }
            }
    }

    private static float2 Mittelwert(float2[] ring)
    {
        var summe = float2.zero;
        foreach (var p in ring) summe += p;
        return summe / ring.Length;
    }

    private static void RunNutzerform()
    {
        var site = new[]
        {
            new float2(-1034.4370f, 489.0600f),
            new float2(-1133.7561f, 508.4530f),
            new float2(-1191.8345f, 570.7349f),
            new float2(-1168.1779f, 640.6107f),
            new float2(-1100.7192f, 610.9385f),
            new float2(-1029.3848f, 600.5112f),
        };

        LayoutSettings Grund()
        {
            var s = LayoutSettings.Cs2;
            s.Zellen = true;
            s.AngleMode = "edge";
            s.Auto = false;
            s.AutomaticEntrances = false;
            s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
            return s;
        }

        void Messe(string name, LayoutSettings settings,
            double2[][] teilPolygone = null)
        {
            ParkingLayout layout;
            try { layout = ParkingGeometry.Build(site, settings); }
            catch (Exception fehler)
            {
                Console.WriteLine($"{name,-22} ABSTURZ: {fehler.Message}");
                return;
            }
            var ungedeckt = CoverGap(site, layout, settings);
            var strasse = MeasureRoadConflict(layout);
            var material = MeasureMaterialConflict(layout);
            var schnitte = layout.GrassSurface.Sum(SelfIntersectionCount)
                + layout.AsphaltSurface.Sum(SelfIntersectionCount);
            var doppelt = layout.GrassSurface.Count(HasNearDuplicatePoints)
                + layout.AsphaltSurface.Count(HasNearDuplicatePoints);
            /*
             * WO FEHLEN DIE BUCHTEN? Der Nutzer sieht leere Stellen, die
             * Kennzahlen melden nichts. Also aufteilen: Randreihe gegen
             * Innenreihen, und die Innenreihen je Teilflaeche - samt der
             * FLAECHE des Teils, denn nur "Buchten je Hektar" sagt, ob ein
             * Teil leer geblieben ist oder einfach klein war.
             */
            /*
             * SELBST ZAEHLEN, NACH LAGE.
             *
             * `TeilflaechenBauInfo.Innenbuchten` meldete 225 Buchten auf
             * 2708 m2 - das waeren 3982 m2 Buchtflaeche in einem Teil von
             * 2708 m2. Die Zahl misst also etwas anderes als "Buchten in
             * diesem Teil". Hier wird geometrisch gezaehlt: Schwerpunkt der
             * Bucht im Teilpolygon.
             */
            var teilText = string.Empty;
            if (teilPolygone != null)
            {
                var texte = new List<string>();
                for (var t = 0; t < teilPolygone.Length; t++)
                {
                    var ring = teilPolygone[t]
                        .Select(pt => new float2((float)pt.x, (float)pt.y))
                        .ToArray();
                    var innen = 0;
                    var rand = 0;
                    for (var b = 0; b < layout.Bay.Length; b++)
                    {
                        var mitte = float2.zero;
                        foreach (var pt in layout.Bay[b]) mitte += pt;
                        mitte /= layout.Bay[b].Length;
                        if (!PointInRing(mitte, ring)) continue;
                        if (layout.BayKind[b] == BayKind.Inner) innen++;
                        else rand++;
                    }
                    var flaeche = Flaeche(teilPolygone[t]);
                    var winkel = layout.Teilflaechen
                        .FirstOrDefault(x => x.Index == t)?.Winkel ?? double.NaN;
                    texte.Add($"[{t}] {winkel,6:F1} Grad "
                        + $"{innen,3} innen + {rand,3} Rand auf {flaeche,6:F0} m2 "
                        + $"= {(flaeche <= 0 ? 0 : innen / flaeche * 10000),4:F0}/ha innen");
                }
                teilText = string.Join("   ", texte);
            }
            Console.WriteLine($"{name,-22} {layout.Stalls,4} Buchten "
                + $"(Rand {layout.PerimeterStalls,3}, innen {layout.InnerStalls,3}) | "
                + $"unerreichbar {UnservedBays(layout),3} | "
                + $"Gassen {layout.Aisles,2} | "
                + $"ungedeckt {ungedeckt.Percent,5:F1} % | "
                + $"Str/Str {strasse.Total,6:F2} | "
                + $"Gras {layout.GrassSurface.Length} Asph {layout.AsphaltSurface.Length}");
            if (teilText.Length != 0)
                Console.WriteLine($"{string.Empty,-22} {teilText}");
        }

        Console.WriteLine("Gemeldete Form PLT-224A4F51, 6 Ecken, "
            + "Reihenwinkel 169,0 und 133,0 Grad");
        Console.WriteLine();

        var ohne = Grund();
        Messe("ohne Ausrichtung", ohne);

        var einWinkel = Grund();
        einWinkel.Ausrichtwinkel = 169.0;
        Messe("nur 169 Grad", einWinkel);

        var nurZweiter = Grund();
        nurZweiter.Ausrichtwinkel = 133.0;
        Messe("nur 133 Grad", nurZweiter);
        Console.WriteLine();

        // Jeder moegliche Handschnitt einer Sechsecks: zwei Ecken, nicht
        // benachbart. Beide Teile bekommen je einen der gemeldeten Winkel.
        for (var i = 0; i < site.Length; i++)
            for (var j = i + 2; j < site.Length; j++)
            {
                if (i == 0 && j == site.Length - 1) continue;
                var doppel = site.Select(p => new double2(p.x, p.y)).ToArray();
                if (!ParkingGeometry.SchnittLiegtInnen(doppel, i, j))
                {
                    Console.WriteLine($"Schnitt {i}-{j,-14} liegt aussen");
                    continue;
                }
                var schnitt = new Teilflaechenschnitt { A = site[i], B = site[j] };
                var teile = ParkingGeometry.TeilflaechenAusSchnitten(
                    doppel, new[] { schnitt });
                if (teile.Length != 2)
                {
                    Console.WriteLine($"Schnitt {i}-{j,-14} ergibt "
                        + teile.Length + " Teile");
                    continue;
                }
                float2 Anker(double2[] teil)
                {
                    var summe = double2.zero;
                    foreach (var p in teil) summe += p;
                    var m = summe / teil.Length;
                    return new float2((float)m.x, (float)m.y);
                }

                /*
                 * LIEGT DER ANKER UEBERHAUPT IN SEINEM TEIL?
                 *
                 * Der Anker ist bis heute der Schwerpunkt der ECKEN. Der
                 * liegt bei einem KONVEXEN Teil immer innen - und konvex
                 * waren die Teile, solange die automatische Zerlegung sie
                 * machte. Ein Handschnitt darf konkave Teile erzeugen; dann
                 * kann der Schwerpunkt ausserhalb liegen, und die Zuweisung
                 * haengt sich ans falsche Teil oder an gar keines.
                 */
                for (var t = 0; t < teile.Length; t++)
                {
                    /*
                     * KONVEX ODER NICHT? Der Verdacht: `Teilflaechenlayout`
                     * schneidet den Innenrand je Teil mit
                     * `SchneideMitKonvexemPolygon` - einem Verfahren, das
                     * gegen jede Kante als HALBEBENE schneidet. Fuer ein
                     * konvexes Teil ist das exakt; fuer ein konkaves
                     * schneidet es viel zu viel weg. Die automatische
                     * Zerlegung lieferte immer konvexe Teile - der
                     * Handschnitt darf konkave liefern.
                     */
                    Console.WriteLine($"   Teil {t}: "
                        + (IstKonvex(teile[t]) ? "konvex" : "KONKAV")
                        + $", {teile[t].Length} Ecken, "
                        + $"{Flaeche(teile[t]):F0} m2");
                    var anker = Anker(teile[t]);
                    var drin = PointInRing(anker, teile[t]
                        .Select(p => new float2((float)p.x, (float)p.y)).ToArray());
                    if (!drin)
                        Console.WriteLine($"   ANKER AUSSERHALB: Schnitt {i}-{j}, "
                            + $"Teil {t} ({teile[t].Length} Ecken)");
                }

                var settings = Grund();
                settings.Ausrichtwinkel = 169.0;
                settings.Teilflaechenschnitte = new[] { schnitt };
                settings.TeilflaechenAusrichtungen = new[]
                {
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[0]), Winkel = 169.0 },
                    new TeilflaechenAusrichtung
                        { Anker = Anker(teile[1]), Winkel = 133.0 },
                };
                Messe($"Schnitt {i}-{j}", settings, teile);

                /*
                 * DER VORSCHLAG DES NUTZERS: jedes Teilstueck als EIGENER
                 * Parkplatz. Dann bekommt jedes Teil seinen eigenen Rand,
                 * seine eigene Randstrasse und - das ist der Punkt - seine
                 * eigene RANDREIHE rund herum, auch entlang des Schnitts.
                 * Die Randreihe folgt der Form genau, statt sich einem
                 * rechteckigen Raster zu beugen.
                 */
                var summe = 0;
                var summeRand = 0;
                var summeInnen = 0;
                var kaputt = false;
                for (var t = 0; t < teile.Length && !kaputt; t++)
                {
                    var eigen = Grund();
                    eigen.Ausrichtwinkel = t == 0 ? 169.0 : 133.0;
                    var polygon = teile[t]
                        .Select(pt => new float2((float)pt.x, (float)pt.y))
                        .ToArray();
                    try
                    {
                        var eigenes = ParkingGeometry.Build(polygon, eigen);
                        summe += eigenes.Stalls;
                        summeRand += eigenes.PerimeterStalls;
                        summeInnen += eigenes.InnerStalls;
                    }
                    catch (Exception fehler)
                    {
                        Console.WriteLine($"   je Teil eigener Parkplatz: "
                            + $"Teil {t} ABSTURZ: {fehler.Message}");
                        kaputt = true;
                    }
                }
                if (!kaputt)
                    Console.WriteLine($"   je Teil eigener Parkplatz: "
                        + $"{summe} Buchten (Rand {summeRand}, "
                        + $"innen {summeInnen})");
            }
    }
}
