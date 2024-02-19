using System;
using System.Linq;
using CiviBotti;
using Microsoft.Extensions.Configuration;

var builder = new ConfigurationBuilder()
    .AddXmlFile("bot.config", optional: true)
    .AddEnvironmentVariables();
var configs = builder.Build();
            
if (configs == null) return;

var dbType = (Database.DatabaseType)Enum.Parse(typeof(Database.DatabaseType), configs["DB_TYPE"]);
var database = new Database(dbType, configs);

var bot = new TelegramBot(configs["BOT_TOKEN"]);

var program = new SubProgram(configs, database, bot);

await program.RunAsync();