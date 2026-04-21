using System.Reflection;
using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class IOperationCoordinatorServiceTests
{
    private static readonly Type _sut = typeof(IOperationCoordinatorService);

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  CurrentOperationDescription — read-only nullable string
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetProperty_CurrentOperationDescription_IsReadOnlyNullableString()
    {
        var prop = _sut.GetProperty("CurrentOperationDescription");

        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(string));
        prop.CanRead.Should().BeTrue();
        prop.CanWrite.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  BeginOperation — accepts optional string? parameter
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetMethod_BeginOperation_AcceptsOptionalStringParameter()
    {
        var method = _sut.GetMethod("BeginOperation");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(bool));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(string));
        parameters[0].HasDefaultValue.Should().BeTrue();
        parameters[0].DefaultValue.Should().BeNull();
    }
}
