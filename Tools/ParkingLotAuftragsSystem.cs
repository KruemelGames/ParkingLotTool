using System;
using System.Collections.Generic;
using Game;
using ParkingLotTool.Geometry;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Duenn am Weltzugriff: Kennungen leben nie im Save. */
    public sealed partial class ParkingLotAuftragsSystem : GameSystemBase
    {
        internal readonly Auftragsregister<Entity> Register = new();
        private readonly Dictionary<Auftragskennung, Func<Bauauftrag<Entity>, Entity, bool, bool>> _antworten = new();
        private static double Jetzt => UnityEngine.Time.realtimeSinceStartup;

        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // Einziger Generationenwechsel. Speichern ruft diesen Pfad nicht auf.
            Register.NeuesLaden(Jetzt);
            _antworten.Clear();
        }
        protected override void OnUpdate() { }

        internal void PufferWiederhergestellt() => Register.PufferWiederhergestellt();

        internal Auftragskennung Merke(Entity lot, Auftragsarbeiter arbeiter, Auftragsgrund grund,
            int schritt = 0, long nutzer = 0, Func<Bauauftrag<Entity>, Entity, bool, bool> antwort = null)
        {
            /*
             * NIE WERFEN, WENN DAS REGISTER NICHT BEREIT IST.
             *
             * Zwischen OnGamePreload und der Puffer-Wiederherstellung ist es
             * das nicht - und genau dort reiht ein Edit-Abbruch beim
             * Spielstandwechsel seinen Rueckweg ein. Eine Ausnahme in einem
             * Lade-Ereignis laesst CS2 das ganze aufrufende System abschalten
             * ("Error on game preload, disabling system"). Eine leere Kennung
             * wird von Uebergib/Beende/Lies abgewiesen; was gespeichert ist,
             * nimmt die Wiederaufnahme nach dem Laden selbst wieder auf.
             */
            if (!Register.Bereit)
            {
                try { Mod.log.Warn("PLT-Auftrag: Register nicht bereit (Laden laeuft); " + arbeiter + "/" + grund
                    + " fuer Lot " + lot.Index + " nicht angenommen."); }
                catch (Exception) { }
                return default;
            }
            var id = Register.Merke(lot, arbeiter, grund, schritt, Jetzt, nutzer);
            if (antwort != null) _antworten.Add(id, antwort);
            return id;
        }

        internal bool Annehmen(Auftragskennung id, Entity lot, Auftragsarbeiter arbeiter)
            => Register.Annehmen(id, lot, arbeiter, Jetzt);
        internal bool Starte(Auftragskennung id) => Register.Starte(id, Jetzt);
        internal bool Gilt(Auftragskennung id) => Register.Lies(id, out _);
        internal bool DarfAnwenden(Auftragskennung id) => Register.DarfAnwenden(id);

        internal bool Uebergib(Auftragskennung id, Func<Auftragskennung, bool> uebergabe)
        {
            if (!Register.Lies(id, out var a) || a.Phase != Auftragsphase.Vorgemerkt) return false;
            try
            {
                bool angenommen = uebergabe(id);
                if (!Register.Lies(id, out a)) return true; // synchron beendet
                if (a.Phase != Auftragsphase.Vorgemerkt) return true; // bestaetigte Annahme ist verbindlich
                if (angenommen)
                {
                    try { Mod.log.Warn("PLT-Auftrag " + id + ": Uebergabe ohne Annahmebestaetigung."); }
                    catch (Exception) { }
                }
            }
            catch (Exception x)
            {
                try { Mod.log.Warn("PLT-Auftrag " + id + ": Uebergabe warf: " + x); }
                catch (Exception) { }
                if (!Register.Lies(id, out a)) return true; // Antwort hat bereits gewirkt
                if (a.Phase != Auftragsphase.Vorgemerkt) return true; // Worker besitzt den Auftrag
            }
            // Keine Frist: eine nicht bestaetigte Uebergabe wurde nie angenommen.
            Beende(id, Entity.Null, false);
            return false;
        }

        internal bool Beende(Auftragskennung id, Entity neu, bool ok)
        {
            if (!Register.Lies(id, out var a)) return false;
            bool bestaetigt = a.Phase != Auftragsphase.Vorgemerkt;
            return Register.Beende(id, Jetzt, auftrag =>
            {
                _antworten.TryGetValue(id, out var antwort);
                _antworten.Remove(id);
                return antwort != null ? antwort(auftrag, neu, ok && bestaetigt) : ok && bestaetigt;
            });
        }
    }
}
