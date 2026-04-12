using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public class PcsProHubTests
{
    private Mock<IPcsProAutomationService> _automationServiceMock = null!;
    private Mock<IManualModeService> _manualModeServiceMock = null!;
    private Mock<IHubCallerClients> _clientsMock = null!;
    private Mock<ISingleClientProxy> _callerMock = null!;

    [TestInitialize]
    public void SetUp()
    {
        _automationServiceMock = new Mock<IPcsProAutomationService>();
        _manualModeServiceMock = new Mock<IManualModeService>();
        _clientsMock = new Mock<IHubCallerClients>();
        _callerMock = new Mock<ISingleClientProxy>();

        _clientsMock.Setup(c => c.Caller).Returns(_callerMock.Object);
        _automationServiceMock.Setup(s => s.CurrentState).Returns(PcsProState.MatchSelection);
        _manualModeServiceMock.Setup(s => s.IsManualModeActive).Returns(false);
    }

    private PcsProHub CreateHub()
    {
        var hub = new PcsProHub(_automationServiceMock.Object, _manualModeServiceMock.Object);
        hub.Clients = _clientsMock.Object;
        return hub;
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

    // ──────────────────────────────────────────────────────────────────────
    // TC-10a  OnConnectedAsync — manual mode inactive — sends false to caller
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnConnectedAsync_ManualModeInactive_SendsFalseManualModeUpdateToCaller()
    {
        _manualModeServiceMock.Setup(s => s.IsManualModeActive).Returns(false);
        using var hub = CreateHub();
        await hub.OnConnectedAsync();

        _callerMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveManualModeUpdate,
                It.Is<object[]>(args => args.Length == 1 && (bool)args[0] == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-10b  OnConnectedAsync — manual mode active — sends true to caller
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnConnectedAsync_ManualModeActive_SendsTrueManualModeUpdateToCaller()
    {
        _manualModeServiceMock.Setup(s => s.IsManualModeActive).Returns(true);
        using var hub = CreateHub();
        await hub.OnConnectedAsync();

        _callerMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveManualModeUpdate,
                It.Is<object[]>(args => args.Length == 1 && (bool)args[0] == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}


