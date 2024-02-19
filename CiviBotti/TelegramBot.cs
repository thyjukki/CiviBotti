using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = Telegram.Bot.Types.File;

namespace CiviBotti
{
    using System.Threading.Tasks;
    using Telegram.Bot.Types.InputFiles;

    public class TelegramBot
    {
        private readonly List<ChatCallback> _replyCallbacks = new ();

        #region variables
        public readonly TelegramBotClient bot;

        public string TechnicalName { get; }
        #endregion

        #region constructors
        public TelegramBot(string token) {
            bot = new TelegramBotClient(token);//Test
            bot.OnCallbackQuery += BotOnCallbackQueryReceived;
            bot.OnMessage += BotOnMessageReceived;
            bot.OnReceiveError += BotOnReceiveError;

            TechnicalName = bot.GetMeAsync().Result.Username;
        }


        #endregion

        #region public functions
        public void StartReceiving()
        {
            bot.StartReceiving();
        }

        public void StopReceiving()
        {
            bot.StopReceiving();
        }

        public void SendText(long chat, string msg, IReplyMarkup replyMarkup = null) {
            try
            {
                bot.SendTextMessageAsync(chat, msg, replyMarkup: replyMarkup);
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public void SendText(Chat chat, string msg, IReplyMarkup replyMarkup = null) {
            SendText(chat.Id, msg, replyMarkup);
        }

        public void SetChatAction(long chatId, ChatAction action)
        {
            try
            {
                bot.SendChatActionAsync(chatId, action);
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine("BotSendText:\n" + ex);
            }
        }

        public ChatMember[] GetAdministrators(long chatId)
        {
            return bot.GetChatAdministratorsAsync(chatId).Result;
        }

        public async Task<Chat> GetChat(long userId) {
            return await bot.GetChatAsync(userId);
        }

        public Message SendVoice(long chatId, InputOnlineFile file)
        {
            return bot.SendVoiceAsync(chatId, file).Result;
        }

        public Message SendFile(long chatId, InputOnlineFile file)
        {
            return bot.SendDocumentAsync(chatId, file).Result;
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

        #endregion


        #region Callbacks


        private void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs) {
            Console.WriteLine("BotOnReceiveError:\n" + receiveErrorEventArgs.ApiRequestException.Message);
        }

        private void BotOnCallbackQueryReceived(object sender, CallbackQueryEventArgs callbackQueryEventArgs) {
            bot.AnswerCallbackQueryAsync(callbackQueryEventArgs.CallbackQuery.Id, $"Received {callbackQueryEventArgs.CallbackQuery.Data}");
        }


        private async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs) {
            var message = messageEventArgs.Message;


            var userId = message.From.Id;

            var chatCb = _replyCallbacks.Find(rbc => rbc.User == userId && rbc.Chat == message.Chat.Id);
            if (chatCb != null) {
                _replyCallbacks.Remove(chatCb);
                chatCb.Callback?.Invoke(message);
                return;
            }

            if (message.Type != MessageType.Text) return;
            var groupstring = "";

            if (message.Chat.Type != ChatType.Private) groupstring = $" ({message.Chat.Username})";
            var user = await bot.GetChatAsync(userId);
            Console.WriteLine($@"{DateTime.Now:MM\/dd\/yyyy HH:mm} [{user.Username}]{groupstring} {message.Text}");


            if (!message.Text.StartsWith("/")) return;

            var commandSplit = message.Text.Split(' ')[0].Split('@');
            if (commandSplit.Length == 2 && !string.Equals(commandSplit[1], TechnicalName, StringComparison.OrdinalIgnoreCase)) return;
            var command = commandSplit[0].Split('/')[1];
            await SubProgram.ParseCommand(command, message);
        }

        #endregion
    }
}