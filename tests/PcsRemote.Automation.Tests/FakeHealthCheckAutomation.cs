using PcsRemote.Core;

namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="IHealthCheckAutomation"/>.
/// Returns a configurable <see cref="HealthCheckResult"/> and records call count.
/// </summary>
internal sealed class FakeHealthCheckAutomation : IHealthCheckAutomation
{
    /// <summary>
    /// The result that <see cref="ReadHealthSignals"/> returns.
    /// Defaults to a healthy snapshot (window present, match loaded, probe succeeded).
    /// </summary>
    public HealthCheckResult NextResult { get; set; } =
        new(true, true, "Synced", "PCS Pro - Test Match", ProbeSucceeded: true);

    /// <summary>Number of times <see cref="ReadHealthSignals"/> has been called.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc/>
    public HealthCheckResult ReadHealthSignals()
    {
        ReadCount++;
        return NextResult;
    }
}
