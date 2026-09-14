using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

/**
 * HAENGEN VERSETZTE GASSENENDEN AN EINER SCHRAEGEN KANTE ZUSAMMEN?
 *
 * Befund des Nutzers am 2026-09-09, zu zwei Formen mit gleichen Reglern:
 *
 *   *"Es wurden halt einfach Fusswege an die Strassen gepappt ohne dass die
 *   sich untereinander verbinden. … Bei [der Diagonalen] sollen die
 *   versetzten Endpunkte der Fahrtgasse durch die Fahrtgassenfusswege
 *   miteinander verbunden werden wie bei den anderen, die halt auf gleicher
 *   Hoehe sind."*
 *
 * Die STUFE ist richtig: zwei Endgruppen, jede fuer sich verbunden. Die
 * DIAGONALE ist falsch: die vier buendigen Gassen bekommen einen Weg, die
 * zwei versetzten je einen 7-m-Stummel ohne Anschluss.
 *
 * Beide Polygone stammen aus den Bauzetteln 2026-09-09 14:08 und 14:10.
 */
internal static partial class Program
{
    private static readonly float2[] DiagonaleForm =
    {
        new float2(-1037.02001953125f, 118.68977355957031f),
        new float2(-1133.9781494140625f, 122.01594543457031f),
        new float2(-1184.1500244140625f, 55.89800262451172f),
        new float2(-1187.3790283203125f, -54.98500442504883f),
        new float2(-1042.7530517578125f, -65.14200592041016f),
    };

    private static readonly float2[] StufenForm =
    {
        new float2(-1037.02001953125f, 118.68977355957031f),
        new float2(-1133.9781494140625f, 122.01594543457031f),
        new float2(-1136.9993896484375f, 33.9863395690918f),
        new float2(-1184.7337646484375f, 35.85301208496094f),
        new float2(-1187.5379638671875f, -60.62631607055664f),
        new float2(-1042.7530517578125f, -65.14200592041016f),
    };

    private static LayoutSettings DiagonalRegler()
    {
        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;
        e.Entrances = new[]
        {
            new Entrance { Edge = 0, Along = 61.9, Art = Zufahrtsart.Zufahrt },
        };
        return e;
    }

    private static int RunDiagonalwege()
    {
        var fehler = 0;
        if (Environment.GetEnvironmentVariable("PLT_LIVE") == "1")
            ParkingGeometry.LiveSchreiber = z => Console.WriteLine("    LIVE " + z);
        /*
         * DIE DREI ABZUEGE VOM 2026-09-09 15:43/15:44.
         *
         * Der Nutzer hat dieselbe Form dreimal leicht verbreitert und drei
         * verschiedene Ergebnisse bekommen:
         *
         *   15:43:54  richtig - beide Streifen stossen bei quer 1096,1
         *   15:43:59  15 m Luecke - die Gehrung feuerte nicht
         *   15:44:15  zwei 7-m-Stummel - der schraege Streifen wurde
         *             abgelehnt ("passt nicht in die Kontur")
         *
         * Drei Fehlermodi an fast gleichen Formen. Alle drei gehoeren in
         * einen Weg.
         */
        var breit1 = new[]
        {
            new float2(-1037.0201416015625f, 118.68981170654297f),
            new float2(-1041.7760009765625f, -19.95400047302246f),
            new float2(-1146.008056640625f, -16.378000259399414f),
            new float2(-1143.0111083984375f, 70.96600341796875f),
            new float2(-1099.5789794921875f, 120.83588409423828f),
        };
        var breit2 = new[]
        {
            new float2(-1037.0201416015625f, 118.68981170654297f),
            new float2(-1041.7760009765625f, -19.95400047302246f),
            new float2(-1153.6671142578125f, -16.115001678466797f),
            new float2(-1150.6700439453125f, 71.22900390625f),
            new float2(-1099.5789794921875f, 120.83588409423828f),
        };
        var breit3 = new[]
        {
            new float2(-1037.0201416015625f, 118.68981170654297f),
            new float2(-1041.7760009765625f, -19.95400047302246f),
            new float2(-1160.1300048828125f, -15.894001007080078f),
            new float2(-1157.133056640625f, 71.45000457763672f),
            new float2(-1099.5789794921875f, 120.83588409423828f),
        };

        foreach (var fall in new[]
        {
            (Name: "Diagonale", Form: DiagonaleForm, Gruppen: 1),
            (Name: "Stufe", Form: StufenForm, Gruppen: 2),
            (Name: "Abzug 15:43:54", Form: breit1, Gruppen: 1),
            (Name: "Abzug 15:43:59", Form: breit2, Gruppen: 1),
            (Name: "Abzug 15:44:15", Form: breit3, Gruppen: 1),
        })
        {
            var regler = DiagonalRegler();
            if (fall.Name.StartsWith("Abzug"))
                regler.Entrances = Array.Empty<Entrance>();
            var layout = ParkingGeometry.Build(fall.Form, regler);
            var g0 = layout.AisleLine[0];
            var achse = math.normalize(g0[g0.Length - 1] - g0[0]);
            var norm = new float2(-achse.y, achse.x);
            double Quer(float2 p) => p.x * norm.x + p.y * norm.y;
            double Laengs(float2 p) => p.x * achse.x + p.y * achse.y;

            Console.WriteLine($"  {fall.Name}: {layout.Stalls} Buchten,"
                + $" {layout.Aisles} Gassen");
            var enden = layout.AisleLine
                .Select(g => (Quer: Quer(g[0]),
                              Bis: Math.Max(Laengs(g[0]), Laengs(g[g.Length - 1]))))
                .OrderBy(g => g.Quer).ToArray();
            Console.WriteLine("    Gassenenden laengs: "
                + string.Join(" / ", enden.Select(x => x.Bis.ToString("F1"))));

            /*
             * WIE VIELE ZUSAMMENHAENGENDE ENDWEGE GIBT ES?
             *
             * Gezaehlt wird auf der positiven Endseite. Zwei Wege haengen
             * zusammen, wenn ihre Rechtecke sich beruehren (1 mm Toleranz).
             * Ein gesetzter Zugang laeuft laengs und faellt heraus.
             */
            var wege = new List<(double Von, double Bis, double Laengs, float2 A, float2 B)>();
            foreach (var n in layout.EntranceLine ?? Array.Empty<float2[]>())
            {
                var A = n[0]; var B = n[n.Length - 1];
                var mitteL = (Laengs(A) + Laengs(B)) / 2;
                if (mitteL < enden.Min(x => x.Bis) - 40) continue;   // andere Seite
                if (Math.Abs(Quer(B) - Quer(A)) < 3.0) continue;     // gesetzter Zugang
                wege.Add((Math.Min(Quer(A), Quer(B)), Math.Max(Quer(A), Quer(B)),
                    mitteL, A, B));
            }
            foreach (var w in wege.OrderBy(w => w.Von))
                Console.WriteLine($"      Endweg quer {w.Von,8:F1} .. {w.Bis,8:F1}"
                    + $"  bei laengs {w.Laengs,7:F1}   Laenge "
                    + $"{math.distance(w.A, w.B),6:F1}");

            /*
             * BERUEHRUNG WIRD GEOMETRISCH GEPRUEFT, NICHT UEBER LAENGSWERTE.
             *
             * Der erste Anlauf verlangte gleiche Laengslage - damit koennte
             * eine SCHRAEGE Verbindung entlang der Kante nie als verbunden
             * gelten, und genau die ist das Ziel. Geprueft wird deshalb, ob
             * sich die 2-m-Rechtecke der Wege beruehren: Trennachsenverfahren
             * ueber beide Rechtecke, 1 mm Toleranz.
             */
            float2[] Rechteck(float2 a, float2 b)
            {
                var d = math.normalize(b - a);
                var n = new float2(-d.y, d.x) * 1.0f;      // halbe Fusswegbreite
                return new[] { a - n, b - n, b + n, a + n };
            }
            bool Beruehrt(float2[] u, float2[] v)
            {
                foreach (var form in new[] { u, v })
                    for (var i = 0; i < form.Length; i++)
                    {
                        var kante = form[(i + 1) % form.Length] - form[i];
                        var achse = math.normalize(new float2(-kante.y, kante.x));
                        double Min(float2[] q) => q.Min(x => (double)math.dot(x, achse));
                        double Max(float2[] q) => q.Max(x => (double)math.dot(x, achse));
                        if (Min(u) > Max(v) + 0.001 || Min(v) > Max(u) + 0.001)
                            return false;
                    }
                return true;
            }
            var kasten = wege.Select(w => Rechteck(w.A, w.B)).ToArray();
            var offen = new List<int>(Enumerable.Range(0, wege.Count));
            var gruppen = 0;
            while (offen.Count > 0)
            {
                gruppen++;
                var haufen = new List<int> { offen[0] };
                offen.RemoveAt(0);
                for (var i = 0; i < haufen.Count; i++)
                    foreach (var k in offen.ToArray())
                    {
                        if (!Beruehrt(kasten[haufen[i]], kasten[k])) continue;
                        offen.Remove(k); haufen.Add(k);
                    }
            }
            Console.WriteLine($"    zusammenhaengende Endwege: {gruppen},"
                + $" gefordert {fall.Gruppen}");

            /*
             * UND KEIN BAND DARF AN JEDER REIHE ZERSCHNITTEN WERDEN.
             *
             * Befund des Nutzers am 2026-09-09: *"An der Diagonale entsteht
             * wieder ein Split an Fahrtgassenfussweg und Randgruen."*
             * Gemessen am Bauzettel 15:11: das 1,0-m-Randgruen und der
             * 2,0-m-Fussweg zerfielen in Stuecke vom Gassenabstand (21,3 m).
             *
             * Ursache war die Bedingung fuer durchgehende Endstreifen in
             * `Layout.cs`: sie verlangte ACHSPARALLELE Fusswege. Der schraege
             * Streifen an der Diagonalen erfuellt das nicht - also fielen die
             * Baender wieder auseinander. Ein Symptom desselben Tages, eine
             * Zeile weiter oben.
             */
            var gassenabstand = DiagonalRegler().Ai + 2 * DiagonalRegler().Sl
                + DiagonalRegler().Md;
            double Kurz(float2[] r) => Enumerable.Range(0, r.Length)
                .Select(i => (double)math.distance(r[i], r[(i + 1) % r.Length])).Min();
            double Lang(float2[] r) => Enumerable.Range(0, r.Length)
                .Select(i => (double)math.distance(r[i], r[(i + 1) % r.Length])).Max();
            int Stuecke(float2[][] v, double breite) =>
                (v ?? Array.Empty<float2[]>()).Count(r =>
                    Math.Abs(Kurz(r) - breite) < 0.15
                    && Math.Abs(Lang(r) - gassenabstand) < 0.6);
            var randStuecke = Stuecke(layout.GrassSurface, DiagonalRegler().Es);
            var fussStuecke = Stuecke(layout.AsphaltSurface, 2.0);
            Console.WriteLine($"    Randgruen in {gassenabstand:F1}-m-Stuecken:"
                + $" {randStuecke} | Fussweg: {fussStuecke}");
            if (randStuecke > 2 || fussStuecke > 2)
            {
                fehler++;
                Console.WriteLine($"FEHLER: {fall.Name}: Randgruen in {randStuecke},"
                    + $" Fussweg in {fussStuecke} Reihenstuecken - die Baender"
                    + " werden an jeder Reihe durchgeschnitten");
            }
            if (gruppen != fall.Gruppen)
            {
                fehler++;
                Console.WriteLine($"FEHLER: {fall.Name}: {gruppen} getrennte"
                    + $" Endwege statt {fall.Gruppen}");
            }
        }

        /*
         * DER UEBERSTAND WIRD AM PLAN GEMESSEN, NICHT AM BELAG.
         *
         * Befund des Nutzers am 2026-09-09, mit Bild: der schraege Streifen
         * ragte ueber die Aussenkante der letzten Fahrgasse hinaus; er hat
         * die gewuenschte Kante blau eingezeichnet. Im BELAG ist das nicht
         * messbar - die Zellzerlegung schneidet den schraegen Streifen, er
         * taucht dort nicht als 2-m-Rechteck auf. Der Plan traegt dagegen die
         * echten Ecken, samt schraeger Stirn.
         *
         * Im Planrahmen liegen die Gassen waagerecht: quer ist schlicht Y.
         */
        {
            var form = new Formdefinition("Diagonale", DiagonaleForm
                .Select(v => new Punkt(v.x, v.y)).ToArray());
            var bau = Layoutbauer.Baue(form, new Zelleneinstellungen
            {
                Randstrassen = false,
                Querstrassenabstand = 33,
                Querstrassenkappen = true,
            }, new[] { new Zufahrtsvorgabe(0, 61.9) });
            var r = bau.Ringlos;
            if (r == null || r.Gassen.Count == 0)
            {
                Console.WriteLine("  (kein ringloser Plan - Ueberstand nicht geprueft)");
            }
            else
            {
                var halbe = r.Gassen.Max(g => g.Breite) / 2;
                var von = r.Gassen.Min(g => g.A.Y) - halbe;
                var bis = r.Gassen.Max(g => g.A.Y) + halbe;
                Console.WriteLine($"  Plan: {r.Gassen.Count} Gassen,"
                    + $" {r.Fusswege.Count} Endwege, Gassenband {von:F1}..{bis:F1}");
                foreach (var w in r.Fusswege)
                {
                    var ys = w.Ecken.Select(v => v.Y).ToArray();
                    var ueber = Math.Max(von - ys.Min(), ys.Max() - bis);
                    Console.WriteLine($"    Endweg y {ys.Min():F2}..{ys.Max():F2}"
                        + $"  Ueberstand {Math.Max(0, ueber):F2} m");
                    if (ueber <= 0.005) continue;
                    fehler++;
                    Console.WriteLine($"FEHLER: Endweg steht {ueber:F2} m ueber die"
                        + " aeusserste Fahrgasse hinaus - er soll buendig"
                        + " abschliessen");
                }
            }
        }

        Console.WriteLine($"Diagonalwege: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
