using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * STRASSENANBINDUNG DES BEGLEITERS IST NOCH OFFEN (2026-10-16).
     *
     * Beim Neubau setzt PLT den Begleiter an die Strasse und wartet bis zu
     * 600 Bilder, bis CS2 ihn anbindet. Dieser Auftrag lag nur im Speicher:
     * ein Speichern in der Zeit hinterliess einen Begleiter ohne Strasse,
     * den nach dem Laden niemand mehr anfasste (geladene Begleiter gelten
     * als "wiedergefunden", der Sync uebersprang den aktuellen Datenstand).
     * Die Markierung am Begleiter uebersteht das Speichern; nach dem Laden
     * wird der Anschluss fortgesetzt. Begleiter ohne Markierung - etwa aus
     * alten Spielstaenden - werden bewusst NICHT umgesetzt.
     */
    public struct ParkingLotBegleiterAnschlussOffen : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(1);
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); }
    }

    public sealed partial class ParkingLotBuildingEconomySystem
    {
        private EntityQuery _offeneAnschluesse;
        private bool _offeneAnschluesseBereit, _wiederaufnahmeGeprueft;

        private void MarkiereAnschlussOffen(Entity begleiter)
        {
            if (Lebt(begleiter) && !EntityManager.HasComponent<ParkingLotBegleiterAnschlussOffen>(begleiter))
                EntityManager.AddComponent<ParkingLotBegleiterAnschlussOffen>(begleiter);
        }

        /** Die Pflicht ist erledigt (Strasse steht) oder an den Datenstand uebergeben (Zeitablauf). */
        private void SchliesseAnschlussOffen(Entity begleiter)
        {
            if (begleiter != Entity.Null && EntityManager.Exists(begleiter)
                && EntityManager.HasComponent<ParkingLotBegleiterAnschlussOffen>(begleiter))
                EntityManager.RemoveComponent<ParkingLotBegleiterAnschlussOffen>(begleiter);
        }

        /** Einmal nach jedem Laden: offene Anschluesse als Wiederaufnahme fortsetzen. */
        private void NimmOffeneAnschluesseWiederAuf()
        {
            if (_wiederaufnahmeGeprueft) return;
            _wiederaufnahmeGeprueft = true;
            if (!_offeneAnschluesseBereit)
            {
                _offeneAnschluesse = EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<ParkingLotBegleiterAnschlussOffen>(),
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                _offeneAnschluesseBereit = true;
            }
            int fortgesetzt = 0, erledigt = 0;
            var begleiterListe = new List<Entity>();
            using (var alle = _offeneAnschluesse.ToEntityArray(Allocator.Temp))
                foreach (var b in alle) begleiterListe.Add(b);
            foreach (var begleiter in begleiterListe)
            {
                var lot = EntityManager.GetComponentData<ParkingLotPartRelation>(begleiter).Lot;
                if (HatStrasse(begleiter)) { SchliesseAnschlussOffen(begleiter); erledigt++; continue; }
                if (!Lebt(lot) || _anschluesse.ContainsKey(lot)) continue;
                var a = Normalanschluss(lot, Auftragsgrund.Wiederaufnahme);
                if (a == null) continue;
                a.Platziert = false;
                fortgesetzt++;
            }
            if (fortgesetzt + erledigt > 0)
                Mod.log.Info($"PLT-Begleiteranschluss: Laden: {fortgesetzt} offene Anbindung(en) fortgesetzt, "
                    + $"{erledigt} inzwischen von CS2 bestaetigt.");
        }
    }
}
