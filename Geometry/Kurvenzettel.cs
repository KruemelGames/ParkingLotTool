using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * WAS VON EINER STRASSENKURVE IM BAUZETTEL STEHT (Etappe 4, 19.10.2026).
     *
     * Die Formel: die Bordstein-Abschnitte als kubische Beziers (je vier
     * Kontrollpunkte, in Richtung der Kante geordnet). Daraus laesst sich die
     * Kruemmung an jeder Stelle nachrechnen - auch S-Kurven und beliebige
     * Groessen, ohne Sonderfall. Dazu eine Zusammenfassung zum schnellen
     * Lesen und die Grenzwerte, mit denen die gebauten Zwischenpunkte
     * entstanden sind.
     *
     * Nur Nachweis: Laden, Edit, Sync und Repair rechnen nie neu ab. Die
     * gebauten Punkte selbst stehen wie jeder Formpunkt im Punktpuffer;
     * hier steht nur, welche davon zwischen `Von` und `Bis` zur Kurve
     * gehoeren, und wo sie auf dem Bogen liegen. Keine Entity, kein Prefab.
     */
    public sealed class Kurvenzettel
    {
        public float2 Von, Bis;
        public int Zwischen;
        /** Je Abschnitt vier Kontrollpunkte A, B, C, D hintereinander. */
        public float3[] Abschnitte = Array.Empty<float3>();
        /** Bogenlaenge (m) jedes Zwischenpunkts ab `Von`. */
        public float[] Bogen = Array.Empty<float>();
        public float Laenge, RadiusMin, Winkel, Pfeilhoehe;
        public int Wendepunkte;
        public float Toleranz, MinSehne, MaxSehne;

        public int AbschnittAnzahl => Abschnitte.Length / 4;

        /**
         * Dieselbe Kurve mit verschobenen Enden (Kurve als Ganzes versetzt).
         * Formel und Stationen beschreiben weiter den Bordstein; `Von`/`Bis`
         * muessen zu den Formpunkten passen, sonst findet Edit sie nicht.
         */
        public Kurvenzettel MitEnden(float2 von, float2 bis)
        {
            var z = (Kurvenzettel)MemberwiseClone();
            z.Von = von;
            z.Bis = bis;
            return z;
        }

        /** Dieselbe Kurve mit anderem Punktlauf (gekuerzt oder wieder verlaengert). */
        public Kurvenzettel MitPunkten(float2 von, float2 bis, float[] bogen)
        {
            var z = MitEnden(von, bis);
            z.Zwischen = bogen.Length;
            z.Bogen = bogen;
            return z;
        }

        /** Wie `MitEnden`, nur fehlen vorn bzw. hinten Zwischenpunkte (abgeschnitten). */
        public Kurvenzettel Gekuerzt(float2 von, float2 bis, int vorn, int hinten)
        {
            var z = MitEnden(von, bis);
            z.Zwischen = Math.Max(0, Zwischen - vorn - hinten);
            if (Bogen.Length == Zwischen)
            {
                z.Bogen = new float[z.Zwischen];
                Array.Copy(Bogen, vorn, z.Bogen, 0, z.Zwischen);
            }
            return z;
        }

        public static Kurvenzettel Aus(Strassenkurve.Ergebnis e, float2 von, float2 bis)
        {
            if (e == null || e.Punkte == null || e.Punkte.Length < 3) return null;
            var abschnitte = new List<float3>();
            if (e.Verlauf != null)
                foreach (var c in e.Verlauf)
                {
                    abschnitte.Add(c.A); abschnitte.Add(c.B);
                    abschnitte.Add(c.C); abschnitte.Add(c.D);
                }
            int zwischen = e.Punkte.Length - 2;
            var bogen = new float[zwischen];
            var stellen = e.Gruppe?.Bogenlaengen;
            for (int i = 0; i < zwischen; i++)
                bogen[i] = stellen != null && i + 1 < stellen.Count ? (float)stellen[i + 1] : float.NaN;
            var zettel = new Kurvenzettel
            {
                Von = von,
                Bis = bis,
                Zwischen = zwischen,
                Abschnitte = abschnitte.ToArray(),
                Bogen = bogen,
                Laenge = (float)e.Laenge,
                RadiusMin = (float)e.Radius,
                Winkel = (float)e.Winkel,
                Pfeilhoehe = (float)e.Abweichung,
                Toleranz = (float)Strassenkurve.Pfeilhoehe,
                MinSehne = (float)Strassenkurve.Mindestsehne,
                MaxSehne = (float)Strassenkurve.Hoechstsehne,
            };
            zettel.Wendepunkte = ZaehleWendepunkte(zettel.Abschnitte);
            return zettel;
        }

        /**
         * Wendepunkte = Vorzeichenwechsel der Kruemmung entlang aller
         * Abschnitte. 0 ist ein einfacher Bogen, ab 1 eine S-Kurve. Ein
         * Kruemmungslauf zaehlt nur, wenn er die Richtung um mindestens
         * `Mindestdrehung` dreht: float-Rauschen auf fast geraden Stuecken
         * (Weltkoordinaten um 1000 m) erzeugte sonst in einem einfachen
         * 88-Grad-Bogen drei Wendepunkte (Spiel 19.10.).
         */
        public const double Mindestdrehung = Math.PI / 180;

        public static int ZaehleWendepunkte(IReadOnlyList<float3> abschnitte)
        {
            if (abschnitte == null) return 0;
            const int proben = 32;
            int lauf = 0, letzterEchter = 0, wechsel = 0;
            double drehung = 0;
            void Schliesse()
            {
                if (lauf == 0 || Math.Abs(drehung) < Mindestdrehung) return;
                if (letzterEchter != 0 && lauf != letzterEchter) wechsel++;
                letzterEchter = lauf;
            }
            for (int s = 0; s + 3 < abschnitte.Count; s += 4)
            {
                double2 a = abschnitte[s].xz, b = abschnitte[s + 1].xz,
                        c = abschnitte[s + 2].xz, d = abschnitte[s + 3].xz;
                // Mittelpunktregel: jede Probe steht fuer 1/proben des Abschnitts.
                for (int i = 0; i < proben; i++)
                {
                    double t = (i + .5) / proben, u = 1 - t;
                    var d1 = 3 * (u * u * (b - a) + 2 * u * t * (c - b) + t * t * (d - c));
                    var d2 = 6 * (u * (c - 2 * b + a) + t * (d - 2 * c + b));
                    double laenge = math.length(d1);
                    if (laenge < 1e-9) continue;
                    double kappa = (d1.x * d2.y - d1.y * d2.x) / (laenge * laenge * laenge);
                    if (kappa == 0) continue;
                    int jetzt = kappa > 0 ? 1 : -1;
                    if (jetzt != lauf) { Schliesse(); lauf = jetzt; drehung = 0; }
                    drehung += kappa * laenge / proben;
                }
            }
            Schliesse();
            return wechsel;
        }

        /** Eine kurze Logzeile: Laenge, kleinster Radius, Winkel, Wendepunkte, Stuecke. */
        public string Kurzzeile()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Format(c, "{0:F1} m, R min {1:F1} m, {2:F1} Grad, {3} Wendepunkt(e), {4} Stuecke, {5} Abschnitt(e)",
                Laenge, RadiusMin, Winkel * 180 / Math.PI, Wendepunkte, Zwischen + 1, AbschnittAnzahl);
        }

        /**
         * Findet die Zwischenpunkte dieser Kurve in einem Punktzug wieder:
         * `Von`, dann `Zwischen` Punkte, dann `Bis` (auch ueber das Ende des
         * Rings hinweg und in Gegenrichtung). Passt es nicht exakt, gibt es
         * nichts - lieber Griffe an jedem Punkt als eine falsche Zuordnung.
         */
        public bool FindeZwischenpunkte(IReadOnlyList<float2> punkte, out float2[] zwischen)
        {
            zwischen = null;
            if (punkte == null || Zwischen <= 0 || punkte.Count < Zwischen + 2) return false;
            int n = punkte.Count;
            float2[] beste = null;
            double besterAbstand = double.PositiveInfinity;
            int treffer = 0;
            for (int start = 0; start < n; start++)
            {
                if (!math.all(punkte[start] == Von)) continue;
                for (int richtung = 1; richtung >= -1; richtung -= 2)
                {
                    int ende = ((start + richtung * (Zwischen + 1)) % n + n) % n;
                    if (!math.all(punkte[ende] == Bis)) continue;
                    var gefunden = new float2[Zwischen];
                    for (int i = 0; i < Zwischen; i++)
                        gefunden[i] = punkte[((start + richtung * (i + 1)) % n + n) % n];
                    treffer++;
                    double abstand = AbstandZurFormel(gefunden);
                    if (abstand < besterAbstand) { besterAbstand = abstand; beste = gefunden; }
                }
            }
            if (beste == null) return false;
            // Ein Ring hat zwei Wege von Von nach Bis. Gleich viele Punkte auf
            // beiden: nur die Formel entscheidet - der richtige Weg liegt auf ihr.
            if (treffer > 1 && !(besterAbstand <= Toleranz + 0.05)) return false;
            zwischen = beste;
            return true;
        }

        /**
         * DIE KURVENLAEUFE FUER DAS LAYOUT (Etappe 6, 20.10.).
         *
         * Abgeleitet aus den gespeicherten Kurvenzetteln statt aus einem
         * eigenen Speicherpuffer: Vorschau, Bau, Edit und Hintergrund-Sync
         * haben dieselben Zettel und dasselbe Polygon, also dieselben Laeufe.
         * Vertrag: `LayoutSettings.Kurvenlaeufe` (BERICHT-KURVEN-10) - `Von`
         * ist der Index im uebergebenen Polygon, `Kanten` laeuft in
         * Polygonrichtung, die Formel in derselben Richtung.
         *
         * Ein Lauf entsteht nur, wenn die Formel noch zu den Formpunkten
         * passt (Enden bis 1 cm). Eine versetzte oder gekuerzte Kurve
         * beschreibt nicht mehr ihren Bordstein - sie bleibt ohne Lauf und
         * bekommt die Randreihe wie jede andere Kante.
         */
        public static KurvenRandlauf[] Laeufe(IReadOnlyList<float2> polygon, IReadOnlyList<Kurvenzettel> zettel)
        {
            if (polygon == null || zettel == null || zettel.Count == 0) return Array.Empty<KurvenRandlauf>();
            int n = polygon.Count;
            var laeufe = new List<KurvenRandlauf>();
            var belegt = new bool[n];
            foreach (var z in zettel)
            {
                if (z == null || z.AbschnittAnzahl == 0 || z.Abschnitte.Length % 4 != 0) continue;
                var abschnitte = z.Abschnitte;
                // Gekuerzt oder wieder verlaengert (Kantenzug, Schliesskante): die Kurve liegt
                // noch auf demselben Bordstein, nur die Formel ist laenger - zuschneiden
                // (Nutzer 20.10.: sonst kam die alte Randreihe je Sehne zurueck).
                if (math.distance(abschnitte[0].xz, z.Von) > .01f
                    || math.distance(abschnitte[abschnitte.Length - 1].xz, z.Bis) > .01f)
                    abschnitte = Zuschneiden(abschnitte, z.Von, z.Bis, Strassenkurve.Pfeilhoehe + .05);
                if (abschnitte == null) continue;
                if (!z.FindeZwischenpunkte(polygon, out var zwischen)) continue;
                int von = -1, richtung = 0;
                for (int i = 0; i < n && richtung == 0; i++)
                {
                    if (!math.all(polygon[i] == z.Von)) continue;
                    if (math.all(polygon[(i + 1) % n] == zwischen[0])) { von = i; richtung = 1; }
                    else if (math.all(polygon[(i - 1 + n) % n] == zwischen[0])) { von = i; richtung = -1; }
                }
                if (richtung == 0) continue;
                int kanten = z.Zwischen + 1;
                var kontrollpunkte = (float3[])abschnitte.Clone();
                if (richtung < 0)
                {
                    // In Polygonrichtung beginnt der Lauf am Bis-Ende; die Formel dreht mit.
                    von = ((von - kanten) % n + n) % n;
                    Array.Reverse(kontrollpunkte);
                }
                bool frei = true;
                for (int k = 0; k < kanten; k++) frei &= !belegt[(von + k) % n];
                if (!frei) continue;
                for (int k = 0; k < kanten; k++) belegt[(von + k) % n] = true;
                laeufe.Add(new KurvenRandlauf { Von = von, Kanten = kanten, Kontrollpunkte = kontrollpunkte });
            }
            return laeufe.ToArray();
        }

        /**
         * Die Formel zwischen den Lotpunkten von `von` und `bis` (XZ), in ihrer
         * eigenen Richtung. Liegt ein Ende weiter als `grenze` neben der Formel
         * (versetzte Kurve) oder kaeme `bis` vor `von`, gibt es nichts. Die
         * Enden werden genau auf die Formpunkte gelegt; das Innere bleibt die
         * Bordsteinformel.
         */
        public static float3[] Zuschneiden(float3[] abschnitte, float2 von, float2 bis, double grenze)
        {
            if (abschnitte == null || abschnitte.Length < 4 || abschnitte.Length % 4 != 0) return null;
            if (!Lot(abschnitte, von, grenze, out int sv, out double tv)
                || !Lot(abschnitte, bis, grenze, out int sb, out double tb)) return null;
            if (sb < sv || (sb == sv && tb <= tv)) return null;
            var aus = new List<float3>();
            for (int s = sv; s <= sb; s++)
            {
                double a = s == sv ? tv : 0, b = s == sb ? tb : 1;
                if (b - a < 1e-9) continue;
                aus.AddRange(Teilstueck(abschnitte, s, a, b));
            }
            if (aus.Count < 4) return null;
            // Enden exakt auf die Formpunkte; die Tangentenpunkte wandern mit.
            var dv = new float3(von.x, aus[0].y, von.y) - aus[0];
            aus[0] += dv; aus[1] += dv;
            int e = aus.Count - 1;
            var db = new float3(bis.x, aus[e].y, bis.y) - aus[e];
            aus[e] += db; aus[e - 1] += db;
            return aus.ToArray();
        }

        private static double3 Punkt(float3[] k, int s, double t)
        {
            double3 a = k[4 * s], b = k[4 * s + 1], c = k[4 * s + 2], d = k[4 * s + 3];
            double u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        private static bool Lot(float3[] k, float2 p, double grenze, out int abschnitt, out double t)
        {
            abschnitt = -1; t = 0;
            double besterAbstand = double.PositiveInfinity;
            var q = new double2(p.x, p.y);
            for (int s = 0; s < k.Length / 4; s++)
            {
                const int proben = 64;
                int beste = 0; double nah = double.PositiveInfinity;
                for (int i = 0; i <= proben; i++)
                {
                    double d = math.distance(Punkt(k, s, i / (double)proben).xz, q);
                    if (d < nah) { nah = d; beste = i; }
                }
                double lo = Math.Max(0, (beste - 1) / (double)proben), hi = Math.Min(1, (beste + 1) / (double)proben);
                for (int n = 0; n < 50; n++)
                {
                    double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                    if (math.distance(Punkt(k, s, m1).xz, q) < math.distance(Punkt(k, s, m2).xz, q)) hi = m2; else lo = m1;
                }
                double tt = (lo + hi) / 2, dd = math.distance(Punkt(k, s, tt).xz, q);
                if (dd < besterAbstand) { besterAbstand = dd; abschnitt = s; t = tt; }
            }
            return abschnitt >= 0 && besterAbstand <= grenze;
        }

        // Kubischer Abschnitt `s` zwischen den Parametern a und b (de Casteljau, double).
        private static float3[] Teilstueck(float3[] k, int s, double a, double b)
        {
            double3 p0 = k[4 * s], p1 = k[4 * s + 1], p2 = k[4 * s + 2], p3 = k[4 * s + 3];
            void Teile(double t, ref double3 q0, ref double3 q1, ref double3 q2, ref double3 q3, bool rechts)
            {
                var ab = math.lerp(q0, q1, t); var bc = math.lerp(q1, q2, t); var cd = math.lerp(q2, q3, t);
                var abc = math.lerp(ab, bc, t); var bcd = math.lerp(bc, cd, t); var m = math.lerp(abc, bcd, t);
                if (rechts) { q0 = m; q1 = bcd; q2 = cd; }
                else { q1 = ab; q2 = abc; q3 = m; }
            }
            if (b < 1) Teile(b, ref p0, ref p1, ref p2, ref p3, false);
            if (a > 0) Teile(a / b, ref p0, ref p1, ref p2, ref p3, true);
            return new[] { (float3)p0, (float3)p1, (float3)p2, (float3)p3 };
        }

        // Groesster Abstand der Punkte zur abgetasteten Formel (XZ); ohne Formel unendlich.
        private double AbstandZurFormel(float2[] punkte)
        {
            if (AbschnittAnzahl == 0) return double.PositiveInfinity;
            double groesster = 0;
            foreach (var p in punkte)
            {
                double naechster = double.PositiveInfinity;
                for (int s = 0; s + 3 < Abschnitte.Length; s += 4)
                {
                    float2 a = Abschnitte[s].xz, b = Abschnitte[s + 1].xz, c = Abschnitte[s + 2].xz, d = Abschnitte[s + 3].xz;
                    for (int i = 0; i <= 64; i++)
                    {
                        float t = i / 64f, u = 1 - t;
                        var q = u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
                        naechster = Math.Min(naechster, math.distance(p, q));
                    }
                }
                groesster = Math.Max(groesster, naechster);
            }
            return groesster;
        }
    }
}
