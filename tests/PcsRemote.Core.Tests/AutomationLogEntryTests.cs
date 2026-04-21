using System.Reflection;
using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class AutomationLogEntryTests
{
    private static readonly Type _sut = typeof(AutomationLogEntry);

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  AutomationLogEntry is a record with expected properties
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AutomationLogEntry_IsRecordWithExpectedProperties()
    {
        _sut.Namespace.Should().Be("PcsRemote.Core");
        _sut.Assembly.GetName().Name.Should().Be("PcsRemote.Core");

        // Record detection: compiler generates a <Clone>$ method
        _sut.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance)
            .Should().NotBeNull("because records have a compiler-generated Clone method");

        var timestamp = _sut.GetProperty("Timestamp");
        timestamp.Should().NotBeNull();
        timestamp!.PropertyType.Should().Be(typeof(DateTimeOffset));

        var action = _sut.GetProperty("Action");
        action.Should().NotBeNull();
        action!.PropertyType.Should().Be(typeof(string));

        var outcome = _sut.GetProperty("Outcome");
        outcome.Should().NotBeNull();
        outcome!.PropertyType.Should().Be(typeof(AutomationLogOutcome));
    }
}
