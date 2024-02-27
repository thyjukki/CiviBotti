namespace CiviBotti.Services;

using Abstract;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

public class ReceiverService(
    ITelegramBotClient botClient,
    UpdateHandler updateHandler,
    ILogger<ReceiverService> logger)
    : ReceiverServiceBase<UpdateHandler>(botClient, updateHandler, logger);