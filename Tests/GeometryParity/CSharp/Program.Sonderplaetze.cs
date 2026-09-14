using System;
using System.Globalization;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * MISST, WIE WEIT DIE SONDERPLAETZE VON IHREM ANKER LIEGEN.
 *
 * Der Nutzer meldete am 2026-08-27, Behinderten- und E-Plaetze landeten
 * "gefuehlt irgendwo random". Aus seinen sechs Abzuegen liess sich das
 * nachrechnen, aber nur von Hand. Dieser Lauf macht daraus eine Zahl, die
 * man vor und nach einer Aenderung vergleichen kann.
 *
 * Der Massstab steht daneben: der Abstand der ALLERNAECHSTEN Bucht. Weiter
 * als die kann ein Sonderplatz nicht heran, naeher soll er auch nicht sein.
 */
internal static partial class Program
{
    private static int RunSonderplaetze(string polygon, string kanten)
    {
        var punkte = polygon.Split(';')
            .Select(t => t.Split(','))
            .Select(t => new float2(
                float.Parse(t[0], CultureInfo.InvariantCulture),
                float.Parse(t[1], CultureInfo.InvariantCulture)))
            .ToArray();

        Console.WriteLine("SONDERPLAETZE: Abstand zum Anker, je Zufahrtslage");
        Console.WriteLine();

        var schlecht = 0;
        /**
         * BEIDE BETRIEBSARTEN, NICHT NUR DIE VOREINGESTELLTE.
         *
         * Der Lauf pruefte bisher stumm nur `Randstrassen = true`. Genau in
         * der anderen Betriebsart lagen die Sonderplaetze am 2026-09-08 dann
         * 38,8 bis 75,6 m vom gesetzten Fussweg weg, ohne dass hier etwas
         * rot wurde. Ein Schalter, den keine Pruefung umlegt, ist ungeprueft.
         */
        foreach (var randstrassen in new[] { true, false })
        foreach (var eintrag in kanten.Split(';'))
        {
            var teile = eintrag.Split(',');
            var kante = int.Parse(teile[0]);
            var entlang = double.Parse(teile[1], CultureInfo.InvariantCulture);
            var art = teile.Length > 2
                ? (Zufahrtsart)Enum.Parse(typeof(Zufahrtsart), teile[2], true)
                : Zufahrtsart.Fussweg;

            var einstellungen = LayoutSettings.Cs2;
            einstellungen.Randstrassen = randstrassen;
            einstellungen.AngleMode = "edge";
            einstellungen.Auto = false;
            einstellungen.Zellen = true;
            einstellungen.Entrances = new[]
            {
                new Entrance { Edge = kante, Along = entlang, Art = art },
            };

            var layout = ParkingGeometry.Build(punkte, einstellungen);
            if (layout.EntranceLine.Length == 0)
            {
                Console.WriteLine($"  Rand {(randstrassen ? "an " : "aus")}"
                    + $" Kante {kante} bei {entlang,6:F1} m: keine Zufahrt entstanden");
                schlecht++;
                continue;
            }

            /**
             * GEMESSEN WIRD GEGEN DEN GESETZTEN ZUGANG - sonst misst sich
             * die Pruefung selbst gruen.
             *
             * Bis zum 2026-09-08 galt als Anker das Ende JEDER Zufahrtslinie,
             * und der kleinste Abstand zaehlte. Ohne Randstrassen legt der
             * Plan aber zwei eigene Randfusswege quer ueber das Grundstueck;
             * damit lag jeder Block irgendeiner Linie nah, und der Lauf blieb
             * gruen, obwohl die Sonderplaetze 30 bis 62 m vom gesetzten
             * Fussweg weg lagen. Mit Mutation nachgewiesen.
             *
             * Der Punkt auf der Arealkante ist vom Layout unabhaengig: er ist
             * genau das, was der Nutzer angeklickt hat.
             */
            var kanteVon = punkte[kante];
            var kanteNach = punkte[(kante + 1) % punkte.Length];
            var kantenlaenge = math.distance(kanteVon, kanteNach);
            var anker = kantenlaenge < 1e-6
                ? kanteVon
                : kanteVon + (kanteNach - kanteVon)
                    * (float)(entlang / kantenlaenge);
            double Abstand(float2[] bucht)
            {
                var mitte = new float2(bucht.Average(p => p.x), bucht.Average(p => p.y));
                return math.distance(mitte, anker);
            }

            var naechste = layout.Bay.Length == 0
                ? double.NaN : layout.Bay.Min(Abstand);
            var behindert = layout.Bay
                .Where((_, i) => layout.BayRole[i] == BayRole.Disabled)
                .Select(Abstand).ToArray();
            var elektro = layout.Bay
                .Where((_, i) => layout.BayRole[i] == BayRole.Electric)
                .Select(Abstand).ToArray();

            /*
             * EINE BLOCKLAENGE SCHLUPF, NICHT MEHR.
             *
             * Ein Sonderplatzblock braucht mehrere zusammenhaengende Buchten
             * und faengt deshalb selten genau an der naechsten an - im
             * schlimmsten Fall erst eine ganze Blocklaenge weiter. Fuenf
             * Felder mal 3,0 m Buchtbreite sind 15 m; so viel darf er
             * hinterherhinken, mehr nicht.
             *
             * Vorher stand hier `naechste * 3 + 5`. Das war noetig, solange
             * der Anker das innere Ende der Zufahrtslinie war und damit
             * selbst wanderte. Gegen den gesetzten Zugang gemessen braucht
             * der reparierte Stand von diesem Schlupf keinen einzigen Meter:
             * der naechste Sonderplatz IST in allen acht Faellen die
             * naechste Bucht ueberhaupt. Die Schranke ist also nicht auf
             * Gruen getrimmt - sie hat 15 m Luft, die niemand braucht.
             */
            const double Blocklaenge = 5 * 3.0;
            var grenze = naechste + Blocklaenge;
            /*
             * GEMESSEN WIRD, WO DER BLOCK ANFAENGT - nicht, wo er aufhoert.
             *
             * Die erste Fassung nahm den AEUSSERSTEN Sonderplatz. Behinderten-
             * und E-Plaetze bilden aber eine zusammenhaengende Kette von 14
             * Plaetzen; die ist konstruktionsbedingt ueber 30 m lang. Ihr
             * fernes Ende kann nie am Zugang liegen, egal wie gut platziert
             * wird - die Schranke haette also selbst bei perfekter Loesung
             * immer angeschlagen.
             *
             * Das ist keine Lockerung, damit es gruen wird: die Frage lautet
             * "sitzt der Block am Zugang", und die beantwortet sein naechster
             * Platz. Der Nutzer hat ausdruecklich ausgeschlossen, die Bloecke
             * aufzuteilen - also ist ihre Laenge gegeben und darf nicht gegen
             * sie gewertet werden.
             */
            var naechsterSonder = new[]
                {
                    behindert.DefaultIfEmpty(double.PositiveInfinity).Min(),
                    elektro.DefaultIfEmpty(double.PositiveInfinity).Min(),
                }
                .Where(x => !double.IsPositiveInfinity(x))
                .DefaultIfEmpty(0)
                .Max();
            var urteil = naechsterSonder <= grenze ? "OK" : "ZU WEIT";
            if (urteil != "OK") schlecht++;

            Console.WriteLine($"  Rand {(randstrassen ? "an " : "aus")}"
                + $" Kante {kante} bei {entlang,6:F1} m, {art,-8}"
                + $" naechste Bucht {naechste,5:F1} m"
                + $" | Behindert {(behindert.Length == 0 ? 0 : behindert.Min()),5:F1}-{(behindert.Length == 0 ? 0 : behindert.Max()),5:F1}"
                + $" | Elektro {(elektro.Length == 0 ? 0 : elektro.Min()),5:F1}-{(elektro.Length == 0 ? 0 : elektro.Max()),5:F1}"
                + $" | Anzahl B/E {behindert.Length}/{elektro.Length}"
                + $" | Grenze {grenze,5:F1} | {urteil}");
            foreach (var zeile in ParkingGeometry.SonderplatzSpur)
                Console.WriteLine("      " + zeile);
        }

        Console.WriteLine();
        Console.WriteLine(schlecht == 0
            ? "ALLE LAGEN OK"
            : $"{schlecht} Lage(n) zu weit vom Anker");
        return schlecht == 0 ? 0 : 1;
    }
}
