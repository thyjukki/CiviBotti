using CiviBotti.Configurations;
using CiviBotti.DataModels;
using CiviBotti.Exceptions;
using CiviBotti.Services;

using Microsoft.Extensions.Options;

using Moq;

using RichardSzalay.MockHttp;

namespace CiviBottiTest.Services;

[TestFixture]
[TestOf(typeof(GmrClient))]
public class GmrClientTest
{
    private static (GameData, UserData, PlayerData) CreateTestGame()
    {
        var user = new UserData(1, "111111", "authKey");
        var player = new PlayerData(1, user.SteamId, 0, DateTime.MinValue);
        var game = new GameData(31578, user, "TestGame", player, false, false, false);
        game.Players.Add(player);
        return (game, user, player);
    }


    [Test]
    public void GetGameDataShouldThrowExceptionIfOwnerDoesNotHaveTheGame()
    {
        (GameData game, UserData user, PlayerData player) = CreateTestGame();
        
        var configuration =  new GmrConfiguration() { GmrUrl = "https://baseurl.com/" };
        var mockBotConfiguration = new Mock<IOptions<GmrConfiguration>>();
        mockBotConfiguration.Setup(c =>  c.Value).Returns(configuration);
        
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"https://baseurl.com/api/Diplomacy/GetGamesAndPlayers?playerIDText={player.SteamId}&authKey={user.AuthKey}")
            .Respond("application/json", "{'Games':[],'Players':[],'CurrentTotalPoints':7511}");
        var client = mockHttp.ToHttpClient();
        
        var gmrClient = new GmrClient(client, mockBotConfiguration.Object);
        Assert.ThrowsAsync<MissingOwnerException>(async () => await gmrClient.GetGameData(game.GameId, player.SteamId, user.AuthKey));
    }
}