using System.Diagnostics;

namespace PcsRemote.Automation;

/// <summary>
/// Abstracts a running OS process to allow deterministic unit testing without a real process.
/// The real implementation wraps <see cref="Process"/>; test fakes control exit signalling and
/// window detection directly.
/// </summary>
internal interface IProcessHandle : IDisposable
{
    /// <summary>OS process identifier.</summary>
    int Id { get; }

    /// <summary>True when the process has exited (including unexpectedly).</summary>
    bool HasExited { get; }

    /// <summary>
    /// Waits asynchronously for the process to exit. Throws <see cref="OperationCanceledException"/>
    /// if <paramref name="ct"/> is cancelled before exit is observed.
    /// </summary>
    Task WaitForExitAsync(CancellationToken ct);

    /// <summary>
    /// Sends a WM_CLOSE message to the main window. Returns <c>true</c> if the message was sent.
    /// Returns <c>false</c> or is a no-op when the process has no main window or has already exited.
    /// </summary>
    bool CloseMainWindow();

    /// <summary>Immediately terminates the process. No-op if the process has already exited.</summary>
    void Kill();

    /// <summary>
    /// Returns a non-null value when the process main window is visible and ready for automation.
    /// Returns <c>null</c> when the window has not yet appeared.
    /// <para>
    /// Real implementation: refreshes the process and checks <c>MainWindowHandle</c>.
    /// Test fakes: return a controlled non-null sentinel to simulate window appearance.
    /// </para>
    /// </summary>
    object? TryGetMainWindow();
}
