using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="ITeamNamesAutomation"/>.
/// Ported from diagnostic tool Step 8 (<c>Step8_FindTeamNameElements</c>).
/// </summary>
internal sealed class FlaUiTeamNamesAutomation : ITeamNamesAutomation
{
    private const int MenuPopupDelayMs = 300;
    private const int DialogTimeoutMs = 5000;

    private readonly PcsProWindowLocator _locator;
    private readonly ILogger<FlaUiTeamNamesAutomation> _logger;

    public FlaUiTeamNamesAutomation(PcsProWindowLocator locator, ILogger<FlaUiTeamNamesAutomation> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void OpenTeamsDialog()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;

        var scoringMenu = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByAutomationId(KnownElements.ScoringMenuAutomationId))
            ?? throw new InvalidOperationException("Scoring menu not found.");

        scoringMenu.Click();
        Thread.Sleep(MenuPopupDelayMs);

        var menuItem = UIAutomationHelpers.WaitForElement(
            () => UIAutomationHelpers.FindDescendant(window, cf.ByName(KnownElements.MatchDetailsMenuItemName)),
            timeoutMs: 2000);

        if (menuItem == null)
        {
            throw new InvalidOperationException(
                $"'{KnownElements.MatchDetailsMenuItemName}' menu item not found.");
        }

        menuItem.Click();

        var dialog = UIAutomationHelpers.WaitForElement(
            () => UIAutomationHelpers.FindDescendant(window, cf.ByName(KnownElements.MatchDetailsDialogName)),
            timeoutMs: DialogTimeoutMs);

        if (dialog == null)
        {
            throw new InvalidOperationException(
                $"Match Details/Teams dialog did not appear within {DialogTimeoutMs}ms.");
        }

        _logger.LogDebug("Match Details/Teams dialog opened");
    }

    /// <inheritdoc/>
    public string ReadHomeTeamName() => ReadTeamName(teamIndex: 0, "home");

    /// <inheritdoc/>
    public string ReadAwayTeamName() => ReadTeamName(teamIndex: 1, "away");

    /// <inheritdoc/>
    public void TryCloseTeamsDialog()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return;
            }

            var cf = _locator.Automation.ConditionFactory;
            var dialog = UIAutomationHelpers.FindDescendant(
                window,
                cf.ByName(KnownElements.MatchDetailsDialogName));

            if (dialog == null)
            {
                return;
            }

            var okButton = UIAutomationHelpers.FindDescendant(
                dialog,
                cf.ByAutomationId(KnownElements.MatchDetailsOkButtonAutomationId));

            if (okButton != null)
            {
                UIAutomationHelpers.InvokeButtonSafely(okButton, _logger);
                _logger.LogDebug("Match Details/Teams dialog closed via OK button");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryCloseTeamsDialog failed with exception");
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

    private string ReadTeamName(int teamIndex, string label)
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;
        var dialog = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByName(KnownElements.MatchDetailsDialogName))
            ?? throw new InvalidOperationException("Match Details/Teams dialog not found.");

        var teamViews = UIAutomationHelpers.FindAllDescendants(
            dialog,
            cf.ByClassName(KnownElements.MatchTeamViewClassName));

        if (teamViews.Length < 2)
        {
            throw new InvalidOperationException(
                $"Expected 2 MatchTeamView elements, found {teamViews.Length}.");
        }

        var teamView = teamViews[teamIndex];
        var teamCombo = UIAutomationHelpers.FindDescendant(
            teamView,
            cf.ByAutomationId(KnownElements.TeamComboBoxAutomationId))
            ?? throw new InvalidOperationException(
                $"{label} team ComboBox not found (AutomationId=\"{KnownElements.TeamComboBoxAutomationId}\").");

        var teamName = ReadComboBoxValue(teamCombo);
        if (string.IsNullOrWhiteSpace(teamName))
        {
            throw new InvalidOperationException($"Could not read {label} team name from ComboBox.");
        }

        _logger.LogDebug("Read {TeamLabel} team name: {TeamName}", label, teamName);
        return teamName;
    }

    private static string ReadComboBoxValue(AutomationElement comboBox)
    {
        // Strategy 1: ValuePattern (most reliable for WPF ComboBox selected text)
        try
        {
            if (comboBox.Patterns.Value.IsSupported)
            {
                var val = comboBox.Patterns.Value.Pattern.Value.Value;
                if (!string.IsNullOrEmpty(val))
                {
                    return val;
                }
            }
        }
        catch
        {
            // Fall through
        }

        // Strategy 2: Name property
        try
        {
            var name = comboBox.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch
        {
            // Fall through
        }

        // Strategy 3: SelectedItem from ComboBox wrapper
        try
        {
            var combo = comboBox.AsComboBox();
            var selected = combo.SelectedItem;
            if (selected != null)
            {
                try
                {
                    var selectedName = selected.Name;
                    if (!string.IsNullOrWhiteSpace(selectedName))
                    {
                        return selectedName;
                    }
                }
                catch
                {
                    // Fall through
                }
            }
        }
        catch
        {
            // Fall through
        }

        return string.Empty;
    }
}
