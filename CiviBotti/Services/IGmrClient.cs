using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using CiviBotti.DataModels;

using GmrData.Gmr;

namespace CiviBotti.Services;

public interface IGmrClient
{
    Task<PackagedGame?> GetGameData(long gameId, string steamId, string ownerAuthKey);
    Task<string> GetPlayerIdFromAuthKey(string authKey);
    Task<TimeSpan?> GetTurntimer(GameData selectedGame, PlayerData player);
    Task<Dictionary<string, TimeSpan>?> GetTurntimers(GameData selectedGame);
    Task<HttpResponseMessage> UploadSave(GameData game, UserData user, MemoryStream ms);
    Task<Stream?> DownloadSave(GameData game, UserData user);
}