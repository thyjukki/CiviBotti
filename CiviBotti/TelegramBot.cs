using System;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CiviBotti
{
    using System.Threading.Tasks;

    public class TelegramBot
    {
        public delegate void CommandReceivedHandler(string command, Message message);
        public event CommandReceivedHandler? CommandReceived;
        
        private readonly List<ChatCallback> _replyCallbacks = new ();

        #region variables
        public readonly TelegramBotClient Client;

        private string TechnicalName { get; }
        #endregion

        #region constructors
        public TelegramBot(string token) {
            Client = new TelegramBotClient(token);//Test
            Client.OnCallbackQuery += BotOnCallbackQueryReceived;
            Client.OnMessage += BotOnMessageReceived;
            Client.OnReceiveError += BotOnReceiveError;

            TechnicalName = Client.GetMeAsync().Result.Username;
        }


        #endregion

        #region public functions

        public async Task<Chat> GetChat(long userId) {
            return await Client.GetChatAsync(userId);
        }

        public void AddReplyGet(long user, long chat, Action<Message> callback)
        {

            var chatCb = _replyCallbacks.Find(c => c.User == user && c.Chat == chat);
            if (chatCb != null)
            {
                Console.WriteLine("ERROR: 2 callbacks for user, removing old!!");
                _replyCallbacks.Remove(chatCb);
            }
            else
            {
                _replyCallbacks.Add(new ChatCallback(user, chat, callback));
            }
        }

        #endregion


        #region Callbacks


        private void BotOnReceiveError(object? sender, ReceiveErrorEventArgs receiveErrorEventArgs) {
            Console.WriteLine("BotOnReceiveError:\n" + receiveErrorEventArgs.ApiRequestException.Message);
        }

        private void BotOnCallbackQueryReceived(object? sender, CallbackQueryEventArgs callbackQueryEventArgs) {
            Client.AnswerCallbackQueryAsync(callbackQueryEventArgs.CallbackQuery.Id, $"Received {callbackQueryEventArgs.CallbackQuery.Data}");
        }


        private async void BotOnMessageReceived(object? sender, MessageEventArgs messageEventArgs) {
            var message = messageEventArgs.Message;


            var userId = message.From.Id;

            var chatCb = _replyCallbacks.Find(rbc => rbc.User == userId && rbc.Chat == message.Chat.Id);
            if (chatCb != null) {
                _replyCallbacks.Remove(chatCb);
                chatCb.Callback.Invoke(message);
                return;
            }

            if (message.Type != MessageType.Text) return;
            var groupString = "";

            if (message.Chat.Type != ChatType.Private) groupString = $" ({message.Chat.Username})";
            var user = await Client.GetChatAsync(userId);
            Console.WriteLine($@"{DateTime.Now:MM\/dd\/yyyy HH:mm} [{user.Username}]{groupString} {message.Text}");


            if (!message.Text.StartsWith("/")) return;

            var commandSplit = message.Text.Split(' ')[0].Split('@');
            if (commandSplit.Length == 2 && !string.Equals(commandSplit[1], TechnicalName, StringComparison.OrdinalIgnoreCase)) return;
            var command = commandSplit[0].Split('/')[1];
            CommandReceived?.Invoke(command, message);
        }

        #endregion
    }
}