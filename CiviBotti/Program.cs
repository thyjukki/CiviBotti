using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Permissions;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using ConfigurationManager = System.Configuration.ConfigurationManager;
using File = Telegram.Bot.Types.File;
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

        public static TelegramBot Bot;

        private static IConfigurationRoot Configs { get; set; }

        [STAThread]
        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.ControlAppDomain)]
        public static void Main() {
            var builder = new ConfigurationBuilder()
                .AddXmlFile("bot.config", optional: true)
                .AddEnvironmentVariables();
            Configs = builder.Build();

            var dbType = (Database.DatabaseType)Enum.Parse(typeof(Database.DatabaseType), Configs["DB_TYPE"]);
            Database = new Database(dbType, Configs);

            Bot = new TelegramBot(Configs["BOT_TOKEN"]);


            Games = GameData.GetAllGames();
            foreach (var game in Games) {
                game.GetGameData();
            }

            var players = (from game in Games from player in game.Players select player.SteamId).ToList();
            var playerSteamNames = GetSteamUserNames(players);

            foreach (var game in Games) {
                Console.WriteLine($"{game} {game.Owner}");
                Console.WriteLine(" chats:");
                foreach (var chat in game.Chats) {
                    Console.WriteLine("  -" + chat);
                }

                Console.WriteLine(" players:");
                foreach (var player in game.Players) {
                    playerSteamNames.TryGetValue(player.SteamId, out player.SteamName);


                    if (player.User != null) {
                        player.TgName = Bot.GetChat(player.User.Id)?.Username;
                    }

                    Console.WriteLine($"  -{player} ({player.TurnOrder}) {player.User}");
                }

                Console.WriteLine("\n");
            }

            Bot.StartReceiving();
            var aTimer = new Timer(30000);
            aTimer.Elapsed += Tick;
            aTimer.Enabled = true;
            aTimer.Start();
            Tick(null, null);
            while (true) {
                var msg = Console.ReadLine();

                if (msg == "quit" || msg == "exit") {
                    break;
                }
            }

            Bot.StopReceiving();
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
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "New game can only be created in private chat!");
                return;
            }

            if (!UserData.CheckDatabase(message.From.Id)) {
                Bot.SendText(message.Chat.Id, "You are need to first register!");
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                Bot.SendText(message.Chat.Id, "Please provide gameid '/newgame gameid'!");
                return;
            }

            long.TryParse(args[1], out var gameId);
            if (gameId == 0) {
                return;
            }


            if (Games.Any(game => game.GameId == gameId)) {
                Bot.SendText(message.Chat.Id, "Game has already been created!");
                return;
            }

            var newGame = new GameData {
                Owner = UserData.Get(message.From.Id),
                GameId = gameId
            };
            JToken data;
            try {
                data = GetGameData(newGame);
            }
            catch (WebException) {
                Bot.SendText(message.Chat.Id, "Could not connect to services, please try again later!");
                return;
            }

            if (data == null) {
                Bot.SendText(message.Chat.Id, "Invalid gameid, or your account is not in the game!");
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
                playerData.SteamName = GetSteamUserName(playerData.SteamId);

                if (playerData.User != null) {
                    playerData.TgName = Bot.GetChat(playerData.User.Id)?.Username;
                }

                if (currentPlayerId == playerData.SteamId) {
                    newGame.CurrentPlayer = playerData;
                }

                newGame.Players.Add(playerData);

                Console.WriteLine(playerData.SteamId + " " + playerData.TurnOrder);
            }

            newGame.TurnStarted = current["Started"].ToObject<DateTime>().AddHours(2);
            newGame.TurntimerNotified = true;
            newGame.InsertFull();
            Games.Add(newGame);
            Bot.SendText(message.Chat.Id, $"Succesfuly created the game {newGame.Name}!");
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
                    Bot.SendText(message.Chat.Id, "Did you mean /order?");
                    break;
                case Command.Oispa:
                    Bot.SendText(message.Chat.Id, "Kaljaa?");
                    break;
                case Command.Teekari:
                    Bot.SendText(message.Chat.Id, "Press /f to pay respect to fallen commands");
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
                case Command.Listsubs:
                    ListSubs(message, chat);
                    break;
                case Command.Addsub:
                    AddSub(message, chat);
                    break;
                case Command.Removesub:
                    RemoveSub(message, chat);
                    break;
                case Command.Doturn:
                    DoTurn(message, chat);
                    break;
                case Command.Submitturn:
                    SubmitTurn(message, chat);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void SubmitTurn(Message message, Chat chat) {
            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "This can only be done in private!");
                return;
            }

            Bot.SetChatAction(message.Chat.Id, ChatAction.Typing);

            UserData selectedUser = null;
            GameData selectedGame = null;

            var callerUser = UserData.Get(message.From.Id);

            if (callerUser == null) {
                Bot.SendText(chat, "You are not registered in any games");
                return;
            }

            void GetSelectedCallback(Message msg) {
                foreach (var game in Games) {
                    var user = game.CurrentPlayer.User;
                    if (user == null) {
                        continue;
                    }

                    if (!user.Subs.Exists(_ => _.SubId == callerUser.Id) && user != callerUser) {
                        continue;
                    }

                    if ($"{user.Name}@{game.Name}" == msg.Text) {
                        selectedUser = user;
                        selectedGame = game;
                        Bot.AddReplyGet(message.From.Id, chat.Id, GetUploadCallback);
                        Bot.SendText(message.Chat.Id, "Upload the file");
                        return;
                    }
                }
            }

            void GetUploadCallback(Message msg) {
                if (msg.Type != MessageType.DocumentMessage) {
                    Bot.AddReplyGet(message.From.Id, chat.Id, GetUploadCallback);
                    Bot.SendText(message.Chat.Id, "Respond by uploading a file");
                    return;
                }


                Bot.SendText(message.Chat.Id, "Submiting turn");
                Bot.SetChatAction(message.Chat.Id, ChatAction.UploadDocument);
                UploadSave(selectedUser, callerUser, selectedGame, msg.Document);
            }

            var test = new List<KeyboardButton>();
            foreach (var game in Games) {
                var user = game.CurrentPlayer.User;
                if (user == null) {
                    continue;
                }

                if (user.Subs.Exists(_ => _.SubId == callerUser.Id) || user == callerUser)
                    test.Add(new KeyboardButton($"{user.Name}@{game.Name}"));
            }

            if (test.Count == 0) {
                Bot.SendText(message.Chat.Id, "You can not submit anyones turn at the moment");
                return;
            }

            test.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(test.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            Bot.AddReplyGet(message.From.Id, chat.Id, GetSelectedCallback);
            Bot.SendText(message.Chat.Id, "Chose game to submit save to", forceReply);
        }

        private static void DoTurn(Message message, Chat chat) {
            Bot.SetChatAction(message.Chat.Id, ChatAction.Typing);

            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "This can only be done in private!");
                return;
            }

            var callerUser = UserData.Get(message.From.Id);

            if (callerUser == null) {
                Bot.SendText(chat, "You are not registered in any games", new ReplyKeyboardRemove());
                return;
            }

            void GetSelectedCallback(Message msg) {
                foreach (var game in Games) {
                    var user = game.CurrentPlayer.User;
                    if (user == null) {
                        continue;
                    }

                    if (!user.Subs.Exists(_ => _.SubId == callerUser.Id) && user != callerUser) {
                        continue;
                    }

                    if ($"{user.Name}@{game.Name}" != msg.Text) {
                        continue;
                    }

                    Bot.SetChatAction(message.Chat.Id, ChatAction.UploadDocument);
                    DownloadSave(user, callerUser, game);
                    return;
                }
            }

            var test = new List<KeyboardButton>();
            foreach (var game in Games) {
                var user = game.CurrentPlayer.User;
                if (user == null) {
                    continue;
                }

                if (user.Subs.Exists(_ => _.SubId == callerUser.Id) || user == callerUser)
                    test.Add(new KeyboardButton($"{user.Name}@{game.Name}"));
            }

            if (test.Count == 0) {
                Bot.SendText(message.Chat.Id, "You can not play anyones turn at the moment", new ReplyKeyboardRemove());
                return;
            }

            test.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(test.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            Bot.AddReplyGet(message.From.Id, chat.Id, GetSelectedCallback);
            Bot.SendText(message.Chat.Id, "Chose game to sub", forceReply);
        }

        private static void DownloadSave(UserData user, UserData callerUser, GameData game) {
            HttpInstance
                .GetAsync(
                    $"http://multiplayerrobot.com/api/Diplomacy/GetLatestSaveFileBytes?authKey={user.AuthKey}&gameId={game.GameId}")
                .ContinueWith(
                    (requestTask) => {
                        var response = requestTask.Result;
                        if (!response.IsSuccessStatusCode) return;

                        var stream = response.Content.ReadAsStreamAsync().Result;
                        var file = new FileToSend($"(GMR) {user.Name} {game.Name}.Civ5Save", stream);
                        Bot.SendFile(callerUser.Id, file);
                        Bot.SendText(callerUser.Id, "Use /submitturn command to submit turn",
                            new ReplyKeyboardRemove());
                        Bot.SendText(user.Id, $"{callerUser.Name} downloaded your turn");
                    });
        }

        private static void UploadSave(UserData user, UserData callerUser, GameData game, File doc) {
            var uri = new Uri("http://multiplayerrobot.com/");
            var httpClient = new HttpClient();
            httpClient.BaseAddress = uri;
            httpClient.DefaultRequestHeaders.ExpectContinue = false;
            var stream = Bot.GetFileAsStream(doc);
            var form =
                new MultipartFormDataContent(
                    $"Upload----{(object)DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)}") {
                    { new StringContent(game.TurnId), "turnId" },
                    { new StringContent("False"), "isCompressed" },
                    { new StringContent(user.AuthKey), "authKey" },
                    { new StreamContent(stream), "saveFileUpload", $"{game.TurnId}.Civ5Save" }
                };

            httpClient
                .PostAsync(
                    "Game/UploadSaveClient",
                    form).ContinueWith(
                    (requestTask) => {
                        var response = requestTask.Result;
                        if (!response.IsSuccessStatusCode) {
                            return;
                        }

                        var json = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                        if (json["ResultType"].ToString() == "1") {
                            Bot.SendText(callerUser.Id, "Turn submited");
                            Bot.SendText(user.Id, $"{callerUser.Name} submited your turn");
                        }
                        else {
                            Bot.SendText(callerUser.Id, $"Failed to submit turn {json["ResultType"]}");
                        }
                    });
        }

        private static void RemoveSub(Message message, Chat chat) {
            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "This can only be done in private!");
                return;
            }

            Bot.SetChatAction(message.Chat.Id, ChatAction.Typing);

            var callerUser = UserData.Get(message.From.Id);

            if (callerUser == null) {
                Bot.SendText(message.Chat.Id, "You need to be registered to use this '/register authkey'!");
                return;
            }

            void GetSelectedCallback(Message msg) {
                var sub = callerUser.Subs.Find(_ => $"{Bot.GetChat(_.SubId).Username}@{_.Game.Name}" == msg.Text);
                if (sub == null) {
                    return;
                }

                var game = sub.Game;
                var user = UserData.Get(sub.SubId);

                callerUser.Subs.Remove(sub);
                sub.RemoveSub();
                if (user == null || game == null) {
                    Console.WriteLine("GetSelectedCallback weird case");
                }

                Bot.SendText(chat, $"Removed {user.Name} subbing from {game.Name}", new ReplyKeyboardRemove());
                Bot.SendText(user.Id, $"{callerUser.Name} revoked your sub rights from {game.Name}");
            }

            var test = callerUser.Subs.Select(_ => new KeyboardButton($"{Bot.GetChat(_.SubId).Username}@{_.Game.Name}"))
                .ToList();

            test.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(test.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            Bot.AddReplyGet(message.From.Id, chat.Id, GetSelectedCallback);
            Bot.SendText(message.Chat.Id, "Chose sub to remove", forceReply);
        }

        private static void AddSub(Message message, Chat chat) {
            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "This can only be done in private!");
                return;
            }

            Bot.SetChatAction(message.Chat.Id, ChatAction.Typing);

            var callerUser = UserData.Get(message.From.Id);

            if (callerUser == null) {
                Bot.SendText(message.Chat.Id, "You need to be registered to use this '/register authkey'!");
                return;
            }

            GameData selectedGame = null;
            UserData selectedUser = null;

            void GetGameCallback(Message msg) {
                selectedGame = Games.Find(_ => _.Name == msg.Text);

                var users = selectedGame.Players.Where(_ => _.User != null).Select(_ => new KeyboardButton(_.Name))
                    .ToArray();

                var userReply = new ReplyKeyboardMarkup(users) {
                    OneTimeKeyboard = true,
                    Selective = true
                };
                Bot.AddReplyGet(msg.From.Id, chat.Id, GetUserCallback);
                Bot.SendText(msg.Chat.Id, "Chose the user", userReply);
            }

            void GetUserCallback(Message msg) {
                selectedUser = selectedGame.Players.Find(_ => _.User != null && _.User.Name == msg.Text).User;

                Bot.AddReplyGet(msg.From.Id, chat.Id, GetTimesCallback);
                Bot.SendText(msg.Chat.Id, "How many times can he play your turns in this game? (0 for unlimited)",
                    new ReplyKeyboardRemove());
            }

            void GetTimesCallback(Message msg) {
                if (!int.TryParse(msg.Text, out var selectedTimes) || selectedTimes < 0) {
                    Bot.AddReplyGet(msg.From.Id, chat.Id, GetTimesCallback);
                    Bot.SendText(msg.Chat.Id, "Please provide a positive integer", new ReplyKeyboardRemove());
                    return;
                }

                SetSub(selectedGame, selectedUser, selectedTimes);
            }


            void SetSub(GameData game, UserData user, int times) {
                var sub = new SubData {
                    Id = callerUser.Id,
                    SubId = user.Id,
                    Times = times,
                    Game = game
                };
                sub.InsertDatabase(false);
                user.Subs.Add(sub);
                Bot.SendText(message.Chat.Id, $"Added {user.Name} as sub in {game.Name}", new ReplyKeyboardRemove());
                Bot.SendText(user.Id, $"{callerUser.Name} has given you rights to do his turn in {game.Name}");
            }

            var games = Games.Where(_ => _.Players.Exists(player => player.User != null && player.User == callerUser))
                .Select(game => new KeyboardButton(game.Name)).ToList();


            var forceReply = new ReplyKeyboardMarkup(games.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            Bot.AddReplyGet(message.From.Id, chat.Id, GetGameCallback);
            Bot.SendText(message.Chat.Id, "Chose the game", forceReply);
        }

        private static void Turntimer(Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatid => chatid == chat.Id));
            if (selectedGame == null) {
                Bot.SendText(chat, "No game added to this chat");
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
                Bot.SendText(chat, "Problem connecting to gmr service");
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

                var hourGroup = Regex.Match(div.InnerHtml, "(\\d+) hour");
                var hour = 0;
                if (hourGroup.Success) {
                    int.TryParse(hourGroup.Groups[1].Value, out hour);
                }

                var minuteGroup = Regex.Match(div.InnerHtml, "(\\d+) minute");
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
                var stringbuilder = "";
                if (hour > 0) {
                    stringbuilder += $" {hour}";
                    if (hour == 1) {
                        stringbuilder += " tunti";
                    }
                    else {
                        stringbuilder += " tuntia";
                    }
                }

                if (minute > 0) {
                    stringbuilder += $" {minute}";
                    if (minute == 1) {
                        stringbuilder += " minuutti";
                    }
                    else {
                        stringbuilder += " minuuttia";
                    }
                }

                Bot.SendText(chat, $"{player.Nametag} turntimer {stringbuilder}");
                return;
            }
        }

        private static void AddGame(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
            /*if (chat.Type != ChatType.Private) {
                await Bot.SendText(message.Chat.Id, "Registering can only be created in private chat!");
                return;
            }*/

            if (chat.Type != ChatType.Private) {
                var admins = new List<ChatMember>(Bot.GetAdministrators(chat.Id));
                if (!admins.Exists(x => x.User.Id == message.From.Id)) {
                    Bot.SendText(message.Chat.Id, "Only group admin can do this!");
                    return;
                }
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                Bot.SendText(message.Chat.Id, "Please provide game id '/addgame gameid'!");
                return;
            }

            long gameId;
            try {
                gameId = long.Parse(args[1]);
            }
            catch {
                Bot.SendText(message.Chat.Id, "Invalid gameid!");
                return;
            }

            GameData selectedGame = null;
            foreach (var game in Games) {
                if (game.GameId == gameId) {
                    selectedGame = game;
                }

                foreach (var chatid in game.Chats) {
                    if (chatid == chat.Id) {
                        Bot.SendText(message.Chat.Id, "Channel already has a game!");
                        return;
                    }
                }
            }

            if (selectedGame == null) {
                Bot.SendText(message.Chat.Id,
                    "Could not find a game with given id, you must create one with '/newgame gameid'");
                return;
            }

            selectedGame.Chats.Add(chat.Id);

            selectedGame.InsertChat(chat.Id);


            Bot.SendText(message.Chat.Id,
                $"Added game {selectedGame.Name} to this channel! You will now receive turn notifications.");
        }

        private static void RemoveGame(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);

            if (chat.Type != ChatType.Private) {
                var admins = new List<ChatMember>(Bot.GetAdministrators(chat.Id));
                if (!admins.Exists(x => x.User.Id == message.From.Id)) {
                    Bot.SendText(message.Chat.Id, "Only group admin can do this!");
                    return;
                }
            }

            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatid => chatid == chat.Id));

            if (selectedGame == null) {
                Bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            selectedGame.Chats.Remove(chat.Id);
            selectedGame.RemoveChat(chat.Id);


            Bot.SendText(message.Chat.Id,
                $"Removed game {selectedGame.Name} from this channel! You will not receive any more notifications.");
        }

        private static void Order(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
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
                Bot.SendText(message.Chat.Id, "No game added to this group!");
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

            Bot.SendText(message.Chat.Id, $"Order is:\n{result}");
        }

        private static void Next(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
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
                Bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            var orders = selectedGame.Players.OrderBy(x => x.TurnOrder);

            var next = selectedGame.CurrentPlayer.TurnOrder + 1;

            if (next >= orders.Count()) {
                next = 0;
            }

            var player = orders.ToList()[next];

            Bot.SendText(message.Chat.Id, $"Next player is: {player.Nametag}");
        }

        private static async Task Tee(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.RecordAudio);
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
                Bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }


            var player = selectedGame.CurrentPlayer;
            var name = player.Name;


            var rnd = new Random();


            string output;
            var args = message.Text.Split(' ');
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

            var speechConfig = SpeechConfig.FromSubscription(Configs["SPEECH_KEY"], Configs["SPEECH_REGION"]);

            // The language of the voice that speaks.
            speechConfig.SpeechSynthesisVoiceName = "fi-FI-NooraNeural";

            var path = Path.GetRandomFileName();
            using var speechSynthesizer = new SpeechSynthesizer(speechConfig, AudioConfig.FromWavFileOutput(path));
            var synthesisResult = await speechSynthesizer.SpeakTextAsync(output);

            if (synthesisResult.Reason == ResultReason.Canceled) return;
            var file = new FileToSend("output.ogg", System.IO.File.Open(path, FileMode.Open));
            Bot.SendVoice(message.Chat.Id, file);
            System.IO.File.Delete(path);
        }

        private static void Eta(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);

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
                Bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            if (selectedGame.CurrentPlayer.User == null) {
                Bot.SendText(message.Chat.Id, "Current player has not registered");
                return;
            }

            if (selectedGame.CurrentPlayer.User.Id != message.From.Id) {
                var stringbuilder = $"{selectedGame.CurrentPlayer.Nametag} ";
                TimeSpan diff;
                int diffMinutes;
                int diffHours;
                int diffDays;

                if (selectedGame.CurrentPlayer.NextEta < DateTime.Now) {
                    var turnTimer = GetTurntimer(selectedGame, selectedGame.CurrentPlayer);
                    if (!turnTimer.HasValue) {
                        Bot.SendText(message.Chat.Id, $"Uusi turntimer tulossa! {selectedGame.CurrentPlayer.Nametag}");
                        return;
                    }

                    var turnTimerHit = selectedGame.TurnStarted + turnTimer.Value;
                    diff = (turnTimerHit - DateTime.UtcNow).Duration();

                    if (turnTimerHit <= DateTime.UtcNow) {
                        stringbuilder += $"turntimer kärsinyt:";
                    }
                    else {
                        stringbuilder += $"turntimer alkaa kärsimään:";
                    }
                }
                else {
                    stringbuilder += "aikaa jäljellä:";
                    diff = (selectedGame.CurrentPlayer.NextEta - DateTime.Now).Duration();
                }

                diffMinutes = diff.Minutes % 60;
                diffHours = diff.Hours % 24;
                diffDays = (diff.Hours - diffHours) / 24;
                if (diffDays > 0) {
                    stringbuilder += $" {diffDays}";
                    if (diffDays == 1) {
                        stringbuilder += " päivä";
                    }
                    else {
                        stringbuilder += " päivää";
                    }
                }

                if (diffHours > 0) {
                    stringbuilder += $" {diffHours}";
                    if (diffHours == 1) {
                        stringbuilder += " tunti";
                    }
                    else {
                        stringbuilder += " tuntia";
                    }
                }

                if (diffMinutes > 0) {
                    stringbuilder += $" {diffMinutes}";
                    if (diffMinutes == 1) {
                        stringbuilder += " minuutti";
                    }
                    else {
                        stringbuilder += " minuuttia";
                    }
                }

                Bot.SendText(message.Chat.Id, stringbuilder);
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                Bot.SendText(message.Chat.Id,
                    "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
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
                var hoursmins = args[1].Split(':');
                if (!int.TryParse(hoursmins[0], out hour)) {
                    Bot.SendText(message.Chat.Id,
                        "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                    return;
                }

                if (hoursmins.Length > 1) {
                    if (!int.TryParse(hoursmins[1], out minute)) {
                        Bot.SendText(message.Chat.Id,
                            "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                        return;
                    }

                    if (hoursmins.Length > 2) {
                        if (!int.TryParse(hoursmins[2], out day)) {
                            Bot.SendText(message.Chat.Id,
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
                Bot.SendText(message.Chat.Id, "Laitappa se vacation mode sit pääl");
                return;
            }

            selectedGame.CurrentPlayer.NextEta = eta;
            selectedGame.CurrentPlayer.UpdateDatabase();
            Bot.SendText(message.Chat.Id,
                $"{selectedGame.CurrentPlayer.Name} eta set to {selectedGame.CurrentPlayer.NextEta:HH:mm ddd}");
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
            }
            else {
                var game = GetGameFromChat(message.Chat.Id);

                var admins = new List<ChatMember>(Bot.GetAdministrators(chat.Id));
                if (admins.Exists(x => x.User.Id == message.From.Id)) {
                    if (game != null) {
                        usage = @"CiviBotti:
/help - lolapua
/addgame 'gameid' - Add a game to this chat";
                    }
                    else {
                        usage = @"CiviBotti:
/help - lolapua
/order - display order of players
/removegame - Remove assigned game from chat";
                    }
                }
                else {
                    if (game != null) {
                        usage = @"CiviBotti:
/help - lolapua";
                    }
                    else {
                        usage = @"CiviBotti:
/help - lolapua
/order - display order of players";
                    }
                }
            }

            Bot.SendText(message.Chat.Id, usage);
        }

        private static void RegisterGame(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "Registering can only be created in private chat!");
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                Bot.SendText(message.Chat.Id, "Please provide authKey '/register authkey'!");
                return;
            }

            if (UserData.CheckDatabase(message.From.Id)) {
                Bot.SendText(message.Chat.Id, "You are already registered!");
                return;
            }

            var steamId = GetPlayerIdFromAuthkey(args[1]);
            if (steamId == "null") {
                Bot.SendText(message.Chat.Id, "Authkey you provided was incorrect!");
                return;
            }


            var newUser = UserData.NewUser(message.From.Id, steamId, args[1]);
            newUser.InsertDatabase(false);
            foreach (var game in Games) {
                foreach (var player in game.Players) {
                    if (player.SteamId == steamId) {
                        player.User = newUser;
                    }
                }
            }

            Bot.SendText(message.Chat.Id, "Registered with steamid " + steamId);
        }

        private static void ListSubs(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "Subs should be done in private chat");
                return;
            }

            var callerUser = UserData.Get(message.From.Id);

            if (callerUser == null) {
                Bot.SendText(message.Chat.Id, "You need to be registered to use this '/register authkey'!");
                return;
            }

            var returnString = "";
            foreach (var game in Games) {
                if (!game.Players.Exists(_ => _.User == callerUser)) continue;

                returnString += $"{game.Name}:\n";

                if (callerUser.Subs == null || callerUser.Subs.Count == 0 ||
                    !callerUser.Subs.Exists(_ => _.Game.GameId == game.GameId)) {
                    returnString += " none\n";
                    continue;
                }

                returnString = callerUser.Subs.FindAll(_ => _.Game.GameId == game.GameId).Aggregate(returnString,
                    (current, sub) =>
                        current +
                        $" -{Bot.GetChat(sub.SubId).Username} for {(sub.Times == 0 ? "unlimited" : sub.Times.ToString())} times\n");
            }

            if (returnString == string.Empty) {
                returnString = "You are not in any games";
            }

            Bot.SendText(message.Chat.Id, returnString);
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

                var hourGroup = Regex.Match(div.InnerHtml, "(\\d+) hour");
                var hour = 0;
                if (hourGroup.Success) {
                    int.TryParse(hourGroup.Groups[1].Value, out hour);
                }

                var minuteGroup = Regex.Match(div.InnerHtml, "(\\d+) minute");
                var minute = 0;
                if (minuteGroup.Success) {
                    int.TryParse(minuteGroup.Groups[1].Value, out minute);
                }

                return new TimeSpan(hour, minute, 0);
            }

            return null;
        }

        private static void PollTurn(GameData game) {
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

                var currentPlayerId = (string)current["UserId"];
                game.TurnStarted = DateTime.Parse(current["Started"].ToString());

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
                        game.TurnId = current["TurnId"].ToString();
                        game.UpdateCurrent();

                        player.SteamName = GetSteamUserName(player.SteamId);

                        player.User = UserData.GetBySteamId(player.SteamId);

                        if (player.User != null) {
                            player.TgName = Bot.GetChat(player.User.Id)?.Username;
                        }

                        break;
                    }

                    foreach (var chat in game.Chats) {
                        Console.WriteLine(chat);
                        Bot.SendText(chat,
                            game.CurrentPlayer != null
                                ? $"It's now your turn {game.CurrentPlayer.Nametag}!"
                                : "It's now your turn waitwhatthishsouldntbehappening?!");
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
                        if (!(game.TurnStarted + turnTimer.Value < DateTime.UtcNow)) return;
                        game.TurntimerNotified = true;
                        game.UpdateCurrent();
                        foreach (var chat in game.Chats) {
                            Bot.SendText(chat, $"Turn timer kärsii {game.CurrentPlayer.Nametag}");
                        }
                    }
                    else {
                        if (game.CurrentPlayer.NextEta >= DateTime.Now) {
                            return;
                        }

                        game.CurrentPlayer.NextEta = DateTime.MinValue;
                        game.CurrentPlayer.UpdateDatabase();
                        foreach (var chat in game.Chats) {
                            Bot.SendText(chat, $"Aikamääreistä pidetään kiinni {game.CurrentPlayer.Nametag}");
                        }
                    }

                    if (game.TurntimerNotified) {
                        string message;
                        var rnd = new Random();
                        if (DateTime.UtcNow.Hour == 7) {
                            switch (rnd.Next(0, 8)) {
                                case 0:
                                    message = $"Uusi päivä, uusi vuoro {game.CurrentPlayer.Nametag}";
                                    break;
                                case 1:
                                    message = $"Linnut laulaa ja vuorot tehää {game.CurrentPlayer.Nametag}";
                                    break;
                                case 2:
                                    message = $"Kahvit ja vuorot tulille {game.CurrentPlayer.Nametag}";
                                    break;
                                case 3:
                                    message = $"Ylös ulos ja civille {game.CurrentPlayer.Nametag}";
                                    break;
                                case 4:
                                    message = $"Welcome back commander {game.CurrentPlayer.Nametag}";
                                    break;
                                case 5:
                                    message = $"Help us {game.CurrentPlayer.Nametag}, your our only hope";
                                    break;
                                case 6:
                                    message = $"Nukuitko hyvin hyvin {game.CurrentPlayer.Nametag}?";
                                    break;
                                case 7:
                                    message = $"Aikanen vuoro kaupungin nappaa {game.CurrentPlayer.Nametag}";
                                    break;
                                default:
                                    message = $"Civivuorossa herätyys {game.CurrentPlayer.Nametag}!";
                                    break;
                            }

                            foreach (var chat in game.Chats) {
                                Bot.SendText(chat, message);
                            }
                        }
                        else if (DateTime.UtcNow.Hour == 17) {
                            switch (rnd.Next(0, 8)) {
                                case 0:
                                    message = $"Muista pestä hampaat ja tehdä vuoro {game.CurrentPlayer.Nametag}";
                                    break;
                                case 1:
                                    message = $"Älä unohda vuoroasi {game.CurrentPlayer.Nametag}";
                                    break;
                                case 2:
                                    message = $"Just one more turn {game.CurrentPlayer.Nametag}";
                                    break;
                                case 3:
                                    message = $"All your turn are belong to {game.CurrentPlayer.Nametag}";
                                    break;
                                case 4:
                                    message = $"It looks like you were trying to sleep {game.CurrentPlayer.Nametag}";
                                    break;
                                case 5:
                                    message =
                                        $"Tee vuoro ja nukkumaan {game.CurrentPlayer.Nametag}. Muuta neuvoa ei tule";
                                    break;
                                case 6:
                                    message = $"Aina voi laittaa lomatilan päälle {game.CurrentPlayer.Nametag}";
                                    break;
                                case 7:
                                    message = $"Älä anna yöunien pilataa civiä {game.CurrentPlayer.Nametag}";
                                    break;
                                default:
                                    message = $"Etkai vai ollut menossa nukkumaan {game.CurrentPlayer.Nametag}?";
                                    break;
                            }

                            foreach (var chat in game.Chats) {
                                Bot.SendText(chat, message);
                            }
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

            return json["Games"].FirstOrDefault(item => (int)item["GameId"] == game.GameId);
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

        public static Dictionary<string, string> GetSteamUserNames(List<string> steamid) {
            var url =
                $"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=E2C1F453B82A8F42092118E4B3F55037&steamids={string.Join(",", steamid.Distinct())}";

            var request = HttpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(html);
            var players = json["response"]["players"].ToArray();

            var dic = players.ToDictionary(player => player["steamid"].ToString(),
                player => player["personaname"].ToString());

            return dic;
        }
    }
}