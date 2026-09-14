using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotOverlay
    {
        private static Color SnapColor(ParkingLotToolSystem.SnapKind snap)
        {
            switch (snap)
            {
                case ParkingLotToolSystem.SnapKind.RoadEdge: return RoadSnapColor;
                case ParkingLotToolSystem.SnapKind.ObjectSide: return ObjectSnapColor;
                case ParkingLotToolSystem.SnapKind.AreaEdge: return AreaSnapColor;
                case ParkingLotToolSystem.SnapKind.Guide: return GuideSnapColor;
                case ParkingLotToolSystem.SnapKind.ZoneGrid: return ZoneGridSnapColor;
                case ParkingLotToolSystem.SnapKind.Crossing: return CrossSnapColor;
                default: return AngleSnapColor;
            }
        }

        private static UnityEngine.Color TeilflaechenFarbe(int index)
        {
            switch (index % 5)
            {
                case 0: return Accent;
                case 1: return ZoningColor;
                case 2: return AreaSnapColor;
                case 3: return ObjectSnapColor;
                default: return PointColor;
            }
        }

        private static void DrawEntranceEditing(ParkingLotPreviewBuffer buffer,
            ParkingLotEntranceOverlayState state)
        {
            if (state == null) return;

            for (var i = 0; i < state.Handles.Count; i++)
            {
                var active = i == state.DragIndex || i == state.HoverIndex;
                buffer.DrawCircle(EntranceHandleColor, state.Handles[i],
                    active ? EntranceActiveDiameter : EntranceHandleDiameter);
            }

            if (!state.HasCandidate) return;
            var color = state.CandidateValid
                ? EntranceCandidateColor : EntranceBlockedColor;
            buffer.DrawLine(color, color, 0f,
                OverlayRenderSystem.StyleFlags.Projected,
                state.CandidateSpacing, EntranceSpacingWidth, default);
            buffer.DrawLine(color, Alpha(color, FillHover), GridLineWidth,
                OverlayRenderSystem.StyleFlags.Projected,
                state.CandidateRoad,
                math.max(EntranceSpacingWidth, state.CandidateWidth),
                default);
            buffer.DrawCircle(color, state.CandidateRoad.a,
                state.CandidateValid ? EntranceActiveDiameter : SnapDiameter);

            if (state.HasSnapGuide)
                buffer.DrawLine(EntranceHandleColor, EntranceHandleColor, 0f,
                    OverlayRenderSystem.StyleFlags.Projected,
                    state.SnapGuide, SnapGuideWidth, default);
        }

    }
}
