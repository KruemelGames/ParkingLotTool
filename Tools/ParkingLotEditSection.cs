using Colossal.UI.Binding;
using Game.Common;
using Game.Tools;
using Game.UI.InGame;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Knopf im Auswahlfenster, ausschliesslich fuer Lots mit Bauzettel. */
    public sealed partial class ParkingLotEditSection : InfoSectionBase
    {
        protected override string group => "ParkingLotTool.EditSection";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_InfoUISystem.AddMiddleSection(this);
        }

        protected override void Reset() => visible = false;

        [Preserve]
        protected override void OnUpdate()
        {
            var lot = selectedEntity;
            visible = lot != Entity.Null
                && EntityManager.Exists(lot)
                && !EntityManager.HasComponent<Deleted>(lot)
                && !EntityManager.HasComponent<Temp>(lot)
                && EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                && EntityManager.HasComponent<ParkingLotBuildReceipt>(lot)
                && EntityManager.HasBuffer<ParkingLotBuildPoint>(lot)
                && EntityManager.HasBuffer<ParkingLotBuildEntrance>(lot)
                && EntityManager.HasBuffer<ParkingLotBuildText>(lot);
        }

        protected override void OnProcess()
        {
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
        }
    }
}
