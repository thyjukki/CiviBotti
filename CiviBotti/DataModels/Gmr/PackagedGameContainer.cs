namespace CiviBotti.DataModels.Gmr;

using System.Collections.Generic;
using Newtonsoft.Json;

public class PackagedGameContainer
{
    [JsonProperty]
    public required List<PackagedGame> Games { get; set; }
}