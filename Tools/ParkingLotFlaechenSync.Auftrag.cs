using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotFlaechenSyncSystem
    {
        private sealed class Lauf
        {
            internal Auftragskennung Kennung;
            internal Entity Lot;
            internal Entity Traeger;
            internal readonly List<Entity> Alt = new();
            internal readonly HashSet<Entity> Vorher = new();
            internal int Neu;
            internal int Seit;
            internal double Verloren;
            // Schritt 16: Flaechen neu aufbauen (ParkingLotFlaechenSync.Neuaufbau.cs).
            internal bool Neuaufbau;
            internal bool Angelegt;
            internal Entity Besitzer;
            internal System.Threading.Tasks.Task<ParkingLayout> Rechnung;
        }

        private readonly List<(Entity Lot, Auftragskennung Kennung, bool Neuaufbau)> _warteschlange = new();
        private Lauf _lauf;

        internal int Offen => _warteschlange.Count + (_lauf != null ? 1 : 0);
        internal bool Laeuft => _lauf != null;

        private ParkingLotAuftragsSystem Auftraege => World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();

        internal bool Einreihen(Entity lot, Auftragskennung id) => Einreihen(lot, id, false);

        internal bool EinreihenNeuaufbau(Entity lot, Auftragskennung id) => Einreihen(lot, id, true);

        private bool Einreihen(Entity lot, Auftragskennung id, bool neuaufbau)
        {
            if (_warteschlange.Any(a => a.Lot == lot) || _lauf?.Lot == lot) return false;
            if (!Auftraege.Annehmen(id, lot, Auftragsarbeiter.Flaechen)) return false;
            _warteschlange.Add((lot, id, neuaufbau));
            Diagnose(() => Mod.log.Info($"PLT-Flaechensync: Lot {lot.Index} eingereiht; offen {Offen}."));
            return true;
        }

        private bool Beginne(Entity lot, Auftragskennung id, int bild, bool neuaufbau)
        {
            if (!Auftraege.Register.Lies(id, out var a) || a.Lot != lot
                || a.Arbeiter != Auftragsarbeiter.Flaechen || !Auftraege.Starte(id)) return false;
            _lauf = new Lauf { Lot = lot, Kennung = id, Seit = bild, Neuaufbau = neuaufbau };
            return true;
        }

        private bool LaufGueltig() => _lauf != null && Auftraege.DarfAnwenden(_lauf.Kennung);

        /*
         * JEDE AUSNAHME BEENDET DEN LAUF (Sync-Haenger, 2026-10-12).
         * Warf Starte oder Pruefe, blieb `_lauf` stehen: der Schritt meldete
         * sich nie fertig und der Sync wartete fuer immer. Hat Beende schon
         * gemeldet (`_lauf` ist dann null), wird nicht ein zweites Mal gemeldet.
         */
        private void BeendeNachAusnahme(string wo, Exception x)
        {
            Diagnose(() => Mod.log.Warn($"PLT-Flaechensync: Ausnahme beim {wo}: {x}"));
            if (_lauf != null) Beende(false, wo + " fehlgeschlagen: " + x.Message);
        }

        private void Beende(bool ok, string text)
        {
            if (!LaufGueltig()) return;
            var a = _lauf;
            _lauf = null;
            Diagnose(() => Mod.log.Info($"PLT-Flaechensync: Lot {a.Lot.Index}: {(ok ? "fertig" : "nicht abgeschlossen")} - {text}."));
            Auftraege.Beende(a.Kennung, a.Lot, ok);
        }

        private static void Diagnose(Action aktion)
        {
            try { aktion(); }
            catch (Exception) { }
        }
    }
}
