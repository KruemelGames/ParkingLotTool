using Colossal.UI.Binding;
using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotUISystem
    {
        internal Fahrzeugart AktuelleFahrzeugart => _editFahrzeugart ?? FahrzeugartFuerNeubau;
        private Fahrradlayout AktuellesFahrradlayout => _editFahrzeugart.HasValue
            ? _editFahrradlayout : FahrradlayoutFuerNeubau;
        private Fahrradstil AktuellerFahrradstil => _editFahrzeugart.HasValue
            ? _editFahrradstil : FahrradstilFuerNeubau;

        private void BindeFahrrad()
        {
            AddUpdateBinding(new GetterValueBinding<int>(Group, "Fahrzeugart",
                () => (int)AktuelleFahrzeugart));
            AddUpdateBinding(new GetterValueBinding<int>(Group, "Fahrradlayout",
                () => (int)AktuellesFahrradlayout));
            AddUpdateBinding(new GetterValueBinding<int>(Group, "Fahrradstil",
                () => (int)AktuellerFahrradstil));
            AddUpdateBinding(new GetterValueBinding<bool>(Group, "BauartEdit",
                () => _editFahrzeugart.HasValue || (Tool()?.IsEditing ?? false)));
            AddUpdateBinding(new GetterValueBinding<bool>(Group, "BauartBeschaeftigt",
                () => Tool()?.BauartBeschaeftigt ?? false));
            AddBinding(new TriggerBinding<int>(Group, "SetFahrzeugart", SetFahrzeugart));
            AddBinding(new TriggerBinding<int>(Group, "SetFahrradlayout", SetFahrradlayout));
            AddBinding(new TriggerBinding<int>(Group, "SetFahrradstil", SetFahrradstil));
        }

        private void SetFahrzeugart(int art)
        {
            var tool = Tool();
            if (!Bauartvertrag.Gueltig((Fahrzeugart)art, FahrradlayoutFuerNeubau)
                || !FahrradBedienung.Artwechsel(_editFahrzeugart.HasValue || (tool?.IsEditing ?? false),
                    tool?.BauartBeschaeftigt ?? false)) return;
            if (FahrzeugartFuerNeubau == (Fahrzeugart)art) return;
            FahrzeugartFuerNeubau = (Fahrzeugart)art;
            // Gleicher Reiter raeumt im bisherigen SetzeReiter nicht ab.
            tool?.BauartGewechselt();
            SetTab(DraftTab);
            Revision++;
            Mod.log.Info("PLT-Bauart: Neubau " + FahrzeugartFuerNeubau
                + ", Fahrradlayout " + FahrradlayoutFuerNeubau + ", Stil " + FahrradstilFuerNeubau + ".");
        }

        private void SetFahrradlayout(int layout)
        {
            if (!FahrradBedienung.Layoutwahl(AktuelleFahrzeugart, layout,
                Tool()?.BauartBeschaeftigt ?? false)) return;
            // Die Art bleibt im Edit fest; ein anderes Fahrradlayout ist ein Edit.
            if (_editFahrzeugart.HasValue) _editFahrradlayout = (Fahrradlayout)layout;
            else FahrradlayoutFuerNeubau = (Fahrradlayout)layout;
            Revision++;
        }

        private void SetFahrradstil(int stil)
        {
            if (!FahrradBedienung.Stilwahl(AktuelleFahrzeugart, stil,
                Tool()?.BauartBeschaeftigt ?? false)) return;
            // Wie das Layout: im Edit ein anderer Stil ist ein Edit.
            if (_editFahrzeugart.HasValue) _editFahrradstil = (Fahrradstil)stil;
            else FahrradstilFuerNeubau = (Fahrradstil)stil;
            Revision++;
        }
    }
}
