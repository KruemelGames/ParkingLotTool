using Colossal.UI.Binding;
using Game.Common;
using Game.UI;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Bindet den Gebuehrenregler im Auswahlfenster an genau eine PLT-Flaeche.
     *
     * Sichtbar wird die Sektion nur, wenn die aktuell ausgewaehlte Entity
     * sowohl den PLT-Rueckweg zum Traeger als auch unsere gespeicherten
     * Wirtschaftsdaten traegt. Diese Typen kommen an keinem Vanilla-Objekt
     * vor; ein Name, Prefab oder raeumlicher Treffer ist nicht beteiligt.
     */
    public sealed partial class ParkingLotFeeUISystem : UISystemBase
    {
        private const string Group = "ParkingLotTool";
        private const int MaximumFee = 50;

        private Game.UI.InGame.SelectedInfoUISystem _selectedInfo;
        private ParkingLotComfortSystem _comfort;
        private ValueBinding<bool> _visible;
        private ValueBinding<int> _fee;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _selectedInfo = World.GetOrCreateSystemManaged<
                Game.UI.InGame.SelectedInfoUISystem>();
            _comfort = World.GetOrCreateSystemManaged<ParkingLotComfortSystem>();

            AddBinding(_visible = new ValueBinding<bool>(
                Group, "ParkingFeeVisible", false));
            AddBinding(_fee = new ValueBinding<int>(
                Group, "SelectedParkingFee", 0));
            AddBinding(new TriggerBinding<int>(
                Group, "SetSelectedParkingFee", SetSelectedParkingFee));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var selected = _selectedInfo?.selectedEntity ?? Entity.Null;
            var visible = IsParkingLot(selected);
            if (_visible.value != visible) _visible.Update(visible);
            if (!visible) return;

            var fee = EntityManager
                .GetComponentData<ParkingLotEconomyData>(selected).ParkingFee;
            fee = UnityEngine.Mathf.Clamp(fee, 0, MaximumFee);
            if (_fee.value != fee) _fee.Update(fee);
        }

        private bool IsParkingLot(Entity entity)
            => entity != Entity.Null
               && EntityManager.Exists(entity)
               && !EntityManager.HasComponent<Deleted>(entity)
               && EntityManager.HasComponent<ParkingLotCarrierReference>(entity)
               && EntityManager.HasComponent<ParkingLotEconomyData>(entity);

        private void SetSelectedParkingFee(int requestedFee)
        {
            // Nicht dem zuletzt sichtbaren Binding vertrauen: zwischen Zug
            // und Trigger kann der Nutzer bereits etwas anderes anklicken.
            var lot = _selectedInfo?.selectedEntity ?? Entity.Null;
            if (!IsParkingLot(lot)) return;

            var fee = UnityEngine.Mathf.Clamp(requestedFee, 0, MaximumFee);
            var economy = EntityManager
                .GetComponentData<ParkingLotEconomyData>(lot);
            if (economy.ParkingFee == fee) return;

            economy.ParkingFee = fee;
            EntityManager.SetComponentData(lot, economy);
            _fee.Update(fee);

            var changed = _comfort?.SetParkingFeeImmediately(lot, fee) ?? 0;
            Mod.log.Info("PLT-Parkgebuehr: Lot " + lot.Index + " auf "
                + fee + " gesetzt; " + changed
                + " vorhandene Parkspuren sofort geaendert.");
        }
    }
}
