using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Nur numerische Vorpruefung. Kein Aufruf aus Layout, Werkzeug oder Bau.
    public static class Kurvenversatz
    {
        public enum Grund { Keiner, Ungueltig, EingangNichtAngenommen, VersatzBrichtAb, VersatzNichtAngenommen }

        public sealed class Ergebnis
        {
            public Grund Grund { get; internal set; }
            public double Abstand { get; internal set; }
            public string Kernmeldung { get; internal set; }
            public bool Passt => Grund == Grund.Keiner;
        }

        public static Ergebnis Pruefe(IReadOnlyList<float2> kontur, IReadOnlyList<double> abstaende, double winkel = 0)
        {
            var e = new Ergebnis();
            if (kontur == null || kontur.Count < 3 || abstaende == null || abstaende.Count == 0
                || !math.isfinite(winkel) || kontur.Any(p => !math.all(math.isfinite(p)))
                || abstaende.Any(d => !math.isfinite(d) || d < 0))
                return new Ergebnis { Grund = Grund.Ungueltig };
            var welt = kontur.Select(p => new Punkt(p.x, p.y)).ToList();
            if (Geometrie.Vorzeichenflaeche(welt) < 0) welt.Reverse();
            if (!Angenommen(welt)) return new Ergebnis { Grund = Grund.EingangNichtAngenommen };
            var rahmen = Geometrie.Reihenrahmen(welt, winkel);
            var lokal = welt.Select(rahmen.NachLokal).ToArray();
            foreach (double d in abstaende)
            {
                e.Abstand = d;
                IReadOnlyList<Punkt> innen;
                try { innen = Layoutplanung.Innenrand(lokal, d); }
                catch (InvalidOperationException fehler)
                {
                    e.Grund = Grund.VersatzBrichtAb;
                    e.Kernmeldung = fehler.Message;
                    return e;
                }
                // In Weltkoordinaten pruefen: dort rechnet auch CS2 in float.
                var ring = innen.Select(rahmen.NachWelt).ToArray();
                if (!Angenommen(ring))
                {
                    e.Grund = Grund.VersatzNichtAngenommen;
                    return e;
                }
            }
            return e;
        }

        private static bool Angenommen(IReadOnlyList<Punkt> ring)
        {
            var punkte = new List<float2>();
            foreach (var p in ring)
            {
                var v = new float2((float)p.X, (float)p.Y);
                if (punkte.Count == 0 || !math.all(punkte[punkte.Count - 1] == v)) punkte.Add(v);
            }
            if (punkte.Count > 1 && math.all(punkte[0] == punkte[punkte.Count - 1])) punkte.RemoveAt(punkte.Count - 1);
            return punkte.Count >= 3 && Cs2Triangulierung.Dreiecke(punkte.ToArray()) == punkte.Count - 2;
        }
    }
}
