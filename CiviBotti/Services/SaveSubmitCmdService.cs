﻿namespace CiviBotti.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataModels;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

public class SaveSubmitCmdService(
    ITelegramBotClient botClient,
    IGameContainerService gameContainer,
    IGmrClient gmrClient,
    IDatabase database)
{
    public async Task HandleCallback(CallbackQuery callbackQuery, string[] dataPoints, CancellationToken cancellationToken) {
        if (dataPoints[0] != "savesubmit") {
            throw new ArgumentException("Invalid callback data");
        }
        
        if (callbackQuery.Message == null) return;

        var action = dataPoints[1] switch
        {
            "submitturn" => OnSubmitTurnGetSelectedCallback(callbackQuery.Message, dataPoints, cancellationToken),
            "doturn" => OnDoTurnGetSelectedCallback(callbackQuery.Message, dataPoints, cancellationToken),
            
            _ => Task.CompletedTask
        };
        
        await action;
    }
        
        
    private async Task OnSubmitTurnGetSelectedCallback(Message message, IReadOnlyList<string> dataPoints, CancellationToken cancellationToken) {
        if (!long.TryParse(dataPoints[2], out var gameId)) return;
        if (!long.TryParse(dataPoints[3], out var selectedUserId)) return;
        var user = UserData.Get(database, message.From!.Id);
        
        if (user == null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You are not registered in any games", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        var selectedGame = gameContainer.Games.FirstOrDefault(game => game.GameId == gameId);
        
        if (selectedGame == null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Game not found", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        var selectedPlayer = selectedGame.Players.Find(player => player.User!.Id == selectedUserId);

        if (selectedPlayer?.User == null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Player not found", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        

        if (!selectedPlayer.User.Subs.Exists(sub => sub.SubId == user.Id)) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You are not allowed to submit this player's turn", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        await botClient.SendTextMessageAsync(message.Chat.Id, $"Upload the file\nGame: {gameId}\n", replyMarkup: new ForceReplyMarkup(), cancellationToken: cancellationToken);
    }

    public async Task HandlePotentialFileUpload(Message message, CancellationToken cancellationToken) {
        if (message.ReplyToMessage == null) {
            return;
        }
        
        if (message.ReplyToMessage.Text == null || !message.ReplyToMessage.Text.Contains("Upload the file")) {
            return;
        }
        

        if (!long.TryParse(message.ReplyToMessage.Text.Split("\n")[^1].Split(":")[^1], out var gameId)) {
            return;
        }
        
        var user = UserData.Get(database, message.From!.Id);
        
        if (user == null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You are not registered in any games", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        var game = gameContainer.Games.FirstOrDefault(game => game.GameId == gameId);
        
        if (game ==  null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Game not found", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }

        if (game.CurrentPlayer.User == null) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "No current player", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }

        if (game.CurrentPlayer.SteamId != user.SteamId || !game.CurrentPlayer.User.Subs.Exists(sub => sub.SubId == user.Id)) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You are not allowed to submit this player's turn", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }
        
        
        
        if (message.Document == null || message.Type != MessageType.Document) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Respond by uploading a file", cancellationToken: cancellationToken);
            await botClient.SendTextMessageAsync(message.Chat.Id, $"Upload the file\nGame: {gameId}\n", replyMarkup: new ForceReplyMarkup(), cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendTextMessageAsync(message.Chat.Id, "Submitting turn", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
        await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.UploadDocument, cancellationToken: cancellationToken);
        await UploadSave(game, user, game.CurrentPlayer.User, message.Document);
    }
    
    public async Task SubmitTurn(Message message, Chat chat, CancellationToken ct) {
        if (chat.Type != ChatType.Private) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!", cancellationToken: ct);
            return;
        }

        await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing, cancellationToken: ct);

        var user = UserData.Get(database, message.From!.Id);

        if (user == null) {
            await botClient.SendTextMessageAsync(chat, "You are not registered in any games", cancellationToken: ct);
            return;
        }

        var keyboardButtons = new List<InlineKeyboardButton>();
        foreach (var game in gameContainer.Games) {
            var otherUser = game.CurrentPlayer.User;
            if (otherUser == null) {
                continue;
            }

            if (!otherUser.Subs.Exists(sub => sub.SubId == user.Id) && otherUser.SteamId != user.SteamId) {
                continue;
            }

            var otherTgUser = await botClient.GetChatAsync(otherUser.Id, cancellationToken: ct);

            keyboardButtons.Add(InlineKeyboardButton.WithCallbackData($"{otherTgUser.Username}@{game.Name}", $"savesubmit:submitturn:{game.GameId}:{otherUser.Id}"));
        }

        if (keyboardButtons.Count == 0) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You can not submit anyone's turn at the moment", cancellationToken: ct);
            return;
        }

        keyboardButtons.Add(InlineKeyboardButton.WithCallbackData("Cancel", "cancel"));


        await botClient.SendTextMessageAsync(message.Chat.Id, "Chose game to submit save to", replyMarkup:new InlineKeyboardMarkup(keyboardButtons), cancellationToken: ct);
    }


    private async Task OnDoTurnGetSelectedCallback(Message originalMsg, string[] dataPoints, CancellationToken ct) {
        if (!long.TryParse(dataPoints[2], out var gameId)) return;
        
        var user = UserData.Get(database, originalMsg.From!.Id);
        if (user == null) {
            await botClient.SendTextMessageAsync(originalMsg.Chat.Id, "You are not registered in any games", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
            return;
        }
        
        var selectedGame = gameContainer.Games.FirstOrDefault(game => game.GameId == gameId);
        if (selectedGame?.CurrentPlayer.User == null) {
            await botClient.SendTextMessageAsync(originalMsg.Chat.Id, "Game not found", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
            return;
        }
        
        if (user.SteamId != selectedGame.CurrentPlayer.SteamId && !selectedGame.CurrentPlayer.User.Subs.Exists(sub => sub.SubId == user.Id)) {
            await botClient.SendTextMessageAsync(originalMsg.Chat.Id, "You are not allowed to download this player's turn", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
            return;
        }

        await botClient.SendChatActionAsync(originalMsg.Chat.Id, ChatAction.UploadDocument, cancellationToken: ct);
        await DownloadSave(user, selectedGame.CurrentPlayer.User, selectedGame, ct);
    }
    
    public async Task DoTurn(Message message, Chat chat, CancellationToken ct) {
        await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing, cancellationToken: ct);

        if (chat.Type != ChatType.Private) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!", cancellationToken: ct);
            return;
        }

        var callerUser = UserData.Get(database, message.From!.Id);

        if (callerUser == null) {
            await botClient.SendTextMessageAsync(chat, "You are not registered in any games", replyMarkup:new ReplyKeyboardRemove(), cancellationToken: ct);
            return;
        }
        

        var keyboardButtons = new List<InlineKeyboardButton>();
        foreach (var game in gameContainer.Games) {
            var user = game.CurrentPlayer.User;
            if (user == null) {
                continue;
            }

            if (!user.Subs.Exists(sub => sub.SubId == callerUser.Id) && user.SteamId != callerUser.SteamId) {
                continue;
            }

            var subUser = await botClient.GetChatAsync(user.Id, cancellationToken: ct);
            keyboardButtons.Add(InlineKeyboardButton.WithCallbackData($"{subUser.Username}@{game.Name}", $"savesubmit:doturn:{game.GameId}"));

        }

        if (keyboardButtons.Count == 0) {
            await botClient.SendTextMessageAsync(message.Chat.Id, "You can not play anyone's turn at the moment", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
            return;
        }

        keyboardButtons.Add(InlineKeyboardButton.WithCallbackData("Cancel", "cancel"));


        await botClient.SendTextMessageAsync(message.Chat.Id, "Chose game to sub", replyMarkup: new InlineKeyboardMarkup(keyboardButtons.ToArray()), cancellationToken: ct);
    }

    private async Task DownloadSave(UserData user, UserData selectedUser, GameData game, CancellationToken ct) {
        var stream = await gmrClient.DownloadSave(game, selectedUser);

        if (stream == null) {
            await botClient.SendTextMessageAsync(user.Id, "Failed to download save", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
            return;
        }
        
        var selectedUsername = (await botClient.GetChatAsync(selectedUser.Id, cancellationToken: ct)).Username;
        var username = (await botClient.GetChatAsync(user.Id, cancellationToken: ct)).Username;
        var file = InputFile.FromStream(stream, $"(GMR) {selectedUsername} {game.Name}.Civ5Save");
        await botClient.SendDocumentAsync(user.Id, file, cancellationToken: ct);
        await botClient.SendTextMessageAsync(user.Id, "Use /submitturn command to submit turn",
            replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
        await botClient.SendTextMessageAsync(user.Id, $"{username} downloaded your turn", cancellationToken: ct);
    }

    private async Task UploadSave(GameData game, UserData user, UserData currentPlayerUser, FileBase doc) {
        var fileInfo = await botClient.GetFileAsync(doc.FileId);

        using var ms = new MemoryStream();
        await botClient.GetInfoAndDownloadFileAsync(fileInfo.FileId, ms);
        var response = await gmrClient.UploadSave(game, currentPlayerUser, ms);


        if (!response.IsSuccessStatusCode) {
            return;
        }

        var userName = (await botClient.GetChatAsync(user.Id)).Username;
        var result = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(result);
        var resultType = json["ResultType"]?.ToString();
        if (resultType == "1") {
            await botClient.SendTextMessageAsync(user.Id, "Turn submitted");
            await botClient.SendTextMessageAsync(currentPlayerUser.Id, $"{userName} submitted your turn");
        }
        else {
            await botClient.SendTextMessageAsync(user.Id, $"Failed to submit turn {json["ResultType"]}");
        }
    }
}