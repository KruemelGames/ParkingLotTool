using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Eindeutige Quellmerkmale, keine Vereinnahmung allein nach Naehe. */
    public static class Erzeugungsherkunft
    {
        public static bool Quelle(bool prefab, int seed, int erwartet, bool alt, bool verworfen)
            => prefab && seed == erwartet && !alt && !verworfen;

        public static bool BushalteVollstaendig(IReadOnlyList<float2> geplant, IReadOnlyList<float2> vorhanden)
        {
            var zuordnung = new int[vorhanden.Count];
            for (int i = 0; i < zuordnung.Length; i++) zuordnung[i] = -1;
            bool Ordne(int plan, HashSet<int> besucht)
            {
                for (int i = 0; i < vorhanden.Count; i++)
                {
                    // CS2 setzt Schilder nach dem Erzeugen um (gemessen 0,70 m).
                    if (!(math.distance(geplant[plan], vorhanden[i]) < 2f) || !besucht.Add(i)) continue;
                    if (zuordnung[i] < 0 || Ordne(zuordnung[i], besucht))
                    { zuordnung[i] = plan; return true; }
                }
                return false;
            }
            for (int i = 0; i < geplant.Count; i++)
                if (!Ordne(i, new HashSet<int>())) return false;
            return true;
        }

        public static bool Leitungsstueck(float3 a, float3 d, float3 von, float3 bis)
        {
            // Senkrechte AuxiliaryNet-Stuecke liegen an einem Quellende.
            if (math.distance(a.xz, d.xz) < 1f && math.abs(a.y - d.y) > .5f)
                return math.distance(a.xz, von.xz) < .1f || math.distance(a.xz, bis.xz) < .1f;
            float laenge = math.distance(von.xz, bis.xz);
            if (laenge < .001f) return false;
            float2 richtung = (bis.xz - von.xz) / laenge;
            bool AufStrecke(float3 p)
            {
                float t = math.dot(p.xz - von.xz, richtung);
                return t >= -.1f && t <= laenge + .1f
                    && math.distance(p.xz, von.xz + math.clamp(t, 0f, laenge) * richtung) < .1f;
            }
            return AufStrecke(a) && AufStrecke(d);
        }
    }
}
