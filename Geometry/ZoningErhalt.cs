using System;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class ZoningErhalt
    {
        // Nur identische gerichtete Kurse erhalten: ein Richtungswechsel
        // vertauscht die Seiten des Zonenblocks. Reihenfolge ist bedeutungslos.
        public static bool Gleich(float2[][] vorher, float2[][] nachher,
            string altesPrefab, string neuesPrefab)
        {
            if (vorher == null || nachher == null || vorher.Length == 0
                || vorher.Length != nachher.Length || altesPrefab != neuesPrefab) return false;
            var benutzt = new bool[nachher.Length];
            foreach (var a in vorher)
            {
                bool gefunden = false;
                for (int i = 0; i < nachher.Length; i++)
                {
                    var b = nachher[i];
                    if (benutzt[i] || math.distancesq(a[0], b[0]) > .000001f
                        || math.distancesq(a[1], b[1]) > .000001f) continue;
                    benutzt[i] = true; gefunden = true; break;
                }
                if (!gefunden) return false;
            }
            return true;
        }
    }
}
