using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CiviBotti.DataModels;
using CiviBotti.Exceptions;

using GmrData.Gmr;

using Microsoft.Extensions.Logging;

using Telegram.Bot;

namespace CiviBotti.Services;

public class PollingTask(
    ISteamApiClient steamClient,
    ITelegramBotClient botClient,
    IGameContainerService gameContainer,
    IDatabase database,
    IGmrClient gmrClient,
    ILogger<GamePollingService> logger)
{

    public async Task PollGames(CancellationToken ct)
    {
        foreach (var game in gameContainer.Games.Where(gameData => gameData.Chats.Count > 0 && !gameData.IsOver))
        {
            await PollTurn(game, ct);
        }
    }

    private async Task PollTurn(GameData game, CancellationToken ct) {
        try {
            var gameData = await gmrClient.GetGameData(game.GameId, game.Owner.SteamId, game.Owner.AuthKey);
            
            if (gameData == null) {
                logger.LogError("Game {GameGameId} data null", game.GameId);
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
        catch (MissingOwnerException exception)
        {
            logger.LogWarning("Owner {OwnerSteamId} not found in game {GameGameId}", game.Owner.SteamId, game.GameId);
            await botClient.SendTextMessageAsync(76746796, "Owner not found in game", cancellationToken: ct);
            await botClient.SendTextMessageAsync(76746796, exception.Message, cancellationToken: ct);
            //await ChangeGameOwner(game, ct); disabled for now because of potential risks
        }
        catch (Exception ex) {
            logger.LogError("Polling failed with exception: {Exception}", ex);
        }
    }

    private async Task ChangeGameOwner(GameData game, CancellationToken ct) {
        PlayerData? nextOwner = null;
        foreach (var player in game.Players)
        {
            if (player.User == null || player.SteamId == game.Owner.SteamId)
            {
                continue;
            }

            PackagedGame? gameData;
            try
            {
                gameData = await gmrClient.GetGameData(game.GameId, player.User.SteamId, player.User.AuthKey);
            }
            catch (MissingOwnerException)
            {
                continue;
            }
            if (gameData == null)
            {
                continue;
            }
            nextOwner = player;
            break;
        }
        
        if (nextOwner == null)
        {
            
            foreach (var chat in game.Chats.ToList())
            {
                game.RemoveChat(database, chat);
                await botClient.SendTextMessageAsync(chat, $"All players have left the game, removing the game from this chat", cancellationToken: ct);
            }
            logger.LogError("No potential owner found for game {GameGameId}", game.GameId);
            return;
        }

        game.UpdateOwner(database, nextOwner.User!);
        foreach (var chat in game.Chats)
        {
            await botClient.SendTextMessageAsync(chat, $"Owner of the game has left the game,  {nextOwner.NameTag} is the new owner", cancellationToken: ct);
        }

    }

    private async Task CheckTurnNotifications(GameData game, CancellationToken ct) {
        if (game.CurrentPlayer.NextEta == DateTime.MinValue) {
            if (game.TurntimerNotified) {
                await DailyNotify(game, ct);
                return;
            }
            var turnTimer = await gmrClient.GetTurntimer(game, game.CurrentPlayer);
            if (turnTimer == null) return;
            if (game.TurnStarted + turnTimer >= DateTime.UtcNow) return;
            game.TurntimerNotified = true;
            game.UpdateCurrent(database);
            foreach (var chat in game.Chats) {
                await botClient.SendTextMessageAsync(chat, $"Turn timer kärsii {game.CurrentPlayer.NameTag}", cancellationToken: ct);
            }
        }
        else {
            if (game.CurrentPlayer.NextEta >= DateTime.Now) {
                return;
            }

            game.CurrentPlayer.NextEta = DateTime.MinValue;
            game.CurrentPlayer.UpdateDatabase(database);
            foreach (var chat in game.Chats) {
                await botClient.SendTextMessageAsync(chat, $"Aikamääreistä pidetään kiinni {game.CurrentPlayer.NameTag}", cancellationToken: ct);
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
        oldPlayer.UpdateDatabase(database);
        
        game.CurrentPlayer = player;
        game.TurntimerNotified = false;
        game.TurnStarted = DateTime.Now;
        game.CurrentPlayer.NextEta = DateTime.MinValue;
        game.TurnId = current.TurnId.ToString();
        game.CurrentPlayer.UpdateDatabase(database);
        game.UpdateCurrent(database);

        player.SteamName = await steamClient.GetSteamUserName(player.SteamId);

        player.User = UserData.GetBySteamId(database, player.SteamId);

        if (player.User != null) {
            var tgUser = await botClient.GetChatAsync(player.User.Id, cancellationToken: ct);
                
            if (tgUser.Username == null) {
                logger.LogWarning("User {UserId} has no username", player.User.Id);
            }
            else {
                player.TgName = tgUser.Username;
            }
        }

        foreach (var chat in game.Chats) {
            await botClient.SendTextMessageAsync(chat, $"It's now your turn {game.CurrentPlayer.NameTag}!", cancellationToken: ct);
        }
    }

    private async Task DailyNotify(GameData game, CancellationToken ct) {
        if (!game.EnableDailyNotified) {
            return;
        }
        
        string message;
#pragma warning disable S2245
        var rnd = new Random();
#pragma warning restore S2245
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
                    game.UpdateCurrent(database);
                }
                return;
        }

        if (game.DailyNotified) {
            return;
        }
        game.DailyNotified = true;
        game.UpdateCurrent(database);
        foreach (var chat in game.Chats) {
            await botClient.SendTextMessageAsync(chat, message, cancellationToken: ct);
        }
    }
}