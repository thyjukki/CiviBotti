using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace CiviBotti.Services;

public class GameCleanerService(
    ITelegramBotClient botClient,
    IGameContainerService gameContainer,
    IDatabase database,
    ILogger<GamePollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botUser = await botClient.GetMeAsync(cancellationToken: stoppingToken);
        foreach (var game in gameContainer.Games.Where(gameData => gameData.Chats.Count > 0 && !gameData.IsOver))
        {
            foreach (var chat in game.Chats.ToList())
            {
                try
                {
                    await botClient.GetChatMemberAsync(chat, botUser.Id, stoppingToken);
                }
                catch (ApiRequestException exception)
                {
                    if (exception.ErrorCode == 403)
                    {
                        game.RemoveChat(database, chat);
                        logger.LogInformation("Bot is not a member of chat {ChatId} anymore, removing chat", chat);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}