using System;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CiviBotti
{
    public class TelegramBot
    {
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

            Name = _bot.GetMeAsync().Result.Username;
            TechnicalName = _bot.GetMeAsync().Result.FirstName;
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

        public void SendText(long chat, string msg) {
            try
            {
                _bot.SendTextMessageAsync(chat, msg);
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public void SendText(Chat chat, string msg) {
            SendText(chat.Id, msg);
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
            return _bot.GetChatAsync(userId).Result;
        }

        public Message SendVoice(long chatId, FileToSend file) {
            return _bot.SendVoiceAsync(chatId, file).Result;
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
            if (message == null || message.Type != MessageType.TextMessage) return;
            
            Console.WriteLine(message.Text);


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