using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Vorschauentscheidung und reine Dreiecksdifferenz; keine ECS-/Werkzeugabhaengigkeit. */
    public static class OberflaechenMuster
    {
        public const float Kantenlaenge = 40f;
        public enum Aktion { Keine, Entfernen, Behalten, Anlegen }

        public static bool DarfZeigen(bool an, bool fenster, bool punkt, bool material,
            bool werkzeug, bool bauphase, bool bauarbeiter, bool speichern, bool andereArbeit)
            => an && fenster && punkt && material && werkzeug && !bauphase
                && !bauarbeiter && !speichern && !andereArbeit;

        public static Aktion Entscheide(bool zeigen, int muster, bool ausgabeOffen, bool gleich)
        {
            if (!zeigen) return muster > 0 || ausgabeOffen ? Aktion.Entfernen : Aktion.Keine;
            if (muster > 1) return Aktion.Entfernen;
            if (gleich && (muster == 1 || ausgabeOffen)) return Aktion.Behalten;
            return Aktion.Anlegen;
        }

        public static float2[] Quadrat(float2 mitte)
        {
            if (!math.all(math.isfinite(mitte))) throw new ArgumentException("Ungueltige Mustermitte.");
            float h = Kantenlaenge * .5f;
            return new[] { mitte + new float2(-h,-h), mitte + new float2(h,-h),
                mitte + new float2(h,h), mitte + new float2(-h,h) };
        }

        /** Konvexe Dreiecke einzeln schneiden: keine Lochringe oder konkaven Restpolygone. */
        public static float2[][] Ausserhalb(float2 a, float2 b, float2 c, float2 mitte)
        {
            if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b))
                || !math.all(math.isfinite(c)) || !math.all(math.isfinite(mitte)))
                throw new ArgumentException("Nicht endliche Musterkoordinate.");
            float h = Kantenlaenge * .5f;
            float2 min = math.min(a,math.min(b,c)), max = math.max(a,math.max(b,c));
            if (max.x <= mitte.x-h || min.x >= mitte.x+h || max.y <= mitte.y-h || min.y >= mitte.y+h)
                return new[] { new[] { a,b,c } };
            if (min.x >= mitte.x-h && max.x <= mitte.x+h && min.y >= mitte.y-h && max.y <= mitte.y+h)
                return Array.Empty<float2[]>();
            var rest = new List<float2> { a,b,c };
            var result = new List<float2[]>();
            // Vier disjunkte Reststreifen; das immer kleinere Innenpolygon geht weiter.
            for (int seite = 0; seite < 4 && rest.Count >= 3; seite++)
            {
                int achse = seite / 2;
                float grenze = mitte[achse] + (seite % 2 == 0 ? -h : h);
                int vorzeichen = seite % 2 == 0 ? 1 : -1;
                var draussen = Halbebene(rest,achse,grenze,-vorzeichen);
                for (int i = 1; i+1 < draussen.Count; i++)
                {
                    var p=draussen[0]; var q=draussen[i]; var r=draussen[i+1];
                    double f = (q.x-(double)p.x)*(r.y-(double)p.y) - (q.y-(double)p.y)*(r.x-(double)p.x);
                    if (Math.Abs(f)>1e-12) result.Add(new[] { p,q,r });
                }
                rest = Halbebene(rest,achse,grenze,vorzeichen);
            }
            return result.ToArray();
        }

        private static List<float2> Halbebene(List<float2> ring, int achse, float grenze, int vorzeichen)
        {
            var result = new List<float2>();
            var a = ring[ring.Count-1];
            double da = (a[achse]-(double)grenze)*vorzeichen;
            foreach (var b in ring)
            {
                double db = (b[achse]-(double)grenze)*vorzeichen;
                if ((da < 0) != (db < 0))
                {
                    double t = da/(da-db);
                    var p = new float2((float)(a.x+(b.x-(double)a.x)*t), (float)(a.y+(b.y-(double)a.y)*t));
                    p[achse] = grenze;
                    result.Add(p);
                }
                if (db >= 0) result.Add(b);
                a=b; da=db;
            }
            return result;
        }
    }
}
