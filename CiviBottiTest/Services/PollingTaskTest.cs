﻿using CiviBotti.DataModels;
using CiviBotti.Exceptions;
using CiviBotti.Services;

using GmrData.Gmr;

using Microsoft.CognitiveServices.Speech.Transcription;
using Microsoft.Extensions.Logging;

using Moq;

using Telegram.Bot;

namespace CiviBottiTest.Services;

[TestFixture]
[TestOf(typeof(PollingTask))]
public class PollingTaskTest
{
    private static (GameData, UserData, PlayerData) CreateTestGame()
    {
        var user = new UserData(1, "steamid", "authKey");
        var player = new PlayerData(1, user.SteamId, 0, DateTime.MinValue);
        var game = new GameData(1, user, "TestGame", player, false, false);
        game.Players.Add(player);
        return (game, user, player);
    }

    [Test]
    public async Task ShouldNotPollIfGameIsNotInAnyChats()
    {
        (GameData game, UserData _, PlayerData _) = CreateTestGame();
        var steamClient = Mock.Of<ISteamApiClient>();
        var botClient = Mock.Of<ITelegramBotClient>();
        var database = Mock.Of<IDatabase>();
        var gmrClient = Mock.Of<IGmrClient>();
        
        var gameContainerMock = new Mock<IGameContainerService>();
        gameContainerMock.Setup(gameContainer => gameContainer.Games).Returns(new List<GameData> { game });
        
        var mockLogger = new Mock<ILogger>();
        
        var gamePollingService = new PollingTask(steamClient, botClient, gameContainerMock.Object, database, gmrClient, mockLogger.Object);
        await gamePollingService.PollGames(new CancellationToken());
        
        gameContainerMock.Verify(gameContainer => gameContainer.Games, Times.Once);
        gameContainerMock.VerifyNoOtherCalls();
        mockLogger.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ShouldLogErrorIfGameGameDataFailsToReturnGame()
    {
        (GameData game, UserData user, PlayerData player) = CreateTestGame();
        game.Chats.Add(1);
        var steamClient = Mock.Of<ISteamApiClient>();
        var botClient = Mock.Of<ITelegramBotClient>();
        var database = Mock.Of<IDatabase>();
        
        var gameContainerMock = new Mock<IGameContainerService>();
        gameContainerMock.Setup(gameContainer => gameContainer.Games).Returns(new List<GameData> { game });
        
        var mockLogger = new Mock<ILogger>();
        
        var gmrClientMock = new Mock<IGmrClient>();
        gmrClientMock.Setup(
                g => g.GetGameData(
                    It.Is<long>(x => x  == game.GameId),
                    It.Is<string>(x => x  == player.SteamId),
                    It.Is<string>(x => x  == user.AuthKey)
                )
            )
            .ReturnsAsync((PackagedGame?)null)
            .Verifiable(Times.Once);
        
        var gamePollingService = new PollingTask(
            steamClient,
            botClient,
            gameContainerMock.Object,
            database,
            gmrClientMock.Object,
            mockLogger.Object);
        
        await gamePollingService.PollGames(new CancellationToken());
        
        gmrClientMock.VerifyAll();
        gmrClientMock.VerifyNoOtherCalls();
        gameContainerMock.Verify(gameContainer => gameContainer.Games, Times.Once);
        gameContainerMock.VerifyNoOtherCalls();
        mockLogger.Verify(
            logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((@object, @type) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once
        );
        mockLogger.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ShouldLogWarningAndLogErrorIfOwnerHasLeftGameAndNoOtherPlayers()
    {
        (GameData game, UserData user, PlayerData player) = CreateTestGame();
        var player2 = new PlayerData(game.GameId, "steamid2", 0, DateTime.MinValue);
        game.Players.Add(player2);
        game.Chats.Add(1);
        var steamClient = Mock.Of<ISteamApiClient>();
        var botClient = Mock.Of<ITelegramBotClient>();
        var database = Mock.Of<IDatabase>();
        
        var gameContainerMock = new Mock<IGameContainerService>();
        gameContainerMock.Setup(gameContainer => gameContainer.Games).Returns(new List<GameData> { game });
        
        var mockLogger = new Mock<ILogger>();
        
        var gmrClientMock = new Mock<IGmrClient>();
        gmrClientMock.Setup(
                g => g.GetGameData(
                    It.Is<long>(x => x  == game.GameId),
                    It.Is<string>(x => x  == player.SteamId),
                    It.Is<string>(x => x  == user.AuthKey)
                )
            )
            .Throws(new MissingOwnerException())
            .Verifiable(Times.Once);
        
        var gamePollingService = new PollingTask(
            steamClient,
            botClient,
            gameContainerMock.Object,
            database,
            gmrClientMock.Object,
            mockLogger.Object);
        
        await gamePollingService.PollGames(new CancellationToken());
        
        gmrClientMock.VerifyAll();
        gmrClientMock.VerifyNoOtherCalls();
        gameContainerMock.Verify(gameContainer => gameContainer.Games, Times.Once);
        gameContainerMock.VerifyNoOtherCalls();
        mockLogger.Verify(
            logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((@object, @type) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once
        );
        mockLogger.Verify(
            logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((@object, @type) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once
        );
        mockLogger.VerifyNoOtherCalls();
        
        Assert.That(game.Owner.SteamId, Is.EqualTo(player.SteamId));
    }

    [Test]
    public async Task ShouldLogWarningAndChangeOwnerIfOwnerHasLeftGame()
    {
        (GameData game, UserData user, PlayerData player) = CreateTestGame();
        var user2 = new UserData(2, "steamid2", "authKey2");
        var player2 = new PlayerData(game.GameId, user2.SteamId, 0, DateTime.MinValue) { User = user2 };
        game.Players.Add(player2);
        game.Chats.Add(1);
        var steamClient = Mock.Of<ISteamApiClient>();
        var botClient = Mock.Of<ITelegramBotClient>();
        var database = Mock.Of<IDatabase>();
        
        var gameContainerMock = new Mock<IGameContainerService>();
        gameContainerMock.Setup(gameContainer => gameContainer.Games).Returns(new List<GameData> { game });
        
        var mockLogger = new Mock<ILogger>();
        
        var gmrClientMock = new Mock<IGmrClient>();
        gmrClientMock.Setup(
                g => g.GetGameData(
                    It.Is<long>(x => x  == game.GameId),
                    It.Is<string>(x => x  == player.SteamId),
                    It.Is<string>(x => x  == user.AuthKey)
                )
            )
            .Throws(new MissingOwnerException())
            .Verifiable(Times.Once);
        
        var gamePollingService = new PollingTask(
            steamClient,
            botClient,
            gameContainerMock.Object,
            database,
            gmrClientMock.Object,
            mockLogger.Object);
        
        await gamePollingService.PollGames(new CancellationToken());
        
        gmrClientMock.VerifyAll();
        gmrClientMock.VerifyNoOtherCalls();
        gameContainerMock.Verify(gameContainer => gameContainer.Games, Times.Once);
        gameContainerMock.VerifyNoOtherCalls();
        mockLogger.Verify(
            logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((@object, @type) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once
        );
        mockLogger.VerifyNoOtherCalls();
        
        Assert.That(game.Owner.SteamId, Is.EqualTo(player2.SteamId));
    }
}