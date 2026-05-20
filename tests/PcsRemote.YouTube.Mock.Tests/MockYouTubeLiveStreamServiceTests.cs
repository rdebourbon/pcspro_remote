using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PcsRemote.Core;
using PcsRemote.YouTube.Mock;

namespace PcsRemote.YouTube.Mock.Tests;

[TestClass]
public class MockYouTubeLiveStreamServiceTests
{
    private MockYouTubeOptions _options = null!; // Set in [TestInitialize] before every test
    private Mock<IPcsProAutomationService> _automationMock = null!; // Set in [TestInitialize] before every test
    private BroadcastTitleRenderer _titleRenderer = null!; // Set in [TestInitialize] before every test
    private MockYouTubeLiveStreamService _sut = null!; // Set in [TestInitialize] before every test

    private static readonly MatchInfo TestMatch = new(
        MatchId: "12345",
        HomeTeam: "High Halstow CC",
        AwayTeam: "Cliffe CC",
        MatchType: "League",
        MatchDate: new DateOnly(2025, 7, 12));

    [TestInitialize]
    public void Setup()
    {
        _options = new MockYouTubeOptions
        {
            StartDelayMs = 50,
            StopDelayMs = 50,
            SimulateStartFailure = false,
            SimulateActiveOnStartup = false,
        };

        _automationMock = new Mock<IPcsProAutomationService>();
        _automationMock.Setup(a => a.LoadedMatch).Returns(TestMatch);

        _titleRenderer = new BroadcastTitleRenderer(
            NullLogger<BroadcastTitleRenderer>.Instance);

        _sut = CreateService();
    }

    private MockYouTubeLiveStreamService CreateService()
    {
        return new MockYouTubeLiveStreamService(
            Options.Create(_options),
            NullLogger<MockYouTubeLiveStreamService>.Instance,
            _titleRenderer,
            _automationMock.Object);
    }

    // AC-1: StartStreamAsync transitions Idle → Starting → Live
    [TestMethod]
    public async Task StartStreamAsync_FromIdle_TransitionsToLive()
    {
        await _sut.InitializeAsync();
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);

        await _sut.StartStreamAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Live);
        _sut.CurrentBroadcast.Should().NotBeNull();
        _sut.CurrentBroadcast!.Title.Should().Be("High Halstow CC vs Cliffe CC"); // Asserted non-null on previous line
    }

    // AC-2: StopStreamAsync transitions Live → Stopping → Idle
    [TestMethod]
    public async Task StopStreamAsync_FromLive_TransitionsToIdle()
    {
        await _sut.InitializeAsync();
        await _sut.StartStreamAsync();
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Live);

        await _sut.StopStreamAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        _sut.CurrentBroadcast.Should().BeNull();
    }

    // AC-3: Cancel during Starting resets to Idle
    [TestMethod]
    public async Task StartStreamAsync_CancelledDuringStarting_ResetsToIdle()
    {
        _options.StartDelayMs = 5000;
        _sut = CreateService();
        await _sut.InitializeAsync();

        using var cts = new CancellationTokenSource(50);
        var act = () => _sut.StartStreamAsync(cts.Token);

        await act.Should().NotThrowAsync();
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        _sut.CurrentBroadcast.Should().BeNull();
    }

    // AC-4: ResetAsync from Error returns to Idle
    [TestMethod]
    public async Task ResetAsync_FromError_TransitionsToIdle()
    {
        _options.SimulateStartFailure = true;
        _sut = CreateService();
        await _sut.InitializeAsync();
        await _sut.StartStreamAsync();
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Error);

        await _sut.ResetAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        _sut.CurrentBroadcast.Should().BeNull();
    }

    // AC-5: ResetAsync when not Error is a no-op
    [TestMethod]
    public async Task ResetAsync_WhenNotError_NoOp()
    {
        await _sut.InitializeAsync();
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);

        await _sut.ResetAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
    }

    // AC-6: StopStreamAsync when not Live or Starting is a no-op
    [TestMethod]
    public async Task StopStreamAsync_WhenIdle_NoOp()
    {
        await _sut.InitializeAsync();
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);

        await _sut.StopStreamAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
    }

    // AC-7: StatusChanged fires correct StreamStateSnapshot at each transition
    [TestMethod]
    public async Task StartStreamAsync_FiresStatusChangedAtEachTransition()
    {
        await _sut.InitializeAsync();
        var snapshots = new List<StreamStateSnapshot>();
        _sut.StatusChanged += (_, s) => snapshots.Add(s);

        await _sut.StartStreamAsync();

        snapshots.Should().HaveCount(2);
        snapshots[0].Status.Should().Be(LiveStreamStatus.Starting);
        snapshots[0].CurrentBroadcast.Should().NotBeNull();
        snapshots[0].ErrorMessage.Should().BeNull();
        snapshots[1].Status.Should().Be(LiveStreamStatus.Live);
        snapshots[1].CurrentBroadcast.Should().NotBeNull();
        snapshots[1].ErrorMessage.Should().BeNull();
    }

    // AC-8: SimulateStartFailure causes Starting → Error with ErrorMessage
    [TestMethod]
    public async Task StartStreamAsync_SimulateFailure_TransitionsToError()
    {
        _options.SimulateStartFailure = true;
        _sut = CreateService();
        await _sut.InitializeAsync();

        var snapshots = new List<StreamStateSnapshot>();
        _sut.StatusChanged += (_, s) => snapshots.Add(s);

        await _sut.StartStreamAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Error);
        _sut.CurrentBroadcast.Should().BeNull();
        snapshots.Should().Contain(s =>
            s.Status == LiveStreamStatus.Error &&
            s.ErrorMessage == "Simulated start failure" &&
            s.CurrentBroadcast == null);
    }

    // AC-9: SimulateActiveOnStartup causes InitializeAsync to reconcile to Live
    [TestMethod]
    public async Task InitializeAsync_SimulateActiveOnStartup_TransitionsToLive()
    {
        _options.SimulateActiveOnStartup = true;
        _sut = CreateService();

        await _sut.InitializeAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Live);
        _sut.CurrentBroadcast.Should().NotBeNull();
        _sut.CurrentBroadcast!.Title.Should().Be("Reconciled broadcast"); // Asserted non-null on previous line
    }

    // AC-10: StartStreamAsync when no LoadedMatch throws InvalidOperationException
    [TestMethod]
    public async Task StartStreamAsync_NoLoadedMatch_ThrowsInvalidOperationException()
    {
        _automationMock.Setup(a => a.LoadedMatch).Returns((MatchInfo?)null);
        _sut = CreateService();
        await _sut.InitializeAsync();

        var act = () => _sut.StartStreamAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no match*");
    }

    // AC-11: StartStreamAsync generates title via BroadcastTitleRenderer
    [TestMethod]
    public async Task StartStreamAsync_UsesRendererForTitle()
    {
        await _sut.InitializeAsync();

        await _sut.StartStreamAsync();

        _sut.CurrentBroadcast.Should().NotBeNull();
        _sut.CurrentBroadcast!.Title.Should().Be("High Halstow CC vs Cliffe CC"); // Asserted non-null on previous line
    }

    // AC-12: CurrentBroadcast is non-null when Live, null when Idle
    [TestMethod]
    public async Task CurrentBroadcast_NullWhenIdle_PopulatedWhenLive()
    {
        await _sut.InitializeAsync();
        _sut.CurrentBroadcast.Should().BeNull();

        await _sut.StartStreamAsync();
        _sut.CurrentBroadcast.Should().NotBeNull();

        await _sut.StopStreamAsync();
        _sut.CurrentBroadcast.Should().BeNull();
    }

    // AC-13: StartStreamAsync when not Idle throws InvalidOperationException
    [TestMethod]
    public async Task StartStreamAsync_WhenNotIdle_ThrowsInvalidOperationException()
    {
        await _sut.InitializeAsync();
        await _sut.StartStreamAsync();
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Live);

        var act = () => _sut.StartStreamAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Idle*");
    }

    // AC-14: Build succeeds with 0 errors, 0 warnings — verified at build time

    // AC-15: All existing tests continue to pass — verified at test time

    // AC-16: InitializeAsync with default options leaves status Idle
    [TestMethod]
    public async Task InitializeAsync_DefaultOptions_RemainsIdle()
    {
        var snapshots = new List<StreamStateSnapshot>();
        _sut.StatusChanged += (_, s) => snapshots.Add(s);

        await _sut.InitializeAsync();

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        _sut.CurrentBroadcast.Should().BeNull();
        snapshots.Should().BeEmpty("StatusChanged should not fire when remaining Idle");
    }

    // Verify StopStreamAsync from Starting state works (IS deviation)
    [TestMethod]
    public async Task StopStreamAsync_FromStarting_TransitionsToIdle()
    {
        _options.StartDelayMs = 5000;
        _sut = CreateService();
        await _sut.InitializeAsync();

        var startingReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.StatusChanged += (_, s) =>
        {
            if (s.Status == LiveStreamStatus.Starting)
            {
                startingReached.TrySetResult();
            }
        };

        var startTask = _sut.StartStreamAsync();
        await startingReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Starting);

        await _sut.StopStreamAsync();
        await startTask;

        _sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        _sut.CurrentBroadcast.Should().BeNull();
    }

    // Verify StatusChanged fires for Stop-flow transitions (Stopping → Idle)
    [TestMethod]
    public async Task StopStreamAsync_FiresStatusChangedAtEachTransition()
    {
        await _sut.InitializeAsync();
        await _sut.StartStreamAsync();

        var snapshots = new List<StreamStateSnapshot>();
        _sut.StatusChanged += (_, s) => snapshots.Add(s);

        await _sut.StopStreamAsync();

        snapshots.Should().HaveCount(2);
        snapshots[0].Status.Should().Be(LiveStreamStatus.Stopping);
        snapshots[1].Status.Should().Be(LiveStreamStatus.Idle);
        snapshots[1].CurrentBroadcast.Should().BeNull();
    }

    // IS-009 S-002: StartStreamAsync calls StartStreamingAsync on automation service
    [TestMethod]
    public async Task StartStreamAsync_CallsStartStreamingOnAutomationService()
    {
        await _sut.InitializeAsync();

        await _sut.StartStreamAsync();

        _automationMock.Verify(
            a => a.StartStreamingAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // IS-009 S-002: StopStreamAsync calls StopStreamingAsync on automation service
    [TestMethod]
    public async Task StopStreamAsync_CallsStopStreamingOnAutomationService()
    {
        await _sut.InitializeAsync();
        await _sut.StartStreamAsync();

        await _sut.StopStreamAsync();

        _automationMock.Verify(
            a => a.StopStreamingAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // S-005 TC-2: RunOAuthSetupAsync returns true
    [TestMethod]
    public async Task RunOAuthSetupAsync_ReturnsTrue()
    {
        var result = await _sut.RunOAuthSetupAsync();

        result.Should().BeTrue();
    }

    // S-003 AC-9: Mock always reports Ready
    [TestMethod]
    public void Availability_AlwaysReady()
    {
        _sut.Availability.Should().Be(YouTubeAvailability.Ready);
    }

    // S-003 AC-9: InitializeAsync fires AuthStatusChanged with Ready
    [TestMethod]
    public async Task InitializeAsync_FiresAuthStatusChanged_Ready()
    {
        var snapshots = new List<YouTubeAuthStatusSnapshot>();
        _sut.AuthStatusChanged += (_, s) => snapshots.Add(s);

        await _sut.InitializeAsync();

        snapshots.Should().ContainSingle()
            .Which.Availability.Should().Be(YouTubeAvailability.Ready);
    }

    // S-003 AC-9: RunOAuthSetupAsync fires AuthStatusChanged with Ready
    [TestMethod]
    public async Task RunOAuthSetupAsync_FiresAuthStatusChanged_Ready()
    {
        var snapshots = new List<YouTubeAuthStatusSnapshot>();
        _sut.AuthStatusChanged += (_, s) => snapshots.Add(s);

        await _sut.RunOAuthSetupAsync();

        snapshots.Should().ContainSingle()
            .Which.Availability.Should().Be(YouTubeAvailability.Ready);
    }

    // IS-020 S-002 TC-1: TokenExpiryApproaching never fires during full init → start → stop lifecycle;
    // Availability remains Ready throughout.
    [TestMethod]
    public async Task TokenExpiryApproaching_DuringFullLifecycle_NeverFires()
    {
        var fired = false;
        _sut.TokenExpiryApproaching += (_, _) => fired = true;

        _sut.Availability.Should().Be(YouTubeAvailability.Ready);

        await _sut.InitializeAsync();
        _sut.Availability.Should().Be(YouTubeAvailability.Ready);

        await _sut.StartStreamAsync();
        _sut.Availability.Should().Be(YouTubeAvailability.Ready);

        await _sut.StopStreamAsync();
        _sut.Availability.Should().Be(YouTubeAvailability.Ready);

        fired.Should().BeFalse("mock tokens do not expire; TokenExpiryApproaching must never fire");
    }

    // IS-020 S-002 TC-2: TokenExpiryApproaching never fires when RunOAuthSetupAsync completes.
    [TestMethod]
    public async Task TokenExpiryApproaching_AfterRunOAuthSetupAsync_NeverFires()
    {
        var fired = false;
        _sut.TokenExpiryApproaching += (_, _) => fired = true;

        await _sut.RunOAuthSetupAsync();

        fired.Should().BeFalse("mock tokens do not expire; TokenExpiryApproaching must never fire");
    }
}
