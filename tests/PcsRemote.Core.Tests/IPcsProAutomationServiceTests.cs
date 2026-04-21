using System.Reflection;
using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class IPcsProAutomationServiceTests
{
    private static readonly Type _sut = typeof(IPcsProAutomationService);

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  DismissAsync — Task return, optional CancellationToken
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetMethod_DismissAsync_ReturnsTaskWithOptionalCancellationToken()
    {
        var method = _sut.GetMethod("DismissAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(CancellationToken));
        parameters[0].HasDefaultValue.Should().BeTrue();
    }
}
