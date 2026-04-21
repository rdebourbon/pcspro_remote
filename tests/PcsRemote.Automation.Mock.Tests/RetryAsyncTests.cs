using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock.Tests;

[TestClass]
public sealed class RetryAsyncTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
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

    /// <summary>
    /// Drives the SUT into Error state using <see cref="MockForcedErrorMode.LaunchingToLoginScreen"/>.
    /// Returns the SUT already in Error state with a non-null LastErrorReason.
    /// </summary>
    private static async Task<MockPcsProAutomationService> CreateInErrorStateAsync()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.LaunchingToLoginScreen);
        await sut.LaunchAndLoginAsync();
        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty();
        return sut;
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-1: RetryAsync from Error transitions to NotRunning then launches
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RetryAsync_FromError_TransitionsToNotRunning_ThenLaunches()
    {
        var sut = await CreateInErrorStateAsync();
        var events = new List<PcsProState>();
        sut.StateChanged += (_, s) => events.Add(s);

        await sut.RetryAsync();

        // Error → NotRunning transition must be fired, then launch sequence starts
        events.Should().StartWith(new[] { PcsProState.NotRunning });
        // Launch sequence should have progressed (at minimum Launching was fired)
        events.Should().Contain(PcsProState.Launching,
            "LaunchAndLoginAsync must be called after transitioning to NotRunning");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2: RetryAsync from non-Error state throws InvalidOperationException
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RetryAsync_FromNonError_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        // SUT starts in NotRunning; drive to MatchLoaded
        await sut.LaunchAndLoginAsync();
        var match = new MatchInfo("m1", "Home", "Away", "T20", DateOnly.FromDateTime(DateTime.Today));
        await sut.LoadMatchAsync(match);
        sut.CurrentState.Should().Be(PcsProState.MatchLoaded);

        var act = async () => await sut.RetryAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "RetryAsync requires the service to be in Error state");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3: RetryAsync clears LastErrorReason on transition to NotRunning
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RetryAsync_ClearsLastErrorReason()
    {
        var sut = await CreateInErrorStateAsync();
        string? reasonAtNotRunning = null;

        sut.StateChanged += (_, state) =>
        {
            if (state == PcsProState.NotRunning)
                reasonAtNotRunning = sut.LastErrorReason;
        };

        await sut.RetryAsync();

        reasonAtNotRunning.Should().BeNull(
            "LastErrorReason must be cleared before the NotRunning transition fires");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-17: ForcedError LaunchingToLoginScreen surfaces a reason
    // (AC-8 H-SC-8 completeness — covered in depth by LastErrorReasonTests;
    //  these TCs verify presence from the spec's test plan perspective)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ForcedError_LaunchingToLoginScreen_SurfacesReason()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.LaunchingToLoginScreen);
        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty(
            "LaunchingToLoginScreen mode must surface a reason string (H-SC-8)");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-18: ForcedError LoginScreenToMatchSelection surfaces a reason
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ForcedError_LoginScreenToMatchSelection_SurfacesReason()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.LoginScreenToMatchSelection);
        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty(
            "LoginScreenToMatchSelection mode must surface a reason string (H-SC-8)");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-19: ForcedError MatchSelectionSearchingToReady surfaces a reason
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ForcedError_MatchSelectionSearchingToReady_SurfacesReason()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.MatchSelectionSearchingToReady);
        await sut.LaunchAndLoginAsync();
        var match = new MatchInfo("m1", "Home", "Away", "T20", DateOnly.FromDateTime(DateTime.Today));
        await sut.LoadMatchAsync(match);

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty(
            "MatchSelectionSearchingToReady mode must surface a reason string (H-SC-8)");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-20: ForcedError MatchSelectionToLoaded surfaces a reason
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ForcedError_MatchSelectionToLoaded_SurfacesReason()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.MatchSelectionToLoaded);
        await sut.LaunchAndLoginAsync();
        var match = new MatchInfo("m1", "Home", "Away", "T20", DateOnly.FromDateTime(DateTime.Today));
        await sut.LoadMatchAsync(match);

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty(
            "MatchSelectionToLoaded mode must surface a reason string (H-SC-8)");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-21: ForcedError UnexpectedDialog surfaces a reason
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ForcedError_UnexpectedDialog_SurfacesReason()
    {
        var sut = CreateSut(o => o.ForcedErrorMode = MockForcedErrorMode.UnexpectedDialog);
        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().NotBeNullOrEmpty(
            "UnexpectedDialog mode must surface a reason string (H-SC-8)");
    }
}
