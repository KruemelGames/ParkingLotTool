using System;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    [Flags]
    public enum NebenarbeitPflicht { Keine = 0, Versorgung = 1, Bushalte = 2, Zoning = 4 }

    public struct ParkingLotNebenarbeitOffen : IComponentData, ISerializable
    {
        public NebenarbeitPflicht Offen;
        public bool VersorgungBekannt;
        public void Serialize<W>(W w) where W : IWriter
        { w.Write(2); w.Write((int)Offen); w.Write(VersorgungBekannt); }
        public void Deserialize<R>(R r) where R : IReader
        {
            r.Read(out int version);
            // V1 kannte die damalige Auto-Versorgungswahl nicht.
            Offen = NebenarbeitPflicht.Bushalte | NebenarbeitPflicht.Zoning;
            VersorgungBekannt = false;
            if (version >= 2) { r.Read(out int offen); Offen = (NebenarbeitPflicht)offen; r.Read(out VersorgungBekannt); }
        }
    }

    internal static class ParkingLotNebenarbeitDaten
    {
        internal static void Diagnose(Action aktion) { try { aktion(); } catch (Exception) { } }

        internal static bool Generation(bool bereit, long aktuell, long gespeichert)
            => bereit && aktuell == gespeichert;

        internal static void Merke(EntityManager em, Entity lot, bool versorgung)
        {
            if (em.HasComponent<ParkingLotNebenarbeitOffen>(lot)) return;
            em.AddComponentData(lot, new ParkingLotNebenarbeitOffen
            {
                Offen = NebenarbeitPflicht.Bushalte | NebenarbeitPflicht.Zoning
                    | (versorgung ? NebenarbeitPflicht.Versorgung : NebenarbeitPflicht.Keine),
                VersorgungBekannt = true,
            });
        }
        internal static bool Offen(EntityManager em, Entity lot, NebenarbeitPflicht pflicht)
            => em.Exists(lot) && em.HasComponent<ParkingLotNebenarbeitOffen>(lot)
                && (em.GetComponentData<ParkingLotNebenarbeitOffen>(lot).Offen & pflicht) != 0;
        internal static void Quittiere(EntityManager em, Entity lot, NebenarbeitPflicht pflicht)
        {
            if (!em.Exists(lot) || !em.HasComponent<ParkingLotNebenarbeitOffen>(lot)) return;
            var m = em.GetComponentData<ParkingLotNebenarbeitOffen>(lot);
            m.Offen &= ~pflicht;
            // Der Worker entfernt den leeren Marker erst beim Abschluss.
            em.SetComponentData(lot, m);
        }
    }
}
