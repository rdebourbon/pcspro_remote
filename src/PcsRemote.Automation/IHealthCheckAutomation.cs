using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// Reads PCS Pro state signals for the periodic health-check poll.
/// Implementations must be safe to call rapidly and must not open dialogs
/// or modify application state.
/// </summary>
internal interface IHealthCheckAutomation
{
    /// <summary>
    /// Reads a snapshot of PCS Pro health signals. Never throws — returns
    /// a degraded <see cref="HealthCheckResult"/> with
    /// <see cref="HealthCheckResult.ProbeSucceeded"/> = <see langword="false"/>
    /// on transient FlaUI errors.
    /// </summary>
    HealthCheckResult ReadHealthSignals();
}
