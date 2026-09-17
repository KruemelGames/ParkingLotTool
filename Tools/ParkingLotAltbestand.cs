using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Parkplaetze, die mit dem AUSGEBAUTEN Rechenweg gebaut wurden.
     *
     * Der alte Rechenweg ist am 2026-09-01 entfallen. Was mit ihm gebaut
     * wurde, bleibt im Spielstand liegen und funktioniert auch weiter - es
     * sind Wege, Flaechen und Aufkleber wie alle anderen. Neu bauen laesst
     * sich so ein Parkplatz aber nie wieder gleich, und wer ihn bearbeitet,
     * bekommt ab dem ersten Uebernehmen das Ergebnis des heutigen Wegs.
     *
     * ERKENNBAR SIND SIE, und das ist der Grund, warum es dieses System
     * ueberhaupt geben kann: der Bauzettel speichert `Zellen`. Steht dort
     * `false`, ist der Parkplatz nachweislich alt gebaut.
     *
     * WAS ER NICHT ERKENNT, und das gehoert in dieselbe Zeile: Parkplaetze
     * OHNE Bauzettel. Die stammen aus der Zeit vor dem Bauzettel und tragen
     * die Angabe schlicht nicht. Sie werden NICHT mitgezaehlt und nicht
     * angeboten - lieber einen alten uebersehen als einen guten loeschen.
     */
    public sealed partial class ParkingLotAltbestandSystem : GameSystemBase
    {
        private EntityQuery _lots;
        private readonly List<Entity> _altbestand = new List<Entity>();
        private ParkingLotUISystem _uiSystem;

        /** Wie viele alt gebaute Parkplaetze gefunden wurden. */
        internal int Anzahl => _altbestand.Count;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _uiSystem = World.GetOrCreateSystemManaged<ParkingLotUISystem>();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<ParkingLotBuildReceipt>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            RequireForUpdate(_lots);
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _altbestand.Clear();
            _uiSystem?.SetAltbestand(0);
            if (mode != GameMode.Game) return;

            /*
             * Erst NACH dem Laden zaehlen, nicht waehrenddessen: vorher sind
             * die Puffer der Entities noch nicht zwingend gefuellt.
             */
            Suchen();
            if (_altbestand.Count == 0) return;

            _uiSystem?.SetAltbestand(_altbestand.Count);
            Mod.log.Info("PLT-Altbestand: " + _altbestand.Count
                + " Parkplatz/Parkplaetze stammen aus dem ausgebauten "
                + "Rechenweg (Bauzettel mit Zellen=false). Dem Nutzer zum "
                + "Loeschen angeboten; ohne seine Zustimmung passiert nichts.");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Altbestand);
        }

        private void Suchen()
        {
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                if (EntityManager.GetComponentData<ParkingLotBuildReceipt>(lot)
                        .Zellen) continue;
                _altbestand.Add(lot);
            }
        }

        /**
         * Loescht auf ausdruecklichen Wunsch.
         *
         * Gesetzt wird nur `Deleted` am Lot; den Rest raeumt der vorhandene
         * relationsbasierte Aufraeumer ab, genau wie beim Bulldozer. Ein
         * eigener Abrissweg waere eine zweite Wahrheit ueber dasselbe.
         */
        internal void LoescheAltbestand()
        {
            var geloescht = 0;
            foreach (var lot in _altbestand)
            {
                if (lot == Entity.Null || !EntityManager.Exists(lot)
                    || EntityManager.HasComponent<Deleted>(lot)) continue;
                EntityManager.AddComponent<Deleted>(lot);
                geloescht++;
            }

            Mod.log.Info("PLT-Altbestand: " + geloescht + " von "
                + _altbestand.Count + " alt gebauten Parkplaetzen auf Wunsch "
                + "des Nutzers dem Aufraeumer uebergeben.");
            _altbestand.Clear();
            _uiSystem?.SetAltbestand(0);
        }

        /** Der Nutzer will sie behalten - nicht noch einmal fragen. */
        internal void BehalteAltbestand()
        {
            Mod.log.Info("PLT-Altbestand: " + _altbestand.Count
                + " alt gebaute Parkplaetze bleiben auf Wunsch des Nutzers "
                + "stehen.");
            _altbestand.Clear();
            _uiSystem?.SetAltbestand(0);
        }
    }
}
