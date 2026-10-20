using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using K = ParkingLotTool.Geometry.Strassenkurve;

internal sealed class Formfall
{
    internal string Name;
    internal float2[] Polygon;
    internal float2[] Bogen;
    internal LayoutSettings Settings;
    internal int Stuecke;
    internal int? ErwarteteBuchten;
}

internal static class Formen
{
    internal static readonly float3 Welt = new float3(1300, 0, 4600);

    private static float3 Punkt(double x, double z) => new float3((float)x, 0, (float)z) + Welt;

    private static K.Bezier Bogen(double r, double von, double bis)
    {
        double a = von * Math.PI / 180, b = bis * Math.PI / 180;
        double h = 4.0 / 3 * Math.Tan((b - a) / 4);
        var pa = Punkt(r * Math.Cos(a), r * Math.Sin(a));
        var pd = Punkt(r * Math.Cos(b), r * Math.Sin(b));
        return new K.Bezier(pa, pa + new float3((float)(-r * Math.Sin(a) * h), 0, (float)(r * Math.Cos(a) * h)),
            pd - new float3((float)(-r * Math.Sin(b) * h), 0, (float)(r * Math.Cos(b) * h)), pd);
    }

    internal static IEnumerable<Formfall> Alle()
    {
        foreach (double r in new[] { 15d, 30, 60, 150 })
        foreach (int grad in new[] { 45, 90, 180 })
        {
            var kurven = grad <= 90 ? new[] { Bogen(r, -grad / 2.0, grad / 2.0) }
                : new[] { Bogen(r, -90, 0), Bogen(r, 0, 90) };
            var stand = K.Einfrieren(1, kurven);
            if (!stand.Folgt || stand.Gruppe == null) throw new Exception($"Produktionsabtastung R{r}/{grad}: {stand.Grund}");
            var rand = stand.Gruppe.Punkte.Select(p => (float2)p.xz).ToArray();
            foreach (bool innen in new[] { true, false })
            {
                // Eine einzige gekruemmte Seite, drei gerade Seiten. Innen:
                // Rueckwand 60 m hinter der Sehne; aussen: 60 m hinter dem
                // Scheitel. Kleine Winkel bleiben bewusst schmale Grundstuecke.
                float rueckwand = innen ? rand.Min(p => p.x) - 60 : rand.Max(p => p.x) + 60;
                float2[] p;
                if (innen)
                    p = rand.Concat(new[] { new float2(rueckwand, rand[^1].y), new float2(rueckwand, rand[0].y) }).ToArray();
                else
                {
                    // Radiale Endkappen: auch bei 180 Grad kein kuenstlicher
                    // fast-180-Grad-Ruecksprung direkt am Kurvenende.
                    var oben = Welt.xz + (rand[^1] - Welt.xz) * (float)((r + 60) / r);
                    var unten = Welt.xz + (rand[0] - Welt.xz) * (float)((r + 60) / r);
                    p = rand.Concat(new[] { oben, new float2(rueckwand, oben.y),
                        new float2(rueckwand, unten.y), unten }).ToArray();
                }
                yield return Fall($"{(innen ? "innen" : "aussen")}/R{r}/{grad}", p, rand, stand.Stuecke);
            }
        }
        // Symmetrische S-Kurve mit Wendepunkt, keine Mittelpunktnaeherung.
        var s = new K.Bezier(Punkt(0, 0), Punkt(40, 55), Punkt(80, -55), Punkt(120, 0));
        var e = K.Einfrieren(2, new[] { s });
        if (!e.Folgt || e.Gruppe == null) throw new Exception("Produktionsabtastung S: " + e.Grund);
        var linie = e.Gruppe.Punkte.Select(p => (float2)p.xz).ToArray();
        foreach (bool oben in new[] { true, false })
        {
            float rueckwand = oben ? linie.Max(p => p.y) + 60 : linie.Min(p => p.y) - 60;
            yield return Fall("S/" + (oben ? "oben" : "unten"), linie.Concat(new[] {
                new float2(linie[^1].x, rueckwand), new float2(linie[0].x, rueckwand) }).ToArray(), linie, e.Stuecke);
        }
        // Die alte Planmessung behalten: fast ruecklaeufige Endkappen.
        // Sie darf nicht als Beweis gelten, dass jeder konkave Bogen scheitert.
        foreach (int n in new[] { 24, 96 })
        {
            var alt = new List<float2> { Welt.xz + new float2(-150, -100), Welt.xz + new float2(150, -100) };
            for (int i = 0; i <= n; i++)
                alt.Add(Welt.xz + new float2((float)(150 * Math.Cos(Math.PI * i / n)),
                    (float)(180 - 150 * Math.Sin(Math.PI * i / n))));
            yield return Fall("Altplan/" + n, alt.ToArray(), Array.Empty<float2>(), n);
        }
    }

    private static Formfall Fall(string name, float2[] p, float2[] rand, int stuecke)
    {
        var s = LayoutSettings.Cs2;
        s.Auto = false; s.AngleMode = "fixed"; s.Angle = 0; s.AutomaticEntrances = false;
        // Die Form bleibt in Zeichenreihenfolge: Build ordnet CCW und
        // transportiert Along/Kantenindex selbst. Zugang auf gerader Rueckwand.
        return new Formfall { Name = name, Polygon = p, Bogen = rand, Settings = s, Stuecke = stuecke };
    }
}
