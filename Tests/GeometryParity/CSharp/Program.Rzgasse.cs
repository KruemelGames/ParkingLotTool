using System;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

/**
 * IST DIE RANDZONING-STRASSE EINE FAHRGASSE - ODER EINE ZUSAETZLICHE STRASSE?
 *
 * Idee des Nutzers am 2026-09-10: *"Anstatt eine Strasse zu erzwingen, warum
 * nehmen wir nicht die am naechsten liegende Fahrtgasse und nutzen die als
 * RZ-Randstrasse. Das ist derzeit so nicht drin, weil wir die RZ-Strasse
 * selbst generieren, so dass es irgendwie reinpasst."*
 *
 * SO SAH ES VORHER AUS, an seinem Fall gemessen (Abstand zur gewaehlten
 * Umrisskante):
 *
 *     RZ-Strasse    10,40 m      selbst erzeugt, auf der Randstrassenachse
 *     Gasse 5       24,18 m      die naechste echte Fahrgasse
 *     Gasse 4       40,11 m
 *
 * Zwischen der erzwungenen Strasse und der ersten Gasse lagen also knapp
 * vierzehn Meter Parkplatz - eine Buchtreihe und ein Streifen Rest. Der
 * Parkplatz trug dort zwei parallele Strassen im Abstand von 13,8 m, wo eine
 * gereicht haette.
 *
 * WAS JETZT GILT. Die naechstliegende Fahrgasse IST die Randzoning-Strasse.
 * Alles zwischen ihr und der Umrisskante wird Bauland - dort steht keine
 * Bucht mehr und liegt kein Asphalt.
 *
 * Ansage des Nutzers zum Umbau: *"Bitte ordentlich einbauen, die Buchten
 * muessen weg, Asphaltierung muss ordentlich sein."*
 */
internal static partial class Program
{
    private static int RunRzgasse()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        // Umriss, Regler, Zufahrt und Randzoning aus dem Zettel 22:22:21.
        var form = new[]
        {
            new float2(-1037.0202637f, 118.6898041f),
            new float2(-1158.2799072f, 122.8496475f),
            new float2(-1183.5230713f, 51.7920036f),
            new float2(-1186.9890137f, -49.1890030f),
            new float2(-1164.2114258f, -49.9706192f),
            new float2(-1042.9484863f, -54.1326942f),
        };

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;
        e.Entrances = new[]
        {
            new Entrance { Edge = 0, Along = 82.71263122558594 },
        };
        e.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie { A = form[2], B = form[3] },
        };

        var l = ParkingGeometry.Build(form, e);

        // Abstand eines Punktes von der gewaehlten Umrisskante.
        var ka = form[2];
        var kd = form[3] - form[2];
        var kl = math.length(kd);
        double Abstand(float2 p)
            => Math.Abs(kd.x * (p.y - ka.y) - kd.y * (p.x - ka.x)) / kl;
        double Laengs(float2 p)
            => (kd.x * (p.x - ka.x) + kd.y * (p.y - ka.y)) / kl;

        var rz = (l.NetLine ?? Array.Empty<NetSegment>())
            .Where(n => string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
            .ToArray();
        Pruefe(rz.Length > 0, "keine Randzoning-Straße geplant");
        if (rz.Length == 0)
        {
            Console.WriteLine($"Rzgasse: {fehler} Fehler");
            return 1;
        }

        var rzAbstand = rz.Average(n => Abstand((n.A + n.B) * 0.5f));
        Console.WriteLine($"  RZ-Straße liegt {rzAbstand:F2} m von der "
            + "gewählten Kante");

        var gassen = (l.AisleLine ?? Array.Empty<float2[]>())
            .Select(g => Abstand((g[0] + g[g.Length - 1]) * 0.5f))
            .OrderBy(d => d).ToArray();
        Console.WriteLine("  Fahrgassen: " + string.Join(", ",
            gassen.Select(d => $"{d:F2} m")));

        /*
         * ERSTENS - UND DAS IST DER KERN: DIE STRASSE *IST* DIE NAECHSTE GASSE.
         *
         * Nicht "es liegt keine Gasse davor" - das war vorher schon wahr und
         * trotzdem falsch. Vorher lag die erzwungene Strasse bei 10,40 m und
         * die naechste Gasse bei 24,18 m: ZWEI parallele Strassen, 13,8 m
         * auseinander, wo eine reicht. Gefordert ist, dass beide Zahlen
         * dieselbe sind.
         */
        var naechsteGasse = gassen.Length == 0 ? double.NaN : gassen[0];
        Console.WriteLine($"  nächste Fahrgasse {naechsteGasse:F2} m, "
            + $"RZ-Straße {rzAbstand:F2} m");
        Pruefe(gassen.Length > 0 && Math.Abs(naechsteGasse - rzAbstand) < 0.5,
            $"die RZ-Straße liegt bei {rzAbstand:F2} m, die nächste Fahrgasse "
            + $"bei {naechsteGasse:F2} m - {Math.Abs(naechsteGasse - rzAbstand):F2} m "
            + "auseinander. Die RZ-Straße ist eine zusätzliche Straße statt "
            + "der nächsten Gasse");

        /*
         * ZWEITENS: NUR DIE AEUSSERE BUCHTREIHE GEHT.
         *
         * Ansage des Nutzers am 2026-09-10: *"Wichtig, nur die eine Seite der
         * Buchten muss weg, nicht beide."* Eine Fahrgasse traegt Buchten auf
         * BEIDEN Seiten. Wird sie zur Randzoning-Strasse, faellt die Reihe
         * zur Umrisskante hin weg - dort ist Bauland. Die INNERE Reihe bleibt
         * und parkt weiter an der Strasse.
         *
         * Gemessen wird deshalb zweierlei: aussen darf nichts stehen, innen
         * MUSS etwas stehen. Ohne den zweiten Teil waere "alle Buchten
         * loeschen" auch eine gruene Antwort.
         */
        var buchten = (l.Bay ?? Array.Empty<float2[]>())
            .Select(b => Abstand(new float2(
                b.Average(p => p.x), b.Average(p => p.y))))
            .ToArray();
        var aussenkante = rzAbstand - e.Ai / 2;
        var innenkante = rzAbstand + e.Ai / 2;
        /*
         * GEMESSEN WIRD LAENGS DER STRASSE, NICHT LAENGS DER KANTE.
         *
         * Die Kacheln haengen an der Strasse. Reicht die nicht ueber die ganze
         * Kante - weil die Gasse frueher endet oder die Kacheln an einer
         * Querstrasse aufhoeren -, gibt es dahinter kein Bauland, und Bucht
         * und Belag sind dort richtig.
         *
         * Der erste Anlauf mass nur quer und meldete acht Buchten bei laengs
         * -3 bis -24 m als Fehler. Die liegen vor dem Anfang der Kante, um die
         * Ecke herum. Ein Messgeraet, das ausserhalb seines Gegenstands misst,
         * meldet Fehler, die es nicht gibt.
         */
        var rzVon = double.PositiveInfinity;
        var rzBis = double.NegativeInfinity;
        foreach (var n in rz)
        foreach (var q in new[] { n.A, n.B })
        {
            rzVon = Math.Min(rzVon, Laengs(q));
            rzBis = Math.Max(rzBis, Laengs(q));
        }
        bool AmAbschnitt(float2 p) => Laengs(p) >= rzVon - 0.01
            && Laengs(p) <= rzBis + 0.01;

        var draussen = (l.Bay ?? Array.Empty<float2[]>())
            .Select(b => new float2(b.Average(q => q.x), b.Average(q => q.y)))
            .Count(m => AmAbschnitt(m) && Abstand(m) < aussenkante - 0.01);
        var direktInnen = buchten.Count(d => d > innenkante - 0.01
            && d < innenkante + e.Sl + 0.5);
        var engste = buchten.Length == 0 ? double.NaN : buchten.Min();
        Console.WriteLine($"  {buchten.Length} Buchten, engste bei "
            + $"{engste:F2} m; {draussen} außerhalb der Straße, "
            + $"{direktInnen} in der inneren Reihe");
        Pruefe(draussen == 0,
            $"{draussen} Buchten liegen zwischen Kante und RZ-Straße - dort "
            + "ist Bauland, dort steht keine Bucht");
        Pruefe(direktInnen > 0,
            "an der Innenseite der RZ-Straße steht keine einzige Bucht - es "
            + "sollte nur die ÄUSSERE Reihe wegfallen, nicht beide");

        /*
         * DRITTENS: KEIN ASPHALT ZWISCHEN KANTE UND STRASSENRAND.
         *
         * *"Asphaltierung muss ordentlich sein."* Bauland traegt keinen
         * Parkplatzbelag. Gemessen wird der Belagring, der der Kante am
         * naechsten kommt; er darf nicht weiter heranreichen als der
         * Aussenrand der Zoningstrasse.
         */
        var strassenrand = rzAbstand - ParkingGeometry.ZoningStrassenbreite / 2;
        var belagRand = double.PositiveInfinity;
        foreach (var ring in l.AsphaltSurface ?? Array.Empty<float2[]>())
        foreach (var p in ring ?? Array.Empty<float2>())
            if (AmAbschnitt(p)) belagRand = Math.Min(belagRand, Abstand(p));
        Console.WriteLine($"  Belag reicht bis {belagRand:F2} m an die Kante "
            + $"heran; Straßenrand liegt bei {strassenrand:F2} m");
        Pruefe(belagRand >= strassenrand - 0.51,
            $"Asphalt reicht bis {belagRand:F2} m an die Kante - der "
            + $"Straßenrand liegt bei {strassenrand:F2} m, davor ist Bauland");

        fehler += PruefeDiagonale();
        fehler += PruefeQuermodus();

        Console.WriteLine($"Rzgasse: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    /**
     * SCHREIBT DEN PLAN IN DERSELBEN FORM WEG WIE EIN VORBAU-ZETTEL.
     *
     * Zahlen sagen nicht, ob ein Parkplatz gut aussieht. Der Nutzer urteilt
     * nach dem Bild, und ich brauche dasselbe Bild aus dem Pruflauf, sonst
     * messe ich weiter etwas, das gruen sein kann, waehrend die Vorschau
     * falsch ist - genau so war es am 2026-09-10 16:38.
     *
     * Das Schema ist absichtlich das des Vorbau-Zettels, damit derselbe
     * Zeichner beides darstellt.
     */
    private static void SchreibeBild(
        string pfad, float2[] form, LayoutSettings e, ParkingLayout l)
    {
        static object Punkt(float2 p) => new { X = p.x, Z = p.y };
        static object[] Ring(float2[] r) => (r ?? Array.Empty<float2>())
            .Select(Punkt).ToArray();

        var zettel = new
        {
            Input = new
            {
                PolygonXZ = form.Select(Punkt).ToArray(),
                LayoutSettings = new
                {
                    Randzoning = (e.Randzoning ?? Array.Empty<ParkingGeometry.RandzoningLinie>())
                        .Select(r => new { A = Punkt(r.A), B = Punkt(r.B) }).ToArray(),
                },
            },
            Preview = new
            {
                Layout = new
                {
                    Bay = (l.Bay ?? Array.Empty<float2[]>())
                        .Select(b => new { Points = Ring(b) }).ToArray(),
                    GrassSurface = (l.GrassSurface ?? Array.Empty<float2[]>()).Select(Ring).ToArray(),
                    AsphaltSurface = (l.AsphaltSurface ?? Array.Empty<float2[]>()).Select(Ring).ToArray(),
                    AisleLine = (l.AisleLine ?? Array.Empty<float2[]>()).Select(Ring).ToArray(),
                    NetLine = (l.NetLine ?? Array.Empty<NetSegment>())
                        .Select(n => new { n.Kind, A = Punkt(n.A), B = Punkt(n.B) }).ToArray(),
                },
            },
        };

        var ordner = System.IO.Path.GetDirectoryName(pfad);
        if (!string.IsNullOrEmpty(ordner)) System.IO.Directory.CreateDirectory(ordner);
        System.IO.File.WriteAllText(pfad,
            System.Text.Json.JsonSerializer.Serialize(zettel),
            new System.Text.UTF8Encoding(false));
        Console.WriteLine($"    Bild geschrieben: {pfad}");
    }

    /**
     * BLEIBT EIN STUECK DER RZ-KANTE OHNE STRASSE?
     *
     * Die Treppenstufen laufen laengs der Gassen, die Kante laeuft schraeg.
     * Gemessen wird deshalb IN GASSENRICHTUNG: eine Stufe ueber dem
     * Laengsbereich [s0, s1] bedient genau die Kantenpunkte, deren
     * Laengswert dazwischen liegt.
     *
     * Ich hatte das zuerst laengs der KANTE gerechnet und daraufhin Luecken
     * gemeldet, die es nicht gab - derselbe Rahmenfehler wie am 2026-09-10
     * bei Welt-y gegen Gassenrahmen. Die Stufen stossen aneinander, ihre
     * Projektion auf die Kante tut es nicht.
     *
     * Gibt zurueck, wie lang das laengste unversorgte Stueck ist.
     */
    private static double LueckeInDerTreppe(
        float2 ka, float2 kb, float2 achse, NetSegment[] zoning)
    {
        double Laengs(float2 p) => achse.x * p.x + achse.y * p.y;

        var von = Math.Min(Laengs(ka), Laengs(kb));
        var bis = Math.Max(Laengs(ka), Laengs(kb));
        if (bis - von < 1e-6) return 0;

        var stufen = zoning
            .Select(n => (Von: Math.Max(von, Math.Min(Laengs(n.A), Laengs(n.B))),
                          Bis: Math.Min(bis, Math.Max(Laengs(n.A), Laengs(n.B)))))
            .Where(t => t.Bis > t.Von + 0.01)
            .OrderBy(t => t.Von)
            .ToArray();

        var laengste = 0.0;
        var erreicht = von;
        foreach (var stufe in stufen)
        {
            if (stufe.Von > erreicht) laengste = Math.Max(laengste, stufe.Von - erreicht);
            erreicht = Math.Max(erreicht, stufe.Bis);
        }
        return Math.Max(laengste, bis - erreicht);
    }

    /**
     * WIE WEIT IST DIE RZ-STRASSE VON DER KANTE WEG, DIE SIE BEDIENT?
     *
     * Das ist das Mass fuer das tote Dreieck. Zwischen Kante und Strasse
     * liegen die Kacheln: dort entstehen keine Buchten, kein Belag, keine
     * Gasse. Wie tief dieses Band ist, entscheidet also darueber, wie viel
     * Parkplatz verschwindet.
     *
     * Der Entwurf gibt die Schranke vor: die RZ-Strasse IST die NAECHSTE
     * Fahrgasse. Gassen liegen einen Modulabstand auseinander (2*Buchttiefe +
     * Fahrgasse + Mittelstreifen = 21,30 m). Zu jedem Punkt der Kante gibt es
     * also eine Gasse hoechstens einen Modulabstand entfernt - mehr darf das
     * Band nirgends messen.
     *
     * Am Bauzettel des Nutzers vom 2026-09-10 16:38 waren es an der
     * Diagonalen bis zu 55 m: die Strasse lag bei quer -1114, die Kante lief
     * von quer -1169 bis -1131. Das ist das Dreieck, das er im Bild sieht.
     */
    private static double KantenabstandZurStrasse(
        float2 ka, float2 kb, NetSegment[] zoning)
    {
        if (zoning.Length == 0) return double.PositiveInfinity;

        double AufStrecke(float2 p, float2 a, float2 b)
        {
            var d = b - a;
            var ll = math.dot(d, d);
            if (ll < 1e-12) return math.distance(p, a);
            var t = math.clamp(math.dot(p - a, d) / ll, 0f, 1f);
            return math.distance(p, a + d * t);
        }

        var laenge = math.distance(ka, kb);
        var schritte = Math.Max(2, (int)(laenge / 2.0));
        var schlimmste = 0.0;
        for (var i = 0; i <= schritte; i++)
        {
            var p = math.lerp(ka, kb, (float)i / schritte);
            var nah = double.PositiveInfinity;
            foreach (var n in zoning)
                nah = Math.Min(nah, AufStrecke(p, n.A, n.B));
            schlimmste = Math.Max(schlimmste, nah);
        }
        return schlimmste;
    }

    /**
     * UND DASSELBE IM WINKELMODUS "QUER".
     *
     * Bauzettel des Nutzers vom 2026-09-10 16:38. Er faehrt dort
     * `AngleMode = quer`, nicht `edge` - und JEDER meiner bisherigen Faelle
     * lief mit `edge`. Der Modus dreht den Reihenrahmen, und damit liegen die
     * Fahrgassen ganz anders zur RZ-Kante.
     *
     * Im Zettel ist zu sehen, was dabei herauskommt: die Zoning-Kurse folgen
     * dem UMRISS statt einer Gassenlinie -
     * (-1157,3/72,7)..(-1158,0/49,8) und (-1158,0/49,8)..(-1159,2/16,1) -
     * also lief der Rueckfall, und er tat es still. Sein Urteil: *"Da kommt
     * der groesste Mist ueberhaupt raus."*
     */
    private static int PruefeQuermodus()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        var form = new[]
        {
            new float2(-1130.8115f, 121.9073f),
            new float2(-1037.0201f, 118.6898f),
            new float2(-1040.7805f, 9.0766f),
            new float2(-1171.3813f, 13.5569f),
            new float2(-1169.2294f, 76.2565f),
        };

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "quer"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;
        e.Entrances = new[]
        {
            new Entrance { Edge = 1, Along = 54.838531494140625 },
        };
        e.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie { A = form[4], B = form[0] },
            new ParkingGeometry.RandzoningLinie { A = form[3], B = form[4] },
        };

        ParkingLayout l;
        try { l = ParkingGeometry.Build(form, e); }
        catch (Exception ex)
        {
            Console.WriteLine("FEHLER: Quermodus-Form baut nicht - " + ex.Message);
            return 1;
        }

        SchreibeBild("artifacts/rzgasse/quermodus.json", form, e, l);

        var zoning = (l.NetLine ?? Array.Empty<NetSegment>())
            .Where(n => n.Kind == "zoning").ToArray();
        var gassen = l.AisleLine ?? Array.Empty<float2[]>();
        Console.WriteLine($"  Quermodus 16:38: {l.Stalls} Buchten, "
            + $"{gassen.Length} Gassen, {zoning.Length} Zoning-Kurse");
        if (gassen.Length == 0 || zoning.Length == 0)
        {
            Pruefe(false, "keine Gassen oder keine Zoning-Straße");
            return fehler;
        }

        var g0 = gassen[0];
        var achse = math.normalize(g0[g0.Length - 1] - g0[0]);
        double Quer(float2 p) => -achse.y * p.x + achse.x * p.y;
        var modul = 2 * e.Sl + e.Ai + e.Md;
        var gassenquer = gassen
            .Select(g => (Quer(g[0]) + Quer(g[g.Length - 1])) / 2).ToArray();

        double AufLinie(double y)
        {
            var d = gassenquer.Min(gy => Math.Abs(gy - y));
            var rest = d % modul;
            return Math.Min(rest, modul - rest);
        }

        /*
         * EIN ZONING-KURS LAEUFT LAENGS EINER GASSE - nicht schraeg.
         *
         * Folgt er dem Umriss, aendert sich sein Querwert ueber die Laenge.
         * Genau daran erkennt man den Rueckfall.
         */
        var schraeg = zoning.Count(n => Math.Abs(Quer(n.A) - Quer(n.B)) > 0.5);
        var neben = zoning.Count(n => AufLinie((Quer(n.A) + Quer(n.B)) / 2) > 0.5);
        Console.WriteLine($"    {schraeg} Kurs(e) laufen schräg zur "
            + $"Gassenrichtung, {neben} liegen neben der Gassenlinie");
        foreach (var n in zoning)
            Console.WriteLine($"      quer {Quer(n.A),8:F2}..{Quer(n.B),8:F2}"
                + $"   Abweichung {AufLinie((Quer(n.A) + Quer(n.B)) / 2),6:F2} m");

        Pruefe(schraeg == 0,
            $"{schraeg} Zoning-Kurs(e) laufen schräg zur Gassenrichtung - das "
            + "ist der alte Rückfall, keine Fahrgasse");
        Pruefe(neben == 0,
            $"{neben} Zoning-Kurs(e) liegen auf keiner Gassenlinie");

        /*
         * DAS LAYOUT MUSS SAGEN, WELCHE KURSE DIE RZ-STRASSEN SIND.
         *
         * Sonst muss das Werkzeug es an der Lage erraten - und genau das ging
         * schief, als die RZ-Strasse eine Fahrgasse wurde: die alte Erkennung
         * verlangte "parallel zur Kante und unter 15 m". An einer Diagonalen
         * steht die Gasse rund 34 Grad zur Kante, fiel durch, galt nicht als
         * Randzoning - und die Regel "Innenseite immer aus" griff nicht. Der
         * Nutzer im Spiel: *"Auffaellig, dass die Tiles nach innen gehen
         * statt aussen."*
         *
         * Hier gibt es keine Zoningflaechen, also sind ALLE Zoning-Kurse
         * RZ-Kurse. Beide Listen muessen sich decken.
         */
        var achsen = l.RandzoningRoad
            ?? Array.Empty<(float2 A, float2 B, float2 Innen)>();
        bool Deckt(float2 a, float2 b, float2 c, float2 d)
            => (math.distance(a, c) < 1e-3f && math.distance(b, d) < 1e-3f)
            || (math.distance(a, d) < 1e-3f && math.distance(b, c) < 1e-3f);
        var ohneAchse = zoning.Count(n =>
            !achsen.Any(r => Deckt(n.A, n.B, r.A, r.B)));
        Console.WriteLine($"    {achsen.Length} offengelegte RZ-Achse(n) zu "
            + $"{zoning.Length} Zoning-Kursen, {ohneAchse} ohne Entsprechung");
        Pruefe(achsen.Length == zoning.Length && ohneAchse == 0,
            $"Layout legt {achsen.Length} RZ-Achsen offen, gebaut sind "
            + $"{zoning.Length} Zoning-Kurse ({ohneAchse} ohne Entsprechung) - "
            + "dann muss das Werkzeug die Seite wieder an der Lage raten");

        /*
         * UND JEDE ACHSE MUSS WISSEN, WO INNEN IST - RICHTIG HERUM.
         *
         * `Innen` entscheidet, auf welcher Seite die Kacheln NICHT liegen.
         * Zeigt es auf die falsche Seite, zont die Stufe in den Parkplatz
         * hinein. Genau das hat der Nutzer im Spiel gesehen: *"Nicht alle
         * Tiles gehen nach aussen, da findet keine ordentliche Pruefung
         * statt."*
         *
         * Geprueft wird gegen die Kante, die die Achse bedient: von der Kante
         * weg ist innen. Der naechste Punkt auf der naechsten RZ-Kante gibt
         * die Richtung vor.
         */
        var falschHerum = 0;
        foreach (var r in achsen)
        {
            var mitte = (r.A + r.B) * 0.5f;
            var beste = float.MaxValue;
            var wegVonDerKante = float2.zero;
            foreach (var (ka, kb) in new[]
            {
                (form[4], form[0]), (form[3], form[4]),
            })
            {
                var d = kb - ka;
                var ll = math.dot(d, d);
                var t = ll < 1e-9f ? 0f
                    : math.clamp(math.dot(mitte - ka, d) / ll, 0f, 1f);
                var fuss = ka + d * t;
                var abstand = math.distance(mitte, fuss);
                if (abstand >= beste) continue;
                beste = abstand;
                wegVonDerKante = mitte - fuss;
            }
            if (math.lengthsq(wegVonDerKante) < 1e-9f) continue;
            if (math.dot(math.normalize(wegVonDerKante), r.Innen) > 0) continue;
            falschHerum++;
        }
        /*
         * ZUM VERGLEICH: WAS HAETTE DIE POLYGONMITTE GESAGT?
         *
         * Keine Pruefung, eine Zahl. Sie belegt, dass die alte Regel an
         * dieser Form wirklich danebenlag - sonst waere der Umbau eine
         * Behauptung.
         */
        var mitteX = form.Average(q => (double)q.x);
        var mitteY = form.Average(q => (double)q.y);
        var lotmitte = new float2((float)mitteX, (float)mitteY);
        var nachAltemMass = 0;
        foreach (var r in achsen)
        {
            var mitte = (r.A + r.B) * 0.5f;
            var alt2 = lotmitte - mitte;
            var richtung = r.B - r.A;
            var linksAlt = richtung.x * alt2.y - richtung.y * alt2.x > 0;
            var linksNeu = richtung.x * r.Innen.y - richtung.y * r.Innen.x > 0;
            if (linksAlt != linksNeu) nachAltemMass++;
        }
        Console.WriteLine($"    {falschHerum} Achse(n) mit Innen zur Kante hin "
            + "(Kacheln liefen in den Parkplatz); nach der alten Regel über "
            + $"die Polygonmitte wären {nachAltemMass} von {achsen.Length} "
            + "falsch herum");
        Pruefe(falschHerum == 0,
            $"{falschHerum} RZ-Achse(n) zeigen mit `Innen` auf die Kante statt "
            + "von ihr weg - dort landen die Kacheln IM Parkplatz");

        /*
         * UND DAS TOTE DRECK ZWISCHEN KANTE UND STRASSE.
         *
         * Der Lauf war vorher gruen und das Bild trotzdem falsch: er fragte
         * nur, OB ein Kurs auf einer Gassenlinie liegt, nie WELCHE. Die
         * innerste tut das auch.
         */
        foreach (var (ka, kb, name) in new[]
        {
            (form[4], form[0], "Diagonale"),
            (form[3], form[4], "linke Kante"),
        })
        {
            var weg = KantenabstandZurStrasse(ka, kb, zoning);
            Console.WriteLine($"    {name}: RZ-Straße bis zu {weg:F2} m "
                + $"entfernt (erlaubt {modul:F2} m)");

            /*
             * UND KEIN STUECK KANTE OHNE STUFE.
             *
             * Die Schranke ist die Strassenbreite: kuerzer als eine
             * Strassenbreite laesst sich ohnehin kein Kurs bauen, was
             * darueber liegt, ist eine fehlende Stufe.
             */
            var luecke = LueckeInDerTreppe(ka, kb, achse, zoning);
            Console.WriteLine($"      längste Lücke in der Treppe: "
                + $"{luecke:F2} m");
            Pruefe(luecke < ParkingGeometry.ZoningStrassenbreite,
                $"{name}: {luecke:F2} m der Kante haben gar keine RZ-Straße - "
                + "dort ist die Treppe unterbrochen");
            Pruefe(weg <= modul + 0.5,
                $"{name}: die RZ-Straße ist bis zu {weg:F2} m von der Kante "
                + $"weg - erlaubt ist ein Modulabstand ({modul:F2} m). "
                + "Dazwischen liegt Kachelband: keine Buchten, kein Belag, "
                + "keine Gasse");
        }

        return fehler;
    }

    /**
     * EINE SCHRAEGE RZ-KANTE BRAUCHT EINE TREPPE.
     *
     * Entwurf des Nutzers vom 2026-09-10 (Skizze `treppe_diagonal.png`): eine
     * Diagonale laesst sich nicht von EINER Strasse bedienen. Sie braucht je
     * Gassenhoehe einen Abschnitt, nach rechts versetzt, jeder so breit wie
     * die Kachelstufe, die er versorgt.
     *
     * Dazu zwei Regeln, die aus derselben Skizze folgen
     * (`fahrtgassenfussweg.png`):
     *
     *   - An einer RZ-Kante entsteht KEIN Endfussweg. Er liefe der Diagonale
     *     nach, mitten in die Kacheln hinein - im Bild orange.
     *   - Stattdessen werden die Gassenenden untereinander verbunden, knapp
     *     ausserhalb der Kachelflaeche - im Bild pink.
     *
     * Die Form hier ist nachgebaut, nicht abgeschrieben: obere Kante
     * waagerecht, rechts oben eine Diagonale. Randzoning liegt auf beiden,
     * wie beim Nutzer.
     */
    private static int PruefeDiagonale()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        /*
         * DIE ECHTE FORM DES NUTZERS, Vorbau-Zettel 2026-09-10 16:06.
         *
         * Vorher stand hier eine erfundene Form. Sie war gruen zu bekommen und
         * sagte trotzdem nichts: an der Form, die der Nutzer wirklich baut,
         * griff der neue Weg gar nicht. Sein Befund dazu: *"Irgendwie hast du
         * nicht eingebaut, dass es auch so eingebaut wird, bzw. die
         * Erkennung."*
         *
         * Sie hat beides - eine fast senkrechte RZ-Kante UND eine diagonale -
         * und sie hat den Stummel: die der senkrechten Kante naechste
         * Fahrgasse ist nur 4 m lang, die uebrigen 74 bis 157 m.
         */
        var form = new[]
        {
            new float2(-1037.02f, 118.69f),
            new float2(-1108.45f, 121.14f),
            new float2(-1159.01f, 44.23f),
            new float2(-1161.89f, -39.57f),
            new float2(-1042.59f, -43.66f),
        };

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;
        e.Entrances = new[]
        {
            new Entrance { Edge = 0, Along = 38.38345718383789 },
        };
        // Randzoning auf der Diagonale UND auf der fast senkrechten Kante -
        // beide genau so, wie der Nutzer sie gesetzt hat.
        e.Randzoning = new[]
        {
            new ParkingGeometry.RandzoningLinie { A = form[1], B = form[2] },
            new ParkingGeometry.RandzoningLinie { A = form[2], B = form[3] },
        };

        ParkingLayout l;
        try { l = ParkingGeometry.Build(form, e); }
        catch (Exception ex)
        {
            Console.WriteLine("FEHLER: Diagonalform baut nicht - " + ex.Message);
            return 1;
        }

        SchreibeBild("artifacts/rzgasse/diagonale.json", form, e, l);

        var netz = l.NetLine ?? Array.Empty<NetSegment>();
        var zoning = netz.Where(n => n.Kind == "zoning").ToArray();

        /*
         * SAGT DIE POLYGONMITTE HIER DASSELBE WIE DER PLAN?
         *
         * Dieselbe Gegenrechnung wie im Quermodus-Fall. Sie belegt, ob die
         * alte Seitenregel an dieser Form daneben lag.
         */
        {
            var achsen0 = l.RandzoningRoad
                ?? Array.Empty<(float2 A, float2 B, float2 Innen)>();
            var lm = new float2(
                (float)form.Average(q => (double)q.x),
                (float)form.Average(q => (double)q.y));
            var uneins = 0;
            var ohneRichtung = 0;
            foreach (var r in achsen0)
            {
                if (math.lengthsq(r.Innen) < 1e-6f) { ohneRichtung++; continue; }
                var m = (r.A + r.B) * 0.5f;
                var d = r.B - r.A;
                var alt2 = lm - m;
                if ((d.x * alt2.y - d.y * alt2.x > 0)
                    != (d.x * r.Innen.y - d.y * r.Innen.x > 0)) uneins++;
            }
            Console.WriteLine($"    Seitenregel: {achsen0.Length} Achsen, "
                + $"{uneins} wo Polygonmitte und Plan uneins sind, "
                + $"{ohneRichtung} ohne Richtung");

            /*
             * UND HIER ZEIGT SICH, WARUM DER PLAN NOETIG IST.
             *
             * An dieser Form ist bei EINER von fuenf Achsen die Polygonmitte
             * anderer Meinung - und sie liegt falsch. Genau das hat der
             * Nutzer im Spiel gesehen: *"Nicht alle Tiles gehen nach aussen."*
             *
             * Geprueft wird dasselbe wie im Quermodus-Fall: `Innen` muss von
             * der bedienten Kante WEG zeigen.
             */
            var falsch = 0;
            foreach (var r in achsen0)
            {
                var m = (r.A + r.B) * 0.5f;
                var beste = float.MaxValue;
                var weg = float2.zero;
                foreach (var (ka2, kb2) in new[]
                {
                    (form[1], form[2]), (form[2], form[3]),
                })
                {
                    var d2 = kb2 - ka2;
                    var ll = math.dot(d2, d2);
                    var t = ll < 1e-9f ? 0f
                        : math.clamp(math.dot(m - ka2, d2) / ll, 0f, 1f);
                    var fuss = ka2 + d2 * t;
                    var ab = math.distance(m, fuss);
                    if (ab >= beste) continue;
                    beste = ab; weg = m - fuss;
                }
                if (math.lengthsq(weg) < 1e-9f) continue;
                if (math.dot(math.normalize(weg), r.Innen) <= 0) falsch++;
            }
            Pruefe(falsch == 0,
                $"{falsch} RZ-Achse(n) zeigen mit `Innen` auf die Kante statt "
                + "von ihr weg - dort landen die Kacheln IM Parkplatz");
        }
        var gassen = l.AisleLine ?? Array.Empty<float2[]>();
        Console.WriteLine($"  Nutzerform 16:06: {l.Stalls} Buchten, "
            + $"{gassen.Length} Gassen, {zoning.Length} Zoning-Kurse");

        /*
         * REGEL 2: DIE TREPPE.
         *
         * Zwei Kanten, davon eine schraeg - es braucht mehr als einen
         * Abschnitt, und sie muessen auf verschiedenen Hoehen liegen.
         */
        /*
         * GEMESSEN WIRD IM GASSENRAHMEN, NICHT IN WELT-Y.
         *
         * Erster Anlauf verglich die Welt-y-Mitte von Gasse und Zoning-Kurs.
         * Das geht nur, solange die Gassen waagerecht durch die Welt laufen.
         * An der echten Form des Nutzers laufen sie fast SENKRECHT - der
         * Reihenwinkel kommt dort von der langen rechten Kante -, und die
         * Welt-y-Mitte einer senkrechten Gasse sagt gar nichts. Der Lauf
         * meldete Fehler, die er nicht belegen konnte.
         *
         * Richtig ist der Abstand QUER zur Gassenrichtung: zwei Gassen
         * unterscheiden sich genau darin, und ein Zoning-Kurs liegt auf einer
         * Gasse, wenn sein Querwert derselbe ist.
         */
        var g0 = gassen[0];
        var achse = math.normalize(g0[g0.Length - 1] - g0[0]);
        double Quer(float2 p) => -achse.y * p.x + achse.x * p.y;

        var hoehen = zoning
            .Select(n => Math.Round((Quer(n.A) + Quer(n.B)) / 2.0, 1))
            .Distinct().OrderBy(h => h).ToArray();
        Console.WriteLine("    Zoning quer: " + (hoehen.Length == 0 ? "keine"
            : string.Join(", ", hoehen.Select(h => $"{h:F1}"))));
        Pruefe(hoehen.Length >= 2,
            $"nur {hoehen.Length} Höhe(n) mit Zoning-Straße - eine Diagonale "
            + "braucht eine Treppe aus Abschnitten, einen je Gassenhöhe");

        /*
         * UND JEDER ABSCHNITT LIEGT AUF EINER GASSE.
         *
         * Sonst ist es wieder eine erzwungene Zusatzstrasse, nur diesmal
         * mehrere davon.
         */
        var gassenhoehen = gassen
            .Select(g => (Quer(g[0]) + Quer(g[g.Length - 1])) / 2)
            .ToArray();
        /*
         * AUF EINER GASSENLINIE - NICHT AUF EINER UEBRIGGEBLIEBENEN GASSE.
         *
         * `Fuege` schneidet jeden Kurs von jeder Zoning-Strasse weg. Eine
         * Gasse, die ZUR Zoning-Strasse geworden ist, wird also gegen sich
         * selbst geschnitten und verschwindet aus `AisleLine` - und zwar zu
         * Recht: sie ist auf diesem Stueck durch den Strassenkurs ERSETZT.
         *
         * Der erste Anlauf suchte sie trotzdem in der Gassenliste und meldete
         * sie als fehlend. Am Bauzettel 2026-09-10 16:18: Kurs bei quer
         * 1142,8, Gassen bei 1057,6 / 1078,9 / 1100,2 / 1121,5. Der Kurs liegt
         * exakt einen Modulabstand (21,30 m) neben der aeussersten Gasse - er
         * IST die naechste Gassenlinie, sie ist nur nicht mehr als Gasse
         * uebrig.
         *
         * Gemessen wird deshalb der Rasterabstand: der Querwert muss ein
         * Vielfaches des Modulabstands von einer Gasse entfernt sein.
         */
        var modul = 2 * e.Sl + e.Ai + e.Md;
        double AufGassenlinie(double y)
        {
            if (gassenhoehen.Length == 0) return double.PositiveInfinity;
            var d = gassenhoehen.Min(gy => Math.Abs(gy - y));
            var rest = d % modul;
            return Math.Min(rest, modul - rest);
        }
        var neben = zoning.Count(n =>
            AufGassenlinie((Quer(n.A) + Quer(n.B)) / 2) > 0.5);
        var abweichung = zoning.Length == 0 ? double.NaN
            : zoning.Max(n => AufGassenlinie((Quer(n.A) + Quer(n.B)) / 2));
        Console.WriteLine($"    Modulabstand {modul:F2} m; größte Abweichung "
            + $"eines Kurses von der Gassenlinie: {abweichung:F2} m");
        Console.WriteLine("    Gassen quer: " + string.Join(", ",
            gassenhoehen.OrderBy(h => h).Select(h => $"{h:F1}")));
        Pruefe(neben == 0,
            $"{neben} von {zoning.Length} Zoning-Kursen liegen auf keiner "
            + "Gassenlinie - die RZ-Straße soll eine Gasse SEIN, keine "
            + "zusätzliche");

        /*
         * REGEL 3: NICHTS LAEUFT IN DIE KACHELN.
         *
         * Das Kachelband reicht von der RZ-Kante bis zur Innenkante der
         * RZ-Strasse. Dort darf ausser der Strasse selbst kein Kurs liegen -
         * kein Endfussweg, kein Verbinder, keine Gasse.
         *
         * Das deckt beide Haelften der Skizze ab: der orange Diagonalweg faellt
         * darunter, die pinken Verbinder liegen ausserhalb und bleiben erlaubt.
         */
        var drin = 0;
        var beispiel = "";
        foreach (var kante in new[] { (form[1], form[2]), (form[2], form[3]) })
        {
            var ka = kante.Item1;
            var kd = kante.Item2 - ka;
            var kl = math.length(kd);
            if (kl < 1e-4f) continue;
            double Abstand(float2 p)
                => Math.Abs(kd.x * (p.y - ka.y) - kd.y * (p.x - ka.x)) / kl;
            double Laengs(float2 p)
                => (kd.x * (p.x - ka.x) + kd.y * (p.y - ka.y)) / kl;

            // Wie tief reicht das Kachelband? Bis zur naechsten Zoning-Achse.
            var band = zoning
                .Where(n => Laengs((n.A + n.B) * 0.5f) > -5
                    && Laengs((n.A + n.B) * 0.5f) < kl + 5)
                .Select(n => Abstand((n.A + n.B) * 0.5f))
                .DefaultIfEmpty(double.NaN).Min();
            if (double.IsNaN(band)) continue;
            /*
             * DAS KACHELBAND ENDET AN DER AUSSENKANTE DER STRASSE.
             *
             * Hier stand `+ Halb` - die INNENkante. Damit zaehlte der
             * Strassenkorridor selbst zum Bauland, und jeder Querweg, der die
             * Strasse erreicht, galt als Fehler. Er muss den Korridor aber
             * durchqueren, sonst kommt er nicht an.
             */
            var innenkante = band - ParkingGeometry.ZoningStrassenbreite / 2;

            foreach (var n in netz)
            {
                if (n.Kind == "zoning") continue;
                var m = (n.A + n.B) * 0.5f;
                if (Laengs(m) < 0 || Laengs(m) > kl) continue;
                if (Abstand(m) >= innenkante - 0.01) continue;
                drin++;
                if (beispiel.Length == 0)
                    beispiel = $"{n.Kind} bei {Abstand(m):F1} m "
                        + $"(Kachelband bis {innenkante:F1} m)";
            }
        }
        /*
         * UND AUCH HIER: KEIN STUECK KANTE OHNE STUFE.
         *
         * Diese Form zeigt den Mangel am deutlichsten - hier endete jede
         * Gasse 15,41 m frueher, als ihr Fenster reichte.
         */
        foreach (var (ka2, kb2, name2) in new[]
        {
            (form[1], form[2], "Diagonale"),
            (form[2], form[3], "senkrechte Kante"),
        })
        {
            var luecke = LueckeInDerTreppe(ka2, kb2, achse, zoning);
            Console.WriteLine($"    {name2}: längste Lücke in der Treppe "
                + $"{luecke:F2} m");
            Pruefe(luecke < ParkingGeometry.ZoningStrassenbreite,
                $"{name2}: {luecke:F2} m der Kante haben gar keine "
                + "RZ-Straße - dort ist die Treppe unterbrochen");
        }

        Console.WriteLine($"    {drin} Kurs(e) im Kachelband"
            + (beispiel.Length == 0 ? "" : " — z. B. " + beispiel));
        Pruefe(drin == 0,
            $"{drin} Kurs(e) laufen in die Kacheln hinein — {beispiel}. An "
            + "einer RZ-Kante entsteht kein Endfußweg; verbunden wird "
            + "außerhalb der Kachelfläche");

        return fehler;
    }
}
