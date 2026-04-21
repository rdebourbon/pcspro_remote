using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;
using PcsRemote.Core;

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
    public TeamNameInfo ReadHomeTeamName() => ReadTeamName(teamIndex: 0, "home");

    /// <inheritdoc/>
    public TeamNameInfo ReadAwayTeamName() => ReadTeamName(teamIndex: 1, "away");

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

    private TeamNameInfo ReadTeamName(int teamIndex, string label)
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

        // Read team name (cbxTeam) — throws on failure
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

        // Read club name (cboClub) — non-throwing; defaults to empty on failure
        var clubName = ReadClubName(teamView, cf, label);

        _logger.LogDebug(
            "Read {TeamLabel} team — ClubName={ClubName}, TeamName={TeamName}",
            label, clubName, teamName);

        return new TeamNameInfo(clubName, teamName);
    }

    private string ReadClubName(
        AutomationElement teamView,
        FlaUI.Core.Conditions.ConditionFactory cf,
        string label)
    {
        try
        {
            var clubCombo = UIAutomationHelpers.FindDescendant(
                teamView,
                cf.ByAutomationId(KnownElements.ClubComboBoxAutomationId));

            if (clubCombo == null)
            {
                _logger.LogWarning(
                    "Club ComboBox not found for {TeamLabel} team (AutomationId=\"{AutomationId}\")",
                    label, KnownElements.ClubComboBoxAutomationId);
                return string.Empty;
            }

            var (value, succeeded) = TryReadComboBoxValue(clubCombo);
            if (!succeeded)
            {
                _logger.LogWarning(
                    "All read strategies failed for {TeamLabel} club ComboBox",
                    label);
            }

            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read club ComboBox for {TeamLabel} team",
                label);
            return string.Empty;
        }
    }

    private static string ReadComboBoxValue(AutomationElement comboBox)
        => TryReadComboBoxValue(comboBox).Value;

    private static (string Value, bool Succeeded) TryReadComboBoxValue(AutomationElement comboBox)
    {
        bool anySucceeded = false;

        // Strategy 1: ValuePattern (most reliable for WPF ComboBox selected text)
        try
        {
            if (comboBox.Patterns.Value.IsSupported)
            {
                var val = comboBox.Patterns.Value.Pattern.Value.Value;
                anySucceeded = true;
                if (!string.IsNullOrEmpty(val))
                {
                    return (val, true);
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
            anySucceeded = true;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return (name, true);
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
            anySucceeded = true;
            if (selected != null)
            {
                try
                {
                    var selectedName = selected.Name;
                    if (!string.IsNullOrWhiteSpace(selectedName))
                    {
                        return (selectedName, true);
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

        return (string.Empty, anySucceeded);
    }
}
