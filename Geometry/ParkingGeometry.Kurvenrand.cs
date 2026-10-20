using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Ein explizit gesetzter Lauf: Kante Von bis Von + Kanten - 1, modulo
     * Punktzahl, in Polygonrichtung. Je vier Kontrollpunkte bilden einen
     * kubischen Bordsteinabschnitt in derselben Richtung. Keine Entities.
     * Die schon gespeicherte Kurvenformel liefert diese Daten auch im Edit.
     */
    public sealed class KurvenRandlauf
    {
        public int Von { get; set; }
        public int Kanten { get; set; }
        public float3[] Kontrollpunkte { get; set; } = Array.Empty<float3>();

        public KurvenRandlauf Clone() => new KurvenRandlauf
        {
            Von = Von, Kanten = Kanten,
            Kontrollpunkte = Kontrollpunkte?.ToArray() ?? Array.Empty<float3>(),
        };

        public bool InhaltGleich(KurvenRandlauf anderer)
            => anderer != null && Von == anderer.Von && Kanten == anderer.Kanten
                && (Kontrollpunkte ?? Array.Empty<float3>())
                    .SequenceEqual(anderer.Kontrollpunkte ?? Array.Empty<float3>());
    }

    /** Abgeleitete Standlinie der Laternen; Stationen messen ihren Bogen. */
    public sealed class KurvenRandreihe
    {
        public int[] Buchten { get; internal set; } = Array.Empty<int>();
        public float2[] Standlinie { get; internal set; } = Array.Empty<float2>();
        public float2[] Innenrichtungen { get; internal set; } = Array.Empty<float2>();
        public double[] Stationen { get; internal set; } = Array.Empty<double>();
        /** Gesperrte Bogenintervalle, vor allem an Zufahrten. */
        public double2[] Sperren { get; internal set; } = Array.Empty<double2>();
    }

    public static partial class ParkingGeometry
    {
        private static KurvenRandlauf[] ZellenKurvenlaeufe(
            float2[] original, LayoutSettings settings)
        {
            if (settings.Kurvenlaeufe == null || settings.Kurvenlaeufe.Length == 0)
                return Array.Empty<KurvenRandlauf>();
            double flaeche = 0;
            for (int i = 0; i < original.Length; i++)
            {
                var a = original[i] - original[0];
                var b = original[(i + 1) % original.Length] - original[0];
                flaeche += (double)a.x * b.y - (double)a.y * b.x;
            }
            int n = original.Length;
            var belegt = new bool[n];
            var ausgabe = new List<KurvenRandlauf>();
            foreach (var lauf in settings.Kurvenlaeufe)
            {
                if (lauf == null || lauf.Von < 0 || lauf.Von >= n
                    || lauf.Kanten < 1 || lauf.Kanten >= n
                    || lauf.Kontrollpunkte == null || lauf.Kontrollpunkte.Length == 0
                    || lauf.Kontrollpunkte.Length % 4 != 0
                    || lauf.Kontrollpunkte.Any(p => !math.all(math.isfinite(p))))
                    throw new ArgumentException("Ungueltiger expliziter Kurvenlauf.");
                int ende = (lauf.Von + lauf.Kanten) % n;
                if (math.distance(lauf.Kontrollpunkte[0].xz, original[lauf.Von]) > 0.01f
                    || math.distance(lauf.Kontrollpunkte[lauf.Kontrollpunkte.Length - 1].xz,
                        original[ende]) > 0.01f)
                    throw new ArgumentException("Kurvenformel und Polygonenden passen nicht zusammen.");
                for (int i = 0; i < lauf.Kanten; i++)
                {
                    int k = (lauf.Von + i) % n;
                    if (belegt[k]) throw new ArgumentException("Kurvenlaeufe ueberlappen sich.");
                    belegt[k] = true;
                }
                var kopie = lauf.Clone();
                if (flaeche < 0)
                {
                    kopie.Von = n - 1 - ende;
                    Array.Reverse(kopie.Kontrollpunkte);
                }
                ausgabe.Add(kopie);
            }
            return ausgabe.ToArray();
        }
    }
}
