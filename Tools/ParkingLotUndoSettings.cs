using System;

namespace ParkingLotTool.Tools
{
    /** Vollstaendiger, vom Panel gehaltener Teil des Zeichenzustands. */
    internal sealed class ParkingLotDraftSettingsSnapshot
    {
        internal string Vegetation;
        internal float EdgeSetback;
        internal float AisleWidth;
        internal float CrossWidth;
        internal float MedianWidth;
        internal float CrossBays;
        internal float RowAngle;
        internal bool GreenMedian;
        internal bool CrossCaps;
                internal bool Randstrassen;
        internal string AngleMode;
        internal string Engine;
        internal string SurfaceRoad;
        internal string SurfaceDecoration;
        internal bool SurfaceRoadOn;
        internal bool SurfaceDecorationOn;
        internal bool SurfaceApronOn;
        internal bool BayIcons;
    }

    public sealed partial class ParkingLotUISystem
    {
        internal ParkingLotDraftSettingsSnapshot CaptureDraftSettings()
            => new ParkingLotDraftSettingsSnapshot
            {
                Vegetation = VegetationJson,
                EdgeSetback = _edgeSetback.value,
                AisleWidth = _aisleWidth.value,
                CrossWidth = _crossWidth.value,
                MedianWidth = _medianWidth.value,
                CrossBays = _crossBays.value,
                RowAngle = _rowAngle.value,
                GreenMedian = _greenMedian.value,
                CrossCaps = _crossCaps.value,
                Randstrassen = _randstrassen.value,
                AngleMode = _angleMode.value,
                Engine = _engine.value,
                SurfaceRoad = _flaecheStrasse.value,
                SurfaceDecoration = _flaecheDeko.value,
                SurfaceRoadOn = _flaecheStrasseAn.value,
                SurfaceDecorationOn = _flaecheDekoAn.value,
                SurfaceApronOn = _vorflaecheAn.value,
                BayIcons = _buchtsymbole.value,
            };

        internal void RestoreDraftSettings(ParkingLotDraftSettingsSnapshot snapshot)
        {
            if (snapshot == null) return;
            var changed = false;
            changed |= UpdateValue(_vegetation, snapshot.Vegetation);
            changed |= UpdateValue(_edgeSetback, snapshot.EdgeSetback);
            changed |= UpdateValue(_aisleWidth, snapshot.AisleWidth);
            changed |= UpdateValue(_crossWidth, snapshot.CrossWidth);
            changed |= UpdateValue(_medianWidth, snapshot.MedianWidth);
            changed |= UpdateValue(_crossBays, snapshot.CrossBays);
            changed |= UpdateValue(_rowAngle, snapshot.RowAngle);
            changed |= UpdateValue(_greenMedian, snapshot.GreenMedian);
            changed |= UpdateValue(_crossCaps, snapshot.CrossCaps);
                changed |= UpdateValue(_randstrassen, snapshot.Randstrassen);
            changed |= UpdateValue(_angleMode, snapshot.AngleMode);
            changed |= UpdateValue(_engine, snapshot.Engine);
            changed |= UpdateValue(_flaecheStrasse, snapshot.SurfaceRoad);
            changed |= UpdateValue(_flaecheDeko, snapshot.SurfaceDecoration);
            changed |= UpdateValue(_flaecheStrasseAn, snapshot.SurfaceRoadOn);
            changed |= UpdateValue(_flaecheDekoAn, snapshot.SurfaceDecorationOn);
            changed |= UpdateValue(_vorflaecheAn, snapshot.SurfaceApronOn);
            changed |= UpdateValue(_buchtsymbole, snapshot.BayIcons);
            if (changed) Revision++;
        }

        /** Legt den Vorzustand nur ab, wenn die Aenderung wirklich griff. */
        private bool ChangeDraftSetting(string action, Func<bool> change)
        {
            var tool = Tool();
            var before = tool?.CaptureUndoState();
            if (!change()) return false;
            tool?.CommitUndoState(before, action);
            Revision++;
            return true;
        }
    }
}
