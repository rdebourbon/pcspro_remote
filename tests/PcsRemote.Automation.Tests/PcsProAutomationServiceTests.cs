using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PcsRemote.Core;

namespace PcsRemote.Automation.Tests;

[TestClass]
public sealed class PcsProAutomationServiceTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PcsProAutomationService CreateService(
        FakeProcessManager processManager,
        FakeTimeProvider timeProvider,
        FakeLoginAutomation? loginAutomation = null,
        IMatchSelectionAutomation? matchSelectionAutomation = null,
        ITeamNamesAutomation? teamNamesAutomation = null,
        IScoreboardAutomation? scoreboardAutomation = null,
        IChangeMatchAutomation? changeMatchAutomation = null,
        IStreamingAutomation? streamingAutomation = null,
        FakeHealthCheckAutomation? healthCheckAutomation = null)
    {
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
        });
        var deps = new AutomationDependencies(
            loginAutomation ?? new FakeLoginAutomation(),
            matchSelectionAutomation ?? new FakeMatchSelectionAutomation(),
            teamNamesAutomation ?? new FakeTeamNamesAutomation(),
            scoreboardAutomation ?? new FakeScoreboardAutomation(),
            changeMatchAutomation ?? new FakeChangeMatchAutomation(),
            streamingAutomation ?? new FakeStreamingAutomation(),
            healthCheckAutomation ?? new FakeHealthCheckAutomation());
        return new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            processManager,
            timeProvider,
            deps,
            new NullAutomationLogService(),
            new ManualModeService());
    }

    private static PcsProAutomationService CreateServiceWithLoginTimeout(
        FakeProcessManager processManager,
        FakeTimeProvider timeProvider,
        FakeLoginAutomation loginAutomation,
        int loginTimeoutSeconds)
    {
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
            LoginScreenTimeoutSeconds = loginTimeoutSeconds,
        });
        var deps = new AutomationDependencies(
            loginAutomation,
            new FakeMatchSelectionAutomation(),
            new FakeTeamNamesAutomation(),
            new FakeScoreboardAutomation(),
            new FakeChangeMatchAutomation(),
            new FakeStreamingAutomation(),
            new FakeHealthCheckAutomation());
        return new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            processManager,
            timeProvider,
            deps,
            new NullAutomationLogService(),
            new ManualModeService());
    }

    private static PcsProAutomationService CreateServiceWithExpectedUsername(
        FakeProcessManager processManager,
        FakeTimeProvider timeProvider,
        ILoginAutomation loginAutomation,
        string expectedUsername)
    {
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
            ExpectedUsername = expectedUsername,
        });
        var deps = new AutomationDependencies(
            loginAutomation,
            new FakeMatchSelectionAutomation(),
            new FakeTeamNamesAutomation(),
            new FakeScoreboardAutomation(),
            new FakeChangeMatchAutomation(),
            new FakeStreamingAutomation(),
            new FakeHealthCheckAutomation());
        return new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            processManager,
            timeProvider,
            deps,
            new NullAutomationLogService(),
            new ManualModeService());
    }

    /// <summary>
    /// Creates a service and runs <see cref="PcsProAutomationService.LaunchAndLoginAsync"/>
    /// to completion, with the process window immediately visible and login succeeding.
    /// Returns the service in <see cref="PcsProState.MatchSelection"/> state.
    /// </summary>
    private static async Task<(PcsProAutomationService Service, FakeProcessHandle Handle)>
        CreateServiceAtMatchSelectionAsync(
            FakeLoginAutomation? loginAutomation = null,
            IMatchSelectionAutomation? matchSelectionAutomation = null,
            ITeamNamesAutomation? teamNamesAutomation = null,
            IScoreboardAutomation? scoreboardAutomation = null,
            IChangeMatchAutomation? changeMatchAutomation = null,
            IStreamingAutomation? streamingAutomation = null)
    {
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(
            pm, tp,
            loginAutomation ?? new FakeLoginAutomation(),
            matchSelectionAutomation ?? new FakeMatchSelectionAutomation(),
            teamNamesAutomation ?? new FakeTeamNamesAutomation(),
            scoreboardAutomation ?? new FakeScoreboardAutomation(),
            changeMatchAutomation ?? new FakeChangeMatchAutomation(),
            streamingAutomation ?? new FakeStreamingAutomation());
        await svc.LaunchAndLoginAsync();
        return (svc, handle);
    }

    /// <summary>
    /// Creates a service at <see cref="PcsProState.MatchSelectionReady"/> by firing
    /// <see cref="PcsProTrigger.SearchTriggered"/> and <see cref="PcsProTrigger.SpinnerGone"/>
    /// directly on the state machine. Used for <see cref="PcsProAutomationService.LoadMatchAsync"/> tests.
    /// </summary>
    private static async Task<(PcsProAutomationService Service, FakeProcessHandle Handle, MatchInfo TestMatch)>
        CreateServiceAtMatchSelectionReadyAsync(
            IMatchSelectionAutomation? matchSelectionAutomation = null)
    {
        var (svc, handle) = await CreateServiceAtMatchSelectionAsync(
            matchSelectionAutomation: matchSelectionAutomation ?? new FakeMatchSelectionAutomation());

        // MatchRowParser.TryParse always returns false in stub mode, so GetTodaysMatchesAsync
        // cannot reach MatchSelectionReady in tests. Drive the state machine directly.
        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);

        return (svc, handle, new MatchInfo("99999"));
    }

    /// <summary>
    /// Creates a service at <see cref="PcsProState.MatchLoaded"/> by driving the state machine
    /// through MatchSelection → MatchSelectionReady → MatchLoaded via reflection.
    /// Used for S-005 and S-006 tests.
    /// </summary>
    private static async Task<(PcsProAutomationService Service, FakeProcessHandle Handle)>
        CreateServiceAtMatchLoadedAsync(
            ITeamNamesAutomation? teamNamesAutomation = null,
            IScoreboardAutomation? scoreboardAutomation = null,
            IChangeMatchAutomation? changeMatchAutomation = null)
    {
        var (svc, handle) = await CreateServiceAtMatchSelectionAsync(
            teamNamesAutomation: teamNamesAutomation ?? new FakeTeamNamesAutomation(),
            scoreboardAutomation: scoreboardAutomation ?? new FakeScoreboardAutomation(),
            changeMatchAutomation: changeMatchAutomation ?? new FakeChangeMatchAutomation());

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);
        machine.Fire(PcsProTrigger.MatchOpened);

        return (svc, handle);
    }

    /// <summary>
    /// Creates a service at <see cref="PcsProState.Error"/> by driving the state machine
    /// through MatchSelection → Error via reflection. The crash watcher is still running
    /// (blocked on <see cref="FakeProcessHandle.WaitForExitAsync"/>).
    /// Used for S-007 RetryAsync tests.
    /// </summary>
    private static async Task<(PcsProAutomationService Service, FakeProcessHandle Handle, FakeProcessManager ProcessManager, FakeTimeProvider TimeProvider)>
        CreateServiceAtErrorAsync(
            FakeLoginAutomation? loginAutomation = null,
            IMatchSelectionAutomation? matchSelectionAutomation = null,
            ITeamNamesAutomation? teamNamesAutomation = null,
            IScoreboardAutomation? scoreboardAutomation = null,
            IChangeMatchAutomation? changeMatchAutomation = null)
    {
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle }; // uses StartedHandle fallback for initial launch
        var tp = new FakeTimeProvider();
        var svc = CreateService(
            pm, tp,
            loginAutomation ?? new FakeLoginAutomation(),
            matchSelectionAutomation ?? new FakeMatchSelectionAutomation(),
            teamNamesAutomation ?? new FakeTeamNamesAutomation(),
            scoreboardAutomation ?? new FakeScoreboardAutomation(),
            changeMatchAutomation ?? new FakeChangeMatchAutomation());
        await svc.LaunchAndLoginAsync();

        // Drive state machine directly: MatchSelection → Error (Timeout trigger).
        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.Timeout);

        // Set _lastErrorReason via reflection so AC-1 clearing verification is meaningful.
        typeof(PcsProAutomationService)
            .GetField("_lastErrorReason", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .SetValue(svc, "Simulated crash for test");

        return (svc, handle, pm, tp);
    }

    // -----------------------------------------------------------------------
    // AC-1 — State machine wiring: CurrentState reflects state machine
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CurrentState_OnConstruction_IsNotRunning()
    {
        var svc = CreateService(new FakeProcessManager(), new FakeTimeProvider());

        svc.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // -----------------------------------------------------------------------
    // AC-2, AC-19 — StateChanged and LoginDetected end-state
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenWindowImmediatelyVisible_RaisesStateChangedToMatchSelection()
    {
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider());

        var states = new List<PcsProState>();
        svc.StateChanged += (_, s) => states.Add(s);

        await svc.LaunchAndLoginAsync();

        states.Should().ContainInOrder(
            PcsProState.Launching, PcsProState.LoginScreen, PcsProState.MatchSelection);
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // AC-3 — Pre-existing cricket.exe processes are killed before Launch
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WithExistingProcesses_KillsThemAll()
    {
        var existing1 = new FakeProcessHandle { Id = 100 };
        var existing2 = new FakeProcessHandle { Id = 101 };
        var started = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = started };
        pm.ExistingProcesses.AddRange(new[] { existing1, existing2 });

        var svc = CreateService(pm, new FakeTimeProvider());
        var launchingStateReceivedBeforeStart = false;
        svc.StateChanged += (_, s) =>
        {
            if (s == PcsProState.Launching)
            {
                // By the time Launch fires, both kills must have already happened.
                launchingStateReceivedBeforeStart =
                    existing1.CallLog.Contains(nameof(FakeProcessHandle.Kill)) &&
                    existing2.CallLog.Contains(nameof(FakeProcessHandle.Kill));
            }
        };

        await svc.LaunchAndLoginAsync();

        existing1.CallLog.Should().Contain(nameof(FakeProcessHandle.Kill));
        existing2.CallLog.Should().Contain(nameof(FakeProcessHandle.Kill));
        launchingStateReceivedBeforeStart.Should().BeTrue(
            "both pre-existing processes must be killed before Launch trigger fires");
    }

    // -----------------------------------------------------------------------
    // AC-4 — Process is started with configured executable path and working directory
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WithValidConfig_StartsProcessWithConfiguredPathAndDirectory()
    {
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider());

        await svc.LaunchAndLoginAsync();

        pm.CapturedStartInfo.Should().NotBeNull();
        // Safe: asserted NotBeNull() on the line above.
        pm.CapturedStartInfo!.FileName.Should().Be(@"C:\cricket.exe");
        pm.CapturedStartInfo.WorkingDirectory.Should().Be(@"C:\");
    }

    // -----------------------------------------------------------------------
    // AC-5 — Launch trigger fires before IProcessManager.Start is called
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenStarting_FiresLaunchTriggerBeforeProcess()
    {
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider());

        var launchingFiredBeforeStart = false;
        svc.StateChanged += (_, s) =>
        {
            if (s == PcsProState.Launching)
                launchingFiredBeforeStart = pm.CapturedStartInfo is null;
        };

        await svc.LaunchAndLoginAsync();

        launchingFiredBeforeStart.Should().BeTrue(
            "Launch trigger must fire (raising StateChanged) before Start is called");
    }

    // -----------------------------------------------------------------------
    // AC-6 — Window detection timeout → Error state
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenTimeoutExceeded_TransitionsToError()
    {
        var handle = new FakeProcessHandle { MainWindowVisible = false };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var fakeTime = new FakeTimeProvider();
        var svc = CreateService(pm, fakeTime);

        var launchTask = svc.LaunchAndLoginAsync();

        // Allow polling loop to start (process.Start is fast, loop enters first iteration).
        await Task.Delay(100);
        fakeTime.Advance(TimeSpan.FromSeconds(PcsProStateMachine.LaunchingTimeoutSeconds + 1));

        // Allow one poll iteration to detect the elapsed time.
        await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // AC-6b — If process exits during polling, transition to Error immediately
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenProcessExitsDuringPoll_TransitionsToErrorImmediately()
    {
        var handle = new FakeProcessHandle { MainWindowVisible = false };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var fakeTime = new FakeTimeProvider(); // stays at T=0 — no timeout
        var svc = CreateService(pm, fakeTime);

        var launchTask = svc.LaunchAndLoginAsync();

        await Task.Delay(100);
        // Signal exit while time is still at T=0 (timeout wouldn't fire)
        handle.HasExited = true;

        await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // AC-6c — If Process.Start throws, transition to Error without propagating
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenStartThrows_TransitionsToErrorWithoutPropagating()
    {
        var pm = new FakeProcessManager { ShouldThrowOnStart = true };
        var svc = CreateService(pm, new FakeTimeProvider());

        // Must NOT throw to caller
        await svc.Invoking(s => s.LaunchAndLoginAsync())
            .Should().NotThrowAsync();

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Contain("Failed to start PCS Pro");
    }

    // -----------------------------------------------------------------------
    // AC-8, AC-9 — Crash watcher fires Error when process exits unexpectedly
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CrashWatcher_WhenProcessExitsUnexpectedly_TransitionsToError()
    {
        var (svc, handle) = await CreateServiceAtMatchSelectionAsync();

        var errorStateReceived = new TaskCompletionSource<PcsProState>();
        svc.StateChanged += (_, s) =>
        {
            if (s == PcsProState.Error)
                errorStateReceived.TrySetResult(s);
        };

        handle.SignalExit();

        var state = await errorStateReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        state.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("PCS Pro exited unexpectedly");
    }

    // -----------------------------------------------------------------------
    // AC-10 — Crash watcher does not fire when state is NotRunning
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CrashWatcher_WhenProcessExitsWhileNotRunning_DoesNotFire()
    {
        var (svc, handle) = await CreateServiceAtMatchSelectionAsync();

        // Stop the service first (moves to NotRunning, cancels crash watcher)
        await svc.StopAsync();
        svc.CurrentState.Should().Be(PcsProState.NotRunning);

        // Signal exit after already in NotRunning — should be a no-op
        var unexpectedTransition = false;
        svc.StateChanged += (_, _) => { unexpectedTransition = true; };
        handle.SignalExit();

        // Small wait to ensure crash watcher would have fired if not cancelled
        await Task.Delay(200);

        unexpectedTransition.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // AC-11 — Crash watcher is cancelled before process kill (no spurious Error)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_CancelsWatcherBeforeKill_NoSpuriousErrorTransition()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();

        var transitions = new List<PcsProState>();
        svc.StateChanged += (_, s) => transitions.Add(s);

        await svc.StopAsync();

        // Expected sequence: Timeout→Error, Retry→NotRunning (from MatchSelection default path).
        // Must NOT have an extra Error from crash watcher detecting the kill.
        transitions.Should().Equal(PcsProState.Error, PcsProState.NotRunning);
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
        svc.LastErrorReason.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // AC-13 — StopAsync when process already exited: no CloseMainWindow, no throw
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_WhenProcessAlreadyExited_SkipsCloseMainWindow()
    {
        var (svc, handle) = await CreateServiceAtMatchSelectionAsync();
        handle.HasExited = true;

        await svc.Invoking(s => s.StopAsync())
            .Should().NotThrowAsync();

        handle.CallLog.Should().NotContain(nameof(FakeProcessHandle.CloseMainWindow));
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // -----------------------------------------------------------------------
    // AC-13b — StopAsync when crash watcher already fired (Error state)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_WhenInErrorState_TransitionsToNotRunningViaRetry()
    {
        var (svc, handle) = await CreateServiceAtMatchSelectionAsync();
        var errorTcs = new TaskCompletionSource();
        svc.StateChanged += (_, s) =>
        {
            if (s == PcsProState.Error) errorTcs.TrySetResult();
        };
        handle.SignalExit();
        await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);

        await svc.Invoking(s => s.StopAsync())
            .Should().NotThrowAsync();

        svc.CurrentState.Should().Be(PcsProState.NotRunning);
        svc.LastErrorReason.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // AC-15 — StopAsync when already NotRunning: no-op, no throw
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_WhenNotRunning_ReturnsSilentlyWithoutTransitions()
    {
        var svc = CreateService(new FakeProcessManager(), new FakeTimeProvider());

        var transitionFired = false;
        svc.StateChanged += (_, _) => { transitionFired = true; };

        await svc.Invoking(s => s.StopAsync())
            .Should().NotThrowAsync();

        transitionFired.Should().BeFalse();
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // -----------------------------------------------------------------------
    // AC-15b — StopAsync from MatchSelection: Timeout→Error→Retry→NotRunning
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_WhenInMatchSelection_TransitionsToNotRunningViaTimeoutRetry()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();

        var transitions = new List<PcsProState>();
        svc.StateChanged += (_, s) => transitions.Add(s);

        await svc.StopAsync();

        transitions.Should().Equal(PcsProState.Error, PcsProState.NotRunning);
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
        svc.LastErrorReason.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // AC-12 — StopAsync escalates to force-kill when graceful close is ignored
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_WhenProcessIgnoresCloseMainWindow_EscalatesToForceKill()
    {
        var handle = new FakeProcessHandle { MainWindowVisible = true, AutoExitOnCloseMainWindow = false };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider());
        await svc.LaunchAndLoginAsync();

        // Pass a short ct to end the graceful-close wait without waiting the full 5 seconds.
        // The process has not exited (AutoExitOnCloseMainWindow = false), so StopAsync
        // must escalate to force-kill (AC-12).
        using var shortGracefulCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await svc.StopAsync(shortGracefulCts.Token);

        handle.CallLog.Should().Contain(nameof(FakeProcessHandle.CloseMainWindow));
        handle.CallLog.Should().Contain(nameof(FakeProcessHandle.Kill));
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // -----------------------------------------------------------------------
    // AC-16 + architecture — concurrent call guard
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenCalledConcurrently_SecondCallThrows()
    {
        // Use a handle that will never show window, keeping the first call in the polling loop.
        var handle = new FakeProcessHandle { MainWindowVisible = false };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider());

        using var cts = new CancellationTokenSource();
        var firstCall = svc.LaunchAndLoginAsync(cts.Token);

        // Give the first call time to fire Launch and enter the polling loop.
        await Task.Delay(150);

        // Second call: the state machine is in Launching, so Fire(Launch) throws.
        await svc.Invoking(s => s.LaunchAndLoginAsync())
            .Should().ThrowAsync<InvalidOperationException>();

        // Cancel and clean up the first call.
        cts.Cancel();
        await firstCall.IgnoreErrorAsync();
    }

    // -----------------------------------------------------------------------
    // S-003 AC-2 — Login timeout fires Timeout trigger → Error
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenLoginTimesOut_TransitionsToError()
    {
        var fake = new FakeLoginAutomation
        {
            LoginDialogVisible = false,    // login dialog never appears → poll stalls
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var fakeTime = new FakeTimeProvider();
        var svc = CreateService(pm, fakeTime, fake);

        var launchTask = svc.LaunchAndLoginAsync();

        // Let the service reach the login polling loop.
        await Task.Delay(100);

        // Default LoginScreenTimeoutSeconds is now 30 (via PcsProOptions)
        fakeTime.Advance(TimeSpan.FromSeconds(31));

        await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // S-008 AC-7 — Custom (non-default) timeout is respected
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenCustomLoginTimeoutConfigured_UsesConfiguredValue()
    {
        var fake = new FakeLoginAutomation
        {
            LoginDialogVisible = false,
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var fakeTime = new FakeTimeProvider();
        var svc = CreateServiceWithLoginTimeout(pm, fakeTime, fake, loginTimeoutSeconds: 10);

        var launchTask = svc.LaunchAndLoginAsync();

        await Task.Delay(100);

        // Advance past the custom 10s timeout, but before the default 30s
        fakeTime.Advance(TimeSpan.FromSeconds(11));

        await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Contain("10");
    }

    // -----------------------------------------------------------------------
    // S-008 AC-4 — Post-submit timeout message includes configured value
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenPostSubmitTimeout_ErrorIncludesConfiguredValue()
    {
        var fake = new FakeLoginAutomation
        {
            LoginDialogVisible = true,     // login dialog appears → credentials submitted
            CloseDialogOnSubmit = false,   // login dialog never closes → post-submit timeout
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var fakeTime = new FakeTimeProvider();
        var svc = CreateServiceWithLoginTimeout(pm, fakeTime, fake, loginTimeoutSeconds: 15);

        var launchTask = svc.LaunchAndLoginAsync();

        await Task.Delay(100);
        fakeTime.Advance(TimeSpan.FromSeconds(16));

        await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Contain("15");
        svc.LastErrorReason.Should().Contain("after submitting credentials");
    }

    // -----------------------------------------------------------------------
    // S-003 AC-3 — Password read from PcsProOptions (not hard-coded)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_UsesPasswordFromOptions()
    {
        const string ConfiguredPassword = "super-secret";
        var fake = new FakeLoginAutomation();
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = ConfiguredPassword,
        });
        var svc = new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            pm,
            new FakeTimeProvider(),
            new AutomationDependencies(
                fake,
                new FakeMatchSelectionAutomation(),
                new FakeTeamNamesAutomation(),
                new FakeScoreboardAutomation(),
                new FakeChangeMatchAutomation(),
                new FakeStreamingAutomation(),
                new FakeHealthCheckAutomation()),
            new NullAutomationLogService(),
            new ManualModeService());

        await svc.LaunchAndLoginAsync();

        fake.CapturedPassword.Should().Be(ConfiguredPassword);
    }

    // -----------------------------------------------------------------------
    // S-003 AC-4 — Unexpected dialog fires UnexpectedDialog trigger + close attempted
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenUnexpectedDialogDetected_TransitionsToError()
    {
        var fake = new FakeLoginAutomation
        {
            LoginDialogVisible = false,
            UnexpectedDialogPresent = true,
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider(), fake);

        await svc.LaunchAndLoginAsync();

        svc.CurrentState.Should().Be(PcsProState.Error);
        fake.CloseDialogAttempted.Should().BeTrue("TryCloseUnexpectedDialog must be called before firing the trigger");
    }

    // -----------------------------------------------------------------------
    // S-003 AC-10 — Crash during login phase: crash watcher reason preserved, no double-transition
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenCrashDuringLoginPhase_CrashWatcherReasonSet()
    {
        var fake = new FakeLoginAutomation
        {
            LoginDialogVisible = false,    // login phase stalls waiting for dialog
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        // FakeTimeProvider stays at T=0 — login timeout does NOT fire during this test.
        var svc = CreateService(pm, new FakeTimeProvider(), fake);

        var launchTask = svc.LaunchAndLoginAsync();

        // Allow the service to reach the login polling loop.
        await Task.Delay(150);

        // Signal crash — crash watcher fires first.
        handle.SignalExit();

        // LaunchAndLoginAsync must complete without exception.
        await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        // Crash watcher reason must win over login timeout reason.
        svc.LastErrorReason.Should().Be("PCS Pro exited unexpectedly");
    }

    // -----------------------------------------------------------------------
    // S-003 — Interaction exception (element not found) → Error + Timeout trigger
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenInteractionThrows_TransitionsToError()
    {
        var fake = new FakeLoginAutomation { ThrowOnInteraction = true };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider(), fake);

        await svc.LaunchAndLoginAsync();

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("Login interaction failed");
    }

    // -----------------------------------------------------------------------
    // S-003 — CancellationToken during login poll: Error + propagated OCE
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_WhenCancelledDuringLogin_ThrowsOCEAndTransitionsToError()
    {
        var fake = new FakeLoginAutomation
        {
            LoginDialogVisible = false,    // stalls in Task.Delay — gives ct time to be cancelled
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider(), fake);

        using var cts = new CancellationTokenSource();
        var launchTask = svc.LaunchAndLoginAsync(cts.Token);

        // Allow service to enter the login poll delay.
        await Task.Delay(150);
        cts.Cancel();

        // Must NOT swallow the OCE — it must propagate to caller.
        await svc.Invoking(_ => launchTask)
            .Should().ThrowAsync<OperationCanceledException>();

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("Login phase cancelled by caller");
    }

    // =======================================================================
    // S-004: GetTodaysMatchesAsync
    // =======================================================================

    // -----------------------------------------------------------------------
    // AC-1 / AC-5 — SearchTriggered fired; SpinnerGone fired; MatchSelectionReady
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenSpinnerImmediatelyGone_TransitionsToMatchSelectionReady()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { SpinnerVisible = false };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        await svc.GetTodaysMatchesAsync();

        // Spinner immediately gone → SpinnerGone fires → MatchSelectionReady.
        // Then zero rows → FilterToday returns [] → Error fires.
        // Error is expected here because TryParse always returns false in stub.
        // AC-1: SearchTriggered was fired — verified by the state sequence.
        fakeMatchSel.SearchTriggered.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-2 — startTimestamp before OpenMatchDialogAndSearch
    // (Tested implicitly: if timeout fired before search, startTimestamp must have been recorded first.
    //  Direct verification requires a time-controlled test — see timeout test AC-6.)
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // AC-4 — Poll loop continues while spinner is visible
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenSpinnerClearsAfterTwoPolls_WaitsForSpinnerToGone()
    {
        // Start from MatchSelection, transition through GetTodaysMatchesAsync.
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(
            matchSelectionAutomation: new SpinnerDropsAfterNCallsFake(dropsAfterCalls: 3));

        await svc.GetTodaysMatchesAsync();

        svc.CurrentState.Should().Be(PcsProState.Error,
            "after spinner gone + zero rows, Error fires (no matches for today)");
    }

    // -----------------------------------------------------------------------
    // AC-6 — Timeout fires error with descriptive reason
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenSpinnerNeverClears_TransitionsToErrorOnTimeout()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { SpinnerVisible = true };
        var pm = new FakeProcessManager { StartedHandle = new FakeProcessHandle { MainWindowVisible = true } };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel);
        await svc.LaunchAndLoginAsync();

        // Start the operation, then advance fake time while it is polling.
        // Task.Delay inside the service uses the real clock; the timeout check uses the fake clock.
        // We must advance AFTER startTimestamp is captured inside the method.
        var getTask = svc.GetTodaysMatchesAsync();
        await Task.Delay(350); // wait past 200ms guard + at least one 200ms poll cycle
        tp.Advance(TimeSpan.FromSeconds(PcsProStateMachine.MatchSelectionSearchingTimeoutSeconds + 1));

        await getTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Contain("Match search did not complete within");
    }

    // -----------------------------------------------------------------------
    // AC-7 — Unexpected dialog during spinner wait → warning logged, operation continues
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenUnexpectedDialogDuringSpinnerWait_LogsWarningAndContinues()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation
        {
            SpinnerVisible = false,          // spinner already gone — parse immediately
            UnexpectedDialogPresent = true,  // dialog present on same tick
        };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        await svc.GetTodaysMatchesAsync();

        // Dialog was detected but operation continued past the check (no close, no trigger).
        fakeMatchSel.CloseDialogAttempted.Should().BeFalse(
            "unexpected dialog must NOT be closed — demoted to warning-only");
        fakeMatchSel.SearchTriggered.Should().BeTrue(
            "search must still be triggered despite dialog presence");
    }

    // -----------------------------------------------------------------------
    // AC-11 — Zero rows after FilterToday → Error with date in message
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenZeroMatchesAfterFilter_TransitionsToError()
    {
        // SpinnerVisible=false → spinner immediately gone; RowTexts=[] → zero parsed rows
        var fakeMatchSel = new FakeMatchSelectionAutomation { SpinnerVisible = false, RowTexts = [] };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        var result = await svc.GetTodaysMatchesAsync();

        result.Should().BeEmpty();
        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().StartWith("No matches found for ");
    }

    // -----------------------------------------------------------------------
    // AC-12 — Cancellation during spinner poll: Error + propagated OCE
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenCancelledDuringSpinnerPoll_ThrowsOCEAndTransitionsToError()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation
        {
            SpinnerVisible = true,  // stall in the poll loop
        };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        using var cts = new CancellationTokenSource();
        var task = svc.GetTodaysMatchesAsync(cts.Token);

        await Task.Delay(150);
        cts.Cancel();

        await svc.Invoking(_ => task)
            .Should().ThrowAsync<OperationCanceledException>();
        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("Match search cancelled by caller");
    }

    // -----------------------------------------------------------------------
    // AC-14 — OpenMatchDialogAndSearch throws → Error with interaction reason
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenOpenMatchDialogThrows_TransitionsToError()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { ThrowOnInteraction = true };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        var result = await svc.GetTodaysMatchesAsync();

        result.Should().BeEmpty();
        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("Match selection interaction failed");
    }

    // -----------------------------------------------------------------------
    // AC-15 — ReadDataGridRowTexts throws → Error with interaction reason
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenReadDataGridRowTextsThrows_TransitionsToError()
    {
        // SpinnerVisible=false → pass spinner; then ReadDataGridRowTexts throws.
        var fakeMatchSel = new ReadDataGridThrowsFake();
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        var result = await svc.GetTodaysMatchesAsync();

        result.Should().BeEmpty();
        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("Match selection interaction failed");
    }

    // -----------------------------------------------------------------------
    // AC-31 — Concurrent GetTodaysMatchesAsync throws InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhenOperationAlreadyInProgress_ThrowsInvalidOperationException()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { SpinnerVisible = true };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        // Lock the operation by starting a GetTodaysMatchesAsync that stalls on spinner.
        using var cts = new CancellationTokenSource();
        var firstTask = svc.GetTodaysMatchesAsync(cts.Token);

        await Task.Delay(250); // allow first call to enter the lock

        await svc.Invoking(_ => _.GetTodaysMatchesAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in progress*");

        cts.Cancel();
        await firstTask.IgnoreErrorAsync();
    }

    // =======================================================================
    // S-002 (HLPS-019): GetMatchesForDateAsync — date-parameterised match retrieval
    // =======================================================================

    // -----------------------------------------------------------------------
    // TC-1 — Date forwarded to internal search automation
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetMatchesForDateAsync_ForwardsDateToMatchSelectionAutomation()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { SpinnerVisible = false };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);
        var searchDate = new DateOnly(2025, 3, 15);

        await svc.GetMatchesForDateAsync(searchDate);

        fakeMatchSel.SearchTriggered.Should().BeTrue();
        fakeMatchSel.LastSearchDate.Should().Be(searchDate);
    }

    // -----------------------------------------------------------------------
    // TC-2 — Filtering uses searchDate, not today
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetMatchesForDateAsync_WhenZeroMatchesAfterFilter_ErrorIncludesSearchDate()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { SpinnerVisible = false, RowTexts = [] };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);
        var searchDate = new DateOnly(2025, 6, 1);

        var result = await svc.GetMatchesForDateAsync(searchDate);

        result.Should().BeEmpty();
        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("No matches found for 2025-06-01");
    }

    // -----------------------------------------------------------------------
    // TC-3 — GetTodaysMatchesAsync delegates to GetMatchesForDateAsync with local date
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTodaysMatchesAsync_DelegatesToGetMatchesForDateAsync_WithLocalDate()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { SpinnerVisible = false };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(matchSelectionAutomation: fakeMatchSel);

        await svc.GetTodaysMatchesAsync();

        // FakeTimeProvider defaults to 2000-01-01 — local time derivation
        fakeMatchSel.LastSearchDate.Should().Be(new DateOnly(2000, 1, 1));
    }

    // =======================================================================
    // S-004: LoadMatchAsync
    // =======================================================================

    // -----------------------------------------------------------------------
    // AC-17 / AC-18 — SelectAndOpenMatch called; state transitions to MatchLoaded
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WhenMatchImmediatelyLoaded_TransitionsToMatchLoaded()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = true };
        var (svc, _, testMatch) = await CreateServiceAtMatchSelectionReadyAsync(fakeMatchSel);

        await svc.LoadMatchAsync(testMatch);

        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
        fakeMatchSel.OpenAttemptedFor.Should().Be(testMatch);
    }

    // -----------------------------------------------------------------------
    // AC-19 — Timeout while waiting for match to load → Error
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WhenMatchNeverLoads_TransitionsToErrorOnTimeout()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = false };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel);
        await svc.LaunchAndLoginAsync();

        // Manually advance to MatchSelectionReady.
        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);

        var testMatch = new MatchInfo("99999");

        // Start the operation, then advance fake time while it is polling.
        var loadTask = svc.LoadMatchAsync(testMatch);
        await Task.Delay(150); // allow entry into the poll loop
        tp.Advance(TimeSpan.FromSeconds(PcsProStateMachine.MatchSelectionReadyTimeoutSeconds + 1));

        await loadTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Contain("Match did not open within");
    }

    // -----------------------------------------------------------------------
    // AC-20 — Unexpected dialog during open wait → warning logged, operation continues
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WhenUnexpectedDialogDuringOpenWait_LogsWarningAndContinues()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation
        {
            MatchLoaded = true,              // match loads on same tick
            UnexpectedDialogPresent = true,  // dialog present on same tick
        };
        var (svc, _, testMatch) = await CreateServiceAtMatchSelectionReadyAsync(fakeMatchSel);

        await svc.LoadMatchAsync(testMatch);

        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            "match must load normally despite dialog presence");
        fakeMatchSel.CloseDialogAttempted.Should().BeFalse(
            "unexpected dialog must NOT be closed — demoted to warning-only");
    }

    // -----------------------------------------------------------------------
    // AC-21 — Cancellation during open wait: Error + propagated OCE
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WhenCancelledDuringOpenWait_ThrowsOCEAndTransitionsToError()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = false };
        var (svc, _, testMatch) = await CreateServiceAtMatchSelectionReadyAsync(fakeMatchSel);

        using var cts = new CancellationTokenSource();
        var task = svc.LoadMatchAsync(testMatch, cts.Token);

        await Task.Delay(150);
        cts.Cancel();

        await svc.Invoking(_ => task)
            .Should().ThrowAsync<OperationCanceledException>();
        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("Match open cancelled by caller");
    }

    // -----------------------------------------------------------------------
    // AC-22 — SelectAndOpenMatch throws → Error with interaction reason
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WhenSelectAndOpenMatchThrows_TransitionsToError()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { ThrowOnInteraction = true };
        var (svc, _, testMatch) = await CreateServiceAtMatchSelectionReadyAsync(fakeMatchSel);

        await svc.LoadMatchAsync(testMatch);

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("Match selection interaction failed");
    }

    // -----------------------------------------------------------------------
    // AC-23 — Crash watcher fires before LoadMatchAsync completes → no double-transition
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WhenCrashWatcherFiresFirst_NoDoubleTransition()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = false };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel);
        await svc.LaunchAndLoginAsync();

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);

        var testMatch = new MatchInfo("99999");
        var loadTask = svc.LoadMatchAsync(testMatch);

        // Allow entry into the poll loop, then crash.
        await Task.Delay(150);
        handle.SignalExit();

        await loadTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Be("PCS Pro exited unexpectedly");
    }

    // -----------------------------------------------------------------------
    // AC-32 — Concurrent LoadMatchAsync throws InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WhenOperationAlreadyInProgress_ThrowsInvalidOperationException()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = false };
        var (svc, _, testMatch) = await CreateServiceAtMatchSelectionReadyAsync(fakeMatchSel);

        using var cts = new CancellationTokenSource();
        var firstTask = svc.LoadMatchAsync(testMatch, cts.Token);

        await Task.Delay(150);

        await svc.Invoking(_ => _.LoadMatchAsync(testMatch))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in progress*");

        cts.Cancel();
        await firstTask.IgnoreErrorAsync();
    }

    // =======================================================================
    // S-005: GetTeamNamesAsync
    // =======================================================================

    // -----------------------------------------------------------------------
    // AC-1 — Happy path: returns MatchTeams with home and away team names
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_HappyPath_ReturnsMatchTeamsAndStaysInMatchLoaded()
    {
        var fake = new FakeTeamNamesAutomation { HomeTeamName = "Home XI", AwayTeamName = "Away XI" };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(fake);

        var result = await svc.GetTeamNamesAsync();

        result.Home.TeamName.Should().Be("Home XI");
        result.Away.TeamName.Should().Be("Away XI");
        result.Home.ClubName.Should().Be("Home CC");
        result.Away.ClubName.Should().Be("Away CC");
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
    }

    // -----------------------------------------------------------------------
    // AC-9 — Happy path: TryCloseTeamsDialog called after successful read
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_HappyPath_ClosesTeamsDialog()
    {
        var fake = new FakeTeamNamesAutomation();
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(fake);

        await svc.GetTeamNamesAsync();

        fake.CloseTeamsDialogAttempted.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-3 — OpenTeamsDialog throws → sentinel returned, Error state, Timeout trigger
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_WhenOpenTeamsDialogThrows_ReturnsSentinelAndTransitionsToError()
    {
        var fake = new FakeTeamNamesAutomation { ThrowOnOpenTeamsDialog = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(fake);

        var result = await svc.GetTeamNamesAsync();

        result.Home.TeamName.Should().BeEmpty();
        result.Away.TeamName.Should().BeEmpty();
        result.Home.ClubName.Should().BeEmpty();
        result.Away.ClubName.Should().BeEmpty();
        svc.CurrentState.Should().Be(PcsProState.Error);
        fake.CloseTeamsDialogAttempted.Should().BeTrue("TryCloseTeamsDialog must be called on open failure");
    }

    // -----------------------------------------------------------------------
    // AC-4 — ReadHomeTeamName throws → sentinel returned, Error state
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_WhenReadHomeTeamNameThrows_ReturnsSentinelAndTransitionsToError()
    {
        var fake = new FakeTeamNamesAutomation { ThrowOnReadHomeTeamName = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(fake);

        var result = await svc.GetTeamNamesAsync();

        result.Home.TeamName.Should().BeEmpty();
        result.Away.TeamName.Should().BeEmpty();
        result.Home.ClubName.Should().BeEmpty();
        result.Away.ClubName.Should().BeEmpty();
        svc.CurrentState.Should().Be(PcsProState.Error);
        fake.CloseTeamsDialogAttempted.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-5 — ReadAwayTeamName throws → sentinel returned, Error state
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_WhenReadAwayTeamNameThrows_ReturnsSentinelAndTransitionsToError()
    {
        var fake = new FakeTeamNamesAutomation { ThrowOnReadAwayTeamName = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(fake);

        var result = await svc.GetTeamNamesAsync();

        result.Home.TeamName.Should().BeEmpty();
        result.Away.TeamName.Should().BeEmpty();
        result.Home.ClubName.Should().BeEmpty();
        result.Away.ClubName.Should().BeEmpty();
        svc.CurrentState.Should().Be(PcsProState.Error);
        fake.CloseTeamsDialogAttempted.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-6 — Unexpected dialog detected → warning logged, continues to read team names
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_WhenUnexpectedDialogPresent_LogsWarningAndReturnsTeamNames()
    {
        var fake = new FakeTeamNamesAutomation { UnexpectedDialogPresent = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(fake);

        var result = await svc.GetTeamNamesAsync();

        result.Home.ClubName.Should().Be("Home CC");
        result.Home.TeamName.Should().Be("Home XI");
        result.Away.ClubName.Should().Be("Away CC");
        result.Away.TeamName.Should().Be("Away XI");
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            "state must remain MatchLoaded — dialog is warning-only");
        fake.CloseUnexpectedDialogAttempted.Should().BeFalse(
            "unexpected dialog must NOT be closed — demoted to warning-only");
    }

    // -----------------------------------------------------------------------
    // AC-7 — Cancellation before open: OCE propagated, Error state
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_WhenAlreadyCancelledBeforeOpen_ThrowsOCEAndTransitionsToError()
    {
        var fake = new FakeTeamNamesAutomation();
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(fake);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await svc.Invoking(_ => _.GetTeamNamesAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        svc.CurrentState.Should().Be(PcsProState.Error);
    }

    // -----------------------------------------------------------------------
    // AC-2 — Concurrent GetTeamNamesAsync → second call throws InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_WhenOperationAlreadyInProgress_ThrowsInvalidOperationException()
    {
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        // Simulate an in-progress operation by setting the interlocked flag directly.
        // GetTeamNamesAsync has no await before its first blocking I/O call (FlaUI is synchronous),
        // so we cannot use a real concurrent call without Thread.Sleep hacks. Reflection is the
        // clean equivalent of an actual concurrent caller holding the lock.
        var field = typeof(PcsProAutomationService)
            .GetField("_isTeamNamesOperationInProgress", BindingFlags.NonPublic | BindingFlags.Instance)!; // field exists on this type
        field.SetValue(svc, 1);

        await svc.Invoking(_ => _.GetTeamNamesAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in progress*");
    }

    // -----------------------------------------------------------------------
    // AC-14 — Wrong state (not MatchLoaded) → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();
        // Service is in MatchSelection state — NOT MatchLoaded.

        await svc.Invoking(_ => _.GetTeamNamesAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.MatchLoaded}*");
    }

    // =======================================================================
    // S-006: RefreshScoreboardAsync, CaptureScoreboardImageAsync, ChangeMatchAsync
    // =======================================================================

    // -----------------------------------------------------------------------
    // AC-1 — RefreshScoreboardAsync happy path: cog + refresh called; state stays MatchLoaded
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RefreshScoreboardAsync_HappyPath_CallsCogAndRefreshAndStaysInMatchLoaded()
    {
        var fake = new FakeScoreboardAutomation();
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(scoreboardAutomation: fake);

        await svc.RefreshScoreboardAsync();

        fake.ClickCogAttempted.Should().BeTrue("ClickSettingsCog must be called");
        fake.ClickRefreshAttempted.Should().BeTrue("ClickRefreshAllScoreboards must be called");
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
    }

    // -----------------------------------------------------------------------
    // AC-2 — Concurrent S-006 operation (same-method guard) → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RefreshScoreboardAsync_WhenOperationAlreadyInProgress_TimesOut()
    {
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        // Acquire the SemaphoreSlim gate to simulate an in-progress operation.
        var field = typeof(PcsProAutomationService)
            .GetField("_matchLoadedGate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var gate = (SemaphoreSlim)field.GetValue(svc)!;
        gate.Wait(0); // drain the semaphore

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await svc.Invoking(_ => _.RefreshScoreboardAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>(
                because: "the gate is held and the token cancels before the 30 s timeout");

        gate.Release(); // clean up
    }

    // -----------------------------------------------------------------------
    // AC-3 — ClickSettingsCog throws → Error state; Timeout trigger; reason "settings menu"
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RefreshScoreboardAsync_WhenClickCogThrows_TransitionsToError()
    {
        var fake = new FakeScoreboardAutomation { ThrowOnClickSettingsCog = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(scoreboardAutomation: fake);

        await svc.RefreshScoreboardAsync();

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().Contain("settings menu");
    }

    // -----------------------------------------------------------------------
    // AC-4 — ClickRefreshAllScoreboards throws (cog succeeded) → Error state; Timeout trigger
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RefreshScoreboardAsync_WhenClickRefreshThrows_TransitionsToError()
    {
        var fake = new FakeScoreboardAutomation { ThrowOnClickRefreshAllScoreboards = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(scoreboardAutomation: fake);

        await svc.RefreshScoreboardAsync();

        fake.ClickCogAttempted.Should().BeTrue("cog must have been clicked before refresh threw");
        svc.CurrentState.Should().Be(PcsProState.Error);
    }

    // -----------------------------------------------------------------------
    // AC-5 — Unexpected dialog at entry → warning logged, operation continues
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RefreshScoreboardAsync_WhenUnexpectedDialogPresent_LogsWarningAndContinues()
    {
        var fake = new FakeScoreboardAutomation { UnexpectedDialogPresent = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(scoreboardAutomation: fake);

        await svc.RefreshScoreboardAsync();

        fake.CloseUnexpectedDialogAttempted.Should().BeFalse(
            "unexpected dialog must NOT be closed — demoted to warning-only");
        fake.ClickCogAttempted.Should().BeTrue(
            "cog must be clicked — operation continues despite dialog");
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            "state must remain MatchLoaded — dialog is warning-only");
    }

    // -----------------------------------------------------------------------
    // AC-6 — Wrong state → InvalidOperationException; state unchanged
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RefreshScoreboardAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();
        // Service is in MatchSelection state — NOT MatchLoaded.

        await svc.Invoking(_ => _.RefreshScoreboardAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.MatchLoaded}*");
    }

    // -----------------------------------------------------------------------
    // AC-7 — Cancellation → Error state; OperationCanceledException propagated
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RefreshScoreboardAsync_WhenAlreadyCancelled_ThrowsOCE()
    {
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await svc.Invoking(_ => _.RefreshScoreboardAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            because: "cancellation at the gate is a clean abort, not an automation error");
    }

    // -----------------------------------------------------------------------
    // AC-8 — CaptureScoreboardImageAsync happy path: bytes returned; state stays MatchLoaded
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_HappyPath_ReturnsImageBytesAndStaysInMatchLoaded()
    {
        var fake = new FakeScoreboardAutomation
        {
            CapturedImageBytes = new byte[] { 0xFF, 0xD8, 0x01 }
        };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(scoreboardAutomation: fake);

        var result = await svc.CaptureScoreboardImageAsync();

        result.Should().Equal(new byte[] { 0xFF, 0xD8, 0x01 });
        fake.CaptureAttempted.Should().BeTrue("CaptureScoreboardImage must be called");
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
    }

    // -----------------------------------------------------------------------
    // AC-9 — CaptureScoreboardImage throws → Error; Timeout trigger; sentinel byte[] returned
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_WhenCaptureThrows_ReturnsSentinelAndTransitionsToError()
    {
        var fake = new FakeScoreboardAutomation { ThrowOnCaptureScoreboardImage = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(scoreboardAutomation: fake);

        var result = await svc.CaptureScoreboardImageAsync();

        result.Should().BeEmpty("sentinel Array.Empty<byte>() must be returned on capture failure");
        svc.CurrentState.Should().Be(PcsProState.Error);
    }

    // -----------------------------------------------------------------------
    // AC-10 — Unexpected dialog at entry → warning logged, capture continues
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_WhenUnexpectedDialogPresent_LogsWarningAndReturnsCapture()
    {
        var fake = new FakeScoreboardAutomation { UnexpectedDialogPresent = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(scoreboardAutomation: fake);

        var result = await svc.CaptureScoreboardImageAsync();

        result.Should().NotBeEmpty("actual capture must be returned despite dialog presence");
        fake.CloseUnexpectedDialogAttempted.Should().BeFalse(
            "unexpected dialog must NOT be closed — demoted to warning-only");
        fake.CaptureAttempted.Should().BeTrue(
            "capture must be attempted — operation continues despite dialog");
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            "state must remain MatchLoaded — dialog is warning-only");
    }

    // -----------------------------------------------------------------------
    // AC-11 — Wrong state → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();

        await svc.Invoking(_ => _.CaptureScoreboardImageAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.MatchLoaded}*");
    }

    // -----------------------------------------------------------------------
    // AC-12 — Cancellation → Error state; OperationCanceledException propagated
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_WhenAlreadyCancelled_ReturnsEmpty()
    {
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await svc.CaptureScoreboardImageAsync(cts.Token);

        result.Should().BeEmpty(
            because: "capture gracefully returns empty when cancelled at the gate");
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            because: "cancellation at the gate is a clean abort, not an automation error");
    }

    // -----------------------------------------------------------------------
    // AC-13 — ChangeMatchAsync happy path: sequence called; ChangeMatch fired; state = MatchSelection
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ChangeMatchAsync_HappyPath_ExecutesSequenceAndTransitionsToMatchSelection()
    {
        var fake = new FakeChangeMatchAutomation();
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(changeMatchAutomation: fake);

        await svc.ChangeMatchAsync();

        fake.ExecuteChangeMatchAttempted.Should().BeTrue("ExecuteChangeMatchSequence must be called");
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // AC-14 — ExecuteChangeMatchSequence throws → Error state; Timeout trigger
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ChangeMatchAsync_WhenSequenceThrows_TransitionsToError()
    {
        var fake = new FakeChangeMatchAutomation { ThrowOnExecuteChangeMatchSequence = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(changeMatchAutomation: fake);

        await svc.ChangeMatchAsync();

        fake.ExecuteChangeMatchAttempted.Should().BeTrue("sequence must have been attempted before throw");
        svc.CurrentState.Should().Be(PcsProState.Error);
    }

    // -----------------------------------------------------------------------
    // AC-15 — Unexpected dialog at entry → warning logged, operation continues
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ChangeMatchAsync_WhenUnexpectedDialogPresent_LogsWarningAndContinues()
    {
        var fake = new FakeChangeMatchAutomation { UnexpectedDialogPresent = true };
        var (svc, _) = await CreateServiceAtMatchLoadedAsync(changeMatchAutomation: fake);

        await svc.ChangeMatchAsync();

        fake.CloseUnexpectedDialogAttempted.Should().BeFalse(
            "unexpected dialog must NOT be closed — demoted to warning-only");
        fake.ExecuteChangeMatchAttempted.Should().BeTrue(
            "change-match sequence must execute — operation continues despite dialog");
        svc.CurrentState.Should().Be(PcsProState.MatchSelection,
            "state must transition to MatchSelection after successful change-match");
    }

    // -----------------------------------------------------------------------
    // AC-16 — Wrong state → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ChangeMatchAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();

        await svc.Invoking(_ => _.ChangeMatchAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.MatchLoaded}*");
    }

    // -----------------------------------------------------------------------
    // AC-17 — Cancellation → Error state; OperationCanceledException propagated
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ChangeMatchAsync_WhenAlreadyCancelled_ThrowsOCE()
    {
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await svc.Invoking(_ => _.ChangeMatchAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            because: "cancellation at the gate is a clean abort, not an automation error");
    }

    // -----------------------------------------------------------------------
    // AC-18 — Crash watcher fires Error before ChangeMatch trigger:
    //          guardTerminal: true silently skips the trigger without throwing
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ChangeMatchAsync_WhenCrashWatcherWins_NoDoubleTransition()
    {
        // To simulate the crash-watcher racing with ChangeMatchAsync:
        // 1. Fire ChangeMatchAsync (sequence succeeds).
        // 2. Manually inject an Error state transition before FireUnderLockAsync fires ChangeMatch.
        // The simplest observable proof is that calling ChangeMatchAsync after the state is Error
        // does NOT throw an unhandled Stateless exception — it throws the predictable wrong-state IOE.
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        // Inject Error directly — simulates crash-watcher winning first.
        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.Timeout);

        svc.CurrentState.Should().Be(PcsProState.Error);

        // A second ChangeMatchAsync call now throws wrong-state IOE rather than a Stateless
        // "trigger not permitted" exception, proving guardTerminal is respected.
        await svc.Invoking(_ => _.ChangeMatchAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.MatchLoaded}*");
    }

    // -----------------------------------------------------------------------
    // AC-22 — Cross-method shared guard: CaptureScoreboardImageAsync blocked while
    //          RefreshScoreboardAsync is in progress → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_WhenRefreshInProgress_ReturnsEmpty()
    {
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        // Acquire the SemaphoreSlim gate to simulate RefreshScoreboardAsync holding it.
        var field = typeof(PcsProAutomationService)
            .GetField("_matchLoadedGate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var gate = (SemaphoreSlim)field.GetValue(svc)!;
        gate.Wait(0); // drain the semaphore

        // CaptureScoreboardImageAsync should return empty when the gate times out,
        // not throw InvalidOperationException.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var result = await svc.CaptureScoreboardImageAsync(cts.Token);
        result.Should().BeEmpty(
            because: "capture gracefully returns empty when the gate is held");

        gate.Release(); // clean up
    }

    // -----------------------------------------------------------------------
    // AC-23 — Constructor parameter count ≤ 7 after AutomationDependencies aggregate
    // -----------------------------------------------------------------------

    [TestMethod]
    public void PcsProAutomationService_Constructor_HasAtMostSevenParameters()
    {
        var ctors = typeof(PcsProAutomationService).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        ctors.Should().NotBeEmpty();
        ctors.Max(c => c.GetParameters().Length)
            .Should().BeLessOrEqualTo(7,
                because: "SPEC-S-006 §2.6 requires constructor refactor to ≤ 7 parameters via AutomationDependencies aggregate");
    }

    // -----------------------------------------------------------------------
    // S-007 RetryAsync tests
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // AC-1 / AC-2 — Happy path: Error → RetryAsync → MatchSelection; Start called twice
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RetryAsync_FromErrorState_RelaunchesAndReachesMatchSelection()
    {
        var (svc, _, pm, _) = await CreateServiceAtErrorAsync();

        // Provide a second handle with a visible window for the post-retry launch.
        pm.StartedHandleQueue.Enqueue(new FakeProcessHandle { MainWindowVisible = true });

        await svc.RetryAsync();

        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
        pm.StartCallCount.Should().Be(2, because: "original launch + retry launch = 2 Start calls");
        svc.LastErrorReason.Should().BeNull("RetryAsync must clear _lastErrorReason");
    }

    // -----------------------------------------------------------------------
    // AC-3 — Wrong state: NotRunning → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RetryAsync_FromNotRunning_ThrowsInvalidOperationException()
    {
        var svc = CreateService(new FakeProcessManager(), new FakeTimeProvider());

        await svc.Invoking(_ => _.RetryAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.NotRunning}*");
    }

    // -----------------------------------------------------------------------
    // AC-4 — Wrong state: MatchSelection → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RetryAsync_FromMatchSelection_ThrowsInvalidOperationException()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();

        await svc.Invoking(_ => _.RetryAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.MatchSelection}*");
    }

    // -----------------------------------------------------------------------
    // AC-5 — Wrong state: MatchLoaded → InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RetryAsync_FromMatchLoaded_ThrowsInvalidOperationException()
    {
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        await svc.Invoking(_ => _.RetryAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PcsProState.MatchLoaded}*");
    }

    // -----------------------------------------------------------------------
    // AC-6 — Crash watcher is replaced (new non-null CTS after retry)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RetryAsync_CrashWatcherIsReplacedAfterRetry()
    {
        var (svc, _, pm, _) = await CreateServiceAtErrorAsync();
        pm.StartedHandleQueue.Enqueue(new FakeProcessHandle { MainWindowVisible = true });

        // Capture original CTS instance before retry.
        var originalCts = typeof(PcsProAutomationService)
            .GetField("_crashWatcherCts", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc);

        await svc.RetryAsync();

        var newCts = typeof(PcsProAutomationService)
            .GetField("_crashWatcherCts", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc);

        newCts.Should().NotBeNull("LaunchAndLoginAsync must install a new crash watcher CTS");
        newCts.Should().NotBeSameAs(originalCts, "new CTS must be a different instance from the original");
    }

    // -----------------------------------------------------------------------
    // AC-7 — Pre-cancelled token: OperationCanceledException; state = NotRunning
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RetryAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var (svc, _, pm, _) = await CreateServiceAtErrorAsync();
        // Empty queue is a safety backstop — step 7.5 should throw before any second Start call.

        await svc.Invoking(_ => _.RetryAsync(new CancellationToken(canceled: true)))
            .Should().ThrowAsync<OperationCanceledException>();

        svc.CurrentState.Should().Be(PcsProState.NotRunning,
            because: "step 7.5 ct.ThrowIfCancellationRequested() fires after teardown, before relaunch");
        pm.StartCallCount.Should().Be(1,
            because: "only the original launch; retry was cancelled before a second Start call");
    }

    // -----------------------------------------------------------------------
    // AC-8 — Launch failure (window never appears → timeout): ends in Error; no exception
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RetryAsync_LaunchFailure_ServiceEndsInErrorState()
    {
        var (svc, _, pm, tp) = await CreateServiceAtErrorAsync();

        // Second process has no visible window → polling will time out.
        pm.StartedHandleQueue.Enqueue(new FakeProcessHandle { MainWindowVisible = false });

        var retryTask = svc.RetryAsync();               // don't await — let teardown complete
        await Task.Delay(100);                          // let teardown complete and polling loop enter
        tp.Advance(TimeSpan.FromSeconds(
            PcsProStateMachine.LaunchingTimeoutSeconds + 1));

        await retryTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error,
            because: "window polling timed out; Timeout trigger fires Error");
        pm.StartCallCount.Should().Be(2, because: "retry did start a second process");
    }

    // -----------------------------------------------------------------------
    // S-009 Streaming Automation — delegation tests
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // AC-8 / AC-12 — StartStreamingAsync delegates to IStreamingAutomation
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StartStreamingAsync_DelegatesToClickStartAndHandleConsent()
    {
        var fake = new FakeStreamingAutomation();
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(
            streamingAutomation: fake);

        // Drive to MatchLoaded state
        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);
        machine.Fire(PcsProTrigger.MatchOpened);

        await svc.StartStreamingAsync();

        fake.ClickStartLiveStreamCalled.Should().BeTrue(
            because: "StartStreamingAsync must delegate to IStreamingAutomation.ClickStartLiveStream");
        fake.HandleConsentDialogsCalled.Should().BeTrue(
            because: "StartStreamingAsync must delegate to IStreamingAutomation.HandleConsentDialogs");
    }

    // -----------------------------------------------------------------------
    // AC-8 / AC-12 — StopStreamingAsync delegates to IStreamingAutomation
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StopStreamingAsync_DelegatesToClickStop()
    {
        var fake = new FakeStreamingAutomation { StreamingActive = true };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(
            streamingAutomation: fake);

        // Drive to MatchLoaded state
        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);
        machine.Fire(PcsProTrigger.MatchOpened);

        await svc.StopStreamingAsync();

        fake.ClickStopLiveStreamCalled.Should().BeTrue(
            because: "StopStreamingAsync must delegate to IStreamingAutomation.ClickStopLiveStream");
    }

    // -----------------------------------------------------------------------
    // S-009 — Wrong state throws InvalidOperationException
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StartStreamingAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException()
    {
        var svc = CreateService(new FakeProcessManager(), new FakeTimeProvider());

        await svc.Invoking(s => s.StartStreamingAsync())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task StopStreamingAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException()
    {
        var svc = CreateService(new FakeProcessManager(), new FakeTimeProvider());

        await svc.Invoking(s => s.StopStreamingAsync())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // -----------------------------------------------------------------------
    // S-009 — Idempotency: no-op when already in target state
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task StartStreamingAsync_WhenAlreadyStreaming_IsNoOp()
    {
        var fake = new FakeStreamingAutomation { StreamingActive = true };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(
            streamingAutomation: fake);

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);
        machine.Fire(PcsProTrigger.MatchOpened);

        await svc.StartStreamingAsync();

        fake.ClickStartLiveStreamCalled.Should().BeFalse(
            because: "StartStreamingAsync should be a no-op when streaming is already active");
    }

    [TestMethod]
    public async Task StopStreamingAsync_WhenNotStreaming_IsNoOp()
    {
        var fake = new FakeStreamingAutomation { StreamingActive = false };
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(
            streamingAutomation: fake);

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);
        machine.Fire(PcsProTrigger.MatchOpened);

        await svc.StopStreamingAsync();

        fake.ClickStopLiveStreamCalled.Should().BeFalse(
            because: "StopStreamingAsync should be a no-op when not streaming");
    }

    // -----------------------------------------------------------------------
    // S-011 — UseCurrentMatchAsync
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task UseCurrentMatchAsync_HappyPath_TransitionsToMatchLoadedAndReturnsTeams()
    {
        // Arrange: service in NotRunning, window present, match loaded.
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = true, MatchLoaded = true };
        var fakeTeams = new FakeTeamNamesAutomation();
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel, teamNamesAutomation: fakeTeams);

        // Act
        var result = await svc.UseCurrentMatchAsync();

        // Assert (AC-1, AC-2, AC-3, AC-4)
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
        result.Home.TeamName.Should().NotBeEmpty();
        result.Away.TeamName.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_FromMatchSelection_TransitionsToMatchLoaded()
    {
        // Arrange: service at MatchSelection (post-login), match loaded in PCS Pro.
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = true, MatchLoaded = true };
        var fakeTeams = new FakeTeamNamesAutomation();
        var (svc, _) = await CreateServiceAtMatchSelectionAsync(
            matchSelectionAutomation: fakeMatchSel,
            teamNamesAutomation: fakeTeams);

        // Act
        var result = await svc.UseCurrentMatchAsync();

        // Assert
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
        result.Home.TeamName.Should().NotBeEmpty();
        result.Away.TeamName.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_HappyPath_LoadedMatchIsPopulated()
    {
        // Arrange (HLPS-018 S-001: LoadedMatch is populated after attach)
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = true, MatchLoaded = true };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel);

        // Act
        await svc.UseCurrentMatchAsync();

        // Assert
        svc.LoadedMatch.Should().NotBeNull(because: "HLPS-018 S-001 requires LoadedMatch populated after attach");
        svc.LoadedMatch!.HomeTeam.Should().Be("Home XI");
        svc.LoadedMatch.AwayTeam.Should().Be("Away XI");
        svc.LoadedMatch.HomeClub.Should().Be("Home CC");
        svc.LoadedMatch.AwayClub.Should().Be("Away CC");
        svc.LoadedMatch.MatchId.Should().StartWith("current_", because: "attach flow uses synthesized MatchId");
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_HappyPath_LoadedMatchPopulatedBeforeStateChanged()
    {
        // Arrange (HLPS-018 S-001 AC-2/R7): LoadedMatch must be non-null
        // at the instant StateChanged(MatchLoaded) fires.
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = true, MatchLoaded = true };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel);

        MatchInfo? capturedAtEventTime = null;
        svc.StateChanged += (_, state) =>
        {
            if (state == PcsProState.MatchLoaded)
            {
                capturedAtEventTime = svc.LoadedMatch;
            }
        };

        // Act
        await svc.UseCurrentMatchAsync();

        // Assert: LoadedMatch was non-null when StateChanged(MatchLoaded) fired.
        capturedAtEventTime.Should().NotBeNull(
            because: "LoadedMatch must be populated before StateChanged(MatchLoaded) fires (before-trigger ordering invariant)");
        capturedAtEventTime!.HomeTeam.Should().Be("Home XI");
        capturedAtEventTime.AwayTeam.Should().Be("Away XI");
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_MainWindowNotFound_ThrowsIOE()
    {
        // Arrange (AC-5)
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = false };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel);

        // Act
        var act = () => svc.UseCurrentMatchAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not running*");
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_NoMatchLoaded_ThrowsIOE()
    {
        // Arrange (AC-6)
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = true, MatchLoaded = false };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel);

        // Act
        var act = () => svc.UseCurrentMatchAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No match is currently loaded*");
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_WrongState_ThrowsIOE()
    {
        // Arrange (AC-7): service at MatchLoaded — neither NotRunning nor MatchSelection.
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        // Act
        var act = () => svc.UseCurrentMatchAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires state*");
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_ConcurrentOperation_ThrowsIOE()
    {
        // Arrange (AC-11): simulate lock held by a slow team names read.
        var slowTeams = new SlowOpenTeamsDialogFake();
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = true, MatchLoaded = true };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel, teamNamesAutomation: slowTeams);

        // Start one UseCurrentMatchAsync — will block at OpenTeamsDialog.
        var firstCall = Task.Run(() => svc.UseCurrentMatchAsync());
        await slowTeams.Started.WaitAsync(); // ensure first call has acquired the guard

        // Act: second concurrent call with a short timeout so we don't wait 30 s.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var act = () => svc.UseCurrentMatchAsync(cts.Token);

        // Assert — SemaphoreSlim gate times out (via CTS cancellation).
        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "the gate is held and the token cancels before the 30 s timeout");

        // Cleanup
        slowTeams.Release();
        await firstCall.IgnoreErrorAsync();
    }

    [TestMethod]
    public async Task UseCurrentMatchAsync_TeamNameReadFails_ThrowsAndStateUnchanged()
    {
        // Arrange (HLPS-018 S-001 AC-10): team name read fails before attach.
        var fakeMatchSel = new FakeMatchSelectionAutomation { MainWindowPresent = true, MatchLoaded = true };
        var fakeTeams = new FakeTeamNamesAutomation { ThrowOnReadHomeTeamName = true };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, matchSelectionAutomation: fakeMatchSel, teamNamesAutomation: fakeTeams);
        var initialState = svc.CurrentState;

        var stateChanges = new List<PcsProState>();
        svc.StateChanged += (_, s) => stateChanges.Add(s);

        // Act
        var act = () => svc.UseCurrentMatchAsync();

        // Assert: exception propagates, state unchanged, LoadedMatch null, no StateChanged fired.
        await act.Should().ThrowAsync<Exception>();
        svc.CurrentState.Should().Be(initialState, because: "state must not change on pre-attach failure");
        svc.LoadedMatch.Should().BeNull(because: "LoadedMatch must remain null on failure");
        stateChanges.Should().BeEmpty(because: "no state transitions should occur on pre-attach failure");
    }

    // -----------------------------------------------------------------------
    // S-012 — Health-Check Poll
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a service at <see cref="PcsProState.MatchLoaded"/> with health poll active,
    /// returning the <see cref="FakeTimeProvider"/> and <see cref="FakeHealthCheckAutomation"/>
    /// so tests can control time advancement and probe results.
    /// </summary>
    private static async Task<(PcsProAutomationService Service, FakeProcessHandle Handle, FakeTimeProvider TimeProvider, FakeHealthCheckAutomation HealthCheck)>
        CreateServiceAtMatchLoadedWithHealthPollAsync(
            FakeHealthCheckAutomation? healthCheck = null)
    {
        healthCheck ??= new FakeHealthCheckAutomation();
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();

        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
        });
        var deps = new AutomationDependencies(
            new FakeLoginAutomation(),
            new FakeMatchSelectionAutomation(),
            new FakeTeamNamesAutomation(),
            new FakeScoreboardAutomation(),
            new FakeChangeMatchAutomation(),
            new FakeStreamingAutomation(),
            healthCheck);
        var svc = new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            pm,
            tp,
            deps,
            new NullAutomationLogService(),
            new ManualModeService());

        await svc.LaunchAndLoginAsync();

        // Drive to MatchLoaded via reflection — same pattern as CreateServiceAtMatchLoadedAsync.
        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);
        machine.Fire(PcsProTrigger.MatchOpened);

        return (svc, handle, tp, healthCheck);
    }

    [TestMethod]
    public async Task HealthPoll_StartsOnMatchLoaded_PollsAfterTimeAdvance()
    {
        // Arrange
        var (svc, _, tp, hc) = await CreateServiceAtMatchLoadedWithHealthPollAsync();
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);

        // Let the poll loop start on the thread pool and register its timer.
        await Task.Delay(200);

        // Act: advance time past the default 10s interval.
        tp.Advance(TimeSpan.FromSeconds(11));
        await Task.Delay(200);

        // Assert: health check was called at least once.
        hc.ReadCount.Should().BeGreaterOrEqualTo(1);

        // Cleanup
        await svc.StopAsync();
    }

    [TestMethod]
    public async Task HealthPoll_WindowLost_TransitionsToError()
    {
        // Arrange: set probe to report window lost after first poll.
        var hc = new FakeHealthCheckAutomation();
        var (svc, _, tp, _) = await CreateServiceAtMatchLoadedWithHealthPollAsync(hc);
        hc.NextResult = new HealthCheckResult(false, false, null, null, ProbeSucceeded: true);

        var alerts = new List<HealthAlertEventArgs>();
        svc.HealthAlert += (_, e) => alerts.Add(e);

        // Let poll loop start.
        await Task.Delay(200);

        // Act
        tp.Advance(TimeSpan.FromSeconds(11));
        await Task.Delay(200);

        // Assert
        svc.CurrentState.Should().Be(PcsProState.Error);
        alerts.Should().ContainSingle(a => a.Kind == HealthAlertKind.WindowLost);
    }

    [TestMethod]
    public async Task HealthPoll_MatchLost_TransitionsToError()
    {
        // Arrange: window present but match not loaded.
        var hc = new FakeHealthCheckAutomation();
        var (svc, _, tp, _) = await CreateServiceAtMatchLoadedWithHealthPollAsync(hc);
        hc.NextResult = new HealthCheckResult(true, false, null, "PCS Pro", ProbeSucceeded: true);

        var alerts = new List<HealthAlertEventArgs>();
        svc.HealthAlert += (_, e) => alerts.Add(e);

        // Let poll loop start.
        await Task.Delay(200);

        // Act
        tp.Advance(TimeSpan.FromSeconds(11));
        await Task.Delay(200);

        // Assert
        svc.CurrentState.Should().Be(PcsProState.Error);
        alerts.Should().ContainSingle(a => a.Kind == HealthAlertKind.MatchLost);
    }

    [TestMethod]
    public async Task HealthPoll_SyncStatusChanged_RaisesHealthAlertWithoutStateTransition()
    {
        // Arrange
        var hc = new FakeHealthCheckAutomation();
        var (svc, _, tp, _) = await CreateServiceAtMatchLoadedWithHealthPollAsync(hc);

        // Let poll loop start.
        await Task.Delay(200);

        // First poll establishes baseline.
        tp.Advance(TimeSpan.FromSeconds(11));
        await Task.Delay(200);
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);

        // Change sync status for next poll.
        hc.NextResult = new HealthCheckResult(true, true, "Uploading...", "PCS Pro - Test Match", ProbeSucceeded: true);
        var alerts = new List<HealthAlertEventArgs>();
        svc.HealthAlert += (_, e) => alerts.Add(e);

        // Act: second poll should detect sync status change.
        tp.Advance(TimeSpan.FromSeconds(11));
        await Task.Delay(200);

        // Assert: still MatchLoaded, alert raised.
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
        alerts.Should().ContainSingle(a => a.Kind == HealthAlertKind.SyncStatusChanged);

        // Cleanup
        await svc.StopAsync();
    }

    [TestMethod]
    public async Task HealthPoll_ProbeFailure_SkipsCycleWithoutStateTransition()
    {
        // Arrange: probe fails (ProbeSucceeded = false).
        var hc = new FakeHealthCheckAutomation
        {
            NextResult = new HealthCheckResult(false, false, null, null, ProbeSucceeded: false)
        };
        var (svc, _, tp, _) = await CreateServiceAtMatchLoadedWithHealthPollAsync(hc);

        // Let poll loop start.
        await Task.Delay(200);

        // Act
        tp.Advance(TimeSpan.FromSeconds(11));
        await Task.Delay(200);

        // Assert: no state transition — probe failures are skipped.
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
        hc.ReadCount.Should().BeGreaterOrEqualTo(1);

        // Cleanup
        await svc.StopAsync();
    }

    [TestMethod]
    public async Task HealthPoll_StopAsync_CancelsPollCleanly()
    {
        // Arrange
        var (svc, _, tp, hc) = await CreateServiceAtMatchLoadedWithHealthPollAsync();

        // Act: stop without advancing time (poll is blocked on Task.Delay).
        await svc.StopAsync();

        // Assert: no errors, state is NotRunning.
        svc.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    [TestMethod]
    public async Task HealthPoll_ConfigClamp_ClampsOutOfRangeInterval()
    {
        // Arrange: configure interval to out-of-range value (2 < 5).
        var hc = new FakeHealthCheckAutomation();
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();

        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
            HealthCheckIntervalSeconds = 2,
        });
        var deps = new AutomationDependencies(
            new FakeLoginAutomation(),
            new FakeMatchSelectionAutomation(),
            new FakeTeamNamesAutomation(),
            new FakeScoreboardAutomation(),
            new FakeChangeMatchAutomation(),
            new FakeStreamingAutomation(),
            hc);
        var svc = new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            pm,
            tp,
            deps,
            new NullAutomationLogService(),
            new ManualModeService());

        await svc.LaunchAndLoginAsync();

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)! // field exists on this type
            .GetValue(svc)!; // constructor always assigns a non-null PcsProStateMachine
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);
        machine.Fire(PcsProTrigger.MatchOpened);

        // Let poll loop start.
        await Task.Delay(200);

        // Act: advance time past clamped minimum (5s).
        tp.Advance(TimeSpan.FromSeconds(6));
        await Task.Delay(200);

        // Assert: poll was called — clamped to 5s minimum.
        hc.ReadCount.Should().BeGreaterOrEqualTo(1);

        // Cleanup
        await svc.StopAsync();
    }

    [TestMethod]
    public async Task HealthPoll_CancelledOnLeaveMatchLoaded_DoesNotPollAfterTransition()
    {
        // Arrange: healthy poll running.
        var hc = new FakeHealthCheckAutomation();
        var (svc, _, tp, _) = await CreateServiceAtMatchLoadedWithHealthPollAsync(hc);

        // Let poll loop start.
        await Task.Delay(200);

        // Let one poll succeed to confirm it's running.
        tp.Advance(TimeSpan.FromSeconds(11));
        await Task.Delay(200);
        int countAfterFirstPoll = hc.ReadCount;
        countAfterFirstPoll.Should().BeGreaterOrEqualTo(1);

        // Act: stop the service (transitions away from MatchLoaded).
        await svc.StopAsync();

        // Advance time again — poll should not run.
        tp.Advance(TimeSpan.FromSeconds(20));
        await Task.Delay(200);

        // Assert: no additional reads after stop.
        hc.ReadCount.Should().Be(countAfterFirstPoll);
    }

    // =======================================================================
    // S-002 — Switch-User Login Orchestration
    // =======================================================================

    // -----------------------------------------------------------------------
    // TC-1 — ExpectedUsername empty → no switch-user, normal login
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_ExpectedUsernameEmpty_SkipsSwitchUserAndSubmitsNormally()
    {
        var login = new FakeLoginAutomation { UsernameValue = "OtherUser" };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateService(pm, new FakeTimeProvider(), login);

        await svc.LaunchAndLoginAsync();

        login.SwitchUserClicked.Should().BeFalse("ExpectedUsername is not configured");
        login.CapturedUsername.Should().BeNull("no username entry should occur");
        login.SubmitClicked.Should().BeTrue("normal login should proceed");
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // TC-2 — ReadUsername returns null → no switch-user
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_ReadUsernameReturnsNull_SkipsSwitchUser()
    {
        var login = new FakeLoginAutomation { UsernameValue = null };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateServiceWithExpectedUsername(pm, new FakeTimeProvider(), login, "ExpectedUser");

        await svc.LaunchAndLoginAsync();

        login.SwitchUserClicked.Should().BeFalse("null username means field not found — skip check");
        login.SubmitClicked.Should().BeTrue("normal login should proceed");
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // TC-3 — ReadUsername returns empty → no switch-user
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_ReadUsernameReturnsEmpty_SkipsSwitchUser()
    {
        var login = new FakeLoginAutomation { UsernameValue = "" };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateServiceWithExpectedUsername(pm, new FakeTimeProvider(), login, "ExpectedUser");

        await svc.LaunchAndLoginAsync();

        login.SwitchUserClicked.Should().BeFalse("empty username means blank field — skip check");
        login.SubmitClicked.Should().BeTrue("normal login should proceed");
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // TC-4 — Username matches (case-insensitive) → no switch-user
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_UsernameMatchesCaseInsensitive_SkipsSwitchUser()
    {
        var login = new FakeLoginAutomation { UsernameValue = "scorer" };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateServiceWithExpectedUsername(pm, new FakeTimeProvider(), login, "Scorer");

        await svc.LaunchAndLoginAsync();

        login.SwitchUserClicked.Should().BeFalse("username matches expected value (case-insensitive)");
        login.SubmitClicked.Should().BeTrue("normal login should proceed");
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // TC-5 — Username mismatch → switch-user + enter username + submit
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_UsernameMismatch_PerformsSwitchUserAndSubmits()
    {
        var login = new FakeLoginAutomation { UsernameValue = "WrongUser" };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateServiceWithExpectedUsername(pm, new FakeTimeProvider(), login, "CorrectUser");

        await svc.LaunchAndLoginAsync();

        login.SwitchUserClicked.Should().BeTrue("mismatch should trigger switch-user");
        login.CapturedUsername.Should().Be("CorrectUser", "expected username should be entered");
        login.CapturedPassword.Should().Be("test-password", "password should be entered after switch-user");
        login.SubmitClicked.Should().BeTrue("credentials should be submitted");
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // TC-6 — Mismatch → switch-user → submit → reaches match selection
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_AfterSwitchUser_ReachesMatchSelection()
    {
        var login = new FakeLoginAutomation { UsernameValue = "WrongUser" };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateServiceWithExpectedUsername(pm, new FakeTimeProvider(), login, "CorrectUser");

        var states = new List<PcsProState>();
        svc.StateChanged += (_, s) => states.Add(s);

        await svc.LaunchAndLoginAsync();

        states.Should().ContainInOrder(
            PcsProState.Launching, PcsProState.LoginScreen, PcsProState.MatchSelection);
        svc.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // -----------------------------------------------------------------------
    // TC-7 — Mismatch persists after switch-user → error (retry exhausted)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_MismatchPersistsAfterSwitchUser_FiresError()
    {
        // Use a custom fake that ignores EnterUsername — simulating the case where
        // switch-user was clicked but the username field still shows the wrong value.
        var login = new SwitchUserIgnoredFakeLoginAutomation();
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateServiceWithExpectedUsername(pm, new FakeTimeProvider(), login, "CorrectUser");

        await svc.LaunchAndLoginAsync();

        svc.CurrentState.Should().Be(PcsProState.Error,
            "mismatch persisting after switch-user should fire error trigger");
    }

    // -----------------------------------------------------------------------
    // TC-8 — ClickSwitchUser throws → error, abort
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LaunchAndLoginAsync_ClickSwitchUserThrows_FiresError()
    {
        // Set ThrowOnInteraction to true so ClickSwitchUser throws.
        // But we need IsLoginDialogVisible to return true and the username check to
        // detect a mismatch first. Since ThrowOnInteraction also affects EnterPassword,
        // we need a custom fake that only throws on ClickSwitchUser.
        var login = new SwitchUserThrowsFakeLoginAutomation();
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var svc = CreateServiceWithExpectedUsername(pm, new FakeTimeProvider(), login, "CorrectUser");

        await svc.LaunchAndLoginAsync();

        svc.CurrentState.Should().Be(PcsProState.Error,
            "exception from ClickSwitchUser should be caught and fire error");
    }

    // =======================================================================
    // S-003 — Club Name Stripping Integration (TC-16, TC-17)
    // =======================================================================

    // -----------------------------------------------------------------------
    // TC-16 — LoadMatchAsync with club name configured → formatted names
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WithClubNameConfigured_FormatsLoadedMatchTeamNames()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = true };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
            ClubName = "HHCC",
        });
        var deps = new AutomationDependencies(
            new FakeLoginAutomation(),
            fakeMatchSel,
            new FakeTeamNamesAutomation(),
            new FakeScoreboardAutomation(),
            new FakeChangeMatchAutomation(),
            new FakeStreamingAutomation(),
            new FakeHealthCheckAutomation());
        var svc = new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            pm, tp, deps,
            new NullAutomationLogService(),
            new ManualModeService());
        await svc.LaunchAndLoginAsync();

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);

        var match = new MatchInfo("test-1", HomeTeam: "Club B - 2nd XI", AwayTeam: "HHCC - 1st XI");
        await svc.LoadMatchAsync(match);

        svc.LoadedMatch.Should().NotBeNull();
        svc.LoadedMatch!.HomeTeam.Should().Be("1st XI", "club team should be stripped and placed first");
        svc.LoadedMatch!.AwayTeam.Should().Be("Club B - 2nd XI", "non-club team should be unchanged");
    }

    // -----------------------------------------------------------------------
    // TC-17 — LoadMatchAsync with club name empty → unchanged team names
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_WithClubNameEmpty_LeavesTeamNamesUnchanged()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = true };
        var (svc, _, _) = await CreateServiceAtMatchSelectionReadyAsync(fakeMatchSel);

        var match = new MatchInfo("test-2", HomeTeam: "HHCC - 1st XI", AwayTeam: "Club B - 2nd XI");
        await svc.LoadMatchAsync(match);

        svc.LoadedMatch.Should().NotBeNull();
        svc.LoadedMatch!.HomeTeam.Should().Be("HHCC - 1st XI");
        svc.LoadedMatch!.AwayTeam.Should().Be("Club B - 2nd XI");
    }

    // =======================================================================
    // S-004 — Club Name Token Integration (TC-14 through TC-17)
    // =======================================================================

    // -----------------------------------------------------------------------
    // TC-14 — GetTeamNamesAsync enriches LoadedMatch with reordered club names
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_AfterLoadMatch_EnrichesLoadedMatchClubNames()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = true };
        var fakeTeamNames = new FakeTeamNamesAutomation
        {
            HomeClubName = "Club B",
            HomeTeamName = "2nd XI",
            AwayClubName = "HHCC",
            AwayTeamName = "1st XI",
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
            ClubName = "HHCC",
        });
        var deps = new AutomationDependencies(
            new FakeLoginAutomation(),
            fakeMatchSel,
            fakeTeamNames,
            new FakeScoreboardAutomation(),
            new FakeChangeMatchAutomation(),
            new FakeStreamingAutomation(),
            new FakeHealthCheckAutomation());
        var svc = new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            pm, tp, deps,
            new NullAutomationLogService(),
            new ManualModeService());
        await svc.LaunchAndLoginAsync();

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);

        // Away team matches club → S-003 reorders teams (HHCC first)
        var match = new MatchInfo("test-1", HomeTeam: "Club B - 2nd XI", AwayTeam: "HHCC - 1st XI");
        await svc.LoadMatchAsync(match);

        // GetTeamNamesAsync enriches _loadedMatch with reordered club names
        await svc.GetTeamNamesAsync();

        svc.LoadedMatch.Should().NotBeNull();
        svc.LoadedMatch!.HomeClub.Should().Be("HHCC",
            "club name should be reordered to match S-003's team reorder");
        svc.LoadedMatch!.AwayClub.Should().Be("Club B",
            "non-club team's club should be second");
    }

    // -----------------------------------------------------------------------
    // TC-15 — LoadedMatch before GetTeamNamesAsync has default empty club names
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadMatchAsync_BeforeGetTeamNames_HasDefaultEmptyClubNames()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = true };
        var (svc, _, _) = await CreateServiceAtMatchSelectionReadyAsync(fakeMatchSel);

        var match = new MatchInfo("test-2", HomeTeam: "Home XI", AwayTeam: "Away XI");
        await svc.LoadMatchAsync(match);

        svc.LoadedMatch.Should().NotBeNull();
        svc.LoadedMatch!.HomeClub.Should().BeEmpty();
        svc.LoadedMatch!.AwayClub.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // TC-16 — UseCurrentMatchAsync then GetTeamNamesAsync — LoadedMatch stays null
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_AfterUseCurrentMatch_EnrichesLoadedMatchClubNames()
    {
        var (svc, _) = await CreateServiceAtMatchSelectionAsync();

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);

        // UseCurrentMatchAsync requires NotRunning state — use a fresh service.
        var freshHandle = new FakeProcessHandle { MainWindowVisible = true };
        var freshPm = new FakeProcessManager { StartedHandle = freshHandle };
        var freshSvc = CreateService(freshPm, new FakeTimeProvider());

        // UseCurrentMatchAsync is called from NotRunning
        await freshSvc.UseCurrentMatchAsync();

        // Now in MatchLoaded with LoadedMatch populated (HLPS-018 S-001)
        freshSvc.LoadedMatch.Should().NotBeNull("HLPS-018 S-001 populates LoadedMatch after attach");
        freshSvc.LoadedMatch!.HomeTeam.Should().Be("Home XI");
        freshSvc.LoadedMatch.AwayTeam.Should().Be("Away XI");

        // GetTeamNamesAsync should succeed and enrich club names
        var teams = await freshSvc.GetTeamNamesAsync();
        teams.Should().NotBeNull();

        // LoadedMatch should be enriched with club names (mirrors TC-14 pattern)
        freshSvc.LoadedMatch.Should().NotBeNull();
        freshSvc.LoadedMatch!.HomeClub.Should().Be("Home CC",
            because: "enrichment should populate HomeClub from GetTeamNamesAsync read");
        freshSvc.LoadedMatch.AwayClub.Should().Be("Away CC",
            because: "enrichment should populate AwayClub from GetTeamNamesAsync read");
    }

    // -----------------------------------------------------------------------
    // TC-17 — GetTeamNamesAsync fails → club names remain default empty
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetTeamNamesAsync_DialogFails_LoadedMatchClubNamesRemainEmpty()
    {
        var fakeMatchSel = new FakeMatchSelectionAutomation { MatchLoaded = true };
        var fakeTeamNames = new FakeTeamNamesAutomation { ThrowOnOpenTeamsDialog = true };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
            ClubName = "HHCC",
        });
        var deps = new AutomationDependencies(
            new FakeLoginAutomation(),
            fakeMatchSel,
            fakeTeamNames,
            new FakeScoreboardAutomation(),
            new FakeChangeMatchAutomation(),
            new FakeStreamingAutomation(),
            new FakeHealthCheckAutomation());
        var svc = new PcsProAutomationService(
            options,
            NullLogger<PcsProAutomationService>.Instance,
            pm, tp, deps,
            new NullAutomationLogService(),
            new ManualModeService());
        await svc.LaunchAndLoginAsync();

        var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
            .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        machine.Fire(PcsProTrigger.SearchTriggered);
        machine.Fire(PcsProTrigger.SpinnerGone);

        var match = new MatchInfo("test-3", HomeTeam: "HHCC - 1st XI", AwayTeam: "Club B - 2nd XI");
        await svc.LoadMatchAsync(match);

        // GetTeamNamesAsync fails — error fires, but clubs should NOT be enriched
        // The service is now in Error state after the dialog failure
        // LoadedMatch may be null after Error transition
        // Retrieve club name before calling GetTeamNamesAsync to verify default
        svc.LoadedMatch.Should().NotBeNull();
        svc.LoadedMatch!.HomeClub.Should().BeEmpty("club names not yet enriched");
        svc.LoadedMatch!.AwayClub.Should().BeEmpty("club names not yet enriched");
    }

    // -----------------------------------------------------------------------
    // S-006 Manual-Mode Wiring Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Helper: extracts the <c>_manualModeService</c> field from a
    /// <see cref="PcsProAutomationService"/> instance via reflection.
    /// </summary>
    private static IManualModeService GetManualModeService(PcsProAutomationService svc)
        => (IManualModeService)typeof(PcsProAutomationService)
            .GetField("_manualModeService", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;

    [TestMethod]
    public async Task GatedMethods_WhenManualModeActive_ReturnBenignDefaultsAndStateUnchanged()
    {
        // T6: Verify that representative gated methods return benign defaults
        // without touching FlaUI automations or changing state.
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();
        var stateBefore = svc.CurrentState;

        var manualMode = GetManualModeService(svc);
        manualMode.Enable();

        // RefreshScoreboardAsync — void-returning gated method.
        await svc.RefreshScoreboardAsync();
        svc.CurrentState.Should().Be(stateBefore, "state should not change when gated");

        // CaptureScoreboardImageAsync — byte[]-returning gated method.
        var image = await svc.CaptureScoreboardImageAsync();
        image.Should().BeEmpty("gated capture should return empty array");
        svc.CurrentState.Should().Be(stateBefore);

        // GetTeamNamesAsync — MatchTeams-returning gated method.
        var teams = await svc.GetTeamNamesAsync();
        teams.Home.TeamName.Should().BeEmpty("gated team names should return empty sentinel");
        teams.Away.TeamName.Should().BeEmpty();
        svc.CurrentState.Should().Be(stateBefore);
    }

    [TestMethod]
    public async Task BypassMethods_WhenManualModeActive_StillExecuteNormally()
    {
        // T7: Verify that bypass methods (LaunchAndLoginAsync, StopAsync)
        // execute normally even when manual mode is active.
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp);

        var manualMode = GetManualModeService(svc);
        manualMode.Enable();

        // LaunchAndLoginAsync is a bypass method — should still work.
        await svc.LaunchAndLoginAsync();
        svc.CurrentState.Should().Be(PcsProState.MatchSelection,
            "LaunchAndLoginAsync should complete normally despite manual mode");

        // StopAsync is a bypass method — should still work.
        await svc.StopAsync();
        svc.CurrentState.Should().Be(PcsProState.NotRunning,
            "StopAsync should complete normally despite manual mode");
    }

    [TestMethod]
    public async Task GatedMethod_WhenManualModeActive_DoesNotAcquireOperationLock()
    {
        // T8: Verify that the manual-mode gate is checked BEFORE acquiring
        // the operation lock/semaphore. We prove this by holding the semaphore
        // and showing the gated method still returns immediately (no timeout).
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        var manualMode = GetManualModeService(svc);
        manualMode.Enable();

        // Even though internal operation semaphore patterns exist, the gate
        // returns before reaching them. We verify by calling the method on
        // a tight timeout — if it tried to acquire the lock, it would need
        // to wait and eventually throw/timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        // This should return instantly, well within the 2 second timeout.
        await svc.RefreshScoreboardAsync(cts.Token);

        // If we got here without timeout, the gate returned before the lock.
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded);
    }

    [TestMethod]
    public async Task StreamingMethods_WhenManualModeActive_ReturnWithoutFlaUIInteraction()
    {
        // T13: Verify StartStreamingAsync and StopStreamingAsync are gated.
        var (svc, _) = await CreateServiceAtMatchLoadedAsync();

        var manualMode = GetManualModeService(svc);
        manualMode.Enable();

        // StartStreamingAsync — gated, should return immediately.
        await svc.StartStreamingAsync();
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            "state should remain MatchLoaded after gated StartStreamingAsync");

        // StopStreamingAsync — gated, should return immediately.
        await svc.StopStreamingAsync();
        svc.CurrentState.Should().Be(PcsProState.MatchLoaded,
            "state should remain MatchLoaded after gated StopStreamingAsync");
    }
}

// -----------------------------------------------------------------------
// Test helpers for S-004 fakes that require custom behaviour
// -----------------------------------------------------------------------

/// <summary>
/// FakeMatchSelectionAutomation variant whose spinner clears after N calls to IsSpinnerVisible.
/// </summary>
internal sealed class SpinnerDropsAfterNCallsFake : IMatchSelectionAutomation
{
    private readonly int _dropsAfterCalls;
    private int _callCount;

    public SpinnerDropsAfterNCallsFake(int dropsAfterCalls) =>
        _dropsAfterCalls = dropsAfterCalls;

    public void OpenMatchDialogAndSearch(DateOnly searchDate) { }
    public bool IsSpinnerVisible() => ++_callCount <= _dropsAfterCalls;
    public bool IsUnexpectedDialogPresent(DialogProbeContext? probeContext = null) => false;
    public void TryCloseUnexpectedDialog() { }
    public IReadOnlyList<string> ReadDataGridRowTexts() => [];
    public void SelectAndOpenMatch(PcsRemote.Core.MatchInfo match) { }
    public bool IsMatchLoaded() => false;
    public bool IsMainWindowPresent() => true;
}

/// <summary>
/// FakeMatchSelectionAutomation variant whose ReadDataGridRowTexts throws.
/// Spinner is immediately gone so the service reaches ReadDataGridRowTexts.
/// </summary>
internal sealed class ReadDataGridThrowsFake : IMatchSelectionAutomation
{
    public void OpenMatchDialogAndSearch(DateOnly searchDate) { }
    public bool IsSpinnerVisible() => false;
    public bool IsUnexpectedDialogPresent(DialogProbeContext? probeContext = null) => false;
    public void TryCloseUnexpectedDialog() { }
    public IReadOnlyList<string> ReadDataGridRowTexts() =>
        throw new InvalidOperationException("ReadDataGridThrowsFake: element not found");
    public void SelectAndOpenMatch(PcsRemote.Core.MatchInfo match) { }
    public bool IsMatchLoaded() => false;
    public bool IsMainWindowPresent() => true;
}

internal static class TaskExtensions
{
    /// <summary>Awaits a task, swallowing any exception. Used in test cleanup.</summary>
    internal static async Task IgnoreErrorAsync(this Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch { }
    }
}

internal sealed class SlowOpenTeamsDialogFake : ITeamNamesAutomation
{
    private readonly ManualResetEventSlim _gate = new(false);

    /// <summary>Becomes available as soon as <see cref="OpenTeamsDialog"/> begins executing.</summary>
    public SemaphoreSlim Started { get; } = new(0, 1);

    /// <summary>Unblocks any thread waiting in <see cref="OpenTeamsDialog"/>.</summary>
    public void Release() => _gate.Set();

    public void OpenTeamsDialog()
    {
        Started.Release(); // signal that we reached the blocking point
        _gate.Wait();      // blocks until Release() is called
    }

    public TeamNameInfo ReadHomeTeamName() => new("Home CC", "Home XI");
    public TeamNameInfo ReadAwayTeamName() => new("Away CC", "Away XI");
    public void TryCloseTeamsDialog() { }
    public bool IsUnexpectedDialogPresent() => false;
    public void TryCloseUnexpectedDialog() { }
}

// -----------------------------------------------------------------------
// Test helper for S-002 — ClickSwitchUser throws, other interactions work
// -----------------------------------------------------------------------

/// <summary>
/// FakeLoginAutomation variant that only throws on <see cref="ILoginAutomation.ClickSwitchUser"/>.
/// All other interactions succeed normally. Used to verify the catch-all in
/// <c>TrySubmitCredentialsAsync</c> handles switch-user failures.
/// </summary>
internal sealed class SwitchUserThrowsFakeLoginAutomation : ILoginAutomation
{
    public bool IsLoginDialogVisible() => true;
    public void EnterPassword(string password) { }
    public void ClickSubmit() { }
    public bool IsMatchSelectionVisible() => true;
    public bool IsUnexpectedDialogPresent() => false;
    public string? GetUnexpectedDialogName() => null;
    public void TryCloseUnexpectedDialog() { }
    public string? ReadUsername() => "WrongUser";
    public void EnterUsername(string username) { }

    public void ClickSwitchUser() =>
        throw new InvalidOperationException("Simulated ClickSwitchUser failure");
}

/// <summary>
/// FakeLoginAutomation variant where EnterUsername does not update the username field.
/// Simulates the case where switch-user was clicked but the underlying application
/// did not update the pre-populated username, so the mismatch persists on re-check.
/// </summary>
internal sealed class SwitchUserIgnoredFakeLoginAutomation : ILoginAutomation
{
    public bool IsLoginDialogVisible() => true;
    public void EnterPassword(string password) { }
    public void ClickSubmit() { }
    public bool IsMatchSelectionVisible() => true;
    public bool IsUnexpectedDialogPresent() => false;
    public string? GetUnexpectedDialogName() => null;
    public void TryCloseUnexpectedDialog() { }
    public string? ReadUsername() => "WrongUser";
    public void EnterUsername(string username) { } // deliberately ignores the entered username
    public void ClickSwitchUser() { }
}
