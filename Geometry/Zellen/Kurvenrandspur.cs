using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    /** Gemeinsame Stationierung einer Formel, auch ueber gerade Abschnitte. */
    internal sealed class Kurvenrandspur
    {
        private const int ProbenJeAbschnitt = 256;
        private readonly Punkt[] _kontrollen;
        private readonly double[] _stationen;
        private readonly Punkt[] _punkte;
        private readonly bool[] _gerade;
        private readonly Punkt[] _geradenRichtungen;
        private readonly int[] _geradenAbschnitte;
        internal double Laenge => _stationen[_stationen.Length - 1];

        internal Kurvenrandspur(Punkt[] kontrollen)
        {
            _kontrollen = kontrollen.ToArray();
            int anzahl = kontrollen.Length / 4;
            _gerade = new bool[anzahl];
            _geradenRichtungen = new Punkt[anzahl];
            _geradenAbschnitte = Enumerable.Repeat(-1, anzahl).ToArray();
            for (int von = 0; von < anzahl;)
            {
                int bis = von - 1;
                for (int k = von; k < anzahl; k++)
                    if (Gerade(von, k)) bis = k;
                if (bis < von) { von++; continue; }
                var a = kontrollen[von * 4]; var d = kontrollen[bis * 4 + 3] - a;
                var u = d * (1 / Geometrie.Laenge(d));
                for (int k = von; k <= bis; k++)
                {
                    var start = a + u * Geometrie.Skalar(kontrollen[k * 4] - a, u);
                    var ende = a + u * Geometrie.Skalar(kontrollen[k * 4 + 3] - a, u);
                    for (int j = 0; j < 4; j++) _kontrollen[k * 4 + j] = start + (ende - start) * (j / 3.0);
                    _gerade[k] = true; _geradenRichtungen[k] = u; _geradenAbschnitte[k] = von;
                }
                von = bis + 1;
            }
            bool Gerade(int von, int bis)
            {
                var a = kontrollen[von * 4]; var d = kontrollen[bis * 4 + 3] - a;
                double l = Geometrie.Laenge(d);
                if (l == 0) return false;
                double vorher = 0;
                for (int i = von * 4; i < (bis + 1) * 4; i++)
                {
                    var v = kontrollen[i] - a;
                    if (Math.Abs(Geometrie.Kreuz(d, v)) / l > ParkingGeometry.ZellenEingangsgitter)
                        return false;
                    double t = Geometrie.Skalar(v, d) / Geometrie.Skalar(d, d);
                    if (t < vorher || t > 1) return false;
                    vorher = t;
                }
                return true;
            }
            _stationen = new double[kontrollen.Length / 4 * ProbenJeAbschnitt + 1];
            _punkte = new Punkt[_stationen.Length];
            var vorher = _punkte[0] = Wert(0).Punkt;
            for (int i = 1; i < _stationen.Length; i++)
            {
                var jetzt = _punkte[i] = Wert((double)i / ProbenJeAbschnitt).Punkt;
                _stationen[i] = _stationen[i - 1] + Geometrie.Laenge(jetzt - vorher);
                vorher = jetzt;
            }
            if (!(Laenge > 0)) throw new ArgumentException("Kurvenlauf ohne Laenge.");
        }

        internal (Punkt Punkt, Punkt Tangente) Bei(double s)
            => Wert(Parameter(s));

        private double Parameter(double s)
        {
            s = Math.Max(0, Math.Min(Laenge, s));
            int i = Array.BinarySearch(_stationen, s);
            if (i >= 0) return (double)i / ProbenJeAbschnitt;
            i = ~i;
            double f = (s - _stationen[i - 1]) / (_stationen[i] - _stationen[i - 1]);
            return (i - 1 + f) / ProbenJeAbschnitt;
        }

        internal int Geradenabschnitt(double s)
            => _geradenAbschnitte[Math.Min(_geradenAbschnitte.Length - 1, (int)Parameter(s))];

        internal double Kruemmung(double s)
        {
            double parameter = Parameter(s);
            int abschnitt = Math.Min(_kontrollen.Length / 4 - 1, (int)parameter);
            if (_gerade[abschnitt]) return 0;
            double t = parameter - abschnitt, u = 1 - t;
            int i = abschnitt * 4;
            var a = _kontrollen[i]; var b = _kontrollen[i + 1];
            var c = _kontrollen[i + 2]; var d = _kontrollen[i + 3];
            var v = (b - a) * (3 * u * u) + (c - b) * (6 * u * t) + (d - c) * (3 * t * t);
            var w = (c - b * 2 + a) * (6 * u) + (d - c * 2 + b) * (6 * t);
            double l = Geometrie.Laenge(v);
            return Geometrie.Kreuz(v, w) / (l * l * l);
        }

        internal double NaechsteStation(Punkt p)
        {
            double beste = double.PositiveInfinity, stelle = 0;
            // Bei(_stationen[i]) fuehrt exakt zu Wert(i / ProbenJeAbschnitt).
            // Diese Punkte wurden fuer die Stationierung bereits gerechnet.
            var a = _punkte[0];
            for (int i = 1; i < _stationen.Length; i++)
            {
                var b = _punkte[i];
                var d = b - a;
                double q = Geometrie.Skalar(d, d);
                double t = q == 0 ? 0 : Math.Max(0, Math.Min(1, Geometrie.Skalar(p - a, d) / q));
                double abstand = Geometrie.Laenge(p - a - d * t);
                if (abstand < beste)
                {
                    beste = abstand;
                    stelle = _stationen[i - 1] + (_stationen[i] - _stationen[i - 1]) * t;
                }
                a = b;
            }
            return stelle;
        }

        private (Punkt Punkt, Punkt Tangente) Wert(double parameter)
        {
            int abschnitt = Math.Min(_kontrollen.Length / 4 - 1, (int)parameter);
            double t = parameter - abschnitt, u = 1 - t;
            int i = abschnitt * 4;
            var a = _kontrollen[i]; var b = _kontrollen[i + 1];
            var c = _kontrollen[i + 2]; var d = _kontrollen[i + 3];
            if (_gerade[abschnitt])
            {
                var delta = d - a;
                return (a + delta * t, _geradenRichtungen[abschnitt]);
            }
            var p = a * (u * u * u) + b * (3 * u * u * t)
                + c * (3 * u * t * t) + d * (t * t * t);
            var v = (b - a) * (3 * u * u) + (c - b) * (6 * u * t) + (d - c) * (3 * t * t);
            double l = Geometrie.Laenge(v);
            if (!(l > 1e-9)) throw new ArgumentException("Kurvenlauf mit Nulltangente.");
            return (p, v * (1 / l));
        }

        internal static bool Ueberlappt(IReadOnlyList<Punkt> a, IReadOnlyList<Punkt> b)
        {
            foreach (var ring in new[] { a, b })
                for (int i = 0; i < ring.Count; i++)
                {
                    var d = ring[(i + 1) % ring.Count] - ring[i];
                    var n = new Punkt(-d.Y, d.X);
                    double a0 = double.PositiveInfinity, a1 = double.NegativeInfinity;
                    double b0 = double.PositiveInfinity, b1 = double.NegativeInfinity;
                    foreach (var p in a) { double v = Geometrie.Skalar(p - a[0], n); a0 = Math.Min(a0, v); a1 = Math.Max(a1, v); }
                    foreach (var p in b) { double v = Geometrie.Skalar(p - a[0], n); b0 = Math.Min(b0, v); b1 = Math.Max(b1, v); }
                    if (a1 <= b0 + 1e-9 || b1 <= a0 + 1e-9) return false;
                }
            return true;
        }


    }
}
