namespace CiviBotti.DataModels.Steam;

using System.Collections.Generic;
using Newtonsoft.Json;

public class SteamPlayersContainer
{
    [JsonProperty("players")]
    public required List<SteamPlayer> Players { get; set; }
}