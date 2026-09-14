using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Zaehlt VEREINZELTE BUCHTEN - Nutzerbefund vom 2026-08-21:
     *
     *   "wenn ich zb ne Treppen-Form baue passiert das immer noch dass 1 oder
     *    2 buchten einzeln bleiben. Derzeit funktioniert es nur auf der
     *    Laengsten Flaeche was dazu fuehrt dass sich die kuerzeren daran
     *    anpassen und das gleiche geschieht."
     *
     * Seine Lesart deckt sich mit dem Code: `Layoutplanung.Spalten` bekommt
     * genau EINE Ausdehnung - die des ganzen Areals. Bei einer Treppe ist das
     * die laengste Reihe; alle kuerzeren erben deren Querstrassenraster und
     * behalten an ihren Enden, was uebrig bleibt.
     *
     * Gemessen wird deshalb nicht "gebaut ja/nein", sondern das, was er
     * wirklich sieht: Gruppen von Buchten, die in ihrer Reihe allein stehen.
     * Die Zahl ist von der Rechenart unabhaengig - sie faellt aus den
     * fertigen Buchten, nicht aus dem Plan.
     */
    private static int RunTreppen()
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var formen = new List<(string Name, float2[] Site)>
        {
            ("Rechteck   ", Punkte("0,0;120,0;120,90;0,90")),
            ("L-Form     ", Punkte("0,0;120,0;120,45;60,45;60,90;0,90")),
            ("L schraeg  ", Punkte("-65.7,-31;120,-31;120,45;76.2,52.4;60,90;-65.7,90")),
            ("Treppe 2   ", Punkte("0,0;180,0;180,45;90,45;90,90;0,90")),
            ("Treppe 3   ", Punkte("0,0;210,0;210,40;140,40;140,80;70,80;70,120;0,120")),
            ("Treppe 4   ", Punkte("0,0;240,0;240,40;180,40;180,80;120,80;120,120;60,120;60,160;0,160")),
            ("Treppe 3 s ", Dreh(Punkte("0,0;210,0;210,40;140,40;140,80;70,80;70,120;0,120"), 13)),
            ("Treppe fein", Punkte("0,0;200,0;200,30;160,30;160,60;120,60;120,90;80,90;80,120;0,120")),
        };

        Console.WriteLine("VEREINZELTE BUCHTEN - Gruppen, die in ihrer Reihe allein stehen");
        Console.WriteLine("  Eine Gruppe ist eine ununterbrochene Folge von Buchten in einer");
        Console.WriteLine("  Reihe. Vereinzelt heisst: hoechstens zwei Buchten in der Gruppe.");
        Console.WriteLine();
        Console.WriteLine("  Form         Weg      Buchten  Reihen  Gruppen  vereinzelt  innen/rand  kleinste");
        var schlecht = 0;
        foreach (var form in formen)
        {
            foreach (var zellen in new[] { false, true })
            {
                var werte = einstellungen.Clone();
                werte.Zellen = zellen;
                var name = zellen ? "zellen" : "alt   ";
                try
                {
                    var layout = ParkingGeometry.Build(form.Site, werte);
                    var gruppen = Buchtengruppen(layout);
                    var vereinzelt = gruppen.Count(g => g.Anzahl <= 2);
                    var vereinzeltInnen = Buchtengruppen(layout, BayKind.Inner)
                        .Count(g => g.Anzahl <= 2);
                    var vereinzeltRand = Buchtengruppen(layout, BayKind.Perimeter)
                        .Count(g => g.Anzahl <= 2);
                    var kleinste = gruppen.Count == 0 ? 0 : gruppen.Min(g => g.Anzahl);
                    var reihen = gruppen.Select(g => g.Reihe).Distinct().Count();
                    Console.WriteLine($"  {form.Name}  {name}  {layout.Stalls,7}  "
                        + $"{reihen,6}  {gruppen.Count,7}  {vereinzelt,10}  "
                        + $"{vereinzeltInnen,5}/{vereinzeltRand,-4}  {kleinste,8}");
                    if (zellen) schlecht += vereinzelt;
                }
                catch (Exception ausnahme)
                {
                    while (ausnahme.InnerException != null)
                        ausnahme = ausnahme.InnerException;
                    Console.WriteLine($"  {form.Name}  {name}  FEHLER: {ausnahme.Message}");
                }
            }
        }
        Console.WriteLine();
        Console.WriteLine($"  Zellenweg gesamt: {schlecht} vereinzelte Gruppen");
        return 0;
    }

    /**
     * Eine Form nachrechnen und JEDE Buchtengruppe mit ihrer Lage nennen.
     *
     * Der Nutzer markiert Stellen im Spiel; um seine Markierung einer Gruppe
     * zuzuordnen, reicht die blosse Anzahl nicht - die Gruppe braucht eine
     * Koordinate. Optional koennen Markierungen mitgegeben werden, dann steht
     * hinter jeder der Abstand zur naechsten kleinen Gruppe.
     */
    private static int RunGruppen(string polygon, string markierungen)
    {
        var site = Punkte(polygon);
        // Mit "eine" als zweitem Argument: beide Kategorien dasselbe Prefab.
        var eineFlaeche = markierungen == "eine";
        if (eineFlaeche) markierungen = null;
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        Console.WriteLine($"BUCHTENGRUPPEN | {site.Length} Ecken | "
            + $"{FlaecheVon(site):F0} m2");
        foreach (var zellen in new[] { false, true })
        {
            var werte = einstellungen.Clone();
            werte.Zellen = zellen;
            werte.EineFlaeche = eineFlaeche && zellen;
            var name = zellen ? "zellen" : "alt";
            Console.WriteLine();
            try
            {
                var layout = ParkingGeometry.Build(site, werte);
                var gruppen = MitLage(layout);
                Console.WriteLine($"  {name}: {layout.Stalls} Buchten, "
                    + $"{layout.Aisles} Fahrgassen, Winkel {layout.Angle:F2} Grad, "
                    + $"{gruppen.Count} Gruppen");
                var klein = gruppen.Where(g => g.Anzahl < 5)
                    .OrderBy(g => g.Anzahl).ToList();
                Console.WriteLine($"  davon unter 5 Buchten: {klein.Count} "
                    + $"({klein.Sum(g => g.Anzahl)} Buchten insgesamt)");
                foreach (var g in klein)
                    Console.WriteLine($"    {g.Anzahl,2} Buchten bei "
                        + $"{g.X,9:F1} / {g.Z,7:F1}  (Reihe {g.Reihe}, "
                        + $"Laenge {g.Laenge:F1} m)");
                Console.WriteLine($"  Flaechen: {layout.GrassSurface.Length} Gras "
                    + $"({RingFlaeche(layout.GrassSurface):F0} m2), "
                    + $"{layout.AsphaltSurface.Length} Belag "
                    + $"({RingFlaeche(layout.AsphaltSurface):F0} m2)");
                Console.WriteLine($"  Netz: {layout.NetLine.Length} Stuecke, "
                    + $"{OffeneEinmuendungen(layout)} ungeteilte Einmuendungen "
                    + "(Endpunkt liegt MITTEN auf einem anderen Stueck)");
                Console.WriteLine("  Zufahrtsknoten: " + Zufahrtsgrad(layout));
                var kuerzestes = (layout.NetLine ?? Array.Empty<NetSegment>())
                    .Select(t => (double)math.distance(t.A, t.B))
                    .DefaultIfEmpty(0).Min();
                Console.WriteLine($"  Kuerzestes Wegstueck: {kuerzestes:F3} m"
                    + (kuerzestes < 0.375 && kuerzestes > 0
                        ? "  <== CS2 verwirft alles unter 0,375 m" : ""));
                Console.WriteLine("  Groessenverteilung: " + string.Join(", ",
                    gruppen.GroupBy(g => g.Anzahl).OrderBy(g => g.Key)
                        .Select(g => $"{g.Count()}x {g.Key}")));
                foreach (var art in new[] { BayKind.Inner, BayKind.Perimeter })
                {
                    var artGruppen = Buchtengruppen(layout, art);
                    Console.WriteLine($"  {art}: {artGruppen.Count} Gruppen, "
                        + $"{artGruppen.Count(g => g.Anzahl < 5)} unter 5, "
                        + $"{artGruppen.Count(g => g.Anzahl <= 2)} vereinzelt; "
                        + string.Join(", ", artGruppen.GroupBy(g => g.Anzahl)
                            .OrderBy(g => g.Key)
                            .Select(g => $"{g.Count()}x {g.Key}")));
                }
                if (zellen)
                {
                    var innenwinkel = Enumerable.Range(0, layout.Bay.Length)
                        .Where(i => layout.BayKind[i] == BayKind.Inner)
                        .Select(i =>
                        {
                            var bucht = layout.Bay[i];
                            var a = new double2(
                                bucht[1].x - bucht[0].x,
                                bucht[1].y - bucht[0].y);
                            var b = new double2(
                                bucht[2].x - bucht[1].x,
                                bucht[2].y - bucht[1].y);
                            var kurz = math.length(a) <= math.length(b) ? a : b;
                            kurz /= math.length(kurz);
                            if (kurz.x < 0 || (kurz.x == 0 && kurz.y < 0))
                                kurz = -kurz;
                            return Math.Atan2(kurz.y, kurz.x) * 180 / Math.PI;
                        }).ToArray();
                    Console.WriteLine("  Innere Buchtwinkel: " + string.Join(", ",
                        innenwinkel.GroupBy(w => (int)Math.Round(w / 2))
                            .OrderBy(g => g.Key)
                            .Select(g => $"{g.Count()}x {g.Min():F6}..{g.Max():F6}")));
                }
                if (zellen) SchreibeModuldiagnose(site);

                if (!string.IsNullOrEmpty(markierungen))
                {
                    Console.WriteLine("  Markierungen des Nutzers:");
                    var nummer = 1;
                    foreach (var marker in Punkte(markierungen))
                    {
                        var naechste = gruppen
                            .OrderBy(g => (g.X - marker.x) * (g.X - marker.x)
                                + (g.Z - marker.y) * (g.Z - marker.y))
                            .First();
                        var abstand = Math.Sqrt(
                            (naechste.X - marker.x) * (naechste.X - marker.x)
                            + (naechste.Z - marker.y) * (naechste.Z - marker.y));
                        Console.WriteLine($"    Markierung {nummer++} bei "
                            + $"{marker.x,9:F1} / {marker.y,7:F1}  -> naechste "
                            + $"Gruppe {naechste.Anzahl,2} Buchten, {abstand:F1} m entfernt");
                    }
                }
            }
            catch (Exception ausnahme)
            {
                while (ausnahme.InnerException != null)
                    ausnahme = ausnahme.InnerException;
                Console.WriteLine($"  {name}: FEHLER {ausnahme.Message}");
            }
        }
        return 0;
    }

    private static void SchreibeModuldiagnose(float2[] site)
    {
        var punkte = site.Select(punkt => new Punkt(
                Math.Round((double)punkt.x / 0.001) * 0.001,
                Math.Round((double)punkt.y / 0.001) * 0.001))
            .ToList();
        if (Geometrie.Vorzeichenflaeche(punkte) < 0) punkte.Reverse();
        var bau = Layoutbauer.Baue(
            new Formdefinition("Gruppendiagnose", punkte),
            new Zelleneinstellungen());
        Console.WriteLine($"  Modulplan: {bau.Modulspaltenplaene.Count} Module, "
            + $"{bau.Querstrassenpruefungen.Count(p => p.Bleibt)}/"
            + $"{bau.Querstrassenpruefungen.Count} Kandidaten bleiben, "
            + $"{bau.Querstrassenstuecke.Count} angeschlossene Stuecke");
        foreach (var plan in bau.Modulspaltenplaene)
        {
            var pruefungen = bau.Querstrassenpruefungen
                .Where(p => p.ModulId == plan.Modul.Id)
                .OrderBy(p => p.QuerstrassenId)
                .Select(p => $"Q{p.QuerstrassenId} "
                    + $"{p.ErsteLinks}/{p.ErsteRechts}/"
                    + $"{p.ZweiteLinks}/{p.ZweiteRechts} "
                    + (p.Bleibt ? "an" : "aus"));
            Console.WriteLine($"    M{plan.Modul.Id} "
                + $"x={plan.MinX:F2}..{plan.MaxX:F2}: "
                + string.Join(" | ", pruefungen));
        }
    }

    /**
     * Ab welcher Luecke zwei Buchten NICHT mehr zusammengehoeren.
     *
     * Stand bis zum 2026-08-21 auf 4,5 m - das passte, solange jede Bucht
     * 3,0 m breit war. Behindertenbuchten sind 4,7 bis 5,6 m breit; seit sie
     * gebaut werden, zerschnitt die alte Schwelle jede Behindertengruppe in
     * lauter Einzelgruppen und meldete 195 statt 11 kurze Gruppen. Das war
     * das Messgeraet, nicht das Layout.
     *
     * 7,0 m trennt sauber: der groesste Abstand INNERHALB einer Gruppe sind
     * 5,6 m, der kleinste ZWISCHEN zwei Gruppen ist eine Querstrasse mit
     * zwei Kappen, also mindestens 3 + 3 + 3 = 9,0 m.
     */
    private const double Luecke = 7.0;

    /**
     * An wie viele RANDSTRASSENstuecke haengt sich jede Zufahrt?
     *
     * Nutzerbefund vom 2026-08-21 an der Spurenansicht: *"bei der Einfahrt zur
     * Randstrasse geht es nur in eine Richtung nicht beide."*
     *
     * Genau das misst diese Zahl. Trifft das innere Zufahrtsende einen Punkt,
     * an dem nur EIN Randstrassenstueck endet, ist der Ring dort eine
     * Sackgasse - man kann nur in die eine Richtung abbiegen. Erst bei ZWEI
     * Stuecken entsteht eine echte T-Einmuendung.
     *
     * Die bisherige Anschlusspruefung war dafuer blind: sie fragte nur, ob der
     * Punkt IRGENDEIN Randstrassenknoten ist.
     */
    private static string Zufahrtsgrad(ParkingLayout layout)
    {
        var netz = layout.NetLine ?? Array.Empty<NetSegment>();
        var rand = netz.Where(t => t.Kind == "perimeter").ToArray();
        var zufahrten = netz.Where(t => t.Kind == "entrance").ToArray();
        if (zufahrten.Length == 0) return "keine Zufahrt im Netz";

        var enden = new HashSet<(float, float)>();
        foreach (var t in zufahrten)
        {
            enden.Add((t.A.x, t.A.y));
            enden.Add((t.B.x, t.B.y));
        }
        var zeilen = new List<string>();
        foreach (var punkt in enden)
        {
            var grad = rand.Count(t =>
                (t.A.x == punkt.Item1 && t.A.y == punkt.Item2)
                || (t.B.x == punkt.Item1 && t.B.y == punkt.Item2));
            if (grad == 0) continue;
            zeilen.Add($"{grad} Randstueck(e)"
                + (grad < 2 ? "  <== SACKGASSE, nur eine Richtung" : ""));
        }
        return zeilen.Count == 0
            ? "kein Zufahrtsende beruehrt die Randstrasse"
            : string.Join(" | ", zeilen);
    }

    /** Gesamtflaeche einer Ringliste, Betrag der Schnuersenkelformel. */
    private static double RingFlaeche(float2[][] ringe)
    {
        var summe = 0.0;
        foreach (var ring in ringe ?? new float2[0][])
        {
            if (ring == null || ring.Length < 3) continue;
            var f = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                f += (double)a.x * b.y - (double)b.x * a.y;
            }
            summe += Math.Abs(f) / 2;
        }
        return summe;
    }

    /**
     * Einmuendungen, an denen CS2 keinen Knoten bilden kann.
     *
     * CS2 verschmilzt nur Segmente mit IDENTISCHEM Endpunkt. Endet ein Weg
     * MITTEN auf einem anderen, entsteht dort keine Kreuzung - die Autos
     * koennen nicht abbiegen. Der alte Pfad teilt sein `NetLine` deshalb an
     * jeder Einmuendung; ob der Zellenweg das auch tut, misst diese Zahl.
     */
    private static int OffeneEinmuendungen(ParkingLayout layout)
    {
        var offen = 0;
        var netz = layout.NetLine ?? Array.Empty<NetSegment>();
        foreach (var stueck in netz)
        foreach (var punkt in new[] { stueck.A, stueck.B })
        {
            foreach (var anderes in netz)
            {
                if (ReferenceEquals(anderes, stueck)) continue;
                if (punkt.Equals(anderes.A) || punkt.Equals(anderes.B)) continue;
                var ab = anderes.B - anderes.A;
                var laenge = math.length(ab);
                if (laenge < 1e-6) continue;
                var t = math.dot(punkt - anderes.A, ab) / (laenge * laenge);
                if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                var lot = anderes.A + ab * t;
                if (math.distance(punkt, lot) > 0.01f) continue;
                offen++;
                break;
            }
        }
        return offen;
    }

    private readonly struct Gruppenlage
    {
        internal Gruppenlage(int reihe, int anzahl, double x, double z, double laenge)
        { Reihe = reihe; Anzahl = anzahl; X = x; Z = z; Laenge = laenge; }
        internal int Reihe { get; }
        internal int Anzahl { get; }
        internal double X { get; }
        internal double Z { get; }
        internal double Laenge { get; }
    }

    /** Wie Buchtengruppen, aber mit Mittelpunkt und Laenge je Gruppe. */
    private static List<Gruppenlage> MitLage(ParkingLayout layout)
    {
        var raus = new List<Gruppenlage>();
        if (layout?.Bay == null || layout.Bay.Length == 0) return raus;

        var buchten = layout.Bay.Select(bucht =>
        {
            var a = new double2(bucht[1].x - bucht[0].x, bucht[1].y - bucht[0].y);
            var b = new double2(bucht[2].x - bucht[1].x, bucht[2].y - bucht[1].y);
            var kurz = math.length(a) <= math.length(b) ? a : b;
            var laenge = math.length(kurz);
            kurz = laenge < 1e-6 ? new double2(1, 0) : kurz / laenge;
            if (kurz.x < 0 || (kurz.x == 0 && kurz.y < 0)) kurz = -kurz;
            var x = bucht.Average(p => (double)p.x);
            var y = bucht.Average(p => (double)p.y);
            return new
            {
                Winkel = Math.Atan2(kurz.y, kurz.x) * 180 / Math.PI,
                U = x * kurz.x + y * kurz.y,
                V = -x * kurz.y + y * kurz.x,
                X = x,
                Z = y,
            };
        }).ToList();

        var reihenId = 0;
        foreach (var richtung in GruppiereNachRichtung(
                     buchten, b => b.Winkel))
        {
            var sortiert = richtung.OrderBy(b => b.V).ToList();
            var reihe = new List<(double U, double X, double Z)>
                { (sortiert[0].U, sortiert[0].X, sortiert[0].Z) };
            for (var i = 1; i < sortiert.Count; i++)
            {
                if (sortiert[i].V - sortiert[i - 1].V > 3.0)
                {
                    Schliesse(raus, reihenId++, reihe);
                    reihe = new List<(double, double, double)>();
                }
                reihe.Add((sortiert[i].U, sortiert[i].X, sortiert[i].Z));
            }
            Schliesse(raus, reihenId++, reihe);
        }
        return raus;
    }

    private static void Schliesse(
        List<Gruppenlage> raus, int reihe, List<(double U, double X, double Z)> reihenbuchten)
    {
        if (reihenbuchten.Count == 0) return;
        reihenbuchten.Sort((a, b) => a.U.CompareTo(b.U));
        var gruppe = new List<(double U, double X, double Z)> { reihenbuchten[0] };
        for (var i = 1; i < reihenbuchten.Count; i++)
        {
            if (reihenbuchten[i].U - reihenbuchten[i - 1].U > Luecke)
            {
                Fuege(raus, reihe, gruppe);
                gruppe = new List<(double, double, double)>();
            }
            gruppe.Add(reihenbuchten[i]);
        }
        Fuege(raus, reihe, gruppe);
    }

    private static void Fuege(
        List<Gruppenlage> raus, int reihe, List<(double U, double X, double Z)> gruppe)
    {
        if (gruppe.Count == 0) return;
        raus.Add(new Gruppenlage(reihe, gruppe.Count,
            gruppe.Average(g => g.X), gruppe.Average(g => g.Z),
            gruppe[gruppe.Count - 1].U - gruppe[0].U));
    }

    private readonly struct Buchtengruppe
    {
        internal Buchtengruppe(
            int reihe,
            int anzahl,
            double minU,
            double maxU,
            double v,
            double richtungX,
            double richtungY)
        {
            Reihe = reihe;
            Anzahl = anzahl;
            MinU = minU;
            MaxU = maxU;
            V = v;
            RichtungX = richtungX;
            RichtungY = richtungY;
        }
        internal int Reihe { get; }
        internal int Anzahl { get; }
        internal double MinU { get; }
        internal double MaxU { get; }
        internal double V { get; }
        internal double RichtungX { get; }
        internal double RichtungY { get; }
    }

    /**
     * Buchten nach Reihen sortieren und je Reihe in zusammenhaengende
     * Gruppen zerlegen.
     *
     * ACHTUNG, hier lag mein erster Fehlschlag: ich hatte alle Buchten in
     * EIN Koordinatensystem projiziert (das der inneren Reihen). Die
     * Randbuchten stehen aber senkrecht zur jeweiligen Arealkante, laufen
     * also quer dazu - das Ergebnis waren 3 "Reihen" fuer ein 120x90-Areal.
     *
     * Richtig ist, jede Bucht in IHREM EIGENEN Rahmen zu betrachten: die
     * Richtung ihrer schmalen Seite ist die Richtung ihrer Reihe. Erst
     * werden die Buchten nach dieser Richtung gruppiert, dann quer dazu
     * nach Reihen, dann laengs nach Luecken.
     */
    private static List<Buchtengruppe> Buchtengruppen(
        ParkingLayout layout,
        BayKind? nurArt = null)
    {
        var gruppen = new List<Buchtengruppe>();
        if (layout?.Bay == null || layout.Bay.Length == 0) return gruppen;

        var buchten = Enumerable.Range(0, layout.Bay.Length)
            .Where(index => !nurArt.HasValue
                || layout.BayKind[index] == nurArt.Value)
            .Select(index =>
        {
            var bucht = layout.Bay[index];
            // Die kuerzere der beiden Seiten ist die Breite; laengs dieser
            // Richtung stehen die Nachbarn derselben Reihe.
            var a = new double2(bucht[1].x - bucht[0].x, bucht[1].y - bucht[0].y);
            var b = new double2(bucht[2].x - bucht[1].x, bucht[2].y - bucht[1].y);
            var kurz = math.length(a) <= math.length(b) ? a : b;
            var laenge = math.length(kurz);
            if (laenge < 1e-6) kurz = new double2(1, 0);
            else kurz /= laenge;
            // Richtung und Gegenrichtung sind dieselbe Reihe.
            if (kurz.x < 0 || (kurz.x == 0 && kurz.y < 0)) kurz = -kurz;
            var winkel = Math.Atan2(kurz.y, kurz.x) * 180 / Math.PI;
            var x = bucht.Average(p => (double)p.x);
            var y = bucht.Average(p => (double)p.y);
            return new
            {
                Winkel = winkel,
                U = x * kurz.x + y * kurz.y,
                V = -x * kurz.y + y * kurz.x,
                RichtungX = kurz.x,
                RichtungY = kurz.y,
            };
        }).ToList();
        if (buchten.Count == 0) return gruppen;

        // Ein echtes Winkelintervall statt Rundungsfaecher: Bei der um 13 Grad
        // gedrehten Treppe lagen identische Reihen nach float2-Ausgabe bei
        // 12,999946..13,000154 Grad. `Round(winkel / 2)` hatte genau dort eine
        // Klassengrenze und erfand daraus 29 vereinzelte innere Gruppen.
        var reihenId = 0;
        foreach (var richtung in GruppiereNachRichtung(
                     buchten, b => b.Winkel))
        {
            var sortiert = richtung.OrderBy(b => b.V).ToList();
            var aktuell = new List<double> { sortiert[0].U };
            var querSumme = sortiert[0].V;
            var richtungX = sortiert[0].RichtungX;
            var richtungY = sortiert[0].RichtungY;
            for (var i = 1; i < sortiert.Count; i++)
            {
                // Quer zur Reihe liegt die naechste Reihe mindestens eine
                // Buchttiefe entfernt; 3,0 m trennt sicher und vertraegt
                // die Rundung der float2-Ausgabe.
                if (sortiert[i].V - sortiert[i - 1].V > 3.0)
                {
                    GruppiereReihe(
                        gruppen, reihenId++, aktuell, Luecke,
                        querSumme / aktuell.Count, richtungX, richtungY);
                    aktuell = new List<double>();
                    querSumme = 0;
                    richtungX = sortiert[i].RichtungX;
                    richtungY = sortiert[i].RichtungY;
                }
                aktuell.Add(sortiert[i].U);
                querSumme += sortiert[i].V;
            }
            GruppiereReihe(
                gruppen, reihenId++, aktuell, Luecke,
                querSumme / aktuell.Count, richtungX, richtungY);
        }
        return gruppen;
    }

    private static List<List<T>> GruppiereNachRichtung<T>(
        IEnumerable<T> werte,
        Func<T, double> winkelVon)
    {
        const double toleranzGrad = 1.0;
        var sortiert = werte.Select(wert => (
                Wert: wert,
                Winkel: ((winkelVon(wert) % 180) + 180) % 180))
            .OrderBy(wert => wert.Winkel)
            .ToList();
        var gruppen = new List<List<(T Wert, double Winkel)>>();
        foreach (var wert in sortiert)
        {
            var letzteGruppe = gruppen.Count == 0
                ? null
                : gruppen[gruppen.Count - 1];
            if (letzteGruppe == null
                || wert.Winkel
                    - letzteGruppe[letzteGruppe.Count - 1].Winkel
                    > toleranzGrad)
            {
                letzteGruppe = new List<(T Wert, double Winkel)>();
                gruppen.Add(letzteGruppe);
            }
            letzteGruppe.Add(wert);
        }
        if (gruppen.Count > 1)
        {
            var erste = gruppen[0];
            var letzte = gruppen[gruppen.Count - 1];
            if (erste[0].Winkel + 180
                - letzte[letzte.Count - 1].Winkel <= toleranzGrad)
            {
                letzte.AddRange(erste);
                gruppen.RemoveAt(0);
            }
        }
        return gruppen.Select(gruppe => gruppe
            .Select(wert => wert.Wert).ToList()).ToList();
    }

    /** Eine Reihe laengs in Gruppen zerlegen, getrennt durch Luecken. */
    private static void GruppiereReihe(
        List<Buchtengruppe> gruppen,
        int reihe,
        List<double> u,
        double luecke,
        double v,
        double richtungX,
        double richtungY)
    {
        if (u.Count == 0) return;
        u.Sort();
        var anzahl = 1;
        for (var i = 1; i < u.Count; i++)
        {
            if (u[i] - u[i - 1] > luecke)
            {
                gruppen.Add(new Buchtengruppe(
                    reihe, anzahl, u[i - anzahl], u[i - 1],
                    v, richtungX, richtungY));
                anzahl = 0;
            }
            anzahl++;
        }
        gruppen.Add(new Buchtengruppe(
            reihe, anzahl, u[u.Count - anzahl], u[u.Count - 1],
            v, richtungX, richtungY));
    }

    private static bool EndetAnQuerstrasse(
        Buchtengruppe gruppe,
        ParkingLayout layout)
    {
        foreach (var linie in layout.CrossRouteLine)
        {
            if (linie == null || linie.Length < 2) continue;
            var a = linie[0];
            var b = linie[linie.Length - 1];
            var va = -a.x * gruppe.RichtungY + a.y * gruppe.RichtungX;
            var vb = -b.x * gruppe.RichtungY + b.y * gruppe.RichtungX;
            if (gruppe.V < Math.Min(va, vb) - 1e-4
                || gruppe.V > Math.Max(va, vb) + 1e-4
                || Math.Abs(vb - va) < 1e-6)
                continue;
            var t = (gruppe.V - va) / (vb - va);
            var ua = a.x * gruppe.RichtungX + a.y * gruppe.RichtungY;
            var ub = b.x * gruppe.RichtungX + b.y * gruppe.RichtungY;
            var schnittU = ua + (ub - ua) * t;
            var abstand = Math.Min(
                Math.Abs(schnittU - gruppe.MinU),
                Math.Abs(schnittU - gruppe.MaxU));
            // Vom Mittelpunkt der letzten 3,0-m-Bucht bis zur Strassenmitte:
            // 1,5 m halbe Bucht + 3,0..<4,5 m Kappe + 1,5 m halbe Strasse.
            if (abstand >= 6.0 - 0.05 && abstand < 7.5 + 0.05)
                return true;
        }
        return false;
    }

    private static float2[] Punkte(string beschreibung) => beschreibung
        .Split(';')
        .Select(paar => paar.Split(','))
        .Select(teile => new float2(
            float.Parse(teile[0], Kultur), float.Parse(teile[1], Kultur)))
        .ToArray();

    private static float2[] Dreh(float2[] punkte, double grad)
    {
        var bogen = grad * Math.PI / 180;
        var cos = Math.Cos(bogen);
        var sin = Math.Sin(bogen);
        return punkte.Select(p => new float2(
            (float)(p.x * cos - p.y * sin),
            (float)(p.x * sin + p.y * cos))).ToArray();
    }
}
