using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Zahlenformat fuer einen eigenen optionalen Bauzettel-Puffer. Ohne den
     * Puffer bleiben alte Kurven reine Sehnenpolygone. Die ECS-Anbindung kann
     * diese Leser/Schreiber direkt mit IReader/IWriter verbinden.
     */
    public static class KurvenRandlaufFormat
    {
        public const int Version = 1;
        public const int HoechstensLaeufe = 1024;
        public const int HoechstensKontrollpunkte = 16384;

        public static void Schreiben(KurvenRandlauf[] laeufe, Action<int> zahl, Action<float3> punkt)
        {
            laeufe = laeufe ?? Array.Empty<KurvenRandlauf>();
            PruefeAnzahl(laeufe.Length, HoechstensLaeufe);
            foreach (var lauf in laeufe) Pruefe(lauf);
            zahl(Version);
            zahl(laeufe.Length);
            foreach (var lauf in laeufe)
            {
                zahl(lauf.Von); zahl(lauf.Kanten); zahl(lauf.Kontrollpunkte.Length);
                foreach (var p in lauf.Kontrollpunkte) punkt(p);
            }
        }

        public static KurvenRandlauf[] Lesen(Func<int> zahl, Func<float3> punkt)
        {
            int version = zahl();
            if (version != Version) throw new InvalidOperationException("Unbekanntes Kurvenrandformat.");
            int anzahl = zahl(); PruefeAnzahl(anzahl, HoechstensLaeufe);
            var ausgabe = new KurvenRandlauf[anzahl];
            for (int i = 0; i < anzahl; i++)
            {
                var lauf = new KurvenRandlauf { Von = zahl(), Kanten = zahl() };
                int punkte = zahl(); PruefeAnzahl(punkte, HoechstensKontrollpunkte);
                lauf.Kontrollpunkte = new float3[punkte];
                for (int j = 0; j < punkte; j++) lauf.Kontrollpunkte[j] = punkt();
                Pruefe(lauf);
                ausgabe[i] = lauf;
            }
            return ausgabe;
        }

        private static void PruefeAnzahl(int anzahl, int hoechstens)
        {
            if (anzahl < 0 || anzahl > hoechstens) throw new ArgumentException("Ungueltige Kurvenrand-Feldzahl.");
        }

        private static void Pruefe(KurvenRandlauf lauf)
        {
            if (lauf == null || lauf.Von < 0 || lauf.Kanten < 1 || lauf.Kontrollpunkte == null
                || lauf.Kontrollpunkte.Length < 4 || lauf.Kontrollpunkte.Length % 4 != 0)
                throw new ArgumentException("Ungueltiger Kurvenrandlauf.");
            PruefeAnzahl(lauf.Kontrollpunkte.Length, HoechstensKontrollpunkte);
            foreach (var p in lauf.Kontrollpunkte)
                if (!math.all(math.isfinite(p))) throw new ArgumentException("Nicht endlicher Kurvenrandpunkt.");
        }
    }
}
