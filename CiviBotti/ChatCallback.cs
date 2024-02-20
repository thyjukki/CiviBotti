using System;
using Telegram.Bot.Types;

namespace CiviBotti
{
    public class ChatCallback
    {
        public long User { get; }
        public long Chat { get; }
        public Action<Message> Callback { get; }

        public ChatCallback(long user, long chat, Action<Message> callback) {
            User = user;
            Chat = chat;
            Callback = callback;
        }
    }
}