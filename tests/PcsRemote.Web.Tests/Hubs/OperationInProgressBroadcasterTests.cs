using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public sealed class OperationInProgressBroadcasterTests
{
    private Mock<IOperationCoordinatorService> _coordinatorMock = null!; // Set in [TestInitialize]
    private Mock<IPcsProAutomationService> _automationMock = null!;      // Set in [TestInitialize]
    private Mock<IHubContext<PcsProHub>> _hubContextMock = null!;        // Set in [TestInitialize]
    private Mock<IHubClients> _hubClientsMock = null!;                   // Set in [TestInitialize]
    private Mock<IClientProxy> _allClientsMock = null!;                  // Set in [TestInitialize]
    private Mock<ILogger<OperationInProgressBroadcaster>> _loggerMock = null!; // Set in [TestInitialize]

    [TestInitialize]
    public void SetUp()
    {
        _coordinatorMock = new Mock<IOperationCoordinatorService>();
        _automationMock = new Mock<IPcsProAutomationService>();
        _hubContextMock = new Mock<IHubContext<PcsProHub>>();
        _hubClientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _loggerMock = new Mock<ILogger<OperationInProgressBroadcaster>>();

        _hubContextMock.Setup(h => h.Clients).Returns(_hubClientsMock.Object);
        _hubClientsMock.Setup(c => c.All).Returns(_allClientsMock.Object);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-9  OperationInProgressChanged(true) → broadcasts true to all clients
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OperationInProgressChanged_True_BroadcastsTrueToAllClients()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _allClientsMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var broadcaster = new OperationInProgressBroadcaster(
            _coordinatorMock.Object, _automationMock.Object, _hubContextMock.Object, _loggerMock.Object);
        await broadcaster.StartAsync(CancellationToken.None);

        _coordinatorMock.Raise(c => c.OperationInProgressChanged += null, this, true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveOperationInProgressUpdate,
                It.Is<object[]>(args => args.Length == 1 && (bool)args[0] == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-10  OperationInProgressChanged(false) → broadcasts false to all clients
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OperationInProgressChanged_False_BroadcastsFalseToAllClients()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _allClientsMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var broadcaster = new OperationInProgressBroadcaster(
            _coordinatorMock.Object, _automationMock.Object, _hubContextMock.Object, _loggerMock.Object);
        await broadcaster.StartAsync(CancellationToken.None);

        _coordinatorMock.Raise(c => c.OperationInProgressChanged += null, this, false);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveOperationInProgressUpdate,
                It.Is<object[]>(args => args.Length == 1 && (bool)args[0] == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-11  StopAsync unsubscribes — no further broadcasts after stop
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_Unsubscribes_NoFurtherBroadcasts()
    {
        var broadcaster = new OperationInProgressBroadcaster(
            _coordinatorMock.Object, _automationMock.Object, _hubContextMock.Object, _loggerMock.Object);
        await broadcaster.StartAsync(CancellationToken.None);
        await broadcaster.StopAsync(CancellationToken.None);

        _coordinatorMock.Raise(c => c.OperationInProgressChanged += null, this, true);

        _allClientsMock.Verify(
            c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-12  Hub broadcast throws → exception logged at Error level, not propagated
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HubBroadcastThrows_ExceptionLoggedAtError_NotPropagated()
    {
        var thrown = new InvalidOperationException("hub error");
        var logTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _allClientsMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        _loggerMock
            .Setup(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception>(e => e == thrown),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => logTcs.TrySetResult(true));

        var broadcaster = new OperationInProgressBroadcaster(
            _coordinatorMock.Object, _automationMock.Object, _hubContextMock.Object, _loggerMock.Object);
        await broadcaster.StartAsync(CancellationToken.None);

        // Raising the event must not throw on the calling thread
        var act = () => _coordinatorMock.Raise(c => c.OperationInProgressChanged += null, this, true);
        act.Should().NotThrow();

        await logTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception>(e => e == thrown),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
