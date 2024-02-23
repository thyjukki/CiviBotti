namespace CiviBotti.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataModels;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

public class GameContainerService
{
    private readonly List<GameData> _gamesContainer = new ();
    private readonly Database _database;
    private readonly SteamApiClient _steamApiClient;
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<GameContainerService> _logger;
    public IEnumerable<GameData> Games =>  _gamesContainer;

    public void Add(GameData game) => _gamesContainer.Add(game);
    
    public GameData? GetGameFromChat(long chatId) {
        return (from game in Games from chat in game.Chats where chat == chatId select game).FirstOrDefault();
    }

    public GameContainerService(Database database, SteamApiClient steamApiClient, ITelegramBotClient botClient, ILogger<GameContainerService> logger) {
        _database = database;
        _steamApiClient = steamApiClient;
        _botClient = botClient;
        _logger = logger;
    }
    
    private async Task InitializePlayersForGame(GameData game, IReadOnlyDictionary<string, string> playerSteamNames) {
        var ownerName = playerSteamNames.GetValueOrDefault(game.Owner.SteamId, game.Owner.SteamId);
        _logger.LogInformation("{Game} {GameOwner}", game, ownerName);
        _logger.LogInformation(" chats:");
        foreach (var chat in game.Chats) {
            _logger.LogInformation("  -{Chat}", chat);
        }

        _logger.LogInformation(" players:");
        foreach (var player in game.Players) {
            if (!playerSteamNames.TryGetValue(player.SteamId, out var steamName)) {
                _logger.LogInformation("  -{Player} ({PlayerTurnOrder}) {PlayerUser} Error getting steam name", player, player.TurnOrder, player.SteamId);
                continue;
            }
            player.SteamName = steamName;

            if (player.User != null) {
                var user = await _botClient.GetChatAsync(player.User.Id);
                if (user.Username == null) {
                    _logger.LogInformation("  -{Player} ({PlayerTurnOrder}) {PlayerUser} Error getting user", player, player.TurnOrder, player.SteamId);
                }
                else {
                    player.TgName = user.Username;
                }
            }

            _logger.LogInformation("  -{Player} ({PlayerTurnOrder}) {PlayerUser}", player, player.TurnOrder, player.TgName);
        }
    }

    public async Task InitializeAsync() {
        _gamesContainer.Clear();
        
        var gameData = GameData.GetAllGames(_database);
        
        _gamesContainer.AddRange(gameData);

        var players = (from game in Games from player in game.Players select player.SteamId).ToList();
        var playerSteamNames = await _steamApiClient.GetSteamUserNames(players);

        foreach (var game in Games) {
            await InitializePlayersForGame(game, playerSteamNames);
        }
    }
}