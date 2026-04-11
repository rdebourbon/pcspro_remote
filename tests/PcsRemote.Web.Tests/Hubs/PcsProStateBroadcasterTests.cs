using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public class PcsProStateBroadcasterTests
{
    private Mock<IPcsProAutomationService> _automationServiceMock = null!;
    private Mock<IHubContext<PcsProHub>> _hubContextMock = null!;
    private Mock<IHubClients> _hubClientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;

    [TestInitialize]
    public void SetUp()
    {
        _automationServiceMock = new Mock<IPcsProAutomationService>();
        _hubContextMock = new Mock<IHubContext<PcsProHub>>();
        _hubClientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();

        _hubContextMock.Setup(h => h.Clients).Returns(_hubClientsMock.Object);
        _hubClientsMock.Setup(c => c.All).Returns(_allClientsMock.Object);
    }

    [TestMethod]
    public async Task StartAsync_ThenStateChanged_BroadcastsNewStateToAllClients()
    {
        var tcs = new TaskCompletionSource<bool>();
        _allClientsMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => tcs.SetResult(true))
            .Returns(Task.CompletedTask);

        var broadcaster = new PcsProStateBroadcaster(_automationServiceMock.Object, _hubContextMock.Object);
        await broadcaster.StartAsync(CancellationToken.None);

        _automationServiceMock.Raise(s => s.StateChanged += null, this, PcsProState.MatchLoaded);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveStateUpdate,
                It.Is<object[]>(args => args.Length == 1 && (PcsProState)args[0] == PcsProState.MatchLoaded),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_ThenStateChanged_SendsCorrectClientMethodName()
    {
        var tcs = new TaskCompletionSource<bool>();
        _allClientsMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => tcs.SetResult(true))
            .Returns(Task.CompletedTask);

        var broadcaster = new PcsProStateBroadcaster(_automationServiceMock.Object, _hubContextMock.Object);
        await broadcaster.StartAsync(CancellationToken.None);

        _automationServiceMock.Raise(s => s.StateChanged += null, this, PcsProState.LoginScreen);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveStateUpdate,
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task StopAsync_UnsubscribesFromStateChanged_NoFurtherBroadcasts()
    {
        var broadcaster = new PcsProStateBroadcaster(_automationServiceMock.Object, _hubContextMock.Object);
        await broadcaster.StartAsync(CancellationToken.None);
        await broadcaster.StopAsync(CancellationToken.None);

        // Unsubscription is synchronous — event raise is a no-op immediately after StopAsync
        _automationServiceMock.Raise(s => s.StateChanged += null, this, PcsProState.Error);

        _allClientsMock.Verify(
            c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
