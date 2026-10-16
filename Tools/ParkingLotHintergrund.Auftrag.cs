using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotHintergrundSystem
    {
        internal bool IstErsatz(Entity lot) => _aktiv != null && !_aktiv.NurNebenarbeit && _aktiv.Neu == lot
            || _queue.Any(a => !a.NurNebenarbeit && a.Neu == lot);

        private ParkingLotAuftragsSystem Auftraege => World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();

        internal void Einreihen(Entity lot, bool rueckweg = false, int fruehestens = 0,
            Auftragsgrund? grund = null)
        {
            // Ein schon vorgemerkter Auftrag behaelt beim Edit-Abbruch seine Kennung.
            if (_angehalten.Contains(lot)) { Sperrmeldung(lot); return; }
            var wartend = _queue.Find(a => a.Lot == lot);
            if (rueckweg && wartend != null)
            {
                if (wartend.NurNebenarbeit) { wartend.NurNebenarbeit = false; wartend.Neu = Entity.Null; }
                wartend.NurRueckweg = true; return;
            }
            if (_regel.Gesperrt(lot)) return;
            var id = Auftraege.Merke(lot, Auftragsarbeiter.Hintergrund,
                grund ?? (rueckweg ? Auftragsgrund.Normalbau : Auftragsgrund.Repair));
            Auftraege.Uebergib(id, kennung => Einreihen(lot, kennung, rueckweg, fruehestens));
        }

        internal bool Einreihen(Entity lot, Auftragskennung id, bool rueckweg = false, int fruehestens = 0)
            => EinreihenKern(lot, id, rueckweg, fruehestens, false);

        private bool EinreihenKern(Entity lot, Auftragskennung id, bool rueckweg, int fruehestens, bool fortsetzung)
        {
            if (!Auftraege.Register.Lies(id, out var daten) || daten.Lot != lot
                || daten.Arbeiter != Auftragsarbeiter.Hintergrund
                || (fortsetzung ? !Auftraege.DarfAnwenden(id) : daten.Phase != Auftragsphase.Vorgemerkt)) return false;
            if (_angehalten.Contains(lot)) { Sperrmeldung(lot); return false; }
            if (!rueckweg && !KannNeubauen(lot, out var grund))
            { World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeBauplanFehlt(lot, grund); return false; }
            // Vor der Annahme lesen: eine fehlgeschlagene Uebergabe besitzt keinen Auftrag.
            var a = new Auftrag { Lot = lot, Kennung = id, Id = ++_nummer,
                NurRueckweg = rueckweg, Fruehestens = fruehestens };
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(lot))
            {
                var ersatz = EntityManager.GetComponentData<ParkingLotOffenerErsatz>(lot);
                a.Neu = ersatz.Lot; a.Traeger = ersatz.Traeger;
            }
            if (rueckweg ? !_regel.EinreihenRueckweg(lot) : !_regel.Einreihen(lot)) return false;
            if (!fortsetzung && !Auftraege.Annehmen(id, lot, Auftragsarbeiter.Hintergrund))
            { _regel.Ende(lot); return false; }
            bool erster = Offen == 0;
            _queue.Add(a);
            if (rueckweg) Diagnose(() => World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeRueckwegStart(erster));
            Diagnose(() => ParkingLotNetzRueckweg.Melde($"Auftrag {a.Id} Lot {lot.Index} eingereiht; Rueckweg={rueckweg}; Register {id}; offen {Offen}."));
            return true;
        }

        internal void EinreihenNebenarbeit(Entity lot)
        {
            if (_regel.Gesperrt(lot) || _angehalten.Contains(lot)
                || !EntityManager.HasComponent<ParkingLotNebenarbeitOffen>(lot)) return;
            var id = Auftraege.Merke(lot, Auftragsarbeiter.Hintergrund, Auftragsgrund.Wiederaufnahme);
            Auftraege.Uebergib(id, kennung =>
            {
                if (!KannNeubauen(lot, out _) || !_regel.EinreihenRueckweg(lot)) return false;
                if (!Auftraege.Annehmen(kennung, lot, Auftragsarbeiter.Hintergrund)) { _regel.Ende(lot); return false; }
                _queue.Add(new Auftrag { Lot = lot, Neu = lot, Kennung = kennung, Id = ++_nummer, NurNebenarbeit = true });
                Diagnose(() => ParkingLotNetzRueckweg.Melde($"Nebenarbeit Lot {lot.Index} wieder eingereiht; Register {kennung}; kein Parkplatz-Neubau."));
                return true;
            });
        }

        private bool Beginne(Auftrag a)
        {
            if (!Auftraege.Register.Lies(a.Kennung, out var daten) || daten.Lot != a.Lot
                || daten.Arbeiter != Auftragsarbeiter.Hintergrund) return false;
            return Auftraege.DarfAnwenden(a.Kennung) || Auftraege.Starte(a.Kennung);
        }

        private bool Aktuell(Auftrag a)
            => a != null && !a.Beendet && ReferenceEquals(_aktiv, a) && Auftraege.DarfAnwenden(a.Kennung);

        private void SetzeFort(Auftrag a)
        {
            // Ein physischer Versuch endet, der Nutzerauftrag bleibt derselbe.
            if (!a.Beendet || !a.Fortsetzen || !Auftraege.DarfAnwenden(a.Kennung)) return;
            bool weiter = false;
            try { weiter = EinreihenKern(a.Lot, a.Kennung, false,
                UnityEngine.Time.frameCount + HintergrundTakt.Versuchspause, true); }
            finally { if (!weiter) Auftraege.Beende(a.Kennung, Entity.Null, false); }
        }

        private void Ende(Auftrag a)
        {
            if (!Aktuell(a)) return;
            a.Beendet = true;
            bool freigegeben = false;
            try { Freigeben(a); freigegeben = true; }
            finally
            {
                _regel.Ende(a.Lot);
                _aktiv = null;
                _naechsterStart = UnityEngine.Time.frameCount + HintergrundTakt.Auftragspause;
                if (!freigegeben) a.Fortsetzen = false;
                if (!a.Fortsetzen)
                {
                    // Ein erfolgreicher Rueckweg bestaetigt keine Migration.
                    bool ok = freigegeben && a.Erfolg && (!a.NurRueckweg
                        || Auftraege.Register.Lies(a.Kennung, out var daten) && daten.Grund != Auftragsgrund.Migration);
                    Auftraege.Beende(a.Kennung, a.Neu, ok);
                    Diagnose(() => World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().NeuAufnehmen());
                }
            }
        }

        private static void Diagnose(Action aktion)
        {
            try { aktion(); }
            catch (Exception) { }
        }

        private void Exklusiv(Auftrag a, Func<int> arbeit)
        {
            if (!Aktuell(a)) return;
            a.GateOffen = true;
            _gate.Erwarte(a.Lot, _ =>
            {
                if (!Aktuell(a)) return 0;
                a.GateOffen = false;
                try { int n = 0; Messe(a,"Exklusiv",() => n = arbeit()); return n; }
                catch (Exception e) { Fehler(a,e); return 0; }
            });
        }

    }
}
