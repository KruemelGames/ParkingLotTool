using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotFlaechennetzSystem
    {
        private float2? _musterMitte;
        internal bool SetzeMuster(float2? mitte)
        {
            if (_musterMitte.HasValue == mitte.HasValue
                && (!mitte.HasValue || _musterMitte.Value.Equals(mitte.Value))) return false;
            _musterMitte = mitte;
            return true;
        }

        private long MitMustermaske(long signatur)
        {
            if (!_musterMitte.HasValue) return signatur;
            unchecked {
                return ((signatur * 31 + _musterMitte.Value.x.GetHashCode()) * 31
                    + _musterMitte.Value.y.GetHashCode()) * 31 + 1;
            }
        }
    }
}
