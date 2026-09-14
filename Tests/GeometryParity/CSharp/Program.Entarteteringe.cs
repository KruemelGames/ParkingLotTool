using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * GEHT EIN ENTARTETER RING AN CS2?
 *
 * Befund des Nutzers am 2026-09-10: *"Wieder Crash, ich kann ihn anscheinend
 * nachstellen. Denn es liegt an RZ und Randstrasse aus."*
 *
 * Zwei native Abstuerze, beide mit demselben Muster: das Modprotokoll endet
 * bei *"PLT-Flaechenvorschau erzeugt: 645 Vorschau-Objekte"*, dann
 * *"UpdateFrame added to unsupported type"* und ein Absturz ohne managed
 * Stacktrace. Der Absturz kommt also in der VORSCHAU, nicht beim Bauen.
 *
 * Direkt davor stehen im Protokoll Ringe mit **0,000 m Kantenlaenge und
 * 0,00 m2 Flaeche**. Beim Ziehen EINES Punktes kippt es:
 *
 *     Stand 21, 02:13:12   zweiter Punkt -1162,658   0 entartete Ringe
 *     Stand 22, 02:13:13   zweiter Punkt -1165,580   4 entartete Ringe
 *     Stand 26, 02:13:21   zweiter Punkt -1167,300   3 entartete Ringe -> Absturz
 *
 * EIN ENTARTETER RING IST IMMER FALSCH. Die vorhandene Meldung sagt "CS2 wird
 * ihn voraussichtlich VERWERFEN, an ihrer Stelle bleibt eine Luecke" - das
 * stimmt fuer einen ZU KLEINEN Ring. Ein Ring ohne Flaeche ist kein kleiner
 * Ring, sondern gar keiner. Er hat nichts zu verwerfen und nichts zu
 * triangulieren.
 */
internal static partial class Program
{
    private static int RunEntarteteringe()
    {
        var fehler = 0;
        var nurMessen = Environment.GetEnvironmentVariable("PLT_NADEL") == "1";

        // Beide Umrisse aus dem Protokoll 2026-09-10 02:13, unveraendert.
        var sauber = new[]
        {
            new float2(-1037.020f, 118.6898f),
            new float2(-1162.658f, 123.001f),
            new float2(-1168.936f, -59.893f),
            new float2(-1043.294f, -64.20625f),
        };
        var absturz = new[]
        {
            new float2(-1037.020f, 118.6898f),
            new float2(-1167.300f, 123.160f),
            new float2(-1173.576f, -59.734f),
            new float2(-1043.294f, -64.20625f),
        };

        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.AutomaticEntrances = false;
        e.Entrances = new[] { new Entrance { Edge = 0, Along = 60 } };

        /*
         * DIE FORM AUS DEM BAUZETTEL 2026-09-10 17:50.
         *
         * Nach dem Umbau auf die Treppe. Dort steht ein Grasring mit
         * 0,1133 m2 und 4,2 cm kuerzester Kante in der WELT - CS2 hat ihn
         * genommen (1 Dreieck, Complete). Beim Weggbaggern kam der Absturz.
         *
         * Der bisherige Massstab findet ihn nicht: er fragt nach 0,01 m2
         * bzw. 0,001 m, und eine Nadel von 5,4 m Laenge und 4,2 cm Breite
         * liegt darueber.
         */
        var treppe = new[]
        {
            new float2(-1106.602f, 121.077f),
            new float2(-1037.020f, 118.690f),
            new float2(-1042.435f, -39.150f),
            new float2(-1172.884f, -34.676f),
            new float2(-1169.442f, 65.643f),
        };

        /*
         * DER MASSSTAB: Flaeche und kuerzeste Kante jedes Rings.
         *
         * Gemessen wird beides, weil beides einzeln entarten kann - ein Ring
         * mit Flaeche kann eine Nullkante haben (zwei gleiche Punkte), und
         * ein Ring ohne Flaeche kann lauter lange Kanten haben (er faltet
         * sich auf sich selbst).
         */
        (double Flaeche, double Kante, int Ringe) Messe(float2[][] ringe)
        {
            var kleinste = double.PositiveInfinity;
            var kuerzeste = double.PositiveInfinity;
            var zahl = 0;
            foreach (var r in ringe ?? Array.Empty<float2[]>())
            {
                if (r == null || r.Length < 3) { zahl++; kleinste = 0; kuerzeste = 0; continue; }
                zahl++;
                var flaeche = Math.Abs(Enumerable.Range(0, r.Length).Sum(i =>
                    (double)r[i].x * r[(i + 1) % r.Length].y
                    - (double)r[(i + 1) % r.Length].x * r[i].y) / 2);
                kleinste = Math.Min(kleinste, flaeche);
                for (var i = 0; i < r.Length; i++)
                    kuerzeste = Math.Min(kuerzeste,
                        math.distance(r[i], r[(i + 1) % r.Length]));
            }
            return (kleinste, kuerzeste, zahl);
        }

        /*
         * WIE DUENN IST DER DUENNSTE RING?
         *
         * `2 * Flaeche / Umfang` - der Radius des groessten Kreises, der
         * hineinpasst. Die kuerzeste KANTE taugt dafuer nicht: ein grosser
         * Ring darf eine 4-cm-Kante haben, das ist eine abgeschraegte Ecke.
         * Genau daran ging der Massstab am 2026-09-10 vorbei - er meldete
         * gruen, waehrend eine Nadel von 0,1132 m2 und 4,2 cm Breite in der
         * Welt stand.
         */
        double Duenne(float2[][] ringe)
        {
            var duennste = double.PositiveInfinity;
            foreach (var r in ringe ?? Array.Empty<float2[]>())
            {
                if (r == null || r.Length < 3) return 0;
                var flaeche = Math.Abs(Enumerable.Range(0, r.Length).Sum(i =>
                    (double)r[i].x * r[(i + 1) % r.Length].y
                    - (double)r[(i + 1) % r.Length].x * r[i].y) / 2);
                var umfang = Enumerable.Range(0, r.Length).Sum(i =>
                    (double)math.distance(r[i], r[(i + 1) % r.Length]));
                if (umfang < 1e-9) return 0;
                duennste = Math.Min(duennste, 2 * flaeche / umfang);
            }
            return duennste;
        }

        void Fall(string name, float2[] form, int rzKante)
        {
            var s = e;
            s.Randzoning = rzKante < 0 ? Array.Empty<ParkingGeometry.RandzoningLinie>()
                : new[]
                {
                    new ParkingGeometry.RandzoningLinie
                    {
                        A = form[rzKante],
                        B = form[(rzKante + 1) % form.Length],
                    },
                };

            ParkingLayout l;
            try { l = ParkingGeometry.Build(form, s); }
            catch (Exception ex)
            {
                fehler++;
                Console.WriteLine($"FEHLER: {name}, RZ-Kante {rzKante}: "
                    + "Bau bricht ab - " + ex.Message);
                return;
            }

            var gras = Messe(l.GrassSurface);
            var belag = Messe(l.AsphaltSurface);
            var duennste = Math.Min(Duenne(l.GrassSurface), Duenne(l.AsphaltSurface));
            var entartet = gras.Flaeche < 0.01 || gras.Kante < 0.001
                || belag.Flaeche < 0.01 || belag.Kante < 0.001
                || duennste < 0.05;

            Console.WriteLine($"  {name,-10} RZ-Kante {rzKante,2}   "
                + $"Gras {gras.Ringe,3} Ringe (kleinste {gras.Flaeche,8:F3} m², "
                + $"kürzeste {gras.Kante,6:F3} m)   "
                + $"Belag {belag.Ringe,3} (kleinste {belag.Flaeche,8:F3} m², "
                + $"kürzeste {belag.Kante,6:F3} m)"
                + $"   dünnster {duennste,6:F3} m"
                + (entartet ? "   ENTARTET" : ""));

            if (!entartet) return;
            fehler++;
            Console.WriteLine($"FEHLER: {name}, RZ-Kante {rzKante}: ein Ring "
                + "ohne Fläche, mit Nullkante oder dünner als 5 cm geht an "
                + $"CS2 (dünnster {duennste:F4} m) - ein solcher Ring ist "
                + "nicht zu klein, sondern gar keiner");
        }

        /*
         * ALLE VIER KANTEN, weil das Protokoll nicht sagt, an welcher das
         * Randzoning lag. Dazu der Fall ganz ohne - er trennt "liegt am
         * Randzoning" von "liegt an der Form".
         */
        Console.WriteLine("  Umriss ohne Randzoning:");
        Fall("sauber", sauber, -1);
        Fall("Absturz", absturz, -1);

        // Woher kommt die Nadel? Jede Kante einzeln, dann beide.
        e.AngleMode = "edge";
        e.Entrances = new[] { new Entrance { Edge = 1, Along = 60 } };
        Fall("Treppe", treppe, -1);
        for (var k = 0; k < treppe.Length; k++) Fall("Treppe", treppe, k);

        /*
         * DER ECHTE FALL DES NUTZERS: ZWEI RZ-KANTEN, WIE ER SIE GESETZT HAT.
         *
         * `Fall` kann nur eine Kante; hier sind es die beiden aus dem Zettel.
         */
        {
            var s2 = e;
            s2.AngleMode = "edge";
            s2.Entrances = new[] { new Entrance { Edge = 1, Along = 60 } };
            s2.Randzoning = new[]
            {
                new ParkingGeometry.RandzoningLinie { A = treppe[3], B = treppe[4] },
                new ParkingGeometry.RandzoningLinie { A = treppe[4], B = treppe[0] },
            };
            var l2 = ParkingGeometry.Build(treppe, s2);
            foreach (var w in l2.Warnings ?? Array.Empty<string>())
                Console.WriteLine("    Warnung: " + w);
            var gras2 = Messe(l2.GrassSurface);
            var belag2 = Messe(l2.AsphaltSurface);
            var duenn2 = Math.Min(Duenne(l2.GrassSurface), Duenne(l2.AsphaltSurface));
            if (duenn2 < 0.05)
            {
                fehler++;
                Console.WriteLine($"FEHLER: Treppe 17:50: ein Ring von "
                    + $"{duenn2:F4} m Dicke geht an CS2 - Haarriss");
            }
            Console.WriteLine($"  Treppe 17:50   Gras {gras2.Ringe} Ringe "
                + $"(kleinste {gras2.Flaeche:F4} m², kürzeste {gras2.Kante:F4} m)"
                + $"   Belag {belag2.Ringe} (kleinste {belag2.Flaeche:F4} m², "
                + $"kürzeste {belag2.Kante:F4} m)");
            foreach (var r in (l2.GrassSurface ?? Array.Empty<float2[]>())
                         .Concat(l2.AsphaltSurface ?? Array.Empty<float2[]>()))
            {
                var f = Math.Abs(Enumerable.Range(0, r.Length).Sum(i =>
                    (double)r[i].x * r[(i + 1) % r.Length].y
                    - (double)r[(i + 1) % r.Length].x * r[i].y) / 2);
                if (f >= 1.0) continue;
                var kurz = Enumerable.Range(0, r.Length)
                    .Min(i => (double)math.distance(r[i], r[(i + 1) % r.Length]));
                Console.WriteLine($"    NADEL {f:F4} m², kürzeste Kante "
                    + $"{kurz:F4} m, {r.Length} Ecken: "
                    + string.Join("  ", r.Select(q => $"({q.x:F3}/{q.y:F3})")));

                /*
                 * WER LIEGT NEBENAN? Die Nadel entsteht zwischen zwei
                 * Rasterlinien. Der Ring, der die andere Seite der duennen
                 * Kante besetzt, verraet, WELCHE Grenze das ist.
                 */
                foreach (var (liste, wie) in new[]
                {
                    (l2.GrassSurface, "Gras"), (l2.AsphaltSurface, "Belag"),
                })
                foreach (var n in liste ?? Array.Empty<float2[]>())
                {
                    if (ReferenceEquals(n, r)) continue;
                    var treffer = n.Count(q => r.Any(w => math.distance(q, w) < 0.02f));
                    if (treffer < 2) continue;
                    var nf = Math.Abs(Enumerable.Range(0, n.Length).Sum(i =>
                        (double)n[i].x * n[(i + 1) % n.Length].y
                        - (double)n[(i + 1) % n.Length].x * n[i].y) / 2);
                    Console.WriteLine($"      Nachbar {wie} {nf:F3} m², "
                        + $"{n.Length} Ecken, {treffer} gemeinsame Punkte: "
                        + string.Join("  ", n.Select(q => $"({q.x:F3}/{q.y:F3})")));
                }
            }
        }
        Console.WriteLine("  Umriss mit Randzoning je Kante:");
        for (var k = 0; k < 4; k++)
        {
            Fall("sauber", sauber, k);
            Fall("Absturz", absturz, k);
        }

        Console.WriteLine($"Entarteteringe: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
