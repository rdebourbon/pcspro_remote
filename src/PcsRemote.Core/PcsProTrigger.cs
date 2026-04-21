namespace PcsRemote.Core;

/// <summary>
/// Models the external events and internal signals that drive PCS Pro state transitions.
/// </summary>
public enum PcsProTrigger
{
    Launch,
    LoginDetected,
    CredentialsEntered,
    SearchTriggered,
    SpinnerGone,
    MatchOpened,
    ChangeMatch,
    Stop,
    Timeout,
    UnexpectedDialog,
    Retry,
    AttachToMatch,
}
