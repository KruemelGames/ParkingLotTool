using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal static partial class ParkingLotKurvenzettelPuffer
    {
        internal static void Schreibe(EntityManager em, Entity lot, IReadOnlyList<Kurvenzettel> kurven)
        {
            if (kurven == null || kurven.Count == 0)
            {
                if (em.HasBuffer<ParkingLotBuildCurve>(lot)) em.RemoveComponent<ParkingLotBuildCurve>(lot);
                if (em.HasBuffer<ParkingLotBuildCurveSection>(lot)) em.RemoveComponent<ParkingLotBuildCurveSection>(lot);
                if (em.HasBuffer<ParkingLotBuildCurveStation>(lot)) em.RemoveComponent<ParkingLotBuildCurveStation>(lot);
                return;
            }
            var kanten = em.HasBuffer<ParkingLotBuildCurve>(lot)
                ? em.GetBuffer<ParkingLotBuildCurve>(lot) : em.AddBuffer<ParkingLotBuildCurve>(lot);
            kanten.Clear();
            var abschnitte = new List<ParkingLotBuildCurveSection>();
            var stationen = new List<ParkingLotBuildCurveStation>();
            foreach (var k in kurven)
            {
                if (k == null) continue;
                kanten.Add(new ParkingLotBuildCurve
                {
                    Version = ParkingLotBuildCurve.CurrentVersion,
                    Von = k.Von, Bis = k.Bis, Zwischen = k.Zwischen,
                    ErsterAbschnitt = abschnitte.Count, Abschnitte = k.AbschnittAnzahl,
                    ErsteStation = stationen.Count,
                    Laenge = k.Laenge, RadiusMin = k.RadiusMin, Winkel = k.Winkel,
                    Pfeilhoehe = k.Pfeilhoehe, Wendepunkte = k.Wendepunkte,
                    Toleranz = k.Toleranz, MinSehne = k.MinSehne, MaxSehne = k.MaxSehne,
                });
                for (int i = 0; i + 3 < k.Abschnitte.Length; i += 4)
                    abschnitte.Add(new ParkingLotBuildCurveSection
                    {
                        Version = ParkingLotBuildCurveSection.CurrentVersion,
                        A = k.Abschnitte[i], B = k.Abschnitte[i + 1],
                        C = k.Abschnitte[i + 2], D = k.Abschnitte[i + 3],
                    });
                foreach (var b in k.Bogen)
                    stationen.Add(new ParkingLotBuildCurveStation
                    {
                        Version = ParkingLotBuildCurveStation.CurrentVersion, Bogen = b,
                    });
            }
            // Erst nach dem Sammeln holen: AddBuffer kann andere Puffer desselben Lots verschieben.
            var abschnittPuffer = em.HasBuffer<ParkingLotBuildCurveSection>(lot)
                ? em.GetBuffer<ParkingLotBuildCurveSection>(lot) : em.AddBuffer<ParkingLotBuildCurveSection>(lot);
            abschnittPuffer.Clear();
            foreach (var a in abschnitte) abschnittPuffer.Add(a);
            var stationPuffer = em.HasBuffer<ParkingLotBuildCurveStation>(lot)
                ? em.GetBuffer<ParkingLotBuildCurveStation>(lot) : em.AddBuffer<ParkingLotBuildCurveStation>(lot);
            stationPuffer.Clear();
            foreach (var st in stationen) stationPuffer.Add(st);
        }
    }
}
