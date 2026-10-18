using Newtonsoft.Json;
using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotUISystem
    {
        private sealed partial class UserDefaults
        {
            [JsonProperty("EchteVorschau", NullValueHandling = NullValueHandling.Ignore)]
            public int? EchteVorschau { get; set; }
            private bool _alteSurfacePreview;
            // Nur lesen: die naechste Speicherung enthaelt ausschliesslich den neuen Wert.
            [JsonProperty("SurfacePreview")]
            private bool AlteSurfacePreview { set { _alteSurfacePreview = value; } }
            [JsonIgnore]
            public EchteVorschauArt GeleseneVorschau => Geometry.EchteVorschau.Lesen(EchteVorschau, _alteSurfacePreview);
        }
    }
}
