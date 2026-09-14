using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Teilflaechenlayout
    {
        internal int RingloserAbschnitt(Punkt wurzel)
        {
            var welt = _wurzelrahmen.NachWelt(wurzel);
            var teil = _teile.FirstOrDefault(t => t.Enthaelt(welt));
            if (teil?.Bandplan == null) return 20000;
            var y = teil.Rahmen.NachLokal(welt).Y;
            var grenzen = teil.Bandplan.InnereGrenzen.Concat(
                teil.Ringlos?.Korridore.SelectMany(w => w.Ecken).Select(v => v.Y) ?? Array.Empty<double>())
                .Distinct().OrderBy(v => v).ToArray();
            return 30000 + teil.Vorgabe.Index * 1000 + Array.FindIndex(grenzen, v => v > y);
        }

        internal Ringlosplan PlaneRingloseAnschluesse(double querbreite,
            IReadOnlyList<Punkt> areal, IReadOnlyList<Zufahrtsvorgabe> vorgaben,
            IReadOnlyList<Zoningvorgabe> zoning, Linienregister register)
        {
            var plan = new Ringlosplan { Links = double.NegativeInfinity, Rechts = double.PositiveInfinity };
            foreach (var teil in _teile.Where(t => t.Ringlos != null))
            {
                Ringlosplan.Weg Welt(Ringlosplan.Weg w) => new Ringlosplan.Weg {
                    A = Wurzelpunkt(teil, _wurzelrahmen, w.A), B = Wurzelpunkt(teil, _wurzelrahmen, w.B),
                    Band = w.Band, Breite = w.Breite, Fuss = w.Fuss, Art = w.Art };
                plan.Gassen.AddRange(teil.Ringlos.Gassen.Select(Welt));
                plan.Fusswege.AddRange(teil.Ringlos.Fusswege.Select(Welt));
                foreach (var q in teil.Ringlos.Querwege)
                    plan.Querwege.Add(new Querstrassenstueck { Querstrasse = q.Querstrasse,
                        Anfang = Wurzelpunkt(teil, _wurzelrahmen, q.Anfang),
                        Ende = Wurzelpunkt(teil, _wurzelrahmen, q.Ende) });
            }
            // Eine Naht hat nur zwischen wirklichen Gassenanschluessen einen
            // Zweck. Die Schnittparameter stammen aus deren Achsen, nicht aus
            // einer frueheren Randstrassentiefe.
            foreach (var naht in _naehte)
            {
                var a = _wurzelrahmen.NachLokal(naht.AnfangWelt);
                var b = _wurzelrahmen.NachLokal(naht.EndeWelt);
                var d = b - a;
                var treffer = new List<(Ringlosplan.Weg Gasse, Punkt Punkt, double T, bool Anfang)>();
                foreach (var g in plan.Gassen)
                {
                    var v = g.B - g.A;
                    var det = Geometrie.Kreuz(v, d);
                    if (Math.Abs(det) < 1e-8) continue;
                    var t = Geometrie.Kreuz(a - g.A, v) / det;
                    var u = Geometrie.Kreuz(a - g.A, d) / det;
                    if (t < 0 || t > 1 || (u > 1e-6 && u < 1 - 1e-6)) continue;
                    var punkt = a + d * t;
                    var ende = u <= 0 ? g.A : g.B;
                    if (Geometrie.Laenge(punkt - ende) > 2 + querbreite + 1e-6) continue;
                    treffer.Add((g, punkt, t, u <= 0));
                }
                if (treffer.Count < 2) continue;
                foreach (var t in treffer)
                {
                    if (t.Anfang) t.Gasse.A = t.Punkt;
                    else t.Gasse.B = t.Punkt;
                }
                var q = new Querstrassenplan(1000000 + naht.Nummer, 0, -querbreite / 2, querbreite / 2,
                    null, null) { Notwendig = true };
                plan.Querwege.Add(new Querstrassenstueck { Querstrasse = q,
                    Anfang = a + d * treffer.Min(t => t.T), Ende = a + d * treffer.Max(t => t.T) });
            }
            // An einer weitergefuehrten Nahtgasse liegt kein Gassenende mehr.
            // Die reservierte Endzone bleibt frei; dort wird kein zweites Netz gebaut.
            var endwege = plan.Fusswege.Where(w => !plan.Gassen.Any(g =>
                Ringlosplan.Ueberlappt(w.Ecken, g.Ecken))).ToArray();
            plan.Fusswege.Clear(); plan.Fusswege.AddRange(endwege);
            plan.PlaneZufahrten(Zufahrtsbauer.Plane(areal, vorgaben, 0, register), areal, zoning);
            foreach (var teil in _teile)
            foreach (var bucht in teil.Buchtgeometrie.ToArray())
            {
                var ecken = bucht.Value.Select(p => Wurzelpunkt(teil, _wurzelrahmen, p)).ToArray();
                if (!plan.Korridore.Any(w => Ringlosplan.Ueberlappt(ecken, w.Ecken))) continue;
                foreach (var key in teil.Buchten.Where(k => k.Value == bucht.Key).Select(k => k.Key).ToArray())
                    teil.Buchten.Remove(key);
                teil.Buchtgeometrie.Remove(bucht.Key);
            }
            return plan;
        }
    }
}
