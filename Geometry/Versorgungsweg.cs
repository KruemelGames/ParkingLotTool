using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Eigene Implementierung. Konvexe Bezier-Huellen statt Meterproben:
    // jeder Kontrollpunkt liegt in der aufgeweiteten Flaeche.
    internal static class Versorgungsweg
    {
        internal sealed class Hindernis
        {
            internal int Strasse;
            internal float2[] Ring;
            internal bool Querbar;
            internal float2 Richtung;

            /*
             * ABGELEITET, NICHT ZUGEWIESEN.
             *
             * Beides haengt allein an `Ring` und `Richtung`. Wer ein
             * Hindernis von Hand baut - der Paritaetstest tut das -, soll
             * nicht daran denken muessen; sonst steht hier irgendwann eine
             * Null und die Wegesuche verwirft stillschweigend das Falsche.
             */
            private bool _abgeleitet;
            private float2 _achse, _min, _max;

            private void Leite()
            {
                _achse = math.normalizesafe(Richtung);
                _min = new float2(float.MaxValue);
                _max = new float2(float.MinValue);
                if (Ring != null)
                    foreach (var p in Ring) { _min = math.min(_min, p); _max = math.max(_max, p); }
                _abgeleitet = true;
            }

            /** Einmal normalisiert statt bei jeder Sichtpruefung erneut. */
            internal float2 Achse { get { if (!_abgeleitet) Leite(); return _achse; } }

            /** Umgebender Kasten - fuer die billige Abweisung in `Frei`.
             *  Ohne Ring bleibt er leer, und `Frei` ueberspringt die Huelle;
             *  eine Huelle ohne Ecken kann ohnehin nichts schneiden. */
            internal float2 Min { get { if (!_abgeleitet) Leite(); return _min; } }
            internal float2 Max { get { if (!_abgeleitet) Leite(); return _max; } }
        }

        internal struct Ziel
        {
            internal float2 Punkt;
            internal int Index;
        }

        internal sealed class Ergebnis
        {
            internal List<float2> Punkte;
            internal int Start, Ziel, Knoten, Erreicht, Sichtpruefungen, Zielpruefungen;
            internal float Laenge;
        }

        private static float Kreuz(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        private const float Epsilon = 0.001f;

        internal static void Bogen(List<Hindernis> ausgabe, int strasse,
            float2 a, float2 b, float2 c, float2 d, float radius, int tiefe = 0, bool querbar = false)
        {
            var v = d - a;
            var l = math.length(v);
            var fehler = l < Epsilon ? math.max(math.distance(a, b), math.distance(a, c))
                : math.max(math.abs(Kreuz(b - a, v)), math.abs(Kreuz(c - a, v))) / l;
            if (fehler > 0.1f && tiefe < 12)
            {
                var ab = (a + b) / 2; var bc = (b + c) / 2; var cd = (c + d) / 2;
                var abc = (ab + bc) / 2; var bcd = (bc + cd) / 2; var m = (abc + bcd) / 2;
                Bogen(ausgabe, strasse, a, ab, abc, m, radius, tiefe + 1, querbar);
                Bogen(ausgabe, strasse, m, bcd, cd, d, radius, tiefe + 1, querbar);
                return;
            }
            var punkte = new List<float2>();
            // Umschriebenes Achteck: keine Unterschaetzung zwischen Stuetzrichtungen.
            foreach (var p in new[] { a, b, c, d })
                for (var i = 0; i < 8; i++)
                {
                    var w = i * math.PI / 4;
                    punkte.Add(p + new float2(math.cos(w), math.sin(w)) * (radius / math.cos(math.PI / 8)));
                }
            punkte.Sort((x, y) => x.x == y.x ? x.y.CompareTo(y.y) : x.x.CompareTo(y.x));
            var h = new List<float2>();
            foreach (var p in punkte)
            {
                while (h.Count >= 2 && Kreuz(h[h.Count - 1] - h[h.Count - 2], p - h[h.Count - 1]) <= 0)
                    h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            var unten = h.Count;
            for (var i = punkte.Count - 2; i >= 0; i--)
            {
                var p = punkte[i];
                while (h.Count > unten && Kreuz(h[h.Count - 1] - h[h.Count - 2], p - h[h.Count - 1]) <= 0)
                    h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            h.RemoveAt(h.Count - 1);
            ausgabe.Add(new Hindernis { Strasse = strasse, Ring = h.ToArray(),
                Querbar = querbar, Richtung = v });
        }

        // Analytisches Clipping gegen alle Halbebenen. Beruehrung der bereits
        // aufgeweiteten Grenze ist erlaubt, Eindringen um mehr als 1 mm nicht.
        internal static bool Innen(float2 a, float2 b, float2[] ring)
        {
            var von = 0f; var bis = 1f;
            for (var i = 0; i < ring.Length; i++)
            {
                var p = ring[i]; var v = ring[(i + 1) % ring.Length] - p;
                var l = math.length(v);
                if (l < Epsilon) continue;
                var x = Kreuz(v, a - p) / l - Epsilon;
                var delta = Kreuz(v, b - a) / l;
                if (math.abs(delta) < 1e-7f) { if (x <= 0) return false; continue; }
                var t = -x / delta;
                if (delta > 0) von = math.max(von, t); else bis = math.min(bis, t);
                if (von >= bis - 1e-7f) return false;
            }
            return von < bis - 1e-7f;
        }

        internal static bool Frei(float2 a, float2 b, List<Hindernis> hindernisse,
            ISet<int> startstrassen = null, float ausgang = 0, ISet<int> zielstrassen = null)
        {
            var laenge = math.distance(a, b);
            var kastenMin = math.min(a, b);
            var kastenMax = math.max(a, b);
            foreach (var h in hindernisse)
            {
                // Liegt die Strecke ganz neben der Huelle, kann sie sie nicht
                // schneiden. Spart das Clipping, ohne die Antwort zu aendern.
                if (kastenMax.x < h.Min.x || kastenMin.x > h.Max.x
                    || kastenMax.y < h.Min.y || kastenMin.y > h.Max.y) continue;
                // Nutzerentscheidung 06.09.: 38 von 43 Huellen waren Fahrgassen.
                // Queranteil mindestens Laengsanteil (45 Grad, 1 mm Toleranz).
                // Entartete Achsen bleiben konservativ gesperrt.
                var achse = h.Achse;
                var kurs = b - a;
                if (h.Querbar && math.lengthsq(achse) > 0.5f
                    && math.abs(Kreuz(achse, kurs)) + Epsilon >= math.abs(math.dot(achse, kurs))) continue;
                var von = startstrassen != null && startstrassen.Contains(h.Strasse) ? ausgang : 0f;
                var bis = laenge - (zielstrassen != null && zielstrassen.Contains(h.Strasse) ? ausgang : 0f);
                if (bis <= von + Epsilon) continue;
                if (Innen(math.lerp(a, b, von / math.max(Epsilon, laenge)),
                    math.lerp(a, b, bis / math.max(Epsilon, laenge)), h.Ring)) return false;
            }
            return true;
        }

        internal static Ergebnis Suche(List<float2> starts, List<Hindernis> hindernisse,
            Func<int, ISet<int>> startstrassen, Func<float2, IEnumerable<Ziel>> ziele,
            Func<List<float2>, int, bool> zulaessig, float ausgang = 8,
            Func<int, ISet<int>> zielstrassen = null, float maxLaenge = float.MaxValue)
        {
            var r = new Ergebnis { Laenge = float.MaxValue };
            var punkte = new List<float2>(starts);
            foreach (var h in hindernisse)
                foreach (var p in h.Ring)
                {
                    // Kein kuerzerer Weg kann einen Knoten erreichen, der schon
                    // in Luftlinie weiter als die bekannte gueltige Trasse liegt.
                    var erreichbar = maxLaenge == float.MaxValue;
                    foreach (var start in starts)
                        if (math.distance(start, p) <= maxLaenge + Epsilon)
                        { erreichbar = true; break; }
                    if (!erreichbar) continue;
                    var innen = false;
                    foreach (var k in hindernisse)
                        if (!k.Querbar && Innen(p, p, k.Ring)) { innen = true; break; }
                    if (!innen && !punkte.Contains(p)) punkte.Add(p);
                }
            r.Knoten = punkte.Count;
            var distanz = new float[punkte.Count]; var vorher = new int[punkte.Count];
            var quelle = new int[punkte.Count]; var fertig = new bool[punkte.Count];
            for (var i = 0; i < punkte.Count; i++)
            {
                distanz[i] = i < starts.Count ? 0 : float.MaxValue;
                vorher[i] = -1; quelle[i] = i < starts.Count ? i : -1;
            }
            for (var lauf = 0; lauf < punkte.Count; lauf++)
            {
                var u = -1;
                for (var i = 0; i < punkte.Count; i++)
                    if (!fertig[i] && distanz[i] < float.MaxValue && (u < 0 || distanz[i] < distanz[u])) u = i;
                if (u < 0 || distanz[u] > math.min(r.Laenge, maxLaenge) + Epsilon) break;
                fertig[u] = true; r.Erreicht++;
                var freiStart = u < starts.Count ? startstrassen(u) : null;
                foreach (var ziel in ziele(punkte[u]))
                {
                    r.Zielpruefungen++;
                    var laenge = distanz[u] + math.distance(punkte[u], ziel.Punkt);
                    if (laenge > maxLaenge || laenge >= r.Laenge - Epsilon || !Frei(punkte[u], ziel.Punkt, hindernisse, freiStart, ausgang, zielstrassen?.Invoke(ziel.Index))) continue;
                    var weg = new List<float2> { ziel.Punkt };
                    for (var n = u; n >= 0; n = vorher[n]) weg.Add(punkte[n]);
                    weg.Reverse();
                    if (zulaessig != null && !zulaessig(weg, ziel.Index)) continue;
                    r.Punkte = weg; r.Start = quelle[u]; r.Ziel = ziel.Index; r.Laenge = laenge;
                }
                for (var v = starts.Count; v < punkte.Count; v++)
                {
                    if (fertig[v]) continue;
                    var d = distanz[u] + math.distance(punkte[u], punkte[v]);
                    if (d >= distanz[v] - Epsilon || d > math.min(r.Laenge, maxLaenge)) continue;
                    r.Sichtpruefungen++;
                    if (!Frei(punkte[u], punkte[v], hindernisse, freiStart, ausgang)) continue;
                    distanz[v] = d; vorher[v] = u; quelle[v] = quelle[u];
                }
            }
            return r;
        }

        internal static List<float2> Versetze(List<float2> weg, float abstand)
        {
            if (weg.Count < 2) return null;
            foreach (var p in weg) if (!math.all(math.isfinite(p))) return null;
            var normal = new List<float2>();
            for (var i = 1; i < weg.Count; i++)
            {
                var v = weg[i] - weg[i - 1]; var l = math.length(v);
                if (l < 1f - Epsilon) return null;
                normal.Add(new float2(-v.y, v.x) / l);
            }
            var r = new List<float2> { weg[0] + normal[0] * abstand };
            for (var i = 1; i < weg.Count - 1; i++)
            {
                var summe = normal[i - 1] + normal[i];
                var nenner = math.dot(summe, normal[i]);
                if (nenner < 0.5f - Epsilon) return null;
                r.Add(weg[i] + summe * (abstand / nenner));
            }
            r.Add(weg[weg.Count - 1] + normal[normal.Count - 1] * abstand);
            for (var i = 1; i < r.Count; i++)
            {
                if (math.distance(r[i - 1], r[i]) < 1f - Epsilon
                    || math.dot(r[i] - r[i - 1], weg[i] - weg[i - 1]) <= 0) return null;
                for (var j = 1; j < i - 1; j++)
                    if (Streckenabstand(r[i - 1], r[i], r[j - 1], r[j]) < Epsilon) return null;
            }
            return r;
        }

        internal static bool Spuren(List<float2> weg, float strombreite, float wasserbreite,
            List<Hindernis> hindernisse, ISet<int> startstrassen, float ausgang,
            out List<float2> strom, out List<float2> wasser,
            Func<float2, bool, bool> zielpruefer = null, ISet<int> zielstrassen = null,
            Func<float2, bool, bool> startpruefer = null)
        {
            var abstand = VersorgungskursPruefung.Achsabstand(strombreite, wasserbreite);
            strom = Versetze(weg, -abstand / 2);
            wasser = Versetze(weg, abstand / 2);
            if (strom == null || wasser == null) return false;
            // Beide versetzten Enden pruefen, bevor die Trasse gewaehlt wird.
            // Im Log vom 05.09. wechselte das fehlende Ende zwischen 2 Arten.
            if (zielpruefer != null && (!zielpruefer(strom[strom.Count - 1], true)
                || !zielpruefer(wasser[wasser.Count - 1], false))) return false;
            if (startpruefer != null && (!startpruefer(strom[0], true)
                || !startpruefer(wasser[0], false))) return false;
            foreach (var spur in new[] { strom, wasser })
                for (var i = 1; i < spur.Count; i++)
                {
                    if (!Frei(spur[i - 1], spur[i], hindernisse,
                        i == 1 ? startstrassen : null, ausgang,
                        i == spur.Count - 1 ? zielstrassen : null)) return false;
                    for (var j = 1; j < i - 1; j++)
                        if (Streckenabstand(spur[i - 1], spur[i], spur[j - 1], spur[j])
                            < math.max(strombreite, wasserbreite) + 0.25f) return false;
                }
            for (var i = 1; i < strom.Count; i++)
                for (var j = 1; j < wasser.Count; j++)
                    if (Streckenabstand(strom[i - 1], strom[i], wasser[j - 1], wasser[j])
                        < (strombreite + wasserbreite) / 2 + 0.249f) return false;
            return true;
        }

        internal static float Streckenabstand(float2 a, float2 b, float2 c, float2 d)
        {
            var v = b - a; var w = d - c; var nenner = Kreuz(v, w);
            if (math.abs(nenner) > 1e-7f)
            {
                var t = Kreuz(c - a, w) / nenner; var u = Kreuz(c - a, v) / nenner;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return 0;
            }
            float Punkt(float2 p, float2 x, float2 y)
                => math.distance(p, x + (y - x) * math.clamp(math.dot(p - x, y - x) / math.max(1e-12f, math.lengthsq(y - x)), 0, 1));
            return math.min(math.min(Punkt(a, c, d), Punkt(b, c, d)), math.min(Punkt(c, a, b), Punkt(d, a, b)));
        }
    }
}
