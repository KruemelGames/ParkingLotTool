using System.Collections.Generic;
using System;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    internal static class Versorgungsnetz
    {
        // Nur reale, freigegebene Graphkanten; geplante Leitungen sind keine Quelle.
        internal static HashSet<int> Erreichbar(IEnumerable<int> wurzeln, IEnumerable<int2> kanten)
        {
            var nachbarn = new Dictionary<int, List<int>>();
            foreach (var k in kanten)
            {
                if (!nachbarn.TryGetValue(k.x, out var a)) nachbarn[k.x] = a = new List<int>();
                if (!nachbarn.TryGetValue(k.y, out var b)) nachbarn[k.y] = b = new List<int>();
                a.Add(k.y); b.Add(k.x);
            }
            var erreicht = new HashSet<int>(wurzeln);
            var offen = new List<int>(erreicht);
            for (var i = 0; i < offen.Count; i++)
                if (nachbarn.TryGetValue(offen[i], out var liste))
                    foreach (var n in liste) if (erreicht.Add(n)) offen.Add(n);
            return erreicht;
        }

        internal static bool Kuerzer(float kandidat, float bisher)
            => kandidat < bisher - 0.001f;

        internal static Versorgungsweg.Ergebnis Gerade(List<float2> starts,
            Func<float2, IEnumerable<Versorgungsweg.Ziel>> ziele,
            Func<int, Versorgungsweg.Ziel, bool> zulaessig, float maxLaenge = float.MaxValue)
        {
            var r = new Versorgungsweg.Ergebnis { Laenge = float.MaxValue };
            for (var i = 0; i < starts.Count; i++)
                foreach (var ziel in ziele(starts[i]))
                {
                    r.Zielpruefungen++;
                    var l = math.distance(starts[i], ziel.Punkt);
                    if (l > maxLaenge || !Kuerzer(l, r.Laenge) || !zulaessig(i, ziel)) continue;
                    r.Laenge = l; r.Start = i; r.Ziel = ziel.Index;
                    r.Punkte = new List<float2> { starts[i], ziel.Punkt };
                }
            return r;
        }

        internal static float2 Projektion(float2 p, float2 a, float2 b)
            => a + (b - a) * math.clamp(math.dot(p - a, b - a)
                / math.max(1e-12f, math.lengthsq(b - a)), 0, 1);

        // Mitte zuerst: bei parallelen Strecken haben viele Punkte dieselbe
        // Laenge. Endprojektionen erfassen auch versetzt gegenueberliegende Kanten.
        internal static IEnumerable<float2> Kantenstarts(float2 a, float2 b, float2 c, float2 d)
        {
            yield return Projektion((c + d) / 2, a, b);
            yield return (a + b) / 2;
            yield return Projektion(c, a, b);
            yield return Projektion(d, a, b);
            yield return a;
            yield return b;
        }

        internal static IEnumerable<float2> Kantenpunkte(Func<float, float2> start,
            Func<float2, float2> startprojektion, Func<float, float2> ziel,
            Func<float2, float2> zielprojektion)
        {
            foreach (var p in Kantenstarts(start(0), start(1), ziel(0), ziel(1)))
                yield return startprojektion(p);
            // Wechselnde Projektion ergaenzt die Sehnenkandidaten fuer
            // gekruemmte Kanten. 17 Startlagen, jeweils 12 Projektionen.
            // Dies ist eine endliche Kandidatensuche, kein globaler Kurvenbeweis.
            for (var i = 0; i <= 16; i++)
            {
                var p = start(i / 16f);
                for (var j = 0; j < 12; j++)
                {
                    var naechster = startprojektion(zielprojektion(p));
                    // Exakter Fixpunkt: weitere identische Projektionen koennen
                    // nichts aendern. Kein Epsilon, damit Kandidaten gleich bleiben.
                    if (math.all(naechster == p)) break;
                    p = naechster;
                }
                yield return p;
            }
        }
    }
}
