using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class RandexportTests
{
    internal static void Laufe(Action<bool, string> pruefe)
    {
        // Ein Loch mit einer doppelt durchlaufenen Bruecke: sein negativer
        // Umlauf darf beim Zerlegen niemals zum positiven Grasring werden.
        var ring = new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10),
            new float2(0, 10), new float2(0, 0), new float2(2, 2), new float2(2, 4),
            new float2(4, 4), new float2(4, 2), new float2(2, 2), new float2(0, 0) };
        var methode = typeof(ParkingGeometry).GetMethod("Kurvenflaechen",
            BindingFlags.Static | BindingFlags.NonPublic);
        var verloren = new List<float2[]>();
        var teile = (float2[][])methode.Invoke(null,
            new object[] { new[] { ring }, verloren, new List<float2[]>() });
        double Flaeche(float2[] p)
        {
            double summe = 0;
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Length];
                summe += (double)a.x * b.y - (double)b.x * a.y;
            }
            return Math.Abs(summe) / 2;
        }
        pruefe(Math.Abs(teile.Sum(Flaeche) - 96) < 1e-6,
            "Materialexport erhaelt den negativen Lochumlauf");
        pruefe(verloren.Count == 0 && teile.All(p => Cs2Triangulierung.Dreiecke(p) != 0),
            "Schwach einfacher Ring bleibt vollstaendig nativ baubar");

        var fall = RandkurvenFaelle.Alle().First(f => f.Name == "R30/150/False/False");
        var s = fall.Settings.Clone(); s.Kurvenlaeufe = new[] { fall.Lauf.Clone() };
        var layout = ParkingGeometry.Build(fall.Polygon, s);
        double Abstand(float2 p, float2 a, float2 b)
        {
            var v = new double2((double)b.x - a.x, (double)b.y - a.y);
            var w = new double2((double)p.x - a.x, (double)p.y - a.y);
            double t = math.clamp(math.dot(w, v) / math.lengthsq(v), 0, 1);
            return math.length(w - v * t);
        }
        foreach (int i in layout.KurvenRandreihen.SelectMany(r => r.Buchten))
        foreach (var achse in layout.NetLine.Where(a => a.Kind == "perimeter"))
        {
            var b = layout.Bay[i];
            double minimum = double.PositiveInfinity;
            for (int k = 0; k < b.Length; k++)
            {
                var a = b[k]; var c = b[(k + 1) % b.Length];
                minimum = Math.Min(minimum, Math.Min(Abstand(a, achse.A, achse.B),
                    Math.Min(Abstand(achse.A, a, c), Abstand(achse.B, a, c))));
            }
            pruefe(minimum >= s.Ai / 2 - 0.002,
                "Endgueltige Netzachse behaelt die volle Fahrbahnfreiheit");
        }
    }
}
