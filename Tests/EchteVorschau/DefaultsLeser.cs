using Newtonsoft.Json;
namespace ParkingLotTool.Tools
{
    // Nur der Container ist Testgeruest, die Eigenschaften/JsonAttribute sind produktiv.
    public sealed partial class ParkingLotUISystem
    {
        internal static int LiesVorschau(string json) => (int)JsonConvert.DeserializeObject<UserDefaults>(json).GeleseneVorschau;
        internal static string SchreibeVorschau(string json)
        {
            var d=JsonConvert.DeserializeObject<UserDefaults>(json);
            d.EchteVorschau=(int)d.GeleseneVorschau;
            return JsonConvert.SerializeObject(d);
        }
    }
}
