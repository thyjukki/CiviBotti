namespace CiviBotti.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataModels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

public class SubsCmdService
{
    private readonly ITelegramBotClient _botClient;
    private readonly GameContainerService _gameContainer;
    private readonly Database _database;

    public SubsCmdService(ITelegramBotClient botClient, GameContainerService gameContainer, Database database) {
        _botClient = botClient;
        _gameContainer = gameContainer;
        _database = database;
    }

    public async Task HandleCallback(CallbackQuery callbackQuery, string[] dataPoints, CancellationToken cancellationToken) {
        if (dataPoints[0] != "subs") {
            throw new ArgumentException("Invalid callback data");
        }
        
        if (callbackQuery.Message == null) return;

        
        
        if (callbackQuery.Message.From is null)
            return;
        
        var action = dataPoints[1] switch
        {
            "add" => AddSubGameSelected(callbackQuery.Message, dataPoints, cancellationToken),
            "adduser" => AddSubUserSelected(callbackQuery.Message, dataPoints, cancellationToken),
            "remove" => RemoveSubUserSelected(callbackQuery.Message, dataPoints, cancellationToken),
            
            _ => Task.CompletedTask
        };
        
        await action;
    }

    public async Task ListSubs(Message message, Chat chat, CancellationToken cancellationToken) {
        await _botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);
        if (chat.Type != ChatType.Private) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Subs should be done in private chat", cancellationToken: cancellationToken);
            return;
        }

        var callerUser = UserData.Get(_database, message.From!.Id);

        if (callerUser == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "You need to be registered to use this '/register authKey'!", cancellationToken: cancellationToken);
            return;
        }

        var stringBuilder = new StringBuilder();
        foreach (var game in _gameContainer.Games.Where(game => game.Players.Exists(userData => userData.User?.SteamId == callerUser.SteamId))) {
            stringBuilder.Append($"{game.Name}:\n");

            if (callerUser.Subs == null || callerUser.Subs.Count == 0 ||
                !callerUser.Subs.Exists(sub => sub.GameId == game.GameId)) {
                stringBuilder.Append(" none\n");
                continue;
            }

            foreach (var sub in callerUser.Subs.FindAll(subData => subData.GameId == game.GameId)) {
                var subUser = await _botClient.GetChatAsync(sub.SubId, cancellationToken: cancellationToken);
                stringBuilder.Append($" -{subUser.Username} for {(sub.Times == 0 ? "unlimited" : sub.Times.ToString())} times\n");
            }
        }

        if (stringBuilder.Length == 0) {
            stringBuilder.Append("You are not in any games");
        }

        await _botClient.SendTextMessageAsync(message.Chat.Id, stringBuilder.ToString(), cancellationToken: cancellationToken);
    }

    public async Task AddSub(Message message, Chat chat, CancellationToken cancellationToken) {
        if (chat.Type != ChatType.Private) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!", cancellationToken: cancellationToken);
            return;
        }

        await _botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);

        var callerUser = UserData.Get(_database, message.From!.Id);

        if (callerUser == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "You need to be registered to use this '/register authKey'!", cancellationToken: cancellationToken);
            return;
        }

        var games = _gameContainer.Games.Where(game => game.Players.Exists(player => player.User != null && player.User.SteamId == callerUser.SteamId)).ToList();
        var keyboardsMarkups = games.Select(game => new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData($"{game.Name}", $"subs:add:{game.GameId}") }).ToList();

        await _botClient.SendTextMessageAsync(message.Chat.Id, "Chose the game", replyMarkup: new InlineKeyboardMarkup(keyboardsMarkups), cancellationToken: cancellationToken);
    }
    
    public async Task RemoveSub(Message message, Chat chat, CancellationToken cancellationToken) {
        if (chat.Type != ChatType.Private) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!", cancellationToken: cancellationToken);
            return;
        }

        await _botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);

        var callerUser = UserData.Get(_database, message.From!.Id);

        if (callerUser == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "You need to be registered to use this '/register authKey'!", cancellationToken: cancellationToken);
            return;
        }

        
        var keyboardsMarkups = new List<InlineKeyboardButton>();
        foreach (var sub in callerUser.Subs) {
            var subUser = await _botClient.GetChatAsync(sub.SubId, cancellationToken: cancellationToken);
            var game = _gameContainer.Games.FirstOrDefault(game => game.GameId == sub.GameId);
            if (game == null) continue;
            keyboardsMarkups.Add(InlineKeyboardButton.WithCallbackData($"{subUser.Username}@{game.Name}", $"subs:remove:{sub.SubId}:{game.GameId}"));
        }

        keyboardsMarkups.Add(InlineKeyboardButton.WithCallbackData("Cancel", "cancel"));

        await _botClient.SendTextMessageAsync(message.Chat.Id, "Chose sub to remove", replyMarkup: new InlineKeyboardMarkup(keyboardsMarkups), cancellationToken: cancellationToken);
    }
    
    
    private async Task RemoveSubUserSelected(Message message, IReadOnlyList<string> dataPoints, CancellationToken cancellationToken) {
        if (!long.TryParse(dataPoints[2], out var subId)) return;
        if (!long.TryParse(dataPoints[3], out var gameId)) return;
        
        
        var user = UserData.Get(_database, message.From!.Id);
        
        if (user == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Unknown user", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        

        var sub = user.Subs.FirstOrDefault(sub => sub.SubId == subId && sub.GameId == gameId);
        
        if (sub == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Unknown sub", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        var game = _gameContainer.Games.FirstOrDefault(game => game.GameId == gameId);
        
        if (game == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Unknown game", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        var userName = (await _botClient.GetChatAsync(user.Id, cancellationToken: cancellationToken)).Username;
        var selectedUser = await _botClient.GetChatAsync(user.Id, cancellationToken: cancellationToken);
        var selectedUserName = selectedUser.Username;
        
        user.Subs.Remove(sub);
        sub.RemoveSub(_database);

        await _botClient.SendTextMessageAsync(message.Chat, $"Removed {selectedUserName} subbing from {game.Name}", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
        await _botClient.SendTextMessageAsync(selectedUser, $"{userName} revoked your sub rights from {game.Name}", cancellationToken: cancellationToken);
    }
    

    private async Task AddSubGameSelected(Message message, IReadOnlyList<string> dataPoints, CancellationToken cancellationToken) {
        
        if (!long.TryParse(dataPoints[2], out var gameId)) return;
        var selectedGame = _gameContainer.Games.FirstOrDefault(game => game.GameId == gameId);

        if (selectedGame == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Unknown game", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }

        var users = selectedGame.Players.Where(player => player.User != null);
        var keyboardsMarkups = users.Select(player => new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(player.Name, $"subs:adduser:{selectedGame.GameId}:{player.User!.Id}") }).ToList();
        
        await _botClient.SendTextMessageAsync(message.Chat.Id, "Chose the user", replyMarkup: new InlineKeyboardMarkup(keyboardsMarkups), cancellationToken: cancellationToken);
    }
    
    private async Task AddSubUserSelected(Message message, IReadOnlyList<string> dataPoints, CancellationToken cancellationToken) {
        
        if (!long.TryParse(dataPoints[2], out var gameId)) return;
        if (!long.TryParse(dataPoints[3], out var userId)) return;
        var selectedGame = _gameContainer.Games.FirstOrDefault(game => game.GameId == gameId);

        if (selectedGame == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Unknown game", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }

        var selectedUser = selectedGame.Players.FirstOrDefault(playerData => playerData.User != null && playerData.User.Id == userId)?.User;

        if (selectedUser == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Unknown user", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        var user = UserData.Get(_database, message.From!.Id);
        if (user == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Wat?", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        
        var sub = new SubData(user.Id, selectedUser.Id, 0, selectedGame.GameId);
        sub.InsertDatabase(_database);
        user.Subs.Add(sub);
            
        var selectedUserName = (await _botClient.GetChatAsync(selectedUser.Id, cancellationToken: cancellationToken)).Username;
        var userName = (await _botClient.GetChatAsync(user.Id, cancellationToken: cancellationToken)).Username;
            
        await _botClient.SendTextMessageAsync(message.Chat.Id, $"Added {selectedUserName} as sub in {selectedGame.Name}", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
        await _botClient.SendTextMessageAsync(user.Id, $"{userName} has given you rights to do his turn in {selectedGame.Name}", cancellationToken: cancellationToken);
    }
}