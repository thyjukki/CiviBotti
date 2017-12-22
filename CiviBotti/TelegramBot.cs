using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Remoting.Messaging;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = Telegram.Bot.Types.File;

namespace CiviBotti
{
    public class TelegramBot
    {
        private readonly List<ChatCallback> _replyCallbacks = new List<ChatCallback>();

        #region variables
        private readonly TelegramBotClient _bot;

        public string Name { get; }
        public string TechnicalName { get; }
        #endregion

        #region constructors
        public TelegramBot(string token) {
            _bot = new TelegramBotClient(token);//Test
            _bot.OnCallbackQuery += BotOnCallbackQueryReceived;
            _bot.OnMessage += BotOnMessageReceived;
            _bot.OnReceiveError += BotOnReceiveError;

            Name = _bot.GetMeAsync().Result.FirstName;
            TechnicalName = _bot.GetMeAsync().Result.Username;
            Console.Title = Name;
        }


        #endregion

        #region public functions
        public void StartReceiving()
        {
            _bot.StartReceiving();
        }

        public void StopReceiving()
        {
            _bot.StopReceiving();
        }

        public void SendText(long chat, string msg, ReplyMarkup replyMarkup = null) {
            try
            {
                _bot.SendTextMessageAsync(chat, msg, replyMarkup: replyMarkup);
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public void SendText(Chat chat, string msg, ReplyMarkup replyMarkup = null) {
            SendText(chat.Id, msg, replyMarkup);
        }

        public void SetChatAction(long chatId, ChatAction action)
        {
            try
            {
                _bot.SendChatActionAsync(chatId, action);
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public ChatMember[] GetAdministrators(long chatId)
        {
            return _bot.GetChatAdministratorsAsync(chatId).Result;
        }

        public Chat GetChat(long userId) {
            try {
                return _bot.GetChatAsync(userId).Result;
            }
            catch {
                return null;
            }
        }

        public Message SendVoice(long chatId, FileToSend file)
        {
            return _bot.SendVoiceAsync(chatId, file).Result;
        }

        public Message SendFile(long chatId, FileToSend file)
        {
            return _bot.SendDocumentAsync(chatId, file).Result;
        }

        public void AddReplyGet(int user, long chat, Action<Message> callback)
        {

            var chatCb = _replyCallbacks.Find(_ => _.User == user && _.Chat == chat);
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

        public Stream GetFileAsStream(File file) {
            var test = _bot.GetFileAsync(file.FileId).Result;
            
            return test.FileStream;
        }
        #endregion


        #region Callbacks


        private void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs) {
            Console.WriteLine("BotOnReceiveError:\n" + receiveErrorEventArgs.ApiRequestException.Message);
        }

        private void BotOnCallbackQueryReceived(object sender, CallbackQueryEventArgs callbackQueryEventArgs) {
            _bot.AnswerCallbackQueryAsync(callbackQueryEventArgs.CallbackQuery.Id, $"Received {callbackQueryEventArgs.CallbackQuery.Data}");
        }


        private void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs) {
            var message = messageEventArgs.Message;


            var user = message.From.Id;

            var chatCb = _replyCallbacks.Find(_ => _.User == user && _.Chat == message.Chat.Id);
            if (chatCb != null) {
                _replyCallbacks.Remove(chatCb);
                chatCb.Callback?.Invoke(message);
                return;
            }

            if (message.Type != MessageType.TextMessage) return;
            var groupstring = "";

            if (message.Chat.Type != ChatType.Private) groupstring = $" ({message.Chat.Username})";
            Console.WriteLine($"{DateTime.Now:MM\\/dd\\/yyyy HH:mm} [{GetChat(user).Username}]{groupstring} {message.Text}");


            if (!message.Text.StartsWith("/")) return;

            var commandSplit = message.Text.Split(' ')[0].Split('@');
            if (commandSplit.Length == 2) {
                if (!string.Equals(commandSplit[1], TechnicalName, StringComparison.OrdinalIgnoreCase)) return;
            }
            var command = commandSplit[0].Split('/')[1];
            Program.ParseCommand(command, message);
        }

        #endregion
    }
}