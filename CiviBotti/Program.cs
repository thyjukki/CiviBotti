using System;
using CiviBotti;
using CiviBotti.Configurations;
using CiviBotti.Services;
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
    }).ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(Environment.CurrentDirectory);
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
        config.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) => {
        services.Configure<BotConfiguration>(context.Configuration.GetSection(BotConfiguration.Configuration));
        services.AddOptions<BotConfiguration>()
            .Bind(context.Configuration.GetSection(BotConfiguration.Configuration));
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


        services.AddHttpClient("steam_client").AddTypedClient<SteamApiClient>();
        services.AddHttpClient("gmr_client").AddTypedClient<GmrClient>();

        services.AddSingleton<GameContainerService>();
        services.AddSingleton<Database>();

        services.AddScoped<UpdateHandler>();
        services.AddScoped<ReceiverService>();
        services.AddScoped<GameAdminCmdService>();
        services.AddScoped<TurntimeCmdServices>();
        services.AddScoped<SaveSubmitCmdService>();
        services.AddScoped<SubsCmdService>();
        services.AddScoped<UtilCmdService>();
        services.AddHostedService<PollingService>();
        services.AddHostedService<GamePollingService>();
    })
    .Build();


var gameContainer = host.Services.GetRequiredService<GameContainerService>();
await gameContainer.InitializeAsync();

await host.RunAsync();