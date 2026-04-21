using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Microsoft.Extensions.Logging;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="ILoginAutomation"/>.
/// </summary>
/// <remarks>
/// All detection methods (<see cref="IsLoginDialogVisible"/>,
/// <see cref="IsMatchSelectionVisible"/>, <see cref="IsUnexpectedDialogPresent"/>)
/// are non-throwing — they wrap FlaUI/COM calls in try/catch and return
/// <see langword="false"/> on any exception.
/// </remarks>
internal sealed class FlaUiLoginAutomation : ILoginAutomation
{
    private readonly PcsProWindowLocator _locator;
    private readonly ILogger<FlaUiLoginAutomation> _logger;

    public FlaUiLoginAutomation(PcsProWindowLocator locator, ILogger<FlaUiLoginAutomation> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsLoginDialogVisible()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return false;
            }

            var cf = _locator.Automation.ConditionFactory;
            var childWindows = UIAutomationHelpers.FindAllDescendants(window, cf.ByControlType(ControlType.Window));

            foreach (var childWindow in childWindows)
            {
                var passwordField = UIAutomationHelpers.FindDescendant(
                    childWindow,
                    cf.ByAutomationId(KnownElements.LoginPasswordFieldAutomationId));

                if (passwordField != null)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsLoginDialogVisible failed with exception");
            return false;
        }
    }

    /// <inheritdoc/>
    public void EnterPassword(string password)
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;
        var pwField = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByAutomationId(KnownElements.LoginPasswordFieldAutomationId))
            ?? throw new InvalidOperationException(
                $"Password field not found (AutomationId=\"{KnownElements.LoginPasswordFieldAutomationId}\").");

        pwField.Focus();
        Thread.Sleep(200);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(password);

        Thread.Sleep(300);

        _logger.LogDebug("Password entered via keyboard simulation");
    }

    /// <inheritdoc/>
    public void ClickSubmit()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;
        var submitBtn = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByAutomationId(KnownElements.LoginSubmitButtonAutomationId))
            ?? throw new InvalidOperationException(
                $"Submit button not found (AutomationId=\"{KnownElements.LoginSubmitButtonAutomationId}\").");

        var error = UIAutomationHelpers.InvokeButtonSafely(submitBtn, _logger);
        if (error != null)
        {
            throw new InvalidOperationException($"Login submit failed: {error}");
        }

        _logger.LogDebug("Login submit button invoked");
    }

    /// <inheritdoc/>
    public bool IsMatchSelectionVisible()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return false;
            }

            var cf = _locator.Automation.ConditionFactory;
            var matchDialog = UIAutomationHelpers.FindDescendant(
                window,
                cf.ByName(KnownElements.MatchSelectionDialogName));

            return matchDialog != null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsMatchSelectionVisible failed with exception");
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

            var cf = _locator.Automation.ConditionFactory;
            var childWindows = UIAutomationHelpers.FindAllDescendants(window, cf.ByControlType(ControlType.Window));

            foreach (var childWindow in childWindows)
            {
                if (IsMatchSelectionDialog(childWindow))
                {
                    continue;
                }

                if (IsLoginDialog(childWindow, cf))
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
            var childWindows = UIAutomationHelpers.FindAllDescendants(window, cf.ByControlType(ControlType.Window));

            foreach (var childWindow in childWindows)
            {
                if (IsMatchSelectionDialog(childWindow) || IsLoginDialog(childWindow, cf))
                {
                    continue;
                }

                _logger.LogWarning("Attempting to close unexpected dialog: {DialogName}", SafeGetName(childWindow));

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

    private static bool IsMatchSelectionDialog(AutomationElement childWindow)
    {
        try
        {
            return string.Equals(
                childWindow.Name,
                KnownElements.MatchSelectionDialogName,
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLoginDialog(AutomationElement childWindow, FlaUI.Core.Conditions.ConditionFactory cf)
    {
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
