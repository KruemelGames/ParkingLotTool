using Colossal.UI.Binding;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotUISystem
    {
        private ValueBinding<bool> _surfacePreview;
        internal bool SurfacePreview => _surfacePreview?.value ?? false;
        internal string SurfacePreviewSlot { get; private set; } = string.Empty;

        internal void VergissSurfacePreviewFenster() => SurfacePreviewSlot = string.Empty;

        private void InitialisiereSurfacePreview()
        {
            AddBinding(_surfacePreview = new ValueBinding<bool>(Group, "SurfacePreview", _defaults.SurfacePreview));
            AddBinding(new TriggerBinding<bool>(Group, "SetSurfacePreview", an =>
            {
                if (!UpdateValue(_surfacePreview, an)) return;
                var next = _defaults.Clone();
                next.SurfacePreview = an;
                if (TryWriteDefaults(next)) _defaults = next;
                if (!an) Tool()?.EntferneSurfacePreview("Schalter aus");
            }));
            AddBinding(new TriggerBinding<string>(Group, "SetSurfacePreviewSlot", slot =>
            {
                // Das Schliessen der alten Auswahl darf die neue nicht abmelden.
                if (slot == "road" || slot == "deco" || slot == "zoning") SurfacePreviewSlot = slot;
                else if (slot == "-" + SurfacePreviewSlot)
                {
                    SurfacePreviewSlot = string.Empty;
                    Tool()?.EntferneSurfacePreview("Auswahl zu");
                }
            }));
        }
    }
}
