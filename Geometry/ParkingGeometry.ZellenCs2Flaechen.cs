using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * Plant die CS2-Flaechen bereits beim Uebergang aus dem Zellatlas.
         *
         * Der Zellgraph kennt die Materialgrenze exakt. Ein zusammenhaengender
         * Ring muss aber nicht zugleich eine von CS2 triangulierbare Flaeche
         * sein: CS2 versetzt seine Knoten vor dem Ear-Clipping um 0,1 m nach
         * innen. Schneidet sich erst dieser versetzte Ring selbst, wird die
         * ganze Flaeche verworfen.
         *
         * Die Sehne verbindet zwei vorhandene Randknoten. Dadurch wandert
         * keine Materialgrenze und es entsteht weder Ueberdeckung noch Loch;
         * nur die Zahl der an CS2 uebergebenen Flaechen aendert sich.
         */
        /**
         * `entwirren` schaltet das Zerlegen selbstberuehrender Ringe zu.
         *
         * NUR FUER DIE ZONING-RINGE, und zwar gemessen: mit dem Zerlegen auf
         * Gras und Asphalt meldete der Paritaetslauf am 2026-09-04 an
         * "Referenz 08s" drei NEUE Abweichungen - 0,08 m2 Asphalt auf Gras,
         * 0,08 m2 Asphalt auf Asphalt und eine von CS2 verworfene Flaeche.
         * Dort sitzen die Ringe seit Monaten anders zusammen, und ihre
         * Beruehrpunkte sind keine Haarkanten, sondern echte Engstellen.
         *
         * Der gemessene Verlust liegt bei den Zoning-Strassen; dort greift es
         * ohne Nebenwirkung. Den allgemeinen Belag laesst es in Ruhe, bis
         * derselbe Befund auch dort mit Zahlen belegt ist.
         */
        private static float2[][] PlaneZellenCs2Flaechen(
            float2[][] ringe, bool entwirren = false,
            List<float2[]> unbaubar = null, bool unbaubareWeglassen = false,
            List<float2[]> haarrisse = null)
        {
            var ausgabe = new List<float2[]>();
            foreach (var roh in ringe ?? System.Array.Empty<float2[]>())
            foreach (var floatRing in entwirren
                ? ZerlegeSelbstberuehrung(roh)
                : new[] { roh })
            {
                if (floatRing == null || floatRing.Length < 3) continue;
                var ring = floatRing
                    .Select(punkt => new double2(punkt.x, punkt.y))
                    .ToArray();
                /*
                 * EIN HAARRISS GEHT NICHT AN CS2.
                 *
                 * CS2 NIMMT so einen Ring an - `Cs2Triangulierung` liefert
                 * brav ein Dreieck, und im Spiel stand er auch wirklich da.
                 * Genau das ist das Problem: gebaut wird ein Gebilde, das
                 * niemand sehen kann und das beim Abraeumen mitmuss.
                 *
                 * GEMESSEN am Bauzettel des Nutzers vom 2026-09-10 17:50: ein
                 * Grasring mit 3 Ecken, 0,1133 m2, 5,4 m lang und an der
                 * schmalsten Stelle 4,2 cm breit - als Entity in der Welt,
                 * 1 Dreieck, `Complete`. Beim Weggbaggern kam der Absturz.
                 * Dieselbe Sorte lag auch bei den Abstuerzen um 02:13 und
                 * 16:38 dabei (0,00 m2 bzw. 0,2356 m2 / 8,7 cm). Das ist ein
                 * Muster ueber drei Abstuerze, kein Beleg fuer die Ursache -
                 * aber ein Ring von vier Zentimetern Breite ist auch ohne
                 * Absturz falsch.
                 *
                 * DAS MASS IST DIE BREITE, NICHT DIE FLAECHE UND NICHT DIE
                 * KUERZESTE KANTE. Ein grosser Ring darf eine 4-cm-Kante
                 * haben - das ist eine abgeschraegte Ecke, kein Haarriss.
                 * `2 * Flaeche / Umfang` ist der Radius des groessten Kreises,
                 * der hineinpasst, und trennt beides sauber:
                 *
                 *     Nadel 0,1132 m2, Umfang 10,84 m   ->  0,021 m
                 *     Streifen 0,5 x 40 m               ->  0,49 m
                 *     Buchtfeld 2 x 3,5 m               ->  1,27 m
                 *
                 * Die Schranke von 5 cm liegt eine Zehnerpotenz unter dem
                 * schmalsten Streifen, den der Bau absichtlich erzeugt.
                 *
                 * NUR OHNE RANDSTRASSE, wie schon beim Ring ohne Dreiecke
                 * darunter: mit Randstrasse ist dieser Weg seit Monaten in
                 * Betrieb, und der Paritaetslauf misst ideale Deckung.
                 */
                if (unbaubareWeglassen && ZellenHaarriss(ring))
                {
                    unbaubar?.Add(floatRing);
                    continue;
                }

                if (Cs2Triangulierung.Dreiecke(floatRing) != 0)
                {
                    ausgabe.Add(floatRing);
                    continue;
                }

                if (!PlaneZellenCs2Sehne(ring, out var erster, out var zweiter))
                {
                    /*
                     * NICHT ABSCHICKEN, WAS CS2 NACHWEISLICH ABLEHNT.
                     *
                     * Hier stand `ausgabe.Add(floatRing)`. Der Ring ging also
                     * an CS2, obwohl `Cs2Triangulierung` - die bitgenaue
                     * Nachbildung von CS2s Ear-Clipping - schon feststand,
                     * dass er null Dreiecke ergibt.
                     *
                     * Die Folge war nicht bloss eine Luecke im Belag: die
                     * Flaeche wird nie zur Entity, und der Bau verlangt in
                     * `AreaTransferMaterialized` `ready >= gesamt`. EIN
                     * abgelehnter Ring hat deshalb den GANZEN Parkplatz
                     * verhindert - der Nutzer sah nur "Nur 137 von 138
                     * Flaechen wurden zu Entities" und konnte nicht bauen.
                     * Gemeldet am 2026-09-08 mit Randstrassen aus.
                     *
                     * Weggelassen kostet ein Stueck nackten Boden. Nicht
                     * bauen zu koennen kostet alles. `unbaubar` sammelt die
                     * Faelle, damit sie im Bauzettel stehen statt still zu
                     * verschwinden.
                     *
                     * NUR OHNE RANDSTRASSE, siehe `unbaubareWeglassen`. Mit
                     * Randstrasse gemessen am 2026-09-08: das Weglassen
                     * erzeugt im Paritaetslauf eine NEUE Abweichung
                     * ("Nutzerpolygon ... ungedeckt 0,6 %"). Der Lauf misst
                     * ideale Deckung und bildet CS2s Ablehnung nicht nach -
                     * die Flaeche fehlt im Spiel so oder so. Der Ringpfad
                     * bleibt deshalb, wie er seit Monaten laeuft; dort ist
                     * dieser Abbruch auch nie aufgetreten.
                     */
                    unbaubar?.Add(floatRing);
                    if (!unbaubareWeglassen) ausgabe.Add(floatRing);
                    continue;
                }

                /*
                 * AUCH DIE HAELFTEN DES SEHNENSCHNITTS.
                 *
                 * HIER entsteht der Haarriss. GEMESSEN: in die Filterung
                 * gingen 28 Grasringe hinein, keiner davon unter 1 m2 - und
                 * 29 kamen heraus, darunter eine Nadel von 0,1132 m2 mit
                 * 4,2 cm Breite. Sie ist also nicht hineingekommen, sondern
                 * hier ENTSTANDEN: ein Ring, den CS2s Ear-Clipping nicht
                 * schafft, wird an einer Sehne in zwei Teile zerlegt, und die
                 * Sehne lag so, dass ein Teil ein Haarriss ist.
                 *
                 * Der andere Teil (136,8 m2, 11 Ecken) deckt die Flaeche
                 * ohnehin; die 0,11 m2 sind nackter Boden von vier
                 * Zentimetern Breite, den niemand sieht. Weglassen ist
                 * billiger als bauen - im Zettel des Nutzers vom 17:50 stand
                 * genau dieses Gebilde als Entity in der Welt.
                 */
                foreach (var teil in new[] { erster, zweiter })
                {
                    var stueck = teil.Select(punkt =>
                        new float2((float)punkt.x, (float)punkt.y)).ToArray();
                    if (unbaubareWeglassen && ZellenHaarriss(teil))
                    {
                        // Eigene Liste: CS2 haette diesen Ring GENOMMEN. Wir
                        // lassen ihn weg. Zwei verschiedene Aussagen gehoeren
                        // nicht in dieselbe Meldung.
                        (haarrisse ?? unbaubar)?.Add(stueck);
                        continue;
                    }
                    ausgabe.Add(stueck);
                }
            }
            return ausgabe.ToArray();
        }

        /**
         * IST DER RING EIN HAARRISS?
         *
         * Gemessen wird der Radius des groessten Kreises, der hineinpasst -
         * genaehert durch `2 * Flaeche / Umfang`. Fuer ein konvexes Polygon
         * ist das der Inkreisradius; fuer ein sperriges ist es kleiner, und
         * das ist die sichere Richtung: ein Ring, der nach dieser Rechnung
         * dick genug ist, ist es wirklich.
         */
        private const double ZellenHaarrissbreite = 0.05;

        private static bool ZellenHaarriss(double2[] ring)
        {
            if (ring == null || ring.Length < 3) return true;
            var flaeche = 0.0;
            var umfang = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                flaeche += a.x * b.y - b.x * a.y;
                umfang += math.distance(a, b);
            }
            flaeche = System.Math.Abs(flaeche) / 2;
            if (umfang < 1e-9) return true;
            return 2 * flaeche / umfang < ZellenHaarrissbreite;
        }

        /**
         * ZERLEGT EINEN RING, DER SICH SELBST BERUEHRT.
         *
         * Ein Materialbauteil darf sich an einer Kante oder einem Punkt selbst
         * beruehren - Zellen sind Zellen, und zwei Aeste desselben Bauteils
         * duerfen aneinanderstossen. Der daraus gezogene Rand ist dann aber
         * KEIN einfaches Polygon mehr: er laeuft in den Beruehrpunkt hinein
         * und wieder heraus. CS2s Ear-Clipping braucht ein einfaches Polygon
         * und liefert sonst null Dreiecke - und verwirft damit den ganzen
         * Ring, nicht nur den Zipfel.
         *
         * GEMESSEN am 2026-09-04 im Protokoll PLT-A950B8E0: der Korridorring
         * einer Zoningflaeche lief 42,10 m an der Parzellenkante entlang,
         * kehrte um und lief 10,10 m auf derselben Linie zurueck. Die beiden
         * Knoten am Wendepunkt lagen 7,6 Mikrometer auseinander. 1.081 m2
         * Belag fielen deshalb ersatzlos aus - im Spiel fuhren die Autos dort
         * ueber Gras.
         *
         * Zerlegt wird am wiederkehrenden Knoten: der dazwischenliegende
         * Teilring wird herausgeschnitten und eigenstaendig ausgegeben. Das
         * ist flaechentreu - beide Teile zusammen decken exakt dasselbe ab -
         * und verschiebt keine Materialgrenze, weil nur vorhandene Randknoten
         * verwendet werden. Ein Zipfel ohne Flaeche (Hin- und Rueckweg auf
         * derselben Linie) faellt dabei von selbst weg.
         *
         * Die Toleranz ist 1 mm. Die kuerzeste Kante eines angenommenen
         * Ringes lag in derselben Messung bei 0,499 m; echte Merkmale des
         * Zellenmodells sind Dezimeter gross. Darunter liegt nur Rauschen.
         */
        private const double SelbstberuehrungToleranz = 1e-3;

        private static IEnumerable<float2[]> ZerlegeSelbstberuehrung(float2[] roh)
        {
            var ring = OhneStacheln(OhneNachbardoppel(roh));
            if (ring == null || ring.Length < 3)
            {
                if (roh != null && roh.Length >= 3) yield return roh;
                yield break;
            }

            var stapel = new List<float2>();
            var teile = new List<float2[]>();
            foreach (var punkt in ring)
            {
                // Rueckwaerts suchen: der zuletzt gesehene gleiche Knoten
                // schliesst die kleinste Schlaufe, also die innerste.
                var treffer = -1;
                for (var i = stapel.Count - 1; i >= 0; i--)
                    if (math.distance(stapel[i], punkt) <= SelbstberuehrungToleranz)
                    {
                        treffer = i;
                        break;
                    }
                if (treffer < 0)
                {
                    stapel.Add(punkt);
                    continue;
                }
                var schlaufe = stapel.Skip(treffer).ToArray();
                stapel.RemoveRange(treffer + 1, stapel.Count - treffer - 1);
                teile.Add(schlaufe);
            }
            teile.Add(stapel.ToArray());

            var gefunden = false;
            foreach (var teil in teile)
            {
                var sauber = OhneStacheln(OhneNachbardoppel(teil));
                if (sauber == null || sauber.Length < 3) continue;
                // Ein Hin- und Rueckweg auf derselben Linie hat keine Flaeche
                // und darf ersatzlos entfallen; er war nie Belag.
                if (System.Math.Abs(RingflaecheFloat(sauber)) < 0.01) continue;
                gefunden = true;
                yield return sauber;
            }
            if (!gefunden) yield return ring;
        }

        /**
         * ENTFERNT NADELN - Zipfel ohne Flaeche.
         *
         * Zwei nur haarfein voneinander getrennte Rasterlinien erzeugen eine
         * Zellreihe ohne nennenswerte Breite. Im gezogenen Rand steht sie als
         * Umkehr auf derselben Geraden: der Ring laeuft hinaus und auf
         * demselben Weg zurueck. GEMESSEN im Protokoll PLT-A950B8E0: 42,10 m
         * hinaus, 10,10 m zurueck, seitlicher Versatz 7,6 Mikrometer.
         *
         * Fuer CS2 ist so ein Ring kein einfaches Polygon: der 0,1-m-Versatz
         * vor dem Ear-Clipping laesst die beiden Kanten einander kreuzen, es
         * entstehen null Dreiecke, und der GANZE Ring faellt weg - hier
         * 1.081 m2 Belag, auf denen im Spiel Autos ueber Gras fuhren.
         *
         * Der Zipfel hat keine Flaeche, also ist sein Entfernen flaechentreu.
         * Gemessen wird in Metern - der seitliche Abstand des kuerzeren Astes
         * von der Geraden des laengeren - und nicht in Grad: ein Winkel sagt
         * bei 42 m Kantenlaenge nichts darueber, ob die Flaeche verschwindet.
         */
        private static float2[] OhneStacheln(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return ring;
            var punkte = new List<float2>(ring);
            var geaendert = true;
            while (geaendert && punkte.Count >= 3)
            {
                geaendert = false;
                for (var i = 0; i < punkte.Count; i++)
                {
                    var c = punkte[i];
                    var vor = punkte[(i - 1 + punkte.Count) % punkte.Count];
                    var nach = punkte[(i + 1) % punkte.Count];
                    var u = vor - c;
                    var v = nach - c;
                    var lu = math.length(u);
                    var lv = math.length(v);
                    if (lu < 1e-6f || lv < 1e-6f) continue;
                    // Beide Nachbarn muessen von hier aus in DIESELBE Richtung
                    // liegen - sonst ist es eine gewoehnliche Ecke.
                    if (math.dot(u / lu, v / lv) <= 0f) continue;
                    var kreuz = System.Math.Abs(
                        (double)u.x * v.y - (double)u.y * v.x);
                    if (kreuz / System.Math.Max(lu, lv) > SelbstberuehrungToleranz)
                        continue;
                    punkte.RemoveAt(i);
                    geaendert = true;
                    break;
                }
            }
            return OhneNachbardoppel(punkte.ToArray());
        }

        /** Entfernt aufeinanderfolgende Knoten, die praktisch derselbe sind. */
        private static float2[] OhneNachbardoppel(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return ring;
            var raus = new List<float2>(ring.Length);
            foreach (var punkt in ring)
                if (raus.Count == 0
                    || math.distance(raus[raus.Count - 1], punkt)
                        > SelbstberuehrungToleranz)
                    raus.Add(punkt);
            while (raus.Count >= 2
                && math.distance(raus[0], raus[raus.Count - 1])
                    <= SelbstberuehrungToleranz)
                raus.RemoveAt(raus.Count - 1);
            return raus.ToArray();
        }

        private static double RingflaecheFloat(float2[] ring)
        {
            var summe = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                summe += (double)a.x * b.y - (double)b.x * a.y;
            }
            return summe / 2.0;
        }

        /** Sucht eine flaechenneutrale Sehne, deren beide Teile CS2 annimmt. */
        private static bool PlaneZellenCs2Sehne(
            double2[] ring, out double2[] erster, out double2[] zweiter)
        {
            erster = null;
            zweiter = null;
            if (ring == null || ring.Length < 4) return false;

            var kandidaten = new List<(int A, int B)>();
            var engstelle = MinimumSurfaceFeature(
                new List<double2[]> { ring });
            if (engstelle != null)
            {
                var a = engstelle.Point;
                for (var schritt = 2; schritt <= ring.Length - 2; schritt++)
                    kandidaten.Add((a, (a + schritt) % ring.Length));
            }
            // Die vollstaendige Suche bleibt auf kleine Ringe beschraenkt.
            // Materialringe mit hunderten Punkten duerfen die Vorschau nicht
            // wieder in die alte quadratische Reparaturzeit treiben.
            if (ring.Length <= 64)
                for (var a = 0; a < ring.Length; a++)
                    for (var b = a + 2; b < ring.Length; b++)
                        if (!(a == 0 && b == ring.Length - 1))
                            kandidaten.Add((a, b));

            var gewollt = System.Math.Abs(SignedArea(ring));
            var besterWert = double.NegativeInfinity;
            var gesehen = new HashSet<(int A, int B)>();
            foreach (var kandidat in kandidaten)
            {
                var a = kandidat.A;
                var b = kandidat.B;
                if (a > b) (a, b) = (b, a);
                if (b - a < 2 || a == 0 && b == ring.Length - 1
                    || !gesehen.Add((a, b))) continue;

                var teilA = SurfaceCcw(
                    ring.Skip(a).Take(b - a + 1).ToArray());
                var teilB = SurfaceCcw(
                    ring.Skip(b).Concat(ring.Take(a + 1)).ToArray());
                if (teilA.Length < 3 || teilB.Length < 3
                    || SelfIntersects(teilA, 2e-6)
                    || SelfIntersects(teilB, 2e-6)) continue;
                var bekommen = System.Math.Abs(SignedArea(teilA))
                    + System.Math.Abs(SignedArea(teilB));
                if (System.Math.Abs(bekommen - gewollt)
                    > System.Math.Max(1e-7, gewollt * 1e-10)) continue;
                if (!ZellenCs2Baubar(teilA) || !ZellenCs2Baubar(teilB)) continue;

                var teile = new List<double2[]> { teilA, teilB };
                var hals = MinimumSurfaceFeature(teile)?.Distance ?? 1e5;
                var wert = System.Math.Min(hals, 1e5) * 1e9
                    + System.Math.Min(System.Math.Abs(SignedArea(teilA)),
                        System.Math.Abs(SignedArea(teilB)));
                if (wert <= besterWert) continue;
                besterWert = wert;
                erster = teilA;
                zweiter = teilB;
            }
            return erster != null;
        }

        /**
         * Flaeche eines Ringes nach der Gaussschen Trapezformel.
         *
         * Nur fuer die Meldung ueber weggelassene Ringe gedacht: sie sagt dem
         * Nutzer, ob ein Loch kosmetisch ist oder wehtut. Das Vorzeichen
         * interessiert dabei nicht, der Aufrufer nimmt den Betrag.
         */
        private static double RingFlaeche(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return 0.0;
            var summe = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                summe += (double)a.x * b.y - (double)b.x * a.y;
            }
            return summe / 2.0;
        }

        private static bool ZellenCs2Baubar(double2[] ring)
        {
            var floatRing = ring.Select(punkt =>
                new float2((float)punkt.x, (float)punkt.y)).ToArray();
            return Cs2Triangulierung.Dreiecke(floatRing) != 0;
        }
    }
}
