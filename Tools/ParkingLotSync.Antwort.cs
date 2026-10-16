using System;
using Game.Common;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotSyncSystem
    {
        internal void MeldeHintergrundEnde(Entity lot, Entity neu, bool erfolgreich)
        {
            // Noch nicht umgestellte Worker bleiben auf ihrem bisherigen Pfad.
            // Eine nackte Entity-Antwort darf keinen registrierten Auftrag treffen.
            if (_auftragsWarten.ContainsKey(lot)) return;
            VerbucheHintergrundEnde(lot, neu, erfolgreich);
        }

        private bool VerbucheHintergrundEnde(Entity lot, Entity neu, bool erfolgreich)
        {
            var erwartet = _wartetAuf.TryGetValue(lot, out var schrittNummer);
            if (!SyncWache.AntwortErwartet(_spielGeladen, erwartet))
            { if (_spielGeladen) NeuAufnehmen(); return false; }
            var ziel = neu != Entity.Null && EntityManager.Exists(neu)
                && !EntityManager.HasComponent<Deleted>(neu) ? neu : lot;
            try
            {
                erfolgreich = erfolgreich && EntityManager.Exists(neu)
                    && !EntityManager.HasComponent<Deleted>(neu)
                    && EntityManager.HasComponent<ParkingLotCarrierReference>(neu);
                if (erfolgreich)
                    erfolgreich = ParkingLotSyncGueltigkeit.Bestand(EntityManager, neu,
                        EntityManager.GetComponentData<ParkingLotCarrierReference>(neu).Carrier);
                // Bestaetigte Wirkung zuerst schreiben; die Zuordnung bleibt bis dahin bestehen.
                if (erfolgreich) SetzeStand(neu, schrittNummer);
                else if (ziel != lot) SetzeStand(ziel, schrittNummer - 1);
            }
            catch (Exception x)
            {
                erfolgreich = false;
                Abschlussdiagnose(() => Mod.log.Warn("PLT-Sync: Abschlusswirkung fehlgeschlagen: " + x));
            }
            _wartetAuf.Remove(lot);
            bool weiter = _fortsetzen.Remove(lot);
            UebertrageNutzer(lot, ziel);
            _offen.Remove(lot); _offenMenge.Remove(lot);
            if (erfolgreich)
            {
                if (weiter) _fortsetzen.Add(neu);
                _neuAufnehmen = true; _index = null;
                if (Mod.Optionen?.AutomatischSynchronisieren ?? false)
                {
                    _meldungSync++;
                    Abschlussdiagnose(VeroeffentlicheMeldung);
                }
            }
            else
            {
                if (EntityManager.Exists(ziel) && !EntityManager.HasComponent<Deleted>(ziel)) _gescheitert.Add(ziel);
                Abschlussdiagnose(() => Mod.log.Warn("PLT-Sync: Hintergrundauftrag (Schritt " + schrittNummer + ") von " + lot
                    + " nicht abgeschlossen; " + ziel + " bleibt auf seinem Datenstand, neuer Versuch nach dem naechsten Laden."));
            }
            return erfolgreich;
        }
    }
}
