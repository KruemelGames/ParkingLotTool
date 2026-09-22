using System.Collections.Generic;
using Game;
using Game.Prefabs;
using Unity.Collections;
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

        /**
         * DIE STEHENDE WACHE - warum es die zusaetzlich braucht.
         *
         * Bis hierher kam der Name aus zwei Quellen, und beide haben ein
         * Loch:
         *
         *  - Die Wache in `ParkingLotZoningBlockReport` laeuft zwoelfmal im
         *    Abstand von 30 Bildern NACH einem Bau. Sie haengt aber an
         *    `ParkingLotToolSystem.OnUpdate`, und das laeuft nur, solange
         *    unser Werkzeug aktiv ist. Wer nach dem Bau sofort das Werkzeug
         *    wechselt, bricht die Wache mitten im Lauf ab.
         *  - Beide Quellen laufen die `SubNet`-Liste des Traegers ab. Die
         *    ueberlebt das Laden nicht (gemessen 109 -> 0). Nach einem
         *    Neustart findet dort niemand mehr eine Gasse.
         *
         * Dazu kommt, dass CS2 den Strassenzug jedes Mal NEU bildet, wenn
         * sich an den Kanten etwas aendert - und die neue Entity traegt
         * wieder "Gasse 12". Das passiert auch lange nach dem Bau, etwa
         * wenn der Nutzer nebenan eine Strasse zieht.
         *
         * Dieses System laeuft in `UIUpdate` und damit IMMER, unabhaengig
         * vom Werkzeug. Es fragt nicht den Traeger, sondern die Kanten
         * selbst: welche tragen eines unserer Klon-Prefabs. Das ist die
         * einzige Auskunft, die einen Neustart ueberlebt.
         */
        private const int WacheAbstand = 60;
        private int _wacheFrames;
        private EntityQuery _kanten;
        private ParkingLotZoningRoadPrefabSystem _klone;
        private readonly HashSet<Entity> _klonprefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> _gesehen = new HashSet<Entity>();
        /** Wie viele Strassenzuege die Wache insgesamt nachbenannt hat. */
        private int _nachbenannt;
        /** Sammelt gesetzte Namen zwischen zwei Logzeilen. */
        private int _gesammelt;
        private const int MeldungAbstand = 600;
        private int _meldungFrames;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _klone = World
                .GetOrCreateSystemManaged<ParkingLotZoningRoadPrefabSystem>();
            /*
             * `Aggregated` ist der Filter, der die Menge klein haelt: den
             * Namen traegt der Strassenzug, nicht die Kante, und nur
             * Strassen bilden ueberhaupt einen. `Owner` grenzt weiter ein -
             * unsere Kanten gehoeren immer einem Parkplatz.
             */
            _kanten = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Game.Net.Aggregated>(),
                    ComponentType.ReadOnly<Game.Common.Owner>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                    ComponentType.ReadOnly<Game.Common.Deleted>(),
                },
            });
        }

        /**
         * Sucht Strassenzuege unserer Kanten, denen der Name fehlt.
         *
         * Gefragt wird `CustomName`, nicht die eigene Erinnerung: CS2 heftet
         * die Komponente in `SetCustomName` nur an, wenn der Name auch
         * angenommen wurde. Eine eigene Liste haette stattdessen gemeldet,
         * dass wir jeden Zug schon mal angefasst haben - das hat frueher
         * genau den Fehlschlag verdeckt.
         */
        private void Nachschau()
        {
            if (--_wacheFrames > 0) return;
            _wacheFrames = WacheAbstand;

            _klonprefabs.Clear();
            _klone?.SammleKlonprefabs(_klonprefabs);
            if (_klonprefabs.Count == 0) return;
            if (_kanten.IsEmptyIgnoreFilter) return;

            using var kanten = _kanten.ToEntityArray(Allocator.TempJob);
            _gesehen.Clear();
            for (var i = 0; i < kanten.Length; i++)
            {
                var kante = kanten[i];
                if (!_klonprefabs.Contains(EntityManager
                        .GetComponentData<PrefabRef>(kante).m_Prefab))
                    continue;
                var aggregat = EntityManager
                    .GetComponentData<Game.Net.Aggregated>(kante).m_Aggregate;
                if (aggregat == Entity.Null
                    || !EntityManager.Exists(aggregat)) continue;
                if (!_gesehen.Add(aggregat)) continue;
                if (EntityManager.HasComponent<Game.UI.CustomName>(aggregat))
                    continue;
                _offen.Add(aggregat);
                _nachbenannt++;
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Strassenname);
            Nachschau();
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

            /*
             * GEDROSSELT, ABER NICHT VERSCHWIEGEN.
             *
             * Die stehende Wache schaut jede Sekunde nach. Bliebe ein Name
             * dauerhaft nicht haften, stuende diese Zeile jede Sekunde im
             * Log und wuerde alles andere zudecken. Sie wird deshalb
             * gesammelt und hoechstens alle zehn Sekunden ausgegeben - mit
             * der Gesamtzahl, denn genau die ist der Befund: waechst sie
             * immer weiter, haftet der Name nicht, und dann hilft kein
             * weiterer Durchgang.
             */
            _gesammelt += gesetzt;
            if (_gesammelt > 0 && --_meldungFrames <= 0)
            {
                _meldungFrames = MeldungAbstand;
                Mod.log.Info("PLT-Zoningstrasse: " + _gesammelt
                    + " Strassenzug/Strassenzuege auf den unsichtbaren Namen "
                    + "gesetzt (in UIUpdate, wo die EndFrameBarrier offen "
                    + "ist). Seit dem Start insgesamt " + _nachbenannt
                    + " von der stehenden Wache nachgereicht; eine Zahl, die "
                    + "nicht zur Ruhe kommt, heisst: der Name haftet nicht.");
                _gesammelt = 0;
            }
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
