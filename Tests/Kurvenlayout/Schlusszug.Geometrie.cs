using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Schlusszug
{
    private static double3 Wert(float3[] k, int s, double t)
    {
        double u = 1 - t;
        return u * u * u * (double3)k[s * 4] + 3 * u * u * t * (double3)k[s * 4 + 1]
            + 3 * u * t * t * (double3)k[s * 4 + 2] + t * t * t * (double3)k[s * 4 + 3];
    }

    private static (int S, double T) Projektion(float3[] k, float2 p)
    {
        var m = typeof(Kurvenzettel).GetMethod("Lot", BindingFlags.NonPublic | BindingFlags.Static);
        object[] a = { k, p, Strassenkurve.Pfeilhoehe + .05, 0, 0d };
        if (!(bool)m.Invoke(null, a)) throw new Exception("Endlage liegt nicht auf der Ausgangsformel.");
        return ((int)a[3], (double)a[4]);
    }

    private static IEnumerable<(string Name, float2[] Polygon, KurvenRandlauf Lauf)> Endlagen(float3[] k, float2[] polygon)
    {
        var innere = polygon.Skip(1).SkipLast(1).Select(p => (P: p, Q: Projektion(k, p))).ToArray();
        var parameter = new SortedSet<double>();
        // Ca. 10-17 cm Schritt plus 1 mm / 1 cm beidseits der echten
        // Abschnittsnaht und der gesetzten Polygonpunkte, sowie Sehnenmitten.
        for (int i = 0; i <= 120; i++) parameter.Add(i / 60d);
        foreach (var p in innere.Where(p => p.Q.S < 2))
            foreach (double e in new[] { -.001, -.0001, 0, .0001, .001 })
                parameter.Add(p.Q.S + p.Q.T + e);
        parameter.Add(.5); parameter.Add(1.5);
        foreach (double v in parameter.Where(v => v >= 0 && v <= 2))
        {
            int a = Math.Min(1, (int)v); double t = v - a;
            int b = k.Length / 4 - 1 - a;
            var von = (float2)Wert(k, a, t).xz;
            var bis = (float2)Wert(k, b, 1 - t).xz;
            var punkte = new[] { von }.Concat(innere.Where(p => p.Q.S + p.Q.T > v
                && p.Q.S + p.Q.T < b + 1 - t).Select(p => p.P)).Concat(new[] { bis }).ToArray();
            var formel = Kurvenzettel.Zuschneiden(k, von, bis, Strassenkurve.Pfeilhoehe + .05);
            if (formel == null) throw new Exception("Gueltige Endlage wurde nicht zugeschnitten.");
            yield return ($"Raster/{v:R}", punkte,
                new KurvenRandlauf { Von = 0, Kanten = punkte.Length - 1, Kontrollpunkte = formel });
        }
    }

    private static IEnumerable<(string Name, float2[] Polygon, KurvenRandlauf Lauf)> Sehnenenden(float3[] k, float2[] polygon)
    {
        var innere = polygon.Skip(1).SkipLast(1).Select(p => (P: p, Q: Projektion(k, p))).ToArray();
        for (int i = 0; i < 2; i++)
        {
            double laenge = math.distance(polygon[i], polygon[i + 1]);
            foreach (double t in new[] { .001 / laenge, .01 / laenge, .5, 1 - .01 / laenge, 1 - .001 / laenge })
            {
                var von = math.lerp(polygon[i], polygon[i + 1], (float)t);
                int j = polygon.Length - 1 - i;
                var bis = math.lerp(polygon[j], polygon[j - 1], (float)t);
                var a = Projektion(k, von); var b = Projektion(k, bis);
                var punkte = new[] { von }.Concat(innere.Where(p => p.Q.S + p.Q.T > a.S + a.T
                    && p.Q.S + p.Q.T < b.S + b.T).Select(p => p.P)).Concat(new[] { bis }).ToArray();
                var formel = Kurvenzettel.Zuschneiden(k, von, bis, Strassenkurve.Pfeilhoehe + .05);
                if (formel == null) throw new Exception("Sehnenende wurde nicht zugeschnitten.");
                yield return ($"Sehne/{i}/{t:R}", punkte,
                    new KurvenRandlauf { Von = 0, Kanten = punkte.Length - 1, Kontrollpunkte = formel });
            }
        }
    }

    private static void PruefeZuschnitt(float3[] k)
    {
        var teil = typeof(Kurvenzettel).GetMethod("Teilstueck", BindingFlags.NonPublic | BindingFlags.Static);
        double maxTeil = 0, maxLot = 0, maxNaht = 0;
        int proben = 0;
        for (int s = 0; s < k.Length / 4; s++)
        {
            if (s > 0) maxNaht = Math.Max(maxNaht, math.distance((double3)k[s * 4 - 1], k[s * 4]));
            foreach (var intervall in new[] { (0d, .5), (.123, .789), (.5, 1d), (0d, 1d) })
            {
                var q = (float3[])teil.Invoke(null, new object[] { k, s, intervall.Item1, intervall.Item2 });
                for (int i = 0; i <= 64; i++)
                {
                    double t = i / 64d;
                    maxTeil = Math.Max(maxTeil, math.distance(Wert(q, 0, t),
                        Wert(k, s, intervall.Item1 + (intervall.Item2 - intervall.Item1) * t)));
                    proben++;
                }
            }
            foreach (double t in new[] { .001, .25, .5, .75, .999 })
            {
                var p = (float2)Wert(k, s, t).xz;
                var lot = Projektion(k, p);
                double gefunden = math.distance(Wert(k, lot.S, lot.T).xz, (double2)p);
                // Unabhaengiger dichter Suchlauf, auch ueber Nachbarabschnitte:
                // die Produktionsprojektion darf nicht schlechter sein.
                double dicht = double.PositiveInfinity;
                for (int n = Math.Max(0, s - 1); n <= Math.Min(k.Length / 4 - 1, s + 1); n++)
                    for (int i = 0; i <= 2048; i++) dicht = Math.Min(dicht,
                        math.distance(Wert(k, n, i / 2048d).xz, (double2)p));
                if (gefunden > dicht + 1e-7) throw new Exception("Lot verfehlt den unabhaengig gemessenen Abstand.");
                maxLot = Math.Max(maxLot, gefunden);
            }
        }
        // Welt-float bei z=4900 hat 0,488 mm ULP; keine Layouttoleranz.
        if (maxTeil > .001 || maxLot > .001)
            throw new Exception($"Zuschnitt: Teil {maxTeil:R}, Lot {maxLot:R}, Quellnaht {maxNaht:R} m.");
        foreach (double t in new[] { .001, .125, .5, .875, .999 })
        {
            var von = (float2)Wert(k, 0, t).xz;
            var bis = (float2)Wert(k, k.Length / 4 - 1, 1 - t).xz;
            var q = Kurvenzettel.Zuschneiden(k, von, bis, Strassenkurve.Pfeilhoehe + .05);
            if (q == null || !q[0].xz.Equals(von) || !q[q.Length - 1].xz.Equals(bis))
                throw new Exception("Zuschnitt hat nicht die exakten Formenden.");
            for (int i = 4; i < q.Length; i += 4)
                if (math.distance((double3)q[i - 1], q[i]) > maxNaht + 1e-12)
                    throw new Exception("Zuschnitt erzeugt eine neue Abschnittsluecke.");
        }
        Console.WriteLine($"Zuschnitt: {proben} Bernsteinproben, max. Teil/Lot/Quellnaht {maxTeil:R}/{maxLot:R}/{maxNaht:R} m.");
    }

    private static void PruefeModulgrenze(float2[] polygon, LayoutSettings s)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var eingabe = typeof(ParkingGeometry).GetMethod("ZellenNormalisiereEingabe", flags)
            .Invoke(null, new object[] { polygon, s });
        var punkte = (Punkt[])eingabe.GetType().GetProperty("Normalisiert", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(eingabe);
        var rahmen = Geometrie.Reihenrahmen(punkte, null);
        var lokal = punkte.Select(rahmen.NachLokal).ToArray();
        var e = new Zelleneinstellungen { Randabstand = s.Es, Buchttiefe = s.Sl,
            Buchtbreite = s.Sw, Fahrgassenbreite = s.Ai, Randstrassen = true,
            Kurvenlaeufe = (KurvenRandlauf[])typeof(ParkingGeometry).GetMethod("ZellenKurvenlaeufe", flags)
                .Invoke(null, new object[] { polygon, s }) };
        var kurve = new Kurvenrandplan(lokal, rahmen, e, Array.Empty<Zufahrtsvorgabe>(), new Linienregister());
        var innen = kurve.Innenrand(e.Randstrasseninnentiefe, d => Layoutplanung.Innenrand(lokal, d));
        double hoehe = innen.Max(p => p.Y) - innen.Min(p => p.Y);
        double modul = 2 * s.Sl + s.Ai;
        if (hoehe >= modul) throw new Exception($"Modulabbruch trotz {hoehe:R} m Innenhoehe / {modul:R} m Modul.");
        Console.WriteLine($"Modulgrenze: {polygon.Length} Punkte, Innenhoehe {hoehe:F6} m < {modul:F6} m.");
    }
}
