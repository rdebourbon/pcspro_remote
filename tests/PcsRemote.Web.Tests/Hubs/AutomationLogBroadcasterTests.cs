using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public class AutomationLogBroadcasterTests
{
    private Mock<IAutomationLogService> _logServiceMock = null!;
    private Mock<IHubContext<PcsProHub>> _hubContextMock = null!;
    private Mock<IHubClients> _hubClientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<ILogger<AutomationLogBroadcaster>> _loggerMock = null!;

    [TestInitialize]
    public void SetUp()
    {
        _logServiceMock = new Mock<IAutomationLogService>();
        _hubContextMock = new Mock<IHubContext<PcsProHub>>();
        _hubClientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _loggerMock = new Mock<ILogger<AutomationLogBroadcaster>>();

        _hubClientsMock.Setup(c => c.All).Returns(_allClientsMock.Object);
        _hubContextMock.Setup(c => c.Clients).Returns(_hubClientsMock.Object);
    }

    private AutomationLogBroadcaster CreateBroadcaster() =>
        new(_logServiceMock.Object, _hubContextMock.Object, _loggerMock.Object);

    // ──────────────────────────────────────────────────────────────────────
    // TC-8  Entry pushed to hub clients
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task EntryAdded_SendsEntryToAllClients()
    {
        var broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(CancellationToken.None);

        var entry = new AutomationLogEntry(
            DateTimeOffset.UtcNow, "Test action", AutomationLogOutcome.Info);

        _logServiceMock.Raise(s => s.EntryAdded += null, _logServiceMock.Object, entry);

        // Allow async handler to complete
        await Task.Delay(50);

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                PcsProHubConstants.ReceiveAutomationLogEntry,
                It.Is<object[]>(args => args.Length == 1 && entry.Equals(args[0])),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-9  Subscribe on start, unsubscribe on stop
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_UnsubscribesFromEvents()
    {
        var broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(CancellationToken.None);
        await broadcaster.StopAsync(CancellationToken.None);

        var entry = new AutomationLogEntry(
            DateTimeOffset.UtcNow, "After stop", AutomationLogOutcome.Info);

        _logServiceMock.Raise(s => s.EntryAdded += null, _logServiceMock.Object, entry);

        await Task.Delay(50);

        _allClientsMock.Verify(
            c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
