using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock.Tests;

[TestClass]
public sealed class LastErrorReasonTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────────────

    private static MockPcsProAutomationService CreateSut(
        Action<MockPcsProOptions>? configure = null)
    {
        var opts = new MockPcsProOptions();
        configure?.Invoke(opts);
        return new MockPcsProAutomationService(
            Options.Create(opts),
            NullLogger<MockPcsProAutomationService>.Instance,
            new NullAutomationLogService());
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-1 + TC-2  Compile-time contract + initial null
    // Assigning Mock to IPcsProAutomationService and reading LastErrorReason
    // proves the interface contract at compile time.
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetLastErrorReason_FreshInstance_ReturnsNull()
    {
        IPcsProAutomationService sut = CreateSut();

        sut.LastErrorReason.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  After StopAsync on an errored mock → null
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLastErrorReason_AfterStopAsyncFromErrorState_ReturnsNull()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.LaunchingToLoginScreen);
        await sut.LaunchAndLoginAsync();
        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty();

        await sut.StopAsync();

        sut.LastErrorReason.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  Launching → LoginScreen failure mode
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLastErrorReason_LaunchingToLoginScreenMode_DescribesLaunchPhase()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.LaunchingToLoginScreen);

        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty();
        sut.LastErrorReason.Should().ContainAny("login", "launch", "Login", "Launch");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  LoginScreen → MatchSelection failure mode
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLastErrorReason_LoginScreenToMatchSelectionMode_DescribesLoginPhase()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.LoginScreenToMatchSelection);

        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty();
        sut.LastErrorReason.Should().ContainAny("login", "match selection", "Login", "Match selection", "Match Selection");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-6  MatchSelectionSearching → MatchSelectionReady failure mode
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLastErrorReason_MatchSelectionSearchingToReadyMode_DescribesSearchPhase()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.MatchSelectionSearchingToReady);
        await sut.LaunchAndLoginAsync();
        var match = new MatchInfo("m1", "Home", "Away", "T20", DateOnly.FromDateTime(DateTime.Today));

        await sut.LoadMatchAsync(match);

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty();
        sut.LastErrorReason.Should().ContainAny("search", "Search", "results", "Results");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-7  MatchSelection → MatchLoaded failure mode
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLastErrorReason_MatchSelectionToLoadedMode_DescribesMatchLoadingPhase()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.MatchSelectionToLoaded);
        await sut.LaunchAndLoginAsync();
        var match = new MatchInfo("m1", "Home", "Away", "T20", DateOnly.FromDateTime(DateTime.Today));

        await sut.LoadMatchAsync(match);

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty();
        sut.LastErrorReason.Should().ContainAny("match", "Match", "open", "Open", "load", "Load");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-8  Unexpected dialog failure mode
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLastErrorReason_UnexpectedDialogMode_DescribesUnexpectedDialogPath()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.UnexpectedDialog);

        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty();
        sut.LastErrorReason.Should().ContainAny("dialog", "Dialog", "unexpected", "Unexpected");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Mode uniqueness — all five forced-mode reason strings must be distinct
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLastErrorReason_AllFiveHSC8Modes_ReasonStringsAreUnique()
    {
        var match = new MatchInfo("m1", "Home", "Away", "T20", DateOnly.FromDateTime(DateTime.Today));
        var reasons = new List<string>();

        foreach (var mode in new[]
        {
            MockForcedErrorMode.LaunchingToLoginScreen,
            MockForcedErrorMode.LoginScreenToMatchSelection,
            MockForcedErrorMode.UnexpectedDialog,
        })
        {
            var sut = CreateSut(o => o.ForcedErrorMode = mode);
            await sut.LaunchAndLoginAsync();
            sut.LastErrorReason.Should().NotBeNull("ForcedErrorMode must produce a reason string");
            reasons.Add(sut.LastErrorReason!); // null checked by assertion on preceding line
        }

        foreach (var mode in new[]
        {
            MockForcedErrorMode.MatchSelectionSearchingToReady,
            MockForcedErrorMode.MatchSelectionToLoaded,
        })
        {
            var sut = CreateSut(o => o.ForcedErrorMode = mode);
            await sut.LaunchAndLoginAsync();
            await sut.LoadMatchAsync(match);
            sut.LastErrorReason.Should().NotBeNull("ForcedErrorMode must produce a reason string");
            reasons.Add(sut.LastErrorReason!); // null checked by assertion on preceding line
        }

        reasons.Should().OnlyHaveUniqueItems("each H-SC-8 failure mode must produce a distinct reason string");
    }
}
