using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        // Vor der Zellklassifikation: eine Bucht braucht eine ganze Vorderkante
        // an einem erreichbaren Weg. Ein durch Bauland gekuerztes Band allein
        // ist keine Fahrbahn. Derselbe Kursplan wird spaeter exportiert.
        internal static Func<Punkt[], bool> ZellenZoningzugang(Bauergebnis bau,
            IReadOnlyList<Zoningvorgabe> zoning,
            IReadOnlyList<(Punkt A, Punkt B)> randzoning, double breite, double querbreite)
        {
            var strassen = ZellenStrassen(bau, zoning, randzoning, breite, querbreite);
            var gefasst = ZellenNetzfassung(strassen, bau.Rahmen,
                bau.Kurvenrand == null || !bau.Randstrassen ? null
                    : new HashSet<Punkt>(bau.Randstrassenmittellinie));
            var netz = gefasst.Select(s => new NetSegment(s.Kind,
                ZellenPunkt(bau.Rahmen.NachWelt(s.A)), ZellenPunkt(bau.Rahmen.NachWelt(s.B)), s.Art)).ToArray();
            var nachbarn = netz.Select(_ => new List<int>()).ToArray();
            bool Nah(float2 a, float2 b) => math.distance(a, b) <= 0.001;
            for (var i = 0; i < netz.Length; i++)
            for (var j = i + 1; j < netz.Length; j++)
            {
                if (netz[i].Art == Zufahrtsart.Fussweg || netz[j].Art == Zufahrtsart.Fussweg) continue;
                var iz = netz[i].Kind == "zoning";
                var jz = netz[j].Kind == "zoning";
                var verbunden = Nah(netz[i].A, netz[j].A) || Nah(netz[i].A, netz[j].B)
                    || Nah(netz[i].B, netz[j].A) || Nah(netz[i].B, netz[j].B);
                if (!verbunden && iz != jz)
                    verbunden = ZoningMuendung.Muendet(netz, netz[iz ? j : i], netz[iz ? i : j],
                        breite, querbreite, out _);
                if (!verbunden) continue;
                nachbarn[i].Add(j); nachbarn[j].Add(i);
            }
            var erreicht = new HashSet<int>();
            var offen = new List<int>();
            var areal = bau.Umrisslokal.Select(p => ZellenPunkt(bau.Rahmen.NachWelt(p))).ToArray();
            bool AmRand(float2 p) => Enumerable.Range(0, areal.Length).Any(k => Abstand(p,
                areal[k], areal[(k + 1) % areal.Length]) <= 0.002);
            for (var i = 0; i < netz.Length; i++)
                if (netz[i].Kind == "entrance" && netz[i].Art != Zufahrtsart.Fussweg
                    && (AmRand(netz[i].A) || AmRand(netz[i].B)))
                { erreicht.Add(i); offen.Add(i); }
            // Ohne gesetzte Autozufahrt ist der Entwurf noch offen. Dann
            // pruefen wir den Fahrbahnzugang, nicht eine fehlende Stadtzufahrt.
            if (offen.Count == 0)
                for (var i = 0; i < netz.Length; i++)
                    if (netz[i].Art != Zufahrtsart.Fussweg) erreicht.Add(i);
            for (var k = 0; k < offen.Count; k++)
                foreach (var i in nachbarn[offen[k]])
                    if (erreicht.Add(i)) offen.Add(i);

            var fahrbahnen = erreicht.Select(i => netz[i]).ToArray();
            bool Kante(float2 a, float2 b)
            {
                var teile = new List<(double Von, double Bis)>();
                foreach (var s in fahrbahnen)
                {
                    var halb = s.Kind == "zoning" ? ZoningStrassenbreite / 2
                        : ZoningMuendung.EigeneBreite(s, breite, querbreite) / 2;
                    // Die meisten Kurse liegen weit von dieser Bucht weg.
                    // Erst die sichere Huelle, dann der genaue Streifenschnitt.
                    if (Math.Min(a.x, b.x) > Math.Max(s.A.x, s.B.x) + halb + 0.001
                        || Math.Max(a.x, b.x) < Math.Min(s.A.x, s.B.x) - halb - 0.001
                        || Math.Min(a.y, b.y) > Math.Max(s.A.y, s.B.y) + halb + 0.001
                        || Math.Max(a.y, b.y) < Math.Min(s.A.y, s.B.y) - halb - 0.001) continue;
                    var dx = (double)s.B.x - s.A.x;
                    var dy = (double)s.B.y - s.A.y;
                    var laenge = Math.Sqrt(dx * dx + dy * dy);
                    if (laenge < 1e-9) continue;
                    var u = new Punkt(dx / laenge, dy / laenge);
                    var n = new Punkt(-u.Y, u.X);
                    var start = new Punkt((double)a.x - s.A.x, (double)a.y - s.A.y);
                    var d = new Punkt((double)b.x - a.x, (double)b.y - a.y);
                    var von = 0.0;
                    var bis = 1.0;
                    foreach (var (achse, lo, hi) in new[] { (u, -0.001, laenge + 0.001), (n, -halb - 0.001, halb + 0.001) })
                    {
                        var wert = Geometrie.Skalar(start, achse);
                        var richtung = Geometrie.Skalar(d, achse);
                        if (Math.Abs(richtung) < 1e-12)
                        {
                            if (wert < lo || wert > hi) { bis = von; break; }
                            continue;
                        }
                        var t1 = (lo - wert) / richtung;
                        var t2 = (hi - wert) / richtung;
                        von = Math.Max(von, Math.Min(t1, t2));
                        bis = Math.Min(bis, Math.Max(t1, t2));
                    }
                    if (bis > von) teile.Add((von, bis));
                }
                var gedeckt = 0.0;
                foreach (var t in teile.OrderBy(t => t.Von))
                {
                    if (t.Von > gedeckt + 1e-9) return false;
                    gedeckt = Math.Max(gedeckt, t.Bis);
                }
                return gedeckt >= 1 - 1e-9;
            }
            return ecken =>
            {
                var p = ecken.Select(e => ZellenPunkt(bau.Rahmen.NachWelt(e))).ToArray();
                return Kante(p[0], p[1]) || Kante(p[2], p[3]);
            };
        }

        private static double Abstand(float2 p, float2 a, float2 b) => Geometrie.AbstandPunktStrecke(
            new Punkt(p.x, p.y), new Punkt(a.x, a.y), new Punkt(b.x, b.y));
    }
}
