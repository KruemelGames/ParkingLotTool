using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Kurvenrandplan
    {
        // Die Seiten schneiden den wirklichen Bordstein und Fahrbahnrand.
        // Die gespeicherte gerade Zufahrtstiefe kennt weder Sehnenwechsel
        // noch den gemeinsam mit den radialen Buchten geplanten Versatz.
        internal Punkt[] Zufahrtskontur(Zufahrtsgeometrie zufahrt, IReadOnlyList<Punkt> fahrbahnrand,
            IReadOnlyList<Punkt> fahrbahnachse)
        {
            if (!_versatzKanten[zufahrt.Vorgabe.Kante]) return null;
            // Die Netzfassung fasst Anschluesse nahe einer festen Kurvenecke
            // mit dieser zusammen. Schon der Belag folgt derselben Gasse.
            var ziel = ParkingGeometry.Kurvenzufahrtsziel(zufahrt.Start, zufahrt.Innennormale, fahrbahnachse);
            var richtung = ziel - zufahrt.Start;
            zufahrt.Innennormale = richtung * (1 / Geometrie.Laenge(richtung));
            zufahrt.Querhalbvektor = zufahrt.Tangente * (zufahrt.Vorgabe.Breite / 2
                / Geometrie.Skalar(zufahrt.Innennormale, new Punkt(-zufahrt.Tangente.Y, zufahrt.Tangente.X)));
            zufahrt.Netzziel = ziel;
            var links = zufahrt.Start - zufahrt.Querhalbvektor;
            var rechts = zufahrt.Start + zufahrt.Querhalbvektor;
            var achse = zufahrt.Innennormale;
            (int Kante, double Along, Punkt P) Treffer(IReadOnlyList<Punkt> ring,
                Punkt ursprung, bool bordstein, double ab)
            {
                double beste = double.PositiveInfinity, along = 0;
                int kante = -1; Punkt punkt = default;
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                    double fa = Geometrie.Kreuz(achse, a - ursprung);
                    double fb = Geometrie.Kreuz(achse, b - ursprung);
                    // Nur der Eintritt in den CCW-Ring. Dieselben Werte am
                    // gemeinsamen Knoten verhindern u = 1 + Rundungsrest.
                    if (fa < 0 || fb > 0 || fa == fb) continue;
                    double t = fa / (fa - fb);
                    var p = t == 0 ? a : t == 1 ? b : a + (b - a) * t;
                    double laengs = Geometrie.Skalar(p - ursprung, achse);
                    if (!bordstein && laengs < ab) continue;
                    double wert = bordstein ? Math.Abs(laengs) : laengs;
                    if (wert >= beste) continue;
                    beste = wert; kante = i; along = t; punkt = p;
                }
                if (kante < 0) throw new InvalidOperationException("Kurvenzufahrt erreicht ihren Bordstein oder Fahrbahnrand nicht.");
                return (kante, along, punkt);
            }
            List<Punkt> Abschnitt(IReadOnlyList<Punkt> ring,
                (int Kante, double Along, Punkt P) a, (int Kante, double Along, Punkt P) b)
            {
                var p = new List<Punkt> { a.P };
                int kanten = (b.Kante - a.Kante + ring.Count) % ring.Count;
                if (kanten == 0 && b.Along < a.Along) kanten = ring.Count;
                for (int i = 0; i < kanten; i++) p.Add(ring[(a.Kante + i + 1) % ring.Count]);
                p.Add(b.P);
                return p;
            }
            var al = Treffer(_areal, links, true, 0);
            var ar = Treffer(_areal, rechts, true, 0);
            var il = Treffer(fahrbahnrand, links, false, Geometrie.Skalar(al.P - links, achse));
            var ir = Treffer(fahrbahnrand, rechts, false, Geometrie.Skalar(ar.P - rechts, achse));
            var kontur = Abschnitt(_areal, al, ar);
            kontur.AddRange(Abschnitt(fahrbahnrand, il, ir).AsEnumerable().Reverse());
            return kontur.ToArray();
        }
    }
}
