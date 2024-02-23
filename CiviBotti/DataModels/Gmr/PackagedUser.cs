namespace CiviBotti.DataModels.Gmr;

using Newtonsoft.Json;

public class PackagedUser
{
    [JsonProperty]
    public int TurnOrder { get; set; }
    [JsonProperty]
    public long UserId { get; set; }
}