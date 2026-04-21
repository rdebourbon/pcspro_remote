using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IChangeMatchAutomation"/>.
/// Ported from diagnostic tool Step 10 (<c>Step10_FindChangeMatchElement</c>).
/// Opens File → Open Match... to return to the match selection dialog.
/// </summary>
internal sealed class FlaUiChangeMatchAutomation : IChangeMatchAutomation
{
    private const int MenuPopupDelayMs = 200;
    private const int DialogTimeoutMs = 15_000;

    private readonly PcsProWindowLocator _locator;
    private readonly ILogger<FlaUiChangeMatchAutomation> _logger;

    public FlaUiChangeMatchAutomation(PcsProWindowLocator locator, ILogger<FlaUiChangeMatchAutomation> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void ExecuteChangeMatchSequence()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;

        var fileMenu = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByAutomationId(KnownElements.FileMenuAutomationId))
            ?? throw new InvalidOperationException("File menu not found.");

        fileMenu.Click();
        Thread.Sleep(MenuPopupDelayMs);

        var openMatchItem = UIAutomationHelpers.WaitForElement(
            () => UIAutomationHelpers.FindDescendant(
                window,
                cf.ByAutomationId(KnownElements.ChangeMatchElementAutomationId)),
            timeoutMs: 2000);

        if (openMatchItem == null)
        {
            throw new InvalidOperationException("'Open Match...' menu item not found for change match.");
        }

        UIAutomationHelpers.InvokeButtonSafely(openMatchItem, _logger);

        // Wait for match selection dialog to appear
        var dialog = UIAutomationHelpers.WaitForElement(
            () => UIAutomationHelpers.FindDescendant(
                window,
                cf.ByName(KnownElements.MatchSelectionDialogName)),
            timeoutMs: DialogTimeoutMs);

        if (dialog == null)
        {
            throw new InvalidOperationException(
                $"Open Match dialog did not appear within {DialogTimeoutMs}ms after change match.");
        }

        _logger.LogInformation("Change match sequence complete — match selection dialog opened");
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

            var cf = _locator.Automation.ConditionFactory;
            var childWindows = UIAutomationHelpers.FindAllDescendants(
                window,
                cf.ByControlType(ControlType.Window));

            foreach (var childWindow in childWindows)
            {
                if (IsKnownDialog(childWindow, cf))
                {
                    continue;
                }

                return true;
            }

            return false;
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

            var cf = _locator.Automation.ConditionFactory;
            var childWindows = UIAutomationHelpers.FindAllDescendants(
                window,
                cf.ByControlType(ControlType.Window));

            foreach (var childWindow in childWindows)
            {
                if (IsKnownDialog(childWindow, cf))
                {
                    continue;
                }

                _logger.LogWarning(
                    "Attempting to close unexpected dialog: {DialogName}",
                    SafeGetName(childWindow));

                var closeBtn = UIAutomationHelpers.FindButtonByChildText(childWindow, "Cancel", cf, _logger)
                    ?? UIAutomationHelpers.FindButtonByChildText(childWindow, "Close", cf, _logger)
                    ?? UIAutomationHelpers.FindButtonByChildText(childWindow, "OK", cf, _logger)
                    ?? UIAutomationHelpers.FindDescendant(childWindow, cf.ByControlType(ControlType.Button));

                if (closeBtn != null)
                {
                    UIAutomationHelpers.InvokeButtonSafely(closeBtn, _logger);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryCloseUnexpectedDialog failed with exception");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private static bool IsKnownDialog(AutomationElement childWindow, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        try
        {
            var name = childWindow.Name;
            if (string.Equals(name, KnownElements.MatchSelectionDialogName, StringComparison.Ordinal) ||
                string.Equals(name, KnownElements.MatchDetailsDialogName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        catch
        {
            // Fall through
        }

        var passwordField = UIAutomationHelpers.FindDescendant(
            childWindow,
            cf.ByAutomationId(KnownElements.LoginPasswordFieldAutomationId));

        return passwordField != null;
    }

    private static string SafeGetName(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
