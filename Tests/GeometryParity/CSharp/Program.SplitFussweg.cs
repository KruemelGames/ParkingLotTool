using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    // Die exportierten Ringe messen, einschliesslich kurzer Stuecke und Gras.
    // Gemeinsame Teilkanten zaehlen auch dann, wenn nur ein Ring Zwischenpunkte hat.
    private static void PruefeSplitFussweg(Flaechenfall fall, ParkingLayout layout,
        Action<bool, string> pruefe)
    {
        double Abstand(float2 p, float2 a, float2 b)
        {
            var d = b - a;
            var t = math.clamp(math.dot(p - a, d) / math.lengthsq(d), 0f, 1f);
            return math.distance(p, a + d * t);
        }
        bool Aussen(float2 p) => Enumerable.Range(0, fall.Umriss.Length)
            .Any(i => Abstand(p, fall.Umriss[i], fall.Umriss[(i + 1) % fall.Umriss.Length]) < .002);
        var fuss = layout.NetLine.Where(n => n.Art == Zufahrtsart.Fussweg).ToArray();
        var fussnaehte = 0;
        var randnaehte = 0;
        foreach (var material in new[] { ("Belag", layout.AsphaltSurface), ("Gras", layout.GrassSurface) })
        foreach (var naht in GemeinsameTeilKanten(material.Item2))
        {
            var mitte = (naht.A + naht.B) / 2;
            var laenge = math.distance(naht.A, naht.B);
            var querZumFuss = fuss.Any(n => Abstand(mitte, n.A, n.B) < .002
                && Math.Abs(math.dot(math.normalize(n.B - n.A), math.normalize(naht.B - naht.A))) < .01);
            var amRand = (Aussen(naht.A) || Aussen(naht.B))
                && laenge <= fall.Einstellungen.Es + .01
                && fall.Umriss.All(p => math.distance(p, mitte) > 5);
            if (material.Item1 == "Belag" && querZumFuss) fussnaehte++;
            if (material.Item1 == "Gras" && amRand) randnaehte++;
            if (Environment.GetEnvironmentVariable("PLT_BREIT") == "1")
                Console.WriteLine($"    Naht {material.Item1}: {laenge:F3} m bei ({mitte.x:F3}/{mitte.y:F3}), Fuss={querZumFuss}, Rand={amRand}");
        }
        var enden = fuss.SelectMany(n => new[] { n.A, n.B }).Distinct().ToArray();
        int Grad(float2 p) => layout.NetLine.Count(n => n.A.Equals(p) || n.B.Equals(p));
        var frei = enden.Count(p => Grad(p) == 1);
        var autoquellen = layout.NetLine.Where(n => n.Kind == "entrance" && n.Art != Zufahrtsart.Fussweg)
            .SelectMany(n => new[] { n.A, n.B }).Distinct().Count(p => Aussen(p) && Grad(p) == 1);
        var suchendeFussenden = enden.Count(p => Grad(p) == 1
            && FusswegAnschluss.Suchmaske(Zufahrtsart.Fussweg, 1) != 0);
        Console.WriteLine($"    Split/Netz: {fussnaehte} Fusswegnaehte, {randnaehte} Grasrandnaehte; "
            + $"{fuss.Length} Fusskurse, {frei}/{enden.Length} freie Fussknoten, {autoquellen} freie Autozufahrten");
        Console.WriteLine($"    Fussweg-Suchregel: {suchendeFussenden} zur automatischen Strassensuche freigegebene Enden");
        if (!fall.Einstellungen.Randstrassen)
        {
            pruefe(fuss.Length > 0 && suchendeFussenden == 0, fall.Name + $": {suchendeFussenden} suchende Fussknoten bei {fuss.Length} Kursen");
            pruefe(fussnaehte == 0, fall.Name + $": {fussnaehte} kuenstliche Fusswegnaehte");
            // Ein geschlossener Grasring ohne Zufahrt braucht einen Schnitt;
            // der gemeldete Bau MIT Zufahrt hat bereits eine echte Oeffnung.
            if (fall.Einstellungen.Entrances.Length > 0)
                pruefe(randnaehte == 0, fall.Name + $": {randnaehte} kuenstliche Grasrandnaehte");
            else
                pruefe(randnaehte == 2, fall.Name + $": {randnaehte} Grasrandnaehte statt zwei konstruierter Ringoeffnungen");
            pruefe(!layout.Warnings.Any(w => w.Contains("hole separation")),
                fall.Name + ": nachtraegliche Lochtrennung statt konstruierter Flaechen");
            pruefe(fuss.All(n => math.distance(n.A, n.B) >= 1f),
                fall.Name + ": Fusskurs unter der 1-m-Baugrenze");
        }
        if (fall.Einstellungen.Entrances.Length > 0)
            pruefe(autoquellen == fall.Einstellungen.Entrances.Length,
                fall.Name + $": {autoquellen} freie Autozufahrten statt {fall.Einstellungen.Entrances.Length}");
    }

    private static IEnumerable<(float2 A, float2 B)> GemeinsameTeilKanten(float2[][] ringe)
    {
        for (var i = 0; i < ringe.Length; i++)
        for (var j = i + 1; j < ringe.Length; j++)
        for (var a = 0; a < ringe[i].Length; a++)
        for (var b = 0; b < ringe[j].Length; b++)
        {
            var p = ringe[i][a]; var q = ringe[i][(a + 1) % ringe[i].Length];
            var r = ringe[j][b]; var s = ringe[j][(b + 1) % ringe[j].Length];
            var laenge = math.distance(p, q);
            if (laenge < .001) continue;
            var d = (q - p) / laenge;
            double Quer(float2 v) => (double)d.x * v.y - (double)d.y * v.x;
            if (Math.Abs(Quer(r - p)) > .001 || Math.Abs(Quer(s - p)) > .001) continue;
            var u = math.dot(r - p, d); var v = math.dot(s - p, d);
            var von = Math.Max(0, Math.Min(u, v)); var bis = Math.Min(laenge, Math.Max(u, v));
            if (bis - von > .001) yield return (p + d * von, p + d * bis);
        }
    }
}
