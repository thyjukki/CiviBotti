namespace CiviBotti.DataModels.Gmr;

using System.Collections.Generic;
using Newtonsoft.Json;

public class PackagedGame
{
    [JsonProperty]
    public required CurrentTurn CurrentTurn { get; set; }
    [JsonProperty]
    public required int GameId { get; set; }
    [JsonProperty]
    public required string Name { get; set; }
    [JsonProperty]
    public required List<PackagedUser> Players { get; set; }
    [JsonProperty]
    public required string Type { get; set; }
}