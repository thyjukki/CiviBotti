using GmrData.Gmr;

namespace CiviBotti.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DataModels;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class GameAdminCmdService(
    ITelegramBotClient botClient,
    IGameContainerService gameContainer,
    IDatabase database,
    ISteamApiClient steamApiClient,
    ILogger<GameAdminCmdService> logger,
    IGmrClient gmrClient)
{
    public async Task NewGame(Message message, Chat chat, CancellationToken ct) {
        await botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);
        if (chat.Type != ChatType.Private) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "New game can only be created in private chat!", cancellationToken: ct);
            return;
        }

        
        var owner = UserData.Get(database, message.From!.Id);
        if (owner == null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You are need to first register!", cancellationToken: ct);
            return;
        }

        var args = message.Text!.Split(' ');
        if (args.Length != 2) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Please provide gameid '/newgame gameid'!", cancellationToken: ct);
            return;
        }

        if (!long.TryParse(args[1], out var gameId) || gameId == 0) {
            return;
        }


        if (gameContainer.Games.Any(game => game.GameId == gameId)) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Game has already been created!", cancellationToken: ct);
            return;
        }

        
        PackagedGame? data;
        try {
            data = await gmrClient.GetGameData(gameId, owner.SteamId, owner.AuthKey);
        }
        catch (WebException) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Could not connect to services, please try again later!", cancellationToken: ct);
            return;
        }

        if (data == null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Invalid gameid, or your account is not in the game!", cancellationToken: ct);
            return;
        }

        var players = new List<PlayerData>();
        var currentPlayerId = data.CurrentTurn.UserId.ToString();
        PlayerData? currentPlayer = null;
        foreach (var playerRawData in data.Players) {
            var playerData = await CreatePlayerData(playerRawData, gameId, ct);

            if (currentPlayerId == playerData.SteamId) {
                currentPlayer = playerData;
            }
            players.Add(playerData);
        }

        
        var newGame = new GameData(gameId, owner, data.Name,
            currentPlayer ?? players[0], true, false, false);
        newGame.Players.Clear();
        newGame.Players.AddRange(players);
        newGame.TurnStarted = data.CurrentTurn.Started;
        newGame.InsertFull(database);
        gameContainer.Add(newGame);
        
        logger.LogInformation("Game {GameId} created by {UserId}", newGame.GameId, message.From.Id);
        await botClient.SendTextMessageAsync(message.Chat.Id, $"Successfully created the game {newGame.Name}!", cancellationToken: ct);
    }

    private async Task<PlayerData> CreatePlayerData(PackagedUser playerRawData, long gameId, CancellationToken ct) {
        var playerData = new PlayerData(gameId, playerRawData.UserId.ToString(), playerRawData.TurnOrder, DateTime.MinValue);

        playerData.User = UserData.GetBySteamId(database, playerData.SteamId);
        playerData.SteamName = await steamApiClient.GetSteamUserName(playerData.SteamId);

        if (playerData.User == null) {
            return playerData;
        }

        var user = await botClient.GetChatAsync(playerData.User.Id, cancellationToken: ct);
        if (user.Username != null) {
            playerData.TgName = user.Username;
        }

        return playerData;
    }

    public async Task RegisterGame(Message message, Chat chat, CancellationToken ct) {
        await botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);
        if (chat.Type != ChatType.Private) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Registering can only be created in private chat!", cancellationToken: ct);
            return;
        }

        var args = message.Text!.Split(' ');
        if (args.Length != 2) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Please provide authKey '/register authKey'!", cancellationToken: ct);
            return;
        }

        if (UserData.CheckDatabase(database, message.From!.Id)) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You are already registered!", cancellationToken: ct);
            return;
        }

        var steamId = await gmrClient.GetPlayerIdFromAuthKey(args[1]);
        if (steamId == "null") {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Authorization key you provided was incorrect!", cancellationToken: ct);
            return;
        }


        var newUser = new UserData(message.From.Id, steamId, args[1]);
        newUser.InsertDatabase(database);
        foreach (var game in gameContainer.Games) {
            foreach (var player in game.Players.Where(player => player.SteamId == steamId)) {
                player.User = newUser;
            }
        }

        await botClient.SendTextMessageAsync(message.Chat.Id, "Registered with steamid " + steamId, cancellationToken: ct);
    }
    
        public async Task AddGame(Message message, Chat chat, CancellationToken ct) {
            await botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);

            if (chat.Type != ChatType.Private) {
                var admins = await botClient.GetChatAdministratorsAsync(chat.Id, cancellationToken: ct);
                
                if (!admins.Select(admin => admin.User.Id).Contains(message.From!.Id)) {
                    await botClient.SendTextMessageAsync(message.Chat.Id, "Only group admin can do this!", cancellationToken: ct);
                    return;
                }
            }

            var args = message.Text!.Split(' ');
            if (args.Length != 2) {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Please provide game id '/addgame gameid'!", cancellationToken: ct);
                return;
            }

            long gameId;
            try {
                gameId = long.Parse(args[1]);
            }
            catch {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Invalid gameid!", cancellationToken: ct);
                return;
            }

            var selectedGame = gameContainer.Games.FirstOrDefault(game => game.GameId == gameId);
            
            if (selectedGame == null) {
                await botClient.SendTextMessageAsync(message.Chat.Id,
                    "Could not find a game with given id, you must create one with '/newgame gameid'", cancellationToken: ct);
                return;
            }
            
            if (selectedGame.Chats.Exists(chatId => chatId == chat.Id)) {
                await botClient.SendTextMessageAsync(message.Chat, "Channel already has a game!", cancellationToken: ct);
                return;
            }


            selectedGame.Chats.Add(chat.Id);

            selectedGame.InsertChat(database, chat.Id);


            await botClient.SendTextMessageAsync(message.Chat.Id,
                $"Added game {selectedGame.Name} to this channel! You will now receive turn notifications.", cancellationToken: ct);
        }

        public async Task RemoveGame(Message message, Chat chat, CancellationToken ct) {
            await botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);

            if (chat.Type != ChatType.Private) {
                var admins = await botClient.GetChatAdministratorsAsync(chat.Id, cancellationToken: ct);
                
                if (!admins.Select(admin => admin.User.Id).Contains(message.From!.Id)) {
                    await botClient.SendTextMessageAsync(message.Chat.Id, "Only group admin can do this!", cancellationToken: ct);
                    return;
                }
            }

            var selectedGame = gameContainer.Games.FirstOrDefault(game => game.Chats.Exists(chatid => chatid == chat.Id));

            if (selectedGame == null) {
                await botClient.SendTextMessageAsync(message.Chat.Id, "No game added to this group!", cancellationToken: ct);
                return;
            }

            selectedGame.RemoveChat(database, chat.Id);


            await botClient.SendTextMessageAsync(message.Chat.Id,
                $"Removed game {selectedGame.Name} from this channel! You will not receive any more notifications.", cancellationToken: ct);
        }
}