using FlaUI.Core.AutomationElements;
using Microsoft.Extensions.Logging;
using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IHealthCheckAutomation"/>.
/// Reads PCS Pro health signals via lightweight element probing —
/// no dialogs are opened or application state modified.
/// </summary>
internal sealed class FlaUiHealthCheckAutomation : IHealthCheckAutomation
{
    private static readonly HealthCheckResult FailedProbe =
        new(false, false, null, null, ProbeSucceeded: false);

    private readonly PcsProWindowLocator _locator;
    private readonly ILogger<FlaUiHealthCheckAutomation> _logger;

    public FlaUiHealthCheckAutomation(
        PcsProWindowLocator locator,
        ILogger<FlaUiHealthCheckAutomation> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public HealthCheckResult ReadHealthSignals()
    {
        var mainWindow = _locator.FindMainWindow();
        if (mainWindow is null)
        {
            return new HealthCheckResult(false, false, null, null, ProbeSucceeded: true);
        }

        try
        {
            var windowTitle = mainWindow.Title;

            bool isMatchLoaded = mainWindow.FindFirstDescendant(cf =>
                cf.ByAutomationId(KnownElements.ScoreSummaryPaneAutomationId)) is not null;

            string? syncStatus = null;
            var statusBar = mainWindow.FindFirstDescendant(cf =>
                cf.ByClassName(KnownElements.StatusBarClassName));
            if (statusBar is not null)
            {
                syncStatus = statusBar.Name;
            }

            return new HealthCheckResult(true, isMatchLoaded, syncStatus, windowTitle, ProbeSucceeded: true);
        }
        catch (Exception ex) // FlaUI COM interop can throw unpredictable exception types (COMException, ElementNotAvailableException, etc.)
        {
            _logger.LogDebug(ex, "Health probe FlaUI exception during element reads");
            return FailedProbe;
        }
    }
}
