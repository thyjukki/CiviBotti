namespace CiviBotti.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataModels;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

public class GameContainerService(
    IDatabase database,
    ISteamApiClient steamApiClient,
    ITelegramBotClient botClient,
    ILogger<GameContainerService> logger) : IGameContainerService
{
    private readonly List<GameData> _gamesContainer = [];
    public IEnumerable<GameData> Games =>  _gamesContainer;

    public void Add(GameData game) => _gamesContainer.Add(game);
    
    public GameData? GetGameFromChat(long chatId) {
        return (from game in Games from chat in game.Chats where chat == chatId select game).FirstOrDefault();
    }

    private async Task InitializePlayersForGame(GameData game, Dictionary<string, string> playerSteamNames) {
        var ownerName = playerSteamNames.GetValueOrDefault(game.Owner.SteamId, game.Owner.SteamId);
        logger.LogInformation("{Game} {GameOwner}", game, ownerName);
        logger.LogInformation(" chats:");
        foreach (var chat in game.Chats) {
            logger.LogInformation("  -{Chat}", chat);
        }

        logger.LogInformation(" players:");
        foreach (var player in game.Players) {
            if (!playerSteamNames.TryGetValue(player.SteamId, out var steamName)) {
                logger.LogInformation("  -{Player} ({PlayerTurnOrder}) {PlayerUser} Error getting steam name", player, player.TurnOrder, player.SteamId);
                continue;
            }
            player.SteamName = steamName;

            if (player.User != null) {
                var user = await botClient.GetChatAsync(player.User.Id);
                if (user.Username == null) {
                    logger.LogInformation("  -{Player} ({PlayerTurnOrder}) {PlayerUser} Error getting user", player, player.TurnOrder, player.SteamId);
                }
                else {
                    player.TgName = user.Username;
                }
            }

            logger.LogInformation("  -{Player} ({PlayerTurnOrder}) {PlayerUser}", player, player.TurnOrder, player.TgName);
        }
    }

    public async Task InitializeAsync() {
        _gamesContainer.Clear();
        
        var gameData = GameData.GetAllGames(database);
        
        _gamesContainer.AddRange(gameData);

        var players = (from game in Games from player in game.Players select player.SteamId).ToList();
        var playerSteamNames = await steamApiClient.GetSteamUserNames(players);

        foreach (var game in Games) {
            await InitializePlayersForGame(game, playerSteamNames);
        }
    }
}