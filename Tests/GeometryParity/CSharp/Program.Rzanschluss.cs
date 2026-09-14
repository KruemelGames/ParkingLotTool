using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * HAENGT DIE RANDZONING-STRASSE AM PARKPLATZ?
 *
 * Befund des Nutzers am 2026-09-09: *"Sehr auffaellig war, dass die RZ niemals
 * ueber Querstrassen verbunden wurde. Das ist ein grober Fehler."*
 *
 * Gemessen am Vorschauzettel 22:22:21: EIN Zoning-Kurs, 89,0 m lang, an
 * BEIDEN Enden beruehrt ihn nichts. Die naechsten Netzenden waren zwei
 * Querstrassen, 8,41 m von der Achse entfernt - sie hoeren an der letzten
 * Fahrgasse auf.
 *
 * WARUM DER MOD DAS NICHT GEMELDET HAT. Er hat es gemeldet, nur falsch:
 * *"PLT-Zoning: 1 Stueck(e) bilden EINEN zusammenhaengenden Strassenzug."*
 * Diese Zeile vergleicht die Zoning-Strassen nur UNTEREINANDER. Bei einem
 * einzigen Stueck ist sie zwangslaeufig gruen und kann gar nicht rot werden.
 *
 * Ansage des Nutzers dazu: *"Eine Zusammenhangspruefung sollte es nur durch
 * dich geben bzw. durch deine Tests und nicht durch die Mod. Denn das ist
 * wieder der Punkt, wo es anfaengt zu rechnen, weil er repariert."* Also
 * steht sie hier - an einer Stelle, an der sie rot werden kann, und wo der
 * Bau sie nicht als Vorwand fuer eine Reparatur nehmen kann.
 */
internal static partial class Program
{
    private static int RunRzanschluss()
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
            new ParkingGeometry.RandzoningLinie
            {
                A = form[2], B = form[3],
            },
        };

        var layout = ParkingGeometry.Build(form, e);
        var netz = layout.NetLine ?? Array.Empty<NetSegment>();
        var arten = netz.GroupBy(n => n.Kind)
            .ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("  Netzkurse: " + string.Join(", ",
            arten.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));

        var rz = netz.Where(n => string.Equals(n.Kind, "zoning",
            StringComparison.Ordinal)).ToArray();
        Pruefe(rz.Length > 0, "keine Randzoning-Straße geplant");
        if (rz.Length == 0)
        {
            Console.WriteLine($"Rzanschluss: {fehler} Fehler");
            return 1;
        }

        /*
         * EIN NETZ, NICHT ZWEI - und zwar ueber ALLE Kursarten.
         *
         * CS2 verbindet nur ueber einen identischen Endpunkt, nicht ueber
         * Naehe (siehe [[cs2-knoten-statt-localconnect]]). Deshalb zaehlt
         * hier auch nur der gemeinsame Punkt, mit einer Toleranz von 5 cm
         * fuer die float-Rechnung des Bauwegs.
         *
         * Ein T-Stoss mitten auf einem Kurs zaehlt NICHT: dort entsteht kein
         * Knoten, und ohne Knoten ist die Strasse fuer das Spiel getrennt.
         */
        const float toleranz = 0.05f;
        var eltern = Enumerable.Range(0, netz.Length).ToArray();
        int Wurzel(int i)
        {
            while (eltern[i] != i) i = eltern[i] = eltern[eltern[i]];
            return i;
        }
        for (var i = 0; i < netz.Length; i++)
        for (var k = i + 1; k < netz.Length; k++)
        {
            var beruehrt =
                math.distance(netz[i].A, netz[k].A) < toleranz
                || math.distance(netz[i].A, netz[k].B) < toleranz
                || math.distance(netz[i].B, netz[k].A) < toleranz
                || math.distance(netz[i].B, netz[k].B) < toleranz;
            if (!beruehrt) continue;
            var wa = Wurzel(i);
            var wb = Wurzel(k);
            if (wa != wb) eltern[wa] = wb;
        }

        var netze = Enumerable.Range(0, netz.Length).Select(Wurzel).Distinct()
            .Count();
        var rzWurzeln = rz.Select(r => Wurzel(Array.IndexOf(netz, r)))
            .Distinct().ToArray();
        var gassenWurzel = netz
            .Select((n, i) => (n, i))
            .Where(t => string.Equals(t.n.Kind, "aisle", StringComparison.Ordinal))
            .Select(t => Wurzel(t.i)).Distinct().ToArray();

        Console.WriteLine($"  {netz.Length} Kurse bilden {netze} Netz(e); "
            + $"Randzoning in {rzWurzeln.Length}, Fahrgassen in "
            + $"{gassenWurzel.Length}");

        /*
         * DIE FORDERUNG. Jede Randzoning-Strasse liegt im selben Netz wie die
         * Fahrgassen. Nicht "es gibt nur ein Netz" - der Parkplatz darf aus
         * gutem Grund mehrere haben -, sondern: die RZ-Strasse haengt nicht
         * allein in der Luft.
         */
        var verbunden = rzWurzeln.All(w => gassenWurzel.Contains(w));
        var anschluesse = netz.Where(n => n.Kind == "cross")
            .Select(n => (Kurs: n, Abstand: rz.Min(r => Math.Min(
                Math.Min(math.distance(n.A, r.A), math.distance(n.A, r.B)),
                Math.Min(math.distance(n.B, r.A), math.distance(n.B, r.B))))))
            .Where(n => n.Abstand < toleranz).ToArray();
        Pruefe(anschluesse.Length > 0, "kein Querstrassenkurs am RZ-Knoten");
        foreach (var anschluss in anschluesse)
            Console.WriteLine($"    Queranschluss: {math.distance(anschluss.Kurs.A, anschluss.Kurs.B):F6} m, "
                + $"Knotenabstand {anschluss.Abstand:F9} m");
        if (!verbunden)
        {
            // Wie weit ist es denn? Die Zahl gehoert in die Meldung, sonst
            // sucht der naechste Leser sie sich selbst zusammen.
            var naechste = double.PositiveInfinity;
            foreach (var r in rz)
            foreach (var n in netz)
            {
                if (string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
                    continue;
                foreach (var p in new[] { n.A, n.B })
                foreach (var q in new[] { r.A, r.B })
                    naechste = Math.Min(naechste, math.distance(p, q));
            }
            Pruefe(false,
                $"die Randzoning-Straße hängt frei - kein gemeinsamer Punkt "
                + $"mit dem Parkplatznetz, der nächste liegt {naechste:F2} m "
                + "entfernt");
            foreach (var n in netz.Where(n => !string.Equals(n.Kind, "zoning",
                StringComparison.Ordinal)))
            foreach (var q in rz)
            {
                var d = Math.Min(
                    Math.Min(math.distance(n.A, q.A), math.distance(n.A, q.B)),
                    Math.Min(math.distance(n.B, q.A), math.distance(n.B, q.B)));
                if (d > naechste + 0.01) continue;
                Console.WriteLine($"    nächster: {n.Kind} "
                    + $"({n.A.x:F2}/{n.A.y:F2})..({n.B.x:F2}/{n.B.y:F2})");
            }
        }

        foreach (var r in rz)
            Console.WriteLine($"    RZ-Kurs ({r.A.x,9:F2}/{r.A.y,8:F2}) .. "
                + $"({r.B.x,9:F2}/{r.B.y,8:F2})   Länge "
                + $"{math.distance(r.A, r.B),6:F1} m");

        /*
         * UND WIE NAH LIEGT EINE FAHRGASSE AN DER RZ-STRASSE?
         *
         * Befund des Nutzers am 2026-09-10: *"Aus dem Vorbauzettel sollte auch
         * herausgehen, dass eine Fahrtgasse auf bzw. nahe an der
         * Randzonenstrasse liegt."*
         *
         * Fuer die RANDSTRASSE gibt es diese Regel laengst: `--gassenabstand`
         * verlangt zwischen zwei parallelen Achsen eine Buchtreihe Platz.
         * Dieser Lauf laeuft aber ausschliesslich mit `Randstrassen = true` -
         * schaltet der Nutzer sie aus und setzt Randzoning, tritt die
         * RZ-Strasse an ihre Stelle und wird von NIEMANDEM geprueft.
         *
         * Die Achsen sind parallel; gemessen wird der Lotabstand. Gefordert
         * ist dasselbe wie bei der Randstrasse: halbe Fahrgasse plus halbe
         * Zoningstrasse plus eine Buchtreihe - sonst steht die Gasse auf der
         * Strasse oder klebt an ihr.
         */
        /*
         * NUR WER LAENGS NEBENEINANDER LIEGT, IST ZU NAH.
         *
         * Gemessen wurde der Lotabstand zur UNENDLICHEN Achse. Solange die
         * RZ-Strasse eigens erzeugt wurde, war das richtig - keine Gasse
         * durfte irgendwo auf dieser Geraden liegen.
         *
         * Seit dem 2026-09-10 IST die naechstliegende Fahrgasse die
         * RZ-Strasse. Auf ihrer Geraden liegt deshalb zwangslaeufig eine
         * Gasse: das Stueck, das nicht Strasse geworden ist. Es stoesst am
         * Knoten an und laeuft in die andere Richtung weiter. Der Lauf mass
         * dort 0,00 m und meldete "steht auf der Strasse" - gemeint war aber
         * das Gegenteil, naemlich der gewuenschte Anschluss.
         *
         * Die Forderung bleibt dieselbe und wird nicht weicher: zwei Achsen,
         * die sich LAENGS UEBERDECKEN, brauchen 13,40 m Abstand. Neu ist nur,
         * dass die Ueberdeckung Bedingung ist - genau so, wie die Mod es im
         * Bauzettel selbst rechnet.
         */
        var mindest = e.Ai / 2 + ParkingGeometry.ZoningStrassenbreite / 2 + e.Sl;
        var engste = double.PositiveInfinity;
        var engsteGasse = -1;
        for (var g = 0; g < (layout.AisleLine?.Length ?? 0); g++)
        {
            var gasse = layout.AisleLine[g];
            var ga = gasse[0];
            var gb = gasse[gasse.Length - 1];
            foreach (var r in rz)
            {
                var d = r.B - r.A;
                var l = math.length(d);
                if (l < 1e-4f) continue;
                var u = d / l;

                // Ueberdecken sich beide laengs der RZ-Achse?
                float Laengs(float2 p) => u.x * (p.x - r.A.x) + u.y * (p.y - r.A.y);
                var g0 = Math.Min(Laengs(ga), Laengs(gb));
                var g1 = Math.Max(Laengs(ga), Laengs(gb));
                if (Math.Min(g1, l) - Math.Max(g0, 0f) <= 0.01) continue;

                var mitte = (ga + gb) * 0.5f;
                var abstand = Math.Abs(d.x * (mitte.y - r.A.y)
                    - d.y * (mitte.x - r.A.x)) / l;
                if (abstand >= engste) continue;
                engste = abstand;
                engsteGasse = g;
            }
        }

        Console.WriteLine($"  Engste Fahrgasse zur RZ-Straße: "
            + (engsteGasse < 0 ? "keine"
                : $"Gasse {engsteGasse}, {engste:F2} m "
                    + $"(gefordert {mindest:F2} m)"));
        Pruefe(engsteGasse < 0 || engste >= mindest - 0.01,
            $"Fahrgasse {engsteGasse} liegt {engste:F2} m von der RZ-Straßen"
            + $"achse - gefordert sind {mindest:F2} m (halbe Fahrgasse + halbe "
            + "Zoningstraße + eine Buchtreihe). Sie steht auf der Straße oder "
            + "klebt an ihr.");

        var messung = ZoningGassenabstand.Beschreibe(layout, e);
        Console.WriteLine("  Zettelmessung: " + messung);
        /*
         * UND DIE ZETTELMESSUNG MUSS DASSELBE FINDEN.
         *
         * Hier standen die Zahlen des alten Entwurfs fest im Text ("Gasse 5",
         * "13.78 m"). Die Stelle, an der die RZ-Strasse liegt, hat sich
         * geaendert; die Zahl mitzuschreiben waere aber nur Buchhaltung.
         *
         * Staerker ist der Vergleich ZWEIER unabhaengiger Rechnungen: der
         * Bauzettel rechnet in `ZoningGassenabstand`, dieser Lauf rechnet
         * oben selbst. Beide muessen auf denselben Abstand kommen - sonst
         * luegt eine von beiden, und welche, sieht man am Unterschied.
         */
        Pruefe(messung.Contains($"gefordert {mindest.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} m"),
            $"Zettelmessung nennt nicht die geforderten {mindest:F2} m");
        Pruefe(engsteGasse < 0
            || messung.Contains($"Achsabstand {engste.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} m"),
            $"Zettelmessung und Lauf kommen auf verschiedene Abstände - "
            + $"der Lauf misst {engste:F2} m, der Zettel sagt: {messung}");

        Console.WriteLine($"Rzanschluss: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
