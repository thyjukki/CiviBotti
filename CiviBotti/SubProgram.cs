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
using Message = Telegram.Bot.Types.Message;
using Timer = System.Timers.Timer;

namespace CiviBotti
{
    using System.Text;
    using HtmlAgilityPack;
    using Telegram.Bot.Types.InputFiles;

    public class SubProgram
    {
        public static List<GameData> Games { get; private set; } = new();

        private readonly Database _database;

        private readonly TelegramBot _bot;
        private readonly IConfigurationRoot _configuration;
        private readonly string _gmrUrl;

        private readonly HttpClient _httpInstance;

        public SubProgram(IConfigurationRoot configuration, Database database, TelegramBot bot) {
            _configuration = configuration;
            _database = database;
            _bot = bot;
            _bot.CommandReceived += OnCommandReceived;
            _gmrUrl = _configuration.GetValue<string>("GMR_URL");

            _httpInstance = new HttpClient();
        }



        public async Task RunAsync() {
            Games = GameData.GetAllGames(_database);
            foreach (var game in Games) {
                game.GetGameData(_database);
            }

            var players = (from game in Games from player in game.Players select player.SteamId).ToList();
            var playerSteamNames = GetSteamUserNames(players);

            await InitializePlayers(playerSteamNames);

            _bot.Client.StartReceiving();
            var aTimer = new Timer(30000);
            aTimer.Elapsed += OnTick;
            aTimer.Enabled = true;
            aTimer.Start();
            foreach (var game in Games) {
                await PollTurn(game);
            }
            while (true) {
                var msg = Console.ReadLine();

                if (msg is "quit" or "exit") {
                    break;
                }
            }

            _bot.Client.StopReceiving();
        }

        private async Task InitializePlayers(IReadOnlyDictionary<string, string> playerSteamNames) {
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
                        var user = await _bot.GetChat(player.User.Id);
                        player.TgName = user.Username;
                    }

                    Console.WriteLine($"  -{player} ({player.TurnOrder}) {player.User}");
                }

                Console.WriteLine("\n");
            }
        }


        private async void OnTick(object? sender, ElapsedEventArgs e) {
            foreach (var game in Games) {
                await PollTurn(game);
            }
        }

        private static GameData? GetGameFromChat(long chatId) {
            return (from game in Games from chat in game.Chats where chat == chatId select game).FirstOrDefault();
        }

        public static bool IsCommand(string a, string b) {
            return string.Equals(a, b, StringComparison.InvariantCultureIgnoreCase) ||
                   string.Equals(a, $"{b}@civi_gmr_bot", StringComparison.InvariantCultureIgnoreCase);
        }

        private async Task NewGame(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "New game can only be created in private chat!");
                return;
            }

            if (!UserData.CheckDatabase(_database, message.From.Id)) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "You are need to first register!");
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Please provide gameid '/newgame gameid'!");
                return;
            }

            long.TryParse(args[1], out var gameId);
            if (gameId == 0) {
                return;
            }


            if (Games.Any(game => game.GameId == gameId)) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Game has already been created!");
                return;
            }

            var newGame = new GameData {
                Owner = UserData.Get(_database, message.From.Id),
                GameId = gameId
            };
            JToken? data;
            try {
                data = GetGameData(newGame);
            }
            catch (WebException) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Could not connect to services, please try again later!");
                return;
            }

            if (data == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Invalid gameid, or your account is not in the game!");
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

                playerData.User = UserData.GetBySteamId(_database, playerData.SteamId);
                playerData.SteamName = GetSteamUserName(playerData.SteamId);

                if (playerData.User != null) {
                    var user = await _bot.GetChat(playerData.User.Id);
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
            newGame.InsertFull(_database);
            Games.Add(newGame);
            await _bot.Client.SendTextMessageAsync(message.Chat.Id, $"Successfully created the game {newGame.Name}!");
        }

        private async void OnCommandReceived(string cmd, Message message) {
            if (message.Type != MessageType.Text) {
                return;
            }

            var chat = message.Chat;


            if (!Enum.TryParse<Command>(cmd, true, out var command)) {
                return;
            }

            Console.WriteLine($"Command {cmd}");

            switch (command) {
                case Command.Newgame:
                    await NewGame(message, chat);
                    break;
                case Command.Register:
                    await RegisterGame(message, chat);
                    break;
                case Command.Addgame:
                    await AddGame(message, chat);
                    break;
                case Command.Removegame:
                    await RemoveGame(message, chat);
                    break;
                case Command.Order:
                    await Order(message, chat);
                    break;
                case Command.Next:
                    await Next(message, chat);
                    break;
                case Command.Autocracy:
                case Command.Freedom:
                    await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Did you mean /order?");
                    break;
                case Command.Oispa:
                    await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Kaljaa?");
                    break;
                case Command.Teekari:
                    await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Press /f to pay respect to fallen commands");
                    break;
                case Command.Tee:
                    await Tee(message, chat);
                    break;
                case Command.Eta:
                    await Eta(message, chat);
                    break;
                case Command.Help:
                    await Help(message, chat);
                    break;
                case Command.Turntimer:
                    await Turntimers(chat, true);
                    break;
                case Command.Turntimers:
                    await Turntimers(chat, false);
                    break;
                case Command.Listsubs:
                    await ListSubs(message, chat);
                    break;
                case Command.Addsub:
                    await AddSub(message, chat);
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

        


        private async void OnSubmitTurnGetUploadCallback(Message originalMsg, Message callbackMsg, UserDataStorage callerUser,
            PlayerDataStorage selectedPlayer) {
            if (callbackMsg.Type != MessageType.Document) {
                _bot.AddReplyGet(originalMsg.From.Id, originalMsg.Chat.Id, message => OnSubmitTurnGetUploadCallback(originalMsg, message, callerUser, selectedPlayer));
                await _bot.Client.SendTextMessageAsync(originalMsg.Chat.Id, "Respond by uploading a file");
                return;
            }

            await _bot.Client.SendTextMessageAsync(originalMsg.Chat.Id, "Submitting turn");
            await _bot.Client.SendChatActionAsync(originalMsg.Chat.Id, ChatAction.UploadDocument);
            await UploadSave(callerUser, selectedPlayer, callbackMsg.Document);
        }
        
        private async void OnSubmitTurnGetSelectedCallback(Message originalMsg,Message callbackMsg, List<PlayerDataStorage> otherPlayerStorage, UserDataStorage callerUser) {
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

                _bot.AddReplyGet(originalMsg.From.Id, originalMsg.Chat.Id, message => OnSubmitTurnGetUploadCallback(originalMsg, message, callerUser, selectedPlayer));
                await _bot.Client.SendTextMessageAsync(originalMsg.Chat.Id, "Upload the file");
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
        private async Task SubmitTurn(Message message, Chat chat) {
            if (chat.Type != ChatType.Private) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!");
                return;
            }

            await _bot.Client.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

            var callerUserData = UserData.Get(_database, message.From.Id);

            if (callerUserData == null) {
                await _bot.Client.SendTextMessageAsync(chat, "You are not registered in any games");
                return;
            }

            var callerUser = new UserDataStorage {
                UserData = callerUserData
            };
            callerUser.Username = (await _bot.GetChat(callerUser.UserData.Id)).Username;

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
                        Username = (await _bot.GetChat(otherUser.Id)).Username
                    };
                    keyboardButtons.Add(new KeyboardButton($"{otherUserStorage.Username}@{game.Name}"));
                    otherPlayers.Add(otherUserStorage);
                }
                
            }

            if (keyboardButtons.Count == 0) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "You can not submit anyone's turn at the moment");
                return;
            }

            keyboardButtons.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(keyboardButtons.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            _bot.AddReplyGet(message.From.Id, chat.Id, callbackMsg => OnSubmitTurnGetSelectedCallback(message, callbackMsg, otherPlayers, callerUser));
            await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Chose game to submit save to", replyMarkup:forceReply);
        }

        private async void OnDoTurnGetSelectedCallback(Message originalMsg, Message callbackMessage, UserData callerUser) {
            foreach (var game in Games) {
                var user = game.CurrentPlayer.User;
                if (user == null) {
                    continue;
                }

                if (!user.Subs.Exists(sub => sub.SubId == callerUser.Id) && user.SteamId != callerUser.SteamId) {
                    continue;
                }
                
                var username = (await _bot.GetChat(user.Id)).Username;
                if ($"{username}@{game.Name}" != callbackMessage.Text) {
                    continue;
                }

                await _bot.Client.SendChatActionAsync(originalMsg.Chat.Id, ChatAction.UploadDocument);
                await DownloadSave(user, username, callerUser, game);
                return;
            }
        }
        private async Task DoTurn(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

            if (chat.Type != ChatType.Private) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!");
                return;
            }

            var callerUser = UserData.Get(_database, message.From.Id);

            if (callerUser == null) {
                await _bot.Client.SendTextMessageAsync(chat, "You are not registered in any games", replyMarkup:new ReplyKeyboardRemove());
                return;
            }
            

            var keyboardButtons = new List<KeyboardButton>();
            foreach (var game in Games) {
                var user = game.CurrentPlayer.User;
                if (user == null) {
                    continue;
                }

                if (user.Subs.Exists(sub => sub.SubId == callerUser.Id) || user.SteamId == callerUser.SteamId) {
                    var subUser = await _bot.GetChat(user.Id);
                    keyboardButtons.Add(new KeyboardButton($"{subUser.Username}@{game.Name}"));
                }
                
            }

            if (keyboardButtons.Count == 0) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "You can not play anyone's turn at the moment", replyMarkup: new ReplyKeyboardRemove());
                return;
            }

            keyboardButtons.Add(new KeyboardButton("cancel"));

            var forceReply = new ReplyKeyboardMarkup(keyboardButtons.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            _bot.AddReplyGet(message.From.Id, chat.Id,  callbackMsg => OnDoTurnGetSelectedCallback(message, callbackMsg, callerUser));
            await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Chose game to sub", replyMarkup: forceReply);
        }

        private async Task DownloadSave(UserData user, string userName, UserData callerUser, GameData game) {
            var response = await _httpInstance.GetAsync($"{_gmrUrl}api/Diplomacy/GetLatestSaveFileBytes?authKey={user.AuthKey}&gameId={game.GameId}");
            response.EnsureSuccessStatusCode();
 
            if (!response.IsSuccessStatusCode) return;

            
            var stream = response.Content.ReadAsStreamAsync().Result;
            var file = new InputOnlineFile(stream, $"(GMR) {userName} {game.Name}.Civ5Save");
            await _bot.Client.SendDocumentAsync(callerUser.Id, file);
            await _bot.Client.SendTextMessageAsync(callerUser.Id, "Use /submitturn command to submit turn",
                replyMarkup: new ReplyKeyboardRemove());
            await _bot.Client.SendTextMessageAsync(user.Id, $"Sub downloaded your turn");
        }

        private async Task UploadSave(UserDataStorage callerUser, PlayerDataStorage selectedPlayer, Document doc) {
            var uri = new Uri(_gmrUrl);
            var httpClient = new HttpClient();
            httpClient.BaseAddress = uri;
            httpClient.DefaultRequestHeaders.ExpectContinue = false;

            var fileInfo = await _bot.Client.GetFileAsync(doc.FileId);
            
            HttpResponseMessage response;
            using (var ms = new MemoryStream(fileInfo.FileSize)) 
            {  
                await _bot.Client.DownloadFileAsync(
                    filePath: fileInfo.FilePath,
                    destination: ms
                );
                var form =
                    new MultipartFormDataContent(
                        $"Upload----{(object)DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)}") {
                        { new StringContent(selectedPlayer.Game.TurnId), "turnId" },
                        { new StringContent("False"), "isCompressed" },
                        { new StringContent(selectedPlayer.UserData.AuthKey), "authKey" },
                        { new StreamContent(ms), "saveFileUpload", $"{selectedPlayer.Game.TurnId}.Civ5Save" }
                    };
                response = await httpClient.PostAsync("Game/UploadSaveClient",  form);
            }

                
            if (!response.IsSuccessStatusCode) {
                return;
            }

            var json = JObject.Parse(response.Content.ReadAsStringAsync().Result);
            if (json["ResultType"].ToString() == "1") {
                await _bot.Client.SendTextMessageAsync(callerUser.UserData.Id, "Turn submitted");
                await _bot.Client.SendTextMessageAsync(selectedPlayer.UserData.Id, $"{callerUser.Username} submitted your turn");
            }
            else {
                await _bot.Client.SendTextMessageAsync(callerUser.UserData.Id, $"Failed to submit turn {json["ResultType"]}");
            }
        }

        private async Task RemoveSub(Message message, Chat chat) {
            if (chat.Type != ChatType.Private) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!");
                return;
            }

            await _bot.Client.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

            var callerUser = UserData.Get(_database, message.From.Id);

            if (callerUser == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "You need to be registered to use this '/register authKey'!");
                return;
            }

            
            var keyboardButtons = new List<KeyboardButton>();
            foreach (var sub in callerUser.Subs) {
                var subUser = await _bot.GetChat(sub.SubId);
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
                var user = UserData.Get(_database, sub.SubId);

                callerUser.Subs.Remove(sub);
                sub.RemoveSub(_database);
                if (user == null) {
                    Console.WriteLine("GetSelectedCallback weird case");
                    await _bot.Client.SendTextMessageAsync(chat,"Weird case, game or user null?");
                    return;
                }

                var userName = (await _bot.GetChat(user.Id)).Username;
                var callerUserName = (await _bot.GetChat(callerUser.Id)).Username;
                await _bot.Client.SendTextMessageAsync(chat, $"Removed {userName} subbing from {game.Name}", replyMarkup: new ReplyKeyboardRemove());
                await _bot.Client.SendTextMessageAsync(user.Id, $"{callerUserName} revoked your sub rights from {game.Name}");
            }

            _bot.AddReplyGet(message.From.Id, chat.Id, OnGetSelectedCallback);
            await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Chose sub to remove", replyMarkup: forceReply);
        }

        private async Task AddSub(Message message, Chat chat) {
            if (chat.Type != ChatType.Private) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "This can only be done in private!");
                return;
            }

            await _bot.Client.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

            var callerUser = UserData.Get(_database, message.From.Id);

            if (callerUser == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "You need to be registered to use this '/register authKey'!");
                return;
            }

            GameData? selectedGame;
            UserData? selectedUser;

            async void OnGetGameCallback(Message msg) {
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
                _bot.AddReplyGet(msg.From.Id, chat.Id, OnGetUserCallback);
                await _bot.Client.SendTextMessageAsync(msg.Chat.Id, "Chose the user", replyMarkup: userReply);
            }

            async void OnGetUserCallback(Message msg) {
                selectedUser = selectedGame.Players.Find(playerData => playerData.User != null && playerData.Name == msg.Text)?.User;

                if (selectedUser == null) {
                    return;
                }
                _bot.AddReplyGet(msg.From.Id, chat.Id, OnGetTimesCallback);
                await _bot.Client.SendTextMessageAsync(msg.Chat.Id, "How many times can he play your turns in this game? (0 for unlimited)",
                    replyMarkup: new ReplyKeyboardRemove());
            }

            async void OnGetTimesCallback(Message msg) {
                if (!int.TryParse(msg.Text, out var selectedTimes) || selectedTimes < 0) {
                    _bot.AddReplyGet(msg.From.Id, chat.Id, OnGetTimesCallback);
                    await _bot.Client.SendTextMessageAsync(msg.Chat.Id, "Please provide a positive integer", replyMarkup: new ReplyKeyboardRemove());
                    return;
                }

                await SetSub(selectedGame, selectedUser, selectedTimes);
            }


            async Task SetSub(GameData game, UserData user, int times) {
                var sub = new SubData(callerUser.Id, user.Id, times, game);
                sub.InsertDatabase(_database);
                user.Subs.Add(sub);
                
                var userName = (await _bot.GetChat(user.Id)).Username;
                var callerUserName = (await _bot.GetChat(callerUser.Id)).Username;
                
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, $"Added {userName} as sub in {game.Name}", replyMarkup: new ReplyKeyboardRemove());
                await _bot.Client.SendTextMessageAsync(user.Id, $"{callerUserName} has given you rights to do his turn in {game.Name}");
            }

            var games = Games.Where(game => game.Players.Exists(player => player.User != null && player.User.SteamId == callerUser.SteamId))
                .Select(game => new KeyboardButton(game.Name)).ToList();


            var forceReply = new ReplyKeyboardMarkup(games.ToArray()) {
                OneTimeKeyboard = true,
                Selective = true
            };

            _bot.AddReplyGet(message.From.Id, chat.Id, OnGetGameCallback);
            await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Chose the game", replyMarkup: forceReply);
        }

        private void PlayerTurntimeFromInnerHtml(PlayerData player, StringBuilder stringBuilder, string innerHtml) {
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

        private async Task Turntimers(Chat chat, bool onlyCurrent) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);
            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatId => chatId == chat.Id));
            if (selectedGame == null) {
                await _bot.Client.SendTextMessageAsync(chat, "No game added to this chat");
                return;
            }

            var url = $"{_gmrUrl}Game/Details?id={selectedGame.GameId}";
            var response = _httpInstance.PostAsync(url, null).Result;
            if (!response.IsSuccessStatusCode) {
                await _bot.Client.SendTextMessageAsync(chat, "Problem connecting to gmr service");
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
            
            await _bot.Client.SendTextMessageAsync(chat, stringBuilder.ToString());
        }

        private async Task AddGame(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);

            if (chat.Type != ChatType.Private) {
                var admins = await _bot.Client.GetChatAdministratorsAsync(chat.Id);
                
                if (!admins.Select(admin => admin.User.Id).Contains(message.From.Id)) {
                    await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Only group admin can do this!");
                    return;
                }
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Please provide game id '/addgame gameid'!");
                return;
            }

            long gameId;
            try {
                gameId = long.Parse(args[1]);
            }
            catch {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Invalid gameid!");
                return;
            }

            var selectedGame = Games.Find(game => game.GameId == gameId);
            
            if (selectedGame == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id,
                    "Could not find a game with given id, you must create one with '/newgame gameid'");
                return;
            }
            
            if (selectedGame.Chats.Any(chatId => chatId == chat.Id)) {
                await _bot.Client.SendTextMessageAsync(message.Chat, "Channel already has a game!");
                return;
            }


            selectedGame.Chats.Add(chat.Id);

            selectedGame.InsertChat(_database, chat.Id);


            await _bot.Client.SendTextMessageAsync(message.Chat.Id,
                $"Added game {selectedGame.Name} to this channel! You will now receive turn notifications.");
        }

        private async Task RemoveGame(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);

            if (chat.Type != ChatType.Private) {
                var admins = await _bot.Client.GetChatAdministratorsAsync(chat.Id);
                
                if (!admins.Select(admin => admin.User.Id).Contains(message.From.Id)) {
                    await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Only group admin can do this!");
                    return;
                }
            }

            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatid => chatid == chat.Id));

            if (selectedGame == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
                return;
            }

            selectedGame.Chats.Remove(chat.Id);
            selectedGame.RemoveChat(_database, chat.Id);


            await _bot.Client.SendTextMessageAsync(message.Chat.Id,
                $"Removed game {selectedGame.Name} from this channel! You will not receive any more notifications.");
        }

        private async Task Order(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);
            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatId => chatId == chat.Id));
            if (selectedGame == null) {
                await _bot.Client.SendTextMessageAsync(chat, "No game added to this chat");
                return;
            }

            var orders = selectedGame.Players.OrderBy(x => x.TurnOrder).ToList();
            var result = new StringBuilder();
            orders.ForEach(player => result.Append($"\n{player.Name}"));

            await _bot.Client.SendTextMessageAsync(message.Chat.Id, $"Order is:{result}");
        }

        private async Task Next(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);
            var selectedGame = Games.FirstOrDefault(game => game.Chats.Any(chatId => chatId == chat.Id));
            if (selectedGame == null) {
                await _bot.Client.SendTextMessageAsync(chat, "No game added to this chat");
                return;
            }
            
            //Get the next player in list from ascending looping TurnOrder
            var player = selectedGame.Players.OrderBy(x => x.TurnOrder).ToList()[(selectedGame.CurrentPlayer.TurnOrder + 1) % selectedGame.Players.Count];
            await _bot.Client.SendTextMessageAsync(message.Chat.Id, $"Next player is: {player.Name}");
        }

        private async Task Tee(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.RecordVoice);
            var selectedGame = Games.Find(g => g.Chats.Any(chatId => chatId == chat.Id));

            if (selectedGame == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
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

            var speechConfig = SpeechConfig.FromSubscription(_configuration["SPEECH_KEY"], _configuration["SPEECH_REGION"]);

            // The language of the voice that speaks.
            speechConfig.SpeechSynthesisVoiceName = "fi-FI-NooraNeural";

            var path = Path.GetRandomFileName();
            using var speechSynthesizer = new SpeechSynthesizer(speechConfig, AudioConfig.FromWavFileOutput(path));
            var synthesisResult = await speechSynthesizer.SpeakTextAsync(output);

            if (synthesisResult.Reason == ResultReason.Canceled) return;
            var file = new InputOnlineFile(System.IO.File.Open(path, FileMode.Open), "output.ogg");
            await _bot.Client.SendVoiceAsync(message.Chat.Id, file);
            System.IO.File.Delete(path);
        }

        private async Task Eta(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);

            var selectedGame = Games.Find(game => game.Chats.Any(chatId => chatId == chat.Id));

            if (selectedGame == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "No game added to this group!");
                return;
            }

            if (selectedGame.CurrentPlayer.User == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Current player has not registered");
                return;
            }

            if (selectedGame.CurrentPlayer.User.Id != message.From.Id) {
                await GetOtherPlayerEta(message, selectedGame, selectedGame.CurrentPlayer);
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id,
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
                if (!ParseTime(args, out hour, ref minute, ref day)) {
                    await _bot.Client.SendTextMessageAsync(message.Chat.Id,
                        "Please provide time in hours '/eta hours(:minutes(:day)) or /eta nyt|kohta'!");
                    return;
                }
            }


            var eta = DateTime.Today.AddDays(day).AddHours(hour).AddMinutes(minute);


            if (eta <= DateTime.Now) {
                eta = DateTime.Now.Date.AddDays(1).AddHours(eta.Hour).AddMinutes(eta.Minute);
            }

            if (eta >= DateTime.Now.AddDays(7)) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Laitappa se vacation mode sit pääl");
                return;
            }

            selectedGame.CurrentPlayer.NextEta = eta;
            selectedGame.CurrentPlayer.UpdateDatabase(_database);
            await _bot.Client.SendTextMessageAsync(message.Chat.Id,
                $"{selectedGame.CurrentPlayer.Name} eta set to {selectedGame.CurrentPlayer.NextEta:HH:mm ddd}");
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

        private async Task GetOtherPlayerEta(Message message, GameData selectedGame, PlayerData player) {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append($"{player.Name} ");
            TimeSpan diff;

            if (player.NextEta < DateTime.Now) {
                var turnTimer = GetTurntimer(selectedGame, player);
                if (!turnTimer.HasValue) {
                    await _bot.Client.SendTextMessageAsync(message.Chat.Id, $"Uusi turntimer tulossa! {player.Name}");
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

            await _bot.Client.SendTextMessageAsync(message.Chat.Id, stringBuilder.ToString());
        }

        private async Task Help(Message message, Chat chat) {
            string usage;
            if (chat.Type == ChatType.Private) {
                usage = @"CiviBotti:
/help - lolapua
/register 'authKey' - register your authorization key
/newgame 'gameid' - creates a new game
/addgame 'gameid' - add a game to this chat
/removegame - Remove assigned game from chat";
            }
            else {
                var game = GetGameFromChat(message.Chat.Id);

                var admins = await _bot.Client.GetChatAdministratorsAsync(chat.Id);
                
                if (admins.Select(admin => admin.User.Id).Contains(message.From.Id)) {
                    usage = game != null ? @"CiviBotti:
/help - lolapua
/addgame 'gameid' - Add a game to this chat" : @"CiviBotti:
/help - lolapua
/order - display order of players
/removegame - Remove assigned game from chat";
                }
                else {
                    usage = game != null ? @"CiviBotti:
/help - lolapua" : @"CiviBotti:
/help - lolapua
/order - display order of players";
                }
            }

            await _bot.Client.SendTextMessageAsync(message.Chat.Id, usage);
        }

        private async Task RegisterGame(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Registering can only be created in private chat!");
                return;
            }

            var args = message.Text.Split(' ');
            if (args.Length != 2) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Please provide authKey '/register authKey'!");
                return;
            }

            if (UserData.CheckDatabase(_database, message.From.Id)) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "You are already registered!");
                return;
            }

            var steamId = GetPlayerIdFromAuthKey(args[1]);
            if (steamId == "null") {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Authorization key you provided was incorrect!");
                return;
            }


            var newUser = UserData.NewUser(message.From.Id, steamId, args[1]);
            newUser.InsertDatabase(_database);
            foreach (var game in Games) {
                foreach (var player in game.Players) {
                    if (player.SteamId == steamId) {
                        player.User = newUser;
                    }
                }
            }

            await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Registered with steamid " + steamId);
        }

        private async Task ListSubs(Message message, Chat chat) {
            await _bot.Client.SendChatActionAsync(chat.Id, ChatAction.Typing);
            if (chat.Type != ChatType.Private) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "Subs should be done in private chat");
                return;
            }

            var callerUser = UserData.Get(_database, message.From.Id);

            if (callerUser == null) {
                await _bot.Client.SendTextMessageAsync(message.Chat.Id, "You need to be registered to use this '/register authKey'!");
                return;
            }

            var stringBuilder = new StringBuilder();
            foreach (var game in Games.Where(game => game.Players.Exists(userData => userData.User?.SteamId == callerUser.SteamId))) {
                stringBuilder.Append($"{game.Name}:\n");

                if (callerUser.Subs == null || callerUser.Subs.Count == 0 ||
                    !callerUser.Subs.Exists(sub => sub.Game.GameId == game.GameId)) {
                    stringBuilder.Append(" none\n");
                    continue;
                }

                foreach (var sub in callerUser.Subs.FindAll(subData => subData.Game.GameId == game.GameId)) {
                    var subUser = await _bot.GetChat(sub.SubId);
                    stringBuilder.Append($" -{subUser.Username} for {(sub.Times == 0 ? "unlimited" : sub.Times.ToString())} times\n");
                }
            }

            if (stringBuilder.Length == 0) {
                stringBuilder.Append("You are not in any games");
            }

            await _bot.Client.SendTextMessageAsync(message.Chat.Id, stringBuilder.ToString());
        }


        private TimeSpan? GetTurntimer(GameData selectedGame, PlayerData player) {
            var url = $"{_gmrUrl}Game/Details?id={selectedGame.GameId}";
            var response = _httpInstance.PostAsync(url, null).Result;
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

        private async Task PollTurn(GameData game) {
            try {
                var gameData = GetGameData(game);
                
                if (gameData == null) {
                    Console.WriteLine($"Game {game.GameId} data null");
                    return;
                }

                JToken current;
                try {
                    current = gameData["CurrentTurn"];
                }
                catch {
                    return;
                }

                if (current == null) {
                    return;
                }

                var oldPlayerId = game.CurrentPlayer.SteamId;

                var currentPlayerId = (string)current["UserId"];
                game.TurnStarted = DateTime.Parse(current["Started"].ToString());

                if (oldPlayerId != currentPlayerId) {
                    await ChangeTurns(game, currentPlayerId, current);
                }
                else {
                    await CheckTurnNotifications(game);
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
        }

        private async Task CheckTurnNotifications(GameData game) {
            if (game.CurrentPlayer.NextEta == DateTime.MinValue) {
                if (game.TurntimerNotified) {
                    await DailyNotify(game);
                    return;
                }
                var turnTimer = GetTurntimer(game, game.CurrentPlayer);
                if (!turnTimer.HasValue) return;
                if (game.TurnStarted + turnTimer.Value >= DateTime.UtcNow) return;
                game.TurntimerNotified = true;
                game.UpdateCurrent(_database);
                foreach (var chat in game.Chats) {
                    await _bot.Client.SendTextMessageAsync(chat, $"Turn timer kärsii {game.CurrentPlayer.NameTag}");
                }
            }
            else {
                if (game.CurrentPlayer.NextEta >= DateTime.Now) {
                    return;
                }

                game.CurrentPlayer.NextEta = DateTime.MinValue;
                game.CurrentPlayer.UpdateDatabase(_database);
                foreach (var chat in game.Chats) {
                    await _bot.Client.SendTextMessageAsync(chat, $"Aikamääreistä pidetään kiinni {game.CurrentPlayer.NameTag}");
                }
            }
        }

        private async Task ChangeTurns(GameData game, string currentPlayerId, JToken current) {
            var player = game.Players.Find(playerData => playerData.SteamId == currentPlayerId);
            
            if (player == null) {
                throw new ArgumentNullException($"Player {currentPlayerId} not found in database!");
            }
            
            game.CurrentPlayer.UpdateDatabase(_database);
            game.CurrentPlayer = player;
            game.TurntimerNotified = false;
            game.TurnStarted = DateTime.Now;
            game.TurnId = current["TurnId"].ToString();
            game.UpdateCurrent(_database);

            player.SteamName = GetSteamUserName(player.SteamId);

            player.User = UserData.GetBySteamId(_database, player.SteamId);

            if (player.User != null) {
                var tgUser = await _bot.GetChat(player.User.Id);
                player.TgName = tgUser.Username;
            }

            foreach (var chat in game.Chats) {
                Console.WriteLine(chat);
                await _bot.Client.SendTextMessageAsync(chat,
                    game.CurrentPlayer != null
                        ? $"It's now your turn {game.CurrentPlayer.NameTag}!"
                        : "It's now your turn waitwhatthishsouldntbehappening?!");
            }
        }

        private async Task DailyNotify(GameData game) {
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
                        game.UpdateCurrent(_database);
                    }
                    return;
            }

            if (game.DailyNotified) {
                return;
            }
            game.DailyNotified = true;
            game.UpdateCurrent(_database);
            foreach (var chat in game.Chats) {
                await _bot.Client.SendTextMessageAsync(chat, message);
            }
        }

        private JToken? GetGameData(GameData game) {
            var url =
                $"{_gmrUrl}api/Diplomacy/GetGamesAndPlayers?playerIDText={game.Owner?.SteamId}&authKey={game.Owner?.AuthKey}";


            var request = _httpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(html);

            return json["Games"].FirstOrDefault(item => (int)item["GameId"] == game.GameId);
        }

        private string GetPlayerIdFromAuthKey(string authKey) {
            var url = $"{_gmrUrl}api/Diplomacy/AuthenticateUser?authKey={authKey}";

            var request = _httpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            return html;
        }

        private string GetSteamUserName(string steamId) {
            var url =
                $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=E2C1F453B82A8F42092118E4B3F55037&steamids={steamId}";

            var request = _httpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(html);
            var players = json["response"]["players"];

            return players.Count() != 1 ? "UNKNOWN" : players.First["personaname"].ToString();
        }

        private Dictionary<string, string> GetSteamUserNames(IEnumerable<string> steamId) {
            var url =
                $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=E2C1F453B82A8F42092118E4B3F55037&steamids={string.Join(",", steamId.Distinct())}";

            var request = _httpInstance.GetAsync(url).Result;
            var html = request.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(html);
            var players = json["response"]["players"].ToArray();

            var dic = players.ToDictionary(player => player["steamid"].ToString(),
                player => player["personaname"].ToString());

            return dic;
        }
    }
}