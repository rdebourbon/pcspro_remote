using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IStreamingAutomation"/>.
/// Ported from diagnostic tool Step 11 (<c>Step11_StartStopLiveStream</c>).
/// Handles the full start/stop live stream cycle including consent dialog handling.
/// </summary>
internal sealed class FlaUiStreamingAutomation : IStreamingAutomation
{
    private const int ConsentDialogTimeoutMs = 5_000;
    private const int PostClickDelayMs = 300;

    private readonly PcsProWindowLocator _locator;
    private readonly ILogger<FlaUiStreamingAutomation> _logger;

    public FlaUiStreamingAutomation(PcsProWindowLocator locator, ILogger<FlaUiStreamingAutomation> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void ClickStartLiveStream()
    {
        var window = GetMainWindowOrThrow();
        var cf = _locator.Automation.ConditionFactory;

        var videoPane = FindVideoDisplayToolWindow(window, cf);
        UIAutomationHelpers.ActivateToolWindow(videoPane, cf, _logger);
        Thread.Sleep(PostClickDelayMs);

        var searchRoot = FindStreamingControlsSearchRoot(videoPane, cf);

        var startButton = UIAutomationHelpers.FindButtonByChildText(
            searchRoot, KnownElements.StartLiveStreamButtonText, cf, _logger);

        if (startButton == null)
        {
            throw new InvalidOperationException(
                "Start Live Stream button not found in Video Display ToolWindow.");
        }

        var error = UIAutomationHelpers.InvokeButtonSafely(startButton, _logger);
        if (error != null)
        {
            throw new InvalidOperationException(
                $"Could not click Start Live Stream: {error}");
        }

        _logger.LogInformation("Clicked Start Live Stream button");
    }

    /// <inheritdoc/>
    public void HandleConsentDialogs()
    {
        var window = GetMainWindowOrThrow();
        var cf = _locator.Automation.ConditionFactory;

        var consentDialog = FindConsentDialog(window, cf);
        if (consentDialog == null)
        {
            throw new InvalidOperationException(
                "Video Consent dialog did not appear within the timeout after clicking Start Live Stream.");
        }

        ClickConsentButton(consentDialog, cf);
        Thread.Sleep(PostClickDelayMs);

        HandleOptionalMatchCentreDialog(window, cf);
    }

    /// <inheritdoc/>
    public void ClickStopLiveStream()
    {
        var window = GetMainWindowOrThrow();
        var cf = _locator.Automation.ConditionFactory;

        var videoPane = FindVideoDisplayToolWindow(window, cf);
        UIAutomationHelpers.ActivateToolWindow(videoPane, cf, _logger);
        Thread.Sleep(PostClickDelayMs);

        var searchRoot = FindStreamingControlsSearchRoot(videoPane, cf);

        var stopButton = UIAutomationHelpers.FindButtonByChildText(
            searchRoot, KnownElements.StopLiveStreamButtonText, cf, _logger);

        if (stopButton == null)
        {
            throw new InvalidOperationException(
                "Stop Live Stream button not found in Video Display ToolWindow. Is streaming active?");
        }

        var error = UIAutomationHelpers.InvokeButtonSafely(stopButton, _logger);
        if (error != null)
        {
            throw new InvalidOperationException(
                $"Could not click Stop Live Stream: {error}");
        }

        _logger.LogInformation("Clicked Stop Live Stream button");
    }

    /// <inheritdoc/>
    public bool IsStreamingActive()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return false;
            }

            var cf = _locator.Automation.ConditionFactory;
            var videoPane = UIAutomationHelpers.FindDescendant(
                window, cf.ByAutomationId(KnownElements.VideoDisplayToolWindowAutomationId));

            if (videoPane == null)
            {
                return false;
            }

            var stopButton = UIAutomationHelpers.FindButtonByChildText(
                videoPane, KnownElements.StopLiveStreamButtonText, cf);

            return stopButton != null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsStreamingActive failed with exception");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return false;
            }

            return UIAutomationHelpers.HasUnexpectedDialog(window, _locator.Automation.ConditionFactory);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsUnexpectedDialogPresent failed with exception");
            return false;
        }
    }

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return;
            }

            UIAutomationHelpers.TryCloseFirstUnexpectedDialog(
                window, _locator.Automation.ConditionFactory, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryCloseUnexpectedDialog failed with exception");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private AutomationElement GetMainWindowOrThrow()
    {
        return _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");
    }

    private AutomationElement FindVideoDisplayToolWindow(AutomationElement window, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var videoPane = UIAutomationHelpers.FindDescendant(
            window, cf.ByAutomationId(KnownElements.VideoDisplayToolWindowAutomationId));

        if (videoPane != null)
        {
            return videoPane;
        }

        // Fallback: search by Name containing "Video Display"
        var allToolWindows = UIAutomationHelpers.FindAllDescendants(
            window, cf.ByClassName(KnownElements.ToolWindowClassName));

        foreach (var tw in allToolWindows)
        {
            var twName = UIAutomationHelpers.SafeGetName(tw);
            if (twName.Contains("Video Display", StringComparison.OrdinalIgnoreCase))
            {
                return tw;
            }
        }

        throw new InvalidOperationException("Video Display ToolWindow not found.");
    }

    private AutomationElement FindStreamingControlsSearchRoot(AutomationElement videoPane, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var streamControls = UIAutomationHelpers.FindDescendant(
            videoPane, cf.ByAutomationId(KnownElements.LiveStreamingControlsAutomationId));

        if (streamControls != null)
        {
            _logger.LogDebug("Found LiveStreamingControls container");
            return streamControls;
        }

        return videoPane;
    }

    private AutomationElement? FindConsentDialog(AutomationElement window, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        // Search top-level windows first
        var consentDialog = UIAutomationHelpers.WaitForElement(
            () =>
            {
                var topWindows = _locator.Automation.GetDesktop().FindAllChildren(
                    cf.ByControlType(ControlType.Window));

                foreach (var w in topWindows)
                {
                    var wTitle = UIAutomationHelpers.SafeGetName(w);
                    if (wTitle.Contains(KnownElements.VideoConsentDialogNamePattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return w;
                    }
                }

                // Fallback: search as child window of main window
                return UIAutomationHelpers.FindDescendant(
                    window, cf.ByName(KnownElements.VideoConsentDialogNamePattern));
            },
            timeoutMs: ConsentDialogTimeoutMs);

        return consentDialog;
    }

    private void ClickConsentButton(AutomationElement consentDialog, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var consentButton = UIAutomationHelpers.FindDescendant(
            consentDialog, cf.ByName(KnownElements.VideoConsentedButtonName));

        if (consentButton == null)
        {
            consentButton = UIAutomationHelpers.FindDescendant(
                consentDialog, cf.ByAutomationId(KnownElements.VideoConsentedButtonFallbackAutomationId));
        }

        if (consentButton == null)
        {
            throw new InvalidOperationException(
                "'Video Consented' button not found in Video Consent dialog.");
        }

        var error = UIAutomationHelpers.InvokeButtonSafely(consentButton, _logger);
        if (error != null)
        {
            throw new InvalidOperationException(
                $"Could not click Video Consented: {error}");
        }

        _logger.LogInformation("Clicked Video Consented button");
    }

    private void HandleOptionalMatchCentreDialog(AutomationElement window, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        Thread.Sleep(PostClickDelayMs);

        AutomationElement? matchCentreDialog = null;
        var topWindows = _locator.Automation.GetDesktop().FindAllChildren(
            cf.ByControlType(ControlType.Window));

        foreach (var w in topWindows)
        {
            var wTitle = UIAutomationHelpers.SafeGetName(w);
            if (wTitle.Contains(KnownElements.MatchCentreDialogNamePattern, StringComparison.OrdinalIgnoreCase) ||
                wTitle.Contains(KnownElements.MatchCentreDialogFallbackPattern, StringComparison.OrdinalIgnoreCase))
            {
                matchCentreDialog = w;
                break;
            }
        }

        if (matchCentreDialog == null)
        {
            matchCentreDialog = UIAutomationHelpers.FindDescendant(
                window, cf.ByName("Add Live Stream to Match Centre?"));
        }

        if (matchCentreDialog == null)
        {
            _logger.LogDebug("No Match Centre dialog appeared — continuing");
            return;
        }

        _logger.LogInformation("Match Centre dialog detected — dismissing with No");

        var noButton = UIAutomationHelpers.FindDescendant(matchCentreDialog, cf.ByName("No"));
        if (noButton == null)
        {
            var mcButtons = UIAutomationHelpers.FindAllDescendants(
                matchCentreDialog, cf.ByControlType(ControlType.Button));
            foreach (var btn in mcButtons)
            {
                var btnName = UIAutomationHelpers.SafeGetName(btn);
                if (btnName.Equals("No", StringComparison.OrdinalIgnoreCase))
                {
                    noButton = btn;
                    break;
                }
            }
        }

        if (noButton == null)
        {
            _logger.LogWarning("Match Centre dialog found but 'No' button not located — continuing without dismissal");
            return;
        }

        var error = UIAutomationHelpers.InvokeButtonSafely(noButton, _logger);
        if (error != null)
        {
            _logger.LogWarning("Could not click No on Match Centre dialog: {InvokeError}", error);
        }
        else
        {
            _logger.LogDebug("Dismissed Match Centre dialog");
        }
    }
}
