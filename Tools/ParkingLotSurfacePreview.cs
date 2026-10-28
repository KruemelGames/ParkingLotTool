using ParkingLotTool.Geometry;
using UnityEngine.InputSystem;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private void PflegeSurfacePreview()
        {
            if (_bauarbeiter) return;
            if (_musterFormstand!=_geometryRevision && _closed && !_layoutDirty && _buildTask==null
                && _dragPoint<0 && _dragEdge<0 && _dragEntrance<0)
            { _musterGriff.PruefeForm(_points); _musterFormstand=_geometryRevision; }
            var art = _uiSystem?.VorschauArt ?? EchteVorschauArt.Aus;
            bool an = art != EchteVorschauArt.Aus,
                aktuell = _areaPreviewLayout != null && !_layoutDirty && _buildTask == null,
                ziehen = _dragPoint >= 0 || _dragEdge >= 0 || _dragEntrance >= 0 || _musterGriff.Zieht,
                werkzeug = m_ToolSystem.activeTool == this && Werkzeugzustand.ZeigtWerkzeugvorschau(_reiter),
                bauphase = _buildStage != BuildStage.Idle || _buildRequestedWhenReady || _avPhase != AvPhase.Idle,
                andere = World.GetExistingSystemManaged<ParkingLotSyncSystem>()?.ArbeitLaeuft == true
                    || World.GetExistingSystemManaged<ParkingLotHintergrundSystem>()?.Laeuft == true;
            aktuell = aktuell && (art == EchteVorschauArt.Flaechen || ReferenceEquals(_musterObjektLayout,_areaPreviewLayout));
            bool zeigen = OberflaechenMuster.DarfZeigen(an, _closed && _points.Count >= 3, aktuell, ziehen,
                werkzeug, bauphase, _bauarbeiter, _speicherLaeuft, andere);
            int zustand = zeigen ? 0 : !an ? 1 : !_closed ? 2 : ziehen ? 3 : !aktuell ? 4
                : !werkzeug ? 5 : bauphase ? 6 : _speicherLaeuft ? 7 : 8;
            if (zustand!=_musterZustand || art!=_musterZustandsart)
            {
                _musterZustand=zustand; _musterZustandsart=art;
                string grund=zustand==0 ? art + ": Layout aktuell" : zustand==1 ? "Schalter aus"
                    : zustand==2 ? "Shape offen" : zustand==3 ? "Punkt/Kante wird gezogen"
                    : zustand==4 ? "Layout fehlt oder rechnet" : zustand==5 ? "Werkzeug nicht aktiv"
                    : zustand==6 ? $"Bauphase {_buildStage}/{_buildRequestedWhenReady}/{_avPhase}"
                    : zustand==7 ? "Speichern laeuft" : "Sync/Hintergrund laeuft";
                ParkingLotHintergrundDiagnose.Sicher(() => Mod.log.Info("PLT-SurfacePreview: Zustand " + grund));
            }
            var muster = World.GetOrCreateSystemManaged<ParkingLotMusterSystem>();
            if (!zeigen) { muster.Entferne("nicht sichtbar"); return; }
            // Nur bei Aenderung Strings/Delegates/Materialliste erzeugen; im ruhenden Bild nichts davon.
            var schluessel=(_areaPreviewLayout,art,_musterObjektStand,_musterGriff.Stand,
                GrassSurfaceName,PavementSurfaceName,ZoningFlaecheAus ? null : ZoningSurfaceName,
                _uiSystem.FlaecheStrasseAn,_uiSystem.FlaecheDekoAn);
            if (_musterMaterialien==null || !_musterPflegeSchluessel.Equals(schluessel))
            {
                _musterPflegeSchluessel=schluessel; _musterPflegeArt=art;
                // Siebte Gruppe: Strassenbelag der Alleys (Issue #22), Asphaltmaterial.
                _musterMaterialien=new[] {schluessel.Item5,schluessel.Item6,schluessel.Item7,
                    schluessel.Item6,schluessel.Item6,schluessel.Item6,schluessel.Item6};
                _musterSchalter=$"{_uiSystem.FlaecheStrasseAn}/{_uiSystem.FlaecheDekoAn}/{_musterGriff.Stand}";
            }
            bool esc=Keyboard.current?.escapeKey.wasPressedThisFrame==true && _musterAbbruchBild!=UnityEngine.Time.frameCount;
            if (art!=EchteVorschauArt.Flaechen)
                muster.PflegeObjekte(_areaPreviewLayout,art,_musterObjektStand+_musterGriff.Stand,
                    _musterObjektBerechnung ??= ()=>BerechneObjektvorschau(_musterPflegeArt),esc);
            else muster.Pflege(_areaPreviewLayout,_musterMaterialien,_musterSchalter,
                _musterBerechnung ??= BerechneSurfacePreview,esc);
        }

        internal void EntferneSurfacePreview(string grund)
        {
            if (_bauarbeiter) return;
            if (_musterGriff.Zieht) _musterGriff.Ende(_points,true);
            _musterGriffHover=false;
            World.GetExistingSystemManaged<ParkingLotMusterSystem>()?.Entferne(grund);
        }

        private EchteVorschauArt _objektMusterArt;
        private OberflaechenMuster.Bereich? _objektMusterBereich;
        private bool _musterMaskenfehler;
        private int _musterZustand=-1;
        private EchteVorschauArt _musterZustandsart, _musterPflegeArt;
        private (ParkingLayout,EchteVorschauArt,long,long,string,string,string,bool,bool) _musterPflegeSchluessel;
        private string[] _musterMaterialien;
        private string _musterSchalter;
        private System.Func<OberflaechenMuster.Plan> _musterBerechnung;
        private System.Func<ParkingLotMusterSystem.Objektplan> _musterObjektBerechnung;
        private OberflaechenMuster.Bereich? _surfaceMusterMitte;
        internal void MusterMaske(OberflaechenMuster.Bereich? mitte, EchteVorschauArt art = EchteVorschauArt.Flaechen)
        {
            if (_musterGriff.Zieht && !mitte.HasValue) return;
            if (mitte.HasValue)
            {
                _musterGriff.MerkeAusgabe(mitte.Value);
                var hoehen=_terrainSystem.GetHeightData(waitForPending:true);
                _musterGriffHoehe=Game.Simulation.TerrainUtils.SampleHeight(ref hoehen,
                    new Unity.Mathematics.float3(mitte.Value.Mitte.x,0,mitte.Value.Mitte.y));
            }
            var aenderung = _objektMusterArt != (mitte.HasValue ? art : EchteVorschauArt.Aus)
                || !_objektMusterBereich.Equals(mitte);
            _objektMusterArt = mitte.HasValue ? art : EchteVorschauArt.Aus;
            _objektMusterBereich = mitte;
            if (_overlay != null)
            {
                _overlay.EchteMusterArt = _objektMusterArt;
                _overlay.EchteMusterBereich = mitte;
            }
            _surfaceMusterMitte = art == EchteVorschauArt.Flaechen ? mitte : null;
            try
            {
                if (_flaechennetz?.SetzeMuster(_surfaceMusterMitte) == true && _areaPreviewLayout != null)
                    FuettereFlaechennetz(_areaPreviewLayout);
                if (aenderung) FuettereePflanzen();
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
