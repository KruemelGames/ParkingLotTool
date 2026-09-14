using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * HAENGT DAS ZONING-STRASSENNETZ ZUSAMMEN?
     *
     * Der Nutzer hat am 2026-09-03 die Wasser- und Abwasseransicht
     * aufgerufen und mitten im Parkplatz eine Luecke in beiden Leitungen
     * gefunden. Rohre laufen in CS2 in den Strassen; eine Luecke im Rohr ist
     * eine Luecke in der Strasse. Sein Log sagte es dann genau:
     *
     *   PLT-Zoningbloecke: 4 Zoning-Kante(n) ... 24,00 | 48,00 | 36,00 | 12,00
     *   PLT-Zoningstrasse: 2 von 2 Strassenzuege
     *
     * Vier Kanten, aber ZWEI Strassenzuege - also zwei Teile, die einander
     * nicht beruehren.
     *
     * Der Fall entsteht erst bei MEHREREN verschieden grossen Flaechen; die
     * bisherigen Zoningtests messen immer nur eine. Deshalb dieser Lauf.
     * Gepruefte Groesse ist nicht die Laenge, sondern die ZUSAMMENHANGSZAHL:
     * alle Stuecke eines Parkplatzes muessen einen einzigen Zug bilden.
     */
    private static int RunZoningnetz()
    {
        // Das Areal des Nutzers aus dem Bauprotokoll PLT-0A18BA8B.
        // Das Areal aus dem Bauprotokoll PLT-E24B7149.
        var site = new[]
        {
            new float2(-1037.0200f, 118.68977f),
            new float2(-1180.9188f, 123.62625f),
            new float2(-1184.6006f, 16.356247f),
            new float2(-1040.7003f, 11.417195f),
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

        var mitte = float2.zero;
        foreach (var p in site) mitte += p;
        mitte /= site.Length;

        /** Ecke einer Flaeche aus Rasterkoordinaten relativ zur Mitte. */
        ParkingGeometry.Zoningflaeche Flaeche(
            int spaltenVersatz, int reihenVersatz, int spalten, int reihen,
            double winkel)
        {
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(winkel);
            var kachel = (float)ParkingGeometry.Zoningparzelle;
            return new ParkingGeometry.Zoningflaeche
            {
                Ecke = mitte
                    + laengs * (spaltenVersatz * kachel)
                    + quer * (reihenVersatz * kachel),
                Spalten = spalten,
                Reihen = reihen,
                Winkel = winkel,
            };
        }

        var fehler = 0;
        foreach (var fall in Zoningnetzfaelle(Flaeche, site))
        {
            var s = Grund();
            s.Zoningflaechen = fall.Flaechen;
            s.Randzoning = fall.Rand;
            var bau = ParkingGeometry.Build(site, s);

            /*
             * IM RANDZONING-ABSCHNITT DARF KEINE RANDSTRASSE MEHR LIEGEN.
             *
             * Ansage des Nutzers: *"Die Randstrasse in dem Bereich wird
             * ERSETZT durch eine Zoning-Strasse."* Zwei Strassen an derselben
             * Stelle waeren genau das, was er nicht will - also wird beides
             * gezaehlt: was dort noch Randstrasse ist (muss null sein) und
             * was dort Zoning-Strasse geworden ist (muss etwas sein).
             */
            var randRest = 0.0;
            var randNeu = 0.0;
            if (fall.Rand.Length > 0)
                foreach (var n in bau.NetLine)
                {
                    if (!ParkingGeometry.RandzoningEnthaelt(
                            fall.Rand, n.A, n.B)) continue;
                    var laenge = math.distance(n.A, n.B);
                    if (string.Equals(n.Kind, "perimeter",
                        StringComparison.Ordinal)) randRest += laenge;
                    else if (string.Equals(n.Kind, "zoning",
                        StringComparison.Ordinal)) randNeu += laenge;
                }

            var stuecke = bau.NetLine
                .Where(n => string.Equals(n.Kind, "zoning",
                    StringComparison.Ordinal))
                .Select(n => (A: n.A, B: n.B))
                .ToArray();

            var teile = EndpunktZusammenhangsteile(stuecke);
            var tKreuzungen = TKreuzungen(stuecke);
            var fahrbahnpaare = ZoningFahrbahnueberlappungen(bau.NetLine);
            var laengen = string.Join(" | ", stuecke
                .Select(st => math.distance(st.A, st.B).ToString("F1"))
                .OrderBy(t => t));

            /*
             * LIEGT ASPHALT AUF FREMDEM BAULAND?
             *
             * Der Nutzer am 2026-09-03: *"Eine Zoningstrassenflaeche, also
             * Asphalt, geht weiterhin in die Tiles."* An einer Innenecke
             * ragt der Strassenkorridor der einen Flaeche ueber die
             * Parzellen der anderen. Der Einzelflaechentest kann das nicht
             * sehen - dort gibt es keinen Nachbarn.
             */
            /*
             * ENTSTEHT UEBERHAUPT ZONINGSTRASSEN-BELAG?
             *
             * *"Es wurde wieder den echten Strassen keine Asphaltflaeche
             * gegeben."* Die Zoning-Strasse ist unsichtbar; ohne Belag saehe
             * man Autos ueber Gras fahren. Null Quadratmeter bei vorhandenen
             * Strassenstuecken ist deshalb immer ein Befund.
             */
            var strassenbelag = RingFlaeche(bau.ZoningRoadSurface);

            var asphaltAufParzellen = 0.0;
            foreach (var f in fall.Flaechen)
            {
                var parzellen = ParkingGeometry.ZoningEcken(f);
                asphaltAufParzellen += (bau.ZoningRoadSurface
                        ?? Array.Empty<float2[]>())
                    .SelectMany(TriangulateMaterialRegion)
                    .Sum(d => d.Weight * ConvexOverlapArea(d.Points, parzellen));
            }

            /*
             * LIEGEN DIE SONDERPLAETZE AN DER ZONINGFLAECHE?
             *
             * Ansage des Nutzers am 2026-09-03, mit der Reihenfolge auf
             * Nachfrage: *"Zuerst ZF, danach Fusswege, danach
             * Ein-/Ausfahrt."* Der Fussweg dieses Laufs liegt an Kante 0, die
             * Flaechen liegen woanders - wenn die Sonderplaetze jetzt naeher
             * an einer Flaechenmitte sind als am Fussweg, hat der Vorrang
             * gegriffen.
             *
             * Gemessen wird der NAECHSTE Sonderplatz, nicht der fernste: die
             * Kette aus Behinderten- und E-Plaetzen ist ueber 30 m lang, ihr
             * fernes Ende kann nie am Ziel liegen. Dieselbe Begruendung wie
             * im Sonderplaetze-Lauf.
             */
            double AbstandZu(float2 ziel, float2[] bucht)
                => math.distance(new float2(bucht.Average(p => p.x),
                    bucht.Average(p => p.y)), ziel);

            var zfMitten = fall.Flaechen
                .Select(ParkingGeometry.ZoningMitte).ToArray();
            var wegAnker = bau.EntranceLine
                .Select(linie => linie[linie.Length - 1]).ToArray();
            var sonder = bau.Bay
                .Where((_, i) => bau.BayRole[i] == BayRole.Disabled
                    || bau.BayRole[i] == BayRole.Electric)
                .ToArray();

            // Ohne innere Flaeche gibt es kein ZF-Ziel - beim reinen
            // Randzoning ist das der Normalfall, kein Fehler.
            var zuZf = sonder.Length == 0 || zfMitten.Length == 0
                ? double.NaN
                : sonder.Min(b => zfMitten.Min(z => AbstandZu(z, b)));
            var zuWeg = sonder.Length == 0 || wegAnker.Length == 0
                ? double.NaN
                : sonder.Min(b => wegAnker.Min(a => AbstandZu(a, b)));

            // Ohne Sonderplaetze gibt es nichts zu pruefen - ein kleiner
            // Parkplatz bekommt keine.
            var sonderFalsch = sonder.Length > 0 && !double.IsNaN(zuWeg)
                && !double.IsNaN(zuZf)
                && zuZf > zuWeg;

            /*
             * KEINE BUCHT IM RANDZONING-ABSCHNITT.
             *
             * Der Grund vom Nutzer: *"Wir haben doch aussen an der
             * Randstrasse Parkbuchten. Wieso sollten wir auf diesen
             * Parkbuchten die Tiles erstellen? Dann haetten wir Parkbuchten,
             * die unter Haeusern stehen."*
             *
             * Gezaehlt wird, was naeher als die Randstrasse an der gewaehlten
             * Linie liegt - also die Randreihe. Innere Buchten sind weiter
             * weg und zaehlen nicht mit.
             */
            var buchtenImAbschnitt = 0;
            if (fall.Rand.Length > 0)
                foreach (var bucht in bau.Bay)
                {
                    var bm = new float2(bucht.Average(p => p.x),
                        bucht.Average(p => p.y));
                    foreach (var linie in fall.Rand)
                    {
                        var spanne = linie.B - linie.A;
                        var laenge = math.length(spanne);
                        if (laenge < 1e-3f) continue;
                        var r = spanne / laenge;
                        var w = bm - linie.A;
                        var laengs = math.dot(w, r);
                        if (laengs < 0f || laengs > laenge) continue;
                        // Die Randstrasse beginnt bei 6,9 m; alles davor ist
                        // Randreihe und Gruen.
                        if (math.abs(r.x * w.y - r.y * w.x) > 6.9f) continue;
                        buchtenImAbschnitt++;
                        break;
                    }
                }

            /*
             * LAEUFT NOCH EIN WEG DURCH DAS NEUE BAULAND?
             *
             * Der vierte Teil der Ansage: *"Die Invisible Paths schneiden
             * wieder ab."* Gezaehlt wird alles, was im Aussenband liegt -
             * also naeher an der Linie als die Randstrasse - und KEINE
             * Zufahrt ist. Zufahrten duerfen bleiben: sie fuehren zur
             * Stadtstrasse, und ohne sie waere der Parkplatz abgehaengt.
             */
            var wegeImBauland = 0.0;
            if (fall.Rand.Length > 0)
                foreach (var n in bau.NetLine)
                {
                    if (string.Equals(n.Kind, "entrance",
                        StringComparison.Ordinal)) continue;
                    if (string.Equals(n.Kind, "zoning",
                        StringComparison.Ordinal)) continue;
                    var m = (n.A + n.B) * 0.5f;
                    foreach (var linie in fall.Rand)
                    {
                        var spanne = linie.B - linie.A;
                        var laenge = math.length(spanne);
                        if (laenge < 1e-3f) continue;
                        var r = spanne / laenge;
                        var w = m - linie.A;
                        var laengs = math.dot(w, r);
                        if (laengs < 0f || laengs > laenge) continue;
                        if (math.abs(r.x * w.y - r.y * w.x) > 6.9f) continue;
                        wegeImBauland += math.distance(n.A, n.B);
                        break;
                    }
                }

            var randFalsch = fall.Rand.Length > 0
                && (randRest > 0.5 || randNeu < 1.0 || buchtenImAbschnitt > 0
                    || wegeImBauland > 0.5);

            /*
             * DIE ZUSAMMENHANGSPRUEFUNG GILT NUR FUER INNERE FLAECHEN.
             *
             * Deren Ringe muessen einen Zug bilden - das war der Fehler mit
             * den abgerissenen Rohren. Ein Randzoning-Abschnitt ist dagegen
             * ein Stueck der RANDSTRASSE; er haengt an deren restlichen
             * Stuecken, und die zaehlen hier nicht mit. Zwei Zuege sind dort
             * also kein Befund, sondern der Normalfall.
             */
            var verworfen = Cs2Annahme("Fahrbahn", bau.ZoningRoadSurface)
                + Cs2Annahme("Parzellenboden", bau.ZoningSurface);

            var schlecht = stuecke.Length == 0
                || (fall.Rand.Length == 0 && teile > 1)
                // Geometrisch beruehrt ist fuer CS2 nicht verbunden: ein Ende
                // mitten auf einer Kante braucht dort einen echten Knoten.
                || tKreuzungen > 0
                || fahrbahnpaare > 0
                || asphaltAufParzellen > 0.5 || sonderFalsch || randFalsch
                // Strassenstuecke ohne Belag: dort saehe man Autos ueber Gras
                // fahren, denn die Strasse selbst ist unsichtbar.
                || (stuecke.Length > 0 && strassenbelag < 1.0)
                // Ein Ring, den CS2 verwirft, ist im Spiel nicht da - egal
                // wieviel Flaeche der Plan ihm zuschreibt.
                || verworfen > 0;
            if (schlecht) fehler++;
            Console.WriteLine($"  {fall.Name,-28} {stuecke.Length,2} Stueck, "
                + (tKreuzungen > 0
                    ? $"{tKreuzungen} T-KREUZUNG(EN) OHNE KNOTEN, " : "")
                + (fahrbahnpaare > 0
                    ? $"{fahrbahnpaare} FAHRBAHNPAAR(E), " : "")
                + $"{teile} Zug/Zuege, Asphalt auf Parzellen "
                + $"{asphaltAufParzellen,6:F2} m2, Strassenbelag {strassenbelag,7:F0} m2, Sonderplatz zur ZF "
                + (double.IsNaN(zuZf) ? "   -  " : $"{zuZf,5:F1}")
                + " m / zum Weg "
                + (double.IsNaN(zuWeg) ? "   -  " : $"{zuWeg,5:F1}") + " m"
                + (fall.Rand.Length > 0
                    ? $", Randabschnitt: Randstrasse {randRest,5:F1} m / "
                        + $"Zoningstrasse {randNeu,5:F1} m, Buchten drin {buchtenImAbschnitt}, Wege drin {wegeImBauland,5:F1} m"
                    : string.Empty)
                + (verworfen > 0 ? $", {verworfen} Ring(e) von CS2 verworfen" : string.Empty)
                + (schlecht ? "  <-- FEHLER" : string.Empty));

            if (!schlecht) continue;
            // Bei einem Zerfall die freien Enden nennen - das ist die Stelle,
            // an der im Spiel das Rohr abreisst.
            foreach (var ende in FreieEnden(stuecke))
                Console.WriteLine($"      freies Ende bei "
                    + $"({ende.x:F1}/{ende.y:F1})");
        }

        fehler += RandzoningNutzerfall();
        fehler += RandzoningTKreuzung();
        fehler += RandzoningKnotenzerfall();
        fehler += RandzoningEingerastet();
        fehler += RandzoningTKreuzung1822();
        fehler += RandzoningFahrbahnueberlappung2304();
        fehler += ZoningTAnschlussProtokolle();

        Console.WriteLine(fehler == 0
            ? "Zoningnetz: alle Faelle haengen zusammen."
            : $"Zoningnetz: {fehler} Fall/Faelle zerfallen.");
        return fehler == 0 ? 0 : 1;
    }

    /** Nutzerbau PLT-749D1BC8: vor dem Schnitt lagen 13 Fahrbahnpaare uebereinander. */
    private static int RandzoningFahrbahnueberlappung2304()
    {
        var site = new[]
        {
            new float2(-1037.02001953125f, 118.68979644775391f),
            new float2(-1162.68408203125f, 123.00100708007813f),
            new float2(-1168.6270751953125f, -50.160003662109375f),
            new float2(-1042.9600830078125f, -54.471378326416016f),
        };
        var s = LayoutSettings.Cs2;
        s.Zellen = true;
        s.AngleMode = "edge";
        s.Auto = false;
        s.AutomaticEntrances = false;
        s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
        s.Zoningflaechen = new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1043.46484375f, -40.045379638671875f),
                Spalten = 6, Reihen = 6, Winkel = 88.04, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1041.8192138671875f, 7.92548942565918f),
                Spalten = 7, Reihen = 2, Winkel = 88.04, Rand = 8.0,
            },
        };
        s.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(-1168.6270751953125f, -50.160003662109375f),
                B = new float2(-1042.9600830078125f, -54.471378326416016f),
            },
        };

        var bau = ParkingGeometry.Build(site, s);
        var paare = ZoningFahrbahnueberlappungen(bau.NetLine);
        Console.WriteLine($"  Bau 23:04 Fahrbahnen         {paare} "
            + "ueberlappende(s) Zoning/Weg-Paar(e)"
            + (paare > 0 ? "  <-- FAHRBAHN AUF FAHRBAHN" : string.Empty));
        return paare > 0 ? 1 : 0;
    }

    /** Exakter Abstand gerader Netzkurssegmente, ohne Rasterabtastung. */
    private static int ZoningFahrbahnueberlappungen(
        IReadOnlyList<NetSegment> segmente)
    {
        var treffer = 0;
        for (var i = 0; i < segmente.Count; i++)
        for (var k = i + 1; k < segmente.Count; k++)
        {
            var a = segmente[i];
            var b = segmente[k];
            var aZoning = string.Equals(a.Kind, "zoning",
                StringComparison.Ordinal);
            var bZoning = string.Equals(b.Kind, "zoning",
                StringComparison.Ordinal);
            if (!aZoning && !bZoning) continue;
            if (aZoning && bZoning)
            {
                // Gemeinsame Endpunkte erlauben den Kreuzungsbereich, aber
                // keine zwei parallelen Fahrbahnen ueber ihre ganze Laenge.
                var geteilt = a.A.Equals(b.A) || a.A.Equals(b.B)
                    || a.B.Equals(b.A) || a.B.Equals(b.B);
                var aa = a.A; var ab = a.B;
                var ba = b.A; var bb = b.B;
                if (geteilt)
                {
                    var schutz = (float)ParkingGeometry.ZoningStrassenbreite * 1.5f;
                    bool Kuerze(ref float2 von, ref float2 bis, float2 x, float2 y)
                    {
                        var start = von.Equals(x) || von.Equals(y);
                        var ende = bis.Equals(x) || bis.Equals(y);
                        var abzug = (start ? schutz : 0) + (ende ? schutz : 0);
                        if (math.distance(von, bis) <= abzug) return false;
                        var r = math.normalize(bis - von) * schutz;
                        if (start) von += r;
                        if (ende) bis -= r;
                        return true;
                    }
                    if (!Kuerze(ref aa, ref ab, b.A, b.B)
                        || !Kuerze(ref ba, ref bb, a.A, a.B)) continue;
                }
                if (ZoningSegmentabstand(aa, ab, ba, bb)
                    < ParkingGeometry.ZoningStrassenbreite - 0.01) treffer++;
                continue;
            }
            var andere = aZoning ? b : a;
            var breite = string.Equals(andere.Kind, "cross",
                    StringComparison.Ordinal)
                ? 3.0 : 7.0;
            var grenze = (ParkingGeometry.ZoningStrassenbreite + breite) * 0.5;
            if (ZoningSegmentabstand(a.A, a.B, b.A, b.B) < grenze - 0.01)
                treffer++;
        }
        return treffer;
    }

    private static double ZoningSegmentabstand(
        float2 a, float2 b, float2 c, float2 d)
    {
        static double Kreuz(float2 x, float2 y) => x.x * y.y - x.y * y.x;
        static double Punktabstand(float2 p, float2 x, float2 y)
        {
            var delta = y - x;
            var q = math.lengthsq(delta);
            if (q <= 1e-12f) return math.distance(p, x);
            var t = math.clamp(math.dot(p - x, delta) / q, 0f, 1f);
            return math.distance(p, x + delta * t);
        }

        var ab = b - a;
        var cd = d - c;
        var nenner = Kreuz(ab, cd);
        if (Math.Abs(nenner) > 1e-9)
        {
            var ac = c - a;
            var t = Kreuz(ac, cd) / nenner;
            var u = Kreuz(ac, ab) / nenner;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return 0;
        }
        return Math.Min(
            Math.Min(Punktabstand(a, c, d), Punktabstand(b, c, d)),
            Math.Min(Punktabstand(c, a, b), Punktabstand(d, a, b)));
    }




    /**
     * DER FALL, DER GENAU DANN BRICHT, WENN DER NUTZER ES RICHTIG MACHT.
     *
     * Zwei Baue am 2026-09-05, wenige Meter auseinander:
     *
     *   01:51  ZF-Korridorachse 2,60 m neben der RZ-Achse
     *          -> 2 Planknoten mit 3 Armen, 1 Netz, aber Doppelstrassen
     *   01:52  ZF-Korridorachse 0,00 m, also GENAU darauf
     *          -> 0 Planknoten mit 3 Armen, 2 getrennte Netze
     *
     * Der zweite ist die eingerastete Lage - die, die der Nutzer haben will
     * und die die 1-Kachel-Regel erzeugt. Dort faellt die Strasse der
     * Zoningflaeche als Doppel weg, und danach findet der Bau keine
     * Einmuendung mehr.
     *
     * Gemessen wird deshalb das, was fehlt: ein Punkt, an dem DREI
     * Zoningstuecke zusammentreffen. Ohne ihn gibt es keine T-Kreuzung, und
     * ohne T-Kreuzung fliesst kein Strom.
     */
    private static int RandzoningEingerastet()
    {
        var site = new[]
        {
            new float2(-1037.020263671875f, 118.68978118896484f),
            new float2(-1173.262939453125f, 123.36361694335938f),
            new float2(-1180.0333251953125f, -73.89756774902344f),
            new float2(-1043.787353515625f, -78.57389831542969f),
        };
        var s = LayoutSettings.Cs2;
        s.Zellen = true;
        s.AngleMode = "edge";
        s.Auto = false;
        s.AutomaticEntrances = false;
        s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
        s.Zoningflaechen = new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1044.2919921875f, -64.14722442626953f),
                Spalten = 6, Reihen = 6, Winkel = 88.04, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1042.6463623046875f, -16.17644500732422f),
                Spalten = 7, Reihen = 2, Winkel = 88.04, Rand = 8.0,
            },
        };
        s.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(-1180.0333251953125f, -73.89756774902344f),
                B = new float2(-1043.787353515625f, -78.57389831542969f),
            },
        };

        var bau = ParkingGeometry.Build(site, s);
        var stuecke = bau.NetLine
            .Where(n => string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
            .Select(n => (A: n.A, B: n.B))
            .ToArray();

        // Wieviele Arme treffen sich an einem Punkt? Drei heisst T-Kreuzung.
        var arme = new Dictionary<(long, long), int>();
        foreach (var st in stuecke)
        foreach (var ende in new[] { st.A, st.B })
        {
            var k = ((long)math.round(ende.x * 100f), (long)math.round(ende.y * 100f));
            arme[k] = arme.TryGetValue(k, out var n) ? n + 1 : 1;
        }
        var kreuzungen = arme.Count(e => e.Value >= 3);
        var teile = Zusammenhangsteile(stuecke);
        var tK = TKreuzungen(stuecke);

        var schlecht = kreuzungen == 0;
        Console.WriteLine($"  Bau 01:52 eingerastet         {stuecke.Length,2} Stueck, "
            + $"{arme.Count} Knoten, {kreuzungen} mit 3+ Armen, "
            + $"{teile} Zug/Zuege, {tK} T-Kreuzung(en) ohne Knoten"
            + (schlecht ? "  <-- KEINE T-KREUZUNG IM PLAN" : string.Empty));
        if (schlecht)
            foreach (var st in stuecke)
                Console.WriteLine($"      ({st.A.x,9:F1}/{st.A.y,7:F1}) -> "
                    + $"({st.B.x,9:F1}/{st.B.y,7:F1})  {math.distance(st.A, st.B),6:F1} m");
        return schlecht ? 1 : 0;
    }

    /**
     * DER BAU VOM 2026-09-04, 19:46 - ZWEI NETZE STATT EINEM.
     *
     * Der Nutzer hatte zwei Baue kurz hintereinander. Der erste ergab
     * 6 Kanten und EIN Netz, der zweite 5 Kanten und ZWEI - und in genau dem
     * bekamen die Gebaeude an der RZ-Strasse keinen Strom und kein Wasser:
     *
     *   19:46  6 Kante(n), 7 Knoten, 1 Netz   Laengen 56,46 | 42,60 | ...
     *   19:47  5 Kante(n), 7 Knoten, 2 NETZE  Laengen ... | 109,46 | ...
     *
     * Im ersten war die RZ-Strasse an der Einmuendung GETEILT (56,46 + 42,60),
     * im zweiten lief sie als eine Kante von 109,46 m durch, und die
     * ZF-Strasse endete mitten darauf. Ohne gemeinsamen Knoten fliesst nichts.
     *
     * Die Eingaben unterscheiden sich in zwei Punkten: Winkelmodus `edge`
     * gegen `fixed`, und das Polygon ist im zweiten Bau breiter, waehrend die
     * RZ-Linie die alte blieb - sie deckt die Polygonkante also nur noch
     * teilweise ab. Dieser Fall haelt den zweiten Bau fest.
     */
    private static int RandzoningKnotenzerfall()
    {
        var site = new[]
        {
            new float2(-1037.02001953125f, 118.68977355957031f),
            new float2(-1182.1700439453125f, 123.66900634765625f),
            new float2(-1188.591064453125f, -63.42000198364258f),
            new float2(-1043.438232421875f, -68.40180969238281f),
        };
        var s = LayoutSettings.Cs2;
        s.Zellen = true;
        s.AngleMode = "fixed";
        s.Auto = false;
        s.AutomaticEntrances = false;
        s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
        s.Zoningflaechen = new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1043.9f, -54.0f),
                Spalten = 6, Reihen = 6, Winkel = 88.04, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1042.3f, -6.0f),
                Spalten = 7, Reihen = 2, Winkel = 88.04, Rand = 8.0,
            },
        };
        // Die RZ-Linie des Nutzers - sie deckt die Polygonkante NICHT ganz ab.
        s.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(-1163.2244873046875f, -64.29041290283203f),
                B = new float2(-1043.438232421875f, -68.40180969238281f),
            },
        };

        var bau = ParkingGeometry.Build(site, s);
        var stuecke = bau.NetLine
            .Where(n => string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
            .Select(n => (A: n.A, B: n.B))
            .ToArray();
        var teile = EndpunktZusammenhangsteile(stuecke);
        var tK = TKreuzungen(stuecke);
        var schlecht = tK > 0 || teile > 1;
        Console.WriteLine($"  Bau 19:46 Knotenzerfall       {stuecke.Length,2} Stueck, "
            + $"{teile} Zug/Zuege, {tK} T-Kreuzung(en) ohne Knoten"
            + (schlecht ? "  <-- KEIN GEMEINSAMER KNOTEN" : string.Empty));
        if (schlecht)
            foreach (var st in stuecke)
                Console.WriteLine($"      Stueck ({st.A.x,9:F1}/{st.A.y,7:F1}) -> "
                    + $"({st.B.x,9:F1}/{st.B.y,7:F1})  {math.distance(st.A, st.B),6:F1} m");
        return schlecht ? 1 : 0;
    }

    /**
     * DER BAU DES NUTZERS VOM 2026-09-04, 17:46 - ZF-STRASSE TRIFFT RZ-STRASSE.
     *
     * Seine Meldung: *"An der T-Kreuzung, also ZF-Strasse trifft auf
     * RZ-Strasse, wird Strom sowie Wasser/Abwasser nicht verbunden."*
     *
     * Gemessen war es keine T-Kreuzung, sondern eine LUECKE: das freie Ende
     * lag 7,95 m neben der RZ-Achse, also genau eine Kachel davor. Ursache
     * war die Einkuerzung freier Enden - sie sah die RZ-Achsen nicht, weil
     * die vorher aus der Kandidatenliste genommen werden.
     *
     * Gemessen wird deshalb genau das: der Abstand jedes freien Endes zur
     * RZ-Achse. Ein Ende, das laengs AUF der RZ-Strasse liegt und trotzdem
     * mehr als einen Zentimeter davon entfernt ist, ist die Luecke.
     */
    private static int RandzoningTKreuzung()
    {
        var site = new[]
        {
            new float2(-1037.020263671875f, 118.68978118896484f),
            new float2(-1043.2412109375f, -62.66079330444336f),
            new float2(-1183.56103515625f, -57.845176696777344f),
            new float2(-1177.3369140625f, 123.50337982177734f),
        };
        var s = LayoutSettings.Cs2;
        s.Zellen = true;
        s.AngleMode = "edge";
        s.Auto = false;
        s.AutomaticEntrances = false;
        s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
        s.Zoningflaechen = new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1043.7459716796875f, -48.23424530029297f),
                Spalten = 6, Reihen = 6, Winkel = 88.04, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1042.100341796875f, -0.2624626159667969f),
                Spalten = 7, Reihen = 2, Winkel = 88.04, Rand = 8.0,
            },
        };
        s.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(-1043.2412109375f, -62.66079330444336f),
                B = new float2(-1183.56103515625f, -57.845176696777344f),
            },
        };

        var bau = ParkingGeometry.Build(site, s);
        var stuecke = bau.NetLine
            .Where(n => string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
            .Select(n => (A: n.A, B: n.B))
            .ToArray();
        var teile = EndpunktZusammenhangsteile(stuecke);
        var tK = TKreuzungen(stuecke);

        // Die RZ-Achse: die gewaehlte Polygonkante, um die Randstrassentiefe
        // nach innen versetzt. Dieselbe Rechnung wie im Werkzeug.
        var rzA = s.Randzoning[0].A;
        var rzB = s.Randzoning[0].B;
        var d = rzB - rzA;
        var laenge = math.length(d);
        var r = d / laenge;
        var n0 = new float2(-r.y, r.x);
        var mitte = float2.zero;
        foreach (var p in site) mitte += p;
        mitte /= site.Length;
        if (math.dot(n0, mitte - rzA) < 0) n0 = -n0;
        var tiefe = (float)(s.Es + s.Sl + s.Ai / 2);
        var achseA = rzA + n0 * tiefe;

        var luecken = 0;
        var groesste = 0.0;
        foreach (var ende in FreieEnden(stuecke))
        {
            var w = ende - achseA;
            var laengs = math.dot(w, r);
            if (laengs < 0f || laengs > laenge) continue;
            var quer = math.abs(math.dot(w, n0));
            // Nur Enden, die die RZ-Strasse tatsaechlich MEINEN. Ein Ende
            // hundert Meter weiter innen gehoert zu einer anderen Flaeche und
            // ist kein Anschlussfehler. Zwoelf Meter sind anderthalb Kacheln -
            // die gemessene Luecke war eine.
            if (quer < 0.01f || quer > 12f) continue;
            luecken++;
            groesste = System.Math.Max(groesste, quer);
        }

        var schlecht = luecken > 0;
        // Die Stuecke nur im Fehlerfall - dort sagen sie, WO die Luecke
        // klafft; im Normalfall waeren sie nur Rauschen.
        if (schlecht)
            foreach (var st in stuecke)
                Console.WriteLine($"      Stueck ({st.A.x,9:F1}/{st.A.y,7:F1}) -> "
                    + $"({st.B.x,9:F1}/{st.B.y,7:F1})  {math.distance(st.A, st.B),6:F1} m");
        Console.WriteLine($"  Protokoll 17:46 T-Kreuzung    {stuecke.Length,2} Stueck, "
            + $"{teile} Zug/Zuege, {tK} T-Kreuzung(en) ohne Knoten, "
            + $"{luecken} freies Ende(n) VOR der RZ-Strasse"
            + (luecken > 0 ? $", groesste Luecke {groesste:F2} m" : string.Empty)
            + (schlecht ? "  <-- LUECKE" : string.Empty));
        return schlecht ? 1 : 0;
    }

    /**
     * DER BAU DES NUTZERS VOM 2026-09-04, 18:22 (PLT-6E2B88E8).
     *
     * Anders als der aeltere 17:46-Fall hatte dieser Bau nicht nur ein Ende
     * vor der RZ-Achse: die RZ-Strasse selbst war in 86,33 m und 38,96 m
     * zerfallen. Zwischen den freien Enden (-1092,1/-46,8) und
     * (-1053,1/-48,2) fehlten rund 39 m. Die ZF-Strasse hing nur am linken
     * Rest; deshalb meldete der Bauzettel zwei Strassenzuege.
     *
     * Geprueft werden beide Fehler getrennt: die erwartete RZ-Achse darf
     * keinen ungedeckten Abschnitt haben, und alle Zoningkurse muessen ueber
     * GEMEINSAME ENDPUNKTE zusammenhaengen. Ein Ende mitten auf einer Kante
     * zaehlt fuer CS2 nicht als Knoten.
     */
    private static int RandzoningTKreuzung1822()
    {
        var site = new[]
        {
            new float2(-1037.02001953125f, 118.68977355957031f),
            new float2(-1186.655517578125f, 123.82305908203125f),
            new float2(-1192.7509765625f, -53.77497863769531f),
            new float2(-1043.1124267578125f, -58.910987854003906f),
        };
        var s = LayoutSettings.Cs2;
        s.Zellen = true;
        s.AngleMode = "edge";
        s.Auto = false;
        s.AutomaticEntrances = false;
        s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
        s.Zoningflaechen = new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1043.6171875f, -44.48427963256836f),
                Spalten = 6, Reihen = 6, Winkel = 88.04, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1041.9715576171875f, 3.4865055084228516f),
                Spalten = 7, Reihen = 2, Winkel = 88.04, Rand = 8.0,
            },
        };
        s.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(-1192.7509765625f, -53.77497863769531f),
                B = new float2(-1043.1124267578125f, -58.910987854003906f),
            },
        };

        var bau = ParkingGeometry.Build(site, s);
        var stuecke = bau.NetLine
            .Where(n => string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
            .Select(n => (A: n.A, B: n.B))
            .ToArray();
        var tK = TKreuzungen(stuecke);
        var endpunktZuege = EndpunktZusammenhangsteile(stuecke);

        var rzPlan = ParkingGeometry.RandzoningStrassen(
            s.Randzoning, site, s.Es + s.Sl + s.Ai / 2);
        var groessteLuecke = 0f;
        var rzTeillaengen = new List<float>();
        if (rzPlan == null || rzPlan.Count != 1)
        {
            groessteLuecke = float.PositiveInfinity;
        }
        else
        {
            var rz = rzPlan[0];
            var d = rz.B - rz.A;
            var laenge = math.length(d);
            var r = d / laenge;
            var intervalle = new List<(float Von, float Bis)>();
            foreach (var st in stuecke)
            {
                var qa = math.abs(r.x * (st.A.y - rz.A.y)
                    - r.y * (st.A.x - rz.A.x));
                var qb = math.abs(r.x * (st.B.y - rz.A.y)
                    - r.y * (st.B.x - rz.A.x));
                if (qa > 0.25f || qb > 0.25f) continue;
                var ta = math.dot(st.A - rz.A, r);
                var tb = math.dot(st.B - rz.A, r);
                var von = math.max(0f, math.min(ta, tb));
                var teilBis = math.min(laenge, math.max(ta, tb));
                intervalle.Add((von, teilBis));
                if (teilBis - von > 0.01f)
                    rzTeillaengen.Add(teilBis - von);
            }
            intervalle = intervalle.OrderBy(x => x.Von).ToList();
            var bis = 0f;
            foreach (var teil in intervalle)
            {
                if (teil.Von > bis) groessteLuecke = math.max(
                    groessteLuecke, teil.Von - bis);
                bis = math.max(bis, teil.Bis);
            }
            groessteLuecke = math.max(groessteLuecke, laenge - bis);
        }

        /*
         * KOSTEN DES T-SCHNITTS NACH BLOCKSYSTEMS GEMESSENER FORMEL.
         *
         * Ein `SnapCellSize`-Kurs traegt seine 8-m-Phase als HalfAligned.
         * Kollineare Anschlusskanten verlaengert BlockSystem am gemeinsamen
         * Knoten nicht; an einem freien Ende kommen 4 m nur ohne Halbphase
         * hinzu. Danach gilt N=floor((L_eff+0,1)/8). Pro Laengsspalte hat der
         * Block sechs Tiefenzellen. Randzoning schaltet nur die Aussenseite an.
         */
        int Spalten(float laenge, bool halbAnfang, bool halbEnde,
            bool stetigAnfang, bool stetigEnde)
        {
            var effektiv = laenge;
            if (!stetigAnfang && !halbAnfang) effektiv += 4f;
            if (!stetigEnde && !halbEnde) effektiv += 4f;
            return (int)math.floor((effektiv + 0.1f) / 8f);
        }
        bool Endhalb(float laenge, bool anfangHalb = false)
            => (((int)math.round(laenge / 4f) & 1) != 0) != anfangHalb;

        rzTeillaengen.Sort((x, y) => y.CompareTo(x));
        var rzGesamt = rzTeillaengen.Sum();
        var spaltenUngeteilt = Spalten(rzGesamt, false,
            Endhalb(rzGesamt), false, false);
        var spaltenGeteilt = 0;
        var roheBloeckeUngeteilt = 2 * (int)math.ceil(spaltenUngeteilt / 10f);
        var roheBloeckeGeteilt = 0;
        if (rzTeillaengen.Count == 2)
        {
            var ersterEndhalb = Endhalb(rzTeillaengen[0]);
            var zweiterAnfanghalb = false;
            var stetig = ersterEndhalb == zweiterAnfanghalb;
            var erster = Spalten(rzTeillaengen[0], false, ersterEndhalb,
                false, stetig);
            var zweiterEndhalb = Endhalb(rzTeillaengen[1], zweiterAnfanghalb);
            var zweiter = Spalten(rzTeillaengen[1], zweiterAnfanghalb,
                zweiterEndhalb, stetig, false);
            spaltenGeteilt = erster + zweiter;
            roheBloeckeGeteilt = 2 * ((int)math.ceil(erster / 10f)
                + (int)math.ceil(zweiter / 10f));
        }
        var zellverlustAussen = (spaltenUngeteilt - spaltenGeteilt) * 6;
        var kostenFalsch = rzTeillaengen.Count != 2
            || spaltenUngeteilt != 17 || spaltenGeteilt != 16
            || zellverlustAussen != 6
            || roheBloeckeGeteilt - roheBloeckeUngeteilt != 2;

        var schlecht = groessteLuecke > 0.01f || tK > 0
            || endpunktZuege != 1 || kostenFalsch;
        Console.WriteLine($"  Protokoll 18:22 T-Kreuzung    {stuecke.Length,2} Stueck, "
            + $"RZ-Luecke {groessteLuecke,5:F2} m, {tK} T-Kreuzung(en) ohne Knoten, "
            + $"{endpunktZuege} Endpunkt-Zug/Zuege"
            + (schlecht ? "  <-- NETZ ABGERISSEN" : string.Empty));
        Console.WriteLine($"      Blockrechnung RZ: {rzGesamt:F2} m ungeteilt "
            + $"{spaltenUngeteilt} Laengsspalten, geteilt "
            + $"{string.Join(" + ", rzTeillaengen.Select(l => l.ToString("F2")))} m "
            + $"= {spaltenGeteilt} Spalten; aussen -{zellverlustAussen} Zellen, "
            + $"roh {roheBloeckeUngeteilt} -> {roheBloeckeGeteilt} Bloecke");
        if (schlecht)
            foreach (var st in stuecke)
                Console.WriteLine($"      ({st.A.x,9:F1}/{st.A.y,7:F1}) -> "
                    + $"({st.B.x,9:F1}/{st.B.y,7:F1})  {math.distance(st.A, st.B),6:F2} m");
        return schlecht ? 1 : 0;
    }

    /**
     * DER FALL AUS DEM PROTOKOLL PLT-A950B8E0 (2026-09-04, 11:42).
     *
     * Erster gemeldeter Fall MIT Randzoning im Protokoll - seit die
     * JSONL-Datei die Linien mitfuehrt, laesst er sich abschreiben statt
     * nachbauen. Meldung dazu: *"Bauzettel sollte jetzt zeigen, dass
     * Strassenasphalt fehlt."*
     *
     * Eigenes Areal, deshalb ein eigener Lauf: die uebrigen Faelle teilen
     * sich ein Polygon, dieses hier ist ein anderes.
     */
    private static int RandzoningNutzerfall()
    {
        var site = new[]
        {
            new float2(-1037.02026f, 118.68978f),
            new float2(-1197.80042f, 124.20539f),
            new float2(-1203.31030f, -36.329002f),
            new float2(-1042.52698f, -41.845070f),
        };
        var s = LayoutSettings.Cs2;
        s.Zellen = true;
        s.AngleMode = "edge";
        s.Auto = false;
        s.AutomaticEntrances = false;
        s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
        s.Zoningflaechen = new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1041.38684f, 20.571302f),
                Spalten = 6, Reihen = 6, Winkel = 178.04, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1039.46680f, 76.538383f),
                Spalten = 2, Reihen = 7, Winkel = 178.04, Rand = 8.0,
            },
        };
        s.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(-1203.31030f, -36.329002f),
                B = new float2(-1042.52698f, -41.845070f),
            },
        };

        var bau = ParkingGeometry.Build(site, s);
        var stuecke = bau.NetLine
            .Count(n => string.Equals(n.Kind, "zoning", StringComparison.Ordinal));
        var laenge = bau.NetLine
            .Where(n => string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
            .Sum(n => (double)math.distance(n.A, n.B));
        var belag = RingFlaeche(bau.ZoningRoadSurface);

        /*
         * DER MASSSTAB: eine 8 m breite Strasse braucht rund 8 m2 Belag je
         * laufendem Meter. Deutlich weniger heisst, dass ein Teil der
         * Strassen blank bleibt - und dort saehe man Autos ueber Gras fahren.
         */
        var erwartet = laenge * ParkingGeometry.ZoningStrassenbreite;
        var deckung = erwartet <= 0 ? 0 : belag / erwartet;
        var verworfen = Cs2Annahme("Fahrbahn", bau.ZoningRoadSurface)
            + Cs2Annahme("Parzellenboden", bau.ZoningSurface);
        var schlecht = deckung < 0.80 || verworfen > 0;
        Console.WriteLine($"  Protokoll PLT-A950B8E0        {stuecke,2} Stueck, "
            + $"{laenge,6:F1} m Strasse, Belag {belag,7:F0} m2 = "
            + $"{deckung * 100,5:F1} % der erwarteten Flaeche"
            + (verworfen > 0 ? $", {verworfen} Ring(e) von CS2 verworfen" : string.Empty)
            + (schlecht ? "  <-- FEHLT BELAG" : string.Empty));

        /*
         * WELCHES STUECK BLANK BLEIBT.
         *
         * Eine Gesamtzahl sagt nur DASS etwas fehlt. Je Stueck gemessen sagt
         * sie WO - und beim Randzoning ist das die entscheidende Frage, weil
         * dort eine andere Regel greift als bei den Ringen der Flaechen.
         */
        foreach (var n in bau.NetLine)
        {
            if (!string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
                continue;
            var d = n.B - n.A;
            var l = math.length(d);
            if (l < 0.01f) continue;
            var r = d / l;
            var q = new float2(-r.y, r.x) * (float)
                (ParkingGeometry.ZoningStrassenbreite * 0.5);
            var korridor = new[]
            {
                n.A - q, n.B - q, n.B + q, n.A + q,
            };
            var drauf = (bau.ZoningRoadSurface ?? Array.Empty<float2[]>())
                .SelectMany(TriangulateMaterialRegion)
                .Sum(t => t.Weight * ConvexOverlapArea(t.Points, korridor));
            var soll = l * ParkingGeometry.ZoningStrassenbreite;
            var imRz = ParkingGeometry.RandzoningEnthaelt(s.Randzoning, n.A, n.B);
            Console.WriteLine($"      {(imRz ? "RZ " : "ZF ")}"
                + $"{l,6:F1} m -> {drauf,6:F0} m2 von {soll,6:F0} = "
                + $"{(soll <= 0 ? 0 : drauf / soll) * 100,5:F1} %");
        }
        return schlecht ? 1 : 0;
    }

    /**
     * WAS CS2 ANNIMMT, NICHT WAS GEPLANT IST.
     *
     * Der Lauf hat bisher die GEPLANTE Flaeche gemessen und war deshalb
     * gruen, waehrend der Nutzer im Spiel gar keinen Asphalt sah. Zwischen
     * Plan und Spiel liegt CS2s Ear-Clipping: es versetzt jeden Ringknoten
     * um 0,1 m nach innen und verwirft den GANZEN Ring, wenn dabei nicht
     * genau n-2 Dreiecke herauskommen.
     *
     * Gemessen am 2026-09-04 im Protokoll PLT-A950B8E0: ein Ring mit
     * 2.286 m2 geplanter Flaeche und NULL Dreiecken. Weil alle
     * zusammenhaengenden Zoningstrassen-Zellen zu einem einzigen Ring
     * verschmolzen, nahm dieser eine Ring die RZ- und die ZF-Strasse
     * gemeinsam mit ins Nichts - genau das Bild des Nutzers: *"auf der
     * ganzen RZ-Strasse UND der ZF-Strasse gleichermassen fehlte der
     * Asphalt."*
     *
     * Gibt die Zahl der verworfenen Ringe zurueck.
     */
    private static int Cs2Annahme(string was, float2[][] ringe)
    {
        var verworfen = 0;
        foreach (var ring in ringe ?? Array.Empty<float2[]>())
        {
            if (ring == null || ring.Length < 3) continue;
            if (Cs2Triangulierung.Dreiecke(ring) != 0) continue;
            verworfen++;
            var kuerzeste = double.PositiveInfinity;
            var wo = 0;
            for (var i = 0; i < ring.Length; i++)
            {
                var d = math.distance(ring[i], ring[(i + 1) % ring.Length]);
                if (d >= kuerzeste) continue;
                kuerzeste = d;
                wo = i;
            }
            Console.WriteLine($"      {was}: Ring mit {ring.Length,3} Punkten "
                + $"und {Math.Abs(RingFlaeche(new[] { ring })),7:F0} m2 "
                + $"liefert 0 Dreiecke, kuerzeste Kante "
                + $"{kuerzeste:F7} m  <-- CS2 VERWIRFT IHN");
            // Nur der erste verworfene Ring wird ausgeschrieben - er zeigt
            // die Stelle; zwanzig weitere Punktlisten zeigen nur noch Rauschen.
            if (verworfen > 1) continue;
            for (var i = 0; i < ring.Length && i < 40; i++)
                Console.WriteLine($"          [{i,2}] ({ring[i].x,12:F5}/{ring[i].y,12:F5})"
                    + $"  Kante {math.distance(ring[i], ring[(i + 1) % ring.Length]),10:F5} m"
                    + (i == wo ? "   <-- HAARKANTE" : string.Empty));
        }
        return verworfen;
    }

    private static IEnumerable<(string Name,
        ParkingGeometry.Zoningflaeche[] Flaechen,
        ParkingGeometry.RandzoningLinie[] Rand)> Zoningnetzfaelle(
        Func<int, int, int, int, double, ParkingGeometry.Zoningflaeche> f,
        float2[] site)
    {
        var keins = Array.Empty<ParkingGeometry.RandzoningLinie>();
        // Eine Flaeche allein - die Gegenprobe. Ihr Ring muss immer
        // zusammenhaengen.
        yield return ("6x6 allein",
            new[] { f(-3, -3, 6, 6, 0) }, keins);

        /*
         * ZWEI VERSCHIEDEN GROSSE, NEBENEINANDER, EINE KACHEL ABSTAND.
         *
         * Das ist der Fall des Nutzers. Die kleinere Flaeche ist kuerzer;
         * ihre Querstrassen enden deshalb MITTEN auf der langen Strasse der
         * grossen - eine T-Einmuendung.
         */
        // Der Versatz ist in KACHELN gerechnet: eine Kachel Abstand heisst
        // Versatz = Breite + 1, buendig heisst Versatz = Breite.
        yield return ("6x6 + 3x3, eine Kachel",
            new[] { f(-7, -3, 6, 6, 0), f(0, -3, 3, 3, 0) }, keins);
        yield return ("6x6 + 3x3, buendig",
            new[] { f(-7, -3, 6, 6, 0), f(-1, -3, 3, 3, 0) }, keins);
        yield return ("6x6 + 3x3, versetzt",
            new[] { f(-7, -3, 6, 6, 0), f(0, 0, 3, 3, 0) }, keins);

        // Eine L-Form aus zwei Bausteinen - ausdruecklicher Wunsch des
        // Nutzers, dass das gehen soll.
        yield return ("L aus 6x2 und 2x4",
            new[] { f(-4, -3, 6, 2, 0), f(-4, 0, 2, 4, 0) }, keins);

        // Drei Flaechen in einer Reihe, je eine Kachel Abstand.
        yield return ("3 x 3x3 in Reihe",
            new[] { f(-8, -2, 3, 3, 0), f(-4, -2, 3, 3, 0), f(0, -2, 3, 3, 0) }, keins);

        /*
         * DER FALL DES NUTZERS, aus dem Bauprotokoll PLT-E24B7149.
         *
         * Nicht nachgebaut, sondern ABGESCHRIEBEN - meine sechs geratenen
         * Anordnungen hingen alle zusammen, seine nicht. Genau dafuer traegt
         * das Protokoll die Flaechen jetzt mit.
         *
         * Die Lage in Zahlen: die 7x2 sitzt BUENDIG an der rechten Kante der
         * 6x6 und schliesst unten mit ihr ab. Die kurze Flaeche liegt damit
         * an der langen an, ohne sie zu ueberragen.
         */
        yield return ("Nutzerfall 6x6 + 7x2 buendig", new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1040.0188f, 60.42268f),
                Spalten = 6, Reihen = 6, Winkel = 178.03, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1089.08826f, 30.088034f),
                Spalten = 7, Reihen = 2, Winkel = 178.03, Rand = 8.0,
            },
        }, keins);

        /*
         * RANDZONING an der laengsten Umrisslinie.
         *
         * Gepruefte Behauptung: in diesem Abschnitt liegt danach KEINE
         * Randstrasse mehr, sondern Zoning-Strasse. *"Die Randstrasse in dem
         * Bereich wird ERSETZT."*
         */
        yield return ("Randzoning an Kante 0",
            Array.Empty<ParkingGeometry.Zoningflaeche>(),
            new[]
            {
                new ParkingGeometry.RandzoningLinie
                {
                    A = site[0],
                    B = site[1],
                },
            });

        /*
         * DER FALL VOM 2026-09-04, Protokoll PLT-6CABA97F.
         *
         * Meldung: *"Bauzettel wurde erzeugt, und es wurde wieder den echten
         * Strassen keine Asphaltflaeche gegeben."* Die Zoning-Strasse ist
         * unsichtbar; ohne Belag saehe man Autos ueber Gras fahren. Gemessen
         * wird deshalb, ob ueberhaupt Zoningstrassen-Belag entsteht.
         *
         * Zwei Flaechen unter 88 Grad - also quer zu allen bisherigen
         * Faellen, die alle um 0 oder 178 Grad lagen.
         */
        yield return ("Nutzerfall 88 Grad, 6x6 + 7x2", new[]
        {
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1042.4568f, -10.626934f),
                Spalten = 6, Reihen = 6, Winkel = 88.03, Rand = 8.0,
            },
            new ParkingGeometry.Zoningflaeche
            {
                Ecke = new float2(-1048.8055f, 37.619232f),
                Spalten = 7, Reihen = 2, Winkel = 88.03, Rand = 8.0,
            },
        }, keins);

        // Und dasselbe mit einer inneren Flaeche daneben - beide Sorten
        // muessen sich vertragen.
        yield return ("Randzoning + 6x6 innen",
            new[] { f(-3, -3, 6, 6, 0) },
            new[]
            {
                new ParkingGeometry.RandzoningLinie
                {
                    A = site[0],
                    B = site[1],
                },
            });
    }

    /**
     * Wieviele voneinander getrennte Straessenzuege bilden die Stuecke?
     *
     * Zwei Stuecke gelten als verbunden, wenn ein Endpunkt des einen auf dem
     * anderen liegt - Endpunkt oder Mitte. Genau so verbindet CS2 auch:
     * ueber einen gemeinsamen Knoten, und eine Einmuendung teilt die
     * durchlaufende Kante.
     */
    private static int Zusammenhangsteile(IReadOnlyList<(float2 A, float2 B)> s)
    {
        if (s.Count == 0) return 0;
        var eltern = Enumerable.Range(0, s.Count).ToArray();
        int Wurzel(int i) { while (eltern[i] != i) i = eltern[i] = eltern[eltern[i]]; return i; }
        void Vereine(int a, int b) { var x = Wurzel(a); var y = Wurzel(b); if (x != y) eltern[x] = y; }

        for (var i = 0; i < s.Count; i++)
        for (var k = i + 1; k < s.Count; k++)
            if (BeruehrtSich(s[i], s[k])) Vereine(i, k);

        return Enumerable.Range(0, s.Count).Select(Wurzel).Distinct().Count();
    }

    /** Wie viele Zuege CS2 aus identischen Kurs-Endpunkten bauen kann. */
    private static int EndpunktZusammenhangsteile(
        IReadOnlyList<(float2 A, float2 B)> s)
    {
        if (s.Count == 0) return 0;
        // CS2 NodeKey verwendet float3.Equals, keine 1-cm-Naehe.
        bool Gleich(float2 a, float2 b) => a.Equals(b);
        var eltern = Enumerable.Range(0, s.Count).ToArray();
        int Wurzel(int i)
        {
            while (eltern[i] != i) i = eltern[i] = eltern[eltern[i]];
            return i;
        }
        void Vereine(int a, int b)
        {
            var x = Wurzel(a);
            var y = Wurzel(b);
            if (x != y) eltern[x] = y;
        }

        for (var i = 0; i < s.Count; i++)
        for (var k = i + 1; k < s.Count; k++)
            if (Gleich(s[i].A, s[k].A) || Gleich(s[i].A, s[k].B)
                || Gleich(s[i].B, s[k].A) || Gleich(s[i].B, s[k].B))
                Vereine(i, k);

        return Enumerable.Range(0, s.Count).Select(Wurzel).Distinct().Count();
    }

    /**
     * ZAEHLT T-KREUZUNGEN: ein Ende mitten auf einem anderen Stueck.
     *
     * CS2 verschmilzt zwei Strassen NUR bei identischen Endpunkten (siehe
     * die Sackgassen-Befunde vom 2026-08-16). Endet ein Stueck mitten auf
     * einem anderen, entsteht dort KEIN Knoten - und ohne Knoten laufen
     * weder Strom noch Wasser oder Abwasser hinueber.
     *
     * Der Zusammenhangstest sieht das nicht: er fragt `BeruehrtSich`, und
     * das akzeptiert einen Punkt irgendwo auf der Strecke. Geometrisch
     * haengt alles zusammen, im Spiel nicht. Der Nutzer am 2026-09-04:
     * *"An der T-Kreuzung, also ZF-Strasse trifft auf RZ-Strasse, wird
     * Strom sowie Wasser/Abwasser nicht verbunden."*
     *
     * Gezaehlt wird deshalb getrennt: Beruehrung ja, aber KEIN gemeinsamer
     * Endpunkt.
     */
    private static int TKreuzungen(
        IReadOnlyList<(float2 A, float2 B)> stuecke)
    {
        const float toleranz = 0.01f;
        bool Gleich(float2 a, float2 b) => math.distance(a, b) < toleranz;
        var zahl = 0;
        for (var i = 0; i < stuecke.Count; i++)
        for (var k = 0; k < stuecke.Count; k++)
        {
            if (i == k) continue;
            foreach (var ende in new[] { stuecke[i].A, stuecke[i].B })
            {
                if (Gleich(ende, stuecke[k].A) || Gleich(ende, stuecke[k].B))
                    continue;
                if (!PunktAufStrecke(stuecke[k].A, stuecke[k].B, ende))
                    continue;
                zahl++;
            }
        }
        return zahl;
    }

    private static bool BeruehrtSich(
        (float2 A, float2 B) x, (float2 A, float2 B) y)
        => PunktAufStrecke(y.A, y.B, x.A) || PunktAufStrecke(y.A, y.B, x.B)
            || PunktAufStrecke(x.A, x.B, y.A) || PunktAufStrecke(x.A, x.B, y.B);

    private static bool PunktAufStrecke(float2 a, float2 b, float2 p)
    {
        const float toleranz = 0.01f;
        var d = b - a;
        var laenge = math.length(d);
        if (laenge < toleranz) return false;
        var r = d / laenge;
        var w = p - a;
        var laengs = math.dot(w, r);
        if (laengs < -toleranz || laengs > laenge + toleranz) return false;
        return math.abs(r.x * w.y - r.y * w.x) < toleranz;
    }

    /** Enden, an denen kein anderes Stueck haengt. */
    private static IEnumerable<float2> FreieEnden(
        IReadOnlyList<(float2 A, float2 B)> s)
    {
        for (var i = 0; i < s.Count; i++)
        foreach (var ende in new[] { s[i].A, s[i].B })
        {
            var haengt = false;
            for (var k = 0; k < s.Count && !haengt; k++)
                if (k != i) haengt = PunktAufStrecke(s[k].A, s[k].B, ende);
            if (!haengt) yield return ende;
        }
    }
}
