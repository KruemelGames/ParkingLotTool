using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * LIEGEN DIE QUERSTRASSEN AUF EINEM GITTER?
 *
 * Befund des Nutzers am 2026-09-09: *"Bei Randstrassen aus verhaelt sich die
 * Position der Querstrassen sehr komisch, die bewegen sich oft - bzw. einige
 * und manche nicht. Es bleibt kein richtiges Grid mehr."*
 *
 * Ein Parkplatz liest sich als Gitter, wenn die Querstrassen benachbarter
 * Korridore FLUCHTEN. Der Plan hat dafuer alles beisammen: einen einzigen
 * Anker (die erste Autozufahrt) und einen einzigen Schritt aus den Reglern.
 * Ob beide auch ankommen, misst dieser Lauf.
 *
 * Gemessen wird die Laengslage jeder Querstrasse im Gassenrahmen. Zwei
 * Querstrassen fluchten, wenn ihr Abstand ein Vielfaches des Schritts ist.
 */
internal static partial class Program
{
    private static int RunQuerstrassengitter()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        /*
         * MEHRERE FORMEN, WEIL EINE NICHTS BEWEIST.
         *
         * Die erste stammt aus dem Bauzettel 2026-09-09 12:19 und hat
         * versetzte Gassenenden. Die uebrigen treiben genau das auf die
         * Spitze: bei einem Dreieck und einer Treppe ist jeder Korridor
         * anders lang, und jeder koennte sich seine eigene Lage suchen.
         */
        var formen = new (string Name, float2[] Punkte, int Kante, double Along)[]
        {
            ("Bauzettel 12:19", new[]
            {
                new float2(-1037.0201416015625f, 118.68980407714844f),
                new float2(-1121.4210205078125f, 121.58500671386719f),
                new float2(-1124.6280517578125f, 28.173002243041992f),
                new float2(-1191.7340087890625f, 30.476001739501953f),
                new float2(-1195.4329833984375f, -77.29215240478516f),
                new float2(-1043.880615234375f, -81.19245910644531f),
            }, 5, 63.35395431518555),
            ("Dreieck 200 x 140", new[]
            {
                new float2(0, 0), new float2(200, 0), new float2(0, 140),
            }, 0, 100),
            ("Treppe 180 x 120", new[]
            {
                new float2(0, 0), new float2(180, 0), new float2(180, 30),
                new float2(120, 30), new float2(120, 60), new float2(60, 60),
                new float2(60, 90), new float2(0, 90),
            }, 0, 90),
            ("Trapez 200/90 x 120", new[]
            {
                new float2(0, 0), new float2(200, 0),
                new float2(155, 120), new float2(45, 120),
            }, 0, 100),
        };

        foreach (var f in formen)
            fehler += PruefeQuerstrassengitter(f.Name, f.Punkte, f.Kante, f.Along);

        fehler += PruefeAnkerruhe();

        Console.WriteLine($"Querstrassengitter: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    /**
     * WANDERT DAS GITTER, WENN SICH DIE KONTUR UM EINEN ZEHNTELMILLIMETER
     * AENDERT?
     *
     * Befund von Astra am 2026-09-09: die Lage der Querstrassen haengt an
     * einem Anker, und der wird auf die erste Gassenhoehe projiziert - die
     * Zufahrt faehrt ihre Innennormale entlang, bis sie die Gasse trifft.
     * Steht sie fast PARALLEL zu den Gassen, ist diese Fahrt fast unendlich
     * lang. Gemessen: `Innennormale.Y = 0,000012428040`, Anker 162 km vom
     * Rahmenursprung - und eine Konturaenderung unter einem Millimeter
     * verschob die Gitterphase um 14,2 cm.
     *
     * Das ist an sich noch kein sichtbarer Fehler, aber es macht das Gitter
     * unberechenbar: der Nutzer zieht eine Ecke um Haaresbreite und die
     * Querstrassen springen. Gemessen wird deshalb die RUHE - dieselbe Form,
     * ein Punkt um 0,1 mm quer versetzt, und die Lage der Querstrassen darf
     * sich nicht mehr bewegen als der Versatz selbst rechtfertigt.
     */
    private static int PruefeAnkerruhe()
    {
        var fehler = 0;

        // Die Nutzerform aus dem Bauzettel 12:19 und die Zufahrt, deren
        // Achse Astra als nahezu reihenparallel gemessen hat.
        var form = new[]
        {
            new float2(-1037.02026f, 118.689781f),
            new float2(-1170.38806f, 123.265007f),
            new float2(-1173.37708f, 36.167f),
            new float2(-1239.602f, 38.439003f),
            new float2(-1242.69409f, -51.6540031f),
            new float2(-1043.099f, -58.504f),
        };

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;

        /*
         * KANTE 5 BEZIEHUNGSWEISE 6, ALONG 80 - Astras Fall.
         *
         * Das ist die rechte, fast senkrechte Kante. Die Reihenrichtung kommt
         * von der langen unteren Kante, also steht die Innennormale dieser
         * Zufahrt fast LAENGS zu den Gassen; Astra hat ihre Querkomponente mit
         * 0,000012428040 gemessen. Genau durch diese Zahl teilt die
         * Ankerprojektion.
         *
         * Verglichen werden die beiden Umrisse aus den Vorschau-Berichten:
         * derselbe Umriss, einmal mit dem gestreckten Punkt. Die Zufahrt liegt
         * in beiden an derselben WELTstelle - nur ihre Kantennummer
         * verschiebt sich, weil der Punkt davor eingefuegt wird.
         */
        var mitPunkt = new[]
        {
            new float2(-1037.02026f, 118.689781f),
            new float2(-1170.38806f, 123.265007f),
            new float2(-1173.37708f, 36.167f),
            new float2(-1239.602f, 38.439003f),
            new float2(-1242.69409f, -51.6540031f),
            new float2(-1175.22607f, -53.969f),
            new float2(-1043.099f, -58.504f),
        };

        double[] Lagen(float2[] punkte, int kante)
        {
            e.Entrances = new[]
            {
                new Entrance
                {
                    Edge = kante, Along = 80, Art = Zufahrtsart.Zufahrt,
                },
            };
            var layout = ParkingGeometry.Build(punkte, e);
            if (layout.AisleLine == null || layout.AisleLine.Length == 0)
                return Array.Empty<double>();
            var g0 = layout.AisleLine[0];
            var achse = math.normalize(g0[g0.Length - 1] - g0[0]);
            return (layout.NetLine ?? Array.Empty<NetSegment>())
                .Where(n => string.Equals(n.Kind, "cross",
                    StringComparison.Ordinal))
                .Select(n => (double)math.dot((n.A + n.B) * 0.5f, achse))
                .OrderBy(x => x).ToArray();
        }

        var vorher = Lagen(form, 5);
        var nachher = Lagen(mitPunkt, 6);

        Console.WriteLine($"  Ankerruhe: {vorher.Length} Querstraßen ohne, "
            + $"{nachher.Length} mit dem gestreckten Punkt");
        if (vorher.Length == 0 || nachher.Length != vorher.Length)
        {
            fehler++;
            Console.WriteLine("FEHLER: die Zahl der Querstraßen ändert sich "
                + "durch einen Punkt auf der Geraden");
            return fehler;
        }

        var groesste = Enumerable.Range(0, vorher.Length)
            .Max(k => Math.Abs(vorher[k] - nachher[k]));
        Console.WriteLine($"    größte Verschiebung {groesste * 1000:F1} mm");
        if (groesste > 0.01)
        {
            fehler++;
            Console.WriteLine($"FEHLER: der gestreckte Punkt verschiebt eine "
                + $"Querstraße um {groesste * 1000:F0} mm - das Gitter hängt "
                + "an einem Anker, der weit aus dem Parkplatz "
                + "herausprojiziert");
        }
        return fehler;
    }

    private static int PruefeQuerstrassengitter(
        string name, float2[] form, int kante, double along)
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;
        e.Entrances = new[]
        {
            new Entrance { Edge = kante, Along = along, Art = Zufahrtsart.Zufahrt },
        };

        var layout = ParkingGeometry.Build(form, e);
        if (layout.AisleLine == null || layout.AisleLine.Length == 0)
        {
            Console.WriteLine($"  {name}: keine Gasse - übersprungen");
            return 0;
        }

        // Der Gassenrahmen: laengs = entlang der Gassen, quer = quer dazu.
        var g0 = layout.AisleLine[0];
        var achse = math.normalize(g0[g0.Length - 1] - g0[0]);
        var norm = new float2(-achse.y, achse.x);
        double Laengs(float2 p) => p.x * achse.x + p.y * achse.y;
        double Quer(float2 p) => p.x * norm.x + p.y * norm.y;

        /*
         * DER SCHRITT, WIE IHN DER PLAN RECHNET.
         *
         * Dieselbe Zeile wie in `Ringlos.PlaneQuerstrassen` - sie steht dort
         * privat, und ein Testwert von Hand waere ein zweites Messgeraet.
         */
        var n = Math.Max(1, (int)Math.Round(
            e.Cr / e.Sw - (e.Qk ? 2 : 0), MidpointRounding.AwayFromZero));
        var schritt = (n + (e.Qk ? 2 : 0)) * e.Sw + e.Cw;
        var rest = (Math.Min(5, n) + (e.Qk ? 2 : 0)) * e.Sw;

        var gassen = layout.AisleLine
            .Select(g => (Quer: Quer(g[0]),
                          Von: Math.Min(Laengs(g[0]), Laengs(g[g.Length - 1])),
                          Bis: Math.Max(Laengs(g[0]), Laengs(g[g.Length - 1]))))
            .ToArray();

        var quer = (layout.NetLine ?? Array.Empty<NetSegment>())
            .Where(s => string.Equals(s.Kind, "cross", StringComparison.Ordinal))
            .Select(s => (Laengs: (Laengs(s.A) + Laengs(s.B)) / 2,
                          Von: Math.Min(Quer(s.A), Quer(s.B)),
                          Bis: Math.Max(Quer(s.A), Quer(s.B))))
            .OrderBy(s => s.Von).ThenBy(s => s.Laengs)
            .ToArray();

        Console.WriteLine($"  {name}: {layout.AisleLine.Length} Gassen, "
            + $"{quer.Length} Querstraßen, Schritt {schritt:F1} m "
            + $"(N = {n}), Buchten {layout.Stalls}");

        /*
         * DIE KORRIDORE EINZELN - je zwei benachbarte Gassen.
         *
         * Fluchten heisst nicht "alle an derselben Stelle": jeder Korridor
         * traegt mehrere Querstrassen im Abstand `schritt`. Verglichen wird
         * deshalb die PHASE, also der Rest zum Schritt.
         */
        var korridore = quer
            .GroupBy(s => (Math.Round(s.Von, 1), Math.Round(s.Bis, 1)))
            .OrderBy(k => k.Key.Item1)
            .ToArray();

        var phasen = new List<double>();
        var gefordert = new List<bool>();
        foreach (var k in korridore)
        {
            var lagen = k.Select(s => s.Laengs).OrderBy(x => x).ToArray();
            var phase = ((lagen[0] % schritt) + schritt) % schritt;
            phasen.Add(phase);

            /*
             * EIN SCHMALES REGELBAND KANN KEINE GITTERLINIE ENTHALTEN.
             *
             * Der Plan haelt `rest` von jedem Gassenende frei. Was uebrig
             * bleibt, ist das Band, in dem die Querstrasse liegen darf. Ist
             * es schmaler als ein Schritt, liegt darin nur zufaellig eine
             * Gitterlinie - dort die Flucht zu FORDERN hiesse, entweder die
             * N-Regel oder die Verbindung zu opfern. Beides steht hoeher.
             *
             * Die drei Zeilen unten sind dieselbe Rechnung wie in
             * `Ringlos.PlaneQuerstrassen` - sie ist dort privat, und ein
             * Zahlenwert von Hand waere ein zweites Messgeraet, das beim
             * naechsten Reglerwechsel still danebenliegt.
             */
            var beteiligt = gassen
                .Where(g => g.Quer >= k.Key.Item1 - 0.5
                    && g.Quer <= k.Key.Item2 + 0.5).ToArray();
            var bandbreite = beteiligt.Length == 0 ? 0
                : (beteiligt.Min(g => g.Bis) - e.Cw / 2 - rest)
                    - (beteiligt.Max(g => g.Von) + e.Cw / 2 + rest);
            var kann = bandbreite >= schritt;
            gefordert.Add(kann);

            Console.WriteLine($"    quer {k.Key.Item1,8:F1} ..{k.Key.Item2,8:F1}"
                + $"   {lagen.Length} Querstraße(n)   Phase {phase,6:F2}"
                + $"   Band {bandbreite,6:F1} m{(kann ? "" : " (zu schmal)")}"
                + "   längs " + string.Join(" ", lagen.Select(x => x.ToString("F1"))));
        }

        /*
         * EIN GITTER HAT EINE PHASE, NICHT VIELE.
         *
         * DIE PHASE KOMMT VON DEN KORRIDOREN MIT PLATZ. Der erste Anlauf nahm
         * die haeufigste Phase ueberhaupt - an der Treppe 180 x 120 waren das
         * die ZWEI erzwungenen (34,50), und der eine richtige (18,00) wurde
         * als Ausreisser gemeldet. Ein Messgeraet, das seinen Nullpunkt vom
         * Fehler nimmt, misst das Falsche.
         *
         * Verglichen wird auf einen halben Meter genau: das liegt unter der
         * Fahrbahnbreite (3 m), was weiter abweicht, sieht man als Versatz.
         */
        var breite = Enumerable.Range(0, phasen.Count)
            .Where(i => gefordert[i]).ToArray();
        if (breite.Length > 0)
        {
            var haeufigste = breite
                .GroupBy(i => Math.Round(phasen[i], 1))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .First().Key;
            bool Daneben(int i) => Math.Min(Math.Abs(phasen[i] - haeufigste),
                schritt - Math.Abs(phasen[i] - haeufigste)) > 0.5;
            var ausserhalb = breite.Count(Daneben);
            var geduldet = Enumerable.Range(0, phasen.Count)
                .Count(i => !gefordert[i] && Daneben(i));
            Console.WriteLine($"  Häufigste Phase {haeufigste:F2}, "
                + $"{ausserhalb} von {breite.Length} breiten "
                + $"Korridoren daneben ({geduldet} schmale geduldet)");
            Pruefe(ausserhalb == 0,
                $"{name}: {ausserhalb} Korridor(e) mit Platz für eine "
                + "Gitterlinie fluchten trotzdem nicht");
        }

        /*
         * UND DIE N-REGEL GILT WEITER.
         *
         * Sie war der Grund, aus dem der Anker ueberhaupt beschnitten wird:
         * *"Dort liegt eine Querstrasse direkt neben einem Randstrasse-aus-
         * Fussweg."* Ein Gitter, das die Regel wieder bricht, waere kein
         * Fortschritt - deshalb steht beides in EINEM Lauf.
         */
        var mindest = Math.Min(5, n) * e.Sw;
        var zuNah = 0;
        var engste = double.PositiveInfinity;
        foreach (var s in quer)
        {
            // Die Gassen, die dieser Korridor verbindet.
            var beteiligt = gassen
                .Where(g => g.Quer >= s.Von - 0.5 && g.Quer <= s.Bis + 0.5)
                .ToArray();
            if (beteiligt.Length == 0) continue;

            /*
             * EIN ZU KURZER KORRIDOR IST KEIN VERSTOSS.
             *
             * Der Plan sagt es selbst: *"Ist der Korridor zu kurz fuer die
             * Regel, gilt weiter die Verbindung - zwei unverbundene
             * Fahrgassen sind schlimmer als eine Querstrasse zu nah am
             * Rand."* Wer hier trotzdem zaehlt, misst gegen eine Regel, die
             * das Werkzeug bewusst nicht haelt.
             */
            var von = beteiligt.Max(g => g.Von);
            var bis = beteiligt.Min(g => g.Bis);
            if (bis - von < 2 * mindest + e.Cw) continue;

            foreach (var g in beteiligt)
            {
                var abstand = Math.Min(Math.Abs(s.Laengs - g.Von),
                    Math.Abs(s.Laengs - g.Bis));
                // Nur Enden, an denen die Gasse wirklich aufhoert - eine
                // Querstrasse mitten im Korridor hat keinen Stirnkontakt.
                if (abstand > mindest) continue;
                engste = Math.Min(engste, abstand);
                zuNah++;
            }
        }
        Console.WriteLine($"  N-Regel: Mindestabstand {mindest:F1} m, "
            + $"{zuNah} Verstoß(e)"
            + (double.IsPositiveInfinity(engste) ? "" : $", engste {engste:F1} m"));
        Pruefe(zuNah == 0,
            $"{name}: {zuNah} Querstraße(n) näher als {mindest:F1} m an einer "
            + "Gassenstirn - die N-Regel ist gebrochen");

        return fehler;
    }
}
