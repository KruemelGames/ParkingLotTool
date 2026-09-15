using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Wir schicken CS2 nichts, was es nachweislich ablehnt.
     *
     * WARUM DAS EIN LAUF WERT IST. Ein abgelehnter Ring war nie bloss eine
     * Luecke im Belag. Die Flaeche wird nie zur Entity, und der Bau verlangt
     * in `AreaTransferMaterialized` `ready >= gesamt` - EIN Ring hat deshalb
     * den GANZEN Parkplatz verhindert. Der Nutzer sah am 2026-09-08 nur
     * „Nur 137 von 138 Flaechen wurden zu Entities" und konnte nicht bauen.
     *
     * `Cs2Triangulierung` bildet CS2s Ear-Clipping nach dem 0,1-m-Innenversatz
     * bitgenau nach (gegen fuenf Ingame-Messungen gegengeprueft, siehe
     * `--triangulierung`). Was sie mit null Dreiecken beantwortet, darf das
     * Layout nicht ausliefern.
     */
    private static int RunFlaechenannahme()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        foreach (var fall in Flaechenfaelle())
        {
            ParkingLayout layout;
            try
            {
                layout = ParkingGeometry.Build(fall.Umriss, fall.Einstellungen);
            }
            catch (Exception ausnahme)
            {
                Pruefe(false, fall.Name + ": " + ausnahme.Message);
                continue;
            }

            var abgelehnt = ZaehleAbgelehnt(layout.GrassSurface)
                + ZaehleAbgelehnt(layout.AsphaltSurface);
            var ringe = (layout.GrassSurface?.Length ?? 0)
                + (layout.AsphaltSurface?.Length ?? 0);
            var weggelassen = layout.Warnings
                .FirstOrDefault(w => w.Contains("left out"));

            Console.WriteLine($"{fall.Name}: {layout.Stalls} Buchten, "
                + $"{ringe} Ringe, {abgelehnt} wuerde CS2 ablehnen"
                + (weggelassen == null ? "" : "; " + weggelassen));

            // Echte Kantenlaengen statt Welt-Bounding-Box: bei schraegen
            // Reihen misst die Box die Schraeglage mit. Der Nutzerfall hatte
            // 14 Halbbänder à 3,5 m; gefordert sind sieben ganze à 7 m.
            var baender = (layout.AsphaltSurface ?? new float2[0][])
                    .Where(r => r != null && r.Length == 4)
                    .Select(r => new {
                        Breite = Enumerable.Range(0, 4).Select(i =>
                            (double)Unity.Mathematics.math.distance(r[i], r[(i + 1) % 4])).Min(),
                        Laenge = Enumerable.Range(0, 4).Select(i =>
                            (double)Unity.Mathematics.math.distance(r[i], r[(i + 1) % 4])).Max() })
                    .Where(g => g.Laenge > 40).ToArray();
            var erwartet = fall.GanzeFahrgassenbaender;
            var ganze = baender.Count(b => Math.Abs(b.Breite - fall.Einstellungen.Ai) < 0.001);
            var halbe = baender.Count(b => Math.Abs(b.Breite - fall.Einstellungen.Ai / 2) < 0.001);
            Pruefe(ganze == erwartet && halbe == 0,
                $"{fall.Name}: Fahrgassenbaender {ganze} ganz, {halbe} halb; erwartet {erwartet} ganz, 0 halb");

            if (Environment.GetEnvironmentVariable("PLT_BREIT") == "1")
            {
                var gruppen = baender
                    .GroupBy(g => Math.Round(g.Breite, 1))
                    .OrderBy(g => g.Key);
                foreach (var g in gruppen)
                    Console.WriteLine($"    lange Belagbaender {g.Key,5:F1} m breit: "
                        + $"{g.Count()} Stueck");
                foreach (var material in new[] { ("Belag", layout.AsphaltSurface), ("Gras", layout.GrassSurface) })
                {
                    for (var i = 0; i < material.Item2.Length; i++)
                    {
                        var r = material.Item2[i];
                        Console.WriteLine($"    {material.Item1} {i}: " + string.Join(" ", r.Select(p => $"({p.x:F3},{p.y:F3})")));
                    }
                }
                foreach (var n in layout.NetLine.Where(n => n.Art == Zufahrtsart.Fussweg))
                    Console.WriteLine($"    Fussnetz ({n.A.x:F3},{n.A.y:F3}) ({n.B.x:F3},{n.B.y:F3})");
            }
            if (Environment.GetEnvironmentVariable("PLT_STREIFEN") == "1")
                MissStreifenGegenDreiecke(fall.Name, layout);
            PruefeNetzFlaeche(fall.Name, layout, Pruefe);
            PruefeSplitFussweg(fall, layout, Pruefe);
            Pruefe(layout.Stalls > 0, fall.Name + ": leeres Layout");
            Pruefe(abgelehnt == 0, fall.Name + ": " + abgelehnt
                + " Ring(e) gehen an CS2, die es ablehnen wird - "
                + "der Bau bricht dann bei der Materialisierung ab");
        }

        PruefeNetzZerlegung(Pruefe);
        PruefeHinweisfilter(Pruefe);
        PruefeFusswegsuche(Pruefe);

        Console.WriteLine($"Flaechenannahme: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    /**
     * DIE DREIECKE MUESSEN DIE FLAECHE SEIN, NICHT IRGENDWAS DARUEBER.
     *
     * `Cs2Triangulierung.Netz` zerlegt einen Ring in Dreiecke, damit die
     * Vorschau ihn gefuellt zeichnen kann. Am 2026-09-15 lag dort im
     * Vorversuch noch ein FAECHER vom ersten Eckpunkt aus - der stimmt nur
     * bei Formen ohne Einbuchtung, und im Spiel standen deshalb grosse weisse
     * Keile quer ueber den Parkplatz.
     *
     * Der Test dagegen ist die FLAECHE: die Summe der Dreiecksflaechen muss
     * die Polygonflaeche treffen. Ein Faecher ueber eine Einbuchtung deckt
     * mehr ab als das Polygon und faellt hier sofort auf - Dreieckszaehlen
     * allein wuerde ihn durchlassen, denn n-2 Dreiecke liefert er auch.
     *
     * Gegengeprueft am 2026-09-15 durch Mutation: `Netz` wieder auf den
     * Faecher umgestellt -> der L-Fall unten meldet 76 statt 64 m2, und die
     * echten Grasringe der Nutzerfaelle melden ebenfalls rot.
     */
    private static double Polygonflaeche(float2[] ring)
    {
        var n = ring.Length;
        while (n > 1 && ring[n - 1].Equals(ring[0])) n--;
        if (n < 3) return 0.0;
        var summe = 0.0;
        for (var i = 0; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            summe += (double)a.x * b.y - (double)b.x * a.y;
        }
        return Math.Abs(summe) * 0.5;
    }

    private static double Dreiecksflaeche(float2[] ring, int[] netz)
    {
        var summe = 0.0;
        for (var i = 0; i + 2 < netz.Length; i += 3)
        {
            var a = ring[netz[i]];
            var b = ring[netz[i + 1]];
            var c = ring[netz[i + 2]];
            summe += Math.Abs((double)(b.x - a.x) * (c.y - a.y)
                            - (double)(c.x - a.x) * (b.y - a.y)) * 0.5;
        }
        return summe;
    }

    private static void PruefeNetzRing(string wo, float2[] ring,
                                       Action<bool, string> Pruefe)
    {
        if (ring == null || ring.Length < 3) return;
        var zahl = Cs2Triangulierung.Dreiecke(ring);
        var netz = Cs2Triangulierung.Netz(ring);

        // Beide Wege muessen dieselbe Rechnung sein - sie SIND es, aber genau
        // das ist die Zusage, auf die sich die Vorschau stuetzt.
        if (zahl == 0)
        {
            Pruefe(netz == null, wo + ": CS2 verwirft den Ring, Netz liefert "
                + "trotzdem Dreiecke");
            return;
        }
        if (netz == null)
        {
            Pruefe(false, wo + ": CS2 nimmt den Ring an (" + zahl
                + " Dreiecke), Netz liefert nichts");
            return;
        }
        Pruefe(netz.Length == zahl * 3, wo + ": " + zahl + " Dreiecke gezaehlt, "
            + "aber " + netz.Length + " Indizes statt " + (zahl * 3));
        foreach (var i in netz)
            if (i < 0 || i >= ring.Length)
            {
                Pruefe(false, wo + ": Index " + i + " zeigt aus dem Ring mit "
                    + ring.Length + " Punkten heraus");
                return;
            }

        var soll = Polygonflaeche(ring);
        var ist = Dreiecksflaeche(ring, netz);
        Pruefe(soll <= 0.0 || Math.Abs(ist - soll) <= Math.Max(0.01, soll * 0.001),
            wo + ": Dreiecke decken " + ist.ToString("F2") + " m2 ab, das "
            + "Polygon hat " + soll.ToString("F2") + " m2");
    }

    /**
     * WIE GUT BILDET DIE VORSCHAU DIE FLAECHEN UEBERHAUPT AB?
     *
     * Die Vorschau kann kein Vieleck fuellen - CS2s Overlay kennt nur Kurve,
     * Kreis und Strich. `ParkingSurfaceStrips` behilft sich deshalb mit
     * ABTASTSTREIFEN von 4 m Breite. Bei echten Rechtecken ist das exakt,
     * bei allem anderen eine Treppe: an schraegen und runden Kanten schiesst
     * jeder Streifen ueber oder bleibt zurueck.
     *
     * Der Nutzer am 2026-09-15: *"Derzeit ist es so dass Unfoermige Flaechen
     * einfach durch Rechtecke gefuellt werden was enorm haesslich aussieht."*
     * Diese Messung sagt, um wieviel es geht - Streifen gegen Dreiecke, Zahl
     * und Flaechenfehler. Kein Pruefpunkt, ein Messpunkt: `PLT_STREIFEN=1`.
     */
    private static void MissStreifenGegenDreiecke(string name,
                                                  ParkingLayout layout)
    {
        foreach (var material in new[]
                 {
                     // GENAU DIE LISTEN, DIE DIE VORSCHAU FUELLT.
                     // `_green` in ParkingLotOverlay.SetLayout wird aus diesen
                     // fuenf gespeist - nicht aus `GrassSurface`. Wer die
                     // verschmolzenen Ringe misst, misst etwas anderes.
                     ("Gruen-Entwurf", Zusammen(layout.Median, layout.Cap,
                         layout.Green, layout.Fill, layout.CrossPavement)),
                     ("Belag-Ringe", layout.AsphaltSurface),
                     ("Gras-Ringe", layout.GrassSurface),
                 })
        {
            var ringe = (material.Item2 ?? new float2[0][])
                .Where(r => r != null && r.Length >= 3).ToArray();
            if (ringe.Length == 0) continue;

            var soll = ringe.Sum(Polygonflaeche);

            var streifen = ParkingSurfaceStrips.Fill(
                ringe, ParkingSurfaceStrips.PreferredWidth);
            var streifenflaeche = streifen.Sum(
                t => Math.Sqrt(Math.Pow(t.To.x - t.From.x, 2)
                             + Math.Pow(t.To.y - t.From.y, 2)) * t.Width);

            var dreiecke = 0;
            var dreiecksflaeche = 0.0;
            var verworfen = 0;
            foreach (var ring in ringe)
            {
                var netz = Cs2Triangulierung.Netz(ring);
                if (netz == null) { verworfen++; continue; }
                dreiecke += netz.Length / 3;
                dreiecksflaeche += Dreiecksflaeche(ring, netz);
            }

            Console.WriteLine($"    {name} {material.Item1}: {ringe.Length} Teile, "
                + $"{soll:F0} m2 | Streifen {streifen.Length} Stueck, "
                + $"{streifenflaeche:F0} m2 ({(streifenflaeche - soll) / soll * 100.0:F1} %) "
                + $"| Dreiecke {dreiecke} Stueck, {dreiecksflaeche:F0} m2 "
                + $"({(dreiecksflaeche - soll) / soll * 100.0:F1} %), "
                + $"{verworfen} Teil(e) verwirft CS2");
        }
    }

    private static float2[][] Zusammen(params float2[][][] listen)
    {
        var alle = new List<float2[]>();
        foreach (var liste in listen)
            if (liste != null)
                alle.AddRange(liste);
        return alle.ToArray();
    }

    private static void PruefeNetzFlaeche(string name, ParkingLayout layout,
                                          Action<bool, string> Pruefe)
    {
        foreach (var material in new[] { ("Belag", layout.AsphaltSurface),
                                         ("Gras", layout.GrassSurface) })
        {
            var ringe = material.Item2;
            if (ringe == null) continue;
            for (var i = 0; i < ringe.Length; i++)
                PruefeNetzRing(name + " " + material.Item1 + " " + i,
                    ringe[i], Pruefe);
        }
    }

    private static void PruefeNetzZerlegung(Action<bool, string> Pruefe)
    {
        // Ein L. Die Einbuchtung ist der ganze Punkt: ein Faecher vom ersten
        // Eckpunkt aus schiesst hier quer ueber die Kerbe hinweg.
        var l = new[]
        {
            new float2(0f, 0f), new float2(10f, 0f), new float2(10f, 4f),
            new float2(4f, 4f), new float2(4f, 10f), new float2(0f, 10f),
        };
        PruefeNetzRing("L-Form", l, Pruefe);
        Pruefe(Math.Abs(Polygonflaeche(l) - 64.0) < 1e-6,
            "L-Form: Testfigur selbst falsch, " + Polygonflaeche(l) + " m2");
        var netzL = Cs2Triangulierung.Netz(l);
        Pruefe(netzL != null && netzL.Length == 4 * 3,
            "L-Form: " + (netzL == null ? "verworfen" : netzL.Length / 3
                + " Dreiecke") + ", erwartet 4");

        // Ein schlichtes Rechteck als Gegenprobe - hier waere der Faecher
        // richtig, und das Ergebnis muss dasselbe sein.
        var rechteck = new[]
        {
            new float2(0f, 0f), new float2(20f, 0f),
            new float2(20f, 5f), new float2(0f, 5f),
        };
        PruefeNetzRing("Rechteck", rechteck, Pruefe);

        // Und ein Ring, den CS2 nachweislich verwirft: eine Haarnadel, die
        // den 0,1-m-Innenversatz nicht ueberlebt. `Netz` muss das genauso
        // sehen wie `Dreiecke` - sonst zeigt die Vorschau eine Flaeche, die
        // es nach dem Bauen nicht gibt.
        var haarnadel = new[]
        {
            new float2(0f, 0f), new float2(20f, 0f),
            new float2(20f, 0.02f), new float2(0f, 0.02f),
        };
        Pruefe(Cs2Triangulierung.Dreiecke(haarnadel) == 0,
            "Haarnadel: CS2 nimmt sie wider Erwarten an - Testfigur taugt nicht");
        PruefeNetzRing("Haarnadel", haarnadel, Pruefe);
    }

    private static int ZaehleAbgelehnt(float2[][] ringe)
    {
        if (ringe == null) return 0;
        var zahl = 0;
        foreach (var ring in ringe)
        {
            if (ring == null || ring.Length < 3) continue;
            if (Cs2Triangulierung.Dreiecke(ring) == 0) zahl++;
        }
        return zahl;
    }

    private readonly struct Flaechenfall
    {
        internal Flaechenfall(string name, float2[] umriss,
                              LayoutSettings einstellungen, int ganzeFahrgassenbaender)
        {
            Name = name;
            Umriss = umriss;
            Einstellungen = einstellungen;
            GanzeFahrgassenbaender = ganzeFahrgassenbaender;
        }

        internal string Name { get; }
        internal float2[] Umriss { get; }
        internal LayoutSettings Einstellungen { get; }
        internal int GanzeFahrgassenbaender { get; }
    }

    private static IEnumerable<Flaechenfall> Flaechenfaelle()
    {
        /*
         * Der gemeldete Fall, Koordinaten aus dem Problembericht des Nutzers
         * (`ParkingLotTool-summary-20260908-182847-091.txt`). Randstrassen
         * aus, Kappen an, eine Zufahrt - genau die Lage, in der 138 Flaechen
         * geplant und nur 137 angenommen wurden.
         */
        var gemeldet = new[]
        {
            new float2(-1037.02014f, 118.689804f),
            new float2(-1042.714f, -47.3021545f),
            new float2(-1195.733f, -42.052002f),
            new float2(-1190.036f, 123.939f),
        };
        yield return new Flaechenfall(
            "Nutzerfall 18:28 ringlos", gemeldet, RinglosSettings(), 7);

        // Derselbe Umriss MIT Randstrasse - dort war nie etwas kaputt, und
        // das muss so bleiben.
        var mitRing = RinglosSettings();
        mitRing.Randstrassen = true;
        yield return new Flaechenfall(
            "Nutzerfall 18:28 mit Ring", gemeldet, mitRing, 6);

        // Der frueher abgestuerzte Umriss vom 17:30, ebenfalls ringlos.
        yield return new Flaechenfall("Nutzerfall 17:30 ringlos", new[]
        {
            new float2(-1037.02026f, 118.689781f),
            new float2(-1183.354f, 123.710007f),
            new float2(-1188.24707f, -18.8730011f),
            new float2(-1041.911f, -23.8930016f),
        }, RinglosSettings(), 6);

        // Derselbe Umriss im anderen Winkelmodus und ohne Zufahrt - damit
        // der Lauf nicht nur die eine gemeldete Kombination abdeckt.
        yield return new Flaechenfall("Nutzerfall 18:28 edge ohne Zufahrt",
            gemeldet, RinglosSettings("edge", false), 7);

        // Ein schlichtes Rechteck als Kontrolle: geht das kaputt, liegt es
        // nicht an der Form des Nutzerpolygons.
        yield return new Flaechenfall("Rechteck 80 x 64 ringlos", new[]
        {
            new float2(0, 0), new float2(80, 0),
            new float2(80, 64), new float2(0, 64),
        }, RinglosSettings("quer", false), 3);
    }

    /**
     * DIE EINSTELLUNGEN SIND TEIL DES FALLS, NICHT BEIWERK.
     *
     * Der erste Anlauf lief mit der Voreinstellung `LayoutSettings.Cs2` -
     * also `angleMode edge` und ohne Zufahrt - und war sofort gruen. Der
     * Nutzer hatte aber `angle mode quer` und EINE Zufahrt eingestellt; genau
     * damit fielen bei ihm drei Flaechen durch. Ein Lauf, der die gemeldete
     * Lage nicht nachstellt, prueft die falsche Sache.
     *
     * Dieselbe Falle steht schon in `plt-parity-blind-fleck`: der
     * Paritaetslauf prueft nur CS2_SETTINGS, waehrend der Mod anders faehrt.
     */
    private static LayoutSettings RinglosSettings(
        string winkelmodus = "quer", bool mitZufahrt = true)
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.Randstrassen = false;
        einstellungen.AngleMode = winkelmodus;
        einstellungen.Angle = 0;
        einstellungen.AutomaticEntrances = false;
        einstellungen.Entrances = mitZufahrt
            // Exakt die Zufahrt des Nutzers aus
            // ParkingLotTool-debug-20260908-182847-091.json.
            ? new[] { new Entrance { Edge = 3, Along = 76.55155944824219 } }
            : Array.Empty<Entrance>();
        return einstellungen;
    }

    /**
     * Der Anzeigefilter darf nur Nullflaechen schlucken.
     *
     * Er entscheidet, was der Nutzer NICHT sieht. Verliest er sich, ver-
     * schwindet eine echte Warnung still - die gefaehrlichste Richtung.
     */
    private static void PruefeHinweisfilter(Action<bool, string> Pruefe)
    {
        void Weg(string text) => Pruefe(Hinweisfilter.IstBelanglos(text),
            "muesste gefiltert werden: " + text);
        void Bleibt(string text) => Pruefe(!Hinweisfilter.IstBelanglos(text),
            "muesste sichtbar bleiben: " + text);

        // Genau die zwei Meldungen, die den Nutzer am 2026-09-08 als rote
        // Fehler angesprungen haben.
        Weg("Halbebenenschnitt x=-6,675 (ID 182): entartete Scherbe "
            + "verworfen, Flaeche 1,63803424622292E-16 m2, Rundungsgrenze "
            + "7,227137881695515E-10 m2.");
        Weg("Cell engine: 1 surface ring(s) with 0,00 m2 left out - CS2 "
            + "would have refused them.");

        // Dieselben Meldungen mit echtem Verlust muessen sichtbar bleiben.
        Bleibt("Cell engine: 3 surface ring(s) with 1009,16 m2 left out - "
            + "CS2 would have refused them.");
        Bleibt("Halbebenenschnitt x=-6,675 (ID 182): entartete Scherbe "
            + "verworfen, Flaeche 12,5 m2, Rundungsgrenze 1E-10 m2.");

        // ZWEITE SORTE: Befunde ueber unsere eigene Konstruktion. Wichtig,
        // aber nicht fuer den Nutzer - sie stehen weiter im Bauzettel.
        void WegBefund(string text) => Pruefe(
            Hinweisfilter.Sichtbare(new[] { text }).Length == 0,
            "muesste aus der Statusleiste verschwinden: " + text);
        WegBefund("Cell engine: hole separation had to cut 1 seam(s) - "
            + "1 hole(s) in 1 surface(s). A surface merged into a ring; that "
            + "is a construction fault upstream, not a repair job.");
        // Sie ist KEINE Nullflaechenmeldung - die beiden Sorten duerfen sich
        // nicht vermischen.
        Bleibt("Cell engine: hole separation had to cut 1 seam(s) - "
            + "1 hole(s) in 1 surface(s). A surface merged into a ring; that "
            + "is a construction fault upstream, not a repair job.");
        Pruefe(!Hinweisfilter.IstEntwicklerbefund(
                "Cell engine: 3 surface ring(s) with 1009,16 m2 left out - "
                + "CS2 would have refused them."),
            "eine gewoehnliche Warnung ist kein Entwicklerbefund");

        // Alles Unbekannte bleibt stehen, auch wenn es Nullflaechen nennt.
        Bleibt("Cell engine: automatic entrances are not implemented; "
            + "place them by hand.");
        Bleibt("Irgendeine neue Meldung mit 0,00 m2 darin.");
        Bleibt("");

        // Punkt statt Komma, und ohne Flaechenangabe.
        Weg("Cell engine: 1 surface ring(s) with 0.00 m2 left out.");
        Bleibt("entartete Scherbe verworfen, ohne Zahl");
    }
}
