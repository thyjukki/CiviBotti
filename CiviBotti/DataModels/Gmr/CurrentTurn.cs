namespace CiviBotti.DataModels.Gmr;

using System;
using Newtonsoft.Json;

public class CurrentTurn
{
    [JsonProperty]
    public DateTime? Expires { get; set; }
    [JsonProperty]
    public bool IsFirstTurn { get; set; }
    [JsonProperty]
    public int Number { get; set; }
    [JsonProperty]
    public int PlayerNumber { get; set; }
    [JsonProperty]
    public bool Skipped { get; set; }
    [JsonProperty]
    public DateTime Started { get; set; }
    [JsonProperty]
    public int TurnId { get; set; }
    [JsonProperty]
    public long UserId { get; set; }
}