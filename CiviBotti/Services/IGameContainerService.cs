using System.Collections.Generic;
using System.Threading.Tasks;

using CiviBotti.DataModels;

namespace CiviBotti.Services;

public interface IGameContainerService
{
    IEnumerable<GameData> Games { get; }
    void Add(GameData game);
    GameData? GetGameFromChat(long chatId);
    Task InitializeAsync();
}