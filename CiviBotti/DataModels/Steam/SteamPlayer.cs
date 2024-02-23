namespace CiviBotti.DataModels.Steam;

using Newtonsoft.Json;

public class SteamPlayer
{
    [JsonProperty("steamid")]
    public required string SteamId { get; set; }

    [JsonProperty("personaname")]
    public required string Personaname { get; set; }
}