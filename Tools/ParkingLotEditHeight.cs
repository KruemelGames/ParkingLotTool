using System;
using Game.Simulation;
using Unity.Collections;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private TerrainHeightData _editHeightSnapshot;
        private NativeArray<ushort> _editHeightCells;
        private NativeArray<ushort> _editHeightCellsDownscaled;
        private long _editHeightReadTick;
        private long _editNetRemovalTick;
        private long _editHeightWaitMilliseconds;

        private void ErfasseEdithoehenVorAbriss()
        {
            VerwerfeEdithoehen();
            try
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var source = _terrainSystem.GetHeightData(waitForPending: true);
                _editHeightReadTick = System.Diagnostics.Stopwatch.GetTimestamp();
                _editHeightWaitMilliseconds = (long)((_editHeightReadTick - start)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _editHeightCells = new NativeArray<ushort>(
                    source.heights.Length, Allocator.Persistent);
                NativeArray<ushort>.Copy(source.heights, _editHeightCells);
                if (source.downscaledHeights.IsCreated)
                {
                    _editHeightCellsDownscaled = new NativeArray<ushort>(
                        source.downscaledHeights.Length, Allocator.Persistent);
                    NativeArray<ushort>.Copy(source.downscaledHeights,
                        _editHeightCellsDownscaled);
                }
                _editHeightSnapshot = new TerrainHeightData(
                    _editHeightCells, _editHeightCellsDownscaled,
                    source.resolution, source.scale, source.offset,
                    source.hasBackdrop);
            }
            catch (Exception exception)
            {
                VerwerfeEdithoehen();
                Mod.log.Warn("PLT-Edithoehe: Hoehenkarte vor Abriss nicht "
                    + "gesichert; Neubau liest die aktuelle Karte: "
                    + exception.Message);
            }
        }

        private void VerwerfeEdithoehen()
        {
            _editHeightSnapshot = default;
            if (_editHeightCells.IsCreated) _editHeightCells.Dispose();
            if (_editHeightCellsDownscaled.IsCreated)
                _editHeightCellsDownscaled.Dispose();
            _editHeightCells = default;
            _editHeightCellsDownscaled = default;
            _editHeightReadTick = 0;
            _editNetRemovalTick = 0;
            _editHeightWaitMilliseconds = 0;
        }
    }
}
