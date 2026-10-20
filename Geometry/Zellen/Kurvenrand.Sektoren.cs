using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Kurvenrandplan
    {
        // Die Materialstuecke enthalten Bucht, Keil und die zugehoerige
        // Fahrbahn gemeinsam. Kurvenlauf und gerade Reihe brauchen nur
        // ihre Endstoesse; Bucht-IDs sind keine Materialgrenzen.
        internal void PlaneMaterialsektoren(IReadOnlyList<Randbuchtplan> alleBuchten)
        {
            if (Buchten.Count == 0) return;
            var innen = Innenrand(_s.Randstrasseninnentiefe,
                d => Layoutplanung.Innenrand(_areal, d));
            (int Kante, double Along, Punkt P) Treffer(IReadOnlyList<Punkt> ring, Punkt p, Punkt n)
            {
                double beste = double.PositiveInfinity, along = 0;
                int kante = -1;
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i]; var v = ring[(i + 1) % ring.Count] - a;
                    double nenner = Geometrie.Kreuz(n, v);
                    if (nenner == 0) continue;
                    double t = Geometrie.Kreuz(a - p, v) / nenner;
                    double u = Geometrie.Kreuz(a - p, n) / nenner;
                    if (t < 0 || u < 0 || u > 1 || t >= beste) continue;
                    beste = t; kante = i; along = u;
                }
                if (kante < 0) throw new InvalidOperationException("Materialsektor erreicht seinen Bandrand nicht.");
                return (kante, along, p + n * beste);
            }
            List<Punkt> Abschnitt(IReadOnlyList<Punkt> ring,
                (int Kante, double Along, Punkt P) a, (int Kante, double Along, Punkt P) b)
            {
                var p = new List<Punkt> { a.P };
                int kanten = (b.Kante - a.Kante + ring.Count) % ring.Count;
                if (kanten == 0 && b.Along < a.Along) kanten = ring.Count;
                for (int k = 0; k < kanten; k++) p.Add(ring[(a.Kante + k + 1) % ring.Count]);
                p.Add(b.P);
                return p;
            }
            var geordnet = alleBuchten.Select(b =>
            {
                var e = b.Ecken;
                var p = (e[0] + e[1]) * 0.5;
                var n = (e[3] + e[2]) * 0.5 - p;
                var aussen = Treffer(_areal, p, n * -1);
                // Die korrespondierenden Offsetkanten sind gleich indiziert.
                // An einer Endgehrung muss ein Stoss nicht senkrecht bleiben.
                var a = innen[aussen.Kante];
                var d = innen[(aussen.Kante + 1) % innen.Count] - a;
                double q = Geometrie.Skalar(d, d);
                double t = q == 0 ? 0 : Math.Max(0, Math.Min(1, Geometrie.Skalar(p - a, d) / q));
                var innenpunkt = (Kante: aussen.Kante, Along: t, P: a + d * t);
                return (Bucht: b, Aussen: aussen, Innen: innenpunkt);
            }).OrderBy(s => s.Aussen.Kante + s.Aussen.Along).ToArray();
            var schnitte = geordnet.Where((s, i) =>
                geordnet[(i + geordnet.Length - 1) % geordnet.Length].Bucht.Randkante != s.Bucht.Randkante
                || geordnet[(i + 1) % geordnet.Length].Bucht.Randkante != s.Bucht.Randkante).ToArray();
            for (int i = 0; i < innen.Count; i++)
                if (Geometrie.Laenge(innen[(i + 1) % innen.Count] - innen[i]) > 0)
                    Bodenstoesse.Add((innen[i], innen[(i + 1) % innen.Count]));
            foreach (var schnitt in schnitte) Bodenstoesse.Add((schnitt.Aussen.P, schnitt.Innen.P));
            for (int i = 0; i < schnitte.Length; i++)
            {
                var a = schnitte[i]; var b = schnitte[(i + 1) % schnitte.Length];
                var sektor = Abschnitt(_areal, a.Aussen, b.Aussen);
                sektor.AddRange(Abschnitt(innen, a.Innen, b.Innen).AsEnumerable().Reverse());
                Materialsektoren.Add(sektor.ToArray());
            }
            _bodenSuche = new PolygonSuche(Materialsektoren);
            _buchtenSuche = new PolygonSuche(Buchten.Select(b => b.Ecken));
            _grasSuche = new PolygonSuche(Graskeile);
            _anschlussSuche = new PolygonSuche(Anschluesse);
        }

        private PolygonSuche _bodenSuche, _buchtenSuche, _grasSuche, _anschlussSuche;
        internal int MaterialsektorBei(Punkt p) => _bodenSuche?.Finde(p) ?? -1;
        internal Randbuchtplan BuchtBei(Punkt p)
        {
            int i = _buchtenSuche?.Finde(p) ?? -1;
            return i < 0 ? null : Buchten[i];
        }
        internal bool IstGraskeil(Punkt p) => (_grasSuche?.Finde(p) ?? -1) >= 0;
        internal bool IstAnschluss(Punkt p) => (_anschlussSuche?.Finde(p) ?? -1) >= 0;

        private sealed class PolygonSuche
        {
            private readonly Punkt[][] _polygone;
            private readonly (double X0, double X1, double Y0, double Y1)[] _grenzen;
            internal PolygonSuche(IEnumerable<Punkt[]> polygone)
            {
                _polygone = polygone.ToArray();
                _grenzen = _polygone.Select(p => (p.Min(v => v.X), p.Max(v => v.X),
                    p.Min(v => v.Y), p.Max(v => v.Y))).ToArray();
            }
            internal int Finde(Punkt p)
            {
                for (int i = 0; i < _polygone.Length; i++)
                {
                    var g = _grenzen[i];
                    if (p.X < g.X0 || p.X > g.X1 || p.Y < g.Y0 || p.Y > g.Y1) continue;
                    if (Geometrie.Enthaelt(_polygone[i], p)) return i;
                }
                return -1;
            }
        }
    }
}
