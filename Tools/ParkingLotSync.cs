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
        // Keine Queue<T>: der Typ steht in CS2 in System UND mscorlib.
        private readonly List<Entity> _warteschlange = new List<Entity>();
        private int _gesamt;
        private int _erledigt;
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
        private ValueBinding<string> _syncLaeuft;
        private ValueBinding<bool> _syncAuto;

        /**
         * "laufnummer\tanzahl" des zuletzt BEENDETEN Durchgangs. Ein kleiner
         * Durchgang ist im selben Bild fertig, in dem er beginnt - `SyncLaeuft`
         * wird dann nie sichtbar (gemessen 2026-09-25, 1 Parkplatz in 1,9 ms).
         * Die Oberflaeche zeigt deshalb auch das Ende, 10 s lang.
         */
        private ValueBinding<string> _syncErgebnis;
        private int _laufnummer;

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
            AddBinding(_syncLaeuft = new ValueBinding<string>(Group, "SyncLaeuft",
                string.Empty));
            AddBinding(_syncErgebnis = new ValueBinding<string>(Group,
                "SyncErgebnis", string.Empty));
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
                }));
            AddBinding(new TriggerBinding(Group, "ParkplaetzeSynchronisieren",
                AlleEinreihen));
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _offen.Clear();
            _offenMenge.Clear();
            _gescheitert.Clear();
            _warteschlange.Clear();
            _gesamt = _erledigt = 0;
            _letzterBestand = -1;
            _neuAufnehmen = mode == GameMode.Game;
            _spielGeladen = mode == GameMode.Game;
        }

        [Preserve]
        protected override void OnGamePreload(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            _spielGeladen = false;
            _warteschlange.Clear();
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
                Aufnehmen();
                if (Mod.Optionen?.AutomatischSynchronisieren ?? false) AlleEinreihen();
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
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                var stand = StandVon(lot);
                if (stand >= Migrationskatalog.Aktuell) continue;
                if (stand < 0) continue;
                var traeger = EntityManager.GetComponentData<
                    ParkingLotCarrierReference>(lot).Carrier;
                if (!index.TryGetValue(lot, out var teile)) teile = new List<Entity>();
                if (NaechsterNoetigerSchritt(lot, traeger, teile, stand) < 0)
                {
                    // Nichts zu tun - nur Buchfuehrung, die Welt bleibt gleich.
                    SetzeStand(lot, Migrationskatalog.Aktuell);
                    still++;
                    continue;
                }
                if (_gescheitert.Contains(lot)) continue;
                _offen.Add(lot);
                _offenMenge.Add(lot);
            }
            Mod.log.Info("PLT-Sync: Aufnahme in " + uhr.ElapsedMilliseconds + " ms: "
                + lots.Length + " Parkplatz/Parkplaetze, " + _offen.Count
                + " brauchen eine Synchronisation, " + still + " still auf Stand "
                + Migrationskatalog.Aktuell + " gesetzt (nichts zu tun).");
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
            if (!_offenMenge.Contains(lot) || _warteschlange.Contains(lot)) return;
            if (_warteschlange.Count == 0) _gesamt = _erledigt = 0;
            _warteschlange.Add(lot);
            _gesamt++;
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
                Synchronisiere(lot, _index);
                _erledigt++;
            }
            while (_warteschlange.Count > 0 && uhr.Elapsed.TotalMilliseconds < BudgetMs);
            if (_warteschlange.Count == 0)
            {
                _index = null;
                _laufnummer++;
                _syncErgebnis.Update(_laufnummer + "\t" + _erledigt);
            }
        }

        private void Synchronisiere(Entity lot, Dictionary<Entity, List<Entity>> index)
        {
            _offen.Remove(lot);
            _offenMenge.Remove(lot);
            if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)
                || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
                return;
            var traeger = EntityManager.GetComponentData<
                ParkingLotCarrierReference>(lot).Carrier;
            if (!EntityManager.Exists(traeger)) return;
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
                    return;
                }
                if (a.Braucht(lot, traeger, teile))
                {
                    a.Ausfuehren(lot, traeger, teile);
                    // Nachpruefung: die Wirkung muss da sein, nicht nur
                    // "kein Fehler".
                    if (a.Braucht(lot, traeger, teile))
                    {
                        Scheitern(lot, s, schritt.Name, "Nachpruefung sieht keine Wirkung");
                        return;
                    }
                    getan.Add(schritt.Name);
                }
                SetzeStand(lot, schritt.Nummer);
            }
            Mod.log.Info("PLT-Sync: Lot " + lot.Index + " auf Stand "
                + Migrationskatalog.Aktuell + " in " + uhr.Elapsed.TotalMilliseconds.ToString("0.0")
                + " ms (" + (getan.Count == 0 ? "nichts zu tun" : string.Join(", ", getan))
                + ", " + teile.Count + " Teile).");
        }

        private void Scheitern(Entity lot, int s, string name, string grund)
        {
            _gescheitert.Add(lot);
            Mod.log.Warn("PLT-Sync: Lot " + lot.Index + " bleibt auf Stand " + s
                + ": Schritt " + (s + 1) + " '" + name + "' - " + grund
                + ". Bis zum naechsten Laden kein neuer Versuch.");
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
            if (_syncOffen.value != _offen.Count) _syncOffen.Update(_offen.Count);
            var laeuft = _warteschlange.Count > 0
                ? _erledigt + "\t" + _gesamt
                : string.Empty;
            if (_syncLaeuft.value != laeuft) _syncLaeuft.Update(laeuft);
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
