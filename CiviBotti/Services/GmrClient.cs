using CiviBotti.Exceptions;

using GmrData.Gmr;

namespace CiviBotti.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Configurations;
using DataModels;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

public class GmrClient(
    HttpClient httpClient,
    IOptions
        <GmrConfiguration> botConfigGmrUrl) : IGmrClient
{
    private readonly string _gmrUrl = botConfigGmrUrl.Value.GmrUrl;

    public async Task<PackagedGame?> GetGameData(long gameId, string steamId, string ownerAuthKey) {
        var url =
            $"{_gmrUrl}api/Diplomacy/GetGamesAndPlayers?playerIDText={steamId}&authKey={ownerAuthKey}";


        var request = await httpClient.GetAsync(url);
        var data = await request.Content.ReadAsStringAsync();

        
        var result = JsonConvert.DeserializeObject<PackagedGameContainer>(data);

        if (result == null) return null;
        
        var game = result.Games.Find(item => item.GameId == gameId);
        if (game == null) throw new MissingOwnerException(data);
        
        return game;
    }

    public async Task<string> GetPlayerIdFromAuthKey(string authKey) {
        var url = $"{_gmrUrl}api/Diplomacy/AuthenticateUser?authKey={authKey}";

        var request = await httpClient.GetAsync(url);
        var html = await request.Content.ReadAsStringAsync();

        return html;
    }
        
    public async Task<TimeSpan?> GetTurntimer(GameData selectedGame, PlayerData player) {
        var dict = await GetTurntimers(selectedGame);
        if (dict == null) {
            return null;
        }
        
        return dict.TryGetValue(player.SteamId, out var ts) ? ts : null;
    }

    public async Task<Dictionary<string, TimeSpan>?> GetTurntimers(GameData selectedGame) {
        var url = $"{_gmrUrl}Game/Details?id={selectedGame.GameId}";
        var response = await httpClient.PostAsync(url, null);
        if (!response.IsSuccessStatusCode) {
            return null;
        }

        var doc = new HtmlDocument();
        var html = await response.Content.ReadAsStringAsync();
        doc.LoadHtml(html);

        var divs = doc.DocumentNode.SelectNodes("//div[@class=\"game-player average\"]");
        
        var result = new Dictionary<string, TimeSpan>();
        foreach (var innerHtml in divs.Select(div => div.InnerHtml)) {
            var idGroup = Regex.Match(innerHtml, @"/Community#\s*([\d+]*)", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            if (!idGroup.Success) {
                continue;
            }

            var steamId = idGroup.Groups[1].Value;

            var dayGroup = Regex.Match(innerHtml, "(\\d+) day", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            var hourGroup = Regex.Match(innerHtml, "(\\d+) hour", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            var minuteGroup = Regex.Match(innerHtml, "(\\d+) minute", RegexOptions.None, TimeSpan.FromMilliseconds(100));

            var day = 0;
            if (dayGroup.Success) {
                day = int.Parse(dayGroup.Groups[1].Value);
            }
            
            var hour = 0;
            if (hourGroup.Success) {
                hour = int.Parse(hourGroup.Groups[1].Value);
            }

            var minute = 0;
            if (minuteGroup.Success) {
                minute = int.Parse(minuteGroup.Groups[1].Value);
            }

            var ts = new TimeSpan(day, hour, minute, 0);
            
            result.Add(steamId, ts);
        }

        return result;
    }
    

    public async Task<HttpResponseMessage> UploadSave(GameData game, UserData user, MemoryStream ms) {
        var form = new MultipartFormDataContent(
                $"Upload----{(object)DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)}") {
                { new StringContent(game.TurnId), "turnId" },
                { new StringContent("False"), "isCompressed" },
                { new StringContent(user.AuthKey), "authKey" },
                { new StreamContent(ms), "saveFileUpload", $"{game.TurnId}.Civ5Save" }
            };
        return await httpClient.PostAsync($"{_gmrUrl}Game/UploadSaveClient",  form);
    }

    public async Task<Stream?> DownloadSave(GameData game, UserData user) {
        var response = await httpClient.GetAsync($"{_gmrUrl}api/Diplomacy/GetLatestSaveFileBytes?authKey={user.AuthKey}&gameId={game.GameId}");
        
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadAsStreamAsync();
    }
}