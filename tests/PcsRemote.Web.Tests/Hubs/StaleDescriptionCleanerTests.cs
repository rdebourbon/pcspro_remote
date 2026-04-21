using FluentAssertions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public sealed class StaleDescriptionCleanerTests
{
    private Mock<IPcsProAutomationService> _automationMock = null!;
    private Mock<IOperationCoordinatorService> _coordinatorMock = null!;

    [TestInitialize]
    public void SetUp()
    {
        _automationMock = new Mock<IPcsProAutomationService>();
        _coordinatorMock = new Mock<IOperationCoordinatorService>();
    }

    private StaleDescriptionCleaner CreateCleaner() =>
        new(_automationMock.Object, _coordinatorMock.Object);

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  StateChanged while coordinator is idle → ClearStaleDescription called
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StateChanged_CoordinatorIdle_CallsClearStaleDescription()
    {
        _coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(false);

        var cleaner = CreateCleaner();
        await cleaner.StartAsync(CancellationToken.None);

        _automationMock.Raise(a => a.StateChanged += null, this, PcsProState.MatchLoaded);

        _coordinatorMock.Verify(c => c.ClearStaleDescription(), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  StateChanged while coordinator is busy → ClearStaleDescription NOT called
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StateChanged_CoordinatorBusy_DoesNotCallClearStaleDescription()
    {
        _coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(true);

        var cleaner = CreateCleaner();
        await cleaner.StartAsync(CancellationToken.None);

        _automationMock.Raise(a => a.StateChanged += null, this, PcsProState.MatchLoaded);

        _coordinatorMock.Verify(c => c.ClearStaleDescription(), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  StopAsync unsubscribes → no further calls after stop
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_Unsubscribes_NoClearAfterStop()
    {
        _coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(false);

        var cleaner = CreateCleaner();
        await cleaner.StartAsync(CancellationToken.None);
        await cleaner.StopAsync(CancellationToken.None);

        _automationMock.Raise(a => a.StateChanged += null, this, PcsProState.MatchLoaded);

        _coordinatorMock.Verify(c => c.ClearStaleDescription(), Times.Never);
    }
}
