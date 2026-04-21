using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class AutomationLogOutcomeTests
{
    private static readonly Type _sut = typeof(AutomationLogOutcome);

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  AutomationLogOutcome is an enum with exactly three members
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AutomationLogOutcome_IsEnumWithExactMembers()
    {
        _sut.IsEnum.Should().BeTrue();
        _sut.Namespace.Should().Be("PcsRemote.Core");
        _sut.Assembly.GetName().Name.Should().Be("PcsRemote.Core");

        var names = Enum.GetNames(_sut);
        names.Should().HaveCount(3);
        names.Should().ContainInConsecutiveOrder("Info", "Success", "Failure");
    }
}
