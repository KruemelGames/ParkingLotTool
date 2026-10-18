using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotFlaechennetzSystem
    {
        private OberflaechenMuster.Bereich? _musterMitte;
        internal bool SetzeMuster(OberflaechenMuster.Bereich? mitte)
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
                return (((signatur * 31 + _musterMitte.Value.Mitte.x.GetHashCode()) * 31
                    + _musterMitte.Value.Mitte.y.GetHashCode()) * 31 + _musterMitte.Value.Halbseite.GetHashCode()) * 31
                    + _musterMitte.Value.Richtung.GetHashCode();
            }
        }
    }
}
