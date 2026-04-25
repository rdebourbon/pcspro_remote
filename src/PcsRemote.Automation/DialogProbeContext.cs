namespace PcsRemote.Automation;

/// <summary>
/// Tracks per-probe-context hysteresis state for unexpected dialog detection.
/// Each looped probe site should hold its own instance to prevent cross-site priming.
/// Single-shot probes do not use this class — they call <see cref="UIAutomationHelpers.HasUnexpectedDialog"/>
/// directly (immediate detection, no hysteresis).
/// </summary>
internal sealed class DialogProbeContext
{
    private int _consecutiveCount;
    private string? _lastDialogIdentity;

    /// <summary>
    /// Evaluates whether an unexpected dialog detection should be confirmed (returned as <c>true</c>)
    /// based on two-tick hysteresis. A dialog must be observed on two consecutive ticks with the same
    /// identity before the detector confirms it.
    /// </summary>
    /// <param name="hasUnexpectedDialog">Whether this tick found a qualifying unexpected dialog.</param>
    /// <param name="dialogIdentity">
    /// The identity (typically <c>Name</c>) of the unexpected dialog found, or <c>null</c> if none.
    /// </param>
    /// <returns><c>true</c> if hysteresis confirms the detection (two consecutive sightings of same dialog).</returns>
    internal bool Evaluate(bool hasUnexpectedDialog, string? dialogIdentity)
    {
        if (!hasUnexpectedDialog)
        {
            _consecutiveCount = 0;
            _lastDialogIdentity = null;
            return false;
        }

        if (_lastDialogIdentity != null &&
            !string.Equals(_lastDialogIdentity, dialogIdentity, StringComparison.Ordinal))
        {
            // Different dialog identity — reset and start new cycle
            _consecutiveCount = 1;
            _lastDialogIdentity = dialogIdentity;
            return false;
        }

        _lastDialogIdentity = dialogIdentity;
        _consecutiveCount++;

        if (_consecutiveCount >= 2)
        {
            // Confirmed — reset for next cycle (persistent dialog cadence)
            _consecutiveCount = 0;
            _lastDialogIdentity = null;
            return true;
        }

        return false;
    }
}
