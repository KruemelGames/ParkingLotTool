using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private readonly Vorschaugriff _musterGriff=new Vorschaugriff();
        private long _musterFormstand=long.MinValue;
        private bool _musterGriffHover;
        private float _musterGriffHoehe;
        private int _musterAbbruchBild=-1;
        internal bool MustergriffHinweis => MustergriffSichtbar() && (_musterGriffHover || _musterGriff.Zieht);

        private bool MustergriffSichtbar()
            => !_bauarbeiter && m_ToolSystem.activeTool==this
                && (_uiSystem?.VorschauArt ?? EchteVorschauArt.Aus)!=EchteVorschauArt.Aus
                && _closed && !_layoutDirty && _buildTask==null && _areaPreviewLayout!=null
                && _buildStage==BuildStage.Idle && !_speicherLaeuft
                && !AusrichtWahlAktiv && !MarkerMode && !MeldeLotWahlAktiv
                && Werkzeugzustand.ZeigtWerkzeugvorschau(_reiter)
                && _objektMusterBereich.HasValue && _musterGriff.Bereich.HasValue
                && World.GetExistingSystemManaged<ParkingLotSyncSystem>()?.ArbeitLaeuft!=true
                && World.GetExistingSystemManaged<ParkingLotHintergrundSystem>()?.Laeuft!=true;

        private void VergissMustergriff()
        { _musterGriff.Vergiss(); _musterGriffHover=false; _musterFormstand=long.MinValue; }

        /** Vor allen Entwurfs-/Bauklicks; Rohmaus bleibt auch bei Modifiern lesbar. */
        private bool HandleMustergriff(bool welt, bool rechts, bool esc)
        {
            rechts |= Mouse.current?.rightButton.wasPressedThisFrame==true;
            esc |= Keyboard.current?.escapeKey.wasPressedThisFrame==true;
            if (_musterGriff.Zieht)
            {
                // Enter/Panel-Bauen darf der Griff nie aufhalten.
                if (!MustergriffSichtbar() || _panelBuildRequest || BuildRequested())
                { _musterGriff.Ende(_points,true); _musterGriffHover=false; return false; }
                bool abbrechen=rechts || esc || Mouse.current?.rightButton.wasPressedThisFrame==true
                    || Keyboard.current?.escapeKey.wasPressedThisFrame==true;
                if (_hasHover) { _musterGriff.Bewege(_hoverPosition.xz); _musterGriffHoehe=_hoverPosition.y; }
                if (abbrechen || (Mouse.current!=null ? !Mouse.current.leftButton.isPressed : applyAction?.IsPressed()!=true))
                {
                    _musterGriff.Ende(_points,abbrechen);
                    if (abbrechen) _musterAbbruchBild=UnityEngine.Time.frameCount;
                    MeldeMustergriffEnde(abbrechen);
                }
                return true;
            }
            bool sichtbar=MustergriffSichtbar();
            _musterGriffHover=Vorschaugriff.Trifft(sichtbar,welt && _hasHover,
                _dragPoint>=0 || _dragEdge>=0 || _dragEntrance>=0,_hoverPosition.xz,
                _musterGriff.Bereich.GetValueOrDefault().Mitte,PointHitDistance);
            bool links=Mouse.current?.leftButton.wasPressedThisFrame==true || applyAction?.WasPressedThisFrame()==true;
            if (!Vorschaugriff.Beginnt(_musterGriffHover,links,rechts || esc,_panelBuildRequest || BuildRequested())
                || !_musterGriff.Beginne(_hoverPosition.xz)) return false;
            // Maske bleibt bis Loslassen an der alten Stelle; nur die echten Teile gehen weg.
            World.GetExistingSystemManaged<ParkingLotMusterSystem>()?.Entferne("Ausschnitt wird gezogen");
            ParkingLotHintergrundDiagnose.Sicher(()=>Mod.log.Info("PLT-EchteVorschau: Ausschnittzug begonnen"));
            return true;
        }

        private void MeldeMustergriffEnde(bool abbrechen)
            => ParkingLotHintergrundDiagnose.Sicher(()=>Mod.log.Info("PLT-EchteVorschau: Ausschnittzug "
                + (abbrechen ? "abgebrochen" : "beendet") + "; Stand " + _musterGriff.Stand
                + "; Mitte " + _musterGriff.Bereich.Value.Mitte + "; Halbseite " + _musterGriff.Bereich.Value.Halbseite));

        private void ZeichneMustergriff(ParkingLotPreviewBuffer buffer)
        {
            if (!MustergriffSichtbar()) return;
            var b=_musterGriff.Bereich.Value;
            var p=_musterGriff.Zieht ? _musterGriff.Ziel : b.Mitte;
            var mitte=new float3(p.x,_musterGriffHoehe,p.y);
            var farbe=_musterGriffHover || _musterGriff.Zieht ? MustergriffAktivColor : MustergriffColor;
            buffer.DrawRing(farbe,mitte,ActivePointDiameter,HandleOutlineWidth);
            for(int i=0;i<4;i++)
            {
                var d=i==0 ? new float3(1,0,0) : i==1 ? new float3(-1,0,0)
                    : i==2 ? new float3(0,0,1) : new float3(0,0,-1);
                var quer=new float3(-d.z,0,d.x);
                var ende=mitte+d*EdgeArrowReach;
                buffer.DrawLine(farbe,new Line3.Segment(mitte+d*EdgeArrowGap,ende),PreviewLineWidth);
                buffer.DrawLine(farbe,new Line3.Segment(ende,ende-d*MusterPfeilspitze+quer*MusterPfeilbreite),PreviewLineWidth);
                buffer.DrawLine(farbe,new Line3.Segment(ende,ende-d*MusterPfeilspitze-quer*MusterPfeilbreite),PreviewLineWidth);
            }
            if (!_musterGriff.Zieht) return;
            var x=new float3(b.Richtung.x,0,b.Richtung.y)*b.Halbseite;
            var y=new float3(-b.Richtung.y,0,b.Richtung.x)*b.Halbseite;
            buffer.DrawLine(farbe,new Line3.Segment(mitte-x-y,mitte+x-y),PolygonLineWidth);
            buffer.DrawLine(farbe,new Line3.Segment(mitte+x-y,mitte+x+y),PolygonLineWidth);
            buffer.DrawLine(farbe,new Line3.Segment(mitte+x+y,mitte-x+y),PolygonLineWidth);
            buffer.DrawLine(farbe,new Line3.Segment(mitte-x+y,mitte-x-y),PolygonLineWidth);
        }
    }
}
