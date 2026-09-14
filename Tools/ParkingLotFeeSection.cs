using Colossal.UI.Binding;
using Game.UI.InGame;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Der Gebuehrenabschnitt im Auswahlfenster - als ECHTE CS2-Sektion.
     *
     * WARUM NICHT UEBER DIE OBERFLAECHE. Der erste Anlauf huellte
     * `SelectedInfoPanel` per `moduleRegistry.extend` ein und schob unseren
     * Abschnitt selbst in `middleSections`. Das warf beim Laden, belegt aus
     * der UI.log des Nutzers vom 2026-08-26:
     *
     *     ParkingLotTool: das Auswahlfenster liess sich nicht erweitern
     *     (extend auf SelectedInfoPanel).
     *     Ursache: TypeError: Assignment to constant variable.
     *
     * Das Spiel fuehrt diesen Export als `const`; ein ES-Modul laesst sich
     * dort nicht neu zuweisen. Bemerkenswert: das Eintragen unseres
     * Sektionstyps in `selectedInfoSectionComponents` ging problemlos - genau
     * umgekehrt zu meiner Vermutung. Ohne die getrennten try/catch haette ich
     * weiter an der falschen Stelle gesucht.
     *
     * Der vorgesehene Weg steht in `SelectedInfoUISystem` Zeile 250 und ist
     * oeffentlich:
     *
     *     public void AddMiddleSection(ISectionSource section)
     *
     * Eine Sektion meldet sich also SELBST an, und CS2 traegt sie in die
     * Bindung `selectedInfo.middleSections` ein. Das Panel muss niemand
     * anfassen.
     *
     * `group` ist der Schluessel, unter dem die Oberflaeche die passende
     * React-Komponente sucht. Er MUSS wortgleich zu `PARKING_FEE_SECTION` in
     * `UI/src/index.tsx` sein - sonst erscheint eine leere Zeile.
     *
     * Die Werte selbst schreibt dieser Abschnitt nicht: die Komponente holt
     * sie ueber die eigenen Bindungen von `ParkingLotFeeUISystem`. Hier zaehlt
     * nur, DASS die Sektion sichtbar ist und wann.
     */
    public sealed partial class ParkingLotFeeSection : InfoSectionBase
    {
        protected override string group => "ParkingLotTool.ParkingFeeSection";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_InfoUISystem.AddMiddleSection(this);
        }

        protected override void Reset()
        {
        }

        /**
         * SICHTBARKEIT GEHOERT IN OnUpdate, NICHT IN OnProcess.
         *
         * Der Ablauf der Basisklasse ist ein Henne-Ei-Problem
         * (`InfoSectionBase.PerformUpdate`, Zeile 105-119):
         *
         *     if (m_Dirty) { Reset(); Update(); if (Visible()) OnProcess(); }
         *
         * `OnProcess` laeuft NUR, wenn der Abschnitt bereits sichtbar ist. Wer
         * `visible` dort setzt, wartet ewig - genau das war mein erster
         * Anlauf, und er zeigte kommentarlos nichts. Jede Vanilla-Sektion
         * macht es in `OnUpdate`, siehe `UpkeepSection` Zeile 169-172.
         *
         * Merkmal ist `ParkingLotCarrierReference` - die traegt jede von PLT
         * gebaute Flaeche und sonst nichts im Spiel. Ein Vanilla-Parkplatz
         * behaelt sein Fenster damit unveraendert.
         */
        [Preserve]
        protected override void OnUpdate()
        {
            var lot = selectedEntity;
            // Ohne Wirtschaft gibt es keine Gebuehr, die man einstellen
            // koennte. Ein Regler, der nichts bewirkt, gehoert weg - der
            // gespeicherte Wert bleibt am Parkplatz und kommt mit dem
            // Schalter zurueck.
            var sichtbar = Mod.WirtschaftAn
                && lot != Entity.Null
                && EntityManager.Exists(lot)
                && EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                && EntityManager.HasComponent<ParkingLotEconomyData>(lot);

            if (sichtbar != visible)
            {
                Mod.log.Info("PLT-Gebuehrenabschnitt: "
                    + (sichtbar ? "sichtbar an Lot " + lot.Index
                                : "ausgeblendet") + ".");
            }
            visible = sichtbar;
        }

        protected override void OnProcess()
        {
        }

        /**
         * DIESE ZEILE TRENNT ZWEI VERDAECHTIGE.
         *
         * `OnWriteProperties` ruft `InfoSectionBase.Write` NUR im sichtbaren
         * Zweig - wer sie im Log sieht, weiss: die Sektion ist tatsaechlich in
         * `selectedInfo.middleSections` gelandet, mit dem vollen Klassennamen
         * als `__Type`. Fehlt der Regler trotzdem, liegt es an der Oberflaeche
         * (Zuordnung Sektionstyp -> React-Komponente), nicht an C#.
         *
         * Am 2026-08-27 hat genau diese Unterscheidung gefehlt: "sichtbar" im
         * Log und nichts auf dem Bildschirm liess beide Seiten offen.
         */
        private Entity _geschriebenFuer = Entity.Null;

        public override void OnWriteProperties(IJsonWriter writer)
        {
            var lot = selectedEntity;
            if (_geschriebenFuer == lot) return;
            _geschriebenFuer = lot;
            Mod.log.Info("PLT-Gebuehrenabschnitt: an die Oberflaeche "
                + "geschrieben fuer Lot " + lot.Index + " (Typ "
                + GetType().FullName + ").");
        }
    }
}
