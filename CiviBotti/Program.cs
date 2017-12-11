using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Timers;
using Newtonsoft.Json.Linq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using Message = Telegram.Bot.Types.Message;
using Timer = System.Timers.Timer;

namespace CiviBotti
{
    public class Program
    {
        public static List<GameData> Games;

        public static Database Database;

        private static readonly HttpClient HttpInstance = new HttpClient();

        private static TelegramBot _bot;

        [STAThread]
        public static void Main() {
            var configMap = new ExeConfigurationFileMap {ExeConfigFilename = "bot.config"};
            var config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            var configValue = config.AppSettings.Settings["DatabaseType"].Value;
            var dbType = (Database.DatabaseType) Enum.Parse(typeof(Database.DatabaseType), configValue);
            Database = new Database(dbType);

            _bot = new TelegramBot(config.AppSettings.Settings["BotToken"].Value);

            var aTimer = new Timer(30000);
            aTimer.Elapsed += Tick;
            aTimer.Enabled = true;
            aTimer.Start();


            Games = GameData.GetAllGames();
            foreach (var game in Games) {
                Console.WriteLine(game.GameId + " " + game.Owner);
                Console.WriteLine(" chats:");
                foreach (var chat in game.Chats) {
                    Console.WriteLine("  -" + chat);
                }
                Console.WriteLine(" players:");
                foreach (var player in game.Players) {
                    Console.WriteLine($"  -{player.SteamId} ({player.TurnOrder}) {player.User}");

                    
                    if (player.User != null) {
                        var member = _bot.GetChat(player.User.Id);
                        player.SetName(member.Username);
                    } else {
                        player.SetName(GetSteamUserName(player.SteamId));
                    }
                }
            }

            _bot.StartReceiving();
            Tick(null, null);
            while (true) {
                var msg = Console.ReadLine();

                if (msg == "quit" || msg == "exit") {
                    break;
                }


                foreach (var game in Games) {
                    foreach (var chat in game.Chats) {
                        _bot.SendText(chat, msg);
                    }
                }
            }
            _bot.StopReceiving();
        }

        private static void Tick(object sender, ElapsedEventArgs e) {
            foreach (var game in Games) {
                PollTurn(game);
            }
        }

        private static GameData GetGameFromChat(long chatId) {
            return (from game in Games from chat in game.Chats where chat == chatId select game).FirstOrDefault();
        }

        public static bool IsCommand(string a, string b) {
            return string.Equals(a, b, StringComparison.InvariantCultureIgnoreCase) ||
                   string.Equals(a, $"{b}@civi_gmr_bot", StringComparison.InvariantCultureIgnoreCase);
        }

        private static void NewGame(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                _bot.SendText(message.Chat.Id, "New game can only be created in private chat!");
                return;
            }

            if (!UserData.CheckDatabase(message.From.Id)) {
                _bot.SendText(message.Chat.Id, "You are need to first register!");
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                _bot.SendText(message.Chat.Id, "Please provide gameid '/newgame gameid'!");
                return;
            }

            long.TryParse(args[1], out var gameId);
            if (gameId == 0) {
                return;
            }


            if (Games.Any(game => game.GameId == gameId)) {
                _bot.SendText(message.Chat.Id, "Game has already been created!");
                return;
            }


            var newGame = new GameData {
                Owner = UserData.Get(message.From.Id),
                GameId = gameId
            };
            JToken data;
            try {
                data = GetGameData(newGame);
            } catch (WebException) {
                _bot.SendText(message.Chat.Id, "Could not connect to services, please try again later!");
                return;
            }
            if (data == null) {
                _bot.SendText(message.Chat.Id, "Invalid gameid, or your account is not in the game!");
                return;
            }

            newGame.Players = new List<PlayerData>();
            newGame.Chats = new List<long>();
            newGame.Name = (string)data["Name"];
            var current = data["CurrentTurn"];

            if (current == null) {
                return;
            }

            var currentPlayerId = (string)current["UserId"];
            foreach (var player in data["Players"]) {
                var playerData = new PlayerData {
                    GameId = gameId,
                    TurnOrder = player["TurnOrder"].Value<int>(),
                    SteamId = player["UserId"].Value<string>()
                };

                playerData.User = UserData.GetBySteamId(playerData.SteamId);

                if (currentPlayerId == playerData.SteamId) {
                    newGame.CurrentPlayer = playerData;
                }

                newGame.Players.Add(playerData);

                Console.WriteLine(playerData.SteamId + " " + playerData.TurnOrder);
            }

            newGame.InsertFull();
            Games.Add(newGame);
            _bot.SendText(message.Chat.Id, $"Succesfuly created the game {newGame.Name}!");
        }

        public static void ParseCommand(string cmd, Message message) {
            if (message == null || message.Type != MessageType.TextMessage) {
                return;
            }

            var chat = message.Chat;
            

            if (!Enum.TryParse<Command>(cmd, true, out var command)) {
                return;
            }

            Console.WriteLine($"Command {cmd}");

            switch (command) {
                case Command.Newgame:
                    NewGame(message, chat);
                    break;
                case Command.Register:
                    RegisterGame(message, chat);
                    break;
                case Command.Addgame:
                    AddGame(message, chat);
                    break;
                case Command.Removegame:
                    RemoveGame(message, chat);
                    break;
                case Command.Order:
                    Order(message, chat);
                    break;
                case Command.Next:
                    Next(message, chat);
                    break;
                case Command.Autocracy:
                case Command.Freedom:
                    _bot.SendText(message.Chat.Id, "Did you mean /order?");
                    break;
                case Command.Oispa:
                    _bot.SendText(message.Chat.Id, "Kaljaa?");
                    break;
                case Command.Teekari:
                    _bot.SendText(message.Chat.Id, "Press /f to pay respect to fallen commands");
                    break;
                case Command.Tee:
                    Tee(message, chat);
                    break;
                case Command.Eta:
                    Eta(message, chat);
                    break;
                case Command.Help:
                    Help(message, chat);
                    break;
                case Command.Turntimer:
                    Turntimer(chat);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void Turntimer(Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);
            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatid => chatid == chat.Id));
            if (selectedGame == null) {
                _bot.SendText(chat, "No game added to this chat");
                return;
            }

            /*var driver =
                new PhantomJSDriver {Url = $"http://multiplayerrobot.com/Game#{selectedGame.GameId}" };
            driver.Navigate();

            
            var html = driver.PageSource;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);*/

            var url = $"http://multiplayerrobot.com/Game/Details?id={selectedGame.GameId}";
            var response = HttpInstance.PostAsync(url, null).Result;
            if (!response.IsSuccessStatusCode) {
                _bot.SendText(chat, "Problem connecting to gmr service");
                return;
            }
            var doc = new HtmlDocument();
            doc.LoadHtml(response.Content.ReadAsStringAsync().Result);

            var divs = doc.DocumentNode.SelectNodes("//div[@class=\"game-player average\"]");

            var player = selectedGame.CurrentPlayer;
            foreach (var div in divs) {
                var idGroup = Regex.Match(div.InnerHtml, "/Community#\\s*([\\d+]*)");
                if (idGroup.Success) {
                    var id = idGroup.Groups[1].Value;

                    if (!string.Equals(id, player.SteamId, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                }
                var hourGroup = Regex.Match(div.InnerHtml, "(\\d+) hours");
                var hour = 0;
                if (hourGroup.Success) {
                    int.TryParse(hourGroup.Groups[1].Value, out hour);
                }
                var minuteGroup = Regex.Match(div.InnerHtml, "(\\d+) minutes");
                var minute = 0;
                if (minuteGroup.Success) {
                    int.TryParse(minuteGroup.Groups[1].Value, out minute);
                }

                /*if (remainingDays > 0) {
                    stringbuilder += $" {remainingDays}";
                    if (remainingDays == 1) {
                        stringbuilder += " päivä";
                    } else {
                        stringbuilder += " päivää";
                    }
                }*/
                string stringbuilder = "";
                if (hour > 0) {
                    stringbuilder += $" {hour}";
                    if (hour == 1) {
                        stringbuilder += " tunti";
                    } else {
                        stringbuilder += " tuntia";
                    }
                }
                if (minute > 0) {
                    stringbuilder += $" {minute}";
                    if (minute == 1) {
                        stringbuilder += " minuutti";
                    } else {
                        stringbuilder += " minuuttia";
                    }
                }
                _bot.SendText(chat, $"{player.Name} turntimer {stringbuilder}");
                return;
            }
        }

        private static void AddGame(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);
            /*if (chat.Type != ChatType.Private) {
                await Bot.SendText(message.Chat.Id, "Registering can only be created in private chat!");
                return;
            }*/

            if (chat.Type != ChatType.Private) {
                var admins = new List<ChatMember>(_bot.GetAdministrators(chat.Id));
                if (!admins.Exists(x => x.User.Id == message.From.Id)) {
                    _bot.SendText(message.Chat.Id, "Only group admin can do this!");
                    return;
                }
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                _bot.SendText(message.Chat.Id, "Please provide game id '/addgame gameid'!");
                return;
            }

            long gameId;
            try {
                gameId = long.Parse(args[1]);
            } catch {
                _bot.SendText(message.Chat.Id, "Invalid gameid!");
                return;
            }

            GameData selectedGame = null;
            foreach (var game in Games) {
                if (game.GameId == gameId) {
                    selectedGame = game;
                }

                foreach (var chatid in game.Chats) {
                    if (chatid == chat.Id) {
                        _bot.SendText(message.Chat.Id, "Channel already has a game!");
                        return;
                    }
                }
            }

            if (selectedGame == null) {
                _bot.SendText(message.Chat.Id,
                    "Could not find a game with given id, you must create one with '/newgame gameid'");
                return;
            }

            selectedGame.Chats.Add(chat.Id);

            selectedGame.InsertChat(chat.Id);


            _bot.SendText(message.Chat.Id,
                $"Added game {selectedGame.Name} to this channel! You will now receive turn notifications.");
        }

        private static void RemoveGame(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);

            if (chat.Type != ChatType.Private) {
                var admins = new List<ChatMember>(_bot.GetAdministrators(chat.Id));
                if (!admins.Exists(x => x.User.Id == message.From.Id)) {
                    _bot.SendText(message.Chat.Id, "Only group admin can do this!");
                    return;
                }
            }

            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatid => chatid == chat.Id));

            if (selectedGame == null) {
                _bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            selectedGame.Chats.Remove(chat.Id);
            selectedGame.RemoveChat(chat.Id);


            _bot.SendText(message.Chat.Id, $"Removed game {selectedGame.Name} from this channel! You will not receive any more notifications.");
        }

        private static void Order(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);
            GameData selectedGame = null;
            foreach (var game in Games) {
                foreach (var chatid in game.Chats) {
                    if (chatid == chat.Id) {
                        selectedGame = game;
                        break;
                    }
                }

                if (selectedGame != null) {
                    break;
                }
            }

            if (selectedGame == null) {
                _bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            var orders = selectedGame.Players.OrderBy(x => x.TurnOrder);
            var result = "";
            foreach (var player in orders) {
                if (result != "") {
                    result += "\n";
                }

                result += player.Name;
            }

            _bot.SendText(message.Chat.Id, $"Order is:\n{result}");
        }

        private static void Next(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);
            GameData selectedGame = null;
            foreach (var game in Games) {
                foreach (var chatid in game.Chats) {
                    if (chatid == chat.Id) {
                        selectedGame = game;
                        break;
                    }
                }

                if (selectedGame != null) {
                    break;
                }
            }

            if (selectedGame == null) {
                _bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            var orders = selectedGame.Players.OrderBy(x => x.TurnOrder);

            var next = selectedGame.CurrentPlayer.TurnOrder + 1;

            if (next >= orders.Count()) {
                next = 0;
            }
            var player = selectedGame.Players[next];

            _bot.SendText(message.Chat.Id, $"Next player is: {player.Name}");
        }

        private static void Tee(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.RecordAudio);
            GameData selectedGame = null;
            foreach (var game in Games) {
                foreach (var chatid in game.Chats) {
                    if (chatid == chat.Id) {
                        selectedGame = game;
                        break;
                    }
                }

                if (selectedGame != null) {
                    break;
                }
            }

            if (selectedGame == null) {
                _bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }


            var player = selectedGame.CurrentPlayer;
            string name = player.Name;


            var synth = new SpeechSynthesizer();
            var stream = new MemoryStream();


            synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new CultureInfo("fi-FI"));

            synth.SetOutputToWaveStream(stream);

            var rnd = new Random(DateTime.Now.Millisecond);


            string output;
            var args = message.Text.Split(' ');
            if (args.Length > 1) {
                try {
                    var parsed = string.Join(" ", args.Skip(1));
                    output = string.Format(parsed, name);
                } catch (FormatException) {
                    output = "Älä hajota prkl";
                }
            } else {
                var rInt = rnd.Next(0, 8);
                switch (rInt) {
                    case 0:
                        output = $"{name} tee vuoros";
                        break;
                    case 1:
                        output = $"Voisitko ystävällisesti tehä sen vuoros {name}";
                        break;
                    case 2:
                        output = $"Oispa vuoro {name}";
                        break;
                    case 3:
                        output = $"Älä nyt kasvata sitä turn timerias siel {name}";
                        break;
                    case 4:
                        output = $"Nyt sitä Civiä {name} perkele!";
                        break;
                    case 5:
                        output = $"Nyt vittu se vuoro {name}";
                        break;
                    case 6:
                        output = $"Älä leiki tapiiria {name}";
                        break;
                    case 7:
                        output = $"Civi ei pelaa itseään {name}";
                        break;
                    default:
                        output = $"{name} tee vuoros";
                        break;
                }
            }

            synth.Speak(output);
            stream.Flush();

            stream.Seek(0, SeekOrigin.Begin);

            var file = new FileToSend("output.ogg", stream);
            _bot.SendVoice(message.Chat.Id, file);
        }

        private static void Eta(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);

            GameData selectedGame = null;
            foreach (var game in Games) {
                if (game.Chats.Any(chatid => chatid == chat.Id)) {
                    selectedGame = game;
                }

                if (selectedGame != null) {
                    break;
                }
            }

            if (selectedGame == null) {
                _bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            if (selectedGame.CurrentPlayer.User == null) {
                _bot.SendText(message.Chat.Id, "Current player has not registered");
                return;
            }
            if (selectedGame.CurrentPlayer.User.Id != message.From.Id) {
                if (selectedGame.CurrentPlayer.NextEta < DateTime.Now) {
                    _bot.SendText(message.Chat.Id, "It is not your turn!");
                } else {
                    var stringbuilder = $"{selectedGame.CurrentPlayer.Name} aikaa jäljellä";
                    var remaining = selectedGame.CurrentPlayer.NextEta - DateTime.Now;
                    var remainingMinutes = remaining.Minutes % 60;
                    var remainingHours = remaining.Hours % 24;
                    var remainingDays = (remaining.Hours - remainingHours) / 24;
                    if (remainingDays > 0) {
                        stringbuilder += $" {remainingDays}";
                        if (remainingDays == 1) {
                            stringbuilder += " päivä";
                        } else {
                            stringbuilder += " päivää";
                        }
                    }
                    if (remainingHours > 0) {
                        stringbuilder += $" {remainingHours}";
                        if (remainingHours == 1) {
                            stringbuilder += " tunti";
                        } else {
                            stringbuilder += " tuntia";
                        }
                    }
                    if (remainingMinutes > 0) {
                        stringbuilder += $" {remainingMinutes}";
                        if (remainingMinutes == 1) {
                            stringbuilder += " minuutti";
                        } else {
                            stringbuilder += " minuuttia";
                        }
                    }
                    _bot.SendText(message.Chat.Id, stringbuilder);
                }
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                _bot.SendText(message.Chat.Id,
                    "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                return;
            }

            int hour;
            var minute = 0;
            var day = 0;
            if (string.Equals(args[1], "kohta", StringComparison.InvariantCultureIgnoreCase)) {
                hour = DateTime.Now.Hour + 1;
                minute = DateTime.Now.Minute;
            } else if (string.Equals(args[1], "nyt", StringComparison.InvariantCultureIgnoreCase)) {
                hour = DateTime.Now.Hour;
                minute = DateTime.Now.Minute + 10;
            } else {
                var hoursmins = args[1].Split(':');
                if (!int.TryParse(hoursmins[0], out hour)) {
                    _bot.SendText(message.Chat.Id,
                        "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                    return;
                }
                if (hoursmins.Length > 1) {
                    if (!int.TryParse(hoursmins[1], out minute)) {
                        _bot.SendText(message.Chat.Id,
                            "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                        return;
                    }
                    if (hoursmins.Length > 2) {
                        if (!int.TryParse(hoursmins[2], out day)) {
                            _bot.SendText(message.Chat.Id,
                                "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                            return;
                        }
                    }
                }
            }

            
            var eta = DateTime.Today.AddDays(day).AddHours(hour).AddMinutes(minute);


            if (eta <= DateTime.Now) {
                eta = DateTime.Now.Date.AddDays(1).AddHours(eta.Hour).AddMinutes(eta.Minute);
            }
            if (eta >= DateTime.Now.AddDays(7)) {
                _bot.SendText(message.Chat.Id, "Laitappa se vacation mode sit pääl");
                return;
            }

            selectedGame.CurrentPlayer.NextEta = eta;
            selectedGame.CurrentPlayer.UpdateDatabase();
            _bot.SendText(message.Chat.Id, $"{selectedGame.CurrentPlayer.Name} eta set to {selectedGame.CurrentPlayer.NextEta:HH:mm ddd}");
        }

        private static void Help(Message message, Chat chat) {
            string usage;
            if (chat.Type == ChatType.Private) {
                usage = @"CiviBotti:
/help - lolapua
/register 'authkey' - register your authorization key
/newgame 'gameid' - creates a new game
/addgame 'gameid' - add a game to this chat
/removegame - Remove assigned game from chat";
            } else {
                var game = GetGameFromChat(message.Chat.Id);

                var admins = new List<ChatMember>(_bot.GetAdministrators(chat.Id));
                if (admins.Exists(x => x.User.Id == message.From.Id)) {
                    if (game != null) {
                        usage = @"CiviBotti:
/help - lolapua
/addgame 'gameid' - Add a game to this chat";
                    } else {
                        usage = @"CiviBotti:
/help - lolapua
/order - display order of players
/removegame - Remove assigned game from chat";
                    }
                } else {
                    if (game != null) {
                        usage = @"CiviBotti:
/help - lolapua";
                    } else {
                        usage = @"CiviBotti:
/help - lolapua
/order - display order of players";
                    }
                }
            }

            _bot.SendText(message.Chat.Id, usage);
        }

        private static void RegisterGame(Message message, Chat chat) {
            _bot.SetChatAction(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                _bot.SendText(message.Chat.Id, "Registering can only be created in private chat!");
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                _bot.SendText(message.Chat.Id, "Please provide authKey '/register authkey'!");
                return;
            }

            if (UserData.CheckDatabase(message.From.Id)) {
                _bot.SendText(message.Chat.Id, "You are already registered!");
                return;
            }

            var steamId = GetPlayerIdFromAuthkey(args[1]);
            if (steamId == "null") {
                _bot.SendText(message.Chat.Id, "Authkey you provided was incorrect!");
                return;
            }


            var newUser = new UserData {
                Id = message.From.Id,
                SteamId = steamId,
                AuthKey = args[1]
            };
            newUser.InsertDatabase(false);
            foreach (var game in Games) {
                foreach (var player in game.Players) {
                    if (player.SteamId == steamId) {
                        player.User = newUser;
                    }
                }
            }
            _bot.SendText(message.Chat.Id, "Registered with steamid " + steamId);
        }


        private static TimeSpan? GetTurntimer(GameData selectedGame, PlayerData player) {


            /*var driver =
                new PhantomJSDriver {Url = $"http://multiplayerrobot.com/Game#{selectedGame.GameId}" };
            driver.Navigate();

            
            var html = driver.PageSource;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);*/

            var url = $"http://multiplayerrobot.com/Game/Details?id={selectedGame.GameId}";
            var response = HttpInstance.PostAsync(url, null).Result;
            if (!response.IsSuccessStatusCode) {
                return null;
            }
            var doc = new HtmlDocument();
            doc.LoadHtml(response.Content.ReadAsStringAsync().Result);

            var divs = doc.DocumentNode.SelectNodes("//div[@class=\"game-player average\"]");

            foreach (var div in divs) {
                var idGroup = Regex.Match(div.InnerHtml, "/Community#\\s*([\\d+]*)");
                if (idGroup.Success) {
                    var id = idGroup.Groups[1].Value;

                    if (!string.Equals(id, player.SteamId, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                }
                var hourGroup = Regex.Match(div.InnerHtml, "(\\d+) hours");
                var hour = 0;
                if (hourGroup.Success) {
                    int.TryParse(hourGroup.Groups[1].Value, out hour);
                }
                var minuteGroup = Regex.Match(div.InnerHtml, "(\\d+) minutes");
                var minute = 0;
                if (minuteGroup.Success) {
                    int.TryParse(minuteGroup.Groups[1].Value, out minute);
                }
                return new TimeSpan(hour, minute, 0);
            }
            return null;
        }

        private static async void PollTurn(GameData game) {
            try {
                var gameData = GetGameData(game);

                JToken current;
                try {
                    current = (gameData)["CurrentTurn"];
                }
                catch {
                    return;
                }

                if (current == null) {
                    return;
                }

                var oldPlayerId = string.Empty;
                if (game.CurrentPlayer != null) {
                    oldPlayerId = game.CurrentPlayer.SteamId;
                }
                var currentPlayerId = (string) current["UserId"];

                if (oldPlayerId != currentPlayerId) {

                    foreach (var player in game.Players) {
                        if (player.SteamId != currentPlayerId) {
                            continue;
                        }
                        if (game.CurrentPlayer != null) {
                            game.CurrentPlayer.NextEta = DateTime.MinValue;
                        }
                        game.CurrentPlayer?.UpdateDatabase();
                        game.CurrentPlayer = player;
                        game.TurntimerNotified = false;
                        game.TurnStarted = DateTime.Now;
                        game.UpdateCurrent();

                        if (player.User == null) {
                            player.SetName(GetSteamUserName(player.SteamId));
                        }
                        break;
                    }

                    foreach (var chat in game.Chats) {
                        Console.WriteLine(chat);
                        if (game.CurrentPlayer != null) {
                            _bot.SendText(chat, $"It's now your turn {game.CurrentPlayer.Name}!");
                        } else {
                            _bot.SendText(chat, "It's now your turn waitwhatthishsouldntbehappening?!");
                        }
                    }
                }
                else {
                    if (game.CurrentPlayer == null) {
                        return;
                    }
                    if (game.CurrentPlayer.NextEta == DateTime.MinValue) {
                        if (game.TurntimerNotified) return;
                        var turnTimer = GetTurntimer(game, game.CurrentPlayer);
                        if (!turnTimer.HasValue) return;
                        if (!(game.TurnStarted + turnTimer.Value > DateTime.Now)) return;
                        game.TurntimerNotified = true;
                        foreach (var chat in game.Chats) {
                            _bot.SendText(chat, $"Turn timer kärsii {game.CurrentPlayer.Name}");
                        }
                    } else {
                        if (game.CurrentPlayer.NextEta >= DateTime.Now) {
                            return;
                        }

                        game.CurrentPlayer.NextEta = DateTime.MinValue;
                        game.CurrentPlayer.UpdateDatabase();
                        foreach (var chat in game.Chats) {
                            _bot.SendText(chat, $"Aikamääreistä pidetään kiinni {game.CurrentPlayer.Name}");
                        }
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
        }

        public static JToken GetGameData(GameData game) {
            var url =
                $"http://multiplayerrobot.com/api/Diplomacy/GetGamesAndPlayers?playerIDText={game.Owner.SteamId}&authKey={game.Owner.AuthKey}";


            var request = HttpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(html);

            return json["Games"].FirstOrDefault(item => (int) item["GameId"] == game.GameId);
        }

        private static string GetPlayerIdFromAuthkey(string authkey) {
            var url = $"http://multiplayerrobot.com/api/Diplomacy/AuthenticateUser?authKey={authkey}";

            var request = HttpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            return html;
        }

        public static string GetSteamUserName(string steamid) {
            var url =
                $"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=E2C1F453B82A8F42092118E4B3F55037&steamids={steamid}";

            var request = HttpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(html);
            var players = json["response"]["players"];

            return players.Count() != 1 ? "UNKNOWN" : players.First["personaname"].ToString();
        }
    }
}