namespace CiviBotti.Services;

using Abstract;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

public class ReceiverService : ReceiverServiceBase<UpdateHandler>
{
    public ReceiverService(
        ITelegramBotClient botClient,
        UpdateHandler updateHandler,
        ILogger<ReceiverService> logger)
        : base(botClient, updateHandler, logger)
    {
    }
}