using System;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * DIE ZONING-FLAECHE: ein Rechteck aus quadratischen Parzellen.
     *
     * Der Nutzer zieht im fertigen Umriss ein Rechteck. Was er zieht, sind
     * NUR DIE PARZELLEN - die Zoning-Strasse kommt aussen dazu, das Ergebnis
     * ist also groesser als der Zug. So hat er es am 2026-09-01 entschieden.
     *
     * WARUM DIE FLAECHE IN PARZELLEN GERECHNET WIRD und nicht in Metern:
     * CS2s Zonenraster kennt nur ganze Zellen zu 8 m (`ZoneUtils.CELL_SIZE`).
     * Ein Rechteck mit 23,4 m Kante gibt es dort nicht - es waere zwei
     * Parzellen breit und der Rest bliebe unbenutzt. Der Zug rastet deshalb
     * auf ganze Parzellen, und die Zahl steht im Panel. Der Nutzer wollte
     * genau das sehen: *"Im UI waere es gut, wenn wir X*Y Parzellen
     * anzeigen."*
     *
     * DIE GRENZEN kommen ebenfalls aus dem Spiel, nicht von uns:
     * `MAX_ZONE_WIDTH 10` und `MAX_ZONE_DEPTH 6`. Sechs ist die harte
     * Tiefengrenze ab der Strasse - dahinter waechst nichts mehr.
     *
     * Der Winkel ist frei: er kommt aus denselben vier Modi wie der
     * Reihenwinkel des Parkplatzes (Kante, Quer, Fest, ausgerichtete Linie)
     * und wird an genau einer Stelle abgeleitet, siehe `Reihenwinkel`.
     */
    public static partial class ParkingGeometry
    {
        /** Kantenlaenge einer Parzelle. CS2: `ZoneUtils.CELL_SIZE`. */
        public const double Zoningparzelle = 8.0;

        /**
         * WIE GROSS DER NUTZER ZIEHEN DARF - 25 x 25 Parzellen.
         *
         * Das sind NICHT die CS2-Grenzen. `ZoneUtils.MAX_ZONE_WIDTH` ist 10
         * und `MAX_ZONE_DEPTH` ist 6; ich hatte sie als Zuggrenze
         * uebernommen, und der Nutzer hat das am 2026-09-03 richtiggestellt:
         * *"Kannst du das Maximum von Zoning erhoehen auf 25x25 Tiles?"*
         *
         * Die beiden Werte bedeuten auch etwas anderes: sie gelten fuer
         * EINEN Zonenblock an EINER Strasse, nicht fuer das Gebiet, das der
         * Nutzer zieht. Unser Ring liefert vier Strassen, also waechst von
         * jeder Seite bis zu 6 Parzellen tief.
         *
         * FOLGE, DIE MAN KENNEN MUSS: Bei mehr als 12 Parzellen Tiefe
         * bleibt in der Mitte ein Streifen, den keine Strasse mehr erreicht
         * - dort wachsen keine Haeuser. Wer so gross zieht, braucht innere
         * Strassen; die baut der Mod bisher nicht.
         */
        /**
         * DER NUTZER DARF DIE GRENZE SELBST HEBEN.
         *
         * Ansage vom 2026-09-04: *"Bei der Max-Breite und -Hoehe kannst du da
         * bitte noch in den Settings was einbauen, damit der User selbst von
         * 25x25 erhoehen kann."*
         *
         * ABGELEITET, NICHT GESPIEGELT. Die Zahl steht in den Optionen; hier
         * haengt nur eine Quelle, die sie bei jedem Zugriff frisch holt. Ein
         * mitgefuehrtes Feld muesste bei jeder Aenderung nachgezogen werden,
         * und genau daran ist in diesem Projekt schon einmal die Anzeige
         * haengengeblieben.
         *
         * Der Geometriekern kennt die Optionen nicht - er darf es auch nicht,
         * sonst laufen die Prueflaeufe ohne CS2 nicht mehr. Ohne gesetzte
         * Quelle gilt deshalb weiter 25, und die Tests messen unveraendert.
         */
        public const int ZoningMaxStandard = 25;

        /**
         * Die harte Obergrenze der Regler.
         *
         * 100 Parzellen sind 800 m Kantenlaenge - mehr als jeder Parkplatz,
         * den der Nutzer bisher gezogen hat, und weit jenseits dessen, was
         * CS2 an einem Zonenblock noch bebaut. Sie ist da, damit ein
         * verrutschter Regler nicht Millionen Zellen rastert.
         */
        public const int ZoningMaxGrenze = 100;

        internal static Func<int> ZoningMaxBreiteQuelle;
        internal static Func<int> ZoningMaxTiefeQuelle;

        public static int ZoningMaxBreite => AusQuelle(ZoningMaxBreiteQuelle);
        public static int ZoningMaxTiefe => AusQuelle(ZoningMaxTiefeQuelle);

        private static int AusQuelle(Func<int> quelle)
        {
            if (quelle == null) return ZoningMaxStandard;
            try { return math.clamp(quelle(), 1, ZoningMaxGrenze); }
            catch { return ZoningMaxStandard; }
        }

        /**
         * Wieviel Platz die Zoning-Strasse braucht - VORLAEUFIG.
         *
         * Die echte Breite kommt vom gewaehlten Strassenprefab, und das
         * steht erst fest, wenn der Sondenlauf sagt, welche Strassen
         * ueberhaupt zoningfaehig sind. Bis dahin steht die Zahl HIER, an
         * genau einer Stelle, damit sie sich spaeter mit einer Zeile
         * berichtigen laesst statt an fuenf.
         *
         * 8 m ist die Breite einer schmalen Vanilla-Strasse einschliesslich
         * ihrer Gehwege - eher zu grosszuegig als zu knapp, denn zu wenig
         * Platz hiesse Parzellen auf unseren Fahrwegen.
         */
        public const double ZoningStrassenbreite = 8.0;

        /** Wo Bauland entstehen soll. */
        public enum Zoningseite
        {
            /** Nur im gezogenen Rechteck. Vorgabe. */
            Innen,
            /** Nur rings um die Strasse herum. */
            Aussen,
            /** Beides - dann traegt dieselbe Strasse zwei Reihen. */
            Beides,
        }

        /**
         * Eine gesetzte Zoning-Flaeche.
         *
         * GEMERKT WIRD DIE ECKE, NICHT DIE MITTE. Die Ecke ist der Punkt, an
         * dem der Zug begonnen hat; von ihr aus laeuft das Raster in zwei
         * festen Richtungen. Rechnete man von der Mitte, muesste bei jeder
         * ungeraden Parzellenzahl eine halbe Zelle dazugerechnet werden - und
         * genau solche halben Zellen gibt es in CS2 nicht.
         */
        public sealed class Zoningflaeche
        {
            /** Die Ecke, an der das Raster beginnt, in Weltkoordinaten (XZ). */
            public float2 Ecke { get; set; }

            /** Parzellen entlang der Winkelrichtung. */
            public int Spalten { get; set; }

            /** Parzellen quer dazu. */
            public int Reihen { get; set; }

            /** Grad, wie der Reihenwinkel des Parkplatzes gemessen. */
            public double Winkel { get; set; }

            /**
             * Wieviel Platz um die Parzellen herum freibleiben muss.
             *
             * Immer mindestens die Strassenbreite - die Zoning-Strasse
             * laeuft aussen um das Rechteck herum, und der Nutzer hat
             * entschieden, dass das gezogene Rechteck NUR die Parzellen
             * sind. Kommt aussen noch Bauland dazu, waechst der Rand um
             * dessen Tiefe.
             *
             * Der Wert steht an der Flaeche und nicht in den Einstellungen,
             * weil die Geometrie ihn braucht - und weil eine gespeicherte
             * Flaeche sonst nicht wuesste, wieviel Platz sie belegt hat.
             */
            public double Rand { get; set; } = ZoningStrassenbreite;

            public int Parzellen => Spalten * Reihen;

            public double Flaeche =>
                Spalten * Reihen * Zoningparzelle * Zoningparzelle;

            public Zoningflaeche Clone() => new Zoningflaeche
            {
                Ecke = Ecke,
                Spalten = Spalten,
                Reihen = Reihen,
                Winkel = Winkel,
                Rand = Rand,
            };
        }

        /** Die beiden Rasterrichtungen dieser Flaeche, als Einheitsvektoren. */
        public static (float2 Laengs, float2 Quer) ZoningRichtungen(double winkel)
        {
            var bogen = winkel * Math.PI / 180.0;
            var laengs = new float2(
                (float)Math.Cos(bogen), (float)Math.Sin(bogen));
            return (laengs, new float2(-laengs.y, laengs.x));
        }

        /**
         * Die vier Ecken des FREIGEHALTENEN Bereichs - Parzellen plus Rand.
         *
         * Das ist die Flaeche, der der Parkplatz ausweichen muss. Gezeichnet
         * und gezogen wird das innere Rechteck; freigehalten wird mehr.
         */
        public static float2[] ZoningEckenMitRand(Zoningflaeche flaeche)
            => ZoningEckenMitAufschlag(flaeche, flaeche.Rand);

        /**
         * Die vier Ecken eines um `aufschlag` vergroesserten Rechtecks.
         *
         * Mit 0 ist es das gezogene Rechteck, mit `Rand` der freigehaltene
         * Bereich. Dazwischen liegen die Grenzen, die das Overlay braucht:
         * die aeussere Fahrbahnkante der Zoning-Strasse und der Anfang des
         * aeusseren Baulands.
         */
        public static float2[] ZoningEckenMitAufschlag(
            Zoningflaeche flaeche, double aufschlag)
        {
            if (aufschlag <= 0) return ZoningEcken(flaeche);
            var (laengs, quer) = ZoningRichtungen(flaeche.Winkel);
            var r = (float)aufschlag;
            var ecke = flaeche.Ecke - laengs * r - quer * r;
            var breite = laengs * (float)(flaeche.Spalten * Zoningparzelle + 2 * r);
            var tiefe = quer * (float)(flaeche.Reihen * Zoningparzelle + 2 * r);
            return new[] { ecke, ecke + breite, ecke + breite + tiefe, ecke + tiefe };
        }

        /**
         * Die vier Eckpunkte im Uhrzeigersinn, beginnend bei `Ecke`.
         *
         * Reihenfolge und Startpunkt sind festgelegt, weil das Overlay, die
         * Trefferpruefung und spaeter der Zonenblock dieselbe Ecke als
         * Nullpunkt brauchen. Wer hier umsortiert, dreht die Parzellen.
         */
        public static float2[] ZoningEcken(Zoningflaeche flaeche)
        {
            var (laengs, quer) = ZoningRichtungen(flaeche.Winkel);
            var breite = laengs * (float)(flaeche.Spalten * Zoningparzelle);
            var tiefe = quer * (float)(flaeche.Reihen * Zoningparzelle);
            return new[]
            {
                flaeche.Ecke,
                flaeche.Ecke + breite,
                flaeche.Ecke + breite + tiefe,
                flaeche.Ecke + tiefe,
            };
        }

        /**
         * Die inneren Rasterlinien als Punktpaare - nur zum Zeichnen.
         *
         * Der Rand ist nicht dabei; den zeichnet das Overlay ohnehin als
         * Umriss, und doppelte Linien sehen dicker aus als beabsichtigt.
         */
        public static (float2 A, float2 B)[] ZoningRasterlinien(
            Zoningflaeche flaeche)
        {
            var (laengs, quer) = ZoningRichtungen(flaeche.Winkel);
            var breite = laengs * (float)(flaeche.Spalten * Zoningparzelle);
            var tiefe = quer * (float)(flaeche.Reihen * Zoningparzelle);
            var linien = new (float2, float2)[
                Math.Max(0, flaeche.Spalten - 1) + Math.Max(0, flaeche.Reihen - 1)];
            var n = 0;
            for (var i = 1; i < flaeche.Spalten; i++)
            {
                var versatz = laengs * (float)(i * Zoningparzelle);
                linien[n++] = (flaeche.Ecke + versatz,
                    flaeche.Ecke + versatz + tiefe);
            }
            for (var k = 1; k < flaeche.Reihen; k++)
            {
                var versatz = quer * (float)(k * Zoningparzelle);
                linien[n++] = (flaeche.Ecke + versatz,
                    flaeche.Ecke + versatz + breite);
            }
            return linien;
        }

        /**
         * Liegt der Punkt auf der Flaeche? Trefferpruefung fuer Hover,
         * Verschieben und Rechtsklick.
         *
         * Gerechnet wird im Rahmen der Flaeche selbst, nicht ueber ein
         * Polygon: zwei Skalarprodukte statt einer Kantenschleife, und keine
         * Frage nach dem Umlaufsinn.
         */
        public static bool ZoningEnthaelt(
            Zoningflaeche flaeche, float2 punkt, double rand = 0)
        {
            var (laengs, quer) = ZoningRichtungen(flaeche.Winkel);
            var d = punkt - flaeche.Ecke;
            var u = math.dot(d, laengs);
            var v = math.dot(d, quer);
            return u >= -rand
                && u <= flaeche.Spalten * Zoningparzelle + rand
                && v >= -rand
                && v <= flaeche.Reihen * Zoningparzelle + rand;
        }

        /**
         * Baut die Flaeche aus einem laufenden Zug.
         *
         * `start` ist der Punkt, an dem gedrueckt wurde, `jetzt` der Zeiger.
         * Beides wird in den Rahmen des Winkels gelegt, auf ganze Parzellen
         * AUFgerundet und auf 10 x 6 begrenzt.
         *
         * AUFGERUNDET, nicht abgerundet: wer einen halben Meter zieht, will
         * eine Parzelle sehen und nicht gar nichts. Sonst bleibt der erste
         * Meter des Zuges wirkungslos, und das fuehlt sich kaputt an.
         *
         * Die Ecke wandert dabei mit: zieht man nach links, ist die linke
         * Seite die Ecke. So bleibt das Rechteck unter dem Zeiger, statt vom
         * Startpunkt aus in die falsche Richtung zu wachsen.
         */
        public static Zoningflaeche ZoningAusZug(
            float2 start, float2 jetzt, double winkel)
            => ZoningAusZug(start, jetzt, winkel, out _, out _);

        /**
         * Wie oben, sagt aber DAZU, ob die Grenze gegriffen hat.
         *
         * Ohne diese Auskunft koennte das Panel nur "10 x 6" anzeigen und der
         * Nutzer ruesse weiter, ohne zu verstehen, warum nichts mehr waechst.
         * Die Bedienung sagt ausdruecklich: *"Ueber 10 x 6 hoert es auf zu
         * wachsen UND SAGT WARUM."*
         */
        public static Zoningflaeche ZoningAusZug(
            float2 start, float2 jetzt, double winkel,
            out bool breiteGekappt, out bool tiefeGekappt)
        {
            var (laengs, quer) = ZoningRichtungen(winkel);
            var d = jetzt - start;
            var u = math.dot(d, laengs);
            var v = math.dot(d, quer);

            var rohSpalten = (int)Math.Ceiling(Math.Abs(u) / Zoningparzelle);
            var rohReihen = (int)Math.Ceiling(Math.Abs(v) / Zoningparzelle);
            var spalten = math.clamp(rohSpalten, 1, ZoningMaxBreite);
            var reihen = math.clamp(rohReihen, 1, ZoningMaxTiefe);
            breiteGekappt = rohSpalten > ZoningMaxBreite;
            tiefeGekappt = rohReihen > ZoningMaxTiefe;

            var ecke = start;
            if (u < 0) ecke -= laengs * (float)(spalten * Zoningparzelle);
            if (v < 0) ecke -= quer * (float)(reihen * Zoningparzelle);

            return new Zoningflaeche
            {
                Ecke = ecke,
                Spalten = spalten,
                Reihen = reihen,
                Winkel = winkel,
            };
        }

        /** Der Mittelpunkt der Flaeche. */
        public static float2 ZoningMitte(Zoningflaeche flaeche)
        {
            var (laengs, quer) = ZoningRichtungen(flaeche.Winkel);
            return flaeche.Ecke
                + laengs * (float)(flaeche.Spalten * Zoningparzelle * 0.5)
                + quer * (float)(flaeche.Reihen * Zoningparzelle * 0.5);
        }

        /**
         * Dreht die Flaeche AN IHREM PLATZ auf einen neuen Winkel.
         *
         * Gedreht wird um die MITTE, nicht um die Ecke. Die Ecke ist zwar
         * der gespeicherte Nullpunkt des Rasters, aber sie liegt am Rand -
         * eine Drehung um sie schwenkt die ganze Flaeche weg, und der Nutzer
         * suchte sie dann auf der Karte. Um die Mitte gedreht bleibt sie, wo
         * sie ist, und tut genau das, was der Regler verspricht.
         */
        public static Zoningflaeche ZoningGedreht(
            Zoningflaeche flaeche, double neuerWinkel)
        {
            var mitte = ZoningMitte(flaeche);
            var (laengs, quer) = ZoningRichtungen(neuerWinkel);
            return new Zoningflaeche
            {
                Ecke = mitte
                    - laengs * (float)(flaeche.Spalten * Zoningparzelle * 0.5)
                    - quer * (float)(flaeche.Reihen * Zoningparzelle * 0.5),
                Spalten = flaeche.Spalten,
                Reihen = flaeche.Reihen,
                Winkel = neuerWinkel,
                // Auch hier: ohne den Rand faellt das aeussere Bauland beim
                // Drehen weg. Derselbe Fehler wie beim Verschieben.
                Rand = flaeche.Rand,
            };
        }

        /** Verschiebt eine Flaeche um einen Weltversatz. */
        public static Zoningflaeche ZoningVerschoben(
            Zoningflaeche flaeche, float2 versatz) => new Zoningflaeche
            {
                Ecke = flaeche.Ecke + versatz,
                Spalten = flaeche.Spalten,
                Reihen = flaeche.Reihen,
                Winkel = flaeche.Winkel,
                // DER RAND MUSS MIT. Er fehlte hier bis zum 2026-09-03, und
                // damit fiel er beim ersten Verschieben auf den Standardwert
                // zurueck: das aeussere Bauland verschwand, nicht nur aus der
                // Vorschau, sondern aus der Flaeche. Der Nutzer sah es als
                // *"und zudem verschwindet der auch, wenn ich die ZF
                // bewege"*.
                Rand = flaeche.Rand,
            };
    }
}
