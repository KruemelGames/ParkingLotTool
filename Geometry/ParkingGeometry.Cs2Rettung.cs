using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * KEIN RING VERLAESST DEN KERN, DEN CS2 VERWERFEN WUERDE.
         *
         * Anlass (2026-10-08): der Nutzer meldete erneut fehlende Flaechen.
         * Gemessen mit `--teilen` (588 Layouts): in 129 fehlten Ringe, 672
         * insgesamt. Die meisten waren Splitter unter 0,05 m2, aber auch
         * Ringe mit 20 bis 400 m2. JEDER grosse hatte irgendwo ein Merkmal
         * schmaler als 0,2 m - eine Engstelle von 1 mm bis 20 cm, eine kurze
         * Stufe, oder eine Ecke, die auf einer anderen Kante desselben Rings
         * liegt (im Spielfall: zwei Rasenrechtecke, verbunden durch einen
         * Korridor der Breite null am Nahtrand).
         *
         * CS2 rueckt jeden Ring vor dem Ear-Clipping 0,1 m ein (siehe
         * `Cs2Triangulierung`). Alles unter 0,2 m klappt dabei um, und dann
         * faellt der GANZE Ring weg. Bis hier gab es genau EINEN Rettungsweg:
         * eine Sehne, nach der beide Haelften baubar sind. Hatte ein Ring zwei
         * solche Stellen, gab es keine solche Sehne.
         *
         * Jetzt drei Stufen, nur fuer Ringe, die das Orakel ablehnt -
         * baubare Ringe bleiben bitgleich:
         *
         *   1. ENTWIRREN: Ecken, die auf einer anderen Kante desselben Rings
         *      liegen, werden dort als Knoten eingesetzt; danach zerfaellt
         *      der Ring an wiederkehrenden Knoten (`ZerlegeSelbstberuehrung`),
         *      flaechenlose Schlaufen fallen weg.
         *   2. SEHNE wie bisher.
         *   3. DREIECKSVERBUND: das Stueck in Dreiecke zerlegen (nur
         *      vorhandene Ecken, keine neuen Punkte) und die Dreiecke gierig
         *      wieder zusammenlegen, solange CS2 das Ergebnis nimmt. Die
         *      Dreiecke einer Zerlegung haengen als Baum zusammen; zwei
         *      zusammenhaengende Teilbaeume teilen genau eine Diagonale, ihre
         *      Vereinigung ist also wieder ein einfaches Polygon.
         *
         * Was danach noch abgelehnt wird, ist schmaler als 0,2 m - das kann
         * CS2 an keiner Stelle bauen. Es faellt weg und wird gezaehlt.
         *
         * Flaechentreu bis auf diese Reste: es entstehen keine neuen Punkte,
         * keine Materialgrenze wandert; nur die Zahl der Flaechen steigt.
         */
        internal static List<float2[]> RetteFuerCs2(
            float2[] ring, List<float2[]> verloren)
        {
            var ausgabe = new List<float2[]>();
            if (LiveAn)
                Live("  cs2-rettung | " + ring.Length + " ecken | "
                    + System.Math.Abs(RingflaecheFloat(ring)).ToString("F2") + " m2 | "
                    + string.Join(" ", ring.Select(q => q.x.ToString("R",
                        System.Globalization.CultureInfo.InvariantCulture) + ","
                        + q.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
            foreach (var stueck in ZerlegeSelbstberuehrung(MitTBeruehrungen(ring)))
            {
                if (stueck == null || stueck.Length < 3) continue;
                if (Cs2Triangulierung.Dreiecke(stueck) != 0)
                {
                    ausgabe.Add(stueck);
                    continue;
                }
                var doppel = stueck.Select(p => new double2(p.x, p.y)).ToArray();
                if (PlaneZellenCs2Sehne(doppel, out var erster, out var zweiter))
                {
                    ausgabe.Add(NachFloat(erster));
                    ausgabe.Add(NachFloat(zweiter));
                    continue;
                }
                ausgabe.AddRange(Dreiecksverbund(stueck, verloren));
            }
            return ausgabe;
        }

        /**
         * WAS VON EINEM RING AN CS2 GEHEN DARF - fuer den Bau UND fuer den
         * Bestand (Sync-Schritt 13), damit die Regel nur an einer Stelle
         * steht.
         *
         * Nimmt CS2 den Ring, kommt er unveraendert zurueck. Sonst die
         * baubaren Stuecke aus `RetteFuerCs2`, ohne Haarrisse (die gehen nach
         * `haarrisse`, sonst nach `unbaubar`); was CS2 nirgends bauen kann,
         * landet in `unbaubar`. Ein abgelehnter Ring kommt NIE zurueck.
         */
        public static List<float2[]> BaubareStuecke(
            float2[] ring, List<float2[]> unbaubar, List<float2[]> haarrisse = null)
        {
            if (ring == null || ring.Length < 3) return new List<float2[]>();
            if (Cs2Triangulierung.Dreiecke(ring) != 0) return new List<float2[]> { ring };
            var verloren = new List<float2[]>();
            var ausgabe = new List<float2[]>();
            foreach (var stueck in RetteFuerCs2(ring, verloren))
            {
                if (ZellenHaarriss(stueck.Select(p => new double2(p.x, p.y)).ToArray()))
                {
                    (haarrisse ?? unbaubar)?.Add(stueck);
                    continue;
                }
                ausgabe.Add(stueck);
            }
            foreach (var rest in verloren) unbaubar?.Add(rest);
            return ausgabe;
        }

        private static float2[] NachFloat(double2[] ring)
            => ring.Select(p => new float2((float)p.x, (float)p.y)).ToArray();

        /**
         * Setzt jede Ecke, die (unter 1 mm) im Inneren einer ANDEREN Kante
         * desselben Rings liegt, dort als Knoten ein - mit exakt denselben
         * Koordinaten. Aus einer T-Beruehrung wird so ein wiederkehrender
         * Knoten, an dem `ZerlegeSelbstberuehrung` den Ring trennen kann.
         */
        private static float2[] MitTBeruehrungen(float2[] ring)
        {
            if (ring == null || ring.Length < 4) return ring;
            var punkte = new List<float2>(ring);
            for (var runde = 0; runde < 4; runde++)
            {
                var eingesetzt = false;
                for (var k = 0; k < punkte.Count; k++)
                {
                    var a = punkte[k];
                    var b = punkte[(k + 1) % punkte.Count];
                    var ab = b - a;
                    var laenge2 = math.lengthsq(ab);
                    if (laenge2 < 1e-12f) continue;
                    var treffer = new List<(float T, float2 P)>();
                    foreach (var p in punkte)
                    {
                        var t = math.dot(p - a, ab) / laenge2;
                        var laenge = math.sqrt(laenge2);
                        if (t * laenge <= SelbstberuehrungToleranz
                            || (1 - t) * laenge <= SelbstberuehrungToleranz) continue;
                        var abstand = math.abs(ab.x * (p.y - a.y) - ab.y * (p.x - a.x)) / laenge;
                        if (abstand > SelbstberuehrungToleranz) continue;
                        treffer.Add((t, p));
                    }
                    if (treffer.Count == 0) continue;
                    punkte.InsertRange(k + 1, treffer.OrderBy(x => x.T).Select(x => x.P));
                    k += treffer.Count;
                    eingesetzt = true;
                }
                if (!eingesetzt) break;
            }
            return punkte.ToArray();
        }

        /**
         * Ear-Clipping in double auf den VORHANDENEN Ecken, dann gieriges
         * Zusammenlegen, solange CS2 das Ergebnis annimmt.
         */
        private static List<float2[]> Dreiecksverbund(
            float2[] ring, List<float2[]> verloren)
        {
            var punkte = ring.ToArray();
            var flaeche = RingflaecheFloat(punkte);
            if (flaeche < 0) System.Array.Reverse(punkte);
            var dreiecke = Ohrenschnitt(punkte);
            if (dreiecke == null)
            {
                verloren?.Add(ring);
                return new List<float2[]>();
            }

            // Jedes Stueck ist eine Liste von Ringindizes gegen den
            // Uhrzeigersinn. Benachbart ist, was eine Kante gegenlaeufig teilt.
            var stuecke = dreiecke.Select(d => new List<int> { d.A, d.B, d.C }).ToList();
            bool Baubar(List<int> s) => Cs2Triangulierung.Dreiecke(
                s.Select(i => punkte[i]).ToArray()) != 0;
            var geaendert = true;
            while (geaendert)
            {
                geaendert = false;
                // Kleine (meist duenne) Stuecke zuerst anlehnen.
                var reihenfolge = Enumerable.Range(0, stuecke.Count)
                    .OrderBy(i => System.Math.Abs(RingflaecheFloat(
                        stuecke[i].Select(k => punkte[k]).ToArray())))
                    .ToList();
                foreach (var i in reihenfolge)
                {
                    var bestes = -1;
                    List<int> vereint = null;
                    var besteLaenge = -1f;
                    for (var j = 0; j < stuecke.Count; j++)
                    {
                        if (j == i) continue;
                        var v = Vereine(stuecke[i], stuecke[j], out var kante);
                        if (v == null) continue;
                        var laenge = math.distance(punkte[kante.A], punkte[kante.B]);
                        if (laenge <= besteLaenge || !Baubar(v)) continue;
                        besteLaenge = laenge;
                        bestes = j;
                        vereint = v;
                    }
                    if (bestes < 0) continue;
                    stuecke[i] = vereint;
                    stuecke.RemoveAt(bestes);
                    geaendert = true;
                    break;
                }
            }

            var ausgabe = new List<float2[]>();
            foreach (var s in stuecke)
            {
                var r = s.Select(i => punkte[i]).ToArray();
                if (Cs2Triangulierung.Dreiecke(r) != 0) ausgabe.Add(r);
                else verloren?.Add(r);
            }
            return ausgabe;
        }

        /**
         * Vereinigt zwei Stuecke, die genau eine Kante gegenlaeufig teilen.
         * `null`, wenn sie keine oder mehr als eine teilen.
         */
        private static List<int> Vereine(List<int> a, List<int> b, out (int A, int B) kante)
        {
            kante = default;
            var treffer = 0;
            int ia = -1, ib = -1;
            for (var x = 0; x < a.Count; x++)
            {
                var u = a[x];
                var v = a[(x + 1) % a.Count];
                for (var y = 0; y < b.Count; y++)
                    if (b[y] == v && b[(y + 1) % b.Count] == u)
                    {
                        treffer++;
                        ia = x;
                        ib = y;
                    }
            }
            if (treffer != 1) return null;
            kante = (a[ia], a[(ia + 1) % a.Count]);
            // a: ... u(ia) v(ia+1) ...   b: ... v(ib) u(ib+1) ...
            // Vereinigung: von v in a weiter bis u, dann in b von u weiter bis v.
            var ergebnis = new List<int>();
            for (var k = 0; k < a.Count; k++)
                ergebnis.Add(a[(ia + 1 + k) % a.Count]);
            // Jetzt endet ergebnis mit u; b ab u (ib+1) weiter bis vor v (ib).
            for (var k = 2; k < b.Count; k++)
                ergebnis.Add(b[(ib + k) % b.Count]);
            return ergebnis;
        }

        /**
         * Einfaches Ear-Clipping in double ueber Ringindizes. Gestreckte und
         * Rueckkehr-Ecken (Kreuzprodukt ~ 0) werden ohne Dreieck entfernt -
         * sie tragen keine Flaeche. `null`, wenn kein Ohr mehr zu finden ist.
         */
        private static List<(int A, int B, int C)> Ohrenschnitt(float2[] punkte)
        {
            var offen = Enumerable.Range(0, punkte.Length).ToList();
            var aus = new List<(int, int, int)>();
            double2 P(int i) => new double2(punkte[i].x, punkte[i].y);
            var budget = punkte.Length * punkte.Length + 10;
            while (offen.Count > 3 && budget-- > 0)
            {
                var geschnitten = false;
                for (var k = 0; k < offen.Count; k++)
                {
                    var ia = offen[(k - 1 + offen.Count) % offen.Count];
                    var ib = offen[k];
                    var ic = offen[(k + 1) % offen.Count];
                    var a = P(ia); var b = P(ib); var c = P(ic);
                    var kreuz = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                    var skala = math.length(b - a) * math.length(c - b);
                    if (math.abs(kreuz) <= 1e-12 * math.max(skala, 1e-12))
                    {
                        offen.RemoveAt(k);
                        geschnitten = true;
                        break;
                    }
                    if (kreuz < 0) continue;
                    var frei = true;
                    foreach (var io in offen)
                    {
                        if (io == ia || io == ib || io == ic) continue;
                        var p = P(io);
                        if (math.all(p == a) || math.all(p == b) || math.all(p == c)) continue;
                        if (ImDreieck(p, a, b, c)) { frei = false; break; }
                    }
                    if (!frei) continue;
                    aus.Add((ia, ib, ic));
                    offen.RemoveAt(k);
                    geschnitten = true;
                    break;
                }
                if (!geschnitten) return null;
            }
            if (offen.Count == 3)
            {
                var a = P(offen[0]); var b = P(offen[1]); var c = P(offen[2]);
                var kreuz = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                if (kreuz > 0) aus.Add((offen[0], offen[1], offen[2]));
            }
            return aus;
        }

        private static bool ImDreieck(double2 p, double2 a, double2 b, double2 c)
        {
            double S(double2 u, double2 v, double2 w)
                => (v.x - u.x) * (w.y - u.y) - (v.y - u.y) * (w.x - u.x);
            var d1 = S(a, b, p);
            var d2 = S(b, c, p);
            var d3 = S(c, a, p);
            // Auf dem Rand zaehlt als drin: ein Ohr, auf dessen Kante eine
            // andere Ecke liegt, wuerde den Ring an dieser Ecke durchtrennen.
            return d1 >= -1e-12 && d2 >= -1e-12 && d3 >= -1e-12;
        }
    }
}
