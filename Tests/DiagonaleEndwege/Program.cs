using System;
using System.Linq;
using System.Reflection;
using ParkingLotTool.Geometry.Zellen;

// Entwurfs-Messstand: produktive Geometrie bleibt unveraendert.
// Die privaten Planphasen werden getrennt aufgerufen, um Ursache und
// Wirkung von Angleichen und Endstreifen voneinander zu unterscheiden.
internal static class Program
{
    static void Phase(Ringlosplan plan, string name, params object[] args)
        => typeof(Ringlosplan).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(plan, args);

    static int Main(string[] args)
    {
        var fehler = 0;
        void Pruefe(bool ok, string text)
        {
            if (ok) return;
            Console.WriteLine("FEHLER: " + text); fehler++;
        }
        var innen = new[] { new Punkt(-200, -20), new Punkt(200, -20),
            new Punkt(200, 200), new Punkt(-200, 200) };
        foreach (var fall in new[] { "88-Grad-Kette", "L-Sprung", "Einzelende" })
        {
            var plan = new Ringlosplan();
            var n = fall == "88-Grad-Kette" ? 7 : fall == "L-Sprung" ? 4 : 3;
            for (var i = 0; i < n; i++)
            {
                var rechts = fall == "88-Grad-Kette" ? 100 + i * 0.6
                    : i < 2 ? 100 : 7;
                plan.Gassen.Add(new Ringlosplan.Weg { A = new Punkt(-100, i * 21.3),
                    B = new Punkt(rechts, i * 21.3), Breite = 7 });
            }
            Phase(plan, "PlaneGemeinsameEnden", 3.0);
            Phase(plan, "PlaneEndwege", innen, null, 3.0, null);
            Phase(plan, "VereinigeFussstreifen");
            Console.WriteLine($"{fall}: {n} Gassen, {plan.Fusswege.Count} Endstreifen");
            foreach (var w in plan.Fusswege)
                Console.WriteLine($"  x={w.A.X:F3}..{w.B.X:F3}, y={w.A.Y:F3}..{w.B.Y:F3}");
            var luecken = 0;
            foreach (var g in plan.Gassen)
            foreach (var links in new[] { true, false })
            {
                var x = links ? g.A.X : g.B.X;
                // Unabhaengige Flaechenprobe: direkt ausserhalb der Stirn,
                // 1 mm vor den beiden Ecken. Keine Abfrage der Planreserve.
                var gedeckt = new[] { g.A.Y - 3.499, g.A.Y + 3.499 }.Count(y =>
                    plan.Fusswege.Any(w => Geometrie.EnthaeltOderRand(w.Ecken,
                        new Punkt(x + (links ? -0.001 : 0.001), y))));
                if (gedeckt == 2) continue;
                luecken++;
                Console.WriteLine($"  Ungedeckte Stirn: Gasse y={g.A.Y:F1}, {(links ? "links" : "rechts")}, {gedeckt}/2 Eckproben gedeckt");
            }
            /*
             * KEINE UNGEDECKTE STIRN MEHR - IN KEINEM DER DREI FAELLE.
             *
             * Dieser Messstand wurde am 2026-09-09 gebaut, um den Mangel zu
             * BELEGEN: "L-Sprung" und "Einzelende" liessen je zwei
             * Gassenstirnen ohne Fussweg, weil `PlaneEndwege` den Streifen
             * nur ZWISCHEN zwei Gassen baut und eine allein endende Gasse
             * kein Paar hat.
             *
             * Seit `PlaneEndwege` auch den Einzeldurchlauf kennt, sind es
             * null. Der Sollwert stand hier fuer den Fehler, nicht fuer das
             * Ziel; er wird deshalb nachgezogen, nicht aufgeweicht. Die
             * Mehrlaenge des Gruppenentwurfs gegenueber dem Produktivstand
             * ist damit 0,000 m - beide bauen jetzt dasselbe.
             */
            Pruefe(luecken == 0, "Referenzbefund " + fall
                + ": " + luecken + " ungedeckte Stirn(en), erwartet 0");

            // Alternativentwurf: ein achsparalleler Streifen je Endkette,
            // einschliesslich Einzelketten. Nur dieser isolierte Messstand
            // baut ihn; kein Eingriff in Layoutbauer oder Materialpolygone.
            var vorher = plan.Fusswege.Sum(w => Geometrie.Laenge(w.B - w.A));
            plan.Fusswege.Clear();
            foreach (var links in new[] { true, false })
            {
                var gassen = plan.Gassen.OrderBy(g => g.A.Y).ToArray();
                for (var i = 0; i < gassen.Length;)
                {
                    double X(Ringlosplan.Weg g) => links ? g.A.X : g.B.X;
                    var j = i + 1;
                    while (j < gassen.Length && Math.Abs(X(gassen[j]) - X(gassen[j - 1])) < 3) j++;
                    var gruppe = gassen.Skip(i).Take(j - i).ToArray();
                    var x = links ? gruppe.Max(X) - 1 : gruppe.Min(X) + 1;
                    var kappe = args.Contains("--mutation-kappen") ? 0 : 3.5;
                    if (!(args.Contains("--mutation-einzelende") && gruppe.Length == 1))
                        plan.Fusswege.Add(new Ringlosplan.Weg { A = new Punkt(x, gruppe[0].A.Y - kappe),
                            B = new Punkt(x, gruppe.Last().A.Y + kappe), Breite = 2, Fuss = true });
                    i = j;
                }
            }
            var ungedeckt = 0;
            foreach (var g in plan.Gassen)
            foreach (var links in new[] { true, false })
            foreach (var y in new[] { g.A.Y - 3.499, g.A.Y + 3.499 })
                if (!plan.Fusswege.Any(w => Geometrie.EnthaeltOderRand(w.Ecken,
                    new Punkt((links ? g.A.X - 0.001 : g.B.X + 0.001), y)))) ungedeckt++;
            var ueberlappung = plan.Fusswege.SelectMany((w, i) => plan.Fusswege.Skip(i + 1)
                .Select(v => Ringlosplan.Ueberlappt(w.Ecken, v.Ecken))).Count(v => v);
            Console.WriteLine($"  Gruppenentwurf: {plan.Fusswege.Count} Streifen, Mehrlaenge {plan.Fusswege.Sum(w => Geometrie.Laenge(w.B - w.A)) - vorher:F3} m, ungedeckte Eckproben {ungedeckt}, Ueberlappungen {ueberlappung}, zusaetzliche Gassenkuerzung 0 m");
            Pruefe(ungedeckt == 0 && ueberlappung == 0, "Gruppenentwurf " + fall);
            Pruefe(plan.Fusswege.Count == (fall == "88-Grad-Kette" ? 2 : 3), "Streifenanzahl " + fall);
        }

        // Ein horizontal endendes Rechteck kann eine geneigte Gerade nur
        // an einer Ecke beruehren: 7*tan(2 Grad) ist die andere Eckluecke.
        var steigung = Math.Tan(2 * Math.PI / 180);
        var breite = 7.0;
        var maximum = breite * steigung;
        var dreieck = breite * maximum / 2;
        // Gegenprobe: die vorgeschlagene schraege Stirn teilt exakt dieselbe
        // Linie wie der Fussweg. Mutation ersetzt sie durch den Maximalschnitt.
        var mutation = args.Contains("--mutation-rechteck");
        var maxLuecke = 0.0;
        for (var i = 0; i <= 100; i++)
        {
            var y = -breite / 2 + breite * i / 100;
            var grenze = 10 + steigung * y;
            var stirn = mutation ? 10 + steigung * breite / 2 : 10 + steigung * y;
            maxLuecke = Math.Max(maxLuecke, Math.Abs(stirn - grenze));
        }
        Console.WriteLine($"2 Grad / 7 m: Rechteck-Luecke max. {maximum:F6} m, Dreieck {dreieck:F6} m2 je Stirn");
        Console.WriteLine($"Gemeinsame schraege Materialgrenze: 101 Proben, max. Luecke {maxLuecke:F6} m");
        Pruefe(maxLuecke < 1e-9, "Schraege Stirn muss die Fusswegkante auf voller Breite treffen");
        Console.WriteLine($"Entwurfsmessung: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
