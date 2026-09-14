using Colossal.UI.Binding;
using Game.Companies;
using Game.Prefabs;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Die Angestellten-Zeile im Auswahlfenster des Parkplatzes.
     *
     * WARUM ES SIE UEBERHAUPT BRAUCHT. Die Arbeitsplaetze sitzen am
     * unsichtbaren Begleiter, nicht an der Flaeche, die man anklickt - der
     * Begleiter ist fuer CS2 das Gebaeude, die Flaeche ist eine Area. Der
     * Vanilla-Abschnitt findet sie deshalb nie, und der Nutzer sah am
     * 2026-08-26: "Keine Anzeige fuer Angestellte im Infopanel." Die
     * Simulation lief trotzdem. Diese Zeile holt die Zahlen dorthin, wo man
     * sie sucht.
     *
     * DIE ZAHLEN GEHEN OHNE EIGENES BINDUNGSSYSTEM HINUEBER.
     * `InfoSectionBase.Write` oeffnet ein Objekt mit dem vollen Klassennamen
     * und ruft danach `OnWriteProperties`. Alles, was dort geschrieben wird,
     * erreicht die React-Komponente als Eigenschaft. Fuer zwei Zahlen ist das
     * der kuerzere Weg als der der Parkgebuehr, die einen Regler bedient und
     * deshalb echte Bindungen braucht.
     *
     * Sichtbarkeit gehoert nach `OnUpdate` - `OnProcess` laeuft erst, wenn
     * der Abschnitt bereits sichtbar ist. Diese Falle steht ausfuehrlich in
     * `ParkingLotFeeSection` beschrieben.
     */
    public sealed partial class ParkingLotEmployeeSection : InfoSectionBase
    {
        protected override string group => "ParkingLotTool.EmployeeSection";

        private EntityQuery _companions;
        private int _besetzt;
        private int _plaetze;
        private string _grund = "noch nicht geprueft";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _companions = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                ComponentType.ReadOnly<ParkingLotPartRelation>(),
                ComponentType.ReadOnly<WorkProvider>(),
                ComponentType.Exclude<Game.Common.Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            m_InfoUISystem.AddMiddleSection(this);
        }

        /**
         * `visible` gehoert hier mit zurueckgesetzt.
         *
         * Dieses System hat genau eine Abfrage, und ohne `RequireForUpdate`
         * laesst Unity ein System mit ausschliesslich leeren Abfragen gar nicht
         * erst laufen. Verschwindet der letzte Begleiter, laeuft `OnUpdate`
         * also nicht mehr - ein einmal gesetztes `visible` bliebe stehen und
         * zeigte veraltete Zahlen. `Reset` ruft `PerformUpdate` dagegen immer.
         */
        protected override void Reset()
        {
            _besetzt = 0;
            _plaetze = 0;
            visible = false;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var gefunden = TryLeseBegleiter(selectedEntity, out _besetzt,
                out _plaetze);
            var sichtbar = Mod.WirtschaftAn && gefunden && _plaetze > 0;
            /*
             * Sagen, WARUM nichts zu sehen ist. Am 2026-08-27 fehlte die Zeile,
             * und das Log schwieg dazu - dass in Wahrheit gar kein Begleiter
             * existierte, liess sich nur aus einer ganz anderen Fehlermeldung
             * erschliessen. Eine Zeile hier haette den Abend gespart.
             */
            if (sichtbar != visible)
            {
                Mod.log.Info("PLT-Angestelltenabschnitt: "
                    + (sichtbar
                        ? "sichtbar, " + _besetzt + " von " + _plaetze
                        /*
                         * ZWEI VERSCHIEDENE FEHLER, BISHER EINE MELDUNG.
                         *
                         * "Arbeitsplaetze 0" stand frueher sowohl da, wenn gar
                         * kein Begleiter gefunden wurde, als auch dann, wenn
                         * er da war und CS2 seine `m_MaxWorkers` noch nicht
                         * gefuellt hatte. Am 2026-08-31 hat mich genau das
                         * eine Runde gekostet: der Begleiter existierte, war
                         * nur frisch erzeugt - und `m_MaxWorkers` fuellt CS2
                         * erst im Simulationstakt, bei pausiertem Spiel also
                         * gar nicht.
                         */
                        : "ausgeblendet (Wirtschaft "
                          + (Mod.WirtschaftAn ? "an" : "aus")
                          + (gefunden
                              ? ", Begleiter gefunden, Arbeitsplaetze "
                                + _plaetze
                                + (_plaetze == 0
                                    ? " - CS2 fuellt m_MaxWorkers erst im "
                                      + "Simulationstakt; laeuft das Spiel?"
                                    : "")
                              : ", " + _grund)
                          + ")") + ".");
            }
            visible = sichtbar;
        }

        /**
         * Vom angeklickten Parkplatz zu seinem Begleiter.
         *
         * Die Verbindung laeuft ueber `ParkingLotPartRelation.Lot` am
         * Begleiter, nicht umgekehrt - der Parkplatz kennt seine Teile ueber
         * denselben Weg wie alle anderen auch. Bei einer Handvoll Begleitern
         * je Stadt ist das Durchgehen billiger als ein zweiter Verweis, den
         * man beim Loeschen sauber halten muesste.
         */
        private bool TryLeseBegleiter(Entity lot, out int besetzt, out int plaetze)
        {
            besetzt = 0;
            plaetze = 0;
            /*
             * DREI GRUENDE, BISHER EIN SATZ.
             *
             * Am 2026-08-31 stand im Log "KEIN Begleiter fuer dieses Lot
             * gefunden" - und zwar in derselben Millisekunde, in der sich auch
             * der Gebuehrenabschnitt ausblendete. Der Gebuehrenabschnitt kennt
             * gar keinen Begleiter. Beide koennen also nur aus demselben Grund
             * gegangen sein: die AUSWAHL war nicht mehr unser Lot. Die
             * Meldung schob es trotzdem auf den Begleiter und schickte mich in
             * die falsche Richtung.
             *
             * Jetzt sagt jeder Ausstieg, welcher es war.
             */
            if (lot == Entity.Null || !EntityManager.Exists(lot))
            {
                _grund = "Auswahl ist " + (lot == Entity.Null
                    ? "leer"
                    : "Entity " + lot.Index + ", die es nicht mehr gibt");
                return false;
            }
            if (!EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
            {
                _grund = "Auswahl Entity " + lot.Index
                    + " ist kein PLT-Parkplatz (kein Traegerverweis)";
                return false;
            }
            if (_companions.IsEmptyIgnoreFilter)
            {
                _grund = "es existiert ueberhaupt kein Begleiter";
                return false;
            }

            using var begleiter = _companions.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < begleiter.Length; i++)
            {
                var eins = begleiter[i];
                if (EntityManager.GetComponentData<ParkingLotPartRelation>(eins)
                        .Lot != lot)
                    continue;
                plaetze = EntityManager
                    .GetComponentData<WorkProvider>(eins).m_MaxWorkers;
                besetzt = EntityManager.HasBuffer<Employee>(eins)
                    ? EntityManager.GetBuffer<Employee>(eins, true).Length
                    : 0;
                return true;
            }

            _grund = "Lot " + lot.Index + " ist ein PLT-Parkplatz, aber keiner "
                + "der " + begleiter.Length + " Begleiter verweist auf ihn";
            return false;
        }

        protected override void OnProcess()
        {
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            writer.PropertyName("employees");
            writer.Write(_besetzt);
            writer.PropertyName("maxEmployees");
            writer.Write(_plaetze);
        }
    }
}
