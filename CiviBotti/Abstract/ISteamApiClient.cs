using System.Collections.Generic;
using System.Threading.Tasks;

namespace CiviBotti.Services;

public interface ISteamApiClient
{
    Task<string> GetSteamUserName(string steamId);
    Task<Dictionary<string, string>> GetSteamUserNames(IEnumerable<string> steamId);
}