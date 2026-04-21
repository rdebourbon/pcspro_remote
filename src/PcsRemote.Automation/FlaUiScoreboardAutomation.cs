using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IScoreboardAutomation"/>.
/// Ported from diagnostic tool Step 9 (<c>Step9_FindScoreboardElements</c>).
/// Uses <see cref="Capture.Rectangle(Rectangle)"/> for DPI-aware screen capture (H-U-1 resolution).
/// </summary>
internal sealed class FlaUiScoreboardAutomation : IScoreboardAutomation
{
    private const int PopupDelayMs = 500;
    private const int ActivationSettleMs = 300;
    private const int FocusSettleMs = 100;

    private readonly PcsProWindowLocator _locator;
    private readonly ILogger<FlaUiScoreboardAutomation> _logger;
    private readonly ScoreboardOptions _scoreboardOptions;

    public FlaUiScoreboardAutomation(
        PcsProWindowLocator locator,
        ILogger<FlaUiScoreboardAutomation> logger,
        IOptions<ScoreboardOptions> scoreboardOptions)
    {
        _locator = locator;
        _logger = logger;
        _scoreboardOptions = scoreboardOptions.Value;
    }

    /// <inheritdoc/>
    public void ClickSettingsCog()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;

        var scoreboardPane = FindMainScoreboardPane(window, cf);
        UIAutomationHelpers.ActivateToolWindow(scoreboardPane, cf, _logger);
        Thread.Sleep(ActivationSettleMs);

        var container = UIAutomationHelpers.WalkUpToClassName(scoreboardPane, "ToolWindowContainer")
            ?? scoreboardPane;

        var titleBar = UIAutomationHelpers.FindDescendant(container, cf.ByAutomationId("PART_TitleBar"))
            ?? UIAutomationHelpers.FindDescendant(container, cf.ByClassName("TitleBarPanel"))
            ?? throw new InvalidOperationException("Scoreboard title bar not found.");

        var settingsButton = FindSettingsPopupButton(titleBar, cf)
            ?? throw new InvalidOperationException("Settings PopupButton not found in scoreboard title bar.");

        OpenPopupButton(settingsButton, cf);
        _logger.LogDebug("Settings popup opened");
    }

    /// <inheritdoc/>
    public void ClickRefreshAllScoreboards()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;

        // Search main window first, then Desktop (WPF popups render at desktop level)
        var refreshItem = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByName(KnownElements.RefreshAllScoreboardsMenuItemName));

        if (refreshItem == null)
        {
            try
            {
                var desktop = _locator.Automation.GetDesktop();
                refreshItem = UIAutomationHelpers.FindDescendant(
                    desktop,
                    cf.ByName(KnownElements.RefreshAllScoreboardsMenuItemName));
            }
            catch
            {
                // Desktop access may fail
            }
        }

        if (refreshItem == null)
        {
            throw new InvalidOperationException(
                $"'{KnownElements.RefreshAllScoreboardsMenuItemName}' menu item not found.");
        }

        refreshItem.Click();
        _logger.LogDebug("Refresh all Scoreboards clicked");
    }

    /// <inheritdoc/>
    public byte[] CaptureScoreboardImage()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;

        var scoreboardPane = FindMainScoreboardPane(window, cf);
        UIAutomationHelpers.ActivateToolWindow(scoreboardPane, cf, _logger);
        Thread.Sleep(ActivationSettleMs);

        // Prefer ReplayScreenPreview (content only) over ToolWindow (includes chrome)
        var captureTarget = UIAutomationHelpers.FindDescendant(
            scoreboardPane,
            cf.ByAutomationId(KnownElements.ReplayScreenPreviewAutomationId))
            ?? scoreboardPane;

        var bounds = captureTarget.BoundingRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException(
                $"Scoreboard bounding rectangle is invalid: {bounds.Width}x{bounds.Height}.");
        }

        using var captureImage = Capture.Rectangle(bounds);
        using var bitmap = captureImage.Bitmap;

        using var ms = new MemoryStream();
        var encoder = GetJpegEncoder();
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_scoreboardOptions.JpegQuality);
        bitmap.Save(ms, encoder, encoderParams);

        var bytes = ms.ToArray();
        _logger.LogInformation(
            "Scoreboard captured: {Width}x{Height}, {SizeKb}KB JPEG (quality={Quality})",
            bounds.Width,
            bounds.Height,
            bytes.Length / 1024,
            _scoreboardOptions.JpegQuality);

        return bytes;
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

    private AutomationElement FindMainScoreboardPane(
        AutomationElement window,
        FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var allToolWindows = UIAutomationHelpers.FindAllDescendants(
            window,
            cf.ByClassName(KnownElements.ToolWindowClassName));

        // Primary match: Name contains both "Main" and "Scoreboard"
        foreach (var tw in allToolWindows)
        {
            var name = UIAutomationHelpers.SafeGetName(tw);
            if (name.Contains("Main", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("Scoreboard", StringComparison.OrdinalIgnoreCase))
            {
                return tw;
            }
        }

        // Fallback: any ToolWindow with "Scoreboard" in name
        foreach (var tw in allToolWindows)
        {
            var name = UIAutomationHelpers.SafeGetName(tw);
            if (name.Contains("Scoreboard", StringComparison.OrdinalIgnoreCase))
            {
                return tw;
            }
        }

        // Fallback: search by known name constant
        var byName = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByName(KnownElements.MainScoreboardToolWindowName));

        return byName
            ?? throw new InvalidOperationException("Main Scoreboard ToolWindow not found.");
    }

    private static AutomationElement? FindSettingsPopupButton(
        AutomationElement titleBar,
        FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var buttons = UIAutomationHelpers.FindAllDescendants(
            titleBar,
            cf.ByControlType(ControlType.Button));

        foreach (var btn in buttons)
        {
            try
            {
                if (btn.ClassName == KnownElements.SettingsPopupButtonClassName)
                {
                    try
                    {
                        var helpText = btn.HelpText;
                        if (helpText != null &&
                            helpText.Contains(KnownElements.SettingsHelpTextPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            return btn;
                        }
                    }
                    catch
                    {
                        // HelpText may not be available
                    }
                }
            }
            catch
            {
                // ClassName may throw
            }
        }

        return null;
    }

    private void OpenPopupButton(
        AutomationElement button,
        FlaUI.Core.Conditions.ConditionFactory cf)
    {
        // Strategy 1: ExpandCollapse pattern
        try
        {
            if (button.Patterns.ExpandCollapse.IsSupported)
            {
                button.Patterns.ExpandCollapse.Pattern.Expand();
                Thread.Sleep(PopupDelayMs);
                if (IsPopupVisible(cf))
                {
                    return;
                }
            }
        }
        catch
        {
            // Fall through
        }

        // Strategy 2: Invoke pattern
        try
        {
            if (button.Patterns.Invoke.IsSupported)
            {
                button.Patterns.Invoke.Pattern.Invoke();
                Thread.Sleep(PopupDelayMs);
                if (IsPopupVisible(cf))
                {
                    return;
                }
            }
        }
        catch
        {
            // Fall through
        }

        // Strategy 3: Focus + Click
        try
        {
            button.Focus();
            Thread.Sleep(FocusSettleMs);
            button.Click();
            Thread.Sleep(PopupDelayMs);
            if (IsPopupVisible(cf))
            {
                return;
            }
        }
        catch
        {
            // Fall through
        }

        throw new InvalidOperationException(
            "Failed to open settings popup — all strategies exhausted and popup not visible.");
    }

    private bool IsPopupVisible(FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var window = _locator.FindMainWindow();
        if (window != null)
        {
            var item = UIAutomationHelpers.FindDescendant(
                window,
                cf.ByName(KnownElements.RefreshAllScoreboardsMenuItemName));
            if (item != null)
            {
                return true;
            }
        }

        try
        {
            var desktop = _locator.Automation.GetDesktop();
            var item = UIAutomationHelpers.FindDescendant(
                desktop,
                cf.ByName(KnownElements.RefreshAllScoreboardsMenuItemName));
            if (item != null)
            {
                return true;
            }
        }
        catch
        {
            // Desktop access may fail
        }

        return false;
    }

    private static ImageCodecInfo GetJpegEncoder()
    {
        var encoders = ImageCodecInfo.GetImageEncoders();
        foreach (var encoder in encoders)
        {
            if (encoder.MimeType == "image/jpeg")
            {
                return encoder;
            }
        }

        throw new InvalidOperationException("JPEG encoder not found.");
    }
}
