using Stateless;

namespace PcsRemote.Core;

/// <summary>
/// Models the full PCS Pro application lifecycle using a deterministic state machine.
/// Wraps the Stateless library; transitions are defined per SPEC-S-004 §3.1.
/// </summary>
public sealed class PcsProStateMachine
{
    /// <summary>Seconds to wait for the PCS Pro process to reach the login screen after launch.</summary>
    public const int LaunchingTimeoutSeconds = 40;

    /// <summary>Seconds to wait for match search results to load.</summary>
    public const int MatchSelectionSearchingTimeoutSeconds = 30;

    /// <summary>Seconds to wait for a match to open from the ready state.</summary>
    public const int MatchSelectionReadyTimeoutSeconds = 15;

    /// <summary>Seconds to wait for PCS Pro to close gracefully before escalating to force-kill.</summary>
    public const int GracefulCloseTimeoutSeconds = 5;

    private readonly StateMachine<PcsProState, PcsProTrigger> _machine;

    /// <summary>Gets the current state of the PCS Pro application lifecycle.</summary>
    public PcsProState CurrentState => _machine.State;

    /// <summary>Initialises the state machine in the <see cref="PcsProState.NotRunning"/> state.</summary>
    public PcsProStateMachine()
    {
        _machine = new StateMachine<PcsProState, PcsProTrigger>(PcsProState.NotRunning);
        ConfigureTransitions();
    }

    /// <summary>
    /// Fires a trigger, advancing the state machine. Throws <see cref="InvalidOperationException"/>
    /// if the trigger is not permitted from the current state.
    /// </summary>
    public void Fire(PcsProTrigger trigger) => _machine.Fire(trigger);

    /// <summary>
    /// Registers a callback that is invoked after each successful state transition.
    /// The callback receives the new <see cref="PcsProState"/>.
    /// The callback is NOT invoked when <see cref="Fire"/> throws.
    /// Each call adds an additional handler; handlers accumulate and are never removed.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="callback"/> is null.</exception>
    public void OnTransitioned(Action<PcsProState> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _machine.OnTransitioned(t => callback(t.Destination));
    }

    private void ConfigureTransitions()
    {
        _machine.Configure(PcsProState.NotRunning)
            .Permit(PcsProTrigger.Launch, PcsProState.Launching)
            .Permit(PcsProTrigger.AttachToMatch, PcsProState.MatchLoaded);

        _machine.Configure(PcsProState.Launching)
            .Permit(PcsProTrigger.LoginDetected, PcsProState.LoginScreen)
            .Permit(PcsProTrigger.UnexpectedDialog, PcsProState.Error)
            .Permit(PcsProTrigger.Timeout, PcsProState.Error);

        _machine.Configure(PcsProState.LoginScreen)
            .Permit(PcsProTrigger.CredentialsEntered, PcsProState.MatchSelection)
            .Permit(PcsProTrigger.UnexpectedDialog, PcsProState.Error)
            .Permit(PcsProTrigger.Timeout, PcsProState.Error);

        _machine.Configure(PcsProState.MatchSelection)
            .Permit(PcsProTrigger.SearchTriggered, PcsProState.MatchSelectionSearching)
            .Permit(PcsProTrigger.AttachToMatch, PcsProState.MatchLoaded)
            .Permit(PcsProTrigger.UnexpectedDialog, PcsProState.Error)
            .Permit(PcsProTrigger.Timeout, PcsProState.Error);

        _machine.Configure(PcsProState.MatchSelectionSearching)
            .Permit(PcsProTrigger.SpinnerGone, PcsProState.MatchSelectionReady)
            .Permit(PcsProTrigger.UnexpectedDialog, PcsProState.Error)
            .Permit(PcsProTrigger.Timeout, PcsProState.Error);

        _machine.Configure(PcsProState.MatchSelectionReady)
            .Permit(PcsProTrigger.MatchOpened, PcsProState.MatchLoaded)
            .Permit(PcsProTrigger.UnexpectedDialog, PcsProState.Error)
            .Permit(PcsProTrigger.Timeout, PcsProState.Error);

        _machine.Configure(PcsProState.MatchLoaded)
            .Permit(PcsProTrigger.ChangeMatch, PcsProState.MatchSelection)
            .Permit(PcsProTrigger.Stop, PcsProState.NotRunning)
            .Permit(PcsProTrigger.UnexpectedDialog, PcsProState.Error)
            .Permit(PcsProTrigger.Timeout, PcsProState.Error);

        _machine.Configure(PcsProState.Error)
            .Permit(PcsProTrigger.Retry, PcsProState.NotRunning)
            .Permit(PcsProTrigger.Dismiss, PcsProState.NotRunning);
    }
}
