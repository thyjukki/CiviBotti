namespace CiviBotti.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

public class UpdateHandler(
    ILogger<UpdateHandler> logger,
    GameAdminCmdService gameAdminCmdService,
    UtilCmdService utilCmdService,
    TurntimeCmdServices turntimeCmdServices,
    SubsCmdService subsCmdService,
    SaveSubmitCmdService saveSubmitCmdService)
    : IUpdateHandler
{
    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var handler = update switch
        {
            // UpdateType.Unknown:
            // UpdateType.ChannelPost:
            // UpdateType.EditedChannelPost:
            // UpdateType.ShippingQuery:
            // UpdateType.PreCheckoutQuery:
            // UpdateType.Poll:
            { Message: { } message } => BotOnMessageReceived(botClient, message, cancellationToken),
            { EditedMessage: { } message } => BotOnMessageReceived(botClient, message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(botClient, callbackQuery, cancellationToken),
            _ => UnknownUpdateHandlerAsync(update)
        };

        await handler;
    }


    private async Task BotOnMessageReceived(ITelegramBotClient botClient, Message message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Receive message type: {MessageType}", message.Type);

        if (message.ReplyToMessage is not null) {
            await saveSubmitCmdService.HandlePotentialFileUpload(message, cancellationToken);
            return;
        }
        
        if (message.Text is null)
            return;
        if (message.From is null)
            return;
        if (message.Chat is not { } chat)
            return;

        var action = message.Text.Split('@')[0].Split(' ')[0] switch
        {
            "/newgame" => gameAdminCmdService.NewGame(message, chat, cancellationToken),
            "/register" => gameAdminCmdService.RegisterGame(message, chat, cancellationToken),
            "/removegame" => gameAdminCmdService.RemoveGame(message, chat, cancellationToken),
            "/addgame" => gameAdminCmdService.AddGame(message, chat, cancellationToken),
            
            "/order" => utilCmdService.Order(message, chat, cancellationToken),
            "/next" => utilCmdService.Next(message, chat, cancellationToken),
            "/help" => utilCmdService.Help(message, chat, cancellationToken),
            
            "/autocracy" => botClient.SendTextMessageAsync(message.Chat.Id, "Did you mean /order?", cancellationToken: cancellationToken),
            "/oispa" => botClient.SendTextMessageAsync(message.Chat.Id, "Kaljaa?", cancellationToken: cancellationToken),
            "/teekari" => botClient.SendTextMessageAsync(message.Chat.Id, "Kaljaa?", cancellationToken: cancellationToken),
            
            "/tee" => turntimeCmdServices.Tee(message, chat, cancellationToken),
            "/eta" => turntimeCmdServices.Eta(message, chat, cancellationToken),
            "/turntimers" => turntimeCmdServices.Turntimers(chat, false, cancellationToken),
            "/turntimer" => turntimeCmdServices.Turntimers(chat, true, cancellationToken),
            
            "/listsubs" => subsCmdService.ListSubs(message, chat, cancellationToken),
            "/addsub" => subsCmdService.AddSub(message, chat, cancellationToken),
            "/removesub" => subsCmdService.RemoveSub(message, chat, cancellationToken),
            
            "/doturn" => saveSubmitCmdService.DoTurn(message, chat, cancellationToken),
            "/submitturn" => saveSubmitCmdService.SubmitTurn(message, chat, cancellationToken),
            
            //"/doturn" => _subsCmdService.ListSubs(chat, true, cancellationToken),
            //"/Submitturn" => _subsCmdService.ListSubs(chat, true, cancellationToken),
            
            _ => Task.CompletedTask
        };

        await action;
    }

    // Process Inline Keyboard callback data
    private async Task BotOnCallbackQueryReceived(ITelegramBotClient botClient, CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);

        if (callbackQuery.Message == null || callbackQuery.Data == null) return;

        var dataPoints = callbackQuery.Data.Split(':');

        var action = dataPoints[0] switch
        {
            "subs" => subsCmdService.HandleCallback(callbackQuery, dataPoints, cancellationToken),
            "savesubmit" => saveSubmitCmdService.HandleCallback(callbackQuery, dataPoints, cancellationToken),
            "cancel" => botClient.SendTextMessageAsync(callbackQuery.Message.Chat, "Canceled", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken),
            
            _ => Task.CompletedTask
        };
        
        await action;
    }

    private Task UnknownUpdateHandlerAsync(Update update)
    {
        logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
        return Task.CompletedTask;
    }

    public async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException =>
                $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        logger.LogInformation("HandleError: {ErrorMessage}", errorMessage);

        // Cooldown in case of network connection error
        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }
}