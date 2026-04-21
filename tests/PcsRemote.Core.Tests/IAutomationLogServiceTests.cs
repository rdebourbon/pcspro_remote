using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class IAutomationLogServiceTests
{
    private static readonly Type _sut = typeof(IAutomationLogService);

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  AddEntry — void, accepts string + AutomationLogOutcome
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IAutomationLogService_HasAddEntryMethod()
    {
        var method = _sut.GetMethod("AddEntry");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(string));
        parameters[1].ParameterType.Should().Be(typeof(AutomationLogOutcome));
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  GetRecentEntries — returns IReadOnlyList<AutomationLogEntry>
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IAutomationLogService_HasGetRecentEntriesMethod()
    {
        var method = _sut.GetMethod("GetRecentEntries");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(IReadOnlyList<AutomationLogEntry>));
        method.GetParameters().Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  EntryAdded — event named EntryAdded, EventHandler<AutomationLogEntry>
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IAutomationLogService_HasEntryAddedEvent()
    {
        var ev = _sut.GetEvent("EntryAdded");

        ev.Should().NotBeNull();
        ev!.EventHandlerType.Should().Be(typeof(EventHandler<AutomationLogEntry>));
    }
}
