using System.Collections.Generic;
using Game;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * SETZT DEN UNSICHTBAREN NAMEN - UND ZWAR IN DER PHASE, IN DER DAS GEHT.
     *
     * `Game.UI.NameSystem.SetCustomName` legt intern selbst einen
     * Befehlspuffer an, ueber die `EndFrameBarrier`. CS2s Barrieren sind
     * `SafeCommandBufferSystem`: ausserhalb ihres eigenen Fensters wirft
     * `CreateCommandBuffer()` die Ausnahme
     *
     *     Trying to create EntityCommandBuffer when it's not allowed!
     *
     * Und aus unserem Werkzeug heraus ist dieses Fenster IMMER zu. Die
     * Reihenfolge steht in `Game.Common.SystemOrder`, alle drei in MainLoop:
     *
     *     Zeile 58:  UpdateAt<ToolSystem>            <- unser Werkzeug
     *     Zeile 62:  UpdateAt<AllowBarrier<EndFrameBarrier>>  <- oeffnet
     *     Zeile 67:  UpdateAt<UIUpdateSystem>        <- hier ist offen
     *
     * `ToolSystem` treibt die Phase `ToolUpdate` (ToolSystem.cs Zeile 329),
     * und das passiert VOR dem Oeffnen. Deshalb hat das Umbenennen der
     * Zoningstrassen nie zuverlaessig funktioniert - der Nutzer am
     * 2026-09-15: *"Ich hab 2 zoningflaechen gesetzt aber nur eine hat den
     * namen abgenommen bekommen."* Im Log standen dazu vier Ausnahmen.
     *
     * Dieses System laeuft in `UIUpdate` und damit im offenen Fenster. Das
     * Werkzeug legt nur noch ab, wer umbenannt werden soll; gesetzt wird
     * hier. Beides passiert im SELBEN Frame - erst ToolSystem, dann
     * UIUpdateSystem.
     *
     * Dieselbe Lehre wie beim Leitungsabriss ein paar Stunden vorher: nicht
     * WAS wir tun war falsch, sondern WANN.
     */
    public sealed partial class ParkingLotStrassennameSystem : GameSystemBase
    {
        /**
         * Wer noch einen Namen braucht.
         *
         * Ein Set, keine Liste: derselbe Strassenzug wird je Durchgang
         * mehrfach gefunden, einmal je Kante.
         */
        private static readonly HashSet<Entity> _offen = new HashSet<Entity>();

        /** Legt einen Strassenzug zum Umbenennen ab. Aus jeder Phase sicher. */
        internal static void Merke(Entity aggregat)
        {
            if (aggregat != Entity.Null) _offen.Add(aggregat);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Strassenname);
            if (_offen.Count == 0) return;

            var nameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            if (nameSystem == null) { _offen.Clear(); return; }

            var gesetzt = 0;
            var fehler = 0;
            string grund = null;
            foreach (var aggregat in _offen)
            {
                if (aggregat == Entity.Null || !EntityManager.Exists(aggregat))
                    continue;
                try
                {
                    nameSystem.SetCustomName(
                        aggregat, ParkingLotToolSystem.Unsichtbarername);
                    gesetzt++;
                }
                catch (System.Exception ausnahme)
                {
                    fehler++;
                    grund = ausnahme.Message;
                }
            }
            _offen.Clear();

            if (gesetzt > 0)
                Mod.log.Info("PLT-Zoningstrasse: " + gesetzt
                    + " Strassenzug/Strassenzuege auf den unsichtbaren Namen "
                    + "gesetzt (in UIUpdate, wo die EndFrameBarrier offen "
                    + "ist).");
            if (fehler > 0)
                Mod.log.Warn("PLT-Zoningstrasse: " + fehler + " Name(n) nicht "
                    + "gesetzt - " + grund + ". Wenn hier wieder 'not "
                    + "allowed' steht, ist die Phase dieses Systems "
                    + "verschoben worden.");
        }

        [Preserve]
        public ParkingLotStrassennameSystem() { }
    }
}
