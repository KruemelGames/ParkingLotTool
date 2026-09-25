using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Geometrischer Nachweis fuer den exportierten Plan, keine Simulation von
    // CS2. Zoning darf als Strasse vermitteln. Ein Pathway braucht dort einen
    // freien Endknoten an der Fahrbahnkante: gemessen 7,50 m Achsabstand bei
    // 8 m Strasse und 7 m Weg. Die zusaetzlichen 4 m LocalConnect-Suchweite
    // werden bewusst nicht als Ersatz fuer einen konstruierten Anschluss benutzt.
    internal static class RandstrassenErreichbarkeit
    {
        internal sealed class Befund
        {
            internal int Autowege;
            internal int Erreicht;
            internal int Quellen;
            internal int Randanschluesse;
            internal bool Vollstaendig => Quellen > 0 && Autowege > 0 && Erreicht == Autowege;
        }

        internal static Befund Pruefe(NetSegment[] netz,
            IReadOnlyList<float2> areal, double fahrbreite, double querbreite)
        {
            var auto = netz.Where(n => n.Art != Zufahrtsart.Fussweg).ToArray();
            var nachbarn = auto.Select(_ => new List<int>()).ToArray();
            var befund = new Befund { Autowege = auto.Count(n => n.Kind != "zoning") };
            bool Nah(float2 a, float2 b) => math.distance(a, b) <= 0.001;
            double Abstand(float2 p, float2 a, float2 b) => Geometrie.AbstandPunktStrecke(
                new Punkt(p.x, p.y), new Punkt(a.x, a.y), new Punkt(b.x, b.y));
            bool Aussen(float2 p) => Enumerable.Range(0, areal.Count)
                .Any(k => Abstand(p, areal[k], areal[(k + 1) % areal.Count]) <= 0.001);
            // Die Regel steht in ZoningMuendung - dieselbe fragen auch die
            // Bushaltestellen.
            bool Randanschluss(int wegIndex, int strassenIndex)
                => ZoningMuendung.Muendet(netz, auto[wegIndex], auto[strassenIndex],
                    fahrbreite, querbreite, out _);
            for (var i = 0; i < auto.Length; i++)
            for (var j = i + 1; j < auto.Length; j++)
            {
                var iz = auto[i].Kind == "zoning"; var jz = auto[j].Kind == "zoning";
                bool verbunden;
                if (iz != jz)
                {
                    verbunden = Randanschluss(iz ? j : i, iz ? i : j);
                    if (verbunden) befund.Randanschluesse++;
                }
                else verbunden = new[] { auto[i].A, auto[i].B }
                    .Any(a => Nah(a, auto[j].A) || Nah(a, auto[j].B));
                if (verbunden) { nachbarn[i].Add(j); nachbarn[j].Add(i); }
            }
            var erreicht = new HashSet<int>(); var offen = new List<int>();
            for (var i = 0; i < auto.Length; i++)
                if (auto[i].Kind == "entrance" && (Aussen(auto[i].A) || Aussen(auto[i].B)))
                { erreicht.Add(i); offen.Add(i); befund.Quellen++; }
            for (var k = 0; k < offen.Count; k++)
                foreach (var i in nachbarn[offen[k]])
                    if (erreicht.Add(i)) offen.Add(i);
            befund.Erreicht = erreicht.Count(i => auto[i].Kind != "zoning");
            return befund;
        }
    }
}
