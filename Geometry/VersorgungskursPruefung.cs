using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Kursabdeckung statt Trefferzahl: 1 von 4 Leitungen durfte am
    // 05.09.2026 im alten Pruefer bereits den gesamten Bau freigeben.
    internal static class VersorgungskursPruefung
    {
        internal const float Toleranz = 0.1f;

        internal static bool Anschluss(float achsabstand, float strassenbreite,
            float leitungsbreite, float suchweite, bool ebenen)
            => ebenen && achsabstand <= strassenbreite / 2
                + math.max(0, leitungsbreite / 2 + suchweite) + 0.001f;

        internal static float Achsabstand(float strombreite, float wasserbreite)
            => math.max(3f, (strombreite + wasserbreite) * 0.5f + 0.25f);

        internal static bool Abschnitt(float2 start, float2 ende,
            float2 a, float2 b, float2 c, float2 d, out float2 intervall)
        {
            intervall = default;
            var v = ende - start;
            var laenge = math.length(v);
            if (laenge < 1f) return false;
            var richtung = v / laenge;
            foreach (var punkt in new[] { a, b, c, d })
            {
                var entlang = math.dot(punkt - start, richtung);
                if (entlang < -Toleranz || entlang > laenge + Toleranz
                    || math.distance(punkt, start + entlang * richtung) > Toleranz)
                    return false;
            }
            var x = math.dot(a - start, richtung);
            var y = math.dot(d - start, richtung);
            intervall = new float2(math.min(x, y), math.max(x, y));
            return intervall.y - intervall.x > Toleranz;
        }

        internal static bool Vollstaendig(float laenge, List<float2> abschnitte)
        {
            if (abschnitte.Count == 0 || laenge < 1f) return false;
            abschnitte.Sort((a, b) => a.x.CompareTo(b.x));
            var erreicht = 0f;
            foreach (var abschnitt in abschnitte)
            {
                if (abschnitt.x > erreicht + Toleranz) return false;
                erreicht = math.max(erreicht, abschnitt.y);
            }
            return erreicht + Toleranz >= laenge;
        }

        internal static bool Zusammenhaengend(List<int2> kanten, int start, int ende)
        {
            if (kanten.Count == 0 || start < 0 || ende < 0 || start == ende) return false;
            var erreicht = new HashSet<int> { start };
            for (var i = 0; i < kanten.Count; i++)
            {
                var neu = false;
                foreach (var k in kanten)
                    if (erreicht.Contains(k.x) || erreicht.Contains(k.y))
                    { neu |= erreicht.Add(k.x); neu |= erreicht.Add(k.y); }
                if (!neu) break;
            }
            if (!erreicht.Contains(ende)) return false;
            foreach (var k in kanten)
                if (!erreicht.Contains(k.x) || !erreicht.Contains(k.y)) return false;
            return true;
        }
    }
}
