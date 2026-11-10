using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Eingabekurse fuer CS2s echtes Pathway-Netz. Der Kern liefert den
     * geschnittenen Belegungsgraphen; CourseSplit bekommt ganze Geraden
     * wie beim Vanilla-NetTool. TwoWayBikePath hat IntersectLayers Pathway
     * und MarkerPathway (sonde-1.log:4167), anders als die Auto-Markerwege.
     */
    public static class FahrradBaukurse
    {
        public static float2 Zugangsrichtung(Fahrradweg w, float2[] polygon, LayoutSettings settings)
        {
            var normal = math.normalizesafe(w.A-w.B);
            foreach(var e in settings?.Entrances ?? Array.Empty<Entrance>())
            {
                if(e?.AxisDirection==null || polygon==null || e.Edge<0 || e.Edge>=polygon.Length)continue;
                var a=polygon[e.Edge];var b=polygon[(e.Edge+1)%polygon.Length];
                var p=a+math.normalizesafe(b-a)*(float)e.Along;
                if(math.distance(p,w.A)>.05f)continue;
                var richtung=math.normalizesafe(e.AxisDirection.Value,normal);
                return math.dot(richtung,normal)<0 ? -richtung : richtung;
            }
            return normal;
        }

        public static Fahrradweg[] Plan(FahrradPlan plan)
        {
            var kurse = plan.Wege.Where(w => w.Art == Fahrradwegart.Fahrradweg)
                .Select(w => new Fahrradweg { A = w.A, B = w.B, Breite = w.Breite,
                    Art = w.Art, Zufahrt = w.Zufahrt }).ToList();
            for (var i = 0; i < kurse.Count; i++)
            {
                for (var j = i + 1; j < kurse.Count; j++)
                {
                    var a = kurse[i]; var b = kurse[j];
                    if (a.Zufahrt != b.Zufahrt || Math.Abs(a.Breite - b.Breite) > .001) continue;
                    float2 start, ende;
                    if (math.distance(a.B, b.A) < .002) { start = a.A; ende = b.B; }
                    else if (math.distance(a.A, b.B) < .002) { start = b.A; ende = a.B; }
                    else if (!a.Zufahrt && math.distance(a.A, b.A) < .002) { start = b.B; ende = a.B; }
                    else if (!a.Zufahrt && math.distance(a.B, b.B) < .002) { start = a.A; ende = b.A; }
                    else continue;
                    var d = (double2)ende - (double2)start;
                    var l = math.length(d);
                    if (l < .001) continue;
                    bool Gerade(float2 p) => Math.Abs(d.x * (p.y - start.y) - d.y * (p.x - start.x)) / l < .002;
                    if (!Gerade(a.A) || !Gerade(a.B) || !Gerade(b.A) || !Gerade(b.B)) continue;
                    a.A = start; a.B = ende;
                    kurse.RemoveAt(j);
                    // Ein Kurs kann auf beiden Seiten weitergehen; die
                    // Entscheidung betrifft nur die Definition, keine Stadt-Entity.
                    j = i;
                }
            }
            return kurse.ToArray();
        }
    }
}
