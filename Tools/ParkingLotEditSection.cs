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
                    if (lot != Entity.Null && EntityManager.Exists(lot))
                        _waisen.Reparieren(lot);
                    RequestUpdate();
                }));
        }

        private ParkingLotWaisenSystem _waisen;
        private int _waise;
        private bool _bauzettel;

        protected override void Reset() => visible = false;

        [Preserve]
        protected override void OnUpdate()
        {
            var lot = selectedEntity;
            var da = lot != Entity.Null
                && EntityManager.Exists(lot)
                && !EntityManager.HasComponent<Deleted>(lot)
                && !EntityManager.HasComponent<Temp>(lot);
            _bauzettel = da
                && EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                && EntityManager.HasComponent<ParkingLotBuildReceipt>(lot)
                && EntityManager.HasBuffer<ParkingLotBuildPoint>(lot)
                && EntityManager.HasBuffer<ParkingLotBuildEntrance>(lot)
                && EntityManager.HasBuffer<ParkingLotBuildText>(lot);
            _waise = da ? _waisen.Zustand(lot) : 0;
            visible = _bauzettel || _waise > 0;
        }

        protected override void OnProcess()
        {
        }

        /** 0 = nicht verwaist, 1 = reparierbar, 2 = nicht reparierbar. */
        public override void OnWriteProperties(IJsonWriter writer)
        {
            writer.PropertyName("waise");
            writer.Write(_waise);
            writer.PropertyName("bauzettel");
            writer.Write(_bauzettel);
        }
    }
}
