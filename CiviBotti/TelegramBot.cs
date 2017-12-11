using System;
using System.Threading.Tasks;
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

        #endregion

        #region constructors
        public TelegramBot(string token) {
            _bot = new TelegramBotClient(token);//Test
            _bot.OnCallbackQuery += BotOnCallbackQueryReceived;
            _bot.OnMessage += BotOnMessageReceived;
            _bot.OnReceiveError += BotOnReceiveError;

            Name = _bot.GetMeAsync().Result.Username;

            Console.Title = Name;
        }

        public string Name { get; }

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

        public async Task SendText(long chat, string msg) {
            try
            {
                await _bot.SendTextMessageAsync(chat, msg);
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public async Task SetChatAction(long chatId, ChatAction action)
        {
            try
            {
                await _bot.SendChatActionAsync(chatId, action);
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public async Task<ChatMember[]> GetAdministrators(long chatId)
        {
            return await _bot.GetChatAdministratorsAsync(chatId);
        }

        #endregion


        #region Callbacks


        private void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs) {
            Console.WriteLine("BotOnReceiveError:\n" + receiveErrorEventArgs.ApiRequestException.Message);
        }

        private async void BotOnCallbackQueryReceived(object sender, CallbackQueryEventArgs callbackQueryEventArgs) {
            await _bot.AnswerCallbackQueryAsync(callbackQueryEventArgs.CallbackQuery.Id,
                $"Received {callbackQueryEventArgs.CallbackQuery.Data}");
        }


        private static void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs) {
            var message = messageEventArgs.Message;
            if (message == null || message.Type != MessageType.TextMessage) return;
            
            Console.WriteLine(message.Text);


            if (!message.Text.StartsWith("/")) return;
            
            Program.ParseCommand(message);
        }

        #endregion

            public async Task<Chat> GetChat(long userId) {
            return await _bot.GetChatAsync(userId);
        }

        public async Task<Message> SendVoice(long chatId, FileToSend file) {
            return await _bot.SendVoiceAsync(chatId, file);
        }
    }
}