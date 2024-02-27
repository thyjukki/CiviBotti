using Newtonsoft.Json;

namespace GmrData.Gmr;

public class PackagedGameContainer
{
    [JsonProperty]
    public required List<PackagedGame> Games { get; set; }
}