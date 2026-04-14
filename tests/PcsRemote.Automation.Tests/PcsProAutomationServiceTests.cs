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
        FakeLoginAutomation? loginAutomation = null)
    {
        var options = Options.Create(new PcsProOptions
        {
            ExecutablePath = @"C:\cricket.exe",
            WorkingDirectory = @"C:\",
            Password = "test-password",
        });
        var scoreboardOptions = Options.Create(new ScoreboardOptions());
        return new PcsProAutomationService(
            options,
            scoreboardOptions,
            NullLogger<PcsProAutomationService>.Instance,
            processManager,
            timeProvider,
            loginAutomation ?? new FakeLoginAutomation());
    }

    /// <summary>
    /// Creates a service and runs <see cref="PcsProAutomationService.LaunchAndLoginAsync"/>
    /// to completion, with the process window immediately visible and login succeeding.
    /// Returns the service in <see cref="PcsProState.MatchSelection"/> state.
    /// </summary>
    private static async Task<(PcsProAutomationService Service, FakeProcessHandle Handle)>
        CreateServiceAtMatchSelectionAsync(FakeLoginAutomation? loginAutomation = null)
    {
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var tp = new FakeTimeProvider();
        var svc = CreateService(pm, tp, loginAutomation ?? new FakeLoginAutomation());
        await svc.LaunchAndLoginAsync();
        return (svc, handle);
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
            MatchSelectionVisible = false,
        };
        var handle = new FakeProcessHandle { MainWindowVisible = true };
        var pm = new FakeProcessManager { StartedHandle = handle };
        var fakeTime = new FakeTimeProvider();
        var svc = CreateService(pm, fakeTime, fake);

        var launchTask = svc.LaunchAndLoginAsync();

        // Let the service reach the login polling loop.
        await Task.Delay(100);
        fakeTime.Advance(TimeSpan.FromSeconds(PcsProStateMachine.LoginScreenTimeoutSeconds + 1));

        await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CurrentState.Should().Be(PcsProState.Error);
        svc.LastErrorReason.Should().NotBeNullOrEmpty();
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
            Options.Create(new ScoreboardOptions()),
            NullLogger<PcsProAutomationService>.Instance,
            pm,
            new FakeTimeProvider(),
            fake);

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
            MatchSelectionVisible = false,
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
            MatchSelectionVisible = false,
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
