using PcsRemote.Core;
using FluentAssertions;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class PcsProStateMachineTests
{
    // ──────────────────────────────────────────────────────────────────────
    // AC-10  Initial state
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void InitialState_IsNotRunning()
    {
        var sut = new PcsProStateMachine();
        sut.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // ──────────────────────────────────────────────────────────────────────
    // §4.1  Valid deterministic transitions (9 rows)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(PcsProState.NotRunning,              PcsProTrigger.Launch,             PcsProState.Launching,              DisplayName = "NotRunning + Launch → Launching")]
    [DataRow(PcsProState.Launching,               PcsProTrigger.LoginDetected,      PcsProState.LoginScreen,            DisplayName = "Launching + LoginDetected → LoginScreen")]
    [DataRow(PcsProState.LoginScreen,             PcsProTrigger.CredentialsEntered, PcsProState.MatchSelection,         DisplayName = "LoginScreen + CredentialsEntered → MatchSelection")]
    [DataRow(PcsProState.MatchSelection,          PcsProTrigger.SearchTriggered,    PcsProState.MatchSelectionSearching, DisplayName = "MatchSelection + SearchTriggered → MatchSelectionSearching")]
    [DataRow(PcsProState.MatchSelectionSearching, PcsProTrigger.SpinnerGone,        PcsProState.MatchSelectionReady,    DisplayName = "MatchSelectionSearching + SpinnerGone → MatchSelectionReady")]
    [DataRow(PcsProState.MatchSelectionReady,     PcsProTrigger.MatchOpened,        PcsProState.MatchLoaded,            DisplayName = "MatchSelectionReady + MatchOpened → MatchLoaded")]
    [DataRow(PcsProState.MatchLoaded,             PcsProTrigger.ChangeMatch,        PcsProState.MatchSelection,         DisplayName = "MatchLoaded + ChangeMatch → MatchSelection")]
    [DataRow(PcsProState.MatchLoaded,             PcsProTrigger.Stop,               PcsProState.NotRunning,             DisplayName = "MatchLoaded + Stop → NotRunning")]
    [DataRow(PcsProState.Error,                   PcsProTrigger.Retry,              PcsProState.NotRunning,             DisplayName = "Error + Retry → NotRunning")]
    [DataRow(PcsProState.NotRunning,              PcsProTrigger.AttachToMatch,       PcsProState.MatchLoaded,            DisplayName = "NotRunning + AttachToMatch → MatchLoaded")]
    public void ValidDeterministicTransition_ProducesExpectedDestination(
        PcsProState from, PcsProTrigger trigger, PcsProState expected)
    {
        var sut = BuildMachineAt(from);
        sut.Fire(trigger);
        sut.CurrentState.Should().Be(expected);
    }

    // ──────────────────────────────────────────────────────────────────────
    // §4.2  Wildcard transitions — UnexpectedDialog and Timeout → Error
    //       from all 6 applicable states
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(PcsProState.Launching,               PcsProTrigger.UnexpectedDialog, DisplayName = "Launching + UnexpectedDialog → Error")]
    [DataRow(PcsProState.Launching,               PcsProTrigger.Timeout,          DisplayName = "Launching + Timeout → Error")]
    [DataRow(PcsProState.LoginScreen,             PcsProTrigger.UnexpectedDialog, DisplayName = "LoginScreen + UnexpectedDialog → Error")]
    [DataRow(PcsProState.LoginScreen,             PcsProTrigger.Timeout,          DisplayName = "LoginScreen + Timeout → Error")]
    [DataRow(PcsProState.MatchSelection,          PcsProTrigger.UnexpectedDialog, DisplayName = "MatchSelection + UnexpectedDialog → Error")]
    [DataRow(PcsProState.MatchSelection,          PcsProTrigger.Timeout,          DisplayName = "MatchSelection + Timeout → Error")]
    [DataRow(PcsProState.MatchSelectionSearching, PcsProTrigger.UnexpectedDialog, DisplayName = "MatchSelectionSearching + UnexpectedDialog → Error")]
    [DataRow(PcsProState.MatchSelectionSearching, PcsProTrigger.Timeout,          DisplayName = "MatchSelectionSearching + Timeout → Error")]
    [DataRow(PcsProState.MatchSelectionReady,     PcsProTrigger.UnexpectedDialog, DisplayName = "MatchSelectionReady + UnexpectedDialog → Error")]
    [DataRow(PcsProState.MatchSelectionReady,     PcsProTrigger.Timeout,          DisplayName = "MatchSelectionReady + Timeout → Error")]
    [DataRow(PcsProState.MatchLoaded,             PcsProTrigger.UnexpectedDialog, DisplayName = "MatchLoaded + UnexpectedDialog → Error")]
    [DataRow(PcsProState.MatchLoaded,             PcsProTrigger.Timeout,          DisplayName = "MatchLoaded + Timeout → Error")]
    public void WildcardTrigger_FromApplicableState_TransitionsToError(
        PcsProState from, PcsProTrigger trigger)
    {
        var sut = BuildMachineAt(from);
        sut.Fire(trigger);
        sut.CurrentState.Should().Be(PcsProState.Error);
    }

    // ──────────────────────────────────────────────────────────────────────
    // §4.3  Invalid transitions → InvalidOperationException
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(PcsProState.NotRunning,  PcsProTrigger.LoginDetected,  DisplayName = "NotRunning + LoginDetected is invalid")]
    [DataRow(PcsProState.MatchLoaded, PcsProTrigger.Launch,         DisplayName = "MatchLoaded + Launch is invalid")]
    [DataRow(PcsProState.Launching,   PcsProTrigger.MatchOpened,    DisplayName = "Launching + MatchOpened is invalid")]
    [DataRow(PcsProState.MatchLoaded,             PcsProTrigger.AttachToMatch,  DisplayName = "MatchLoaded + AttachToMatch is invalid")]
    [DataRow(PcsProState.Launching,               PcsProTrigger.AttachToMatch,  DisplayName = "Launching + AttachToMatch is invalid")]
    [DataRow(PcsProState.Error,                   PcsProTrigger.AttachToMatch,  DisplayName = "Error + AttachToMatch is invalid")]
    [DataRow(PcsProState.LoginScreen,             PcsProTrigger.AttachToMatch,  DisplayName = "LoginScreen + AttachToMatch is invalid")]
    [DataRow(PcsProState.MatchSelection,          PcsProTrigger.AttachToMatch,  DisplayName = "MatchSelection + AttachToMatch is invalid")]
    [DataRow(PcsProState.MatchSelectionSearching, PcsProTrigger.AttachToMatch,  DisplayName = "MatchSelectionSearching + AttachToMatch is invalid")]
    [DataRow(PcsProState.MatchSelectionReady,     PcsProTrigger.AttachToMatch,  DisplayName = "MatchSelectionReady + AttachToMatch is invalid")]
    public void InvalidTrigger_ThrowsInvalidOperationException(PcsProState from, PcsProTrigger trigger)
    {
        var sut = BuildMachineAt(from);
        var act = () => sut.Fire(trigger);
        act.Should().Throw<InvalidOperationException>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // §4.4  Timeout constants — exact values
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TimeoutConstants_HaveCorrectValues()
    {
        PcsProStateMachine.LaunchingTimeoutSeconds.Should().Be(40);
        PcsProStateMachine.MatchSelectionSearchingTimeoutSeconds.Should().Be(30);
        PcsProStateMachine.MatchSelectionReadyTimeoutSeconds.Should().Be(15);
    }

    // ──────────────────────────────────────────────────────────────────────
    // §4.5  Timeout trigger from each of the 4 timed states → Error
    //       (F-SC-5 traceability; subset of §4.2 Timeout rows)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(PcsProState.Launching,               DisplayName = "Launching timeout → Error")]
    [DataRow(PcsProState.LoginScreen,             DisplayName = "LoginScreen timeout → Error")]
    [DataRow(PcsProState.MatchSelectionSearching, DisplayName = "MatchSelectionSearching timeout → Error")]
    [DataRow(PcsProState.MatchSelectionReady,     DisplayName = "MatchSelectionReady timeout → Error")]
    public void TimeoutTrigger_FromTimedState_TransitionsToError(PcsProState from)
    {
        var sut = BuildMachineAt(from);
        sut.Fire(PcsProTrigger.Timeout);
        sut.CurrentState.Should().Be(PcsProState.Error);
    }

    // ──────────────────────────────────────────────────────────────────────
    // §4.6  Transition notification
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnTransitioned_Callback_FiresWithNewStateAfterSuccessfulTransition()
    {
        var sut = new PcsProStateMachine();
        PcsProState? observed = null;
        sut.OnTransitioned(s => observed = s);

        sut.Fire(PcsProTrigger.Launch);

        observed.Should().Be(PcsProState.Launching);
    }

    [TestMethod]
    public void OnTransitioned_Callback_IsNotInvokedWhenFireThrows()
    {
        var sut = new PcsProStateMachine(); // NotRunning
        var callbackInvoked = false;
        sut.OnTransitioned(_ => callbackInvoked = true);

        var act = () => sut.Fire(PcsProTrigger.LoginDetected); // invalid from NotRunning
        act.Should().Throw<InvalidOperationException>();

        callbackInvoked.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="PcsProStateMachine"/> and drives it to the desired starting state
    /// by following the canonical happy-path sequence. Used to seed tests that require a
    /// specific starting state without directly coupling to the internal representation.
    /// </summary>
    private static PcsProStateMachine BuildMachineAt(PcsProState target)
    {
        var sm = new PcsProStateMachine();
        if (target == PcsProState.NotRunning) return sm;

        sm.Fire(PcsProTrigger.Launch);
        if (target == PcsProState.Launching) return sm;

        sm.Fire(PcsProTrigger.LoginDetected);
        if (target == PcsProState.LoginScreen) return sm;

        sm.Fire(PcsProTrigger.CredentialsEntered);
        if (target == PcsProState.MatchSelection) return sm;

        // Error is reachable from MatchSelection via UnexpectedDialog
        if (target == PcsProState.Error)
        {
            sm.Fire(PcsProTrigger.UnexpectedDialog);
            return sm;
        }

        sm.Fire(PcsProTrigger.SearchTriggered);
        if (target == PcsProState.MatchSelectionSearching) return sm;

        sm.Fire(PcsProTrigger.SpinnerGone);
        if (target == PcsProState.MatchSelectionReady) return sm;

        sm.Fire(PcsProTrigger.MatchOpened);
        if (target == PcsProState.MatchLoaded) return sm;

        throw new ArgumentOutOfRangeException(nameof(target), $"Cannot build machine at {target}");
    }
}
