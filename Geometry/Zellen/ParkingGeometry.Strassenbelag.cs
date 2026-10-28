using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        // Kompositionsbreite der sichtbaren Alley, nicht die 5,5-m-Belagwahl.
        public const double Zufahrtsgassenbreite = 8.0;

        internal static Punkt[][] GruenRastervorgaben(Rahmen rahmen,
            IReadOnlyList<Flaeche> flaechen, IReadOnlyList<Zelle> zellen)
        {
            var roadknoten = new HashSet<(float X, float Y)>();
            foreach (var z in zellen.Where(z => z.Material == Material.Strassenbelag))
            foreach (var p in z.Polygon.Punkte)
            {
                var w = rahmen.NachWelt(p);
                roadknoten.Add(((float)w.X, (float)w.Y));
            }
            var haarrisse = new List<float2[]>();
            foreach (var f in flaechen.Where(f => f.Material == Material.Gruen))
            {
                // Nur am neuen Gassenrand planen, keine globalen Altlasten.
                if (!f.AlleRinge.SelectMany(r => r.Rohkanten ?? r.Kanten).Any(k =>
                {
                    var w = rahmen.NachWelt(k.Von.Punkt);
                    return roadknoten.Contains(((float)w.X, (float)w.Y));
                })) continue;
                foreach (var ring in ZerlegeSelbstberuehrung(ZellenRing(rahmen, f.Aussenring)))
                {
                    if (ZellenHaarriss(ring.Select(p => new double2(p.x, p.y)).ToArray())) haarrisse.Add(ring);
                    else if (Cs2Triangulierung.Dreiecke(ring) == 0)
                        BaubareStuecke(ring, new List<float2[]>(), haarrisse);
                }
            }
            List<(Punkt A, Punkt B)> roadkanten = null;
            bool Kantenkontakt(float2 punkt)
            {
                if (roadkanten == null)
                {
                    roadkanten = new List<(Punkt, Punkt)>();
                    foreach (var z in zellen.Where(z => z.Material == Material.Strassenbelag))
                    for (int i = 0; i < z.Polygon.Anzahl; i++)
                    {
                        var a = rahmen.NachWelt(z.Polygon.Knoten(i).Punkt);
                        var b = rahmen.NachWelt(z.Polygon.Knoten(i + 1).Punkt);
                        roadkanten.Add((new Punkt((float)a.X, (float)a.Y), new Punkt((float)b.X, (float)b.Y)));
                    }
                }
                return roadkanten.Any(k => Geometrie.AbstandPunktStrecke(new Punkt(punkt.x, punkt.y), k.A, k.B)
                    <= SelbstberuehrungToleranz);
            }
            // Sehnenschnitte koennen neue Punkte auf einer Gassenkante
            // liefern. Auch sie zaehlen, nicht nur vorhandene Knoten-IDs.
            var relevant = haarrisse.Where(r => r.Any(p => roadknoten.Contains((p.x, p.y)))
                || r.Any(Kantenkontakt)).ToArray();
            if (relevant.Length == 0) return Array.Empty<Punkt[]>();
            var knoten = new Dictionary<(float X, float Y), Punkt>();
            foreach (var z in zellen)
            foreach (var p in z.Polygon.Punkte)
            {
                var w = rahmen.NachWelt(p);
                var key = ((float)w.X, (float)w.Y);
                if (!knoten.ContainsKey(key)) knoten.Add(key, p);
            }
            var saume = relevant.Select(saum => saum.Select(p => knoten.TryGetValue((p.x, p.y), out var lokal)
                ? lokal : rahmen.NachLokal(new Punkt(p.x, p.y))).ToArray()).ToArray();
            return saume;
        }

        private static Bauergebnis ZellenStrassenbelag(Bauergebnis bau,
            IReadOnlyList<ZellenStrasse> strassen, LayoutSettings settings)
        {
            var gassen = strassen.Where(s => s.Kind == "entrance"
                && Zufahrtsarten.IstGasse(s.Art)).Select(s => (s.A, s.B)).ToArray();
            if (gassen.Length == 0) return bau;
            var bruecken = (settings.Brueckengrundrisse ?? Array.Empty<float2[]>())
                .Where(r => r != null && r.Length >= 3)
                .Select(r => r.Select(p => bau.Rahmen.NachLokal(new Punkt(p.x, p.y))).ToArray())
                .ToArray();
            return Strassenbelagplan.Baue(bau, gassen, bruecken);
        }
    }
}
