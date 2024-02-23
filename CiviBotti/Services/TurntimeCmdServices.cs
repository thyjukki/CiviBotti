namespace CiviBotti.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Configurations;
using DataModels;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class TurntimeCmdServices
{
    private readonly ITelegramBotClient _botClient;
    private readonly GameContainerService _gameContainer;
    private readonly Database _database;
    private readonly GmrClient _gmrClient;
    private readonly SpeechConfig _speechConfig;
    private readonly ILogger<TurntimeCmdServices> _logger;

    public TurntimeCmdServices(ITelegramBotClient botClient, GameContainerService gameContainer,
        ILogger<TurntimeCmdServices> logger, IOptions<BotConfiguration> configuration, GmrClient gmrClient, Database database) {
        _botClient = botClient;
        _gameContainer = gameContainer;
        _logger = logger;
        _gmrClient = gmrClient;
        _database = database;
        var config = configuration.Value;
        _speechConfig = SpeechConfig.FromSubscription(config.SpeechKey, config.SpeechRegion);
        _speechConfig.SpeechSynthesisVoiceName = "fi-FI-NooraNeural";
    }

    public async Task Tee(Message message, Chat chat, CancellationToken ct) {
        await _botClient.SendChatActionAsync(chat.Id, ChatAction.RecordVoice, cancellationToken: ct);
        var selectedGame = _gameContainer.Games.FirstOrDefault(g => g.Chats.Exists(chatId => chatId == chat.Id));

        if (selectedGame == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "No game added to this group!", cancellationToken: ct);
            return;
        }


        var player = selectedGame.CurrentPlayer;
        var name = player.Name;


        var rnd = new Random();


        string output;
        var args = message.Text!.Split(' ');
        if (args.Length > 1) {
            try {
                var parsed = string.Join(" ", args.Skip(1));
                output = string.Format(parsed, name);
            }
            catch (FormatException) {
                output = "Älä hajota prkl";
            }
        }
        else {
            var rInt = rnd.Next(0, 8);
            output = rInt switch {
                0 => $"{name} tee vuoros",
                1 => $"Voisitko ystävällisesti tehä sen vuoros {name}",
                2 => $"Oispa vuoro {name}",
                3 => $"Älä nyt kasvata sitä turn timerias siel {name}",
                4 => $"Nyt sitä Civiä {name} perkele!",
                5 => $"Nyt vittu se vuoro {name}",
                6 => $"Älä leiki tapiiria {name}",
                7 => $"Civi ei pelaa itseään {name}",
                _ => $"{name} tee vuoros"
            };
        }

        using var speechSynthesizer = new SpeechSynthesizer(_speechConfig);
                
                
        var synthesisResult = await speechSynthesizer.SpeakTextAsync(output);
                
                
        if (synthesisResult.Reason == ResultReason.Canceled) return;
        using var audioDataStream = AudioDataStream.FromResult(synthesisResult);


        using var mStream = new MemoryStream();
        var buffer = new byte[32000];
        uint bytesRead = 0;
        while ((bytesRead = audioDataStream.ReadData(bytesRead, buffer)) > 0)
        {
            mStream.Write(buffer, 0, (int)bytesRead);
        }

        mStream.Position = 0;
        if (synthesisResult.Reason == ResultReason.Canceled) return;
        var file = InputFile.FromStream(mStream, "output.ogg");
        await _botClient.SendVoiceAsync(message.Chat.Id, file, cancellationToken: ct);
    }
    
    public async Task Eta(Message message, Chat chat, CancellationToken ct) {
        await _botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);

        var selectedGame = _gameContainer.Games.FirstOrDefault(g => g.Chats.Exists(chatId => chatId == chat.Id));

        if (selectedGame == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "No game added to this group!", cancellationToken: ct);
            return;
        }

        if (selectedGame.CurrentPlayer.User == null) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Current player has not registered", cancellationToken: ct);
            return;
        }

        if (selectedGame.CurrentPlayer.User.Id != message.From!.Id) {
            await GetOtherPlayerEta(message, selectedGame, selectedGame.CurrentPlayer, ct);
            return;
        }

        var args = message.Text!.Split(' ');
        if (args.Length != 2) {
            await _botClient.SendTextMessageAsync(message.Chat.Id,
                "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!", cancellationToken: ct);
            return;
        }

        int hour;
        var minute = 0;
        var day = 0;
        if (string.Equals(args[1], "kohta", StringComparison.InvariantCultureIgnoreCase)) {
            hour = DateTime.Now.Hour + 1;
            minute = DateTime.Now.Minute;
        }
        else if (string.Equals(args[1], "nyt", StringComparison.InvariantCultureIgnoreCase)) {
            hour = DateTime.Now.Hour;
            minute = DateTime.Now.Minute + 10;
        }
        else {
            if (!ParseTime(args, out hour, ref minute, ref day)) {
                await _botClient.SendTextMessageAsync(message.Chat.Id,
                    "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!", cancellationToken: ct);
                return;
            }
        }


        var eta = DateTime.Today.AddDays(day).AddHours(hour).AddMinutes(minute);


        if (eta <= DateTime.Now) {
            eta = DateTime.Now.Date.AddDays(1).AddHours(eta.Hour).AddMinutes(eta.Minute);
        }

        if (eta >= DateTime.Now.AddDays(7)) {
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Laitappa se vacation mode sit pääl", cancellationToken: ct);
            return;
        }

        selectedGame.CurrentPlayer.NextEta = eta;
        selectedGame.CurrentPlayer.UpdateDatabase(_database);
        await _botClient.SendTextMessageAsync(message.Chat.Id,
            $"{selectedGame.CurrentPlayer.Name} eta set to {selectedGame.CurrentPlayer.NextEta:HH:mm ddd}", cancellationToken: ct);
    }
    
    public async Task Turntimers(Chat chat, bool onlyCurrent, CancellationToken ct) {
        await _botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);
        var selectedGame = _gameContainer.Games.FirstOrDefault(game => game.Chats.Exists(chatId => chatId == chat.Id));
        if (selectedGame == null) {
            await _botClient.SendTextMessageAsync(chat, "No game added to this chat", cancellationToken: ct);
            return;
        }

        var stringBuilder = new StringBuilder();

        var turntimers = await _gmrClient.GetTurntimers(selectedGame);

        if (turntimers == null) {
            await _botClient.SendTextMessageAsync(chat, "Problem fetching data from gmr", cancellationToken: ct);
            return;
        }
        
        var sortedDict = from entry in turntimers orderby entry.Value ascending select entry;

        foreach (var (steamId, timer) in sortedDict) {
            var player = selectedGame.Players.Find(p => p.SteamId == steamId);
            
            if (player == null) {
                _logger.LogWarning("Player not found: {SteamId} in game {SelectedGameGameId}", steamId, selectedGame.GameId);
                continue;
            }
            
            stringBuilder.Append($"{player.Name}");
            if (timer.Days > 0) {
                stringBuilder.Append($" {timer.Days}");
                stringBuilder.Append(timer.Days == 1 ? " päivä" : " päivää");
            }
            if (timer.Hours > 0) {
                stringBuilder.Append($" {timer.Hours}");
                stringBuilder.Append(timer.Hours == 1 ? " tunti" : " tuntia");
            }
            if (timer.Minutes > 0) {
                stringBuilder.Append($" {timer.Minutes}");
                stringBuilder.Append(timer.Minutes == 1 ? " minuutti" : " minuuttia");
            }
            
            stringBuilder.Append('\n');
        }
            
        await _botClient.SendTextMessageAsync(chat, stringBuilder.ToString(), cancellationToken: ct);
    }
    
    
    private async Task GetOtherPlayerEta(Message message, GameData selectedGame, PlayerData player, CancellationToken ct) {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append($"{player.Name} ");
        TimeSpan diff;

        if (player.NextEta < DateTime.Now) {
            var turnTimer = await _gmrClient.GetTurntimer(selectedGame, player);
            if (!turnTimer.HasValue) {
                await _botClient.SendTextMessageAsync(message.Chat.Id, $"Uusi turntimer tulossa! {player.Name}", cancellationToken: ct);
                return;
            }

            var turnTimerHit = selectedGame.TurnStarted + turnTimer.Value;
            diff = (turnTimerHit - DateTime.UtcNow).Duration();

            stringBuilder.Append(turnTimerHit <= DateTime.UtcNow
                ? $"turntimer kärsinyt:"
                : $"turntimer alkaa kärsimään:");
        }
        else {
            stringBuilder.Append("aikaa jäljellä:");
            diff = (player.NextEta - DateTime.Now).Duration();
        }

        var diffMinutes = diff.Minutes % 60;
        var diffHours = diff.Hours % 24;
        var diffDays = (diff.Hours - diffHours) / 24;
        if (diffDays > 0) {
            stringBuilder.Append($" {diffDays}");
            stringBuilder.Append(diffDays == 1 ? " päivä" : " päivää");
        }

        if (diffHours > 0) {
            stringBuilder.Append($" {diffHours}");
            stringBuilder.Append(diffHours == 1 ? " tunti" : " tuntia");
        }

        if (diffMinutes > 0) {
            stringBuilder.Append($" {diffMinutes}");
            stringBuilder.Append(diffMinutes == 1 ? " minuutti" : " minuuttia");
        }

        await _botClient.SendTextMessageAsync(message.Chat.Id, stringBuilder.ToString(), cancellationToken: ct);
    }
    
    private bool ParseTime(IReadOnlyList<string> args, out int hour, ref int minute, ref int day) {
        var hoursMins = args[1].Split(':');
        if (!int.TryParse(hoursMins[0], out hour)) {
            return false;
        }

        if (hoursMins.Length <= 1) {
            return true;
        }

        if (!int.TryParse(hoursMins[1], out minute)) {
            return false;
        }

        return hoursMins.Length <= 2 || int.TryParse(hoursMins[2], out day);
    }
}