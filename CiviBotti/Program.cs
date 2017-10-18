
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CiviBotti {
    class Program {
        public static List<GameData> Games;
        
        public static Database Database;
        
        public static TelegramBotClient Bot;

        private static readonly HttpClient HttpInstance = new HttpClient();

        public static void Main() {

            var configMap = new ExeConfigurationFileMap {ExeConfigFilename = "bot.config"};
            var config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            var configValue = config.AppSettings.Settings["DatabaseType"].Value;
            var dbType = (Database.DatabaseType)Enum.Parse(typeof(Database.DatabaseType), configValue);
            Database = new Database(dbType);

            Bot = new TelegramBotClient(config.AppSettings.Settings["BotToken"].Value);//Test
            Bot.OnCallbackQuery += BotOnCallbackQueryReceived;
            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnReceiveError += BotOnReceiveError;

            var me = Bot.GetMeAsync().Result;

            Console.Title = me.Username;

            var aTimer = new System.Timers.Timer(30000);
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
                }
            }
            
            Bot.StartReceiving();
            Tick(null, null);
            while (true)
            {
                var msg = Console.ReadLine();

                if (msg == "quit" || msg == "exit")
                {
                    break;
                }


                foreach (var game in Games)
                {
                    foreach (var chat in game.Chats)
                    {
                        Bot.SendTextMessageAsync(chat, msg);
                    }
                }
            }
            Bot.StopReceiving();
        }

        private static void Tick(object sender, System.Timers.ElapsedEventArgs e) {
            foreach (var game in Games) {
                PollTurn(game);
            }
        }

        private static void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs) {
            Console.WriteLine("BotOnReceiveError:\n" + receiveErrorEventArgs.ApiRequestException.Message);
        }

        private static GameData GetGameFromChat(long chatId)
        {
            return (from game in Games from chat in game.Chats where chat == chatId select game).FirstOrDefault();
        }

        public static async void BotSendText(long id, string message) {
            try {
                await Bot.SendTextMessageAsync(id, message);
            } catch (ApiRequestException ex) {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public static bool IsCommand(string a, string b)
        {
            return string.Equals(a, b, StringComparison.InvariantCultureIgnoreCase) || string.Equals(a, $"{b}@civi_gmr_bot", StringComparison.InvariantCultureIgnoreCase);
        }

        private static async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs) {
            var message = messageEventArgs.Message;
            if (message == null || message.Type != MessageType.TextMessage) return;

            var chat = message.Chat;
            Console.WriteLine(message.Text);

            if (message.ReplyToMessage != null) {

            }


            if (!message.Text.StartsWith("/")) return;

            var command = message.Text.Split(' ')[0];
            
            if (IsCommand(command,"/newgame")) {
                await Bot.SendChatActionAsync(chat.Id, ChatAction.Typing);
                if (chat.Type != ChatType.Private) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "New game can only be created in private chat!");
                    return;
                }

                if (!UserData.CheckDatabase(message.From.Id)) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "You are need to first register!");
                    return;
                }

                var args = message.Text.Split(' ');
                if (args.Length != 2) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Please provide gameid '/newgame gameid'!");
                    return;
                }

                long.TryParse(args[1], out var gameId);
                if (gameId == 0) return;
                

                if (Games.Any(game => game.GameId == gameId))
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Game has already been created!");
                    return;
                }




                var newGame = new GameData
                {
                    Owner = UserData.Get(message.From.Id),
                    GameId = gameId
                };
                JToken data;
                try {
                    data = await GetGameData(newGame);

                } catch (WebException) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Could not connect to services, please try again later!");
                    return;
                }
                if (data == null) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Invalid gameid, or your account is not in the game!");
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
                    var playerData = new PlayerData
                    {
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
                await Bot.SendTextMessageAsync(message.Chat.Id, $"Succesfuly created the game {newGame.Name}!");
            } else if (IsCommand(command,"/register")) {
                await Bot.SendChatActionAsync(chat.Id, ChatAction.Typing);
                if (chat.Type != ChatType.Private) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Registering can only be created in private chat!");
                    return;
                }

                var args = message.Text.Split(' ');
                if (args.Length != 2) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Please provide authKey '/register authkey'!");
                    return;
                }

                if (UserData.CheckDatabase(message.From.Id)) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "You are already registered!");
                    return;
                }

                var steamId = await GetPlayerIdFromAuthkey(args[1]);
                if (steamId == "null") {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Authkey you provided was incorrect!");
                    return;
                }


                var newUser = new UserData
                {
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
                await Bot.SendTextMessageAsync(message.Chat.Id, "Registered with steamid " + steamId);
            } else if (IsCommand(command,"/addgame")) {
                await Bot.SendChatActionAsync(chat.Id, ChatAction.Typing);
                /*if (chat.Type != ChatType.Private) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Registering can only be created in private chat!");
                    return;
                }*/

                if (chat.Type != ChatType.Private) {
                    var admins = new List<ChatMember>(await Bot.GetChatAdministratorsAsync(chat.Id));
                    if (!admins.Exists(x => x.User.Id == message.From.Id)) {
                        await Bot.SendTextMessageAsync(message.Chat.Id, "Only group admin can do this!");
                        return;
                    }
                }

                var args = message.Text.Split(' ');
                if (args.Length != 2) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Please provide game id '/addgame gameid'!");
                    return;
                }

                long gameId;
                try {
                    gameId = long.Parse(args[1]);
                } catch {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Invalid gameid!");
                    return;
                }

                GameData selectedGame = null;
                foreach (var game in Games) {
                    if (game.GameId == gameId) {
                        selectedGame = game;
                    }

                    foreach (var chatid in game.Chats) {
                        if (chatid == chat.Id) {
                            await Bot.SendTextMessageAsync(message.Chat.Id, "Channel already has a game!");
                            return;
                        }
                    }
                }

                if (selectedGame == null) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Could not find a game with given id, you must create one with '/newgame gameid'");
                    return;
                }

                selectedGame.Chats.Add(chat.Id);

                selectedGame.InsertChat(chat.Id);


                await Bot.SendTextMessageAsync(message.Chat.Id, $"Added game {selectedGame.Name} to this channel! You will now receive turn notifications.",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (IsCommand(command,"/removegame")) {
                await Bot.SendChatActionAsync(chat.Id, ChatAction.Typing);

                if (chat.Type != ChatType.Private) {
                    var admins = new List<ChatMember>(await Bot.GetChatAdministratorsAsync(chat.Id));
                    if (!admins.Exists(x => x.User.Id == message.From.Id)) {
                        await Bot.SendTextMessageAsync(message.Chat.Id, "Only group admin can do this!");
                        return;
                    }
                }

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
                    await Bot.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
                    return;
                }

                selectedGame.Chats.Remove(chat.Id);
                selectedGame.RemoveChat(chat.Id);


                await Bot.SendTextMessageAsync(message.Chat.Id, $"Removed game {selectedGame.Name} from this channel! You will not receive any more notifications.",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (IsCommand(command,"/order")) {
                await Bot.SendChatActionAsync(chat.Id, ChatAction.Typing);
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
                    await Bot.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
                    return;
                }

                var orders = selectedGame.Players.OrderBy(x => x.TurnOrder);
                var result = "";
                foreach (var player in orders) {
                    string name;

                    if (player.User != null) {
                        var member = await Bot.GetChatAsync(player.User.Id);
                        name = member.Username;
                    } else {
                        name = await GetSteamUserName(player.SteamId);
                    }

                    if (result != "") {
                        result += "\n";
                    }

                    result += name;
                }

                await Bot.SendTextMessageAsync(message.Chat.Id, $"Order is:\n{result}",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (IsCommand(command,"/next")) {
                await Bot.SendChatActionAsync(chat.Id, ChatAction.Typing);
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
                    await Bot.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
                    return;
                }

                var orders = selectedGame.Players.OrderBy(x => x.TurnOrder);

                var next = selectedGame.CurrentPlayer.TurnOrder + 1;

                if (next >= orders.Count()) {
                    next = 0;
                }
                var player = selectedGame.Players[next];
                string name;

                if (player.User != null) {
                    var member = await Bot.GetChatAsync(player.User.Id);
                    name = member.Username;
                } else {
                    name = await GetSteamUserName(player.SteamId);
                }

                await Bot.SendTextMessageAsync(message.Chat.Id, $"Next player is: {name}");
            } else if (IsCommand(command,"/autocracy") || IsCommand(command,"/freedom")) {
                await Bot.SendTextMessageAsync(message.Chat.Id, "Did you mean /order?");
            } else if (IsCommand(command,"/oispa")) {
                await Bot.SendTextMessageAsync(message.Chat.Id, "Kaljaa?");
            }
            else if (IsCommand(command, "/teekkari"))
            {
                await Bot.SendTextMessageAsync(message.Chat.Id, "Press /f to pay respect to fallen commands");
            }
            else if (IsCommand(command,"/tee"))
            {

                await Bot.SendChatActionAsync(chat.Id, ChatAction.RecordAudio);
                GameData selectedGame = null;
                foreach (var game in Games)
                {
                    foreach (var chatid in game.Chats)
                    {
                        if (chatid == chat.Id)
                        {
                            selectedGame = game;
                            break;
                        }
                    }

                    if (selectedGame != null)
                    {
                        break;
                    }
                }

                if (selectedGame == null)
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
                    return;
                }



                var player = selectedGame.CurrentPlayer;
                string name;
                if (player.User != null)
                {
                    var member = await Bot.GetChatAsync(player.User.Id);
                    name = member.Username;
                }
                else
                {
                    name = await GetSteamUserName(player.SteamId);
                }


                var synth = new SpeechSynthesizer();
                var stream = new MemoryStream();


                synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new CultureInfo("fi-FI"));

                synth.SetOutputToWaveStream(stream);

                var rnd = new Random(DateTime.Now.Millisecond);



                string output;
                var args = message.Text.Split(' ');
                if (args.Length > 1)
                {
                    try
                    {
                        string parsed = string.Join(" ", args.Skip(1));
                        output = string.Format(parsed, name);
                    }
                    catch (FormatException)
                    {
                        output = "Älä hajota prkl";
                    }
                }
                else
                {
                    var rInt = rnd.Next(0, 8);
                    switch (rInt)
                    {
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
                await Bot.SendVoiceAsync(message.Chat.Id, file);
            }
            else if (IsCommand(command,"/eta"))
            {
                await Bot.SendChatActionAsync(chat.Id, ChatAction.Typing);

                GameData selectedGame = null;
                foreach (var game in Games)
                {
                    foreach (var chatid in game.Chats)
                    {
                        if (chatid == chat.Id)
                        {
                            selectedGame = game;
                            break;
                        }
                    }

                    if (selectedGame != null)
                    {
                        break;
                    }
                }

                if (selectedGame == null)
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
                    return;
                }

                if (selectedGame.CurrentPlayer.User == null)
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Current player has not registered");
                    return;
                }
                if (selectedGame.CurrentPlayer.User.Id != message.From.Id)
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "It is not your turn!");
                    return;
                }

                var args = message.Text.Split(' ');
                if (args.Length != 2)
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Please provide time in hours '/eta hours'!");
                    return;
                }

                double hour;
                if (string.Equals(args[1], "kohta", StringComparison.InvariantCultureIgnoreCase))
                {
                    var random = new Random();
                    hour = random.NextDouble();
                }
                else
                {
                    if (!double.TryParse(args[1], out hour))
                    {
                        await Bot.SendTextMessageAsync(message.Chat.Id, "Please provide time in hours '/eta hours'!");
                        return;
                    }
                }


                if (hour <= 0)
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Älä leiki brownstonea");
                    return;
                }
                if (hour >= 168)
                {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Laitappa se vacation mode sit pääl");
                    return;
                }

                string name;
                if (selectedGame.CurrentPlayer.User != null)
                {
                    var member = await Bot.GetChatAsync(selectedGame.CurrentPlayer.User.Id);
                    name = member.Username;
                }
                else
                {
                    name = await GetSteamUserName(selectedGame.CurrentPlayer.SteamId);
                }
                selectedGame.CurrentPlayer.NextEta = DateTime.Now.AddHours(hour);

                selectedGame.CurrentPlayer.UpdateDatabase();
                await Bot.SendTextMessageAsync(message.Chat.Id, $"{name} eta set to {selectedGame.CurrentPlayer.NextEta}");
            }
            else if (IsCommand(command,"/help")) {
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

                    var admins = new List<ChatMember>(await Bot.GetChatAdministratorsAsync(chat.Id));
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

                await Bot.SendTextMessageAsync(message.Chat.Id, usage,
                    replyMarkup: new ReplyKeyboardHide());
            }
        }

        private static async void BotOnCallbackQueryReceived(object sender, CallbackQueryEventArgs callbackQueryEventArgs) {
            await Bot.AnswerCallbackQueryAsync(callbackQueryEventArgs.CallbackQuery.Id,
                $"Received {callbackQueryEventArgs.CallbackQuery.Data}");
        }


        private static async void PollTurn(GameData game) {

            try {
                var gameData = GetGameData(game);

                JToken current;
                try
                {
                    current = (await gameData)["CurrentTurn"];
                }
                catch
                {
                    return;
                }

                if (current == null) {
                    return;
                }
                
                var oldPlayerId = string.Empty;
                if (game.CurrentPlayer != null) {
                    oldPlayerId = game.CurrentPlayer.SteamId;
                }
                var currentPlayerId = (string)current["UserId"];

                if (oldPlayerId != currentPlayerId)
                {

                    string name;

                    var user = UserData.GetBySteamId(currentPlayerId);
                    if (user != null)
                    {
                        var member = await Bot.GetChatAsync(user.Id);
                        name = "@" + member.Username;
                    }
                    else
                    {
                        name = await GetSteamUserName(currentPlayerId);
                    }

                    foreach (var player in game.Players)
                    {
                        if (player.SteamId != currentPlayerId) continue;
                        if (game.CurrentPlayer != null) game.CurrentPlayer.NextEta = DateTime.MinValue;
                        game.CurrentPlayer?.UpdateDatabase();
                        game.CurrentPlayer = player;
                        game.UpdateCurrent();
                    }

                    foreach (var chat in game.Chats)
                    {
                        Console.WriteLine(chat);
                        await Bot.SendTextMessageAsync(chat, $"It's now your turn {name}!",
                            replyMarkup: new ReplyKeyboardHide());
                    }

                }
                else
                {
                    if (game.CurrentPlayer == null) return;
                    if (game.CurrentPlayer.NextEta == DateTime.MinValue) return;
                    if (game.CurrentPlayer.NextEta >= DateTime.Now) return;
                    string name;
                    var user = game.CurrentPlayer.User;
                    if (user != null)
                    {
                        var member = await Bot.GetChatAsync(user.Id);
                        name = "@" + member.Username;
                    }
                    else
                    {
                        name = await GetSteamUserName(currentPlayerId);
                    }

                    game.CurrentPlayer.NextEta = DateTime.MinValue;
                    game.CurrentPlayer.UpdateDatabase();
                    foreach (var chat in game.Chats)
                    {
                        Console.WriteLine(chat);
                        await Bot.SendTextMessageAsync(chat, $"Aikamääreistä pidetään kiinni {name}",
                            replyMarkup: new ReplyKeyboardHide());
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine(ex);
            }

        }

        public static async Task<JToken> GetGameData(GameData game) {
            var url = $"http://multiplayerrobot.com/api/Diplomacy/GetGamesAndPlayers?playerIDText={game.Owner.SteamId}&authKey={game.Owner.AuthKey}";


            var request = await HttpInstance.GetAsync(url);
            var html = await request.Content.ReadAsStringAsync();

            var json = JObject.Parse(html);

            return json["Games"].FirstOrDefault(item => (int) item["GameId"] == game.GameId);
        }

        private static async Task<string> GetPlayerIdFromAuthkey(string authkey) {
            var url = $"http://multiplayerrobot.com/api/Diplomacy/AuthenticateUser?authKey={authkey}";

            var request = await HttpInstance.GetAsync(url);
            var html = await request.Content.ReadAsStringAsync();

            return html;
        }

        private static async Task<string> GetSteamUserName(string steamid) {
            var url = $"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=E2C1F453B82A8F42092118E4B3F55037&steamids={steamid}";

            var request = await HttpInstance.GetAsync(url);
            var html = await request.Content.ReadAsStringAsync();

            var json = JObject.Parse(html);
            var players = json["response"]["players"];

            return players.Count() != 1 ? "UNKNOWN" : players.First["personaname"].ToString();
            
        }
    }
}
