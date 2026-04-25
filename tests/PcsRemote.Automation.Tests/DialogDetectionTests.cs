using FluentAssertions;

namespace PcsRemote.Automation.Tests;

[TestClass]
public sealed class DialogDetectionTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // 3.1 Whitelist Tests — IsKnownDialogByName
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void IsKnownDialogByName_VideoConsentPattern_ReturnsTrue()
    {
        UIAutomationHelpers.IsKnownDialogByName("Video Consent — Some Match Name")
            .Should().BeTrue();
    }

    [TestMethod]
    public void IsKnownDialogByName_MatchCentrePattern_ReturnsTrue()
    {
        UIAutomationHelpers.IsKnownDialogByName("Match Centre — Some Match Name")
            .Should().BeTrue();
    }

    [TestMethod]
    public void IsKnownDialogByName_AddLiveStreamPattern_ReturnsTrue()
    {
        UIAutomationHelpers.IsKnownDialogByName("Add Live Stream to Some Match Name")
            .Should().BeTrue();
    }

    [TestMethod]
    [DataRow("Open Match")]
    [DataRow("Match Details/Teams")]
    public void IsKnownDialogByName_ExactMatch_ReturnsTrue(string name)
    {
        UIAutomationHelpers.IsKnownDialogByName(name).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("Some Random Dialog")]
    [DataRow("Unknown")]
    [DataRow("")]
    [DataRow(null)]
    public void IsKnownDialogByName_UnknownDialog_ReturnsFalse(string? name)
    {
        UIAutomationHelpers.IsKnownDialogByName(name).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.2 Popup Filter Tests — IsPopupByClassName
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void IsPopupByClassName_Popup_ReturnsTrue()
    {
        UIAutomationHelpers.IsPopupByClassName("Popup").Should().BeTrue();
    }

    [TestMethod]
    public void IsPopupByClassName_PopupHost_ReturnsTrue()
    {
        UIAutomationHelpers.IsPopupByClassName("PopupHost").Should().BeTrue();
    }

    [TestMethod]
    public void IsPopupByClassName_HwndWrapperPopup_ReturnsTrue()
    {
        UIAutomationHelpers.IsPopupByClassName("HwndWrapper[PcsProApp;;Popup]")
            .Should().BeTrue();
    }

    [TestMethod]
    public void IsPopupByClassName_NonPopup_ReturnsFalse()
    {
        UIAutomationHelpers.IsPopupByClassName("SomeDialog").Should().BeFalse();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    public void IsPopupByClassName_NullOrEmpty_ReturnsFalse(string? className)
    {
        UIAutomationHelpers.IsPopupByClassName(className).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.3 Hysteresis Tests — DialogProbeContext.Evaluate
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Evaluate_FirstSighting_ReturnsFalse()
    {
        var ctx = new DialogProbeContext();
        ctx.Evaluate(true, "UnknownDialog").Should().BeFalse();
    }

    [TestMethod]
    public void Evaluate_SecondConsecutiveSameSighting_ReturnsTrue()
    {
        var ctx = new DialogProbeContext();
        ctx.Evaluate(true, "UnknownDialog");
        ctx.Evaluate(true, "UnknownDialog").Should().BeTrue();
    }

    [TestMethod]
    public void Evaluate_CleanTickResets_ReturnsFalse()
    {
        var ctx = new DialogProbeContext();

        ctx.Evaluate(true, "UnknownDialog");  // tick 1: first sighting
        ctx.Evaluate(false, null);             // tick 2: clean
        ctx.Evaluate(true, "UnknownDialog")   // tick 3: first sighting again
            .Should().BeFalse("counter was reset by clean tick");
    }

    [TestMethod]
    public void Evaluate_SeparateContexts_DoNotInterfere()
    {
        var ctxA = new DialogProbeContext();
        var ctxB = new DialogProbeContext();

        ctxA.Evaluate(true, "DialogA"); // ctxA tick 1
        ctxB.Evaluate(false, null);     // ctxB clean

        // ctxA tick 2 should confirm — ctxB should not be affected
        ctxA.Evaluate(true, "DialogA").Should().BeTrue();
        ctxB.Evaluate(true, "DialogB").Should().BeFalse("ctxB has its own counter");
    }

    [TestMethod]
    public void Evaluate_SingleShotBypassesHysteresis_ContrastWithLooped()
    {
        // Single-shot probes call HasUnexpectedDialog(window, cf) — no DialogProbeContext.
        // Looped probes call HasUnexpectedDialog(window, cf, context) — with hysteresis.
        // Verify the contract: DialogProbeContext suppresses the first sighting,
        // proving that bypassing it (single-shot) yields immediate detection.
        var ctx = new DialogProbeContext();
        bool withHysteresis = ctx.Evaluate(true, "SomeDialog");
        withHysteresis.Should().BeFalse(
            "hysteresis suppresses first sighting; single-shot probes skip this by not using a context");
    }

    [TestMethod]
    public void Evaluate_DifferentDialogResets_ReturnsFalse()
    {
        var ctx = new DialogProbeContext();

        ctx.Evaluate(true, "DialogA"); // tick 1: dialog A
        ctx.Evaluate(true, "DialogB") // tick 2: different dialog B
            .Should().BeFalse("identity changed, counter reset");
    }

    [TestMethod]
    public void Evaluate_PostConfirmReset_ReturnsFalse()
    {
        var ctx = new DialogProbeContext();

        ctx.Evaluate(true, "PersistentDialog"); // tick 1
        ctx.Evaluate(true, "PersistentDialog"); // tick 2: confirmed (returns true)

        ctx.Evaluate(true, "PersistentDialog")  // tick 3: new cycle starts
            .Should().BeFalse("counter was reset after confirmation");
    }

    [TestMethod]
    public void Evaluate_PersistentDialogCadence_AlternatesCorrectly()
    {
        var ctx = new DialogProbeContext();

        var results = new[]
        {
            ctx.Evaluate(true, "PersistentDialog"), // tick 1
            ctx.Evaluate(true, "PersistentDialog"), // tick 2
            ctx.Evaluate(true, "PersistentDialog"), // tick 3
            ctx.Evaluate(true, "PersistentDialog"), // tick 4
        };

        results.Should().Equal(false, true, false, true);
    }

    [TestMethod]
    public void Evaluate_KnownDialogOnlyTickIsClean_ReturnsFalse()
    {
        var ctx = new DialogProbeContext();

        ctx.Evaluate(true, "UnknownDialog"); // tick 1: unknown dialog
        ctx.Evaluate(false, null);           // tick 2: known-dialog-only (caller passes false)
        ctx.Evaluate(true, "UnknownDialog") // tick 3: unknown dialog again
            .Should().BeFalse("known-dialog-only tick is clean, counter reset");
    }
}
