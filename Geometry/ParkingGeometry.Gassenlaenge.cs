using System;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * Teilt nur den Netzkurs. Die Fangachse und ihre Endpunkte bleiben
         * erhalten; bei den acht Nutzerkanten werden aus 8 genau 14 Kurse.
         * Die Zoningstrasse darf diesen Weg nicht benutzen: ihr Schnitt
         * kostet Kachelreihen, die Zufahrtsgasse hat 0 ZoneBlockPrefabs.
         */
        public static NetSegment[] TeileGassenkurs(NetSegment kurs,
            float hoechstlaenge = 16f)
        {
            if (!math.all(math.isfinite(kurs.A))
                || !math.all(math.isfinite(kurs.B))
                || !math.isfinite(hoechstlaenge) || hoechstlaenge <= 0f)
                throw new ArgumentException("Gassenkurs und Grenze muessen endlich sein.");

            var laenge = math.distance(kurs.A, kurs.B);
            if (laenge <= hoechstlaenge)
                return new[] { kurs };

            var anzahl = (int)Math.Ceiling((double)laenge / hoechstlaenge);
            while (true)
            {
                var teile = new NetSegment[anzahl];
                var vorher = kurs.A;
                var zuLang = false;
                for (var i = 0; i < anzahl; i++)
                {
                    var nachher = i + 1 == anzahl
                        ? kurs.B
                        : math.lerp(kurs.A, kurs.B, (float)(i + 1) / anzahl);
                    teile[i] = new NetSegment(kurs.Kind, vorher, nachher, kurs.Art);
                    if (math.distance(vorher, nachher) > hoechstlaenge)
                        zuLang = true;
                    vorher = nachher;
                }
                if (!zuLang) return teile;
                anzahl++;
            }
        }
    }
}
