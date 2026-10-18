using Colossal.UI.Binding;
using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotUISystem
    {
        private ValueBinding<bool> _surfacePreview;
        private ValueBinding<int> _echteVorschau;
        internal EchteVorschauArt VorschauArt => EchteVorschau.Lesen(_echteVorschau?.value);
        internal bool SurfacePreview => VorschauArt == EchteVorschauArt.Flaechen;
        private bool UebernimmEchteVorschau(EchteVorschauArt art)
        {
            bool anders = _echteVorschau != null && _echteVorschau.value != (int)art;
            if (!anders) return false;
            World.GetExistingSystemManaged<ParkingLotMusterSystem>()?.Entferne("Vorschauart gewechselt");
            _echteVorschau.Update((int)art);
            UpdateValue(_surfacePreview,art == EchteVorschauArt.Flaechen);
            return anders;
        }
        private void WaehleEchteVorschau(EchteVorschauArt art)
        {
            if (VorschauArt == art) return;
            UebernimmEchteVorschau(art);
            var next = _defaults.Clone(); next.EchteVorschau = (int)art;
            if (TryWriteDefaults(next)) _defaults = next;
        }
        private void InitialisiereSurfacePreview()
        {
            var art = _defaults.GeleseneVorschau;
            AddBinding(_echteVorschau = new ValueBinding<int>(Group,"EchteVorschau",(int)art));
            AddBinding(_surfacePreview = new ValueBinding<bool>(Group,"SurfacePreview",art == EchteVorschauArt.Flaechen));
            AddBinding(new TriggerBinding<int>(Group,"SetEchteVorschau", wert => WaehleEchteVorschau(EchteVorschau.Lesen(wert))));
            AddBinding(new TriggerBinding<bool>(Group,"SetSurfacePreview", an => WaehleEchteVorschau(
                EchteVorschau.Schalten(VorschauArt,EchteVorschauArt.Flaechen,an))));
        }
    }
}
