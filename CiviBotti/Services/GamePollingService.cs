namespace CiviBotti.Services;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

public class GamePollingService(
    ISteamApiClient steamClient,
    ITelegramBotClient botClient,
    IGameContainerService gameContainer,
    IDatabase database,
    IGmrClient gmrClient,
    ILogger<GamePollingService> logger)
    : BackgroundService
{
    private readonly PollingTask _pollingTask =
        new PollingTask(steamClient, botClient, gameContainer, database, gmrClient, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _pollingTask.PollGames(stoppingToken);
            await Task.Delay(30000, stoppingToken);
        }
    }
}