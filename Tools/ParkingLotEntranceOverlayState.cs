using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Kurzlebiger Zeichenstand der Zufahrts-Bedienung; keine Baugeometrie. */
    internal sealed class ParkingLotEntranceOverlayState
    {
        internal readonly List<float3> Handles = new List<float3>();
        internal int HoverIndex = -1;
        internal int DragIndex = -1;
        internal bool HasCandidate;
        internal bool CandidateValid;
        internal float CandidateWidth;
        internal Line3.Segment CandidateRoad;
        internal Line3.Segment CandidateSpacing;
        internal bool HasSnapGuide;
        internal Line3.Segment SnapGuide;

        internal void Clear()
        {
            Handles.Clear();
            HoverIndex = -1;
            DragIndex = -1;
            HasCandidate = false;
            CandidateValid = false;
            CandidateWidth = 0f;
            CandidateRoad = default;
            CandidateSpacing = default;
            HasSnapGuide = false;
            SnapGuide = default;
        }
    }
}
