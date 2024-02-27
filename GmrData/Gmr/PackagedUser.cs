using Newtonsoft.Json;

namespace GmrData.Gmr;

public class PackagedUser
{
    [JsonProperty]
    public int TurnOrder { get; set; }
    [JsonProperty]
    public long UserId { get; set; }
}