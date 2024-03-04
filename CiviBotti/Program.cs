using System;
using CiviBotti.Configurations;
using CiviBotti.Services;

using Gelf.Extensions.Logging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;



var host = Host.CreateDefaultBuilder(args).ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
        {
            logging.AddGelf();
        }
    }).ConfigureAppConfiguration((_, config) =>
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        config.SetBasePath(Environment.CurrentDirectory);
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) => {
        services.Configure<BotConfiguration>(context.Configuration.GetSection(BotConfiguration.Configuration));
        services.AddOptions<BotConfiguration>().Bind(context.Configuration.GetSection(BotConfiguration.Configuration));
        services.Configure<GmrConfiguration>(context.Configuration.GetSection(GmrConfiguration.Configuration));
        services.AddHttpClient("telegram_bot_client")
            .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
            {
                var botConfig = sp.GetService<IOptions<BotConfiguration>>();
                if (botConfig is null)
                    throw new ArgumentNullException(nameof(sp));
                var config = botConfig.Value;
                TelegramBotClientOptions options = new(config.BotToken);
                return new TelegramBotClient(options, httpClient);
            });


        services.AddHttpClient<ISteamApiClient, SteamApiClient>();
        services.AddHttpClient<IGmrClient, GmrClient>();

        services.AddSingleton<IGameContainerService, GameContainerService>();
        services.AddSingleton<IDatabase, Database>();

        services.AddScoped<UpdateHandler>();
        services.AddScoped<ReceiverService>();
        services.AddScoped<GameAdminCmdService>();
        services.AddScoped<TurntimeCmdServices>();
        services.AddScoped<SaveSubmitCmdService>();
        services.AddScoped<SubsCmdService>();
        services.AddScoped<UtilCmdService>();
        services.AddHostedService<PollingService>();
        services.AddHostedService<GamePollingService>();
        services.AddHostedService<GameCleanerService>();
    })
    .Build();


var gameContainer = host.Services.GetRequiredService<IGameContainerService>();
await gameContainer.InitializeAsync();

await host.RunAsync();