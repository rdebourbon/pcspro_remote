using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public class PcsProHubTests
{
    private Mock<IConnectionTracker> _trackerMock = null!;
    private Mock<IPcsProAutomationService> _automationServiceMock = null!;
    private Mock<IHubCallerClients> _clientsMock = null!;
    private Mock<ISingleClientProxy> _callerMock = null!;

    [TestInitialize]
    public void SetUp()
    {
        _trackerMock = new Mock<IConnectionTracker>();
        _automationServiceMock = new Mock<IPcsProAutomationService>();
        _clientsMock = new Mock<IHubCallerClients>();
        _callerMock = new Mock<ISingleClientProxy>();

        _clientsMock.Setup(c => c.Caller).Returns(_callerMock.Object);
        _automationServiceMock.Setup(s => s.CurrentState).Returns(PcsProState.MatchSelection);
    }

    private PcsProHub CreateHub()
    {
        var hub = new PcsProHub(_trackerMock.Object, _automationServiceMock.Object);
        hub.Clients = _clientsMock.Object;
        return hub;
    }

    [TestMethod]
    public async Task OnConnectedAsync_IncrementsConnectionCountExactlyOnce()
    {
        using var hub = CreateHub();
        await hub.OnConnectedAsync();
        _trackerMock.Verify(t => t.Increment(), Times.Once);
    }

    [TestMethod]
    public async Task OnConnectedAsync_SendsCurrentStateToCaller()
    {
        using var hub = CreateHub();
        await hub.OnConnectedAsync();

        _callerMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveStateUpdate,
                It.Is<object[]>(args => args.Length == 1 && (PcsProState)args[0] == PcsProState.MatchSelection),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task OnDisconnectedAsync_DecrementsConnectionCountExactlyOnce()
    {
        using var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);
        _trackerMock.Verify(t => t.Decrement(), Times.Once);
    }

    [TestMethod]
    public async Task OnConnectedAsync_DoesNotDecrement()
    {
        using var hub = CreateHub();
        await hub.OnConnectedAsync();
        _trackerMock.Verify(t => t.Decrement(), Times.Never);
    }

    [TestMethod]
    public async Task OnDisconnectedAsync_DoesNotIncrement()
    {
        using var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);
        _trackerMock.Verify(t => t.Increment(), Times.Never);
    }
}
