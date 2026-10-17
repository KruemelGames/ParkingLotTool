using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private void PflegeSurfacePreview()
        {
            if (_bauarbeiter) return;
            var slot = _uiSystem?.SurfacePreviewSlot;
            string wahl = slot == "road" && _uiSystem.FlaecheStrasseAn ? PavementSurfaceName
                : slot == "deco" && _uiSystem.FlaecheDekoAn ? GrassSurfaceName
                : slot == "zoning" && !ZoningFlaecheAus ? ZoningSurfaceName : null;
            bool zeigen = OberflaechenMuster.DarfZeigen(_uiSystem?.SurfacePreview == true,
                !string.IsNullOrEmpty(slot), _points.Count > 0, !string.IsNullOrEmpty(wahl),
                m_ToolSystem.activeTool == this && Werkzeugzustand.ZeigtWerkzeugvorschau(_reiter),
                _buildStage != BuildStage.Idle || _buildRequestedWhenReady || _avPhase != AvPhase.Idle,
                _bauarbeiter, _speicherLaeuft,
                World.GetExistingSystemManaged<ParkingLotSyncSystem>()?.ArbeitLaeuft == true);
            var muster = World.GetOrCreateSystemManaged<ParkingLotMusterSystem>();
            if (zeigen) muster.Pflege(wahl,_points[0]);
            else muster.Entferne("nicht sichtbar");
        }

        internal void EntferneSurfacePreview(string grund)
        {
            if (!_bauarbeiter) World.GetExistingSystemManaged<ParkingLotMusterSystem>()?.Entferne(grund);
        }

        private bool _musterMaskenfehler;
        private float2? _surfaceMusterMitte;
        internal void MusterMaske(float2? mitte)
        {
            _surfaceMusterMitte = mitte;
            try
            {
                if (_flaechennetz?.SetzeMuster(mitte) == true && _areaPreviewLayout != null)
                    FuettereFlaechennetz(_areaPreviewLayout);
                _musterMaskenfehler = false;
            }
            catch (System.Exception e)
            {
                if (_musterMaskenfehler) return;
                _musterMaskenfehler = true;
                ParkingLotHintergrundDiagnose.Sicher(() => Mod.log.Warn("PLT-SurfacePreview: Farbnetzmaske fehlt: " + e.Message));
            }
        }
    }
}
