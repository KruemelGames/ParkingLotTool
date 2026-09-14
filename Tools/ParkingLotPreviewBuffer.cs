using Colossal.Mathematics;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;

namespace ParkingLotTool.Tools
{
    // Nur fuer diesen Frame. Native Puffer werden weder gespeichert noch entsorgt.
    // Der Wrapper erzwingt dieselbe Gelaendeprojektion fuer Linien und Griffe.
    internal readonly struct ParkingLotPreviewBuffer
    {
        private readonly OverlayRenderSystem.Buffer _buffer;
        internal ParkingLotPreviewBuffer(OverlayRenderSystem.Buffer buffer)
            => _buffer = buffer;

        internal void DrawCircle(Color color, float3 position, float diameter)
        {
            _buffer.DrawCircle(color, Alpha(color, FillQuiet), HandleOutlineWidth,
                OverlayRenderSystem.StyleFlags.Projected,
                new float2(0f, 1f), position, diameter);
        }

        internal void DrawLine(Color color, Line3.Segment line, float width,
            bool cameraFacing = false)
        {
            _buffer.DrawLine(color, color, 0f, OverlayRenderSystem.StyleFlags.Projected,
                line, width, default);
        }

        internal void DrawDashedLine(Color color, Line3.Segment line, float width,
            float dashLength, float gapLength)
        {
            _buffer.DrawDashedLine(color, color, 0f,
                OverlayRenderSystem.StyleFlags.Projected, line, width,
                dashLength, gapLength);
        }

        // Flaechenbreiten sind Baumasse. Sie duerfen nicht mit Griffgroessen
        // skaliert oder geklemmt werden, sonst zeigt die Vorschau falschen Platzbedarf.
        internal void DrawLine(Color outline, Color fill, float outlineWidth,
            OverlayRenderSystem.StyleFlags flags, Line3.Segment line,
            float width, float2 roundness)
        {
            _buffer.DrawLine(outline, fill, outlineWidth, flags,
                line, width, roundness);
        }
    }
}
