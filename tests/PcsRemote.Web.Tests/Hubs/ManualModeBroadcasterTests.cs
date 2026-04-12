using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public sealed class ManualModeBroadcasterTests
{
    private Mock<IManualModeService> _manualModeServiceMock = null!;
    private Mock<IHubContext<PcsProHub>> _hubContextMock = null!;
    private Mock<IHubClients> _hubClientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private ILogger<ManualModeBroadcaster> _logger = null!;

    [TestInitialize]
    public void SetUp()
    {
        _manualModeServiceMock = new Mock<IManualModeService>();
        _hubContextMock = new Mock<IHubContext<PcsProHub>>();
        _hubClientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _logger = new Mock<ILogger<ManualModeBroadcaster>>().Object;

        _hubContextMock.Setup(h => h.Clients).Returns(_hubClientsMock.Object);
        _hubClientsMock.Setup(c => c.All).Returns(_allClientsMock.Object);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-8  ManualModeChanged fires true → broadcasts true to all clients
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ManualModeChanged_True_BroadcastsTrueToAllClients()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _allClientsMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var broadcaster = new ManualModeBroadcaster(_manualModeServiceMock.Object, _hubContextMock.Object, _logger);
        await broadcaster.StartAsync(CancellationToken.None);

        _manualModeServiceMock.Raise(s => s.ManualModeChanged += null, this, true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveManualModeUpdate,
                It.Is<object[]>(args => args.Length == 1 && (bool)args[0] == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-9  ManualModeChanged fires false → broadcasts false to all clients
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ManualModeChanged_False_BroadcastsFalseToAllClients()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _allClientsMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var broadcaster = new ManualModeBroadcaster(_manualModeServiceMock.Object, _hubContextMock.Object, _logger);
        await broadcaster.StartAsync(CancellationToken.None);

        _manualModeServiceMock.Raise(s => s.ManualModeChanged += null, this, false);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveManualModeUpdate,
                It.Is<object[]>(args => args.Length == 1 && (bool)args[0] == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-10  StopAsync unsubscribes — no further broadcasts after stop
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_UnsubscribesFromManualModeChanged_NoFurtherBroadcasts()
    {
        var broadcaster = new ManualModeBroadcaster(_manualModeServiceMock.Object, _hubContextMock.Object, _logger);
        await broadcaster.StartAsync(CancellationToken.None);
        await broadcaster.StopAsync(CancellationToken.None);

        _manualModeServiceMock.Raise(s => s.ManualModeChanged += null, this, true);

        _allClientsMock.Verify(
            c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
