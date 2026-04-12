namespace PcsRemote.Automation.Mock;

/// <summary>
/// Identifies which H-SC-8 error path the mock should fire deterministically,
/// bypassing the probabilistic <see cref="MockPcsProOptions.ErrorProbability"/> roll.
/// Use <see cref="None"/> (the default) for no forced mode.
/// Use <see cref="Probabilistic"/> at injection sites that have no named H-SC-8 counterpart;
/// those sites participate only in the probability roll and are never deterministically targeted.
/// </summary>
public enum MockForcedErrorMode
{
    /// <summary>No forced mode configured — all injection sites use the probability roll.</summary>
    None,
    /// <summary>Marks an injection site with no named H-SC-8 counterpart; never fired in forced mode.</summary>
    Probabilistic,
    LaunchingToLoginScreen,
    LoginScreenToMatchSelection,
    MatchSelectionSearchingToReady,
    MatchSelectionToLoaded,
    UnexpectedDialog,
}
