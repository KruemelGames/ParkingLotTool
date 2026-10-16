using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotBestandsTauschSystem
    {
        private Auftragskennung _kennung;
        private ParkingLotAuftragsSystem Auftraege => World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();

        internal bool Einreihen(Entity lot, Tauschart art, Auftragskennung id)
        {
            if (_warteschlange.Any(a => a.Lot == lot && a.Art == art)
                || (_laeuft?.Lot == lot && _laeuft.Art == art)
                || (_imWerkzeug.HasValue && _imWerkzeug.Value.Lot == lot && _imWerkzeug.Value.Art == art)) return false;
            if (!Auftraege.Annehmen(id, lot, Auftragsarbeiter.Bestand)) return false;
            _warteschlange.Add((lot, art, id));
            Diagnose(() => Mod.log.Info($"PLT-Bestandstausch: Lot {lot.Index} ({art}) eingereiht; offen {Offen}."));
            return true;
        }

        private bool RechnungLesen(Vorbereitung v, out Ergebnis e)
        {
            e = null;
            if (_kennung != v.Kennung || !Auftraege.DarfAnwenden(v.Kennung))
            { if (ReferenceEquals(_laeuft, v)) _laeuft = null; return false; }
            if (!v.Rechnung.IsCompleted) return false;
            try { e = v.Rechnung.GetAwaiter().GetResult(); return true; }
            catch (Exception x)
            {
                _laeuft = null;
                Fertig(v.Lot, v.Kennung, false, "Rechnung fehlgeschlagen: " + x.Message);
                return false;
            }
        }

        private void Fertig(Entity lot, Auftragskennung id, bool ok, string text)
        {
            if (_kennung != id || !Auftraege.DarfAnwenden(id)
                || !Auftraege.Register.Lies(id, out var a) || a.Lot != lot) return;
            _kennung = default;
            bool frei = false;
            try { Freigeben(ok); frei = true; }
            finally
            {
                Diagnose(() => Mod.log.Info($"PLT-Bestandstausch: Lot {lot.Index}: {(ok ? "fertig" : "nicht getauscht")} - {text}."));
                Auftraege.Beende(id, lot, ok && frei);
            }
        }

        private static void Diagnose(Action aktion)
        {
            try { aktion(); }
            catch (Exception) { }
        }
    }
}
