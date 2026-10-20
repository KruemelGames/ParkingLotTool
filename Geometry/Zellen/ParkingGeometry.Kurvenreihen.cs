using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        private static Dictionary<int, float2[]> ZellenKurvenmaterial(Bauergebnis bau)
        {
            var ausgabe = new Dictionary<int, float2[]>();
            if (bau.Kurvenrand == null) return ausgabe;
            var rechtecke = bau.Kurvenrand.Buchten.ToDictionary(b => b.Id);
            var anzahl = bau.Zellen.Where(z => z.BuchtId.HasValue)
                .GroupBy(z => z.BuchtId.Value).ToDictionary(g => g.Key, g => g.Count());
            foreach (var flaeche in bau.Flaechen)
            {
                if (flaeche.Loecher.Count != 0 || flaeche.ZellIds.Count == 0) continue;
                int? id = bau.Zellen[flaeche.ZellIds[0]].BuchtId;
                if (!id.HasValue || !rechtecke.ContainsKey(id.Value)
                    || flaeche.ZellIds.Count != anzahl[id.Value]
                    || flaeche.ZellIds.Any(i => bau.Zellen[i].BuchtId != id)) continue;
                // Der Plan ist bereits ein ganzes Rechteck. Zusaetzliche
                // Schnittpunkte auf seinen vier Geraden sind keine Formecken;
                // ihr float-Runden darf CS2 keine 2-cm-Zacken unterschieben.
                ausgabe.Add(flaeche.Id, rechtecke[id.Value].Ecken
                    .Select(p => ZellenPunkt(bau.Rahmen.NachWelt(p))).ToArray());
            }
            return ausgabe;
        }

        private static List<double2[]> ZellenEntnehmeKurvenbuchten(Bauergebnis bau,
            IGrouping<int, Zelle>[] gruppen, WorkLayout rollen)
        {
            var gesichert = new List<double2[]>();
            if (bau.Kurvenrand == null) return gesichert;
            var ids = new HashSet<int>(bau.Kurvenrand.Buchten.Select(b => b.Id));
            for (int i = gruppen.Length - 1; i >= 0; i--)
            {
                if (!ids.Contains(gruppen[i].Key)) continue;
                gesichert.Add(rollen.Bay[i]);
                rollen.Bay.RemoveAt(i); rollen.BayKind.RemoveAt(i);
            }
            gesichert.Reverse();
            return gesichert;
        }

        private static KurvenRandreihe[] ZellenKurvenreihen(Bauergebnis bau,
            IGrouping<int, Zelle>[] gruppen, int buchtzahl)
        {
            if (bau.Kurvenrand == null || bau.Kurvenrand.Laternen.Count == 0)
                return Array.Empty<KurvenRandreihe>();
            var ids = new HashSet<int>(bau.Kurvenrand.Buchten.Select(b => b.Id));
            var erhalten = gruppen.Where(g => ids.Contains(g.Key)).ToArray();
            // Diese ganzen Rechtecke wurden nach der Rollenvergabe in genau
            // dieser Reihenfolge angehaengt. Weder Koordinatenraten noch ein
            // veralteter Index vor dem Umbau der Behindertenbuchten.
            var indizes = erhalten.Select((g, i) => (g.Key, Index: buchtzahl - erhalten.Length + i))
                .ToDictionary(p => p.Key, p => p.Index);
            foreach (var reihe in bau.Kurvenrand.Laternen)
                reihe.Buchten = reihe.Buchten
                    .Select(i => bau.Kurvenrand.Buchten[i].Id)
                    .Where(indizes.ContainsKey).Select(i => indizes[i]).ToArray();
            return bau.Kurvenrand.Laternen.ToArray();
        }
    }
}
