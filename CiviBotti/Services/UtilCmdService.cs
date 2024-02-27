namespace CiviBotti.Services;

using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class UtilCmdService(ITelegramBotClient botClient, GameContainerService gameContainer)
{
    public async Task Order(Message message, Chat chat, CancellationToken ct) {
        await botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);
        var selectedGame = gameContainer.Games.FirstOrDefault(game => game.Chats.Exists(chatId => chatId == chat.Id));
        if (selectedGame == null) {
            await botClient.SendTextMessageAsync(chat, "No game added to this chat", cancellationToken: ct);
            return;
        }

        var orders = selectedGame.Players.OrderBy(x => x.TurnOrder).ToList();
        var result = new StringBuilder();
        orders.ForEach(player => result.Append($"\n{player.Name}"));

        await botClient.SendTextMessageAsync(message.Chat.Id, $"Order is:{result}", cancellationToken: ct);
    }
    
    public async Task Next(Message message, Chat chat, CancellationToken ct) {
        await botClient.SendChatActionAsync(chat.Id, ChatAction.Typing, cancellationToken: ct);
        var selectedGame = gameContainer.Games.FirstOrDefault(game => game.Chats.Exists(chatId => chatId == chat.Id));
        if (selectedGame == null) {
            await botClient.SendTextMessageAsync(chat, "No game added to this chat", cancellationToken: ct);
            return;
        }
            
        //Get the next player in list from ascending looping TurnOrder
        var player = selectedGame.Players.OrderBy(x => x.TurnOrder).ToList()[(selectedGame.CurrentPlayer.TurnOrder + 1) % selectedGame.Players.Count];
        await botClient.SendTextMessageAsync(message.Chat.Id, $"Next player is: {player.Name}", cancellationToken: ct);
    }

    public async Task Help(Message message, Chat chat, CancellationToken ct) {
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
            var game = gameContainer.GetGameFromChat(message.Chat.Id);

            var admins = await botClient.GetChatAdministratorsAsync(chat.Id, cancellationToken: ct);
                
            if (admins.Select(admin => admin.User.Id).Contains(message.From!.Id)) {
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

        await botClient.SendTextMessageAsync(message.Chat.Id, usage, cancellationToken: ct);
    }
}