namespace CiviBotti.Services;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

public class UpdateHandler : IUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<UpdateHandler> _logger;
    private readonly GameAdminCmdService _gameAdminCmdService;
    private readonly UtilCmdService _utilCmdService;
    private readonly TurntimeCmdServices _turntimeCmdServices;
    private readonly SubsCmdService _subsCmdService;
    private readonly SaveSubmitCmdService _saveSubmitCmdService;

    public UpdateHandler(ITelegramBotClient botClient, ILogger<UpdateHandler> logger, GameAdminCmdService gameAdminCmdService, UtilCmdService utilCmdService, TurntimeCmdServices turntimeCmdServices, SubsCmdService subsCmdService, SaveSubmitCmdService saveSubmitCmdService)
    {
        _botClient = botClient;
        _logger = logger;
        _gameAdminCmdService = gameAdminCmdService;
        _utilCmdService = utilCmdService;
        _turntimeCmdServices = turntimeCmdServices;
        _subsCmdService = subsCmdService;
        _saveSubmitCmdService = saveSubmitCmdService;
    }

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
            { Message: { } message } => BotOnMessageReceived(message, cancellationToken),
            { EditedMessage: { } message } => BotOnMessageReceived(message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
            _ => UnknownUpdateHandlerAsync(update)
        };

        await handler;
    }


    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Receive message type: {MessageType}", message.Type);

        if (message.ReplyToMessage is not null) {
            await _saveSubmitCmdService.HandlePotentialFileUpload(message, cancellationToken);
            return;
        }
        
        if (message.Text is null)
            return;
        if (message.From is null)
            return;
        if (message.Chat is not { } chat)
            return;

        var action = message.Text.Split('@').First().Split(' ').First() switch
        {
            "/newgame" => _gameAdminCmdService.NewGame(message, chat, cancellationToken),
            "/register" => _gameAdminCmdService.RegisterGame(message, chat, cancellationToken),
            "/removegame" => _gameAdminCmdService.RemoveGame(message, chat, cancellationToken),
            "/addgame" => _gameAdminCmdService.AddGame(message, chat, cancellationToken),
            
            "/order" => _utilCmdService.Order(message, chat, cancellationToken),
            "/next" => _utilCmdService.Next(message, chat, cancellationToken),
            "/help" => _utilCmdService.Help(message, chat, cancellationToken),
            
            "/autocracy" => _botClient.SendTextMessageAsync(message.Chat.Id, "Did you mean /order?", cancellationToken: cancellationToken),
            "/oispa" => _botClient.SendTextMessageAsync(message.Chat.Id, "Kaljaa?", cancellationToken: cancellationToken),
            "/teekari" => _botClient.SendTextMessageAsync(message.Chat.Id, "Kaljaa?", cancellationToken: cancellationToken),
            
            "/tee" => _turntimeCmdServices.Tee(message, chat, cancellationToken),
            "/eta" => _turntimeCmdServices.Eta(message, chat, cancellationToken),
            "/turntimer" => _turntimeCmdServices.Turntimers(chat, true, cancellationToken),
            "/turntimers" => _turntimeCmdServices.Turntimers(chat, false, cancellationToken),
            
            "/listsubs" => _subsCmdService.ListSubs(message, chat, cancellationToken),
            "/addsub" => _subsCmdService.AddSub(message, chat, cancellationToken),
            "/removesub" => _subsCmdService.RemoveSub(message, chat, cancellationToken),
            
            "/doturn" => _saveSubmitCmdService.DoTurn(message, chat, cancellationToken),
            "/submitturn" => _saveSubmitCmdService.SubmitTurn(message, chat, cancellationToken),
            
            //"/doturn" => _subsCmdService.ListSubs(chat, true, cancellationToken),
            //"/Submitturn" => _subsCmdService.ListSubs(chat, true, cancellationToken),
            
            _ => Task.CompletedTask
        };

        await action;
    }

    // Process Inline Keyboard callback data
    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);

        if (callbackQuery.Message == null || callbackQuery.Data == null) return;

        var dataPoints = callbackQuery.Data.Split(':');

        var action = dataPoints[0] switch
        {
            "subs" => _subsCmdService.HandleCallback(callbackQuery, dataPoints, cancellationToken),
            "savesubmit" => _saveSubmitCmdService.HandleCallback(callbackQuery, dataPoints, cancellationToken),
            "cancel" => _botClient.SendTextMessageAsync(callbackQuery.Message.Chat, "Canceled", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken),
            
            _ => Task.CompletedTask
        };
        
        await action;
    }

    private Task UnknownUpdateHandlerAsync(Update update)
    {
        _logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
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

        _logger.LogInformation("HandleError: {ErrorMessage}", errorMessage);

        // Cooldown in case of network connection error
        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }
}