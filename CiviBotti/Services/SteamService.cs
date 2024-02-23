namespace CiviBotti.Services;

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Configurations;
using DataModels.Steam;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class SteamApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _steamApiKey;
    private readonly string _steamApiUrl;
    public SteamApiClient(HttpClient httpClient, IOptions<BotConfiguration> configuration) {
        _httpClient = httpClient;
        
        _steamApiUrl = configuration.Value.SteamApiUrl;
        _steamApiKey = configuration.Value.SteamApiKey;
    }

    public async Task<string> GetSteamUserName(string steamId) {
        var dic = await GetSteamUserNames(new List<string> {steamId});
        return dic.GetValueOrDefault(steamId, "UNKNOWN");
    }

    public async Task<Dictionary<string, string>> GetSteamUserNames(IEnumerable<string> steamId) {
        var url =
            $"{_steamApiUrl}ISteamUser/GetPlayerSummaries/v0002/?key={_steamApiKey}&steamids={string.Join(",", steamId.Distinct())}";

        var request = await _httpClient.GetAsync(url);
        var data = await request.Content.ReadAsStringAsync();
        var responseObject = JObject.Parse(data).SelectToken("response");
        if (responseObject == null) {
            return new Dictionary<string, string>();
        }
        
        var steamPlayers = JsonConvert.DeserializeObject<SteamPlayersContainer>(responseObject.ToString());
        if (steamPlayers == null) {
            return new Dictionary<string, string>();
        }

        var dic = steamPlayers.Players.ToDictionary(player => player.SteamId,
            player => player.Personaname);

        return dic;
    }
}