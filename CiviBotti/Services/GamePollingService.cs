namespace CiviBotti.Services;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataModels;
using DataModels.Gmr;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

public class GamePollingService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly GameContainerService _gameContainer;
    private readonly Database _database;
    private readonly GmrClient _gmrClient;
    private readonly ILogger<GamePollingService> _logger;
    private readonly SteamApiClient _steamClient;
    public GamePollingService(SteamApiClient steamClient, ITelegramBotClient botClient, GameContainerService gameContainer, Database database, GmrClient gmrClient, ILogger<GamePollingService> logger) {
        _steamClient = steamClient;
        _botClient = botClient;
        _gameContainer = gameContainer;
        _database = database;
        _gmrClient = gmrClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            //Run polling every 30 seconds
            foreach (var game in _gameContainer.Games.Where(gameData => gameData.Chats.Count> 0 && !gameData.IsOver )) {
                await PollTurn(game, stoppingToken);
            }
            await Task.Delay(30000, stoppingToken);
        }
    }
    
    private async Task PollTurn(GameData game, CancellationToken ct) {
        try {
            var gameData = await _gmrClient.GetGameData(game.GameId, game.Owner);
            
            if (gameData == null) {
                _logger.LogError("Game {GameGameId} data null", game.GameId);
                return;
            }

            var oldPlayerId = game.CurrentPlayer.SteamId;

            var currentPlayerId = gameData.CurrentTurn.UserId.ToString();
            game.TurnStarted = gameData.CurrentTurn.Started;

            if (oldPlayerId != currentPlayerId) {
                await ChangeTurns(game, currentPlayerId, gameData.CurrentTurn, ct);
            }
            else {
                await CheckTurnNotifications(game, ct);
            }
        }
        catch (Exception ex) {
            _logger.LogError("Polling failed with exception: {Exception}", ex);
        }
    }

    private async Task CheckTurnNotifications(GameData game, CancellationToken ct) {
        if (game.CurrentPlayer.NextEta == DateTime.MinValue) {
            if (game.TurntimerNotified) {
                await DailyNotify(game, ct);
                return;
            }
            var turnTimer = await _gmrClient.GetTurntimer(game, game.CurrentPlayer);
            if (turnTimer == null) return;
            if (game.TurnStarted + turnTimer >= DateTime.UtcNow) return;
            game.TurntimerNotified = true;
            game.UpdateCurrent(_database);
            foreach (var chat in game.Chats) {
                await _botClient.SendTextMessageAsync(chat, $"Turn timer kärsii {game.CurrentPlayer.NameTag}", cancellationToken: ct);
            }
        }
        else {
            if (game.CurrentPlayer.NextEta >= DateTime.Now) {
                return;
            }

            game.CurrentPlayer.NextEta = DateTime.MinValue;
            game.CurrentPlayer.UpdateDatabase(_database);
            foreach (var chat in game.Chats) {
                await _botClient.SendTextMessageAsync(chat, $"Aikamääreistä pidetään kiinni {game.CurrentPlayer.NameTag}", cancellationToken: ct);
            }
        }
    }

    private async Task ChangeTurns(GameData game, string currentPlayerId, CurrentTurn current, CancellationToken ct) {
        var player = game.Players.Find(playerData => playerData.SteamId == currentPlayerId);
        
        if (player == null) {
            throw new ArgumentNullException($"Player {currentPlayerId} not found in database!");
        }
        
        var oldPlayer = game.CurrentPlayer;
        
        oldPlayer.NextEta = DateTime.MinValue;
        oldPlayer.UpdateDatabase(_database);
        
        game.CurrentPlayer = player;
        game.TurntimerNotified = false;
        game.TurnStarted = DateTime.Now;
        game.CurrentPlayer.NextEta = DateTime.MinValue;
        game.TurnId = current.TurnId.ToString();
        game.CurrentPlayer.UpdateDatabase(_database);
        game.UpdateCurrent(_database);

        player.SteamName = await _steamClient.GetSteamUserName(player.SteamId);

        player.User = UserData.GetBySteamId(_database, player.SteamId);

        if (player.User != null) {
            var tgUser = await _botClient.GetChatAsync(player.User.Id, cancellationToken: ct);
                
            if (tgUser.Username == null) {
                _logger.LogWarning("User {UserId} has no username", player.User.Id);
            }
            else {
                player.TgName = tgUser.Username;
            }
        }

        foreach (var chat in game.Chats) {
            await _botClient.SendTextMessageAsync(chat, $"It's now your turn {game.CurrentPlayer.NameTag}!", cancellationToken: ct);
        }
    }

    private async Task DailyNotify(GameData game, CancellationToken ct) {
        if (!game.EnableDailyNotified) {
            return;
        }
        
        string message;
        var rnd = new Random();
        switch (DateTime.UtcNow.Hour) {
            case 7: {
                message = rnd.Next(0, 8) switch {
                    0 => $"Uusi päivä, uusi vuoro {game.CurrentPlayer.NameTag}",
                    1 => $"Linnut laulaa ja vuorot tehää {game.CurrentPlayer.NameTag}",
                    2 => $"Kahvit ja vuorot tulille {game.CurrentPlayer.NameTag}",
                    3 => $"Ylös ulos ja civille {game.CurrentPlayer.NameTag}",
                    4 => $"Welcome back commander {game.CurrentPlayer.NameTag}",
                    5 => $"Help us {game.CurrentPlayer.NameTag}, your our only hope",
                    6 => $"Nukuitko hyvin hyvin {game.CurrentPlayer.NameTag}?",
                    7 => $"Aikanen vuoro kaupungin nappaa {game.CurrentPlayer.NameTag}",
                    _ => $"Civivuorossa herätyys {game.CurrentPlayer.NameTag}!"
                };

                break;
            }
            case 17: {
                message = rnd.Next(0, 8) switch {
                    0 => $"Muista pestä hampaat ja tehdä vuoro {game.CurrentPlayer.NameTag}",
                    1 => $"Älä unohda vuoroasi {game.CurrentPlayer.NameTag}",
                    2 => $"Just one more turn {game.CurrentPlayer.NameTag}",
                    3 => $"All your turn are belong to {game.CurrentPlayer.NameTag}",
                    4 => $"It looks like you were trying to sleep {game.CurrentPlayer.NameTag}",
                    5 => $"Tee vuoro ja nukkumaan {game.CurrentPlayer.NameTag}. Muuta neuvoa ei tule",
                    6 => $"Aina voi laittaa lomatilan päälle {game.CurrentPlayer.NameTag}",
                    7 => $"Älä anna yöunien pilataa civiä {game.CurrentPlayer.NameTag}",
                    _ => $"Etkai vai ollut menossa nukkumaan {game.CurrentPlayer.NameTag}?"
                };

                break;
            }
            default:
                if (game.DailyNotified) {
                    game.DailyNotified = false;
                    game.UpdateCurrent(_database);
                }
                return;
        }

        if (game.DailyNotified) {
            return;
        }
        game.DailyNotified = true;
        game.UpdateCurrent(_database);
        foreach (var chat in game.Chats) {
            await _botClient.SendTextMessageAsync(chat, message, cancellationToken: ct);
        }
    }
}