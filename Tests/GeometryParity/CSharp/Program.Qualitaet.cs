using System;
using System.Diagnostics;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * DER EIGENE QUALITAETSTEST.
 *
 * WARUM ES IHN GIBT: der bisherige Paritaetstest vergleicht den Mod mit dem
 * Prototyp in StreetBlockAlgo. Der Nutzer hat den Prototyp am 2026-08-17 als
 * Massstab aufgegeben ("so verdrahtet und vom Geruest bruechig") - damit
 * verlieren alle Vergleichszahlen ihren Bezugspunkt. Was bleibt, sind
 * Eigenschaften, die aus sich heraus richtig oder falsch sind.
 *
 * Genau dieser Teil hat sich als der wertvolle erwiesen: er fing am
 * 2026-08-17 eine echte Regression (0,3 % ungedeckte Flaeche auf `Schraeg`),
 * waehrend alle roten Zeilen daneben blosse Abweichungen vom Prototyp waren.
 *
 * WAS ER ANDERS MACHT ALS DER ALTE:
 *   - er prueft in `angleMode edge`, dem Modus, den der Mod WIRKLICH faehrt
 *     (der alte prueft CS2_SETTINGS - siehe plt-parity-blind-fleck)
 *   - er laeuft ueber beliebig viele Formen, nicht ueber vier
 *   - keine Zahl darin stammt aus einer anderen Umsetzung
 *
 * JEDE SCHRANKE HAT EINEN GEMELDETEN FEHLER HINTER SICH. Sie sind keine
 * Wunschwerte, sondern die Grenzen, ab denen der Nutzer etwas Kaputtes sah.
 *
 * Aufruf: dotnet run -c Release -- --qualitaet [Anzahl Zufallsformen]
 */
internal static partial class Program
{
    /** Ungedeckte Flaeche in Prozent. "Da fehlt Gras" ist genau das. */
    private const double GrenzeUngedeckt = 0.05;
    /** CS2 verwirft Flaechen mit kuerzeren Kanten - sie werden unsichtbar. */
    private const double GrenzeMindestkante = 0.375;
    /** Ab hier fuehlt sich die Vorschau nach Haenger an. */
    private const long GrenzeBauzeitMs = 5000;

    private sealed class Befund
    {
        internal string Name;
        internal long Zeit;
        internal int Buchten;
        internal int GrasRinge;
        internal int BelagRinge;
        internal string Polygon;
        /** Fester Fall, echte Nutzerform oder Zufallsform. */
        internal string Herkunft = "Zufall";
        /** Was am EINGANGSPOLYGON auffaellt, bevor gebaut wird. */
        internal System.Collections.Generic.List<string> Eingang
            = new System.Collections.Generic.List<string>();
        /** Wie viele fehlerhafte Ringe den Arealrand beruehren. */
        internal int MaengelAmRand;
        internal int MaengelInnen;
        /**
         * Auch die GESAMTZAHL je Zone, sonst ist die Aufteilung wertlos.
         *
         * "389 innen gegen 61 am Rand" sagt nichts, wenn innen schlicht
         * zehnmal so viele Ringe liegen. Erst die Quote beantwortet die Frage
         * des Nutzers, ob der Rand besonders anfaellig ist.
         */
        internal int RingeAmRand;
        internal int RingeInnen;
        /** Fahrgassen, die spitz auf die Randstrasse zulaufen. */
        internal int SpitzeGassenEnden;
        internal int GassenGesamt;
        internal double SpitzesteEcke = 180;
        internal System.Collections.Generic.List<string> Verstoesse
            = new System.Collections.Generic.List<string>();
        /** Gemessen und angezeigt, aber nicht als Mangel gewertet. */
        internal System.Collections.Generic.List<string> Hinweise
            = new System.Collections.Generic.List<string>();
    }

    internal static int RunQualitaet(int zufallsformen)
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        /**
         * MIT ZUFAHRT RECHNEN, WIE IM SPIEL.
         *
         * Der Qualitaetslauf baute bisher OHNE Zufahrt - also einen Fall, den
         * es im Spiel nicht gibt: jeder echte Parkplatz hat mindestens eine.
         * Der Polygonlauf macht es seit dem 2026-08-18 richtig und begruendet
         * es dort ausfuehrlich (Program.Schwarm.cs); hier war es nie
         * nachgezogen.
         *
         * Aufgefallen am 2026-08-20: eine Bandpruefung fand auf `Gebaut 133`
         * einen Streifen von 0,40 x 5,60 m, im Qualitaetslauf war dieselbe
         * Form sauber. Der Unterschied war ausschliesslich die fehlende
         * Zufahrt.
         *
         * Gemessen beim Umstellen, mit derselben Geometrie:
         *     ohne Zufahrt   fest 6/6 | echt 54/133 | Ringe innen 3,4 %
         *     mit Zufahrt    fest 6/6 | echt 59/133 | Ringe innen 5,3 %
         * Die festen Faelle bleiben heil, die echten Formen werden BESSER
         * bewertet - der Test wird dabei nicht milder, die Fehlerquote je
         * Ring steigt sogar. Er misst nur endlich den richtigen Fall.
         */
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var formen = new System.Collections.Generic.List<(string Name, float2[] Site)>();
        foreach (var fall in Cases) formen.Add((fall.Name, fall.Site));
        /**
         * DIE L-FORM AUS DEM PROTOTYP-DEBUG DES NUTZERS (2026-08-20).
         *
         * Sie steht hier als FESTER Fall, weil sie der Massstab ist, an dem
         * der Nutzer Mod und Prototyp vergleicht - und weil sie als einzige
         * Form ueberhaupt die zu enge Verbindungsstrasse zeigt: von 133
         * echten Bauten und den vier festen Faellen loest keiner die Regel
         * aus, diese schon (2,99 m freier Streifen bei 5,9 m Buchttiefe).
         *
         * Achsparallel und gross genug fuer zwei Fahrgassen je Schenkel -
         * genau der Fall, den die freihaendig gezogenen Formen des Nutzers
         * nie treffen, weil sie immer ein paar Grad schief stehen.
         */
        formen.Add(("L-Prototyp", new[]
        {
            new float2(-45.545136f, -28.210793f),
            new float2(132.066062f, -28.210793f),
            new float2(132.066062f, 37.522440f),
            new float2(60f, 37.522440f),
            new float2(60f, 85.786789f),
            new float2(-45.545136f, 85.786789f),
        }));
        /**
         * DIESELBE L-FORM, NACH AUSSEN GEZOGEN (Debug des Nutzers, 11:40 UTC).
         *
         * Er hat die linke und die obere linke Kante nach aussen geschoben.
         * Damit wird der Mangel drastisch: die Verbindungsstrasse bei x=45,3
         * und die Randstrasse bei x=49,6 UEBERLAPPEN sich um 0,70 m - zwei
         * Fahrbahnen uebereinander. Bei der kleineren Fassung waren es noch
         * 2,99 m Abstand.
         */
        formen.Add(("L-Prototyp gross", new[]
        {
            new float2(-65.7f, -31f), new float2(120f, -31f),
            new float2(120f, 45f), new float2(60f, 45f),
            new float2(60f, 90f), new float2(-65.7f, 90f),
        }));
        var festeFaelle = formen.Count;
        /**
         * DIE ECHTEN FORMEN DES NUTZERS ZUERST.
         *
         * Die Zufallsformen sind sternfoermige Kleckse um einen Mittelpunkt -
         * 4 bis 8 Ecken, Radius 35 bis 130 m. Keine L-Form, kein langgezogener
         * Schlauch, kein enger Hals. Der Nutzer zeichnet frei Hand und trifft
         * genau die Faelle, die dort fehlen.
         *
         * Sein Bauprotokoll haelt jedes gezogene Polygon mit voller
         * Genauigkeit - das ist die ehrlichste Formenquelle, die es gibt.
         * Fehlt die Datei (anderer Rechner, frische Installation), laeuft der
         * Test einfach ohne sie.
         */
        formen.AddRange(FormenAusBauprotokoll());
        _schwarmSeed = 12345;
        for (var i = 0; i < zufallsformen; i++)
            formen.Add(($"Zufall {i,3}", SchwarmForm(i)));

        Console.WriteLine($"QUALITAETSTEST ueber {formen.Count} Formen, "
            + "Modus edge (wie im Spiel)");
        Console.WriteLine();

        var befunde = new System.Collections.Generic.List<Befund>();
        for (var i = 0; i < formen.Count; i++)
        {
            var befund = PruefeForm(formen[i].Name, formen[i].Site, einstellungen,
                                    feinesRaster: i < festeFaelle);
            befund.Herkunft = i < festeFaelle ? "fest"
                : formen[i].Name.StartsWith("Gebaut") ? "echt" : "Zufall";
            befunde.Add(befund);
        }

        var schlecht = befunde.Where(b => b.Verstoesse.Count != 0).ToList();
        foreach (var b in schlecht)
        {
            Console.Error.WriteLine($"  FEHLER {b.Name}: "
                + string.Join(" | ", b.Verstoesse));
            // Ohne das Polygon ist ein Fehler auf einer Zufallsform nicht
            // untersuchbar. So laesst sich jeder Fall sofort mit --polygon
            // nachrechnen.
            Console.Error.WriteLine($"         --polygon \"{b.Polygon}\"");
        }
        var mitHinweis = befunde.Where(b => b.Hinweise.Count != 0).ToList();
        if (mitHinweis.Count != 0)
            Console.WriteLine($"  Zurueckgestellt: {mitHinweis.Count} Formen mit "
                + "Buchten ohne Zufahrt (bewertet wird das nicht)");

        Console.WriteLine();
        Console.WriteLine($"  {befunde.Count - schlecht.Count} von {befunde.Count} "
            + "Formen ohne Beanstandung");
        var zeiten = befunde.Select(b => b.Zeit).OrderBy(x => x).ToArray();
        Console.WriteLine($"  Bauzeit Median {zeiten[zeiten.Length / 2]} ms, "
            + $"schlimmste {zeiten.Last()} ms");
        // Zersplitterung ist kein Fehler, aber die Zahl, um die es dem Nutzer
        // geht: wenige grosse Flaechen statt vieler kleiner.
        var ringe = befunde.Select(b => (double)b.GrasRinge).OrderBy(x => x).ToArray();
        Console.WriteLine($"  Grasflaechen je Form: Median {ringe[ringe.Length / 2]:F0}, "
            + $"schlimmste {ringe.Last():F0}");

        /**
         * DAS URTEIL HAENGT AN DEN FESTEN FAELLEN.
         *
         * Ansage des Nutzers am 2026-08-18: "Prioritaet haben immer die 4
         * festen Faelle. Das sind die Formen, wo ich weiss, die muessen
         * funktionieren." Bei seinen gebauten Formen weiss er selbst nicht, ob
         * die Eingabe sauber war - deshalb sind sie Bericht, kein Urteil.
         */
        Console.WriteLine();
        foreach (var gruppe in new[] { "fest", "echt", "Zufall" })
        {
            var teil = befunde.Where(b => b.Herkunft == gruppe).ToList();
            if (teil.Count == 0) continue;
            var heil = teil.Count(b => b.Verstoesse.Count == 0);
            var rand = teil.Sum(b => b.MaengelAmRand);
            var innen = teil.Sum(b => b.MaengelInnen);
            var krummeEingabe = teil.Count(b => b.Eingang.Count != 0);
            var ringeRand = teil.Sum(b => b.RingeAmRand);
            var ringeInnen = teil.Sum(b => b.RingeInnen);
            Console.WriteLine($"  {gruppe,-7} {heil,3}/{teil.Count,-3} ohne Mangel"
                + $" | Rand {rand,4}/{ringeRand,-5} fehlerhaft "
                + $"({100.0 * rand / Math.Max(ringeRand, 1),4:F1} %)"
                + $" | innen {innen,4}/{ringeInnen,-5} "
                + $"({100.0 * innen / Math.Max(ringeInnen, 1),4:F1} %)"
                + $" | {krummeEingabe} auffaellige Eingaben");
        }
        var gassen = befunde.Sum(b => b.GassenGesamt);
        var spitz = befunde.Sum(b => b.SpitzeGassenEnden);
        Console.WriteLine($"  Fahrgassen: {gassen} gesamt, {spitz} laufen spitzer "
            + $"als 30 Grad aus ({100.0 * spitz / Math.Max(gassen, 1):F1} %), "
            + $"spitzeste Ecke {befunde.Min(b => b.SpitzesteEcke):F1} Grad");
        // Die Kappzaehler des ALTEN Rechenwegs sind mit ihm entfallen
        // (2026-09-01). Sie standen zuletzt konstant auf 0 - eine Zeile, die
        // nur noch Nullen meldet, liest man irgendwann als "alles gut".
        // Womit die auffaelligen Eingaben auffallen - sonst bleibt es bei
        // "irgendwas stimmt da nicht".
        var eingangsArten = befunde.SelectMany(b => b.Eingang)
            .Select(e => e.Split(' ')[0]).GroupBy(x => x)
            .OrderByDescending(g => g.Count());
        if (eingangsArten.Any())
            Console.WriteLine("  Eingangspolygone: "
                + string.Join(" | ", eingangsArten.Select(g => $"{g.Key} {g.Count()}x")));

        var festeSchlecht = befunde
            .Where(b => b.Herkunft == "fest" && b.Verstoesse.Count != 0).ToList();
        Console.WriteLine();
        if (festeSchlecht.Count == 0)
        {
            Console.WriteLine("QUALITAETSTEST BESTANDEN (die 4 festen Faelle sind heil)");
            if (schlecht.Count != 0)
                Console.WriteLine($"  Hinweis: {schlecht.Count} andere Formen haben "
                    + "Maengel - siehe oben, sie zaehlen nicht ins Urteil.");
            return 0;
        }
        Console.Error.WriteLine("QUALITAETSTEST GESCHEITERT: "
            + $"{festeSchlecht.Count} der festen Faelle haben Maengel ("
            + string.Join(", ", festeSchlecht.Select(b => b.Name)) + ")");
        return 1;
    }

    /** Jedes je gezogene Polygon aus `ParkingLotTool-builds.jsonl`. */
    private static System.Collections.Generic.List<(string Name, float2[] Site)>
        FormenAusBauprotokoll()
    {
        var raus = new System.Collections.Generic.List<(string, float2[])>();
        // Die Mod-Toolchain reicht das echte Spielprofil bereits explizit
        // durch. Unter einem getrennten Testkonto zeigte GetFolderPath sonst
        // auf ein leeres Profil und der Lauf meldete 0 statt der gemessenen
        // 133 echten Formen.
        var spielprofil = Environment.GetEnvironmentVariable("CSII_USERDATAPATH");
        var pfad = string.IsNullOrWhiteSpace(spielprofil)
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
                "Logs", "ParkingLotTool-builds.jsonl")
            : System.IO.Path.Combine(
                spielprofil, "Logs", "ParkingLotTool-builds.jsonl");
        if (!System.IO.File.Exists(pfad)) return raus;

        var nummer = 0;
        foreach (var zeile in System.IO.File.ReadAllLines(pfad))
        {
            var start = zeile.IndexOf("\"polygon\":[[", StringComparison.Ordinal);
            if (start < 0) continue;
            start = zeile.IndexOf('[', start + 10);
            var ende = zeile.IndexOf("]]", start, StringComparison.Ordinal);
            if (start < 0 || ende < 0) continue;
            var roh = zeile.Substring(start + 1, ende - start - 1);
            var punkte = new System.Collections.Generic.List<float2>();
            foreach (var paar in roh.Split(new[] { "],[" }, StringSplitOptions.None))
            {
                var teile = paar.Trim('[', ']').Split(',');
                if (teile.Length != 2) continue;
                if (float.TryParse(teile[0],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(teile[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var y))
                    punkte.Add(new float2(x, y));
            }
            // Unter drei Ecken ist es kein Areal; solche Zeilen stammen aus
            // abgebrochenen Zuegen.
            if (punkte.Count >= 3)
                raus.Add(($"Gebaut {++nummer,3}", punkte.ToArray()));
        }
        return raus;
    }


    /**
     * WAS AM EINGANGSPOLYGON AUFFAELLT, BEVOR ueberhaupt gebaut wird.
     *
     * Ansage des Nutzers am 2026-08-18: bei seinen 93 gebauten Formen weiss er
     * selbst nicht, wie sie geformt sind - ob sich Linien ueberschneiden, ob zu
     * wenige oder zu viele Punkte drin sind, ob Punkte zu nah beieinander
     * liegen. "Das kann wirklich zu Problemen fuehren." Ein Mangel auf einer
     * kaputten Eingabe ist etwas anderes als einer auf einer sauberen.
     *
     * Die Schranken kommen aus CS2: unter 0,375 m verwirft das Spiel Kanten,
     * und ein Innenwinkel unter 10 Grad ist eine Nadel, an der jede
     * Flaechenzerlegung leidet.
     */
    private static System.Collections.Generic.List<string> PruefeEingang(float2[] site)
    {
        var notiz = new System.Collections.Generic.List<string>();
        if (site.Length < 3) { notiz.Add("weniger als 3 Ecken"); return notiz; }
        if (SelfIntersectionCount(site) > 0) notiz.Add("Linien ueberschneiden sich");
        if (HasNearDuplicatePoints(site)) notiz.Add("doppelte Punkte");

        var kuerzeste = double.MaxValue;
        var spitzeste = 180.0;
        for (var i = 0; i < site.Length; i++)
        {
            var vor = site[(i + site.Length - 1) % site.Length];
            var hier = site[i];
            var nach = site[(i + 1) % site.Length];
            kuerzeste = Math.Min(kuerzeste, Abstand(hier, nach));
            var a = new double2(vor.x - hier.x, vor.y - hier.y);
            var b = new double2(nach.x - hier.x, nach.y - hier.y);
            var la = Math.Sqrt(a.x * a.x + a.y * a.y);
            var lb = Math.Sqrt(b.x * b.x + b.y * b.y);
            if (la < 1e-9 || lb < 1e-9) continue;
            var cos = (a.x * b.x + a.y * b.y) / (la * lb);
            cos = Math.Max(-1, Math.Min(1, cos));
            spitzeste = Math.Min(spitzeste, Math.Acos(cos) * 180 / Math.PI);
        }
        if (kuerzeste < 0.375) notiz.Add($"Kante nur {kuerzeste:F3} m");
        else if (kuerzeste < 3) notiz.Add($"kurze Kante {kuerzeste:F2} m");
        if (spitzeste < 10) notiz.Add($"Nadelecke {spitzeste:F1} Grad");
        if (site.Length > 12) notiz.Add($"{site.Length} Ecken");
        return notiz;
    }

    /** Abstand eines Punktes zum Rand eines Polygons. */
    private static double AbstandZumRand(float2 p, float2[] polygon)
    {
        var best = double.MaxValue;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            double abx = b.x - a.x, aby = b.y - a.y;
            var l2 = abx * abx + aby * aby;
            if (l2 < 1e-12) l2 = 1;
            var t = ((p.x - a.x) * abx + (p.y - a.y) * aby) / l2;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            var dx = p.x - (a.x + abx * t);
            var dy = p.y - (a.y + aby * t);
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }

    /**
     * SITZT DER MANGEL AM RAND ODER INNEN?
     *
     * Vermutung des Nutzers am 2026-08-18: alle anderen Flaechen liegen
     * ZWISCHEN Strassen und sind dadurch sauber begrenzt. Die Randform liegt
     * zwischen Strasse und der frei gezogenen Hauptlinie - dort trifft
     * gerechnete Geometrie auf die Handzeichnung. Wenn das stimmt, muessen die
     * Maengel dort gehaeuft sitzen.
     */
    private static bool BeruehrtRand(float2[] ring, float2[] site)
    {
        foreach (var p in ring)
            if (AbstandZumRand(p, site) < 0.5) return true;
        return false;
    }

    private static Befund PruefeForm(string name, float2[] site,
                                     LayoutSettings einstellungen,
                                     bool feinesRaster = false)
    {
        var befund = new Befund
        {
            Name = name,
            Polygon = string.Join(";", site.Select(p =>
                $"{p.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},"
                + p.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture))),
        };
        ParkingLayout layout;
        var uhr = Stopwatch.StartNew();
        try
        {
            layout = ParkingGeometry.Build(site, einstellungen);
        }
        catch (Exception ex)
        {
            uhr.Stop();
            befund.Zeit = uhr.ElapsedMilliseconds;
            befund.Verstoesse.Add($"Absturz: {ex.GetType().Name} {ex.Message}");
            return befund;
        }
        uhr.Stop();
        befund.Zeit = uhr.ElapsedMilliseconds;
        befund.Buchten = layout.Stalls;
        befund.GrasRinge = layout.GrassSurface.Length;
        befund.BelagRinge = layout.AsphaltSurface.Length;

        void Pruefe(bool gut, string text)
        {
            if (!gut) befund.Verstoesse.Add(text);
        }

        befund.Eingang.AddRange(PruefeEingang(site));

        /**
         * WIE OFT LAEUFT EINE FAHRGASSE SPITZ AUF DIE RANDSTRASSE ZU?
         *
         * Vermutung des Nutzers am 2026-08-18 mit Skizze: die Fahrgasse endet
         * heute auf Gehrung entlang der Randstrassenkante. Je spitzer der
         * Winkel zwischen beiden, desto duenner laeuft die Ecke aus - und ein
         * Keil unter CS2s Mindestkante wird nicht gebaut, es bleibt eine
         * Luecke im Belag. Sein Vorschlag: die Fahrgasse rechtwinklig kappen
         * und den Rest der Randstrasse zuschlagen.
         *
         * Gezaehlt wird die spitzeste Ecke jedes Fahrgassen-Vierecks. Unter
         * 30 Grad laeuft die Flaeche sichtbar aus.
         */
        befund.GassenGesamt = layout.AisleQuad.Length;
        foreach (var quad in layout.AisleQuad)
        {
            var spitzeste = 180.0;
            for (var i = 0; i < quad.Length; i++)
            {
                var vor = quad[(i + quad.Length - 1) % quad.Length];
                var hier = quad[i];
                var nach = quad[(i + 1) % quad.Length];
                var a = new double2(vor.x - hier.x, vor.y - hier.y);
                var b = new double2(nach.x - hier.x, nach.y - hier.y);
                var la = Math.Sqrt(a.x * a.x + a.y * a.y);
                var lb = Math.Sqrt(b.x * b.x + b.y * b.y);
                if (la < 1e-9 || lb < 1e-9) continue;
                var cos = (a.x * b.x + a.y * b.y) / (la * lb);
                cos = Math.Max(-1, Math.Min(1, cos));
                spitzeste = Math.Min(spitzeste, Math.Acos(cos) * 180 / Math.PI);
            }
            befund.SpitzesteEcke = Math.Min(befund.SpitzesteEcke, spitzeste);
            if (spitzeste < 30) befund.SpitzeGassenEnden++;
        }

        // Jeden fehlerhaften Ring einzeln einordnen: liegt er am Arealrand
        // oder innen? Siehe BeruehrtRand.
        void PruefeRinge(float2[][] ringe, string was)
        {
            foreach (var ring in ringe)
            {
                var einzeln = new[] { ring };
                var schlecht = ShortestEdge(einzeln) < GrenzeMindestkante - 1e-6
                    || MinimumMaterialBottleneck(einzeln)
                        < ParkingGeometry.SurfaceNeckLimit - 1e-6
                    || SelfIntersectionCount(ring) > 0
                    || HasNearDuplicatePoints(ring);
                var amRand = BeruehrtRand(ring, site);
                if (amRand) befund.RingeAmRand++; else befund.RingeInnen++;
                if (!schlecht) continue;
                if (amRand) befund.MaengelAmRand++;
                else befund.MaengelInnen++;
            }
        }

        // 1. Fehlende Flaeche. Das Leitsymptom des Nutzers am 2026-08-17.
        var ungedeckt = CoverGap(site, layout, einstellungen);
        Pruefe(ungedeckt.Percent < GrenzeUngedeckt,
            $"ungedeckt {ungedeckt.Percent:F2} %");

        /**
         * VERBINDUNGSSTRASSE ZU NAH AN EINER PARALLELEN FAHRBAHN.
         *
         * Ansage des Nutzers am 2026-08-20, an seinem Prototyp-Debug belegt:
         * bei der L-Form lief eine Verbindungsstrasse 2,99 m frei neben der
         * senkrechten Randstrasse her. In 2,99 m passt keine Bucht (5,9 m
         * tief) - der Streifen trug auf 90 m Laenge ganze 4 Buchten. Die
         * Strasse kostet dort Flaeche und einen Kontaktpunkt, ohne etwas zu
         * erschliessen.
         *
         * `ResolveCrossRouteConflicts` prueft Verbindungsstrassen bisher NUR
         * gegeneinander (Abstand `Cr`), nie gegen eine Randstrasse - deshalb
         * fiel das nie auf.
         */
        /**
         * NACKTE STREIFEN - die Fehlerklasse, um die es die ganze Zeit geht.
         *
         * `CoverGap` oben tastet auf 0,5 m ab und sieht einen 0,4 m schmalen
         * Streifen grundsaetzlich nie. Am 2026-08-20 wurde das teuer: ein
         * Patch erzeugte auf `Gebaut 18` einen Streifen von 0,40 x 31,50 m,
         * und der Qualitaetslauf meldete unveraendert 53/133 - er konnte ihn
         * gar nicht sehen.
         *
         * Das feine Raster kostet das 25-fache an Tastpunkten. Ueber 139
         * Formen waere das zu langsam fuer den Standardlauf, deshalb laeuft es
         * nur ueber die FESTEN Faelle - die sechs, an denen das Urteil ohnehin
         * haengt.
         *
         * Gemeldet wird nur, was wirklich eine Narbe ist: laenger als 5 m UND
         * schmaler als 1 m. Ein kompaktes vergessenes Eck von 2 x 2 m ist ein
         * anderes Problem und faellt schon dem groben Raster auf.
         */
        if (feinesRaster)
        {
            var streifen = LangsterSchmalerFleck(site, layout, einstellungen);
            Pruefe(streifen == null, $"nackter Streifen {streifen}");
        }

        var querNaehe = EngsteQuerNaehe(layout, einstellungen);
        Pruefe(double.IsPositiveInfinity(querNaehe.Frei)
               || querNaehe.Frei >= einstellungen.Sl - 0.01,
            $"Verbindungsstrasse {querNaehe.Frei:F2} m neben paralleler Fahrbahn "
            + $"(unter Buchttiefe {einstellungen.Sl:F2} m)");

        // 2. Buchten muessen brauchbar sein.
        Pruefe(layout.Stalls > 0, "keine einzige Bucht");
        Pruefe(OverlapCount(layout.Bay) == 0, "Buchten ueberlappen");
        Pruefe(BayOnRoad(layout, einstellungen) == 0, "Bucht auf der Fahrbahn");
        Pruefe(EndsOutside(layout, site) == 0, "Bucht ragt aus dem Areal");
        /**
         * BUCHT OHNE ZUFAHRT ist bewusst KEIN Mangel.
         *
         * Ansage des Nutzers am 2026-08-17: er kennt den Fall und fuehrt ihn
         * auf schiefe Fahrgassen und Verbindungsstrassen zurueck, an die sich
         * nicht anschliessen laesst. Er hat ihn zurueckgestellt, solange die
         * ungedeckten Flaechen offen sind.
         *
         * Gemessen und angezeigt wird er weiter - zurueckgestellt ist nicht
         * dasselbe wie unsichtbar.
         */
        var ohneZufahrt = UnservedBays(layout);
        if (ohneZufahrt != 0)
            befund.Hinweise.Add($"{ohneZufahrt} Bucht(en) ohne Zufahrt");

        // 3. Flaechen muessen von CS2 baubar sein. Kanten unter 0,375 m
        //    verwirft das Spiel - die Flaeche wird unsichtbar, es entsteht
        //    eine Luecke. Genau das sah der Nutzer als "Streifen mit Leere".
        var gras = layout.GrassSurface;
        var belag = layout.AsphaltSurface;
        Pruefe(ShortestEdge(gras) >= GrenzeMindestkante - 1e-6,
            $"Graskante {ShortestEdge(gras):F3} m unter {GrenzeMindestkante} m");
        Pruefe(ShortestEdge(belag) >= GrenzeMindestkante - 1e-6,
            $"Belagkante {ShortestEdge(belag):F3} m unter {GrenzeMindestkante} m");
        Pruefe(MinimumMaterialBottleneck(gras)
            >= ParkingGeometry.SurfaceNeckLimit - 1e-6,
            $"Gras-Engstelle {MinimumMaterialBottleneck(gras):F3} m");
        Pruefe(MinimumMaterialBottleneck(belag)
            >= ParkingGeometry.SurfaceNeckLimit - 1e-6,
            $"Belag-Engstelle {MinimumMaterialBottleneck(belag):F3} m");
        PruefeRinge(gras, "Gras");
        PruefeRinge(belag, "Belag");

        // 4. Formfehler, die CS2 gar nicht erst annimmt.
        Pruefe(gras.Sum(SelfIntersectionCount) + belag.Sum(SelfIntersectionCount) == 0,
            "Flaeche ueberschneidet sich selbst");
        Pruefe(gras.Count(HasNearDuplicatePoints)
             + belag.Count(HasNearDuplicatePoints) == 0,
            "Flaeche hat doppelte Punkte");

        /**
         * 5. Materialien duerfen einander nicht ueberdecken - sonst blitzt im
         *    Spiel die untere Flaeche durch.
         *
         * Die Messung triangliert dafuer jeden Ring und wirft, wenn ihr das
         * nicht gelingt. Das ist selbst ein Befund und kein Testfehler: was
         * sich hier nicht zerlegen laesst, zerlegt CS2 auch nicht - die
         * Flaeche bliebe im Spiel unsichtbar.
         */
        try
        {
            var materialKonflikt = MeasureMaterialConflict(layout);
            Pruefe(materialKonflikt < 0.005,
                $"Belag auf Gras {materialKonflikt:F3} m2");
        }
        catch (Exception ex)
        {
            befund.Verstoesse.Add($"Flaeche nicht triangulierbar: {ex.Message}");
        }
        try
        {
            var strassenKonflikt = MeasureRoadConflict(layout).Total;
            Pruefe(strassenKonflikt < 0.005,
                $"Strasse auf Strasse {strassenKonflikt:F3} m2");
        }
        catch (Exception ex)
        {
            befund.Verstoesse.Add($"Fahrbahn nicht triangulierbar: {ex.Message}");
        }

        // 6. Vorschau-Zeit ist Nutzererlebnis - und ein Zeitabbruch in der
        //    Materialreparatur laesst die Flaechen unzusammengefasst.
        Pruefe(befund.Zeit < GrenzeBauzeitMs, $"{befund.Zeit} ms Bauzeit");

        return befund;
    }
}
