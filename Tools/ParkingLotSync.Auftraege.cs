using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotSyncSystem
    {
        private readonly Dictionary<Entity, Auftragskennung> _auftragsWarten = new();
        private readonly Dictionary<Entity, long> _nutzerAuftraege = new();
        private ParkingLotAuftragsSystem Auftraege => World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();

        private long NutzerVon(Entity lot)
        {
            if (!_nutzerAuftraege.TryGetValue(lot, out var nutzer))
                _nutzerAuftraege[lot] = nutzer = Auftraege.Register.NeuerNutzer();
            return nutzer;
        }

        private void PflegeAuftraege()
        {
            foreach (var lot in new List<Entity>(_nutzerAuftraege.Keys))
                if (!_warteschlange.Contains(lot) && !_wartetAuf.ContainsKey(lot) && !_fortsetzen.Contains(lot)) NutzerAbgeschlossen(lot, !_gescheitert.Contains(lot));
        }

        private void NutzerAbgeschlossen(Entity lot, bool ok)
        {
            if (_nutzerAuftraege.TryGetValue(lot, out var nutzer) && Auftraege.Register.NutzerBeendet(nutzer, ok))
                _nutzerAuftraege.Remove(lot);
        }

        private void UebertrageNutzer(Entity lot, Entity neu)
        {
            if (lot == neu || neu == Entity.Null || !_nutzerAuftraege.TryGetValue(lot, out var nutzer)) return;
            _nutzerAuftraege.Remove(lot);
            _nutzerAuftraege[neu] = nutzer;
        }

        private void AsynchronenSchrittStarten(Entity lot, int schritt, Auftragsarbeiter? arbeiter,
            Func<Entity, Auftragskennung, bool> uebergabe)
        {
            if (!arbeiter.HasValue || uebergabe == null) throw new InvalidOperationException("Asynchroner Schritt ohne Register-Adapter.");
            _wartetAuf.Add(lot, schritt);
            _fortsetzen.Add(lot);
            _offen.Add(lot); _offenMenge.Add(lot);
            RegistriertenSchrittStarten(lot, schritt, arbeiter.Value, uebergabe);
        }

        private void RegistriertenSchrittStarten(Entity lot, int schritt, Auftragsarbeiter arbeiter,
            Func<Entity, Auftragskennung, bool> uebergabe)
        {
            var id = Auftraege.Merke(lot, arbeiter, Auftragsgrund.Migration, schritt, NutzerVon(lot), AuftragsAntwort);
            _auftragsWarten.Add(lot, id);
            Auftraege.Uebergib(id, kennung => uebergabe(lot, kennung));
        }

        private bool AuftragsAntwort(Bauauftrag<Entity> auftrag, Entity neu, bool ok)
        {
            if (auftrag.Grund != Auftragsgrund.Migration
                || !_auftragsWarten.TryGetValue(auftrag.Lot, out var id) || id != auftrag.Kennung
                || !_wartetAuf.TryGetValue(auftrag.Lot, out var schritt) || schritt != auftrag.Schritt) return false;
            try { return VerbucheHintergrundEnde(auftrag.Lot, neu, ok); }
            finally { _auftragsWarten.Remove(auftrag.Lot); }
        }

        private static void Abschlussdiagnose(Action diagnose)
        {
            try { diagnose(); }
            catch (Exception x)
            {
                try { Mod.log.Warn("PLT-Sync: Abschlussdiagnose fehlgeschlagen: " + x); }
                catch (Exception) { }
            }
        }
    }
}
