namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="IProcessHandle"/>.
/// Controls exit signalling, main window visibility, and records method calls.
/// </summary>
internal sealed class FakeProcessHandle : IProcessHandle
{
    private readonly TaskCompletionSource _exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id { get; init; } = 1234;

    /// <summary>Set to true before calling <see cref="SignalExit"/> to make <see cref="HasExited"/> reflect exit.</summary>
    public bool HasExited { get; set; }

    /// <summary>When true, <see cref="TryGetMainWindow"/> returns a non-null sentinel.</summary>
    public bool MainWindowVisible { get; set; }

    /// <summary>Ordered log of method calls for verification of call sequence.</summary>
    public List<string> CallLog { get; } = new();

    /// <summary>
    /// Signals the fake process as having exited, completing any pending
    /// <see cref="WaitForExitAsync"/> awaits.
    /// </summary>
    public void SignalExit()
    {
        HasExited = true;
        _exitTcs.TrySetResult();
    }

    public Task WaitForExitAsync(CancellationToken ct) =>
        _exitTcs.Task.WaitAsync(ct);

    /// <summary>
    /// When true (default), <see cref="CloseMainWindow"/> automatically calls
    /// <see cref="SignalExit"/>, simulating a process that responds to the close request.
    /// Set to false to simulate a process that ignores the close and requires force-kill.
    /// </summary>
    public bool AutoExitOnCloseMainWindow { get; set; } = true;

    public bool CloseMainWindow()
    {
        CallLog.Add(nameof(CloseMainWindow));
        if (AutoExitOnCloseMainWindow)
            SignalExit();
        return true;
    }

    public void Kill()
    {
        CallLog.Add(nameof(Kill));
    }

    public object? TryGetMainWindow() =>
        MainWindowVisible ? new object() : null;

    public void Dispose() { }
}
