using System;
using Telegram.Bot.Types;

namespace CiviBotti
{
    public class ChatCallback
    {
        public int User { get; }
        public long Chat { get; }
        public Action<Message> Callback { get; }

        public ChatCallback(int user, long chat, Action<Message> callback) {
            User = user;
            Chat = chat;
            Callback = callback;
        }
    }
}