using System.Collections.Generic;
using System.Diagnostics;
using Colossal.UI.Binding;
using Game;
using Game.Common;
using Game.Tools;
using Game.UI;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * "SYNCHRONISIEREN": bestehende Parkplaetze auf den Stand dieser Version
     * nachruesten.
     *
     * Ansage des Nutzers am 2026-09-24: nicht aggressiv durchholzen, damit
     * das Spiel nicht stottert. Einstellung "automatisch synchronisieren",
     * Standard AUS, in den Optionen und in der Liste. An: laeuft von selbst,
     * unten eine kleine Fortschrittsmeldung. Aus: Hinweis im Panelkopf,
     * in der Liste je Parkplatz oder alle auf einmal.
     *
     * ABLAUF. Nach dem Laden einmal ein Index Parkplatz -> Teile (ein
     * Durchlauf ueber alle Teilrelationen). Je Parkplatz der Datenstand;
     * fuer jeden Schritt danach fragt `Braucht`, ob es hier etwas zu tun
     * gibt. Gibt es nichts, wird nur der Stand hochgesetzt - kein Eintrag
     * in der Liste, keine Meldung. Gibt es etwas, steht der Parkplatz als
     * offen da. Abgearbeitet wird mit Zeitbudget je Bild.
     *
     * Ein Schritt, dessen Nachpruefung scheitert, haelt NUR diesen
     * Parkplatz an; er bleibt auf seinem Stand und wird bis zum naechsten
     * Laden nicht erneut versucht.
     */
    public sealed partial class ParkingLotSyncSystem : UISystemBase
    {
        private const string Group = "ParkingLotTool";

        /** Hoechstens so viel Zeit je Bild. Mindestens ein Parkplatz geht immer. */
        private const double BudgetMs = 2.0;

        private ParkingLotToolSystem _werkzeug;
        private EntityQuery _lots;
        private EntityQuery _teile;

        private readonly List<Entity> _offen = new List<Entity>();
        private readonly HashSet<Entity> _offenMenge = new HashSet<Entity>();
        private readonly HashSet<Entity> _gescheitert = new HashSet<Entity>();
        private readonly HashSet<Entity> _ohneBauplan = new HashSet<Entity>();
        /**
         * Auf welchen Schritt ein Parkplatz gerade im Hintergrund wartet. Bis
         * 2026-10-04 setzte ein Erfolg den Stand auf `Aktuell` - richtig, solange
         * der Tausch der letzte Schritt war; mit Schritt 10 haette ein Parkplatz,
         * der noch Schritt 9 brauchte, Schritt 10 still uebersprungen.
         */
        private readonly Dictionary<Entity, int> _wartetAuf = new Dictionary<Entity, int>();
        /**
         * Parkplaetze, deren Sync angestossen wurde und die nach einem
         * Hintergrundauftrag mit den restlichen Schritten weiterlaufen. Vorher
         * endete der Durchgang am Tausch, und der Nutzer musste fuer jeden
         * weiteren Schritt erneut druecken (2026-10-04: "Mach alles auf einmal").
         */
        private readonly HashSet<Entity> _fortsetzen = new HashSet<Entity>();
        // Keine Queue<T>: der Typ steht in CS2 in System UND mscorlib.
        private readonly List<Entity> _warteschlange = new List<Entity>();
        private int _gesamt;
        private int _erledigt;
        private int _syncErfolge;
        private bool _neuAufnehmen;
        private int _letzterBestand = -1;

        /**
         * Erst nach dem Ladeabschluss arbeiten. Gemessen am 2026-09-25: der
         * Bestand aendert sich schon WAEHREND des Ladens (0 -> 1), und die
         * Aufnahme lief, bevor die Traeger ihren SubNet-Puffer zurueck
         * hatten - ein halb geladener Zustand.
         */
        private bool _spielGeladen;

        /**
         * Parkplatz -> Teile, EINMAL je Durchgang. Vorher entstand er in
         * jedem Bild neu - bei zehntausenden Teilen teurer als das ganze
         * Budget. Verworfen, wenn sich der Bestand aendert oder die
         * Warteschlange leer ist; Teile, die inzwischen fehlen, werden beim
         * Benutzen uebersprungen.
         */
        private Dictionary<Entity, List<Entity>> _index;

        private ValueBinding<int> _syncOffen;
        /**
         * Was in der Parkplatzliste zu tun ist: synchronisieren, reparieren,
         * Bauplan wiederherstellen. Der Reiter "Parkplaetze" zeigt die Zahl und
         * faerbt sich (Nutzer 2026-10-04). Jedes Bild neu gezaehlt - nach dem
         * Sync oder der Reparatur verschwindet der Hinweis von selbst.
         */
        private ValueBinding<int> _arbeitOffen;
        private ValueBinding<string> _syncLaeuft;
        private ValueBinding<int> _syncPuls;
        private ValueBinding<bool> _syncAuto;

        /**
         * Anzahl des zuletzt BEENDETEN Durchgangs, als Text; "" = nichts
         * zeigen. Ein kleiner Durchgang ist im selben Bild fertig, in dem er
         * beginnt - `SyncLaeuft` wird dann nie sichtbar (gemessen
         * 2026-09-25, 1 Parkplatz in 1,9 ms).
         *
         * DIE ANZEIGEDAUER ENTSCHEIDET C#, NICHT DIE OBERFLAECHE. Die erste
         * Fassung liess die Oberflaeche den Wechsel erkennen und alles
         * verwerfen, was beim Einhaengen schon da war - und die Oberflaeche
         * im Spiel haengt sich genau dann ein, wenn der Ladeabschluss die
         * Synchronisation ausloest. Die Meldung wurde verschluckt.
         */
        private ValueBinding<string> _syncErgebnis;
        private readonly Stopwatch _ergebnisUhr = new Stopwatch();
        // 30 s (Nutzer 2026-10-04: "10 sind echt wenig, damit der User das mitbekommt").
        private const double ErgebnisSekunden = 30.0;

        /*
         * EINE MELDUNG FUER ALLES, WAS VON SELBST PASSIERT (Nutzer,
         * 2026-09-25): synchronisierte Parkplaetze, reparierte Waisen und
         * wiederhergestellte Bauplaene stehen zusammen darin, nicht in zwei
         * Meldungen hintereinander. Jeder neue Beitrag verlaengert die
         * 10 Sekunden - die Waisen werden einzeln je Bild repariert, die
         * Synchronisation folgt danach.
         */
        private int _meldungSync;
        private int _meldungWaisen;
        private int _meldungBauplaene;
        /*
         * OFFENE PARKPLAETZE NACH DEM LADEN - bei abgeschalteter Automatik.
         *
         * Bis 2026-10-02 stand das nur im Panelkopf; wer PLT nach dem Laden
         * nicht oeffnete, erfuhr nie, dass Reparaturen warten (Baeume
         * einfrieren, verstreute Ladesaeulen). Dieselbe Meldung unten mittig,
         * einmal je Laden.
         */
        private int _meldungOffen;

        /** Die Waisen-Automatik meldet hier, was sie getan hat. */
        internal void MeldeWaisenreparatur(bool verbunden, bool bauplan)
        {
            if (bauplan) _neuAufnehmen = true;
            if (verbunden) _meldungWaisen++;
            if (bauplan) _meldungBauplaene++;
            if (verbunden || bauplan) VeroeffentlicheMeldung();
        }

        private void VeroeffentlicheMeldung()
        {
            _ergebnisUhr.Restart();
            _syncErgebnis.Update(_meldungSync + "\t" + _meldungWaisen + "\t"
                + _meldungBauplaene + "\t" + _meldungOffen);
        }

        /*
         * "N PARKPLAETZE KOENNEN AKTUALISIERT WERDEN" IST VERALTET, SOBALD
         * SYNCHRONISIERT WIRD (1.0.6). Die Meldung steht 30 s; der
         * Fortschrittsbalken ueberdeckte sie nur und gab sie nach dem
         * Durchgang wieder frei - "5 koennen aktualisiert werden" direkt
         * nachdem alle 5 aktualisiert waren (Nutzer 2026-10-06).
         */
        private void VergissOffenMeldung()
        {
            if (_meldungOffen == 0) return;
            _meldungOffen = 0;
            if (_meldungSync + _meldungWaisen + _meldungBauplaene == 0) LeereMeldung();
            else VeroeffentlicheMeldung();
        }

        private void LeereMeldung()
        {
            _ergebnisUhr.Reset();
            _meldungSync = _meldungWaisen = _meldungBauplaene = _meldungOffen = 0;
            if (_syncErgebnis.value != string.Empty) _syncErgebnis.Update(string.Empty);
        }

        /** Fuer die Liste: braucht dieser Parkplatz eine Synchronisation? */
        internal bool BrauchtSync(Entity lot) => _offenMenge.Contains(lot);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _werkzeug = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotCarrierReference>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            _teile = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotPartRelation>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            LegeSchritteAn();

            AddBinding(_syncOffen = new ValueBinding<int>(Group, "SyncOffen", 0));
            AddBinding(_arbeitOffen = new ValueBinding<int>(Group, "ArbeitOffen", 0));
            AddBinding(_syncLaeuft = new ValueBinding<string>(Group, "SyncLaeuft",
                string.Empty));
            AddBinding(_syncPuls = new ValueBinding<int>(Group, "SyncPuls", 0));
            AddBinding(_syncErgebnis = new ValueBinding<string>(Group,
                "SyncErgebnis", string.Empty));
            AddBinding(new TriggerBinding(Group, "SyncErgebnisSchliessen", LeereMeldung));
            AddBinding(_syncAuto = new ValueBinding<bool>(Group, "SyncAuto",
                Mod.Optionen?.AutomatischSynchronisieren ?? false));
            AddBinding(new TriggerBinding<bool>(Group, "SetSyncAuto", wert =>
            {
                if (Mod.Optionen != null)
                {
                    Mod.Optionen.AutomatischSynchronisieren = wert;
                    Mod.Optionen.ApplyAndSave();
                }
                _syncAuto.Update(wert);
                if (wert) AlleEinreihen();
            }));
            AddBinding(new TriggerBinding<string>(Group, "ParkplatzSynchronisieren",
                schluessel =>
                {
                    if (VersucheSchluessel(schluessel, out var lot)) Einreihen(lot);
                    World.GetOrCreateSystemManaged<ParkingLotUISystem>().SchliesseWerkzeug();
                }));
            AddBinding(new TriggerBinding(Group, "ParkplaetzeSynchronisieren", () =>
            {
                AlleEinreihen();
                World.GetOrCreateSystemManaged<ParkingLotUISystem>().SchliesseWerkzeug();
            }));
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _offen.Clear();
            _offenMenge.Clear();
            _gescheitert.Clear();
            _ohneBauplan.Clear();
            _warteschlange.Clear();
            _wartetAuf.Clear();
            _fortsetzen.Clear();
            _gesamt = _erledigt = 0;
            _letzterBestand = -1;
            _neuAufnehmen = mode == GameMode.Game;
            _spielGeladen = mode == GameMode.Game;
            if (_spielGeladen)
                ParkingLotSchrittmarke.Setze("Laden: Sync vorbereitet");
        }

        [Preserve]
        protected override void OnGamePreload(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            if (mode == GameMode.Game)
                ParkingLotSchrittmarke.Setze("Laden: Sync.OnGamePreload");
            base.OnGamePreload(purpose, mode);
            _spielGeladen = false;
            _warteschlange.Clear();
            LeereMeldung();
            _offen.Clear();
            _offenMenge.Clear();
            _index = null;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();
            PflegeSchalter();
            if (!_spielGeladen)
            {
                Melde();
                return;
            }

            // Ein neuer Parkplatz (gebaut, repariert) oder ein abgerissener
            // aendert den Bestand - dann neu aufnehmen. Gebaute tragen schon
            // den aktuellen Stand und kosten nur die Abfrage.
            var bestand = _lots.CalculateEntityCount();
            if (bestand != _letzterBestand && _letzterBestand >= 0)
            {
                _neuAufnehmen = true;
                _index = null;
            }
            _letzterBestand = bestand;

            if (_neuAufnehmen)
            {
                _neuAufnehmen = false;
                ParkingLotSchrittmarke.Setze("Laden: Sync-Aufnahme beginnt");
                Aufnehmen();
                ParkingLotSchrittmarke.Setze("Laden: Sync-Aufnahme beendet");
                // Angestossene Parkplaetze laufen weiter, statt als "offen" zu warten.
                foreach (var lot in new List<Entity>(_fortsetzen))
                {
                    if (_wartetAuf.ContainsKey(lot)) continue;
                    _fortsetzen.Remove(lot);
                    if (_offenMenge.Contains(lot)) Einreihen(lot);
                }
                if (Mod.Optionen?.AutomatischSynchronisieren ?? false) AlleEinreihen();
                else
                {
                    var offen = 0;
                    var hintergrund = World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
                    // Was im Hintergrund schon gebaut wird, wartet auf nichts mehr.
                    foreach (var lot in _offen)
                        if (!_warteschlange.Contains(lot) && !_wartetAuf.ContainsKey(lot)
                            && !hintergrund.Gesperrt(lot)) offen++;
                    if (offen > 0)
                    {
                        _meldungOffen = offen;
                        VeroeffentlicheMeldung();
                    }
                }
            }
            Abarbeiten();
            Melde();
        }

        private void PflegeSchalter()
        {
            if (Mod.Optionen == null) return;
            var an = Mod.Optionen.AutomatischSynchronisieren;
            if (_syncAuto.value == an) return;
            _syncAuto.Update(an);
            // Im Optionsmenue eingeschaltet: sofort loslegen.
            if (an) AlleEinreihen();
        }

        // ------------------------------------------------------------------
        // Aufnahme
        // ------------------------------------------------------------------

        private Dictionary<Entity, List<Entity>> TeileJeLot()
        {
            var index = new Dictionary<Entity, List<Entity>>();
            using var teile = _teile.ToEntityArray(Allocator.Temp);
            using var relationen = _teile.ToComponentDataArray<ParkingLotPartRelation>(
                Allocator.Temp);
            for (var i = 0; i < teile.Length; i++)
            {
                var lot = relationen[i].Lot;
                if (!index.TryGetValue(lot, out var liste))
                    index[lot] = liste = new List<Entity>();
                liste.Add(teile[i]);
            }
            return index;
        }

        private void Aufnehmen()
        {
            var uhr = Stopwatch.StartNew();
            _offen.Clear();
            _offenMenge.Clear();
            var index = TeileJeLot();
            var still = 0;
            var fehlend = World.GetOrCreateSystemManaged<ParkingLotFehlendeAssetsSystem>();
            // Frisch, nicht vom letzten Takt: nach dem Laden kann die Aufnahme sonst vor der Erkennung laufen.
            fehlend.ErfasseJetzt();
            var erstReparieren = 0;
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                var hintergrund = World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
                if (hintergrund.IstErsatz(lot)) continue;
                /*
                 * ERST REPARIEREN, DANN SYNCHRONISIEREN (1.0.6, Nutzer 2026-10-06).
                 * Ohne Bauplan scheitern die Schritte 10/11 an "Bauzettel
                 * unlesbar", mit fehlenden Assets Schritt 10 an der Artenliste;
                 * und eine Asset-Reparatur reisst den Parkplatz ab, waehrend ein
                 * Tausch ihn noch anfasst. Solche Parkplaetze zeigt die Liste
                 * nur mit "Reparieren"; danach kommen sie von selbst hierher.
                 * Damit ist die Reihenfolge der Knoepfe egal.
                 */
                if (!ParkingLotBaukontextLeser.Vollstaendig(EntityManager, lot) || fehlend.Betrifft(lot))
                { erstReparieren++; continue; }
                // Wird gerade wegen fehlender Assets neu gebaut: das ist keine
                // offene Synchronisation (sonst "1 Parkplatz kann aktualisiert
                // werden" nach jedem Reparieren, Nutzer 2026-10-05).
                if (hintergrund.Gesperrt(lot) && ParkingLotFehlendeAssetsSystem.ErsatzFuer(lot) != null) continue;
                // Dasselbe fuer den Neubau wegen falsch verlegter Wege (Issue #10).
                if (hintergrund.Gesperrt(lot) && World.GetOrCreateSystemManaged<ParkingLotWegpruefungSystem>().InReparatur(lot)) continue;
                if (hintergrund.Gesperrt(lot))
                { _offen.Add(lot); _offenMenge.Add(lot); continue; }
                var stand = StandVon(lot);
                if (stand >= Migrationskatalog.Aktuell) continue;
                if (stand < 0) continue;
                var traeger = EntityManager.GetComponentData<
                    ParkingLotCarrierReference>(lot).Carrier;
                if (!index.TryGetValue(lot, out var teile)) teile = new List<Entity>();
                int noetig = NaechsterNoetigerSchritt(lot, traeger, teile, stand);
                if (noetig >= 0 && _ausfuehrungen.TryGetValue(
                        Migrationskatalog.Schritte[noetig].Name,out var ausfuehrung)
                    && ausfuehrung.Hintergrund
                    && !hintergrund.KannNeubauen(lot,out var grund))
                { MeldeBauplanFehlt(lot,grund); continue; }
                _ohneBauplan.Remove(lot);
                if (noetig < 0)
                {
                    // Nichts zu tun - nur Buchfuehrung, die Welt bleibt gleich.
                    SetzeStand(lot, Migrationskatalog.Aktuell);
                    still++;
                    continue;
                }
                // Gescheitert heisst: bis zum naechsten Laden kein Versuch -
                // also auch kein Sync-Knopf, der nichts tun wuerde.
                if (_gescheitert.Contains(lot)) continue;
                _offen.Add(lot);
                _offenMenge.Add(lot);
            }
            Mod.log.Info("PLT-Sync: Aufnahme in " + uhr.ElapsedMilliseconds + " ms: "
                + lots.Length + " Parkplatz/Parkplaetze, " + _offen.Count
                + " brauchen eine Synchronisation, " + still + " still auf Stand "
                + Migrationskatalog.Aktuell + " gesetzt (nichts zu tun)"
                + (erstReparieren > 0 ? ", " + erstReparieren + " erst nach der Reparatur" : "") + ".");
        }

        /** Index des ersten Schritts ab `stand`, der hier etwas zu tun hat, sonst -1. */
        private int NaechsterNoetigerSchritt(Entity lot, Entity traeger,
            List<Entity> teile, int stand)
        {
            for (var s = stand; s < Migrationskatalog.Schritte.Length; s++)
            {
                if (!_ausfuehrungen.TryGetValue(Migrationskatalog.Schritte[s].Name,
                        out var a)) return s; // fehlt: nie still ueberspringen
                if (a.Braucht(lot, traeger, teile)) return s;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // Abarbeiten
        // ------------------------------------------------------------------

        private void Einreihen(Entity lot)
        {
            // Wartet der Parkplatz schon auf seinen Tausch oder Neubau, kommt er
            // erst danach wieder dran (1.0.6: sonst doppelte Laternen).
            if (!_offenMenge.Contains(lot) || _warteschlange.Contains(lot)
                || _gescheitert.Contains(lot) || _wartetAuf.ContainsKey(lot)) return;
            var hintergrund = World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
            if (hintergrund.Gesperrt(lot)) return;
            if (_warteschlange.Count == 0 && hintergrund.Offen == 0)
                _gesamt = _erledigt = _syncErfolge = 0;
            _warteschlange.Add(lot);
            _gesamt++;
            VergissOffenMeldung();
        }

        private void AlleEinreihen()
        {
            foreach (var lot in _offen) Einreihen(lot);
        }

        private void Abarbeiten()
        {
            if (_warteschlange.Count == 0) return;
            // Waehrend eines Umbaus wird das alte Lot gleich ersetzt; seine
            // Teile jetzt anzufassen waere verschwendet oder schlimmer.
            if (_werkzeug.IsEditing) return;
            var uhr = Stopwatch.StartNew();
            _index ??= TeileJeLot();
            do
            {
                var lot = _warteschlange[0];
                _warteschlange.RemoveAt(0);
                if (Synchronisiere(lot, _index)) _syncErfolge++;
                _erledigt++;
            }
            while (_warteschlange.Count > 0 && uhr.Elapsed.TotalMilliseconds < BudgetMs);
            if (_warteschlange.Count == 0)
            {
                _index = null;
                // In die Meldung nur, was von selbst lief - ein Klick in der
                // Liste hat seine Rueckmeldung schon in der Liste.
                if (Mod.Optionen?.AutomatischSynchronisieren ?? false)
                {
                    _meldungSync += _syncErfolge;
                    if (_syncErfolge > 0) VeroeffentlicheMeldung();
                }
            }
        }

        private bool Synchronisiere(Entity lot, Dictionary<Entity, List<Entity>> index)
        {
            _offen.Remove(lot);
            _offenMenge.Remove(lot);
            if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)
                || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
                return false;
            // Zwischen Aufnahme und Abarbeiten koennen Assets verschwunden sein.
            if (!ParkingLotBaukontextLeser.Vollstaendig(EntityManager, lot)
                || World.GetOrCreateSystemManaged<ParkingLotFehlendeAssetsSystem>().Betrifft(lot))
                return false;
            var traeger = EntityManager.GetComponentData<
                ParkingLotCarrierReference>(lot).Carrier;
            if (!EntityManager.Exists(traeger)) return false;
            if (!index.TryGetValue(lot, out var teile)) teile = new List<Entity>();
            teile.RemoveAll(t => !EntityManager.Exists(t)
                || EntityManager.HasComponent<Deleted>(t));

            var uhr = Stopwatch.StartNew();
            var stand = StandVon(lot);
            var getan = new List<string>();
            for (var s = stand; s < Migrationskatalog.Schritte.Length; s++)
            {
                var schritt = Migrationskatalog.Schritte[s];
                if (!_ausfuehrungen.TryGetValue(schritt.Name, out var a))
                {
                    Scheitern(lot, s, schritt.Name, "keine Ausfuehrung");
                    return false;
                }
                if (a.Braucht(lot, traeger, teile))
                {
                    if (a.Hintergrund && !World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>().KannNeubauen(lot,out var grund))
                    { MeldeBauplanFehlt(lot,grund); return false; }
                    ParkingLotSchrittmarke.Setze("Sync: Lot " + lot.Index
                        + " Schritt " + schritt.Nummer + " " + schritt.Name + " beginnt");
                    a.Ausfuehren(lot, traeger, teile);
                    ParkingLotSchrittmarke.Setze("Sync: Lot " + lot.Index
                        + " Schritt " + schritt.Nummer + " " + schritt.Name + " beendet");
                    if (a.Hintergrund || a.Tausch)
                    {
                        _wartetAuf[lot] = schritt.Nummer;
                        _fortsetzen.Add(lot);
                        // Ein vorgemerkter Neubau ist noch KEINE Wirkung.
                        // Datenstand bleibt vor diesem Schritt, bis der echte
                        // Hintergrundpfad das alte Lot ersetzt und die Aufnahme die
                        // neuen Wege nachprueft. Bei Abbruch bleibt es offen.
                        _offen.Add(lot);
                        _offenMenge.Add(lot);
                        Mod.log.Info("PLT-Sync: Lot " + lot.Index + " Schritt "
                            + schritt.Nummer + " wartet auf Hintergrundauftrag; Datenstand bleibt " + s + ".");
                        return false;
                    }
                    // Nachpruefung: die Wirkung muss da sein, nicht nur
                    // "kein Fehler".
                    if (a.Braucht(lot, traeger, teile))
                    {
                        Scheitern(lot, s, schritt.Name, "Nachpruefung sieht keine Wirkung");
                        return false;
                    }
                    getan.Add(schritt.Name);
                }
                SetzeStand(lot, schritt.Nummer);
            }
            Mod.log.Info("PLT-Sync: Lot " + lot.Index + " auf Stand "
                + Migrationskatalog.Aktuell + " in " + uhr.Elapsed.TotalMilliseconds.ToString("0.0")
                + " ms (" + (getan.Count == 0 ? "nichts zu tun" : string.Join(", ", getan))
                + ", " + teile.Count + " Teile).");
            return true;
        }

        private void Scheitern(Entity lot, int s, string name, string grund)
        {
            _gescheitert.Add(lot);
            Mod.log.Warn("PLT-Sync: Lot " + lot.Index + " bleibt auf Stand " + s
                + ": Schritt " + (s + 1) + " '" + name + "' - " + grund
                + ". Bis zum naechsten Laden kein neuer Versuch.");
        }

        internal void MeldeHintergrundEnde(Entity lot, Entity neu, bool erfolgreich)
        {
            if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot))
            {
                _offen.Remove(lot);
                _offenMenge.Remove(lot);
            }
            var schrittNummer = _wartetAuf.TryGetValue(lot, out var gewartet) ? gewartet : Migrationskatalog.Aktuell;
            _wartetAuf.Remove(lot);
            // Weiterlaufen nur nach Erfolg - und am neuen Lot, falls ein Neubau es ersetzt hat.
            var weiter = _fortsetzen.Remove(lot);
            if (erfolgreich && weiter && neu != Entity.Null) _fortsetzen.Add(neu);
            if (erfolgreich)
            {
                // Nur nach der echten Nachpruefung des Auftrags. Stand = dieser
                // Schritt; die Neuaufnahme arbeitet die Schritte danach ab.
                SetzeStand(neu, schrittNummer);
                _offen.Remove(lot);
                _offenMenge.Remove(lot);
                _neuAufnehmen = true;
                _index = null;
                if (Mod.Optionen?.AutomatischSynchronisieren ?? false)
                {
                    _meldungSync++;
                    VeroeffentlicheMeldung();
                }
            }
            else
            {
                var ziel = neu != Entity.Null && EntityManager.Exists(neu) ? neu : lot;
                if (ziel != lot)
                {
                    _offen.Remove(lot);
                    _offenMenge.Remove(lot);
                    SetzeStand(ziel, schrittNummer - 1);
                }
                _offen.Remove(lot);
                _offenMenge.Remove(lot);
                if (EntityManager.Exists(ziel) && !EntityManager.HasComponent<Deleted>(ziel))
                    _gescheitert.Add(ziel);
                Mod.log.Warn("PLT-Sync: Hintergrundauftrag (Schritt " + schrittNummer + ") von " + lot
                    + " nicht abgeschlossen; " + ziel + " bleibt auf seinem Datenstand, neuer Versuch nach dem naechsten Laden.");
            }
        }

        internal void MeldeBauplanFehlt(Entity lot, string grund)
        {
            _offen.Remove(lot); _offenMenge.Remove(lot);
            if (!_ohneBauplan.Add(lot)) return;
            Mod.log.Warn($"PLT-Sync: Lot {lot.Index} braucht Reparatur/Bauplan: {grund}; Schritt 8 nicht eingereiht, 0 Neubauversuche.");
        }

        internal void MeldeRueckwegEnde()
        { _neuAufnehmen = true; _index = null; }

        internal void MeldeRueckwegStart(bool erster)
        {
            if (erster && _warteschlange.Count == 0) _gesamt = _erledigt = 0;
            _gesamt++;
        }

        private int StandVon(Entity lot)
            => EntityManager.HasComponent<ParkingLotDatenstand>(lot)
                ? EntityManager.GetComponentData<ParkingLotDatenstand>(lot).Stand
                : 0;

        private void SetzeStand(Entity lot, int stand)
        {
            var wert = new ParkingLotDatenstand { Stand = stand };
            if (EntityManager.HasComponent<ParkingLotDatenstand>(lot))
                EntityManager.SetComponentData(lot, wert);
            else EntityManager.AddComponentData(lot, wert);
        }

        // ------------------------------------------------------------------
        // Anzeige
        // ------------------------------------------------------------------

        private void Melde()
        {
            if (_ergebnisUhr.IsRunning
                && _ergebnisUhr.Elapsed.TotalSeconds >= ErgebnisSekunden)
                LeereMeldung();
            if (_syncOffen.value != _offen.Count) _syncOffen.Update(_offen.Count);
            var zuTun = _offen.Count + _ohneBauplan.Count + OffeneWaisen()
                + World.GetOrCreateSystemManaged<ParkingLotFehlendeAssetsSystem>().Anzahl
                + World.GetOrCreateSystemManaged<ParkingLotWegpruefungSystem>().Anzahl;
            if (_arbeitOffen.value != zuTun) _arbeitOffen.Update(zuTun);
            var arbeit = World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
            var hintergrund = arbeit.Offen
                + World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().Offen
                + World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().Offen;
            _gesamt = System.Math.Max(_gesamt,_warteschlange.Count + hintergrund);
            var fortschritt = HintergrundFortschritt.Zaehle(_gesamt, _warteschlange.Count, hintergrund);
            var laeuft = _warteschlange.Count + hintergrund > 0
                ? fortschritt.Fertig + "\t" + fortschritt.Gesamt + "\t" + arbeit.Fortschrittshinweis
                : string.Empty;
            if (_syncLaeuft.value != laeuft) _syncLaeuft.Update(laeuft);
            World.GetOrCreateSystemManaged<ParkingLotUISystem>().PflegeWiederOeffnen(ArbeitLaeuft);
        }

        /*
         * SOLANGE SYNC ODER REPARATUR LAUFEN, BLEIBT DAS PLT-WERKZEUG ZU
         * (Nutzer 2026-10-06). Zwei Spieler meldeten einen bei "1/3"
         * haengenden Sync, einer dazu Abstuerze; das Oeffnen des Panels oder
         * eine Bearbeitung mitten im Tausch war einer der Wege dorthin. Statt
         * das Panel zu oeffnen, pulsiert die Fortschrittsmeldung kurz - "ich
         * arbeite noch, bitte warten".
         */
        internal bool ArbeitLaeuft
            => _warteschlange.Count > 0
               || World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>().Laeuft
               || World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().Offen > 0
               || World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().Offen > 0;

        /** Bestand neu aufnehmen - nach einer Reparatur, die keine Parkplatzzahl aendert (Bauplan zurueck). */
        internal void NeuAufnehmen()
        {
            _neuAufnehmen = true;
            _index = null;
        }

        /** Nur der Sync selbst (Warteschlange und beide Tausche), ohne Reparatur-Neubauten. */
        internal bool SyncLaeuft
            => _warteschlange.Count > 0
               || World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().Offen > 0
               || World.GetOrCreateSystemManaged<ParkingLotBestandsTauschSystem>().Offen > 0;

        /** True heisst: gesperrt, Meldung pulsiert. Fuer jeden Weg, der das PLT-Werkzeug oeffnet. */
        internal bool SperrtWegenArbeit(string wofuer)
        {
            if (!ArbeitLaeuft) return false;
            _syncPuls.Update(_syncPuls.value + 1);
            Mod.log.Info("PLT-Sync: " + wofuer + " gesperrt, Synchronisation/Reparatur laeuft noch.");
            return true;
        }

        /** Verwaiste Parkplaetze, die noch keinen Traeger und Bauzettel zurueck haben. */
        private int OffeneWaisen()
        {
            var waisen = World.GetOrCreateSystemManaged<ParkingLotWaisenSystem>();
            var n = 0;
            foreach (var liste in new[] { waisen.Waisen, waisen.Halbwaisen })
                foreach (var lot in liste)
                    if (EntityManager.Exists(lot) && !EntityManager.HasComponent<Deleted>(lot)
                        && !(EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                             && EntityManager.HasComponent<ParkingLotBuildReceipt>(lot)))
                        n++;
            return n;
        }

        private bool VersucheSchluessel(string schluessel, out Entity entity)
        {
            entity = Entity.Null;
            if (string.IsNullOrEmpty(schluessel)) return false;
            var teile = schluessel.Split(':');
            if (teile.Length != 2 || !int.TryParse(teile[0], out var index)
                || !int.TryParse(teile[1], out var version)) return false;
            entity = new Entity { Index = index, Version = version };
            return EntityManager.Exists(entity);
        }
    }
}
