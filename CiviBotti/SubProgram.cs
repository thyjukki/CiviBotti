using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using File = Telegram.Bot.Types.File;
using Message = Telegram.Bot.Types.Message;
using Timer = System.Timers.Timer;

namespace CiviBotti
{
    using System.Text;
    using HtmlAgilityPack;

    public class SubProgram
    {
        public static List<GameData> Games { get; private set; } = new();

        public static Database Database { get; private set; }

        public static TelegramBot Bot { get; private set; }
        private static IConfigurationRoot Configs { get; set; } = null!;

        private static readonly HttpClient HttpInstance = new ();

        public SubProgram(IConfigurationRoot configs, Database database, TelegramBot bot) {
            Configs = configs;
            Database = database;
            Bot = bot;
        }

        
        
        public static string GmrUrl { get; private set; } = "";

        public async Task RunAsync() {
            GmrUrl = Configs["GMR_URL"];
            Games = GameData.GetAllGames();
            foreach (var game in Games) {
                game.GetGameData();
            }

            var players = (from game in Games from player in game.Players select player.SteamId).ToList();
            var playerSteamNames = GetSteamUserNames(players);

            await InitializePlayers(playerSteamNames);

            Bot.StartReceiving();
            var aTimer = new Timer(30000);
            aTimer.Elapsed += OnTick;
            aTimer.Enabled = true;
            aTimer.Start();
            OnTick(null, null);
            while (true) {
                var msg = Console.ReadLine();

                if (msg == "quit" || msg == "exit") {
                    break;
                }
            }

            Bot.StopReceiving();
        }

        private static async Task InitializePlayers(IReadOnlyDictionary<string, string> playerSteamNames) {
            foreach (var game in Games) {
                Console.WriteLine($"{game} {game.Owner}");
                Console.WriteLine(" chats:");
                foreach (var chat in game.Chats) {
                    Console.WriteLine("  -" + chat);
                }

                Console.WriteLine(" players:");
                foreach (var player in game.Players) {
                    if (!playerSteamNames.TryGetValue(player.SteamId, out var steamName)) {
                        Console.WriteLine($"  -{player} ({player.TurnOrder}) {player.User} Error getting steam name");
                        continue;
                    }
                    player.SteamName = steamName;


                    if (player.User != null) {
                        var user = await Bot.GetChat(player.User.Id);
                        player.TgName = user.Username;
                    }

                    Console.WriteLine($"  -{player} ({player.TurnOrder}) {player.User}");
                }

                Console.WriteLine("\n");
            }
        }


        private static async void OnTick(object sender, ElapsedEventArgs e) {
            foreach (var game in Games) {
                await PollTurn(game);
            }
        }

        private static GameData GetGameFromChat(long chatId) {
            return (from game in Games from chat in game.Chats where chat == chatId select game).FirstOrDefault();
        }

        public static bool IsCommand(string a, string b) {
            return string.Equals(a, b, StringComparison.InvariantCultureIgnoreCase) ||
                   string.Equals(a, $"{b}@civi_gmr_bot", StringComparison.InvariantCultureIgnoreCase);
        }

        private static async Task NewGame(Message message, Chat chat) {
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
            newGame.Name = (string)data["Name"];
            var current = data["CurrentTurn"];

            if (current == null) {
                return;
            }

            var currentPlayerId = (string)current["UserId"];
            foreach (var player in data["Players"]) {
                var playerData = new PlayerData(gameId, player["UserId"].Value<string>(), player["TurnOrder"].Value<int>(), DateTime.MinValue);

                playerData.User = UserData.GetBySteamId(playerData.SteamId);
                playerData.SteamName = GetSteamUserName(playerData.SteamId);

                if (playerData.User != null) {
                    var user = await Bot.GetChat(playerData.User.Id);
                    playerData.TgName = user.Username;
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

        public static async Task ParseCommand(string cmd, Message message) {
            if (message.Type != MessageType.TextMessage) {
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
                    Turntimers(chat, true);
                    break;
                case Command.Turntimers:
                    Turntimers(chat, false);
                    break;
                case Command.Listsubs:
                    await ListSubs(message, chat);
                    break;
                case Command.Addsub:
                    AddSub(message, chat);
                    break;
                case Command.Removesub:
                    await RemoveSub(message, chat);
                    break;
                case Command.Doturn:
                    await DoTurn(message, chat);
                    break;
                case Command.Submitturn:
                    await SubmitTurn(message, chat);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cmd), $"Command {nameof(command)} not in command list");
            }
        }

        


        private static async void OnSubmitTurnGetUploadCallback(Message originalMsg, Message callbackMsg, UserDataStorage callerUser,
            PlayerDataStorage selectedPlayer) {
            if (callbackMsg.Type != MessageType.DocumentMessage) {
                Bot.AddReplyGet(originalMsg.From.Id, originalMsg.Chat.Id, message => OnSubmitTurnGetUploadCallback(originalMsg, message, callerUser, selectedPlayer));
                Bot.SendText(originalMsg.Chat.Id, "Respond by uploading a file");
                return;
            }

            Bot.SendText(originalMsg.Chat.Id, "Submitting turn");
            Bot.SetChatAction(originalMsg.Chat.Id, ChatAction.UploadDocument);
            await UploadSave(callerUser, selectedPlayer, callbackMsg.Document);
        }
        
        private static void OnSubmitTurnGetSelectedCallback(Message originalMsg,Message callbackMsg, List<PlayerDataStorage> otherPlayerStorage, UserDataStorage callerUser) {
            foreach (var game in Games) {
                var user = game.CurrentPlayer.User;
                if (user == null) {
                    continue;
                }

                if (!user.Subs.Exists(sub => sub.SubId == callerUser.UserData.Id) && user.SteamId != callerUser.UserData.SteamId) {
                    continue;
                }
                
                var selectedPlayer = otherPlayerStorage.Find(playerStorage => $"{playerStorage.Username}@{playerStorage.Game.Name}" == callbackMsg.Text);
                if (selectedPlayer.Username == null) {
                    continue;
                }

                Bot.AddReplyGet(originalMsg.From.Id, originalMsg.Chat.Id, message => OnSubmitTurnGetUploadCallback(originalMsg, message, callerUser, selectedPlayer));
                Bot.SendText(originalMsg.Chat.Id, "Upload the file");
                return;
            }
        }

        struct UserDataStorage
        {
            public string Username;
            public UserData UserData;
        }
        struct PlayerDataStorage
        {
            public string Username;
            public UserData UserData;
            public GameData Game;
        }
        private static async Task SubmitTurn(Message message, Chat chat) {
            if (chat.Type != ChatType.Private) {
                Bot.SendText(message.Chat.Id, "This can only be done in private!");
                return;
            }

            Bot.SetChatAction(message.Chat.Id, ChatAction.Typing);

            var callerUserData = UserData.Get(message.From.Id);

            if (callerUserData == null) {
                Bot.SendText(chat, "You are not registered in any games");
                return;
            }

            var callerUser = new UserDataStorage {
                UserData = callerUserData
            };
            callerUser.Username = (await Bot.GetChat(callerUser.UserData.Id)).Username;

            var otherPlayers = new List<PlayerDataStorage>();
            var keyboardButtons = new List<KeyboardButton>();
            foreach (var game in Games) {
                var otherUser = game.CurrentPlayer.User;
                if (otherUser == null) {
                    continue;
                }

                if (otherUser.Subs.Exists(sub => sub.SubId == callerUser.UserData.Id) || otherUser.SteamId == callerUser.UserData.SteamId) {
                    var otherUserStorage = new PlayerDataStorage {
                        UserData = otherUser,
                        Game = game,
                        Username = (await Bot.GetChat(otherUser.Id)).Username
                    };
                    keyboardButtons.Add(new KeyboardButton($"{otherUserStorage.Username}@{game.Name}"));
                    otherPlayers.Add(otherUserStorage);
                }
                
            }

            if (keyboardButtons.Count == 0) {
                Bot.SendText(message.Chat.Id, "You can not submit anyones turn at the moment");
                return;
            }

            keyboardButtons.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(keyboardButtons.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            Bot.AddReplyGet(message.From.Id, chat.Id, callbackMsg => OnSubmitTurnGetSelectedCallback(message, callbackMsg, otherPlayers, callerUser));
            Bot.SendText(message.Chat.Id, "Chose game to submit save to", forceReply);
        }

        private static async void OnDoTurnGetSelectedCallback(Message originalMsg, Message callbackMessage, UserData callerUser) {
            foreach (var game in Games) {
                var user = game.CurrentPlayer?.User;
                if (user == null) {
                    continue;
                }

                if (!user.Subs.Exists(sub => sub.SubId == callerUser.Id) && user.SteamId != callerUser.SteamId) {
                    continue;
                }
                
                var username = (await Bot.GetChat(user.Id)).Username;
                if ($"{username}@{game.Name}" != callbackMessage.Text) {
                    continue;
                }

                Bot.SetChatAction(originalMsg.Chat.Id, ChatAction.UploadDocument);
                await DownloadSave(user, username, callerUser, game);
                return;
            }
        }
        private static async Task DoTurn(Message message, Chat chat) {
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
            

            var keyboardButtons = new List<KeyboardButton>();
            foreach (var game in Games) {
                var user = game.CurrentPlayer.User;
                if (user == null) {
                    continue;
                }

                if (user.Subs.Exists(sub => sub.SubId == callerUser.Id) || user.SteamId == callerUser.SteamId) {
                    var subUser = await Bot.GetChat(user.Id);
                    keyboardButtons.Add(new KeyboardButton($"{subUser.Username}@{game.Name}"));
                }
                
            }

            if (keyboardButtons.Count == 0) {
                Bot.SendText(message.Chat.Id, "You can not play anyone's turn at the moment", new ReplyKeyboardRemove());
                return;
            }

            keyboardButtons.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(keyboardButtons.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            Bot.AddReplyGet(message.From.Id, chat.Id,  callbackMsg => OnDoTurnGetSelectedCallback(message, callbackMsg, callerUser));
            Bot.SendText(message.Chat.Id, "Chose game to sub", forceReply);
        }

        private static async Task DownloadSave(UserData user, string userName, UserData callerUser, GameData game) {
            var response = await HttpInstance.GetAsync($"{GmrUrl}api/Diplomacy/GetLatestSaveFileBytes?authKey={user.AuthKey}&gameId={game.GameId}");
            response.EnsureSuccessStatusCode();
 
            if (!response.IsSuccessStatusCode) return;

            
            var stream = response.Content.ReadAsStreamAsync().Result;
            var file = new FileToSend($"(GMR) {userName} {game.Name}.Civ5Save", stream);
            Bot.SendFile(callerUser.Id, file);
            Bot.SendText(callerUser.Id, "Use /submitturn command to submit turn",
                new ReplyKeyboardRemove());
            Bot.SendText(user.Id, $"Sub downloaded your turn");
        }

        private static async Task UploadSave(UserDataStorage callerUser, PlayerDataStorage selectedPlayer, File doc) {
            var uri = new Uri(GmrUrl);
            var httpClient = new HttpClient();
            httpClient.BaseAddress = uri;
            httpClient.DefaultRequestHeaders.ExpectContinue = false;
            var stream = Bot.GetFileAsStream(doc);
            var form =
                new MultipartFormDataContent(
                    $"Upload----{(object)DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)}") {
                    { new StringContent(selectedPlayer.Game.TurnId), "turnId" },
                    { new StringContent("False"), "isCompressed" },
                    { new StringContent(selectedPlayer.UserData.AuthKey), "authKey" },
                    { new StreamContent(stream), "saveFileUpload", $"{selectedPlayer.Game.TurnId}.Civ5Save" }
                };

            var response = await httpClient
                .PostAsync(
                    "Game/UploadSaveClient",
                    form);
                
            if (!response.IsSuccessStatusCode) {
                return;
            }

            var json = JObject.Parse(response.Content.ReadAsStringAsync().Result);
            if (json["ResultType"].ToString() == "1") {
                Bot.SendText(callerUser.UserData.Id, "Turn submited");
                Bot.SendText(selectedPlayer.UserData.Id, $"{callerUser.Username} submited your turn");
            }
            else {
                Bot.SendText(callerUser.UserData.Id, $"Failed to submit turn {json["ResultType"]}");
            }
        }

        private static async Task RemoveSub(Message message, Chat chat) {
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

            
            var keyboardButtons = new List<KeyboardButton>();
            foreach (var sub in callerUser.Subs) {
                var subUser = await Bot.GetChat(sub.SubId);
                keyboardButtons.Add(new KeyboardButton($"{subUser.Username}@{sub.Game.Name}"));
            }

            keyboardButtons.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(keyboardButtons.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };
            

            async void OnGetSelectedCallback(Message msg) {
                var index = keyboardButtons.FindIndex(button => button.Text == msg.Text);
                if (index == keyboardButtons.Count - 1) {
                    return;
                }
                var sub = callerUser.Subs[index];

                var game = sub.Game;
                var user = UserData.Get(sub.SubId);

                callerUser.Subs.Remove(sub);
                sub.RemoveSub();
                if (user == null) {
                    Console.WriteLine("GetSelectedCallback weird case");
                    Bot.SendText(chat,"Weird case, game or user null?");
                    return;
                }

                var userName = (await Bot.GetChat(user.Id)).Username;
                var callerUserName = (await Bot.GetChat(callerUser.Id)).Username;
                Bot.SendText(chat, $"Removed {userName} subbing from {game.Name}", new ReplyKeyboardRemove());
                Bot.SendText(user.Id, $"{callerUserName} revoked your sub rights from {game.Name}");
            }

            Bot.AddReplyGet(message.From.Id, chat.Id, OnGetSelectedCallback);
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

            GameData? selectedGame;
            UserData? selectedUser;

            void GetGameCallback(Message msg) {
                selectedGame = Games.Find(gameData => gameData.Name == msg.Text);

                if (selectedGame == null) {
                    return;
                }

                var users = selectedGame.Players.Where(player => player.User != null).Select(player => new KeyboardButton(player.Name))
                    .ToArray();

                var userReply = new ReplyKeyboardMarkup(users) {
                    OneTimeKeyboard = true,
                    Selective = true
                };
                Bot.AddReplyGet(msg.From.Id, chat.Id, OnGetUserCallback);
                Bot.SendText(msg.Chat.Id, "Chose the user", userReply);
            }

            void OnGetUserCallback(Message msg) {
                selectedUser = selectedGame.Players.Find(playerData => playerData.User != null && playerData.Name == msg.Text).User;

                Bot.AddReplyGet(msg.From.Id, chat.Id, OnGetTimesCallback);
                Bot.SendText(msg.Chat.Id, "How many times can he play your turns in this game? (0 for unlimited)",
                    new ReplyKeyboardRemove());
            }

            async void OnGetTimesCallback(Message msg) {
                if (!int.TryParse(msg.Text, out var selectedTimes) || selectedTimes < 0) {
                    Bot.AddReplyGet(msg.From.Id, chat.Id, OnGetTimesCallback);
                    Bot.SendText(msg.Chat.Id, "Please provide a positive integer", new ReplyKeyboardRemove());
                    return;
                }

                await SetSub(selectedGame, selectedUser, selectedTimes);
            }


            async Task SetSub(GameData game, UserData user, int times) {
                var sub = new SubData(callerUser.Id, user.Id, times, game);
                sub.InsertDatabase();
                user.Subs.Add(sub);
                
                var userName = (await Bot.GetChat(user.Id)).Username;
                var callerUserName = (await Bot.GetChat(callerUser.Id)).Username;
                
                Bot.SendText(message.Chat.Id, $"Added {userName} as sub in {game.Name}", new ReplyKeyboardRemove());
                Bot.SendText(user.Id, $"{callerUserName} has given you rights to do his turn in {game.Name}");
            }

            var games = Games.Where(_ => _.Players.Exists(player => player.User != null && player.User.SteamId == callerUser.SteamId))
                .Select(game => new KeyboardButton(game.Name)).ToList();


            var forceReply = new ReplyKeyboardMarkup(games.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            Bot.AddReplyGet(message.From.Id, chat.Id, GetGameCallback);
            Bot.SendText(message.Chat.Id, "Chose the game", forceReply);
        }

        private static void PlayerTurntimeFromInnerHtml(PlayerData player, StringBuilder stringBuilder, string innerHtml) {
            var idGroup = Regex.Match(innerHtml, @"/Community#\s*([\d+]*)", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            if (idGroup.Success) {
                var id = idGroup.Groups[1].Value;

                if (!string.Equals(id, player.SteamId, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
            }

            var hourGroup = Regex.Match(innerHtml, "(\\d+) hour", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            var minuteGroup = Regex.Match(innerHtml, "(\\d+) minute", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            var dayGroup = Regex.Match(innerHtml, "(\\d+) day", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            
            stringBuilder.Append($"{player.Name} turntimer");
            if (dayGroup.Success && int.TryParse(dayGroup.Groups[1].Value, out var day) && day > 0) {
                stringBuilder.Append($" {day}");
                stringBuilder.Append(day == 1 ? " päivä" : " päivää");
            }
            
            if (hourGroup.Success && int.TryParse(hourGroup.Groups[1].Value, out var hour) && hour > 0) {
                stringBuilder.Append($" {hour}");
                stringBuilder.Append(hour == 1 ? " tunti" : " tuntia");
            }

            if (minuteGroup.Success && int.TryParse(minuteGroup.Groups[1].Value, out var minute) && minute > 0) {
                stringBuilder.Append($" {minute}");
                stringBuilder.Append(minute == 1 ? " minuutti" : " minuuttia");
            }
                
            stringBuilder.Append('\n');
        }

        private static void Turntimers(Chat chat, bool onlyCurrent) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);
            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatId => chatId == chat.Id));
            if (selectedGame == null) {
                Bot.SendText(chat, "No game added to this chat");
                return;
            }

            var url = $"{GmrUrl}Game/Details?id={selectedGame.GameId}";
            var response = HttpInstance.PostAsync(url, null).Result;
            if (!response.IsSuccessStatusCode) {
                Bot.SendText(chat, "Problem connecting to gmr service");
                return;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(response.Content.ReadAsStringAsync().Result);

            var divs = doc.DocumentNode.SelectNodes("//div[@class=\"game-player average\"]");

            var stringBuilder = new StringBuilder();
            foreach (var player in selectedGame.Players) {
                if (selectedGame.CurrentPlayer.SteamId != player.SteamId && onlyCurrent) {
                    continue;
                }
                foreach (var innerHtml in divs.Select(div => div.InnerHtml)) {
                    PlayerTurntimeFromInnerHtml(player, stringBuilder, innerHtml);
                }
            }
            
            Bot.SendText(chat, stringBuilder.ToString());
        }

        private static void AddGame(Message message, Chat chat) {
            Bot.SetChatAction(chat.Id, ChatAction.Typing);

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

            var orders = selectedGame.Players.OrderBy(x => x.TurnOrder).ToList();
            var result = new StringBuilder();
            orders.ForEach(player => result.Append($"\n{player.Name}"));

            Bot.SendText(message.Chat.Id, $"Order is:{result}");
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

            Bot.SendText(message.Chat.Id, $"Next player is: {player.NameTag}");
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

            var selectedGame = Games.Find(game => game.Chats.Any(chatId => chatId == chat.Id));

            if (selectedGame == null) {
                Bot.SendText(message.Chat.Id, "No game added to this group!");
                return;
            }

            if (selectedGame.CurrentPlayer?.User == null) {
                Bot.SendText(message.Chat.Id, "Current player has not registered");
                return;
            }

            if (selectedGame.CurrentPlayer.User.Id != message.From.Id) {
                GetOtherPlayerEta(message, selectedGame, selectedGame.CurrentPlayer);
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
                if (!ParseTime(message, args, out hour, ref minute, ref day)) {
                    return;
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

        private static bool ParseTime(Message message, IReadOnlyList<string> args, out int hour, ref int minute, ref int day) {
            var hoursMins = args[1].Split(':');
            if (!int.TryParse(hoursMins[0], out hour)) {
                Bot.SendText(message.Chat.Id,
                    "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                return false;
            }

            if (hoursMins.Length <= 1) {
                return true;
            }

            if (!int.TryParse(hoursMins[1], out minute)) {
                Bot.SendText(message.Chat.Id,
                    "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                return false;
            }

            if (hoursMins.Length <= 2 || int.TryParse(hoursMins[2], out day)) {
                return true;
            }

            Bot.SendText(message.Chat.Id,
                "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
            return false;

        }

        private static void GetOtherPlayerEta(Message message, GameData selectedGame, PlayerData player) {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append($"{player.NameTag} ");
            TimeSpan diff;

            if (player.NextEta < DateTime.Now) {
                var turnTimer = GetTurntimer(selectedGame, player);
                if (!turnTimer.HasValue) {
                    Bot.SendText(message.Chat.Id, $"Uusi turntimer tulossa! {player.NameTag}");
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

            Bot.SendText(message.Chat.Id, stringBuilder.ToString());
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

        private static async Task ListSubs(Message message, Chat chat) {
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

            var stringBuilder = new StringBuilder();
            foreach (var game in Games.Where(game => game.Players.Exists(userData => userData.User.SteamId == callerUser.SteamId))) {
                stringBuilder.Append($"{game.Name}:\n");

                if (callerUser.Subs == null || callerUser.Subs.Count == 0 ||
                    !callerUser.Subs.Exists(_ => _.Game.GameId == game.GameId)) {
                    stringBuilder.Append(" none\n");
                    continue;
                }

                foreach (var sub in callerUser.Subs.FindAll(subData => subData.Game.GameId == game.GameId)) {
                    var subUser = await Bot.GetChat(sub.SubId);
                    stringBuilder.Append($" -{subUser.Username} for {(sub.Times == 0 ? "unlimited" : sub.Times.ToString())} times\n");
                }
            }

            if (stringBuilder.Length == 0) {
                stringBuilder.Append("You are not in any games");
            }

            Bot.SendText(message.Chat.Id, stringBuilder.ToString());
        }


        private static TimeSpan? GetTurntimer(GameData selectedGame, PlayerData player) {
            var url = $"{GmrUrl}Game/Details?id={selectedGame.GameId}";
            var response = HttpInstance.PostAsync(url, null).Result;
            if (!response.IsSuccessStatusCode) {
                return null;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(response.Content.ReadAsStringAsync().Result);

            var divs = doc.DocumentNode.SelectNodes("//div[@class=\"game-player average\"]");

            foreach (var innerHtml in divs.Select(div => div.InnerHtml)) {
                var idGroup = Regex.Match(innerHtml, "/Community#\\s*([\\d+]*)", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                if (idGroup.Success) {
                    var id = idGroup.Groups[1].Value;

                    if (!string.Equals(id, player.SteamId, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                }

                var hourGroup = Regex.Match(innerHtml, "(\\d+) hour", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                var hour = 0;
                if (hourGroup.Success) {
                    int.TryParse(hourGroup.Groups[1].Value, out hour);
                }

                var minuteGroup = Regex.Match(innerHtml, "(\\d+) minute", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                var minute = 0;
                if (minuteGroup.Success) {
                    int.TryParse(minuteGroup.Groups[1].Value, out minute);
                }

                return new TimeSpan(hour, minute, 0);
            }

            return null;
        }

        private static async Task PollTurn(GameData game) {
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
                    ChangeTurns(game, currentPlayerId, current);
                }
                else {
                    CheckTurnNotifications(game);
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
        }

        private static void CheckTurnNotifications(GameData game) {
            if (game.CurrentPlayer == null) {
                return;
            }

            if (game.CurrentPlayer.NextEta == DateTime.MinValue) {
                if (game.TurntimerNotified) {
                    DailyNotify(game);
                    return;
                }
                var turnTimer = GetTurntimer(game, game.CurrentPlayer);
                if (!turnTimer.HasValue) return;
                if (game.TurnStarted + turnTimer.Value >= DateTime.UtcNow) return;
                game.TurntimerNotified = true;
                game.UpdateCurrent();
                foreach (var chat in game.Chats) {
                    Bot.SendText(chat, $"Turn timer kärsii {game.CurrentPlayer.NameTag}");
                }
            }
            else {
                if (game.CurrentPlayer.NextEta >= DateTime.Now) {
                    return;
                }

                game.CurrentPlayer.NextEta = DateTime.MinValue;
                game.CurrentPlayer.UpdateDatabase();
                foreach (var chat in game.Chats) {
                    Bot.SendText(chat, $"Aikamääreistä pidetään kiinni {game.CurrentPlayer.NameTag}");
                }
            }
        }

        private static async Task ChangeTurns(GameData game, string currentPlayerId, JToken current) {
            var player = game.Players.Find(playerData => playerData.SteamId == currentPlayerId);
            
            if (player == null) {
                throw new ArgumentNullException($"Player {currentPlayerId} not found in database!");
            }
            
            game.CurrentPlayer.UpdateDatabase();
            game.CurrentPlayer = player;
            game.TurntimerNotified = false;
            game.TurnStarted = DateTime.Now;
            game.TurnId = current["TurnId"].ToString();
            game.UpdateCurrent();

            player.SteamName = GetSteamUserName(player.SteamId);

            player.User = UserData.GetBySteamId(player.SteamId);

            if (player.User != null) {
                var tgUser = await Bot.GetChat(player.User.Id);
                player.TgName = tgUser.Username;
            }

            foreach (var chat in game.Chats) {
                Console.WriteLine(chat);
                Bot.SendText(chat,
                    game.CurrentPlayer != null
                        ? $"It's now your turn {game.CurrentPlayer.NameTag}!"
                        : "It's now your turn waitwhatthishsouldntbehappening?!");
            }
        }

        private static void DailyNotify(GameData game) {
            if (!game.EnableDailyNotified) {
                return;
            }
            
            string message;
            var rnd = new Random();
            switch (DateTime.UtcNow.Hour) {
                case 7: {
                    message = rnd.Next(0, 8) switch {
                        0 => $"Uusi päivä, uusi vuoro {game.CurrentPlayer.NameTag}",
                        1 => $"Linnut laulaa ja vuorot tehää {game.CurrentPlayer.NameTag}",
                        2 => $"Kahvit ja vuorot tulille {game.CurrentPlayer.NameTag}",
                        3 => $"Ylös ulos ja civille {game.CurrentPlayer.NameTag}",
                        4 => $"Welcome back commander {game.CurrentPlayer.NameTag}",
                        5 => $"Help us {game.CurrentPlayer.NameTag}, your our only hope",
                        6 => $"Nukuitko hyvin hyvin {game.CurrentPlayer.NameTag}?",
                        7 => $"Aikanen vuoro kaupungin nappaa {game.CurrentPlayer.NameTag}",
                        _ => $"Civivuorossa herätyys {game.CurrentPlayer.NameTag}!"
                    };

                    break;
                }
                case 17: {
                    message = rnd.Next(0, 8) switch {
                        0 => $"Muista pestä hampaat ja tehdä vuoro {game.CurrentPlayer.NameTag}",
                        1 => $"Älä unohda vuoroasi {game.CurrentPlayer.NameTag}",
                        2 => $"Just one more turn {game.CurrentPlayer.NameTag}",
                        3 => $"All your turn are belong to {game.CurrentPlayer.NameTag}",
                        4 => $"It looks like you were trying to sleep {game.CurrentPlayer.NameTag}",
                        5 => $"Tee vuoro ja nukkumaan {game.CurrentPlayer.NameTag}. Muuta neuvoa ei tule",
                        6 => $"Aina voi laittaa lomatilan päälle {game.CurrentPlayer.NameTag}",
                        7 => $"Älä anna yöunien pilataa civiä {game.CurrentPlayer.NameTag}",
                        _ => $"Etkai vai ollut menossa nukkumaan {game.CurrentPlayer.NameTag}?"
                    };

                    break;
                }
                default:
                    if (game.DailyNotified) {
                        game.DailyNotified = false;
                        game.UpdateCurrent();
                    }
                    return;
            }

            if (game.DailyNotified) {
                return;
            }
            game.DailyNotified = true;
            game.UpdateCurrent();
            foreach (var chat in game.Chats) {
                Bot.SendText(chat, message);
            }
        }

        public static JToken GetGameData(GameData game) {
            var url =
                $"{GmrUrl}api/Diplomacy/GetGamesAndPlayers?playerIDText={game.Owner.SteamId}&authKey={game.Owner.AuthKey}";


            var request = HttpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(html);

            return json["Games"].FirstOrDefault(item => (int)item["GameId"] == game.GameId);
        }

        private static string GetPlayerIdFromAuthkey(string authkey) {
            var url = $"{GmrUrl}api/Diplomacy/AuthenticateUser?authKey={authkey}";

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