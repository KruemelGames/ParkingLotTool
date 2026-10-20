using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Kurvenzettel <-> Lotpuffer. Einzige Stelle, die das Format kennt (Lesen hier, Schreiben nebenan). */
    internal static partial class ParkingLotKurvenzettelPuffer
    {
        /**
         * Liest die Kurven eines Lots. Unbekannte Version oder kaputte
         * Verweise: diese Kurve faellt weg (ihre Punkte bleiben normale
         * Formpunkte) - nie ein halb gelesener Zettel.
         */
        internal static Kurvenzettel[] Lies(EntityManager em, Entity lot)
        {
            if (lot == Entity.Null || !em.Exists(lot) || !em.HasBuffer<ParkingLotBuildCurve>(lot))
                return null;
            var kanten = em.GetBuffer<ParkingLotBuildCurve>(lot, true);
            if (!em.HasBuffer<ParkingLotBuildCurveSection>(lot) || !em.HasBuffer<ParkingLotBuildCurveStation>(lot))
                return null;
            var abschnitte = em.GetBuffer<ParkingLotBuildCurveSection>(lot, true);
            var stationen = em.GetBuffer<ParkingLotBuildCurveStation>(lot, true);
            int abschnittZahl = abschnitte.Length;
            int stationZahl = stationen.Length;
            var ergebnis = new List<Kurvenzettel>();
            for (int i = 0; i < kanten.Length; i++)
            {
                var k = kanten[i];
                if (k.Version != ParkingLotBuildCurve.CurrentVersion || k.Zwischen <= 0
                    || k.ErsterAbschnitt < 0 || k.Abschnitte < 0 || k.ErsterAbschnitt + k.Abschnitte > abschnittZahl
                    || k.ErsteStation < 0 || k.ErsteStation + k.Zwischen > stationZahl
                    || !math.all(math.isfinite(k.Von)) || !math.all(math.isfinite(k.Bis))) continue;
                var punkte = new float3[k.Abschnitte * 4];
                bool gut = true;
                for (int a = 0; a < k.Abschnitte; a++)
                {
                    var s = abschnitte[k.ErsterAbschnitt + a];
                    if (s.Version != ParkingLotBuildCurveSection.CurrentVersion) { gut = false; break; }
                    punkte[a * 4] = s.A; punkte[a * 4 + 1] = s.B; punkte[a * 4 + 2] = s.C; punkte[a * 4 + 3] = s.D;
                }
                var bogen = new float[k.Zwischen];
                for (int b = 0; gut && b < k.Zwischen; b++)
                {
                    var st = stationen[k.ErsteStation + b];
                    if (st.Version != ParkingLotBuildCurveStation.CurrentVersion) gut = false;
                    else bogen[b] = st.Bogen;
                }
                if (!gut) continue;
                ergebnis.Add(new Kurvenzettel
                {
                    Von = k.Von, Bis = k.Bis, Zwischen = k.Zwischen, Abschnitte = punkte, Bogen = bogen,
                    Laenge = k.Laenge, RadiusMin = k.RadiusMin, Winkel = k.Winkel, Pfeilhoehe = k.Pfeilhoehe,
                    Wendepunkte = k.Wendepunkte, Toleranz = k.Toleranz, MinSehne = k.MinSehne, MaxSehne = k.MaxSehne,
                });
            }
            return ergebnis.Count == 0 ? null : ergebnis.ToArray();
        }
    }
}
