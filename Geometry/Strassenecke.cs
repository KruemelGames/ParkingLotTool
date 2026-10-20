using System;
using System.Collections.Generic;
using Unity.Mathematics;
using K = ParkingLotTool.Geometry.Strassenkurve;

namespace ParkingLotTool.Geometry
{
    // Herkunft eines bestaetigten Schnitts zweier nativer Strassenrand-Tangenten.
    public sealed class Strassenecke : IEquatable<Strassenecke>
    {
        public readonly K.Fang Eins, Zwei;
        public readonly float3 Punkt;
        /** Unter diesem Abstand zum Randende ist ein Schnitt am glatten Knoten keine Ecke. */
        public const float Eckenmindestabstand = .5f;
        /**
         * So weit darf eine Bordsteinlinie neben dem Randende vorbeilaufen. Der
         * Fang legt die Tangente an der dem Zeiger naechsten Stelle an; an einer
         * gebogenen Strasse verfehlt sie das Randende daher um Zentimeter
         * (Spiel 19.10.: 3-11 cm). Gleiche Toleranz wie die Abtastung.
         */
        public const double Linienabstand = K.Pfeilhoehe;

        public readonly struct Linienanker
        {
            public readonly K.Fang Eins, Zwei;
            public Linienanker(K.Fang eins, K.Fang zwei) { Eins = eins; Zwei = zwei; }
        }

        private Strassenecke(float3 punkt, K.Fang a, K.Fang b)
        {
            Punkt = punkt;
            Eins = a.Kante < b.Kante ? a : b;
            Zwei = a.Kante < b.Kante ? b : a;
        }

        private Strassenecke(float3 punkt, K.Fang anker)
        {
            Punkt = punkt;
            Eins = anker;
            Zwei = default;
        }

        /**
         * Punkt auf dem begradigten Knotenstueck hinter dem Randende (Ecke zu
         * einer anderen Strasse, Strassenende): er liegt auf der Tangente des
         * Randendes. Wie bei der Ecke fuehrt ein gerades Stueck zum Rand.
         */
        public static Strassenecke Verlaengerung(float3 punkt, K.Fang anker)
            => anker.Vorhanden ? new Strassenecke(punkt, anker) : null;

        public bool Equals(Strassenecke e) => e != null && math.all(Punkt == e.Punkt)
            && Eins.Equals(e.Eins) && Zwei.Equals(e.Zwei);

        public K.Fang AlsFang() => new K.Fang
        {
            Kante = Eins.Kante, Seite = Eins.Seite, Teil = Eins.Teil, T = Eins.T,
            Punkt = Punkt, Ecke = this
        };

        public static Linienanker Anker(K.Kante k, K.Bezier quelle, bool knotenkurve, float3 position, float2 richtung)
            => Anker(k, quelle, knotenkurve, position, richtung, out _);

        /** Wie oben; `warum` sagt fuers Log, woran ein fehlender Anker scheiterte (null = gefunden). */
        public static Linienanker Anker(K.Kante k, K.Bezier quelle, bool knotenkurve, float3 position, float2 richtung,
            out string warum)
        {
            warum = "keine Strasse";
            if (k == null) return default;
            K.Fang a = default, b = default;
            double naechstesEnde = double.PositiveInfinity;
            warum = null;
            for (int seite = -1; seite <= 1; seite += 2)
            {
                var teile = k.Seite(seite);
                if (teile == null || teile.Length == 0) continue;
                for (int ende = 0; ende < 2; ende++)
                {
                    int teil = ende == 0 ? 0 : teile.Length - 1;
                    var kurve = teile[teil].Kurve;
                    var p = ende == 0 ? kurve.Punkt(0) : kurve.Punkt(1);
                    naechstesEnde = Math.Min(naechstesEnde, Math.Min(math.distance(p, quelle.A),
                        knotenkurve ? double.PositiveInfinity : math.distance(p, quelle.D)));
                    if (math.distance(p, quelle.A) > K.Fangabstand
                        && (knotenkurve || math.distance(p, quelle.D) > K.Fangabstand)) continue;
                    var f = new K.Fang { Kante = k.Id, Seite = seite, Teil = teil, T = ende, Punkt = p };
                    var tangente = NachInnen(k, f);
                    var v = position.xz - p.xz;
                    if (math.lengthsq(tangente.xz) < .5f || math.lengthsq(richtung) < .5f)
                    { warum ??= "ohne Richtung"; continue; }
                    double winkel = Math.Acos(Math.Min(1, Math.Abs(math.dot(math.normalize(tangente.xz), richtung))));
                    double quer = Math.Abs(v.x * tangente.z - v.y * tangente.x);
                    if (winkel > K.Winkelgrenze || quer > Linienabstand)
                    {
                        warum ??= FormattableString.Invariant($"Randende {winkel * 180 / Math.PI:F1} Grad und {quer:F3} m neben der Linie");
                        continue;
                    }
                    if (!a.Vorhanden) a = f;
                    else if (!a.Equals(f) && !b.Vorhanden) b = f;
                }
            }
            if (a.Vorhanden) warum = null;
            else warum ??= FormattableString.Invariant($"kein Randende an der Linie (naechstes {naechstesEnde:F3} m)");
            return new Linienanker(a, b);
        }

        public static bool Bestaetige(float3 punkt, Linienanker a, Linienanker b,
            IReadOnlyDictionary<long, K.Kante> kanten, out Strassenecke ecke)
        {
            ecke = null;
            if (!math.all(math.isfinite(punkt))) return false;
            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
            {
                var x = i == 0 ? a.Eins : a.Zwei;
                var y = j == 0 ? b.Eins : b.Zwei;
                if (!x.Vorhanden || !y.Vorhanden || x.Ecke != null || y.Ecke != null || x.Kante == y.Kante
                    || !kanten.TryGetValue(x.Kante, out var kx) || !kanten.TryGetValue(y.Kante, out var ky)) continue;
                long nx = Knoten(kx, x), ny = Knoten(ky, y);
                if (nx == 0 || nx != ny) continue;
                // Glatter Durchlaufknoten: ein Schnitt dicht am Bordstein ist dort keine
                // Ecke. Liegt er deutlich daneben - die Ecke vor einer engen Innenrundung
                // derselben Strasse -, gilt er als Ecke (Nutzer, 19.10.).
                if (K.PruefeFortsetzung(kx, ky, nx) == K.Grund.Keiner
                    && math.distance(punkt.xz, x.Punkt.xz) < Eckenmindestabstand
                    && math.distance(punkt.xz, y.Punkt.xz) < Eckenmindestabstand) continue;
                var neu = new Strassenecke(punkt, x, y);
                if (ecke != null && !ecke.Equals(neu)) { ecke = null; return false; }
                ecke = neu;
            }
            return ecke != null;
        }

        private static long Knoten(K.Kante k, K.Fang f)
        {
            var teile = k.Seite(f.Seite);
            if (f.Teil == 0 && f.T == 0) return k.Start;
            return f.Teil == teile.Length - 1 && f.T == 1 ? k.Ende : 0;
        }

        private static float3 NachInnen(K.Kante k, K.Fang f)
        {
            var c = k.Seite(f.Seite)[f.Teil].Kurve;
            var t = f.T == 0 ? c.B - c.A : c.C - c.D;
            float l = math.length(t.xz);
            return l > 1e-8f ? t / l : default;
        }

        private static bool Anlauf(K.Fang ecke, K.Fang anker, K.Kante k)
        {
            var d = anker.Punkt - ecke.Punkt;
            if (math.distance(anker.Punkt, ecke.Punkt) <= K.Fangabstand) return true;
            var t = NachInnen(k, anker);
            double along = math.dot(d.xz, t.xz);
            return along > 0 && Math.Abs(d.x * t.z - d.z * t.x) <= Linienabstand
                && Math.Abs(d.y - along * t.y) <= K.Hoehensprung
                && math.dot(math.normalizesafe(d.xz), t.xz) >= Math.Cos(K.Winkelgrenze);
        }

        internal static K.Ergebnis Verfolge(K.Fang a, K.Fang b, IReadOnlyDictionary<long, K.Kante> kanten,
            IReadOnlyDictionary<long, K.Knoten> knoten, double d, int innen)
        {
            K.Ergebnis fund = null, fehler = null;
            for (int i = 0; i < (a.Ecke == null ? 1 : 2); i++)
            for (int j = 0; j < (b.Ecke == null ? 1 : 2); j++)
            {
                var x = a.Ecke == null ? a : i == 0 ? a.Ecke.Eins : a.Ecke.Zwei;
                var y = b.Ecke == null ? b : j == 0 ? b.Ecke.Eins : b.Ecke.Zwei;
                if (!x.Vorhanden || !y.Vorhanden) continue;
                var e = K.Verfolge(x, y, kanten, knoten, d, innen);
                if (e.Grund == K.Grund.EckeAndereStrasse || e.Grund == K.Grund.Kreuzung) { fehler ??= e; continue; }
                if (!e.Folgt && e.Grund != K.Grund.ZuKurz && e.Grund != K.Grund.Nullsehne)
                { fehler = e; continue; }
                if (!kanten.TryGetValue(x.Kante, out var kx) || !kanten.TryGetValue(y.Kante, out var ky)) continue;
                if ((a.Ecke != null && !Anlauf(a, x, kx)) || (b.Ecke != null && !Anlauf(b, y, ky)))
                { fehler = new K.Ergebnis { Grund = K.Grund.EckenTangente }; continue; }
                var kurven = new List<K.Bezier>();
                void Gerade(float3 von, float3 bis)
                {
                    if (math.distance(von, bis) > K.Fangabstand)
                        kurven.Add(new K.Bezier(von, math.lerp(von, bis, 1f / 3), math.lerp(von, bis, 2f / 3), bis));
                }
                if (a.Ecke != null) Gerade(a.Punkt, x.Punkt);
                if (e.Verlauf != null) kurven.AddRange(e.Verlauf);
                if (b.Ecke != null) Gerade(y.Punkt, b.Punkt);
                var ganz = K.Einfrieren(1, kurven, innenversatz: d, innenseite: innen);
                if (!ganz.Folgt) { fehler = ganz; continue; }
                if (fund != null) return new K.Ergebnis { Grund = K.Grund.Mehrdeutig };
                fund = ganz;
            }
            return fund ?? fehler ?? new K.Ergebnis { Grund = K.Grund.EckeAndereStrasse };
        }
    }
}
