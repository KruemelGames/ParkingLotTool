using Colossal.UI.Binding;
using Game.Common;
using Game.Tools;
using Game.UI.InGame;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Knoepfe im Auswahlfenster: Bearbeiten fuer Lots mit Bauzettel, und
     * fuer verwaiste Lots (ohne PLT gespeichert) ein Hinweis mit
     * Reparaturknopf.
     */
    public sealed partial class ParkingLotEditSection : InfoSectionBase
    {
        protected override string group => "ParkingLotTool.EditSection";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_InfoUISystem.AddMiddleSection(this);
            _waisen = World.GetOrCreateSystemManaged<ParkingLotWaisenSystem>();
            AddBinding(new TriggerBinding("ParkingLotTool",
                "GewaehltenReparieren", () =>
                {
                    var lot = selectedEntity;
                    if (IstParkplatz(lot))
                    {
                        AktualisiereFreigabe(lot);
                        if (_reparierbar) _waisen.Reparieren(lot);
                    }
                    RequestUpdate();
                }));
        }

        private ParkingLotWaisenSystem _waisen;
        private int _waise;
        private bool _bauzettel;
        /**
         * Die zuletzt gueltig ausgewaehlte Flaeche - NUR fuer die Spur.
         *
         * Der Nutzer sah die Auswahl "irgendwann" verschwinden und konnte
         * nicht sagen, ob die Flaeche geloescht wurde oder das Infofenster
         * nur die Auswahl verlor. Beides steht jetzt in der Schrittspur:
         * die alte Flaeche und ob sie noch existiert, geloescht oder
         * temporaer ist.
         */
        private Entity _letzteAuswahl = Entity.Null;

        protected override void Reset() => visible = false;

        [Preserve]
        protected override void OnUpdate()
        {
            var lot = selectedEntity;
            var da = lot != Entity.Null
                && EntityManager.Exists(lot)
                && !EntityManager.HasComponent<Deleted>(lot)
                && !EntityManager.HasComponent<Temp>(lot);
            if (da)
            {
                _letzteAuswahl = lot;
            }
            else if (_letzteAuswahl != Entity.Null)
            {
                var alt = _letzteAuswahl;
                ParkingLotSchrittmarke.Setze("Auswahl: verloren (war " + alt
                    + "; jetzt " + lot + "; existiert="
                    + EntityManager.Exists(alt)
                    + "; geloescht=" + (EntityManager.Exists(alt)
                        && EntityManager.HasComponent<Deleted>(alt))
                    + "; temporaer=" + (EntityManager.Exists(alt)
                        && EntityManager.HasComponent<Temp>(alt)) + ")");
                _letzteAuswahl = Entity.Null;
            }
            visible = da && IstParkplatz(lot);
            _bearbeitbar = _reparierbar = false;
            _grund = string.Empty;
            if (visible) AktualisiereFreigabe(lot);
            _schluessel = visible ? ParkingLotListeUISystem.SchluesselVon(lot) : string.Empty;
        }

        protected override void OnProcess()
        {
        }

        /** 0 = nicht verwaist, 1 = reparierbar, 2 = nicht reparierbar. */
        public override void OnWriteProperties(IJsonWriter writer)
        {
            writer.PropertyName("bearbeitbar");
            writer.Write(_bearbeitbar);
            writer.PropertyName("reparierbar");
            writer.Write(_reparierbar);
            writer.PropertyName("grund");
            writer.Write(_grund);
            writer.PropertyName("waise");
            writer.Write(_waise);
            writer.PropertyName("bauzettel");
            writer.Write(_bauzettel);
            /*
             * DER SCHLUESSEL GEHOERT MIT IN DIE OBERFLAECHE.
             *
             * Der Bearbeiten-Knopf lag vorher auf einem eigenen Ausloeser,
             * der `SelectedInfoUISystem.selectedEntity` erst beim Klick las.
             * Beim Einstieg ins Bearbeiten wechselt das Werkzeug, und CS2
             * raeumt dabei die Auswahl im Infofenster auf - mal vor, mal
             * nach dem Ausloeser. Von Zeit zu Zeit kam deshalb gar keine
             * oder die falsche Flaeche an, und die Auswahl war weg.
             *
             * Der Schluessel steht schon hier fest, solange die Sektion
             * sichtbar ist. Die Oberflaeche schickt ihn zusammen mit dem
             * Klick zurueck - derselbe Weg wie aus der Parkplatzliste, der
             * die Flaeche ebenfalls ueber den Schluessel adressiert.
             */
            writer.PropertyName("schluessel");
            writer.Write(_schluessel);
        }

        private string _schluessel = string.Empty;
    }
}
