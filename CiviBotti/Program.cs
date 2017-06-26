
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.InputMessageContents;
using Telegram.Bot.Types.ReplyMarkups;

namespace CiviBotti {
    class Program {
        public static List<GameData> Games;
        
        public static Database database;
        
        private static TelegramBotClient Bot;

        static void Main(string[] args) {
 
            ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
            configMap.ExeConfigFilename = "bot.config";
            Configuration config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            string configValue = config.AppSettings.Settings["DatabaseType"].Value;
            Database.DatabaseType dbType = (Database.DatabaseType)Enum.Parse(typeof(Database.DatabaseType), configValue);
            database = new Database(dbType);

            Bot = new TelegramBotClient(config.AppSettings.Settings["BotToken"].Value);//Test
            Bot.OnCallbackQuery += BotOnCallbackQueryReceived;
            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnReceiveError += BotOnReceiveError;

            var me = Bot.GetMeAsync().Result;

            Console.Title = me.Username;

            System.Timers.Timer aTimer = new System.Timers.Timer(30000);
            aTimer.Elapsed += Tick;
            aTimer.Enabled = true;
            aTimer.Start();


            Games = GameData.GetAllGames();
            foreach (var game in Games) {
                Console.WriteLine(game.gameID + " " + game.owner);
                Console.WriteLine(" chats:");
                foreach (var chat in game.chats) {
                    Console.WriteLine("  -" + chat);
                }
                Console.WriteLine(" players:");
                foreach (var player in game.players) {
                    Console.WriteLine($"  -{player.steamID} ({player.turnOrder}) {player.user}");
                }
            }
            
            Bot.StartReceiving();
            Tick(null, null);
            Console.ReadLine();
            Bot.StopReceiving();
        }

        private static void Tick(object sender, System.Timers.ElapsedEventArgs e) {
            foreach (var game in Games) {
                PollTurn(game);
            }
        }

        private static void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs) {
            Debugger.Break();
        }

        private static GameData getGameFromChat(long chatID) {
            foreach (var game in Games) {
                foreach (var chat in game.chats) {
                    if (chat == chatID)
                        return game;
                }
            }

            return null;
        }

        private static async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs) {
            var message = messageEventArgs.Message;
            if (message == null || message.Type != MessageType.TextMessage) return;

            Chat chat = message.Chat;
            Console.WriteLine(message.Text);

            if (message.ReplyToMessage != null) {

            }

            if (message.Text.StartsWith("/newgame")) {
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

                long gameID;
                try {
                    gameID = long.Parse(args[1]);
                } catch {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Invalid gameid!");
                    return;
                }

                foreach (var game in Games) {
                    if (game.gameID == gameID) {
                        await Bot.SendTextMessageAsync(message.Chat.Id, "Game has already been created!");
                        return;
                    }
                }




                GameData newGame = new GameData();
                newGame.owner = UserData.Get(message.From.Id);
                newGame.gameID = gameID;
                var data = getGameData(newGame);
                if (data == null) {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Invalid gameid, or your account is not in the game!");
                    return;
                }

                newGame.players = new List<PlayerData>();
                newGame.chats = new List<long>();
                newGame.name = (string)data["Name"];
                JToken current = data["CurrentTurn"];

                if (current == null) {
                    return;
                }

                string currentPlayerID = (string)current["UserId"];
                foreach (var player in data["Players"]) {
                    PlayerData playerData = new PlayerData();

                    playerData.gameID = gameID;
                    playerData.turnOrder = player["TurnOrder"].Value<int>();
                    playerData.steamID = player["UserId"].Value<string>();
                    playerData.user = UserData.GetBySteamID(playerData.steamID);

                    if (currentPlayerID == playerData.steamID) {
                        newGame.currentPlayer = playerData;
                    }

                    newGame.players.Add(playerData);

                    Console.WriteLine(playerData.steamID + " " + playerData.turnOrder);
                }

                newGame.InsertFull();
                Games.Add(newGame);
                await Bot.SendTextMessageAsync(message.Chat.Id, $"Succesfuly created the game {newGame.name}!");
            } else if (message.Text.StartsWith("/register")) {
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
                    await Bot.SendTextMessageAsync(message.Chat.Id, $"You are already registered!");
                    return;
                }

                string steamID = getPlayerIDFromAuthkey(args[1]);
                if (steamID == "null") {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Authkey you provided was incorrect!");
                    return;
                }


                UserData newUser = new UserData();
                newUser.ID = message.From.Id;
                newUser.steamID = steamID;
                newUser.authKey = args[1];
                newUser.InsertDatabase(false);
                foreach (var game in Games) {
                    foreach (var player in game.players) {
                        if (player.steamID == steamID) {
                            player.user = newUser;
                        }
                    }

                }
                await Bot.SendTextMessageAsync(message.Chat.Id, "Registered with steamid " + steamID);
            } else if (message.Text.StartsWith("/addgame")) {
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

                long gameID;
                try {
                    gameID = long.Parse(args[1]);
                } catch {
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Invalid gameid!");
                    return;
                }

                GameData selectedGame = null;
                foreach (var game in Games) {
                    if (game.gameID == gameID) {
                        selectedGame = game;
                    }

                    foreach (var chatid in game.chats) {
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

                selectedGame.chats.Add(chat.Id);

                selectedGame.InsertChats();


                await Bot.SendTextMessageAsync(message.Chat.Id, $"Added game {selectedGame.name} to this channel! You will now receive turn notifications.",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (message.Text.StartsWith("/removegame")) {

                if (chat.Type != ChatType.Private) {
                    var admins = new List<ChatMember>(await Bot.GetChatAdministratorsAsync(chat.Id));
                    if (!admins.Exists(x => x.User.Id == message.From.Id)) {
                        await Bot.SendTextMessageAsync(message.Chat.Id, "Only group admin can do this!");
                        return;
                    }
                }

                GameData selectedGame = null;
                foreach (var game in Games) {
                    foreach (var chatid in game.chats) {
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

                selectedGame.chats.Remove(chat.Id);
                selectedGame.RemoveChat(chat.Id);


                await Bot.SendTextMessageAsync(message.Chat.Id, $"Removed game {selectedGame.name} from this channel! You will not receive any more notifications.",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (message.Text.StartsWith("/order")) {
                GameData selectedGame = null;
                foreach (var game in Games) {
                    foreach (var chatid in game.chats) {
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

                var orders = selectedGame.players.OrderBy(x => x.turnOrder);
                string result = "";
                foreach (var player in orders) {
                    string name = "";

                    if (player.user != null) {
                        var member = await Bot.GetChatAsync(player.user.ID);
                        name = member.Username;
                    } else {
                        name = GetSteamUserName(player.steamID);
                    }

                    if (result != "") {
                        result += "\n";
                    }

                    result += name;
                }

                await Bot.SendTextMessageAsync(message.Chat.Id, $"Order is:\n{result}",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (message.Text.StartsWith("/next")) {
                GameData selectedGame = null;
                foreach (var game in Games) {
                    foreach (var chatid in game.chats) {
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

                var orders = selectedGame.players.OrderBy(x => x.turnOrder);

                int next = selectedGame.currentPlayer.turnOrder + 1;

                if (next >= orders.Count()) {
                    next = 0;
                }
                var player = selectedGame.players[next];
                string name = "";

                if (player.user != null) {
                    var member = await Bot.GetChatAsync(player.user.ID);
                    name = member.Username;
                } else {
                    name = GetSteamUserName(player.steamID);
                }

                await Bot.SendTextMessageAsync(message.Chat.Id, $"Next player is: {name}",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (message.Text.StartsWith("/autocracy") || message.Text.StartsWith("/freedom")) {
                await Bot.SendTextMessageAsync(message.Chat.Id, $"Did you mean /order?",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (message.Text.StartsWith("/oispa")) {
                await Bot.SendTextMessageAsync(message.Chat.Id, $"Kaljaa?",
                    replyMarkup: new ReplyKeyboardHide());
            } else if (message.Text.StartsWith("/tee")) {


                GameData selectedGame = null;
                foreach (var game in Games) {
                    foreach (var chatid in game.chats) {
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
                
                var player = selectedGame.currentPlayer;
                string name;
                if (player.user != null) {
                    var member = await Bot.GetChatAsync(player.user.ID);
                    name = member.Username;
                } else {
                    name = GetSteamUserName(player.steamID);
                }


                var synth = new SpeechSynthesizer();
                var stream = new MemoryStream();

                
                synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new CultureInfo("fi-FI"));

                synth.SetOutputToWaveStream(stream);
                synth.Speak($"{name} tee vuoros");

                stream.Flush();

                stream.Seek(0, SeekOrigin.Begin);

                var file = new FileToSend("output.ogg", stream);
                await Bot.SendVoiceAsync(message.Chat.Id, file);
            } else if (message.Text.StartsWith("/help")) {
                var usage = "";
                if (chat.Type == ChatType.Private) {
                    usage = @"CiviBotti:
/help - lolapua
/register 'authkey' - register your authorization key
/newgame 'gameid' - creates a new game
/addgame 'gameid' - add a game to this chat
/removegame - Remove assigned game from chat";
                } else {
                    GameData game = getGameFromChat(message.Chat.Id);

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
            
            JToken gameData = getGameData(game);

            try {
                JToken current = gameData["CurrentTurn"];
                JToken players = gameData["Players"];

                if (current == null) {
                    return;
                }
                
                string oldPlayerID = string.Empty;
                if (game.currentPlayer != null) {
                    oldPlayerID = game.currentPlayer.steamID;
                }
                string currentPlayerID = (string)current["UserId"];
                
                if (oldPlayerID != currentPlayerID) {

                    string name = "";

                    UserData user = UserData.GetBySteamID(currentPlayerID);
                    if (user != null) {
                        var member = await Bot.GetChatAsync(user.ID);
                        name = "@" + member.Username;
                    } else {
                        name = GetSteamUserName(currentPlayerID);
                    }

                    foreach (var player in game.players) {
                        if (player.steamID == currentPlayerID) {
                            game.currentPlayer = player;
                            game.UpdateCurrent();
                        }
                    }

                    foreach (var chat in game.chats) {
                        await Bot.SendTextMessageAsync(chat, $"It's now your turn {name}!",
                                replyMarkup: new ReplyKeyboardHide());
                    }
                    
                }
            } catch (Exception) {
                return;
            }

        }

        public static JToken getGameData(GameData game) {
            string url = $"http://multiplayerrobot.com/api/Diplomacy/GetGamesAndPlayers?playerIDText={game.owner.steamID}&authKey={game.owner.authKey}";
            string html = string.Empty;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream)) {
                html = reader.ReadToEnd();
            }

            JObject json = JObject.Parse(html);

            foreach (var item in json["Games"]) {
                if ((int)item["GameId"] == game.gameID) {
                    return item;
                }
            }

            return null;
        }

        private static string getPlayerIDFromAuthkey(string authkey) {
            string url = $"http://multiplayerrobot.com/api/Diplomacy/AuthenticateUser?authKey={authkey}";
            string html = string.Empty;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream)) {
                html = reader.ReadToEnd();
            }
            
            return html;
        }

        private static string GetSteamUserName(string steamid) {
            string url = $"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=E2C1F453B82A8F42092118E4B3F55037&steamids={steamid}";
            string html = string.Empty;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream)) {
                html = reader.ReadToEnd();
            }

            JObject json = JObject.Parse(html);
            var players = json["response"]["players"];

            if (players.Count() != 1) {
                return "UNKNOWN";
            } else {
                return players.First["personaname"].ToString();
            }
            
        }



        
    }
}
